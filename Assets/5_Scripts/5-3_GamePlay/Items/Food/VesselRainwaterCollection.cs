using System;

/// <summary>开口容器按当地雨量累计整份水，时钟与液体存储、天气采样分别负责自己的规则。</summary>
public static class VesselRainwaterCollection
{
    #region 接雨份数与进度

    public const float DefaultIntervalSeconds = 60f;
    public const string PureWaterLiquidId = "core:pure_water";
    public const string DirtyWaterLiquidId = "core:dirty_water";
    private const float ReferenceRainIntensity = .5f;

    // 新接到的水按所在地块当前污染选种类，未知污染状态不生成水。
    public static string ResolveLiquidId(float contaminationLoad) =>
        !float.IsFinite(contaminationLoad) || contaminationLoad < 0f ? null :
        contaminationLoad > 0f ? DirtyWaterLiquidId : PureWaterLiquidId;

    // 中雨一分钟一份，小雨最慢两分钟，大雨最快三十秒，满桶不积攒额外水量。
    public static int Advance(ref double progress, float seconds, float intensity, float availableCapacity,
        float intervalSeconds = DefaultIntervalSeconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0f || !float.IsFinite(intensity) || intensity <= 0f ||
            !float.IsFinite(intervalSeconds) || intervalSeconds <= 0f)
            return 0;

        if (!float.IsFinite(availableCapacity) || availableCapacity < 1f)
        {
            progress = 0d;
            return 0;
        }

        double rate = Math.Clamp(intensity / ReferenceRainIntensity, .5f, 2f);
        progress += seconds * rate / intervalSeconds;
        double completed = Math.Floor(progress + .0000001d);
        double space = Math.Min(int.MaxValue, Math.Floor(availableCapacity));
        int collected = (int)Math.Min(space, completed);
        progress = completed >= space ? 0d : Math.Max(0d, progress - completed);
        return collected;
    }

    #endregion
}
