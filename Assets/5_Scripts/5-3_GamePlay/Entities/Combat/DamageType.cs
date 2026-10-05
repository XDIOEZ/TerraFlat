using System;
using MemoryPack;
using UnityEngine;
using Newtonsoft.Json;
using Sirenix.OdinInspector;

/// <summary>命中表现与刃伤资格的稳定标识，不再代表四套伤害或防御。</summary>
public enum CombatDamageKind
{
    None,
    Cutting,
    Piercing,
    Chopping,
    Blunt
}

/// <summary>
/// 单一物理攻击力；四个旧存档槽仅保留序列化兼容和命中表现标签。
/// </summary>
[Serializable]
[MemoryPackable]
public partial class CombatDamage
{
    [HideInInspector] public float Cutting;
    [HideInInspector] public float Piercing;
    [HideInInspector] public float Chopping;
    [HideInInspector] public float Blunt;

    #region 物理攻击配置与旧存档兼容
    [MemoryPackIgnore, JsonProperty, ShowInInspector, LabelText("物理攻击力"), MinValue(0f)]
    public float Physical
    {
        get => Mathf.Max(0f, Cutting) + Mathf.Max(0f, Piercing) + Mathf.Max(0f, Chopping) + Mathf.Max(0f, Blunt);
        set => SetPhysical(value, DominantKind);
    }

    [MemoryPackIgnore, JsonProperty, ShowInInspector, LabelText("命中表现")]
    public CombatDamageKind ImpactKind
    {
        get => DominantKind;
        set => SetPhysical(Physical, value);
    }

    private void SetPhysical(float value, CombatDamageKind kind)
    {
        Cutting = Piercing = Chopping = Blunt = 0f;
        value = Mathf.Max(0f, value);
        switch (kind)
        {
            case CombatDamageKind.Cutting: Cutting = value; break;
            case CombatDamageKind.Piercing: Piercing = value; break;
            case CombatDamageKind.Chopping: Chopping = value; break;
            default: Blunt = value; break;
        }
    }

    public bool ShouldSerializeCutting() => false;
    public bool ShouldSerializePiercing() => false;
    public bool ShouldSerializeChopping() => false;
    public bool ShouldSerializeBlunt() => false;
    #endregion

    [MemoryPackIgnore, JsonIgnore]
    public float TotalCombatPower => Physical;

    #region 表现分类

    /// <summary>选择攻击数值占比最大的类型；并列依次优先切割、穿刺、劈砍、钝击。</summary>
    [MemoryPackIgnore, JsonIgnore]
    public CombatDamageKind DominantKind
    {
        get
        {
            float highest = 0f;
            CombatDamageKind kind = CombatDamageKind.None;
            if (Cutting > highest)
            {
                highest = Cutting;
                kind = CombatDamageKind.Cutting;
            }
            if (Piercing > highest)
            {
                highest = Piercing;
                kind = CombatDamageKind.Piercing;
            }
            if (Chopping > highest)
            {
                highest = Chopping;
                kind = CombatDamageKind.Chopping;
            }
            if (Blunt > highest)
                kind = CombatDamageKind.Blunt;

            return kind;
        }
    }

    #endregion

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

    /// <summary>物理攻击只减一次物理防御。</summary>
    public float CalculateAgainst(CombatDefense defense)
    {
        return ResolveAgainst(defense).TotalCombatPower;
    }

    /// <summary>防御后保持命中表现比例，分槽不再影响破防资格。</summary>
    public CombatDamage ResolveAgainst(CombatDefense defense)
    {
        defense ??= CombatDefense.Zero;
        float attack = Physical;
        return attack > 0f ? Scaled(Mathf.Max(0f, attack - defense.Physical) / attack) : new CombatDamage();
    }
}
/// <summary>
/// 单一物理防御；旧四槽取最大值兼容，避免等值旧护甲被累加四次。
/// </summary>
[Serializable]
[MemoryPackable]
public partial class CombatDefense
{
    private static readonly CombatDefense Empty = new CombatDefense();

    [HideInInspector] public float Cutting;
    [HideInInspector] public float Piercing;
    [HideInInspector] public float Chopping;
    [HideInInspector] public float Blunt;

    #region 物理防御配置与旧存档兼容
    [MemoryPackIgnore, JsonProperty, ShowInInspector, LabelText("物理防御"), MinValue(0f)]
    public float Physical
    {
        get => Mathf.Max(0f, Mathf.Max(Mathf.Max(Cutting, Piercing), Mathf.Max(Chopping, Blunt)));
        set { Cutting = Piercing = Chopping = 0f; Blunt = Mathf.Max(0f, value); }
    }

    public bool ShouldSerializeCutting() => false;
    public bool ShouldSerializePiercing() => false;
    public bool ShouldSerializeChopping() => false;
    public bool ShouldSerializeBlunt() => false;
    #endregion

    [MemoryPackIgnore, JsonIgnore]
    public float TotalDefense => Physical;

    [MemoryPackIgnore, JsonIgnore]
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

    /// <summary>不同护甲来源的物理防御相加。</summary>
    public void Add(CombatDefense value)
    {
        if (value == null)
            return;

        Physical += value.Physical;
    }

    /// <summary>撤销一个护甲来源，物理防御不能低于零。</summary>
    public void Remove(CombatDefense value)
    {
        if (value == null)
            return;

        Physical -= value.Physical;
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
