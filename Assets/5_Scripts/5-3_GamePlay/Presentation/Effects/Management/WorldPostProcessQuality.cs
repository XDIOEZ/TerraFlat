using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 世界常态后处理的画质适配器，挂在 WorldManager 的 Global Volume 上。
/// 美术参数由共享 Volume Profile 定义，只在世界存续期间克隆并调整泛光成本：
/// 高档使用配置原值，中档使用四分之一分辨率、最多 4 次迭代和 75% 强度，低档关闭泛光。
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
    private VolumeProfile runtimeProfile; // 本组件拥有的临时配置和子组件。
    private Bloom runtimeBloom; // 仅对克隆的泛光参数应用质量档位。
    private Bloom authoredBloom; // 共享配置作为恢复高档参数的只读来源。

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

    /// <summary>进入世界时克隆美术配置，按当前特效质量启用画面。</summary>
    private void EnterWorld()
    {
        if (runtimeProfile != null)
            return;

        if (worldVolume.sharedProfile == null ||
            !worldVolume.sharedProfile.TryGet(out authoredBloom))
        {
            throw new InvalidOperationException("世界后处理的 Global Volume 必须引用含 Bloom 的画面配置。");
        }

        runtimeProfile = worldVolume.profile;
        runtimeProfile.TryGet(out runtimeBloom);
        ScreenPostProcessSettings.Changed += ApplyQuality;
        ApplyQuality();
        worldVolume.enabled = true;
    }

    /// <summary>取消订阅并释放克隆及其子资源，防止反复进出世界累积配置。</summary>
    private void ExitWorld()
    {
        ScreenPostProcessSettings.Changed -= ApplyQuality;
        if (worldVolume != null)
            worldVolume.enabled = false;

        if (runtimeProfile == null)
            return;

        if (worldVolume != null)
            worldVolume.profile = null;

        foreach (VolumeComponent component in runtimeProfile.components)
            Destroy(component);

        Destroy(runtimeProfile);
        runtimeProfile = null;
        runtimeBloom = null;
        authoredBloom = null;
    }

    #endregion

    #region 画质适配

    /// <summary>从美术原值重新计算泛光，避免多次切换后强度累计衰减。</summary>
    private void ApplyQuality()
    {
        ScreenPostProcessQuality quality = ScreenPostProcessSettings.Quality;
        bool medium = quality == ScreenPostProcessQuality.Medium;

        // 低档停用整个泛光组件，省去下采样与上采样链；调色继续共用 URP LUT。
        runtimeBloom.active = authoredBloom.active && quality != ScreenPostProcessQuality.Low;
        runtimeBloom.intensity.Override(authoredBloom.intensity.value * (medium ? 0.75f : 1f));
        runtimeBloom.highQualityFiltering.Override(
            quality == ScreenPostProcessQuality.High && authoredBloom.highQualityFiltering.value);
        runtimeBloom.downscale.Override(medium ? BloomDownscaleMode.Quarter : authoredBloom.downscale.value);
        runtimeBloom.maxIterations.Override(medium
            ? Mathf.Min(4, authoredBloom.maxIterations.value)
            : authoredBloom.maxIterations.value);
    }

    #endregion
}
