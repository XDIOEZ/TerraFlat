using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 河流生成器：
/// - 作为 Map.mapGenerators 管线中的一个步骤执行（通常放在大陆生成器之后）
/// - 遮罩（网络状噪声 / Voronoi 边界）：
///   1) 用 Worley/Voronoi 的“细胞边界”生成天然分叉的网络（更接近树状/蜘蛛网状河网）
///   2) 可选：若存在 EnvironmentData，则用 Hight 做高度门控
///   3) 轻量后处理（补洞/去孤点）提升连通性
/// - 最后用 riverTileBlock 覆盖地表（TileData + Tilemap）
/// </summary>
[Serializable]
public class ChunkGenerator_River : ChunkGeneratorBase
{
    private const float SeaSalt = 80f;

    #region 配置参数
    [Header("Tilemap")]
    [Tooltip("不填则使用 Map.tileMap")]
    public Tilemap targetTilemap;

    [Header("河流 Tile")]
    [Tooltip("河流/水面用的 Tile_Block（会用它的 tileDataTemplate 克隆运行时 TileData，并用 TileBase 覆盖到 Tilemap）")]
    public Tile_Block riverTileBlock;

    [Header("遮罩（网络状噪声 / Voronoi 边界）")]
    [Tooltip("噪声种子（用于让不同星球/地图拥有不同河网分布）")]
    public int seed = 12345;

    [Tooltip("Voronoi 单元大小（世界格子单位）。越大河网越稀疏；越小河网越密")]
    public float cellSize = 18f;

    [Tooltip("边界厚度（世界格子单位）。越大河越宽/越容易出现")]
    public float edgeWidth = 1.2f;

    [Tooltip("河流宽度范围（用于让 edgeWidth 在地图上随机变化）。x=最细，y=最粗；如果 x==y 则为固定宽度")]
    public Vector2 edgeWidthRange = new Vector2(1.2f, 1.2f);

    [Header("抗断裂（细河推荐）")]
    [Tooltip("多点采样：用中心+四角采样来判断“河道是否穿过格子”。细河时能明显减少断开")]
    public bool useMultiSample = true;

    [Tooltip("采样内缩（0~0.49）。越大越靠近中心（更保守、更细）；越小越靠近格子边缘（更连续）")]
    public float sampleInset = 0.18f;

    [Header("形变（可选）")]
    [Tooltip("对采样坐标做 Domain Warp 的幅度（世界格子单位）。0=更规则的细胞边界；越大越自然")]
    public float warpAmplitude = 4f;

    [Tooltip("Domain Warp 频率（越大变化越快）")]
    public float warpFrequency = 0.025f;

    [Header("波动（类似三角函数）")]
    [Tooltip("在 Voronoi 边界上叠加类似 sin/cos 的波动幅度（世界格子单位）。0=关闭")]
    public float trigWaveAmplitude = 2.0f;

    [Tooltip("三角波动频率（越大波动越密）")]
    public float trigWaveFrequency = 0.10f;

    [Tooltip("三角波动的相位扰动噪声频率（让波动不那么“严格周期”）")]
    public float trigWaveNoiseFrequency = 0.035f;

    [Header("连通性后处理（轻量，推荐开）")]
    [Tooltip("补洞迭代次数：每次把“周围河流邻居数>=阈值”的格子补成河流。0=不补")]
    public int connectPasses = 2;

    [Tooltip("补洞邻居阈值：2=比较容易连起来；3=更保守")]
    public int connectNeighborThreshold = 2;

    [Tooltip("是否去掉孤立河点（四邻域没有任何河流邻居的河点）")]
    public bool removeIsolated = true;

    [Header("断点桥接（细河建议开）")]
    [Tooltip("是否桥接 1 格断点：当某格左右都是河（或上下都是河）时，把中间空格补成河。基本不增粗但能减少断裂")]
    public bool bridgeGaps = true;

    [Tooltip("桥接迭代次数：1 通常足够；想更强一点可设 2")]
    public int bridgePasses = 1;

    [Header("高度限制(可选)")]
    [Tooltip("仅在有 EnvironmentData 时生效：Hight 低于该值则不生成河流")]
    public float minHeight = 0.20f;

    [Tooltip("仅在有 EnvironmentData 时生效：Hight 高于该值则不生成河流")]
    public float maxHeight = 0.95f;

