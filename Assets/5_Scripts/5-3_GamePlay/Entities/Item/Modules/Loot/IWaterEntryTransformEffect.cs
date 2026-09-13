/// <summary>物品在水体中发生真实转换前可播放的一次性表现。</summary>
public interface IWaterEntryTransformEffect
{
    /// <summary>在源物品回收前播放入水转换表现；表现对象必须能脱离源物品独立存活。</summary>
    void PlayWaterEntryTransformEffect();
}
