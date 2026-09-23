using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>
/// 太阳长投影的本机画质偏好：默认开启投影与柔化，柔化强度默认 30%。
/// 强度按 1% 步进保存，只影响普通实体和 ECS 共用的投影 Shader，不写入世界存档。
/// </summary>
public static class SunShadowSettings
{
    #region 偏好与设置契约

    public const string EnabledSettingKey = "sun-shadows"; // 稳定控件键。
    public const string BlurEnabledSettingKey = "sun-shadow-blur-enabled"; // 柔化开关。
    public const string BlurStrengthSettingKey = "sun-shadow-blur-strength"; // 柔化强度。
    public const bool DefaultEnabled = true; // 首次启动的默认外观。
    public const bool DefaultBlurEnabled = true; // 首次启动保留柔和边缘。
    public const float DefaultBlurStrength = 0.30f; // 默认保留树冠结构。
    public const float MinBlurStrength = 0f; // 清晰轮廓。
    public const float MaxBlurStrength = 1f; // 模糊色块。
    private const string PreferenceKey = "FlatWorld.Visual.SunShadows";
    private const string BlurEnabledPreferenceKey = "FlatWorld.Visual.SunShadowBlur"; // 本机柔化开关。
    private const string BlurStrengthPreferenceKey = "FlatWorld.Visual.SunShadowBlurStrength"; // 本机强度。
    private static readonly ISettingsProvider provider = new SunSettingsProvider();
    private static bool initialized;
    private static bool currentEnabled;
    private static bool currentBlurEnabled; // 当前柔化开关缓存。
    private static float currentBlurStrength; // 当前强度缓存。
    public static event Action Changed; // 仅在设置改变时通知表现层。

    public static bool Enabled
    {
        get { EnsureInitialized(); return currentEnabled; }
    }

    public static bool BlurEnabled
    {
        get { EnsureInitialized(); return currentBlurEnabled; }
    }

    public static float BlurStrength
    {
        get { EnsureInitialized(); return currentBlurStrength; }
    }

    public static float EffectiveBlurStrength
    {
        get { EnsureInitialized(); return currentEnabled && currentBlurEnabled ? currentBlurStrength : 0f; }
    }

    public static ISettingsProvider SettingsProvider
    {
        get { SettingsProviderRegistry.Register(provider); return provider; }
    }

    /// <summary>立即保存本机偏好；先清除 GPU 参数，避免当前帧仍显示旧投影。</summary>
    public static void SetEnabled(bool value)
    {
        if (Enabled == value) return;
        currentEnabled = value;
        PlayerPrefs.SetInt(PreferenceKey, value ? 1 : 0);
        PlayerPrefs.Save();
        if (!value) SunShadowParametersProvider.ClearGlobals();
        Changed?.Invoke();
    }

    /// <summary>保存柔化开关，并通知设置页和投影渲染器。</summary>
    public static void SetBlurEnabled(bool value)
    {
        if (BlurEnabled == value) return;
        currentBlurEnabled = value;
        PlayerPrefs.SetInt(BlurEnabledPreferenceKey, value ? 1 : 0);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>把柔化强度约束到 1% 步进，拖动时即时保存。</summary>
    public static void SetBlurStrength(float value)
    {
        float next = Mathf.Round(Mathf.Clamp(value, MinBlurStrength, MaxBlurStrength) * 100f) * 0.01f;
        if (Mathf.Approximately(BlurStrength, next)) return;
        currentBlurStrength = next;
        PlayerPrefs.SetFloat(BlurStrengthPreferenceKey, next);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>在第一次访问时读取完整的本机投影偏好。</summary>
    private static void EnsureInitialized()
    {
        if (initialized) return;
        currentEnabled = PlayerPrefs.GetInt(PreferenceKey, DefaultEnabled ? 1 : 0) != 0;
        currentBlurEnabled = PlayerPrefs.GetInt(BlurEnabledPreferenceKey, DefaultBlurEnabled ? 1 : 0) != 0;
        currentBlurStrength = Mathf.Round(Mathf.Clamp(PlayerPrefs.GetFloat(BlurStrengthPreferenceKey,
            DefaultBlurStrength), MinBlurStrength, MaxBlurStrength) * 100f) * 0.01f;
        initialized = true;
    }

    /// <summary>暴露标准开关，支持设置页及全局恢复默认。</summary>
    private sealed class SunSettingsProvider : ISettingsProvider
    {
        public string ProviderId => "sun-shadows";
        public string DisplayName => "太阳长投影";
        public int Order => 46;
        public IReadOnlyList<ISettingsToggle> ToggleSettings { get; } = new ISettingsToggle[]
        {
            new SettingsToggle(new SettingDescriptor(EnabledSettingKey, "太阳长投影",
                SettingControlType.Toggle, "visual-effects"), () => Enabled, SetEnabled),
            new SettingsToggle(new SettingDescriptor(BlurEnabledSettingKey, "阴影柔化",
                SettingControlType.Toggle, "visual-effects"), () => BlurEnabled, SetBlurEnabled)
        };
        public IReadOnlyList<ISettingsSlider> SliderSettings { get; } = new ISettingsSlider[]
        {
            new SettingsSlider(new SettingDescriptor(BlurStrengthSettingKey, "模糊程度",
                SettingControlType.Slider, "visual-effects"), MinBlurStrength, MaxBlurStrength,
                0.01f, () => BlurStrength, SetBlurStrength)
        };
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();
        public void ResetToDefaults()
        {
            SetEnabled(DefaultEnabled);
            SetBlurEnabled(DefaultBlurEnabled);
            SetBlurStrength(DefaultBlurStrength);
        }
    }

    #endregion

    #region 运行会话

    /// <summary>关闭 Domain Reload 时也丢弃上一局订阅及全局表现。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(provider);
        initialized = false;
        Changed = null;
        SunShadowParametersProvider.ClearGlobals();
    }

    /// <summary>进入主菜单前注册，保证没有进入世界也能恢复默认。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterOnLoad() => SettingsProviderRegistry.Register(provider);

    #endregion
}
