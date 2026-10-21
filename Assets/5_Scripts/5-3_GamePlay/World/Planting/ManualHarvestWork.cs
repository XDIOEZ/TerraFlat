using System;

/// <summary>徒手采集按有效交互次数结算，作物、独立采集物和原生实体共用同一规则。</summary>
public static class ManualHarvestWork
{
    #region 徒手采集工作量
    public const int DefaultSteps = 5;

    public static bool Advance(ref int progress, int requiredSteps)
    {
        int steps = Math.Max(1, requiredSteps);
        progress = Math.Clamp(progress, 0, steps - 1) + 1;
        if (progress < steps) return false;
        progress = 0;
        return true;
    }
    #endregion
}
