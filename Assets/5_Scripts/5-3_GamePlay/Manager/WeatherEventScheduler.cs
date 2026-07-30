using System;
using UnityEngine;

public enum WeatherPhase
{
    Clear,
    Forecast,
    RainStarting,
    RainSteady,
    RainHeavy,
    RainEnding,
    Recovery
}

[Serializable]
public sealed class RainEventScheduleConfig
{
    [Min(0.1f)] public float MinClearInterval = 360f;
    [Min(0.1f)] public float MaxClearInterval = 720f;
    [Min(0.1f)] public float ForecastDuration = 90f;
    [Min(0.1f)] public float RainStartingDuration = 45f;
    [Min(0.1f)] public float MinSteadyRainDuration = 180f;
    [Min(0.1f)] public float MaxSteadyRainDuration = 300f;
    [Min(0.1f)] public float HeavyRainDuration = 150f;
    [Min(0.1f)] public float RainEndingDuration = 60f;
    [Min(0.1f)] public float RecoveryDuration = 90f;

    [Range(0f, 1f)] public float ForecastIntensity = 0.3f;
    [Range(0f, 1f)] public float StartingRainIntensity = 0.35f;
    [Range(0f, 1f)] public float SteadyRainIntensity = 0.65f;
    [Range(0f, 1f)] public float HeavyRainIntensity = 1f;
    [Range(0f, 1f)] public float EndingRainIntensity = 0.3f;
    [Range(0f, 1f)] public float RecoveryIntensity = 0.15f;
}

/// <summary>
/// 纯数据天气调度器。所有阶段边界都保存为绝对世界时间，确保正常推进、睡眠跳时和存档恢复走同一条结算链。
/// </summary>
public static class WeatherEventScheduler
{
    public const int CurrentDataVersion = 1;
    public const int MaxTransitionsPerAdvance = 100000;

#region 初始化与推进

    public static void InitializeIfNeeded(
        PlanetData planetData,
        float currentTotalTime,
        int worldSeed,
        RainEventScheduleConfig config)
    {
        if (planetData == null)
            throw new ArgumentNullException(nameof(planetData));

        config ??= new RainEventScheduleConfig();
        NormalizeWeatherValues(planetData);

        if (planetData.WeatherDataVersion < CurrentDataVersion)
        {
            planetData.WeatherPhase = ResolveLegacyPhase(planetData);
            planetData.WeatherPhaseStartedTotalTime = currentTotalTime;
            planetData.WeatherEventSequence = planetData.WeatherPhase == WeatherPhase.Clear ? 0 : 1;
            planetData.WeatherRandomCursor = Mathf.Max(0, planetData.WeatherRandomCursor);

            if (planetData.WeatherPhase == WeatherPhase.Clear)
            {
                planetData.WeatherPhaseEndTotalTime = 0f;
                ScheduleNextEvent(planetData, currentTotalTime, worldSeed, config);
            }
            else
            {
                planetData.NextWeatherEventTotalTime = 0f;
                planetData.WeatherPhaseEndTotalTime =
                    currentTotalTime + GetPhaseDuration(planetData.WeatherPhase, planetData, worldSeed, config);
            }

            planetData.WeatherDataVersion = CurrentDataVersion;
            return;
        }

        if (!Enum.IsDefined(typeof(WeatherPhase), planetData.WeatherPhase))
            EnterClearPhase(planetData, currentTotalTime, worldSeed, config);
        else if (planetData.WeatherPhase == WeatherPhase.Clear &&
                 planetData.NextWeatherEventTotalTime <= 0f)
            ScheduleNextEvent(planetData, currentTotalTime, worldSeed, config);
        else if (planetData.WeatherPhase != WeatherPhase.Clear &&
                 planetData.WeatherPhaseEndTotalTime <= 0f)
            planetData.WeatherPhaseEndTotalTime =
                currentTotalTime + GetPhaseDuration(planetData.WeatherPhase, planetData, worldSeed, config);

        ApplyPhasePresentation(planetData, config);
    }

    public static int Advance(
        PlanetData planetData,
        float oldTotalTime,
        float newTotalTime,
        int worldSeed,
        RainEventScheduleConfig config)
    {
        if (planetData == null)
            throw new ArgumentNullException(nameof(planetData));
        if (newTotalTime <= oldTotalTime)
            return 0;

        config ??= new RainEventScheduleConfig();
        InitializeIfNeeded(planetData, oldTotalTime, worldSeed, config);

        int transitions = 0;
        while (transitions < MaxTransitionsPerAdvance)
        {
            float boundary = GetCurrentBoundary(planetData);
            if (boundary > newTotalTime)
                break;

            TransitionAtBoundary(planetData, boundary, worldSeed, config);
            transitions++;
        }

        if (transitions >= MaxTransitionsPerAdvance && GetCurrentBoundary(planetData) <= newTotalTime)
            throw new InvalidOperationException("天气跳时跨越阶段过多，已中止以防止死循环。");

        return transitions;
    }

#endregion

#region 阶段切换

