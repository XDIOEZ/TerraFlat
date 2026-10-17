using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;

/// <summary>显示页中的帧率控件只负责输入、选项和错误提示，数值由设置服务持有。</summary>
[DisallowMultipleComponent]
public sealed class FrameRateSettingsPanelController : MonoBehaviour, ISettingsPageLifecycle
{
    #region 控件绑定与生命周期

    public const string DropdownName = "帧率上限下拉列表";
    public const string InputName = "自定义帧数输入框";
    public const string StatusName = "帧率状态文本";
    private TMP_Dropdown dropdown;
    private TMP_InputField input;
    private TextMeshProUGUI status;
    private ISettingsDropdown limitSetting;
    private ISettingsSlider customSetting;
    private bool pageVisible;

    public static void Ensure(Transform pageRoot)
    {
        if (pageRoot.GetComponent<FrameRateSettingsPanelController>() == null)
            pageRoot.gameObject.AddComponent<FrameRateSettingsPanelController>().Initialize();
    }

    private void Initialize()
    {
        dropdown = Find<TMP_Dropdown>(DropdownName);
        input = Find<TMP_InputField>(InputName);
        status = Find<TextMeshProUGUI>(StatusName);
        ISettingsProvider provider = FrameRateUserSettings.SettingsProvider;
        limitSetting = provider.GetDropdown(FrameRateUserSettings.LimitSettingKey);
        customSetting = provider.GetSlider(FrameRateUserSettings.CustomSettingKey);
        if (dropdown == null || input == null || status == null)
        {
            Debug.LogError("[FrameRateSettings] 显示设置页缺少帧率控件。", this);
            return;
        }
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.characterLimit = 4;
        dropdown.onValueChanged.AddListener(OnPresetChanged);
        input.onEndEdit.AddListener(OnCustomEdited);
        FlatWorldLocalizationService.LanguageChanged += OnLanguageChanged;
        RefreshView();
    }

    private T Find<T>(string objectName) where T : Component
    {
        foreach (T component in GetComponentsInChildren<T>(true))
            if (component.name == objectName) return component;
        return null;
    }

    public void OnSettingsPageShown()
    {
        pageVisible = true;
        RefreshView();
    }
    public void OnSettingsPageHidden()
    {
        pageVisible = false;
        // 切页时丢弃尚未确认的输入，避免重新进入后误提交旧草稿。
        RefreshView();
    }
    private void OnLanguageChanged(string _) => RefreshView();
    private void OnDestroy()
    {
        if (dropdown != null) dropdown.onValueChanged.RemoveListener(OnPresetChanged);
        if (input != null) input.onEndEdit.RemoveListener(OnCustomEdited);
        FlatWorldLocalizationService.LanguageChanged -= OnLanguageChanged;
    }

    #endregion

    #region 读写与显示

    private void OnPresetChanged(int index)
    {
        limitSetting.TrySetSelectedIndex(index, out _);
        RefreshView();
    }

    private void OnCustomEdited(string text)
    {
        if (!pageVisible || limitSetting.SelectedIndex != FrameRateUserSettings.CustomOptionIndex) return;
        if (!FrameRateUserSettings.TryParseCustomFrameRate(text, out int rate))
        {
            RefreshView();
            status.text = FlatWorldLocalizationService.GetUiText("请输入 1～1000 之间的整数帧数。");
            return;
        }
        customSetting.SetValue(rate);
        RefreshView();
    }

    private void RefreshView()
    {
        if (dropdown == null || input == null || status == null || limitSetting == null) return;
        var labels = new List<string>();
        foreach (SettingOption option in limitSetting.Options)
            labels.Add(FlatWorldLocalizationService.GetUiText(option.DisplayName));
        dropdown.ClearOptions();
        dropdown.AddOptions(labels);
        dropdown.SetValueWithoutNotify(limitSetting.SelectedIndex);
        dropdown.RefreshShownValue();
        input.interactable = limitSetting.SelectedIndex == FrameRateUserSettings.CustomOptionIndex;
        input.SetTextWithoutNotify(FrameRateUserSettings.CustomFrameRate.ToString());
        int target = FrameRateUserSettings.ResolveTargetFrameRate(limitSetting.SelectedIndex, FrameRateUserSettings.CustomFrameRate);
        status.text = target < 0
            ? FlatWorldLocalizationService.GetUiText("当前帧数上限：不限制")
            : FlatWorldLocalizationService.GetUiFormat("当前帧数上限：{0} FPS", target);
    }

    #endregion
}
