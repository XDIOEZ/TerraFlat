using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using Sirenix.OdinInspector;
using Force.DeepCloner;
using AYellowpaper.SerializedCollections;

/// <summary>
/// 随机地图生成器：
/// - 基于噪声 + 生物群系（Biome）
/// - 支持分帧生成 / 大地图无缝衔接 / 群系资源随机生成
/// - 记录每个格子的环境因子 (EnvFactorsGrid)
/// - 支持 Gizmos 可视化调试
/// - 支持按键获取Tile环境参数（默认F3）
/// </summary>
public class RandomMapGenerator : MonoBehaviour
{
    #region 配置参数
    [Header("地图配置")]
    [Required] public Map map; // 地图管理对象
    [Tooltip("（可选）手动指定Grid组件，未指定则自动从当前对象/子对象获取")]
    public Grid mapGrid;
    [Tooltip("（可选）手动指定Tilemap组件，未指定则自动从当前对象的子对象获取")]
    public Tilemap targetTilemap;

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

    // Debug 调试用颜色字典
    public Dictionary<Vector2Int, Color> ColorDicitionary = new();

    [Header("应用噪声")]
    [Tooltip("噪声配置字典，可在Inspector中设置不同类型的噪声SO引用")]
    public NoiseDictionary Noises = new NoiseDictionary();

    [Header("鼠标检测设置")]
    [Tooltip("触发环境参数检测的按键（默认F3）")]
    public KeyCode detectKey = KeyCode.F3;
    #endregion

    #region 内部变量
    private int Seed => SaveDataMgr.Instance.SaveData.Seed;

    public float NoiseScale => (plantData != null) ? plantData.NoiseScale : 0.01f;

    public EnvironmentFactors[,] EnvFactorsGrid { get => map.Data.EnvironmentData; set => map.Data.EnvironmentData = value; }

    public static System.Random rng; // 系统级随机实例

    private Dictionary<string, TileData> tileDataCache = new(); // TileData 缓存
    #endregion

    #region Unity 生命周期
    public void Awake()
    {
        rng = new System.Random(Seed);
        map.OnMapGenerated_Start += GenerateRandomMap_TileData;

        // 1. 自动获取Grid组件（优先级：手动指定 > 当前对象 > 子对象）
        if (mapGrid == null)
        {
            mapGrid = GetComponent<Grid>();
            if (mapGrid == null)
            {
                mapGrid = map.GetComponentInChildren<Grid>(includeInactive: false);
            }
        }

        // 2. 自动获取当前对象的子对象Tilemap
        if (targetTilemap == null)
        {
            Tilemap[] childTilemaps = map.GetComponentsInChildren<Tilemap>(includeInactive: false);
            if (childTilemaps != null && childTilemaps.Length > 0)
            {
                targetTilemap = childTilemaps[0];
                Debug.Log($"[RandomMapGenerator] 自动获取子对象Tilemap：{targetTilemap.name}");
            }
            else
            {
                Debug.LogError($"[RandomMapGenerator] 当前对象下未找到任何Tilemap子对象！");
            }
        }
    }

    private void Update()
    {
        // 检测按键触发环境参数检测
        if (Input.GetKeyDown(detectKey))
        {
            GetEnvFactorsAtMousePosition();
        }
    }
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

        // 初始化环境因子二维数组
        EnvFactorsGrid = new EnvironmentFactors[(int)size.x, (int)size.y];

        if (tilesPerFrame > 0)
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

    #region 地图生成流程
    private IEnumerator GenerateMapCoroutine(Vector2Int startPos, Vector2 size)
    {
        int processed = 0;
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                GenerateTileAtPosition(new Vector2Int(startPos.x + x, startPos.y + y));
                processed++;

                if (processed % tilesPerFrame == 0)
                    yield return null;
            }
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
        float gx = position.x * NoiseScale;
        float gy = position.y * NoiseScale;

