using Mirror;
using UnityEngine;

namespace FlatWorld.Networking.Gameplay
{
    /// <summary>只复制盾牌动作表现；防御租约和命中始终在服务器产生。</summary>
    public struct NetworkPlayerShieldState
    {
        public ShieldActionKind Kind;
        public float Elapsed, Duration, TimeScale;
        public double SampleTime;
        public string OffhandItemId;
    }

    public sealed partial class NetworkWorldPlayer
    {
        #region 盾牌动作与副手表现同步
        [SyncVar] private NetworkPlayerShieldState authoritativeShield;
        private Mod_ShieldCombat coreShield;
        private float nextShieldPublishTime;
        private uint shieldRealm;

        private void UpdateNetworkShield()
        {
            Mod_ShieldCombat current = corePlayer?.itemMods.GetMod_ByID<Mod_ShieldCombat>(Mod_ShieldCombat.ModuleId);
            if (current != coreShield)
            {
                ReleaseNetworkShield();
                coreShield = current;
                nextShieldPublishTime = 0f;
                shieldRealm = authoritativeRealmRevision;
            }
            if (coreShield == null) return;
            if (isServer)
            {
                using var worldScope = NetworkGameplayWorldScope.Use(this);
                if (authoritativeWorldTransferPending || shieldRealm != authoritativeRealmRevision)
                {
                    coreShield.CancelAction();
                    shieldRealm = authoritativeRealmRevision;
                }
                bool changed = authoritativeShield.Kind != coreShield.ActionKind ||
                    authoritativeShield.OffhandItemId != coreShield.EquippedShieldId ||
                    authoritativeShield.TimeScale != Time.timeScale;
                if (!changed && (coreShield.ActionKind == ShieldActionKind.Idle || Time.unscaledTime < nextShieldPublishTime)) return;
                nextShieldPublishTime = Time.unscaledTime + networkSendInterval;
                authoritativeShield = new NetworkPlayerShieldState
                {
                    Kind = coreShield.ActionKind, Elapsed = coreShield.ActionElapsed,
                    Duration = coreShield.ActionDuration, TimeScale = Time.timeScale,
                    SampleTime = NetworkTime.time, OffhandItemId = coreShield.EquippedShieldId
                };
                return;
            }
            float elapsed = authoritativeShield.Elapsed + (float)System.Math.Max(0d,
                NetworkTime.time - authoritativeShield.SampleTime) * Mathf.Max(0f, authoritativeShield.TimeScale);
            coreShield.ApplyNetworkPresentation(authoritativeShield.Kind, elapsed,
                authoritativeShield.Duration, authoritativeShield.OffhandItemId);
        }

        private void ReleaseNetworkShield()
        {
            if (coreShield != null)
            {
                coreShield.CancelAction();
                coreShield.ClearPresentation();
            }
            coreShield = null;
        }
        #endregion
    }
}
