using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>保存本机新建世界的默认参数，已有世界继续使用自身存档数据。</summary>
public static class NewWorldUserSettings
{
    #region 设置键与状态

    public const string SettingsProviderId = "newWorld";
    public const string ChunkWidthSettingKey = "newWorld.defaultChunkWidth";
    public const string ChunkHeightSettingKey = "newWorld.defaultChunkHeight";
    public const int DefaultChunkDimension = 64;

    private const string ChunkWidthPreferenceKey = "FlatWorld.NewWorld.DefaultChunkWidth";
    private const string ChunkHeightPreferenceKey = "FlatWorld.NewWorld.DefaultChunkHeight";
    private static readonly ISettingsProvider settingsProvider = new NewWorldSettingsProvider();
    private static Vector2Int defaultChunkSize = new Vector2Int(DefaultChunkDimension, DefaultChunkDimension);
    private static bool initialized;

    public static Vector2Int DefaultChunkSize
    {
        get
        {
            EnsureInitialized();
            return defaultChunkSize;
        }
    }

    public static ISettingsProvider SettingsProvider => RegisterSettingsProvider();

    #endregion

    #region 持久化与注册

    /// <summary>单边修改只写入新世界默认值，不触碰当前星球。</summary>
    private static void SetChunkDimension(bool width, float value)
    {
        EnsureInitialized();
        int dimension = Mathf.Clamp(Mathf.RoundToInt(value), PlanetData.MinChunkDimension, PlanetData.MaxChunkDimension);
        if (width)
            defaultChunkSize.x = dimension;
        else
            defaultChunkSize.y = dimension;
        PlayerPrefs.SetInt(width ? ChunkWidthPreferenceKey : ChunkHeightPreferenceKey, dimension);
        PlayerPrefs.Save();
    }

    /// <summary>首次读取时校正损坏的偏好值，后续访问直接读取缓存。</summary>
    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        int width = PlayerPrefs.GetInt(ChunkWidthPreferenceKey, DefaultChunkDimension);
        int height = PlayerPrefs.GetInt(ChunkHeightPreferenceKey, DefaultChunkDimension);
        defaultChunkSize = new Vector2Int(
            PlanetData.IsValidChunkDimension(width) ? width : DefaultChunkDimension,
            PlanetData.IsValidChunkDimension(height) ? height : DefaultChunkDimension);
        initialized = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeRegistration()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        initialized = false;
        defaultChunkSize = new Vector2Int(DefaultChunkDimension, DefaultChunkDimension);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterOnLoad() => RegisterSettingsProvider();

    private static ISettingsProvider RegisterSettingsProvider()
    {
        SettingsProviderRegistry.Register(settingsProvider);
        return settingsProvider;
    }

    #endregion

    #region 设置契约

    /// <summary>通过滑块契约参与通用设置保存、取消与恢复默认流程。</summary>
    private sealed class NewWorldSettingsProvider : ISettingsProvider
    {
        private readonly IReadOnlyList<ISettingsSlider> sliders = new ISettingsSlider[]
        {
            new SettingsSlider(
                new SettingDescriptor(ChunkWidthSettingKey, "默认区块宽度", SettingControlType.Slider, "newWorld", order: 0),
                PlanetData.MinChunkDimension, PlanetData.MaxChunkDimension, 1f,
                () => DefaultChunkSize.x, value => SetChunkDimension(true, value)),
            new SettingsSlider(
                new SettingDescriptor(ChunkHeightSettingKey, "默认区块高度", SettingControlType.Slider, "newWorld", order: 1),
                PlanetData.MinChunkDimension, PlanetData.MaxChunkDimension, 1f,
                () => DefaultChunkSize.y, value => SetChunkDimension(false, value))
        };

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "游戏设置";
        public int Order => 10;
        public IReadOnlyList<ISettingsToggle> ToggleSettings => Array.Empty<ISettingsToggle>();
        public IReadOnlyList<ISettingsSlider> SliderSettings => sliders;
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();

        public void ResetToDefaults()
        {
            SetChunkDimension(true, DefaultChunkDimension);
            SetChunkDimension(false, DefaultChunkDimension);
        }
    }

    #endregion
}
