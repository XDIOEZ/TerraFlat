using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

// 只补齐未参与 SQLite 核心记录的运行时入口，存档类型从生产源码编译。
public static class GameplayWorldRegistry { public static string ExecutionWorldKey; }
public sealed class WorldAddress { public string WorldKey; public string RootWorldKey; }
public static class TimeSystemModes { public const string Unlimited = "unlimited"; }
public static class OrbitalSeasonPhysics { public static double GetSeasonFraction(WorldSeason season, double eccentricity) => .25d; }
public static class SeasonCalendar
{
    public static List<SeasonCalendarHistoryEntry> CopyHistory(List<SeasonCalendarHistoryEntry> history) => history;
}

public static class AtmosphereCatalog { public const string DefaultProfileId = "core:temperate"; }
public static class AtmosphereService
{
    public static double PressureKPa(AtmosphereState value) => 0;
    public static double MaximumPressureKPa(AtmosphereState value) => 0;
    public static float GetAtmosphericGreenhouseTemperatureOffset(AtmosphereState value) => 0;
}

public sealed class TimeData
{
    public float CurrentTime, RotationPeriodSeconds, OrbitalPeriodSeconds, AxialTiltDegrees, OrbitalEccentricity,
        TimeScaleModifier, TimeLimitTotalGameTime, LunarCycleDays, NewMoonNightIntensity, FullMoonNightIntensity, InitialMoonPhase;
    public string ReferenceScene, TimeSystemProfileId, TimeSystemMode;
    public int TotalDays;
    public AnimationCurve LightParams;
    public Gradient dayNightGradient;
    public SeasonCycleSettings Seasons = new();
    public double OrbitalOffsetSeconds;
    public List<SeasonCalendarHistoryEntry> SeasonHistory;
    public void InitializePhysicalPeriods(float rotation, float orbit) { RotationPeriodSeconds = rotation; OrbitalPeriodSeconds = orbit; }
    public void EnsureTimeSystemDefaults() { }
}
