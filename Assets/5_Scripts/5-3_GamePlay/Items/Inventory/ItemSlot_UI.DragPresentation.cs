using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 拖拽图标与槽位反馈。
public partial class ItemSlot_UI
{
    #region 拖拽图标与槽位反馈
    /// <summary>
    /// 创建跟随鼠标的半透明图标，让拖拽状态和携带物品清晰可见。
    /// </summary>
    private void CreateMouseDragGhost(Sprite draggedSprite, Vector2 screenPosition)
    {
        Canvas rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        if (rootCanvas == null || draggedSprite == null)
            return;

        GameObject ghostObject = new GameObject(
            "InventoryDragGhost",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasRenderer),
            typeof(Image));
        ghostObject.transform.SetParent(rootCanvas.transform, false);
        ghostObject.transform.SetAsLastSibling();

        // 拖拽图标使用独立顶层 Canvas；仅靠根 Canvas 的兄弟顺序会被快捷栏/模态页的独立 Canvas 压住。
        Canvas ghostCanvas = ghostObject.GetComponent<Canvas>();
        ghostCanvas.overrideSorting = true;
        ghostCanvas.sortingLayerID = rootCanvas.sortingLayerID;
        ghostCanvas.sortingOrder = UIManager.HeldItemSortingOrder;

        mouseDragGhost = ghostObject.GetComponent<RectTransform>();
        mouseDragGhost.sizeDelta = image != null
            ? image.rectTransform.rect.size
            : new Vector2(64f, 64f);

        Image ghostImage = ghostObject.GetComponent<Image>();
        ghostImage.sprite = draggedSprite;
        ghostImage.preserveAspect = true;
        ghostImage.raycastTarget = false;
        ghostImage.color = new Color(1f, 1f, 1f, 0.82f);
        // 复用来源槽的数量样式，数量与图标共用顶层 Canvas 和拖拽生命周期。
        TMP_Text ghostAmount = Instantiate(text, mouseDragGhost);
        ghostAmount.name = "拖拽数量";
        ghostAmount.gameObject.SetActive(true);
        ghostAmount.enabled = true;
        ghostAmount.raycastTarget = false;
        ghostAmount.text = activeDragTransaction.DraggedAmount.ToString("0.##");
        RectTransform amountRect = ghostAmount.rectTransform;
        amountRect.anchorMin = amountRect.anchorMax = amountRect.pivot = new Vector2(1f, 0f);
        amountRect.anchoredPosition = new Vector2(2f, -2f);
        amountRect.sizeDelta = new Vector2(Mathf.Max(64f, mouseDragGhost.sizeDelta.x), 28f);
        amountRect.localScale = Vector3.one;
        amountRect.localRotation = Quaternion.identity;
        ghostAmount.alignment = TextAlignmentOptions.BottomRight;
        PositionMouseDragGhost(screenPosition);
    }

    private void PositionMouseDragGhost(Vector2 screenPosition)
    {
        if (mouseDragGhost == null)
            return;

        Canvas rootCanvas = mouseDragGhost.parent != null
            ? mouseDragGhost.parent.GetComponentInParent<Canvas>()?.rootCanvas
            : null;
        RectTransform canvasRect = rootCanvas != null ? rootCanvas.transform as RectTransform : null;
        Camera eventCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? rootCanvas.worldCamera
            : null;
        if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenPosition, eventCamera, out Vector2 localPosition))
        {
            mouseDragGhost.anchoredPosition = localPosition;
        }
    }

    private void UpdateMouseDragHover(PointerEventData eventData)
    {
        pointerQuery.Capture(eventData);
        ItemSlot_UI nextSlot = pointerQuery.FindSlot();
        if (nextSlot != mouseDragHoverSlot)
        {
            SetMouseDragHover(mouseDragHoverSlot, false);
            mouseDragHoverSlot = nextSlot;
            SetMouseDragHover(mouseDragHoverSlot, true);
        }

        if (!IsTouchPointer(eventData) || !touchItemDragActive)
            return;

        if (pointerQuery.IsOverBlockingUI(eventData))
            CancelTouchWorldLongPress();
        else
            UpdateTouchWorldLongPress(eventData);
    }

    private void SetMouseDragHover(ItemSlot_UI slot, bool active)
    {
        if (slot == null)
            return;

        slot.EnsureSelectionOutline();
        if (slot.selectionOutline == null)
            return;

        if (active)
        {
            slot.dragHoverOutlineEnabled = slot.selectionOutline.enabled;
            slot.dragHoverOutlineColor = slot.selectionOutline.effectColor;
            slot.dragHoverOutlineDistance = slot.selectionOutline.effectDistance;
            slot.dragHoverOutlineUsesGraphicAlpha = slot.selectionOutline.useGraphicAlpha;
            slot.selectionOutline.enabled = true;
            slot.selectionOutline.effectColor = FlatWorldUITheme.AccentHover;
            slot.selectionOutline.effectDistance = FlatWorldUITheme.SelectionOutlineDistance;
            slot.selectionOutline.useGraphicAlpha = false;
            return;
        }

        slot.selectionOutline.enabled = slot.dragHoverOutlineEnabled;
        slot.selectionOutline.effectColor = slot.dragHoverOutlineColor;
        slot.selectionOutline.effectDistance = slot.dragHoverOutlineDistance;
        slot.selectionOutline.useGraphicAlpha = slot.dragHoverOutlineUsesGraphicAlpha;
    }

    private void EndMouseDragVisual()
    {
        mouseFlickGesture.Reset();
        SetMouseDragHover(mouseDragHoverSlot, false);
        mouseDragHoverSlot = null;
        if (mouseDragGhost != null)
            Destroy(mouseDragGhost.gameObject);
        mouseDragGhost = null;
    }

    /// <summary>结束当前拖拽事务并按最终数据恢复来源槽显示。</summary>
    private void CompleteActiveDrag(bool prepareFallback, bool refreshSource = true)
    {
        InventoryDragTransaction transaction = activeDragTransaction;
        activeDragTransaction = null;
        transaction?.Complete(prepareFallback);
        hideSourceContentWhileDragging = false;
        if (refreshSource)
            RefreshUI();
    }

    /// <summary>切换来源槽内容可见性，拖拽图标仍由独立幽灵节点显示。</summary>
    private void SetSourceContentHidden(bool hidden)
    {
        hideSourceContentWhileDragging = hidden;
        if (hidden)
        {
            HideSlotContent();
            return;
        }

        RefreshUI();
    }

    /// <summary>隐藏来源槽的物品图标和数量文本。</summary>
    private void HideSlotContent()
    {
        if (image != null)
            image.gameObject.SetActive(false);
        if (text != null)
            text.enabled = false;
    }

    #endregion
}
