using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>鼠标悬浮与触屏长按共用的物品介绍，汇总定义、实例参数和模块返回文本。</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public sealed class InventoryItemTooltip : MonoBehaviour
{
    #region 配置

    private const float ScreenEdgeMargin = 8f;
    public const double MouseHoverDelaySeconds = 0.3d;
    private readonly WaitForSecondsRealtime refreshInterval = new WaitForSecondsRealtime(1f);

    [SerializeField] private RectTransform panelRect;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image background;
    [SerializeField] private Outline outline;
    [SerializeField] private Image accent;
    [SerializeField, Min(240f)] private float panelWidth = 360f;
    [SerializeField, Min(0f)] private float contentPadding = 14f;
    [SerializeField, Min(0f)] private float titleHeight = 30f;
    [SerializeField, Min(0f)] private float sectionGap = 8f;
    [SerializeField, Min(0f)] private float pointerOffset = 18f;

    #endregion

    #region 运行时

    private static InventoryItemTooltip current;
    private ItemSlot_UI owner;
    private Vector2 pointerScreenPosition;
    private Coroutine refreshRoutine;
    private bool visible;
    private bool hoverPending;
    private double hoverStartedAt;
    private Vector2 lastContainerSize;
    private bool touchPinned;
    private int touchShownFrame;
    private ItemSlot ownerSlot;
    private ItemData ownerItem;
    private BasePanel ownerPanel;
    private CanvasGroup[] ownerGroups;
    private readonly ItemInfoBuffer information = new();
    private readonly List<string> parameterLines = new(12);
    private readonly IItemTooltipInfoProvider infoProvider = ItemMoistureTooltipInfoProvider.Default;

    public static void Show(ItemSlot_UI slot, GameObject prefab, Vector2 screenPosition)
    {
        if (current != null && current.visible && current.touchPinned) return;
        ShowInternal(slot, prefab, screenPosition, false);
    }

    public static bool ShowTouch(ItemSlot_UI slot, GameObject prefab, Vector2 screenPosition)
    {
        ShowInternal(slot, prefab, screenPosition, true);
        return IsTouchPinned(slot);
    }

    private static void ShowInternal(ItemSlot_UI slot, GameObject prefab, Vector2 screenPosition, bool pinTouch)
    {
        if (slot == null || prefab == null || slot.GetBoundSlotData()?.itemData == null)
        {
            Hide(slot);
            return;
        }

        InventoryItemTooltip tooltip = EnsureInstance(prefab);
        if (tooltip == null)
            return;

        tooltip.owner = slot;
        tooltip.touchPinned = pinTouch;
        tooltip.touchShownFrame = Time.frameCount;
        tooltip.ownerSlot = slot.GetBoundSlotData();
        tooltip.ownerItem = tooltip.ownerSlot?.itemData;
        tooltip.ownerPanel = slot.GetComponentInParent<BasePanel>();
        tooltip.ownerGroups = slot.GetComponentsInParent<CanvasGroup>();
        tooltip.pointerScreenPosition = screenPosition;
        tooltip.hoverPending = !pinTouch;
        tooltip.hoverStartedAt = Time.unscaledTimeAsDouble;
        if (!pinTouch)
        {
            // 等待期间也保留真实槽位身份，离开、重绑或面板隐藏都会取消。
            tooltip.visible = false;
            tooltip.canvasGroup.alpha = 0f;
            return;
        }
        tooltip.Reveal();
    }

    public static bool HasCompletedMouseHover(double startedAt, double now)
        => now - startedAt >= MouseHoverDelaySeconds;

    private void Reveal()
    {
        hoverPending = false;
        visible = true;
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        transform.SetAsLastSibling();
        RefreshContent();
        PositionPanel();
        RestartRefreshRoutine();
    }

    public static void Move(ItemSlot_UI slot, Vector2 screenPosition)
    {
        if (current == null || (!current.visible && !current.hoverPending) || current.owner != slot || current.touchPinned)
            return;

        current.pointerScreenPosition = screenPosition;
        current.PositionPanel();
    }

    public static void Refresh(ItemSlot_UI slot)
    {
        if (current == null || !current.visible || current.owner != slot)
            return;

        current.RefreshContent();
        current.PositionPanel();
    }

    public static void Hide(ItemSlot_UI slot)
    {
        if (current == null || (slot != null && current.owner != slot))
            return;

        current.HideInternal();
    }

    public static bool IsTouchPinned(ItemSlot_UI slot)
        => current != null && current.visible && current.touchPinned && current.owner == slot;

    // 点击窗外时只收起介绍，不拦截这次点击本身的正常用途。
    public static void DismissTouchAt(Vector2 screenPosition)
    {
        if (current == null || !current.visible || !current.touchPinned) return;
        Canvas canvas = current.GetComponentInParent<Canvas>()?.rootCanvas;
        Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (!RectTransformUtility.RectangleContainsScreenPoint(current.panelRect, screenPosition, camera))
            current.HideInternal();
    }

    internal static bool MatchesItem(ItemData currentItem, ItemData expected)
        => currentItem != null && expected != null && (ReferenceEquals(currentItem, expected) ||
            currentItem.Guid != 0 && currentItem.Guid == expected.Guid && currentItem.IDName == expected.IDName);

    private void LateUpdate()
    {
        if (hoverPending)
        {
            if (!HasValidOwner()) { HideInternal(); return; }
            if (HasCompletedMouseHover(hoverStartedAt, Time.unscaledTimeAsDouble)) Reveal();
        }
        if (!visible) return;
        if (!HasValidOwner()) { HideInternal(); return; }
        if (!touchPinned) return;
        if (Time.frameCount == touchShownFrame) return;
        Touchscreen screen = Touchscreen.current;
        if (screen != null)
            foreach (var touch in screen.touches)
                if (touch.press.wasPressedThisFrame) DismissTouchAt(touch.position.ReadValue());
        Mouse mouse = Mouse.current;
        if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame ||
                              mouse.middleButton.wasPressedThisFrame))
            DismissTouchAt(mouse.position.ReadValue());
    }

    private bool HasValidOwner()
    {
        if (owner == null || !owner.isActiveAndEnabled ||
            (ownerPanel != null && !ownerPanel.IsOpen()) ||
            !ReferenceEquals(owner.GetBoundSlotData(), ownerSlot) ||
            !MatchesItem(ownerSlot?.itemData, ownerItem) ||
            (touchPinned && owner.CanTouchInspectItem?.Invoke(owner.slotIndex) != true))
            return false;
        if (ownerGroups != null)
            foreach (CanvasGroup group in ownerGroups)
                if (group != null && group.alpha <= 0.001f) return false;
        return true;
    }

    private static InventoryItemTooltip EnsureInstance(GameObject prefab)
    {
        if (current != null)
            return current;

        Canvas rootCanvas = UIManager.Instance?.RootCanvas;
        if (rootCanvas == null)
            return null;

        // 提示框属于交互内容，跟随安全区而不是全屏装饰层。
        RectTransform parent = UIManager.Instance.SafeAreaRoot ?? (RectTransform)rootCanvas.transform;
        GameObject instance = Instantiate(prefab, parent, false);
        instance.name = prefab.name;
        current = instance.GetComponent<InventoryItemTooltip>();
        if (current == null)
        {
            Debug.LogError("[InventoryItemTooltip] UI_ItemTooltip.prefab 缺少 InventoryItemTooltip 组件。", instance);
            Destroy(instance);
            return null;
        }

        current.EnsureReferences();
        // 独立画布跨过场景机器与常驻背包的根排序，所有槽位共用同一层级且不参与射线。
        Canvas tooltipCanvas = current.GetComponent<Canvas>();
        tooltipCanvas.overrideSorting = true;
        tooltipCanvas.sortingLayerID = rootCanvas.sortingLayerID;
        tooltipCanvas.sortingOrder = UIManager.ItemTooltipSortingOrder;
        current.HideInternal();
        return current;
    }

    private void Awake()
    {
        EnsureReferences();
        ApplyTheme();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void OnDestroy()
    {
        if (current == this)
            current = null;
    }

    private void EnsureReferences()
    {
        panelRect ??= transform as RectTransform;
        canvasGroup ??= GetComponent<CanvasGroup>();
        if (panelRect == null || titleText == null || bodyText == null || canvasGroup == null)
            Debug.LogError("[InventoryItemTooltip] 正式 Prefab 引用不完整。", this);
    }

    /// <summary>悬浮面板只从全局 UI 主题读取外观参数，避免形成第二套库存视觉规范。</summary>
    private void ApplyTheme()
    {
        if (background != null)
            background.color = FlatWorldUITheme.SurfaceLow;
        if (outline != null)
        {
            outline.effectColor = FlatWorldUITheme.Border;
            outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;
        }
        if (accent != null)
            accent.color = FlatWorldUITheme.ItemTooltipAccent;
        if (titleText != null)
            titleText.color = FlatWorldUITheme.TextPrimary;
        if (bodyText != null)
            bodyText.color = FlatWorldUITheme.TextSecondary;
    }

    private void RestartRefreshRoutine()
    {
        if (refreshRoutine != null)
            StopCoroutine(refreshRoutine);
        refreshInterval.Reset();
        refreshRoutine = StartCoroutine(RefreshWhileVisible());
    }

    private IEnumerator RefreshWhileVisible()
    {
        while (visible)
        {
            yield return refreshInterval;
            if (!visible)
                break;
            RefreshContent();
            PositionPanel();
        }

        refreshRoutine = null;
    }

    private void HideInternal()
    {
        visible = false;
        hoverPending = false;
        touchPinned = false;
        ownerPanel = null;
        ownerGroups = null;
        ownerSlot = null;
        ownerItem = null;
        owner = null;
        if (refreshRoutine != null)
        {
            StopCoroutine(refreshRoutine);
            refreshRoutine = null;
        }

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    #endregion

    #region 内容

    private void RefreshContent()
    {
        if (!HasValidOwner()) { HideInternal(); return; }
        ItemData itemData = owner?.GetBoundSlotData()?.itemData;
        if (itemData == null)
        {
            HideInternal();
            return;
        }

        RuntimeItemDefinition definition = null;
        GameRes resources = GameRes.ExistingInstance;
        resources?.TryGetItemDefinition(itemData.IDName, out definition);

        string title = definition?.DisplayName ??
            (!string.IsNullOrWhiteSpace(itemData.GameName) ? itemData.GameName : itemData.IDName);

        information.Clear();
        parameterLines.Clear();
        Inventory inventory = owner.BoundInventory;
        string worldKey = inventory?.item != null ? inventory.item.gameObject.scene.name : inventory?.MachineOwner?.ScopeKey;
        ItemInfoQuery.Collect(itemData, definition?.ModuleDefinitions, information,
            ItemInfoQuery.ReadClock(worldKey));
        infoProvider.AppendItemTooltipInfo(new ItemTooltipInfoContext(itemData, definition, information), parameterLines);

        string description = definition?.Description?.Trim() ?? string.Empty;
        string parameterText = string.Join("\n", parameterLines);
        string body = string.IsNullOrEmpty(description)
            ? parameterText
            : string.IsNullOrEmpty(parameterText)
                ? description
                : description + "\n\n" + parameterText;

        // 动态属性未变化时不重排整个提示框，避免周期刷新造成布局抖动。
        if (string.Equals(titleText.text, title, StringComparison.Ordinal) &&
            string.Equals(bodyText.text, body, StringComparison.Ordinal))
            return;

        titleText.text = title;
        bodyText.text = body;
        RebuildLayout();
    }

    private void RebuildLayout()
    {
        if (panelRect == null || titleText == null || bodyText == null)
            return;

        RectTransform container = panelRect.parent as RectTransform;
        Rect bounds = container != null ? container.rect : panelRect.rect;
        lastContainerSize = bounds.size;
        float width = Mathf.Min(panelWidth, Mathf.Max(1f, bounds.width - ScreenEdgeMargin * 2f));
        float innerWidth = Mathf.Max(1f, width - contentPadding * 2f);
        float preferredBodyHeight = Mathf.Max(
            22f,
            bodyText.GetPreferredValues(bodyText.text, innerWidth, 0f).y);
        float chromeHeight = contentPadding * 2f + titleHeight + sectionGap;
        float availableHeight = Mathf.Max(1f, bounds.height - ScreenEdgeMargin * 2f);
        float bodyHeight = Mathf.Min(preferredBodyHeight, Mathf.Max(1f, availableHeight - chromeHeight));
        float panelHeight = contentPadding + titleHeight + sectionGap + bodyHeight + contentPadding;

        // 极长的说明至少显示截断标记，不再让文字溢出屏幕边界。
        bodyText.overflowMode = bodyHeight < preferredBodyHeight - 0.5f
            ? TextOverflowModes.Ellipsis
            : TextOverflowModes.Overflow;
        panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight);

        RectTransform titleRect = titleText.rectTransform;
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(0f, 1f);
        titleRect.pivot = new Vector2(0f, 1f);
        titleRect.anchoredPosition = new Vector2(contentPadding, -contentPadding);
        titleRect.sizeDelta = new Vector2(innerWidth, titleHeight);

        RectTransform bodyRect = bodyText.rectTransform;
        bodyRect.anchorMin = new Vector2(0f, 1f);
        bodyRect.anchorMax = new Vector2(0f, 1f);
        bodyRect.pivot = new Vector2(0f, 1f);
        bodyRect.anchoredPosition = new Vector2(
            contentPadding,
            -(contentPadding + titleHeight + sectionGap));
        bodyRect.sizeDelta = new Vector2(innerWidth, bodyHeight);
    }

    #endregion

    #region 定位

    private void PositionPanel()
    {
        if (!visible || panelRect == null)
            return;

        Canvas rootCanvas = UIManager.ExistingInstance?.RootCanvas;
        RectTransform rootRect = panelRect.parent as RectTransform;
        if (rootCanvas == null || rootRect == null)
            return;

        if (lastContainerSize != rootRect.rect.size)
            RebuildLayout();

        Camera camera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rootRect,
                pointerScreenPosition,
                camera,
                out Vector2 localPoint))
        {
            return;
        }

        float width = panelRect.rect.width;
        float height = panelRect.rect.height;
        Rect bounds = rootRect.rect;
        float left = bounds.xMin + ScreenEdgeMargin;
        float right = bounds.xMax - ScreenEdgeMargin;
        float top = bounds.yMax - ScreenEdgeMargin;
        float bottom = bounds.yMin + ScreenEdgeMargin;

        float x = localPoint.x + pointerOffset;
        if (x + width > right)
            x = localPoint.x - pointerOffset - width;
        x = Mathf.Clamp(x, left, Mathf.Max(left, right - width));

        float y = localPoint.y - pointerOffset;
        y = Mathf.Clamp(y, Mathf.Min(top, bottom + height), top);

        Vector2 anchor = new Vector2(
            Mathf.Lerp(bounds.xMin, bounds.xMax, panelRect.anchorMin.x),
            Mathf.Lerp(bounds.yMin, bounds.yMax, panelRect.anchorMin.y));
        panelRect.anchoredPosition = new Vector2(x, y) - anchor;
    }

    #endregion
}
