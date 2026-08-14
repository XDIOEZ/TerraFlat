using System;
using MemoryPack;
using UnityEngine;

/// <summary>
/// 四类攻击数值。总战斗力只用于评价，实际结算会逐类减去对应防御后再求和。
/// </summary>
[Serializable]
[MemoryPackable]
public partial class CombatDamage
{
    [Min(0f)] public float Cutting;
    [Min(0f)] public float Piercing;
    [Min(0f)] public float Chopping;
    [Min(0f)] public float Blunt;

    [MemoryPackIgnore]
    public float TotalCombatPower => Cutting + Piercing + Chopping + Blunt;

    [MemoryPackConstructor]
    public CombatDamage()
    {
    }

    public CombatDamage(float cutting, float piercing, float chopping, float blunt)
    {
        Cutting = Mathf.Max(0f, cutting);
        Piercing = Mathf.Max(0f, piercing);
        Chopping = Mathf.Max(0f, chopping);
        Blunt = Mathf.Max(0f, blunt);
    }

    /// <summary>返回经过倍率缩放的新攻击数据，不修改原配置。</summary>
    public CombatDamage Scaled(float multiplier)
    {
        float safeMultiplier = Mathf.Max(0f, multiplier);
        return new CombatDamage(
            Cutting * safeMultiplier,
            Piercing * safeMultiplier,
            Chopping * safeMultiplier,
            Blunt * safeMultiplier);
    }

    /// <summary>限制运行时或反序列化产生的负数。</summary>
    public void ClampNonNegative()
    {
        Cutting = Mathf.Max(0f, Cutting);
        Piercing = Mathf.Max(0f, Piercing);
        Chopping = Mathf.Max(0f, Chopping);
        Blunt = Mathf.Max(0f, Blunt);
    }

    /// <summary>按四类独立减法计算最终伤害。</summary>
    public float CalculateAgainst(CombatDefense defense)
    {
        defense ??= CombatDefense.Zero;
        return Mathf.Max(0f, Cutting - defense.Cutting) +
               Mathf.Max(0f, Piercing - defense.Piercing) +
               Mathf.Max(0f, Chopping - defense.Chopping) +
               Mathf.Max(0f, Blunt - defense.Blunt);
    }
}

/// <summary>
/// 四类防御数值，分别只抵消同类型攻击，不提供最低伤害保底。
/// </summary>
[Serializable]
[MemoryPackable]
public partial class CombatDefense
{
    private static readonly CombatDefense Empty = new CombatDefense();

    [Min(0f)] public float Cutting;
    [Min(0f)] public float Piercing;
    [Min(0f)] public float Chopping;
    [Min(0f)] public float Blunt;

    [MemoryPackIgnore]
    public float TotalDefense => Cutting + Piercing + Chopping + Blunt;

    [MemoryPackIgnore]
    public static CombatDefense Zero => Empty;

    [MemoryPackConstructor]
    public CombatDefense()
    {
    }

    public CombatDefense(float cutting, float piercing, float chopping, float blunt)
    {
        Cutting = Mathf.Max(0f, cutting);
        Piercing = Mathf.Max(0f, piercing);
        Chopping = Mathf.Max(0f, chopping);
        Blunt = Mathf.Max(0f, blunt);
    }

    /// <summary>四类防御逐项相加。</summary>
    public void Add(CombatDefense value)
    {
        if (value == null)
            return;

        Cutting += Mathf.Max(0f, value.Cutting);
        Piercing += Mathf.Max(0f, value.Piercing);
        Chopping += Mathf.Max(0f, value.Chopping);
        Blunt += Mathf.Max(0f, value.Blunt);
    }

    /// <summary>四类防御逐项移除，并保证不会低于零。</summary>
    public void Remove(CombatDefense value)
    {
        if (value == null)
            return;

        Cutting = Mathf.Max(0f, Cutting - Mathf.Max(0f, value.Cutting));
        Piercing = Mathf.Max(0f, Piercing - Mathf.Max(0f, value.Piercing));
        Chopping = Mathf.Max(0f, Chopping - Mathf.Max(0f, value.Chopping));
        Blunt = Mathf.Max(0f, Blunt - Mathf.Max(0f, value.Blunt));
    }

    /// <summary>限制运行时或反序列化产生的负数。</summary>
    public void ClampNonNegative()
    {
        Cutting = Mathf.Max(0f, Cutting);
        Piercing = Mathf.Max(0f, Piercing);
        Chopping = Mathf.Max(0f, Chopping);
        Blunt = Mathf.Max(0f, Blunt);
    }
}

/// <summary>旧伤害标签等级数据，只用于读取历史 Prefab/JSON 并迁移，不再参与战斗结算。</summary>
[Serializable]
public struct DamageType
{
    public DamageTag Tag;
    public int Level;

    public DamageType(DamageTag tag, int level = 1)
    {
        Tag = tag;
        Level = level;
    }
}
