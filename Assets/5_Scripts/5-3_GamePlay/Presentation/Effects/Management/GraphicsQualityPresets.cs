using System;

/// <summary>整体画质预设的表现参数，不承载玩法规则或模拟频率。</summary>
public readonly struct GraphicsQualityProfile
{
    #region 预设参数

    public WaterVisualStyle WaterStyle { get; }
    public ScreenPostProcessQuality PostProcessQuality { get; }
    public bool SunShadows { get; }
    public bool ShadowBlur { get; }
    public bool GroundShadows { get; }

    public GraphicsQualityProfile(WaterVisualStyle waterStyle, ScreenPostProcessQuality postProcessQuality,
        bool sunShadows, bool shadowBlur, bool groundShadows)
    {
        WaterStyle = waterStyle;
        PostProcessQuality = postProcessQuality;
        SunShadows = sunShadows;
        ShadowBlur = shadowBlur;
        GroundShadows = groundShadows;
    }

    #endregion
}

/// <summary>组合现有渲染设置；玩家选择预设后仍可单独修改各项效果。</summary>
public static class GraphicsQualityPresets
{
    #region 预设规则

    public static GraphicsQualityProfile GetProfile(GraphicsPreset preset) => preset switch
    {
        GraphicsPreset.VeryLow => new GraphicsQualityProfile(WaterVisualStyle.LowSpec,
            ScreenPostProcessQuality.Low, false, false, false),
        GraphicsPreset.Low => new GraphicsQualityProfile(WaterVisualStyle.LowSpec,
            ScreenPostProcessQuality.Low, false, false, true),
        GraphicsPreset.Medium => new GraphicsQualityProfile(WaterVisualStyle.LowSpec,
            ScreenPostProcessQuality.Medium, true, false, true),
        GraphicsPreset.High => new GraphicsQualityProfile(WaterVisualStyle.Realistic,
            ScreenPostProcessQuality.Medium, true, false, true),
        GraphicsPreset.VeryHigh or GraphicsPreset.Ultra => new GraphicsQualityProfile(WaterVisualStyle.Realistic,
            ScreenPostProcessQuality.High, true, true, true),
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "未注册的整体画质预设。")
    };

    #endregion

    #region 显式应用

    /// <summary>仅在玩家选择整体预设时联动，启动和放弃修改只恢复各设置自身保存的值。</summary>
    public static void ApplyVisualSettings(GraphicsPreset preset)
    {
        GraphicsQualityProfile profile = GetProfile(preset);
        // 先登记全部 Provider，使正在编辑的设置会话能捕获写入前的基线。
        _ = WaterVisualSettings.SettingsProvider;
        _ = ScreenPostProcessSettings.SettingsProvider;
        _ = SunShadowSettings.SettingsProvider;
        _ = GroundElevationShadowSettings.SettingsProvider;
        WaterVisualSettings.SetStyle(profile.WaterStyle);
        SunShadowSettings.SetEnabled(profile.SunShadows);
        SunShadowSettings.SetBlurEnabled(profile.ShadowBlur);
        SunShadowSettings.SetBlurStrength(SunShadowSettings.DefaultBlurStrength);
        GroundElevationShadowSettings.SetEnabled(profile.GroundShadows);
        ScreenPostProcessSettings.SetQualityIndex((int)profile.PostProcessQuality);
    }

    #endregion
}
