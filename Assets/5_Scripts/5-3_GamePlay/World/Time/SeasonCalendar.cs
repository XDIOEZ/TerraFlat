using System;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
using Newtonsoft.Json;
using UnityEngine;
using FlatWorld.AIECS;
using Unity.Mathematics;

/// <summary>四季的稳定身份；数值同时用于日历顺序与季节配置查询。</summary>
public enum WorldSeason { Spring, Summer, Autumn, Winter }

/// <summary>一次已结束的季节配置区间。记录切换日和旧日历，保证修改季长不会重写离开区块期间的气候。</summary>
[Serializable, MemoryPackable]
public partial class SeasonCalendarHistoryEntry
{
    public double EndTimeSeconds; // 不包含此秒的绝对世界时间。
    public double OrbitalOffsetSeconds;
    public float RotationPeriodSeconds, OrbitalPeriodSeconds, AxialTiltDegrees, OrbitalEccentricity;
    public SeasonCycleSettings Settings; // 当时生效的独立参数。
}

/// <summary>每个世界独立保存四季派生长度与温差；总季长和不对称程度由公转周期与离心率决定。</summary>
[Serializable, MemoryPackable]
public partial class SeasonCycleSettings
{
    #region 配置
    [MemoryPackIgnore, JsonIgnore] public double SpringDays { get; private set; } = 6d;
    [MemoryPackIgnore, JsonIgnore] public double SummerDays { get; private set; } = 6d;
    [MemoryPackIgnore, JsonIgnore] public double AutumnDays { get; private set; } = 6d;
    [MemoryPackIgnore, JsonIgnore] public double WinterDays { get; private set; } = 6d;
    public float SpringTemperatureOffset = 0f; // 春季中心温差
    public float SummerTemperatureOffset = 12f; // 夏季中心温差
    public float AutumnTemperatureOffset = 0f; // 秋季中心温差
    public float WinterTemperatureOffset = -30f; // 冬季中心温差

    [MemoryPackIgnore, JsonIgnore] public double YearDays { get; private set; } = 24d;
    private double appliedEccentricity;
    #endregion

    /// <summary>读取指定季节的长度。</summary>
    public double GetDays(WorldSeason season) => season switch
    {
        WorldSeason.Spring => SpringDays,
        WorldSeason.Summer => SummerDays,
        WorldSeason.Autumn => AutumnDays,
        WorldSeason.Winter => WinterDays,
        _ => throw new ArgumentOutOfRangeException(nameof(season))
    };

    /// <summary>读取指定季节中心的温度偏移。</summary>
    public float GetTemperature(WorldSeason season) => season switch
    {
        WorldSeason.Spring => SpringTemperatureOffset,
        WorldSeason.Summer => SummerTemperatureOffset,
        WorldSeason.Autumn => AutumnTemperatureOffset,
        WorldSeason.Winter => WinterTemperatureOffset,
        _ => throw new ArgumentOutOfRangeException(nameof(season))
    };

    /// <summary>校验配置或当前版本存档，不修补无效季节数据。</summary>
    public void Validate()
    {
        for (int index = 0; index < 4; index++)
        {
            WorldSeason season = (WorldSeason)index;
            float temperature = GetTemperature(season);
            if (float.IsNaN(temperature) || float.IsInfinity(temperature))
                throw new InvalidDataException($"季节 {season} 的温差必须为有限数值。");
        }
    }

    /// <summary>按椭圆轨道的等真近点角四分之一重新计算四季长度。</summary>
    public void ApplyOrbitalDurations(double yearDays, double eccentricity)
    {
        Validate();
        if (double.IsNaN(yearDays) || double.IsInfinity(yearDays) || yearDays <= 0d)
            throw new InvalidDataException("公转周期换算出的一年长度必须为有限正数。");
        if (double.IsNaN(eccentricity) || double.IsInfinity(eccentricity) ||
            eccentricity < 0d || eccentricity >= 1d)
            throw new InvalidDataException("轨道离心率必须在 [0, 1) 范围内。");

        if (YearDays == yearDays && appliedEccentricity == eccentricity) return;
        YearDays = yearDays;
        appliedEccentricity = eccentricity;
        SpringDays = yearDays * OrbitalSeasonPhysics.GetSeasonFraction(WorldSeason.Spring, eccentricity);
        SummerDays = yearDays * OrbitalSeasonPhysics.GetSeasonFraction(WorldSeason.Summer, eccentricity);
        AutumnDays = yearDays * OrbitalSeasonPhysics.GetSeasonFraction(WorldSeason.Autumn, eccentricity);
        WinterDays = yearDays - (SpringDays + SummerDays + AutumnDays);
    }

