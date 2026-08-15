using UnityEngine;

/// <summary>
/// 让安全区节点内的装饰层反向扩展到根 Canvas 全屏范围。
/// 用于主菜单背景等非交互内容；交互控件仍由父级安全区约束，避免刘海遮挡。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class FullScreenRectController : MonoBehaviour
{
    #region 缓存

    private readonly Vector3[] canvasWorldCorners = new Vector3[4];
    private RectTransform target;
    private RectTransform parentRect;
    private RectTransform canvasRect;
    private UIManager subscribedUIManager;
    private bool isApplying;

    #endregion

    #region 生命周期

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        BindUIManager();
        ApplyFullScreenRect();
    }

    private void OnDisable()
    {
        if (subscribedUIManager != null)
            subscribedUIManager.InteractionSurfaceChanged -= ApplyFullScreenRect;
        subscribedUIManager = null;
    }

    private void OnTransformParentChanged()
    {
        ResolveReferences();
        BindUIManager();
        ApplyFullScreenRect();
    }

    private void OnCanvasHierarchyChanged()
    {
        ResolveReferences();
        ApplyFullScreenRect();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (!isApplying && isActiveAndEnabled)
            ApplyFullScreenRect();
    }

    #endregion

    #region 全屏换算

    /// <summary>把当前矩形换算为根 Canvas 在父节点坐标系中的完整边界。</summary>
    public void ApplyFullScreenRect()
    {
        if (isApplying)
            return;

        ResolveReferences();
        if (target == null || parentRect == null || canvasRect == null)
            return;

        canvasRect.GetWorldCorners(canvasWorldCorners);
        Vector3 localBottomLeft = parentRect.InverseTransformPoint(canvasWorldCorners[0]);
        Vector3 localTopRight = parentRect.InverseTransformPoint(canvasWorldCorners[2]);
        Vector2 canvasCenter = (localBottomLeft + localTopRight) * 0.5f;
        Vector2 canvasSize = localTopRight - localBottomLeft;

        isApplying = true;
        target.anchorMin = Vector2.one * 0.5f;
        target.anchorMax = Vector2.one * 0.5f;
        target.pivot = Vector2.one * 0.5f;
        target.anchoredPosition = canvasCenter - parentRect.rect.center;
        target.sizeDelta = canvasSize;
        isApplying = false;
    }

    private void ResolveReferences()
    {
        if (target == null)
            target = (RectTransform)transform;

        parentRect = target.parent as RectTransform;
        Canvas canvas = GetComponentInParent<Canvas>();
        canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
    }

    /// <summary>跟随安全区横向翻转或偏好变更，补偿只变位置不变尺寸的情况。</summary>
    private void BindUIManager()
    {
        UIManager nextManager = UIManager.ExistingInstance;
        if (subscribedUIManager == nextManager)
            return;

        if (subscribedUIManager != null)
            subscribedUIManager.InteractionSurfaceChanged -= ApplyFullScreenRect;

        subscribedUIManager = nextManager;
        if (subscribedUIManager != null)
            subscribedUIManager.InteractionSurfaceChanged += ApplyFullScreenRect;
    }

    #endregion
}
