using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>渲染前集中筛掉任何局部光都照不到的静态墙阴影，保留原有阴影几何。</summary>
public static class ChunkLightOccluderVisibilityScheduler
{
    #region 注册与渲染调度

    private const float IntersectionEpsilon = 0.001f;
    private static readonly HashSet<ChunkLightOccluderRenderer> Renderers = new();
    private static List<BoundingSphere> lightSpheres = new();
    private static List<BoundingSphere> nextLightSpheres = new();
    private static bool subscribed;
    private static bool keepAllOccluders;
    private static bool hasSnapshot;
    private static int snapshotFrame = -1;

    internal static void Register(ChunkLightOccluderRenderer renderer)
    {
        if (renderer == null || !Renderers.Add(renderer) || subscribed)
            return;

        RenderPipelineManager.beginContextRendering += BeforeContextRendering;
        subscribed = true;
    }

    internal static void Unregister(ChunkLightOccluderRenderer renderer)
    {
        if (!Renderers.Remove(renderer) || Renderers.Count != 0)
            return;

        RenderPipelineManager.beginContextRendering -= BeforeContextRendering;
        subscribed = false;
        lightSpheres.Clear();
        nextLightSpheres.Clear();
        hasSnapshot = false;
        snapshotFrame = -1;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        RenderPipelineManager.beginContextRendering -= BeforeContextRendering;
        subscribed = false;
        Renderers.Clear();
        lightSpheres.Clear();
        nextLightSpheres.Clear();
        keepAllOccluders = false;
        hasSnapshot = false;
        snapshotFrame = -1;
    }

    /// <summary>等待全部 LateUpdate 和环绕灯代理同步完成，同一帧的多个相机共用一次灯快照。</summary>
    private static void BeforeContextRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (snapshotFrame == Time.frameCount)
            return;

        snapshotFrame = Time.frameCount;
        bool lightsChanged = RefreshLightSnapshot();
        foreach (ChunkLightOccluderRenderer renderer in Renderers)
        {
            if (renderer != null)
                renderer.RefreshShadowVisibility(lightSpheres, keepAllOccluders, lightsChanged);
        }
    }

    #endregion

    #region 局部投影光快照

    private static bool RefreshLightSnapshot()
    {
        nextLightSpheres.Clear();
        bool nextKeepAll = !WorldLightRegistry.TryGetActiveLights(out IReadOnlyList<Light2D> lights);
        if (!nextKeepAll)
        {
            try
            {
                if (lights == null)
                {
                    nextKeepAll = true;
                }
                else
                {
                    for (int i = 0; i < lights.Count; i++)
                    {
                        Light2D light = lights[i];
                        if (light == null || !light.isActiveAndEnabled ||
                            light.lightType == Light2D.LightType.Global ||
                            !(light.shadowsEnabled && light.shadowIntensity > 0f ||
                              light.volumetricShadowsEnabled && light.shadowVolumeIntensity > 0f))
                        {
                            continue;
                        }

                        // 非点光和异常范围保持原表现，不能为了筛选墙体引入漏光。
                        if (light.lightType != Light2D.LightType.Point)
                        {
                            nextKeepAll = true;
                            break;
                        }

                        var sphere = new BoundingSphere(light.transform.position, light.pointLightOuterRadius);
                        if (!IsFinite(sphere))
                        {
                            nextKeepAll = true;
                            break;
                        }
                        nextLightSpheres.Add(sphere);
                    }
                }
            }
            catch (Exception)
            {
                nextKeepAll = true;
            }
        }

        if (nextKeepAll)
            nextLightSpheres.Clear();
        bool changed = !hasSnapshot || keepAllOccluders != nextKeepAll ||
                       !SnapshotsEqual(lightSpheres, nextLightSpheres);
        if (changed)
        {
            List<BoundingSphere> previous = lightSpheres;
            lightSpheres = nextLightSpheres;
            nextLightSpheres = previous;
            keepAllOccluders = nextKeepAll;
        }
        hasSnapshot = true;
        return changed;
    }

    private static bool SnapshotsEqual(List<BoundingSphere> previous, List<BoundingSphere> current)
    {
        if (previous.Count != current.Count)
            return false;
        for (int i = 0; i < previous.Count; i++)
        {
            if (!previous[i].position.Equals(current[i].position) || previous[i].radius != current[i].radius)
                return false;
        }
        return true;
    }

    #endregion

    #region 保守球形相交

    /// <summary>保留 Z 和边界余量，完整包含 URP ShadowCaster2D.IsLit 的球形相交结果。</summary>
    public static bool SpheresIntersect(BoundingSphere first, BoundingSphere second)
    {
        if (!IsFinite(first) || !IsFinite(second))
            return true;
        float radius = first.radius + second.radius + IntersectionEpsilon;
        return Vector3.SqrMagnitude(first.position - second.position) <= radius * radius;
    }

    internal static bool IsFinite(BoundingSphere sphere)
        => IsFinite(sphere.position.x) && IsFinite(sphere.position.y) && IsFinite(sphere.position.z) &&
           IsFinite(sphere.radius) && sphere.radius >= 0f;

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal static bool IntersectsAny(BoundingSphere sphere, IReadOnlyList<BoundingSphere> lights)
    {
        for (int i = 0; i < lights.Count; i++)
        {
            if (SpheresIntersect(sphere, lights[i]))
                return true;
        }
        return false;
    }

    #endregion
}
