using System;
using System.Collections;
using FlatWorld.Networking;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 玩家太空传送事务

        // 传送只接收宇宙运动状态，目标选择规则交给调用方。
        public bool TryTeleportPlayerToSpace(Player player, OrbitState motion, Action<bool> completed = null)
        {
            if (State == null || Universe == null || transitionRunning || GameNetwork.IsOnline ||
                !GameNetwork.HasStateAuthority || GameManager.Instance?.IsGameplayReady != true ||
                DimensionManager.Instance.IsTransitioning || player == null || !player.IsLocalProfile ||
                player != ItemMgr.Instance?.User_Player || player.Data == null ||
                string.IsNullOrWhiteSpace(player.ProfileName) || motion == null ||
                !motion.PositionMeters.IsFinite || !motion.VelocityMetersPerSecond.IsFinite ||
                !SpaceVector2.Finite(motion.RadiusMeters) || motion.RadiusMeters < 0d)
                return false;

            foreach (BodyState body in Universe.State.Bodies)
                if ((motion.PositionMeters - body.PositionMeters).Magnitude <= body.RadiusMeters + motion.RadiusMeters)
                    return false;

            var targetMotion = new OrbitState
            {
                PositionMeters = motion.PositionMeters,
                VelocityMetersPerSecond = motion.VelocityMetersPerSecond,
                RadiusMeters = motion.RadiusMeters,
                ReferenceBodyId = motion.ReferenceBodyId
            };
            Universe.UpdateReference(targetMotion);
            StartCoroutine(TeleportPlayerToSpace(player, targetMotion, completed));
            return true;
        }

        private IEnumerator TeleportPlayerToSpace(Player player, OrbitState motion, Action<bool> completed)
        {
            SpaceSessionState sessionState = State;
            SpacePassengerState previous = GetPassenger(player);
            int index = sessionState.Passengers.IndexOf(previous);
            var target = new SpacePassengerState
            {
                ProfileId = previous.ProfileId,
                IsInSpace = true,
                FreeMotion = motion,
                FreeHeadingRadians = player.transform.eulerAngles.z * Math.PI / 180d,
                SavedCameraSize = previous.SavedCameraSize
            };
            bool hadImpulse = passengerImpulses.Remove(previous.ProfileId, out SpaceVector2 impulse);
            bool succeeded = false;
            bool migrationStarted = false;
            transitionRunning = true;
            try
            {
                if (!string.IsNullOrWhiteSpace(previous.ConsolePieceId))
                {
                    ClearControl(previous.ConsolePieceId);
                    player.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera)?.SetOrthographicSize(
                        (float)Math.Max(1d, previous.SavedCameraSize));
                }
                sessionState.Passengers[index] = target;
                SetTerrainSuppression(player, false);
                if (!IsSpaceView)
                {
                    ReleaseViews();
                    migrationStarted = DimensionManager.Instance.TryBeginPlanetLanding(player,
                        WorldAddress.FromWorldKey("SpaceScene"), Vector2.zero);
                    if (!migrationStarted) yield break;
                    while (DimensionManager.Instance.IsTransitioning) yield return null;
                }

                Player currentPlayer = ItemMgr.Instance?.User_Player;
                if (State != sessionState || !IsSpaceView || currentPlayer == null ||
                    currentPlayer.ProfileName != target.ProfileId || GameManager.Instance?.IsInGameWorld != true ||
                    GameManager.Instance.IsWorldEntryInProgress)
                    yield break;

                RestorePassengerPose(currentPlayer);
                currentPlayer.Data.transform.position = currentPlayer.transform.position;
                currentPlayer.Data.transform.rotation = currentPlayer.transform.rotation;
                Mod_Mover mover = currentPlayer.itemMods.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
                if (mover?.rb != null) { mover.rb.velocity = Vector2.zero; mover.rb.angularVelocity = 0f; }
                ItemMgr.Instance.NotifyRuntimeItemMoved(currentPlayer);
                succeeded = true;
            }
            finally
            {
                try
                {
                    if (!succeeded && State == sessionState)
                    {
                        sessionState.Passengers[index] = previous;
                        if (hadImpulse) passengerImpulses[previous.ProfileId] = impulse;
                        Capture(owner);
                        SaveDataMgr saveManager = SaveDataMgr.Instance;
                        if (migrationStarted && ReferenceEquals(saveManager?.SaveData, owner))
                            saveManager.Save_And_WriteToDisk();
                        Player restored = ItemMgr.Instance?.User_Player;
                        if (restored != null && restored.ProfileName == previous.ProfileId)
                        {
                            RestorePassengerPose(restored);
                            SetTerrainSuppression(restored, previous.Supported);
                            if (!string.IsNullOrWhiteSpace(previous.ConsolePieceId))
                                restored.itemMods.GetMod_ByID<Mod_Cam>(ModText.Camera)?.SetOrthographicSize(
                                    SpaceGameplaySettings.Current.DrivingCameraSize);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
                finally
                {
                    transitionRunning = false;
                    completed?.Invoke(succeeded);
                }
            }
        }

        #endregion
    }
}
