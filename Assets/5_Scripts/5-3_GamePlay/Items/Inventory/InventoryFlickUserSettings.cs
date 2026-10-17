using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>保存鼠标与触屏共用的甩动门槛和目标方向偏好。</summary>
public static class InventoryFlickUserSettings
{
    #region 设置键与缓存

    public const string SettingsProviderId = "inventory_flick";
    public const string SpeedPercentSettingKey = "inventory_flick.speedPercent";
    public const string DistancePercentSettingKey = "inventory_flick.distancePercent";
    public const string AngleLimitSettingKey = "inventory_flick.angleLimit";
    public const float MinimumThresholdPercent = 25f;
    public const float MaximumThresholdPercent = 200f;
    public const float DefaultThresholdPercent = 100f;
    public const float ThresholdPercentStep = 1f;
    public const bool DefaultAngleLimitEnabled = true;

    private const string SpeedPreferenceKey = "FlatWorld.Inventory.FlickSpeedPercent";
    private const string DistancePreferenceKey = "FlatWorld.Inventory.FlickDistancePercent";
    private const string AngleLimitPreferenceKey = "FlatWorld.Inventory.FlickAngleLimit";
    private static readonly ISettingsProvider settingsProvider = new FlickSettingsProvider();
    private static bool initialized;
    private static float speedPercent = DefaultThresholdPercent;
    private static float distancePercent = DefaultThresholdPercent;
    private static bool angleLimitEnabled = DefaultAngleLimitEnabled;

    public static ISettingsProvider SettingsProvider => RegisterSettingsProvider();

    public static float SpeedPercent
    {
        get { EnsureInitialized(); return speedPercent; }
    }

    public static float DistancePercent
    {
        get { EnsureInitialized(); return distancePercent; }
    }

    public static bool AngleLimitEnabled
    {
        get { EnsureInitialized(); return angleLimitEnabled; }
    }

    // 百分比以现有手感为基准，末段速度门槛继续由手势识别器按主门槛派生。
    public static float MinimumSpeed => InventoryFlickGesture.DefaultMinimumSpeed * (SpeedPercent / DefaultThresholdPercent);
    public static float MinimumDistance => InventoryFlickGesture.DefaultMinimumDistance * (DistancePercent / DefaultThresholdPercent);

    #endregion

    #region 偏好读写

    public static float NormalizeThresholdPercent(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) value = DefaultThresholdPercent;
        return Mathf.Round(Mathf.Clamp(value, MinimumThresholdPercent, MaximumThresholdPercent) /
                           ThresholdPercentStep) * ThresholdPercentStep;
    }

    public static float SetSpeedPercent(float value)
    {
        EnsureInitialized();
        float next = NormalizeThresholdPercent(value);
        if (Mathf.Approximately(speedPercent, next)) return speedPercent;
        speedPercent = next;
        PlayerPrefs.SetFloat(SpeedPreferenceKey, next);
        PlayerPrefs.Save();
        return next;
    }

    public static float SetDistancePercent(float value)
    {
        EnsureInitialized();
        float next = NormalizeThresholdPercent(value);
        if (Mathf.Approximately(distancePercent, next)) return distancePercent;
        distancePercent = next;
        PlayerPrefs.SetFloat(DistancePreferenceKey, next);
        PlayerPrefs.Save();
        return next;
    }

    public static void SetAngleLimitEnabled(bool value)
    {
        EnsureInitialized();
        if (angleLimitEnabled == value) return;
        angleLimitEnabled = value;
        PlayerPrefs.SetInt(AngleLimitPreferenceKey, value ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void ResetToDefault()
    {
        EnsureInitialized();
        if (Mathf.Approximately(speedPercent, DefaultThresholdPercent) &&
            Mathf.Approximately(distancePercent, DefaultThresholdPercent) &&
            angleLimitEnabled == DefaultAngleLimitEnabled) return;
        speedPercent = distancePercent = DefaultThresholdPercent;
        angleLimitEnabled = DefaultAngleLimitEnabled;
        PlayerPrefs.SetFloat(SpeedPreferenceKey, speedPercent);
        PlayerPrefs.SetFloat(DistancePreferenceKey, distancePercent);
        PlayerPrefs.SetInt(AngleLimitPreferenceKey, angleLimitEnabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    private static void EnsureInitialized()
    {
        if (initialized) return;
        speedPercent = NormalizeThresholdPercent(PlayerPrefs.GetFloat(SpeedPreferenceKey, DefaultThresholdPercent));
        distancePercent = NormalizeThresholdPercent(PlayerPrefs.GetFloat(DistancePreferenceKey, DefaultThresholdPercent));
        angleLimitEnabled = PlayerPrefs.GetInt(AngleLimitPreferenceKey, DefaultAngleLimitEnabled ? 1 : 0) != 0;
        initialized = true;
    }

    #endregion

    #region 设置契约与运行注册

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        initialized = false;
        speedPercent = distancePercent = DefaultThresholdPercent;
        angleLimitEnabled = DefaultAngleLimitEnabled;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterOnLoad() => RegisterSettingsProvider();

    private static ISettingsProvider RegisterSettingsProvider()
    {
        SettingsProviderRegistry.Register(settingsProvider);
        return settingsProvider;
    }

    private sealed class FlickSettingsProvider : ISettingsProvider
    {
        private readonly IReadOnlyList<ISettingsSlider> sliders = new ISettingsSlider[]
        {
            new SettingsSlider(new SettingDescriptor(SpeedPercentSettingKey, "甩动转移速度",
                    SettingControlType.Slider, "controls", order: 0),
                MinimumThresholdPercent, MaximumThresholdPercent, ThresholdPercentStep,
                () => SpeedPercent, value => SetSpeedPercent(value)),
            new SettingsSlider(new SettingDescriptor(DistancePercentSettingKey, "甩动转移距离",
                    SettingControlType.Slider, "controls", order: 1),
                MinimumThresholdPercent, MaximumThresholdPercent, ThresholdPercentStep,
                () => DistancePercent, value => SetDistancePercent(value))
        };
        private readonly IReadOnlyList<ISettingsToggle> toggles = new ISettingsToggle[]
        {
            new SettingsToggle(new SettingDescriptor(AngleLimitSettingKey, "甩动方向限制",
                    SettingControlType.Toggle, "controls", order: 2),
                () => AngleLimitEnabled, SetAngleLimitEnabled)
        };

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "甩动快速转移";
        public int Order => 16;
        public IReadOnlyList<ISettingsSlider> SliderSettings => sliders;
        public IReadOnlyList<ISettingsToggle> ToggleSettings => toggles;
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();
        public void ResetToDefaults() => ResetToDefault();
    }

    #endregion
}
