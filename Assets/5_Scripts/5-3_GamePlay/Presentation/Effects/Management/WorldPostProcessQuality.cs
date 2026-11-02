using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 世界常态后处理适配器：三种画面风格与三档后处理性能独立组合。
/// 只创建一次 VolumeProfile，切换时原位更新颜色与光晕，保留所有屏幕警示效果。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Volume))]
public sealed class WorldPostProcessQuality : MonoBehaviour
{
    #region 世界生命周期

    [SerializeField, Tooltip("同一 WorldManager 上的游戏生命周期事件源。")]
    private GameManager gameManager;

    #endregion

    #region 运行时资源

    private Volume worldVolume; // 世界已有的全局 Volume。
    private VolumeProfile runtimeProfile; // 本组件创建的临时配置和子组件。
    private Bloom runtimeBloom;
    private static WorldRenderingConfig.WorldVolume CurrentStyle =>
        WorldRenderingConfigCatalog.Default.postProcess.worldVolumes[(int)ScreenPostProcessSettings.Style];

    #endregion

    #region 生命周期

    /// <summary>绑定世界事件；WorldManager 跨场景常驻，菜单阶段停用常态后处理。</summary>
    private void OnEnable()
    {
        worldVolume = GetComponent<Volume>();
        worldVolume.enabled = false;
        gameManager.Event_GameWorldEnter += EnterWorld;
        gameManager.Event_GameWorldExit += ExitWorld;

        if (gameManager.IsInGameWorld)
            EnterWorld();
    }

    /// <summary>解除原事件源并释放画面资源，销毁阶段不重新查询单例。</summary>
    private void OnDisable()
    {
        if (gameManager != null)
        {
            gameManager.Event_GameWorldEnter -= EnterWorld;
            gameManager.Event_GameWorldExit -= ExitWorld;
        }

        ExitWorld();
    }

    /// <summary>进入世界时仅创建一次 Volume 配置，画风切换复用已有组件。</summary>
    private void EnterWorld()
    {
        if (runtimeProfile != null)
            return;

        runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeProfile.name = "World Rendering JSON Volume";
        runtimeBloom = runtimeProfile.Add<Bloom>(true);
        runtimeProfile.Add<ColorAdjustments>(true);
        runtimeProfile.Add<WhiteBalance>(true);
        runtimeProfile.Add<SplitToning>(true);
        runtimeProfile.Add<Tonemapping>(true);
        runtimeProfile.Add<Vignette>(true);

        worldVolume.sharedProfile = runtimeProfile;
        ScreenPostProcessSettings.Changed += ApplyStyleAndQuality;
        ApplyStyleAndQuality();
        worldVolume.enabled = true;
    }

    /// <summary>取消订阅并释放运行时配置及其子资源，防止反复进出世界累积配置。</summary>
    private void ExitWorld()
    {
        ScreenPostProcessSettings.Changed -= ApplyStyleAndQuality;
        if (worldVolume != null)
            worldVolume.enabled = false;

        if (runtimeProfile == null)
            return;

        if (worldVolume != null)
            worldVolume.sharedProfile = null;

        foreach (VolumeComponent component in runtimeProfile.components)
            Destroy(component);

        Destroy(runtimeProfile);
        runtimeProfile = null;
        runtimeBloom = null;
    }

    #endregion

    #region 画面风格与性能适配

    /// <summary>颜色与光晕按当前风格更新；性能档位只降低 Bloom 成本，不改变昼夜权威光照。</summary>
    private void ApplyStyleAndQuality()
    {
        if (runtimeProfile == null || runtimeBloom == null)
            return;

        WorldRenderingConfig.WorldVolume style = CurrentStyle;
        ScreenPostProcessQuality quality = ScreenPostProcessSettings.Quality;
        bool medium = quality == ScreenPostProcessQuality.Medium;

        // 所有风格在低档保留调色，但跳过 Bloom 全部采样链。
        runtimeBloom.active = style.bloom.enabled && quality != ScreenPostProcessQuality.Low;
        runtimeBloom.threshold.Override(style.bloom.threshold);
        runtimeBloom.intensity.Override(style.bloom.intensity *
            (medium ? style.mediumBloomIntensityScale : 1f));
        runtimeBloom.scatter.Override(style.bloom.scatter);
        runtimeBloom.clamp.Override(style.bloom.clamp);
        runtimeBloom.highQualityFiltering.Override(
            quality == ScreenPostProcessQuality.High && style.bloom.highQualityFiltering);
        runtimeBloom.downscale.Override(medium ? BloomDownscaleMode.Quarter :
            (BloomDownscaleMode)style.bloom.downscale);
        runtimeBloom.maxIterations.Override(medium
            ? Mathf.Min(style.mediumBloomMaxIterations, style.bloom.maxIterations)
            : style.bloom.maxIterations);

        runtimeProfile.TryGet(out ColorAdjustments grading);
        grading.postExposure.Override(style.colorGrading.postExposure);
        grading.contrast.Override(style.colorGrading.contrast);
        grading.saturation.Override(style.colorGrading.saturation);

        runtimeProfile.TryGet(out WhiteBalance balance);
        balance.temperature.Override(style.whiteBalance.temperature);
        balance.tint.Override(style.whiteBalance.tint);

        runtimeProfile.TryGet(out SplitToning toning);
        toning.shadows.Override(style.splitToning.shadows);
        toning.highlights.Override(style.splitToning.highlights);
        toning.balance.Override(style.splitToning.balance);

        runtimeProfile.TryGet(out Tonemapping mapping);
        mapping.mode.Override((TonemappingMode)style.tonemappingMode);

        runtimeProfile.TryGet(out Vignette vignette);
        vignette.color.Override(style.vignette.color);
        vignette.center.Override(style.vignette.center);
        vignette.intensity.Override(style.vignette.intensity);
        vignette.smoothness.Override(style.vignette.smoothness);
        vignette.rounded.Override(style.vignette.rounded);
    }

    #endregion
}
