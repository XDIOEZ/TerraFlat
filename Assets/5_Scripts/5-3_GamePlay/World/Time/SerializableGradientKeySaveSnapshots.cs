using MemoryPack;
using UnityEngine;

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class GradientColorKeySaveSnapshot
{
    #region 颜色曲线稳定字段
    [MemoryPackOrder(0)] public float Time;
    [MemoryPackOrder(1)] public float Red;
    [MemoryPackOrder(2)] public float Green;
    [MemoryPackOrder(3)] public float Blue;
    [MemoryPackOrder(4)] public float Alpha;
    public static GradientColorKeySaveSnapshot Capture(GradientColorKey key) => new()
        { Time = key.time, Red = key.color.r, Green = key.color.g, Blue = key.color.b, Alpha = key.color.a };
    public GradientColorKey Restore() => new(new Color(Red, Green, Blue, Alpha), Time);
    #endregion
}

[MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial class GradientAlphaKeySaveSnapshot
{
    #region 透明曲线稳定字段
    [MemoryPackOrder(0)] public float Time;
    [MemoryPackOrder(1)] public float Alpha;
    public static GradientAlphaKeySaveSnapshot Capture(GradientAlphaKey key) => new() { Time = key.time, Alpha = key.alpha };
    public GradientAlphaKey Restore() => new(Alpha, Time);
    #endregion
}
