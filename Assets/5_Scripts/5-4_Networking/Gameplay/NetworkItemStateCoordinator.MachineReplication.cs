using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Building;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;

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

        // 机器复制独占脏节点及捕获缓冲，物品通道只负责连接生命周期和最终发布。
        private sealed class WorldMachineStateReplication
        {
            private readonly Dictionary<int, string> dirtyMechanicalWorlds = new();
            private readonly List<int> mechanicalFlushKeys = new();
            private readonly List<MachineEntity> mechanicalSyncNodes = new();
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
            }

            internal void Tick(float now)
            {
                if (!NetworkServer.active || now < nextMechanicalScanTime) return;
                nextMechanicalScanTime = now + ScanInterval;
                FlushMechanicalStates();
            }

            internal void ForgetPendingNode(int id) => dirtyMechanicalWorlds.Remove(id);

            internal void SendBaseline(NetworkConnectionToClient connection)
            {
                connection.Send(new NetworkMechanicalNodeMessage { Reset = true, WorldKey = GameplayWorldRegistry.ExecutionWorldKey });
                MachineWorld.CollectNodes(mechanicalSyncNodes);
                foreach (MachineEntity node in mechanicalSyncNodes)
                    SendSnapshot(connection, node);
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
                if (NetworkServer.active)
                    SendToGroundObservers(new NetworkMechanicalNodeMessage { NodeGuid = id, Removed = true, WorldKey = MachineWorld.WorldKey }, observerWorld: true);
            }

            /// <summary>动力解算只发转速变化，不重复发送整份加工库存。</summary>
            private void OnMechanicalSpeedChanged(MachineEntity node)
            {
                if (MachineWorld.IsShipScope) return;
                if (NetworkServer.active && node != null)
                    SendToGroundObservers(new NetworkMechanicalNodeMessage
                    { NodeGuid = node.Id, WorldKey = node.ScopeKey, SpeedOnly = true, Rpm = node.Rpm,
                        EntryDirection = node.EntryDirection }, observerWorld: true);
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
                return new NetworkMechanicalNodeMessage
                { NodeGuid = node.Id, Payload = payload, WorldKey = MachineWorld.WorldKey, Rpm = node.Rpm,
                    EntryDirection = node.EntryDirection };
            }

            internal static void SendSnapshot(NetworkConnectionToClient connection, MachineEntity node)
                => connection.Send(CaptureNodeSnapshot(node));

            internal static void BroadcastSnapshot(MachineEntity node)
            { if (!MachineWorld.IsShipScope) SendToGroundObservers(CaptureNodeSnapshot(node), node.ScopeKey, observerWorld: true); }

            /// <summary>客户端把服务器快照还原为数据节点并通知区块 BRG。</summary>
            internal void ApplyServerSnapshot(NetworkMechanicalNodeMessage message)
            {
                if (NetworkServer.active) return;
                if (!string.IsNullOrEmpty(message.WorldKey) && message.WorldKey.StartsWith("ship:", StringComparison.Ordinal))
                {
                    using (MachineWorld.UseScope(message.WorldKey)) ApplyClientMechanicalNode(message);
                    return;
                }
                if (!NetworkGameplayWorldScope.TryGetReadyLocalSurface(out _) ||
                    message.WorldKey != GameplayWorldRegistry.LocalPresentationWorldKey ||
                    !GameplayWorldRegistry.TryGet(message.WorldKey, out _)) return;
                using (GameplayWorldRegistry.UseWorld(message.WorldKey)) ApplyClientMechanicalNode(message);
            }
            private void ApplyClientMechanicalNode(NetworkMechanicalNodeMessage message)
            {
                if (message.Reset) { MachineWorld.ApplyRemoteReset(); return; }
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
