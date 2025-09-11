using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using Force.DeepCloner;
using UltEvents;

/// <summary>
/// 随机地图生成器：
/// - 基于噪声 + 生物群系（Biome）
/// - 支持分帧生成 / 大地图无缝衔接 / 群系资源随机生成
/// </summary>
public class RandomMapGenerator : MonoBehaviour
{
    #region 配置参数
    [Header("地图配置")]
    [Required] public Map map; // 地图管理对象

    [ShowInInspector]
    public PlanetData plantData => SaveDataMgr.Instance.Active_PlanetData;

    public Vector2 ChunkSize => ChunkMgr.GetChunkSize();

    [Tooltip("赤道坐标")] public float Equator = 0;

    [Header("生物群系列表")]
    [Tooltip("不同温度/湿度对应的生物群系配置")]
    public List<BiomeData> biomes;

    [Header("性能选项")]
    [Tooltip("每帧生成的最大地块数 (0=立即生成)")]
    public int tilesPerFrame = 1;

    [Header("边界连接")]
    public bool seamlessBorders = true;

    [Header("边界过渡范围")]
    [Range(1, 20)]
    [Tooltip("用于无缝连接时的边界混合宽度（格子数）")]
    public int transitionRange = 5;

    // Debug 调试用颜色字典
    public Dictionary<Vector2Int, Color> ColorDicitionary = new();

    #region —— Inspector 可配置噪声 ——
    // 这些都继承自 BaseNoise，方便你在 Inspector 里换不同噪声算法
    [Header("应用噪声")]
    [Tooltip("高度噪声(陆地/海洋)")]
    public BaseNoise LandNoise;

    [Tooltip("湿度")]
    public BaseNoise HumidityNoise;

    [Tooltip("降水")]
    public BaseNoise PrecipitationNoise;

    [Tooltip("温度")]
    public BaseNoise TemperatureNoise;

    [Tooltip("河流")]
    public BaseNoise RieverNoise;

    [Tooltip("地块固化程度/坚硬度 (例如岩石/泥土区别)")]
    public BaseNoise SolidityNoise;

    #endregion
    #endregion

    #region 内部变量
    private int Seed => SaveDataMgr.Instance.SaveData.Seed;

    public float PlantRadius => plantData.Radius;
    public float Temp => plantData.TemperatureOffset;
    public float LandOceanRatio => plantData.OceanHeight;
    public float NoiseScale => plantData.NoiseScale;

    public static System.Random rng; // 系统级随机实例

    private Dictionary<string, TileData> tileDataCache = new(); // TileData 缓存
    #endregion

    #region Unity 生命周期
    public void Awake()
    {
        rng = new System.Random(Seed);
        map.OnMapGenerated_Start += GenerateRandomMap_TileData;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (map == null || map.Data == null) return;

        Vector2 startPos = map.Data.position;
        Vector2 size = ChunkMgr.GetChunkSize();
        Vector3 center = new(startPos.x + size.x / 2f, startPos.y + size.y / 2f, 0f);
        Vector3 size3D = new(size.x, size.y, 0.1f);

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, size3D);

        UnityEditor.Handles.Label(center,
            $"Map:{startPos}\nSize:{size}",
            new GUIStyle { alignment = TextAnchor.MiddleCenter });
    }
