using System;
using FlatWorld.Combat;
using Unity.Mathematics;
using UnityEngine;

public partial class Mod_DamageReceiver
{
    #region 环境结构损伤与压力爆炸
    public float ApplyStructuralDamage(float damage)
    {
        if (!CanMutateBodyState() || _resolvingDamage || _deathHandled || Hp <= 0f) return 0f;
        if (!float.IsFinite(damage) || damage < 0f) throw new ArgumentOutOfRangeException(nameof(damage));
        // 罐壁自身超压直接扣真实生命，库存与世界载体使用同一数值。
        return ResolveDamage(damage, null, 0f);
    }
    public float HurtPressureExplosion(ulong eventId, Vector2 logicalOrigin, float physicalDamage)
    {
        if (!CanMutateBodyState() || _resolvingDamage || _deathHandled || Hp <= 0f || item == null) return 0f;
        if (!float.IsFinite(physicalDamage) || physicalDamage < 0f) throw new ArgumentOutOfRangeException(nameof(physicalDamage));
        CombatIdentity target = GameplayCombatBridge.Identity(item);
        var source = new CombatIdentity { Backend = CombatBackend.Environment, Value = eventId,
            World = target.World, Dimension = target.Dimension };
        var context = new CombatDamageContext
        {
            Attack = new CombatAttackKey { Source = source, Sequence = (uint)eventId, Pulse = 1 },
            Credit = source, Damage = new float4(0f, 0f, 0f, physicalDamage),
            Origin = logicalOrigin, HitPoint = (Vector2)WorldLocalPresentation.ToLogical(item.transform.position),
            Clock = new CombatClock { Tick = (ulong)Time.frameCount, Time = Time.timeAsDouble, DeltaTime = Time.deltaTime },
            BuildingMultiplier = 1f, ResourceToolEfficiency = 1f
        };
        return HurtContext(context, 1f, ignoreInterval: true);
    }
    #endregion
}
