using UnityEngine;

/// <summary>
/// 角色通用颜色与受击表现模块。
/// 通过 ActorRenderEffectController 统一写入 MaterialPropertyBlock，提供可复用的状态染色、受击闪红和渐变强度。
/// 受击持续时间默认 0.2 秒、闪烁次数默认 1 次、受击颜色为明显的红色；不会访问 Renderer.material，因此不会为每个角色创建材质实例。
/// </summary>
[DisallowMultipleComponent]
public sealed class ActorRenderColorEffect : ActorRenderEffectModule
{
    #region Shader IDs

    private static readonly int ActorTintId = Shader.PropertyToID("_ActorTint");
    private static readonly int ActorTintStrengthId = Shader.PropertyToID("_ActorTintStrength");
    private static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
    private static readonly int HitFlashColorId = Shader.PropertyToID("_HitFlashColor");

    #endregion

    #region Inspector

    [Header("状态染色")]
    [SerializeField] private Color statusTint = Color.white;
    [SerializeField, Range(0f, 1f)] private float statusTintStrength;
    [SerializeField, Min(0f)] private float statusTransitionSeconds = 0.12f;

    [Header("受击闪红")]
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.08f, 0.08f, 1f);
    [SerializeField, Min(0.01f)] private float defaultFlashDuration = 0.2f;
    [SerializeField, Min(1)] private int defaultFlashCount = 1;
    [SerializeField] private AnimationCurve flashIntensity = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.5f, 1f),
        new Keyframe(1f, 0f));

    #endregion

    #region Runtime State

    private Color targetStatusTint = Color.white;
    private Color currentStatusTint = Color.white;
    private float targetStatusStrength;
    private float currentStatusStrength;
    private float flashElapsed;
    private float flashDuration;
    private int flashCount;
    private Color currentFlashColor = new Color(1f, 0.08f, 0.08f, 1f);
    private float currentFlashAmount;

    #endregion

    #region Lifecycle

    private void Awake()
    {
        EnsureFlashCurve();
        targetStatusTint = statusTint;
        currentStatusTint = statusTint;
        targetStatusStrength = Mathf.Clamp01(statusTintStrength);
        currentStatusStrength = targetStatusStrength;
        currentFlashColor = hitFlashColor;
        flashDuration = Mathf.Max(0.01f, defaultFlashDuration);
        flashCount = Mathf.Max(1, defaultFlashCount);
    }

    private void OnValidate()
    {
        EnsureFlashCurve();
        statusTintStrength = Mathf.Clamp01(statusTintStrength);
        statusTransitionSeconds = Mathf.Max(0f, statusTransitionSeconds);
        defaultFlashDuration = Mathf.Max(0.01f, defaultFlashDuration);
        defaultFlashCount = Mathf.Max(1, defaultFlashCount);
    }

    #endregion

    #region Public API

    /// <summary>设置持续状态染色；强度为 0 时恢复角色原色。</summary>
    public void SetStatusTint(Color tint, float strength)
    {
        targetStatusTint = tint;
        targetStatusStrength = Mathf.Clamp01(strength);
    }

    /// <summary>清除持续状态染色。</summary>
    public void ClearStatusTint()
    {
        SetStatusTint(Color.white, 0f);
    }

    /// <summary>播放一次受击闪红，可被重复命中重新触发。</summary>
    public void PlayHitFlash(Color color, float duration, int count)
    {
        currentFlashColor = color;
        flashDuration = Mathf.Max(0.01f, duration > 0f ? duration : defaultFlashDuration);
        flashCount = Mathf.Max(1, count > 0 ? count : defaultFlashCount);
        flashElapsed = 0f;
    }

    /// <summary>使用组件默认参数播放受击闪红。</summary>
    public void PlayHitFlash()
    {
        PlayHitFlash(hitFlashColor, defaultFlashDuration, defaultFlashCount);
    }

    #endregion

    #region Effect Module

    protected override bool AppliesTo(Renderer renderer)
    {
        return renderer is SpriteRenderer;
    }

    protected override void PrepareFrame(float deltaTime)
    {
        float tintBlend = GetTransitionBlend(deltaTime, statusTransitionSeconds);
        currentStatusTint = Color.Lerp(currentStatusTint, targetStatusTint, tintBlend);
        currentStatusStrength = Mathf.Lerp(currentStatusStrength, targetStatusStrength, tintBlend);

        if (flashElapsed < flashDuration)
            flashElapsed += Mathf.Max(0f, deltaTime);

        currentFlashAmount = EvaluateFlashAmount();
    }

    protected override void ApplyEffect(Renderer renderer, MaterialPropertyBlock block, float deltaTime)
    {
        EnsureFlashCurve();

        block.SetColor(ActorTintId, currentStatusTint);
        block.SetFloat(ActorTintStrengthId, Mathf.Clamp01(currentStatusStrength));
        block.SetColor(HitFlashColorId, currentFlashColor);
        block.SetFloat(HitFlashId, currentFlashAmount);
    }

    #endregion

    #region Helpers

    /// <summary>按时间常数计算无分配的平滑插值系数。</summary>
    private static float GetTransitionBlend(float deltaTime, float seconds)
    {
        if (seconds <= 0f)
            return 1f;

        return 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / seconds);
    }

    /// <summary>按闪烁次数和渐变曲线计算当前闪白强度。</summary>
    private float EvaluateFlashAmount()
    {
        if (flashElapsed >= flashDuration || flashDuration <= 0f)
            return 0f;

        float progress = Mathf.Clamp01(flashElapsed / flashDuration);
        float cycleProgress = Mathf.Repeat(progress * flashCount, 1f);
        return Mathf.Clamp01(flashIntensity.Evaluate(cycleProgress));
    }

    /// <summary>确保运行时动态创建的模块也拥有可用的闪白曲线。</summary>
    private void EnsureFlashCurve()
    {
        flashIntensity ??= new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0f));
    }

    #endregion
}
