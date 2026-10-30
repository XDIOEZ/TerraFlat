using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

        private sealed class PendingPersistedChunk
        {
            public string WorldKey;
            public GameSaveData SaveOwner;
            public RuntimeWorldAddress Address;
            public long Epoch, Revision;
            public uint Token, Realm;
            public int Bootstrap, Received, TotalBytes, Count;
            public uint Hash;
            public byte[][] Parts;
            public float RetryAt;
            public bool Ready, NeedsTerrainRefresh;
            public int Waiters;
            public readonly List<NetworkTerrainChunkMessage> Deltas = new();
            public readonly TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        private readonly Dictionary<(string, long, RuntimeWorldAddress), PendingPersistedChunk> persistedChunks = new();
        private readonly List<(string, long, RuntimeWorldAddress)> persistedPrune = new();
        private CancellationTokenSource persistedServerCancellation;
        private sealed class ServerAuthorityChunkDemand
        {
            public NetworkPersistedChunkRequest Request;
            public ChunkMgr Manager;
            public WorldRuntime World;
            public GameSaveData Save;
            public IDisposable Lease;
            public CancellationTokenSource Cancellation;
            public Task<ChunkRuntime> Load;
            public int InFlight;
            public bool Served;
        }
        private sealed class ClientEntityBaseline
        {
            public uint Token;
            public float RetryAt;
        }
        private readonly Dictionary<(NetworkConnectionToClient, string, RuntimeWorldAddress), ServerAuthorityChunkDemand> serverAuthorityChunks = new();
        private readonly List<(NetworkConnectionToClient, string, RuntimeWorldAddress)> serverAuthorityPrune = new();
        private readonly HashSet<(NetworkConnectionToClient, string, RuntimeWorldAddress, long, uint, int, uint, bool)> serverChunkRequests = new();
        private readonly Dictionary<RuntimeWorldAddress, ClientEntityBaseline> clientEntityBaselines = new();
        private readonly HashSet<RuntimeWorldAddress> clientEntityReady = new();
        private readonly List<RuntimeWorldAddress> clientEntityPrune = new();

        private void StartTerrainServerBridge()
        {
            NetworkServer.RegisterHandler<NetworkTerrainChunkRequest>(
                NetworkGameplayWorldScope.Wrap<NetworkTerrainChunkRequest>(OnServerTerrainChunkRequest, observerWorld: true), true);
            NetworkServer.RegisterHandler<NetworkPersistedChunkRequest>(OnServerPersistedChunkRequest, true);
            NetworkServer.RegisterHandler<NetworkChunkEntityBaselineRequest>(OnServerChunkEntityBaselineRequest, true);
            persistedServerCancellation = new CancellationTokenSource();
            SaveDataMgr.AuthoritativeTerrainCellChanged += QueueTerrainCell;
        }

        private void StopTerrainServerBridge()
        {
            NetworkServer.UnregisterHandler<NetworkTerrainChunkRequest>();
            NetworkServer.UnregisterHandler<NetworkPersistedChunkRequest>();
            NetworkServer.UnregisterHandler<NetworkChunkEntityBaselineRequest>();
            persistedServerCancellation?.Cancel();
            foreach (ServerAuthorityChunkDemand demand in serverAuthorityChunks.Values) DisposeServerAuthorityChunkDemand(demand);
            serverAuthorityChunks.Clear();
            serverChunkRequests.Clear();
            persistedServerCancellation?.Dispose();
            persistedServerCancellation = null;
            SaveDataMgr.AuthoritativeTerrainCellChanged -= QueueTerrainCell;
            dirtyTerrain.Clear();
            terrainFlushBuffer.Clear();
        }

        private void StartTerrainClientBridge()
        {
            NetworkClient.RegisterHandler<NetworkTerrainChunkMessage>(OnClientTerrainChunkMessage, true);
            NetworkClient.RegisterHandler<NetworkPersistedChunkPart>(OnClientPersistedChunkPart, true);
            NetworkClient.RegisterHandler<NetworkChunkEntityBaselineComplete>(OnClientChunkEntityBaselineComplete, true);
            ChunkMgr.PrepareNetworkChunkPersistenceAsync = PrepareNetworkPersistedChunkAsync;
            ResetTerrainClient();
        }

        private void StopTerrainClientBridge()
        {
            NetworkClient.UnregisterHandler<NetworkTerrainChunkMessage>();
            NetworkClient.UnregisterHandler<NetworkPersistedChunkPart>();
            NetworkClient.UnregisterHandler<NetworkChunkEntityBaselineComplete>();
            if (ChunkMgr.PrepareNetworkChunkPersistenceAsync == PrepareNetworkPersistedChunkAsync)
                ChunkMgr.PrepareNetworkChunkPersistenceAsync = null;
            foreach (PendingPersistedChunk pending in persistedChunks.Values)
            {
                pending.Completion.TrySetCanceled();
            }
            persistedChunks.Clear();
            ResetTerrainClient();
        }

        private void ResetTerrainClient()
        {
            terrainCursors.Clear();
            terrainPruneBuffer.Clear();
            clientEntityBaselines.Clear();
            clientEntityReady.Clear();
            clientEntityPrune.Clear();
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
                connection?.identity?.GetComponent<NetworkWorldPlayer>() is not NetworkWorldPlayer player ||
                player.IsInSpace || !player.TryGetSpaceObserver(out SpaceNetworkObserver observer) || observer.InSpace ||
                observer.WorldKey != request.WorldKey || !IsChunkInsideObserverWindow(request.WorldKey,
                    new Vector2Int(request.ChunkX, request.ChunkY), observer.Position.LocalMeters.ToVector2(),
                    Mathf.Max(32, player.CorePlayer?.GetComponentInChildren<Mod_ChunkLoader>(true)?.CurrentDestroyChunkDistance ?? 0)))
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
            int distance = Mathf.Max(message.RequestToken == 0 ? 4 : 32,
                player.CorePlayer?.GetComponentInChildren<Mod_ChunkLoader>(true)?.CurrentDestroyChunkDistance ?? 0);
            if (!IsChunkInsideObserverWindow(message.WorldKey, new Vector2Int(message.ChunkX, message.ChunkY),
                realm.Position.LocalMeters.ToVector2(), distance)) return;
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
            {
                terrainCursors[chunk.Address] = cursor = new NetworkTerrainChunkCursor(chunk.Terrain);
                if (!clientEntityBaselines.ContainsKey(chunk.Address))
                    clientEntityBaselines.Add(chunk.Address, new ClientEntityBaseline { Token = NextNonZeroToken(ref nextTerrainRequest) });
            }
            return cursor;
        }

        private void OnClientTerrainChunkMessage(NetworkTerrainChunkMessage message)
        {
            if (NetworkServer.active || !PrepareTerrainClient(out ChunkMgr manager, out uint realm) || message.RealmRevision != realm ||
                message.WorldKey != GameplayWorldRegistry.LocalPresentationWorldKey || string.IsNullOrWhiteSpace(message.DimensionId)) return;
            var address = new RuntimeWorldAddress(message.DimensionId, new Int2(message.ChunkX, message.ChunkY));
            var key = (message.WorldKey, manager.WorldRuntime.Epoch, address);
            if (message.RequestToken == 0 && persistedChunks.TryGetValue(key, out PendingPersistedChunk pending))
            {
                if (pending.Deltas.Count < 1024) pending.Deltas.Add(message);
                else { pending.Deltas.Clear(); pending.NeedsTerrainRefresh = true; }
                return;
            }
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
            if (serverHandlersRegistered && NetworkServer.active) { PruneServerAuthorityChunks(); FlushTerrainChanges(); }
            // Host 已直接持有服务器地形，禁止再给自己创建游标和重复请求区块快照。
            if (NetworkServer.active) return;
            TickPersistedChunkRequests();
            if (Time.unscaledTime < nextTerrainWindowScan || !PrepareTerrainClient(out ChunkMgr manager, out uint realm)) return;
            nextTerrainWindowScan = Time.unscaledTime + .25f;
            TickClientEntityBaselineRequests(manager, realm);
            terrainPruneBuffer.Clear();
            foreach (var pair in terrainCursors)
                if (!manager.TryGetChunkRuntime(pair.Key, out ChunkRuntime chunk) ||
                    !manager.IsTerrainRestoredForEnvironment(chunk) || !ReferenceEquals(pair.Value.Terrain, chunk.Terrain))
                    terrainPruneBuffer.Add(pair.Key);
            foreach (RuntimeWorldAddress address in terrainPruneBuffer) terrainCursors.Remove(address);
            int requests = 0;
            NetworkWorldPlayer localPlayer = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            foreach (var pair in manager.Chunks)
            {
                if (!manager.IsTerrainRestoredForEnvironment(pair.Value)) continue;
                NetworkTerrainChunkCursor cursor = GetTerrainCursor(pair.Value);
                CompletePersistedChunkTerrainBaseline(manager, pair.Key, cursor);
                if (localPlayer != null && IsPositionInsideObserverWindow(localPlayer, manager.WorldRuntime.WorldId,
                    new Vector3(pair.Key.ChunkOrigin.X, pair.Key.ChunkOrigin.Y, 0f)) &&
                    !clientEntityReady.Contains(pair.Key) && !clientEntityBaselines.ContainsKey(pair.Key))
                    clientEntityBaselines.Add(pair.Key, new ClientEntityBaseline { Token = NextNonZeroToken(ref nextTerrainRequest) });
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

        #region 区块持久状态按需同步
        private Task PrepareNetworkPersistedChunkAsync(string world, long epoch, RuntimeWorldAddress address, CancellationToken cancellation)
        {
            var key = (world, epoch, address);
            if (persistedChunks.TryGetValue(key, out PendingPersistedChunk old))
            {
                // 旧区块已取消后重新进入时，必须重新取得可能已释放的权威快照。
                if (!old.Ready && !old.Completion.Task.IsCanceled && IsCurrentPersistedChunkWorld(old))
                    return AwaitPersistedChunkForCallerAsync(old, cancellation);
                old.Completion.TrySetCanceled();
                persistedChunks.Remove(key);
            }
            var pending = new PendingPersistedChunk { WorldKey = world, Epoch = epoch, Address = address, SaveOwner = SaveDataMgr.Instance?.SaveData };
            persistedChunks.Add(key, pending);
            return AwaitPersistedChunkForCallerAsync(pending, cancellation);
        }

        private static bool IsCurrentPersistedChunkWorld(PendingPersistedChunk pending)
            => ReferenceEquals(pending.SaveOwner, SaveDataMgr.Instance?.SaveData) &&
                GameplayWorldRegistry.TryGet(pending.WorldKey, out GameplayWorldRuntime world) &&
                world.Chunks?.WorldRuntime?.Epoch == pending.Epoch;

        private static async Task AwaitPersistedChunkForCallerAsync(PendingPersistedChunk pending, CancellationToken cancellation)
        {
            Interlocked.Increment(ref pending.Waiters);
            int finished = 0;
            void FinishWaiter()
            {
                if (Interlocked.Exchange(ref finished, 1) == 0 && Interlocked.Decrement(ref pending.Waiters) == 0 &&
                    !pending.Completion.Task.IsCompleted) pending.Completion.TrySetCanceled();
            }
            try
            {
                if (!cancellation.CanBeCanceled) { await pending.Completion.Task.ConfigureAwait(false); return; }
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellation.Register(() => { cancelled.TrySetCanceled(); FinishWaiter(); }))
                {
                    if (await Task.WhenAny(pending.Completion.Task, cancelled.Task).ConfigureAwait(false) != pending.Completion.Task)
                        await cancelled.Task.ConfigureAwait(false);
                    await pending.Completion.Task.ConfigureAwait(false);
                }
            }
            finally
            {
                FinishWaiter();
            }
        }

        private bool TryResolvePersistedChunkRequest(PendingPersistedChunk pending, out uint realm, out int bootstrap)
        {
            realm = 0; bootstrap = 0;
            if (!NetworkClient.active) return false;
            NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            if (player?.TryGetSpaceObserver(out SpaceNetworkObserver observer) == true && !observer.InSpace &&
                observer.WorldKey == pending.WorldKey && player.RealmRevision == observer.RealmRevision)
            { realm = observer.RealmRevision; return true; }
            if (NetworkManager.singleton is FlatWorldGameNetworkManager manager &&
                manager.ClientBootstrapWorld == pending.WorldKey && manager.ClientBootstrapTransfer != 0 && player == null)
            { bootstrap = manager.ClientBootstrapTransfer; return true; }
            return false;
        }

        private void TickPersistedChunkRequests()
        {
            persistedPrune.Clear();
            int sent = 0;
            foreach (var pair in persistedChunks)
            {
                PendingPersistedChunk pending = pair.Value;
                if (pending.Completion.Task.IsCanceled || !IsCurrentPersistedChunkWorld(pending))
                { pending.Completion.TrySetCanceled(); persistedPrune.Add(pair.Key); continue; }
                if (pending.Ready && Volatile.Read(ref pending.Waiters) == 0 &&
                    GameplayWorldRegistry.TryGet(pending.WorldKey, out GameplayWorldRuntime currentWorld) &&
                    (!currentWorld.Chunks.TryGetChunkRuntime(pending.Address, out ChunkRuntime pendingChunk) ||
                     pendingChunk.DataStatus == ChunkDataStatus.Absent))
                { persistedPrune.Add(pair.Key); continue; }
                pending.NeedsTerrainRefresh |= NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>()?.IsWorldTransferPending == true;
                if (pending.Ready || Time.unscaledTime < pending.RetryAt || sent >= 8 ||
                    !TryResolvePersistedChunkRequest(pending, out uint realm, out int bootstrap)) continue;
                if (pending.Token == 0 || pending.Realm != realm || pending.Bootstrap != bootstrap)
                    pending.Token = NextNonZeroToken(ref nextTerrainRequest);
                pending.Realm = realm; pending.Bootstrap = bootstrap;
                pending.NeedsTerrainRefresh |= bootstrap != 0 ||
                    NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>()?.IsWorldTransferPending == true;
                pending.Parts = null; pending.Received = 0;
                pending.RetryAt = Time.unscaledTime + 2f;
                NetworkClient.Send(new NetworkPersistedChunkRequest
                {
                    WorldKey = pending.WorldKey, DimensionId = pending.Address.DimensionId,
                    ChunkX = pending.Address.ChunkOrigin.X, ChunkY = pending.Address.ChunkOrigin.Y,
                    WorldEpoch = pending.Epoch, RequestToken = pending.Token,
                    RealmRevision = realm, BootstrapTransfer = bootstrap
                }, Channels.Reliable);
                sent++;
            }
            foreach (var key in persistedPrune) persistedChunks.Remove(key);
        }

        private static bool AuthorizePersistedChunkRequest(NetworkConnectionToClient connection, NetworkPersistedChunkRequest request)
        {
            if (connection == null || !connection.isAuthenticated ||
                !NetworkServer.connections.TryGetValue(connection.connectionId, out NetworkConnectionToClient current) ||
                !ReferenceEquals(current, connection) || request.RequestToken == 0 ||
                request.WorldEpoch <= 0 || string.IsNullOrWhiteSpace(request.WorldKey) ||
                string.IsNullOrWhiteSpace(request.DimensionId) ||
                global::WorldAddress.FromWorldKey(request.WorldKey).DimensionId != request.DimensionId) return false;
            if (request.BootstrapTransfer != 0)
                return connection.identity == null && NetworkManager.singleton is FlatWorldGameNetworkManager manager &&
                    manager.IsAuthorizedBootstrapChunkRequest(connection, request.WorldKey, request.BootstrapTransfer,
                        new Vector2Int(request.ChunkX, request.ChunkY));
            NetworkWorldPlayer player = connection.identity?.GetComponent<NetworkWorldPlayer>();
            return player != null && player.TryGetSpaceObserver(out SpaceNetworkObserver observer) && !observer.InSpace &&
                observer.WorldKey == request.WorldKey && observer.RealmRevision == request.RealmRevision &&
                player.RealmRevision == request.RealmRevision && IsChunkInsideObserverWindow(request.WorldKey,
                    new Vector2Int(request.ChunkX, request.ChunkY), observer.Position.LocalMeters.ToVector2(),
                    Mathf.Max(32, player.CorePlayer?.GetComponentInChildren<Mod_ChunkLoader>(true)?.CurrentDestroyChunkDistance ?? 0));
        }

        internal static bool IsChunkInsideObserverWindow(string worldKey, Vector2Int origin, Vector2 position, int distance)
        {
            GameSaveData save = SaveDataMgr.Instance?.SaveData;
            global::WorldAddress world = global::WorldAddress.FromWorldKey(worldKey);
            if (save?.PlanetData_Dict == null ||
                !save.PlanetData_Dict.TryGetValue(worldKey, out PlanetData planet) &&
                !save.PlanetData_Dict.TryGetValue(world.RootWorldKey, out planet)) return false;
            Vector2Int size = PlanetData.NormalizeChunkSize(planet.ChunkSize);
            if (origin.x % size.x != 0 || origin.y % size.y != 0 || !float.IsFinite(position.x) || !float.IsFinite(position.y)) return false;
            Vector2 center = new(origin.x + size.x * .5f, origin.y + size.y * .5f);
            Vector2 delta = WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds)
                ? bounds.ShortestDelta(position, center) : center - position;
            return Mathf.Abs(delta.x) <= (distance + 1) * size.x && Mathf.Abs(delta.y) <= (distance + 1) * size.y;
        }

        // 临时请求与正式观察窗口共享区块数据票，直到正式窗口接管或连接离开。
        private ServerAuthorityChunkDemand BeginServerAuthorityChunkDemand(NetworkConnectionToClient connection, NetworkPersistedChunkRequest request)
        {
            var address = new RuntimeWorldAddress(request.DimensionId, new Int2(request.ChunkX, request.ChunkY));
            var key = (connection, request.WorldKey, address);
            if (!GameplayWorldRegistry.TryGet(request.WorldKey, out GameplayWorldRuntime world) || world.Chunks == null)
                throw new InvalidOperationException("目标世界尚未注册。");
            if (serverAuthorityChunks.TryGetValue(key, out ServerAuthorityChunkDemand old))
            {
                if (ReferenceEquals(old.Manager, world.Chunks) && ReferenceEquals(old.World, world.Chunks.WorldRuntime) &&
                    ReferenceEquals(old.Save, SaveDataMgr.Instance?.SaveData) && old.Request.WorldEpoch == request.WorldEpoch)
                { old.Request = request; old.InFlight++; return old; }
                serverAuthorityChunks.Remove(key);
                DisposeServerAuthorityChunkDemand(old);
            }
            var demand = new ServerAuthorityChunkDemand
            {
                Request = request, Manager = world.Chunks, Save = SaveDataMgr.Instance?.SaveData,
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(persistedServerCancellation.Token), InFlight = 1
            };
            try
            {
                using (GameplayWorldRegistry.UseWorld(request.WorldKey))
                {
                    demand.Lease = demand.Manager.AcquireNetworkAuthorityChunkDemand(address);
                    demand.World = demand.Manager.WorldRuntime;
                    demand.Load = demand.Manager.RequestNetworkAuthorityChunkAsync(address, demand.Cancellation.Token);
                }
                serverAuthorityChunks.Add(key, demand);
                return demand;
            }
            catch { DisposeServerAuthorityChunkDemand(demand); throw; }
        }

        private static void DisposeServerAuthorityChunkDemand(ServerAuthorityChunkDemand demand)
        {
            demand.Cancellation?.Cancel();
            demand.Cancellation?.Dispose();
            if (demand.Lease == null) return;
            if (demand.World != null && GameplayWorldRegistry.TryGet(demand.World.WorldId, out _))
            { using var scope = GameplayWorldRegistry.UseWorld(demand.World.WorldId); demand.Lease.Dispose(); }
            else demand.Lease.Dispose();
            demand.Lease = null;
        }

        private async Task<bool> AwaitServerAuthorityChunkAsync(NetworkConnectionToClient connection,
            NetworkPersistedChunkRequest request, ServerAuthorityChunkDemand demand)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 15d;
            var address = new RuntimeWorldAddress(request.DimensionId, new Int2(request.ChunkX, request.ChunkY));
            CancellationToken cancellation = demand.Cancellation.Token;
            using (var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                Task timeout = Task.Delay(TimeSpan.FromSeconds(15), timeoutCancellation.Token);
                if (await Task.WhenAny(demand.Load, timeout) != demand.Load)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Debug.LogWarning("[联机] 区块数据加载未完成，将重新请求：" + address);
                    return false;
                }
                timeoutCancellation.Cancel();
                await demand.Load;
            }
            while (!cancellation.IsCancellationRequested && AuthorizePersistedChunkRequest(connection, request) &&
                ReferenceEquals(demand.Save, SaveDataMgr.Instance?.SaveData) && ReferenceEquals(demand.Manager.WorldRuntime, demand.World))
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                { Debug.LogWarning("[联机] 区块实体恢复未完成，将重新请求：" + address); return false; }
                using (GameplayWorldRegistry.UseWorld(request.WorldKey))
                    if (demand.Manager.TryRestoreNetworkAuthorityChunk(address)) return true;
                await Task.Yield();
            }
            return false;
        }

        // 仅最后一个未完成的连接需求归还自己的数据票，其他共享等待者继续使用生成任务。
        private void FinishServerAuthorityChunkDemand(NetworkConnectionToClient connection,
            NetworkPersistedChunkRequest request, ServerAuthorityChunkDemand demand)
        {
            if (demand == null || --demand.InFlight != 0 || demand.Served) return;
            var address = new RuntimeWorldAddress(request.DimensionId, new Int2(request.ChunkX, request.ChunkY));
            var key = (connection, request.WorldKey, address);
            if (serverAuthorityChunks.TryGetValue(key, out ServerAuthorityChunkDemand current) && ReferenceEquals(current, demand))
            {
                serverAuthorityChunks.Remove(key);
                DisposeServerAuthorityChunkDemand(demand);
            }
        }

        private void PruneServerAuthorityChunks()
        {
            serverAuthorityPrune.Clear();
            foreach (var pair in serverAuthorityChunks)
            {
                NetworkConnectionToClient connection = pair.Key.Item1;
                ServerAuthorityChunkDemand demand = pair.Value;
                NetworkWorldPlayer player = connection?.identity?.GetComponent<NetworkWorldPlayer>();
                bool connected = connection != null && NetworkServer.connections.TryGetValue(connection.connectionId, out var current) &&
                    ReferenceEquals(current, connection) && connection.isAuthenticated;
                if (!connected || !ReferenceEquals(demand.Save, SaveDataMgr.Instance?.SaveData) ||
                    !ReferenceEquals(demand.Manager.WorldRuntime, demand.World) || demand.Load.IsFaulted || demand.Load.IsCanceled)
                { serverAuthorityPrune.Add(pair.Key); continue; }
                bool relevant;
                if (player == null) relevant = connected && AuthorizePersistedChunkRequest(connection, demand.Request);
                else
                {
                    relevant = connected && player.TryGetSpaceObserver(out SpaceNetworkObserver observer) && !observer.InSpace &&
                        observer.WorldKey == demand.Request.WorldKey && observer.RealmRevision == player.RealmRevision &&
                        (demand.Request.BootstrapTransfer != 0 || demand.Request.RealmRevision == player.RealmRevision) &&
                        IsChunkInsideObserverWindow(demand.Request.WorldKey, new Vector2Int(pair.Key.Item3.ChunkOrigin.X, pair.Key.Item3.ChunkOrigin.Y),
                            observer.Position.LocalMeters.ToVector2(),
                            Mathf.Max(demand.InFlight != 0 ? 32 : 4,
                                player.CorePlayer?.GetComponentInChildren<Mod_ChunkLoader>(true)?.CurrentDestroyChunkDistance ?? 4));
                }
                if (!relevant || demand.InFlight == 0 && (!demand.Served ||
                    demand.Manager.IsNetworkAuthorityChunkInRuntimeWindow(pair.Key.Item3)))
                    serverAuthorityPrune.Add(pair.Key);
            }
            foreach (var key in serverAuthorityPrune)
                if (serverAuthorityChunks.Remove(key, out ServerAuthorityChunkDemand demand)) DisposeServerAuthorityChunkDemand(demand);
        }

        private async void OnServerPersistedChunkRequest(NetworkConnectionToClient connection, NetworkPersistedChunkRequest request)
        {
            if (!NetworkServer.active || !AuthorizePersistedChunkRequest(connection, request)) return;
            var origin = new Vector2Int(request.ChunkX, request.ChunkY);
            var requestKey = (connection, request.WorldKey,
                new RuntimeWorldAddress(request.DimensionId, new Int2(request.ChunkX, request.ChunkY)),
                request.WorldEpoch, request.RealmRevision, request.BootstrapTransfer, request.RequestToken, false);
            if (!serverChunkRequests.Add(requestKey)) return;
            ServerAuthorityChunkDemand demand = null;
            try
            {
                demand = BeginServerAuthorityChunkDemand(connection, request);
                SaveDataMgr saves = SaveDataMgr.Instance;
                GameSaveData saveOwner = saves.SaveData;
                CancellationToken token = demand.Cancellation.Token;
                if (!await AwaitServerAuthorityChunkAsync(connection, request, demand)) return;
                if (token.IsCancellationRequested || !ReferenceEquals(saves.SaveData, saveOwner) ||
                    !NetworkServer.active || !AuthorizePersistedChunkRequest(connection, request)) return;
                byte[] payload;
                long revision;
                using (GameplayWorldRegistry.UseWorld(request.WorldKey))
                {
                    FlushTerrainChanges();
                    ScanServerItemsForChunk(request.WorldKey, origin);
                    payload = saves.CaptureNetworkChunkSnapshot(request.WorldKey, origin);
                    revision = ++nextTerrainRevision;
                }
                if (payload == null || payload.Length == 0 || payload.Length > NetworkGameplayProtocol.MaxSnapshotBytes)
                    throw new InvalidDataException("区块资料大小无效。");
                int count = (payload.Length + NetworkGameplayProtocol.SnapshotChunkBytes - 1) / NetworkGameplayProtocol.SnapshotChunkBytes;
                uint hash = NetworkGameplayProtocol.CalculatePayloadHash(payload);
                bool requiresTerrainRefresh = request.BootstrapTransfer != 0 ||
                    connection.identity?.GetComponent<NetworkWorldPlayer>()?.IsWorldTransferPending == true;
                for (int i = 0; i < count; i++)
                {
                    int offset = i * NetworkGameplayProtocol.SnapshotChunkBytes;
                    var part = new byte[Math.Min(NetworkGameplayProtocol.SnapshotChunkBytes, payload.Length - offset)];
                    Buffer.BlockCopy(payload, offset, part, 0, part.Length);
                    connection.Send(new NetworkPersistedChunkPart
                    {
                        WorldKey = request.WorldKey, DimensionId = request.DimensionId, ChunkX = request.ChunkX, ChunkY = request.ChunkY,
                        WorldEpoch = request.WorldEpoch, RealmRevision = request.RealmRevision, BootstrapTransfer = request.BootstrapTransfer,
                        RequestToken = request.RequestToken, Revision = revision, Index = i, Count = count,
                        TotalBytes = payload.Length, Hash = hash, RequiresTerrainRefresh = requiresTerrainRefresh, Data = part
                    }, Channels.Reliable);
                }
                SendPersistedChunkItemStates(connection, request.WorldKey, origin);
                demand.Served = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogWarning("[联机] 区块同步失败：" + error.Message); }
            finally
            {
                try { FinishServerAuthorityChunkDemand(connection, request, demand); }
                finally { serverChunkRequests.Remove(requestKey); }
            }
        }

        private async void OnServerChunkEntityBaselineRequest(NetworkConnectionToClient connection, NetworkChunkEntityBaselineRequest message)
        {
            var request = new NetworkPersistedChunkRequest
            {
                WorldKey = message.WorldKey, DimensionId = message.DimensionId, ChunkX = message.ChunkX, ChunkY = message.ChunkY,
                WorldEpoch = message.WorldEpoch, RealmRevision = message.RealmRevision, RequestToken = message.RequestToken
            };
            if (!NetworkServer.active || !AuthorizePersistedChunkRequest(connection, request) ||
                connection.identity?.GetComponent<NetworkWorldPlayer>()?.IsWorldTransferPending != false) return;
            var requestKey = (connection, request.WorldKey,
                new RuntimeWorldAddress(request.DimensionId, new Int2(request.ChunkX, request.ChunkY)),
                request.WorldEpoch, request.RealmRevision, request.BootstrapTransfer, request.RequestToken, true);
            if (!serverChunkRequests.Add(requestKey)) return;
            ServerAuthorityChunkDemand demand = null;
            try
            {
                demand = BeginServerAuthorityChunkDemand(connection, request);
                if (!await AwaitServerAuthorityChunkAsync(connection, request, demand) ||
                    connection.identity?.GetComponent<NetworkWorldPlayer>()?.IsWorldTransferPending != false ||
                    !AuthorizePersistedChunkRequest(connection, request)) return;
                Vector2Int origin = new(request.ChunkX, request.ChunkY);
                using (GameplayWorldRegistry.UseWorld(request.WorldKey))
                {
                    ScanServerItemsForChunk(request.WorldKey, origin);
                    SendPersistedChunkItemStates(connection, request.WorldKey, origin);
                }
                connection.Send(new NetworkChunkEntityBaselineComplete
                {
                    WorldKey = request.WorldKey, DimensionId = request.DimensionId, ChunkX = request.ChunkX, ChunkY = request.ChunkY,
                    WorldEpoch = request.WorldEpoch, RealmRevision = request.RealmRevision, RequestToken = request.RequestToken
                }, Channels.Reliable);
                demand.Served = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { Debug.LogWarning("[联机] 区块实体基线失败：" + error.Message); }
            finally
            {
                try { FinishServerAuthorityChunkDemand(connection, request, demand); }
                finally { serverChunkRequests.Remove(requestKey); }
            }
        }

        private void TickClientEntityBaselineRequests(ChunkMgr manager, uint realm)
        {
            int sent = 0;
            clientEntityPrune.Clear();
            NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
            foreach (RuntimeWorldAddress address in clientEntityReady)
                if (player == null || !manager.TryGetChunkRuntime(address, out ChunkRuntime ready) ||
                    !manager.IsTerrainRestoredForEnvironment(ready) || !IsPositionInsideObserverWindow(player, manager.WorldRuntime.WorldId,
                        new Vector3(address.ChunkOrigin.X, address.ChunkOrigin.Y, 0f))) clientEntityPrune.Add(address);
            foreach (var address in clientEntityPrune) clientEntityReady.Remove(address);
            clientEntityPrune.Clear();
            foreach (var pair in clientEntityBaselines)
            {
                if (player == null || !manager.TryGetChunkRuntime(pair.Key, out ChunkRuntime chunk) ||
                    !manager.IsTerrainRestoredForEnvironment(chunk) || !IsPositionInsideObserverWindow(player, manager.WorldRuntime.WorldId,
                        new Vector3(pair.Key.ChunkOrigin.X, pair.Key.ChunkOrigin.Y, 0f)))
                { clientEntityPrune.Add(pair.Key); continue; }
                if (Time.unscaledTime < pair.Value.RetryAt || sent >= 8) continue;
                pair.Value.RetryAt = Time.unscaledTime + 2f;
                NetworkClient.Send(new NetworkChunkEntityBaselineRequest
                {
                    WorldKey = manager.WorldRuntime.WorldId, DimensionId = pair.Key.DimensionId,
                    ChunkX = pair.Key.ChunkOrigin.X, ChunkY = pair.Key.ChunkOrigin.Y,
                    WorldEpoch = manager.WorldRuntime.Epoch, RealmRevision = realm, RequestToken = pair.Value.Token
                }, Channels.Reliable);
                sent++;
            }
            foreach (var address in clientEntityPrune) clientEntityBaselines.Remove(address);
        }

        private void OnClientChunkEntityBaselineComplete(NetworkChunkEntityBaselineComplete message)
        {
            if (!PrepareTerrainClient(out ChunkMgr manager, out uint realm) || message.RealmRevision != realm ||
                manager.WorldRuntime.WorldId != message.WorldKey || manager.WorldRuntime.Epoch != message.WorldEpoch) return;
            var address = new RuntimeWorldAddress(message.DimensionId, new Int2(message.ChunkX, message.ChunkY));
            if (clientEntityBaselines.TryGetValue(address, out ClientEntityBaseline pending) && pending.Token == message.RequestToken)
            { clientEntityBaselines.Remove(address); clientEntityReady.Add(address); }
        }

        private void OnClientPersistedChunkPart(NetworkPersistedChunkPart part)
        {
            if (NetworkServer.active || string.IsNullOrWhiteSpace(part.WorldKey) || string.IsNullOrWhiteSpace(part.DimensionId)) return;
            var address = new RuntimeWorldAddress(part.DimensionId, new Int2(part.ChunkX, part.ChunkY));
            if (!persistedChunks.TryGetValue((part.WorldKey, part.WorldEpoch, address), out PendingPersistedChunk pending) ||
                !IsCurrentPersistedChunkWorld(pending) || pending.Completion.Task.IsCanceled || pending.Ready || part.RequestToken != pending.Token ||
                part.RealmRevision != pending.Realm || part.BootstrapTransfer != pending.Bootstrap ||
                !TryResolvePersistedChunkRequest(pending, out uint realm, out int bootstrap) || realm != pending.Realm || bootstrap != pending.Bootstrap)
                return;
            if (part.Revision <= 0 || part.TotalBytes <= 0 || part.TotalBytes > NetworkGameplayProtocol.MaxSnapshotBytes ||
                part.Count != (part.TotalBytes + NetworkGameplayProtocol.SnapshotChunkBytes - 1) / NetworkGameplayProtocol.SnapshotChunkBytes ||
                (uint)part.Index >= (uint)part.Count || part.Data == null || part.Data.Length !=
                Math.Min(NetworkGameplayProtocol.SnapshotChunkBytes, part.TotalBytes - part.Index * NetworkGameplayProtocol.SnapshotChunkBytes))
            { pending.RetryAt = 0f; return; }
            if (pending.Parts == null)
            {
                pending.Parts = new byte[part.Count][]; pending.Count = part.Count; pending.TotalBytes = part.TotalBytes;
                pending.Hash = part.Hash; pending.Revision = part.Revision;
            }
            pending.NeedsTerrainRefresh |= part.RequiresTerrainRefresh;
            if (pending.Count != part.Count || pending.TotalBytes != part.TotalBytes || pending.Hash != part.Hash || pending.Revision != part.Revision)
            { pending.RetryAt = 0f; return; }
            if (pending.Parts[part.Index] != null) return;
            pending.Parts[part.Index] = part.Data;
            pending.RetryAt = Time.unscaledTime + 2f;
            if (++pending.Received != pending.Count) return;
            try
            {
                var payload = new byte[pending.TotalBytes];
                int offset = 0;
                foreach (byte[] bytes in pending.Parts) { Buffer.BlockCopy(bytes, 0, payload, offset, bytes.Length); offset += bytes.Length; }
                if (NetworkGameplayProtocol.CalculatePayloadHash(payload) != pending.Hash) throw new InvalidDataException("区块资料校验失败。");
                SaveDataMgr.Instance.ApplyNetworkChunkSnapshot(payload, pending.WorldKey,
                    new Vector2Int(part.ChunkX, part.ChunkY), pending.Epoch, pending.Revision);
                Vector2Int weatherRegion = WeatherMgr.ResolveRegionalWeatherStorageOrigin(pending.WorldKey, new Vector2Int(part.ChunkX, part.ChunkY));
                if (!SaveDataMgr.Instance.IsPersistedWeatherRegionReady(pending.WorldKey, weatherRegion))
                    throw new InvalidDataException("区块所属天气区域未就绪。");
                WeatherMgr.ApplyNetworkPersistedWeatherRegion(pending.WorldKey, weatherRegion);
                machineStateReplication.PrepareClientChunkForPersistence(pending.WorldKey, new Vector2Int(part.ChunkX, part.ChunkY));
                pending.Ready = true; pending.Parts = null;
                pending.NeedsTerrainRefresh |= pending.Bootstrap != 0 ||
                    NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>()?.IsWorldTransferPending == true;
                pending.Completion.TrySetResult(true);
            }
            catch (Exception error)
            {
                pending.Parts = null; pending.RetryAt = 0f;
                Debug.LogWarning("[联机] 区块资料无效，将重新请求：" + error.Message);
            }
        }

        private void CompletePersistedChunkTerrainBaseline(ChunkMgr manager, RuntimeWorldAddress address, NetworkTerrainChunkCursor cursor)
        {
            var key = (manager.WorldRuntime.WorldId, manager.WorldRuntime.Epoch, address);
            if (!persistedChunks.TryGetValue(key, out PendingPersistedChunk pending) || !pending.Ready) return;
            cursor.SetPersistenceBaseline(pending.Revision);
            foreach (NetworkTerrainChunkMessage delta in pending.Deltas)
                if (delta.Revision > pending.Revision && !cursor.Apply(delta.Revision, 0, delta.TotalCells, delta.Cells))
                    pending.NeedsTerrainRefresh = true;
            if (pending.NeedsTerrainRefresh) cursor.RequireSnapshot();
            persistedChunks.Remove(key);
            if (!clientEntityReady.Contains(address) && !clientEntityBaselines.ContainsKey(address))
                clientEntityBaselines.Add(address, new ClientEntityBaseline { Token = NextNonZeroToken(ref nextTerrainRequest) });
        }
        #endregion
    }
}
