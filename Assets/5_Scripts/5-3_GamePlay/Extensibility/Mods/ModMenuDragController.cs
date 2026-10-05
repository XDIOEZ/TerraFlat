using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>MOD 条目拖拽排序，共用面板接收手势，翻页不会丢失拖动中的条目。</summary>
[DisallowMultipleComponent]
public sealed class ModMenuDragController : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    #region 拖拽视图

    [SerializeField] private Button[] rows;
    [SerializeField] private RectTransform listBounds;
    [SerializeField] private RectTransform dragPreview;
    [SerializeField] private TMP_Text previewNumber;
    [SerializeField] private TMP_Text previewTitle;
    [SerializeField] private TMP_Text previewDescription;
    [SerializeField] private RectTransform insertionLine;
    [SerializeField] private Button previousPage;
    [SerializeField] private Button nextPage;
    [SerializeField] private float pageHoverSeconds = 0.7f;

    private Func<int, bool> canDrag;
    private Action<int> dragStarted;
    private Action<int> dropRequested;
    private Action<int> pageRequested;
    private Action dragCancelled;
    private RectTransform surface;
    private PointerEventData activePointer;
    private Vector3 previewOffset;
    private int dropSlot = -1;
    private int hoveredPage;
    private float pageHoverStarted;

    public void Bind(Func<int, bool> allowed, Action<int> started, Action<int> dropped,
        Action<int> changePage, Action cancelled)
    {
        CancelDrag();
        canDrag = allowed;
        dragStarted = started;
        dropRequested = dropped;
        pageRequested = changePage;
        dragCancelled = cancelled;
        surface = (RectTransform)transform;
    }

    #endregion

    #region 拖拽与跨页

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (activePointer != null || eventData.button != PointerEventData.InputButton.Left ||
            rows == null || dragPreview == null || listBounds == null || insertionLine == null)
            return;

        for (int slot = 0; slot < rows.Length; slot++)
        {
            Button row = rows[slot];
            if (row == null || !row.gameObject.activeInHierarchy || !row.IsInteractable() ||
                !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)row.transform,
                    eventData.pressPosition, eventData.pressEventCamera) || canDrag?.Invoke(slot) != true)
                continue;

            surface ??= (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(surface,
                    eventData.pressPosition, eventData.pressEventCamera, out Vector2 startPoint))
                return;

            // 拖动副本来自正式 Prefab，原条目保持布局和点击绑定。
            foreach (TMP_Text text in row.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.name.EndsWith("_序号", StringComparison.Ordinal)) previewNumber.text = text.text;
                else if (text.name.EndsWith("_标题", StringComparison.Ordinal)) previewTitle.text = text.text;
                else if (text.name.EndsWith("_说明", StringComparison.Ordinal)) previewDescription.text = text.text;
            }
            previewOffset = surface.InverseTransformPoint(row.transform.position) - (Vector3)startPoint;
            activePointer = eventData;
            eventData.eligibleForClick = false;
            dragPreview.gameObject.SetActive(true);
            dragPreview.SetAsLastSibling();
            insertionLine.SetAsLastSibling();
            dragStarted?.Invoke(slot);
            UpdateDrag(eventData);
            return;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (activePointer == null || eventData.pointerId != activePointer.pointerId) return;
        activePointer = eventData;
        UpdateDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (activePointer == null || eventData.pointerId != activePointer.pointerId) return;
        UpdateDrag(eventData);
        int target = dropSlot;
        ClearDragVisuals();
        if (target >= 0) dropRequested?.Invoke(target);
        else dragCancelled?.Invoke();
    }

    private void Update()
    {
        if (activePointer == null || hoveredPage == 0 ||
            Time.unscaledTime - pageHoverStarted < Mathf.Max(0.2f, pageHoverSeconds)) return;
        int direction = hoveredPage;
        pageHoverStarted = Time.unscaledTime;
        pageRequested?.Invoke(direction);
        UpdateDrag(activePointer);
    }

    private void UpdateDrag(PointerEventData eventData)
    {
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(surface, eventData.position,
                eventData.pressEventCamera, out Vector2 point))
            dragPreview.localPosition = (Vector3)point + previewOffset;

        int page = IsPageHit(previousPage, eventData) ? -1 : IsPageHit(nextPage, eventData) ? 1 : 0;
        if (page != hoveredPage)
        {
            hoveredPage = page;
            pageHoverStarted = Time.unscaledTime;
        }

        dropSlot = -1;
        insertionLine.gameObject.SetActive(false);
        if (!RectTransformUtility.RectangleContainsScreenPoint(listBounds, eventData.position,
                eventData.pressEventCamera)) return;

        RectTransform lastRow = null;
        for (int slot = 0; slot < rows.Length; slot++)
        {
            Button row = rows[slot];
            if (row == null || !row.gameObject.activeInHierarchy) break;
            var rect = (RectTransform)row.transform;
            lastRow = rect;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position,
                    eventData.pressEventCamera, out Vector2 rowPoint) && rowPoint.y >= rect.rect.center.y)
            {
                ShowInsertionLine(slot, rect, rect.rect.yMax);
                return;
            }
            dropSlot = slot + 1;
        }
        if (lastRow != null) ShowInsertionLine(dropSlot, lastRow, lastRow.rect.yMin);
    }

    private void ShowInsertionLine(int slot, RectTransform row, float localY)
    {
        dropSlot = slot;
        insertionLine.position = row.TransformPoint(new Vector3(row.rect.center.x, localY, 0f));
        insertionLine.gameObject.SetActive(true);
    }

    private static bool IsPageHit(Button button, PointerEventData eventData)
        => button != null && button.IsActive() && button.IsInteractable() &&
           RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform,
               eventData.position, eventData.pressEventCamera);

    public void CancelDrag()
    {
        bool wasDragging = activePointer != null;
        ClearDragVisuals();
        if (wasDragging) dragCancelled?.Invoke();
    }

    private void ClearDragVisuals()
    {
        activePointer = null;
        hoveredPage = 0;
        dropSlot = -1;
        if (dragPreview != null) dragPreview.gameObject.SetActive(false);
        if (insertionLine != null) insertionLine.gameObject.SetActive(false);
    }

    private void OnDisable() => CancelDrag();

    #endregion
}
