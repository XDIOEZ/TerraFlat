using UnityEngine;

/// <summary>地表云层与宇宙合成共用缩放进度，满屏云之后才交接画面。</summary>
public static class SurfaceZoomTransition
{
    #region 共用表现状态

    public readonly struct Frame
    {
        public readonly float CloudStrength;
        public readonly float Approach;
        public readonly float Closure;
        public readonly float UniverseOpacity;

        public Frame(float cloudStrength, float approach, float closure, float universeOpacity)
        {
            CloudStrength = cloudStrength;
            Approach = approach;
            Closure = closure;
            UniverseOpacity = universeOpacity;
        }
    }

    private static Mod_Cam owner;
    private static int sceneHandle;
    private static int sampledFrame = -1;
    private static float presentedSize;
    private static Frame current;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        owner = null;
        sceneHandle = 0;
        sampledFrame = -1;
        presentedSize = 0f;
        current = default;
    }

    #endregion

    #region 顺序一致的缩放过渡

    public static Frame Sample(Mod_Cam camera)
    {
        if (camera == null || camera.Player == null)
        {
            Reset();
            return default;
        }
        var config = WorldRenderingConfigCatalog.Default;
        var clouds = config.zoomClouds;
        var universe = config.universeView;
        float target = Mathf.Clamp(camera.CurrentOrthographicSize,
            clouds.startOrthographicSize, universe.opaqueOrthographicSize);
        int currentScene = camera.Player.gameObject.scene.handle;
        if (owner != camera || sceneHandle != currentScene)
        {
            owner = camera;
            sceneHandle = currentScene;
            sampledFrame = -1;
            presentedSize = target;
        }
        if (sampledFrame == Time.frameCount) return current;

        float fullCloudSize = Mathf.Lerp(clouds.opaqueOrthographicSize, universe.startOrthographicSize, 0.75f);
        bool inUniverse = presentedSize > universe.startOrthographicSize ||
            presentedSize >= universe.startOrthographicSize && target >= universe.startOrthographicSize;
        float duration = inUniverse ? universe.transitionSeconds : clouds.transitionSeconds;
        float range = inUniverse ? universe.opaqueOrthographicSize - universe.startOrthographicSize :
            universe.startOrthographicSize - clouds.startOrthographicSize;
        float next = duration <= 0f ? target : Mathf.MoveTowards(presentedSize, target,
            range * Mathf.Max(0f, Time.deltaTime) / duration);
        // 大幅拖动或低帧率跨过整个过渡时，正反方向都至少留一帧满屏云。
        if (presentedSize < universe.startOrthographicSize && next >= universe.startOrthographicSize ||
            presentedSize > universe.startOrthographicSize && next <= universe.startOrthographicSize)
            next = universe.startOrthographicSize;
        presentedSize = next;
        float approach = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
            clouds.startOrthographicSize, clouds.opaqueOrthographicSize, presentedSize));
        float closure = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
            clouds.opaqueOrthographicSize, fullCloudSize, presentedSize));
        float universeOpacity = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
            universe.startOrthographicSize, universe.opaqueOrthographicSize, presentedSize));
        current = new Frame(approach, approach, closure, universeOpacity);
        sampledFrame = Time.frameCount;
        return current;
    }

    #endregion
}
