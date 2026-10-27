using System;
using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkItemStateCoordinator
    {
        #region 状态版本与独立位姿

        private static bool IsSameCapturedState(StateRecord state, byte[] payload, ulong version, uint hash)
        {
            if (state.Payload == null) return false;
            if (state.CaptureVersion == version && ReferenceEquals(state.LastCapturedPayload, payload)) return true;
            if (state.Hash != hash || state.Payload.Length != payload.Length) return false;
            for (int i = 0; i < payload.Length; i++)
                if (state.Payload[i] != payload[i]) return false;
            state.CaptureVersion = version;
            state.LastCapturedPayload = payload;
            return true;
        }

        private void BroadcastServerPose(int guid, StateRecord state)
        {
            if (!NetworkServer.active) return;
            SendToGroundObservers(new NetworkItemPoseMessage
            {
                ItemGuid = guid, ItemId = state.ItemId, WorldKey = state.WorldKey,
                StateRevision = state.Revision, PoseRevision = state.PoseRevision,
                Position = state.Position, Rotation = state.Rotation, Scale = state.Scale
            }, state.WorldKey);
        }

        private void OnClientPoseMessage(NetworkItemPoseMessage message)
        {
            if (!AcceptLocalWorld(message.WorldKey)) return;
            if (!GameplayWorldRegistry.TryGet(message.WorldKey, out _)) return;
            using var scope = GameplayWorldRegistry.UseWorld(message.WorldKey);
            if (NetworkServer.active || !clientStates.TryGetValue(message.ItemGuid, out StateRecord state) ||
                state.WorldKey != message.WorldKey || !string.Equals(state.ItemId, message.ItemId, StringComparison.Ordinal) ||
                message.StateRevision != state.Revision || message.PoseRevision <= state.PoseRevision ||
                !IsFinite(message.Position) || !IsFinite(message.Rotation) || !IsFinite(message.Scale)) return;
            state.PoseRevision = message.PoseRevision;
            state.Position = message.Position;
            state.Rotation = message.Rotation;
            state.Scale = SanitizeScale(message.Scale);
            if (pendingClientStates.TryGetValue(message.ItemGuid, out NetworkItemStateMessage pending))
            {
                pending.PoseRevision = state.PoseRevision;
                pending.Position = state.Position;
                pending.Rotation = state.Rotation;
                pending.Scale = state.Scale;
                pendingClientStates[message.ItemGuid] = pending;
            }
            Item item = ItemMgr.Instance?.GetItemByGuid(message.ItemGuid);
            if (ShouldSynchronize(item) && GameplayWorldRegistry.GetItemWorldKey(item) == message.WorldKey &&
                string.Equals(item.itemData.IDName, message.ItemId, StringComparison.Ordinal))
                ApplyClientPose(item, state.Position, state.Rotation, state.Scale);
        }

        private static void ApplyClientPose(Item item, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (item == null || !IsFinite(position) || !IsFinite(rotation) || !IsFinite(scale) ||
                Mod_Droping.IsDropInProgress(item)) return;
            // 局部世界建筑和作物的位置由对应的占地事务更新。
            if (GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.GetItemWorldKey(item)) &&
                (item.PersistenceOwner is FlatWorld.Spaceflight.SpaceSession or ChunkAgricultureRenderer ||
                 item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building)?.Data?.Role == BuildingRole.PlacedBuilding)) return;
            Vector3 logical = WorldTopologyRuntime.NormalizePosition(position);
            Vector3 safeScale = SanitizeScale(scale);
            item.transform.SetPositionAndRotation(WorldLocalPresentation.ProjectPosition(logical), rotation);
            item.transform.localScale = safeScale;
            if (item.itemData.transform != null)
            {
                item.itemData.transform.position = logical;
                item.itemData.transform.rotation = rotation;
                item.itemData.transform.scale = safeScale;
            }
            ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        }

        #endregion
    }
}
