using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;

/// <summary>把主菜单与游戏内共用的语言页绑定到本地化设置提供者。</summary>
[DisallowMultipleComponent]
public sealed class LanguageSettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    #region 控件绑定

    private TMP_Dropdown languageDropdown;
    private TextMeshProUGUI statusText;
    private ISettingsDropdown languageSetting;
    private bool initialized;

    /// <summary>复用指定语言页上的现有控件并接入设置 Provider。</summary>
    public static LanguageSettingsPanelLauncher Ensure(Transform pageRoot)
    {
        if (pageRoot == null)
            return null;

        LanguageSettingsPanelLauncher launcher =
            pageRoot.GetComponent<LanguageSettingsPanelLauncher>();
        if (launcher == null)
            launcher = pageRoot.gameObject.AddComponent<LanguageSettingsPanelLauncher>();
        launcher.Initialize();
        return launcher;
    }

    private void Awake() => Initialize();

    private void Initialize()
    {
        if (initialized)
            return;

        ISettingsProvider provider = FlatWorldLocalizationService.SettingsProvider;
        languageSetting = provider.GetDropdown(FlatWorldLocalizationService.LocaleSettingKey);
        languageDropdown = FindComponent<TMP_Dropdown>(GameManager.MainMenuSettingsLanguageDropdownKey);
        statusText = FindComponent<TextMeshProUGUI>(GameManager.MainMenuSettingsLanguageStatusTextKey);
        initialized = true;

        if (languageSetting == null || languageDropdown == null || statusText == null)
        {
            Debug.LogError("[LanguageSettingsPanelLauncher] 语言页控件或设置 Provider 缺失。", this);
            return;
        }

        FlatWorldLocalizationService.Initialize();
        languageDropdown.onValueChanged.AddListener(OnLanguageSelected);
        FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
        RefreshView();
    }

    public void OnSettingsPageShown()
    {
        Initialize();
        RefreshView();
    }

    public void OnSettingsPageHidden() { }

    private void OnDestroy()
    {
        if (languageDropdown != null)
            languageDropdown.onValueChanged.RemoveListener(OnLanguageSelected);
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
    }

    #endregion

    #region 语言设置

    /// <summary>按当前 Locale 更新选项和状态文字。</summary>
    public void RefreshView()
    {
        if (languageDropdown == null || languageSetting == null || statusText == null)
            return;

        IReadOnlyList<SettingOption> options = languageSetting.Options;
        var labels = new List<string>(options?.Count ?? 0);
        if (options != null)
        {
            for (int index = 0; index < options.Count; index++)
                labels.Add(FlatWorldLocalizationService.GetUiText(options[index]?.DisplayName ?? string.Empty));
        }

        languageDropdown.ClearOptions();
        languageDropdown.AddOptions(labels);
        int selectedIndex = Mathf.Clamp(languageSetting.SelectedIndex, 0, Mathf.Max(0, labels.Count - 1));
        languageDropdown.SetValueWithoutNotify(selectedIndex);
        languageDropdown.RefreshShownValue();
        statusText.text = selectedIndex == 0
            ? FlatWorldLocalizationService.GetUiText("当前语言：简体中文")
            : FlatWorldLocalizationService.GetUiText("当前语言：English");
    }

    private void OnLanguageSelected(int selectedIndex)
    {
        if (languageSetting == null)
        {
            RefreshView();
            return;
        }

        if (!languageSetting.TrySetSelectedIndex(selectedIndex, out string error))
        {
            RefreshView();
            if (!string.IsNullOrWhiteSpace(error) && statusText != null)
                statusText.text = FlatWorldLocalizationService.GetUiText(error);
            return;
        }

        RefreshView();
    }

    private void HandleLanguageChanged(string _)
    {
        RefreshView();
    }

    private T FindComponent<T>(string objectName) where T : Component
    {
        foreach (T component in GetComponentsInChildren<T>(true))
            if (component.name == objectName)
                return component;
        return null;
    }

    #endregion
}
