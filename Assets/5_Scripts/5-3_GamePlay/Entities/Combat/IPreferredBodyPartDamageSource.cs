/// <summary>
/// 自然坠落或 MOD 伤害可声明部位偏好；存在该部位时定向命中，否则回退正常受击规则。
/// 偏好不绕过阵营、无敌窗口、护甲、部位倍率或整体生命结算。
/// </summary>
public interface IPreferredBodyPartDamageSource
{
    BodyPartType PreferredBodyPart { get; }
}
