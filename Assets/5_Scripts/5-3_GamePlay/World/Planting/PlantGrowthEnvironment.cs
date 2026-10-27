using System;
using FlatWorld.AIECS;
using UnityEngine;

/// <summary>植物共用环境查询与成长数值，具体植物只保留阶段和收获能力。</summary>
public static class PlantGrowthEnvironment
{
    #region 世界时钟与成长倍率

    public static bool TryReadWorldClock(out TimeData clock, out double now)
    {
        clock = null;
        now = 0d;
        if (DayTimeSystem.Instance == null || !DayTimeSystem.Instance.TryGetActiveTimeData(out clock))
            return false;
        now = (double)clock.TotalDays * clock.DayLength + clock.CurrentTime;
        return true;
    }

    public static float CalculateGrowthDelta(float baseSpeed, float farmlandMultiplier,
        float weatherMultiplier, float growthMultiplier, float deltaTime) =>
        Mathf.Max(0f, baseSpeed) * Mathf.Max(0f, farmlandMultiplier) *
        Mathf.Max(0f, weatherMultiplier) * Mathf.Max(0f, growthMultiplier) * Mathf.Max(0f, deltaTime);

    public static float CalculateFarmlandGrowthMultiplier(TileData_Farmland soil,
        float minimumWaterMultiplier = 0.5f, float minimumFertilityMultiplier = 0.5f)
    {
        if (soil == null) throw new ArgumentNullException(nameof(soil));
        soil.NormalizeValues();
        return EntityPlantRules.CalculateSoilGrowthMultiplier(soil.waterValue, soil.maxWater,
            soil.Fertility, soil.maxFertility, minimumWaterMultiplier, minimumFertilityMultiplier);
    }

    public static float ResolveWeatherGrowthMultiplier(Item item, float rainGrowthBonus)
    {
        WeatherMgr weather = WeatherMgr.ExistingInstance;
        if (weather == null || item == null || !weather.TryGetWeatherAt(item.transform.position, out var localWeather))
            return 1f;
        // 雨水补给交给地表系统，植物只结算当前天气对成长的影响。
        float rainIntensity = weather.TryGetPrecipitationAt(item.transform.position, out float rain, out _) ? rain : 0f;
        return EntityWeatherInput.ResolveGrowthMultiplier(rainIntensity, localWeather.CloudCoverage, localWeather.WindStrength) *
            (1f + Mathf.Clamp01(rainIntensity) * rainGrowthBonus);
    }

    #endregion

    #region 地块光照与每日耗水

    public static bool HasSufficientLight(Vector2Int cell, float minimum)
    {
        LightLayerMgr lights = LightLayerMgr.Instance;
        return lights != null && lights.TryGetLightLevel(new Vector2(cell.x + 0.5f, cell.y + 0.5f), out float light) &&
            EntityPlantRules.HasSufficientLight(light, minimum);
    }

    /// <summary>生长暂停和成熟都不停止耗水，回拨时只重建时钟而不产生欠账。</summary>
    public static void AdvanceWater(Vector2Int cell, float multiplier, bool naturalSoil,
        ref bool initialized, ref double lastTime, double? untilTime = null)
    {
        if (!FlatWorld.Networking.GameNetwork.HasStateAuthority || DayTimeSystem.Instance == null ||
            !DayTimeSystem.Instance.TryGetActiveTimeData(out TimeData clock)) return;
        double now = untilTime ?? clock.GetTotalGameTimeSeconds();
        if (untilTime.HasValue && initialized && now < lastTime) return;
        float seconds = initialized ? (float)Math.Max(0d, now - lastTime) : 0f;
        initialized = true;
        lastTime = now;
        if (seconds <= 0f || !FarmlandSystem.TryReadSoil(cell, out TileData_Farmland soil, out var sample, naturalSoil) ||
            !FarmlandSystem.IsFarmland(sample.Cell)) return;
        float used = EntityPlantRules.CalculateWaterConsumption(soil.waterValue, soil.waterConsumptionRemainder,
            EntityPlantRules.WaterRate(clock.DayLength, multiplier), seconds);
        if (used <= 0f) return;
        soil.AccumulateWaterConsumption(used);
        FarmlandSystem.CommitSoil(soil, naturalSoil);
    }

    #endregion
}
