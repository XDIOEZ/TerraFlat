using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>
/// 后处理质量档位。高/中/低控制世界泛光成本及警示暗角的强度、柔和度和脉冲动画，
/// 仅保存玩家的画质偏好，不承载任何玩法规则。
/// </summary>
public enum ScreenPostProcessQuality
{
    High = 0,
    Medium = 1,
    Low = 2
}

/// <summary>独立于性能档位的三种世界画面风格，索引对应渲染 JSON 的 worldVolumes。</summary>
public enum ScreenPostProcessStyle
{
    Clear = 0,
    Soft = 1,
    Cinematic = 2
}

/// <summary>
/// 统一管理后处理质量偏好，为设置 UI 和后处理管理器提供稳定接口。
/// </summary>
public static class ScreenPostProcessSettings
{
    #region 键与默认值

    private const string QualityKey = "FlatWorld.Graphics.PostProcessQuality";
    private const string StyleKey = "FlatWorld.Graphics.PostProcessStyle";

    public static ScreenPostProcessQuality DefaultQuality =>
        (ScreenPostProcessQuality)WorldRenderingConfigCatalog.Default.preferences.postProcessQuality;
    public static ScreenPostProcessStyle DefaultStyle =>
        (ScreenPostProcessStyle)WorldRenderingConfigCatalog.Default.preferences.postProcessStyle;

    public const string SettingsProviderId = "screen-effects";
    public const string QualitySettingKey = "screenEffects.quality";
    public const string StyleSettingKey = "screenEffects.style";

    #endregion

    #region 缓存与事件

    private static bool initialized;
    private static ScreenPostProcessQuality cachedQuality;
    private static ScreenPostProcessStyle cachedStyle;

    /// <summary>后处理质量实际改变后触发。</summary>
    public static event Action Changed;

    private static readonly ISettingsProvider settingsProvider =
        CreateSettingsProvider();

    /// <summary>供主菜单设置使用的特效质量下拉列表契约。</summary>
    public static ISettingsProvider SettingsProvider => RegisterSettingsProvider();

    public static ScreenPostProcessQuality Quality
    {
        get
        {
            EnsureInitialized();
            return cachedQuality;
        }
    }

    public static ScreenPostProcessStyle Style
    {
        get
        {
            EnsureInitialized();
            return cachedStyle;
        }
    }

    /// <summary>供下拉列表直接使用的高/中/低索引。</summary>
    public static int QualityIndex => (int)Quality;
    public static int StyleIndex => (int)Style;

    #endregion

    #region 写入入口

    /// <summary>按高/中/低索引保存后处理质量，并立即通知运行时效果。</summary>
    public static ScreenPostProcessQuality SetQualityIndex(int index)
    {
        EnsureInitialized();
        ScreenPostProcessQuality sanitized = Sanitize(index);
        if (cachedQuality == sanitized)
            return cachedQuality;

        cachedQuality = sanitized;
        PlayerPrefs.SetInt(QualityKey, (int)cachedQuality);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return cachedQuality;
    }

    /// <summary>切换纯视觉风格；画质、阴影和昼夜权威光照不随风格重置。</summary>
    public static ScreenPostProcessStyle SetStyleIndex(int index)
    {
        EnsureInitialized();
        ScreenPostProcessStyle sanitized = SanitizeStyle(index);
        if (cachedStyle == sanitized)
            return cachedStyle;

        cachedStyle = sanitized;
        PlayerPrefs.SetInt(StyleKey, (int)cachedStyle);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return cachedStyle;
    }

    /// <summary>恢复后处理高质量默认值。</summary>
    public static void ResetToDefaults()
    {
        EnsureInitialized();
        bool changed = cachedQuality != DefaultQuality || cachedStyle != DefaultStyle;
        cachedQuality = DefaultQuality;
        cachedStyle = DefaultStyle;
        PlayerPrefs.SetInt(QualityKey, (int)cachedQuality);
        PlayerPrefs.SetInt(StyleKey, (int)cachedStyle);
        PlayerPrefs.Save();
        if (changed)
            Changed?.Invoke();
    }

    #endregion

    #region 初始化与校验

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        initialized = false;
        cachedQuality = default;
        cachedStyle = default;
        Changed = null;
    }

    #region 设置提供者

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSettingsProviderOnLoad()
    {
        RegisterSettingsProvider();
    }

    private static ISettingsProvider RegisterSettingsProvider()
    {
        SettingsProviderRegistry.Register(settingsProvider);
        return settingsProvider;
    }

    private static ISettingsProvider CreateSettingsProvider()
    {
        return new ScreenEffectsSettingsProvider();
    }

    private sealed class ScreenEffectsSettingsProvider : ISettingsProvider
    {
        private static readonly IReadOnlyList<SettingOption> QualityOptions =
            new SettingOption[]
            {
                new SettingOption("high", "高"),
                new SettingOption("medium", "中"),
                new SettingOption("low", "低")
            };

        private static readonly IReadOnlyList<SettingOption> StyleOptions =
            new SettingOption[]
            {
                new SettingOption("clear", "清晰原色"),
                new SettingOption("soft", "自然柔光"),
                new SettingOption("cinematic", "电影氛围")
            };

        private readonly IReadOnlyList<ISettingsDropdown> dropdowns;

        public ScreenEffectsSettingsProvider()
        {
            dropdowns = new ISettingsDropdown[]
            {
                new SettingsDropdown(
                    new SettingDescriptor(
                        QualitySettingKey,
                        "特效质量",
                        SettingControlType.Dropdown,
                        "graphics",
                        order: 1),
                    QualityOptions,
                    () => QualityIndex,
                    index =>
                    {
                        SetQualityIndex(index);
                        return null;
                    }),
                new SettingsDropdown(
                    new SettingDescriptor(
                        StyleSettingKey,
                        "画面风格",
                        SettingControlType.Dropdown,
                        "graphics",
                        order: 2),
                    StyleOptions,
                    () => StyleIndex,
                    index =>
                    {
                        if (index < 0 || index > (int)ScreenPostProcessStyle.Cinematic)
                            return "未注册的画面风格选项。";
                        SetStyleIndex(index);
                        return null;
                    })
            };
        }

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "特效";
        public int Order => 41;
        public IReadOnlyList<ISettingsToggle> ToggleSettings =>
            Array.Empty<ISettingsToggle>();
        public IReadOnlyList<ISettingsSlider> SliderSettings =>
            Array.Empty<ISettingsSlider>();
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => dropdowns;
        public IReadOnlyList<ISettingsSwitch> SwitchSettings =>
            Array.Empty<ISettingsSwitch>();

        public void ResetToDefaults() => ScreenPostProcessSettings.ResetToDefaults();
    }

    #endregion

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        cachedQuality = Sanitize(PlayerPrefs.GetInt(QualityKey, (int)DefaultQuality));
        cachedStyle = SanitizeStyle(PlayerPrefs.GetInt(StyleKey, (int)DefaultStyle));
        initialized = true;
    }

    private static ScreenPostProcessQuality Sanitize(int value)
    {
        return (ScreenPostProcessQuality)Mathf.Clamp(
            value,
            (int)ScreenPostProcessQuality.High,
            (int)ScreenPostProcessQuality.Low);
    }

    private static ScreenPostProcessStyle SanitizeStyle(int value)
    {
        return (ScreenPostProcessStyle)Mathf.Clamp(
            value,
            (int)ScreenPostProcessStyle.Clear,
            (int)ScreenPostProcessStyle.Cinematic);
    }

    #endregion
}
