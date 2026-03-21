using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Tilemaps;
using Sirenix.OdinInspector;

/// <summary>
/// 随机地图生成器：
/// - 基于噪声 + 生物群系（Biome）
/// - 支持分帧生成 / 大地图无缝衔接 / 群系资源随机生成
/// - 记录每个格子的环境因子 (EnvFactorsGrid)
/// - 支持 Gizmos 可视化调试
/// - 支持按键获取Tile环境参数（默认F3）
/// </summary>
[System.Serializable]
public class ChunkGenerator_Land : ChunkGeneratorBase
{
    #region 配置参数
    [Header("世界配置")]
    [Tooltip("星球/世界配置数据（可选，不填则使用默认噪声缩放值）")]
    public PlanetData plantData;

    [Header("地图配置")]
    [Tooltip("（可选）手动指定Grid组件，未指定则自动从当前对象/子对象获取")]
    public Grid mapGrid;
    [Tooltip("（可选）手动指定Tilemap组件，未指定则自动从当前对象的子对象获取")]
    public Tilemap targetTilemap;
    [Tooltip("赤道坐标")] public float Equator = 0;

    [Header("生物群系 & 噪声")]
    [Tooltip("不同温度/湿度对应的生物群系配置")]
    public List<BiomeData> biomes;

    [Tooltip("噪声配置列表：直接配置 BaseNoise（SerializeReference 多态）。\n通过 BaseNoise.noiseType 匹配采样类型。")]
    [SerializeReference]
    public List<BaseNoise> NoiseConfigs = new List<BaseNoise>();

    [Header("调试设置")]
    [Tooltip("是否在屏幕上用颜色块可视化各个格子的生物群系分布")]
    public bool showBiomeOverlay = false;

    [Header("性能设置")]
    [Tooltip("是否将地形采样与群系匹配放到后台线程计算")]
    public bool enableBackgroundGeneration = true;

    [Tooltip("主线程每帧提交到地图的数据量（越大越快但越容易卡顿）")]
    [Min(64)]
    public int applyTilesPerFrame = 512;
    #endregion

    #region 只读属性
    public Vector2 ChunkSize => ChunkMgr.GetChunkSize();
    #endregion

    #region 内部变量
    private int Seed => SaveDataMgr.Instance.SaveData.Seed;

    public float NoiseScale => plantData != null ? plantData.NoiseScale : 0.01f;

    [NonSerialized]
    private bool _hasLoggedNoiseConfigsNull;

    [NonSerialized]
    private bool _hasLoggedNoiseConfigsEmpty;

    [NonSerialized]
    private bool _hasLoggedMissingLandNoise;

    private struct BiomeConditionSnapshot
    {
        public int biomeIndex;
        public float tMin;
        public float tMax;
        public float hMin;
        public float hMax;
        public float pMin;
        public float pMax;
        public float sMin;
        public float sMax;
        public float htMin;
        public float htMax;
    }

    private sealed class LandComputeResult
    {
        public int width;
        public int height;
        public Vector2Int startPos;
        public EnvironmentFactors[] envArray;
        public int[] biomeIndexArray;
    }

    #endregion

