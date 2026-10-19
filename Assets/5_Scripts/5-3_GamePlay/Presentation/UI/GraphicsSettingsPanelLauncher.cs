using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;

/// <summary>主菜单与游戏内共用画质页，整体预设和细项分别保存与回填。</summary>
[DisallowMultipleComponent]
public sealed class GraphicsSettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    #region 控件与初始化

    private TMP_Dropdown presetDropdown;
    private TMP_Dropdown effectsDropdown;
    private ISettingsDropdown presetSetting;
    private ISettingsDropdown effectsSetting;
    private bool initialized;

    public static GraphicsSettingsPanelLauncher Ensure(Transform pageRoot)
    {
        if (pageRoot == null) return null;
        var launcher = pageRoot.GetComponent<GraphicsSettingsPanelLauncher>();
        if (launcher == null) launcher = pageRoot.gameObject.AddComponent<GraphicsSettingsPanelLauncher>();
        launcher.Initialize();
        return launcher;
    }

    private void Initialize()
    {
        if (initialized) return;
        presetSetting = GraphicsUserSettings.SettingsProvider.GetDropdown(GraphicsUserSettings.PresetSettingKey);
        effectsSetting = ScreenPostProcessSettings.SettingsProvider.GetDropdown(ScreenPostProcessSettings.QualitySettingKey);
        foreach (TMP_Dropdown dropdown in GetComponentsInChildren<TMP_Dropdown>(true))
        {
            if (dropdown.name == GameManager.MainMenuSettingsQualityPresetKey) presetDropdown = dropdown;
            if (dropdown.name == GameManager.MainMenuSettingsEffectsQualityKey) effectsDropdown = dropdown;
        }
        if (presetDropdown == null || effectsDropdown == null || presetSetting == null || effectsSetting == null)
        {
            Debug.LogError("[GraphicsSettingsPanelLauncher] 画质页缺少预设或特效质量下拉列表。", this);
            return;
        }
        initialized = true;
        presetDropdown.onValueChanged.AddListener(SelectPreset);
        effectsDropdown.onValueChanged.AddListener(SelectEffects);
        RefreshOptions();
    }

    #endregion

    #region 选择与刷新

    private void SelectPreset(int index)
    {
        if (presetSetting.TrySetSelectedIndex(index, out string error))
            GraphicsQualityPresets.ApplyVisualSettings(GraphicsUserSettings.Preset);
        else
            Debug.LogWarning(error, this);
        RefreshView();
    }

    private void SelectEffects(int index)
    {
        if (!effectsSetting.TrySetSelectedIndex(index, out string error))
            Debug.LogWarning(error, this);
        RefreshView();
    }

    private void RefreshOptions()
    {
        if (!initialized) return;
        FillOptions(presetDropdown, presetSetting.Options);
        FillOptions(effectsDropdown, effectsSetting.Options);
        RefreshView();
    }

    private static void FillOptions(TMP_Dropdown dropdown, IReadOnlyList<SettingOption> options)
    {
        var labels = new List<string>(options.Count);
        foreach (SettingOption option in options)
            labels.Add(FlatWorldLocalizationService.GetUiText(option.DisplayName));
        dropdown.ClearOptions();
        dropdown.AddOptions(labels);
    }

    private void RefreshView()
    {
        if (!initialized) return;
        presetDropdown.SetValueWithoutNotify(presetSetting.SelectedIndex);
        effectsDropdown.SetValueWithoutNotify(effectsSetting.SelectedIndex);
        presetDropdown.RefreshShownValue();
        effectsDropdown.RefreshShownValue();
    }

    private void OnLanguageChanged(string _) => RefreshOptions();

    #endregion

    #region 生命周期

    private void OnEnable()
    {
        GraphicsUserSettings.Changed += RefreshView;
        ScreenPostProcessSettings.Changed += RefreshView;
        FlatWorldLocalizationService.LanguageChanged += OnLanguageChanged;
        RefreshOptions();
    }

    private void OnDisable()
    {
        GraphicsUserSettings.Changed -= RefreshView;
        ScreenPostProcessSettings.Changed -= RefreshView;
        FlatWorldLocalizationService.LanguageChanged -= OnLanguageChanged;
    }

    void ISettingsPageLifecycle.OnSettingsPageShown()
    {
        Initialize();
        RefreshOptions();
    }

    void ISettingsPageLifecycle.OnSettingsPageHidden() { }

    private void OnDestroy()
    {
        if (presetDropdown != null) presetDropdown.onValueChanged.RemoveListener(SelectPreset);
        if (effectsDropdown != null) effectsDropdown.onValueChanged.RemoveListener(SelectEffects);
    }

    #endregion
}
