using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>设置子页共用的画布尺寸约束，保证调整 UI 缩放时视觉安全边距不会反向放大。</summary>
public static class SettingsPanelLayoutUtility
{
    /// <summary>在当前根画布内限制面板尺寸，并让安全边距不随用户缩放值重复放大。</summary>
    public static void ClampToCanvas(
        BasePanel panel,
        Vector2 preferredSize,
        float visualSafeMargin)
    {
        if (panel == null)
            return;

        Canvas canvas = panel.GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        RectTransform panelRect = panel.rectTransform;
        if (canvasRect == null || panelRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        float userScale = Mathf.Max(0.5f, UIUserSettings.Scale);
        float logicalMargin = Mathf.Max(0f, visualSafeMargin) / userScale;
        Vector2 available = canvasRect.rect.size - Vector2.one * (logicalMargin * 2f);
        panelRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            Mathf.Min(preferredSize.x, Mathf.Max(1f, available.x)));
        panelRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            Mathf.Min(preferredSize.y, Mathf.Max(1f, available.y)));
        panelRect.anchoredPosition = Vector2.zero;
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
    }
}

/// <summary>
/// 设置主菜单与二级页之间的模态交互守卫。任一已登记子页打开时，主菜单保持可见但不再接收点击，
/// 避免玩家从子页外侧误关掉后方设置菜单。
/// </summary>
[DisallowMultipleComponent]
public sealed class SettingsSubPanelInteractionGuard : MonoBehaviour
{
    private sealed class ChildLink
    {
        public BasePanel Panel;
        public Action Opened;
        public Action Closed;
    }

    private readonly List<ChildLink> childLinks = new List<ChildLink>();
    private readonly HashSet<BasePanel> openChildren = new HashSet<BasePanel>();
    private BasePanel parentPanel;

    /// <summary>把二级页登记到所属设置主菜单，重复登记会被忽略。</summary>
    public static void Link(Transform settingsRoot, BasePanel childPanel)
    {
        if (settingsRoot == null || childPanel == null)
            return;

        SettingsSubPanelInteractionGuard guard =
            settingsRoot.GetComponent<SettingsSubPanelInteractionGuard>();
        if (guard == null)
            guard = settingsRoot.gameObject.AddComponent<SettingsSubPanelInteractionGuard>();
        guard.Track(childPanel);
    }

    /// <summary>缓存主面板，供子页事件统一切换交互状态。</summary>
    private void Awake()
    {
        parentPanel = GetComponent<BasePanel>();
        if (parentPanel != null)
            parentPanel.Opened += RefreshParentInteraction;
    }

    /// <summary>订阅一个设置子页的开关事件。</summary>
    private void Track(BasePanel childPanel)
    {
        for (int i = 0; i < childLinks.Count; i++)
        {
            if (childLinks[i].Panel == childPanel)
                return;
        }

        Action opened = () => HandleChildOpened(childPanel);
        Action closed = () => HandleChildClosed(childPanel);
        childPanel.Opened += opened;
        childPanel.Closed += closed;
        childLinks.Add(new ChildLink
        {
            Panel = childPanel,
            Opened = opened,
            Closed = closed
        });

        if (childPanel.IsOpen())
            HandleChildOpened(childPanel);
    }

    /// <summary>记录打开的子页并停用后方主菜单交互。</summary>
    private void HandleChildOpened(BasePanel childPanel)
    {
        if (childPanel != null)
            openChildren.Add(childPanel);
        RefreshParentInteraction();
    }

    /// <summary>移除已关闭子页，并在最后一个子页关闭后恢复主菜单交互。</summary>
    private void HandleChildClosed(BasePanel childPanel)
    {
        if (childPanel != null)
            openChildren.Remove(childPanel);
        RefreshParentInteraction();
    }

    /// <summary>根据是否存在打开的子页更新主菜单 CanvasGroup。</summary>
    private void RefreshParentInteraction()
    {
        parentPanel ??= GetComponent<BasePanel>();
        CanvasGroup group = parentPanel != null ? parentPanel.canvasGroup : null;
        if (group == null || !parentPanel.IsOpen())
            return;

        bool childOpen = openChildren.Count > 0;
        group.interactable = !childOpen;
        group.blocksRaycasts = !childOpen;
    }

    /// <summary>销毁设置主菜单时解除全部子页事件订阅。</summary>
    private void OnDestroy()
    {
        if (parentPanel != null)
            parentPanel.Opened -= RefreshParentInteraction;

        for (int i = 0; i < childLinks.Count; i++)
        {
            ChildLink link = childLinks[i];
            if (link.Panel == null)
                continue;
            link.Panel.Opened -= link.Opened;
            link.Panel.Closed -= link.Closed;
        }

        childLinks.Clear();
        openChildren.Clear();
    }
}
