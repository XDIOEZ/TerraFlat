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
    public const string TickRateDropdownName = "模拟频率下拉列表";
    public const string TickRateStatusName = "模拟频率状态文本";
    private TMP_Dropdown dropdown;
    private TMP_Dropdown tickRateDropdown;
    private TMP_InputField input;
    private TextMeshProUGUI status;
    private TextMeshProUGUI tickRateStatus;
    private TextMeshProUGUI tickRateLabel;
    private ISettingsDropdown limitSetting;
    private ISettingsDropdown tickRateSetting;
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
        EnsureTickRateControlsForLegacyPrefab();
        tickRateDropdown = Find<TMP_Dropdown>(TickRateDropdownName);
        tickRateStatus = Find<TextMeshProUGUI>(TickRateStatusName);
        tickRateLabel = Find<TextMeshProUGUI>("模拟频率标签");
        ISettingsProvider provider = FrameRateUserSettings.SettingsProvider;
        limitSetting = provider.GetDropdown(FrameRateUserSettings.LimitSettingKey);
        customSetting = provider.GetSlider(FrameRateUserSettings.CustomSettingKey);
        tickRateSetting = GameplayTickRateUserSettings.SettingsProvider.GetDropdown(GameplayTickRateUserSettings.TickRateSettingKey);
        if (dropdown == null || input == null || status == null || tickRateDropdown == null || tickRateStatus == null)
        {
            Debug.LogError("[FrameRateSettings] 显示设置页缺少帧率或 TPS 控件。", this);
            return;
        }
        input.contentType = TMP_InputField.ContentType.IntegerNumber;
        input.characterLimit = 4;
        dropdown.onValueChanged.AddListener(OnPresetChanged);
        tickRateDropdown.onValueChanged.AddListener(OnTickRateChanged);
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

    /// <summary>旧 Prefab 复用已存在的下拉行，避免玩家升级后缺少 TPS 设置。</summary>
    private void EnsureTickRateControlsForLegacyPrefab()
    {
        if (Find<TMP_Dropdown>(TickRateDropdownName) != null || dropdown == null || status == null) return;
        Transform sourceRow = dropdown.transform.parent;
        GameObject tickRow = Instantiate(sourceRow.gameObject, sourceRow.parent, false);
        tickRow.name = "模拟频率行";
        tickRow.transform.SetSiblingIndex(sourceRow.GetSiblingIndex() + 1);
        TMP_Dropdown newDropdown = tickRow.GetComponentInChildren<TMP_Dropdown>(true);
        if (newDropdown != null) newDropdown.name = TickRateDropdownName;
        foreach (TextMeshProUGUI label in tickRow.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label.name != "帧数上限标签") continue;
            label.name = "模拟频率标签";
            if (label.TryGetComponent(out LocalizedTextBinder binder)) Destroy(binder);
            break;
        }

        GameObject tickStatus = Instantiate(status.gameObject, status.transform.parent, false);
        tickStatus.name = TickRateStatusName;
        tickStatus.transform.SetSiblingIndex(tickRow.transform.GetSiblingIndex() + 1);
        if (tickStatus.TryGetComponent(out LocalizedTextBinder statusBinder)) Destroy(statusBinder);
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
        if (tickRateDropdown != null) tickRateDropdown.onValueChanged.RemoveListener(OnTickRateChanged);
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

    private void OnTickRateChanged(int index)
    {
        tickRateSetting.TrySetSelectedIndex(index, out _);
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

        if (tickRateDropdown == null || tickRateSetting == null || tickRateStatus == null) return;
        var tickRateLabels = new List<string>();
        foreach (SettingOption option in tickRateSetting.Options)
            tickRateLabels.Add(FlatWorldLocalizationService.GetUiText(option.DisplayName));
        tickRateDropdown.ClearOptions();
        tickRateDropdown.AddOptions(tickRateLabels);
        tickRateDropdown.SetValueWithoutNotify(tickRateSetting.SelectedIndex);
        tickRateDropdown.RefreshShownValue();
        if (tickRateLabel != null)
            tickRateLabel.text = FlatWorldLocalizationService.GetUiText("模拟频率");
        tickRateStatus.text = GameplayTickRateUserSettings.EffectiveTicksPerSecond != GameplayTickRateUserSettings.SelectedTicksPerSecond
            ? FlatWorldLocalizationService.GetUiText("联机固定 30 TPS，单机时应用所选频率。")
            : FlatWorldLocalizationService.GetUiFormat("当前模拟频率：{0} TPS", GameplayTickRateUserSettings.EffectiveTicksPerSecond);
    }

    #endregion
}
