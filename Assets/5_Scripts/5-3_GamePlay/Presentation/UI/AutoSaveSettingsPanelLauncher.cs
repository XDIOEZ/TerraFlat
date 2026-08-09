// AI-Context: 设置菜单的自动保存入口及运行时 uGUI 面板；下拉列表提供常用间隔与“永远不自动保存”，输入框提供自定义分钟数。
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class AutoSaveSettingsPanelLauncher : MonoBehaviour
{
    private const string EntryButtonName = "自动保存";
    private const int CustomOptionIndex = 6;

    private static readonly int[] PresetMinutes = { 0, 1, 5, 10, 15, 30, -1 };
    private Button entryButton;
    private BasePanel settingsPanel;
    private TMP_Dropdown intervalDropdown;
    private TMP_InputField intervalInput;
    private TextMeshProUGUI statusText;

    public static AutoSaveSettingsPanelLauncher Ensure(Transform settingsPanel)
    {
        if (settingsPanel == null)
            return null;

        AutoSaveSettingsPanelLauncher launcher =
            settingsPanel.GetComponent<AutoSaveSettingsPanelLauncher>();
        if (launcher == null)
            launcher = settingsPanel.gameObject.AddComponent<AutoSaveSettingsPanelLauncher>();

        launcher.EnsureEntryButton();
        return launcher;
    }

private void EnsureEntryButton()
    {
        if (entryButton != null)
            return;

        entryButton = FindButton(EntryButtonName);
        if (entryButton == null)
        {
            Debug.LogError(
                $"[AutoSaveSettingsPanelLauncher] Prefab 缺少入口按钮“{EntryButtonName}”。",
                this);
            return;
        }

        entryButton.onClick.RemoveListener(Open);
        entryButton.onClick.AddListener(Open);
    }

private void Open()
    {
        EnsureWindow();
        if (settingsPanel == null)
            return;

        RefreshControls();
        settingsPanel.Open();
        settingsPanel.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(settingsPanel.rectTransform);
    }

private void EnsureWindow()
    {
        if (settingsPanel != null)
            return;

        GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.AutoSaveSettings);
        if (prefab == null)
        {
            Debug.LogError(
                $"[AutoSaveSettingsPanelLauncher] 缺少 Prefab：{RuntimeUIPrefabKeys.AutoSaveSettings}。",
                this);
            return;
        }

        settingsPanel = UIManager.Instance.CreatePanelFromGameObject(
            prefab,
            RuntimeUIPrefabKeys.AutoSaveSettings);
        intervalDropdown = settingsPanel.GetComponentInChildren<TMP_Dropdown>(true);
        intervalInput = settingsPanel.GetInputField("自动保存间隔输入框");
        statusText = settingsPanel.GetText("状态文本");

        Button closeButton = settingsPanel.GetButton("关闭按钮");
        Button cancelButton = settingsPanel.GetButton("取消按钮");
        Button applyButton = settingsPanel.GetButton("应用按钮");
        closeButton?.onClick.AddListener(Close);
        cancelButton?.onClick.AddListener(Close);
        applyButton?.onClick.AddListener(Apply);

        if (intervalDropdown != null)
        {
            intervalDropdown.ClearOptions();
            intervalDropdown.AddOptions(BuildPresetLabels());
            intervalDropdown.onValueChanged.AddListener(OnPresetChanged);
        }

        if (intervalDropdown == null || intervalInput == null || statusText == null)
            Debug.LogError("[AutoSaveSettingsPanelLauncher] 自动保存 Prefab 控件命名契约不完整。", settingsPanel);

        settingsPanel.PrepareForGamepadNavigation();
        settingsPanel.Close();
    }













    private void RefreshControls()
    {
        if (intervalDropdown == null || intervalInput == null)
            return;

        int selectedIndex = ResolveCurrentOptionIndex();
        intervalDropdown.SetValueWithoutNotify(selectedIndex);
        intervalInput.SetTextWithoutNotify(AutoSavePreferences.IntervalMinutes.ToString());
        intervalInput.interactable = selectedIndex == CustomOptionIndex;
        SetCurrentStatus();
    }

    private int ResolveCurrentOptionIndex()
    {
        if (!AutoSavePreferences.Enabled)
            return 0;

        int currentMinutes = AutoSavePreferences.IntervalMinutes;
        for (int i = 1; i < PresetMinutes.Length - 1; i++)
        {
            if (PresetMinutes[i] == currentMinutes)
                return i;
        }

        return CustomOptionIndex;
    }

    private void OnPresetChanged(int selectedIndex)
    {
        bool custom = selectedIndex == CustomOptionIndex;
        intervalInput.interactable = custom;

        if (selectedIndex > 0 && selectedIndex < CustomOptionIndex)
            intervalInput.SetTextWithoutNotify(PresetMinutes[selectedIndex].ToString());

        if (selectedIndex == 0)
            SetStatus(
                FlatWorldLocalizationService.GetUiText("已选择：永远不自动保存。点击“应用”后生效。"),
                false);
        else if (custom)
            SetStatus(
                FlatWorldLocalizationService.GetUiText("请输入 1–1440 分钟，然后点击“应用”。"),
                false);
        else
            SetStatus(
                FlatWorldLocalizationService.GetUiFormat(
                    "已选择：每 {0} 分钟自动保存。",
                    PresetMinutes[selectedIndex]),
                false);
    }

    private void Apply()
    {
        int selectedIndex = intervalDropdown.value;
        if (selectedIndex == 0)
        {
            AutoSavePreferences.Disable();
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
                    FlatWorldLocalizationService.GetUiText("请输入 1–1440 之间的整数分钟数。"),
                    true);
                return;
            }
        }
        else
        {
            minutes = PresetMinutes[selectedIndex];
        }

        AutoSavePreferences.Enable(minutes);
        intervalInput.SetTextWithoutNotify(minutes.ToString());
        SetCurrentStatus();
    }

    private void SetCurrentStatus()
    {
        if (!AutoSavePreferences.Enabled)
            SetStatus(
                FlatWorldLocalizationService.GetUiText("当前设置：永远不自动保存。"),
                false);
        else
            SetStatus(
                FlatWorldLocalizationService.GetUiFormat(
                    "当前设置：每 {0} 分钟自动保存。",
                    AutoSavePreferences.IntervalMinutes),
                false);
    }

    private static List<string> BuildPresetLabels()
    {
        var labels = new List<string>(PresetMinutes.Length);
        labels.Add(FlatWorldLocalizationService.GetUiText("永远不自动保存"));
        for (int i = 1; i < PresetMinutes.Length - 1; i++)
            labels.Add(FlatWorldLocalizationService.GetUiFormat("每 {0} 分钟", PresetMinutes[i]));
        labels.Add(FlatWorldLocalizationService.GetUiText("自定义间隔"));
        return labels;
    }

    private void SetStatus(string message, bool isError)
    {
        if (statusText == null)
            return;

        statusText.text = message;
        statusText.color = isError
            ? new Color(0.95f, 0.38f, 0.31f)
            : new Color(0.21f, 0.78f, 0.74f);
    }

private void Close()
    {
        settingsPanel?.Close();
    }

private void OnDestroy()
    {
        if (entryButton != null)
            entryButton.onClick.RemoveListener(Open);
        if (settingsPanel != null)
            Destroy(settingsPanel.gameObject);
    }


















private Button FindButton(string buttonName)
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].name == buttonName)
                return buttons[i];
        }

        return null;
    }
}
