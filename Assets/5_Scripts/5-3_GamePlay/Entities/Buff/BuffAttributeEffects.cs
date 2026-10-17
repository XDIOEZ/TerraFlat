using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

/// <summary>按 Buff 实例登记临时属性，叠层时替换本来源，结束时只撤销本来源。</summary>
public static class BuffAttributeEffects
{
    #region 固定攻击力

    private static readonly Dictionary<BuffInstance, float> AttackSources = new();
    private static readonly Dictionary<Item, float> ReceiverAttackModifiers = new();

    public static void ApplyAttackFlatModifier(BuffEffectDefinition effect, BuffInstance runtime)
    {
        if (effect.Phase == BuffEffectPhase.Stop)
            AttackSources.Remove(runtime);
        else
            AttackSources[runtime] = effect.Value * (effect.ScaleWithStacks ? runtime.StackCount : 1);
        RefreshAttackModifier(runtime.Receiver);
    }

    private static void RefreshAttackModifier(Item receiver)
    {
        if (ReferenceEquals(receiver, null)) return;
        float total = 0f;
        foreach (var source in AttackSources)
            if (ReferenceEquals(source.Key.Receiver, receiver)) total += source.Value;
        if (total == 0f) ReceiverAttackModifiers.Remove(receiver);
        else ReceiverAttackModifiers[receiver] = total;
    }

    /// <summary>只修正本次攻击的总值并保留伤害比例，不改武器基础值，最低攻击力为零。</summary>
    public static float4 ModifyAttackDamage(Item owner, float4 damage)
    {
        if (ReferenceEquals(owner, null) || !ReceiverAttackModifiers.TryGetValue(owner, out float modifier))
            return damage;
        float4 positive = math.max(float4.zero, damage);
        float total = math.csum(positive);
        return total > 0f ? positive * (math.max(0f, total + modifier) / total) : float4.zero;
    }

    #endregion

    #region 移动速度

    private readonly struct SpeedSource
    {
        public readonly GameValue_float Speed;
        public readonly float Multiplier;
        public SpeedSource(GameValue_float speed, float multiplier)
        {
            Speed = speed;
            Multiplier = multiplier;
        }
    }

    private static readonly Dictionary<BuffInstance, SpeedSource> SpeedSources = new();

    public static void ApplyMoveSpeedReduction(BuffEffectDefinition effect, BuffInstance runtime)
    {
        if (SpeedSources.Remove(runtime, out SpeedSource previous))
            previous.Speed.MultiplicativeModifier /= previous.Multiplier;
        if (effect.Phase == BuffEffectPhase.Stop) return;
        GameValue_float speed = runtime.Receiver?.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover)?.Speed;
        if (speed == null) return;
        float multiplier = 1f - effect.Value * (effect.ScaleWithStacks ? runtime.StackCount : 1);
        SpeedSources[runtime] = new SpeedSource(speed, multiplier);
        speed.MultiplicativeModifier *= multiplier;
    }

    #endregion
}
