using Force.DeepCloner;
using NavMeshPlus.Components;
using NPOI.SS.Formula.Functions;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using System;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public class Map : Item
{
    #region 属性和字段
    [Header("地图配置")]
    [SerializeField]
    public Data_TileMap Data = new Data_TileMap();

    [Header("Tilemap 组件")]
    [SerializeField]
    public Tilemap tileMap;

    public UltEvent OnMapGenerated_Start = new UltEvent();
    public Chunk chunk;

    public GameObject ParentObject;

    // 协程引用管理，避免协程叠加
    public Coroutine loadTileMapCoroutine;
    public Coroutine backTilePenaltyCoroutine;
    public Coroutine loadOrGenerateCoroutine;

    [Header("寻路权重烘焙配置")]
    [SerializeField, Min(1)]
    private int backTilePenaltyTilesPerYield = 200;

    [SerializeField, Min(0.01f)]
    private float backTilePenaltyMinInterval = 0.08f;

    [SerializeField, Min(0f)]
    private float backTilePenaltyYieldSeconds = 0f;

    [Header("Tilemap加载分帧配置")]
    [SerializeField, Min(16)]
    private int loadTileBatchSize = 256;

    private float backTilePenaltyWaitSeconds = -1f;
    private WaitForSeconds backTilePenaltyWait;
    private float lastBackTilePenaltyTime = -999f;
    private bool backTilePenaltyPending;
    private bool backTilePenaltyForceFull = true;
    private readonly HashSet<Vector2Int> backTilePenaltyDirtyCells = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> backTilePenaltyDirtySnapshot = new List<Vector2Int>(128);

    [SerializeReference]
    public List<ChunkGeneratorBase> mapGenerators = new List<ChunkGeneratorBase>();

    /// <summary>
    /// 兼容调试/显示：通常 0 号位放 ChunkGenerator_Land。
    /// </summary>
    public ChunkGenerator_Land LandGenerator => GetGenerator<ChunkGenerator_Land>();

    private bool isMapGeneratorHooked;

    private void Awake()
    {
        InitMapGenerators();
    }

    private object GetBackTilePenaltyYieldToken()
    {
        float seconds = backTilePenaltyYieldSeconds;
        if (seconds <= 0f)
            return null;

        if (backTilePenaltyWait == null || !Mathf.Approximately(backTilePenaltyWaitSeconds, seconds))
        {
            backTilePenaltyWaitSeconds = seconds;
            backTilePenaltyWait = new WaitForSeconds(seconds);
        }

        return backTilePenaltyWait;
    }

    /// <summary>
    /// 初始化随机地图生成器：
    /// - 确保有实例（可在 Inspector 中直接配置字段）
    /// - 绑定当前 Map / Item 引用
    /// - 订阅 OnMapGenerated_Start 事件
    /// </summary>
    private void InitMapGenerators()
    {
        for (int i = 0; i < mapGenerators.Count; i++)
        {
            var gen = mapGenerators[i];
            if (gen == null)
            {
                Debug.LogError($"[Map.InitMapGenerators] ❌ mapGenerators[{i}] 为空（SerializeReference 丢失/未实例化？）", this);
                continue;
            }

            gen.Init(this);
        }

        // 由 Map 负责把事件桥接到生成器（生成器不再持有 Map 引用）
        if (!isMapGeneratorHooked)
        {
            OnMapGenerated_Start += HandleMapGeneratedStart;
            isMapGeneratorHooked = true;
        }
    }

    /// <summary>
    /// 获取第一个指定类型的生成器（常用于调试/显示脚本取 LandGenerator）。
    /// </summary>
    public T GetGenerator<T>() where T : ChunkGeneratorBase
    {
        if (mapGenerators == null)
            return null;

        for (int i = 0; i < mapGenerators.Count; i++)
        {
            if (mapGenerators[i] is T typed)
                return typed;
        }

        return null;
    }

    private void HandleMapGeneratedStart()
    {
        var planetData = SaveDataMgr.Instance != null ? SaveDataMgr.Instance.GetCurrentPlanetData() : null;
        GenerateByPipeline(planetData);
    }

    /// <summary>
    /// 按列表顺序执行所有生成器：0号位（大陆）→ 1号位（河流）→ ...
    /// </summary>
    private void GenerateByPipeline(PlanetData planetData)
    {
        InitMapGenerators();

        if (mapGenerators == null || mapGenerators.Count == 0)
        {
            Debug.LogError("[Map.GenerateByPipeline] ❌ mapGenerators 为空，无法生成", this);
            return;
        }

        if (Data == null)
        {
            Debug.LogWarning("[Map.GenerateByPipeline] ⚠️ Data 为空，已自动创建 Data_TileMap", this);
            Data = new Data_TileMap();
        }

        // 确保数组已初始化（数组为主存储）
        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        Data.EnsureTileDataArray((int)chunkSize.x, (int)chunkSize.y, initCells: true);

        // 开始生成：先标记为未完成
        Data.TileLoaded = false;

        var context = new MapGenerationContext(this, planetData);

        for (int i = 0; i < mapGenerators.Count; i++)
        {
            var gen = mapGenerators[i];
            if (gen == null)
            {
                Debug.LogError($"[Map.GenerateByPipeline] ❌ mapGenerators[{i}] 为空，已跳过", this);
                continue;
            }

            try
            {
                gen.Init(this);
                gen.Generate(context);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Map.GenerateByPipeline] ❌ 执行生成器[{i}]({gen.GetType().Name})异常：{ex}", this);
            }
        }

        // 统一收尾：保证“迭代式生成”全部完成后再标记 TileLoaded
        if (tileMap != null)
        {
            tileMap.RefreshAllTiles();
        }

        Data.TileLoaded = true;
        MarkPenaltyDirtyFull();
        BackTilePenalty_Async();
    }

    private void OnGUI()
    {
        // Map 自身不再负责调试 GUI，相关调试由 EnvironmentInfoDisplay 处理
    }

    // 强制类型转换属性（保持与基类 Item 的兼容）
    public override ItemData itemData { get => Data; set => Data = value as Data_TileMap; }
    #endregion

    #region 基类方法实现
    public override void Act()
    {
        if (Data.TileLoaded == false
            && SaveDataMgr.Instance.SaveData.PlanetData_Dict.TryGetValue(SceneManager.GetActiveScene().name, out var planetData))
        {
            OnMapGenerated_Start.Invoke();
        }
    }
    #endregion

    #region 保存和加载


    [Button("从数据加载地图")]
    public override void Load()
    {
        base.Load();

        chunk = GetComponentInParent<Chunk>();
        if (chunk != null)
        {
            chunk.Map = this;
        }

        // 确保生成器列表已初始化并绑定当前 Map
        InitMapGenerators();

        // 确保 tileMap 引用有效（支持不在 Inspector 里手动拖拽）
        if (tileMap == null)
        {
            // 优先从“大陆生成器”拿 targetTilemap（兼容旧逻辑）
            tileMap = LandGenerator != null ? LandGenerator.targetTilemap : null;
            if (tileMap == null)
            {
                tileMap = GetComponentInChildren<Tilemap>(includeInactive: false);
            }
        }

        if (tileMap == null)
        {
            Debug.LogError("[Map.Load] tileMap 为空，无法加载/生成地图", this);
            return;
        }

        // TODO 1：检查 Data，如果不存在或为空，则按种子 + 噪声生成 TileData
        if (Data == null)
        {
            Data = new Data_TileMap();
        }

        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        Data.EnsureTileDataArray((int)chunkSize.x, (int)chunkSize.y, initCells: true);
        Data.EnsureEnvironmentStorage((int)chunkSize.x, (int)chunkSize.y);

        bool hasTileData = Data.CountNonEmptyCells() > 0;

        // 先停止上一轮加载/生成流程（避免多次点击按钮叠加协程）
        if (loadOrGenerateCoroutine != null)
        {
            StopCoroutine(loadOrGenerateCoroutine);
            loadOrGenerateCoroutine = null;
        }

        if (!hasTileData)
        {
            // 生成数据（可能是分帧生成），生成完成后再把数据刷到 Tilemap
            Data.TileLoaded = false;
            loadOrGenerateCoroutine = StartCoroutine(GenerateThenLoadTilemapCoroutine());
            return;
        }

        // TODO 2：直接加载 Data 到 TileMap 上
        tileMap.ClearAllTiles();
        LoadTileData_To_TileMap_Ansync();
        Data.TileLoaded = true;
    }


    private IEnumerator GenerateThenLoadTilemapCoroutine()
    {
        // 触发生成器（生成器内部会根据 tilesPerFrame 选择立即/分帧生成）
        OnMapGenerated_Start.Invoke();

        // 等待生成完成：既要有 TileData，也要等 TileLoaded 被标记为 true
        // （TileLoaded 在 Map.GenerateByPipeline() 统一收尾中设置）
        while (Data == null || Data.TileLoaded == false)
        {
            yield return null;
        }

        // 将生成的数据渲染到 Tilemap
        tileMap.ClearAllTiles();
        LoadTileData_To_TileMap_Ansync();

        loadOrGenerateCoroutine = null;
    }


    //不需要保存数据 因为游戏中的所有对地图的行为 直接影响背后数据
    [Button("保存地图到数据")]
    public override void Save()
    {
        // 只有 tileMapData 为空或其 TileData 为空时才初始化数据
        if (Data == null || Data.CountNonEmptyCells() == 0)
        {
            SaveTileMap_TO_TileData();
        }
        base.Save();
    }
    #endregion

    #region TileMap加载方法
    public void LoadTileData_To_TileMap_Sync()
    {
        if (Data == null || Data.CountNonEmptyCells() == 0)
        {
            Debug.LogWarning("TileData is empty. Nothing to load.");
            return;
        }

        foreach (var (worldPos, tileDataList) in Data.EnumerateNonEmptyTiles())
        {
            // 获取最顶层 TileData（倒数第一个）
            TileData topTile = tileDataList[^1];

            TileBase tile = GameRes.Instance.GetTileBase(topTile.ID);
            if (tile == null)
            {
                Debug.LogError($"无法加载 Tile: {topTile.ID}");
                continue;
            }

            Vector3Int position3D = new Vector3Int(worldPos.x, worldPos.y, 0);

            tileMap.SetTile(position3D, tile);
        }

        // 直接调用权重烘焙，不延迟
        MarkPenaltyDirtyFull();
        BackTilePenalty_Async();
    }

    public void LoadTileData_To_TileMap_Ansync()
    {
        // 如果已有协程在运行，先停止它
        if (loadTileMapCoroutine != null)
        {
            StopCoroutine(loadTileMapCoroutine);
        }

        // 启动新的协程
            loadTileMapCoroutine = StartCoroutine(LoadTileData_To_TileMapCoroutine());
    }

    /// <summary>
    /// 使用协程优化的加载Tile数据到Tilemap的方法
    /// </summary>
    private IEnumerator LoadTileData_To_TileMapCoroutine()
    {
        if (Data == null || Data.CountNonEmptyCells() == 0)
        {
            Debug.LogWarning("TileData is empty. Nothing to load.");
            loadTileMapCoroutine = null;
            yield break;
        }

        // 分批处理Tile数据，避免长时间阻塞主线程
        int batchSize = Mathf.Max(16, loadTileBatchSize);
        int processedCount = 0;

        foreach (var (worldPos, tileDataList) in Data.EnumerateNonEmptyTiles())
        {
            // 获取最顶层 TileData（倒数第一个）
            TileData topTile = tileDataList[^1];

            TileBase tile = GameRes.Instance.GetTileBase(topTile.ID);
            if (tile == null)
            {
                Debug.LogError($"无法加载 Tile: {topTile.ID}");
                continue;
            }

            Vector3Int position3D = new Vector3Int(worldPos.x, worldPos.y, 0);

            tileMap.SetTile(position3D, tile);

            processedCount++;

            // 每处理一批就等待一帧，让出控制权给其他任务
            if (processedCount % batchSize == 0)
            {
                yield return null;
            }
        }

        // 等待一帧确保所有Tile设置完成
        yield return null;

        // 使用异步权重烘焙，避免同帧尖峰
        MarkPenaltyDirtyFull();
        BackTilePenalty_Async();

        Debug.Log($"✅ 完成加载 {processedCount} 个Tile到Tilemap");

        // 清理协程引用
        loadTileMapCoroutine = null;
    }
    #endregion

    #region 寻路权重烘焙方法
    [Button("异步烘焙地块寻路权重")]
    public void BackTilePenalty_Async()
    {
        // 检查自身是否处于激活状态
        if (!gameObject.activeInHierarchy || !enabled)
        {
            Debug.Log("地图未激活，跳过权重烘焙");
            return;
        }

        float now = Time.unscaledTime;
        if (now - lastBackTilePenaltyTime < backTilePenaltyMinInterval)
        {
            backTilePenaltyPending = true;
            return;
        }

        // 非全量且无脏区时，跳过空烘焙
        if (!backTilePenaltyForceFull && backTilePenaltyDirtyCells.Count == 0)
        {
            return;
        }

        // 已有协程在跑时，仅标记一次补跑，避免频繁Stop/Start
        if (backTilePenaltyCoroutine != null)
        {
            backTilePenaltyPending = true;
            return;
        }

        lastBackTilePenaltyTime = now;
        // 启动新的协程
        backTilePenaltyCoroutine = StartCoroutine(BackTilePenaltyCoroutine());
    }

    public void BackTilePenalty_Sync()
    {
        if (backTilePenaltyForceFull)
        {
            BakePenaltyForAllTilesSync();
            backTilePenaltyForceFull = false;
            backTilePenaltyDirtyCells.Clear();
            return;
        }

        if (backTilePenaltyDirtyCells.Count > 0)
        {
            BakePenaltyForDirtyCellsSync();
            backTilePenaltyDirtyCells.Clear();
            return;
        }

        // 无脏区时无需执行
    }

    private void BakePenaltyForAllTilesSync()
    {
        // 获取GridGraph以获得节点尺寸信息
        var gridGraph = AstarGameManager.Instance?.Pathfinder?.data?.gridGraph;
        float nodeSize = gridGraph != null ? gridGraph.nodeSize : 1f;

        // 处理所有节点数据 这个是根据地块数据进行烘焙的
        foreach (var (worldPos, tileDataList) in Data.EnumerateNonEmptyTiles())
        {
            // 获取最顶层 TileData（倒数第一个）
            TileData topTile = tileDataList[^1];

            Vector3Int position3D = new Vector3Int(worldPos.x, worldPos.y, 0);

            // 使用更精确的世界坐标计算方法，解决偏移问题
            Vector3 cellCenterWorld = tileMap.CellToWorld(position3D) + tileMap.cellSize / 2f;

            // 进一步校正坐标以匹配A*网格节点中心
            float alignedX = Mathf.Floor(cellCenterWorld.x / nodeSize) * nodeSize + nodeSize * 0.5f;
            float alignedY = Mathf.Floor(cellCenterWorld.y / nodeSize) * nodeSize + nodeSize * 0.5f;
            Vector3 alignedWorldPos = new Vector3(alignedX, alignedY, cellCenterWorld.z);

            AstarGameManager.Instance?.ModifyNodePenalty_Optimized(alignedWorldPos, topTile.Penalty, topTile.IsWalkable);
        }

        Debug.Log($"✅ 完成同步烘焙 {Data.CountNonEmptyCells()} 个地块的寻路权重");
    }

    private void BakePenaltyForDirtyCellsSync()
    {
        foreach (var worldPos in backTilePenaltyDirtyCells)
        {
            TileData topTile = GetTopTile(worldPos);
            if (topTile == null)
            {
                continue;
            }

            AstarGameManager.Instance?.ModifyNodePenalty_GridGraphFast(
                new Vector2(worldPos.x + 0.5f, worldPos.y + 0.5f),
                topTile.Penalty,
                topTile.IsWalkable);
        }
    }

    /// <summary>
    /// 使用协程优化的烘焙地块寻路权重方法
    /// </summary>
    private IEnumerator BackTilePenaltyCoroutine()
    {
        var astar = AstarGameManager.Instance;
        if (astar == null)
        {
            backTilePenaltyCoroutine = null;
            yield break;
        }

        if (tileMap == null)
        {
            Debug.LogError("[Map.BackTilePenaltyCoroutine] tileMap 为空，无法执行烘焙", this);
            backTilePenaltyCoroutine = null;
            yield break;
        }

        if (Data == null)
        {
            Debug.LogError("[Map.BackTilePenaltyCoroutine] Data 为空，无法执行烘焙", this);
            backTilePenaltyCoroutine = null;
            yield break;
        }

        while (!astar.IsGridGraphReady)
            yield return null;

        int batchSize = Mathf.Max(1, backTilePenaltyTilesPerYield);
        object yieldToken = GetBackTilePenaltyYieldToken();
        int processed = 0;

        if (backTilePenaltyForceFull)
        {
            foreach (var (worldPos, tileDataList) in Data.EnumerateNonEmptyTiles())
            {
                TileData topTile = tileDataList[^1];

                Vector3Int position3D = new Vector3Int(worldPos.x, worldPos.y, 0);
                Vector3 cellCenterWorld = tileMap.GetCellCenterWorld(position3D);

                astar.ModifyNodePenalty_GridGraphFast(
                    new Vector2(cellCenterWorld.x, cellCenterWorld.y),
                    topTile.Penalty,
                    topTile.IsWalkable);

                processed++;
                if (processed % batchSize == 0)
                {
                    yield return yieldToken;
                }
            }

            backTilePenaltyForceFull = false;
            backTilePenaltyDirtyCells.Clear();
        }
        else if (backTilePenaltyDirtyCells.Count > 0)
        {
            backTilePenaltyDirtySnapshot.Clear();
            foreach (var dirtyPos in backTilePenaltyDirtyCells)
            {
                backTilePenaltyDirtySnapshot.Add(dirtyPos);
            }
            backTilePenaltyDirtyCells.Clear();

            for (int i = 0; i < backTilePenaltyDirtySnapshot.Count; i++)
            {
                Vector2Int worldPos = backTilePenaltyDirtySnapshot[i];
                TileData topTile = GetTopTile(worldPos);
                if (topTile == null)
                {
                    continue;
                }

                Vector3Int position3D = new Vector3Int(worldPos.x, worldPos.y, 0);
                Vector3 cellCenterWorld = tileMap.GetCellCenterWorld(position3D);

                astar.ModifyNodePenalty_GridGraphFast(
                    new Vector2(cellCenterWorld.x, cellCenterWorld.y),
                    topTile.Penalty,
                    topTile.IsWalkable);

                processed++;
                if (processed % batchSize == 0)
                {
                    yield return yieldToken;
                }
            }
        }

        //        Debug.Log($"✅ 完成烘焙 {nodesToProcess.Count} 个地块的寻路权重");

        // 清理协程引用
        backTilePenaltyCoroutine = null;

        // 合并短时间内的重复请求：当前批次结束后补跑一次
        if (backTilePenaltyPending)
        {
            backTilePenaltyPending = false;
            MarkPenaltyDirtyFull();
            BackTilePenalty_Async();
        }
    }
    #endregion

    #region 地块数据烘焙
    /// <summary>
    /// 烘焙单个地块的寻路权重
    /// </summary>
    /// <param name="position2D">地块的2D坐标</param>
    public void BackTilePenalty_Cell(Vector2 position2D)
    {
        var tile = GetTile(new Vector2Int(x: (int)position2D.x, y: (int)position2D.y));
        if (tile == null) return;

        MarkPenaltyDirty(new Vector2Int((int)position2D.x, (int)position2D.y));

        uint penalty = tile.Penalty;
        AstarGameManager.Instance?.ModifyNodePenalty_Optimized(position2D, penalty, tile.IsWalkable);
    }
    /// <summary>
    /// 烘焙指定位置为中心的 3×3 地块寻路权重
    /// </summary>
    public void BackTilePenalty_Cell_3x3(Vector2 centerPosition2D)
    {
        Vector2Int center = new Vector2Int(
            Mathf.FloorToInt(centerPosition2D.x),
            Mathf.FloorToInt(centerPosition2D.y)
        );

        for (int offsetX = -1; offsetX <= 1; offsetX++)
        {
            for (int offsetY = -1; offsetY <= 1; offsetY++)
            {
                Vector2Int tilePos = new Vector2Int(
                    center.x + offsetX,
                    center.y + offsetY
                );

                var tile = GetTile(tilePos);
                if (tile == null)
                    continue;

                // 卸载建筑或恢复区域时，默认把地块恢复为可通行
                tile.IsWalkable = true;

                MarkPenaltyDirty(tilePos);

                uint penalty = tile.Penalty;

                AstarGameManager.Instance?.ModifyNodePenalty_Optimized(
                    new Vector2(tilePos.x, tilePos.y),
                    penalty,
                    tile.IsWalkable
                );
            }
        }
    }



    /// <summary>
    /// 烘焙单个地块的寻路权重（强制设为不可通行）
    /// </summary>
    /// <param name="position2D">地块的2D坐标</param>
    public void BackTilePenalty_Cell_NotMove(Vector2 position2D)
    {
        // 先更新 TileData：将该格子的最顶层 Tile 标记为不可通行
        Vector2Int gridPos = new Vector2Int(
            Mathf.FloorToInt(position2D.x),
            Mathf.FloorToInt(position2D.y)
        );

        var tile = GetTile(gridPos);
        if (tile != null)
        {
            tile.IsWalkable = false;
            MarkPenaltyDirty(gridPos);
        }

        // 再更新寻路节点：Penalty=0 + Walkable=false
        AstarGameManager.Instance?.ModifyNodePenalty_Optimized(position2D, 0, false);
    }

    /// <summary>
    /// 烘焙指定区域（Bounds）内所有地块的寻路权重
    /// </summary>
    /// <param name="bounds">要烘焙的区域</param>
    public void BackTilePenalty_Bounds(Bounds bounds, bool useTilepenalty = false)
    {
        Debug.Log($"[BackTilePenalty_Bounds] 开始烘焙Bounds区域，中心: {bounds.center}, 大小: {bounds.size}");

        // 检查必要组件
        if (Data == null)
        {
            Debug.LogError("[BackTilePenalty_Bounds] Data为空，无法执行烘焙");
            return;
        }

        // TileData_Array 在 Data 内部维护

        if (tileMap == null)
        {
            Debug.LogError("[BackTilePenalty_Bounds] tileMap为空，无法执行烘焙");
            return;
        }

        if (AstarGameManager.Instance == null)
        {
            Debug.LogError("[BackTilePenalty_Bounds] AstarGameManager.Instance为空，无法执行烘焙");
            return;
        }

        // 获取GridGraph以获得节点尺寸信息
        var gridGraph = AstarGameManager.Instance?.Pathfinder?.data?.gridGraph;
        float nodeSize = gridGraph != null ? gridGraph.nodeSize : 1f;
        Debug.Log($"[BackTilePenalty_Bounds] 节点尺寸: {nodeSize}");

        // 计算Bounds覆盖的整数坐标范围，带有0.5的右上角偏移
        Vector2Int min = new Vector2Int(
            Mathf.FloorToInt(bounds.min.x),
            Mathf.FloorToInt(bounds.min.y)
        );
        Vector2Int max = new Vector2Int(
            Mathf.FloorToInt(bounds.max.x),
            Mathf.FloorToInt(bounds.max.y)
        );

        // 添加0.5的右上角偏移，确保与建筑放置时的网格对齐方式一致
        max.x += 1; // 右上角偏移0.5相当于增加一个单位
        max.y += 1; // 右上角偏移0.5相当于增加一个单位

        Debug.Log($"[BackTilePenalty_Bounds] 计算出的坐标范围(带0.5偏移): min({min.x}, {min.y}) - max({max.x}, {max.y})");

        int processedTiles = 0;
        int skippedTiles = 0;

        // 遍历Bounds内的所有地块
        for (int x = min.x; x <= max.x; x++)
        {
            for (int y = min.y; y <= max.y; y++)
            {
                Vector2Int position2D = new Vector2Int(x, y);

                // 只有当地图数据中存在该位置的地块时才处理
                var list = Data.GetTileListAt(position2D);
                if (list != null && list.Count > 0)
                {
                    TileData topTile = list[^1];
                    Vector3Int position3D = new Vector3Int(position2D.x, position2D.y, 0);

                    // 使用更精确的世界坐标计算方法，解决偏移问题
                    Vector3 cellCenterWorld = tileMap.CellToWorld(position3D) + tileMap.cellSize / 2f;

                    // 进一步校正坐标以匹配A*网格节点中心
                    float alignedX = Mathf.Floor(cellCenterWorld.x / nodeSize) * nodeSize + nodeSize * 0.5f;
                    float alignedY = Mathf.Floor(cellCenterWorld.y / nodeSize) * nodeSize + nodeSize * 0.5f;
                    Vector3 alignedWorldPos = new Vector3(alignedX, alignedY, cellCenterWorld.z);
                    if (useTilepenalty == false)
                        // 区域强制不可通行
                        AstarGameManager.Instance?.ModifyNodePenalty_Optimized(alignedWorldPos, 0, false);
                    else
                        // 使用地块自身的可通行性与权重
                        AstarGameManager.Instance?.ModifyNodePenalty_Optimized(alignedWorldPos, topTile.Penalty, topTile.IsWalkable);
                    processedTiles++;
                }
                else
                {
                    skippedTiles++;
                }
            }
        }

        Debug.Log($"[BackTilePenalty_Bounds] 烘焙完成: 处理了{processedTiles}个地块，跳过了{skippedTiles}个不存在的地块");
    }

    public void MarkPenaltyDirty(Vector2Int worldPos)
    {
        backTilePenaltyDirtyCells.Add(worldPos);
    }

    public void MarkPenaltyDirtyFull()
    {
        backTilePenaltyForceFull = true;
        backTilePenaltyDirtyCells.Clear();
    }

    /// <summary>
    /// 协程：异步烘焙指定区域（Bounds）内所有地块的寻路权重
    /// </summary>
    /// <param name="bounds">要烘焙的区域</param>
    /// <returns></returns>
    private IEnumerator BackTilePenalty_BoundsCoroutine(Bounds bounds)
    {
        // 获取GridGraph以获得节点尺寸信息
        var gridGraph = AstarGameManager.Instance?.Pathfinder?.data?.gridGraph;
        float nodeSize = gridGraph != null ? gridGraph.nodeSize : 1f;

        // 计算Bounds覆盖的整数坐标范围，带有0.5的右上角偏移
        Vector2Int min = new Vector2Int(
            Mathf.FloorToInt(bounds.min.x),
            Mathf.FloorToInt(bounds.min.y)
        );
        Vector2Int max = new Vector2Int(
            Mathf.FloorToInt(bounds.max.x),
            Mathf.FloorToInt(bounds.max.y)
        );

        // 添加0.5的右上角偏移，确保与建筑放置时的网格对齐方式一致
        max.x += 1; // 右上角偏移0.5相当于增加一个单位
        max.y += 1; // 右上角偏移0.5相当于增加一个单位

        // 计算总共需要处理的地块数量
        int totalTiles = (max.x - min.x + 1) * (max.y - min.y + 1);

        // 分批处理地块，避免长时间阻塞主线程
        const int batchSize = 100;
        int processedCount = 0;

        // 遍历Bounds内的所有地块
        for (int x = min.x; x <= max.x; x++)
        {
            for (int y = min.y; y <= max.y; y++)
            {
                Vector2Int position2D = new Vector2Int(x, y);

                // 只有当地图数据中存在该位置的地块时才处理
                var list = Data.GetTileListAt(position2D);
                if (list != null && list.Count > 0)
                {
                    TileData topTile = list[^1];
                    Vector3Int position3D = new Vector3Int(position2D.x, position2D.y, 0);

                    // 使用更精确的世界坐标计算方法，解决偏移问题
                    Vector3 cellCenterWorld = tileMap.CellToWorld(position3D) + tileMap.cellSize / 2f;

                    // 进一步校正坐标以匹配A*网格节点中心
                    float alignedX = Mathf.Floor(cellCenterWorld.x / nodeSize) * nodeSize + nodeSize * 0.5f;
                    float alignedY = Mathf.Floor(cellCenterWorld.y / nodeSize) * nodeSize + nodeSize * 0.5f;
                    Vector3 alignedWorldPos = new Vector3(alignedX, alignedY, cellCenterWorld.z);

                    AstarGameManager.Instance?.ModifyNodePenalty_Optimized(alignedWorldPos, topTile.Penalty, topTile.IsWalkable);
                }

                processedCount++;

                // 每处理一批就等待一帧，让出控制权给其他任务
                if (processedCount % batchSize == 0)
                {
                    yield return null;
                }
            }
        }

        Debug.Log($"✅ 完成烘焙 Bounds 区域内 {processedCount} 个地块的寻路权重");
    }
    #endregion

    #region 数据初始化
    public void SaveTileMap_TO_TileData()
    {
        if (tileMap == null)
        {
            Debug.LogError("Tilemap 组件为空，无法保存数据！");
            return;
        }

        BoundsInt bounds = tileMap.cellBounds;

        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        Data.position = new Vector2Int(
            Mathf.RoundToInt(transform.parent.position.x),
            Mathf.RoundToInt(transform.parent.position.y)
        );
        Data.EnsureTileDataArray((int)chunkSize.x, (int)chunkSize.y, initCells: true);
        Data.ClearAllTiles();

        // 遍历 Tilemap 上的所有 Tile
        foreach (Vector3Int pos3D in bounds.allPositionsWithin)
        {
            TileBase tilebase = tileMap.GetTile(pos3D);

            if (tilebase == null) continue;

            Vector2Int pos2D = new Vector2Int(pos3D.x, pos3D.y);

            string Name_ItemName = ConvertTileBaseNameToItemName(tilebase.name); // 使用转换方法

            TileData tileData;
            tileData = GameRes.Instance.GetPrefab(Name_ItemName).
                GetComponent<IBlockTile>().TileData.DeepClone();

            Data.AddTileData(pos2D, tileData);
        }

        Debug.Log("多层 TileData 已保存到 Data_TileMap 中" + Data.CountNonEmptyCells());
    }
    #endregion

    #region 工具方法
    /// <summary>
    /// 将TileBase名称转换为对应的ItemName
    /// 规则：TileBase_XXX -> TileItem_XXX
    /// </summary>
    /// <param name="tileBaseName">TileBase的名称</param>
    /// <returns>对应的ItemName</returns>
    private string ConvertTileBaseNameToItemName(string tileBaseName)
    {
        if (string.IsNullOrEmpty(tileBaseName))
        {
            Debug.LogWarning("TileBase名称为空，无法转换为ItemName");
            return "";
        }

        // 检查是否以"TileBase_"开头
        if (tileBaseName.StartsWith("TileBase_"))
        {
            // 提取后缀部分（如：Grass, Water, Mountain）
            string suffix = tileBaseName.Substring("TileBase_".Length);

            // 组合成新的ItemName
            string itemName = "TileItem_" + suffix;

            Debug.Log($"TileBase名称转换：{tileBaseName} -> {itemName}");
            return itemName;
        }
        else
        {
            Debug.LogWarning($"TileBase名称 '{tileBaseName}' 不符合预期格式（应以'TileBase_'开头）");
            return "";
        }
    }
    #endregion

    #region 环境查询

    public Vector2Int GetEnvironmentLocalPos(Vector2Int worldPos)
    {
        if (Data == null)
        {
            throw new InvalidOperationException("[Map.GetEnvironmentLocalPos] Data 为空，无法读取环境参数。");
        }

        if (Data.TryGetEnvironmentLocalPos(worldPos, out Vector2Int localPos))
        {
            return localPos;
        }

        int width = Data.EnvironmentLayers != null ? Data.EnvironmentLayers.Width : 0;
        int height = Data.EnvironmentLayers != null ? Data.EnvironmentLayers.Height : 0;
        localPos = worldPos - Data.position;

        if ((uint)localPos.x >= (uint)width || (uint)localPos.y >= (uint)height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(worldPos),
                $"[Map.GetEnvironmentLocalPos] 坐标越界：world={worldPos}, local={localPos}, size={width}x{height}");
        }

        return localPos;
    }

    public float GetTemperatureCelsius(Vector2Int worldPos)
    {
        Vector2Int localPos = GetEnvironmentLocalPos(worldPos);
        return Data.EnvironmentLayers.TemperatureCelsius[localPos.x, localPos.y];
    }

    #endregion

    #region Tile操作方法
    public void ADDTile(Vector2Int position, TileData tileData)
    {
        tileData.position = (Vector3Int)position;

        Data.AddTileData(position, tileData);

        UpdateTileBaseAtPosition(position);
    }

    public void ADDTileData(Vector2Int position, TileData tileData)
    {
        tileData.position = (Vector3Int)position;

        Data.AddTileData(position, tileData);
    }

    [Button("获取 TileData")]
    public TileData GetTile(Vector2Int position, int? index = null)
    {
        return Data.GetTileDataAt(position, index);
    }

    // 重载方法：只获取最上层的 TileData
    public TileData GetTopTile(Vector2Int position)
    {
        return GetTile(position);
    }

    // 重载方法：获取指定索引的 TileData
    public TileData GetTileAt(Vector2Int position, int index)
    {
        return GetTile(position, index);
    }

    // 重载方法：获取所有 TileData
    public List<TileData> GetAllTiles(Vector2Int position)
    {
        var list = Data.GetTileListAt(position);
        return list != null ? new List<TileData>(list) : new List<TileData>();
    }

    public void DELTile(Vector2Int position, int? index = null)
    {
        if (!Data.RemoveTileData(position, index))
            return;

        UpdateTileBaseAtPosition(position);
    }

    public void UPDTile(Vector2Int position, int index, TileData tileData)
    {
        tileData.position = (Vector3Int)position;
        Data.UpdateTileData(position, index, tileData);
        UpdateTileBaseAtPosition(position);
    }

    public void UpdateTileBaseAtPosition(Vector2Int position)
    {
        Vector3Int position3D = new Vector3Int(position.x, position.y, 0);

        var list = Data.GetTileListAt(position);
        if (list == null || list.Count == 0)
        {
            tileMap.SetTile(position3D, null); // 清除该 Tile
            Debug.Log($"清除了位置 {position} 上的 TileBase（无数据）");
            return;
        }

        // 获取该位置最顶层的 TileData（最后一个）
        TileData topTile = list[^1];
        TileBase tile = GameRes.Instance.GetTileBase(topTile.ID);

        if (tile == null)
        {
            Debug.LogError($"无法加载 TileBase：{topTile.ID}，更新失败。");
            return;
        }

        tileMap.SetTile(position3D, tile);
        //Debug.Log($"已更新 TileBase 于位置 {position}，使用资源：{topTile.Name_TileBase}");
    }
    #endregion
}