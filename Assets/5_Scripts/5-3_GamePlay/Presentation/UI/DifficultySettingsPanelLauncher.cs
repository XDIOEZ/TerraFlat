using System.Collections.Generic;
using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 绑定内嵌难度页的草稿选择与提交逻辑。
/// 页面每次显示都从当前存档难度重新建草稿，取消仅返回世界入口而不写入设置。
/// </summary>
[DisallowMultipleComponent]
public sealed class DifficultySettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    private const string EntryButtonName = "游戏难度";

    private readonly Dictionary<GameDifficultyId, Button> optionButtons =
        new Dictionary<GameDifficultyId, Button>();
    private readonly Dictionary<GameDifficultyId, UnityAction> optionCallbacks =
        new Dictionary<GameDifficultyId, UnityAction>();

    private Button entryButton;
    private Button cancelButton;
    private Button applyButton;
    private TextMeshProUGUI statusText;
    private ISettingsSwitch difficultySetting;
    private SettingsActionListPagination pagination;
    private GameDifficultyId selectedDifficulty;
    private bool initialized;

    #region 初始化

    /// <summary>在指定内嵌页上建立唯一难度绑定器。</summary>
    public static DifficultySettingsPanelLauncher Ensure(
        Transform pageRoot,
        Transform worldPageRoot,
        SettingsActionListPagination ownerPagination)
    {
        if (pageRoot == null)
            return null;

        DifficultySettingsPanelLauncher launcher =
            pageRoot.GetComponent<DifficultySettingsPanelLauncher>();
        if (launcher == null)
            launcher = pageRoot.gameObject.AddComponent<DifficultySettingsPanelLauncher>();

        launcher.Initialize(worldPageRoot, ownerPagination);
        return launcher;
    }

    /// <summary>只从本页查找表单控件，并从世界入口页取得标签按钮。</summary>
    private void Initialize(Transform worldPageRoot, SettingsActionListPagination ownerPagination)
    {
        pagination = ownerPagination;
        entryButton = FindButton(worldPageRoot, EntryButtonName);
        if (initialized)
        {
            UpdateEntryLabel();
            return;
        }

        initialized = true;
        difficultySetting = GameDifficultyService.SettingsProvider.GetSwitch(
            GameDifficultyService.DifficultySettingKey);
        statusText = FindComponent<TextMeshProUGUI>(transform, "状态文本");
        cancelButton = FindButton(transform, "取消按钮");
        applyButton = FindButton(transform, "应用按钮");

        IReadOnlyList<GameDifficultyDefinition> definitions = GameDifficultyCatalog.All;
        for (int i = 0; i < definitions.Count; i++)
        {
            GameDifficultyId id = definitions[i].Id;
            Button option = FindButton(transform, $"难度_{id}");
            if (option == null)
                continue;

            UnityAction callback = () => SelectDifficulty(id);
            option.onClick.AddListener(callback);
            optionButtons[id] = option;
            optionCallbacks[id] = callback;
        }

        cancelButton?.onClick.AddListener(Cancel);
        applyButton?.onClick.AddListener(Apply);

        if (entryButton == null || statusText == null || cancelButton == null ||
            applyButton == null || difficultySetting == null ||
            optionButtons.Count != definitions.Count)
        {
            Debug.LogError(
                "[DifficultySettingsPanelLauncher] 内嵌难度页控件命名契约不完整。",
                this);
        }

        UpdateEntryLabel();
    }

    #endregion

    #region 页面生命周期

    /// <summary>页面显示时丢弃旧草稿并重新读取当前已应用难度。</summary>
    public void OnSettingsPageShown()
    {
        if (difficultySetting == null)
        {
            difficultySetting = GameDifficultyService.SettingsProvider.GetSwitch(
                GameDifficultyService.DifficultySettingKey);
        }

        selectedDifficulty = difficultySetting != null
            ? (GameDifficultyId)difficultySetting.SelectedIndex
            : GameDifficultyService.CurrentId;
        RefreshSelectionVisuals();
        UpdateEntryLabel();
        SetStatus(
            FlatWorldLocalizationService.GetUiFormat(
                "当前存档难度：{0}",
                LocalizeDifficultyName(GameDifficultyService.Current)),
            false);
    }

    /// <summary>页面隐藏时不提交草稿，下次显示会重新读取已应用值。</summary>
    public void OnSettingsPageHidden()
    {
    }

    #endregion

    #region 草稿操作

    /// <summary>只更新当前页草稿与选中样式。</summary>
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

    /// <summary>提交难度草稿并停留在当前分页显示结果。</summary>
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

    /// <summary>放弃难度草稿并返回世界入口页。</summary>
    private void Cancel()
    {
        pagination?.ShowWorldPage();
    }

    /// <summary>通过现有难度设置提供者提交选中值。</summary>
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

    #endregion

    #region 视图刷新

    /// <summary>刷新难度选项的选中颜色。</summary>
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

    /// <summary>刷新世界入口按钮上的当前难度名称。</summary>
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

    /// <summary>显示当前操作结果或错误。</summary>
    private void SetStatus(string message, bool isError)
    {
        if (statusText == null)
            return;

        statusText.text = message;
        statusText.color = isError ? FlatWorldUITheme.Danger : FlatWorldUITheme.Teal;
    }

    /// <summary>更新按钮的 TMP 或旧版文本组件。</summary>
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

    /// <summary>取得当前语言下的难度名称。</summary>
    private static string LocalizeDifficultyName(GameDifficultyDefinition definition)
    {
        if (definition == null)
            return string.Empty;

        return FlatWorldLocalizationService.GetUiText(definition.DisplayName);
    }

    #endregion

    #region 清理与局部查找

    /// <summary>解除本页注册的按钮事件。</summary>
    private void OnDestroy()
    {
        foreach (KeyValuePair<GameDifficultyId, UnityAction> pair in optionCallbacks)
        {
            if (optionButtons.TryGetValue(pair.Key, out Button button) && button != null)
                button.onClick.RemoveListener(pair.Value);
        }

        cancelButton?.onClick.RemoveListener(Cancel);
        applyButton?.onClick.RemoveListener(Apply);
    }

    /// <summary>在给定页面范围内按名称查找按钮。</summary>
    private static Button FindButton(Transform root, string buttonName)
    {
        return FindComponent<Button>(root, buttonName);
    }

    /// <summary>在给定页面范围内按名称查找指定组件。</summary>
    private static T FindComponent<T>(Transform root, string objectName) where T : Component
    {
        if (root == null)
            return null;

        T[] components = root.GetComponentsInChildren<T>(true);
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] != null && components[i].name == objectName)
                return components[i];
        }

        return null;
    }

    #endregion
}
