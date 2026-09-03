using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

/// <summary>
/// 维护已加载区块的逐格光照层。
/// 光照值范围为 0~1，来源为全局光与 Point Light2D 的近似叠加。
/// </summary>
public class LightLayerMgr : SingletonAutoMono<LightLayerMgr>
{
    public const float CompletelyDarkValue = 0f;
    private const string RuntimeLightLayerId = "light";

    [Header("光照层分级刷新")]
    [Tooltip("玩家可见/激活区块的刷新间隔")]
    [FormerlySerializedAs("refreshInterval")]
    [SerializeField, Min(0.05f)]
    private float activeChunkRefreshInterval = 0.25f;

    [Tooltip("已实例化但失活区块的刷新间隔；未加载的存档区块不会被遍历")]
    [SerializeField, Min(0.1f)]
    private float inactiveChunkRefreshInterval = 5f;

    [SerializeField, Min(0f)]
    private float darknessEpsilon = 0.0001f;

    private readonly List<Light2D> _otherGlobalLights = new();
    private readonly List<Light2D> _pointLights = new();
    private Light2D _timeGlobalLight;
    private float _timeLightValue;
    private bool _hasTimeLightValue;
    private float _nextActiveRefreshTime;
    private float _nextInactiveRefreshTime;
    private bool _lightSourceCacheInitialized;

    private void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsInGameWorld)
            return;

        float now = Time.unscaledTime;
        if (now < _nextActiveRefreshTime)
            return;

        _nextActiveRefreshTime = now + Mathf.Max(0.05f, activeChunkRefreshInterval);
        RefreshLightSourceCache();
    }

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

    /// <summary>
    /// 立即刷新所有已加载区块的光照层。
    /// </summary>
    public void RefreshAllActiveChunks()
    {
        _nextActiveRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, activeChunkRefreshInterval);
        RefreshLightSourceCache();
    }

    /// <summary>
    /// 立即刷新已实例化但失活的区块，不会遍历存档数据。
    /// </summary>
    public void RefreshAllInactiveChunks()
    {
        _nextInactiveRefreshTime = Time.unscaledTime + Mathf.Max(activeChunkRefreshInterval, inactiveChunkRefreshInterval);
        RefreshLightSourceCache();
    }

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

        // 热路径只读取缓存的光源引用；光源强度/位置仍逐次实时读取。
        // 过去这里每次查询都会 FindObjectsOfType，怪物生成一次位置搜索会触发 5~16 次全场景扫描并制造大量 GC。
        EnsureLightSourceCacheInitialized();
        Vector2 cellCenter = new Vector2(sample.WorldCell.x + 0.5f, sample.WorldCell.y + 0.5f);
        lightLevel = EvaluateLightAt(cellCenter);
        sample.Terrain.SetEnvironmentValue(
            RuntimeLightLayerId, sample.LocalCell.x, sample.LocalCell.y, lightLevel);
        return true;
    }

    public bool IsCompletelyDark(Vector2 worldPos)
    {
        return TryGetLightLevel(worldPos, out float lightLevel) && lightLevel <= darknessEpsilon;
    }

    /// <summary>首次查询前只允许初始化一次，后续成员变化由 Update 的低频刷新负责。</summary>
    private void EnsureLightSourceCacheInitialized()
    {
        if (!_lightSourceCacheInitialized)
            RefreshLightSourceCache();
    }

    private void RefreshLightSourceCache()
    {
        _otherGlobalLights.Clear();
        _pointLights.Clear();

        // 不需要 Unity 默认的实例排序；低频扫描只负责维护成员集合。
        Light2D[] lights = FindObjectsByType<Light2D>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            Light2D light = lights[i];
            if (light == null || !light.enabled || !light.gameObject.activeInHierarchy || light.intensity <= 0f)
                continue;
            if (light.GetComponent<WorldTopologyLightProxy>() != null)
                continue;

            if (light.lightType == Light2D.LightType.Global)
            {
                // 时间系统的全局光已通过 SetTimeLighting 直接传入，避免重复叠加。
                if (!_hasTimeLightValue || light != _timeGlobalLight)
                    _otherGlobalLights.Add(light);
            }
            else if (light.lightType == Light2D.LightType.Point)
                _pointLights.Add(light);
        }

        _lightSourceCacheInitialized = true;
    }

    private float EvaluateLightAt(Vector2 worldPos)
    {
        float value = EvaluateGlobalLight();

        for (int i = 0; i < _pointLights.Count && value < 1f; i++)
        {
            Light2D light = _pointLights[i];
            if (!IsActiveLight(light, Light2D.LightType.Point))
                continue;

            value += EvaluatePointLight(light, worldPos, Mathf.Max(0f, light.pointLightOuterRadius));
        }

        return Mathf.Clamp01(value);
    }

    private float EvaluateGlobalLight()
    {
        float value = _hasTimeLightValue ? _timeLightValue : 0f;

        for (int i = 0; i < _otherGlobalLights.Count; i++)
        {
            Light2D light = _otherGlobalLights[i];
            if (!IsActiveLight(light, Light2D.LightType.Global))
                continue;

            value += GetColorBrightness(light.color) * Mathf.Max(0f, light.intensity);
        }

        return Mathf.Clamp01(value);
    }

    /// <summary>缓存允许短暂保留已禁用/销毁引用，查询时必须无分配地过滤。</summary>
    private static bool IsActiveLight(Light2D light, Light2D.LightType expectedType)
    {
        return light != null &&
               light.enabled &&
               light.gameObject.activeInHierarchy &&
               light.intensity > 0f &&
               light.lightType == expectedType;
    }

    private static float EvaluatePointLight(Light2D light, Vector2 worldPos, float outerRadius)
    {
        if (outerRadius <= 0f)
            return 0f;

        float distance = WorldTopologyRuntime.Distance(worldPos, light.transform.position);
        if (distance >= outerRadius)
            return 0f;

        float innerRadius = Mathf.Clamp(light.pointLightInnerRadius, 0f, outerRadius);
        float attenuation = distance <= innerRadius
            ? 1f
            : 1f - Mathf.InverseLerp(innerRadius, outerRadius, distance);

        return GetColorBrightness(light.color) * Mathf.Max(0f, light.intensity) * attenuation;
    }

    private static float GetColorBrightness(Color color)
    {
        return Mathf.Clamp01(Mathf.Max(color.r, Mathf.Max(color.g, color.b)) * color.a);
    }
}
