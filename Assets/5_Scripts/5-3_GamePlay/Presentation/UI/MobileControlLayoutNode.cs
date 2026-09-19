using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 标记一个允许玩家在触屏布局编辑器中拖动的正式 HUD 控件。
/// 位置以 SafeAreaRoot 内的归一化中心坐标持久化，因此分辨率、刘海和 UI 缩放变化后仍保持相对布局。
/// </summary>
[DisallowMultipleComponent]
public sealed class MobileControlLayoutNode : MonoBehaviour,
    IPointerDownHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    #region 序列化配置

    [SerializeField] private string controlId;
    [SerializeField] private RectTransform layoutTarget;
    [SerializeField] private bool fixedMoveJoystickOnly;
    [SerializeField] private bool overrideGeometryForEditor;
    [SerializeField] private Vector2 editorAnchorMin;
    [SerializeField] private Vector2 editorAnchorMax;
    [SerializeField] private Vector2 editorPivot;
    [SerializeField] private Vector2 editorAnchoredPosition;
    [SerializeField] private Vector2 editorSizeDelta;

    #endregion

    #region 运行时状态

    private RectTransform editingRoot;
    private Graphic dragGraphic;
    private CanvasGroup dragCanvasGroup;
    private Vector2 pointerOffset;
    private bool editing;
    private bool originalRaycastTarget;
    private float originalCanvasAlpha;
    private bool originalCanvasBlocksRaycasts;
    private Vector3 baseLocalScale;
    private Vector2 baseAnchoredPosition;
    private bool scaleCaptured;
    private int dragPointerId = int.MinValue;

    public event System.Action<MobileControlLayoutNode> Selected;
    public bool SupportsSize => !fixedMoveJoystickOnly;
    public float SizeMultiplier { get; private set; } = 1f;

    public string ControlId => controlId;
    public bool FixedMoveJoystickOnly => fixedMoveJoystickOnly;
    public RectTransform LayoutTarget => layoutTarget != null
        ? layoutTarget
        : transform as RectTransform;

    #endregion

    #region 配置与布局

    /// <summary>由正式手机 HUD 构建器写入稳定控件 ID、实际移动目标和适用模式。</summary>
    public void Configure(string id, RectTransform target, bool fixedMoveOnly = false)
    {
        controlId = id;
        layoutTarget = target;
        fixedMoveJoystickOnly = fixedMoveOnly;
    }

    /// <summary>为浮动移动摇杆声明编辑器中的固定预览几何，不改变正常游戏的浮动捕获区。</summary>
    public void ConfigureEditorGeometry(
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 sizeDelta)
    {
        overrideGeometryForEditor = true;
        editorAnchorMin = anchorMin;
        editorAnchorMax = anchorMax;
        editorPivot = pivot;
        editorAnchoredPosition = anchoredPosition;
        editorSizeDelta = sizeDelta;
    }

    /// <summary>把特殊控件切到可拖动的编辑预览几何。</summary>
    public void PrepareForEditingPreview()
    {
        RectTransform target = LayoutTarget;
        if (!overrideGeometryForEditor || target == null)
            return;

        target.anchorMin = editorAnchorMin;
        target.anchorMax = editorAnchorMax;
        target.pivot = editorPivot;
        target.anchoredPosition = editorAnchoredPosition;
        target.sizeDelta = editorSizeDelta;
    }

    /// <summary>读取并应用玩家保存的位置覆盖；没有覆盖时保持 Prefab 当前布局。</summary>
    public bool ApplySavedPosition(RectTransform root)
    {
        if (SupportsSize)
        {
            CaptureBaseGeometry();
            LayoutTarget.localScale = baseLocalScale;
            LayoutTarget.anchoredPosition = baseAnchoredPosition;
            ApplySize(root, UIUserSettings.GetMobileControlSize(controlId));
        }
        if (!UIUserSettings.TryGetMobileControlLayoutPosition(controlId, out Vector2 position))
            return false;

        ApplyNormalizedPosition(root, position);
        return true;
    }

    /// <summary>每次从原始比例计算尺寸；视觉、射线矩形共同缩放，超小安全区只缩不溢出。</summary>
    public void ApplySize(RectTransform root, float multiplier)
    {
        RectTransform target = LayoutTarget;
        if (!SupportsSize || target == null || root == null)
            return;
        Vector2 center = CaptureNormalizedPosition(root);
        CaptureBaseGeometry();
        SizeMultiplier = UIUserSettings.SanitizeMobileControlSize(multiplier);
        target.localScale = new Vector3(baseLocalScale.x * SizeMultiplier,
            baseLocalScale.y * SizeMultiplier, baseLocalScale.z);
        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);
        Vector3 minimum = root.InverseTransformPoint(corners[0]);
        Vector3 maximum = minimum;
        for (int index = 1; index < corners.Length; index++)
        {
            Vector3 corner = root.InverseTransformPoint(corners[index]);
            minimum = Vector3.Min(minimum, corner);
            maximum = Vector3.Max(maximum, corner);
        }
        Vector3 extent = maximum - minimum;
        float fit = Mathf.Min(1f, root.rect.width / Mathf.Max(0.001f, extent.x),
            root.rect.height / Mathf.Max(0.001f, extent.y));
        target.localScale = new Vector3(baseLocalScale.x * SizeMultiplier * fit,
            baseLocalScale.y * SizeMultiplier * fit, baseLocalScale.z);
        ApplyNormalizedPosition(root, center);
    }

    /// <summary>只捕获一次 Prefab 基准；删除偏好后恢复锚点位置，不保留上次用户覆盖。</summary>
    private void CaptureBaseGeometry()
    {
        if (scaleCaptured)
            return;
        baseLocalScale = LayoutTarget.localScale;
        baseAnchoredPosition = LayoutTarget.anchoredPosition;
        scaleCaptured = true;
    }

    /// <summary>把目标中心放到根节点的归一化位置，并保证整个目标仍留在可用区域内。</summary>
    public void ApplyNormalizedPosition(RectTransform root, Vector2 normalizedPosition)
    {
        RectTransform target = LayoutTarget;
        if (root == null || target == null)
            return;

        Vector2 clamped = new Vector2(
            Mathf.Clamp01(normalizedPosition.x),
            Mathf.Clamp01(normalizedPosition.y));
        Rect rootRect = root.rect;
        Vector3 desiredWorldCenter = root.TransformPoint(new Vector3(
            Mathf.Lerp(rootRect.xMin, rootRect.xMax, clamped.x),
            Mathf.Lerp(rootRect.yMin, rootRect.yMax, clamped.y),
            0f));
        Vector3 currentWorldCenter = GetWorldCenter(target);
        target.position += desiredWorldCenter - currentWorldCenter;
        ClampInsideRoot(root, target);
    }

    /// <summary>把目标当前中心换算成根节点内的归一化位置。</summary>
    public Vector2 CaptureNormalizedPosition(RectTransform root)
    {
        RectTransform target = LayoutTarget;
        if (root == null || target == null)
            return new Vector2(0.5f, 0.5f);

        Vector2 localCenter = root.InverseTransformPoint(GetWorldCenter(target));
        Rect rootRect = root.rect;
        return new Vector2(
            Mathf.InverseLerp(rootRect.xMin, rootRect.xMax, localCenter.x),
            Mathf.InverseLerp(rootRect.yMin, rootRect.yMax, localCenter.y));
    }

    #endregion

    #region 编辑交互

    /// <summary>只在布局编辑器中打开拖拽射线；正常游戏保持原控件输入语义。</summary>
    public void SetEditing(RectTransform root, bool value)
    {
        if (editing == value && editingRoot == root)
            return;

        if (editing)
            RestoreHandleVisualState();

        editing = value;
        dragPointerId = int.MinValue;
        editingRoot = value ? root : null;
        if (!editing)
            return;

        dragGraphic = GetComponent<Graphic>();
        if (dragGraphic != null)
        {
            originalRaycastTarget = dragGraphic.raycastTarget;
            dragGraphic.raycastTarget = true;
        }

        dragCanvasGroup = GetComponent<CanvasGroup>();
        if (dragCanvasGroup != null)
        {
            originalCanvasAlpha = dragCanvasGroup.alpha;
            originalCanvasBlocksRaycasts = dragCanvasGroup.blocksRaycasts;
            dragCanvasGroup.alpha = 1f;
            dragCanvasGroup.blocksRaycasts = true;
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!editing || eventData == null)
            return;
        Selected?.Invoke(this);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!editing || editingRoot == null || LayoutTarget == null ||
            eventData == null || dragPointerId != int.MinValue)
            return;

        if (!TryGetPointerLocalPosition(eventData, out Vector2 pointerLocal))
            return;

        Vector2 centerLocal = editingRoot.InverseTransformPoint(GetWorldCenter(LayoutTarget));
        dragPointerId = eventData.pointerId;
        Selected?.Invoke(this);
        pointerOffset = centerLocal - pointerLocal;
        eventData.Use();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!editing || editingRoot == null || LayoutTarget == null ||
            eventData == null || eventData.pointerId != dragPointerId)
            return;

        if (!TryGetPointerLocalPosition(eventData, out Vector2 pointerLocal))
            return;

        Vector2 desiredCenter = pointerLocal + pointerOffset;
        Rect rootRect = editingRoot.rect;
        ApplyNormalizedPosition(
            editingRoot,
            new Vector2(
                Mathf.InverseLerp(rootRect.xMin, rootRect.xMax, desiredCenter.x),
                Mathf.InverseLerp(rootRect.yMin, rootRect.yMax, desiredCenter.y)));
        eventData.Use();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerId != dragPointerId)
            return;
        dragPointerId = int.MinValue;
        if (editing)
            eventData.Use();
    }

    private bool TryGetPointerLocalPosition(PointerEventData eventData, out Vector2 position)
    {
        position = default;
        return eventData != null &&
               RectTransformUtility.ScreenPointToLocalPointInRectangle(
                   editingRoot,
                   eventData.position,
                   eventData.pressEventCamera,
                   out position);
    }

    private void OnDisable()
    {
        dragPointerId = int.MinValue;
        if (editing)
        {
            RestoreHandleVisualState();
            editing = false;
            editingRoot = null;
        }
    }

    /// <summary>恢复正式 Prefab 原本的射线和透明度，避免编辑状态泄漏到游戏 HUD。</summary>
    private void RestoreHandleVisualState()
    {
        if (dragGraphic != null)
            dragGraphic.raycastTarget = originalRaycastTarget;
        if (dragCanvasGroup != null)
        {
            dragCanvasGroup.alpha = originalCanvasAlpha;
            dragCanvasGroup.blocksRaycasts = originalCanvasBlocksRaycasts;
        }

        dragGraphic = null;
        dragCanvasGroup = null;
    }

    #endregion

    #region 几何工具

    private static Vector3 GetWorldCenter(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return (corners[0] + corners[2]) * 0.5f;
    }

    /// <summary>在根坐标中计算越界修正，确保按钮与摇杆整体留在安全区。</summary>
    private static void ClampInsideRoot(RectTransform root, RectTransform target)
    {
        Vector3[] corners = new Vector3[4];
        target.GetWorldCorners(corners);

        float minX = float.PositiveInfinity;
        float maxX = float.NegativeInfinity;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        for (int index = 0; index < corners.Length; index++)
        {
            Vector3 local = root.InverseTransformPoint(corners[index]);
            minX = Mathf.Min(minX, local.x);
            maxX = Mathf.Max(maxX, local.x);
            minY = Mathf.Min(minY, local.y);
            maxY = Mathf.Max(maxY, local.y);
        }

        Rect bounds = root.rect;
        float deltaX = 0f;
        float deltaY = 0f;
        if (maxX - minX > bounds.width)
            deltaX = bounds.center.x - (minX + maxX) * 0.5f;
        else if (minX < bounds.xMin)
            deltaX = bounds.xMin - minX;
        else if (maxX > bounds.xMax)
            deltaX = bounds.xMax - maxX;

        if (maxY - minY > bounds.height)
            deltaY = bounds.center.y - (minY + maxY) * 0.5f;
        else if (minY < bounds.yMin)
            deltaY = bounds.yMin - minY;
        else if (maxY > bounds.yMax)
            deltaY = bounds.yMax - maxY;

        if (!Mathf.Approximately(deltaX, 0f) || !Mathf.Approximately(deltaY, 0f))
            target.position += root.TransformVector(new Vector3(deltaX, deltaY, 0f));
    }

    #endregion
}