    [Header("写入模式")]
    [Tooltip("ReplaceTop：替换最顶层 TileData\nAddLayer：在顶部增加一层河流 TileData")]
    public RiverWriteMode writeMode = RiverWriteMode.ReplaceTop;

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
        Map.Data.EnsureTileDataArray((int)chunkSize.x, (int)chunkSize.y, initCells: true);

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
                ApplyRiverAt(worldPos);
                appliedCount++;
            }
        }

        // 4) 生成河床/河岸石头
        if (spawnRiverStones)
        {
            SpawnStones_ForRiver(width, height, startPos, river);
        }

        Debug.Log($"[ChunkGenerator_River] ✅ 河流遮罩生成完成（网络噪声/Voronoi边界），覆盖格子数: {appliedCount}", Map);
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

        bool hasEnv = Map != null && Map.Data != null && Map.Data.EnvironmentData != null;
        if (!hasEnv && !_hasLoggedEnvMissing)
        {
            _hasLoggedEnvMissing = true;
            Debug.LogWarning("[ChunkGenerator_River] ⚠️ Map.Data.EnvironmentData 为空：将跳过 minHeight/maxHeight 高度门控（河网遮罩仍可生成）", Map);
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

        // PerlinNoise 输入不要太大：用 seed 做偏移
        float seedX = (seed % 100000) * 0.001f;
        float seedY = ((seed / 100000) % 100000) * 0.001f;

        var envGrid = hasEnv ? Map.Data.EnvironmentData : null;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int idx = Index(x, y, width);
                Vector2 world = new Vector2(startPos.x + x, startPos.y + y);

                // 可选高度门控
                if (hasEnv)
                {
                    float h = envGrid[x, y].Hight;
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
                    minEdge = Mathf.Min(minEdge, ComputeVoronoiEdgeValue(world + new Vector2(0.5f, 0.5f), cs, wf, wa, twA, twF, twNF, seedX, seedY, seed));
                    minEdge = Mathf.Min(minEdge, ComputeVoronoiEdgeValue(world + new Vector2(a, a), cs, wf, wa, twA, twF, twNF, seedX, seedY, seed));
                    minEdge = Mathf.Min(minEdge, ComputeVoronoiEdgeValue(world + new Vector2(b, a), cs, wf, wa, twA, twF, twNF, seedX, seedY, seed));
                    minEdge = Mathf.Min(minEdge, ComputeVoronoiEdgeValue(world + new Vector2(a, b), cs, wf, wa, twA, twF, twNF, seedX, seedY, seed));
                    minEdge = Mathf.Min(minEdge, ComputeVoronoiEdgeValue(world + new Vector2(b, b), cs, wf, wa, twA, twF, twNF, seedX, seedY, seed));
                }
                else
                {
                    minEdge = ComputeVoronoiEdgeValue(world + new Vector2(0.5f, 0.5f), cs, wf, wa, twA, twF, twNF, seedX, seedY, seed);
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
                    ewLocal = Mathf.Lerp(ewMin, ewMax, w);
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

        float riverChance = Mathf.Clamp01(riverStoneChance);
        float bankChance = Mathf.Clamp01(bankStoneChance);
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
                float r01 = Hash01(worldPos.x, worldPos.y, seed ^ saltRiver);
                if (r01 > riverChance)
                    continue;

                PlaceOneStone(worldPos, root, seed ^ saltRiver);
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

                float r01 = Hash01(worldPos.x, worldPos.y, seed ^ saltBank);
                if (r01 > bankChance)
                    continue;

                PlaceOneStone(worldPos, root, seed ^ saltBank);
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

    private void PlaceOneStone(Vector2Int worldPos, Transform parent, int seedSalt)
    {
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

        GameObject go = GameObject.Instantiate(Prefab_Stone, pos, rot, parent);
        go.transform.localScale = go.transform.localScale * s;
    }
    #endregion

    #region 写入
    private void ApplyRiverAt(Vector2Int worldPos)
    {
        // 海水（salt=80）不覆盖：避免河流/石头刷进海里
        if (IsSeaWaterAt(worldPos))
            return;

        // 1) 克隆 TileData
        TileData riverTile = riverTileBlock.tileDataTemplate.Clone();
       
        if (riverTile is not TileData_Water waterTile)
        {
            Debug.LogError($"[ChunkGenerator_River] ❌ riverTileBlock({riverTileBlock.name}) 生成的 TileData 不是 TileData_Water，无法写入 salt=0 的河流", Map);
            return;
        }

        waterTile.salt = 0f;

        // 2) 设置位置
        riverTile.position = new Vector3Int(worldPos.x, worldPos.y, 0);

        // 3) 环境初始化（如果有）
        if (Map != null && Map.Data != null)
        {
            if (Map.Data.EnvironmentData != null)
            {
                Vector2Int localPos = worldPos - Map.Data.position;
                if (localPos.x >= 0 && localPos.y >= 0 &&
                    localPos.x < Map.Data.EnvironmentData.GetLength(0) &&
                    localPos.y < Map.Data.EnvironmentData.GetLength(1))
                {
                    var env = Map.Data.EnvironmentData[localPos.x, localPos.y];

                    // 河流覆盖：同步修改该格环境参数
                    env.Humidity = 1f;
                    env.Solidity = 0f;
                    Map.Data.EnvironmentData[localPos.x, localPos.y] = env;

                    // 用更新后的 env 初始化 TileData
                    riverTile.Initialize_Env(env);
                    waterTile.deepValue += 0.5f; // 河流更深一些
                }
            }
            else
            {
                if (!_hasLoggedEnvMissing)
                {
                    _hasLoggedEnvMissing = true;
                    Debug.LogWarning("[ChunkGenerator_River] ⚠️ Map.Data.EnvironmentData 为空：无法把河流格子的湿度设为1、固体设为0（仍会生成河流 Tile）", Map);
                }
            }
        }

        // 4) 写入数据层：替换顶层 or 加层
        var list = Map.Data.GetTileListAt(worldPos);
        if (list == null)
        {
            Debug.LogError($"[ChunkGenerator_River] ❌ TileData_Array 未初始化或越界：worldPos={worldPos} mapPos={Map.Data.position}", Map);
            return;
        }

        if (writeMode == RiverWriteMode.AddLayer)
        {
            list.Add(riverTile);
        }
        else
        {
            if (list.Count == 0)
                list.Add(riverTile);
            else
                list[list.Count - 1] = riverTile;
        }


        // 5) 写入视觉层
        TileBase unityTileBase = riverTileBlock.GetTileBaseAsset();
        if (unityTileBase == null)
        {
            Debug.LogError($"[ChunkGenerator_River] ❌ riverTileBlock({riverTileBlock.name}) 的 TileBase 为空，无法绘制", Map);
            return;
        }

        targetTilemap.SetTile(new Vector3Int(worldPos.x, worldPos.y, 0), unityTileBase);
    }
    #endregion
}
