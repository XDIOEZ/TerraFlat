using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>保存玩家的交互目标选择方式；默认允许在光标未命中时选择交互半径内的合适目标。</summary>
public static class InteractionUserSettings
{
    #region 设置键与状态

    public const string SettingsProviderId = "interaction";
    public const string PreciseInteractionSettingKey = "interaction.preciseTargeting";
    public const bool DefaultPreciseInteraction = false;

    private const string PreciseInteractionPreferenceKey = "FlatWorld.Interaction.PreciseTargeting";
    private static readonly ISettingsProvider settingsProvider = new InteractionSettingsProvider();
    private static bool initialized;
    private static bool preciseInteraction = DefaultPreciseInteraction;

    /// <summary>开启后，交互键只选择光标实际命中的范围内目标。</summary>
    public static bool PreciseInteraction
    {
        get
        {
            EnsureInitialized();
            return preciseInteraction;
        }
    }

    /// <summary>供设置页绑定的布尔选项。</summary>
    public static ISettingsProvider SettingsProvider => RegisterSettingsProvider();

    #endregion

    #region 持久化与注册

    /// <summary>立即保存交互目标选择方式。</summary>
    public static void SetPreciseInteraction(bool enabled)
    {
        EnsureInitialized();
        preciseInteraction = enabled;
        PlayerPrefs.SetInt(PreciseInteractionPreferenceKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>恢复默认的邻近目标选择方式。</summary>
    public static void ResetToDefault() => SetPreciseInteraction(DefaultPreciseInteraction);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeRegistration()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        initialized = false;
        preciseInteraction = DefaultPreciseInteraction;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterOnLoad() => RegisterSettingsProvider();

    /// <summary>让主菜单和世界内的设置页共享同一设置契约。</summary>
    private static ISettingsProvider RegisterSettingsProvider()
    {
        SettingsProviderRegistry.Register(settingsProvider);
        return settingsProvider;
    }

    /// <summary>首次访问时读取本机交互偏好，后续每帧查询只访问缓存。</summary>
    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        preciseInteraction = PlayerPrefs.GetInt(
            PreciseInteractionPreferenceKey,
            DefaultPreciseInteraction ? 1 : 0) != 0;
        initialized = true;
    }

    #endregion

    #region 设置契约

    /// <summary>把交互目标选择偏好接入通用设置保存与恢复流程。</summary>
    private sealed class InteractionSettingsProvider : ISettingsProvider
    {
        private readonly IReadOnlyList<ISettingsToggle> toggles = new ISettingsToggle[]
        {
            new SettingsToggle(
                new SettingDescriptor(
                    PreciseInteractionSettingKey,
                    "精确交互",
                    SettingControlType.Toggle,
                    "controls",
                    order: 0),
                () => PreciseInteraction,
                SetPreciseInteraction)
        };

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "操作";
        public int Order => 15;
        public IReadOnlyList<ISettingsToggle> ToggleSettings => toggles;
        public IReadOnlyList<ISettingsSlider> SliderSettings => Array.Empty<ISettingsSlider>();
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();
        public void ResetToDefaults() => ResetToDefault();
    }

    #endregion
}
