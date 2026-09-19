using UnityEngine;

/// <summary>
/// 水体生存与表现共用的纯规则。有效浸没按十档每档降温 2℃，游泳消耗只读取自然水深，
/// 0.3 以下不消耗、1.0 达到基础消耗；河流流量以有界单调函数换算，湖泊和海洋独立。
/// </summary>
public static class WaterEnvironmentRules
{
    #region 生存规则

    public const float SwimmingDepthThreshold = 0.3f;

    /// <summary>浮点容差仅用于十分位边界，不改变权威水深。</summary>
    public static float ResolveCoolingDrop(float effectiveImmersion) =>
        Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(effectiveImmersion) * 10f - 0.00001f), 0, 10) * 2f;

    /// <summary>必须传入未经过浮力修正的自然水深。</summary>
    public static float ResolveSwimmingMultiplier(float naturalImmersion) =>
        Mathf.InverseLerp(SwimmingDepthThreshold, 1f, naturalImmersion);

    /// <summary>持续水域内使用同一入水基准，深度变化不能在上一目标上重复扣减。</summary>
    public static float ResolveCoolingTarget(float entryTemperature, float coolingDrop, float floor) =>
        Mathf.Max(Mathf.Max(0f, floor), entryTemperature - Mathf.Max(0f, coolingDrop));

    #endregion

    #region 流动与风浪

    /// <summary>河流优先读取水文类型；湖泊即使位于海洋群系也不能进入洋流分支。</summary>
    public static RuntimeWaterCurrentKind ResolveCurrentKind(int riverKind, bool oceanBiome) =>
        riverKind == 1 ? RuntimeWaterCurrentKind.River :
        riverKind == 2 ? RuntimeWaterCurrentKind.None :
        oceanBiome ? RuntimeWaterCurrentKind.Ocean : RuntimeWaterCurrentKind.None;

    /// <summary>流量 0 静止、1 对应原 0.45 格/秒，极大流量渐近两倍速度。</summary>
    public static float ResolveRiverStrength(float flow)
    {
        float positiveFlow = Mathf.Max(0f, flow);
        return 2f * (positiveFlow / (1f + positiveFlow));
    }

    /// <summary>风力只控制海浪速度、振幅和白沫，不参与潮汐相位。</summary>
    public static Vector3 ResolveOceanWaveFactors(float windStrength)
    {
        float wind = Mathf.Clamp01(windStrength);
        return new Vector3(Mathf.Lerp(0.65f, 1.8f, wind),
            Mathf.Lerp(0.45f, 1.65f, wind), Mathf.Lerp(0.2f, 2f, wind));
    }

    /// <summary>确定性潮流位移相位；签名刻意不接收风力。</summary>
    public static float ResolveTidePhase(float gameDay, float cyclesPerDay, float flowSpeed) =>
        Mathf.Sin(gameDay * Mathf.Max(cyclesPerDay, 0.01f) * Mathf.PI * 2f) * flowSpeed * 96f;

    #endregion
}
