using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>主菜单设置编辑会话控制器；保存按钮提交修改，关闭面板时恢复最近一次保存状态。</summary>
[DisallowMultipleComponent]
public sealed class SettingsEditSessionController : MonoBehaviour
{
    #region 节点命名契约

    public const string SaveButtonName = "保存设置";
    private const string CloseButtonName = GameManager.MainMenuSettingsCloseButtonKey;
    private const string UnsavedConfirmationPanelName = "UI_SettingsUnsavedExitConfirmation";
    private const string ConfirmationPromptName = "退出确认提示";

    #endregion

    #region 运行时状态

    /// <summary>拥有本次设置会话的基础面板。</summary>
    private BasePanel basePanel;

    /// <summary>底部保存按钮。</summary>
    private Button saveButton;

    /// <summary>设置窗口右上角关闭按钮。</summary>
    private Button closeButton;

    /// <summary>负责按键绑定编辑的页面控制器。</summary>
    private InputBindingPanelLauncher inputBindingLauncher;

    /// <summary>本控制器是否持有正在进行的编辑会话。</summary>
    private bool sessionActive;

    /// <summary>复用主菜单确认 Prefab 创建的未保存修改弹窗。</summary>
    private BasePanel unsavedConfirmationPanel;

    private Button exitWithoutSavingButton;
    private Button saveAndExitButton;

    #endregion

    #region 初始化与面板生命周期

    /// <summary>在设置面板上确保唯一编辑会话控制器，并绑定底部保存按钮。</summary>
    public static SettingsEditSessionController Ensure(BasePanel panel)
    {
        if (panel == null)
            return null;

        SettingsEditSessionController controller =
            panel.GetComponent<SettingsEditSessionController>();
        if (controller == null)
            controller = panel.gameObject.AddComponent<SettingsEditSessionController>();

        controller.Bind(panel);
        return controller;
    }

    /// <summary>绑定保存按钮和面板开关事件。</summary>
    private void Bind(BasePanel panel)
    {
        Unbind();
        basePanel = panel;
        saveButton = FindButton(basePanel.transform, SaveButtonName);
        closeButton = FindButton(basePanel.transform, CloseButtonName);
        inputBindingLauncher =
            basePanel.GetComponentInChildren<InputBindingPanelLauncher>(true);

        if (saveButton == null)
        {
            Debug.LogError(
                $"[SettingsEditSessionController] 缺少保存按钮：{SaveButtonName}。",
                basePanel);
        }
        else
        {
            saveButton.onClick.RemoveListener(CommitChanges);
            saveButton.onClick.AddListener(CommitChanges);
        }

        if (closeButton == null)
        {
            Debug.LogError(
                $"[SettingsEditSessionController] 缺少关闭按钮：{CloseButtonName}。",
                basePanel);
        }
        else
        {
            // BasePanel 会自动给名为“关闭”的按钮绑定 Close；这里改为先检查未保存修改。
            closeButton.onClick.RemoveListener(basePanel.Close);
            closeButton.onClick.RemoveListener(RequestClose);
            closeButton.onClick.AddListener(RequestClose);
        }

        basePanel.CancelOverride = HandlePanelCancel;
        basePanel.CancelShortcutOverride = HandleCancelShortcut;

        basePanel.Opened -= HandlePanelOpened;
        basePanel.Closed -= HandlePanelClosed;
        basePanel.Opened += HandlePanelOpened;
        basePanel.Closed += HandlePanelClosed;
        if (basePanel.IsOpen())
            BeginSession();
    }

    /// <summary>打开面板时快照全部设置 Provider 和按键覆盖。</summary>
    private void HandlePanelOpened()
    {
        BeginSession();
    }

    /// <summary>关闭面板时放弃未保存修改并恢复运行时表现。</summary>
    private void HandlePanelClosed()
    {
        CloseUnsavedConfirmation();
        if (!sessionActive)
            return;

        try
        {
            inputBindingLauncher?.DiscardSettingsEditSession();
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                $"[SettingsEditSessionController] 放弃按键修改失败：{exception.Message}",
                this);
        }