    #region Unity 生命周期
    public override void Init(Map map)
    {
        base.Init(map);
        Map = map;

        // 1. 自动获取Grid组件（优先级：手动指定 > 当前对象 > 子对象）
        if (mapGrid == null)
        {
            mapGrid = map.GetComponent<Grid>();
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

    #endregion

    #region 管线入口
    public override void Generate(MapGenerationContext context)
    {
        if (context == null)
        {
            LogNullContext(nameof(ChunkGenerator_Land));
            return;
        }

        if (context.Map == null)
        {
            LogNullMap(nameof(ChunkGenerator_Land));
            return;
        }

        GenerateRandomMap_TileData(context.Map, context.PlanetData);
    }

    public bool EnableBackgroundGeneration => enableBackgroundGeneration;

    public IEnumerator GenerateAsyncCoroutine(MapGenerationContext context)
    {
        if (context == null)
        {
            LogNullContext(nameof(ChunkGenerator_Land));
            yield break;
        }

        if (context.Map == null)
        {
            LogNullMap(nameof(ChunkGenerator_Land));
            yield break;
        }

        if (!enableBackgroundGeneration)
        {
            GenerateRandomMap_TileData(context.Map, context.PlanetData);
            yield break;
        }

        yield return GenerateRandomMap_TileData_Async(context.Map, context.PlanetData);
    }
    #endregion

    #region 主逻辑
    /// <summary>
    /// 生成随机地图的主要入口方法
    /// </summary>
    [Button("生成随机地图")]
    [Tooltip("根据当前配置生成随机地图")]
    public void GenerateRandomMap_TileData(Map map, PlanetData planetData)
    {
        Map = map;

        // 以参数为准：允许外部显式传入 PlanetData
        // 若未传入，则退回到存档当前星球，再退回到 Inspector 字段
        plantData = planetData ?? SaveDataMgr.Instance.GetCurrentPlanetData() ?? plantData;

        // === 初始化生成环境 ===
        InitializeGenerationEnvironment(map);

        // === 启动地图生成 ===
        StartMapGeneration(map);
    }

    private IEnumerator GenerateRandomMap_TileData_Async(Map map, PlanetData planetData)
    {
        Map = map;

        plantData = planetData ?? SaveDataMgr.Instance.GetCurrentPlanetData() ?? plantData;

        InitializeGenerationEnvironment(map);

        yield return StartMapGenerationAsync(map);
    }

    /// <summary>
    /// 初始化生成环境
    /// </summary>
    private void InitializeGenerationEnvironment(Map map)
    {
        // 清空旧数据
        ClearMap(map);

        // 设置地图位置（从父对象位置获取）
        map.Data.position = new Vector2Int(
            Mathf.RoundToInt(map.transform.parent.position.x),
            Mathf.RoundToInt(map.transform.parent.position.y)
        );

        // 初始化环境因子网格
        Vector2 size = ChunkSize;
        map.Data.EnvironmentData = new EnvironmentFactors[(int)size.x, (int)size.y];
    }

    /// <summary>
    /// 启动地图生成流程
    /// </summary>
    private void StartMapGeneration(Map map)
    {
        Vector2Int startPos = map.Data.position;
        Vector2 size = ChunkSize;

        // 先按同步方式生成（协程分帧后续再做优化）
        GenerateAllTiles(map, startPos, size);
    }

    private IEnumerator StartMapGenerationAsync(Map map)
    {
        Vector2Int startPos = map.Data.position;
        Vector2 size = ChunkSize;

        int width = (int)size.x;
        int height = (int)size.y;
        if (width <= 0 || height <= 0)
        {
            Debug.LogError($"[ChunkGenerator_Land] ❌ ChunkSize 非法: {width}x{height}");
            yield break;
        }

        var biomeSnapshots = BuildBiomeSnapshots();
        int seed = Seed;
        float noiseScale = NoiseScale;
        var localNoiseConfigs = NoiseConfigs != null ? new List<BaseNoise>(NoiseConfigs) : null;

        Task<LandComputeResult> computeTask = Task.Run(() =>
            ComputeLandData(startPos, width, height, noiseScale, seed, localNoiseConfigs, biomeSnapshots));

        while (!computeTask.IsCompleted)
        {
            yield return null;
        }

        if (computeTask.IsFaulted)
        {
            Debug.LogError($"[ChunkGenerator_Land] ❌ 后台地形计算失败，回退主线程生成: {computeTask.Exception}");
            GenerateAllTiles(map, startPos, size);
            yield break;
        }

        LandComputeResult result = computeTask.Result;
        if (result == null || result.envArray == null || result.biomeIndexArray == null)
        {
            Debug.LogError("[ChunkGenerator_Land] ❌ 后台地形计算结果无效");
            yield break;
        }

        int total = width * height;
        int batchSize = Mathf.Max(64, applyTilesPerFrame);

        for (int i = 0; i < total; i++)
        {
            int x = i % width;
            int y = i / width;
            Vector2Int worldPos = new Vector2Int(result.startPos.x + x, result.startPos.y + y);

            EnvironmentFactors env = result.envArray[i];
            map.Data.EnvironmentData[x, y] = env;

            int biomeIndex = result.biomeIndexArray[i];
            if ((uint)biomeIndex < (uint)biomes.Count)
            {
                BiomeData biome = biomes[biomeIndex];
                if (biome != null)
                {
                    GenerateTerrainTile(map, worldPos, biome, env);
                }
            }

            if ((i + 1) % batchSize == 0)
            {
                yield return null;
            }
        }
    }
    #endregion

    #region 地图生成流程
    /// <summary>
    /// 立即生成所有地图瓦片（无分帧）
    /// </summary>
    private void GenerateAllTiles(Map map, Vector2Int startPos, Vector2 size)
    {
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Vector2Int worldPos = new Vector2Int(startPos.x + x, startPos.y + y);
                // 1. 计算环境参数
                EnvironmentFactors env = CalculateEnvironmentFactors(worldPos);
                // 2. 写入环境因子网格（数据层）
                StoreEnvironmentFactors(map, worldPos, env);
                // 3. 写入地形 TileData（数据层），不直接操作 Tilemap
                BiomeData biome = GenerateBiomeTile(map, worldPos, env);
                // 2. 生成地形瓦片
                GenerateTerrainTile(map, worldPos, biome, env);
            }
        }
        // 注意：收尾（TileLoaded/烘焙/刷新）由 Map.GenerateByPipeline() 统一处理，
        // 以保证后续生成器（例如河流）可以基于本次大陆生成结果继续加工。
    }
    #endregion

