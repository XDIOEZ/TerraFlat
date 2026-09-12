using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 玩家被高大世界物体遮挡时的 Shader 全局参数桥接。
/// 每台 URP 相机开始渲染前同步本地主角位置；具体哪些 Sprite 参与遮挡由 Renderer 的 _PlayerOccluder 参数决定。
/// </summary>
public static class PlayerOcclusionShaderGlobals
{
    #region Configuration

    private const float MaskRadius = 0.55f;
    private const float MaskFeather = 0.14f;
    private const float OccluderAlpha = 0.18f;
    private const float PlayerCenterOffsetY = 0.18f;
    private const float BehindVerticalPadding = 0.04f;

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
        Shader.SetGlobalFloat(EnabledId, 0f);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
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
        if (localPlayer == null || !localPlayer.gameObject.activeInHierarchy)
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
