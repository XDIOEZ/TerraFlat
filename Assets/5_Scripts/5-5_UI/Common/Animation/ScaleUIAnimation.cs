using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 面板纯缩放动画：保留原始缩放，以可见内容中心为轴由小变大，不叠加透明度过渡。
/// 建议选择独立 MotionRoot，让 SafeAreaScaleGroup、布局和玩家 UI 缩放继续拥有外层节点。
/// </summary>
[AddComponentMenu("FlatWorld/UI/Animation/Scale UI Animation")]
public class ScaleUIAnimation : BaseUIAnimation
{
    #region 缩放表现
    private Vector3 restScale;
    private Vector3 restLocalPosition;
    private Vector3 centerInRootSpace;
    private Vector3 restCenterInParentSpace;
    private Vector3 restCenterWorld;
    private RectTransform capturedRoot;
    private SafeAreaScaleGroup safeAreaScaleGroup;
    private bool usesSafeAreaScaleGroup;

    protected override bool UsesAlphaTransition => false;

    protected override void CaptureRestPose()
    {
        capturedRoot = MotionRoot != null ? MotionRoot : transform as RectTransform;
        if (capturedRoot == null)
            return;

        safeAreaScaleGroup = GetComponent<SafeAreaScaleGroup>();
        usesSafeAreaScaleGroup = safeAreaScaleGroup != null && safeAreaScaleGroup.HasScaleTargets;
        restScale = capturedRoot.localScale;
        restLocalPosition = capturedRoot.localPosition;
        if (!usesSafeAreaScaleGroup)
        {
            centerInRootSpace = CaptureVisualCenter(capturedRoot);
            restCenterWorld = capturedRoot.TransformPoint(centerInRootSpace);
            if (capturedRoot.parent != null)
                restCenterInParentSpace = capturedRoot.parent.InverseTransformPoint(restCenterWorld);
        }
    }

    protected override void ApplyVisualState(float progress)
    {
        base.ApplyVisualState(progress);
        if (capturedRoot == null)
            return;

        float scale = Mathf.LerpUnclamped(Profile.ClosedScale, 1f, progress);
        if (usesSafeAreaScaleGroup)
        {
            safeAreaScaleGroup.SetAnimationScale(scale);
            return;
        }

        capturedRoot.localScale = restScale * scale;
        KeepVisualCenterInPlace();
    }

    protected override void RestoreRestPose()
    {
        if (usesSafeAreaScaleGroup && safeAreaScaleGroup != null)
            safeAreaScaleGroup.SetAnimationScale(1f);
        else if (capturedRoot != null)
        {
            capturedRoot.localPosition = restLocalPosition;
            capturedRoot.localScale = restScale;
        }
        capturedRoot = null;
        safeAreaScaleGroup = null;
        usesSafeAreaScaleGroup = false;
    }

    /// <summary>按面板图形边界取中心，兼容零尺寸全屏容器和非居中 RectTransform 锚点。</summary>
    private static Vector3 CaptureVisualCenter(RectTransform root)
    {
        Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
        if (graphics.Length == 0)
            return root.rect.center;

        Vector3 minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 0f);
        Vector3 maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, 0f);
        Vector3[] corners = new Vector3[4];
        bool hasBounds = false;

        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic graphic = graphics[i];
            if (graphic == null || !graphic.enabled || !graphic.gameObject.activeInHierarchy || graphic.color.a <= 0f)
                continue;

            RectTransform graphicRect = graphic.rectTransform;
            graphicRect.GetWorldCorners(corners);
            for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
            {
                Vector3 localCorner = root.InverseTransformPoint(corners[cornerIndex]);
                minimum = Vector3.Min(minimum, localCorner);
                maximum = Vector3.Max(maximum, localCorner);
                hasBounds = true;
            }
        }

        return hasBounds ? (minimum + maximum) * 0.5f : root.rect.center;
    }

    /// <summary>缩放非居中锚点时补偿位移，使视觉中心留在原位置。</summary>
    private void KeepVisualCenterInPlace()
    {
        Transform parent = capturedRoot.parent;
        Vector3 targetCenter = parent != null
            ? parent.TransformPoint(restCenterInParentSpace)
            : restCenterWorld;
        Vector3 currentCenter = capturedRoot.TransformPoint(centerInRootSpace);
        capturedRoot.position += targetCenter - currentCenter;
    }
    #endregion
}
