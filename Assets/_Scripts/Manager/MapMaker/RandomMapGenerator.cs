using System;
using UnityEngine;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using Force.DeepCloner;
using UltEvents;

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

/*    [Header("噪声参数")]
    [Tooltip("噪声缩放系数：越小生成越大范围的高低起伏")]
    private float noiseScale = 0.01f;

    [Tooltip("陆地海洋比例：陆地占比，海洋占比")]
    private float landOceanRatio = 0.5f;

    [Tooltip("温度偏移")]
    private float temp = 0.0f;

    [Tooltip("地球半径")]
    private float plantRadius = 100;*/

    [Tooltip("赤道坐标")]
    public float Equator = 0;

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

    //Debug使用
    public Dictionary<Vector2Int,Color> ColorDicitionary  = new ();
    #endregion

    #region 内部变量
    /// <summary>
    /// 地图种子，字符串形式，从存档读取
    /// </summary>
    private int Seed => SaveAndLoad.Instance.SaveData.Seed;

    public float PlantRadius { get => SaveAndLoad.Instance.SaveData.Active_PlanetData.Radius;}
    public float Temp { get => SaveAndLoad.Instance.SaveData.Active_PlanetData.TemperatureOffset; }
    public float LandOceanRatio { get => SaveAndLoad.Instance.SaveData.Active_PlanetData.OceanHeight;}
    public float NoiseScale { get => SaveAndLoad.Instance.SaveData.Active_PlanetData.NoiseScale;  }

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
    private void Start()
    {
        rng = new System.Random(SaveAndLoad.Instance.SaveData.Seed);
      /*  if (map.Data.TileCount < SaveAndLoad.Instance.SaveData.MapSize.x * SaveAndLoad.Instance.SaveData.MapSize.y)
            GenerateRandomMap();*/
    }
    public void Awake()
    {
        map.GenerateMap += GenerateRandomMap;
    }
#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (map == null || map.Data == null) return;

        Vector2Int startPos = map.Data.position;
        Vector2Int size = SaveAndLoad.Instance.SaveData.MapSize;
        Vector3 center = new Vector3(startPos.x + size.x / 2f, startPos.y + size.y / 2f, 0f);
        Vector3 size3D = new Vector3(size.x, size.y, 0.1f);

        // 绘制地图边框（绿色描边）
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, size3D);


        //根据ColorDicitionary 显示颜色
        // 绘制颜色块（根据ColorDictionary）
/*
        foreach (var kvp in ColorDicitionary)
        {
            Vector2Int cellPos = kvp.Key;
            Color cellColor = kvp.Value;

            // 计算世界坐标（假设每个单元格为1x1单位）
            Vector3 worldPos = new Vector3(startPos.x + cellPos.x, startPos.y + cellPos.y, 0f);

            // 设置颜色并绘制立方体
            Gizmos.color = cellColor;
            Gizmos.DrawCube(worldPos, new Vector3(1f, 1f, 0.1f));
        }*/
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

      

        ClearMap();

        map.Data.position = SaveAndLoad.Instance.SaveData.Active_MapPos;

        Vector2Int startPos = map.Data.position;
        Vector2Int size = SaveAndLoad.Instance.SaveData.MapSize;

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
        solidity += LandOceanRatio;


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

    /// <summary>
    /// 生成随机资源 (原步骤4)
    /// </summary>
    private void GenerateRandomResources(Vector2Int position, BiomeData biome, EnvironmentFactors env)
    {
        foreach (Biome_ItemSpawn spawn in biome.TerrainConfig.ItemSpawn)
        {
            if(spawn.environmentConditionRange.IsMatch(env))
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
        Debug.Log($"[RandomMapGenerator] 地图完成 @ {map.Data.position} 大小{map.Data.size} 种子{Seed}");
        onMapGenerated?.Invoke();
    }
    #endregion
}