        // 从噪声配置中获取各项环境参数
        float temp = Noises.ContainsKey(NoiseType.Temperature) ? Noises[NoiseType.Temperature].Sample(gx, gy, Seed) : 0.5f;
        float humid = Noises.ContainsKey(NoiseType.Humidity) ? Noises[NoiseType.Humidity].Sample(gx, gy, Seed) : 0.5f;
        float precip = Noises.ContainsKey(NoiseType.Precipitation) ? Noises[NoiseType.Precipitation].Sample(gx, gy, Seed) : 0.5f;
        float solidity = Noises.ContainsKey(NoiseType.Solidity) ? Noises[NoiseType.Solidity].Sample(gx, gy, Seed) : 0.5f;
        float hight = Noises.ContainsKey(NoiseType.Land) ? Noises[NoiseType.Land].Sample(gx, gy, Seed) : 0.5f;

        // 河流噪声处理
        float Water = Noises[NoiseType.River].Sample(gx, gy, Seed);
        if (Water > 0.5f)
        {
            // 生成河流时调整环境参数
            solidity -= 0.8f;
            humid += 0.8f;
        }

        // 构建环境因子对象并限制在0-1范围内
        EnvironmentFactors env = new EnvironmentFactors
        {
            Temperature = Mathf.Clamp01(temp),
            Humidity = Mathf.Clamp01(humid),
            Precipitation = Mathf.Clamp01(precip),
            Solidity = Mathf.Clamp01(solidity),
            Hight = Mathf.Clamp01(hight)
        };

        // 计算本地坐标并存储环境因子
        Vector2Int localPos = position - map.Data.position;
        if (localPos.x >= 0 && localPos.x < EnvFactorsGrid.GetLength(0) &&
            localPos.y >= 0 && localPos.y < EnvFactorsGrid.GetLength(1))
        {
            EnvFactorsGrid[localPos.x, localPos.y] = env;
        }

        // 匹配生物群系并生成地块
        BiomeData biome = MatchAndGenerateBiomeTile(position, env);

