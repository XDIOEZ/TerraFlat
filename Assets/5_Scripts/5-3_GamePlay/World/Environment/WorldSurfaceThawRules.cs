using System;

/// <summary>地表冰雪共用连续温差积分，不在显示量化时丢失剩余量。</summary>
public static class WorldSurfaceThawRules
{
    #region 连续雪量推进
    public readonly struct SnowStep
    {
        public readonly bool Edited;
        public readonly float Depth, SeasonalBaseline, WeatherDepth, Melted;
        public SnowStep(bool edited, float depth, float seasonalBaseline, float weatherDepth, float melted)
        {
            Edited = edited; Depth = depth; SeasonalBaseline = seasonalBaseline;
            WeatherDepth = weatherDepth; Melted = melted;
        }
    }

    // 天然雪第一次受暖融时固化为可保存覆盖，后续冷暖变化不会重新铺回。
    public static SnowStep AdvanceSnow(float naturalDepth, bool edited, float depth, float seasonalBaseline,
        float seasonalDepth, float weatherDepth, float temperature, float freezingTemperature, float seconds,
        float precipitationSeconds, float accumulationPerSecond, float meltPerDegreeSecond)
    {
        if (!float.IsFinite(temperature) || !float.IsFinite(seconds) || seconds <= 0f)
            return new SnowStep(edited, depth, seasonalBaseline, weatherDepth, 0f);
        if (temperature <= freezingTemperature)
            return new SnowStep(edited, depth, seasonalBaseline,
                Math.Clamp(weatherDepth + Math.Max(0f, precipitationSeconds) * Math.Max(0f, accumulationPerSecond), 0f, 1f), 0f);
        float budget = (temperature - freezingTemperature) * Math.Max(0f, meltPerDegreeSecond) * seconds;
        if (!(budget > 0f)) return new SnowStep(edited, depth, seasonalBaseline, weatherDepth, 0f);
        float before = edited ? Math.Max(0f, depth + Math.Clamp(seasonalDepth + weatherDepth, 0f, 1f) - seasonalBaseline)
            : Math.Max(naturalDepth, Math.Clamp(seasonalDepth + weatherDepth, 0f, 1f));
        if (edited || naturalDepth > 0f || seasonalDepth > 0f)
            return new SnowStep(true, Math.Max(0f, before - budget), seasonalDepth, 0f, Math.Min(before, budget));
        float nextWeather = Math.Max(0f, weatherDepth - budget);
        return new SnowStep(false, depth, seasonalBaseline, nextWeather, Math.Max(0f, weatherDepth - nextWeather));
    }
    #endregion

    #region 冰层融化推进
    public static float AdvanceIceMeltProgress(float progress, float temperatureCelsius,
        float seconds, float meltPerDegreeSecond)
    {
        if (!float.IsFinite(progress) || !float.IsFinite(temperatureCelsius) ||
            !float.IsFinite(seconds) || seconds <= 0f || temperatureCelsius <= 0f ||
            !float.IsFinite(meltPerDegreeSecond) || meltPerDegreeSecond <= 0f)
            return Math.Clamp(float.IsFinite(progress) ? progress : 0f, 0f, 1f);
        return Math.Clamp(progress + temperatureCelsius * seconds * meltPerDegreeSecond, 0f, 1f);
    }
    #endregion
}
