using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Building;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using Mirror;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkItemStateCoordinator
    {
        #region 机器状态复制

        private readonly WorldMachineStateReplication machineStateReplication = new();

        private static NetworkMechanicalNodeMessage CreateMechanicalSnapshot(MachineEntity node) =>
            WorldMachineStateReplication.CaptureNodeSnapshot(node);
        private static void SendMechanicalSnapshot(NetworkConnectionToClient connection, MachineEntity node) =>
            WorldMachineStateReplication.SendSnapshot(connection, node);
        private static void BroadcastMechanicalSnapshot(MachineEntity node) =>
            WorldMachineStateReplication.BroadcastSnapshot(node);
        private static void BroadcastMechanicalMessage(NetworkMechanicalNodeMessage message) =>
            WorldMachineStateReplication.SendToMechanicalObservers(message);

        // 机器复制独占脏节点及捕获缓冲，物品通道只负责连接生命周期和最终发布。
        private sealed class WorldMachineStateReplication
        {
            private static readonly Dictionary<(string World, int Guid), Vector2> mechanicalPositions = new();
            private readonly Dictionary<int, string> dirtyMechanicalWorlds = new();
            private readonly List<int> mechanicalFlushKeys = new();
            private readonly List<MachineEntity> mechanicalSyncNodes = new();
            private readonly Dictionary<RuntimeWorldAddress, List<NetworkMechanicalNodeMessage>> pendingClientChunks = new();
            private readonly List<RuntimeWorldAddress> pendingClientChunkKeys = new();
            private WorldRuntime clientWorld;
            private long clientEpoch;
            private uint clientRealm;
            private float nextMechanicalScanTime;

            internal void StartServerSide()
            {
                MachineWorld.NodeStateChanged += OnMechanicalStateChanged;
                MachineWorld.NodeRemoved += OnMechanicalRemoved;
                MachineWorld.VisualSpeedChanged += OnMechanicalSpeedChanged;
            }

            internal void StopServerSide()
            {
                MachineWorld.NodeStateChanged -= OnMechanicalStateChanged;
                MachineWorld.NodeRemoved -= OnMechanicalRemoved;
                MachineWorld.VisualSpeedChanged -= OnMechanicalSpeedChanged;
                dirtyMechanicalWorlds.Clear();
                mechanicalFlushKeys.Clear();
                mechanicalSyncNodes.Clear();
                mechanicalPositions.Clear();
            }

            internal void Tick(float now)
            {
                if (!NetworkServer.active)
                {
                    DrainPendingClientChunks();
                    if (NetworkClient.active && now >= nextMechanicalScanTime)
                    { nextMechanicalScanTime = now + ScanInterval; PruneUnloadedClientMachines(); }
                    return;
                }
                if (now < nextMechanicalScanTime) return;
                nextMechanicalScanTime = now + ScanInterval;
                FlushMechanicalStates();
            }

            internal void ForgetPendingNode(int id) => dirtyMechanicalWorlds.Remove(id);
            internal void ResetClientSide()
            { pendingClientChunks.Clear(); clientWorld = null; clientEpoch = 0; clientRealm = 0; }

            internal void PrepareClientChunkForPersistence(string worldKey, Vector2Int origin)
            {
                if (!GameplayWorldRegistry.TryGet(worldKey, out GameplayWorldRuntime world) || world.Chunks == null) return;
                using var scope = GameplayWorldRegistry.UseWorld(worldKey);
                RuntimeWorldAddress address = world.Chunks.ResolveWorldAddress(origin);
                if (world.Chunks.TryGetChunkRuntime(address, out ChunkRuntime chunk) &&
                    world.Chunks.IsChunkStateRestoredForNetworking(chunk)) return;
                MachineWorld.CollectNodes(mechanicalSyncNodes);
                foreach (MachineEntity node in mechanicalSyncNodes)
                    if (world.Chunks.ResolveRuntimeChunkOrigin(node.Position) == origin) MachineWorld.ApplyRemoteRemoval(node.Id);
            }

            internal void SendBaseline(NetworkConnectionToClient connection)
            {
                SendToMechanicalObserver(connection, new NetworkMechanicalNodeMessage { Reset = true, WorldKey = GameplayWorldRegistry.ExecutionWorldKey });
                MachineWorld.CollectNodes(mechanicalSyncNodes);
                NetworkWorldPlayer player = connection?.identity?.GetComponent<NetworkWorldPlayer>();
                SpaceNetworkObserver observer = default;
                bool hasObserver = player != null && player.TryGetSpaceObserver(out observer) && !observer.InSpace;
                int distance = Mathf.Max(4, player?.CorePlayer?.GetComponentInChildren<Mod_ChunkLoader>(true)?.CurrentDestroyChunkDistance ?? 4);
                foreach (MachineEntity node in mechanicalSyncNodes)
                    if (MachineWorld.IsShipScope || hasObserver && IsChunkInsideObserverWindow(node.ScopeKey,
                            ChunkMgr.Instance.ResolveRuntimeChunkOrigin(node.Position), observer.Position.LocalMeters.ToVector2(), distance))
                        SendSnapshot(connection, node);
            }

            internal void SendChunk(NetworkConnectionToClient connection, string worldKey, Vector2Int origin)
            {
                if (connection?.identity == null || !GameplayWorldRegistry.TryGet(worldKey, out GameplayWorldRuntime world)) return;
                using (GameplayWorldRegistry.UseWorld(worldKey))
                {
                    RuntimeWorldAddress address = world.Chunks.ResolveWorldAddress(origin);
                    if (!world.Chunks.TryGetChunkRuntime(address, out ChunkRuntime chunk) ||
                        !world.Chunks.IsChunkStateRestoredForNetworking(chunk)) return;
                    SendToMechanicalObserver(connection, new NetworkMechanicalNodeMessage
                    { ResetChunk = true, WorldKey = worldKey, Position = origin }, requested: true);
                    MachineWorld.CollectNodes(mechanicalSyncNodes);
                    foreach (MachineEntity node in mechanicalSyncNodes)
                        if (world.Chunks.ResolveRuntimeChunkOrigin(node.Position) == origin) SendSnapshot(connection, node);
                }
            }

            /// <summary>机械状态变化只记录身份，定时合并同一节点的进度与库存更新。</summary>
            private void OnMechanicalStateChanged(MachineEntity node)
            {
                if (NetworkServer.active && node != null)
                    dirtyMechanicalWorlds[node.Id] = node.ScopeKey;
            }

            /// <summary>拆除和摧毁立即广播数据格删除。</summary>
            private void OnMechanicalRemoved(int id)
            {
                dirtyMechanicalWorlds.Remove(id);
                if (MachineWorld.IsShipScope) return;
                string world = MachineWorld.WorldKey;
                if (NetworkServer.active && mechanicalPositions.Remove((world, id), out Vector2 position))
                    SendToMechanicalObservers(new NetworkMechanicalNodeMessage { NodeGuid = id, Removed = true, WorldKey = world, Position = position });
            }

            /// <summary>动力解算只发转速变化，不重复发送整份加工库存。</summary>
            private void OnMechanicalSpeedChanged(MachineEntity node)
            {
                if (MachineWorld.IsShipScope) return;
                if (NetworkServer.active && node != null)
                {
                    mechanicalPositions[(node.ScopeKey, node.Id)] = node.Position;
                    SendToMechanicalObservers(new NetworkMechanicalNodeMessage
                    { NodeGuid = node.Id, WorldKey = node.ScopeKey, SpeedOnly = true, Rpm = node.Rpm,
                        EntryDirection = node.EntryDirection, Position = node.Position });
                }
            }

            /// <summary>把本帧变化节点合并为每半秒一次的权威快照。</summary>
            private void FlushMechanicalStates()
            {
                if (dirtyMechanicalWorlds.Count == 0) return;
                // 循环复用待同步节点快照，避免机械状态频繁变化时反复分配数组。
                mechanicalFlushKeys.Clear();
                mechanicalFlushKeys.AddRange(dirtyMechanicalWorlds.Keys);
                foreach (int id in mechanicalFlushKeys)
                {
                    if (!dirtyMechanicalWorlds.Remove(id, out string worldKey)) continue;
                    using var worldScope = GameplayWorldRegistry.UseExistingWorldOrMachineScope(worldKey);
                    MachineEntity node = MachineWorld.GetById(id);
                    if (node != null) BroadcastSnapshot(node);
                }
                mechanicalFlushKeys.Clear();
            }

            /// <summary>独立节点快照传输，不生成 NetworkItemSpawnMessage。</summary>
            internal static NetworkMechanicalNodeMessage CaptureNodeSnapshot(MachineEntity node)
            {
                ItemData snapshot = MachineWorld.CaptureSnapshot(node);
                if (snapshot == null ||
                    !ItemNetworkStateSerialization.TrySerializeItemData(snapshot, out byte[] payload))
                    throw new InvalidOperationException("机械联机快照序列化失败：" + node?.Id);
                mechanicalPositions[(MachineWorld.WorldKey, node.Id)] = node.Position;
                return new NetworkMechanicalNodeMessage
                { NodeGuid = node.Id, Payload = payload, WorldKey = MachineWorld.WorldKey, Rpm = node.Rpm,
                    EntryDirection = node.EntryDirection, Position = node.Position };
            }

            internal static void SendSnapshot(NetworkConnectionToClient connection, MachineEntity node)
                => SendToMechanicalObserver(connection, CaptureNodeSnapshot(node), requested: true);

            internal static void BroadcastSnapshot(MachineEntity node)
            { if (!MachineWorld.IsShipScope) SendToMechanicalObservers(CaptureNodeSnapshot(node)); }

            internal static void SendToMechanicalObservers(NetworkMechanicalNodeMessage message)
            {
                foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
                    SendToMechanicalObserver(connection, message);
            }

            private static void SendToMechanicalObserver(NetworkConnectionToClient connection, NetworkMechanicalNodeMessage message, bool requested = false)
            {
                if (connection == null || !connection.isAuthenticated || !connection.isReady ||
                    connection.identity?.GetComponent<NetworkWorldPlayer>() is not NetworkWorldPlayer player ||
                    player.IsWorldTransferPending || !player.TryGetSpaceObserver(out SpaceNetworkObserver observer)) return;
                if (GameplayWorldRegistry.UsesLocalCoordinates(message.WorldKey))
                {
                    if (GameplayWorldRegistry.GetItemWorldKey(player.CorePlayer) != message.WorldKey) return;
                }
                else
                {
                    if (observer.InSpace || observer.WorldKey != message.WorldKey) return;
                    if (!message.Reset)
                    {
                        if (!GameplayWorldRegistry.TryGet(message.WorldKey, out GameplayWorldRuntime world) || world.Chunks == null) return;
                        using var scope = GameplayWorldRegistry.UseWorld(message.WorldKey);
                        int distance = Mathf.Max(requested ? 32 : 4,
                            player.CorePlayer?.GetComponentInChildren<Mod_ChunkLoader>(true)?.CurrentDestroyChunkDistance ?? 0);
                        if (!IsChunkInsideObserverWindow(message.WorldKey, world.Chunks.ResolveRuntimeChunkOrigin(message.Position),
                            observer.Position.LocalMeters.ToVector2(), distance)) return;
                    }
                }
                message.RealmRevision = observer.RealmRevision;
                connection.Send(message);
            }

            /// <summary>客户端把服务器快照还原为数据节点并通知区块 BRG。</summary>
            internal void ApplyServerSnapshot(NetworkMechanicalNodeMessage message)
            {
                if (NetworkServer.active) return;
                if (!string.IsNullOrEmpty(message.WorldKey) && message.WorldKey.StartsWith("ship:", StringComparison.Ordinal))
                {
                    NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
                    if (player == null || player.IsWorldTransferPending || player.RealmRevision != message.RealmRevision ||
                        GameplayWorldRegistry.GetItemWorldKey(player.CorePlayer) != message.WorldKey) return;
                    using (MachineWorld.UseScope(message.WorldKey)) ApplyClientMechanicalNode(message);
                    return;
                }
                if (!NetworkGameplayWorldScope.TryGetReadyLocalSurface(out _) || message.RealmRevision != NetworkGameplayWorldScope.LocalRevision ||
                    message.WorldKey != GameplayWorldRegistry.LocalPresentationWorldKey ||
                    !GameplayWorldRegistry.TryGet(message.WorldKey, out GameplayWorldRuntime world) || world.Chunks?.WorldRuntime == null) return;
                if (!ReferenceEquals(clientWorld, world.Chunks.WorldRuntime) || clientEpoch != world.Chunks.WorldRuntime.Epoch ||
                    clientRealm != message.RealmRevision)
                {
                    pendingClientChunks.Clear(); clientWorld = world.Chunks.WorldRuntime;
                    clientEpoch = clientWorld.Epoch; clientRealm = message.RealmRevision;
                }
                using (GameplayWorldRegistry.UseWorld(message.WorldKey))
                {
                    if (message.Reset) { pendingClientChunks.Clear(); ApplyClientMechanicalNode(message); return; }
                    NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
                    if (!message.Removed && !message.ResetChunk &&
                        (player == null || !IsPositionInsideObserverWindow(player, message.WorldKey, message.Position))) return;
                    RuntimeWorldAddress address = world.Chunks.ResolveWorldAddress(message.Position);
                    if (pendingClientChunks.TryGetValue(address, out var queued) ||
                        !world.Chunks.TryGetChunkRuntime(address, out ChunkRuntime chunk) || !world.Chunks.IsChunkStateRestoredForNetworking(chunk))
                    {
                        if (queued == null) pendingClientChunks.Add(address, queued = new List<NetworkMechanicalNodeMessage>());
                        QueueClientMechanicalMessage(queued, message);
                        return;
                    }
                    ApplyClientMechanicalNode(message);
                }
            }

            // 同一区块的清理、基线和增量按接收顺序等待地形恢复。
            private static void QueueClientMechanicalMessage(List<NetworkMechanicalNodeMessage> queue, NetworkMechanicalNodeMessage message)
            {
                if (message.ResetChunk) queue.Clear();
                else
                {
                    for (int i = queue.Count - 1; i >= 0; i--)
                    {
                        NetworkMechanicalNodeMessage old = queue[i];
                        if (old.ResetChunk) break;
                        if (old.NodeGuid != message.NodeGuid) continue;
                        if (message.SpeedOnly && !old.SpeedOnly) break;
                        queue.RemoveAt(i);
                    }
                }
                queue.Add(message);
            }

            private void DrainPendingClientChunks()
            {
                if (pendingClientChunks.Count == 0 || !NetworkGameplayWorldScope.TryGetReadyLocalSurface(out uint realm) ||
                    realm != clientRealm || clientWorld == null || clientWorld.Epoch != clientEpoch ||
                    !GameplayWorldRegistry.TryGet(clientWorld.WorldId, out GameplayWorldRuntime world) ||
                    !ReferenceEquals(world.Chunks?.WorldRuntime, clientWorld)) return;
                using var scope = GameplayWorldRegistry.UseWorld(clientWorld.WorldId);
                pendingClientChunkKeys.Clear();
                pendingClientChunkKeys.AddRange(pendingClientChunks.Keys);
                foreach (RuntimeWorldAddress address in pendingClientChunkKeys)
                {
                    NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
                    if (player == null || !IsPositionInsideObserverWindow(player, clientWorld.WorldId,
                        new Vector3(address.ChunkOrigin.X, address.ChunkOrigin.Y, 0f)))
                    { pendingClientChunks.Remove(address); continue; }
                    if (!world.Chunks.TryGetChunkRuntime(address, out ChunkRuntime chunk) ||
                        !world.Chunks.IsChunkStateRestoredForNetworking(chunk)) continue;
                    List<NetworkMechanicalNodeMessage> messages = pendingClientChunks[address];
                    pendingClientChunks.Remove(address);
                    foreach (NetworkMechanicalNodeMessage message in messages) ApplyClientMechanicalNode(message);
                }
            }

            private void PruneUnloadedClientMachines()
            {
                if (!NetworkGameplayWorldScope.TryGetReadyLocalSurface(out uint realm) || clientWorld == null ||
                    clientRealm != realm || clientEpoch != clientWorld.Epoch ||
                    !GameplayWorldRegistry.TryGet(clientWorld.WorldId, out GameplayWorldRuntime world) ||
                    !ReferenceEquals(world.Chunks?.WorldRuntime, clientWorld)) return;
                using var scope = GameplayWorldRegistry.UseWorld(clientWorld.WorldId);
                MachineWorld.CollectNodes(mechanicalSyncNodes);
                NetworkWorldPlayer player = NetworkClient.localPlayer?.GetComponent<NetworkWorldPlayer>();
                foreach (MachineEntity node in mechanicalSyncNodes)
                    if (player == null || !IsPositionInsideObserverWindow(player, clientWorld.WorldId, node.Position) ||
                        !world.Chunks.TryGetChunkRuntime(world.Chunks.ResolveWorldAddress(node.Position), out _))
                        MachineWorld.ApplyRemoteRemoval(node.Id);
            }

            private void ApplyClientMechanicalNode(NetworkMechanicalNodeMessage message)
            {
                if (message.Reset) { MachineWorld.ApplyRemoteReset(); return; }
                if (message.ResetChunk)
                {
                    ChunkMgr manager = ChunkMgr.ExistingInstance;
                    Vector2Int origin = manager.ResolveRuntimeChunkOrigin(message.Position);
                    MachineWorld.CollectNodes(mechanicalSyncNodes);
                    foreach (MachineEntity node in mechanicalSyncNodes)
                        if (manager.ResolveRuntimeChunkOrigin(node.Position) == origin) MachineWorld.ApplyRemoteRemoval(node.Id);
                    return;
                }
                if (message.Removed) { MachineWorld.ApplyRemoteRemoval(message.NodeGuid); return; }
                if (message.SpeedOnly)
                { MachineWorld.ApplyRemoteSpeed(message.NodeGuid, message.Rpm, message.EntryDirection); return; }
                if (!ItemNetworkStateSerialization.TryDeserializeItemData(message.Payload, out ItemData snapshot) ||
                    snapshot.Guid != message.NodeGuid)
                    throw new InvalidOperationException("机械联机节点快照损坏：" + message.NodeGuid);
                MachineWorld.ApplyRemoteSnapshot(snapshot, message.Rpm, message.EntryDirection);
            }
        }
        #endregion
    }
}
