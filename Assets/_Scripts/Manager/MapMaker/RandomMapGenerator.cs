using System;
using UnityEngine;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using Force.DeepCloner;
using UltEvents;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Text;
using Codice.Client.BaseCommands;

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

    [ShowInInspector]
    public PlanetData plantData => SaveDataMgr.Instance.Active_PlanetData;

    public Vector2 ChunkSize => ChunkMgr.GetChunkSize();
    [Tooltip("赤道坐标")]
    public float Equator = 0;

    [Header("生物群系列表")]
    [Tooltip("不同温度/湿度对应的生物群系配置")]
    public List<BiomeData> biomes;

    [Header("性能选项")]
    [Tooltip("每帧生成的最大地块数 (0=全部立即生成)")]
    public int tilesPerFrame = 1;

    [Header("边界连接")]
    [Tooltip("是否启用与相邻地图区域的无缝对接")]
    public bool seamlessBorders = true;

    [Header("边界过渡范围")]
    [Range(1, 20)]
    [Tooltip("用于无缝连接时的边界混合宽度（格子数）")]
    public int transitionRange = 5;
    //Debug使用
    public Dictionary<Vector2Int,Color> ColorDicitionary  = new ();

    #endregion

    #region 内部变量
    /// <summary>
    /// 地图种子，字符串形式，从存档读取
    /// </summary>
    private int Seed => SaveDataMgr.Instance.SaveData.Seed;

    public float PlantRadius { get => plantData.Radius;}
    public float Temp { get => plantData.TemperatureOffset; }
    public float LandOceanRatio { get => plantData.OceanHeight;}
    public float NoiseScale { get => plantData.NoiseScale;  }

    /// <summary>
    /// 系统级可复现随机实例，用于资源生成
    /// </summary>
    public static System.Random  rng;

    /// <summary>
    /// 缓存 TileData 模板，避免重复加载开销
    /// </summary>
    private Dictionary<string, TileData> tileDataCache = new Dictionary<string, TileData>();
    #endregion

    #region Unity生命周期
    public void Awake()
    {
        RandomMapGenerator.rng = new System.Random(SaveDataMgr.Instance.SaveData.Seed);
        map.OnMapGenerated_Start += GenerateRandomMap_TileData;
    }
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (map == null || map.Data == null) return;

        Vector2 startPos = map.Data.position;
        Vector2 size = ChunkMgr.GetChunkSize();
        Vector3 center = new Vector3(startPos.x + size.x / 2f, startPos.y + size.y / 2f, 0f);
        Vector3 size3D = new Vector3(size.x, size.y, 0.1f);

        // 绘制地图边框（绿色描边）
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, size3D);

        UnityEditor.Handles.Label(center,
            $"Map:{startPos}\nSize:{size}",
            new GUIStyle { alignment = TextAnchor.MiddleCenter });
    }
#endif
    #endregion

    #region 地图生成主逻辑
    [Button("生成随机地图")]
    public void GenerateRandomMap_TileData()
    {
        if (map == null)
        {
            Debug.LogError("[RandomMapGenerator] 地图引用未设置！");
            return;
        }

        ClearMap();


        // Fix for CS0029: Convert Vector3 to Vector2Int explicitly using Vector3Int.RoundToInt and then accessing x and y properties.
        map.Data.position = new Vector2Int(
           Mathf.RoundToInt(transform.parent.transform.position.x),
           Mathf.RoundToInt(transform.parent.transform.position.y)
        );

        Vector2Int startPos = map.Data.position;
        Vector2 size = ChunkSize;

        if (tilesPerFrame == 114514)
        {
            ChunkMgr.Instance.StartCoroutine(GenerateMapCoroutine(startPos, size));
        }
        else
        {
            GenerateAllTiles(startPos, size);
            OnGenerationComplete();
        }

    }

    #endregion

    #region 边界生成
    public void GenerateMapEdges()
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var d in dirs)
        {
            Item edge = ItemMgr.Instance.InstantiateItem("MapEdge",default,default,default,map.ParentObject);
            if (edge is WorldEdge we)
                we.SetupMapEdge(d, map.Data.position);
            else
                Debug.LogError("[RandomMapGenerator] 边界对象类型错误");
        }
        Debug.Log("[RandomMapGenerator] 边界生成完成");
    }
    #endregion

    #region 生成流程
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

    #region 地块生成逻辑 (完善 TODO)
    private void GenerateTileAtPosition(Vector2Int position)
    {
        // 1. 噪声采样坐标
        float gx = position.x * NoiseScale;
        float gy = position.y * NoiseScale;

        // Fixing the CS0019 error by ensuring consistent types for the operation

        float seedofNoise = (float)Seed*0.000001f;
        // 使用种子修改噪声函数的输入
        float temp = Mathf.PerlinNoise(gx + 2000 + seedofNoise, gy + 2000 + seedofNoise);
        float humid = Mathf.PerlinNoise(gx + 5000f + seedofNoise, gy + 5000f + seedofNoise);
        float precip = Mathf.PerlinNoise(gx + 10000f + seedofNoise, gy + 10000f + seedofNoise);
        float solidity = Mathf.PerlinNoise(gx + 20000f + seedofNoise, gy + 20000f + seedofNoise);
        float hight = Mathf.PerlinNoise(gx + 220000f + seedofNoise, gy + 220000f + seedofNoise);
        //位于赤道附近 且地球半径为100
        //(0+ 50)/100 = 0.5 的温度偏移系数   所以赤道的期望温度为 0.5 + 0.5 = 1
        temp += Temp + (PlantRadius / 2f - Mathf.Abs(position.y)) / PlantRadius;
        solidity -= LandOceanRatio;
        hight -= LandOceanRatio;


        EnvironmentFactors env = new EnvironmentFactors
        {
            Temperature = Mathf.Clamp01(temp),
            Humidity = Mathf.Clamp01(humid),
            Precipitation = Mathf.Clamp01(precip),
            Solidity = Mathf.Clamp01(solidity),
            Hight = Mathf.Clamp01(hight)
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

        //更据biome.PreviewColor 实现Debug颜色的显示 OnDrawGizmos()
        ColorDicitionary[position] =  biome.PreviewColor;
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
    private static uint Xorshift32(ref uint state)
    {
        // 简单快速的伪随机数生成器
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    private void GenerateRandomResources(Vector2Int ChunkPosition, BiomeData biome, EnvironmentFactors env)
    {
        // 生成初始种子（唯一且确定性）
        uint state = (uint)(ChunkPosition.x * 114514 ^ ChunkPosition.y * 1919810);

        foreach (Biome_ItemSpawn spawn in biome.TerrainConfig.ItemSpawn)
        {
            if (spawn.environmentConditionRange.IsMatch(env))
            {
                // 获取 [0,1) 的伪随机数
                float chance = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;

                if (chance <= spawn.SpawnChance)
                {
                    float offsetX = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;
                    float offsetY = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;

                    Vector2 spawnPosition = new Vector2(
                        (ChunkPosition.x + (int)offsetX) - 0.5f,
                        (ChunkPosition.y + (int)offsetY) - 0.5f
                    );
                    ItemMgr.Instance.InstantiateItem(
                        spawn.itemName,
                        spawnPosition,
                        default,
                        default,
                        map.ParentObject
                    );
                }
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
        map.Data.TileLoaded = true;
    }
    #endregion
}
