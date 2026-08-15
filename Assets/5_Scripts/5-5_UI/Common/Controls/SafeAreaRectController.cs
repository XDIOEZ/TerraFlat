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
        ApplySafeArea(force: true);
    }

    private void OnDisable()
    {
        UIUserSettings.Changed -= HandleUISettingsChanged;
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

    #endregion

    #region 安全区换算

    public void ApplySafeArea(bool force)
    {
        if (target == null)
            target = (RectTransform)transform;

        int width = Mathf.Max(1, Screen.width);
        int height = Mathf.Max(1, Screen.height);
        Rect safeArea = UIUserSettings.RespectSafeArea
            ? Screen.safeArea
            : new Rect(0f, 0f, width, height);
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

    #endregion
}
