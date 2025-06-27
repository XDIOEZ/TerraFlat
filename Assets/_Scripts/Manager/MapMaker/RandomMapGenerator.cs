using System;
using UnityEngine;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using Force.DeepCloner;
using UltEvents;
using NUnit;

/// <summary>
/// 随机地图生成器：基于噪声与生物群系（Biome）
/// 支持分帧生成、大地图无缝衔接，以及群系资源的随机生成。
/// </summary>
public class RandomMapGenerator : MonoBehaviour
{
    #region 配置参数
    [Header("地图配置")]
    [Required]
    public Map map;  // 地图管理对象，包含 TileData 和 Tilemap 引用

    [Header("噪声参数")]
    [Tooltip("噪声缩放系数：越小生成越大范围的高低起伏")]
    public float noiseScale = 0.01f;

    [Tooltip("陆地海洋比例：陆地占比，海洋占比")]
    public float landOceanRatio = 0.5f;

    [Tooltip("温度偏移")]
    public float Temp = 0.5f;
    [Tooltip("当前经度")]
    public float Longitude = 0.5f;
    [Tooltip("当前经度")]
    public float LongitudeRate = 0.5f;

    [Header("生物群系列表")]
    [Tooltip("不同温度/湿度对应的生物群系配置")]
    public List<BiomeData> biomes;

    [Header("性能选项")]
    [Tooltip("每帧生成的最大地块数 (0=全部立即生成)")]
    public int tilesPerFrame = 100;

    [Header("边界连接")]
    [Tooltip("是否启用与相邻地图区域的无缝对接")]
    public bool seamlessBorders = true;

    [Header("边界过渡范围")]
    [Range(1, 20)]
    [Tooltip("用于无缝连接时的边界混合宽度（格子数）")]
    public int transitionRange = 5;

    [Header("完成事件")]
    [Tooltip("地图完全生成后触发的事件，可在 Inspector 挂接自定义响应")]
    public UltEvent onMapGenerated = new UltEvent();

    /// <summary>
    /// 地图尺寸，读取自全局存档
    /// </summary>
    public Vector2Int MapSize => SaveAndLoad.Instance.SaveData.MapSize;
    #endregion

    #region 内部变量
    /// <summary>
    /// 地图种子，字符串形式，从存档读取
    /// </summary>
    private string Seed => SaveAndLoad.Instance.SaveData.MapSeed;

    /// <summary>
    /// 种子整数值，用于初始化随机数
    /// </summary>
    private int seed;

    /// <summary>
    /// 系统级可复现随机实例，用于资源生成
    /// </summary>
    private System.Random rng;

    /// <summary>
    /// 缓存 TileData 模板，避免重复加载开销
    /// </summary>
    private Dictionary<string, TileData> tileDataCache = new Dictionary<string, TileData>();
    #endregion

    #region Unity生命周期
    private void Start()
    {
        if (map.Data.TileData.Count <= 0)
            GenerateRandomMap();
    }
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (map == null || map.Data == null) return;

        Vector2Int startPos = map.Data.position;
        Vector2Int size = MapSize;
        Vector3 center = new Vector3(startPos.x + size.x / 2f, startPos.y + size.y / 2f, 0f);
        Vector3 size3D = new Vector3(size.x, size.y, 0.1f);

        // 绘制地图边框（绿色描边）
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, size3D);

        // 可选：显示地图信息（不带背景色）
        UnityEditor.Handles.Label(center,
            $"Map:{startPos}\nSize:{size}",
            new GUIStyle { alignment = TextAnchor.MiddleCenter });
    }
