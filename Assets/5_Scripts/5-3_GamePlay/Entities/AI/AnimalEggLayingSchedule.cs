using System;

/// <summary>用绝对游戏日保存单个动物的产蛋冷却，不累计待补发的蛋。</summary>
[Serializable]
public sealed class AnimalEggLayingSchedule
{
    #region 产蛋时间存档
    public const double MinimumIntervalDays = 2d;
    public bool Initialized;
    public double CycleStartedDay;
    public double NextEggDay;
    #endregion

    #region 时间规则
    public void BeginCycle(double currentDay, double minimumIntervalDays, double randomDelayDays, double randomSample)
    {
        RequireFinite(currentDay, nameof(currentDay));
        RequireFinite(randomDelayDays, nameof(randomDelayDays));
        RequireFinite(randomSample, nameof(randomSample));
        double minimum = ResolveMinimum(minimumIntervalDays);
        CycleStartedDay = currentDay;
        NextEggDay = currentDay + minimum + Math.Max(0d, randomDelayDays) * Math.Max(0d, Math.Min(1d, randomSample));
        Initialized = true;
    }

    public bool IsDue(double currentDay, double minimumIntervalDays)
    {
        if (!Initialized) return false;
        RequireFinite(currentDay, nameof(currentDay));
        RequireFinite(CycleStartedDay, nameof(CycleStartedDay));
        RequireFinite(NextEggDay, nameof(NextEggDay));
        // 配置热更新提高最小间隔时，已保存的较早截止时间也不能提前放行。
        return currentDay >= Math.Max(NextEggDay, CycleStartedDay + ResolveMinimum(minimumIntervalDays));
    }

    private static double ResolveMinimum(double configuredDays)
    {
        RequireFinite(configuredDays, nameof(configuredDays));
        return Math.Max(MinimumIntervalDays, configuredDays);
    }

    private static void RequireFinite(double value, string parameter)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            throw new ArgumentOutOfRangeException(parameter, "产蛋时间必须是有限数值。");
    }
    #endregion
}
