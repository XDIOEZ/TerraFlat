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

    private void Update()
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsInGameWorld)
            return;

        float now = Time.unscaledTime;
        bool refreshActive = now >= _nextActiveRefreshTime;
        bool refreshInactive = now >= _nextInactiveRefreshTime;
        if (!refreshActive && !refreshInactive)
            return;

        ChunkMgr chunkMgr = ChunkMgr.Instance;
        if (chunkMgr == null)
            return;

        RefreshLightSourceCache();

        if (refreshActive)
        {
            _nextActiveRefreshTime = now + Mathf.Max(0.05f, activeChunkRefreshInterval);
            RefreshChunks(chunkMgr.Chunk_Dic_Active_ByPos.Values);
        }

        if (refreshInactive)
        {
            _nextInactiveRefreshTime = now + Mathf.Max(activeChunkRefreshInterval, inactiveChunkRefreshInterval);
            RefreshChunks(chunkMgr.Chunk_Dic_UnActive_ByPos.Values);
        }
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

        ChunkMgr chunkMgr = ChunkMgr.Instance;
        if (chunkMgr == null)
            return;

        RefreshChunks(chunkMgr.Chunk_Dic_Active_ByPos.Values);
    }

    /// <summary>
    /// 立即刷新已实例化但失活的区块，不会遍历存档数据。
    /// </summary>
    public void RefreshAllInactiveChunks()
    {
        _nextInactiveRefreshTime = Time.unscaledTime + Mathf.Max(activeChunkRefreshInterval, inactiveChunkRefreshInterval);
        RefreshLightSourceCache();

        ChunkMgr chunkMgr = ChunkMgr.Instance;
        if (chunkMgr == null)
            return;

        RefreshChunks(chunkMgr.Chunk_Dic_UnActive_ByPos.Values);
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

        Vector2Int chunkPos = Chunk.GetChunkPosition(worldPos);
        if (!chunkMgr.TryGetActiveChunkByPos(chunkPos, out Chunk chunk) || !TryGetMapData(chunk, out Data_TileMap mapData))
            return false;

        Vector2Int worldCell = new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
        if (!mapData.TryGetEnvironmentLocalPos(worldCell, out Vector2Int localPos))
            return false;

        // 怪物生成查询必须使用即时值，避免恰好发生在周期刷新之前。
        RefreshLightSourceCache();
        Vector2 cellCenter = new Vector2(worldCell.x + 0.5f, worldCell.y + 0.5f);
        lightLevel = EvaluateLightAt(cellCenter);
        mapData.SetLightAtLocal(localPos.x, localPos.y, lightLevel);
        return true;
    }

    public bool IsCompletelyDark(Vector2 worldPos)
    {
        return TryGetLightLevel(worldPos, out float lightLevel) && lightLevel <= darknessEpsilon;
    }

    private void RefreshChunk(Chunk chunk)
    {
        if (!TryGetMapData(chunk, out Data_TileMap mapData))
            return;

        EnvironmentLayers layers = mapData.EnvironmentLayers;
        float globalLight = EvaluateGlobalLight();
        for (int x = 0; x < layers.Width; x++)
        {
            for (int y = 0; y < layers.GridHeight; y++)
            {
                layers.SetLight(x, y, globalLight);
            }
        }

        if (globalLight >= 1f)
            return;

        // 局部光只遍历自己半径覆盖的格子，避免每个格子遍历全部火把。
        for (int i = 0; i < _pointLights.Count; i++)
        {
            AddPointLightToChunk(_pointLights[i], mapData, layers);
        }
    }

    private static void AddPointLightToChunk(Light2D light, Data_TileMap mapData, EnvironmentLayers layers)
    {
        float outerRadius = Mathf.Max(0f, light.pointLightOuterRadius);
        if (outerRadius <= 0f || light.intensity <= 0f)
            return;

        Vector2 lightPosition = light.transform.position;
        int minX = Mathf.Max(0, Mathf.FloorToInt(lightPosition.x - outerRadius) - mapData.position.x);
        int maxX = Mathf.Min(layers.Width - 1, Mathf.CeilToInt(lightPosition.x + outerRadius) - 1 - mapData.position.x);
        int minY = Mathf.Max(0, Mathf.FloorToInt(lightPosition.y - outerRadius) - mapData.position.y);
        int maxY = Mathf.Min(layers.GridHeight - 1, Mathf.CeilToInt(lightPosition.y + outerRadius) - 1 - mapData.position.y);
        if (minX > maxX || minY > maxY)
            return;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2 cellCenter = new Vector2(mapData.position.x + x + 0.5f, mapData.position.y + y + 0.5f);
                float contribution = EvaluatePointLight(light, cellCenter, outerRadius);
                if (contribution > 0f)
                {
                    layers.SetLight(x, y, layers.GetLight(x, y) + contribution);
                }
            }
        }
    }

    private void RefreshChunks(IEnumerable<Chunk> chunks)
    {
        foreach (Chunk chunk in chunks)
        {
            RefreshChunk(chunk);
        }
    }

    private static bool TryGetMapData(Chunk chunk, out Data_TileMap mapData)
    {
        mapData = chunk?.Map?.Data;
        if (mapData == null)
            return false;

        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        int width = Mathf.Max(1, Mathf.RoundToInt(chunkSize.x));
        int height = Mathf.Max(1, Mathf.RoundToInt(chunkSize.y));
        mapData.EnsureEnvironmentStorage(width, height);
        return mapData.EnvironmentLayers != null && mapData.EnvironmentLayers.IsValidSize(width, height);
    }

    private void RefreshLightSourceCache()
    {
        _otherGlobalLights.Clear();
        _pointLights.Clear();

        Light2D[] lights = FindObjectsOfType<Light2D>(false);
        for (int i = 0; i < lights.Length; i++)
        {
            Light2D light = lights[i];
            if (light == null || !light.enabled || !light.gameObject.activeInHierarchy || light.intensity <= 0f)
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
    }

    private float EvaluateLightAt(Vector2 worldPos)
    {
        float value = EvaluateGlobalLight();

        for (int i = 0; i < _pointLights.Count && value < 1f; i++)
        {
            Light2D light = _pointLights[i];
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
            value += GetColorBrightness(light.color) * Mathf.Max(0f, light.intensity);
        }

        return Mathf.Clamp01(value);
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
