using UnityEngine;

/// <summary>地表与宇宙画面共用淡入淡出进度，固定雾圈不参与缩放过渡。</summary>
public static class SurfaceZoomTransition
{
    #region 共用表现状态

    private static Mod_Cam owner;
    private static int sceneHandle;
    private static int sampledFrame = -1;
    private static float currentOpacity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        owner = null;
        sceneHandle = 0;
        sampledFrame = -1;
        currentOpacity = 0f;
    }

    #endregion

    #region 宇宙画面淡入淡出

    public static float ResolveOpacity(float orthographicSize, WorldRenderingConfig.RenderingUniverseView settings)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
            settings.startOrthographicSize, settings.opaqueOrthographicSize, orthographicSize));
    }

    public static float Sample(Mod_Cam camera)
    {
        if (camera == null || camera.Player == null)
        {
            Reset();
            return 0f;
        }
        var settings = WorldRenderingConfigCatalog.Default.universeView;
        float target = ResolveOpacity(camera.CurrentOrthographicSize, settings);
        int currentScene = camera.Player.gameObject.scene.handle;
        if (owner != camera || sceneHandle != currentScene)
        {
            owner = camera;
            sceneHandle = currentScene;
            sampledFrame = -1;
            currentOpacity = target;
        }
        if (sampledFrame == Time.frameCount) return currentOpacity;

        // 大幅拉远或拉近时直接平滑交接宇宙画面，不插入满屏白雾。
        currentOpacity = settings.transitionSeconds <= 0f ? target :
            Mathf.MoveTowards(currentOpacity, target, Mathf.Max(0f, Time.deltaTime) / settings.transitionSeconds);
        sampledFrame = Time.frameCount;
        return currentOpacity;
    }

    #endregion
}
