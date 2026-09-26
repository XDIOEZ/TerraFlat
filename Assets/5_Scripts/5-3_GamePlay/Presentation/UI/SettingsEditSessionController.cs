using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>主菜单设置编辑会话控制器；保存按钮提交修改，关闭面板时恢复最近一次保存状态。</summary>
[DisallowMultipleComponent]
public sealed class SettingsEditSessionController : MonoBehaviour
{
    #region 节点命名契约

    public const string SaveButtonName = "保存设置";

    #endregion

    #region 运行时状态

    /// <summary>拥有本次设置会话的基础面板。</summary>
    private BasePanel basePanel;

    /// <summary>底部保存按钮。</summary>
    private Button saveButton;

    /// <summary>负责按键绑定编辑的页面控制器。</summary>
    private InputBindingPanelLauncher inputBindingLauncher;

    /// <summary>本控制器是否持有正在进行的编辑会话。</summary>
    private bool sessionActive;

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
        if (!sessionActive)
            BeginSession();
        if (!sessionActive)
            return;

        try
        {
            inputBindingLauncher?.CommitSettingsEditSession();
            PlayerPrefs.Save();
            SettingsProviderRegistry.CommitEditSession();
            SetSavedStatus();
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                $"[SettingsEditSessionController] 保存设置失败：{exception.Message}",
                this);
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

        if (basePanel != null)
        {
            basePanel.Opened -= HandlePanelOpened;
            basePanel.Closed -= HandlePanelClosed;
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
