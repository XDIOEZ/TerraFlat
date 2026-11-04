using System;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 联机玩家地表归属与复活
        // 登陆资格由正式世界目录决定，目标地形交给对应世界运行时独立准备。
        public bool CanTransferToNetworkSurface(string world, out string reason)
        {
            reason = "目标星球地表地址或生成定义无效。";
            if (string.IsNullOrWhiteSpace(world) || SpaceMgr.IsSpaceScene(world)) return false;
            WorldAddress address = WorldAddress.FromWorldKey(world);
            if (!address.IsValid || !address.IsSurface ||
                !DimensionManager.Instance.TryGetDefinition(address.DimensionId, out _)) return false;
            DimensionManager.Instance.EnsureWorldData(address);
            reason = null; return true;
        }

        public bool CompleteNetworkWorldTransfer(Player player, string world, Vector3 position)
        {
            if (!GameNetwork.HasStateAuthority || player?.Data == null || State == null ||
                !float.IsFinite(position.x) || !float.IsFinite(position.y) ||
                !GameplayWorldRegistry.TryGet(world, out GameplayWorldRuntime runtime)) return false;
            SpacePassengerState passenger = GetPassenger(player);
            if (passenger.ConsolePieceId != null) ClearControl(passenger.ConsolePieceId);
            RemoveNetworkPassengerInput(player.ProfileName);
            EndNetworkAttack(player.ProfileName);
            passenger.ShipId = passenger.ConsolePieceId = null;
            passenger.Supported = passenger.IsInSpace = passenger.SurfaceDescending = passenger.LandingResolved = false;
            passenger.DescentDriving = passenger.DescentControlEntered = passenger.LandingSelected = false;
            passenger.FreeMotion = new SpaceMotionState { PositionMeters = SpaceVector2.FromVector2(position) };
            passenger.FreeWalkVelocity = default;
            passenger.LandingWorldKey = world; passenger.LandingX = position.x; passenger.LandingY = position.y;
            unchecked { ++passenger.RealmRevision; }
            player.Data.CurrentSceneName = world; player.Data.transform.position = position;
            if (player != ItemMgr.Instance?.User_Player)
            {
                player.transform.SetParent(null, true);
                SceneManager.MoveGameObjectToScene(player.gameObject, runtime.Scene);
                ItemWorldPlacement.AttachToSceneItemCategory(player, player.gameObject);
            }
            // 同世界本机先提交实体落点，避免观察者从旧 Transform 读回死亡位置。
            if (player.gameObject.scene == runtime.Scene)
                using (GameplayWorldRegistry.UseWorld(world)) PlayerTeleportService.ApplySurfacePose(player, position);
            if (player != ItemMgr.Instance?.User_Player)
                foreach (Item child in player.GetComponentsInChildren<Item>(true)) ItemMgr.Instance.NotifyRuntimeItemMoved(child);
            if (owner?.PlayerData_Dict != null) owner.PlayerData_Dict[player.ProfileName] = player.Data;
            if (player == ItemMgr.Instance?.User_Player && TryGetNetworkObserver(player.ProfileName, out SpaceNetworkObserver observer))
                GameManager.Instance.ApplyNetworkLocalSpaceObserver(player, observer);
            return true;
        }

        public bool TryBeginNetworkSurfaceTransfer(Player player, string world, Vector2 position,
            bool isDirectTeleport = false)
        {
            if (!GameNetwork.IsOnline || !GameNetwork.HasStateAuthority || State == null || player?.Data == null ||
                !float.IsFinite(position.x) || !float.IsFinite(position.y)) return false;
            if (!CanTransferToNetworkSurface(world, out string reason))
            { Debug.LogWarning("[太空联机] " + reason, this); return false; }
            SpacePassengerState passenger = GetPassenger(player);
            passenger.IsInSpace = false; passenger.LandingWorldKey = world;
            unchecked { ++passenger.RealmRevision; }
            // 同世界 GM 传送保留新代际门禁，但不要求重新显示维度加载幕。
            passenger.DirectSurfaceTeleportRevision = isDirectTeleport ? passenger.RealmRevision : 0;
            if (!passenger.Supported)
            {
                passenger.LandingX = position.x; passenger.LandingY = position.y;
                passenger.FreeMotion.PositionMeters = SpaceVector2.FromVector2(position);
            }
            player.Data.CurrentSceneName = world;
            player.Data.transform.position = position;
            if (player == ItemMgr.Instance?.User_Player && TryGetNetworkObserver(player.ProfileName, out SpaceNetworkObserver observer))
                GameManager.Instance.ApplyNetworkLocalSpaceObserver(player, observer);
            return true;
        }

        public bool ExecuteNetworkRespawn(string profile, out string reason)
        {
            reason = "重生请求无法开始。";
            Player actor = FindNetworkPlayer(profile);
            bool accepted = NetworkDimensionBridge.RequestRespawn(actor, default, default, null);
            if (accepted) reason = null;
            return accepted;
        }
        #endregion
    }
}
