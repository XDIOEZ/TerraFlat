using System;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Mirror;
using Unity.Profiling;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkItemStateCoordinator
    {
        #region 地形复制生命周期
        private static readonly ProfilerMarker TerrainReplicationMarker = new("FlatWorld.Network.Items.TerrainReplication");
        private sealed class DirtyTerrainChunk
        {
            public string WorldKey;
            public ChunkRuntime Chunk;
            public readonly HashSet<int> Cells = new();
        }

        private readonly Dictionary<(string, RuntimeWorldAddress), DirtyTerrainChunk> dirtyTerrain = new();
        private readonly List<DirtyTerrainChunk> terrainFlushBuffer = new();
        private readonly Dictionary<RuntimeWorldAddress, NetworkTerrainChunkCursor> terrainCursors = new();
        private readonly List<RuntimeWorldAddress> terrainPruneBuffer = new();
        private WorldRuntime terrainClientWorld;
        private long terrainClientEpoch;
        private uint terrainClientRealm, nextTerrainRequest;
        private long nextTerrainRevision;
        private float nextTerrainWindowScan;

        private void StartTerrainServerBridge()
        {
            NetworkServer.RegisterHandler<NetworkTerrainChunkRequest>(
                NetworkGameplayWorldScope.Wrap<NetworkTerrainChunkRequest>(OnServerTerrainChunkRequest), true);
            SaveDataMgr.AuthoritativeTerrainCellChanged += QueueTerrainCell;
        }

        private void StopTerrainServerBridge()
        {
            NetworkServer.UnregisterHandler<NetworkTerrainChunkRequest>();
            SaveDataMgr.AuthoritativeTerrainCellChanged -= QueueTerrainCell;
            dirtyTerrain.Clear();
            terrainFlushBuffer.Clear();
        }

        private void StartTerrainClientBridge()
        {
            NetworkClient.RegisterHandler<NetworkTerrainChunkMessage>(OnClientTerrainChunkMessage, true);
            ResetTerrainClient();
        }

        private void StopTerrainClientBridge()
        {
            NetworkClient.UnregisterHandler<NetworkTerrainChunkMessage>();
            ResetTerrainClient();
        }

        private void ResetTerrainClient()
        {
            terrainCursors.Clear();
            terrainPruneBuffer.Clear();
            terrainClientWorld = null;
            terrainClientEpoch = 0;
            terrainClientRealm = 0;
            nextTerrainWindowScan = 0f;
        }
        #endregion

        #region 权威最终状态发布
        private void QueueTerrainCell(string worldKey, ChunkRuntime chunk, Vector2Int local)
        {
            ChunkTerrainData terrain = chunk?.Terrain;
            if (!NetworkServer.active || string.IsNullOrWhiteSpace(worldKey) || terrain == null ||
                terrain.IsDisposed || (uint)local.x >= (uint)terrain.Width || (uint)local.y >= (uint)terrain.Height)
                return;
            var key = (worldKey, chunk.Address);
            if (!dirtyTerrain.TryGetValue(key, out DirtyTerrainChunk pending) || !ReferenceEquals(pending.Chunk, chunk))
                dirtyTerrain[key] = pending = new DirtyTerrainChunk { WorldKey = worldKey, Chunk = chunk };
            pending.Cells.Add(local.y * terrain.Width + local.x);
        }

        private bool PublishTerrainPlacement(TileBuildingCell cell)
        {
            ChunkTerrainData terrain = cell.RuntimeChunk?.Terrain;
            if (!serverHandlersRegistered || !NetworkServer.active || terrain == null || terrain.IsDisposed ||
                cell.RuntimeChunk.DataStatus != ChunkDataStatus.Ready)
                return false;
            int x = cell.LocalPosition.x, y = cell.LocalPosition.y;
            if ((uint)x >= (uint)terrain.Width || (uint)y >= (uint)terrain.Height || cell.RuntimeTileId <= 0)
                return false;
            int installed = cell.ReplacedGroundCell.HasValue
                ? TerrainSupportLayer.GetTileId(terrain, x, y) : terrain.GetCell(x, y).BlockingTileId;
            string world = GameplayWorldRegistry.ExecutionWorldKey;
            if (installed != cell.RuntimeTileId || string.IsNullOrWhiteSpace(world)) return false;
            if (!TerrainBuildingCellState.Capture(terrain, y * terrain.Width + x).IsValid(terrain.Width * terrain.Height))
                return false;
            QueueTerrainCell(world, cell.RuntimeChunk, cell.LocalPosition);
            return true;
        }

        private void FlushTerrainChanges()
        {
            if (dirtyTerrain.Count == 0) return;
            // 无远端玩家时不组装/分片无接收者的地形差量；加入时通过完整区块快照恢复。
            if (!HasRemoteReadyClient()) { dirtyTerrain.Clear(); return; }
            terrainFlushBuffer.Clear();
            terrainFlushBuffer.AddRange(dirtyTerrain.Values);
            dirtyTerrain.Clear();
            foreach (DirtyTerrainChunk pending in terrainFlushBuffer)
            {
                ChunkTerrainData terrain = pending.Chunk.Terrain;
                if (terrain == null || terrain.IsDisposed || pending.Chunk.DataStatus != ChunkDataStatus.Ready) continue;
                try
                {
                    var cells = new TerrainBuildingCellState[pending.Cells.Count];
                    int index = 0;
                    foreach (int cell in pending.Cells) cells[index++] = TerrainBuildingCellState.Capture(terrain, cell);
                    SendTerrainCells(pending.WorldKey, pending.Chunk, cells, null, 0);
                }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            terrainFlushBuffer.Clear();
        }

        private void OnServerTerrainChunkRequest(NetworkConnectionToClient connection, NetworkTerrainChunkRequest request)
        {
            if (!NetworkServer.active || request.RequestToken == 0 || string.IsNullOrWhiteSpace(request.DimensionId) ||
                request.WorldKey != GameplayWorldRegistry.ExecutionWorldKey ||
                connection?.identity?.GetComponent<NetworkWorldPlayer>()?.IsInSpace != false)
                return;
            ChunkMgr manager = ChunkMgr.ExistingInstance;
            var address = new RuntimeWorldAddress(request.DimensionId, new Int2(request.ChunkX, request.ChunkY));
            if (manager == null || !manager.TryGetChunkRuntime(address, out ChunkRuntime chunk) ||
                !manager.IsTerrainRestoredForEnvironment(chunk)) return;
            var cells = new TerrainBuildingCellState[checked(chunk.Terrain.Width * chunk.Terrain.Height)];
            for (int i = 0; i < cells.Length; i++) cells[i] = TerrainBuildingCellState.Capture(chunk.Terrain, i);
            SendTerrainCells(request.WorldKey, chunk, cells, connection, request.RequestToken);
        }

        private void SendTerrainCells(string world, ChunkRuntime chunk, TerrainBuildingCellState[] cells,
            NetworkConnectionToClient target, uint requestToken)
        {
            int limit = Transport.active?.GetMaxPacketSize(Channels.Reliable) ?? 32 * 1024;
            int batchSize = Math.Min(64, Math.Max(1, (limit - 1024) / 64));
            long revision = ++nextTerrainRevision;
            for (int offset = 0; offset < cells.Length; offset += batchSize)
            {
                int count = Math.Min(batchSize, cells.Length - offset);
                var batch = new TerrainBuildingCellState[count];
                Array.Copy(cells, offset, batch, 0, count);
                var message = new NetworkTerrainChunkMessage
                {
                    WorldKey = world, DimensionId = chunk.Address.DimensionId,
                    ChunkX = chunk.Address.ChunkOrigin.X, ChunkY = chunk.Address.ChunkOrigin.Y,
                    TotalCells = checked(chunk.Terrain.Width * chunk.Terrain.Height), Revision = revision,
                    RequestToken = requestToken, Cells = batch
                };
                if (target != null) SendTerrainToObserver(target, message);
                else foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                    SendTerrainToObserver(connection, message);
            }
        }

        private static void SendTerrainToObserver(NetworkConnectionToClient connection, NetworkTerrainChunkMessage message)
        {
            if (connection == null || connection is LocalConnectionToClient || !connection.isAuthenticated || !connection.isReady ||
                connection.identity?.GetComponent<NetworkWorldPlayer>() is not NetworkWorldPlayer player ||
                player.IsWorldTransferPending || !player.TryGetSpaceObserver(out SpaceNetworkObserver realm) ||
                realm.InSpace || realm.WorldKey != message.WorldKey) return;
            message.RealmRevision = realm.RealmRevision;
            connection.Send(message, Channels.Reliable);
        }
        #endregion

        #region 客户端窗口补拉与差量应用
        private bool PrepareTerrainClient(out ChunkMgr manager, out uint realm)
        {
            manager = ChunkMgr.ExistingInstance;
            if (!NetworkGameplayWorldScope.TryGetReadyLocalSurface(out realm) || manager?.WorldRuntime == null)
                return false;
            if (!ReferenceEquals(terrainClientWorld, manager.WorldRuntime) ||
                terrainClientEpoch != manager.WorldRuntime.Epoch || terrainClientRealm != realm)
            {
                ResetTerrainClient();
                terrainClientWorld = manager.WorldRuntime;
                terrainClientEpoch = manager.WorldRuntime.Epoch;
                terrainClientRealm = realm;
            }
            return true;
        }

        private NetworkTerrainChunkCursor GetTerrainCursor(ChunkRuntime chunk)
        {
            if (!terrainCursors.TryGetValue(chunk.Address, out NetworkTerrainChunkCursor cursor) ||
                !ReferenceEquals(cursor.Terrain, chunk.Terrain))
                terrainCursors[chunk.Address] = cursor = new NetworkTerrainChunkCursor(chunk.Terrain);
            return cursor;
        }

        private void OnClientTerrainChunkMessage(NetworkTerrainChunkMessage message)
        {
            if (NetworkServer.active || !PrepareTerrainClient(out ChunkMgr manager, out uint realm) || message.RealmRevision != realm ||
                message.WorldKey != GameplayWorldRegistry.LocalPresentationWorldKey || string.IsNullOrWhiteSpace(message.DimensionId)) return;
            var address = new RuntimeWorldAddress(message.DimensionId, new Int2(message.ChunkX, message.ChunkY));
            // 未生成或已卸载的区块不缓存无限消息，就绪后从服务器重新拉完整状态。
            if (!manager.TryGetChunkRuntime(address, out ChunkRuntime chunk) || !manager.IsTerrainRestoredForEnvironment(chunk)) return;
            NetworkTerrainChunkCursor cursor = GetTerrainCursor(chunk);
            if (message.RequestToken != 0 && message.RequestToken != cursor.RequestToken) return;
            try
            {
                if (!cursor.Apply(message.Revision, message.RequestToken, message.TotalCells, message.Cells))
                    cursor.RequireSnapshot();
                else if (message.RequestToken != 0)
                    cursor.NextRequestTime = Time.unscaledTime + 2f;
            }
            catch (Exception exception)
            {
                terrainCursors.Remove(address);
                Debug.LogWarning($"[联机建造] 地形复制失败，将重新拉取：{exception.Message}");
            }
        }

        private void TickTerrainReplication()
        {
            using var marker = TerrainReplicationMarker.Auto();
            if (serverHandlersRegistered && NetworkServer.active) FlushTerrainChanges();
            // Host 已直接持有服务器地形，禁止再给自己创建游标和重复请求区块快照。
            if (NetworkServer.active) return;
            if (Time.unscaledTime < nextTerrainWindowScan || !PrepareTerrainClient(out ChunkMgr manager, out uint realm)) return;
            nextTerrainWindowScan = Time.unscaledTime + .25f;
            terrainPruneBuffer.Clear();
            foreach (var pair in terrainCursors)
                if (!manager.TryGetChunkRuntime(pair.Key, out ChunkRuntime chunk) ||
                    !manager.IsTerrainRestoredForEnvironment(chunk) || !ReferenceEquals(pair.Value.Terrain, chunk.Terrain))
                    terrainPruneBuffer.Add(pair.Key);
            foreach (RuntimeWorldAddress address in terrainPruneBuffer) terrainCursors.Remove(address);
            int requests = 0;
            foreach (var pair in manager.Chunks)
            {
                if (!manager.IsTerrainRestoredForEnvironment(pair.Value)) continue;
                NetworkTerrainChunkCursor cursor = GetTerrainCursor(pair.Value);
                if (cursor.HasFullState || Time.unscaledTime < cursor.NextRequestTime || requests >= 8) continue;
                cursor.BeginSnapshot(NextNonZeroToken(ref nextTerrainRequest));
                cursor.NextRequestTime = Time.unscaledTime + 2f;
                NetworkClient.Send(new NetworkTerrainChunkRequest
                {
                    RealmRevision = realm, RequestToken = cursor.RequestToken,
                    WorldKey = GameplayWorldRegistry.LocalPresentationWorldKey,
                    DimensionId = pair.Key.DimensionId,
                    ChunkX = pair.Key.ChunkOrigin.X, ChunkY = pair.Key.ChunkOrigin.Y
                }, Channels.Reliable);
                requests++;
            }
        }
        #endregion
    }
}
