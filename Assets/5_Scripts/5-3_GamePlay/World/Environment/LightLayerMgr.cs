using System;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

/// <summary>
/// 维护已加载区块的逐格光照层。
/// 光照值范围为 0~1，来源为全局光与 Point Light2D 的近似叠加。
/// </summary>
[PersistentRuntimeObjectCategoryAttribute(PersistentRuntimeObjectCategory.Rendering)]
public class LightLayerMgr : SingletonAutoMono<LightLayerMgr>
{
    #region 光源输入与回退缓存

    public const float CompletelyDarkValue = 0f;
    private const string RuntimeLightLayerId = "light";

    private static readonly ProfilerMarker SampleMarker = new("FlatWorld.Lighting.SamplePoint");
    private static readonly ProfilerMarker FallbackMarker = new("FlatWorld.Lighting.FallbackSceneScan");

    [Header("登记表不可用时的回退刷新")]
    [Tooltip("仅在 URP 登记表适配失败时，按实际查询刷新场景灯成员")]
    [FormerlySerializedAs("refreshInterval")]
    [SerializeField, Min(0.05f)]
    private float activeChunkRefreshInterval = 0.25f;

    [SerializeField, Min(0.1f)]
    private float inactiveChunkRefreshInterval = 5f;

    [SerializeField, Min(0f)]
    private float darknessEpsilon = 0.0001f;

    private Light2D[] _fallbackLights = Array.Empty<Light2D>();
    private Light2D _timeGlobalLight;
    private float _timeLightValue;
    private bool _hasTimeLightValue;
    private float _nextFallbackRefreshTime;
    private bool _fallbackInitialized;

    /// <summary>
    /// 由时间系统提供当前太阳光贡献。
    /// 这个接口只更新运行时快照，不访问未加载的区块存档。
    /// </summary>
    public void SetTimeLighting(
        Light2D source,
        float intensity,
        Color color,
        float activeRefreshInterval,
        float inactiveRefreshInterval)
    {
        _timeGlobalLight = source;
        _timeLightValue = Mathf.Clamp01(GetColorBrightness(color) * Mathf.Max(0f, intensity));
        _hasTimeLightValue = true;

        activeChunkRefreshInterval = Mathf.Max(0.05f, activeRefreshInterval);
        inactiveChunkRefreshInterval = Mathf.Max(activeChunkRefreshInterval, inactiveRefreshInterval);
    }

    /// <summary>成员已由 URP 即时登记，显式刷新只在登记表不可用时重建回退缓存。</summary>
    public void RefreshAllActiveChunks()
    {
        RefreshFallbackIfRequired(true);
    }

    /// <summary>保留调用入口，不遍历未加载的区块或存档数据。</summary>
    public void RefreshAllInactiveChunks()
    {
        RefreshFallbackIfRequired(true);
    }

    private IReadOnlyList<Light2D> GetActiveLights()
    {
        if (WorldLightRegistry.TryGetActiveLights(out IReadOnlyList<Light2D> lights))
            return lights;
        RefreshFallbackIfRequired(false);
        return _fallbackLights;
    }

    private void RefreshFallbackIfRequired(bool force)
    {
        if (WorldLightRegistry.TryGetActiveLights(out _))
            return;
        float now = Time.unscaledTime;
        if (!force && _fallbackInitialized && now < _nextFallbackRefreshTime)
            return;
        using var scope = FallbackMarker.Auto();
        _fallbackLights = FindObjectsByType<Light2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        _fallbackInitialized = true;
        _nextFallbackRefreshTime = now + Mathf.Max(0.05f, activeChunkRefreshInterval);
    }

    #endregion

    #region 玩法光照查询

    /// <summary>
    /// 获取指定世界坐标当前的实时光照，并同步写回所在格子的光照层。
    /// </summary>
    public bool TryGetLightLevel(Vector2 worldPos, out float lightLevel)
    {
        lightLevel = CompletelyDarkValue;

        ChunkMgr chunkMgr = ChunkMgr.Instance;
        if (chunkMgr == null)
            return false;

        if (!chunkMgr.TryGetRuntimeTerrainTile(worldPos, out RuntimeTerrainTileSample sample))
            return false;

        lightLevel = CreatePointSamplingBatch().Sample(sample);
        return true;
    }

