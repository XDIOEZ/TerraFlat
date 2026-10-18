using System;
using FlatWorld.AIECS;
using UnityEngine;

/// <summary>植物共用地块光照查询与独立世界时钟耗水，具体植物只配置门槛和倍率。</summary>
public static class PlantGrowthEnvironment
{
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
        if (seconds <= 0f || !FarmlandSystem.TryReadSoil(cell, out TileData_Farmland soil, out _, naturalSoil)) return;
        float used = EntityPlantRules.CalculateWaterConsumption(soil.waterValue, soil.waterConsumptionRemainder,
            EntityPlantRules.WaterRate(clock.DayLength, multiplier), seconds);
        if (used <= 0f) return;
        soil.AccumulateWaterConsumption(used);
        FarmlandSystem.CommitSoil(soil, naturalSoil);
    }

    #endregion
}
