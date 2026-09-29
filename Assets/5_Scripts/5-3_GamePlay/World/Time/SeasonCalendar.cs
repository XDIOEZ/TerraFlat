using System;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
using UnityEngine;
using FlatWorld.AIECS;
using Unity.Mathematics;

/// <summary>四季的稳定身份；数值同时用于日历顺序与季节配置查询。</summary>
public enum WorldSeason { Spring, Summer, Autumn, Winter }

/// <summary>一次已结束的季节配置区间。记录切换日和旧日历，保证修改季长不会重写离开区块期间的气候。</summary>
[Serializable, MemoryPackable]
public partial class SeasonCalendarHistoryEntry
{
    public double EndWorldDay; // 不包含此日的绝对世界时间。
    public double OffsetDays; // 当时使用的日历偏移。
    public SeasonCycleSettings Settings; // 当时生效的独立参数。
}

/// <summary>每个世界独立保存的季节参数；默认四季各 6 天，温度表示相对当地生成气候的摄氏度偏移。</summary>
[Serializable, MemoryPackable]
public partial class SeasonCycleSettings
{
    #region 配置
    public float SpringDays = 6f; // 春季游戏日数
    public float SummerDays = 6f; // 夏季游戏日数
    public float AutumnDays = 6f; // 秋季游戏日数
    public float WinterDays = 6f; // 冬季游戏日数
    public float SpringTemperatureOffset = 0f; // 春季中心温差
    public float SummerTemperatureOffset = 12f; // 夏季中心温差
    public float AutumnTemperatureOffset = 0f; // 秋季中心温差
    public float WinterTemperatureOffset = -30f; // 冬季中心温差

    [MemoryPackIgnore] public double YearDays => (double)SpringDays + SummerDays + AutumnDays + WinterDays;
    #endregion

    /// <summary>读取指定季节的长度。</summary>
    public float GetDays(WorldSeason season) => season switch
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
            float days = GetDays(season);
            float temperature = GetTemperature(season);
            if (float.IsNaN(days) || float.IsInfinity(days) || days <= 0f ||
                float.IsNaN(temperature) || float.IsInfinity(temperature))
                throw new InvalidDataException($"季节 {season} 的天数必须为有限正数，温差必须为有限数值。");
        }
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

    /// <summary>保存计算后的日历与环境读数。</summary>
    public SeasonSnapshot(WorldSeason season, int year, float progress, float elapsedDays, float durationDays, float temperatureOffset)
    {
        Season = season;
        Year = year;
        Progress = progress;
        ElapsedDays = elapsedDays;
        DurationDays = durationDays;
        TemperatureOffset = temperatureOffset;
    }
}

/// <summary>由绝对游戏日直接计算四季，不逐帧累积第二套时钟；跳时和重载使用相同计算。</summary>
public static class SeasonCalendar
{
    /// <summary>补算历史时间时使用当时的季长，设置修改只影响之后的时间。</summary>
    public static SeasonSnapshot SampleHistorical(TimeData time, double absoluteDays)
    {
        foreach (SeasonCalendarHistoryEntry entry in time.SeasonHistory)
            if (absoluteDays < entry.EndWorldDay)
                return Sample(entry.Settings, Math.Max(0d, absoluteDays + entry.OffsetDays));
        return Sample(time.Seasons, Math.Max(0d, absoluteDays + time.SeasonOffsetDays));
    }

    /// <summary>检查日历历史并复制独立数据，存档与运行时不共享可修改的配置引用。</summary>
    public static List<SeasonCalendarHistoryEntry> CopyHistory(List<SeasonCalendarHistoryEntry> source)
    {
        if (source == null) throw new InvalidDataException("当前版本存档缺少季节历史。");
        var copy = new List<SeasonCalendarHistoryEntry>(source.Count);
        double previousEnd = 0d;
        foreach (SeasonCalendarHistoryEntry entry in source)
        {
            if (entry == null || entry.Settings == null || double.IsNaN(entry.EndWorldDay) || double.IsInfinity(entry.EndWorldDay) ||
                entry.EndWorldDay < previousEnd || double.IsNaN(entry.OffsetDays) || double.IsInfinity(entry.OffsetDays))
                throw new InvalidDataException("季节历史区间或参数无效。");
            entry.Settings.Validate();
            copy.Add(new SeasonCalendarHistoryEntry { EndWorldDay = entry.EndWorldDay, OffsetDays = entry.OffsetDays, Settings = entry.Settings.Copy() });
            previousEnd = entry.EndWorldDay;
        }
        return copy;
    }
    /// <summary>读取世界时间对应的季节，允许修改季长后保留原有季节进度。</summary>
    public static SeasonSnapshot Sample(TimeData time)
    {
        double days = time.TotalDays + (double)time.CurrentTime / time.DayLength + time.SeasonOffsetDays;
        return Sample(time.Seasons, Math.Max(0d, days));
    }

    /// <summary>按各季长度定位季节，并在季节中心与交界之间平滑连接温差。</summary>
    public static SeasonSnapshot Sample(SeasonCycleSettings settings, double elapsedDays)
    {
        EntitySeasonPeriod period = CreateEntityPeriod(settings, 0d, double.PositiveInfinity);
        float temperature = period.Sample(elapsedDays, out int index, out double completedYears, out float progress);
        WorldSeason season = (WorldSeason)index;
        float duration = settings.GetDays(season);
        return new SeasonSnapshot(season, (int)Math.Min(int.MaxValue - 1d, completedYears) + 1,
            progress, progress * duration, duration, temperature);
    }

    /// <summary>把当前日历配置冻结给共享能力 Job；季节温度公式保持唯一来源。</summary>
    public static EntitySeasonPeriod CreateEntityPeriod(SeasonCycleSettings settings, double offset, double endDay) => new()
    {
        EndDay = endDay, OffsetDays = offset,
        Days = new float4(settings.SpringDays, settings.SummerDays, settings.AutumnDays, settings.WinterDays),
        Temperatures = new float4(settings.SpringTemperatureOffset, settings.SummerTemperatureOffset,
            settings.AutumnTemperatureOffset, settings.WinterTemperatureOffset)
    };

    /// <summary>替换季长时保持当前年份、季节与完成比例，不改世界时间或重新触发生产结算。</summary>
    public static void ChangeLengths(TimeData time, float spring, float summer, float autumn, float winter)
    {
        SeasonCycleSettings replacement = time.Seasons.Copy();
        replacement.SpringDays = spring;
        replacement.SummerDays = summer;
        replacement.AutumnDays = autumn;
        replacement.WinterDays = winter;
        replacement.Validate();
        SeasonSnapshot current = Sample(time);
        double adjustedDays = (current.Year - 1d) * replacement.YearDays;
        for (int index = 0; index < (int)current.Season; index++)
            adjustedDays += replacement.GetDays((WorldSeason)index);
        adjustedDays += current.Progress * replacement.GetDays(current.Season);
        double absoluteDays = time.TotalDays + (double)time.CurrentTime / time.DayLength;
        time.SeasonHistory.Add(new SeasonCalendarHistoryEntry
        {
            EndWorldDay = absoluteDays,
            OffsetDays = time.SeasonOffsetDays,
            Settings = time.Seasons.Copy()
        });
        time.SeasonOffsetDays = adjustedDays - absoluteDays;
        time.Seasons = replacement;
    }
}
