using System;
using UnityEngine;

/// <summary>所有自然传热共用温差百分比公式，特殊接触和设备保留显式固定速率接口。</summary>
public static class ThermalRuntime
{
    #region 环境传热

    private const double AmbientDifferenceRetentionPerSecond = 0.99d;
    public const double MinimumAmbientTemperatureChangePerSecond = 0.5d;
    private static readonly double AmbientDecayRate = -Math.Log(AmbientDifferenceRetentionPerSecond);
    private static readonly double MinimumRateDifference = MinimumAmbientTemperatureChangePerSecond / AmbientDecayRate;

    /// <summary>自然温差按每秒 1% 缩小，升降温至少每游戏秒 0.5℃，到达目标即停止。</summary>
    public static float AdvanceAmbient(float currentTemperature, float ambientTemperature, float seconds)
        => (float)AdvanceAmbient((double)currentTemperature, ambientTemperature, seconds);

    /// <summary>流体内能使用双精度温度，仍与角色和物品共用同一自然传热公式。</summary>
    public static double AdvanceAmbient(double currentTemperature, double ambientTemperature, double seconds)
    {
        if (!double.IsFinite(currentTemperature))
            return double.IsFinite(ambientTemperature) ? ambientTemperature : TemperatureMgr.DefaultAmbientTemperature;
        if (!double.IsFinite(ambientTemperature) || !double.IsFinite(seconds) || seconds <= 0d)
            return currentTemperature;

        double difference = currentTemperature - ambientTemperature;
        double distance = Math.Abs(difference);
        if (distance == 0d)
            return ambientTemperature;
        double retention = Math.Pow(AmbientDifferenceRetentionPerSecond, seconds);
        double nextTemperature;
        if (double.IsFinite(distance))
        {
            // 跨越速率下限时先结算百分比阶段，再按剩余秒数匀速收敛，保证分步与累计一致。
            double exponentialSeconds = distance > MinimumRateDifference
                ? Math.Log(distance / MinimumRateDifference) / AmbientDecayRate : 0d;
            double remainingDistance = seconds <= exponentialSeconds
                ? distance * retention
                : Math.Max(0d, Math.Min(distance, MinimumRateDifference) -
                    MinimumAmbientTemperatureChangePerSecond * (seconds - exponentialSeconds));
            if (remainingDistance == 0d)
                return ambientTemperature;
            nextTemperature = ambientTemperature + Math.Sign(difference) * remainingDistance;
        }
        else
        {
            nextTemperature = currentTemperature * retention + ambientTemperature * (1d - retention);
        }
        return Math.Clamp(nextTemperature, Math.Min(currentTemperature, ambientTemperature),
            Math.Max(currentTemperature, ambientTemperature));
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

/// <summary>通用冷热源只给环境提供目标温度，按距离衰减后的温差择优合成。</summary>
public static class ThermalInfluenceRuntime
{
    #region 目标温度影响

    public static float DistanceWeight(float distanceSquared, float radiusSquared)
    {
        if (radiusSquared <= 0f || distanceSquared >= radiusSquared)
            return 0f;
        float weight = Mathf.Max(0f, 1f - distanceSquared / radiusSquared);
        return weight * weight;
    }

    /// <summary>冷热分别取最强影响，不因堆叠相同热源而无限升温。</summary>
    public static void Accumulate(float ambient, float target, float weight,
        ref float strongestHeating, ref float strongestCooling, bool cooling = false)
    {
        float delta = (target - ambient) * Mathf.Clamp01(weight);
        if (cooling)
            strongestCooling = Mathf.Min(strongestCooling, delta);
        else
            strongestHeating = Mathf.Max(strongestHeating, delta);
    }

    public static float Compose(float ambient, float strongestHeating, float strongestCooling)
        => ambient + strongestHeating + strongestCooling;

    #endregion
}
