using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 一个可叠加的屏幕后处理效果提交接口。效果只描述目标表现，最终屏幕合成由
/// <see cref="ScreenPostProcessManager"/> 与 URP Renderer Feature 统一完成。
/// </summary>
public interface IScreenPostProcessEffect
{
    string EffectId { get; }
    int Priority { get; }
    bool IsValid { get; }
    void Apply(ScreenPostProcessFrame frame, float unscaledDeltaTime);
}

#region 屏幕后处理质量档位接口

/// <summary>标记可在低特效质量运行的屏幕后处理；低、中、高档位都会保留。</summary>
public interface IScreenPostProcessLowQualityEffect
{
}

/// <summary>标记需要至少中等特效质量的屏幕后处理；低档位会跳过。</summary>
public interface IScreenPostProcessMediumQualityEffect
{
}

/// <summary>标记只在高特效质量运行的屏幕后处理；中、低档位会跳过。</summary>
public interface IScreenPostProcessHighQualityEffect
{
}

#endregion

/// <summary>
/// 单帧屏幕警示快照。保留 Vignette 命名作为效果语义，但最终不再使用 URP 内置的
/// 乘法 Vignette，而是交给 GPU 全屏红边 Pass 做真正的红色 Alpha 混合。
/// </summary>
public sealed class ScreenPostProcessFrame
{
    #region 当前帧数据

    public float BlurStrength { get; private set; }
    public void AddBlur(float strength) => BlurStrength = Mathf.Max(BlurStrength, Mathf.Clamp(strength, 0f, 4f));

    public float VignetteIntensity { get; private set; }
    public float VignetteSmoothness { get; private set; }
    public float VignettePulseAmount { get; private set; }
    public Color VignetteColor { get; private set; }

    #endregion

    #region 帧操作

    /// <summary>清空上一帧的合成结果。</summary>
    public void Reset()
    {
        BlurStrength = 0f;
        VignetteIntensity = 0f;
        VignetteSmoothness = 0.82f;
        VignettePulseAmount = 0f;
        VignetteColor = new Color(0.9f, 0.015f, 0.02f, 1f);
    }

    /// <summary>按强度叠加一个屏幕边缘警示请求，避免多个效果互相覆盖。</summary>
    public void AddVignette(
        float intensity,
        Color color,
        float smoothness,
        float pulseAmount)
    {
        intensity = Mathf.Clamp01(intensity);
        if (intensity <= 0f)
            return;

        if (intensity >= VignetteIntensity)
        {
            VignetteIntensity = intensity;
            VignetteColor = color;
            VignetteSmoothness = Mathf.Clamp01(smoothness);
            VignettePulseAmount = Mathf.Clamp01(pulseAmount);
            return;
        }

        VignettePulseAmount = Mathf.Max(VignettePulseAmount, Mathf.Clamp01(pulseAmount));
    }

    #endregion
}

