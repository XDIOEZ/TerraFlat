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
/// 管理玩家坐标 HUD 的本地显示偏好。
/// 设置独立于存档保存，切换世界或重启游戏后仍保持玩家最后一次选择。
/// </summary>
public static class PlayerWorldCoordinateDisplayPreferences
{
    #region 常量与缓存

    private const string DisplayModeKey = "FlatWorld.UI.PlayerCoordinateDisplayMode";

    public const PlayerWorldCoordinateDisplayMode DefaultMode =
        PlayerWorldCoordinateDisplayMode.WorldCoordinates;

    public const string SettingsProviderId = "coordinate-display";
    public const string ModeSettingKey = "coordinateDisplay.mode";

    private static bool initialized;
    private static PlayerWorldCoordinateDisplayMode currentMode;

    /// <summary>显示格式实际变化后广播，常驻 HUD 无需逐帧读取偏好。</summary>
    public static event Action Changed;

    private static readonly ISettingsProvider settingsProvider =
        CreateSettingsProvider();

    /// <summary>供显示设置面板使用的按钮式模式切换契约。</summary>
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

    /// <summary>恢复默认的世界坐标显示。</summary>
    public static void ResetToDefault()
    {
        SetMode(DefaultMode);
    }

    #endregion

    #region 初始化与校验

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeCache()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        initialized = false;
        currentMode = DefaultMode;
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
        return new CoordinateDisplaySettingsProvider();
    }

    private sealed class CoordinateDisplaySettingsProvider : ISettingsProvider
    {
        private static readonly IReadOnlyList<SettingOption> Options =
            new SettingOption[]
            {
                new SettingOption("world", "世界坐标（X / Y）"),
                new SettingOption("latitude-longitude", "经纬度（经度 / 纬度）")
            };

        private readonly IReadOnlyList<ISettingsSwitch> switches;

        public CoordinateDisplaySettingsProvider()
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
        }

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "显示";
        public int Order => 70;
        public IReadOnlyList<ISettingsToggle> ToggleSettings =>
            Array.Empty<ISettingsToggle>();
        public IReadOnlyList<ISettingsSlider> SliderSettings =>
            Array.Empty<ISettingsSlider>();
        public IReadOnlyList<ISettingsDropdown> DropdownSettings =>
            Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => switches;

        public void ResetToDefaults() => ResetToDefault();

        private static string TrySetMode(int index)
        {
            if (index < 0 || index >= Options.Count)
                return "坐标显示模式无效。";

            SetMode((PlayerWorldCoordinateDisplayMode)index);
            return null;
        }
    }

    #endregion

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        currentMode = Normalize((PlayerWorldCoordinateDisplayMode)PlayerPrefs.GetInt(
            DisplayModeKey,
            (int)DefaultMode));
        initialized = true;
    }

    private static PlayerWorldCoordinateDisplayMode Normalize(
        PlayerWorldCoordinateDisplayMode mode)
    {
        return mode == PlayerWorldCoordinateDisplayMode.LatitudeLongitude
            ? PlayerWorldCoordinateDisplayMode.LatitudeLongitude
            : DefaultMode;
    }

    #endregion
}