    // 一批植物复用灯成员与拓扑，单格灯参数和权威光照层仍同步读取与提交。
    internal readonly struct PointSamplingBatch
    {
        private readonly IReadOnlyList<Light2D> lights;
        private readonly WorldTopologyDomain domain;
        private readonly Light2D timeSource;
        private readonly float timeValue;
        private readonly bool hasTimeValue;
        private readonly int worldSceneHandle;

        internal PointSamplingBatch(LightLayerMgr owner)
        {
            lights = owner.GetActiveLights();
            domain = WorldTopologyRuntime.GetActiveDomain();
            timeSource = owner._timeGlobalLight;
            timeValue = owner._timeLightValue;
            hasTimeValue = owner._hasTimeLightValue;
            worldSceneHandle = GameplayWorldRegistry.CurrentScene.handle;
        }

        internal float Sample(RuntimeTerrainTileSample sample)
        {
            if (lights == null || sample.Terrain == null || sample.Terrain.IsDisposed) return CompletelyDarkValue;
            float value = EvaluateLights(lights, new Vector2(sample.WorldCell.x + 0.5f, sample.WorldCell.y + 0.5f),
                hasTimeValue, timeSource, timeValue, domain, worldSceneHandle);
            sample.Terrain.SetEnvironmentValue(RuntimeLightLayerId, sample.LocalCell.x, sample.LocalCell.y, value);
            return value;
        }
    }

    internal PointSamplingBatch CreatePointSamplingBatch() => new(this);

    public bool IsCompletelyDark(Vector2 worldPos)
    {
        return TryGetLightLevel(worldPos, out float lightLevel) && lightLevel <= darknessEpsilon;
    }

    // 同次查询只读取一份拓扑域，光源启停、位置、半径和亮度仍实时参与计算。
    internal static float EvaluateLights(IReadOnlyList<Light2D> lights, Vector2 worldPos,
        bool hasTimeValue, Light2D timeSource, float timeValue, WorldTopologyDomain domain, int worldSceneHandle = 0)
    {
        using var scope = SampleMarker.Auto();
        float value = hasTimeValue ? timeValue : 0f;
        for (int i = 0; i < lights.Count && value < 1f; i++)
        {
            Light2D light = lights[i];
            if (light == null)
                continue;
            Light2D.LightType type = light.lightType;
            if ((type != Light2D.LightType.Global && type != Light2D.LightType.Point) ||
                (hasTimeValue && type == Light2D.LightType.Global && light == timeSource) ||
                !light.isActiveAndEnabled || light.intensity <= 0f || !WorldLightRegistry.IsGameplaySource(light))
                continue;
            if (type == Light2D.LightType.Point && worldSceneHandle != 0 &&
                light.gameObject.scene.handle != worldSceneHandle) continue;
            if (type == Light2D.LightType.Global)
                value += GetColorBrightness(light.color) * Mathf.Max(0f, light.intensity);
            else
                value += EvaluatePointLight(light, worldPos, domain);
        }
        return Mathf.Clamp01(value);
    }

    private static float EvaluatePointLight(Light2D light, Vector2 worldPos, WorldTopologyDomain domain)
    {
        float outerRadius = Mathf.Max(0f, light.pointLightOuterRadius);
        if (outerRadius <= 0f)
            return 0f;
        Vector2 position = WorldLightRegistry.GetSourceTransform(light).position;
        float squaredDistance = domain.SqrDistance(
            new float2(worldPos.x, worldPos.y), new float2(position.x, position.y));
        if (squaredDistance >= outerRadius * outerRadius)
            return 0f;
        float innerRadius = Mathf.Clamp(light.pointLightInnerRadius, 0f, outerRadius);
        float attenuation = squaredDistance <= innerRadius * innerRadius
            ? 1f
            : 1f - Mathf.InverseLerp(innerRadius, outerRadius, Mathf.Sqrt(squaredDistance));

        return GetColorBrightness(light.color) * Mathf.Max(0f, light.intensity) * attenuation;
    }

    private static float GetColorBrightness(Color color)
    {
        return Mathf.Clamp01(Mathf.Max(color.r, Mathf.Max(color.g, color.b)) * color.a);
    }

    #endregion
}
