using System;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 联机玩家地表归属与复活
        // 地表目前仍只有一个正式运行时，只能恢复仍在本机保留的真实地表。
        public bool CanTransferToNetworkSurface(string world, out string reason)
        {
            reason = "联机暂不支持进入尚未保留的另一颗星球地表。";
            if (string.IsNullOrWhiteSpace(world) || SpaceMgr.IsSpaceScene(world) ||
                !WorldAddress.FromWorldKey(world).IsSurface || owner?.PlanetData_Dict.ContainsKey(world) != true) return false;
            Scene scene = SceneManager.GetSceneByName(world);
            if (!scene.IsValid() || !scene.isLoaded || ChunkMgr.ExistingInstance?.WorldRuntime == null ||
                DimensionManager.ExistingInstance?.ActiveAddress.WorldKey != world) return false;
            reason = null; return true;
        }

        public bool TryBeginNetworkSurfaceTransfer(Player player, string world, Vector2 position)
        {
            if (!GameNetwork.IsOnline || !GameNetwork.HasStateAuthority || State == null || player?.Data == null ||
                !float.IsFinite(position.x) || !float.IsFinite(position.y)) return false;
            if (!CanTransferToNetworkSurface(world, out string reason))
            { Debug.LogWarning("[太空联机] " + reason, this); return false; }
            SpacePassengerState passenger = GetPassenger(player);
            passenger.IsInSpace = false; passenger.LandingWorldKey = world;
            unchecked { ++passenger.RealmRevision; }
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

        private bool TryBeginNetworkRespawn(Player player, WorldAddress address, Vector3 position, Action preparePlayer)
        {
            if (State == null || player?.Data == null || preparePlayer == null || !address.IsValid || !address.IsSurface) return false;
            if (!GameNetwork.HasStateAuthority)
                return SpaceNetworkBridge.SendIntent(player.ProfileName, new SpaceNetworkIntent { Kind = SpaceNetworkIntentKind.Respawn });
            if (!SpaceVector2.Finite(position.x) || !SpaceVector2.Finite(position.y) ||
                !CanTransferToNetworkSurface(address.WorldKey, out _))
            { Debug.LogWarning("[太空联机] 重生目标地表尚未准备好。", this); return false; }
            ResetPassengerForRespawn(player);
            EndNetworkAttack(player.ProfileName);
            preparePlayer();
            if (!TryBeginNetworkSurfaceTransfer(player, address.WorldKey, position))
                throw new InvalidOperationException("联机重生已准备好的地表归属丢失。");
            player.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller)?.SetGameplayInputLocked(false);
            player.ModuleSave(); owner.PlayerData_Dict[player.ProfileName] = player.Data;
            Capture(owner);
            return true;
        }

        public bool ExecuteNetworkRespawn(string profile, out string reason)
        {
            reason = "玩家尚未死亡或重生目标未准备好";
            Player player = FindNetworkPlayer(profile);
            Mod_DamageReceiver health = player?.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            Mod_PlayerDeathState death = player?.itemMods.GetMod_ByID<Mod_PlayerDeathState>(Mod_PlayerDeathState.ModuleId);
            if (!GameNetwork.HasStateAuthority || health == null || health.Hp > 0f || death == null || !death.IsRuntimeLoaded) return false;
            WorldAddress address;
            Vector3 position;
            if (PlayerMainWorldSpawnStore.TryGetMainWorldSpawn(player.Data, out position, out string world))
                address = WorldAddress.FromWorldKey(world);
            else
            {
                address = Universe.GetSurfaceAddress("earth");
                if (!DimensionTravelProgressStore.TryGetLastPosition(player.Data, address, out position))
                    position = DimensionManager.Instance.TryGetDefinition(address.DimensionId, out DimensionDefinition definition)
                        ? definition.DefaultSpawnPosition : Vector3.zero;
            }
            if (!CanTransferToNetworkSurface(address.WorldKey, out reason)) return false;
            if (!TryBeginNetworkRespawn(player, address, position, death.CompleteNetworkRespawn)) return false;
            reason = null; return true;
        }
        #endregion
    }
}
