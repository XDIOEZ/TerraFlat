using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>本机游戏固定步频率偏好；联机会话统一使用默认频率。</summary>
public static class GameplayTickRateUserSettings
{
    #region 选项与本机持久化

    public const string SettingsProviderId = "simulation-tick-rate";
    public const string TickRateSettingKey = "simulation-tick-rate.limit";
    private const string PreferenceKey = "FlatWorld.Simulation.TickRate";
    private const int CatchUpSteps = 4;
    private static readonly int[] Rates = { 30, GameplayFixedStepSimulation.DefaultTicksPerSecond, 120 };
    private static readonly IReadOnlyList<SettingOption> Options = new SettingOption[]
    {
        new("30", "30 TPS"),
        new("60", "60 TPS"),
        new("120", "120 TPS")
    };
    private static readonly ISettingsProvider Provider = new TickRateSettingsProvider();
    private static bool initialized;
    private static bool lastAppliedOnline;
    private static int selectedIndex;

    public static ISettingsProvider SettingsProvider
    {
        get { SettingsProviderRegistry.Register(Provider); return Provider; }
    }
    public static int SelectedIndex { get { EnsureInitialized(); return selectedIndex; } }
    public static int SelectedTicksPerSecond => Rates[SelectedIndex];
    public static int EffectiveTicksPerSecond => GameNetwork.IsOnline
        ? GameplayFixedStepSimulation.DefaultTicksPerSecond : SelectedTicksPerSecond;

    private static void EnsureInitialized()
    {
        if (initialized) return;
        int savedRate = PlayerPrefs.GetInt(PreferenceKey, GameplayFixedStepSimulation.DefaultTicksPerSecond);
        selectedIndex = Array.IndexOf(Rates, savedRate);
        if (selectedIndex < 0) selectedIndex = Array.IndexOf(Rates, GameplayFixedStepSimulation.DefaultTicksPerSecond);
        initialized = true;
    }

    public static bool TrySetSelectedIndex(int index, out string error)
    {
        error = null;
        if (index < 0 || index >= Rates.Length)
        {
            error = "TPS 档位无效。";
            return false;
        }
        EnsureInitialized();
        if (selectedIndex == index) return true;
        selectedIndex = index;
        PlayerPrefs.SetInt(PreferenceKey, Rates[index]);
        PlayerPrefs.Save();
        ApplyCurrentSettings();
        return true;
    }

    #endregion

    #region Unity 时钟与网络会话

    /// <summary>保持有限追赶，较高 TPS 至少保留原有的时间预算。</summary>
    public static void ApplyCurrentSettings()
    {
        int rate = EffectiveTicksPerSecond;
        Time.fixedDeltaTime = 1f / rate;
        Time.maximumDeltaTime = Mathf.Max(CatchUpSteps * GameplayFixedStepSimulation.DefaultFixedDeltaTime,
            CatchUpSteps * Time.fixedDeltaTime);
        lastAppliedOnline = GameNetwork.IsOnline;
    }

    /// <summary>联机进入或退出时恢复统一 60 TPS，离线再使用玩家选择。</summary>
    public static void RefreshWhenNetworkStateChanges()
    {
        if (lastAppliedOnline != GameNetwork.IsOnline)
            ApplyCurrentSettings();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeOnLoad()
    {
        SettingsProviderRegistry.Register(Provider);
        ApplyCurrentSettings();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(Provider);
        initialized = false;
        lastAppliedOnline = false;
    }

    #endregion

    #region 设置提供者

    private sealed class TickRateSettingsProvider : ISettingsProvider
    {
        public string ProviderId => SettingsProviderId;
        public string DisplayName => "模拟频率";
        public int Order => 42;
        public IReadOnlyList<ISettingsToggle> ToggleSettings => Array.Empty<ISettingsToggle>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();
        public IReadOnlyList<ISettingsSlider> SliderSettings => Array.Empty<ISettingsSlider>();
        public IReadOnlyList<ISettingsDropdown> DropdownSettings { get; } = new ISettingsDropdown[]
        {
            new SettingsDropdown(new SettingDescriptor(TickRateSettingKey, "模拟频率", SettingControlType.Dropdown,
                "display"), Options, () => SelectedIndex,
                index => TrySetSelectedIndex(index, out string error) ? null : error)
        };

        public void ResetToDefaults() => TrySetSelectedIndex(1, out _);
    }

    #endregion
}