    #region 地块生成逻辑

    /// <summary>
    /// 计算指定位置的环境参数
    /// </summary>
    private EnvironmentFactors CalculateEnvironmentFactors(Vector2Int worldPos)
    {
        float gx = worldPos.x * NoiseScale;
        float gy = worldPos.y * NoiseScale;
        EnvironmentFactors factors = SampleEnvironmentFactors(gx, gy);
        return factors;
    }

    /// <summary>
    /// 采样环境因子（一次遍历 NoiseConfigs，每个 noise 执行一次 Sample）
    /// </summary>
    private EnvironmentFactors SampleEnvironmentFactors(float x, float y)
    {
        // 默认值：当某个类型未配置噪声时使用
        const float defaultValue = 0.5f;

        if (NoiseConfigs == null)
        {
            if (!_hasLoggedNoiseConfigsNull)
            {
                _hasLoggedNoiseConfigsNull = true;
                Debug.LogError("[ChunkGenerator_Land] ❌ NoiseConfigs 为 null，无法采样环境因子（将使用默认值 Env=0.5）。");
            }
            return new EnvironmentFactors
            {
                Temperature = defaultValue,
                Humidity = defaultValue,
                Precipitation = defaultValue,
                Solidity = defaultValue,
                Hight = defaultValue
            };
        }

        if (NoiseConfigs.Count == 0)
        {
            if (!_hasLoggedNoiseConfigsEmpty)
            {
                _hasLoggedNoiseConfigsEmpty = true;
                Debug.LogWarning("[ChunkGenerator_Land] ⚠️ NoiseConfigs 为空，无法采样环境因子（将使用默认值 Env=0.5）。");
            }
            return new EnvironmentFactors
            {
                Temperature = defaultValue,
                Humidity = defaultValue,
                Precipitation = defaultValue,
                Solidity = defaultValue,
                Hight = defaultValue
            };
        }

        float sumTemperature = 0f;
        float sumHumidity = 0f;
        float sumPrecipitation = 0f;
        float sumSolidity = 0f;
        float sumHeight = 0f;

        int countTemperature = 0;
        int countHumidity = 0;
        int countPrecipitation = 0;
        int countSolidity = 0;
        int countHeight = 0;

        int seed = Seed;

        for (int i = 0; i < NoiseConfigs.Count; i++)
        {
            var noise = NoiseConfigs[i];
            if (noise == null)
                continue;

            float v = noise.Sample(x, y, seed);

            switch (noise.noiseType)
            {
                case NoiseType.Temperature:
                    sumTemperature += v;
                    countTemperature++;
                    break;
                case NoiseType.Humidity:
                    sumHumidity += v;
                    countHumidity++;
                    break;
                case NoiseType.Precipitation:
                    sumPrecipitation += v;
                    countPrecipitation++;
                    break;
                case NoiseType.Solidity:
                    sumSolidity += v;
                    countSolidity++;
                    break;
                case NoiseType.Land:
                    sumHeight += v;
                    countHeight++;
                    break;
                case NoiseType.River:
                    // 当前版本暂不启用河流：保留枚举兼容，但不影响 Env
                    break;
                default:
                    break;
            }
        }

        float temperature = countTemperature > 0 ? (sumTemperature / countTemperature) : defaultValue;
        float humidity = countHumidity > 0 ? (sumHumidity / countHumidity) : defaultValue;
        float precipitation = countPrecipitation > 0 ? (sumPrecipitation / countPrecipitation) : defaultValue;
        float solidity = countSolidity > 0 ? (sumSolidity / countSolidity) : defaultValue;
        float height = countHeight > 0 ? (sumHeight / countHeight) : defaultValue;

        if (countHeight == 0 && !_hasLoggedMissingLandNoise)
        {
            _hasLoggedMissingLandNoise = true;
            Debug.LogWarning("[ChunkGenerator_Land] ⚠️ 未配置任何 NoiseType.Land 噪声，高度将使用默认值 0.5。");
        }

        return new EnvironmentFactors
        {
            Temperature = Mathf.Clamp01(temperature),
            Humidity = Mathf.Clamp01(humidity),
            Precipitation = Mathf.Clamp01(precipitation),
            Solidity = Mathf.Clamp01(solidity),
            Hight = Mathf.Clamp01(height)
        };
    }



