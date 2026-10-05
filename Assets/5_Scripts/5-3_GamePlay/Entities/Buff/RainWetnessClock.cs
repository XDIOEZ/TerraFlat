using System;

/// <summary>每个生物独立累计按雨量折算的叠层进度，不保存天气全局时间或离线时间债务。</summary>
public struct RainWetnessClock
{
    #region 淋雨累积

    private double layerProgress;

    public void Reset() => layerProgress = 0d;

    public int Advance(float deltaTime, float intensity, float intervalSeconds, float referenceIntensity, int maxStacks)
    {
        if (!IsPositiveFinite(deltaTime))
            return 0;
        if (!IsPositiveFinite(intensity) || !IsPositiveFinite(intervalSeconds) ||
            !IsPositiveFinite(referenceIntensity) || maxStacks <= 0)
        {
            Reset();
            return 0;
        }

        layerProgress += (double)deltaTime * Math.Min(1f, intensity) / intervalSeconds / referenceIntensity;
        double completed = Math.Floor(layerProgress + 0.0000001d);
        if (completed < 1d)
            return 0;

        // 即使已达上限也消费完整周期，不能把多余雨量攒成下一次瞬间满层。
        layerProgress = Math.Max(0d, layerProgress - completed);
        return (int)Math.Min(maxStacks, completed);
    }

    private static bool IsPositiveFinite(float value) =>
        value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

    #endregion
}
