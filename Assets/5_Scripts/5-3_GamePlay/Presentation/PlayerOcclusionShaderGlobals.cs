using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 玩家被高大世界物体遮挡时的 Shader 全局参数桥接。
/// 每台 URP 相机开始渲染前同步本地主角位置；具体哪些 Sprite 参与遮挡由 Renderer 的 _PlayerOccluder 参数决定。
/// </summary>
public static class PlayerOcclusionShaderGlobals
{
    #region Configuration

    private const float MaskRadius = 1.10f;
    private const float MaskFeather = 0.14f;
    private const float OccluderAlpha = 0.18f;
    private const float PlayerCenterOffsetY = 0.18f;
    private const float BehindVerticalPadding = 0.04f;
    private const string PreferenceKey = "FlatWorld.Visual.PlayerOcclusion";
    public const string EnabledSettingKey = "player-occlusion";
    private static bool initialized;
    private static bool enabled;
    private static readonly ISettingsProvider settingsProvider = new OcclusionSettingsProvider();
    public static event Action Changed;

    public static ISettingsProvider SettingsProvider
    {
        get
        {
            SettingsProviderRegistry.Register(settingsProvider);
            return settingsProvider;
        }
    }

    public static bool Enabled
    {
        get
        {
            if (!initialized)
            {
                enabled = PlayerPrefs.GetInt(PreferenceKey, 0) != 0;
                initialized = true;
            }
            return enabled;
        }
    }

    /// <summary>持久化透视偏好；关闭时立即清空所有渲染通道共用的开关。</summary>
    public static void SetEnabled(bool value)
    {
        if (Enabled == value)
            return;
        enabled = value;
        PlayerPrefs.SetInt(PreferenceKey, value ? 1 : 0);
        PlayerPrefs.Save();
        if (!value)
            Shader.SetGlobalFloat(EnabledId, 0f);
        Changed?.Invoke();
    }

    private sealed class OcclusionSettingsProvider : ISettingsProvider
    {
        public string ProviderId => "player-occlusion";
        public string DisplayName => "物品透视";
        public int Order => 71;
        public IReadOnlyList<ISettingsToggle> ToggleSettings { get; } = new ISettingsToggle[]
        {
            new SettingsToggle(new SettingDescriptor(EnabledSettingKey, "物品透视",
                SettingControlType.Toggle, "visual"), () => Enabled, SetEnabled)
        };
        public IReadOnlyList<ISettingsSlider> SliderSettings => Array.Empty<ISettingsSlider>();
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();
        public void ResetToDefaults() => SetEnabled(false);
    }

    #endregion

    #region Shader Properties

    private static readonly int EnabledId = Shader.PropertyToID("_PlayerOcclusionEnabled");
    private static readonly int CenterId = Shader.PropertyToID("_PlayerOcclusionCenter");
    private static readonly int RadiusId = Shader.PropertyToID("_PlayerOcclusionRadius");
    private static readonly int FeatherId = Shader.PropertyToID("_PlayerOcclusionFeather");
    private static readonly int AlphaId = Shader.PropertyToID("_PlayerOcclusionAlpha");
    private static readonly int VerticalPaddingId = Shader.PropertyToID("_PlayerOcclusionVerticalPadding");
    private static Player localPlayer;

    #endregion

    #region Lifecycle

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        GameManager.Event_PlayerEnterWorld -= HandlePlayerEnteredWorld;
        localPlayer = null;
        SettingsProviderRegistry.Unregister(settingsProvider);
        initialized = false;
        enabled = false;
        Changed = null;
        Shader.SetGlobalFloat(EnabledId, 0f);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SettingsProviderRegistry.Register(settingsProvider);
        RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
        RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
        GameManager.Event_PlayerEnterWorld -= HandlePlayerEnteredWorld;
        GameManager.Event_PlayerEnterWorld += HandlePlayerEnteredWorld;

        Shader.SetGlobalFloat(RadiusId, MaskRadius);
        Shader.SetGlobalFloat(FeatherId, MaskFeather);
        Shader.SetGlobalFloat(AlphaId, OccluderAlpha);
        Shader.SetGlobalFloat(VerticalPaddingId, BehindVerticalPadding);
    }

    #endregion

    #region Rendering

    /// <summary>只追踪本地档案玩家，远程玩家不会驱动本机遮挡窗口。</summary>
    private static void HandlePlayerEnteredWorld(Player player)
    {
        if (player != null && player.IsLocalProfile)
            localPlayer = player;
    }

    /// <summary>在相机提交世界 Sprite 前同步本地主角位置。</summary>
    private static void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!Enabled || localPlayer == null || !localPlayer.gameObject.activeInHierarchy)
        {
            Shader.SetGlobalFloat(EnabledId, 0f);
            return;
        }

        Vector3 playerPosition = localPlayer.transform.position;
        Shader.SetGlobalVector(
            CenterId,
            new Vector4(
                playerPosition.x,
                playerPosition.y + PlayerCenterOffsetY,
                playerPosition.y,
                0f));
        Shader.SetGlobalFloat(EnabledId, 1f);
    }

    #endregion
}
