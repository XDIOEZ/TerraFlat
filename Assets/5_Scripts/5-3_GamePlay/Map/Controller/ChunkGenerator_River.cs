using System;
using System.Collections;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 河流生成器：
/// - 作为 Map.mapGenerators 管线中的一个步骤执行（通常放在大陆生成器之后）
/// - 当前正式管线基于大陆高度与降水预测源头、追踪下坡路径、形成湖泊并按汇流量扩宽
/// - 旧 Voronoi 遮罩实现仅保留兼容，不在 Generate/GenerateAsync 中执行
/// - 最后用 riverTileBlock 覆盖地表（TileData + Tilemap）
/// </summary>
[Serializable]
public class ChunkGenerator_River : ChunkGeneratorBase
{
    private const float SeaSalt = 80f;

    #region 配置参数
    [FoldoutGroup("高级/运行时引用", Expanded = false)]
    [LabelText("目标 Tilemap")]
    [PropertyTooltip("可选。未指定时使用当前 Map.tileMap。")]
    public Tilemap targetTilemap;

    [Title("河流地块")]
    [LabelText("河流 TileBlock")]
    [PropertyTooltip("河流/水面使用的 Tile_Block；其模板必须是 TileData_Water。")]
    public Tile_Block riverTileBlock;

    [Title("确定性与终止条件")]
    [LabelText("河流局部种子盐")]
    [PropertyTooltip("与世界种子组合，控制源头与河网细节；修改后会改变未生成区域的河流结果。")]
    public int seed = 12345;

    [NonSerialized]
    private int activeWorldSeed = 1;

    [HideInInspector]
    public float cellSize = 18f;

    [HideInInspector]
    public float edgeWidth = 1.2f;

    [HideInInspector]
    public Vector2 edgeWidthRange = new Vector2(1.2f, 1.2f);

    [HideInInspector]
    public bool useMultiSample = true;

    [HideInInspector]
    public float sampleInset = 0.18f;

    [HideInInspector]
    public float warpAmplitude = 4f;

    [HideInInspector]
    public float warpFrequency = 0.025f;

    [HideInInspector]
    public float trigWaveAmplitude = 2.0f;

    [HideInInspector]
    public float trigWaveFrequency = 0.10f;

    [HideInInspector]
    public float trigWaveNoiseFrequency = 0.035f;

    [HideInInspector]
    public int connectPasses = 2;

    [HideInInspector]
    public int connectNeighborThreshold = 2;

    [HideInInspector]
    public bool removeIsolated = true;

    [HideInInspector]
    public bool bridgeGaps = true;

    [HideInInspector]
    public int bridgePasses = 1;

    [LabelText("低地终止海拔")]
    [PropertyTooltip("河流追踪到该归一化海拔以下时停止，视为已经汇入低地或海洋。")]
    [Range(0f, 1f)]
    public float minHeight = 0.20f;

    [HideInInspector]
    public float maxHeight = 0.95f;

    [Title("写入与水深")]
    [LabelText("地块写入模式")]
    [PropertyTooltip("ReplaceTop 替换最顶层 TileData；AddLayer 在顶部增加河流层。")]
    public RiverWriteMode writeMode = RiverWriteMode.ReplaceTop;

    [LabelText("边缘最浅深度")]
    [PropertyTooltip("河流边缘的归一化水深。")]
    [Range(0f, 1f)]
    public float riverDepthMin = 0.15f;

    [LabelText("主河道最深深度")]
    [PropertyTooltip("主河道中心的归一化水深。")]
    [Range(0f, 1f)]
    public float riverDepthMax = 0.95f;

    [LabelText("水深曲线指数")]
    [PropertyTooltip("大于 1 更强调中心深度，小于 1 会让整体更深。")]
    [Range(0.2f, 4f)]
    public float riverDepthPower = 1.35f;

    [HideInInspector]
    public bool useDepthPowerForWidth = true;

    [HideInInspector]
    public float riverWidthPower = 1.35f;

    #region 水文河湖
    [Title("真实水文生成（正式管线）")]
    [LabelText("跨区块计算边距")]
    [PropertyTooltip("当前 Chunk 外额外参与水文计算的格数。越大越容易形成连续河流，但生成成本越高。")]
    [Min(8)] public int hydrologyHalo = 32;

    [LabelText("每帧处理格数")]
    [PropertyTooltip("水文生成每帧至少处理的格子数量。")]
    [Min(64)] public int hydrologyCellsPerFrame = 768;

    [LabelText("源头候选间距")]
    [PropertyTooltip("候选网格的世界格间距，每个候选网格最多产生一个源头。")]
    [Min(6)] public int sourceSpacing = 18;

    [LabelText("源头区域生成概率")]
    [PropertyTooltip("每个源头候选区域实际生成源头的确定性概率，用于控制河网密度。")]
    [Range(0f, 1f)] public float sourceCellChance = 0.35f;

    [Tooltip("源头最低海拔，候选区域只从达到该海拔的地形中选择源头")]
    [Range(0f, 1f)] public float sourceMinHeight = 0.68f;

    [Tooltip("源头最低降水量，避免极干旱石地大量产生河流")]
    [Range(0f, 1f)] public float sourceMinPrecipitation = 0.08f;

    [Tooltip("单条河流最多追踪步数")]
    [Min(32)] public int maxRiverTraceSteps = 480;

    [Tooltip("河流遇到低洼时，搜索湖泊出口的最大半径")]
    [Min(2)] public int maxLakeRadius = 12;

    [Tooltip("湖泊水位允许高于洼地最低点的最大高度")]
    [Range(0.001f, 0.25f)] public float maxLakeLevelRise = 0.08f;

    [Tooltip("形成湖泊至少需要覆盖的格子数")]
    [Min(2)] public int minLakeCells = 5;

    [Tooltip("河道最小半径")]
    [Range(0f, 4f)] public float minRiverRadius = 0.35f;

    [Tooltip("主河道最大半径")]
    [Range(1f, 8f)] public float maxRiverRadius = 3f;

    [Tooltip("汇流量对河宽的影响")]
    [Range(0.05f, 2f)] public float flowWidthScale = 0.55f;

    [Tooltip("河流曲流强度。越大越倾向沿近似等高线摆动，但始终限制在可下降方向")]
    [Range(0f, 0.08f)] public float meanderStrength = 0.025f;

    [Tooltip("河流曲流噪声频率。越低弯道越舒缓，越高弯道越频繁")]
    [Range(0.002f, 0.2f)] public float meanderFrequency = 0.025f;

    [Tooltip("湖岸形变强度。越大湖岸越不规则，0 表示完全服从等高线")]
    [Range(0f, 0.08f)] public float lakeShoreIrregularity = 0.025f;

    [Tooltip("湖岸形变噪声频率")]
    [Range(0.01f, 0.3f)] public float lakeShoreFrequency = 0.09f;
    #endregion