    /// <summary>创建独立配置副本，避免准备界面、世界与存档共享可变对象。</summary>
    public SeasonCycleSettings Copy() => (SeasonCycleSettings)MemberwiseClone();
}

/// <summary>一次只读日历快照；年份从 1 开始，季节进度为 0～1，供环境与表现共同读取。</summary>
public readonly struct SeasonSnapshot
{
    public readonly WorldSeason Season; // 当前季节
    public readonly int Year; // 当前年份
    public readonly float Progress; // 本季进度
    public readonly float ElapsedDays; // 本季已经过的游戏日数
    public readonly float DurationDays; // 本季长度
    public readonly float TemperatureOffset; // 平滑季节温差
    public readonly float OrbitalProgress; // 与年份、四季共用的公转年内进度

    /// <summary>保存计算后的日历与环境读数。</summary>
    public SeasonSnapshot(WorldSeason season, int year, float progress, float elapsedDays, float durationDays, float temperatureOffset,
        float orbitalProgress)
    {
        Season = season;
        Year = year;
        Progress = progress;
        ElapsedDays = elapsedDays;
        DurationDays = durationDays;
        TemperatureOffset = temperatureOffset;
        OrbitalProgress = orbitalProgress;
    }
}

/// <summary>由绝对游戏日直接计算四季，不逐帧累积第二套时钟；跳时和重载使用相同计算。</summary>
public static class SeasonCalendar
{
    #region 秒制日历与历史
    /// <summary>补算历史时间时使用当时的季长，设置修改只影响之后的时间。</summary>
    public static SeasonSnapshot SampleHistorical(TimeData time, double gameSeconds) => Sample(GetHistoricalPeriod(time, gameSeconds), gameSeconds);

    public static EntitySeasonPeriod GetHistoricalPeriod(TimeData time, double gameSeconds)
    {
        foreach (SeasonCalendarHistoryEntry entry in time.SeasonHistory)
            if (gameSeconds < entry.EndTimeSeconds) return CreateEntityPeriod(entry);
        return CreateEntityPeriod(time);
    }

    public static float SampleHistoricalTemperatureOffset(TimeData time, double gameSeconds, float baselineCelsius, float poleProximity) =>
        GetHistoricalPeriod(time, gameSeconds).SamplePhysicalOffset(gameSeconds, baselineCelsius, poleProximity);

    /// <summary>修改物理参数前冻结旧区间，历史气候与年份仍读取同一游戏秒轴。</summary>
    public static void RecordHistory(TimeData time, double gameSeconds)
    {
        time.SeasonHistory.Add(new SeasonCalendarHistoryEntry
        {
            EndTimeSeconds = gameSeconds, OrbitalOffsetSeconds = time.OrbitalOffsetSeconds,
            RotationPeriodSeconds = time.RotationPeriodSeconds, OrbitalPeriodSeconds = time.OrbitalPeriodSeconds,
            AxialTiltDegrees = time.AxialTiltDegrees, OrbitalEccentricity = time.OrbitalEccentricity,
            Settings = time.Seasons.Copy()
        });
    }

    /// <summary>检查日历历史并复制独立数据，存档与运行时不共享可修改的配置引用。</summary>
    public static List<SeasonCalendarHistoryEntry> CopyHistory(List<SeasonCalendarHistoryEntry> source)
    {
        if (source == null) throw new InvalidDataException("当前版本存档缺少季节历史。");
        var copy = new List<SeasonCalendarHistoryEntry>(source.Count);
        double previousEnd = 0d;
        foreach (SeasonCalendarHistoryEntry entry in source)
        {
            if (entry == null || entry.Settings == null || double.IsNaN(entry.EndTimeSeconds) || double.IsInfinity(entry.EndTimeSeconds) ||
                entry.EndTimeSeconds < previousEnd || double.IsNaN(entry.OrbitalOffsetSeconds) || double.IsInfinity(entry.OrbitalOffsetSeconds) ||
                !IsPeriod(entry.RotationPeriodSeconds) || !IsPeriod(entry.OrbitalPeriodSeconds) ||
                !IsFinite(entry.AxialTiltDegrees) || entry.AxialTiltDegrees < 0f || entry.AxialTiltDegrees > 90f ||
                !IsFinite(entry.OrbitalEccentricity) || entry.OrbitalEccentricity < 0f || entry.OrbitalEccentricity >= 1f)
                throw new InvalidDataException("季节历史区间或参数无效。");
            entry.Settings.Validate();
            var restored = new SeasonCalendarHistoryEntry
            {
                EndTimeSeconds = entry.EndTimeSeconds, OrbitalOffsetSeconds = entry.OrbitalOffsetSeconds,
                RotationPeriodSeconds = entry.RotationPeriodSeconds, OrbitalPeriodSeconds = entry.OrbitalPeriodSeconds,
                AxialTiltDegrees = entry.AxialTiltDegrees, OrbitalEccentricity = entry.OrbitalEccentricity, Settings = entry.Settings.Copy()
            };
            restored.Settings.ApplyOrbitalDurations((double)restored.OrbitalPeriodSeconds / restored.RotationPeriodSeconds, restored.OrbitalEccentricity);
            copy.Add(restored);
            previousEnd = entry.EndTimeSeconds;
        }
        return copy;
    }
    /// <summary>读取世界时间对应的季节，允许修改季长后保留原有季节进度。</summary>
    public static SeasonSnapshot Sample(TimeData time) => Sample(CreateEntityPeriod(time), time.GetTotalGameTimeSeconds());

