
public interface IDamageSender
{
    /// <summary>本次攻击的四类基础伤害。</summary>
    public CombatDamage DamageValues { get; }

    public Item attacker { get; set; }
}