        if (biome != null)
        {
            GenerateResourcesForBiome(position, biome, env);
        }
    }

    private BiomeData MatchAndGenerateBiomeTile(Vector2Int position, EnvironmentFactors env)
    {
        BiomeData biome = null;
        foreach (var b in biomes)
        {
            if (b.IsEnvironmentValid(env))
            {
                biome = b;
                break;
            }
        }
        if (biome == null) return null;

        // 记录调试颜色并生成地形Tile
        ColorDicitionary[position] = biome.PreviewColor;
        GenerateTerrainTile(position, biome, env);

        return biome;
    }

    private void GenerateResourcesForBiome(Vector2Int pos, BiomeData biome, EnvironmentFactors env)
    {
        uint state = (uint)(pos.x * 114514 ^ pos.y * 1919810);

        foreach (Biome_ItemSpawn spawn in biome.TerrainConfig.ItemSpawn)
        {
            if (!spawn.environmentConditionRange.IsMatch(env)) continue;

            // 随机判定是否生成资源
            float chance = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;
            if (chance > spawn.SpawnChance) continue;

            Vector2 spawnPos = new Vector2(pos.x  + 0.5f, pos.y  + 0.5f);

            // 实例化资源物品
            ItemMgr.Instance.InstantiateItem(
                spawn.itemName,
                spawnPos,
                default,
                default,
                map.ParentObject
            ).Load();
        }
        foreach (Biome_ItemSpawn_NoSO spawn in biome.TerrainConfig.ItemSpawn_NoSO)
        {
            if (!spawn.environmentConditionRange.IsMatch(env)) continue;

            // 随机判定是否生成资源
            float chance = (Xorshift32(ref state) & 0xFFFFFF) / (float)0x1000000;
            if (chance > spawn.SpawnChance) continue;

            Vector2 spawnPos = new Vector2(pos.x + 0.5f, pos.y + 0.5f);

            // 实例化资源物品
            ItemMgr.Instance.InstantiateItem(
                spawn.itemName,
                spawnPos,
                default,
                default,
                map.ParentObject
            ).Load();
        }
    }

    private void GenerateTerrainTile(Vector2Int position, BiomeData biome, EnvironmentFactors env)
    {
        string key = biome.TerrainConfig.GetTilePrefab(env);

        // 缓存TileData避免重复加载
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
                Debug.LogError($"预制体 {key} 缺少 IBlockTile 组件，群系: {biome.BiomeName}");
                return;
            }

            tileDataCache[key] = blockTile.TileData;
        }

        // 克隆并添加Tile到地图
        var tile = tileDataCache[key].DeepClone();
        tile.Initialize(env);
        tile.position = new Vector3Int(position.x, position.y, 0);
        map.ADDTile(position, tile);
    }
    #endregion

    #region 边界处理
    public void GenerateMapEdges()
    {
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var d in dirs)
        {
            Item edge = ItemMgr.Instance.InstantiateItem("MapEdge", default, default, default, map.ParentObject);
            if (edge is WorldEdge we)
                we.SetupMapEdge(d, map.Data.position);
            else
                Debug.LogError("[RandomMapGenerator] 边界对象类型错误，应为WorldEdge");
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
        map.BackTilePenalty_Sync(); //地图生成完毕后直接烘焙 因为生成的时候自动调用的SetTile的绘制层
        Debug.Log("[RandomMapGenerator] 地图生成完成");
    }

    /// <summary>
    /// Xorshift32随机数生成器
    /// </summary>
    private static uint Xorshift32(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
    #endregion

    #region 鼠标位置环境参数检测
    /// <summary>
    /// 获取鼠标位置下的环境参数并打印到Debug窗口
    /// </summary>
    private void GetEnvFactorsAtMousePosition()
    {
        // 前置检查：必要组件是否齐全
        if (mapGrid == null)
        {
            Debug.LogError("[RandomMapGenerator] 缺少Grid组件，无法转换Tile坐标");
            return;
        }
        if (targetTilemap == null)
        {
            Debug.LogError("[RandomMapGenerator] 缺少Tilemap组件，无法检测Tile");
            return;
        }

        // 1. 鼠标屏幕坐标 → 世界坐标
        Vector3 mouseScreenPos = Input.mousePosition;
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("[RandomMapGenerator] 未找到MainCamera");
            return;
        }
        
        mouseScreenPos.z = Mathf.Abs(mainCamera.transform.position.z - targetTilemap.transform.position.z);
        Vector3 mouseWorldPos = mainCamera.ScreenToWorldPoint(mouseScreenPos);
        mouseWorldPos.z = 0; // 强制在Tilemap平面

        // 2. 世界坐标 → Tilemap格子坐标
        Vector3Int cellPos = mapGrid.WorldToCell(mouseWorldPos);
        Vector2Int gridPos = new Vector2Int(cellPos.x, cellPos.y);

        // 3. 检查该格子是否存在Tile
        if (!targetTilemap.HasTile(cellPos))
        {
            Debug.LogWarning($"[鼠标检测] 格子({gridPos.x}, {gridPos.y}) 无Tile，跳过检测");
            return;
        }

        // 4. 计算本地坐标
        Vector2Int localGridPos = gridPos - map.Data.position;

        // 5. 检测是否在有效范围内
        if (EnvFactorsGrid == null ||
            localGridPos.x < 0 || localGridPos.x >= EnvFactorsGrid.GetLength(0) ||
            localGridPos.y < 0 || localGridPos.y >= EnvFactorsGrid.GetLength(1))
        {
            Debug.LogWarning($"[鼠标检测] 格子({gridPos.x}, {gridPos.y}) 不在当前地图数据范围内");
            return;
        }

        // 6. 获取环境参数并查找生物群系
        EnvironmentFactors env = EnvFactorsGrid[localGridPos.x, localGridPos.y];
        string biomeName = "未知";
        foreach (var biome in biomes)
        {
            if (biome.IsEnvironmentValid(env))
            {
                biomeName = biome.BiomeName;
                break;
            }
        }

        // 7. 打印Debug信息
        Debug.Log($"=== 鼠标Tile环境参数 ===\n" +
                  $"格子坐标：({gridPos.x}, {gridPos.y})\n" +
                  $"生物群系：{biomeName}\n" +
                  $"温度：{env.Temperature:F2} | 湿度：{env.Humidity:F2}\n" +
                  $"降水量：{env.Precipitation:F2} | 坚固度：{env.Solidity:F2}\n" +
                  $"高度：{env.Hight:F2}\n" +
                  $"瓦片数据：{map.GetTile(gridPos)}");
    }
    #endregion
}

[Serializable]
public enum NoiseType
{
    Land,
    Humidity,
    Precipitation,
    Temperature,
    River,
    Solidity
}

[Serializable]
public class NoiseDictionary : SerializedDictionary<NoiseType, BaseNoise> { }