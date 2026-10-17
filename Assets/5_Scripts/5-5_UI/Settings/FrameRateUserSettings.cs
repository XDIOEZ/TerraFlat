using System;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>统一管理本机帧率上限，提供预设、自定义数值和设置会话契约。</summary>
public static class FrameRateUserSettings
{
    #region 选项与偏好

    public const int DefaultFrameRate = 60;
    public const int MinCustomFrameRate = 1;
    public const int MaxCustomFrameRate = 1000;
    public const string SettingsProviderId = "frame-rate";
    public const string LimitSettingKey = "frame-rate.limit";
    public const string CustomSettingKey = "frame-rate.custom";
    private const string ModePreferenceKey = "FlatWorld.FrameRate.Mode";
    private const string CustomPreferenceKey = "FlatWorld.FrameRate.Custom";
    private static readonly int[] presetRates =
        { -1, 30, 40, 45, 50, 60, 72, 75, 90, 100, 120, 144, 165, 180, 200, 240, 300, 360, 480 };
    public static IReadOnlyList<SettingOption> Options { get; } = BuildOptions();
    public static int DefaultOptionIndex => Array.IndexOf(presetRates, DefaultFrameRate);
    public static int CustomOptionIndex => presetRates.Length;
    private static readonly ISettingsProvider provider = new FrameRateSettingsProvider();
    private static bool initialized;
    private static int selectedIndex;
    private static int customFrameRate;
    public static ISettingsProvider SettingsProvider
    {
        get { SettingsProviderRegistry.Register(provider); return provider; }
    }
    public static int SelectedIndex { get { EnsureInitialized(); return selectedIndex; } }
    public static int CustomFrameRate { get { EnsureInitialized(); return customFrameRate; } }

    private static IReadOnlyList<SettingOption> BuildOptions()
    {
        var options = new List<SettingOption>();
        foreach (int rate in presetRates)
            options.Add(new SettingOption(rate < 0 ? "unlimited" : rate.ToString(CultureInfo.InvariantCulture),
                rate < 0 ? "不限制" : rate.ToString(CultureInfo.InvariantCulture) + " FPS"));
        options.Add(new SettingOption("custom", "自定义"));
        return options.AsReadOnly();
    }

    private static void EnsureInitialized()
    {
        if (initialized) return;
        string mode = PlayerPrefs.GetString(ModePreferenceKey, "60");
        selectedIndex = DefaultOptionIndex;
        for (int index = 0; index < Options.Count; index++)
            if (Options[index].Id == mode) { selectedIndex = index; break; }
        int saved = PlayerPrefs.GetInt(CustomPreferenceKey, DefaultFrameRate);
        customFrameRate = saved >= MinCustomFrameRate && saved <= MaxCustomFrameRate ? saved : DefaultFrameRate;
        initialized = true;
    }

    #endregion

    #region 校验与运行时应用

    /// <summary>解析输入框整数，非法文本和超出范围的值保持原设置不变。</summary>
    public static bool TryParseCustomFrameRate(string text, out int frameRate)
    {
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out frameRate) &&
            frameRate >= MinCustomFrameRate && frameRate <= MaxCustomFrameRate;
    }

    /// <summary>解析正式选项对应的帧率，-1 表示不限制。</summary>
    public static int ResolveTargetFrameRate(int optionIndex, int customRate)
    {
        if (optionIndex >= 0 && optionIndex < presetRates.Length) return presetRates[optionIndex];
        return optionIndex == CustomOptionIndex && customRate >= MinCustomFrameRate && customRate <= MaxCustomFrameRate
            ? customRate : DefaultFrameRate;
    }

    public static bool TrySetSelectedIndex(int index, out string error)
    {
        error = null;
        if (index < 0 || index >= Options.Count) { error = "帧率选项无效。"; return false; }
        EnsureInitialized();
        if (selectedIndex == index) return true;
        selectedIndex = index;
        PlayerPrefs.SetString(ModePreferenceKey, Options[index].Id);
        PlayerPrefs.Save();
        ApplyCurrentSettings();
        return true;
    }

    public static bool TrySetCustomFrameRate(int frameRate)
    {
        if (frameRate < MinCustomFrameRate || frameRate > MaxCustomFrameRate) return false;
        EnsureInitialized();
        if (customFrameRate == frameRate) return true;
        customFrameRate = frameRate;
        PlayerPrefs.SetInt(CustomPreferenceKey, frameRate);
        PlayerPrefs.Save();
        if (selectedIndex == CustomOptionIndex) ApplyCurrentSettings();
        return true;
    }

    /// <summary>画质切换后重新应用，避免质量档位的垂直同步覆盖手动上限。</summary>
    public static void ApplyCurrentSettings()
    {
        EnsureInitialized();
        int target = ResolveTargetFrameRate(selectedIndex, customFrameRate);
        // 手机的 -1 会回到平台默认低帧率，不限制时改用屏幕刷新率。
        if (target < 0 && Application.isMobilePlatform)
        {
            float refreshRate = (float)Screen.currentResolution.refreshRateRatio.value;
            target = refreshRate > 0f && !float.IsInfinity(refreshRate)
                ? Mathf.Max(1, Mathf.RoundToInt(refreshRate)) : DefaultFrameRate;
        }
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = target;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeOnLoad()
    {
        SettingsProviderRegistry.Register(provider);
        ApplyCurrentSettings();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(provider);
        initialized = false;
    }

    #endregion

    #region 设置提供者

    private sealed class FrameRateSettingsProvider : ISettingsProvider
    {
        public string ProviderId => SettingsProviderId;
        public string DisplayName => "帧率限制";
        public int Order => 41;
        public IReadOnlyList<ISettingsToggle> ToggleSettings => Array.Empty<ISettingsToggle>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();
        public IReadOnlyList<ISettingsDropdown> DropdownSettings { get; } = new ISettingsDropdown[]
        {
            new SettingsDropdown(new SettingDescriptor(LimitSettingKey, "帧数上限", SettingControlType.Dropdown, "display"),
                Options, () => SelectedIndex, index => TrySetSelectedIndex(index, out string error) ? null : error)
        };
        public IReadOnlyList<ISettingsSlider> SliderSettings { get; } = new ISettingsSlider[]
        {
            new SettingsSlider(new SettingDescriptor(CustomSettingKey, "自定义帧数", SettingControlType.Slider, "display"),
                MinCustomFrameRate, MaxCustomFrameRate, 1f, () => CustomFrameRate,
                value => { if (!float.IsNaN(value) && !float.IsInfinity(value)) TrySetCustomFrameRate(Mathf.RoundToInt(value)); })
        };
        public void ResetToDefaults()
        {
            TrySetCustomFrameRate(DefaultFrameRate);
            TrySetSelectedIndex(DefaultOptionIndex, out _);
        }
    }

    #endregion
}
