using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

[RequireComponent(typeof(RectTransform))]
public class UIDragResizer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("设置")]
    public float edgeWidth = 10f;
    public float minWidth = 100f;
    public float minHeight = 100f;
    public float maxWidth = 2000f;
    public float maxHeight = 2000f;

    [Header("光标(可选)")]
    public Texture2D horizontalResizeCursor;
    public Texture2D verticalResizeCursor;
    public Texture2D diagonalNE;
    public Texture2D diagonalNW;

    private RectTransform rt;
    private ResizeDir currentHover = ResizeDir.None;
    private ResizeDir lockedDir = ResizeDir.None;
    private bool isDragging = false;

    // 保存开始拖拽时状态（基于 RectTransform 本地空间）
    private Vector2 startLocalPoint;
    private Vector2 startSize;
    private Vector2 startAnchoredPos;

    private enum ResizeDir { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

    void Awake()
    {
        rt = GetComponent<RectTransform>();
    }

    void Update()
    {
        // 非拖拽时更新 hover 方向（使用本地坐标判断）
        if (isDragging) return;

        Vector2 local;
        Camera cam = null;
        // 尝试使用当前 EventCamera，如果没有就传 null（Screen Space Overlay）
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
        {
            // no-op: we don't rely on event camera here
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, Input.mousePosition, null, out local))
        {
            currentHover = ResizeDir.None;
            UpdateCursor();
            return;
        }

        Rect r = rt.rect;
        float aw = edgeWidth / Mathf.Max(rt.lossyScale.x, rt.lossyScale.y);

        bool left = local.x <= r.xMin + aw;
        bool right = local.x >= r.xMax - aw;
        bool top = local.y >= r.yMax - aw;
        bool bottom = local.y <= r.yMin + aw;

        if (left && top) currentHover = ResizeDir.TopLeft;
        else if (right && top) currentHover = ResizeDir.TopRight;
        else if (left && bottom) currentHover = ResizeDir.BottomLeft;
        else if (right && bottom) currentHover = ResizeDir.BottomRight;
        else if (left) currentHover = ResizeDir.Left;
        else if (right) currentHover = ResizeDir.Right;
        else if (top) currentHover = ResizeDir.Top;
        else if (bottom) currentHover = ResizeDir.Bottom;
        else currentHover = ResizeDir.None;

        UpdateCursor();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // 直接 rely on Update hover — no-op here
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isDragging)
        {
            currentHover = ResizeDir.None;
            UpdateCursor();
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right) return;
        if (currentHover == ResizeDir.None) return;

        // 锁定方向
        lockedDir = currentHover;
        isDragging = true;

        // 记录开始时在本地坐标系的鼠标位置（非常关键）
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out startLocalPoint);

        startSize = rt.sizeDelta;
        startAnchoredPos = rt.anchoredPosition;

        // 选中，避免其它 UI 打断
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(gameObject);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging || eventData.button != PointerEventData.InputButton.Right) return;
        if (lockedDir == ResizeDir.None) return;

        // 当前鼠标在本地坐标
        Vector2 currLocal;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out currLocal);

        Vector2 localDelta = currLocal - startLocalPoint; // localDelta.x -> 宽相关, localDelta.y -> 高相关

        // 先计算“未限制”的新 size（基于 startSize + delta）
        float newWidth = startSize.x;
        float newHeight = startSize.y;

        // 判断左右（互斥）
        bool isLeft = lockedDir == ResizeDir.Left || lockedDir == ResizeDir.TopLeft || lockedDir == ResizeDir.BottomLeft;
        bool isRight = lockedDir == ResizeDir.Right || lockedDir == ResizeDir.TopRight || lockedDir == ResizeDir.BottomRight;
        bool isTop = lockedDir == ResizeDir.Top || lockedDir == ResizeDir.TopLeft || lockedDir == ResizeDir.TopRight;
        bool isBottom = lockedDir == ResizeDir.Bottom || lockedDir == ResizeDir.BottomLeft || lockedDir == ResizeDir.BottomRight;

        // 对应的 size 变化（注意 localDelta 的符号）
        if (isLeft) newWidth = startSize.x - localDelta.x;     // 向左拉（local.x 增大 -> 宽变小）
        if (isRight) newWidth = startSize.x + localDelta.x;    // 向右拉

        if (isTop) newHeight = startSize.y + localDelta.y;     // 向上拉（local.y 增大）
        if (isBottom) newHeight = startSize.y - localDelta.y;  // 向下拉

        // clamp
        float clampedW = Mathf.Clamp(newWidth, minWidth, maxWidth);
        float clampedH = Mathf.Clamp(newHeight, minHeight, maxHeight);

        // 计算最终位置补偿（使用 startSize 与 clamped size，保证当 clamp 发生时位置也能对应）
        Vector2 finalAnchored = startAnchoredPos;
        float pivotX = rt.pivot.x;
        float pivotY = rt.pivot.y;

        // 当左边被抓住时，width 变化会让 anchoredPosition.x 移动：用 (startSize.x - clampedW) * (1 - pivotX)
        // 当右边被抓住时，使用 (startSize.x - clampedW) * (-pivotX)
        if (isLeft)
        {
            finalAnchored.x = startAnchoredPos.x + (startSize.x - clampedW) * (1 - pivotX);
        }
        else if (isRight)
        {
            finalAnchored.x = startAnchoredPos.x + (startSize.x - clampedW) * (-pivotX);
        }

        if (isBottom)
        {
            finalAnchored.y = startAnchoredPos.y + (startSize.y - clampedH) * (1 - pivotY);
        }
        else if (isTop)
        {
            finalAnchored.y = startAnchoredPos.y + (startSize.y - clampedH) * (-pivotY);
        }

        // 应用
        rt.sizeDelta = new Vector2(clampedW, clampedH);
        rt.anchoredPosition = finalAnchored;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;
        lockedDir = ResizeDir.None;
        // hover 会在 Update 里恢复
    }

    private void UpdateCursor()
    {
        ResizeDir d = isDragging ? lockedDir : currentHover;

        if (d == ResizeDir.Left || d == ResizeDir.Right)
        {
            if (horizontalResizeCursor != null) Cursor.SetCursor(horizontalResizeCursor, new Vector2(horizontalResizeCursor.width / 2, horizontalResizeCursor.height / 2), CursorMode.Auto);
            else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
        else if (d == ResizeDir.Top || d == ResizeDir.Bottom)
        {
            if (verticalResizeCursor != null) Cursor.SetCursor(verticalResizeCursor, new Vector2(verticalResizeCursor.width / 2, verticalResizeCursor.height / 2), CursorMode.Auto);
            else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
        else if (d == ResizeDir.TopRight || d == ResizeDir.BottomLeft)
        {
            if (diagonalNE != null) Cursor.SetCursor(diagonalNE, new Vector2(diagonalNE.width / 2, diagonalNE.height / 2), CursorMode.Auto);
            else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
        else if (d == ResizeDir.TopLeft || d == ResizeDir.BottomRight)
        {
            if (diagonalNW != null) Cursor.SetCursor(diagonalNW, new Vector2(diagonalNW.width / 2, diagonalNW.height / 2), CursorMode.Auto);
            else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
        else
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }
}
