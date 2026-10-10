using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using MemoryPack;
using Mirror;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkWeatherStateCoordinator
    {
        #region 地表复制缓存

        private const int MaxPendingSurfaceChunks = 256;
        private const int MaxSurfacePayloadBytes = 8 * 1024 * 1024;
        private readonly Dictionary<RuntimeWorldAddress, SurfaceCursor> surfaceCursors = new();
        private readonly Dictionary<RuntimeWorldAddress, PendingSurface> pendingSurfaces = new();
        private readonly Dictionary<RuntimeWorldAddress, SurfaceAssembly> surfaceAssemblies = new();
        private readonly List<RuntimeWorldAddress> surfaceRemoveBuffer = new();
        private WorldRuntime surfaceWorld;
        private PlanetData surfacePlanet;
        private long surfaceWorldEpoch;
        private float nextSurfaceRefresh;
        private int nextSurfaceTransferId;
        private long nextSurfaceRevision;

        private sealed class SurfaceCursor
        {
            public ChunkTerrainData Terrain;
            public long Revision = -1;
            public bool HasFullState;
            public float NextRequestTime;
        }

        private sealed class PendingSurface
        {
            public readonly Dictionary<Vector2Int, WeatherSurfaceCellDelta> Cells = new();
            public long Revision = -1;
            public bool FullState;

            public WeatherSurfaceDelta Capture()
            {
                var result = new WeatherSurfaceDelta();
                foreach (WeatherSurfaceCellDelta cell in Cells.Values)
                    result.Cells.Add(cell);
                return result;
            }
        }

        private sealed class SurfaceAssembly
        {
            public NetworkWeatherSurfaceMessage Header;
            public byte[] Bytes;
            public bool[] Received;
            public int ReceivedCount;
        }

        #endregion

        #region 地表消息生命周期

        private void StartWeatherSurfaceServer()
        {
            NetworkServer.RegisterHandler<NetworkWeatherSurfaceRequest>(OnServerWeatherSurfaceRequest, false);
            SaveDataMgr.AuthoritativeWeatherSurfaceChanged += HandleAuthoritativeWeatherSurfaceChanged;
        }

        private void StopWeatherSurfaceServer()
        {
            NetworkServer.UnregisterHandler<NetworkWeatherSurfaceRequest>();
            SaveDataMgr.AuthoritativeWeatherSurfaceChanged -= HandleAuthoritativeWeatherSurfaceChanged;
        }

        private void StartWeatherSurfaceClient()
        {
            NetworkClient.RegisterHandler<NetworkWeatherSurfaceMessage>(OnClientWeatherSurfaceMessage, false);
            ResetWeatherSurfaceClient();
        }

        private void StopWeatherSurfaceClient()
        {
            NetworkClient.UnregisterHandler<NetworkWeatherSurfaceMessage>();
            ResetWeatherSurfaceClient();
        }

        private void ResetWeatherSurfaceClient()
        {
            surfaceCursors.Clear();
            pendingSurfaces.Clear();
            surfaceAssemblies.Clear();
            surfaceRemoveBuffer.Clear();
            surfaceWorld = null;
            surfacePlanet = null;
            surfaceWorldEpoch = 0;
            nextSurfaceRefresh = 0f;
        }

        private bool PrepareWeatherSurfaceClient(out ChunkMgr manager, out PlanetData planet)
        {
            manager = ChunkMgr.ExistingInstance;
            planet = null;
            if (!NetworkClient.active || NetworkServer.active ||
                GameManager.Instance?.IsInGameWorld != true || manager?.WorldRuntime == null)
                return false;
            planet = SaveDataMgr.Instance?.Active_PlanetData;
            if (planet == null)
                return false;
            if (!ReferenceEquals(surfaceWorld, manager.WorldRuntime) ||
                surfaceWorldEpoch != manager.WorldRuntime.Epoch || !ReferenceEquals(surfacePlanet, planet))
            {
                ResetWeatherSurfaceClient();
                surfaceWorld = manager.WorldRuntime;
                surfaceWorldEpoch = manager.WorldRuntime.Epoch;
                surfacePlanet = planet;
            }
            return true;
        }

        #endregion

        #region 服务端地表批次

        private void OnServerWeatherSurfaceRequest(NetworkConnectionToClient connection,
            NetworkWeatherSurfaceRequest request)
        {
            if (connection?.identity == null || string.IsNullOrWhiteSpace(request.DimensionId) ||
                !NetworkServer.active || SaveDataMgr.Instance?.Active_PlanetData is not PlanetData planet ||
                !string.Equals(planet.Name, request.WorldKey, StringComparison.Ordinal))
                return;
            ChunkMgr manager = ChunkMgr.ExistingInstance;
            var address = new RuntimeWorldAddress(request.DimensionId, new Int2(request.ChunkX, request.ChunkY));
            if (manager == null || !manager.TryGetChunkRuntime(address, out ChunkRuntime chunk) ||
                !manager.IsTerrainRestoredForEnvironment(chunk))
                return;
            SendWeatherSurface(SaveDataMgr.Instance.CaptureWeatherSurfaceChunk(chunk), connection);
        }

        private void HandleAuthoritativeWeatherSurfaceChanged(WeatherSurfaceChunkSnapshot snapshot)
        {
            if (NetworkServer.active)
                SendWeatherSurface(snapshot, null);
        }

        // 大区块沿现有世界快照方式分片，客户端只接收本地已有窗口。
        private void SendWeatherSurface(WeatherSurfaceChunkSnapshot snapshot, NetworkConnectionToClient target)
        {
            byte[] payload = MemoryPackSerializer.Serialize(snapshot.Payload);
            if (payload.Length > MaxSurfacePayloadBytes)
                throw new InvalidOperationException("天气地表载荷超过区块复制上限。");
            int transportLimit = Transport.active?.GetMaxPacketSize(Channels.Reliable) ?? 32 * 1024;
            int partBytes = Math.Min(NetworkGameplayProtocol.SnapshotChunkBytes, Math.Max(512, transportLimit - 1024));
            int partCount = Math.Max(1, (payload.Length + partBytes - 1) / partBytes);
            int transferId = unchecked(++nextSurfaceTransferId);
            long revision = unchecked(++nextSurfaceRevision);
            for (int part = 0; part < partCount; part++)
            {
                int offset = part * partBytes;
                int count = Math.Min(partBytes, payload.Length - offset);
                var bytes = new byte[count];
                Buffer.BlockCopy(payload, offset, bytes, 0, count);
                var message = new NetworkWeatherSurfaceMessage
                {
                    WorldKey = snapshot.WorldKey,
                    DimensionId = snapshot.Address.DimensionId,
                    ChunkX = snapshot.Address.ChunkOrigin.X,
                    ChunkY = snapshot.Address.ChunkOrigin.Y,
                    Revision = revision,
                    FullState = snapshot.FullState,
                    TransferId = transferId,
                    PartIndex = part,
                    PartCount = partCount,
                    PartBytes = partBytes,
                    TotalBytes = payload.Length,
                    Payload = bytes
                };
                if (target != null)
                    target.Send(message);
                else
                    foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                        if (connection?.identity != null)
                            connection.Send(message);
            }
        }

        #endregion

        #region 客户端分片与有界排队

        private void OnClientWeatherSurfaceMessage(NetworkWeatherSurfaceMessage message)
        {
            if (!PrepareWeatherSurfaceClient(out ChunkMgr manager, out PlanetData planet) ||
                !string.Equals(planet.Name, message.WorldKey, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(message.DimensionId))
                return;
            var address = new RuntimeWorldAddress(message.DimensionId, new Int2(message.ChunkX, message.ChunkY));
            if (!IsLocalSurfaceChunk(manager, address, out ChunkRuntime chunk) || !IsValidSurfacePart(message))
                return;
            if (surfaceCursors.TryGetValue(address, out SurfaceCursor previousCursor) &&
                !ReferenceEquals(previousCursor.Terrain, chunk.Terrain))
                surfaceCursors.Remove(address);
            if (surfaceCursors.TryGetValue(address, out SurfaceCursor cursor) &&
                (message.Revision < cursor.Revision ||
                 (message.Revision == cursor.Revision && (!message.FullState || cursor.HasFullState))))
                return;

            if (!surfaceAssemblies.TryGetValue(address, out SurfaceAssembly assembly) ||
                assembly.Header.TransferId != message.TransferId)
            {
                if (assembly != null && message.Revision < assembly.Header.Revision)
                    return;
                EnsureSurfaceQueueCapacity(address);
                assembly = new SurfaceAssembly
                {
                    Header = message,
                    Bytes = new byte[message.TotalBytes],
                    Received = new bool[message.PartCount]
                };
                surfaceAssemblies[address] = assembly;
            }
            if (assembly.Header.TotalBytes != message.TotalBytes || assembly.Header.PartCount != message.PartCount ||
                assembly.Header.PartBytes != message.PartBytes || assembly.Header.Revision != message.Revision ||
                assembly.Header.FullState != message.FullState || assembly.Received[message.PartIndex])
                return;
            Buffer.BlockCopy(message.Payload, 0, assembly.Bytes, message.PartIndex * message.PartBytes, message.Payload.Length);
            assembly.Received[message.PartIndex] = true;
            if (++assembly.ReceivedCount < assembly.Received.Length)
                return;
            surfaceAssemblies.Remove(address);
            try
            {
                WeatherSurfaceDelta payload = MemoryPackSerializer.Deserialize<WeatherSurfaceDelta>(assembly.Bytes);
                int width = chunk.Terrain?.Width ?? manager.ActiveGenerationProfile?.Width ?? planet.ChunkSize.x;
                int height = chunk.Terrain?.Height ?? manager.ActiveGenerationProfile?.Height ?? planet.ChunkSize.y;
                if (payload == null)
                    throw new InvalidOperationException("天气地表载荷为空。");
                payload.Validate(width, height);
                QueueWeatherSurface(address, message.Revision, payload, message.FullState);
                if (manager.IsTerrainRestoredForEnvironment(chunk))
                    ApplyPendingWeatherSurface(address, chunk);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[联机天气] 地表批次应用失败：{exception.Message}");
                if (surfaceCursors.TryGetValue(address, out SurfaceCursor failed))
                    failed.HasFullState = false;
            }
        }

        private static bool IsValidSurfacePart(NetworkWeatherSurfaceMessage message)
        {
            if (message.Revision < 0 || message.Payload == null || message.TotalBytes <= 0 ||
                message.TotalBytes > MaxSurfacePayloadBytes || message.PartBytes < 512 ||
                message.PartBytes > NetworkGameplayProtocol.SnapshotChunkBytes || message.PartCount <= 0 ||
                message.PartCount != (message.TotalBytes + message.PartBytes - 1) / message.PartBytes ||
                (uint)message.PartIndex >= (uint)message.PartCount)
                return false;
            return message.Payload.Length == Math.Min(message.PartBytes,
                message.TotalBytes - message.PartIndex * message.PartBytes);
        }

        private void QueueWeatherSurface(RuntimeWorldAddress address, long revision,
            WeatherSurfaceDelta payload, bool fullState)
        {
            if (!pendingSurfaces.TryGetValue(address, out PendingSurface pending))
            {
                EnsureSurfaceQueueCapacity(address);
                pending = new PendingSurface();
                pendingSurfaces.Add(address, pending);
            }
            if (revision < pending.Revision)
                return;
            if (fullState)
            {
                pending.Cells.Clear();
                pending.FullState = true;
            }
            foreach (WeatherSurfaceCellDelta cell in payload.Cells)
            {
                if (!pending.Cells.TryGetValue(cell.LocalPosition, out WeatherSurfaceCellDelta previous))
                {
                    pending.Cells.Add(cell.LocalPosition, cell);
                    continue;
                }
                if (cell.SoilChanged)
                {
                    previous.SoilChanged = true;
                    previous.SoilSourceTileId = cell.SoilSourceTileId;
                    previous.Water = cell.Water;
                    previous.Fertility = cell.Fertility;
                }
                if (cell.SnowChanged)
                {
                    previous.SnowChanged = true;
                    previous.WeatherDepth = cell.WeatherDepth;
                    previous.Edited = cell.Edited;
                    previous.Depth = cell.Depth;
                    previous.SeasonalDepth = cell.SeasonalDepth;
                }
            }
            pending.Revision = revision;
        }

        private void ApplyPendingWeatherSurface(RuntimeWorldAddress address, ChunkRuntime chunk)
        {
            if (!pendingSurfaces.TryGetValue(address, out PendingSurface pending))
                return;
            if (!surfaceCursors.TryGetValue(address, out SurfaceCursor cursor) ||
                !ReferenceEquals(cursor.Terrain, chunk.Terrain))
            {
                cursor = new SurfaceCursor { Terrain = chunk.Terrain };
                surfaceCursors[address] = cursor;
            }
            if (pending.Revision >= cursor.Revision)
            {
                SaveDataMgr.ApplyWeatherSurfaceDelta(chunk, pending.Capture(), pending.FullState);
                cursor.Revision = pending.Revision;
                cursor.HasFullState |= pending.FullState;
            }
            pendingSurfaces.Remove(address);
        }

        private static bool IsLocalSurfaceChunk(ChunkMgr manager, RuntimeWorldAddress address, out ChunkRuntime chunk)
        {
            return manager.TryGetChunkRuntime(address, out chunk) && chunk != null &&
                   chunk.DataStatus is ChunkDataStatus.Ready or ChunkDataStatus.Requested or ChunkDataStatus.Generating;
        }

        private void EnsureSurfaceQueueCapacity(RuntimeWorldAddress incoming)
        {
            if (pendingSurfaces.ContainsKey(incoming) || surfaceAssemblies.ContainsKey(incoming))
                return;
            int chunkCount = pendingSurfaces.Count;
            foreach (RuntimeWorldAddress address in surfaceAssemblies.Keys)
                if (!pendingSurfaces.ContainsKey(address)) chunkCount++;
            if (chunkCount < MaxPendingSurfaceChunks) return;
            bool found = false;
            RuntimeWorldAddress selected = default;
            float farthest = -1f;
            Vector2 observer = ItemMgr.Instance?.User_Player != null
                ? (Vector2)ItemMgr.Instance.User_Player.transform.position : Vector2.zero;
            void Consider(RuntimeWorldAddress address)
            {
                float distance = WorldTopologyRuntime.ShortestDelta(observer,
                    new Vector2(address.ChunkOrigin.X, address.ChunkOrigin.Y)).sqrMagnitude;
                if (found && distance <= farthest)
                    return;
                found = true;
                selected = address;
                farthest = distance;
            }
            foreach (RuntimeWorldAddress address in pendingSurfaces.Keys) Consider(address);
            foreach (RuntimeWorldAddress address in surfaceAssemblies.Keys) Consider(address);
            if (found)
            {
                pendingSurfaces.Remove(selected);
                surfaceAssemblies.Remove(selected);
                if (surfaceCursors.TryGetValue(selected, out SurfaceCursor cursor))
                    cursor.HasFullState = false;
            }
        }

        #endregion

        #region 本地窗口完整拉取

        private void MaintainWeatherSurfaceClient()
        {
            if (Time.unscaledTime < nextSurfaceRefresh ||
                !PrepareWeatherSurfaceClient(out ChunkMgr manager, out PlanetData planet))
                return;
            nextSurfaceRefresh = Time.unscaledTime + 1f;
            surfaceRemoveBuffer.Clear();
            foreach (KeyValuePair<RuntimeWorldAddress, SurfaceCursor> pair in surfaceCursors)
                if (!manager.TryGetChunkRuntime(pair.Key, out ChunkRuntime chunk) ||
                    !manager.IsTerrainRestoredForEnvironment(chunk) || !ReferenceEquals(pair.Value.Terrain, chunk.Terrain))
                    surfaceRemoveBuffer.Add(pair.Key);
            foreach (RuntimeWorldAddress address in surfaceRemoveBuffer)
                surfaceCursors.Remove(address);
            PruneSurfaceQueue(manager, pendingSurfaces);
            PruneSurfaceQueue(manager, surfaceAssemblies);

            int requests = 0;
            foreach (KeyValuePair<RuntimeWorldAddress, ChunkRuntime> pair in manager.Chunks)
            {
                ChunkRuntime chunk = pair.Value;
                if (!manager.IsTerrainRestoredForEnvironment(chunk))
                    continue;
                ApplyPendingWeatherSurface(pair.Key, chunk);
                if (!surfaceCursors.TryGetValue(pair.Key, out SurfaceCursor cursor))
                {
                    cursor = new SurfaceCursor { Terrain = chunk.Terrain };
                    surfaceCursors.Add(pair.Key, cursor);
                }
                if (cursor.HasFullState || Time.unscaledTime < cursor.NextRequestTime || requests >= 8)
                    continue;
                cursor.NextRequestTime = Time.unscaledTime + 2f;
                NetworkClient.Send(new NetworkWeatherSurfaceRequest
                {
                    WorldKey = planet.Name,
                    DimensionId = pair.Key.DimensionId,
                    ChunkX = pair.Key.ChunkOrigin.X,
                    ChunkY = pair.Key.ChunkOrigin.Y
                });
                requests++;
            }
        }

        private void PruneSurfaceQueue<T>(ChunkMgr manager, Dictionary<RuntimeWorldAddress, T> queue)
        {
            surfaceRemoveBuffer.Clear();
            foreach (RuntimeWorldAddress address in queue.Keys)
                if (!IsLocalSurfaceChunk(manager, address, out _))
                    surfaceRemoveBuffer.Add(address);
            foreach (RuntimeWorldAddress address in surfaceRemoveBuffer)
                queue.Remove(address);
        }

        #endregion
    }
}
