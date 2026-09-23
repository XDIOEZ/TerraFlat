using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>
/// 地面高度分层阴影的本机视觉偏好。宽度按单个地格的比例保存，默认 0.20 格，
/// 玩家可在 0.04～0.45 格之间调整；开关仅影响地面高度着色，不改变墙脚接触阴影和水面。
/// </summary>
public static class GroundElevationShadowSettings
{
    #region 偏好与设置契约

    public const string EnabledSettingKey = "ground-elevation-shadows"; // 地面高度阴影开关。
    public const string WidthSettingKey = "ground-elevation-shadow-width"; // 地格内阴影宽度。
    public const bool DefaultEnabled = true;
    public const float DefaultWidth = 0.20f;
    public const float MinWidth = 0.04f;
    public const float MaxWidth = 0.45f;
    private const string EnabledPreferenceKey = "FlatWorld.Visual.GroundElevationShadows";
    private const string WidthPreferenceKey = "FlatWorld.Visual.GroundElevationShadowWidth";
    private static readonly ISettingsProvider provider = new GroundElevationSettingsProvider();
    private static bool initialized;
    private static bool enabled;
    private static float width;

    public static event Action Changed; // 仅在偏好实际变化时通知地形材质与设置页。

    public static bool Enabled
    {
        get { EnsureInitialized(); return enabled; }
    }

    public static float Width
    {
        get { EnsureInitialized(); return width; }
    }

    public static ISettingsProvider SettingsProvider
    {
        get { SettingsProviderRegistry.Register(provider); return provider; }
    }

    /// <summary>保存阴影显隐并立即通知所有已注册的地形材质。</summary>
    public static void SetEnabled(bool value)
    {
        EnsureInitialized();
        if (enabled == value) return;
        enabled = value;
        PlayerPrefs.SetInt(EnabledPreferenceKey, value ? 1 : 0);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>把滑块值约束到百分之一格，避免拖动产生无意义的材质刷新。</summary>
    public static void SetWidth(float value)
    {
        EnsureInitialized();
        float next = NormalizeWidth(value);
        if (Mathf.Approximately(width, next)) return;
        width = next;
        PlayerPrefs.SetFloat(WidthPreferenceKey, next);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>读取并约束本机偏好。</summary>
    private static void EnsureInitialized()
    {
        if (initialized) return;
        enabled = PlayerPrefs.GetInt(EnabledPreferenceKey, DefaultEnabled ? 1 : 0) != 0;
        width = NormalizeWidth(PlayerPrefs.GetFloat(WidthPreferenceKey, DefaultWidth));
        initialized = true;
    }

    /// <summary>统一材质和设置滑块使用的阴影宽度步进。</summary>
    private static float NormalizeWidth(float value) =>
        Mathf.Round(Mathf.Clamp(value, MinWidth, MaxWidth) * 100f) * 0.01f;

    /// <summary>通过统一设置 Provider 暴露开关和宽度滑块。</summary>
    private sealed class GroundElevationSettingsProvider : ISettingsProvider
    {
        public string ProviderId => "ground-elevation-shadows";
        public string DisplayName => "地面层级阴影";
        public int Order => 47;
        public IReadOnlyList<ISettingsToggle> ToggleSettings { get; } = new ISettingsToggle[]
        {
            new SettingsToggle(new SettingDescriptor(EnabledSettingKey, "地面层级阴影",
                SettingControlType.Toggle, "visual-effects"), () => Enabled, SetEnabled)
        };
        public IReadOnlyList<ISettingsSlider> SliderSettings { get; } = new ISettingsSlider[]
        {
            new SettingsSlider(new SettingDescriptor(WidthSettingKey, "阴影宽度",
                SettingControlType.Slider, "visual-effects"), MinWidth, MaxWidth, 0.01f,
                () => Width, SetWidth)
        };
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();

        public void ResetToDefaults()
        {
            SetEnabled(DefaultEnabled);
            SetWidth(DefaultWidth);
        }
    }

    #endregion

    #region 运行会话

    /// <summary>关闭 Domain Reload 时清除上次运行的缓存和事件。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(provider);
        initialized = false;
        Changed = null;
    }

    /// <summary>进入主菜单前注册视觉设置，保证未进世界也能调整。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterOnLoad() => SettingsProviderRegistry.Register(provider);

    #endregion
}
