/// <summary>受击前的可组合规则；战斗接收器只负责应用规则，不依赖采矿等具体玩法。</summary>
public interface IIncomingDamageRule
{
    /// <summary>返回本次伤害倍率；零表示拒绝，不消耗受击冷却。</summary>
    float GetDamageMultiplier(IDamageSender sender);
}

/// <summary>纯上下文受击能力；未实现此契约的旧规则不能被数据后端静默绕过。</summary>
public interface IIncomingDamageContextRule
{
    /// <summary>返回该上下文的倍率，零代表此能力拒绝攻击且不消耗受伤间隔。</summary>
    float GetDamageMultiplier(in FlatWorld.Combat.CombatDamageContext context);
}
