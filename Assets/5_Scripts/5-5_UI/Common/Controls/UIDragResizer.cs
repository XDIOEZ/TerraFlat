using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 通过右键拖拽调整 RectTransform 四边与四角，并限制最小/最大尺寸。
/// 边缘命中只在 EventSystem 指针事件发生时计算，静止状态不会保留组件级帧循环。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class UIDragResizer : MonoBehaviour,
    IPointerEnterHandler,
    IPointerMoveHandler,
    IPointerExitHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    #region 配置与状态

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
    
    // 添加鼠标样式控制开关，默认关闭
    [Header("鼠标样式控制")]
    public bool useCustomCursor = false;

    private RectTransform rt;
    private UI_Drag windowDragger;
    private ResizeDir currentHover = ResizeDir.None;
    private ResizeDir lockedDir = ResizeDir.None;
    private bool isDragging = false;

    // 固定拖动开始时的坐标系，避免窗口移动或缩放后反过来改变鼠标增量。
    private Matrix4x4 startWorldToLocal;
    private Vector3 startLocalScale;
    private Vector2 startLocalPoint;
    private Vector2 startSize;
    private Vector2 startAnchoredPos;

    private enum ResizeDir { None, Left, Right, Top, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

    #endregion

    #region 生命周期与指针事件

    private void Awake()
    {
        rt = GetComponent<RectTransform>();
        windowDragger = GetComponent<UI_Drag>();
    }

    private void OnDisable()
    {
        isDragging = false;
        lockedDir = ResizeDir.None;
        currentHover = ResizeDir.None;
        UpdateCursor();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        RefreshHoverDirection(eventData.position, eventData.enterEventCamera);
    }

    /// <summary>仅在指针实际移动时重新计算命中的缩放边缘。</summary>
    public void OnPointerMove(PointerEventData eventData)
    {
        if (!isDragging)
            RefreshHoverDirection(eventData.position, eventData.enterEventCamera);
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
        RefreshHoverDirection(eventData.position, eventData.pressEventCamera);
        if (currentHover == ResizeDir.None) return;

        // 锁定方向
        lockedDir = currentHover;
        isDragging = true;

        startWorldToLocal = rt.worldToLocalMatrix;
        startLocalScale = rt.localScale;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, eventData.position, eventData.pressEventCamera, out startLocalPoint);

        startSize = rt.rect.size;
        startAnchoredPos = rt.anchoredPosition;

        // 选中，避免其它 UI 打断
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(gameObject);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging || eventData.button != PointerEventData.InputButton.Right) return;
        if (lockedDir == ResizeDir.None) return;

        if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                rt, eventData.position, eventData.pressEventCamera, out Vector3 worldPoint)) return;
        Vector2 localDelta = (Vector2)startWorldToLocal.MultiplyPoint3x4(worldPoint) - startLocalPoint;
        bool isLeft = lockedDir == ResizeDir.Left || lockedDir == ResizeDir.TopLeft || lockedDir == ResizeDir.BottomLeft;
        bool isRight = lockedDir == ResizeDir.Right || lockedDir == ResizeDir.TopRight || lockedDir == ResizeDir.BottomRight;
        bool isTop = lockedDir == ResizeDir.Top || lockedDir == ResizeDir.TopLeft || lockedDir == ResizeDir.TopRight;
        bool isBottom = lockedDir == ResizeDir.Bottom || lockedDir == ResizeDir.BottomLeft || lockedDir == ResizeDir.BottomRight;

        Vector2 size = new Vector2(
            Mathf.Clamp(startSize.x + (isRight ? localDelta.x : isLeft ? -localDelta.x : 0f), minWidth, maxWidth),
            Mathf.Clamp(startSize.y + (isTop ? localDelta.y : isBottom ? -localDelta.y : 0f), minHeight, maxHeight));
        Vector2 change = size - startSize;
        Vector3 compensation = new Vector3(
            change.x * (isRight ? rt.pivot.x : isLeft ? rt.pivot.x - 1f : 0f),
            change.y * (isTop ? rt.pivot.y : isBottom ? rt.pivot.y - 1f : 0f), 0f);
        compensation = rt.localRotation * Vector3.Scale(compensation, startLocalScale);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
        rt.anchoredPosition = startAnchoredPos + (Vector2)compensation;
        windowDragger?.KeepVisibleWindowInsideParentBounds();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        isDragging = false;
        lockedDir = ResizeDir.None;
        RefreshHoverDirection(eventData.position, eventData.enterEventCamera);
    }

    #endregion

    #region 边缘计算与光标

    /// <summary>把屏幕指针位置转换为面板本地坐标，并计算当前缩放方向。</summary>
    private void RefreshHoverDirection(Vector2 screenPosition, Camera eventCamera)
    {
        if (rt == null || isDragging)
            return;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rt,
                screenPosition,
                eventCamera,
                out Vector2 local))
        {
            currentHover = ResizeDir.None;
            UpdateCursor();
            return;
        }

        Rect rect = rt.rect;
        if (!rect.Contains(local))
        {
            currentHover = ResizeDir.None;
            UpdateCursor();
            return;
        }

        float scale = Mathf.Max(
            0.0001f,
            Mathf.Max(Mathf.Abs(rt.lossyScale.x), Mathf.Abs(rt.lossyScale.y)));
        float activeEdgeWidth = edgeWidth / scale;
        bool left = local.x <= rect.xMin + activeEdgeWidth;
        bool right = local.x >= rect.xMax - activeEdgeWidth;
        bool top = local.y >= rect.yMax - activeEdgeWidth;
        bool bottom = local.y <= rect.yMin + activeEdgeWidth;

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

    private void UpdateCursor()
    {
        // 如果不使用自定义光标，直接返回
        if (!useCustomCursor)
            return;
            
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

    #endregion
}