    /// <summary>按各季长度定位季节，并在季节中心与交界之间平滑连接温差。</summary>
    private static SeasonSnapshot Sample(EntitySeasonPeriod period, double gameSeconds)
    {
        float temperature = period.Sample(gameSeconds, out int index, out double completedYears, out float progress);
        WorldSeason season = (WorldSeason)index;
        float duration = (float)(period.Fractions[index] * period.OrbitalPeriodSeconds / period.RotationPeriodSeconds);
        return new SeasonSnapshot(season, (int)Math.Min(int.MaxValue - 1d, completedYears) + 1,
            progress, progress * duration, duration, temperature, (float)period.GetOrbitalProgress(gameSeconds));
    }

    /// <summary>把当前日历配置冻结给共享能力 Job；季节温度公式保持唯一来源。</summary>
    public static EntitySeasonPeriod CreateEntityPeriod(TimeData time) => CreateEntityPeriod(time.Seasons,
        time.RotationPeriodSeconds, time.OrbitalPeriodSeconds, time.OrbitalOffsetSeconds, double.PositiveInfinity,
        time.AxialTiltDegrees, time.OrbitalEccentricity);

    public static EntitySeasonPeriod CreateEntityPeriod(SeasonCalendarHistoryEntry entry) => CreateEntityPeriod(entry.Settings,
        entry.RotationPeriodSeconds, entry.OrbitalPeriodSeconds, entry.OrbitalOffsetSeconds, entry.EndTimeSeconds,
        entry.AxialTiltDegrees, entry.OrbitalEccentricity);

    private static EntitySeasonPeriod CreateEntityPeriod(SeasonCycleSettings settings, float rotation, float orbital,
        double offset, double end, float tilt, float eccentricity)
    {
        double yearDays = (double)orbital / rotation;
        settings.ApplyOrbitalDurations(yearDays, eccentricity);
        double spring = settings.SpringDays / yearDays, summer = settings.SummerDays / yearDays, autumn = settings.AutumnDays / yearDays;
        return new EntitySeasonPeriod
        {
            EndTimeSeconds = end, OffsetSeconds = offset, RotationPeriodSeconds = rotation, OrbitalPeriodSeconds = orbital,
            Fractions = new double4(spring, summer, autumn, 1d - (spring + summer + autumn)),
            Temperatures = new float4(settings.SpringTemperatureOffset, settings.SummerTemperatureOffset,
                settings.AutumnTemperatureOffset, settings.WinterTemperatureOffset),
            TiltScale = OrbitalSeasonPhysics.ResolveTiltScale(tilt), OrbitalEccentricity = eccentricity
        };
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsPeriod(float value) => IsFinite(value) && value >= 1f;
    #endregion
}

/// <summary>当前时间系统使用的简化轨道物理：近日点固定在冬季中段，四季边界按 90° 真近点角分区。</summary>
public static class OrbitalSeasonPhysics
{
    #region 轨道与温差
    private const double TwoPi = Math.PI * 2d;
    private const double SpringStartTrueAnomaly = Math.PI * 0.25d;
    private const double ReferenceTiltDegrees = 23.44d;

    /// <summary>按开普勒第二定律计算一个季节占整年的时间比例。</summary>
    public static double GetSeasonFraction(WorldSeason season, double eccentricity)
    {
        if (eccentricity == 0d) return 0.25d;
        int index = (int)season;
        double start = SpringStartTrueAnomaly + index * Math.PI * 0.5d;
        double end = start + Math.PI * 0.5d;
        double startMean = MeanAnomalyFromTrueAnomaly(start, eccentricity);
        double endMean = MeanAnomalyFromTrueAnomaly(end, eccentricity);
        return Math.Max(0d, (endMean - startMean) / TwoPi);
    }

    /// <summary>倾角把既有四季冷热幅度按极区接近程度放大；暖带中心附近几乎不吃倾角季节差。</summary>
    public static float ResolveTiltTemperatureOffset(TimeData time, float configuredOffset, float poleProximity)
    {
        if (time == null || configuredOffset == 0f)
            return 0f;
        return configuredOffset * ResolveTiltScale(time.AxialTiltDegrees) * Mathf.Clamp01(poleProximity);
    }

