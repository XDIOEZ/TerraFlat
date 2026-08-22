// AI-Context: 区块流送性能模式的全局 PlayerPrefs；只控制客户端生成吞吐，不写入世界存档。
using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>
/// 区块流送的玩家性能偏好。平滑模式只使用一个后台生成线程，并继续通过协程逐帧绘制；
/// 高吞吐模式使用 CPU 安全上限内的多个线程；自动模式采用项目默认值。
/// </summary>
public enum WorldStreamingPerformanceMode
{
    Automatic = 0,
    Smooth = 1,
    Throughput = 2
}

/// <summary>保存并广播区块流送性能偏好。</summary>
public static class WorldStreamingPreferences
{
    private const string ModeKey = "FlatWorld.WorldStreaming.PerformanceMode.v1";

    public const string SettingsProviderId = "world-streaming";
    public const string ModeSettingKey = "worldStreaming.performanceMode";

    public static event Action Changed;

    private static readonly ISettingsProvider settingsProvider =
        CreateSettingsProvider();

    /// <summary>供设置 UI 使用的区块流送模式下拉列表契约。</summary>
    public static ISettingsProvider SettingsProvider => RegisterSettingsProvider();

    public static WorldStreamingPerformanceMode Mode
    {
        get
        {
            int value = PlayerPrefs.GetInt(ModeKey,
                (int)WorldStreamingPerformanceMode.Automatic);
            return Enum.IsDefined(typeof(WorldStreamingPerformanceMode), value)
                ? (WorldStreamingPerformanceMode)value
                : WorldStreamingPerformanceMode.Automatic;
        }
    }

    /// <summary>保存模式并让正在运行的 ChunkMgr 立即更新调度器。</summary>
    public static void SetMode(WorldStreamingPerformanceMode mode)
    {
        if (!Enum.IsDefined(typeof(WorldStreamingPerformanceMode), mode))
            mode = WorldStreamingPerformanceMode.Automatic;
        if (Mode == mode)
            return;

        PlayerPrefs.SetInt(ModeKey, (int)mode);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>把玩家模式转换为实际后台生成基础并发。</summary>
    public static int ResolveBaseGenerationConcurrency(int automaticValue)
    {
        return Mode switch
        {
            WorldStreamingPerformanceMode.Smooth => 1,
            WorldStreamingPerformanceMode.Throughput =>
                ChunkMgr.SafeBackgroundGenerationCeiling,
            _ => Mathf.Clamp(automaticValue, 1,
                ChunkMgr.SafeBackgroundGenerationCeiling)
        };
    }

    #region 设置提供者

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSettingsProviderOnLoad()
    {
        RegisterSettingsProvider();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        Changed = null;
    }

    private static ISettingsProvider RegisterSettingsProvider()
    {
        SettingsProviderRegistry.Register(settingsProvider);
        return settingsProvider;
    }

    private static ISettingsProvider CreateSettingsProvider()
    {
        return new WorldStreamingSettingsProvider();
    }

    private sealed class WorldStreamingSettingsProvider : ISettingsProvider
    {
        private static readonly IReadOnlyList<SettingOption> Options =
            new SettingOption[]
            {
                new SettingOption("automatic", "自动（推荐）"),
                new SettingOption("smooth", "流畅优先（单后台线程）"),
                new SettingOption("throughput", "高吞吐（安全多线程）")
            };

        private readonly IReadOnlyList<ISettingsDropdown> dropdowns;

        public WorldStreamingSettingsProvider()
        {
            dropdowns = new ISettingsDropdown[]
            {
                new SettingsDropdown(
                    new SettingDescriptor(
                        ModeSettingKey,
                        "流送性能",
                        SettingControlType.Dropdown,
                        "world",
                        order: 0),
                    Options,
                    () => (int)Mode,
                    TrySetMode)
            };
        }

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "区块流送";
        public int Order => 60;
        public IReadOnlyList<ISettingsToggle> ToggleSettings =>
            Array.Empty<ISettingsToggle>();
        public IReadOnlyList<ISettingsSlider> SliderSettings =>
            Array.Empty<ISettingsSlider>();
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => dropdowns;
        public IReadOnlyList<ISettingsSwitch> SwitchSettings =>
            Array.Empty<ISettingsSwitch>();

        public void ResetToDefaults() => SetMode(WorldStreamingPerformanceMode.Automatic);

        private static string TrySetMode(int index)
        {
            if (index < 0 || index >= Options.Count)
                return "区块流送模式无效。";

            SetMode((WorldStreamingPerformanceMode)index);
            return null;
        }
    }

    #endregion
}
