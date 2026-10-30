using System;
using FlatWorld.Combat;
using Unity.Mathematics;
using UnityEngine;

public partial class Mod_DamageReceiver
{
    #region 环境结构损伤与范围命中
    public float ApplyStructuralDamage(float damage)
    {
        if (IsTemporarilyInvulnerable || !CanMutateBodyState() || _resolvingDamage || _deathHandled || Hp <= 0f) return 0f;
        if (!float.IsFinite(damage) || damage < 0f) throw new ArgumentOutOfRangeException(nameof(damage));
        // 罐壁自身超压直接扣真实生命，库存与世界载体使用同一数值。
        return ResolveDamage(damage, null);
    }
    public float HurtPressureExplosion(ulong eventId, Vector2 logicalOrigin, float physicalDamage)
        => HurtEnvironmentalDamage(eventId, logicalOrigin, physicalDamage, "explosion");

    /// <summary>中立环境命中保留防御、部位生命和死亡链，不占用普通攻击冷却。</summary>
    public float HurtEnvironmentalDamage(ulong eventId, Vector2 logicalOrigin, float physicalDamage, string deathCauseId = "unknown")
    {
        if (IsTemporarilyInvulnerable) return -1f;
        if (!CanMutateBodyState() || _resolvingDamage || _deathHandled || Hp <= 0f || item == null) return 0f;
        if (eventId == 0) throw new ArgumentOutOfRangeException(nameof(eventId));
        if (!float.IsFinite(physicalDamage) || physicalDamage < 0f) throw new ArgumentOutOfRangeException(nameof(physicalDamage));
        CombatIdentity target = GameplayCombatBridge.Identity(item);
        var source = new CombatIdentity { Backend = CombatBackend.Environment, Value = eventId,
            World = target.World, Dimension = target.Dimension };
        var context = new CombatDamageContext
        {
            Attack = new CombatAttackKey { Source = source, Sequence = (uint)eventId, Pulse = 1 },
            Credit = source, Damage = new float4(0f, 0f, 0f, physicalDamage),
            DeathCauseId = GameplayCombatBridge.NarrativeCauseId(deathCauseId),
            Origin = logicalOrigin, HitPoint = (Vector2)WorldLocalPresentation.ToLogical(item.transform.position),
            Clock = new CombatClock { Tick = GameplayFixedStepSimulation.TickIndex, Time = Time.timeAsDouble, DeltaTime = Time.deltaTime },
            BuildingMultiplier = 1f, ResourceToolEfficiency = 1f
        };
        return HurtContext(context, 1f, ignoreInterval: true);
    }
    #endregion
}