    /// <summary>
    /// 存储环境参数到网格
    /// </summary>
    private void StoreEnvironmentFactors(Map map, Vector2Int worldPos, EnvironmentFactors env)
    {
        if (map == null || map.Data == null || map.Data.EnvironmentData == null)
        {
            Debug.LogError("[环境存储] ❌ EnvFactorsGrid 未初始化");
            return;
        }
        Vector2Int localPos = worldPos - map.Data.position;

        // 边界检查
        if (localPos.x < 0 || localPos.x >= map.Data.EnvironmentData.GetLength(0) ||
            localPos.y < 0 || localPos.y >= map.Data.EnvironmentData.GetLength(1))
        {
            return; // 超出范围，直接跳过
        }

        map.Data.EnvironmentData[localPos.x, localPos.y] = env;
    }

    /// <summary>
    /// 生成生物群系对应的地形瓦片
    /// </summary>
    private BiomeData GenerateBiomeTile(Map map, Vector2Int worldPos, EnvironmentFactors env)
    {
        // 1. 匹配生物群系
        BiomeData biome = FindMatchingBiome(env);
        if (biome == null)
        {
            Debug.LogWarning($"[生物群系] ⚠️ 位置 {worldPos} 无法匹配任何生物群系");
            return null;
        }

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

    private BiomeConditionSnapshot[] BuildBiomeSnapshots()
    {
        if (biomes == null || biomes.Count == 0)
            return Array.Empty<BiomeConditionSnapshot>();

        var snapshots = new List<BiomeConditionSnapshot>(biomes.Count);
        for (int i = 0; i < biomes.Count; i++)
        {
            var biome = biomes[i];
            if (biome == null || biome.Condition == null)
                continue;

            var cond = biome.Condition;
            snapshots.Add(new BiomeConditionSnapshot
            {
                biomeIndex = i,
                tMin = cond.TemperatureRange.x,
                tMax = cond.TemperatureRange.y,
                hMin = cond.HumidityRange.x,
                hMax = cond.HumidityRange.y,
                pMin = cond.PrecipitationRange.x,
                pMax = cond.PrecipitationRange.y,
                sMin = cond.SolidityRange.x,
                sMax = cond.SolidityRange.y,
                htMin = cond.HightRange.x,
                htMax = cond.HightRange.y
            });
        }

        return snapshots.ToArray();
    }

    private static LandComputeResult ComputeLandData(
        Vector2Int startPos,
        int width,
        int height,
        float noiseScale,
        int seed,
        List<BaseNoise> noiseConfigs,
        BiomeConditionSnapshot[] biomeSnapshots)
    {
        var result = new LandComputeResult
        {
            width = width,
            height = height,
            startPos = startPos,
            envArray = new EnvironmentFactors[width * height],
            biomeIndexArray = new int[width * height]
        };

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int idx = y * width + x;
                int worldX = startPos.x + x;
                int worldY = startPos.y + y;

                float gx = worldX * noiseScale;
                float gy = worldY * noiseScale;

                EnvironmentFactors env = SampleEnvironmentFactorsStatic(gx, gy, seed, noiseConfigs);
                int biomeIndex = FindMatchingBiomeIndexStatic(env, biomeSnapshots);

                result.envArray[idx] = env;
                result.biomeIndexArray[idx] = biomeIndex;
            }
        }

