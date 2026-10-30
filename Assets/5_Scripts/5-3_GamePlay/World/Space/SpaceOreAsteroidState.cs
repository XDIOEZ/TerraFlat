using System;
using Newtonsoft.Json;

namespace FlatWorld.Spaceflight
{
    [Serializable]
    public sealed class SpaceOreAsteroidState
    {
        [Newtonsoft.Json.JsonExtensionData] public System.Collections.Generic.IDictionary<string, Newtonsoft.Json.Linq.JToken> UnknownOptionalFields;
        #region 矿石小行星的持久状态
        public string AsteroidId, CenterBodyId, CompositionId;
        public SpaceSectorId Sector;
        public SpaceVector2 OffsetMeters;
        public double RadiusMeters, MaximumHitPoints, HitPoints;
        public int OreAmount, StoneAmount;
        public bool Depleted;
        [JsonIgnore] public bool IsAlive => !Depleted && HitPoints > 0d;

        // 致命扣血必须先备齐真实掉落，耗尽标记随存档保留且不能重复产矿。
        public bool TryApplyResolvedDamage(double damage, bool destructionDropsPrepared, out double applied)
        {
            applied = 0d;
            if (!IsAlive || !double.IsFinite(damage) || damage <= 0d) return false;
            double remaining = Math.Max(0d, HitPoints - damage);
            if (remaining <= 0d && !destructionDropsPrepared) return false;
            applied = HitPoints - remaining;
            HitPoints = remaining;
            if (remaining <= 0d) Depleted = true;
            return true;
        }
        #endregion
    }
}