    #region 石头生成（河床/河岸）
    [Header("河床/河岸石头")]
    [Tooltip("是否在生成河流后，额外在河内与河两侧生成石头")]
    public bool spawnRiverStones = true;

    [Tooltip("石头预制体（必填）")]
    public GameObject Prefab_Stone;

    [Tooltip("石头父物体（不填则挂到 Map.transform 下）")]
    public Transform stoneParent;

    [Tooltip("每个 Chunk 生成石头数量上限（防止过量实例）")]
    public int maxStonesPerChunk = 120;

    [Header("河床石头")]
    [Range(0f, 1f)]
    [Tooltip("河流格子生成石头的概率")]
    public float riverStoneChance = 0.18f;

    [Header("河岸石头")]
    [Range(0f, 1f)]
    [Tooltip("河岸（紧邻河流的非河格子）生成石头的概率")]
    public float bankStoneChance = 0.10f;

    [Header("河岸燧石")]
    [Tooltip("燧石预制体（可选，不填则不生成燧石）")]
    public GameObject Prefab_Flint;

    [Range(0f, 1f)]
    [Tooltip("河岸（紧邻河流的非河格子）生成燧石的概率（建议低于河岸石头）")]
    public float bankFlintChance = 0.04f;

    [Min(1)]
    [Tooltip("河岸判定半径（格子）。1=紧邻一圈")]
    public int bankRadius = 1;

    [Header("外观随机")]
    [Tooltip("位置随机偏移（世界单位）")]
    public Vector2 stoneOffsetRange = new Vector2(0.25f, 0.25f);

    [Tooltip("随机旋转范围（Z 轴角度）")]
    public Vector2 stoneRotationZRange = new Vector2(0f, 360f);

    [Tooltip("随机缩放范围（统一缩放）")]
    public Vector2 stoneUniformScaleRange = new Vector2(0.75f, 1.25f);

    [Tooltip("每次 Generate 是否清理旧的河流石头（推荐开启，避免重复堆叠）")]
    public bool clearPreviousStones = true;

    [Tooltip("父物体命名前缀（用于清理与组织层级）")]
    public string stoneRootNamePrefix = "RiverStones";
    #endregion

    public enum RiverWriteMode
    {
        ReplaceTop,
        AddLayer
    }
    #endregion

    #region 内部状态
    [NonSerialized] private bool _hasLoggedMissingTilemap;
    [NonSerialized] private bool _hasLoggedMissingRiverTileBlock;
    [NonSerialized] private bool _hasLoggedEnvMissing;
    [NonSerialized] private bool _hasLoggedMissingStonePrefab;
    [NonSerialized] private bool _hasLoggedRiverTileNotWater;
    #endregion

    #region 管线入口
    [Button("生成河流")]
    public override void Generate(MapGenerationContext context)
    {
        IEnumerator routine = GenerateHydrologyCoroutine(context, int.MaxValue);
        while (routine.MoveNext())
        {
        }
    }

    public override IEnumerator GenerateAsync(MapGenerationContext context, int workBatchSize)
    {
        return GenerateHydrologyCoroutine(
            context,
            Mathf.Max(Mathf.Max(1, workBatchSize), hydrologyCellsPerFrame));
    }

    private void GenerateLegacyVoronoi(MapGenerationContext context)
    {
        if (context == null)
        {
            LogNullContext(nameof(ChunkGenerator_River));
            return;
        }

        if (context.Map == null)
        {
            LogNullMap(nameof(ChunkGenerator_River));
            return;
        }

        Map = context.Map;
        activeWorldSeed = context.WorldSeed;

        // Tilemap
        if (targetTilemap == null)
        {
            targetTilemap = Map.tileMap;
        }

        if (targetTilemap == null)
        {
            if (!_hasLoggedMissingTilemap)
            {
                _hasLoggedMissingTilemap = true;
                Debug.LogError("[ChunkGenerator_River] ❌ targetTilemap 为空（且 Map.tileMap 也为空），无法绘制河流", Map);
            }
            return;
        }

        // Tile_Block
        if (riverTileBlock == null)
        {
            if (!_hasLoggedMissingRiverTileBlock)
            {
                _hasLoggedMissingRiverTileBlock = true;
                Debug.LogError("[ChunkGenerator_River] ❌ riverTileBlock 为空，无法生成河流 TileData/TileBase", Map);
            }
            return;
        }

        if (riverTileBlock.tileDataTemplate is not TileData_Water)
        {
            if (!_hasLoggedRiverTileNotWater)
            {
                _hasLoggedRiverTileNotWater = true;
                Debug.LogError($"[ChunkGenerator_River] ❌ riverTileBlock({riverTileBlock.name}) 的 tileDataTemplate 不是 TileData_Water：无法按 salt=0 河流逻辑生成/判定", Map);
            }
            return;
        }

        if (Map.Data == null)
        {
            Debug.LogError("[ChunkGenerator_River] ❌ Map.Data 为空，无法写入河流 TileData", Map);
            return;
        }

        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        // 河流仅为命中的格子按需创建列表，避免为整块地图预建大量空 List。
        Map.Data.EnsureTileDataArray((int)chunkSize.x, (int)chunkSize.y, initCells: false);

        Vector2Int startPos = Map.Data.position;
        int width = (int)chunkSize.x;
        int height = (int)chunkSize.y;

        // 1) 基于网络状噪声生成遮罩
        bool[] river = new bool[width * height];
        BuildRiverMask_ByVoronoiEdges(width, height, startPos, river);

        // 2) 轻量连通性后处理
        if (connectPasses > 0)
        {
            bool[] tmp = new bool[river.Length];
            for (int pass = 0; pass < connectPasses; pass++)
            {
                ImproveConnectivity(width, height, river, tmp);
                // swap
                var swap = river;
                river = tmp;
                tmp = swap;
            }
        }

        // 2.5) 断点桥接（对“变细后断开”更直接）
        if (bridgeGaps && bridgePasses > 0)
        {
            bool[] tmp = new bool[river.Length];
            for (int pass = 0; pass < bridgePasses; pass++)
            {
                BridgeOneCellGaps(width, height, river, tmp);
                var swap = river;
                river = tmp;
                tmp = swap;
            }
        }

        if (removeIsolated)
        {
            RemoveIsolatedCells(width, height, river);
        }

        // 3) 写入 TileData + Tilemap
        int appliedCount = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (!river[Index(x, y, width)])
                    continue;

                Vector2Int worldPos = new Vector2Int(startPos.x + x, startPos.y + y);
                ApplyRiverAt(worldPos, x, y, width, height, river);
                appliedCount++;
            }
        }

        // 4) 生成河床/河岸石头
        if (spawnRiverStones)
        {
            SpawnStones_ForRiver(width, height, startPos, river);
        }

