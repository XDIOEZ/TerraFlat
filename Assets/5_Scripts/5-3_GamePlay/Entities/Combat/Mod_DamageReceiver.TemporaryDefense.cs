using System;
using System.Collections.Generic;

public partial class Mod_DamageReceiver
{
    #region 来源持有的临时防御
    private readonly HashSet<TemporaryDefenseLease> temporaryDefenseLeases = new();
    private float temporaryPhysicalDefense;
    public float TemporaryPhysicalDefense => temporaryPhysicalDefense;

    /// <summary>临时防御只参与结算，不修改基础防御、部位护甲或存档。</summary>
    public IDisposable AcquireTemporaryDefense(float physicalDefense)
    {
        if (float.IsNaN(physicalDefense) || float.IsInfinity(physicalDefense) || physicalDefense < 0f)
            throw new ArgumentOutOfRangeException(nameof(physicalDefense));
        if (this == null || _deathHandled || Hp <= 0f || IsRuntimeTransitioning)
            throw new InvalidOperationException("生命模块当前不能接收临时防御。");
        var lease = new TemporaryDefenseLease(this, physicalDefense);
        temporaryDefenseLeases.Add(lease);
        temporaryPhysicalDefense += physicalDefense;
        return lease;
    }

    public CombatDefense GetEffectiveDefense() => new()
    { Physical = (Defense?.Physical ?? 0f) + temporaryPhysicalDefense };

    private void ClearTemporaryDefense()
    {
        foreach (TemporaryDefenseLease lease in temporaryDefenseLeases) lease.Invalidate();
        temporaryDefenseLeases.Clear();
        temporaryPhysicalDefense = 0f;
    }

    private void ClearTemporaryCombatEffects()
    {
        ClearTemporaryInvulnerability();
        ClearTemporaryDefense();
    }

    private sealed class TemporaryDefenseLease : IDisposable
    {
        private Mod_DamageReceiver owner;
        private readonly float amount;
        public TemporaryDefenseLease(Mod_DamageReceiver receiver, float value) { owner = receiver; amount = value; }
        public void Dispose()
        {
            Mod_DamageReceiver receiver = owner;
            owner = null;
            if (receiver != null && receiver.temporaryDefenseLeases.Remove(this))
                receiver.temporaryPhysicalDefense = Math.Max(0f, receiver.temporaryPhysicalDefense - amount);
        }
        public void Invalidate() => owner = null;
    }
    #endregion
}