    public static void ForcePhase(
        PlanetData planetData,
        WeatherPhase phase,
        float currentTotalTime,
        int worldSeed,
        RainEventScheduleConfig config)
    {
        if (planetData == null)
            throw new ArgumentNullException(nameof(planetData));

        config ??= new RainEventScheduleConfig();
        planetData.WeatherDataVersion = CurrentDataVersion;
        planetData.WeatherPhase = phase;
        planetData.WeatherPhaseStartedTotalTime = currentTotalTime;

        if (phase == WeatherPhase.Clear)
        {
            EnterClearPhase(planetData, currentTotalTime, worldSeed, config);
            return;
        }

        planetData.WeatherEventSequence = Mathf.Max(1, planetData.WeatherEventSequence + 1);
        planetData.NextWeatherEventTotalTime = 0f;
        planetData.WeatherPhaseEndTotalTime =
            currentTotalTime + GetPhaseDuration(phase, planetData, worldSeed, config);
        ApplyPhasePresentation(planetData, config);
    }

    private static void TransitionAtBoundary(
        PlanetData planetData,
        float boundary,
        int worldSeed,
        RainEventScheduleConfig config)
    {
        WeatherPhase nextPhase = planetData.WeatherPhase switch
        {
            WeatherPhase.Clear => WeatherPhase.Forecast,
            WeatherPhase.Forecast => WeatherPhase.RainStarting,
            WeatherPhase.RainStarting => WeatherPhase.RainSteady,
            WeatherPhase.RainSteady => WeatherPhase.RainHeavy,
            WeatherPhase.RainHeavy => WeatherPhase.RainEnding,
            WeatherPhase.RainEnding => WeatherPhase.Recovery,
            _ => WeatherPhase.Clear
        };

        if (nextPhase == WeatherPhase.Clear)
        {
            EnterClearPhase(planetData, boundary, worldSeed, config);
            return;
        }

        if (planetData.WeatherPhase == WeatherPhase.Clear)
            planetData.WeatherEventSequence++;

        planetData.WeatherPhase = nextPhase;
        planetData.WeatherPhaseStartedTotalTime = boundary;
        planetData.NextWeatherEventTotalTime = 0f;
        planetData.WeatherPhaseEndTotalTime =
            boundary + GetPhaseDuration(nextPhase, planetData, worldSeed, config);
        ApplyPhasePresentation(planetData, config);
    }

    private static void EnterClearPhase(
        PlanetData planetData,
        float currentTotalTime,
        int worldSeed,
        RainEventScheduleConfig config)
    {
        planetData.WeatherPhase = WeatherPhase.Clear;
        planetData.WeatherPhaseStartedTotalTime = currentTotalTime;
        planetData.WeatherPhaseEndTotalTime = 0f;
        ApplyPhasePresentation(planetData, config);
        ScheduleNextEvent(planetData, currentTotalTime, worldSeed, config);
    }

    private static void ScheduleNextEvent(
        PlanetData planetData,
        float currentTotalTime,
        int worldSeed,
        RainEventScheduleConfig config)
    {
        float interval = NextRange(
            planetData,
            worldSeed,
            config.MinClearInterval,
            config.MaxClearInterval);
        planetData.NextWeatherEventTotalTime = currentTotalTime + Mathf.Max(0.1f, interval);
    }

#endregion

#region 查询与确定性随机

    public static float GetRemainingTime(PlanetData planetData, float currentTotalTime)
    {
        if (planetData == null)
            return 0f;

        return Mathf.Max(0f, GetCurrentBoundary(planetData) - currentTotalTime);
    }

    public static float GetCurrentBoundary(PlanetData planetData)
    {
        if (planetData == null)
            return float.PositiveInfinity;

        return planetData.WeatherPhase == WeatherPhase.Clear
            ? planetData.NextWeatherEventTotalTime
            : planetData.WeatherPhaseEndTotalTime;
    }

