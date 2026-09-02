using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>
/// 左上角世界坐标 HUD 的显示格式。
/// 数值必须保持稳定，因为会写入本地 PlayerPrefs 偏好。
/// </summary>
public enum PlayerWorldCoordinateDisplayMode
{
    WorldCoordinates = 0,
    LatitudeLongitude = 1
}

/// <summary>
/// 管理玩家左上角信息 HUD 的本地显示偏好。
/// 坐标格式与 FPS 显隐独立于存档保存，切换世界或重启游戏后仍保持玩家最后一次选择。
/// </summary>
public static class PlayerWorldCoordinateDisplayPreferences
{
    #region 常量与缓存

    private const string DisplayModeKey = "FlatWorld.UI.PlayerCoordinateDisplayMode";
    private const string ShowFpsKey = "FlatWorld.UI.PlayerShowFps";

    public const PlayerWorldCoordinateDisplayMode DefaultMode =
        PlayerWorldCoordinateDisplayMode.WorldCoordinates;
    public const bool DefaultShowFps = false;

    public const string SettingsProviderId = "coordinate-display";
    public const string ModeSettingKey = "coordinateDisplay.mode";
    public const string ShowFpsSettingKey = "hudDisplay.showFps";

    private static bool initialized;
    private static PlayerWorldCoordinateDisplayMode currentMode;
    private static bool showFps;

    /// <summary>任一显示偏好实际变化后广播，常驻 HUD 无需逐帧读取 PlayerPrefs。</summary>
    public static event Action Changed;

    private static readonly ISettingsProvider settingsProvider =
        CreateSettingsProvider();

    /// <summary>供显示设置面板使用的坐标模式与 FPS 开关契约。</summary>
    public static ISettingsProvider SettingsProvider => RegisterSettingsProvider();

    #endregion

    #region 公共接口

    /// <summary>当前已保存的显示格式。</summary>
    public static PlayerWorldCoordinateDisplayMode Mode
    {
        get
        {
            EnsureInitialized();
            return currentMode;
        }
    }

    /// <summary>当前是否在坐标下方显示 FPS。</summary>
    public static bool ShowFps
    {
        get
        {
            EnsureInitialized();
            return showFps;
        }
    }

    /// <summary>立即保存并应用新的坐标显示格式。</summary>
    public static void SetMode(PlayerWorldCoordinateDisplayMode mode)
    {
        EnsureInitialized();
        PlayerWorldCoordinateDisplayMode nextMode = Normalize(mode);
        if (currentMode == nextMode)
            return;

        currentMode = nextMode;
        PlayerPrefs.SetInt(DisplayModeKey, (int)currentMode);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>立即保存并应用 FPS 显隐偏好。</summary>
    public static void SetShowFps(bool visible)
    {
        EnsureInitialized();
        if (showFps == visible)
            return;

        showFps = visible;
        PlayerPrefs.SetInt(ShowFpsKey, showFps ? 1 : 0);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>恢复默认的世界坐标格式并关闭 FPS 显示。</summary>
    public static void ResetToDefault()
    {
        SetMode(DefaultMode);
        SetShowFps(DefaultShowFps);
    }

    #endregion

    #region 初始化与校验

    /// <summary>退出当前运行域时清空静态缓存与事件。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeCache()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        initialized = false;
        currentMode = DefaultMode;
        showFps = DefaultShowFps;
        Changed = null;
    }

    #region 设置提供者

    /// <summary>场景加载前注册显示设置 Provider。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSettingsProviderOnLoad()
    {
        RegisterSettingsProvider();
    }

    /// <summary>幂等注册并返回显示设置 Provider。</summary>
    private static ISettingsProvider RegisterSettingsProvider()
    {
        SettingsProviderRegistry.Register(settingsProvider);
        return settingsProvider;
    }

    /// <summary>创建唯一的显示设置 Provider 实例。</summary>
    private static ISettingsProvider CreateSettingsProvider()
    {
        return new HudDisplaySettingsProvider();
    }

    /// <summary>把左上角 HUD 偏好适配为通用设置控件契约。</summary>
    private sealed class HudDisplaySettingsProvider : ISettingsProvider
    {
        private static readonly IReadOnlyList<SettingOption> Options =
            new SettingOption[]
            {
                new SettingOption("world", "世界坐标（X / Y）"),
                new SettingOption("latitude-longitude", "经纬度（经度 / 纬度）")
            };

        private readonly IReadOnlyList<ISettingsSwitch> switches;
        private readonly IReadOnlyList<ISettingsToggle> toggles;

        /// <summary>建立坐标格式与 FPS 显隐设置项。</summary>
        public HudDisplaySettingsProvider()
        {
            switches = new ISettingsSwitch[]
            {
                new SettingsSwitch(
                    new SettingDescriptor(
                        ModeSettingKey,
                        "坐标显示模式",
                        SettingControlType.Switch,
                        "display",
                        order: 0),
                    Options,
                    () => (int)Mode,
                    TrySetMode)
            };

            toggles = new ISettingsToggle[]
            {
                new SettingsToggle(
                    new SettingDescriptor(
                        ShowFpsSettingKey,
                        "FPS",
                        SettingControlType.Toggle,
                        "display",
                        order: 1),
                    () => ShowFps,
                    SetShowFps)
            };
        }

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "显示";
        public int Order => 70;
        public IReadOnlyList<ISettingsToggle> ToggleSettings => toggles;
        public IReadOnlyList<ISettingsSlider> SliderSettings =>
            Array.Empty<ISettingsSlider>();
        public IReadOnlyList<ISettingsDropdown> DropdownSettings =>
            Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => switches;

        /// <summary>恢复本 Provider 管理的全部默认显示偏好。</summary>
        public void ResetToDefaults() => ResetToDefault();

        /// <summary>校验并应用坐标显示方式。</summary>
        private static string TrySetMode(int index)
        {
            if (index < 0 || index >= Options.Count)
                return "坐标显示模式无效。";

            SetMode((PlayerWorldCoordinateDisplayMode)index);
            return null;
        }
    }

    #endregion

    /// <summary>首次访问时从 PlayerPrefs 恢复全部显示偏好。</summary>
    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        currentMode = Normalize((PlayerWorldCoordinateDisplayMode)PlayerPrefs.GetInt(
            DisplayModeKey,
            (int)DefaultMode));
        showFps = PlayerPrefs.GetInt(ShowFpsKey, DefaultShowFps ? 1 : 0) != 0;
        initialized = true;
    }

    /// <summary>把未知枚举值回退到默认坐标格式。</summary>
    private static PlayerWorldCoordinateDisplayMode Normalize(
        PlayerWorldCoordinateDisplayMode mode)
    {
        return mode == PlayerWorldCoordinateDisplayMode.LatitudeLongitude
            ? PlayerWorldCoordinateDisplayMode.LatitudeLongitude
            : DefaultMode;
    }

    #endregion
}
