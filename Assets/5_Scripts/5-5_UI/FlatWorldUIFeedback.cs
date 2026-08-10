// AI-Context: FlatWorld 通用 UI 微交互；负责按钮悬停、按压和导航选中反馈，不查找业务节点、不绑定点击事件。

using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 为普通操作按钮提供悬停、按压和导航选中反馈。
/// 交互事件只在状态变化时创建一个使用非缩放时间的 DOTween 缩放动画，静止时没有组件级 Update。
/// 槽位按钮和可拖拽内容不应挂载本组件，避免干扰背包交互。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Selectable))]
public sealed class FlatWorldUIFeedback : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler,
    ISelectHandler,
    IDeselectHandler
{
    #region 视觉配置

    [SerializeField, Range(1f, 1.08f)] private float hoverScale = 1.018f;
    [SerializeField, Range(0.92f, 1f)] private float pressedScale = 0.975f;
    [SerializeField, Range(1f, 1.12f)] private float selectedScale = 1.035f;
    [Tooltip("数值越高，DOTween 缩放反馈越快。")]
    [SerializeField, Range(4f, 30f)] private float response = 18f;

    #endregion

    #region 运行时状态

    private const float MinimumTweenDuration = 0.04f;
    private const float ScaleComparisonEpsilonSquared = 0.000001f;

    private Selectable selectable;
    private Tween scaleTween;
    private Graphic selectionGraphic;
    private Outline selectionOutline;
    private bool createdSelectionOutline;
    private bool outlineBaselineCaptured;
    private bool outlineBaselineEnabled;
    private Color outlineBaselineColor;
    private Vector2 outlineBaselineDistance;
    private bool outlineBaselineUsesGraphicAlpha;
    private bool hovered;
    private bool pressed;
    private bool selected;

    #endregion

    #region Unity生命周期

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
        ConfigureSelectedColor();
        EnsureSelectionOutline();
    }

    private void OnEnable()
    {
        transform.localScale = Vector3.one;
        selected = EventSystem.current != null &&
                   EventSystem.current.currentSelectedGameObject == gameObject;
        SetSelectionVisual(selected);
        AnimateToCurrentState();
    }

    private void OnDisable()
    {
        KillScaleTween();
        hovered = false;
        pressed = false;
        selected = false;
        RestoreSelectionOutline();
        transform.localScale = Vector3.one;
    }

    private void OnDestroy()
    {
        KillScaleTween();
        if (createdSelectionOutline && selectionOutline != null)
            Destroy(selectionOutline);
    }

    #endregion

    #region 指针反馈

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        AnimateToCurrentState();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        pressed = false;
        AnimateToCurrentState();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressed = true;
        AnimateToCurrentState();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pressed = false;
        AnimateToCurrentState();
    }

    #endregion

    #region 选中反馈

    public void OnSelect(BaseEventData eventData)
    {
        selected = true;
        ConfigureSelectedColor();
        SetSelectionVisual(true);
        AnimateToCurrentState();
    }

    public void OnDeselect(BaseEventData eventData)
    {
        selected = false;
        SetSelectionVisual(false);
        AnimateToCurrentState();
    }

    /// <summary>
    /// 确保运行时动态创建的普通按钮也具备统一的导航选中反馈。
    /// </summary>
    public static void EnsureFor(Transform root)
    {
        if (root == null || !Application.isPlaying)
            return;

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        EnsureFor(buttons);
    }

    /// <summary>复用面板按钮快照补齐视觉反馈，避免重复扫描层级。</summary>
    public static void EnsureFor(IReadOnlyList<Button> buttons)
    {
        if (buttons == null || !Application.isPlaying)
            return;

        for (int i = 0; i < buttons.Count; i++)
        {
            Button button = buttons[i];
            if (button == null || IsSlotButton(button.transform))
                continue;

            if (button.GetComponent<FlatWorldUIFeedback>() == null)
                button.gameObject.AddComponent<FlatWorldUIFeedback>();
        }
    }

    /// <summary>根据当前交互优先级重定向唯一缩放 Tween，暂停游戏时仍使用非缩放时间。</summary>
    private void AnimateToCurrentState()
    {
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            return;

        bool canInteract = selectable == null || selectable.IsInteractable();
        if (!canInteract && selected)
        {
            selected = false;
            RestoreSelectionOutline();
        }

        float target = !canInteract
            ? 1f
            : pressed ? pressedScale : selected ? selectedScale : hovered ? hoverScale : 1f;
        Vector3 targetScale = Vector3.one * target;
        KillScaleTween();
        if ((transform.localScale - targetScale).sqrMagnitude <= ScaleComparisonEpsilonSquared)
        {
            transform.localScale = targetScale;
            return;
        }

        float duration = Mathf.Max(MinimumTweenDuration, 2f / Mathf.Max(1f, response));
        scaleTween = transform
            .DOScale(targetScale, duration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .OnComplete(ClearScaleTweenReference);
    }

    /// <summary>动画自然完成后清除引用，避免后续状态切换重复操作已结束的 Tween。</summary>
    private void ClearScaleTweenReference() => scaleTween = null;

    /// <summary>终止当前缩放 Tween，不影响同一 Transform 上的其他动画。</summary>
    private void KillScaleTween()
    {
        Tween tween = scaleTween;
        scaleTween = null;
        if (tween != null && tween.active)
            tween.Kill(false);
    }

    /// <summary>
    /// 将普通按钮的选中颜色切换到主题强调色。
    /// </summary>
    private void ConfigureSelectedColor()
    {
        if (selectable == null)
            return;

        ColorBlock colors = selectable.colors;
        colors.selectedColor = FlatWorldUITheme.Selection;
        selectable.colors = colors;
    }

    /// <summary>
    /// 在按钮图形上复用或创建选中描边，不新增层级和布局节点。
    /// </summary>
    private void EnsureSelectionOutline()
    {
        if (selectable == null)
            return;

        Graphic targetGraphic = selectable.targetGraphic != null
            ? selectable.targetGraphic
            : GetComponent<Graphic>();
        if (targetGraphic == null)
            return;

        if (selectionOutline != null && selectionGraphic == targetGraphic)
            return;

        RestoreSelectionOutline();
        selectionGraphic = targetGraphic;
        selectionOutline = targetGraphic.GetComponent<Outline>();
        createdSelectionOutline = selectionOutline == null;

        if (createdSelectionOutline)
        {
            selectionOutline = targetGraphic.gameObject.AddComponent<Outline>();
            selectionOutline.enabled = false;
            selectionOutline.effectColor = Color.white;
            selectionOutline.effectDistance = Vector2.zero;
            selectionOutline.useGraphicAlpha = true;
        }

        outlineBaselineEnabled = selectionOutline.enabled;
        outlineBaselineColor = selectionOutline.effectColor;
        outlineBaselineDistance = selectionOutline.effectDistance;
        outlineBaselineUsesGraphicAlpha = selectionOutline.useGraphicAlpha;
        outlineBaselineCaptured = true;
    }

    /// <summary>
    /// 按选中状态应用高亮描边或恢复原有描边参数。
    /// </summary>
    private void SetSelectionVisual(bool isSelected)
    {
        EnsureSelectionOutline();
        if (selectionOutline == null || !outlineBaselineCaptured)
            return;

        if (isSelected)
        {
            selectionOutline.enabled = true;
            selectionOutline.effectColor = FlatWorldUITheme.SelectionOutline;
            selectionOutline.effectDistance = FlatWorldUITheme.SelectionOutlineDistance;
            selectionOutline.useGraphicAlpha = false;
            return;
        }

        RestoreSelectionOutline();
    }

    /// <summary>
    /// 恢复按钮原本的描边状态，避免取消选中后改变主题边框。
    /// </summary>
    private void RestoreSelectionOutline()
    {
        if (selectionOutline == null || !outlineBaselineCaptured)
            return;

        selectionOutline.enabled = outlineBaselineEnabled;
        selectionOutline.effectColor = outlineBaselineColor;
        selectionOutline.effectDistance = outlineBaselineDistance;
        selectionOutline.useGraphicAlpha = outlineBaselineUsesGraphicAlpha;
    }

    /// <summary>
    /// 判断按钮是否属于背包槽位，槽位保留原有拖拽与选中表现。
    /// </summary>
    private static bool IsSlotButton(Transform target)
    {
        for (Transform current = target; current != null; current = current.parent)
        {
            if (current.name.Equals("UI_Slot", StringComparison.OrdinalIgnoreCase)
                || current.name.Equals("物品槽", StringComparison.OrdinalIgnoreCase)
                || current.name.Equals("ItemSlot", StringComparison.OrdinalIgnoreCase))
                return true;

            Component[] components = current.GetComponents<Component>();
            foreach (Component component in components)
            {
                if (component == null)
                    continue;

                string typeName = component.GetType().Name;
                if (typeName.IndexOf("ItemSlot", StringComparison.OrdinalIgnoreCase) >= 0
                    || typeName.Equals("Slot_UI", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    #endregion
}
