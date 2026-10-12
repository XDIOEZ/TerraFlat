using System;
using UnityEngine;

/// <summary>角色和普通物品共用环境传热公式，特殊接触和设备保留显式固定速率接口。</summary>
public static class ThermalRuntime
{
    #region 环境传热

    private const double AmbientDifferenceRetentionPerSecond = 0.99d;
    private const double AmbientConvergenceThreshold = 0.0001d; // 温差小于万分之一度时收敛，避免持续触发状态更新。

    /// <summary>每游戏秒缩小当前温差的 1%，升降温共用同一指数计算且不会越过环境温度。</summary>
    public static float AdvanceAmbient(float currentTemperature, float ambientTemperature, float seconds)
    {
        if (!float.IsFinite(currentTemperature))
            return float.IsFinite(ambientTemperature) ? ambientTemperature : TemperatureMgr.DefaultAmbientTemperature;
        if (!float.IsFinite(ambientTemperature) || !float.IsFinite(seconds) || seconds <= 0f)
            return currentTemperature;

        // 用双精度中间值避免大温差溢出，并减少分帧结算的舍入误差。
        double difference = (double)currentTemperature - ambientTemperature;
        if (Math.Abs(difference) <= AmbientConvergenceThreshold)
            return ambientTemperature;
        double remainingDifference = difference * Math.Pow(AmbientDifferenceRetentionPerSecond, seconds);
        if (Math.Abs(remainingDifference) <= AmbientConvergenceThreshold)
            return ambientTemperature;
        float nextTemperature = (float)(ambientTemperature + remainingDifference);
        return Mathf.Clamp(nextTemperature, Mathf.Min(currentTemperature, ambientTemperature),
            Mathf.Max(currentTemperature, ambientTemperature));
    }

    #endregion

    #region 特殊固定速率传热

    /// <summary>按设备或接触效果的显式速率推进温度，不用于普通环境传热。</summary>
    public static float AdvanceTowards(
        float currentTemperature,
        float targetTemperature,
        float transferPerSecond,
        float seconds,
        float transferMultiplier = 1f)
    {
        float speed = Mathf.Max(0f, transferPerSecond) * Mathf.Max(0f, transferMultiplier);
        return Mathf.MoveTowards(currentTemperature, targetTemperature, speed * Mathf.Max(0f, seconds));
    }

    #endregion
}
