using System;
using System.Collections.Generic;
using FlatWorld.Diagnostics;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>
/// 管理运行时日志悬浮窗的本机偏好，并通过通用 Settings Provider 暴露给设置界面。
/// 该设置只控制悬浮窗 UI 是否运行，不关闭 GameLogManager 的日志记录与磁盘输出。
/// </summary>
public static class RuntimeDebugOverlayPreferences
{
    #region 常量与状态

    private const string OverlayEnabledPreferenceKey = "FlatWorld.RuntimeDebugOverlay.Enabled";

    public const bool DefaultOverlayEnabled = true;
    public const string SettingsProviderId = "runtime-debug";
    public const string OverlayEnabledSettingKey = "runtimeDebug.overlayEnabled";
    public const string PerformanceStatisticsSettingKey = "runtimeDebug.performanceStatistics";
    public const string PerformanceSlowSummarySettingKey = "runtimeDebug.performanceSlowSummary";
    public const string PerformanceThresholdSettingKey = "runtimeDebug.performanceThresholdMs";

    private static bool initialized;
    private static bool overlayEnabled = DefaultOverlayEnabled;
    private static readonly ISettingsProvider settingsProvider = CreateSettingsProvider();

    /// <summary>日志悬浮窗启停偏好变化后广播，设置页据此回填状态。</summary>
    public static event Action Changed;

    /// <summary>供设置页绑定的通用设置契约。</summary>
    public static ISettingsProvider SettingsProvider => RegisterSettingsProvider();

    #endregion

    #region 公共接口

    /// <summary>当前是否启用运行时日志悬浮窗。</summary>
    public static bool OverlayEnabled
    {
        get
        {
            EnsureInitialized();
            return overlayEnabled;
        }
    }

    /// <summary>立即保存并应用日志悬浮窗启停状态。</summary>
    public static void SetOverlayEnabled(bool enabled)
    {
        EnsureInitialized();
        if (overlayEnabled == enabled)
        {
            RuntimeDebugOverlay.SetRuntimeEnabled(enabled);
            return;
        }

        overlayEnabled = enabled;
        PlayerPrefs.SetInt(OverlayEnabledPreferenceKey, overlayEnabled ? 1 : 0);
        PlayerPrefs.Save();
        RuntimeDebugOverlay.SetRuntimeEnabled(overlayEnabled);
        Changed?.Invoke();
    }

    /// <summary>恢复默认行为：启动日志悬浮窗。</summary>
    public static void ResetToDefault()
    {
        SetOverlayEnabled(DefaultOverlayEnabled);
        SetPerformanceStatistics(false);
        SetPerformanceSlowSummary(false);
        SetPerformanceThreshold((float)PerformanceDebugRegistry.DefaultSlowThresholdMilliseconds);
    }

    /// <summary>性能开关只影响本次会话，避免下次启动自动承担诊断成本。</summary>
    public static void SetPerformanceStatistics(bool enabled)
    {
        PerformanceDebugRegistry.SetStatisticsEnabled(enabled);
        Changed?.Invoke();
    }

    public static void SetPerformanceSlowSummary(bool enabled)
    {
        PerformanceDebugRegistry.SetSlowSummaryEnabled(enabled);
        PerformanceDebugLogPump.ApplyEnabled(enabled);
        Changed?.Invoke();
    }

    public static void SetPerformanceThreshold(float milliseconds)
    {
        PerformanceDebugRegistry.SlowThresholdMilliseconds = milliseconds;
        Changed?.Invoke();
    }

    #endregion

    #region 初始化与设置 Provider

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeCache()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        initialized = false;
        overlayEnabled = DefaultOverlayEnabled;
        Changed = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSettingsProviderOnLoad() => RegisterSettingsProvider();

    /// <summary>幂等注册并返回运行时调试设置 Provider。</summary>
    private static ISettingsProvider RegisterSettingsProvider()
    {
        SettingsProviderRegistry.Register(settingsProvider);
        return settingsProvider;
    }

    /// <summary>创建唯一的日志悬浮窗设置 Provider。</summary>
    private static ISettingsProvider CreateSettingsProvider() => new DebugSettingsProvider();

    /// <summary>把调试悬浮窗偏好适配到通用布尔设置契约。</summary>
    private sealed class DebugSettingsProvider : ISettingsProvider
    {
        private readonly IReadOnlyList<ISettingsToggle> toggles;
        private readonly IReadOnlyList<ISettingsSlider> sliders;

        public DebugSettingsProvider()
        {
            toggles = new ISettingsToggle[]
            {
                new SettingsToggle(
                    new SettingDescriptor(
                        OverlayEnabledSettingKey,
                        "日志悬浮窗",
                        SettingControlType.Toggle,
                        "debug",
                        order: 0),
                    () => OverlayEnabled,
                    SetOverlayEnabled),
                new SettingsToggle(new SettingDescriptor(PerformanceStatisticsSettingKey,
                    "性能次数统计", SettingControlType.Toggle, "debug", order: 10),
                    () => PerformanceDebugRegistry.StatisticsEnabled, SetPerformanceStatistics),
                new SettingsToggle(new SettingDescriptor(PerformanceSlowSummarySettingKey,
                    "性能慢操作摘要", SettingControlType.Toggle, "debug", order: 11),
                    () => PerformanceDebugRegistry.SlowSummaryEnabled, SetPerformanceSlowSummary)
            };
            sliders = new ISettingsSlider[]
            {
                new SettingsSlider(new SettingDescriptor(PerformanceThresholdSettingKey,
                    "性能慢操作阈值（ms）", SettingControlType.Slider, "debug", order: 12),
                    .1f, 10000f, .1f, () => (float)PerformanceDebugRegistry.SlowThresholdMilliseconds,
                    SetPerformanceThreshold)
            };
        }

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "调试";
        public int Order => 90;
        public IReadOnlyList<ISettingsToggle> ToggleSettings => toggles;
        public IReadOnlyList<ISettingsSlider> SliderSettings => sliders;
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();
        public void ResetToDefaults() => ResetToDefault();
    }

    /// <summary>首次访问时从 PlayerPrefs 恢复本机调试偏好。</summary>
    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        overlayEnabled = PlayerPrefs.GetInt(
            OverlayEnabledPreferenceKey,
            DefaultOverlayEnabled ? 1 : 0) != 0;
        initialized = true;
    }

    #endregion
}
