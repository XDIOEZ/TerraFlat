using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    public sealed partial class NetworkWorldPlayer
    {
        #region 临时动作控制同步
        [SyncVar] private bool authoritativeForcedFollowing;
        [SyncVar] private float authoritativeActionSpeedMultiplier = 1f;
        private Mod_Mover.DrivenMotionLease networkFollowLease;
        private GameValue_float networkSlowedSpeed;
        private float networkAppliedSlow = 1f;

        private void UpdateNetworkActionConstraints()
        {
            if (corePlayer == null) return;
            if (isServer)
            {
                using var worldScope = NetworkGameplayWorldScope.Use(this);
                authoritativeActionSpeedMultiplier = ActorActionConstraints.GetSpeedMultiplier(corePlayer);
                authoritativeForcedFollowing = !IsInSpace && !authoritativeWorldTransferPending &&
                    ActorActionConstraints.TryGetForcedPosition(corePlayer, out _);
                if (authoritativeForcedFollowing && ActorActionConstraints.TryGetForcedPosition(corePlayer, out Vector3 position))
                {
                    ApplyMotionOnServer(position, Vector2.zero, authoritativeFacingLeft, authoritativeVisualState);
                    SyncCorePlayerPosition(WorldLocalPresentation.ProjectPosition(authoritativePosition), forceLocal: isOwned);
                }
            }
            if (!isOwned || isServer || coreMover == null) return;
            GameValue_float speed = coreMover.Speed;
            float multiplier = Mathf.Clamp(authoritativeActionSpeedMultiplier, .001f, 1f);
            if (!ReferenceEquals(speed, networkSlowedSpeed) || !Mathf.Approximately(multiplier, networkAppliedSlow))
            {
                if (networkSlowedSpeed != null) networkSlowedSpeed.MultiplicativeModifier /= networkAppliedSlow;
                networkSlowedSpeed = speed; networkAppliedSlow = multiplier;
                if (speed != null) speed.MultiplicativeModifier *= multiplier;
            }
            if (!authoritativeForcedFollowing)
            { networkFollowLease?.Dispose(); networkFollowLease = null; return; }
            if (networkFollowLease?.IsActive != true)
                coreMover.TryAcquireDrivenMotion(Vector2.zero, out networkFollowLease);
            SyncCorePlayerPosition(WorldLocalPresentation.ProjectPosition(authoritativePosition), forceLocal: true);
            transform.position = authoritativePosition;
        }
        private void ReleaseNetworkActionConstraints()
        {
            networkFollowLease?.Dispose(); networkFollowLease = null;
            if (networkSlowedSpeed != null) networkSlowedSpeed.MultiplicativeModifier /= networkAppliedSlow;
            networkSlowedSpeed = null; networkAppliedSlow = 1f;
        }
        #endregion
    }
}
