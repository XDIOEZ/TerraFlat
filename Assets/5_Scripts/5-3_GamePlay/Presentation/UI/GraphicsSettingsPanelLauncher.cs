using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>主菜单与游戏内共用画质页，整体预设和细项分别保存与回填。</summary>
[DisallowMultipleComponent]
public sealed class GraphicsSettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    #region 控件与初始化

    private TMP_Dropdown presetDropdown;
    private TMP_Dropdown effectsDropdown;
    private TMP_Dropdown styleDropdown;
    private Button resetButton;
    private ISettingsDropdown presetSetting;
    private ISettingsDropdown effectsSetting;
    private ISettingsDropdown styleSetting;
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
        styleSetting = ScreenPostProcessSettings.SettingsProvider.GetDropdown(ScreenPostProcessSettings.StyleSettingKey);
        foreach (TMP_Dropdown dropdown in GetComponentsInChildren<TMP_Dropdown>(true))
        {
            if (dropdown.name == GameManager.MainMenuSettingsQualityPresetKey) presetDropdown = dropdown;
            if (dropdown.name == GameManager.MainMenuSettingsEffectsQualityKey) effectsDropdown = dropdown;
            if (dropdown.name == GameManager.MainMenuSettingsPostProcessStyleKey) styleDropdown = dropdown;
        }
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            if (button.name == "恢复默认按钮")
                resetButton = button;
        }
        // 已发布的两份设置 Prefab 没有风格行，沿用正式下拉控件行的布局而不拼临时控件。
        if (styleDropdown == null && effectsDropdown != null)
        {
            Transform sourceRow = effectsDropdown.transform.parent;
            GameObject styleRow = Instantiate(sourceRow.gameObject, sourceRow.parent);
            styleRow.name = "画面风格行";
            styleRow.transform.SetSiblingIndex(sourceRow.GetSiblingIndex() + 1);
            styleDropdown = styleRow.GetComponentInChildren<TMP_Dropdown>(true);
            if (styleDropdown != null)
                styleDropdown.name = GameManager.MainMenuSettingsPostProcessStyleKey;
            foreach (TextMeshProUGUI label in styleRow.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (label.transform.parent != styleRow.transform)
                    continue;
                label.gameObject.name = "画面风格标签";
                LocalizedTextRuntime.SetText(label, "画面风格");
                break;
            }
        }
        if (presetDropdown == null || effectsDropdown == null || styleDropdown == null ||
            presetSetting == null || effectsSetting == null || styleSetting == null)
        {
            Debug.LogError("[GraphicsSettingsPanelLauncher] 画质页缺少预设、特效质量或画面风格下拉列表。", this);
            return;
        }
        initialized = true;
        presetDropdown.onValueChanged.AddListener(SelectPreset);
        effectsDropdown.onValueChanged.AddListener(SelectEffects);
        styleDropdown.onValueChanged.AddListener(SelectStyle);
        // 复用同一个重置按钮：视觉控制器重置细项，这里只负责预设与后处理。
        resetButton?.onClick.AddListener(ResetGraphicsDefaults);
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

    private void SelectStyle(int index)
    {
        if (!styleSetting.TrySetSelectedIndex(index, out string error))
            Debug.LogWarning(error, this);
        RefreshView();
    }

    private void ResetGraphicsDefaults()
    {
        GraphicsUserSettings.SettingsProvider.ResetToDefaults();
        ScreenPostProcessSettings.SettingsProvider.ResetToDefaults();
    }

    private void RefreshOptions()
    {
        if (!initialized) return;
        FillOptions(presetDropdown, presetSetting.Options);
        FillOptions(effectsDropdown, effectsSetting.Options);
        FillOptions(styleDropdown, styleSetting.Options);
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
        styleDropdown.SetValueWithoutNotify(styleSetting.SelectedIndex);
        presetDropdown.RefreshShownValue();
        effectsDropdown.RefreshShownValue();
        styleDropdown.RefreshShownValue();
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
        if (styleDropdown != null) styleDropdown.onValueChanged.RemoveListener(SelectStyle);
        resetButton?.onClick.RemoveListener(ResetGraphicsDefaults);
    }

    #endregion
}
