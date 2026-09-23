using UnityEngine;

/// <summary>太阳在当前游戏日内的表现阶段，不持有独立时间。</summary>
public enum SunShadowPhase { Night, Sunrise, Morning, Noon, Afternoon, Sunset }

/// <summary>供表现与 MOD 读取的太阳快照；方向为世界 XY 平面中的单位向量。</summary>
public readonly struct SunShadowParameters
{
    #region 只读快照
    public readonly Vector2 ShadowDirection;
    public readonly float LengthMultiplier;
    public readonly float Opacity;
    public readonly float MaximumDistance;
    public readonly SunShadowPhase Phase;
    public Vector2 SunDirection => -ShadowDirection;
    public bool IsVisible => Opacity > 0.001f;
    public Vector4 ShaderVector => new Vector4(ShadowDirection.x * LengthMultiplier,
        ShadowDirection.y * LengthMultiplier, MaximumDistance, Opacity);

    /// <summary>创建本帧统一快照。</summary>
    public SunShadowParameters(Vector2 direction, float length, float opacity, float maximumDistance, SunShadowPhase phase)
    {
        ShadowDirection = direction; LengthMultiplier = length; Opacity = opacity;
        MaximumDistance = maximumDistance; Phase = phase;
    }
    #endregion
}

/// <summary>
/// 直接采样 DayTimeSystem 的场景时间和有效光照：6 点日出、18 点日落，边界约 1 小时淡入淡出。
/// 正午仍保留短投影；最大倍率和世界长度同时限制，不使用 Unity _Time 或另一个时钟。
/// </summary>
public static class SunShadowParametersProvider
{
    #region 太阳采样与维度规则

    public const string ShaderVectorName = "_WorldSunShadow";
    private static readonly int ParametersId = Shader.PropertyToID(ShaderVectorName);
    private static readonly int ColorId = Shader.PropertyToID("_WorldSunShadowColor");
    private static readonly int BlurId = Shader.PropertyToID("_WorldSunShadowBlur"); // 共用柔化全局参数。

    /// <summary>统一的太阳采样入口，允许 Harmony/MOD 替换轨迹或特殊维度规则。</summary>
    public static SunShadowParameters Evaluate(string worldKey, float minimumLength, float maximumLength,
        float maximumDistance, float opacity)
    {
        DayTimeSystem time = DayTimeSystem.GetInstance();
        if (time == null || !AllowsSunShadows(worldKey) ||
            !time.TryGetResolvedTimeData(worldKey, out _, out TimeData data) || data == null)
            return default;

        float day = Mathf.Repeat(data.CurrentTime / Mathf.Max(1f, data.DayLength), 1f);
        float progress = (day - 0.25f) * 2f;
        if (progress <= 0f || progress >= 1f) return default;

        float altitude = Mathf.Sin(progress * Mathf.PI);
        Vector2 direction = new Vector2(-Mathf.Cos(progress * Mathf.PI), -0.55f).normalized;
        float length = Mathf.Lerp(Mathf.Max(minimumLength, maximumLength), minimumLength,
            Mathf.Pow(altitude, 0.75f));
        float fade = Mathf.SmoothStep(0f, 1f, Mathf.Min(progress, 1f - progress) / 0.08f);
        float alpha = Mathf.Clamp01(opacity) * fade * Mathf.Clamp01(time.GetLighting(worldKey));
        SunShadowPhase phase = progress < 0.08f ? SunShadowPhase.Sunrise :
            progress > 0.92f ? SunShadowPhase.Sunset :
            Mathf.Abs(progress - 0.5f) < 0.04f ? SunShadowPhase.Noon :
            progress < 0.5f ? SunShadowPhase.Morning : SunShadowPhase.Afternoon;
        return new SunShadowParameters(direction, length, alpha, Mathf.Max(0.1f, maximumDistance), phase);
    }

    /// <summary>自动规则禁止地下及固定光照维度；作者可显式覆盖。</summary>
    public static bool AllowsSunShadows(string worldKey)
    {
        DimensionManager dimensions = DimensionManager.ExistingInstance;
        if (dimensions == null || !dimensions.TryGetDefinitionForWorldKey(worldKey, out DimensionDefinition definition))
            return false;
        if (definition.SunShadows == DimensionSunShadowMode.Enabled) return true;
        if (definition.SunShadows == DimensionSunShadowMode.Disabled) return false;
        return definition.GenerationMode == DimensionGenerationMode.Surface && !definition.UseFixedLighting;
    }

    #endregion

    #region Shader 全局参数

    /// <summary>普通 Sprite 和 ECS 网格共用太阳参数、颜色与本机柔化强度。</summary>
    public static void Publish(SunShadowParameters parameters, Color color)
    {
        Shader.SetGlobalVector(ParametersId, parameters.ShaderVector);
        Shader.SetGlobalColor(ColorId, color);
        Shader.SetGlobalFloat(BlurId, SunShadowSettings.EffectiveBlurStrength);
    }

    /// <summary>禁用、退出世界和域重载时立即清零。</summary>
    public static void ClearGlobals()
    {
        Shader.SetGlobalVector(ParametersId, Vector4.zero);
        Shader.SetGlobalFloat(BlurId, 0f);
    }

    #endregion
}