        return result;
    }

    private static EnvironmentFactors SampleEnvironmentFactorsStatic(float x, float y, int seed, List<BaseNoise> noiseConfigs)
    {
        const float defaultValue = 0.5f;
        if (noiseConfigs == null || noiseConfigs.Count == 0)
        {
            return new EnvironmentFactors
            {
                Temperature = defaultValue,
                Humidity = defaultValue,
                Precipitation = defaultValue,
                Solidity = defaultValue,
                Hight = defaultValue
            };
        }

        float sumTemperature = 0f;
        float sumHumidity = 0f;
        float sumPrecipitation = 0f;
        float sumSolidity = 0f;
        float sumHeight = 0f;

        int countTemperature = 0;
        int countHumidity = 0;
        int countPrecipitation = 0;
        int countSolidity = 0;
        int countHeight = 0;

        for (int i = 0; i < noiseConfigs.Count; i++)
        {
            var noise = noiseConfigs[i];
            if (noise == null)
                continue;

            float v = noise.Sample(x, y, seed);
            switch (noise.noiseType)
            {
                case NoiseType.Temperature:
                    sumTemperature += v;
                    countTemperature++;
                    break;
                case NoiseType.Humidity:
                    sumHumidity += v;
                    countHumidity++;
                    break;
                case NoiseType.Precipitation:
                    sumPrecipitation += v;
                    countPrecipitation++;
                    break;
                case NoiseType.Solidity:
                    sumSolidity += v;
                    countSolidity++;
                    break;
                case NoiseType.Land:
                    sumHeight += v;
                    countHeight++;
                    break;
            }
        }

        float temperature = countTemperature > 0 ? (sumTemperature / countTemperature) : defaultValue;
        float humidity = countHumidity > 0 ? (sumHumidity / countHumidity) : defaultValue;
        float precipitation = countPrecipitation > 0 ? (sumPrecipitation / countPrecipitation) : defaultValue;
        float solidity = countSolidity > 0 ? (sumSolidity / countSolidity) : defaultValue;
        float height = countHeight > 0 ? (sumHeight / countHeight) : defaultValue;

        return new EnvironmentFactors
        {
            Temperature = Mathf.Clamp01(temperature),
            Humidity = Mathf.Clamp01(humidity),
            Precipitation = Mathf.Clamp01(precipitation),
            Solidity = Mathf.Clamp01(solidity),
            Hight = Mathf.Clamp01(height)
        };
    }

    private static int FindMatchingBiomeIndexStatic(EnvironmentFactors env, BiomeConditionSnapshot[] snapshots)
    {
        if (snapshots == null || snapshots.Length == 0)
            return -1;

        for (int i = 0; i < snapshots.Length; i++)
        {
            var b = snapshots[i];
            if (b.tMin <= env.Temperature && env.Temperature <= b.tMax
                && b.hMin <= env.Humidity && env.Humidity <= b.hMax
                && b.pMin <= env.Precipitation && env.Precipitation <= b.pMax
                && b.htMin <= env.Hight && env.Hight <= b.htMax
                && b.sMin <= env.Solidity && env.Solidity <= b.sMax)
            {
                return b.biomeIndex;
            }
        }

        return -1;
    }

    /// <summary>
    /// 生成地形瓦片数据并添加到地图
    /// </summary>
    private void GenerateTerrainTile(Map map, Vector2Int worldPos, BiomeData biome, EnvironmentFactors env)
    {
        // 1. 获取 Tile_Block SO
        Tile_Block tileBlock = biome.TerrainConfig.Get_Tile_Block(env);

        // 2. 直接从 Tile_Block 的模板生成 TileData（无需额外缓存）
        TileData template = tileBlock.tileDataTemplate;

        // 3. 克隆 TileData（使用手写 Clone，避免通用深拷贝开销）
        var tile = template.Clone();

        var unityTileBase = tileBlock.GetTileBaseAsset();

        // 4. 初始化瓦片（根据环境因子调整）
        tile.Initialize_Env(env);

        // 5. 设置瓦片位置
        tile.position = new Vector3Int(worldPos.x, worldPos.y, 0);

        // 6. 仅添加到地图数据层；实际 Tilemap 绘制由 Map 自身的加载/刷新流程负责
        map.ADDTileData(worldPos, tile);
        // 7. 直接设置 Tilemap（视觉层）
        map.tileMap.SetTile(new Vector3Int(worldPos.x, worldPos.y, 0), unityTileBase);
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 清除当前地图的所有瓦片和数据
    /// </summary>
    private void ClearMap(Map map)
    {
        if (map == null)
            return;

        if (map.tileMap != null)
            map.tileMap.ClearAllTiles();

        if (map.Data != null)
            map.Data.ClearAllTiles();
    }

    /// <summary>
    /// 地图生成完成后的回调方法
    /// </summary>
    // 旧版在生成器内收尾；现改为由 Map 统一收尾（见 Map.GenerateByPipeline）。

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
    // 此生成器不直接处理鼠标检测与调试输出。
    // 相关运行时调试已由 EnvironmentInfoDisplay 等工具脚本负责，以保持本类职责单一。
    #endregion
}