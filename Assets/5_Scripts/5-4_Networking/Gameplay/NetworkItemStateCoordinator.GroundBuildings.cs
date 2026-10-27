using System;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkItemStateCoordinator
    {
        #region 普通建筑整批发布
        // 模板和落地建筑先完成整批快照预检，再复用正式机器与物品复制。
        public bool PublishGroundBuildings(IReadOnlyList<ShipGroundBuildingStore.Entry> entries)
        {
            if (!NetworkServer.active || entries == null) return false;
            var machines = new List<NetworkMechanicalNodeMessage>();
            var items = new List<(ShipGroundBuildingStore.Entry Entry, byte[] Payload)>();
            foreach (ShipGroundBuildingStore.Entry entry in entries)
            {
                if (entry?.Machine != null)
                {
                    using var scope = MachineWorld.UseNodeScope(entry.Machine);
                    machines.Add(CreateMechanicalSnapshot(entry.Machine));
                }
                else
                {
                    if (entry?.Item == null || !CanSynchronizeWorldItem(entry.Item)) return false;
                    byte[] payload = CaptureSafely(entry.Item);
                    if (!ItemNetworkStateSerialization.IsValidPayload(payload)) return false;
                    items.Add((entry, payload));
                }
            }
            foreach (NetworkMechanicalNodeMessage message in machines)
            {
                SendToGroundObservers(message, observerWorld: true);
                dirtyMechanicalNodes.Remove(message.NodeGuid);
            }
            foreach (var prepared in items)
            {
                Vector2 position = prepared.Entry.Item.transform.position;
                if (!PublishServerSpawn(prepared.Entry.Item, position, position, .05f, false, prepared.Payload))
                    throw new InvalidOperationException("普通地面建筑网络发布失败。");
            }
            return true;
        }
        #endregion
    }
}
