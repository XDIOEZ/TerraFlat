using UnityEngine;

/// <summary>
/// 世界散落物共用的水体规则：ECS 与联机 Item 兼容路径读取同一组数值。
/// 重量/体积为内容中的玩法比值，淡水下沉阈值为 0.64；液体定义只放大浮力阈值，
/// 不改写物品重量、体积或淡水原有的水线与沉没时长。
/// </summary>
public static class WorldItemWaterRules
{
    #region 共用参数

    public const float SinkRatioThreshold = 0.64f;
    // 所有掉落后端共用三倍沉没时长，不改变密度阈值或上浮速度。
    public const float SlowSinkDuration = 13.5f;
    public const float FastSinkDuration = 3f;
    public const float FastSinkRatioMultiplier = 1.3f;
    public const float FloatingMinDepth = 0.08f;
    public const float FloatingMaxDepth = 0.42f;
    public const float FloatingEntryDepth = 0.48f;
    public const float FloatingRiseDuration = 0.8f;
    public const float RiverDriftSpeed = 0.45f;
    public const float OceanDriftSpeed = 0.15f;
    public const float SettledTickInterval = 0.5f;
    public const float FloatingEntrySplashMinIntensity = 0.18f;
    public const float FloatingEntrySplashMaxIntensity = 0.6f;

    #endregion

    #region 无实例浮沉计算

    /// <summary>旧 Item 与 ECS 必须通过同一流量规则换算漂移速度；湖泊静止。</summary>
    public static float ResolveDriftSpeed(RuntimeWaterCurrentKind kind, float flow) => kind switch
    {
        RuntimeWaterCurrentKind.River => RiverDriftSpeed * WaterEnvironmentRules.ResolveRiverStrength(flow),
        RuntimeWaterCurrentKind.Ocean => OceanDriftSpeed,
        _ => 0f
    };

    /// <summary>整组数量不影响单件重量/体积比，零体积沿用既有最小分母。</summary>
    public static float ResolveWeightVolumeRatio(ItemStack stack) => stack == null ? 0f :
        Mathf.Max(0f, stack.Weight) / Mathf.Max(0.0001f, stack.Volume);

    /// <summary>液体倍率来自内容目录；达到阈值就下沉，与原 Item 路径保持一致。</summary>
    public static float ResolveSinkRatioThreshold(float buoyancyMultiplier) =>
        SinkRatioThreshold * Mathf.Max(0.0001f, buoyancyMultiplier);

    public static float ResolveSinkRatioThreshold(Vector2 worldPosition) =>
        WorldLiquidSourceResolver.TryResolve(worldPosition, out WorldLiquidSourceTarget source)
            ? ResolveSinkRatioThreshold(source.Liquid.BuoyancyThresholdMultiplier)
            : SinkRatioThreshold;

    public static bool ShouldSink(float ratio, float effectiveThreshold) => ratio >= effectiveThreshold;

    public static float ResolveSinkDuration(float ratio) => Mathf.Lerp(SlowSinkDuration, FastSinkDuration,
        Mathf.InverseLerp(SinkRatioThreshold, SinkRatioThreshold * FastSinkRatioMultiplier,
            Mathf.Max(SinkRatioThreshold, ratio)));

    public static float ResolveFloatingDepth(float ratio) => Mathf.Lerp(FloatingMinDepth, FloatingMaxDepth,
        Mathf.Clamp01(ratio / SinkRatioThreshold));

    public static float ResolveSplashIntensity(float targetDepth) => Mathf.Lerp(
        FloatingEntrySplashMinIntensity, FloatingEntrySplashMaxIntensity,
        Mathf.InverseLerp(FloatingMinDepth, FloatingMaxDepth, targetDepth));

    #endregion
}
