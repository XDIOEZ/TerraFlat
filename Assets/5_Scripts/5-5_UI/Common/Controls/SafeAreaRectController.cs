using UnityEngine;

/// <summary>
/// 将指定 RectTransform 约束到设备安全区。外层 Canvas 继续铺满屏幕承载背景，正式面板与手机 HUD 只挂在本节点内；
/// 横屏左右翻转、分辨率变化和应用恢复时都会重新计算锚点，不依赖固定刘海尺寸。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class SafeAreaRectController : MonoBehaviour
{
    #region 状态

    private RectTransform target;
    private Rect lastSafeArea;
    private int lastScreenWidth;
    private int lastScreenHeight;
    private bool isApplying;

    #endregion

    #region 创建与生命周期

    public static SafeAreaRectController Ensure(RectTransform rectTransform)
    {
        if (rectTransform == null)
            return null;

        SafeAreaRectController controller = rectTransform.GetComponent<SafeAreaRectController>();
        if (controller == null)
        {
            controller = rectTransform.gameObject.AddComponent<SafeAreaRectController>();
            // AddComponent 会同步触发 Awake/OnEnable，并完成首次强制应用。
            // 此处不能再次广播，否则订阅者在回调中读取 SafeAreaRoot 会形成递归。
            return controller;
        }

        // 已存在时仅在屏幕或安全区实际变化后更新，保持 Ensure 可重入且幂等。
        controller.ApplySafeArea(force: false);
        return controller;
    }

    private void Awake()
    {
        target = (RectTransform)transform;
    }

    private void OnEnable()
    {
        UIUserSettings.Changed -= HandleUISettingsChanged;
        UIUserSettings.Changed += HandleUISettingsChanged;
        Canvas.preWillRenderCanvases -= HandleCanvasRendering;
        Canvas.preWillRenderCanvases += HandleCanvasRendering;
        ApplySafeArea(force: true);
    }

    private void OnDisable()
    {
        UIUserSettings.Changed -= HandleUISettingsChanged;
        Canvas.preWillRenderCanvases -= HandleCanvasRendering;
    }

    private void OnRectTransformDimensionsChange()
    {
        if (!isApplying && isActiveAndEnabled)
            ApplySafeArea(force: false);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            ApplySafeArea(force: false);
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused)
            ApplySafeArea(force: false);
    }

    /// <summary>安全区偏好变更后立即在全屏与安全区之间切换。</summary>
    private void HandleUISettingsChanged()
    {
        ApplySafeArea(force: true);
    }

    /// <summary>Canvas 尺寸回调可能早于 Screen 更新；在正式绘制前补齐同一帧的屏幕快照，不重建未变化的布局。</summary>
    private void HandleCanvasRendering()
    {
        if (!isApplying && isActiveAndEnabled)
            ApplySafeArea(force: false);
    }

    #endregion

    #region 安全区换算

    public void ApplySafeArea(bool force)
    {
        if (target == null)
            target = (RectTransform)transform;

        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
        Rect reportedSafeArea = UIUserSettings.RespectSafeArea
            ? Screen.safeArea
            : new Rect(0f, 0f, width, height);
        Rect safeArea = ResolveSafeArea(reportedSafeArea, width, height);
        if (!force && safeArea == lastSafeArea && width == lastScreenWidth && height == lastScreenHeight)
            return;

        lastSafeArea = safeArea;
        lastScreenWidth = width;
        lastScreenHeight = height;

        Vector2 anchorMin = new Vector2(safeArea.xMin / width, safeArea.yMin / height);
        Vector2 anchorMax = new Vector2(safeArea.xMax / width, safeArea.yMax / height);
        isApplying = true;
        target.anchorMin = anchorMin;
        target.anchorMax = anchorMax;
        target.offsetMin = Vector2.zero;
        target.offsetMax = Vector2.zero;
        isApplying = false;

        // 安全区或横屏方向变化后立即通知 HUD 重排，并让它释放旧坐标系中的触摸所有权。
        if (Application.isPlaying)
            UIManager.ExistingInstance?.NotifyInteractionSurfaceChanged();
    }

    /// <summary>
    /// 安全区必须属于当前屏幕；模拟器切换/旋转过渡帧的旧分辨率矩形不能变成大于 1 的锚点。
    /// 无效快照暂用完整屏幕，下一次有效快照会自动恢复刘海边距；容忍半像素取整误差。
    /// </summary>
    public static Rect ResolveSafeArea(Rect reported, int width, int height)
    {
        Rect screen = new Rect(0f, 0f, Mathf.Max(1, width), Mathf.Max(1, height));
        if (!IsFinite(reported.x) || !IsFinite(reported.y) ||
            !IsFinite(reported.width) || !IsFinite(reported.height) ||
            reported.width <= 0f || reported.height <= 0f ||
            reported.xMin < -0.5f || reported.yMin < -0.5f ||
            reported.xMax > screen.width + 0.5f || reported.yMax > screen.height + 0.5f)
            return screen;

        return Rect.MinMaxRect(Mathf.Clamp(reported.xMin, 0f, screen.width),
            Mathf.Clamp(reported.yMin, 0f, screen.height),
            Mathf.Clamp(reported.xMax, 0f, screen.width),
            Mathf.Clamp(reported.yMax, 0f, screen.height));
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>计算已缩放/旋转矩形越过下边界的修正量；只修正最终显示，不改写用户的负间距偏好。</summary>
    public static float ResolveBottomCorrection(Rect rect, Matrix4x4 localToArea, float minimumY)
    {
        float bottom = Mathf.Min(
            localToArea.MultiplyPoint3x4(new Vector3(rect.xMin, rect.yMin)).y,
            localToArea.MultiplyPoint3x4(new Vector3(rect.xMax, rect.yMin)).y);
        bottom = Mathf.Min(bottom, Mathf.Min(
            localToArea.MultiplyPoint3x4(new Vector3(rect.xMin, rect.yMax)).y,
            localToArea.MultiplyPoint3x4(new Vector3(rect.xMax, rect.yMax)).y));
        return Mathf.Max(0f, minimumY - bottom);
    }

    #endregion
}
