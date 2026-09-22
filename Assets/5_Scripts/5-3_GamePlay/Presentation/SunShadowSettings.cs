using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>
/// 太阳长投影的本机画质偏好，默认开启；关闭后同时停止实体代理和 ECS 投影。
/// 偏好通过独立 Provider 注册，不写入世界存档，不随其它玩家同步。
/// </summary>
public static class SunShadowSettings
{
    #region 偏好与设置契约

    public const string EnabledSettingKey = "sun-shadows"; // 稳定控件键。
    public const bool DefaultEnabled = true; // 首次启动的默认外观。
    private const string PreferenceKey = "FlatWorld.Visual.SunShadows";
    private static readonly ISettingsProvider provider = new SunSettingsProvider();
    private static bool initialized;
    private static bool currentEnabled;
    public static event Action Changed; // 仅在设置改变时通知表现层。

    public static bool Enabled
    {
        get
        {
            if (!initialized)
            {
                currentEnabled = PlayerPrefs.GetInt(PreferenceKey, DefaultEnabled ? 1 : 0) != 0;
                initialized = true;
            }
            return currentEnabled;
        }
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

    /// <summary>暴露标准开关，支持设置页及全局恢复默认。</summary>
    private sealed class SunSettingsProvider : ISettingsProvider
    {
        public string ProviderId => "sun-shadows";
        public string DisplayName => "太阳长投影";
        public int Order => 46;
        public IReadOnlyList<ISettingsToggle> ToggleSettings { get; } = new ISettingsToggle[]
        {
            new SettingsToggle(new SettingDescriptor(EnabledSettingKey, "太阳长投影",
                SettingControlType.Toggle, "visual-effects"), () => Enabled, SetEnabled)
        };
        public IReadOnlyList<ISettingsSlider> SliderSettings => Array.Empty<ISettingsSlider>();
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();
        public void ResetToDefaults() => SetEnabled(DefaultEnabled);
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
