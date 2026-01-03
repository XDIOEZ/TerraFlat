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
    #endregion

    #region Unity 生命周期
    public void Awake()
    {
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
    /// <summary>
    /// 生成随机地图的主要入口方法
    /// </summary>
    [Button("生成随机地图")]
    [Tooltip("根据当前配置生成随机地图")]
    public void GenerateRandomMap_TileData()
    {
        // === 前置验证 ===
        if (!ValidatePrerequisites())
            return;

        // === 初始化生成环境 ===
        InitializeGenerationEnvironment();

        // === 启动地图生成 ===
        StartMapGeneration();
    }

    /// <summary>
    /// 前置条件验证
    /// </summary>
    private bool ValidatePrerequisites()
    {
        // 检查地图引用
        if (map == null)
        {
            Debug.LogError("[地图生成] ❌ 地图引用未设置");
            return false;
        }

        // 检查生物群系配置
        if (biomes == null || biomes.Count == 0)
        {
            Debug.LogError("[地图生成] ❌ 未配置任何生物群系");
            return false;
        }

        // 检查噪声配置
        if (Noises == null || Noises.Count == 0)
        {
            Debug.LogError("[地图生成] ❌ 未配置任何噪声");
            return false;
        }

        // 检查必要的噪声类型
        if (!Noises.ContainsKey(NoiseType.River))
        {
            Debug.LogWarning("[地图生成] ⚠️ 未配置河流噪声，将使用默认值");
        }

        return true;
    }

    /// <summary>
    /// 初始化生成环境
    /// </summary>
    private void InitializeGenerationEnvironment()
    {
        // 清空旧数据
        ClearMap();

        // 设置地图位置（从父对象位置获取）
        map.Data.position = new Vector2Int(
            Mathf.RoundToInt(transform.parent.position.x),
            Mathf.RoundToInt(transform.parent.position.y)
        );

        // 初始化环境因子网格
        Vector2 size = ChunkSize;
        EnvFactorsGrid = new EnvironmentFactors[(int)size.x, (int)size.y];

        // 清空调试颜色字典
        ColorDicitionary.Clear();
    }

    /// <summary>
    /// 启动地图生成流程
    /// </summary>
    private void StartMapGeneration()
    {
        Vector2Int startPos = map.Data.position;
        Vector2 size = ChunkSize;

        if (tilesPerFrame > 0)
        {
            // 分帧生成（避免卡顿）
            Coroutine coroutine = ChunkMgr.Instance.StartCoroutine(GenerateMapCoroutine(startPos, size));
            ChunkMgr.Instance.RandomMapCoroutines.Add(coroutine);
        }
        else
        {
            // 立即生成
            GenerateAllTiles(startPos, size);
            OnGenerationComplete();
        }
    }
    #endregion

    #region 地图生成流程
    /// <summary>
    /// 协程方式生成地图，分帧处理以避免卡顿
    /// </summary>
    private IEnumerator GenerateMapCoroutine(Vector2Int startPos, Vector2 size)
    {
        int processed = 0;
        int totalTiles = (int)(size.x * size.y);

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Vector2Int worldPos = new Vector2Int(startPos.x + x, startPos.y + y);
                GenerateTileAtPosition(worldPos);
                processed++;

                // 分帧：每处理 tilesPerFrame 个地块让出一帧
                if (processed % tilesPerFrame == 0)
                {
                    yield return null;
                }
            }
        }

        OnGenerationComplete();
    }

    /// <summary>
    /// 立即生成所有地图瓦片（无分帧）
    /// </summary>
    private void GenerateAllTiles(Vector2Int startPos, Vector2 size)
    {
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Vector2Int worldPos = new Vector2Int(startPos.x + x, startPos.y + y);
                GenerateTileAtPosition(worldPos);
            }
        }
    }
    #endregion

    #region 地块生成逻辑
    /// <summary>
    /// 在指定位置生成单个地图瓦片（包含环境参数、地形、资源）
    /// </summary>
    private void GenerateTileAtPosition(Vector2Int worldPos)
    {
        try
        {
            // 1. 计算环境参数
            EnvironmentFactors env = CalculateEnvironmentFactors(worldPos);

            // 2. 存储环境参数到网格
            StoreEnvironmentFactors(worldPos, env);

            // 3. 生成地形瓦片
            BiomeData biome = GenerateBiomeTile(worldPos, env);
            if (biome == null)
                return;

            // 4. 生成资源物品
            GenerateResourcesForBiome(worldPos, biome, env);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[地块生成] ❌ 生成位置 {worldPos} 的地块失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 计算指定位置的环境参数
    /// </summary>
    private EnvironmentFactors CalculateEnvironmentFactors(Vector2Int worldPos)
    {
        float gx = worldPos.x * NoiseScale;
        float gy = worldPos.y * NoiseScale;

        // === 获取各项噪声值 ===
        float temp = SampleNoise(NoiseType.Temperature, gx, gy, 0.5f);
        float humid = SampleNoise(NoiseType.Humidity, gx, gy, 0.5f);
        float precip = SampleNoise(NoiseType.Precipitation, gx, gy, 0.5f);
        float solidity = SampleNoise(NoiseType.Solidity, gx, gy, 0.5f);
        float height = SampleNoise(NoiseType.Land, gx, gy, 0.5f);

        // === 河流处理（特殊环境参数调整） ===
        float waterValue = SampleNoise(NoiseType.River, gx, gy, 0.5f);
        if (waterValue > 0.5f)
        {
            solidity = Mathf.Clamp01(solidity - 0.8f);
            humid = Mathf.Clamp01(humid + 0.8f);
        }

        // === 创建环境因子对象 ===
        EnvironmentFactors env = new EnvironmentFactors
        {
            Temperature = Mathf.Clamp01(temp),
            Humidity = Mathf.Clamp01(humid),
            Precipitation = Mathf.Clamp01(precip),
            Solidity = Mathf.Clamp01(solidity),
            Hight = Mathf.Clamp01(height)
        };

        return env;
    }

    /// <summary>
    /// 采样噪声值（带默认值降级处理）
    /// </summary>
    private float SampleNoise(NoiseType noiseType, float x, float y, float defaultValue)
    {
        if (!Noises.ContainsKey(noiseType))
            return defaultValue;

        try
        {
            return Noises[noiseType].Sample(x, y, Seed);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[噪声采样] ⚠️ 采样 {noiseType} 失败: {ex.Message}，使用默认值 {defaultValue}");
            return defaultValue;
        }
    }

    /// <summary>
    /// 存储环境参数到网格
    /// </summary>
    private void StoreEnvironmentFactors(Vector2Int worldPos, EnvironmentFactors env)
    {
        if (EnvFactorsGrid == null)
        {
            Debug.LogError("[环境存储] ❌ EnvFactorsGrid 未初始化");
            return;
        }

        Vector2Int localPos = worldPos - map.Data.position;

        // 边界检查
        if (localPos.x < 0 || localPos.x >= EnvFactorsGrid.GetLength(0) ||
            localPos.y < 0 || localPos.y >= EnvFactorsGrid.GetLength(1))
        {
            return; // 超出范围，直接跳过
        }

        EnvFactorsGrid[localPos.x, localPos.y] = env;
    }

    /// <summary>
    /// 生成生物群系对应的地形瓦片
    /// </summary>
    private BiomeData GenerateBiomeTile(Vector2Int worldPos, EnvironmentFactors env)
    {
        // 1. 匹配生物群系
        BiomeData biome = FindMatchingBiome(env);
        if (biome == null)
        {
            Debug.LogWarning($"[生物群系] ⚠️ 位置 {worldPos} 无法匹配任何生物群系");
            return null;
        }

        // 2. 记录调试颜色
        ColorDicitionary[worldPos] = biome.PreviewColor;

        // 3. 生成地形瓦片
        GenerateTerrainTile(worldPos, biome, env);

        return biome;
    }

    /// <summary>
    /// 查找匹配的生物群系（带缓存优化）
    /// </summary>
    private BiomeData FindMatchingBiome(EnvironmentFactors env)
    {
        foreach (var biome in biomes)
        {
            if (biome.IsEnvironmentValid(env))
                return biome;
        }
        return null;
    }

    /// <summary>
    /// 生成地形瓦片数据并添加到地图
    /// </summary>
    private void GenerateTerrainTile(Vector2Int worldPos, BiomeData biome, EnvironmentFactors env)
    {
        if (biome == null || biome.TerrainConfig == null)
        {
            Debug.LogError($"[地形生成] ❌ 生物群系或其配置为空");
            return;
        }
        // 1. 获取 Tile_Block SO
        Tile_Block tileBlock = biome.TerrainConfig.GetTilePrefab(env);
        if (tileBlock == null)
        {
            Debug.LogWarning($"[地形生成] ⚠️ 无法获取 Tile_Block: {biome.BiomeName}");
            return;
        }

        // 2. 直接从 Tile_Block 的模板生成 TileData（无需额外缓存）
        TileData template = tileBlock.tileDataTemplate;
        if (template == null)
        {
            Debug.LogError($"[地形生成] ❌ Tile_Block 的 tileDataTemplate 为 null: {tileBlock.name}");
            return;
        }

        // 3. 克隆 TileData（使用手写 Clone，避免通用深拷贝开销）
        var tile = template.Clone();

        // 3.1 若 Tile_Block 提供了对应的 TileBase，则同步写入 TileData 的 ID，
        // 让后续 Map.UpdateTileBaseAtPosition 可以正确渲染 Tilemap
        var unityTileBase = tileBlock.GetTileBaseAsset();
        if (unityTileBase != null)
        {
            tile.ID = unityTileBase.name;
        }
        else if (string.IsNullOrEmpty(tile.ID))
        {
            Debug.LogError($"[地形生成] ❌ Tile_Block {tileBlock.name} 未提供 TileBase，且模板 ID 为空，无法渲染瓦片");
            return;
        }

        // 4. 初始化瓦片（根据环境因子调整）
        tile.Initialize_Env(env);

        // 5. 设置瓦片位置
        tile.position = new Vector3Int(worldPos.x, worldPos.y, 0);

        // 6. 添加到地图
        map.ADDTileData(worldPos, tile);
        map.UpdateTileBaseAtPosition(worldPos);
    }

    #endregion

    #region 资源生成逻辑
    /// <summary>
    /// 为指定生物群系生成资源物品（包含 SO 和非 SO 物品）
    /// </summary>
    private void GenerateResourcesForBiome(Vector2Int worldPos, BiomeData biome, EnvironmentFactors env)
    {
        if (biome == null || biome.TerrainConfig == null)
            return;

        // 初始化伪随机数生成器（使用坐标作为种子，确保同一位置生成结果一致）
        uint randomState = (uint)(worldPos.x * 114514 ^ worldPos.y * 1919810);
        Vector2 spawnCenterPos = new Vector2(worldPos.x + 0.5f, worldPos.y + 0.5f);

        // === 生成配置中的 SO 物品 ===
        if (biome.TerrainConfig.ItemSpawn_NoSO != null)
        {
            foreach (Biome_ItemSpawn_NoSO spawn in biome.TerrainConfig.ItemSpawn_NoSO)
            {
                TrySpawnItem(spawn.itemName, spawn.SpawnChance, spawn.environmentConditionRange,
                            spawnCenterPos, ref randomState, env, biome.BiomeName);
            }
        }

        // === 生成非 SO 物品 ===
        if (biome.TerrainConfig.ItemSpawn_NoSO != null)
        {
            foreach (Biome_ItemSpawn_NoSO spawn in biome.TerrainConfig.ItemSpawn_NoSO)
            {
                TrySpawnItem(spawn.itemName, spawn.SpawnChance, spawn.environmentConditionRange,
                            spawnCenterPos, ref randomState, env, biome.BiomeName);
            }
        }
    }

    /// <summary>
    /// 尝试生成单个物品
    /// </summary>
    private void TrySpawnItem(string itemName, float spawnChance, EnvironmentConditionRange envCondition,
                               Vector2 spawnPos, ref uint randomState, EnvironmentFactors env, string biomeName)
    {
        // 1. 环境条件检查
        if (!envCondition.IsMatch(env))
            return;

        // 2. 概率检查
        float randomValue = (Xorshift32(ref randomState) & 0xFFFFFF) / (float)0x1000000;
        if (randomValue > spawnChance)
            return;

        // 3. 实例化物品
        try
        {
            Item spawnedItem = ItemMgr.Instance.InstantiateItem(
                itemName,
                spawnPos,
                default,
                default,
                map.ParentObject
            );

            if (spawnedItem == null)
            {
                Debug.LogWarning($"[资源生成] ⚠️ 无法实例化物品: {itemName} (群系: {biomeName})");
                return;
            }

            // 4. 初始化物品
            spawnedItem.Load();

            // 5. 添加到区块
            if (map.chunk != null)
            {
                map.chunk.AddItem(spawnedItem);
            }
            else
            {
                Debug.LogWarning($"[资源生成] ⚠️ 无法添加物品到区块: 区块为 null");
            }

            spawnedItem.Initialize_Env(env);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[资源生成] ❌ 生成物品异常 {itemName}: {ex.Message}");
        }
    }
    #endregion

    #region 工具方法

    /// <summary>
    /// 清除当前地图的所有瓦片和数据
    /// </summary>
    private void ClearMap()
    {
        if (map == null)
            return;

        if (map.tileMap != null)
            map.tileMap.ClearAllTiles();

        if (map.Data != null && map.Data.TileData != null)
            map.Data.TileData.Clear();
    }

    /// <summary>
    /// 地图生成完成后的回调方法
    /// </summary>
    private void OnGenerationComplete()
    {
        try
        {
            // 1. 刷新所有瓦片视觉
            if (map?.tileMap != null)
                map.tileMap.RefreshAllTiles();

            // 2. 标记数据加载完成
            if (map?.Data != null)
                map.Data.TileLoaded = true;

            // 3. 异步烘焙权重（导航网格）
            map?.BackTilePenalty_Async();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[地图生成] ❌ 完成回调异常: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Xorshift32 伪随机数生成器
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
        // === 前置验证 ===
        if (!ValidateDetectionPrerequisites())
            return;

        // === 获取鼠标坐标 ===
        Vector2Int gridPos = GetMouseGridPosition();

        // === 检查瓦片有效性 ===
        if (!targetTilemap.HasTile(new Vector3Int(gridPos.x, gridPos.y, 0)))
        {
            Debug.LogWarning($"[鼠标检测] ⚠️ 鼠标位置无有效Tile");
            return;
        }

        // === 获取环境参数 ===
        if (!TryGetEnvironmentFactorsAt(gridPos, out EnvironmentFactors env))
            return;

        // === 查找生物群系 ===
        string biomeName = FindBiomeNameForEnvironment(env);

        // === 输出调试信息 ===
        PrintEnvironmentDebugInfo(gridPos, env, biomeName);
    }

    /// <summary>
    /// 验证检测的前置条件
    /// </summary>
    private bool ValidateDetectionPrerequisites()
    {
        if (mapGrid == null)
        {
            Debug.LogError("[鼠标检测] ❌ 缺少Grid组件");
            return false;
        }

        if (targetTilemap == null)
        {
            Debug.LogError("[鼠标检测] ❌ 缺少Tilemap组件");
            return false;
        }

        if (Camera.main == null)
        {
            Debug.LogError("[鼠标检测] ❌ 未找到MainCamera");
            return false;
        }

        if (map == null || map.Data == null)
        {
            Debug.LogError("[鼠标检测] ❌ 地图数据未初始化");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 获取鼠标所在的网格位置
    /// </summary>
    private Vector2Int GetMouseGridPosition()
    {
        Vector3 mouseScreenPos = Input.mousePosition;
        mouseScreenPos.z = Mathf.Abs(Camera.main.transform.position.z - targetTilemap.transform.position.z);

        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(mouseScreenPos);
        mouseWorldPos.z = 0; // 强制在Tilemap平面

        Vector3Int cellPos = mapGrid.WorldToCell(mouseWorldPos);
        return new Vector2Int(cellPos.x, cellPos.y);
    }

    /// <summary>
    /// 尝试获取指定位置的环境参数
    /// </summary>
    private bool TryGetEnvironmentFactorsAt(Vector2Int gridPos, out EnvironmentFactors env)
    {
        env = default;
        
        Vector2Int localGridPos = gridPos - map.Data.position;

        // 检查是否在有效范围内
        if (EnvFactorsGrid == null ||
            localGridPos.x < 0 || localGridPos.x >= EnvFactorsGrid.GetLength(0) ||
            localGridPos.y < 0 || localGridPos.y >= EnvFactorsGrid.GetLength(1))
        {
            Debug.LogWarning($"[鼠标检测] ⚠️ 位置 ({gridPos.x}, {gridPos.y}) 不在地图数据范围内");
            return false;
        }

        env = EnvFactorsGrid[localGridPos.x, localGridPos.y];
        return true;
    }

    /// <summary>
    /// 根据环境参数查找对应的生物群系名称
    /// </summary>
    private string FindBiomeNameForEnvironment(EnvironmentFactors env)
    {
        foreach (var biome in biomes)
        {
            if (biome.IsEnvironmentValid(env))
                return biome.BiomeName;
        }
        return "未知";
    }

    /// <summary>
    /// 打印环境参数调试信息
    /// </summary>
    private void PrintEnvironmentDebugInfo(Vector2Int gridPos, EnvironmentFactors env, string biomeName)
    {
        string debugInfo = $"=== 鼠标Tile环境参数 ===\n" +
                          $"格子坐标：({gridPos.x}, {gridPos.y})\n" +
                          $"生物群系：{biomeName}\n" +
                          $"温度：{env.Temperature:F2} | 湿度：{env.Humidity:F2}\n" +
                          $"降水量：{env.Precipitation:F2} | 坚固度：{env.Solidity:F2}\n" +
                          $"高度：{env.Hight:F2}";

        if (map != null)
        {
            var tileData = map.GetTile(gridPos);
            if (tileData != null)
            {
                // TileData 可能有 ID 或其他标识符，使用 ToString() 作为后备
                debugInfo += $"\n瓦片数据：{tileData.ToString()}";
            }
        }

        Debug.Log(debugInfo);
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