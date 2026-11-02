using System;
using MemoryPack;
using UnityEngine;

/// <summary>仅描述物理命中的表现与出血资格，不参与防御分类。</summary>
public enum CombatDamageKind
{
    None,
    Cutting,
    Piercing,
    Chopping,
    Blunt
}

/// <summary>物理、火焰、辐射分别减对应防御；真实伤害不受防御影响。</summary>
[Serializable]
[MemoryPackable]
public partial class CombatDamage
{
    [Min(0f)] public float Physical;
    [Min(0f)] public float Fire;
    [Min(0f)] public float Radiation;
    [Min(0f)] public float True;
    public CombatDamageKind ImpactKind = CombatDamageKind.Blunt;

    [MemoryPackIgnore, Newtonsoft.Json.JsonIgnore]
    public float TotalCombatPower => Mathf.Max(0f, Physical) + Mathf.Max(0f, Fire) +
                                     Mathf.Max(0f, Radiation) + Mathf.Max(0f, True);

    [MemoryPackIgnore, Newtonsoft.Json.JsonIgnore]
    public CombatDamageKind DominantKind => Physical > 0f
        ? ImpactKind == CombatDamageKind.None ? CombatDamageKind.Blunt : ImpactKind
        : CombatDamageKind.None;

    [MemoryPackConstructor]
    public CombatDamage() { }

    public CombatDamage(float physical, float fire, float radiation, float trueDamage, CombatDamageKind impactKind = CombatDamageKind.Blunt)
    {
        Physical = Mathf.Max(0f, physical);
        Fire = Mathf.Max(0f, fire);
        Radiation = Mathf.Max(0f, radiation);
        True = Mathf.Max(0f, trueDamage);
        ImpactKind = impactKind;
    }

    public CombatDamage Scaled(float multiplier)
    {
        float value = Mathf.Max(0f, multiplier);
        return new CombatDamage(Physical * value, Fire * value, Radiation * value, True * value, ImpactKind);
    }

    public void ClampNonNegative()
    {
        Physical = Mathf.Max(0f, Physical);
        Fire = Mathf.Max(0f, Fire);
        Radiation = Mathf.Max(0f, Radiation);
        True = Mathf.Max(0f, True);
    }

    public float CalculateAgainst(CombatDefense defense) => ResolveAgainst(defense).TotalCombatPower;

    public CombatDamage ResolveAgainst(CombatDefense defense)
    {
        defense ??= CombatDefense.Zero;
        return new CombatDamage(
            Mathf.Max(0f, Physical - defense.Physical),
            Mathf.Max(0f, Fire - defense.Fire),
            Mathf.Max(0f, Radiation - defense.Radiation),
            True, ImpactKind);
    }
}

/// <summary>防御与伤害类型一一对应，所有抗性使用固定数值减法。</summary>
[Serializable]
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class CombatDefense
{
    private static readonly CombatDefense Empty = new();

    [MemoryPackOrder(0), Min(0f)] public float Physical;
    [MemoryPackOrder(1), Min(0f)] public float Fire;
    [MemoryPackOrder(2), Min(0f)] public float Radiation;

    [MemoryPackIgnore, Newtonsoft.Json.JsonIgnore]
    public float TotalDefense => Physical + Fire + Radiation;

    [MemoryPackIgnore, Newtonsoft.Json.JsonIgnore]
    public static CombatDefense Zero => Empty;

    [MemoryPackConstructor]
    public CombatDefense() { }

    public CombatDefense(float physical, float fire, float radiation)
    {
        Physical = Mathf.Max(0f, physical);
        Fire = Mathf.Max(0f, fire);
        Radiation = Mathf.Max(0f, radiation);
    }

    public CombatDefense Copy() => new(Physical, Fire, Radiation);

    public void Add(CombatDefense value)
    {
        if (value == null) return;
        Physical += value.Physical;
        Fire += value.Fire;
        Radiation += value.Radiation;
    }

    public void Remove(CombatDefense value)
    {
        if (value == null) return;
        Physical = Mathf.Max(0f, Physical - value.Physical);
        Fire = Mathf.Max(0f, Fire - value.Fire);
        Radiation = Mathf.Max(0f, Radiation - value.Radiation);
    }

    public void ClampNonNegative()
    {
        Physical = Mathf.Max(0f, Physical);
        Fire = Mathf.Max(0f, Fire);
        Radiation = Mathf.Max(0f, Radiation);
    }
}
