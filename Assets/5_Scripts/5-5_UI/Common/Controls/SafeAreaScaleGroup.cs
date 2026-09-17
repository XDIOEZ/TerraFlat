using UnityEngine;

/// <summary>
/// 将一组固定尺寸 UI 在可用安全区不足时等比缩小；仅做“向下限幅”，空间足够时保持 Prefab 原始比例。
/// 适用于主菜单固定卡片等内部采用绝对参考尺寸排版、但外层需要随手机高度自动适配的界面。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class SafeAreaScaleGroup : MonoBehaviour
{
    #region 配置

    [SerializeField]
    private RectTransform boundsTarget;

    [SerializeField]
    private RectTransform[] scaledTargets = new RectTransform[0];

    [SerializeField]
    private Vector2 safeMargin = new Vector2(24f, 24f);

    [SerializeField]
    private float maxScale = 1f;

    #endregion

    #region 运行时状态

    private RectTransform availableRect;
    private Vector3[] baseLocalScales;
    private UIManager subscribedUIManager;
    private bool isApplying;

    #endregion

    #region 配置入口

    /// <summary>由 Prefab 构建器写入需要共同缩放的固定尺寸节点。</summary>
    public void Configure(RectTransform targetBounds, RectTransform[] targets, Vector2 margin)
    {
        boundsTarget = targetBounds;
        scaledTargets = targets ?? new RectTransform[0];
        safeMargin = new Vector2(Mathf.Max(0f, margin.x), Mathf.Max(0f, margin.y));
        maxScale = 1f;

        if (Application.isPlaying)
        {
            CaptureBaseScales(force: true);
            ApplyScale();
        }
    }

    #endregion

    #region 生命周期

    private void Awake()
    {
        ResolveReferences();
        CaptureBaseScales(force: true);
    }

    private void OnEnable()
    {
        UIUserSettings.Changed -= ApplyScale;
        UIUserSettings.Changed += ApplyScale;
        BindUIManager();
        ApplyScale();
    }

    private void OnDisable()
    {
        UnbindCallbacks();
    }

    private void OnDestroy()
    {
        // 同一 GameObject 上其它组件的 OnDisable 顺序不受保证；销毁阶段再次幂等清理，
        // 避免 UIManager 的事件链保留已销毁的 Unity 对象委托。
        UnbindCallbacks();
    }

    private void OnTransformParentChanged()
    {
        ResolveReferences();
        BindUIManager();
        ApplyScale();
    }

    private void OnCanvasHierarchyChanged()
    {
        BindUIManager();
        ApplyScale();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (!isApplying && isActiveAndEnabled)
            ApplyScale();
    }

    private void OnValidate()
    {
        safeMargin.x = Mathf.Max(0f, safeMargin.x);
        safeMargin.y = Mathf.Max(0f, safeMargin.y);
        maxScale = Mathf.Max(0.01f, maxScale);
    }

    #endregion

    #region 缩放计算

    /// <summary>根据当前安全区可用宽高计算统一缩放，避免顶部、底部或左右越界。</summary>
    public void ApplyScale()
    {
        // Unity 对象在原生侧销毁后，托管委托仍可能在同一轮销毁回调中被触发。
        // 必须在访问 transform / RectTransform 前拦截“假 null”和已禁用状态。
        if (this == null || !isActiveAndEnabled || isApplying)
            return;

        ResolveReferences();
        CaptureBaseScales(force: false);
        if (availableRect == null || boundsTarget == null || scaledTargets == null || scaledTargets.Length == 0)
            return;

        Vector2 containerSize = availableRect.rect.size;
        Vector2 usableSize = new Vector2(
            Mathf.Max(0f, containerSize.x - safeMargin.x * 2f),
            Mathf.Max(0f, containerSize.y - safeMargin.y * 2f));
        if (usableSize.x <= 0f || usableSize.y <= 0f)
            return;

        Vector3 boundsBaseScale = GetBaseScale(boundsTarget);
        Vector2 boundsSize = new Vector2(
            boundsTarget.rect.width * Mathf.Abs(boundsBaseScale.x),
            boundsTarget.rect.height * Mathf.Abs(boundsBaseScale.y));
        if (boundsSize.x <= Mathf.Epsilon || boundsSize.y <= Mathf.Epsilon)
            return;

        float allowedMaxScale = Mathf.Max(0.01f, maxScale);
        float fitScale = Mathf.Min(
            allowedMaxScale,
            usableSize.x / boundsSize.x,
            usableSize.y / boundsSize.y);
        fitScale = Mathf.Clamp(fitScale, 0.01f, allowedMaxScale);

        isApplying = true;
        try
        {
            for (int i = 0; i < scaledTargets.Length; i++)
            {
                RectTransform target = scaledTargets[i];
                if (target == null)
                    continue;

                Vector3 baseScale = i < baseLocalScales.Length ? baseLocalScales[i] : Vector3.one;
                target.localScale = new Vector3(
                    baseScale.x * fitScale,
                    baseScale.y * fitScale,
                    baseScale.z);
            }
        }
        finally
        {
            isApplying = false;
        }
    }

    private void ResolveReferences()
    {
        if (availableRect == null)
            availableRect = (RectTransform)transform;
    }

    private void CaptureBaseScales(bool force)
    {
        int count = scaledTargets != null ? scaledTargets.Length : 0;
        if (!force && baseLocalScales != null && baseLocalScales.Length == count)
            return;

        baseLocalScales = new Vector3[count];
        for (int i = 0; i < count; i++)
            baseLocalScales[i] = scaledTargets[i] != null ? scaledTargets[i].localScale : Vector3.one;
    }

    private Vector3 GetBaseScale(RectTransform target)
    {
        if (target == null || scaledTargets == null || baseLocalScales == null)
            return Vector3.one;

        int count = Mathf.Min(scaledTargets.Length, baseLocalScales.Length);
        for (int i = 0; i < count; i++)
        {
            if (scaledTargets[i] == target)
                return baseLocalScales[i];
        }

        return Vector3.one;
    }

    /// <summary>安全区或 UI 缩放变化后重新计算，不使用常驻 Update。</summary>
    private void BindUIManager()
    {
        UIManager nextManager = UIManager.ExistingInstance;
        if (ReferenceEquals(subscribedUIManager, nextManager))
            return;

        // 这里使用托管引用判空：Unity 已销毁对象会让 operator == 返回 null，
        // 但其 C# 事件字段仍可解除订阅，否则会留下指向已销毁组件的委托。
        if (!ReferenceEquals(subscribedUIManager, null))
            subscribedUIManager.InteractionSurfaceChanged -= ApplyScale;

        subscribedUIManager = nextManager;
        if (subscribedUIManager != null)
            subscribedUIManager.InteractionSurfaceChanged += ApplyScale;
    }

    /// <summary>幂等解除所有全局回调，供禁用与销毁阶段共同使用。</summary>
    private void UnbindCallbacks()
    {
        UIUserSettings.Changed -= ApplyScale;
        if (!ReferenceEquals(subscribedUIManager, null))
            subscribedUIManager.InteractionSurfaceChanged -= ApplyScale;
        subscribedUIManager = null;
    }

    #endregion
}
