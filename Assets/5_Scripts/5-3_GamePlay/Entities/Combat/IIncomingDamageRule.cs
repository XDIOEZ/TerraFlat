/// <summary>受击前的可组合规则；战斗接收器只负责应用规则，不依赖采矿等具体玩法。</summary>
public interface IIncomingDamageRule
{
    /// <summary>返回本次伤害倍率；零表示拒绝，不消耗受击冷却。</summary>
    float GetDamageMultiplier(IDamageSender sender);
}
