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
/// 直接采样 DayTimeSystem 的场景时间和有效光照：5～6 点渐显、18～19 点渐隐。
/// 正午仍保留短投影；最大倍率和世界长度同时限制，不使用 Unity _Time 或另一个时钟。
/// </summary>
public static class SunShadowParametersProvider
{
    #region 太阳采样与维度规则

    public const string ShaderVectorName = "_WorldSunShadow";
    private const float SunriseFadeStart = 5f / 24f; // 日出前一小时开始显现。
    private const float SunriseFadeEnd = 6f / 24f;
    private const float SunsetFadeStart = 18f / 24f;
    private const float SunsetFadeEnd = 19f / 24f; // 日落后一小时完全消失。
    public static float MaximumOpacity => WorldRenderingConfigCatalog.Default.shadows.maximumOpacity;
    private static float MinimumSunlight => WorldRenderingConfigCatalog.Default.shadows.minimumSunlight;
    private static readonly int ParametersId = Shader.PropertyToID(ShaderVectorName);
    private static readonly int ColorId = Shader.PropertyToID("_WorldSunShadowColor");

    /// <summary>统一的太阳采样入口，允许 Harmony/MOD 替换轨迹或特殊维度规则。</summary>
    public static SunShadowParameters Evaluate(string worldKey, float minimumLength, float maximumLength,
        float maximumDistance)
    {
        DayTimeSystem time = DayTimeSystem.GetInstance();
        if (time == null || !AllowsSunShadows(worldKey) ||
            !time.TryGetResolvedTimeData(worldKey, out _, out TimeData data) || data == null)
            return default;

        float day = Mathf.Repeat(data.CurrentTime / Mathf.Max(1f, data.DayLength), 1f);
        if (ResolveDaylightFade(day) <= 0f) return default;

        // 渐显和渐隐期间保持地平线方向，避免 6 点或 18 点切换投影几何。
        float progress = Mathf.Clamp01((day - SunriseFadeEnd) /
            (SunsetFadeStart - SunriseFadeEnd));

        float altitude = Mathf.Sin(progress * Mathf.PI);
        Vector2 direction = new Vector2(-Mathf.Cos(progress * Mathf.PI), -0.55f).normalized;
        float length = Mathf.Lerp(Mathf.Max(minimumLength, maximumLength), minimumLength,
            Mathf.Pow(altitude, 0.75f));
        float alpha = ResolveSolarOpacity(time.GetLighting(worldKey), day);
        SunShadowPhase phase = progress < 0.08f ? SunShadowPhase.Sunrise :
            progress > 0.92f ? SunShadowPhase.Sunset :
            Mathf.Abs(progress - 0.5f) < 0.04f ? SunShadowPhase.Noon :
            progress < 0.5f ? SunShadowPhase.Morning : SunShadowPhase.Afternoon;
        return new SunShadowParameters(direction, length, alpha, Mathf.Max(0.1f, maximumDistance), phase);
    }

    /// <summary>同一场景光照对应的基础透明度；晨昏进度由 ResolveSolarOpacity 叠加。</summary>
    public static float ResolveOpacity(float sunlight)
    {
        return MaximumOpacity * Mathf.InverseLerp(MinimumSunlight, 1f, Mathf.Clamp01(sunlight));
    }

    /// <summary>所有太阳投影与脚底阴影共用的日出渐显、日落渐隐系数。</summary>
    public static float ResolveDaylightFade(float dayFraction)
    {
        float day = Mathf.Repeat(dayFraction, 1f);
        if (day <= SunriseFadeStart || day >= SunsetFadeEnd) return 0f;
        if (day < SunriseFadeEnd)
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(SunriseFadeStart, SunriseFadeEnd, day));
        if (day > SunsetFadeStart)
            return 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(SunsetFadeStart, SunsetFadeEnd, day));
        return 1f;
    }

    /// <summary>有效光照与当天太阳出现进度共同决定阴影透明度，月光不会留下太阳阴影。</summary>
    public static float ResolveSolarOpacity(float sunlight, float dayFraction)
    {
        return ResolveOpacity(sunlight) * ResolveDaylightFade(dayFraction);
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

    /// <summary>普通 Sprite 和 ECS 网格共用太阳参数、颜色与两种阴影的柔化强度。</summary>
    public static void Publish(SunShadowParameters parameters, Color color)
    {
        Shader.SetGlobalVector(ParametersId, parameters.ShaderVector);
        Shader.SetGlobalColor(ColorId, color);
        SunShadowSettings.PublishBlur();
    }

    /// <summary>太阳长投影关闭时，接触阴影仍使用同一颜色和柔化设置。</summary>
    public static void PublishContactAppearance()
    {
        Shader.SetGlobalColor(ColorId, WorldRenderingConfigCatalog.Default.shadows.color);
        SunShadowSettings.PublishBlur();
    }

    /// <summary>禁用或退出世界时只清除太阳投影，不影响脚底阴影柔化。</summary>
    public static void ClearGlobals()
    {
        Shader.SetGlobalVector(ParametersId, Vector4.zero);
    }

    #endregion
}
