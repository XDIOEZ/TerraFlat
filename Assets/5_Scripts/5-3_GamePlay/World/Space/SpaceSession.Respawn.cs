using System;
using System.Collections;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 玩家重生迁移

        // 重生暂时解除船体与下落归属，完整迁移失败时恢复原来的乘员权威状态。
        public bool TryBeginRespawnTransition(Player player, WorldAddress targetAddress,
            Vector3 targetPosition, Action preparePlayerForSave)
        {
            return NetworkDimensionBridge.RequestRespawn(player, targetAddress, targetPosition, preparePlayerForSave);
        }

        public void ResetPassengerForRespawn(Player player)
        {
            if (State == null || player == null) return;
            RestorePassengerSurface(player);
            SpacePassengerState previous = State.Passengers.Find(value => value.ProfileId == player.ProfileName);
            if (!string.IsNullOrWhiteSpace(previous?.ConsolePieceId))
            {
                ClearControl(previous.ConsolePieceId);
                player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera)?.SetOrthographicSize(
                    (float)Math.Max(1d, previous.SavedCameraSize));
            }
            if (player == ItemMgr.Instance?.User_Player)
            {
                ReleaseDrivingCamera();
                pickingNavigationConsole = null;
                ShipPanelSession.CloseForPlayer(this, player);
                SpaceLandingPanelSession.Refresh(null, null);
            }
            SetTerrainSuppression(player, false);
            player.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.SetSpaceUpright(false);
            State.Passengers.RemoveAll(value => value.ProfileId == player.ProfileName);
            string world = player.Data?.CurrentSceneName;
            Vector2 position = WorldLocalPresentation.ToLogical((Vector2)player.transform.position);
            // 中立乘员仍保留地表归属和位置，避免后续联机保存覆盖玩家世界。
            State.Passengers.Add(new SpacePassengerState
            {
                ProfileId = player.ProfileName, LandingResolved = true,
                RealmRevision = previous?.RealmRevision ?? 0,
                LandingWorldKey = SpaceMgr.IsSpaceScene(world) ? null : world,
                LandingX = position.x, LandingY = position.y,
                FreeMotion = new SpaceMotionState { PositionMeters = SpaceVector2.FromVector2(position) }
            });
        }

        private IEnumerator FinishRespawnTransition(SpaceSessionState sessionState, string profileId,
            WorldAddress targetAddress, SpacePassengerState previous, ShipControlRequest previousControl,
            string previousPickingConsole)
        {
            try
            {
                while (DimensionManager.Instance.IsTransitioning) yield return null;
                if (State != sessionState) yield break;
                if (SceneManager.GetActiveScene().name == targetAddress.WorldKey &&
                    GameManager.Instance.IsInGameWorld && !GameManager.Instance.IsWorldEntryInProgress)
                {
                    State.Passengers.RemoveAll(value => value.ProfileId == profileId);
                    Capture(owner);
                }
                else
                {
                    RestoreRespawnPassenger(sessionState, profileId, previous,
                        previousControl, previousPickingConsole);
                    Capture(owner);
                    if (ReferenceEquals(SaveDataMgr.Instance?.SaveData, owner))
                        SaveDataMgr.Instance.Save_And_WriteToDisk();
                }
            }
            finally
            {
                transitionRunning = false;
            }
        }

        private void RestoreRespawnPassenger(SpaceSessionState sessionState, string profileId,
            SpacePassengerState previous, ShipControlRequest previousControl, string previousPickingConsole)
        {
            if (State != sessionState) return;
            State.Passengers.RemoveAll(value => value.ProfileId == profileId);
            if (previous != null) State.Passengers.Add(previous);
            if (previousControl != null) controlRequests[previousControl.ConsolePieceId] = previousControl;
            pickingNavigationConsole = previousPickingConsole;
            Player player = ItemMgr.Instance?.User_Player;
            if (player == null || player.ProfileName != profileId || previous == null) return;
            SetTerrainSuppression(player, previous.Supported);
            RestorePassengerPose(player);
            if (!string.IsNullOrWhiteSpace(previous.ConsolePieceId))
                player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera)?.SetOrthographicSize(
                    SpaceGameplaySettings.Current.DrivingCameraSize);
        }

        #endregion
    }
}