/// <summary>
/// FlatWorld 全局屏幕警示合成器。CPU 只负责收集玩法状态并平滑出少量参数；
/// 最终红边由 <see cref="LowHealthRedEdgeRendererFeature"/> 在 GPU 上用一个全屏三角形绘制。
/// 该路径不创建额外 Camera、不创建 UI 网格，也不使用 URP 内置会把画面乘暗的 Vignette。
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class ScreenPostProcessManager : SingletonAutoMono<ScreenPostProcessManager>
{
    #region 配置常量

    private const float TransitionSeconds = 0.12f;
    private const float MinimumActiveIntensity = 0.0005f;

    #endregion

    #region 运行时状态

    private readonly List<IScreenPostProcessEffect> effects =
        new List<IScreenPostProcessEffect>(4);
    private readonly ScreenPostProcessFrame frame = new ScreenPostProcessFrame();

    private float currentVignetteIntensity;
    private float vignetteIntensityVelocity;

    /// <summary>当前已注册的屏幕后处理效果，供调试和后续扩展查询。</summary>
    public IReadOnlyList<IScreenPostProcessEffect> Effects => effects;

    /// <summary>只获取已经存在的单例，避免销毁阶段因访问 Instance 而重新创建管理器。</summary>
    public static ScreenPostProcessManager ExistingInstance => instance;

    #endregion

    #region 生命周期

    protected override void Awake()
    {
        base.Awake();
        if (instance != this)
            return;

        DontDestroyOnLoad(gameObject);
        TraumaBlurRendererFeature.SetStrength(0f);
        LowHealthRedEdgeRendererFeature.SetState(Color.red, 0f, 0.82f);
    }

    private void LateUpdate()
    {
        if (instance != this)
            return;

        frame.Reset();

        float deltaTime = Mathf.Max(0f, Time.unscaledDeltaTime);
        for (int i = effects.Count - 1; i >= 0; i--)
        {
            IScreenPostProcessEffect effect = effects[i];
            if (effect == null || !effect.IsValid)
            {
                effects.RemoveAt(i);
                continue;
            }

            if (!IsEffectEnabledForQuality(effect))
                continue;

            effect.Apply(frame, deltaTime);
        }

        ApplyFrame(frame, deltaTime);
    }

    protected override void OnDestroy()
    {
        effects.Clear();
        TraumaBlurRendererFeature.SetStrength(0f);
        LowHealthRedEdgeRendererFeature.SetState(Color.red, 0f, 0.82f);
        base.OnDestroy();
    }

    #endregion

    #region 对外接口

    /// <summary>注册一个屏幕后处理效果；重复注册同一实例会被忽略。</summary>
    public void RegisterEffect(IScreenPostProcessEffect effect)
    {
        if (effect == null || effects.Contains(effect))
            return;

        effects.Add(effect);
        effects.Sort(CompareEffects);
    }

    /// <summary>注销屏幕后处理效果，供玩家销毁、切换本地档案和禁用模块时调用。</summary>
    public void UnregisterEffect(IScreenPostProcessEffect effect)
    {
        if (effect != null)
            effects.Remove(effect);
    }

    #endregion

    #region GPU 合成参数

    /// <summary>按特效脚本实现的档位接口筛选当前质量允许提交的效果。</summary>
    private static bool IsEffectEnabledForQuality(IScreenPostProcessEffect effect)
    {
        ScreenPostProcessQuality quality = ScreenPostProcessSettings.Quality;
        if (effect is IScreenPostProcessHighQualityEffect)
            return quality == ScreenPostProcessQuality.High;
        if (effect is IScreenPostProcessMediumQualityEffect)
            return quality != ScreenPostProcessQuality.Low;

        return true;
    }

    /// <summary>把本帧强度平滑后提交给 GPU Renderer Feature。</summary>
    private void ApplyFrame(ScreenPostProcessFrame nextFrame, float deltaTime)
    {
        TraumaBlurRendererFeature.SetStrength(nextFrame.BlurStrength);
        float targetIntensity = Mathf.Clamp01(nextFrame.VignetteIntensity) * GetQualityIntensityScale();
        currentVignetteIntensity = Mathf.SmoothDamp(
            currentVignetteIntensity,
            targetIntensity,
            ref vignetteIntensityVelocity,
            TransitionSeconds,
            Mathf.Infinity,
            deltaTime);

        float pulseAmount = GetQualityPulseScale() * nextFrame.VignettePulseAmount;
        if (currentVignetteIntensity > MinimumActiveIntensity && pulseAmount > 0f)
        {
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * 4.5f) * pulseAmount;
            currentVignetteIntensity = Mathf.Clamp01(currentVignetteIntensity * pulse);
        }

        float smoothness = Mathf.Clamp01(
            nextFrame.VignetteSmoothness * GetQualitySmoothnessScale());

        LowHealthRedEdgeRendererFeature.SetState(
            nextFrame.VignetteColor,
            currentVignetteIntensity,
            smoothness);
    }

    private static int CompareEffects(IScreenPostProcessEffect left, IScreenPostProcessEffect right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;
        return right.Priority.CompareTo(left.Priority);
    }

    private static float GetQualityIntensityScale()
    {
        switch (ScreenPostProcessSettings.Quality)
        {
            case ScreenPostProcessQuality.Medium:
                return 0.88f;
            case ScreenPostProcessQuality.Low:
                return 0.78f;
            default:
                return 1f;
        }
    }

    private static float GetQualitySmoothnessScale()
    {
        switch (ScreenPostProcessSettings.Quality)
        {
            case ScreenPostProcessQuality.Medium:
                return 0.94f;
            case ScreenPostProcessQuality.Low:
                return 0.88f;
            default:
                return 1f;
        }
    }

    private static float GetQualityPulseScale()
    {
        switch (ScreenPostProcessSettings.Quality)
        {
            case ScreenPostProcessQuality.Medium:
                return 0.55f;
            case ScreenPostProcessQuality.Low:
                return 0f;
            default:
                return 1f;
        }
    }

    #endregion
}
