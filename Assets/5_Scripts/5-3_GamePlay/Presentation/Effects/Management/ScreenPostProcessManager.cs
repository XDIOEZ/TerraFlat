using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 一个可叠加的屏幕后处理效果提交接口。效果只描述目标表现，实际 Volume 参数由
/// <see cref="ScreenPostProcessManager"/> 统一合成，后续可继续接入受伤闪屏、醉酒、寒冷等效果。
/// </summary>
public interface IScreenPostProcessEffect
{
    string EffectId { get; }
    int Priority { get; }
    bool IsValid { get; }
    void Apply(ScreenPostProcessFrame frame, float unscaledDeltaTime);
}

/// <summary>
/// 单帧后处理效果快照。当前提供 Vignette 通道，保留独立接口以便后续扩展其他 URP Volume 通道。
/// </summary>
public sealed class ScreenPostProcessFrame
{
    #region 当前帧数据

    public float VignetteIntensity { get; private set; }
    public float VignetteSmoothness { get; private set; }
    public float VignettePulseAmount { get; private set; }
    public Color VignetteColor { get; private set; }

    #endregion

    #region 帧操作

    /// <summary>清空上一帧的合成结果。</summary>
    public void Reset()
    {
        VignetteIntensity = 0f;
        VignetteSmoothness = 0.82f;
        VignettePulseAmount = 0f;
        VignetteColor = new Color(0.58f, 0.01f, 0.015f, 1f);
    }

    /// <summary>按强度叠加一个 Vignette 请求，避免多个效果互相覆盖。</summary>
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
/// FlatWorld 全局后处理单例。运行时创建常驻 URP Volume，统一合成所有屏幕后处理效果，
/// 不修改全局 Volume Profile 资产；Vignette 强度为 0 时自动停用该组件，避免常态增加渲染开销。
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class ScreenPostProcessManager : SingletonAutoMono<ScreenPostProcessManager>
{
    #region 配置常量

    private const float TransitionSeconds = 0.12f;
    private const float MinimumActiveIntensity = 0.0005f;
    private const int RuntimeVolumeLayer = 0;
    private const float DefaultPriority = 100f;

    #endregion

    #region 运行时状态

    private readonly List<IScreenPostProcessEffect> effects =
        new List<IScreenPostProcessEffect>(4);
    private readonly ScreenPostProcessFrame frame = new ScreenPostProcessFrame();

    private GameObject volumeObject;
    private Volume runtimeVolume;
    private VolumeProfile runtimeProfile;
    private Vignette vignette;
    private UniversalAdditionalCameraData postProcessCameraData;
    private float currentVignetteIntensity;
    private float vignetteIntensityVelocity;
    private bool hasInitialized;

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
        ScreenPostProcessSettings.Changed -= HandleQualityChanged;
        ScreenPostProcessSettings.Changed += HandleQualityChanged;
        EnsureRuntimeVolume();
    }

    private void LateUpdate()
    {
        if (instance != this)
            return;

        EnsureRuntimeVolume();
        EnsureMainCameraPostProcess();
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

            effect.Apply(frame, deltaTime);
        }

        ApplyFrame(frame, deltaTime);
    }

    protected override void OnDestroy()
    {
        ScreenPostProcessSettings.Changed -= HandleQualityChanged;
        effects.Clear();

        if (runtimeProfile != null)
        {
            Destroy(runtimeProfile);
            runtimeProfile = null;
        }

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

    #region Volume 合成

    /// <summary>创建仅属于本单例的运行时 Volume，不污染项目内置全局 Volume Profile。</summary>
    private void EnsureRuntimeVolume()
    {
        if (hasInitialized && runtimeVolume != null && runtimeProfile != null && vignette != null)
            return;

        if (volumeObject == null)
        {
            volumeObject = new GameObject("ScreenPostProcessVolume");
            volumeObject.layer = RuntimeVolumeLayer;
            volumeObject.transform.SetParent(transform, false);
        }

        if (runtimeVolume == null)
        {
            runtimeVolume = volumeObject.GetComponent<Volume>();
            if (runtimeVolume == null)
                runtimeVolume = volumeObject.AddComponent<Volume>();
        }

        runtimeVolume.isGlobal = true;
        runtimeVolume.priority = DefaultPriority;
        runtimeVolume.weight = 1f;

        if (runtimeProfile == null)
        {
            runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            runtimeProfile.name = "ScreenPostProcessRuntimeProfile";
            runtimeVolume.sharedProfile = runtimeProfile;
        }

        if (vignette == null)
            vignette = runtimeProfile.Add<Vignette>(true);

        vignette.active = true;
        vignette.color.overrideState = true;
        vignette.center.overrideState = true;
        vignette.intensity.overrideState = true;
        vignette.smoothness.overrideState = true;
        vignette.rounded.overrideState = true;
        vignette.center.value = new Vector2(0.5f, 0.5f);
        vignette.rounded.value = true;
        hasInitialized = true;
    }

    /// <summary>把本帧效果平滑写入 URP Vignette，并按画质档位降低移动端成本。</summary>
    private void ApplyFrame(ScreenPostProcessFrame nextFrame, float deltaTime)
    {
        if (vignette == null)
            return;

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

        vignette.active = currentVignetteIntensity > MinimumActiveIntensity;
        vignette.color.value = nextFrame.VignetteColor;
        vignette.intensity.value = Mathf.Clamp01(currentVignetteIntensity);
        vignette.smoothness.value = Mathf.Lerp(
            0.72f,
            0.9f,
            Mathf.Clamp01(nextFrame.VignetteSmoothness * GetQualitySmoothnessScale()));
    }

    /// <summary>质量切换时立即刷新配置，强度本身继续由 LateUpdate 平滑过渡。</summary>
    private void HandleQualityChanged()
    {
        EnsureRuntimeVolume();
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
                return 0.78f;
            case ScreenPostProcessQuality.Low:
                return 0.62f;
            default:
                return 0.92f;
        }
    }

    private static float GetQualitySmoothnessScale()
    {
        switch (ScreenPostProcessSettings.Quality)
        {
            case ScreenPostProcessQuality.Medium:
                return 0.9f;
            case ScreenPostProcessQuality.Low:
                return 0.78f;
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

    #region 相机适配

    /// <summary>主相机由场景/Prefab 提供；这里只确保它启用了 URP 后处理，不接管相机生命周期。</summary>
    private void EnsureMainCameraPostProcess()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return;

        UniversalAdditionalCameraData cameraData =
            mainCamera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData == null)
            return;

        postProcessCameraData = cameraData;
        postProcessCameraData.renderPostProcessing = true;
    }

    #endregion
}
