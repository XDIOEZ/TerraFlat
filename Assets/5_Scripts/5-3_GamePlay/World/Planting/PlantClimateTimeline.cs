using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>植物共用世界时间补算器；每次最多 2048 段，未结算完的游标保留，历史段只使用可重建的气候和季节。</summary>
public static class PlantClimateTimeline
{
    /// <summary>推进暴露状态，并让调用方按同一时间段结算成长；死亡时不执行本段产出。</summary>
    public static bool Advance(Item item, TimeData clock, double now, ref double cursor,
        IReadOnlyList<IPlantEnvironmentCondition> conditions, Action<float, float, bool> grow, out float stress)
    {
        stress = 0f;
        TemperatureMgr manager = TemperatureMgr.Instance;
        if (manager == null || !manager.TryGetClimateBaseline(item.transform.position, out float baseline)) return true;
        bool historical = now - cursor > 1d;
        bool seasonal = DimensionManager.ExistingInstance?.ActiveDefinition?.SuppressWeather != true;
        if (!manager.TryGetAmbientTemperature(item.transform.position, out float current)) return true;
        double maxStep = Math.Min(30d, clock.DayLength / 96d);
        for (int segment = 0; segment < 2048 && now - cursor > 0.00001d; segment++)
        {
            float seconds = (float)Math.Min(maxStep, now - cursor);
            double midpoint = (cursor + seconds * 0.5d) / clock.DayLength;
            float temperature = historical ? baseline + (seasonal
                ? SeasonCalendar.SampleHistorical(clock, midpoint).TemperatureOffset : 0f) : current;
            float growth = 1f;
            stress = 0f;
            foreach (IPlantEnvironmentCondition condition in conditions)
            {
                growth *= condition.AdvanceEnvironment(temperature, seconds, clock.DayLength);
                stress = Mathf.Max(stress, condition.Stress);
                if (condition.IsDead) return false;
            }
            grow?.Invoke(seconds, growth, historical);
            cursor += seconds;
        }
        return true;
    }
}
