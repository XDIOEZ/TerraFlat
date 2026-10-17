using UnityEngine;

/// <summary>按完整真实时间推进雾圈半径，天气采样与画面合成仍由各自入口负责。</summary>
public sealed class DenseFogPresentationTransition
{
    #region 雾圈过渡状态

    private const float OutsidePadding = 0.01f;
    private float targetStrength;
    private float elapsed;
    private float duration;
    private float outsideRadius;

    public bool HasValue { get; private set; }
    public Vector2 Radii { get; private set; }
    public float Progress { get; private set; }
    public bool IsTransitioning => HasValue && Progress < 1f;
    public bool IsVisible => HasValue && Radii.x < outsideRadius &&
                             (targetStrength > 0f || IsTransitioning);

    public void Reset()
    {
        HasValue = false;
        Radii = default;
        Progress = targetStrength = elapsed = duration = outsideRadius = 0f;
    }

    #endregion

    #region 半径与真实时间推进

    public static float ResolveOutsideRadius(Vector2 origin, Vector2 right, Vector2 up)
    {
        // 清晰圆盖住最远角即为全屏无雾，只留很小余量让雾从画面外围开始收拢。
        float farthestSquared = Mathf.Max(Mathf.Max(origin.sqrMagnitude, (origin + right).sqrMagnitude),
            Mathf.Max((origin + up).sqrMagnitude, (origin + right + up).sqrMagnitude));
        return Mathf.Sqrt(farthestSquared) + OutsidePadding;
    }

    public void Step(float strength, float realtimeDelta, float clearScreenRadius,
        WorldRenderingConfig.RenderingDenseFog config)
    {
        if (config == null || !config.IsValid || !float.IsFinite(strength) ||
            !float.IsFinite(realtimeDelta) || !float.IsFinite(clearScreenRadius) || clearScreenRadius <= 0f)
        {
            Reset();
            return;
        }

        float featherWidth = config.opaqueRadius - config.clearRadius;
        outsideRadius = Mathf.Max(config.clearRadius, clearScreenRadius);
        strength = Mathf.Clamp01(strength);
        if (!HasValue)
        {
            // 首帧可能包含世界加载耗时，过渡从首次可见画面之后才开始计时。
            realtimeDelta = 0f;
            HasValue = true;
            Radii = new Vector2(outsideRadius, outsideRadius + featherWidth);
            Progress = 1f;
        }

        if (!Mathf.Approximately(targetStrength, strength))
        {
            // 每次实际浓度变化都从当前雾边续接，小雾、起雾和散雾同样走完整段时间。
            float visibleRadius = Mathf.Min(Radii.x, outsideRadius);
            Radii = new Vector2(visibleRadius, visibleRadius + featherWidth);
            targetStrength = strength;
            elapsed = 0f;
            duration = config.transitionSeconds;
            Progress = 0f;
        }

        float targetRadius = config.TryResolveRadii(targetStrength, out Vector2 targetRadii)
            ? Mathf.Min(targetRadii.x, outsideRadius) : outsideRadius;
        float radius = Radii.x;
        if (duration <= 0f || Progress >= 1f)
        {
            radius = targetRadius;
            Progress = 1f;
        }
        else
        {
            float previousBlend = Mathf.SmoothStep(0f, 1f, Progress);
            elapsed = Mathf.Min(duration, elapsed + Mathf.Max(0f, realtimeDelta));
            Progress = Mathf.Clamp01(elapsed / duration);
            float blend = Mathf.SmoothStep(0f, 1f, Progress);
            // 递推与固定目标的 SmoothStep 等价，镜头改变屏外端点时当前半径仍连续。
            float remainingBlend = 1f - previousBlend;
            float step = Progress >= 1f ? 1f : remainingBlend > 0f
                ? Mathf.Clamp01((blend - previousBlend) / remainingBlend) : 0f;
            radius = Mathf.Lerp(radius, targetRadius, step);
        }
        Radii = new Vector2(radius, radius + featherWidth);
    }

    #endregion
}
