using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 世界常态后处理的画质适配器，挂在 WorldManager 的 Global Volume 上。
/// 美术参数由世界渲染 JSON 定义，只在世界存续期间创建并调整泛光成本：
/// 高档使用配置原值，中档按 JSON 降低采样与强度，低档关闭泛光。
/// 调色、色调映射与轻暗角在各档保持一致；低血量警示仍由高优先级的独立 Volume 合成。
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
    private static WorldRenderingConfig.WorldVolume Defaults =>
        WorldRenderingConfigCatalog.Default.postProcess.worldVolume;

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

    /// <summary>进入世界时从统一 JSON 创建画面配置，按当前特效质量启用画面。</summary>
    private void EnterWorld()
    {
        if (runtimeProfile != null)
            return;

        runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        runtimeProfile.name = "World Rendering JSON Volume";
        runtimeBloom = runtimeProfile.Add<Bloom>(true);
        runtimeBloom.threshold.Override(Defaults.bloom.threshold);
        runtimeBloom.intensity.Override(Defaults.bloom.intensity);
        runtimeBloom.scatter.Override(Defaults.bloom.scatter);
        runtimeBloom.clamp.Override(Defaults.bloom.clamp);
        runtimeBloom.highQualityFiltering.Override(Defaults.bloom.highQualityFiltering);
        runtimeBloom.downscale.Override((BloomDownscaleMode)Defaults.bloom.downscale);
        runtimeBloom.maxIterations.Override(Defaults.bloom.maxIterations);

        ColorAdjustments grading = runtimeProfile.Add<ColorAdjustments>(true);
        grading.postExposure.Override(Defaults.colorGrading.postExposure);
        grading.contrast.Override(Defaults.colorGrading.contrast);
        grading.saturation.Override(Defaults.colorGrading.saturation);

        WhiteBalance whiteBalance = runtimeProfile.Add<WhiteBalance>(true);
        whiteBalance.temperature.Override(Defaults.whiteBalance.temperature);
        whiteBalance.tint.Override(Defaults.whiteBalance.tint);

        SplitToning splitToning = runtimeProfile.Add<SplitToning>(true);
        splitToning.shadows.Override(Defaults.splitToning.shadows);
        splitToning.highlights.Override(Defaults.splitToning.highlights);
        splitToning.balance.Override(Defaults.splitToning.balance);

        Tonemapping tonemapping = runtimeProfile.Add<Tonemapping>(true);
        tonemapping.mode.Override((TonemappingMode)Defaults.tonemappingMode);

        Vignette vignette = runtimeProfile.Add<Vignette>(true);
        vignette.color.Override(Defaults.vignette.color);
        vignette.center.Override(Defaults.vignette.center);
        vignette.intensity.Override(Defaults.vignette.intensity);
        vignette.smoothness.Override(Defaults.vignette.smoothness);
        vignette.rounded.Override(Defaults.vignette.rounded);

        worldVolume.sharedProfile = runtimeProfile;
        ScreenPostProcessSettings.Changed += ApplyQuality;
        ApplyQuality();
        worldVolume.enabled = true;
    }

    /// <summary>取消订阅并释放运行时配置及其子资源，防止反复进出世界累积配置。</summary>
    private void ExitWorld()
    {
        ScreenPostProcessSettings.Changed -= ApplyQuality;
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

    #region 画质适配

    /// <summary>从 JSON 原值重新计算泛光，避免多次切换后强度累计衰减。</summary>
    private void ApplyQuality()
    {
        ScreenPostProcessQuality quality = ScreenPostProcessSettings.Quality;
        bool medium = quality == ScreenPostProcessQuality.Medium;

        // 低档停用整个泛光组件，省去下采样与上采样链；调色继续共用 URP LUT。
        runtimeBloom.active = Defaults.bloom.enabled && quality != ScreenPostProcessQuality.Low;
        runtimeBloom.intensity.Override(Defaults.bloom.intensity *
            (medium ? Defaults.mediumBloomIntensityScale : 1f));
        runtimeBloom.highQualityFiltering.Override(
            quality == ScreenPostProcessQuality.High && Defaults.bloom.highQualityFiltering);
        runtimeBloom.downscale.Override(medium ? BloomDownscaleMode.Quarter :
            (BloomDownscaleMode)Defaults.bloom.downscale);
        runtimeBloom.maxIterations.Override(medium
            ? Mathf.Min(Defaults.mediumBloomMaxIterations, Defaults.bloom.maxIterations)
            : Defaults.bloom.maxIterations);
    }

    #endregion
}
