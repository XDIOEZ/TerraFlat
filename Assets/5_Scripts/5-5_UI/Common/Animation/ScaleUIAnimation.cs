using UnityEngine;

/// <summary>
/// 在基类淡入淡出上增加相对缩放；保留视觉节点原始缩放，不假设其为 Vector3.one。
/// 建议选择独立 MotionRoot，让 SafeAreaScaleGroup、布局和玩家 UI 缩放继续拥有外层节点。
/// </summary>
[AddComponentMenu("FlatWorld/UI/Animation/Scale UI Animation")]
public class ScaleUIAnimation : BaseUIAnimation
{
    #region 缩放表现
    private Vector3 restScale;
    private RectTransform capturedRoot;

    protected override void CaptureRestPose()
    {
        capturedRoot = MotionRoot;
        if (capturedRoot != null)
            restScale = capturedRoot.localScale;
    }

    protected override void ApplyVisualState(float progress)
    {
        base.ApplyVisualState(progress);
        if (capturedRoot != null)
            capturedRoot.localScale = restScale * Mathf.LerpUnclamped(Profile.ClosedScale, 1f, progress);
    }

    protected override void RestoreRestPose()
    {
        if (capturedRoot != null)
            capturedRoot.localScale = restScale;
        capturedRoot = null;
    }
    #endregion
}