    private static float GetPhaseDuration(
        WeatherPhase phase,
        PlanetData planetData,
        int worldSeed,
        RainEventScheduleConfig config)
    {
        return phase switch
        {
            WeatherPhase.Forecast => Mathf.Max(0.1f, config.ForecastDuration),
            WeatherPhase.RainStarting => Mathf.Max(0.1f, config.RainStartingDuration),
            WeatherPhase.RainSteady => Mathf.Max(
                0.1f,
                NextRange(
                    planetData,
                    worldSeed,
                    config.MinSteadyRainDuration,
                    config.MaxSteadyRainDuration)),
            WeatherPhase.RainHeavy => Mathf.Max(0.1f, config.HeavyRainDuration),
            WeatherPhase.RainEnding => Mathf.Max(0.1f, config.RainEndingDuration),
            WeatherPhase.Recovery => Mathf.Max(0.1f, config.RecoveryDuration),
            _ => 0.1f
        };
    }

    private static float NextRange(PlanetData planetData, int worldSeed, float min, float max)
    {
        float lower = Mathf.Min(min, max);
        float upper = Mathf.Max(min, max);
        uint value = unchecked((uint)(worldSeed == 0 ? 1 : worldSeed));
        value ^= unchecked((uint)(planetData.WeatherRandomCursor + 1) * 0x9E3779B9u);
        value ^= unchecked((uint)(planetData.WeatherEventSequence + 1) * 0x85EBCA6Bu);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        planetData.WeatherRandomCursor++;
        float unit = (value & 0x00FFFFFFu) / 16777216f;
        return Mathf.Lerp(lower, upper, unit);
    }

#endregion

#region 表现映射

    public static void ApplyPhasePresentation(PlanetData planetData, RainEventScheduleConfig config)
    {
        config ??= new RainEventScheduleConfig();
        switch (planetData.WeatherPhase)
        {
            case WeatherPhase.Forecast:
                planetData.CurrentWeather = WeatherType.Cloudy;
                planetData.WeatherIntensity = Mathf.Clamp01(config.ForecastIntensity);
                break;
            case WeatherPhase.RainStarting:
                planetData.CurrentWeather = WeatherType.Rain;
                planetData.WeatherIntensity = Mathf.Clamp01(config.StartingRainIntensity);
                break;
            case WeatherPhase.RainSteady:
                planetData.CurrentWeather = WeatherType.Rain;
                planetData.WeatherIntensity = Mathf.Clamp01(config.SteadyRainIntensity);
                break;
            case WeatherPhase.RainHeavy:
                planetData.CurrentWeather = WeatherType.Rain;
                planetData.WeatherIntensity = Mathf.Clamp01(config.HeavyRainIntensity);
                break;
            case WeatherPhase.RainEnding:
                planetData.CurrentWeather = WeatherType.Rain;
                planetData.WeatherIntensity = Mathf.Clamp01(config.EndingRainIntensity);
                break;
            case WeatherPhase.Recovery:
                planetData.CurrentWeather = WeatherType.Cloudy;
                planetData.WeatherIntensity = Mathf.Clamp01(config.RecoveryIntensity);
                break;
            default:
                planetData.CurrentWeather = WeatherType.Clear;
                planetData.WeatherIntensity = 0f;
                break;
        }
    }

    private static WeatherPhase ResolveLegacyPhase(PlanetData planetData)
    {
        return planetData.CurrentWeather switch
        {
            WeatherType.Cloudy => WeatherPhase.Forecast,
            WeatherType.Rain when planetData.WeatherIntensity >= 0.85f => WeatherPhase.RainHeavy,
            WeatherType.Rain when planetData.WeatherIntensity >= 0.5f => WeatherPhase.RainSteady,
            WeatherType.Rain => WeatherPhase.RainStarting,
            WeatherType.Storm => WeatherPhase.RainHeavy,
            _ => WeatherPhase.Clear
        };
    }

    private static void NormalizeWeatherValues(PlanetData planetData)
    {
        planetData.WeatherIntensity = Mathf.Clamp01(planetData.WeatherIntensity);
        planetData.RainTemperatureOffset = Mathf.Min(0f, planetData.RainTemperatureOffset);
        planetData.CloudyTemperatureOffset = Mathf.Min(0f, planetData.CloudyTemperatureOffset);
        planetData.StormTemperatureOffset = Mathf.Min(
            planetData.RainTemperatureOffset,
            planetData.StormTemperatureOffset);
        planetData.WeatherRandomCursor = Mathf.Max(0, planetData.WeatherRandomCursor);
        planetData.WeatherEventSequence = Mathf.Max(0, planetData.WeatherEventSequence);
    }

#endregion
}
