using System;
using MemoryPack;
using UnityEditor;
using UnityEngine;

/// <summary>验证正式物理日历的边界、运行时改周期和存档，不创建或修改玩家世界。</summary>
public static class TimeSystemPhysicsValidation
{
    #region 物理日历验收

    private static int checks;

    [MenuItem("FlatWorld/诊断/验证星球物理日历")]
    public static void RunMenu() => Debug.Log(Run());

    public static string Run()
    {
        checks = 0;
        TimeData time = TimeSystemConfigLoader.LoadBuiltIn().Profiles[0].CreateTimeData();
        Equal(time.DayLength, 1440d, "默认日长");
        Equal(time.OrbitalPeriodSeconds, 34560d, "默认年秒数");
        Equal(time.YearLengthDays, 24d, "默认年天数");
        Equal(time.Seasons.SpringDays + time.Seasons.SummerDays + time.Seasons.AutumnDays + time.Seasons.WinterDays, 24d, "四季总长");
        for (int season = 0; season < 4; season++) Equal(time.Seasons.GetDays((WorldSeason)season), 6d, "默认单季");
        time.SetTotalGameTimeSeconds(1439d);
        Check(time.TotalDays == 0, "日界前");
        time.SetTotalGameTimeSeconds(1440d);
        Check(time.TotalDays == 1 && time.CurrentTime == 0f, "1440 秒日界");

        foreach (float rotation in new[] { 1440f, 730f, 1f })
        foreach (float orbital in new[] { 34560f, 35001f, 3f })
        foreach (float eccentricity in new[] { 0f, 0.0167f, 0.8f, 0.999f })
        {
            var candidate = new TimeSystemProfileConfig
            {
                RotationPeriodSeconds = rotation, OrbitalPeriodSeconds = orbital,
                OrbitalEccentricity = eccentricity, InitialTime = 0f
            }.CreateTimeData();
            Equal(candidate.YearLengthDays, (double)orbital / rotation, "非整数年天数");
            Equal(candidate.Seasons.SpringDays + candidate.Seasons.SummerDays + candidate.Seasons.AutumnDays + candidate.Seasons.WinterDays,
                candidate.YearLengthDays, "偏心轨道四季归一");
            candidate.SetTotalGameTimeSeconds(orbital - 0.125d);
            Check(SeasonCalendar.Sample(candidate).Year == 1, "公转年界前");
            candidate.SetTotalGameTimeSeconds(orbital);
            SeasonSnapshot boundary = SeasonCalendar.Sample(candidate);
            Check(boundary.Year == 2 && boundary.Season == WorldSeason.Spring, "公转年界与四季边界相同");
            Equal(boundary.Progress, 0d, "跨年季节进度");
            Equal(boundary.OrbitalProgress, 0d, "跨年公转进度");
        }

        time.SetTotalGameTimeSeconds(18000d);
        SeasonSnapshot before = SeasonCalendar.Sample(time);
        SeasonSnapshot past = SeasonCalendar.SampleHistorical(time, 4000d);
        float pastTemperature = SeasonCalendar.SampleHistoricalTemperatureOffset(time, 4000d, 26f, 1f);
        time.SetRotationPeriod(720f);
        Equal(time.GetTotalGameTimeSeconds(), 18000d, "改自转保留绝对秒数");
        Equal(time.OrbitalPeriodSeconds, 34560d, "改自转不改公转");
        Equal(time.YearLengthDays, 48d, "改自转换算年天数");
        SameSeason(before, SeasonCalendar.Sample(time), "改自转保留公转季节进度");
        time.SetOrbitalPeriod(69120f);
        Equal(time.DayLength, 720d, "改公转不改日长");
        Equal(time.GetTotalGameTimeSeconds(), 18000d, "改公转不改绝对秒数");
        SameSeason(before, SeasonCalendar.Sample(time), "改公转保留年份与进度");
        SameSeason(past, SeasonCalendar.SampleHistorical(time, 4000d), "修改后历史日历不漂移");
        Equal(pastTemperature, SeasonCalendar.SampleHistoricalTemperatureOffset(time, 4000d, 26f, 1f), "历史物理温差不漂移");
        Equal(SeasonCalendar.GetHistoricalPeriod(time, 4000d).RotationPeriodSeconds, 1440d, "历史保留旧日长");

        var saved = new SerializableTimeData(time);
        TimeData restored = MemoryPackSerializer.Deserialize<SerializableTimeData>(MemoryPackSerializer.Serialize(saved)).ToTimeData();
        Equal(restored.RotationPeriodSeconds, time.RotationPeriodSeconds, "恢复自转");
        Equal(restored.OrbitalPeriodSeconds, time.OrbitalPeriodSeconds, "恢复公转");
        Equal(restored.CurrentTime, time.CurrentTime, "恢复日内时间");
        Equal(restored.OrbitalOffsetSeconds, time.OrbitalOffsetSeconds, "恢复公转起点");
        SameSeason(SeasonCalendar.Sample(time), SeasonCalendar.Sample(restored), "恢复年份与季节");
        Equal(pastTemperature, SeasonCalendar.SampleHistoricalTemperatureOffset(restored, 4000d, 26f, 1f), "恢复历史物理温差");
        Check(typeof(SerializableTimeData).GetField("DayLength") == null && typeof(SerializableTimeData).GetField("YearLengthDays") == null,
            "不保存独立日长年长");
        Check(typeof(TimeData).GetProperty("RotationPeriodSeconds").GetSetMethod() == null, "自转只经统一入口写入");
        Check(typeof(TimeData).GetProperty("OrbitalPeriodSeconds").GetSetMethod() == null, "公转只经统一入口写入");
        return "{\"success\":true,\"checks\":" + checks + ",\"daySeconds\":1440,\"yearSeconds\":34560,\"yearDays\":24}";
    }

    private static void SameSeason(SeasonSnapshot a, SeasonSnapshot b, string message)
    {
        Check(a.Year == b.Year && a.Season == b.Season, message);
        Equal(a.Progress, b.Progress, message);
        Equal(a.OrbitalProgress, b.OrbitalProgress, message);
    }

    private static void Equal(double actual, double expected, string message) =>
        Check(Math.Abs(actual - expected) <= Math.Max(1e-6d, Math.Abs(expected) * 1e-12d), message + ": " + actual + " / " + expected);

    private static void Check(bool passed, string message)
    {
        checks++;
        if (!passed) throw new InvalidOperationException("物理日历验证失败：" + message);
    }

    #endregion
}
