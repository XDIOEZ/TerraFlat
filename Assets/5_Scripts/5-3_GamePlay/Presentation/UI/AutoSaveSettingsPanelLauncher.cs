// AI-Context: 设置主面板内嵌自动保存页；预设与自定义分钟数仅在点击应用时提交。
using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>绑定内嵌自动保存页，并在页面重进时丢弃未应用草稿。</summary>
[DisallowMultipleComponent]
public sealed class AutoSaveSettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    private const int CustomOptionIndex = 6;
    private static readonly int[] PresetMinutes = { 0, 1, 5, 10, 15, 30, -1 };

    private SettingsActionListPagination pagination;
    private TMP_Dropdown intervalDropdown;
    private TMP_InputField intervalInput;
    private TextMeshProUGUI statusText;
    private Button cancelButton;
    private Button applyButton;
    private ISettingsDropdown intervalSetting;
    private bool initialized;

    /// <summary>在指定内嵌页面根节点上复用或挂载自动保存控制器。</summary>
    public static AutoSaveSettingsPanelLauncher Ensure(
        Transform pageRoot,
        SettingsActionListPagination pagination)
    {
        if (pageRoot == null)
            return null;

        AutoSaveSettingsPanelLauncher launcher =
            pageRoot.GetComponent<AutoSaveSettingsPanelLauncher>();
        if (launcher == null)
            launcher = pageRoot.gameObject.AddComponent<AutoSaveSettingsPanelLauncher>();
        launcher.Initialize(pagination);
        return launcher;
    }

    /// <summary>解析页面局部控件并绑定自动保存设置 Provider。</summary>
    private void Initialize(SettingsActionListPagination ownerPagination)
    {
        pagination = ownerPagination;
        if (initialized)
            return;

        intervalSetting = AutoSavePreferences.SettingsProvider.GetDropdown(
            AutoSavePreferences.IntervalSettingKey);
        intervalDropdown = FindComponent<TMP_Dropdown>(transform, "自动保存间隔下拉列表");
        intervalInput = FindComponent<TMP_InputField>(transform, "自动保存间隔输入框");
        statusText = FindComponent<TextMeshProUGUI>(transform, "状态文本");
        cancelButton = FindComponent<Button>(transform, "取消按钮");
        applyButton = FindComponent<Button>(transform, "应用按钮");

        if (intervalDropdown != null)
        {
            intervalDropdown.ClearOptions();
            intervalDropdown.AddOptions(BuildPresetLabels(intervalSetting?.Options));
            intervalDropdown.onValueChanged.AddListener(OnPresetChanged);
        }

        cancelButton?.onClick.AddListener(Cancel);
        applyButton?.onClick.AddListener(Apply);
        initialized = true;

        if (intervalDropdown == null || intervalInput == null || statusText == null ||
            cancelButton == null || applyButton == null || intervalSetting == null)
        {
            Debug.LogError(
                "[AutoSaveSettingsPanelLauncher] 内嵌自动保存页控件命名契约不完整。",
                this);
        }
    }

    /// <summary>从当前已生效值重建下拉、输入框与状态文本。</summary>
    private void RefreshControls()
    {
        if (intervalDropdown == null || intervalInput == null)
            return;

        int selectedIndex = intervalSetting != null
            ? intervalSetting.SelectedIndex
            : ResolveCurrentOptionIndex();
        intervalDropdown.SetValueWithoutNotify(selectedIndex);
        intervalInput.SetTextWithoutNotify(AutoSavePreferences.IntervalMinutes.ToString());
        intervalInput.interactable = selectedIndex == CustomOptionIndex;
        SetCurrentStatus();
    }

    /// <summary>把当前自动保存状态解析为预设下拉索引。</summary>
    private int ResolveCurrentOptionIndex()
    {
        if (!AutoSavePreferences.Enabled)
            return 0;

        int currentMinutes = AutoSavePreferences.IntervalMinutes;
        for (int index = 1; index < PresetMinutes.Length - 1; index++)
        {
            if (PresetMinutes[index] == currentMinutes)
                return index;
        }

        return CustomOptionIndex;
    }

    /// <summary>切换预设时更新自定义输入可用性和草稿提示。</summary>
    private void OnPresetChanged(int selectedIndex)
    {
        bool custom = selectedIndex == CustomOptionIndex;
        intervalInput.interactable = custom;

        if (selectedIndex > 0 && selectedIndex < CustomOptionIndex)
            intervalInput.SetTextWithoutNotify(PresetMinutes[selectedIndex].ToString());

        if (selectedIndex == 0)
        {
            SetStatus(
                FlatWorldLocalizationService.GetUiText(
                    "已选择：永远不自动保存。点击“应用”后生效。"),
                false);
        }
        else if (custom)
        {
            SetStatus(
                FlatWorldLocalizationService.GetUiText(
                    "请输入 1–1440 分钟，然后点击“应用”。"),
                false);
        }
        else
        {
            SetStatus(
                FlatWorldLocalizationService.GetUiFormat(
                    "已选择：每 {0} 分钟自动保存。",
                    PresetMinutes[selectedIndex]),
                false);
        }
    }

    /// <summary>校验并提交当前自动保存草稿，成功后留在本页。</summary>
    private void Apply()
    {
        int selectedIndex = intervalDropdown.value;
        if (selectedIndex == 0)
        {
            if (!TryApplyPreset(selectedIndex, out string error))
            {
                SetStatus(FlatWorldLocalizationService.GetUiText(error), true);
                return;
            }

            SetCurrentStatus();
            return;
        }

        int minutes;
        if (selectedIndex == CustomOptionIndex)
        {
            if (!int.TryParse(intervalInput.text, out minutes) ||
                minutes < AutoSavePreferences.MinIntervalMinutes ||
                minutes > AutoSavePreferences.MaxIntervalMinutes)
            {
                SetStatus(
                    FlatWorldLocalizationService.GetUiText(
                        "请输入 1–1440 之间的整数分钟数。"),
                    true);
                return;
            }
        }
        else
        {
            minutes = PresetMinutes[selectedIndex];
            if (!TryApplyPreset(selectedIndex, out string error))
            {
                SetStatus(FlatWorldLocalizationService.GetUiText(error), true);
                return;
            }
        }

        if (selectedIndex == CustomOptionIndex)
            AutoSavePreferences.Enable(minutes);
        intervalInput.SetTextWithoutNotify(minutes.ToString());
        SetCurrentStatus();
    }

    /// <summary>通过设置 Provider 提交一个预设索引。</summary>
    private bool TryApplyPreset(int selectedIndex, out string error)
    {
        if (intervalSetting == null)
        {
            error = "自动保存设置提供者尚未注册。";
            return false;
        }

        return intervalSetting.TrySetSelectedIndex(selectedIndex, out error);
    }

    /// <summary>显示当前已经生效的自动保存状态。</summary>
    private void SetCurrentStatus()
    {
        if (!AutoSavePreferences.Enabled)
        {
            SetStatus(
                FlatWorldLocalizationService.GetUiText("当前设置：永远不自动保存。"),
                false);
        }
        else
        {
            SetStatus(
                FlatWorldLocalizationService.GetUiFormat(
                    "当前设置：每 {0} 分钟自动保存。",
                    AutoSavePreferences.IntervalMinutes),
                false);
        }
    }

    /// <summary>把稳定选项元数据转换为本地化下拉标签。</summary>
    private static List<string> BuildPresetLabels(IReadOnlyList<SettingOption> options)
    {
        var labels = new List<string>(options?.Count ?? 0);
        if (options == null)
            return labels;

        for (int index = 0; index < options.Count; index++)
            labels.Add(FlatWorldLocalizationService.GetUiText(options[index]?.DisplayName));
        return labels;
    }

    /// <summary>刷新页内状态文本与错误颜色。</summary>
    private void SetStatus(string message, bool isError)
    {
        if (statusText == null)
            return;

        statusText.text = message;
        statusText.color = isError
            ? new Color(0.95f, 0.38f, 0.31f)
            : new Color(0.21f, 0.78f, 0.74f);
    }

    /// <summary>放弃当前视图草稿并返回世界设置入口页。</summary>
    private void Cancel()
    {
        pagination?.ShowWorldPage();
    }

    /// <summary>自动保存页显示时丢弃旧草稿并读取已生效值。</summary>
    public void OnSettingsPageShown()
    {
        RefreshControls();
    }

    /// <summary>自动保存页隐藏时不提交当前草稿。</summary>
    public void OnSettingsPageHidden()
    {
    }

    /// <summary>解除页面控件监听。</summary>
    private void OnDestroy()
    {
        intervalDropdown?.onValueChanged.RemoveListener(OnPresetChanged);
        cancelButton?.onClick.RemoveListener(Cancel);
        applyButton?.onClick.RemoveListener(Apply);
    }

    /// <summary>在页面局部按名称查找指定组件。</summary>
    private static T FindComponent<T>(Transform root, string objectName) where T : Component
    {
        T[] components = root.GetComponentsInChildren<T>(true);
        for (int index = 0; index < components.Length; index++)
        {
            if (components[index] != null && components[index].name == objectName)
                return components[index];
        }

        return null;
    }
}
