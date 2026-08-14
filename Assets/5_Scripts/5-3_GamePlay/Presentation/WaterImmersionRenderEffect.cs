using UnityEngine;

/// <summary>
/// 水体浸没渲染效果模块。
/// deepValue 仍然控制角色被水面覆盖的高度：浅水保持主体可见，深水逐步把水线推到头部下方。
/// 水下区域通过染色和透明度表现，不再使用硬裁剪；效果由 ActorRenderEffectController 统一提交。
/// </summary>
public sealed class WaterImmersionRenderEffect : ActorRenderEffectModule
{
    #region Shader IDs

    private static readonly int WaterEnabledId = Shader.PropertyToID("_WaterEnabled");
    private static readonly int WaterWorldSpaceId = Shader.PropertyToID("_WaterWorldSpace");
    private static readonly int WaterYId = Shader.PropertyToID("_WaterY");
    private static readonly int WaterReferenceHeightId = Shader.PropertyToID("_WaterReferenceHeight");
    private static readonly int WaterSurfaceVId = Shader.PropertyToID("_WaterSurfaceV");
    private static readonly int WaterFeatherId = Shader.PropertyToID("_WaterFeather");
    private static readonly int WaterTintId = Shader.PropertyToID("_WaterTint");
    private static readonly int WaterTintStrengthId = Shader.PropertyToID("_WaterTintStrength");
    private static readonly int WaterAlphaId = Shader.PropertyToID("_WaterAlpha");
    private static readonly int WaterLineColorId = Shader.PropertyToID("_WaterLineColor");
    private static readonly int WaterLineStrengthId = Shader.PropertyToID("_WaterLineStrength");
    private static readonly int WaterLineWidthId = Shader.PropertyToID("_WaterLineWidth");
    private static readonly int WaterWaveAmplitudeId = Shader.PropertyToID("_WaterWaveAmplitude");
    private static readonly int WaterWaveFrequencyId = Shader.PropertyToID("_WaterWaveFrequency");
    private static readonly int WaterWaveSpeedId = Shader.PropertyToID("_WaterWaveSpeed");

    #endregion

    #region Inspector

