using System;
using System.Collections;
using System.Collections.Generic;
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

    [Header("温度映射")]
    [Tooltip("温度映射配置（0~1 -> 摄氏温度），可由策划配表控制")]
    public TemperatureMappingProfile temperatureMappingProfile;
    [Tooltip("当 temperatureMappingProfile 为空时使用的默认摄氏区间")]
    public Vector2 defaultTemperatureRangeCelsius = new Vector2(-10f, 16f);

    [Tooltip("噪声配置列表：直接配置 BaseNoise（SerializeReference 多态）。\n通过 BaseNoise.noiseType 匹配采样类型。")]
    [SerializeReference]
    public List<BaseNoise> NoiseConfigs = new List<BaseNoise>();

    [Header("调试设置")]
    [Tooltip("是否在屏幕上用颜色块可视化各个格子的生物群系分布")]
    public bool showBiomeOverlay = false;

    [Header("高度二次强化")]
    [Tooltip("开启后二次强化地形高度：高的更高，低的更低")]
    public bool enableHeightSecondaryBoost = false;
    [Tooltip("二次强化强度，0=关闭效果，1=明显强化")]
    [Range(0f, 2f)]
    public float heightSecondaryBoostStrength = 1f;
    #endregion

    #region 只读属性
    public Vector2 ChunkSize => ChunkMgr.GetChunkSize();
    #endregion

    #region 内部变量
    [NonSerialized]
    private int generationSeed;

    private int Seed => generationSeed != 0
        ? generationSeed
        : (SaveDataMgr.Instance?.SaveData?.Seed ?? 1);

    public float NoiseScale => plantData != null ? plantData.NoiseScale : 0.01f;

    [NonSerialized]
    private bool _hasLoggedNoiseConfigsNull;

    [NonSerialized]
    private bool _hasLoggedNoiseConfigsEmpty;

    [NonSerialized]
    private bool _hasLoggedMissingLandNoise;

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

        generationSeed = context.WorldSeed;
        GenerateRandomMap_TileData(context.Map, context.PlanetData);
    }

    public override IEnumerator GenerateAsync(MapGenerationContext context, int workBatchSize)
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

        generationSeed = context.WorldSeed;
        Map = context.Map;
        plantData = context.PlanetData ?? SaveDataMgr.Instance?.GetCurrentPlanetData() ?? plantData;
        InitializeGenerationEnvironment(Map);

        Vector2Int startPos = Map.Data.position;
        Vector2 size = ChunkSize;
        int batchSize = Mathf.Max(1, workBatchSize);
        int processedInBatch = 0;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                GenerateTile(Map, startPos, x, y, renderImmediately: false);
                processedInBatch++;

                if (processedInBatch >= batchSize)
                {
                    processedInBatch = 0;
                    yield return null;
                }
            }
        }
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
        map.Data.EnsureEnvironmentStorage((int)size.x, (int)size.y);
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
                GenerateTile(map, startPos, x, y, renderImmediately: true);
            }
        }
        // 注意：收尾（TileLoaded/烘焙/刷新）由 Map.GenerateByPipeline() 统一处理，
        // 以保证后续生成器（例如河流）可以基于本次大陆生成结果继续加工。
    }

    private void GenerateTile(Map map, Vector2Int startPos, int x, int y, bool renderImmediately)
    {
        Vector2Int worldPos = new Vector2Int(startPos.x + x, startPos.y + y);
        Vector2Int localPos = worldPos - map.Data.position;

        CalculateEnvironmentFactors(
            worldPos,
            out float temperature,
            out float humidity,
            out float precipitation,
            out float solidity,
            out float hight,
            out float pollution);

        float defaultTempCelsius = EvaluateTemperatureCelsius(temperature, null);
        StoreEnvironmentFactors(
            map,
            localPos,
            temperature,
            defaultTempCelsius,
            humidity,
            precipitation,
            solidity,
            hight,
            pollution);

        BiomeData biome = GenerateBiomeTile(map, localPos);
        if (biome == null)
            return;

        float temperatureCelsius = EvaluateTemperatureCelsius(temperature, biome);
        StoreEnvironmentFactors(
            map,
            localPos,
            temperature,
            temperatureCelsius,
            humidity,
            precipitation,
            solidity,
            hight,
            pollution);

        GenerateTerrainTile(map, worldPos, localPos, biome, renderImmediately);
    }
    #endregion

    #region 地块生成逻辑

    /// <summary>
    /// 计算指定位置的环境参数
    /// </summary>
    private void CalculateEnvironmentFactors(
        Vector2Int worldPos,
        out float temperature,
        out float humidity,
        out float precipitation,
        out float solidity,
        out float hight,
        out float pollution)
    {
        float gx = worldPos.x * NoiseScale;
        float gy = worldPos.y * NoiseScale;
        SampleEnvironmentFactors(
            gx,
            gy,
            Seed,
            out temperature,
            out humidity,
            out precipitation,
            out solidity,
            out hight,
            out pollution);
    }

    /// <summary>
    /// 不创建 Chunk，直接使用与正式地形生成相同的噪声配置预测任意世界坐标。
    /// </summary>
    public EnvironmentSample SampleEnvironmentAtWorld(Vector2Int worldPos, int worldSeed)
    {
        float gx = worldPos.x * NoiseScale;
        float gy = worldPos.y * NoiseScale;
        SampleEnvironmentFactors(
            gx,
            gy,
            worldSeed == 0 ? 1 : worldSeed,
            out float temperature,
            out float humidity,
            out float precipitation,
            out float solidity,
            out float hight,
            out float pollution);

        return new EnvironmentSample(
            temperature,
            EvaluateTemperatureCelsius(temperature, null),
            humidity,
            precipitation,
            solidity,
            hight,
            pollution);
    }

    /// <summary>
    /// 采样环境因子（一次遍历 NoiseConfigs，每个 noise 执行一次 Sample）
    /// </summary>
    private void SampleEnvironmentFactors(
        float x,
        float y,
        int seed,
        out float temperature,
        out float humidity,
        out float precipitation,
        out float solidity,
        out float hight,
        out float pollution)
    {
        // 默认值：当某个类型未配置噪声时使用
        const float defaultValue = 0.5f;
        pollution = 0f;

        if (NoiseConfigs == null)
        {
            if (!_hasLoggedNoiseConfigsNull)
            {
                _hasLoggedNoiseConfigsNull = true;
                Debug.LogError("[ChunkGenerator_Land] ❌ NoiseConfigs 为 null，无法采样环境因子（将使用默认值 Env=0.5）。");
            }
            temperature = defaultValue;
            humidity = defaultValue;
            precipitation = defaultValue;
            solidity = defaultValue;
            hight = defaultValue;
            return;
        }

        if (NoiseConfigs.Count == 0)
        {
            if (!_hasLoggedNoiseConfigsEmpty)
            {
                _hasLoggedNoiseConfigsEmpty = true;
                Debug.LogWarning("[ChunkGenerator_Land] ⚠️ NoiseConfigs 为空，无法采样环境因子（将使用默认值 Env=0.5）。");
            }
            temperature = defaultValue;
            humidity = defaultValue;
            precipitation = defaultValue;
            solidity = defaultValue;
            hight = defaultValue;
            return;
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

        temperature = countTemperature > 0 ? (sumTemperature / countTemperature) : defaultValue;
        humidity = countHumidity > 0 ? (sumHumidity / countHumidity) : defaultValue;
        precipitation = countPrecipitation > 0 ? (sumPrecipitation / countPrecipitation) : defaultValue;
        solidity = countSolidity > 0 ? (sumSolidity / countSolidity) : defaultValue;
        hight = countHeight > 0 ? (sumHeight / countHeight) : defaultValue;
        hight = ApplyHeightSecondaryBoost(hight);

        if (countHeight == 0 && !_hasLoggedMissingLandNoise)
        {
            _hasLoggedMissingLandNoise = true;
            Debug.LogWarning("[ChunkGenerator_Land] ⚠️ 未配置任何 NoiseType.Land 噪声，高度将使用默认值 0.5。");
        }

        temperature = Mathf.Clamp01(temperature);
        humidity = Mathf.Clamp01(humidity);
        precipitation = Mathf.Clamp01(precipitation);
        solidity = Mathf.Clamp01(solidity);
        hight = Mathf.Clamp01(hight);
    }

    private float ApplyHeightSecondaryBoost(float height)
    {
        if (!enableHeightSecondaryBoost)
            return Mathf.Clamp01(height);

        float h = Mathf.Clamp01(height);
        float d = h - 0.5f;

        // 二次项强化：中心附近变化小，两端变化更明显
        float boosted = h + Mathf.Sign(d) * d * d * 4f * Mathf.Max(0f, heightSecondaryBoostStrength);
        return Mathf.Clamp01(boosted);
    }

    private float EvaluateTemperatureCelsius(float normalizedTemperature, BiomeData biome)
    {
        float t = Mathf.Clamp01(normalizedTemperature);

        if (temperatureMappingProfile != null)
        {
            return temperatureMappingProfile.Evaluate(t);
        }

        return Mathf.Lerp(defaultTemperatureRangeCelsius.x, defaultTemperatureRangeCelsius.y, t);
    }



    /// <summary>
    /// 存储环境参数到网格
    /// </summary>
    private void StoreEnvironmentFactors(
        Map map,
        Vector2Int localPos,
        float temperature,
        float temperatureCelsius,
        float humidity,
        float precipitation,
        float solidity,
        float hight,
        float pollution)
    {
        if (map == null || map.Data == null || map.Data.EnvironmentLayers == null)
        {
            Debug.LogError("[环境存储] ❌ EnvironmentLayers 未初始化");
            return;
        }

        int width = map.Data.EnvironmentLayers.Width;
        int height = map.Data.EnvironmentLayers.Height;

        // 边界检查
        if (localPos.x < 0 || localPos.x >= width ||
            localPos.y < 0 || localPos.y >= height)
        {
            return; // 超出范围，直接跳过
        }

        map.Data.SetEnvironmentAtLocal(
            localPos.x,
            localPos.y,
            temperature,
            temperatureCelsius,
            humidity,
            precipitation,
            solidity,
            hight,
            pollution);
    }

    /// <summary>
    /// 生成生物群系对应的地形瓦片
    /// </summary>
    private BiomeData GenerateBiomeTile(Map map, Vector2Int localPos)
    {
        // 1. 匹配生物群系
        BiomeData biome = FindMatchingBiome(localPos);
        if (biome == null)
        {
            Vector2Int worldPos = map.Data.position + localPos;
            Debug.LogWarning($"[生物群系] ⚠️ 位置 {worldPos} 无法匹配任何生物群系");
            return null;
        }

        return biome;
    }

    /// <summary>
    /// 查找匹配的生物群系（带缓存优化）
    /// </summary>
    private BiomeData FindMatchingBiome(Vector2Int localPos)
    {
        EnvironmentLayers layers = Map != null && Map.Data != null ? Map.Data.EnvironmentLayers : null;

        foreach (var biome in biomes)
        {
            if (biome != null && biome.IsEnvironmentValid(layers, localPos.x, localPos.y))
                return biome;
        }
        return null;
    }

    /// <summary>
    /// 生成地形瓦片数据并添加到地图
    /// </summary>
    private void GenerateTerrainTile(
        Map map,
        Vector2Int worldPos,
        Vector2Int localPos,
        BiomeData biome,
        bool renderImmediately)
    {
        // 1. 获取 Tile_Block SO
        Tile_Block tileBlock = biome.TerrainConfig.Get_Tile_Block();

        // 2. 直接从 Tile_Block 的模板生成 TileData（无需额外缓存）
        TileData template = tileBlock.tileDataTemplate;

        // 3. 克隆 TileData（使用手写 Clone，避免通用深拷贝开销）
        var tile = template.Clone();

        // 4. 初始化瓦片（根据环境因子调整）
        tile.Initialize_Env(map.Data.EnvironmentLayers, localPos.x, localPos.y);

        // 5. 设置瓦片位置
        tile.position = new Vector3Int(worldPos.x, worldPos.y, 0);

        // 6. 仅添加到地图数据层；实际 Tilemap 绘制由 Map 自身的加载/刷新流程负责
        map.ADDTileData(worldPos, tile);

        // 手动同步生成入口仍立即绘制；运行时异步管线统一交给 Map 分帧写入。
        if (renderImmediately)
        {
            var unityTileBase = tileBlock.GetTileBaseAsset();
            map.tileMap.SetTile(new Vector3Int(worldPos.x, worldPos.y, 0), unityTileBase);
        }
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
