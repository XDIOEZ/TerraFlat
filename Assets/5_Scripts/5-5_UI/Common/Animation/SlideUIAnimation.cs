using UnityEngine;

/// <summary>
/// 在基类淡入淡出上增加相对位移；移动独立 MotionRoot，避免修改面板安全区和外部布局。
/// 位移距离来自 JSON Offset，时长统一使用开关 Duration；CanvasScaler 自动负责分辨率缩放。
/// </summary>
[AddComponentMenu("FlatWorld/UI/Animation/Slide UI Animation")]
public class SlideUIAnimation : BaseUIAnimation
{
    #region 位移表现
    private Vector2 restPosition;
    private RectTransform capturedRoot;

    protected override void CaptureRestPose()
    {
        capturedRoot = MotionRoot;
        if (capturedRoot != null)
            restPosition = capturedRoot.anchoredPosition;
    }

    protected override void ApplyVisualState(float progress)
    {
        base.ApplyVisualState(progress);
        if (capturedRoot != null)
            capturedRoot.anchoredPosition = restPosition + Profile.ClosedOffset * (1f - progress);
    }

    protected override void RestoreRestPose()
    {
        if (capturedRoot != null)
            capturedRoot.anchoredPosition = restPosition;
        capturedRoot = null;
    }
    #endregion
}
