using System;

/// <summary>只在飞行期间创建的临时请求，默认 10 点钝击、头部优先；不会给 ECS 掉落或库存添加伤害模块。</summary>
public sealed class FallingFruitDamage : IDamageSender, IPreferredBodyPartDamageSource
{
    #region 伤害契约
    public CombatDamage DamageValues { get; } // 此次坠落伤害。
    public Item attacker { get; set; } // 自然坠落为空，不借用树木阵营。
    public BodyPartType PreferredBodyPart => BodyPartType.Head;

    /// <summary>创建临时伤害请求。</summary>
    public FallingFruitDamage(float bluntDamage) => DamageValues = new CombatDamage { Blunt = bluntDamage };
    #endregion

    #region 一次性命中
    /// <summary>仅允许第一次有效接触，零伤害也有效；随机可注入，严格小于概率才转半果。</summary>
    public static bool TryHit(CanopyFruitRecord fruit, double now, Func<float> hurt, Func<double> random, double splitChance)
    {
        if (fruit.HitConsumed || now < fruit.FallAt || now >= fruit.LandAt) return false;
        fruit.HitConsumed = true;
        float result = hurt();
        if (result < 0) { fruit.HitConsumed = false; return false; }
        fruit.Split = random() < splitChance;
        return true;
    }
    #endregion
}