//        Debug.Log($"[ChunkGenerator_River] ✅ 河流遮罩生成完成（网络噪声/Voronoi边界），覆盖格子数: {appliedCount}", Map);
    }

    private IEnumerator GenerateHydrologyCoroutine(MapGenerationContext context, int workBatchSize)
    {
        if (!TryPrepareGeneration(context, out ChunkGenerator_Land land, out int width, out int height))
            yield break;

        int halo = Mathf.Max(8, hydrologyHalo);
        int workWidth = width + halo * 2;
        int workHeight = height + halo * 2;
        int workCount = workWidth * workHeight;
        Vector2Int coreOrigin = Map.Data.position;
        Vector2Int workOrigin = coreOrigin - new Vector2Int(halo, halo);

        float[] heights = new float[workCount];
        float[] precipitation = new float[workCount];
        bool[] stoneGround = new bool[workCount];
        bool[] centerLine = new bool[workCount];
        bool[] lakeMask = new bool[workCount];
        float[] waterDepth = new float[workCount];
        int[] flow = new int[workCount];
        int[] visitStamp = new int[workCount];
        int stamp = 0;
        int processed = 0;

        for (int x = 0; x < workWidth; x++)
        {
            for (int y = 0; y < workHeight; y++)
            {
                Vector2Int worldPos = workOrigin + new Vector2Int(x, y);
                EnvironmentSample sample = land.SampleEnvironmentAtWorld(worldPos, context.WorldSeed);
                int index = Index(x, y, workWidth);
                heights[index] = sample.Hight;
                precipitation[index] = sample.Precipitation;
                stoneGround[index] = IsStoneGround(land, sample);

                if (++processed >= workBatchSize)
                {
                    processed = 0;
                    yield return null;
                }
            }
        }

        int spacing = Mathf.Max(6, sourceSpacing);
        int minSourceCellX = FloorDiv(workOrigin.x, spacing) - 1;
        int maxSourceCellX = FloorDiv(workOrigin.x + workWidth - 1, spacing) + 1;
        int minSourceCellY = FloorDiv(workOrigin.y, spacing) - 1;
        int maxSourceCellY = FloorDiv(workOrigin.y + workHeight - 1, spacing) + 1;
        int generationSeed = GetGenerationSeed();
        int sourceSalt = unchecked((int)0x51ED270B);
        float sourceChance = Mathf.Clamp01(sourceCellChance);

        for (int sourceCellX = minSourceCellX; sourceCellX <= maxSourceCellX; sourceCellX++)
        {
            for (int sourceCellY = minSourceCellY; sourceCellY <= maxSourceCellY; sourceCellY++)
            {
                if (sourceChance <= 0f ||
                    Hash01(sourceCellX, sourceCellY, generationSeed ^ sourceSalt) >= sourceChance)
                {
                    continue;
                }

                if (!TrySelectHeadwater(
                        sourceCellX,
                        sourceCellY,
                        spacing,
                        workOrigin,
                        workWidth,
                        workHeight,
                        heights,
                        precipitation,
                        stoneGround,
                        generationSeed,
                        out int sourceX,
                        out int sourceY))
                {
                    continue;
                }

                stamp++;
                int currentX = sourceX;
                int currentY = sourceY;
                Vector2Int previousDirection = Vector2Int.zero;
                int maxSteps = Mathf.Max(32, maxRiverTraceSteps);
                for (int step = 0; step < maxSteps; step++)
                {
                    if (!Contains(currentX, currentY, workWidth, workHeight))
                        break;

                    int currentIndex = Index(currentX, currentY, workWidth);
                    if (visitStamp[currentIndex] == stamp)
                        break;

                    visitStamp[currentIndex] = stamp;
                    centerLine[currentIndex] = true;
                    flow[currentIndex]++;

                    if (heights[currentIndex] <= minHeight)
                        break;

                    if (TryGetDownhillNeighbor(
                            currentX,
                            currentY,
                            workWidth,
                            workHeight,
                            heights,
                            generationSeed,
                            workOrigin,
                            previousDirection,
                            meanderStrength,
                            meanderFrequency,
                            out int nextX,
                            out int nextY))
                    {
                        previousDirection = new Vector2Int(nextX - currentX, nextY - currentY);
                        currentX = nextX;
                        currentY = nextY;
                    }
                    else if (TryCreateLakeAtSink(
                                 currentX,
                                 currentY,
                                 workWidth,
                                 workHeight,
                                 heights,
                                 lakeMask,
                                 waterDepth,
                                 generationSeed,
                                 workOrigin,
                                 out nextX,
                                 out nextY))
                    {
                        previousDirection = Vector2Int.zero;
                        currentX = nextX;
                        currentY = nextY;
                    }
                    else
                    {
                        break;
                    }

                    if (++processed >= workBatchSize)
                    {
                        processed = 0;
                        yield return null;
                    }
                }
            }
        }

        bool[] riverMask = new bool[workCount];
        float maxRadius = Mathf.Max(1f, maxRiverRadius);
        for (int x = 0; x < workWidth; x++)
        {
            for (int y = 0; y < workHeight; y++)
            {
                int centerIndex = Index(x, y, workWidth);
                if (!centerLine[centerIndex])
                    continue;

                float radius = Mathf.Clamp(
                    minRiverRadius + Mathf.Log(flow[centerIndex] + 1f, 2f) * flowWidthScale,
                    Mathf.Max(0f, minRiverRadius),
                    maxRadius);
                int cellRadius = Mathf.CeilToInt(radius);
                for (int dx = -cellRadius; dx <= cellRadius; dx++)
                {
                    int nx = x + dx;
                    if ((uint)nx >= (uint)workWidth)
                        continue;

                    for (int dy = -cellRadius; dy <= cellRadius; dy++)
                    {
                        int ny = y + dy;
                        if ((uint)ny >= (uint)workHeight)
                            continue;

                        float distance = Mathf.Sqrt(dx * dx + dy * dy);
                        if (distance > radius + 0.35f)
                            continue;

                        int targetIndex = Index(nx, ny, workWidth);
                        riverMask[targetIndex] = true;
                        float centerStrength = 1f - Mathf.Clamp01(distance / (radius + 0.5f));
                        float depth = Mathf.Lerp(
                            Mathf.Clamp01(riverDepthMin),
                            Mathf.Clamp01(riverDepthMax),
                            Mathf.Pow(centerStrength, Mathf.Max(0.2f, riverDepthPower)));
                        waterDepth[targetIndex] = Mathf.Max(waterDepth[targetIndex], depth);
                    }
                }

                if (++processed >= workBatchSize)
                {
                    processed = 0;
                    yield return null;
                }
            }
        }

        bool[] coreWaterMask = new bool[width * height];
        for (int localX = 0; localX < width; localX++)
        {
            for (int localY = 0; localY < height; localY++)
            {
                int workIndex = Index(localX + halo, localY + halo, workWidth);
                if (!riverMask[workIndex] && !lakeMask[workIndex])
                    continue;

                Vector2Int worldPos = coreOrigin + new Vector2Int(localX, localY);
                if (IsSeaWaterAt(worldPos))
                    continue;

                WriteFreshWaterAt(worldPos, waterDepth[workIndex], renderImmediately: false);
                coreWaterMask[Index(localX, localY, width)] = true;

                if (++processed >= workBatchSize)
                {
                    processed = 0;
                    yield return null;
                }
            }
        }

        if (spawnRiverStones)
        {
            SpawnStones_ForRiver(width, height, coreOrigin, coreWaterMask);
            yield return null;
        }
    }

    private bool TrySelectHeadwater(
        int sourceCellX,
        int sourceCellY,
        int spacing,
        Vector2Int workOrigin,
        int workWidth,
        int workHeight,
        float[] heights,
        float[] precipitation,
        bool[] stoneGround,
        int generationSeed,
        out int sourceX,
        out int sourceY)
    {
        sourceX = 0;
        sourceY = 0;

        int minX = sourceCellX * spacing - workOrigin.x;
        int minY = sourceCellY * spacing - workOrigin.y;
        int maxX = minX + spacing - 1;
        int maxY = minY + spacing - 1;

        // Only evaluate complete global cells. This keeps headwater selection identical
        // for neighboring chunks while the hydrology halo still covers the playable core.
        if (minX < 1 || minY < 1 || maxX >= workWidth - 1 || maxY >= workHeight - 1)
            return false;

        int bestPeakIndex = -1;
        int bestFallbackIndex = -1;
        float bestPeakScore = float.NegativeInfinity;
        float bestFallbackScore = float.NegativeInfinity;
        int tieSalt = unchecked((int)0x36D8F13B);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                int index = Index(x, y, workWidth);
                if (heights[index] < sourceMinHeight ||
                    precipitation[index] < sourceMinPrecipitation)
                {
                    continue;
                }

                int worldX = workOrigin.x + x;
                int worldY = workOrigin.y + y;
                float score = heights[index] * 2f + precipitation[index] * 0.15f;
                if (stoneGround[index])
                    score += 0.1f;
                score += Hash01(worldX, worldY, generationSeed ^ tieSalt) * 0.0001f;

                if (score > bestFallbackScore)
                {
                    bestFallbackScore = score;
                    bestFallbackIndex = index;
                }

                if (!IsLocalHighPoint(x, y, workWidth, workHeight, heights) ||
                    score <= bestPeakScore)
                {
                    continue;
                }

                bestPeakScore = score;
                bestPeakIndex = index;
            }
        }

        int selectedIndex = bestPeakIndex >= 0 ? bestPeakIndex : bestFallbackIndex;
        if (selectedIndex < 0)
            return false;

        sourceX = selectedIndex % workWidth;
        sourceY = selectedIndex / workWidth;
        return true;
    }

    private bool TryPrepareGeneration(
        MapGenerationContext context,
        out ChunkGenerator_Land land,
        out int width,
        out int height)
    {
        land = null;
        width = 0;
        height = 0;
        if (context?.Map == null)
        {
            LogNullContext(nameof(ChunkGenerator_River));
            return false;
        }

        Map = context.Map;
        activeWorldSeed = context.WorldSeed;
        targetTilemap ??= Map.tileMap;
        if (riverTileBlock == null || riverTileBlock.tileDataTemplate is not TileData_Water)
        {
            Debug.LogError("[ChunkGenerator_River] riverTileBlock 必须配置淡水 TileData_Water", Map);
            return false;
        }

        if (Map.Data == null)
        {
            Debug.LogError("[ChunkGenerator_River] Map.Data 为空，无法生成水文", Map);
            return false;
        }

        land = Map.GetGenerator<ChunkGenerator_Land>();
        if (land == null)
        {
            Debug.LogError("[ChunkGenerator_River] 缺少 ChunkGenerator_Land，无法采样世界高度", Map);
            return false;
        }

        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        width = Mathf.Max(1, Mathf.RoundToInt(chunkSize.x));
        height = Mathf.Max(1, Mathf.RoundToInt(chunkSize.y));
        Map.Data.EnsureTileDataArray(width, height, initCells: false);
        return true;
    }

    private static bool Contains(int x, int y, int width, int height)
    {
        return (uint)x < (uint)width && (uint)y < (uint)height;
    }

    private static int FloorDiv(int value, int divisor)
    {
        return Mathf.FloorToInt((float)value / divisor);
    }

    private static bool IsLocalHighPoint(int x, int y, int width, int height, float[] heights)
    {
        float center = heights[Index(x, y, width)];
        int lowerNeighbors = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                int nx = x + dx;
                int ny = y + dy;
                if (!Contains(nx, ny, width, height))
                    continue;

                if (heights[Index(nx, ny, width)] <= center)
                    lowerNeighbors++;
            }
        }
        return lowerNeighbors >= 6;
    }

    private bool IsStoneGround(ChunkGenerator_Land land, EnvironmentSample sample)
    {
        if (land.biomes == null)
            return false;

        for (int i = 0; i < land.biomes.Count; i++)
        {
            BiomeData biome = land.biomes[i];
            if (biome?.Condition == null || !biome.Condition.IsMatch(sample))
                continue;

            List<BiomeTileSpawn_NoSo> tileSpawns = biome.TerrainConfig?.TileSpawns_NoSO;
            if (tileSpawns == null || tileSpawns.Count == 0 || tileSpawns[0]?.TileBlock?.tileDataTemplate == null)
                return false;

            return string.Equals(tileSpawns[0].TileBlock.tileDataTemplate.ID, "Tile_Stone", StringComparison.Ordinal);
        }
        return false;
    }

    private static bool TryGetDownhillNeighbor(
        int x,
        int y,
        int width,
        int height,
        float[] heights,
        int generationSeed,
        Vector2Int workOrigin,
        Vector2Int previousDirection,
        float curveStrength,
        float curveFrequency,
        out int nextX,
        out int nextY)
    {
        nextX = x;
        nextY = y;
        float currentHeight = heights[Index(x, y, width)];
        float bestScore = float.PositiveInfinity;
        bool found = false;
        Vector2 previous = previousDirection.sqrMagnitude > 0
            ? ((Vector2)previousDirection).normalized
            : Vector2.zero;
        int worldX = workOrigin.x + x;
        int worldY = workOrigin.y + y;
        float frequency = Mathf.Max(0.002f, curveFrequency);
        float phase = Mathf.PerlinNoise(
            worldX * frequency + generationSeed * 0.000013f,
            worldY * frequency - generationSeed * 0.000017f) * Mathf.PI * 2f;
        Vector2 preferredCurve = new Vector2(Mathf.Cos(phase), Mathf.Sin(phase));

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                int nx = x + dx;
                int ny = y + dy;
                if (!Contains(nx, ny, width, height))
                    continue;

                float neighborHeight = heights[Index(nx, ny, width)];
                if (neighborHeight >= currentHeight - 0.00005f)
                    continue;

                Vector2 direction = new Vector2(dx, dy).normalized;
                float turnPenalty = previousDirection.sqrMagnitude > 0
                    ? (1f - Vector2.Dot(previous, direction)) * 0.006f
                    : 0f;
                float curveBias = -Vector2.Dot(preferredCurve, direction) * Mathf.Max(0f, curveStrength);
                float diagonalPenalty = dx != 0 && dy != 0 ? 0.0005f : 0f;
                float tieBreak = Hash01(workOrigin.x + nx, workOrigin.y + ny, generationSeed ^ 0x1974) * 0.0001f;
                float score = neighborHeight + turnPenalty + curveBias + diagonalPenalty + tieBreak;
                if (score >= bestScore)
                    continue;

                bestScore = score;
                nextX = nx;
                nextY = ny;
                found = true;
            }
        }
        return found;
    }

    private bool TryCreateLakeAtSink(
        int sinkX,
        int sinkY,
        int width,
        int height,
        float[] heights,
        bool[] lakeMask,
        float[] waterDepth,
        int generationSeed,
        Vector2Int workOrigin,
        out int outletX,
        out int outletY)
    {
        outletX = sinkX;
        outletY = sinkY;
        float sinkHeight = heights[Index(sinkX, sinkY, width)];
        int radius = Mathf.Max(2, maxLakeRadius);
        float spillHeight = float.PositiveInfinity;

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != radius)
                    continue;

                int x = sinkX + dx;
                int y = sinkY + dy;
                if (!Contains(x, y, width, height))
                    continue;

                float heightValue = heights[Index(x, y, width)];
                if (heightValue >= spillHeight)
                    continue;

                spillHeight = heightValue;
                outletX = x;
                outletY = y;
            }
        }

        if (float.IsPositiveInfinity(spillHeight))
            return false;

        float lakeLevel = Mathf.Max(sinkHeight, spillHeight);
        if (lakeLevel - sinkHeight > Mathf.Max(0.001f, maxLakeLevelRise))
            lakeLevel = sinkHeight + Mathf.Max(0.001f, maxLakeLevelRise);

        bool[] visited = new bool[width * height];
        Queue<Vector2Int> frontier = new Queue<Vector2Int>();
        List<int> basinCells = new List<int>();
        frontier.Enqueue(new Vector2Int(sinkX, sinkY));
        visited[Index(sinkX, sinkY, width)] = true;
        float shoreFrequency = Mathf.Max(0.01f, lakeShoreFrequency);
        float shoreIrregularity = Mathf.Max(0f, lakeShoreIrregularity);

        while (frontier.Count > 0)
        {
            Vector2Int cell = frontier.Dequeue();
            int dx = cell.x - sinkX;
            int dy = cell.y - sinkY;
            if (dx * dx + dy * dy > radius * radius)
                continue;

            int index = Index(cell.x, cell.y, width);
            int worldX = workOrigin.x + cell.x;
            int worldY = workOrigin.y + cell.y;
            float shoreNoise = Mathf.PerlinNoise(
                worldX * shoreFrequency + generationSeed * 0.000021f,
                worldY * shoreFrequency - generationSeed * 0.000019f);
            float localLakeLevel = lakeLevel + (shoreNoise - 0.5f) * 2f * shoreIrregularity;
            if (heights[index] > localLakeLevel + 0.002f)
                continue;

            basinCells.Add(index);
            for (int nx = cell.x - 1; nx <= cell.x + 1; nx++)
            {
                for (int ny = cell.y - 1; ny <= cell.y + 1; ny++)
                {
                    if ((nx == cell.x && ny == cell.y) || !Contains(nx, ny, width, height))
                        continue;

                    int neighborIndex = Index(nx, ny, width);
                    if (visited[neighborIndex])
                        continue;

                    visited[neighborIndex] = true;
                    frontier.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }

        if (basinCells.Count < Mathf.Max(2, minLakeCells))
            return false;

        for (int i = 0; i < basinCells.Count; i++)
        {
            int index = basinCells[i];
            lakeMask[index] = true;
            float normalizedDepth = Mathf.Clamp01(
                (lakeLevel - heights[index] + 0.01f) /
                Mathf.Max(0.01f, maxLakeLevelRise));
            waterDepth[index] = Mathf.Max(
                waterDepth[index],
                Mathf.Lerp(riverDepthMin, riverDepthMax, normalizedDepth));
        }

        // 从湖岸最低点继续出流；若最低边界仍高于允许水位，则保留为封闭湖。
        float bestOutletHeight = float.PositiveInfinity;
        bool foundOutlet = false;
        for (int i = 0; i < basinCells.Count; i++)
        {
            int index = basinCells[i];
            int cellY = index / width;
            int cellX = index - cellY * width;
            for (int nx = cellX - 1; nx <= cellX + 1; nx++)
            {
                for (int ny = cellY - 1; ny <= cellY + 1; ny++)
                {
                    if (!Contains(nx, ny, width, height))
                        continue;

                    int neighborIndex = Index(nx, ny, width);
                    if (lakeMask[neighborIndex] || heights[neighborIndex] >= bestOutletHeight)
                        continue;

                    bestOutletHeight = heights[neighborIndex];
                    outletX = nx;
                    outletY = ny;
                    foundOutlet = true;
                }
            }
        }

        if (!foundOutlet || bestOutletHeight > lakeLevel + Mathf.Max(0.001f, maxLakeLevelRise))
        {
            outletX = sinkX;
            outletY = sinkY;
            return false;
        }

        return true;
    }
    #endregion

    #region 遮罩：网络状噪声（Voronoi 边界）
    private void BuildRiverMask_ByVoronoiEdges(int width, int height, Vector2Int startPos, bool[] river)
    {
        if (cellSize <= 0f)
        {
            Debug.LogError("[ChunkGenerator_River] ❌ cellSize 必须大于 0", Map);
            return;
        }

        if (edgeWidth <= 0f && edgeWidthRange.x <= 0f && edgeWidthRange.y <= 0f)
        {
            Debug.LogError("[ChunkGenerator_River] ❌ edgeWidth / edgeWidthRange 均无效：需要提供大于 0 的边界厚度", Map);
            return;
        }

        var layers = Map != null && Map.Data != null ? Map.Data.EnvironmentLayers : null;
        bool hasEnv = layers != null && layers.Width == width && layers.Height == height;
        if (!hasEnv && !_hasLoggedEnvMissing)
        {
            _hasLoggedEnvMissing = true;
            Debug.LogWarning("[ChunkGenerator_River] ⚠️ EnvironmentLayers 未就绪：将跳过 minHeight/maxHeight 高度门控（河网遮罩仍可生成）", Map);
        }

        float cs = Mathf.Max(0.0001f, cellSize);
        float ewFallback = Mathf.Max(0.0001f, edgeWidth);
        float wf = Mathf.Max(0.0001f, warpFrequency);
        float wa = Mathf.Max(0f, warpAmplitude);
        float twA = Mathf.Max(0f, trigWaveAmplitude);
        float twF = Mathf.Max(0.0001f, trigWaveFrequency);
        float twNF = Mathf.Max(0.0001f, trigWaveNoiseFrequency);

        float ewMin = edgeWidthRange.x;
        float ewMax = edgeWidthRange.y;
        if (ewMin > ewMax)
        {
            float tmp = ewMin;
            ewMin = ewMax;
            ewMax = tmp;
        }

        bool useWidthRange = ewMin > 0f && ewMax > 0f;
        if (!useWidthRange)
        {
            ewMin = ewFallback;
            ewMax = ewFallback;
        }

        int generationSeed = GetGenerationSeed();

        // PerlinNoise 输入不要太大：用世界种子与生成器种子的组合值做偏移
        float seedX = (generationSeed % 100000) * 0.001f;
        float seedY = ((generationSeed / 100000) % 100000) * 0.001f;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int idx = Index(x, y, width);
                Vector2 world = new Vector2(startPos.x + x, startPos.y + y);

                // 可选高度门控
                if (hasEnv)
                {
                    float h = layers.Hight[x, y];
                    if (h < minHeight || h > maxHeight)
                    {
                        river[idx] = false;
                        continue;
                    }
                }

                // 对非常小的区块（20x20）细河会“跳格断裂”，多点采样能显著缓解：
                // 只要河道穿过该格子的任意采样点，就算命中。
                float minEdge;
                if (useMultiSample)
                {
                    float inset = Mathf.Clamp(sampleInset, 0f, 0.49f);
                    float a = inset;
                    float b = 1f - inset;

                    // 5点采样：中心 + 四角（内缩）
                    minEdge = float.PositiveInfinity;
                    minEdge = Mathf.Min(minEdge, ComputeVoronoiEdgeValue(world + new Vector2(0.5f, 0.5f), cs, wf, wa, twA, twF, twNF, seedX, seedY, generationSeed));
                    minEdge = Mathf.Min(minEdge, ComputeVoronoiEdgeValue(world + new Vector2(a, a), cs, wf, wa, twA, twF, twNF, seedX, seedY, generationSeed));
                    minEdge = Mathf.Min(minEdge, ComputeVoronoiEdgeValue(world + new Vector2(b, a), cs, wf, wa, twA, twF, twNF, seedX, seedY, generationSeed));
                    minEdge = Mathf.Min(minEdge, ComputeVoronoiEdgeValue(world + new Vector2(a, b), cs, wf, wa, twA, twF, twNF, seedX, seedY, generationSeed));
                    minEdge = Mathf.Min(minEdge, ComputeVoronoiEdgeValue(world + new Vector2(b, b), cs, wf, wa, twA, twF, twNF, seedX, seedY, generationSeed));
                }
                else
                {
                    minEdge = ComputeVoronoiEdgeValue(world + new Vector2(0.5f, 0.5f), cs, wf, wa, twA, twF, twNF, seedX, seedY, generationSeed);
                }

                float ewLocal;
                if (ewMin == ewMax)
                {
                    ewLocal = ewMin;
                }
                else
                {
                    // 宽度随机变化：用噪声在 [ewMin, ewMax] 插值
                    float w = Mathf.PerlinNoise(world.x * twNF + seedX + 211.7f, world.y * twNF + seedY + 97.3f);

                    float widthPower = Mathf.Max(0.2f, useDepthPowerForWidth ? riverDepthPower : riverWidthPower);
                    float wShaped = Mathf.Pow(Mathf.Clamp01(w), widthPower);

                    ewLocal = Mathf.Lerp(ewMin, ewMax, wShaped);
                }

                river[idx] = minEdge <= ewLocal;
            }
        }
    }

    private static float ComputeVoronoiEdgeValue(
        Vector2 world,
        float cellSize,
        float warpFrequency,
        float warpAmplitude,
        float trigWaveAmplitude,
        float trigWaveFrequency,
        float trigWaveNoiseFrequency,
        float seedX,
        float seedY,
        int seed)
    {
        // Domain warp：让细胞边界更自然
        Vector2 p = world;
        if (warpAmplitude > 0f)
        {
            float wx = Mathf.PerlinNoise(world.x * warpFrequency + seedX, world.y * warpFrequency + seedY);
            float wy = Mathf.PerlinNoise(world.x * warpFrequency + seedX + 13.37f, world.y * warpFrequency + seedY + 9.17f);
            Vector2 warp = new Vector2((wx - 0.5f) * 2f, (wy - 0.5f) * 2f) * warpAmplitude;
            p += warp;
        }

        // 三角函数式波动（但相位由噪声扰动 -> 不会强周期）
        if (trigWaveAmplitude > 0f)
        {
            float phaseNoiseA = Mathf.PerlinNoise(world.x * trigWaveNoiseFrequency + seedX + 31.1f, world.y * trigWaveNoiseFrequency + seedY + 17.7f);
            float phaseNoiseB = Mathf.PerlinNoise(world.x * trigWaveNoiseFrequency + seedX + 71.3f, world.y * trigWaveNoiseFrequency + seedY + 53.9f);
            float phaseA = phaseNoiseA * Mathf.PI * 2f;
            float phaseB = phaseNoiseB * Mathf.PI * 2f;

            float sx = Mathf.Sin((p.y * trigWaveFrequency) + phaseA);
            float sy = Mathf.Cos((p.x * trigWaveFrequency * 0.87f) + phaseB);

            float ampNoise = Mathf.PerlinNoise(world.x * trigWaveNoiseFrequency * 0.7f + seedX + 101.9f, world.y * trigWaveNoiseFrequency * 0.7f + seedY + 7.3f);
            float amp = (0.35f + ampNoise * 0.65f) * trigWaveAmplitude;
            p += new Vector2(sx, sy) * amp;
        }

        int cx = Mathf.FloorToInt(p.x / cellSize);
        int cy = Mathf.FloorToInt(p.y / cellSize);

        float best1 = float.PositiveInfinity;
        float best2 = float.PositiveInfinity;

        // 只检查邻近 3x3 cell 的 feature point
        for (int ox = -1; ox <= 1; ox++)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                int fxCell = cx + ox;
                int fyCell = cy + oy;

                Vector2 fp = GetFeaturePoint(fxCell, fyCell, cellSize, seed);
                float dx = p.x - fp.x;
                float dy = p.y - fp.y;
                float d2 = dx * dx + dy * dy;

                if (d2 < best1)
                {
                    best2 = best1;
                    best1 = d2;
                }
                else if (d2 < best2)
                {
                    best2 = d2;
                }
            }
        }

        float f1 = Mathf.Sqrt(best1);
        float f2 = Mathf.Sqrt(best2);
        return f2 - f1;
    }

    private static Vector2 GetFeaturePoint(int cellX, int cellY, float cellSize, int seed)
    {
        float rx = Hash01(cellX, cellY, seed);
        int salt = unchecked((int)0x9E3779B9);
        float ry = Hash01(cellX, cellY, seed ^ salt);
        return new Vector2((cellX + rx) * cellSize, (cellY + ry) * cellSize);
    }

    private static float Hash01(int x, int y, int seed)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)seed) * 16777619u;
            h = (h ^ (uint)x) * 16777619u;
            h = (h ^ (uint)y) * 16777619u;
            h ^= h >> 16;
            h *= 2246822519u;
            h ^= h >> 13;
            h *= 3266489917u;
            h ^= h >> 16;
            return (h & 0x00FFFFFFu) / 16777216f;
        }
    }

    private int GetGenerationSeed()
    {
        unchecked
        {
            uint mixed = 2166136261u;
            mixed = (mixed ^ (uint)activeWorldSeed) * 16777619u;
            mixed = (mixed ^ (uint)seed) * 16777619u;
            return mixed == 0u ? 1 : (int)mixed;
        }
    }

    private void ImproveConnectivity(int width, int height, bool[] src, bool[] dst)
    {
        // dst 由 src 推导：
        // - 已经是河流的保持
        // - 非河流但河流邻居数达到阈值 -> 补成河流
        Array.Copy(src, dst, src.Length);
        int threshold = Mathf.Clamp(connectNeighborThreshold, 1, 4);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int idx = Index(x, y, width);
                if (src[idx])
                    continue;

                int neighbors = CountRiverNeighbors4(x, y, width, height, src);
                if (neighbors >= threshold)
                {
                    dst[idx] = true;
                }
            }
        }
    }

    private void RemoveIsolatedCells(int width, int height, bool[] river)
    {
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int idx = Index(x, y, width);
                if (!river[idx])
                    continue;

                if (CountRiverNeighbors4(x, y, width, height, river) == 0)
                {
                    river[idx] = false;
                }
            }
        }
    }

    private void BridgeOneCellGaps(int width, int height, bool[] src, bool[] dst)
    {
        // dst 由 src 推导：
        // - 已经是河流的保持
        // - 非河流但左右同时是河（或上下同时是河） -> 补成河
        Array.Copy(src, dst, src.Length);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int idx = Index(x, y, width);
                if (src[idx])
                    continue;

                bool lr = (x - 1 >= 0) && (x + 1 < width) && src[Index(x - 1, y, width)] && src[Index(x + 1, y, width)];
                bool ud = (y - 1 >= 0) && (y + 1 < height) && src[Index(x, y - 1, width)] && src[Index(x, y + 1, width)];

                if (lr || ud)
                {
                    dst[idx] = true;
                }
            }
        }
    }

    private int CountRiverNeighbors4(int x, int y, int width, int height, bool[] river)
    {
        int count = 0;
        if (x + 1 < width && river[Index(x + 1, y, width)]) count++;
        if (x - 1 >= 0 && river[Index(x - 1, y, width)]) count++;
        if (y + 1 < height && river[Index(x, y + 1, width)]) count++;
        if (y - 1 >= 0 && river[Index(x, y - 1, width)]) count++;
        return count;
    }

    private static int Index(int x, int y, int width) => y * width + x;
    #endregion

    #region 石头生成
    private void SpawnStones_ForRiver(int width, int height, Vector2Int startPos, bool[] river)
    {
        if (Prefab_Stone == null)
        {
            if (!_hasLoggedMissingStonePrefab)
            {
                _hasLoggedMissingStonePrefab = true;
                Debug.LogError("[ChunkGenerator_River] ❌ Prefab_Stone 为空：已开启 spawnRiverStones 但无法生成石头", Map);
            }
            return;
        }

        if (maxStonesPerChunk <= 0)
        {
            Debug.LogWarning($"[ChunkGenerator_River] ⚠️ maxStonesPerChunk({maxStonesPerChunk}) <= 0，已跳过石头生成", Map);
            return;
        }

        // 组织父物体
        Transform parent = stoneParent != null ? stoneParent : (Map != null ? Map.transform : null);
        if (parent == null)
        {
            Debug.LogError("[ChunkGenerator_River] ❌ 无法确定石头父物体（stoneParent 与 Map.transform 均为空）", Map);
            return;
        }

        string rootName = $"{stoneRootNamePrefix}_{startPos.x}_{startPos.y}";

        Transform root = parent.Find(rootName);
        if (root != null && clearPreviousStones)
        {
            // Generate 可能在编辑器按钮触发：DestroyImmediate 更安全
            if (Application.isPlaying)
                GameObject.Destroy(root.gameObject);
            else
                GameObject.DestroyImmediate(root.gameObject);

            root = null;
        }

        if (root == null)
        {
            var go = new GameObject(rootName);
            go.transform.SetParent(parent, false);
            root = go.transform;
        }

        int placed = 0;
        int saltRiver = unchecked((int)0x6D2B79F5);
        int saltBank = unchecked((int)0x1B873593);
        int saltFlintBank = unchecked((int)0x3C6EF372);
        int generationSeed = GetGenerationSeed();

        float riverChance = Mathf.Clamp01(riverStoneChance);
        float bankChance = Mathf.Clamp01(bankStoneChance);
        float bankFlintChance01 = Mathf.Clamp01(bankFlintChance);
        int radius = Mathf.Max(1, bankRadius);

        // 以 TileData 为准：salt==0 代表河流水；salt==80 代表海水
        bool[] riverWater = new bool[width * height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2Int worldPos = new Vector2Int(startPos.x + x, startPos.y + y);
                TileData top = Map.GetTopTile(worldPos);
                if (top is not TileData_Water water)
                    continue;

                if (IsSeaWater(water))
                    continue;

                if (Mathf.Approximately(water.salt, 0f))
                {
                    riverWater[Index(x, y, width)] = true;
                }
            }
        }

        // 先放河床石头（更符合“河底”）
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (placed >= maxStonesPerChunk)
                    break;

                if (!riverWater[Index(x, y, width)])
                    continue;

                Vector2Int worldPos = new Vector2Int(startPos.x + x, startPos.y + y);
                float r01 = Hash01(worldPos.x, worldPos.y, generationSeed ^ saltRiver);
                if (r01 > riverChance)
                    continue;

                PlaceOnePickup(Prefab_Stone, worldPos, root, generationSeed ^ saltRiver);
                placed++;
            }
        }

        // 再放河岸石头（河两侧）
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (placed >= maxStonesPerChunk)
                    break;

                int idx = Index(x, y, width);
                if (riverWater[idx])
                    continue;

                Vector2Int worldPos = new Vector2Int(startPos.x + x, startPos.y + y);
                if (IsSeaWaterAt(worldPos))
                    continue;

                if (!IsBankCell(x, y, width, height, riverWater, radius))
                    continue;

                float r01 = Hash01(worldPos.x, worldPos.y, generationSeed ^ saltBank);
                if (r01 <= bankChance)
                {
                    PlaceOnePickup(Prefab_Stone, worldPos, root, generationSeed ^ saltBank);
                    placed++;
                    continue;
                }

                if (Prefab_Flint == null)
                    continue;

                float flintR01 = Hash01(worldPos.x, worldPos.y, generationSeed ^ saltFlintBank);
                if (flintR01 > bankFlintChance01)
                    continue;

                PlaceOnePickup(Prefab_Flint, worldPos, root, generationSeed ^ saltFlintBank);
                placed++;
            }
        }