#endif
    #endregion

    #region 主逻辑
    [Button("生成随机地图")]
    public void GenerateRandomMap_TileData()
    {
        if (map == null)
        {
            Debug.LogError("[RandomMapGenerator] 地图引用未设置！");
            return;
        }

        ClearMap();

        map.Data.position = new Vector2Int(
            Mathf.RoundToInt(transform.parent.position.x),
            Mathf.RoundToInt(transform.parent.position.y)
        );

        Vector2Int startPos = map.Data.position;
        Vector2 size = ChunkSize;

        if (tilesPerFrame == 114514) // 特殊：协程分帧生成
            ChunkMgr.Instance.StartCoroutine(GenerateMapCoroutine(startPos, size));
        else
        {
            GenerateAllTiles(startPos, size);
            OnGenerationComplete();
        }
    }
    #endregion

    #region 地图生成流程
    private IEnumerator GenerateMapCoroutine(Vector2Int startPos, Vector2 size)
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

    private void GenerateAllTiles(Vector2Int startPos, Vector2 size)
    {
        for (int x = 0; x < size.x; x++)
            for (int y = 0; y < size.y; y++)
                GenerateTileAtPosition(new Vector2Int(startPos.x + x, startPos.y + y));
    }
    #endregion

    #region 地块生成逻辑
    private void GenerateTileAtPosition(Vector2Int position)
    {
        // 1. 采样噪声 —— 转换成世界坐标
        float gx = position.x * NoiseScale;
        float gy = position.y * NoiseScale;

        // 使用 ScriptableObject 的噪声配置进行采样
        float temp = TemperatureNoise != null ? TemperatureNoise.Sample(gx, gy, Seed) : 0.5f;
        float humid = HumidityNoise != null ? HumidityNoise.Sample(gx, gy, Seed) : 0.5f;
        float precip = PrecipitationNoise != null ? PrecipitationNoise.Sample(gx, gy, Seed) : 0.5f;
        float solidity = SolidityNoise != null ? SolidityNoise.Sample(gx, gy, Seed) : 0.5f; // solidity 你之前写的是 solidit
        float hight = LandNoise != null ? LandNoise.Sample(gx, gy, Seed) : 0.5f;
        float Water = RieverNoise.Sample(gx, gy, Seed);
        // 2. 封装为环境因子
        EnvironmentFactors env = new()
        {
            Temperature = Mathf.Clamp01(temp),
            Humidity = Mathf.Clamp01(humid),
            Precipitation = Mathf.Clamp01(precip),
            Solidity = Mathf.Clamp01(solidity),
            Hight = Mathf.Clamp01(hight)
        };

        // 3. Biome 匹配 —— 找到第一个符合条件的群系
        BiomeData biome = null;
        foreach (var b in biomes)
        {
            if (b.IsEnvironmentValid(env))
            {
                biome = b;
                break;
            }
        }
        if (biome == null) return; // 没有匹配群系就跳过

        // 4. 设置预览颜色（调试用）
        ColorDicitionary[position] = biome.PreviewColor;

        // 5. 生成瓦片与资源
        GenerateTerrainTile(position, biome, env);
        if(Water > 0.5f)
        AddTile(position, RieverNoise.biomeData.TerrainConfig.TileSpawns[0].TileDataName);
        GenerateRandomResources(position, biome, env);
    }

    public void AddTile(Vector2Int position, string key)
    {
        if (!tileDataCache.ContainsKey(key))
        {
            var prefab = GameRes.Instance.GetPrefab(key);
            if (prefab == null)
            {
                return;
            }

            var blockTile = prefab.GetComponent<IBlockTile>();
            if (blockTile == null)
            {
                return;
            }

            tileDataCache[key] = blockTile.TileData;
        }
        var tile = tileDataCache[key].DeepClone();
        map.ADDTile(position, tile);
    }
    private void GenerateTerrainTile(Vector2Int position, BiomeData biome, EnvironmentFactors env)
    {
        string key = biome.TerrainConfig.GetTilePrefab(env);

        if (!tileDataCache.ContainsKey(key))
        {
            var prefab = GameRes.Instance.GetPrefab(key);
            if (prefab == null)
            {
                Debug.LogError($"无法获取预制体: {key}，群系: {biome.BiomeName}");
                return;
            }

            var blockTile = prefab.GetComponent<IBlockTile>();
            if (blockTile == null)
            {
                Debug.LogError($"预制体 {key} 缺少 IBlockTile，群系: {biome.BiomeName}");
                return;
            }

            tileDataCache[key] = blockTile.TileData;
        }

        var tile = tileDataCache[key].DeepClone();
        tile.position = new Vector3Int(position.x, position.y, 0);
        map.ADDTile(position, tile);
    }



    private void GenerateRandomResources(Vector2Int pos, BiomeData biome, EnvironmentFactors env)
    {
        uint state = (uint)(pos.x * 114514 ^ pos.y * 1919810);

        foreach (Biome_ItemSpawn spawn in biome.TerrainConfig.ItemSpawn)
        {
            if (!spawn.environmentConditionRange.IsMatch(env)) continue;

            float chance = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;
            if (chance > spawn.SpawnChance) continue;

            float offsetX = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;
            float offsetY = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;

            Vector2 spawnPos = new(pos.x + offsetX + 0.5f, pos.y + offsetY + 0.5f);

            ItemMgr.Instance.InstantiateItem(
                spawn.itemName,
                spawnPos,
                default,
                default,
                map.ParentObject
            );
        }
    }
    #endregion

    #region 边界
    public void GenerateMapEdges()
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var d in dirs)
        {
            Item edge = ItemMgr.Instance.InstantiateItem("MapEdge", default, default, default, map.ParentObject);
            if (edge is WorldEdge we) we.SetupMapEdge(d, map.Data.position);
            else Debug.LogError("[RandomMapGenerator] 边界对象类型错误");
        }
        Debug.Log("[RandomMapGenerator] 边界生成完成");
    }
    #endregion

    #region 工具方法
    private void ClearMap()
    {
        map.tileMap?.ClearAllTiles();
        map.Data.TileData?.Clear();
        Debug.Log("[RandomMapGenerator] 地图已清除");
    }

    private void OnGenerationComplete()
    {
        map.tileMap?.RefreshAllTiles();
        map.Data.TileLoaded = true;
    }
    private static uint Xorshift32(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
    #endregion
}
