using System;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Character body regions managed by <see cref="DamageReceiver"/>.
/// </summary>
public enum BodyPartType
{
    Head,
    Chest,
    Abdomen,
    LeftHand,
    RightHand,
    Pelvis,
    LeftLeg,
    RightLeg
}

[Serializable]
public sealed class BodyPartHealth
{
    public BodyPartType Part;
    // 保留公开字段名以兼容调用方；语义仅为部位耐久，不参与整体生命求和。
    public float Hp;
    public float MaxHp;
    public CombatDefense DefenseValues = new();
    public float DamageMultiplier = 1f;
    public System.Collections.Generic.List<string> DepletionBuffIds = new();

    [JsonIgnore] public float Durability { get => Hp; set => Hp = value; }
    [JsonIgnore] public float MaxDurability { get => MaxHp; set => MaxHp = value; }

    [Range(0f, 1f)]
    [Tooltip("Relative visible area. Runtime hit weight is AreaRatio x InjuryProbability.")]
    public float AreaRatio = 0.125f;

    [Range(0f, 1f)]
    [Tooltip("Additional injury multiplier. Set to 0 to prevent random hits on this part.")]
    public float InjuryProbability = 1f;

    [JsonIgnore]
    public float Health01 => MaxHp <= 0f ? 0f : Mathf.Clamp01(Hp / MaxHp);
}

/// <summary>
/// One body part's share of a damage event.
/// </summary>
[Serializable]
public sealed class BodyPartDamageInfo
{
    public BodyPartType Part;
    public float DamageValue;
    public float DamageShare;
    public float HpBefore;
    public float HpAfter;
    public float MaxHp;
    public bool IsDepleted;
}

/// <summary>
/// Raised whenever a part changes, including healing and network state application.
/// External systems should subscribe to this event and query the current ratios.
/// </summary>
public sealed class BodyPartHealthChangeInfo
{
    public DamageReceiver Receiver;
    public BodyPartType Part;
    public float HpBefore;
    public float HpAfter;
    public float MaxHp;
    public float Delta;
    public float Health01;
    public bool IsDepleted;
}
