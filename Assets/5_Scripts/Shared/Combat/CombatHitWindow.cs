using System;
using System.Collections.Generic;

namespace FlatWorld.Combat
{
    /// <summary>攻击窗口只登记逻辑身份，不持有 Collider、GameObject 或生命模块。</summary>
    public sealed class CombatHitWindow
    {
        #region 逻辑命中预约
        private readonly HashSet<CombatIdentity> targets = new();
        public uint Sequence { get; private set; }
        public int Count => targets.Count;

        public int Remaining(int maximumTargets) => Math.Max(0, Math.Max(1, maximumTargets) - targets.Count);
        public bool Contains(CombatIdentity target) => target.IsValid && targets.Contains(target);

        public bool TryReserve(CombatIdentity target, int maximumTargets)
            => target.IsValid && Remaining(maximumTargets) > 0 && targets.Add(target);

        public void Begin()
        {
            Clear();
            Sequence = unchecked(Sequence + 1);
            if (Sequence == 0) Sequence = 1;
        }

        public void Clear() => targets.Clear();
        public void Reset() { Clear(); Sequence = 0; }
        #endregion
    }
}