#endif
    #endregion

    #region 地图生成主逻辑
    [Button("生成随机地图")]
    public void GenerateRandomMap()
    {
        if (map == null)
        {
            Debug.LogError("[RandomMapGenerator] 地图引用未设置！");
            return;
        }

        // 初始化随机种子并创建系统随机实例
        seed = string.IsNullOrEmpty(Seed) ? DateTime.Now.GetHashCode() : Seed.GetHashCode();
        UnityEngine.Random.InitState(seed);
        rng = new System.Random(seed);

        ClearMap();
        map.Data.position = SaveAndLoad.Instance.SaveData.ActiveMapPos;

        Vector2Int startPos = map.Data.position;
        Vector2Int size = MapSize;

        if (tilesPerFrame > 0)
            StartCoroutine(GenerateMapCoroutine(startPos, size));
        else
        {
            GenerateAllTiles(startPos, size);
            OnGenerationComplete();
        }

        GenerateMapEdges();
    }

    #endregion

    #region 边界生成
    public void GenerateMapEdges()
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var d in dirs)
        {
            Item edge = RunTimeItemManager.Instance.InstantiateItem("MapEdge");
            if (edge is WorldEdge we)
                we.SetupMapEdge(d);
            else
                Debug.LogError("[RandomMapGenerator] 边界对象类型错误");
        }
        Debug.Log("[RandomMapGenerator] 边界生成完成");
    }
    #endregion

    #region 生成流程
    private IEnumerator GenerateMapCoroutine(Vector2Int startPos, Vector2Int size)
    {
        int processed = 0;
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
            {
                GenerateTileAtPosition(new Vector2Int(startPos.x + x, startPos.y + y));
                if (++processed % tilesPerFrame == 0)
                    yield return null;
            }
        OnGenerationComplete();
    }

    private void GenerateAllTiles(Vector2Int startPos, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
                GenerateTileAtPosition(new Vector2Int(startPos.x + x, startPos.y + y));
    }
    #endregion

    #region 地块生成逻辑 (完善 TODO)
    private void GenerateTileAtPosition(Vector2Int position)
    {
        // 1. 噪声采样坐标
        float gx = position.x * noiseScale;
        float gy = position.y * noiseScale;

        // Fixing the CS0019 error by ensuring consistent types for the operation
        float temp = Mathf.Clamp01(Mathf.PerlinNoise(gx + 2000, gy + 2000) + Temp);
        float humid = Mathf.Clamp01(Mathf.PerlinNoise(gx + 5000f, gy + 5000f));
        float precipitation = Mathf.Clamp01(  Mathf.PerlinNoise(gx + 10000f, gy + 10000f)  );
        float solidityNoise = Mathf.Clamp01(  Mathf.PerlinNoise(gx + 20000f, gy + 20000f)+landOceanRatio);
        float hightNoise = Mathf.Clamp01(Mathf.PerlinNoise(gx + 220000f, gy + 220000f));

        EnvironmentFactors env = new EnvironmentFactors
        {
            Temperature = temp,
            Humidity = humid,
            Precipitation = precipitation,
            Solidity = solidityNoise,
            Hight = hightNoise
        };


        // 3. Biome 匹配（改为循环版本）
        BiomeData biome = null;
        foreach (var b in biomes)
        {
            if (b.IsEnvironmentValid(env))
            {
                biome = b;
                break;  // 找到第一个匹配的就退出循环
            }
        }
        // 如果找不到合适的Biome，跳过后续步骤
        if (biome == null)
        {
            Debug.LogWarning(env);
            return;
        }

        // 4. 生成地形瓦片
        GenerateTerrainTile(position, biome, env);

        // 5. 生成随机资源
        GenerateRandomResources(position, biome, env);
    }


    /// <summary>
    /// 生成地形瓦片 (原步骤3)
    /// </summary>
    private void GenerateTerrainTile(Vector2Int position, BiomeData biome, EnvironmentFactors env)
    {
        // 获取地块预制体
        string key = biome.TerrainConfig.GetTilePrefab(env);

        // 获取或缓存TileData模板
        if (!tileDataCache.ContainsKey(key))
        {

            string name = biome.TerrainConfig.GetTilePrefab(env);

            var prefab = GameRes.Instance.GetPrefab(name);
            if (prefab == null)
            {
                Debug.LogError($"无法获取预制体: {name}，生物群系: {biome.BiomeName}");
                return;
            }

            var blockTile = prefab.GetComponent<IBlockTile>();
            if (blockTile == null)
            {
                Debug.LogError($"预制体 {name} 上未找到 IBlockTile 组件，生物群系: {biome.BiomeName}");
                return;
            }

            tileDataCache[key] = blockTile.TileData;
        }

        // 创建并放置瓦片
        var tile = tileDataCache[key].DeepClone();
        tile.position = new Vector3Int(position.x, position.y, 0);
        map.ADDTile(position, tile);
    }

    /// <summary>
    /// 生成随机资源 (原步骤4)
    /// </summary>
    private void GenerateRandomResources(Vector2Int position, BiomeData biome, EnvironmentFactors env)
    {
        foreach (var spawn in biome.TerrainConfig.ItemSpawn)
        {
            if (rng.NextDouble() <= spawn.SpawnChance)
            {
                Vector3 spawnPosition = new Vector3(
                    position.x + 0.5f,
                    position.y + 0.5f,
                    0f);

                RunTimeItemManager.Instance.InstantiateItem(
                    spawn.itemName,
                    spawnPosition);
            }
        }
    }
    #endregion

    #region 工具方法：清除与完成事件
    private void ClearMap()
    {
        map.tileMap?.ClearAllTiles();
        map.Data.TileData?.Clear();
        Debug.Log("[RandomMapGenerator] 地图已清除");
    }

    private void OnGenerationComplete()
    {
        map.tileMap?.RefreshAllTiles();
        Debug.Log($"[RandomMapGenerator] 地图完成 @ {map.Data.position} 大小{map.Data.size} 种子{seed}");
        onMapGenerated?.Invoke();
    }
    #endregion
}