        bool restored = SettingsProviderRegistry.DiscardEditSession(out string error);
        PlayerPrefs.Save();
        sessionActive = false;
        if (!restored && !string.IsNullOrEmpty(error))
        {
            Debug.LogError(
                $"[SettingsEditSessionController] 还原未保存设置失败：\n{error}",
                this);
        }
    }

    /// <summary>只在没有活动会话时开始新一轮设置编辑。</summary>
    private void BeginSession()
    {
        if (sessionActive)
            return;

        sessionActive = SettingsProviderRegistry.BeginEditSession();
        if (sessionActive)
            inputBindingLauncher?.BeginSettingsEditSession();
    }

    #endregion

    #region 保存提交

    /// <summary>保存当前设置值并以此建立后续放弃修改的新基线。</summary>
    private void CommitChanges()
    {
        TryCommitChanges();
    }

    /// <summary>尝试提交当前设置；只有全部保存成功才允许“保存退出”继续关闭。</summary>
    private bool TryCommitChanges()
    {
        if (!sessionActive)
            BeginSession();
        if (!sessionActive)
            return false;

        try
        {
            inputBindingLauncher?.CommitSettingsEditSession();
            PlayerPrefs.Save();
            SettingsProviderRegistry.CommitEditSession();
            SetSavedStatus();
            return true;
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                $"[SettingsEditSessionController] 保存设置失败：{exception.Message}",
                this);
            return false;
        }
    }

    /// <summary>在设置页状态区域提示本次保存已完成。</summary>
    private void SetSavedStatus()
    {
        TextMeshProUGUI statusText = basePanel?.GetText(GameManager.MainMenuSettingsLanguageStatusTextKey);
        if (statusText != null)
        {
            statusText.text = FlatWorldLocalizationService.GetUiText("设置修改已保存。");
        }
    }

    #endregion

    #region 未保存修改退出确认

    /// <summary>关闭按钮统一入口：有未保存修改时先弹确认，没有修改则直接关闭。</summary>
    public void RequestClose()
    {
        if (!HasUnsavedChanges())
        {
            basePanel?.Close();
            return;
        }

        OpenUnsavedConfirmation();
    }

    /// <summary>检查 Provider 与按键绑定两部分是否偏离最近一次保存基线。</summary>
    private bool HasUnsavedChanges()
    {
        return sessionActive &&
               (SettingsProviderRegistry.HasEditSessionChanges() ||
                (inputBindingLauncher != null &&
                 inputBindingLauncher.HasSettingsEditSessionChanges));
    }

    /// <summary>Escape/手柄取消在有修改时也走同一层保存确认。</summary>
    private bool HandlePanelCancel(BaseEventData eventData)
    {
        if (!HasUnsavedChanges())
            return false;

        OpenUnsavedConfirmation();
        return true;
    }

    /// <summary>全局返回快捷键在有修改时阻止直接关闭设置面板。</summary>
    private bool HandleCancelShortcut()
    {
        if (!HasUnsavedChanges())
            return false;

        OpenUnsavedConfirmation();
        return true;
    }

    /// <summary>打开复用的双按钮确认弹窗。</summary>
    private void OpenUnsavedConfirmation()
    {
        if (!EnsureUnsavedConfirmationPanel())
            return;

        RefreshUnsavedConfirmationText();
        unsavedConfirmationPanel.Open();
    }

    /// <summary>按需从正式主菜单确认 Prefab 创建未保存修改弹窗，不在运行时拼视觉节点。</summary>
    private bool EnsureUnsavedConfirmationPanel()
    {
        if (unsavedConfirmationPanel != null)
            return true;

        UIManager uiManager = UIManager.Instance;
        GameObject prefab = GameRes.Instance?.GetPrefab(
            RuntimeUIPrefabKeys.MainMenuExitConfirmation,
            false);
        if (uiManager == null || prefab == null)
        {
            Debug.LogError(
                "[SettingsEditSessionController] 无法创建未保存修改确认弹窗：确认 Prefab 或 UIManager 未就绪。",
                this);
            return false;
        }

        unsavedConfirmationPanel = uiManager.CreatePanelFromGameObject(
            prefab,
            UnsavedConfirmationPanelName);
        if (unsavedConfirmationPanel == null)
            return false;

        Button dismissButton = unsavedConfirmationPanel.GetButton(
            GameManager.MainMenuExitConfirmationCloseButtonKey);
        exitWithoutSavingButton = unsavedConfirmationPanel.GetButton(
            GameManager.MainMenuExitConfirmationCancelButtonKey);
        saveAndExitButton = unsavedConfirmationPanel.GetButton(
            GameManager.MainMenuExitConfirmationConfirmButtonKey);
        if (dismissButton == null ||
            exitWithoutSavingButton == null ||
            saveAndExitButton == null)
        {
            Debug.LogError(
                "[SettingsEditSessionController] 未保存修改确认 Prefab 的按钮命名契约不完整。",
                unsavedConfirmationPanel);
            return false;
        }

        dismissButton.onClick.AddListener(CloseUnsavedConfirmation);
        exitWithoutSavingButton.onClick.AddListener(ExitWithoutSaving);
        saveAndExitButton.onClick.AddListener(SaveAndExit);
        unsavedConfirmationPanel.PrepareForGamepadNavigation(
            GameManager.MainMenuExitConfirmationCancelButtonKey);
        return true;
    }

    /// <summary>刷新弹窗文案；复用已有本地化条目避免引入另一套提示语。</summary>
    private void RefreshUnsavedConfirmationText()
    {
        TextMeshProUGUI prompt = unsavedConfirmationPanel?.GetText(
            ConfirmationPromptName);
        if (prompt != null)
            prompt.text = FlatWorldLocalizationService.GetUiText("是否保存再退出");

        SetButtonLabel(
            exitWithoutSavingButton,
            FlatWorldLocalizationService.GetUiText("不保存直接退出"));
        SetButtonLabel(
            saveAndExitButton,
            FlatWorldLocalizationService.GetUiText("保存与退出"));
    }

    /// <summary>明确放弃本次修改，再由原关闭流程恢复最近保存基线。</summary>
    private void ExitWithoutSaving()
    {
        CloseUnsavedConfirmation();
        basePanel?.Close();
    }

    /// <summary>先提交本次修改，成功后再关闭设置面板。</summary>
    private void SaveAndExit()
    {
        if (!TryCommitChanges())
            return;

        CloseUnsavedConfirmation();
        basePanel?.Close();
    }

    private void CloseUnsavedConfirmation()
    {
        if (unsavedConfirmationPanel != null && unsavedConfirmationPanel.IsOpen())
            unsavedConfirmationPanel.Close();
    }

    /// <summary>复用确认 Prefab 的按钮视觉，只替换业务文案。</summary>
    private static void SetButtonLabel(Button button, string label)
    {
        TextMeshProUGUI text = button != null
            ? button.GetComponentInChildren<TextMeshProUGUI>(true)
            : null;
        if (text != null)
            text.text = label ?? string.Empty;
    }

    #endregion

    #region 清理与局部查找

    /// <summary>销毁时也放弃尚未保存的值，避免场景切换遗留修改。</summary>
    private void OnDestroy()
    {
        HandlePanelClosed();
        Unbind();
    }

    /// <summary>退出应用时将未提交设置还原到 PlayerPrefs 中的最近一次保存值。</summary>
    private void OnApplicationQuit()
    {
        HandlePanelClosed();
    }

    /// <summary>解除按钮与面板事件，避免重复绑定。</summary>
    private void Unbind()
    {
        if (saveButton != null)
            saveButton.onClick.RemoveListener(CommitChanges);
        if (closeButton != null)
            closeButton.onClick.RemoveListener(RequestClose);

        if (unsavedConfirmationPanel != null)
        {
            Button dismissButton = unsavedConfirmationPanel.GetButton(
                GameManager.MainMenuExitConfirmationCloseButtonKey);
            dismissButton?.onClick.RemoveListener(CloseUnsavedConfirmation);
            exitWithoutSavingButton?.onClick.RemoveListener(ExitWithoutSaving);
            saveAndExitButton?.onClick.RemoveListener(SaveAndExit);
        }

        if (basePanel != null)
        {
            basePanel.Opened -= HandlePanelOpened;
            basePanel.Closed -= HandlePanelClosed;
            basePanel.CancelOverride = null;
            basePanel.CancelShortcutOverride = null;
        }
    }

    /// <summary>按稳定节点名查找设置面板内的按钮。</summary>
    private static Button FindButton(Transform root, string buttonName)
    {
        if (root == null)
            return null;

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int index = 0; index < buttons.Length; index++)
        {
            if (buttons[index] != null && buttons[index].name == buttonName)
                return buttons[index];
        }

        return null;
    }

    #endregion
}