    [Header("深度映射")]
    [Tooltip("将水格 deepValue 映射到角色身体高度；最高值保留头部区域。")]
    [SerializeField] private AnimationCurve depthToSurface = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.2f, 0.12f),
        new Keyframe(0.5f, 0.45f),
        new Keyframe(0.75f, 0.7f),
        new Keyframe(1f, 0.9f));

    [Tooltip("水深对水下染色强度的映射。")]
    [SerializeField] private AnimationCurve depthToTintStrength = new AnimationCurve(
        new Keyframe(0f, 0.12f),
        new Keyframe(0.5f, 0.45f),
        new Keyframe(1f, 0.8f));

    [Tooltip("所有水下区域固定保留的透明度，不随水深变化。")]
    [Range(0f, 1f)]
    [SerializeField] private float underwaterAlpha = 0.1f;

    [Tooltip("水线强度随水深的映射。")]
    [SerializeField] private AnimationCurve depthToLineStrength = new AnimationCurve(
        new Keyframe(0f, 0.55f),
        new Keyframe(0.5f, 0.8f),
        new Keyframe(1f, 1f));

    [Header("水下外观")]
    [SerializeField] private Color underwaterTint = new Color(0.18f, 0.42f, 0.78f, 1f);
    [SerializeField] private Color waterLineColor = new Color(0.65f, 0.9f, 1f, 1f);

    [Tooltip("水面边缘的柔化宽度，单位为身体归一化高度。")]
    [Range(0.001f, 0.2f)]
    [SerializeField] private float waterFeather = 0.035f;

    [Tooltip("水线在身体归一化高度上的半宽。")]
    [Range(0.001f, 0.2f)]
    [SerializeField] private float waterLineWidth = 0.035f;

    [Header("动态水线")]
    [Tooltip("水线波动的垂直幅度，单位为身体归一化高度。")]
    [Range(0f, 0.1f)]
    [SerializeField] private float waterWaveAmplitude = 0.018f;

    [Tooltip("水线沿角色横向的波浪频率。")]
    [Range(0f, 30f)]
    [SerializeField] private float waterWaveFrequency = 8f;

    [Tooltip("水线波浪的播放速度。")]
    [Range(0f, 10f)]
    [SerializeField] private float waterWaveSpeed = 2.4f;

    [Header("过渡")]
    [Tooltip("进水、出水和水深变化的平滑时间。")]
    [Min(0f)]
    [SerializeField] private float transitionSeconds = 0.18f;

    #endregion

    #region Runtime State

    private float targetDepth;
    private float targetBlend;
    private float currentDepth;
    private float currentBlend;
    private float depthVelocity;
    private float blendVelocity;
    private SpriteRenderer referenceSpriteRenderer;
    private float currentSurfaceV;
    private float currentTintStrength;
    private float currentLineStrength;
    private float currentWaterY;
    private float currentReferenceHeight = 1f;
    private bool hasWorldWaterReference;

    #endregion

    #region Lifecycle

    private void Awake()
    {
        EnsureCurves();
        CacheReferenceRenderer();
    }

    private void OnValidate()
    {
        EnsureCurves();
        waterFeather = Mathf.Clamp(waterFeather, 0.001f, 0.2f);
        waterLineWidth = Mathf.Clamp(waterLineWidth, 0.001f, 0.2f);
        underwaterAlpha = Mathf.Clamp01(underwaterAlpha);
        waterWaveAmplitude = Mathf.Clamp(waterWaveAmplitude, 0f, 0.1f);
        waterWaveFrequency = Mathf.Clamp(waterWaveFrequency, 0f, 30f);
        waterWaveSpeed = Mathf.Clamp(waterWaveSpeed, 0f, 10f);
        transitionSeconds = Mathf.Max(0f, transitionSeconds);
    }

    #endregion

    #region Public API

    /// <summary>设置水体目标状态；进入水格时传入 deepValue，离开水格时传入 false。</summary>
    public void SetWaterState(float depth, bool inWater)
    {
        targetDepth = Mathf.Clamp01(depth);
        targetBlend = inWater ? 1f : 0f;
    }

    #endregion

    #region Effect Module

    protected override bool AppliesTo(Renderer renderer)
    {
        return renderer is SpriteRenderer;
    }

    protected override void PrepareFrame(float deltaTime)
    {
        float smoothTime = Mathf.Max(0.0001f, transitionSeconds);
        currentDepth = Mathf.SmoothDamp(
            currentDepth,
            targetDepth,
            ref depthVelocity,
            smoothTime,
            Mathf.Infinity,
            deltaTime);
        currentBlend = Mathf.SmoothDamp(
            currentBlend,
            targetBlend,
            ref blendVelocity,
            smoothTime,
            Mathf.Infinity,
            deltaTime);

        currentSurfaceV = Mathf.Clamp01(depthToSurface.Evaluate(Mathf.Clamp01(currentDepth)));
        currentTintStrength = Mathf.Clamp01(depthToTintStrength.Evaluate(Mathf.Clamp01(currentDepth)));
        currentLineStrength = Mathf.Clamp01(depthToLineStrength.Evaluate(Mathf.Clamp01(currentDepth)));
        UpdateWorldWaterSurface();
    }

    protected override void ApplyEffect(Renderer renderer, MaterialPropertyBlock block, float deltaTime)
    {
        float blend = Mathf.Clamp01(currentBlend);

        block.SetFloat(WaterEnabledId, blend);
        block.SetFloat(WaterWorldSpaceId, hasWorldWaterReference ? 1f : 0f);
        block.SetFloat(WaterYId, currentWaterY);
        block.SetFloat(WaterReferenceHeightId, currentReferenceHeight);
        block.SetFloat(WaterSurfaceVId, currentSurfaceV);
        block.SetFloat(WaterFeatherId, Mathf.Max(0.001f, waterFeather));
        block.SetColor(WaterTintId, underwaterTint);
        block.SetFloat(WaterTintStrengthId, currentTintStrength);
        block.SetFloat(WaterAlphaId, Mathf.Clamp01(underwaterAlpha));
        block.SetColor(WaterLineColorId, waterLineColor);
        block.SetFloat(WaterLineStrengthId, currentLineStrength);
        block.SetFloat(WaterLineWidthId, Mathf.Max(0.001f, waterLineWidth));
        block.SetFloat(WaterWaveAmplitudeId, Mathf.Clamp01(waterWaveAmplitude));
        block.SetFloat(WaterWaveFrequencyId, Mathf.Max(0f, waterWaveFrequency));
        block.SetFloat(WaterWaveSpeedId, Mathf.Max(0f, waterWaveSpeed));
    }

    #endregion

    #region Validation

    /// <summary>确保通过代码新建的组件也拥有可用的深度曲线。</summary>
    private void EnsureCurves()
    {
        depthToSurface ??= new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0.9f));
        depthToTintStrength ??= new AnimationCurve(new Keyframe(0f, 0.12f), new Keyframe(1f, 0.8f));
        depthToLineStrength ??= new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(1f, 1f));
    }

    /// <summary>缓存角色主体 Sprite，避免每帧查找组件。</summary>
    private void CacheReferenceRenderer()
    {
        if (referenceSpriteRenderer == null)
            referenceSpriteRenderer = GetComponent<SpriteRenderer>();
    }

    /// <summary>由角色主体计算统一的世界水平水面，旋转的手持物不会带着水线一起旋转。</summary>
    private void UpdateWorldWaterSurface()
    {
        CacheReferenceRenderer();
        if (referenceSpriteRenderer == null || referenceSpriteRenderer.sprite == null)
        {
            hasWorldWaterReference = false;
            return;
        }

        Bounds bounds = referenceSpriteRenderer.bounds;
        hasWorldWaterReference = true;
        currentReferenceHeight = Mathf.Max(0.0001f, bounds.size.y);
        currentWaterY = bounds.min.y + currentReferenceHeight * currentSurfaceV;
    }

    #endregion
}
