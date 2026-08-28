using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 管理设置会话页的唯一保存退出确认层：记录返回主界面或桌面的目标，并将“取消”解释为
/// 不保存直接退出、“确认”解释为保存后退出。视觉节点全部来自 UI_ActionList Prefab，本组件只负责状态、
/// 焦点与决策回调；Escape、Android 返回键和手柄取消都优先关闭确认层。
/// </summary>
[DisallowMultipleComponent]
public sealed class SettingsExitConfirmationController : MonoBehaviour
{
    #region 节点契约

    public const string LayerName = "保存退出确认层";
    public const string DialogName = "保存退出确认窗口";
    public const string PromptName = "保存退出提示";
    public const string CancelButtonName = "取消按钮";
    public const string ConfirmButtonName = "确认按钮";

    #endregion

    #region 运行时状态

    private BasePanel basePanel;
    private GameObject confirmationLayer;
    private RectTransform dialogRect;
    private Button cancelButton;
    private Button confirmButton;
    private Selectable returnFocus;
    private Action<SettingsExitDestination, bool> decisionHandler;
    private SettingsExitDestination? pendingDestination;

    public bool IsOpen => confirmationLayer != null && confirmationLayer.activeSelf;

    #endregion

    #region 初始化

    /// <summary>在设置主面板上建立唯一确认控制器并绑定退出决策回调。</summary>
    public static SettingsExitConfirmationController Ensure(
        BasePanel panel,
        Action<SettingsExitDestination, bool> onDecision)
    {
        if (panel == null)
            throw new ArgumentNullException(nameof(panel));
        if (onDecision == null)
            throw new ArgumentNullException(nameof(onDecision));

        SettingsExitConfirmationController controller =
            panel.GetComponent<SettingsExitConfirmationController>();
        if (controller == null)
            controller = panel.gameObject.AddComponent<SettingsExitConfirmationController>();

        controller.Bind(panel, onDecision);
        return controller;
    }

    /// <summary>解析 Prefab 契约并绑定两种退出决策。</summary>
    private void Bind(BasePanel panel, Action<SettingsExitDestination, bool> onDecision)
    {
        Unbind();
        basePanel = panel;
        decisionHandler = onDecision;

        Transform layerTransform = FindTransform(panel.transform, LayerName);
        confirmationLayer = layerTransform != null ? layerTransform.gameObject : null;
        dialogRect = FindTransform(layerTransform, DialogName) as RectTransform;
        cancelButton = FindButton(layerTransform, CancelButtonName);
        confirmButton = FindButton(layerTransform, ConfirmButtonName);

        if (confirmationLayer == null || dialogRect == null ||
            cancelButton == null || confirmButton == null)
        {
            throw new MissingReferenceException(
                "[SettingsExitConfirmationController] UI_ActionList Prefab 的保存退出确认层节点不完整。");
        }

        ConfigureNavigation();
        cancelButton.onClick.RemoveListener(ExitWithoutSaving);
        cancelButton.onClick.AddListener(ExitWithoutSaving);
        confirmButton.onClick.RemoveListener(SaveAndExit);
        confirmButton.onClick.AddListener(SaveAndExit);
        basePanel.CancelOverride = HandlePanelCancel;
        basePanel.CancelShortcutOverride = TryClose;
        basePanel.Closed -= HandlePanelClosed;
        basePanel.Closed += HandlePanelClosed;
        Hide(false);
    }

    #endregion

    #region 开关与决策

    /// <summary>显示共用确认层并记住本次退出目标。</summary>
    public void Open(SettingsExitDestination destination, Selectable origin)
    {
        pendingDestination = destination;
        returnFocus = origin;
        cancelButton.interactable = true;
        confirmButton.interactable = true;
        confirmationLayer.SetActive(true);
        confirmationLayer.transform.SetAsLastSibling();

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(dialogRect);
        basePanel.RefreshGamepadNavigationState();
        Focus(cancelButton);
    }

    /// <summary>返回键只取消本次询问，不执行退出。</summary>
    public bool TryClose()
    {
        if (!IsOpen)
            return false;

        Hide(true);
        return true;
    }

    /// <summary>左侧灰色“取消”表示取消保存，按当前目标直接退出。</summary>
    private void ExitWithoutSaving()
    {
        CompleteDecision(false);
    }

    /// <summary>右侧黄色“确认”在退出前写入当前世界存档。</summary>
    private void SaveAndExit()
    {
        CompleteDecision(true);
    }

    /// <summary>锁定按钮防止重入，再把一次性决策交给世界退出流程。</summary>
    private void CompleteDecision(bool saveBeforeExit)
    {
        if (!pendingDestination.HasValue)
            throw new InvalidOperationException("退出确认缺少目标。");

        cancelButton.interactable = false;
        confirmButton.interactable = false;
        SettingsExitDestination destination = pendingDestination.Value;
        decisionHandler.Invoke(destination, saveBeforeExit);
    }

    /// <summary>手柄 Cancel 优先关闭内嵌确认层。</summary>
    private bool HandlePanelCancel(BaseEventData eventData)
    {
        return TryClose();
    }

    private void HandlePanelClosed()
    {
        Hide(false);
    }

    /// <summary>隐藏确认层并按需恢复发起按钮焦点。</summary>
    private void Hide(bool restoreFocus)
    {
        if (confirmationLayer != null)
            confirmationLayer.SetActive(false);

        pendingDestination = null;
        if (basePanel != null)
            basePanel.RefreshGamepadNavigationState();

        if (restoreFocus)
            Focus(returnFocus);
        returnFocus = null;
    }

    #endregion

    #region 焦点与清理

    /// <summary>限制确认层的方向导航在左右两颗按钮之间。</summary>
    private void ConfigureNavigation()
    {
        cancelButton.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnRight = confirmButton
        };
        confirmButton.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnLeft = cancelButton
        };
    }

    private static void Focus(Selectable selectable)
    {
        if (selectable == null || !selectable.IsActive() || !selectable.IsInteractable() ||
            EventSystem.current == null)
        {
            return;
        }

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(selectable.gameObject);
    }

    private void OnDestroy()
    {
        Unbind();
    }

    /// <summary>解除所有按钮与面板回调，避免重新创建玩家时残留领域委托。</summary>
    private void Unbind()
    {
        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(ExitWithoutSaving);
        if (confirmButton != null)
            confirmButton.onClick.RemoveListener(SaveAndExit);

        if (basePanel != null)
        {
            basePanel.Closed -= HandlePanelClosed;
            basePanel.CancelOverride = null;
            basePanel.CancelShortcutOverride = null;
        }
    }

    private static Transform FindTransform(Transform root, string objectName)
    {
        if (root == null)
            return null;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            if (transforms[index] != null && transforms[index].name == objectName)
                return transforms[index];
        }

        return null;
    }

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

/// <summary>设置页退出确认层支持的两个稳定目标。</summary>
public enum SettingsExitDestination
{
    MainMenu,
    Desktop
}
