
public interface IDamageSender
{
    /// <summary>本次攻击的四类基础伤害。</summary>
    public CombatDamage DamageValues { get; }

    public Item attacker { get; set; }
}

/// <summary>
/// 建筑伤害来源能力：倍率只应用在目标防御结算完成之后，不能用于提高破甲等级。
/// </summary>
public interface IBuildingDamageSource
{
    float BuildingDamageMultiplier { get; }
}

/// <summary>可选的攻击者受击减速参数，供武器或技能按自身特性提供效果。</summary>
public interface IHitSlowdownSource
{
    /// <summary>是否启用本次攻击的受击减速。</summary>
    bool HitSlowdownEnabled { get; }

    /// <summary>受击后的移动速度倍率。</summary>
    float HitSlowMultiplier { get; }

    /// <summary>受击减速持续时间（秒）。</summary>
    float HitSlowDuration { get; }
}
