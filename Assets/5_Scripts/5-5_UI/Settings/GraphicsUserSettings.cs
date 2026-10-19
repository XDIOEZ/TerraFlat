using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>
/// 主菜单六档画质预设映射到项目 QualitySettings 中同名档位，
/// 通过 PlayerPrefs 保存并在运行开始时应用，不写入世界存档。
/// </summary>
public enum GraphicsPreset
{
    High = 0,
    Medium = 1,
    Low = 2,
    VeryLow = 3,
    VeryHigh = 4,
    Ultra = 5
}

/// <summary>
/// 统一提供画质预设的读取、保存和 Unity QualitySettings 应用接口。
/// </summary>
public static class GraphicsUserSettings
{
    #region 键与默认值

    private const string PresetKey = "FlatWorld.Graphics.Preset";

    // 下拉顺序独立于持久化枚举值，避免改变本机已保存的档位。
    private static readonly GraphicsPreset[] OrderedPresets =
    {
        GraphicsPreset.VeryLow,
        GraphicsPreset.Low,
        GraphicsPreset.Medium,
        GraphicsPreset.High,
        GraphicsPreset.VeryHigh,
        GraphicsPreset.Ultra
    };

    public static int PresetCount => OrderedPresets.Length;

    public static GraphicsPreset DefaultPreset =>
        (GraphicsPreset)WorldRenderingConfigCatalog.Default.preferences.graphicsPreset;

    public const string SettingsProviderId = "graphics";
    public const string PresetSettingKey = "graphics.preset";

    #endregion
    #region 缓存与事件

    private static bool initialized;
    private static GraphicsPreset cachedPreset;

    /// <summary>画质预设实际改变后触发。</summary>
    public static event Action Changed;

    private static readonly ISettingsProvider settingsProvider =
        CreateSettingsProvider();

    /// <summary>供主菜单设置使用的画质下拉列表契约。</summary>
    public static ISettingsProvider SettingsProvider => RegisterSettingsProvider();

    public static GraphicsPreset Preset
    {
        get
        {
            EnsureInitialized();
            return cachedPreset;
        }
    }

    /// <summary>供主菜单下拉列表使用的从低到高索引。</summary>
    public static int PresetIndex => GetIndexForPreset(Preset);

    #endregion
    #region 写入入口

    /// <summary>按从低到高索引保存并立即应用画质。</summary>
    public static GraphicsPreset SetPresetIndex(int index)
    {
        GraphicsPreset sanitized = GetPresetForIndex(index);
        EnsureInitialized();
        bool changed = cachedPreset != sanitized;
        cachedPreset = sanitized;
        PlayerPrefs.SetInt(PresetKey, (int)cachedPreset);
        PlayerPrefs.Save();
        ApplyCurrentSettings();

        if (changed)
            Changed?.Invoke();
        return cachedPreset;
    }

    /// <summary>将当前保存的预设再次应用到 Unity 质量系统。</summary>
    public static void ApplyCurrentSettings()
    {
        EnsureInitialized();
        int qualityLevel = FindQualityLevel(cachedPreset);
        if (qualityLevel >= 0 && qualityLevel < QualitySettings.names.Length)
            QualitySettings.SetQualityLevel(qualityLevel, true);
        FrameRateUserSettings.ApplyCurrentSettings();
    }

    /// <summary>恢复高画质默认值并立即应用。</summary>
    public static void ResetToDefaults()
    {
        EnsureInitialized();
        bool changed = cachedPreset != DefaultPreset;
        cachedPreset = DefaultPreset;
        PlayerPrefs.SetInt(PresetKey, (int)cachedPreset);
        PlayerPrefs.Save();
        ApplyCurrentSettings();

        if (changed)
            Changed?.Invoke();
    }

    #endregion
    #region 初始化与质量档位映射

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySavedSettingsBeforeSceneLoad()
    {
        EnsureInitialized();
        ApplyCurrentSettings();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        initialized = false;
        cachedPreset = default;
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
        return new GraphicsSettingsProvider();
    }

    private sealed class GraphicsSettingsProvider : ISettingsProvider
    {
        private static readonly IReadOnlyList<SettingOption> Options =
            new SettingOption[]
            {
                new SettingOption("very-low", "极低"),
                new SettingOption("low", "低"),
                new SettingOption("medium", "中"),
                new SettingOption("high", "高"),
                new SettingOption("very-high", "极高"),
                new SettingOption("ultra", "极致")
            };

        private readonly IReadOnlyList<ISettingsDropdown> dropdowns;

        public GraphicsSettingsProvider()
        {
            dropdowns = new ISettingsDropdown[]
            {
                new SettingsDropdown(
                    new SettingDescriptor(
                        PresetSettingKey,
                        "画质预设",
                        SettingControlType.Dropdown,
                        "graphics",
                        order: 0),
                    Options,
                    () => PresetIndex,
                    index =>
                    {
                        if (index < 0 || index >= PresetCount)
                            return "画质档位无效。";
                        SetPresetIndex(index);
                        return null;
                    })
            };
        }

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "画质";
        public int Order => 40;
        public IReadOnlyList<ISettingsToggle> ToggleSettings =>
            Array.Empty<ISettingsToggle>();
        public IReadOnlyList<ISettingsSlider> SliderSettings =>
            Array.Empty<ISettingsSlider>();
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => dropdowns;
        public IReadOnlyList<ISettingsSwitch> SwitchSettings =>
            Array.Empty<ISettingsSwitch>();

        public void ResetToDefaults() => GraphicsUserSettings.ResetToDefaults();
    }

    #endregion

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        cachedPreset = Sanitize(PlayerPrefs.GetInt(PresetKey, (int)DefaultPreset));
        initialized = true;
    }

    private static GraphicsPreset Sanitize(int value)
    {
        return value >= (int)GraphicsPreset.High && value <= (int)GraphicsPreset.Ultra
            ? (GraphicsPreset)value
            : DefaultPreset;
    }

    public static GraphicsPreset GetPresetForIndex(int index)
    {
        if (index < 0 || index >= OrderedPresets.Length)
            throw new ArgumentOutOfRangeException(nameof(index));
        return OrderedPresets[index];
    }

    public static int GetIndexForPreset(GraphicsPreset preset)
    {
        for (int index = 0; index < OrderedPresets.Length; index++)
        {
            if (OrderedPresets[index] == preset)
                return index;
        }

        throw new ArgumentOutOfRangeException(nameof(preset));
    }

    public static string GetQualityNameForPreset(GraphicsPreset preset)
    {
        switch (preset)
        {
            case GraphicsPreset.VeryLow: return "Very Low";
            case GraphicsPreset.Low: return "Low";
            case GraphicsPreset.Medium: return "Medium";
            case GraphicsPreset.High: return "High";
            case GraphicsPreset.VeryHigh: return "Very High";
            case GraphicsPreset.Ultra: return "Ultra";
            default: throw new ArgumentOutOfRangeException(nameof(preset));
        }
    }

    private static int FindQualityLevel(GraphicsPreset preset)
    {
        string[] names = QualitySettings.names;
        if (names == null || names.Length == 0)
            return -1;

        string targetName = GetQualityNameForPreset(preset);

        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], targetName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return Mathf.Clamp(GetIndexForPreset(preset), 0, names.Length - 1);
    }

    #endregion
}
