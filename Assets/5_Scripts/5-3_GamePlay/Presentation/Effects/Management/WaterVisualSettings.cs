using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>水体的两种正式美术风格；只影响本地渲染，不改变水深、地形或玩法。</summary>
public enum WaterVisualStyle
{
    Stylized = 0,
    Realistic = 1
}

/// <summary>
/// 水体风格偏好的权威入口：使用稳定选项 ID 写入 PlayerPrefs，默认保持写实风格。
/// 设置页通过 Provider 读写，水面渲染器订阅变化，在切换、区块加载及重新激活时应用。
/// </summary>
public static class WaterVisualSettings
{
    #region 偏好与设置契约

    private const string PreferenceKey = "FlatWorld.VisualEffects.WaterStyle";
    public const string ProviderId = "water-visuals";
    public const string StyleSettingKey = "waterVisuals.style";
    public const WaterVisualStyle DefaultStyle = WaterVisualStyle.Realistic;

    private static bool initialized;
    private static WaterVisualStyle currentStyle;
    private static readonly ISettingsProvider provider = new WaterSettingsProvider();

    /// <summary>仅在水体风格实际改变时通知表现组件。</summary>
    public static event Action Changed;

    /// <summary>读取本机保存的水体风格。</summary>
    public static WaterVisualStyle Style
    {
        get
        {
            EnsureInitialized();
            return currentStyle;
        }
    }

    /// <summary>取得按稳定 Key 注册的设置 Provider。</summary>
    public static ISettingsProvider SettingsProvider
    {
        get
        {
            SettingsProviderRegistry.Register(provider);
            return provider;
        }
    }

    /// <summary>保存合法风格并立即通知当前水面。</summary>
    public static void SetStyle(WaterVisualStyle style)
    {
        string optionId = GetOptionId(style);
        if (Style == style)
            return;

        currentStyle = style;
        PlayerPrefs.SetString(PreferenceKey, optionId);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>将枚举映射为持久化 ID，拒绝未注册的风格。</summary>
    private static string GetOptionId(WaterVisualStyle style) => style switch
    {
        WaterVisualStyle.Stylized => "stylized",
        WaterVisualStyle.Realistic => "realistic",
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, "未注册的水体风格。")
    };

    /// <summary>首次使用时读取偏好；没有保存值时使用默认风格。</summary>
    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        string savedId = PlayerPrefs.GetString(PreferenceKey, GetOptionId(DefaultStyle));
        currentStyle = savedId switch
        {
            "stylized" => WaterVisualStyle.Stylized,
            "realistic" => WaterVisualStyle.Realistic,
            _ => throw new InvalidOperationException($"未注册的水体风格偏好：{savedId}")
        };
        initialized = true;
    }

    #endregion

    #region 启动与 Provider

    /// <summary>关闭 Domain Reload 时也清理上一轮运行的缓存和订阅。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(provider);
        initialized = false;
        Changed = null;
    }

    /// <summary>进入场景前注册偏好，使恢复所有设置也包含水体风格。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterOnLoad()
    {
        EnsureInitialized();
        SettingsProviderRegistry.Register(provider);
    }

    /// <summary>仅暴露设置元数据和读写能力，不依赖任何 UI 控件。</summary>
    private sealed class WaterSettingsProvider : ISettingsProvider
    {
        private readonly IReadOnlyList<ISettingsSwitch> switches = new ISettingsSwitch[]
        {
            new SettingsSwitch(
                new SettingDescriptor(StyleSettingKey, "水体风格", SettingControlType.Switch,
                    "visual-effects"),
                new SettingOption[]
                {
                    new SettingOption("stylized", "风格化"),
                    new SettingOption("realistic", "写实")
                },
                () => (int)Style,
                index =>
                {
                    if (index < 0 || index > 1)
                        return "未注册的水体风格选项。";
                    SetStyle((WaterVisualStyle)index);
                    return null;
                })
        };

        public string ProviderId => WaterVisualSettings.ProviderId;
        public string DisplayName => "水体特效";
        public int Order => 45;
        public IReadOnlyList<ISettingsToggle> ToggleSettings => Array.Empty<ISettingsToggle>();
        public IReadOnlyList<ISettingsSlider> SliderSettings => Array.Empty<ISettingsSlider>();
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => switches;
        public void ResetToDefaults() => SetStyle(DefaultStyle);
    }

    #endregion
}
