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
    [Header("每日降雨")]
    [Range(0f, 1f), Tooltip("每天只判定一次降雨；0.05 表示每天有 5% 概率下雨。")]
    public float DailyRainChance = 0.05f;

    [Min(0.01f), Tooltip("一次降雨最少持续多少个游戏日；总时长包含起雨、稳定、暴雨和收尾阶段。")]
    public float MinRainDurationDays = 0.5f;

    [Min(0.01f), Tooltip("一次降雨最多持续多少个游戏日；1 表示一整天。")]
    public float MaxRainDurationDays = 1f;

    [Header("天气阶段（游戏秒）")]
    [Min(0.1f)] public float ForecastDuration = 90f;
    [Min(0.1f)] public float RainStartingDuration = 45f;
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
    public const int CurrentDataVersion = 2;
    public const int MaxTransitionsPerAdvance = 100000;

#region 初始化与推进

    public static void InitializeIfNeeded(
        PlanetData planetData,
        float currentTotalTime,
        float dayLength,
        int worldSeed,
        RainEventScheduleConfig config)
    {
        if (planetData == null)
            throw new ArgumentNullException(nameof(planetData));

        config ??= new RainEventScheduleConfig();
        NormalizeWeatherValues(planetData);

        if (planetData.WeatherDataVersion < CurrentDataVersion)
        {
            bool hasStructuredWeatherData = planetData.WeatherDataVersion >= 1;
            if (!hasStructuredWeatherData || !Enum.IsDefined(typeof(WeatherPhase), planetData.WeatherPhase))
            {
                planetData.WeatherPhase = ResolveLegacyPhase(planetData);
                planetData.WeatherPhaseStartedTotalTime = currentTotalTime;
                planetData.WeatherEventSequence = planetData.WeatherPhase == WeatherPhase.Clear ? 0 : 1;
            }

            if (planetData.WeatherPhase == WeatherPhase.Clear)
            {
                planetData.WeatherPhaseStartedTotalTime = currentTotalTime;
                planetData.WeatherPhaseEndTotalTime = 0f;
                ScheduleNextDailyCheck(planetData, currentTotalTime, dayLength);
            }
            else if (!hasStructuredWeatherData || planetData.WeatherPhaseEndTotalTime <= currentTotalTime)
            {
                planetData.NextWeatherEventTotalTime = 0f;
                planetData.WeatherPhaseStartedTotalTime = currentTotalTime;
                planetData.WeatherPhaseEndTotalTime =
                    currentTotalTime + GetPhaseDuration(
                        planetData.WeatherPhase,
                        planetData,
                        dayLength,
                        worldSeed,
                        config);
            }

            planetData.WeatherDataVersion = CurrentDataVersion;
            ApplyPhasePresentation(planetData, config);
            return;
        }

        if (!Enum.IsDefined(typeof(WeatherPhase), planetData.WeatherPhase))
            EnterClearPhase(planetData, currentTotalTime, dayLength, config);
        else if (planetData.WeatherPhase == WeatherPhase.Clear &&
                 planetData.NextWeatherEventTotalTime <= 0f)
            ScheduleNextDailyCheck(planetData, currentTotalTime, dayLength);
        else if (planetData.WeatherPhase != WeatherPhase.Clear &&
                 planetData.WeatherPhaseEndTotalTime <= 0f)
            planetData.WeatherPhaseEndTotalTime =
                currentTotalTime + GetPhaseDuration(
                    planetData.WeatherPhase,
                    planetData,
                    dayLength,
                    worldSeed,
                    config);

        ApplyPhasePresentation(planetData, config);
    }

    public static int Advance(
        PlanetData planetData,
        float oldTotalTime,
        float newTotalTime,
        float dayLength,
        int worldSeed,
        RainEventScheduleConfig config)
    {
        if (planetData == null)
            throw new ArgumentNullException(nameof(planetData));
        if (newTotalTime <= oldTotalTime)
            return 0;

        config ??= new RainEventScheduleConfig();
        InitializeIfNeeded(planetData, oldTotalTime, dayLength, worldSeed, config);

        int transitions = 0;
        while (transitions < MaxTransitionsPerAdvance)
        {
            float boundary = GetCurrentBoundary(planetData);
            if (boundary > newTotalTime)
                break;

            TransitionAtBoundary(planetData, boundary, dayLength, worldSeed, config);
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
        float dayLength,
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
            EnterClearPhase(planetData, currentTotalTime, dayLength, config);
            return;
        }

        planetData.WeatherEventSequence = Mathf.Max(1, planetData.WeatherEventSequence + 1);
        planetData.NextWeatherEventTotalTime = 0f;
        planetData.WeatherPhaseEndTotalTime =
            currentTotalTime + GetPhaseDuration(phase, planetData, dayLength, worldSeed, config);
        ApplyPhasePresentation(planetData, config);
    }

    private static void TransitionAtBoundary(
        PlanetData planetData,
        float boundary,
        float dayLength,
        int worldSeed,
        RainEventScheduleConfig config)
    {
        if (planetData.WeatherPhase == WeatherPhase.Clear &&
            !ShouldStartRainEvent(planetData, worldSeed, config))
        {
            planetData.WeatherPhaseStartedTotalTime = boundary;
            ScheduleNextDailyCheck(planetData, boundary, dayLength);
            ApplyPhasePresentation(planetData, config);
            return;
        }

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
            EnterClearPhase(planetData, boundary, dayLength, config);
            return;
        }

        if (planetData.WeatherPhase == WeatherPhase.Clear)
            planetData.WeatherEventSequence++;

        planetData.WeatherPhase = nextPhase;
        planetData.WeatherPhaseStartedTotalTime = boundary;
        planetData.NextWeatherEventTotalTime = 0f;
        planetData.WeatherPhaseEndTotalTime =
            boundary + GetPhaseDuration(nextPhase, planetData, dayLength, worldSeed, config);
        ApplyPhasePresentation(planetData, config);
    }

    private static void EnterClearPhase(
        PlanetData planetData,
        float currentTotalTime,
        float dayLength,
        RainEventScheduleConfig config)
    {
        planetData.WeatherPhase = WeatherPhase.Clear;
        planetData.WeatherPhaseStartedTotalTime = currentTotalTime;
        planetData.WeatherPhaseEndTotalTime = 0f;
        ApplyPhasePresentation(planetData, config);
        ScheduleNextDailyCheck(planetData, currentTotalTime, dayLength);
    }

    private static void ScheduleNextDailyCheck(
        PlanetData planetData,
        float currentTotalTime,
        float dayLength)
    {
        double normalizedDayLength = Math.Max(1d, dayLength);
        double normalizedCurrentTime = Math.Max(0d, currentTotalTime);
        double nextDay = (Math.Floor(normalizedCurrentTime / normalizedDayLength) + 1d) * normalizedDayLength;
        planetData.NextWeatherEventTotalTime = (float)Math.Max(normalizedCurrentTime + 0.1d, nextDay);
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
        float dayLength,
        int worldSeed,
        RainEventScheduleConfig config)
    {
        return phase switch
        {
            WeatherPhase.Forecast => Mathf.Max(0.1f, config.ForecastDuration),
            WeatherPhase.RainStarting => Mathf.Max(0.1f, config.RainStartingDuration),
            WeatherPhase.RainSteady => GetSteadyRainDuration(planetData, dayLength, worldSeed, config),
            WeatherPhase.RainHeavy => Mathf.Max(0.1f, config.HeavyRainDuration),
            WeatherPhase.RainEnding => Mathf.Max(0.1f, config.RainEndingDuration),
            WeatherPhase.Recovery => Mathf.Max(0.1f, config.RecoveryDuration),
            _ => 0.1f
        };
    }

    private static float GetSteadyRainDuration(
        PlanetData planetData,
        float dayLength,
        int worldSeed,
        RainEventScheduleConfig config)
    {
        float totalRainDuration = NextRange(
                                      planetData,
                                      worldSeed,
                                      config.MinRainDurationDays,
                                      config.MaxRainDurationDays) *
                                  Mathf.Max(1f, dayLength);
        float fixedRainDuration =
            Mathf.Max(0.1f, config.RainStartingDuration) +
            Mathf.Max(0.1f, config.HeavyRainDuration) +
            Mathf.Max(0.1f, config.RainEndingDuration);
        return Mathf.Max(0.1f, totalRainDuration - fixedRainDuration);
    }

    private static bool ShouldStartRainEvent(
        PlanetData planetData,
        int worldSeed,
        RainEventScheduleConfig config)
    {
        return NextUnit(planetData, worldSeed) < Mathf.Clamp01(config.DailyRainChance);
    }

    private static float NextRange(PlanetData planetData, int worldSeed, float min, float max)
    {
        float lower = Mathf.Min(min, max);
        float upper = Mathf.Max(min, max);
        return Mathf.Lerp(lower, upper, NextUnit(planetData, worldSeed));
    }

    private static float NextUnit(PlanetData planetData, int worldSeed)
    {
        uint value = unchecked((uint)(worldSeed == 0 ? 1 : worldSeed));
        value ^= unchecked((uint)(planetData.WeatherRandomCursor + 1) * 0x9E3779B9u);
        value ^= unchecked((uint)(planetData.WeatherEventSequence + 1) * 0x85EBCA6Bu);
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        planetData.WeatherRandomCursor++;
        return (value & 0x00FFFFFFu) / 16777216f;
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
