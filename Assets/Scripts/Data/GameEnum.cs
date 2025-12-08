/// <summary>
/// 特效叠加模式
/// </summary>
public enum EffectStackMode
{
    /// <summary>
    /// 可叠加 - 同一特效可以多次叠加
    /// </summary>
    Stackable,
    /// <summary>
    /// 不可叠加 - 同一特效只能存在一个
    /// </summary>
    NonStackable
}