//        Debug.Log($"[ChunkGenerator_River] ✅ 河床/河岸石头生成完成，数量: {placed}（上限 {maxStonesPerChunk}）", Map);
    }

    private bool IsBankCell(int x, int y, int width, int height, bool[] riverWater, int radius)
    {
        // 以 (x,y) 为中心检查周围 radius 圈，只要邻居存在河流就认为是河岸
        for (int dx = -radius; dx <= radius; dx++)
        {
            int nx = x + dx;
            if (nx < 0 || nx >= width)
                continue;

            for (int dy = -radius; dy <= radius; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= height)
                    continue;

                if (dx == 0 && dy == 0)
                    continue;

                if (riverWater[Index(nx, ny, width)])
                    return true;
            }
        }
        return false;
    }

    private static bool IsSeaWater(TileData_Water water)
    {
        return Mathf.Abs(water.salt - SeaSalt) <= 0.01f;
    }

    private bool IsSeaWaterAt(Vector2Int worldPos)
    {
        TileData top = Map.GetTopTile(worldPos);
        return top is TileData_Water water && IsSeaWater(water);
    }

    private void PlaceOnePickup(GameObject prefab, Vector2Int worldPos, Transform parent, int seedSalt)
    {
        if (prefab == null)
            throw new ArgumentNullException(nameof(prefab));

        Vector3Int cell = new Vector3Int(worldPos.x, worldPos.y, 0);
        Vector3 centerWorld;
        if (targetTilemap != null)
            centerWorld = targetTilemap.GetCellCenterWorld(cell);
        else
            centerWorld = new Vector3(worldPos.x + 0.5f, worldPos.y + 0.5f, 0f);

        float ox = (Hash01(worldPos.x, worldPos.y, seedSalt ^ 101) - 0.5f) * 2f * Mathf.Abs(stoneOffsetRange.x);
        float oy = (Hash01(worldPos.x, worldPos.y, seedSalt ^ 202) - 0.5f) * 2f * Mathf.Abs(stoneOffsetRange.y);
        Vector3 pos = centerWorld + new Vector3(ox, oy, 0f);

        float rz01 = Hash01(worldPos.x, worldPos.y, seedSalt ^ 303);
        float rz = Mathf.Lerp(stoneRotationZRange.x, stoneRotationZRange.y, rz01);
        Quaternion rot = Quaternion.Euler(0f, 0f, rz);

        float s01 = Hash01(worldPos.x, worldPos.y, seedSalt ^ 404);
        float s = Mathf.Lerp(stoneUniformScaleRange.x, stoneUniformScaleRange.y, s01);

        GameObject go = GameObject.Instantiate(prefab, pos, rot, parent);
        go.transform.localScale = go.transform.localScale * s;
    }
    #endregion

    #region 写入
    private void ApplyRiverAt(Vector2Int worldPos, int localX, int localY, int width, int height, bool[] riverMask)
    {
        WriteFreshWaterAt(
            worldPos,
            ComputeRiverDepth(localX, localY, width, height, riverMask),
            renderImmediately: true);
    }

    private void WriteFreshWaterAt(Vector2Int worldPos, float depth, bool renderImmediately)
    {
        if (IsSeaWaterAt(worldPos))
            return;

        TileData riverTile = riverTileBlock.tileDataTemplate.Clone();
        if (riverTile is not TileData_Water waterTile)
        {
            Debug.LogError($"[ChunkGenerator_River] riverTileBlock({riverTileBlock.name}) 无法生成淡水 TileData", Map);
            return;
        }

        riverTile.position = new Vector3Int(worldPos.x, worldPos.y, 0);
        Vector2Int localPos = worldPos - Map.Data.position;
        EnvironmentLayers layers = Map.Data.EnvironmentLayers;
        if (layers != null && layers.Contains(localPos.x, localPos.y))
        {
            Map.Data.SetHumidityAtLocal(localPos.x, localPos.y, 1f);
            Map.Data.SetSolidityAtLocal(localPos.x, localPos.y, 0f);
            riverTile.Initialize_Env(layers, localPos.x, localPos.y);
        }

        waterTile.salt = 0f;
        waterTile.deepValue = Mathf.Clamp01(depth);

        List<TileData> list = Map.Data.GetTileListAt(worldPos);
        if (list == null || list.Count == 0)
        {
            Map.Data.AddTileData(worldPos, riverTile);
        }
        else if (writeMode == RiverWriteMode.AddLayer &&
                 list[list.Count - 1] is not TileData_Water existingWater)
        {
            list.Add(riverTile);
        }
        else
        {
            list[list.Count - 1] = riverTile;
        }

        if (!renderImmediately || targetTilemap == null)
            return;

        TileBase unityTileBase = riverTileBlock.GetTileBaseAsset();
        if (unityTileBase != null)
            targetTilemap.SetTile(new Vector3Int(worldPos.x, worldPos.y, 0), unityTileBase);
    }

    private float ComputeRiverDepth(int x, int y, int width, int height, bool[] riverMask)
    {
        int riverCells = 0;
        int sampled = 0;

        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = x + dx;
            if (nx < 0 || nx >= width)
                continue;

            for (int dy = -1; dy <= 1; dy++)
            {
                int ny = y + dy;
                if (ny < 0 || ny >= height)
                    continue;

                sampled++;
                if (riverMask[Index(nx, ny, width)])
                    riverCells++;
            }
        }

        float strength = sampled > 0 ? (float)riverCells / sampled : 0f;
        float t = Mathf.Pow(Mathf.Clamp01(strength), Mathf.Max(0.2f, riverDepthPower));

        float minDepth = Mathf.Clamp01(riverDepthMin);
        float maxDepth = Mathf.Clamp01(riverDepthMax);
        if (minDepth > maxDepth)
        {
            float tmp = minDepth;
            minDepth = maxDepth;
            maxDepth = tmp;
        }

        return Mathf.Lerp(minDepth, maxDepth, t);
    }
    #endregion
}
