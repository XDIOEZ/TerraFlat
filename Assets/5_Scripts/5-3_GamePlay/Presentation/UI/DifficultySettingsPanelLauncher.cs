using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class DifficultySettingsPanelLauncher : MonoBehaviour
{
    private const string EntryButtonName = "游戏难度";

    private readonly Dictionary<GameDifficultyId, Button> optionButtons =
        new Dictionary<GameDifficultyId, Button>();

    private Button entryButton;
    private BasePanel settingsPanel;
    private TextMeshProUGUI statusText;
    private ISettingsSwitch difficultySetting;
    private GameDifficultyId selectedDifficulty;

    public static DifficultySettingsPanelLauncher Ensure(Transform settingsPanel)
    {
        if (settingsPanel == null)
            return null;

        DifficultySettingsPanelLauncher launcher =
            settingsPanel.GetComponent<DifficultySettingsPanelLauncher>();
        if (launcher == null)
            launcher = settingsPanel.gameObject.AddComponent<DifficultySettingsPanelLauncher>();

        launcher.EnsureEntryButton();
        return launcher;
    }

private void EnsureEntryButton()
    {
        if (entryButton == null)
            entryButton = FindButton(transform, EntryButtonName);

        if (entryButton == null)
        {
            Debug.LogError(
                $"[DifficultySettingsPanelLauncher] Prefab 缺少入口按钮“{EntryButtonName}”。",
                this);
            return;
        }

        entryButton.onClick.RemoveListener(Open);
        entryButton.onClick.AddListener(Open);
        UpdateEntryLabel();
    }

private void Open()
    {
        EnsureWindow();
        if (settingsPanel == null)
            return;

        selectedDifficulty = difficultySetting != null
            ? (GameDifficultyId)difficultySetting.SelectedIndex
            : GameDifficultyService.CurrentId;
        RefreshSelectionVisuals();
        SetStatus(
            FlatWorldLocalizationService.GetUiFormat(
                "当前存档难度：{0}",
                LocalizeDifficultyName(GameDifficultyService.Current)),
            false);
        settingsPanel.Open();
        settingsPanel.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(settingsPanel.rectTransform);
    }

private void EnsureWindow()
    {
        if (settingsPanel != null)
            return;

        GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.DifficultySettings);
        if (prefab == null)
        {
            Debug.LogError(
                $"[DifficultySettingsPanelLauncher] 缺少 Prefab：{RuntimeUIPrefabKeys.DifficultySettings}。",
                this);
            return;
        }

        settingsPanel = UIManager.Instance.CreatePanelFromGameObject(
            prefab,
            RuntimeUIPrefabKeys.DifficultySettings);
        difficultySetting = GameDifficultyService.SettingsProvider.GetSwitch(
            GameDifficultyService.DifficultySettingKey);
        statusText = settingsPanel.GetText("状态文本");
        optionButtons.Clear();

        IReadOnlyList<GameDifficultyDefinition> definitions = GameDifficultyCatalog.All;
        for (int i = 0; i < definitions.Count; i++)
        {
            GameDifficultyDefinition definition = definitions[i];
            Button option = settingsPanel.GetButton($"难度_{definition.Id}");
            if (option == null)
                continue;

            GameDifficultyId id = definition.Id;
            option.onClick.AddListener(() => SelectDifficulty(id));
            optionButtons[id] = option;
        }

        settingsPanel.GetButton("关闭按钮")?.onClick.AddListener(Close);
        settingsPanel.GetButton("取消按钮")?.onClick.AddListener(Close);
        settingsPanel.GetButton("应用按钮")?.onClick.AddListener(Apply);

        if (statusText == null || optionButtons.Count != definitions.Count ||
            difficultySetting == null)
            Debug.LogError("[DifficultySettingsPanelLauncher] 难度 Prefab 控件命名契约不完整。", settingsPanel);

        settingsPanel.PrepareForGamepadNavigation();
        settingsPanel.Close();
    }







    private void SelectDifficulty(GameDifficultyId difficulty)
    {
        selectedDifficulty = difficulty;
        RefreshSelectionVisuals();
        GameDifficultyDefinition definition = GameDifficultyCatalog.Get(difficulty);
        SetStatus(
            FlatWorldLocalizationService.GetUiFormat(
                "已选择：{0}。点击“应用”后生效。",
                LocalizeDifficultyName(definition)),
            false);
    }

    private void Apply()
    {
        if (!TryApplyDifficulty(out string error))
        {
            SetStatus(
                FlatWorldLocalizationService.GetUiText(
                    error ?? "难度设置提供者尚未注册。"),
                true);
            return;
        }

        GameDifficultyDefinition definition = GameDifficultyCatalog.Get(selectedDifficulty);
        UpdateEntryLabel();
        RefreshSelectionVisuals();
        SetStatus(
            FlatWorldLocalizationService.GetUiFormat(
                "已应用：{0}。设置将在正常存档时写入磁盘。",
                LocalizeDifficultyName(definition)),
            false);
    }

    private bool TryApplyDifficulty(out string error)
    {
        if (difficultySetting == null)
        {
            error = "难度设置提供者尚未注册。";
            return false;
        }

        return difficultySetting.TrySetSelectedIndex(
            (int)selectedDifficulty,
            out error);
    }

    private void RefreshSelectionVisuals()
    {
        foreach (KeyValuePair<GameDifficultyId, Button> pair in optionButtons)
        {
            if (pair.Value?.targetGraphic == null)
                continue;

            pair.Value.targetGraphic.color = pair.Key == selectedDifficulty
                ? FlatWorldUITheme.Accent
                : FlatWorldUITheme.Surface;
        }
    }

    private void UpdateEntryLabel()
    {
        if (entryButton == null)
            return;

        SetButtonLabel(
            entryButton,
            FlatWorldLocalizationService.GetUiFormat(
                "游戏难度：{0}",
                LocalizeDifficultyName(GameDifficultyService.Current)));
    }

    private void SetStatus(string message, bool isError)
    {
        if (statusText == null)
            return;

        statusText.text = message;
        statusText.color = isError ? FlatWorldUITheme.Danger : FlatWorldUITheme.Teal;
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







private static void SetButtonLabel(Button button, string label)
    {
        if (button == null)
            return;

        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
        {
            text.text = label;
            return;
        }

        Text legacyText = button.GetComponentInChildren<Text>(true);
        if (legacyText != null)
            legacyText.text = label;
    }

    private static string LocalizeDifficultyName(GameDifficultyDefinition definition)
    {
        if (definition == null)
            return string.Empty;

        return FlatWorldLocalizationService.GetUiText(definition.DisplayName);
    }






private static Button FindButton(Transform root, string buttonName)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].name == buttonName)
                return buttons[i];
        }

        return null;
    }
}
