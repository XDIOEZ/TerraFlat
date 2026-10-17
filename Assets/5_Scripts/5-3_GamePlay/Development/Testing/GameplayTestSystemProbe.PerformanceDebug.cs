using System;
using System.Threading.Tasks;
using FlatWorld.Diagnostics;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 性能统计真实聚合与代际采样

    private static readonly PerformanceDebugPoint PerformanceFixturePoint = new("FlatWorld.PerformanceDebug.Fixture");

    private static JObject ServicePerformanceDebug(JObject args)
    {
        if (PerformanceDebugRegistry.StatisticsEnabled || PerformanceDebugRegistry.SlowSummaryEnabled)
            throw new InvalidOperationException("请先关闭性能诊断，再在隔离规则中检查清空与重置。 ");
        double threshold = PerformanceDebugRegistry.SlowThresholdMilliseconds;
        var result = new JObject();
        try
        {
            PerformanceDebugRegistry.Clear();
            int registered = PerformanceDebugRegistry.CaptureSnapshot().Length;
            using (PerformanceFixturePoint.Measure(7)) { PerformanceFixturePoint.AddWork(2); PerformanceFixturePoint.SetGauge(9); }
            result["disabled"] = ReadPerformanceFixture();
            PerformanceDebugRegistry.SetStatisticsEnabled(true);
            using (PerformanceFixturePoint.Measure(3))
            {
                using (PerformanceFixturePoint.Measure(2)) { }
                PerformanceFixturePoint.AddWork(5);
            }
            try { using var sample = PerformanceFixturePoint.Measure(4); throw new InvalidOperationException("作用域异常夹具"); }
            catch (InvalidOperationException) { }
            PerformanceFixturePoint.SetGauge(5);
            PerformanceFixturePoint.SetGauge(2);
            result["aggregated"] = ReadPerformanceFixture();

            using (PerformanceFixturePoint.Measure(100))
                PerformanceDebugRegistry.Clear();
            result["cleared"] = ReadPerformanceFixture();
            using (PerformanceFixturePoint.Measure(1)) { }
            result["afterClear"] = ReadPerformanceFixture();

            using (var staleSample = PerformanceFixturePoint.Measure(200))
            {
                PerformanceDebugRegistry.ResetSession();
                result["resetDisabled"] = !PerformanceDebugRegistry.StatisticsEnabled && !PerformanceDebugRegistry.SlowSummaryEnabled;
                PerformanceDebugRegistry.SetStatisticsEnabled(true);
                staleSample.AddWork(500);
                staleSample.SetGauge(999);
            }
            result["staleReset"] = ReadPerformanceFixture();
            using (PerformanceFixturePoint.Measure(2)) { }
            Task.Run(() =>
            {
                using var sample = PerformanceFixturePoint.Measure(6);
                sample.AddWork(4);
            }).GetAwaiter().GetResult();
            result["background"] = ReadPerformanceFixture();
            result["registrationRetained"] = PerformanceDebugRegistry.CaptureSnapshot().Length == registered;
            return result;
        }
        finally
        {
            PerformanceDebugRegistry.ResetSession();
            PerformanceDebugRegistry.SlowThresholdMilliseconds = threshold;
        }
    }

    private static JObject ReadPerformanceFixture()
    {
        foreach (PerformanceDebugRegistry.Snapshot point in PerformanceDebugRegistry.CaptureSnapshot())
            if (point.Name == PerformanceFixturePoint.Name)
                return new JObject { ["calls"] = point.Calls, ["work"] = point.Work, ["hasGauge"] = point.HasGauge,
                    ["gauge"] = point.Gauge, ["peakGauge"] = point.PeakGauge,
                    ["totalMs"] = point.TotalMilliseconds, ["maxMs"] = point.MaximumMilliseconds };
        throw new InvalidOperationException("性能夹具点位注册丢失。");
    }

    #endregion
}