    /// <summary>以当前地球倾角为 1 倍季节响应，供 ECS 冻结纯值参数复用。</summary>
    public static float ResolveTiltScale(float axialTiltDegrees)
    {
        double reference = Math.Sin(ReferenceTiltDegrees * Math.PI / 180d);
        double actual = Math.Sin(Math.Max(0d, Math.Min(90d, axialTiltDegrees)) * Math.PI / 180d);
        double tiltScale = reference > 0d ? actual / reference : 0d;
        return (float)tiltScale;
    }

    /// <summary>轨道距离变化按辐射平衡 T∝r^-1/2 换算额外冷热差。</summary>
    public static float ResolveEccentricTemperatureOffset(TimeData time, float baselineCelsius, double gameSeconds)
    {
        float factor = ResolveEccentricTemperatureFactor(time, gameSeconds);
        double kelvin = Math.Max(1d, baselineCelsius + 273.15d);
        return (float)(kelvin * factor);
    }

    /// <summary>返回当前轨道距离造成的绝对温标相对变化率，供高频逐格温度查询复用。</summary>
    public static float ResolveEccentricTemperatureFactor(TimeData time, double gameSeconds)
    {
        if (time == null || time.OrbitalEccentricity <= 0f || time.YearLengthDays <= 0d)
            return 0f;
        double eccentricity = Math.Max(0d, Math.Min(0.999999d, time.OrbitalEccentricity));
        double phase = SeasonCalendar.CreateEntityPeriod(time).GetOrbitalProgress(gameSeconds);
        double springMean = MeanAnomalyFromTrueAnomaly(SpringStartTrueAnomaly, eccentricity);
        double meanAnomaly = NormalizeRadians(springMean + phase * TwoPi);
        double eccentricAnomaly = SolveEccentricAnomaly(meanAnomaly, eccentricity);
        double radiusRatio = Math.Max(0.000001d, 1d - eccentricity * Math.Cos(eccentricAnomaly));
        return (float)(1d / Math.Sqrt(radiusRatio) - 1d);
    }

    /// <summary>合成当前季节的倾角温差与离心率温差。</summary>
    public static float ResolveTemperatureOffset(TimeData time, SeasonSnapshot snapshot, float poleProximity,
        float baselineCelsius, double gameSeconds)
    {
        return ResolveTiltTemperatureOffset(time, snapshot.TemperatureOffset, poleProximity) +
               ResolveEccentricTemperatureOffset(time, baselineCelsius, gameSeconds);
    }

    /// <summary>把世界 Y 位置换成 0=暖带中心、1=极区的内部空间权重；不把它作为世界参数暴露。</summary>
    public static float ResolvePoleProximity(in WorldTopologyBounds bounds, Vector2 worldPosition)
    {
        if (!bounds.IsWrapped || bounds.HalfExtent.y <= 0)
            return 1f;
        Vector2 normalized = bounds.NormalizePosition(worldPosition);
        float centerY = (bounds.Min.y + bounds.MaxExclusive.y) * 0.5f;
        return Mathf.Clamp01(Mathf.Abs(normalized.y - centerY) / bounds.HalfExtent.y);
    }

    private static double MeanAnomalyFromTrueAnomaly(double trueAnomaly, double eccentricity)
    {
        double revolutions = Math.Floor(trueAnomaly / TwoPi);
        double principal = trueAnomaly - revolutions * TwoPi;
        double eccentricAnomaly = 2d * Math.Atan2(
            Math.Sqrt(1d - eccentricity) * Math.Sin(principal * 0.5d),
            Math.Sqrt(1d + eccentricity) * Math.Cos(principal * 0.5d));
        if (eccentricAnomaly < 0d)
            eccentricAnomaly += TwoPi;
        return eccentricAnomaly - eccentricity * Math.Sin(eccentricAnomaly) + revolutions * TwoPi;
    }

    private static double SolveEccentricAnomaly(double meanAnomaly, double eccentricity)
    {
        double eccentricAnomaly = eccentricity < 0.8d ? meanAnomaly : Math.PI;
        for (int i = 0; i < 8; i++)
        {
            double value = eccentricAnomaly - eccentricity * Math.Sin(eccentricAnomaly) - meanAnomaly;
            double derivative = 1d - eccentricity * Math.Cos(eccentricAnomaly);
            eccentricAnomaly -= value / Math.Max(0.000001d, derivative);
        }
        return eccentricAnomaly;
    }

    private static double NormalizeRadians(double value)
    {
        value %= TwoPi;
        return value < 0d ? value + TwoPi : value;
    }
    #endregion
}
