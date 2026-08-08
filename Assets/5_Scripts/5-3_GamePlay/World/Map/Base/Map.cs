using Force.DeepCloner;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using System;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

public class Map : Item
{
    #region 属性和字段
    [Header("地图配置")]
    [SerializeField, FormerlySerializedAs("Data")]
    private Data_TileMap data = new Data_TileMap();

    public Data_TileMap Data => data;

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

    [SerializeField, Min(0f)]
    private float backTilePenaltyYieldSeconds = 0f;

    [Header("Tilemap加载分帧配置")]
    [SerializeField, Min(16)]
    private int loadTileBatchSize = 256;

    [SerializeField, Min(0.25f)]
    private float loadTileFrameBudgetMilliseconds = 2f;

    [Header("程序化生成分帧配置")]
    [SerializeField, Min(1)]
    private int proceduralGenerationCellsPerFrame = 256;

    [SerializeField, Min(0.25f)]
    private float proceduralGenerationFrameBudgetMilliseconds = 1.5f;

    public float ProceduralGenerationFrameBudgetMilliseconds =>
        ChunkMgr.ScaleCurrentChunkLoadFrameBudget(
            proceduralGenerationFrameBudgetMilliseconds,
            0.25f);

    public float TilemapLoadFrameBudgetMilliseconds =>
        ChunkMgr.ScaleCurrentChunkLoadFrameBudget(loadTileFrameBudgetMilliseconds, 0.25f);

    public int ProceduralGenerationCellsPerFrame =>
        ChunkMgr.ScaleCurrentChunkLoadItemBudget(proceduralGenerationCellsPerFrame, 1);

    public int ScaledTilemapLoadBatchSize =>
        ChunkMgr.ScaleCurrentChunkLoadItemBudget(TilemapLoadBatchSize, 16);

    private float backTilePenaltyWaitSeconds = -1f;
    private WaitForSeconds backTilePenaltyWait;
    private float lastBackTilePenaltyTime = -999f;
    private bool backTilePenaltyPending;
    private bool backTilePenaltyForceFull = true;
    [NonSerialized] private bool tilemapVisualReady;
    private static Map activeProceduralGenerationMap;
    [NonSerialized] private bool ownsProceduralGenerationSlot;
    [NonSerialized] private MapGenerationContext activeGenerationContext;
    [NonSerialized] private string lastGenerationFailure;
    [NonSerialized] private TileBase[] groundTileRowBuffer;
    [NonSerialized] private TileBase[] blockingTileRowBuffer;
    [NonSerialized] private int lastInitialRenderBatchCount;
    [NonSerialized] private int lastInitialRenderTileCount;
    private readonly HashSet<Vector2Int> backTilePenaltyDirtyCells = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> backTilePenaltyDirtySnapshot = new List<Vector2Int>(128);

    [SerializeReference]
    public List<ChunkGeneratorBase> mapGenerators = new List<ChunkGeneratorBase>();

    /// <summary>
    /// 兼容调试/显示：通常 0 号位放 ChunkGenerator_Land。
    /// </summary>
    public ChunkGenerator_Land LandGenerator => GetGenerator<ChunkGenerator_Land>();

    public bool IsReadyForChunkLifecycle =>
        Data != null &&
        Data.TileLoaded &&
        string.IsNullOrEmpty(lastGenerationFailure) &&
        tilemapVisualReady &&
        loadOrGenerateCoroutine == null &&
        loadTileMapCoroutine == null;

    public bool IsTilemapVisualReady => tilemapVisualReady;
    public bool HasGenerationFailed => !string.IsNullOrEmpty(lastGenerationFailure);
    public string LastGenerationFailure => lastGenerationFailure;
    public MapGenerationContext ActiveGenerationContext => activeGenerationContext;
    public int LastInitialRenderBatchCount => lastInitialRenderBatchCount;
    public int LastInitialRenderTileCount => lastInitialRenderTileCount;
    public event Action<Map, string> OnGenerationFailed;

    protected virtual bool ShouldBakePenaltyAfterTilemapLoad => true;
    protected virtual int TilemapLoadBatchSize => Mathf.Max(16, loadTileBatchSize);

    /// <summary>
    /// 重置地图就绪状态，用于重新加载场景
    /// </summary>
    public void ResetMapReadyState()
    {
        tilemapVisualReady = false;
        lastGenerationFailure = null;
        chunk?.ResetLifecycleState();
    }

    protected void BeginMapLoad()
    {
        tilemapVisualReady = false;
        lastGenerationFailure = null;
        chunk?.BeginMapLoad();
    }

    protected void NotifyChunkReady()
    {
        if (IsReadyForChunkLifecycle)
        {
            if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
                Debug.Log($"[WorldNav][Map] NotifyChunkReady | chunk={chunk?.name ?? "null"} | Map={name}");
            chunk?.NotifyMapLoaded();
        }
        else
        {
            if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
                Debug.LogWarning($"[WorldNav][Map] NotifyChunkReady 条件不满足 | Data={Data != null} TileLoaded={Data?.TileLoaded} loadOrGenerateCoroutine={loadOrGenerateCoroutine != null} loadTileMapCoroutine={loadTileMapCoroutine != null}");
        }
    }

    protected virtual void OnTilemapLoaded()
    {
        GetComponent<GrassDetailLayer>()?.Rebuild(this);

        if (!ShouldBakePenaltyAfterTilemapLoad)
        {
            if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
                Debug.Log($"[WorldNav][Map] 跳过导航注册 | ShouldBakePenaltyAfterTilemapLoad=false | Map={name}");
            return;
        }

        if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
            Debug.Log($"[WorldNav][Map] 注册地图导航 | Map={name} | Ready={WorldNavigationManager.Instance?.IsNavigationReady}");
        MarkPenaltyDirtyFull();
        BackTilePenalty_Async();
    }

    protected void FinalizeTilemapLoad()
    {
        try
        {
            tilemapVisualReady = true;
            if (Data != null)
                Data.TileLoaded = true;
            activeGenerationContext?.MarkSucceeded();
            if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
                Debug.Log($"[WorldNav][Map] FinalizeTilemapLoad | chunk={chunk?.name ?? "null"} | Map={name} | Data.TileLoaded={Data?.TileLoaded}");
            WrappedTilemapCollisionProxy.Ensure(this);
            OnTilemapLoaded();
            NotifyChunkReady();
            activeGenerationContext = null;
        }
        catch (Exception exception)
        {
            FailGeneration(activeGenerationContext, "地图最终化失败", exception);
            activeGenerationContext = null;
        }
    }

    private void Awake()
    {
        BindChunkOwner(GetComponentInParent<Chunk>());
        InitMapGenerators();
    }

    private void OnEnable()
    {
        if (Data?.TileLoaded == true)
            WorldNavigationManager.Instance?.RegisterMap(this);
    }

    private void OnDisable()
    {
        WorldNavigationManager.ExistingInstance?.UnregisterMap(this);
        StopMapCoroutines();
    }

    private new void OnDestroy()
    {
        WorldNavigationManager.ExistingInstance?.UnregisterMap(this);
        StopMapCoroutines();
    }

    private void StopMapCoroutines()
    {
        if (loadOrGenerateCoroutine != null || loadTileMapCoroutine != null)
            tilemapVisualReady = false;

        activeGenerationContext?.Cancel("地图对象已停用或销毁");
        CancelPendingGeneratorWork();

        if (loadTileMapCoroutine != null)
        {
            StopCoroutine(loadTileMapCoroutine);
            loadTileMapCoroutine = null;
        }

        if (backTilePenaltyCoroutine != null)
        {
            StopCoroutine(backTilePenaltyCoroutine);
            backTilePenaltyCoroutine = null;
        }

        if (loadOrGenerateCoroutine != null)
        {
            StopCoroutine(loadOrGenerateCoroutine);
            loadOrGenerateCoroutine = null;
        }

        ReleaseProceduralGenerationSlot();
        backTilePenaltyPending = false;
        activeGenerationContext = null;
    }

    private void CancelPendingGeneratorWork()
    {
        if (mapGenerators == null)
            return;

        for (int i = 0; i < mapGenerators.Count; i++)
            mapGenerators[i]?.CancelPendingWork();
    }

    private IEnumerator WaitForProceduralGenerationSlot()
    {
        while (activeProceduralGenerationMap != null &&
               activeProceduralGenerationMap != this)
        {
            yield return null;
        }

        activeProceduralGenerationMap = this;
        ownsProceduralGenerationSlot = true;
    }

    private void ReleaseProceduralGenerationSlot()
    {
        if (!ownsProceduralGenerationSlot)
            return;

        ownsProceduralGenerationSlot = false;
        if (activeProceduralGenerationMap == this)
            activeProceduralGenerationMap = null;
    }

    public void EnsureTilemapVisualReady()
    {
        if (!isActiveAndEnabled ||
            tilemapVisualReady ||
            loadOrGenerateCoroutine != null ||
            loadTileMapCoroutine != null ||
            Data == null ||
            !Data.TileLoaded ||
            Data.CountNonEmptyCells() == 0 ||
            !EnsureTilemapReference())
        {
            return;
        }

        tileMap.ClearAllTiles();
        LoadTileData_To_TileMap_Ansync();
    }

    protected void BindChunkOwner(Chunk ownerChunk)
    {
        chunk = ownerChunk;
        if (chunk != null)
        {
            chunk.Map = this;
        }
    }

    protected bool EnsureTilemapReference()
    {
        if (tileMap != null)
            return true;

        tileMap = GetComponentInChildren<Tilemap>(includeInactive: false);

        if (tileMap != null)
            return true;

        Debug.LogError("[Map.Load] tileMap 为空，无法加载/生成地图", this);
        return false;
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

    private void InitMapGenerators()
    {
        for (int i = 0; i < mapGenerators.Count; i++)
        {
            ChunkGeneratorBase generator = mapGenerators[i];
            if (generator == null)
            {
                Debug.LogError($"[Map.InitMapGenerators] mapGenerators[{i}] 为空", this);
                continue;
            }

            generator.Init(this);
        }

    }

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

    private IEnumerator GenerateByPipelineCoroutine(PlanetData planetData)
    {
        if (!TryBuildOrderedPipeline(out List<ChunkGeneratorBase> orderedGenerators, out string validationError))
        {
            FailGeneration(null, validationError, null);
            yield break;
        }

        data ??= new Data_TileMap();
        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        Data.EnsureTileStorage(
            Mathf.Max(1, Mathf.RoundToInt(chunkSize.x)),
            Mathf.Max(1, Mathf.RoundToInt(chunkSize.y)));
        Data.TileLoaded = false;

        int baseSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        DimensionManager dimensionManager = DimensionManager.Instance;
        int worldSeed = dimensionManager != null
            ? dimensionManager.GetActiveGenerationSeed(baseSeed)
            : baseSeed;
        WorldAddress worldAddress = dimensionManager != null
            ? dimensionManager.ActiveAddress
            : WorldAddress.FromWorldKey(SceneManager.GetActiveScene().name);
        var context = new MapGenerationContext(
            this,
            planetData,
            worldSeed,
            worldAddress,
            dimensionManager != null ? dimensionManager.ActiveDefinition : null,
            WrappedWorldGenerationDomain.Create(planetData));
        activeGenerationContext = context;

        GenerationStage? activeStage = null;
        for (int i = 0; i < orderedGenerators.Count; i++)
        {
            ChunkGeneratorBase generator = orderedGenerators[i];

            IEnumerator generatorRoutine;
            try
            {
                generator.Init(this);
                if (activeStage != generator.Stage)
                {
                    context.BeginStage(generator.Stage);
                    activeStage = generator.Stage;
                }
                generatorRoutine = generator.GenerateAsync(
                    context,
                    ProceduralGenerationCellsPerFrame);
            }
            catch (Exception exception)
            {
                FailGeneration(
                    context,
                    $"阶段 {generator.Stage} 初始化失败：{generator.GetType().Name}",
                    exception);
                yield break;
            }

            bool generatorFailed = false;
            Exception generatorException = null;
            while (generatorRoutine != null)
            {
                bool hasNext;
                object current = null;
                try
                {
                    hasNext = generatorRoutine.MoveNext();
                    if (hasNext)
                        current = generatorRoutine.Current;
                }
                catch (Exception exception)
                {
                    generatorFailed = true;
                    generatorException = exception;
                    break;
                }

                if (!hasNext)
                    break;

                yield return current;
            }

            try
            {
                (generatorRoutine as IDisposable)?.Dispose();
            }
            catch (Exception exception)
            {
                generatorFailed = true;
                generatorException ??= exception;
            }
            if (context.IsCancellationRequested)
                yield break;
            if (generatorFailed)
            {
                FailGeneration(
                    context,
                    $"阶段 {generator.Stage} 执行失败：{generator.GetType().Name}",
                    generatorException);
                yield break;
            }

            bool stageCompleted = i == orderedGenerators.Count - 1 ||
                                  orderedGenerators[i + 1].Stage != generator.Stage;
            if (!stageCompleted)
                continue;

            try
            {
                context.CompleteStage(generator.Stage);
                activeStage = null;
            }
            catch (Exception exception)
            {
                FailGeneration(
                    context,
                    $"阶段 {generator.Stage} 完成状态提交失败：{generator.GetType().Name}",
                    exception);
                yield break;
            }
            yield return null;
        }

        if (SaveDataMgr.Instance != null &&
            !SaveDataMgr.Instance.TryFinalizeProceduralChunk(chunk, out string persistenceFailure))
        {
            FailGeneration(context, $"程序化区块基线或差量应用失败：{persistenceFailure}", null);
            yield break;
        }

        // TileLoaded and Succeeded are committed only after batched Tilemap finalization succeeds.
    }

    private bool TryBuildOrderedPipeline(
        out List<ChunkGeneratorBase> orderedGenerators,
        out string failureReason)
    {
        orderedGenerators = new List<ChunkGeneratorBase>();
        failureReason = null;
        if (mapGenerators == null || mapGenerators.Count == 0)
        {
            failureReason = "mapGenerators 为空。";
            return false;
        }

        var entries = new List<(ChunkGeneratorBase Generator, int SerializedIndex)>(mapGenerators.Count);
        int baseTerrainCount = 0;
        for (int i = 0; i < mapGenerators.Count; i++)
        {
            ChunkGeneratorBase generator = mapGenerators[i];
            if (generator == null)
            {
                failureReason = $"mapGenerators[{i}] 为空。";
                return false;
            }

            if (!Enum.IsDefined(typeof(GenerationStage), generator.Stage))
            {
                failureReason = $"mapGenerators[{i}] 使用了非法生成阶段 {(int)generator.Stage}。";
                return false;
            }

            if (generator.Stage == GenerationStage.BaseTerrain)
                baseTerrainCount++;
            entries.Add((generator, i));
        }

        if (baseTerrainCount != 1)
        {
            failureReason = $"生成管线必须恰好包含一个 BaseTerrain，当前为 {baseTerrainCount}。";
            return false;
        }

        entries.Sort((left, right) =>
        {
            int stageOrder = left.Generator.Stage.CompareTo(right.Generator.Stage);
            return stageOrder != 0 ? stageOrder : left.SerializedIndex.CompareTo(right.SerializedIndex);
        });
        for (int i = 0; i < entries.Count; i++)
            orderedGenerators.Add(entries[i].Generator);
        return true;
    }

    private void FailGeneration(MapGenerationContext context, string reason, Exception exception)
    {
        string message = string.IsNullOrWhiteSpace(reason) ? "地图生成失败" : reason;
        context?.Fail(message, exception);
        if (Data != null)
            Data.TileLoaded = false;
        tilemapVisualReady = false;
        tileMap?.ClearAllTiles();
        BlockingTilemapLayer.ClearMap(this);
        SaveDataMgr.Instance?.DiscardProceduralChunkBaseline(chunk);
        lastGenerationFailure = exception == null ? message : $"{message}: {exception.Message}";
        Debug.LogError($"[Map.GenerateByPipeline] {lastGenerationFailure}\n{exception}", this);
        OnGenerationFailed?.Invoke(this, lastGenerationFailure);
        chunk?.MarkFailed(lastGenerationFailure);
    }

    private void OnGUI()
    {
    }

    public override ItemData itemData => data;

    protected override void SetItemData(ItemData value)
    {
        data = RequireData<Data_TileMap>(value);
    }

    #endregion

    #region 基础方法实现

    public override void Act()
    {
        if (Data.TileLoaded)
            return;

        var saveDataMgr = SaveDataMgr.Instance;
        if (saveDataMgr?.SaveData?.PlanetData_Dict == null)
            return;

        if (saveDataMgr.SaveData.PlanetData_Dict.TryGetValue(SceneManager.GetActiveScene().name, out _))
        {
            BeginMapLoad();
            Data.TileLoaded = false;
            loadOrGenerateCoroutine = StartCoroutine(GenerateThenLoadTilemapCoroutine());
        }
    }

    #endregion

    #region 保存和加载

    [Button("从数据加载地图")]
    public override void Load()
    {
        base.Load();
        BindChunkOwner(GetComponentInParent<Chunk>());
        InitMapGenerators();

        if (!EnsureTilemapReference())
            return;

        if (Data == null)
        {
            data = new Data_TileMap();
        }

        Vector2 chunkSize = ChunkMgr.GetChunkSize();
        Data.EnsureTileStorage((int)chunkSize.x, (int)chunkSize.y);
        Data.EnsureEnvironmentStorage((int)chunkSize.x, (int)chunkSize.y);

        bool hasTileData = Data.CountNonEmptyCells() > 0;

        if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
            Debug.Log($"[WorldNav][Map] Load | Map={name} chunk={chunk?.name ?? "null"} hasTileData={hasTileData} Ready={WorldNavigationManager.Instance?.IsNavigationReady}");

        // 先停止上一轮加载/生成流程（避免多次点击按钮叠加协程）
        if (loadOrGenerateCoroutine != null)
        {
            StopCoroutine(loadOrGenerateCoroutine);
            loadOrGenerateCoroutine = null;
        }

        BeginMapLoad();
        Data.TileLoaded = false;

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
    }


    private IEnumerator GenerateThenLoadTilemapCoroutine()
    {
        yield return WaitForProceduralGenerationSlot();
        try
        {
            try
            {
                OnMapGenerated_Start.Invoke();
            }
            catch (Exception exception)
            {
                FailGeneration(null, "地图生成开始回调失败", exception);
            }

            if (HasGenerationFailed)
                yield break;

            PlanetData planetData = SaveDataMgr.Instance != null
                ? SaveDataMgr.Instance.GetCurrentPlanetData()
                : null;
            yield return GenerateByPipelineCoroutine(planetData);
        }
        finally
        {
            ReleaseProceduralGenerationSlot();
        }

        bool generationSucceeded =
            activeGenerationContext != null &&
            !activeGenerationContext.HasFailed &&
            !activeGenerationContext.IsCancellationRequested &&
            !HasGenerationFailed;
        if (!generationSucceeded)
        {
            activeGenerationContext = null;
            loadOrGenerateCoroutine = null;
            yield break;
        }

        chunk?.NotifyItemsLoaded();

        tileMap.ClearAllTiles();
        loadOrGenerateCoroutine = null;
        LoadTileData_To_TileMap_Ansync();
        // NotifyChunkReady 由 LoadTileData_To_TileMapCoroutine 结束时的 FinalizeTilemapLoad 触发
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
        tilemapVisualReady = false;
        lastInitialRenderBatchCount = 0;
        lastInitialRenderTileCount = 0;
        tileMap.ClearAllTiles();
        BlockingTilemapLayer blockingLayer = BlockingTilemapLayer.BeginBatch(this);
        if (Data == null || Data.CountNonEmptyCells() == 0)
        {
            Debug.LogWarning("TileData is empty. Nothing to load.");
            CompleteInitialTilemapBatch(blockingLayer);
            FinalizeTilemapLoad();
            return;
        }

        EnsureTilemapRowBuffers(Data.Width);
        for (int localY = 0; localY < Data.Height; localY++)
        {
            if (!TryRenderTilemapRow(localY, ref blockingLayer, out string failureReason))
            {
                FailGeneration(activeGenerationContext, failureReason, null);
                activeGenerationContext = null;
                return;
            }
        }

        CompleteInitialTilemapBatch(blockingLayer);
        FinalizeTilemapLoad();
    }

    public void LoadTileData_To_TileMap_Ansync()
    {
        tilemapVisualReady = false;
        // 如果已有协程在运行，先停止它
        if (loadTileMapCoroutine != null)
        {
            StopCoroutine(loadTileMapCoroutine);
        }

        // 启动新的协程
        loadTileMapCoroutine = StartCoroutine(LoadTileData_To_TileMapCoroutine());
    }

    private IEnumerator LoadTileData_To_TileMapCoroutine()
    {
        lastInitialRenderBatchCount = 0;
        lastInitialRenderTileCount = 0;
        tileMap.ClearAllTiles();
        BlockingTilemapLayer blockingLayer = BlockingTilemapLayer.BeginBatch(this);
        if (Data == null || Data.CountNonEmptyCells() == 0)
        {
            Debug.LogWarning("TileData is empty. Nothing to load.");
            if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
                Debug.LogWarning($"[WorldNav][Map] TileData为空，直接Finalize | Map={name} chunk={chunk?.name ?? "null"}");
            CompleteInitialTilemapBatch(blockingLayer);
            loadTileMapCoroutine = null;
            FinalizeTilemapLoad();
            yield break;
        }

        EnsureTilemapRowBuffers(Data.Width);
        int batchSize = ScaledTilemapLoadBatchSize;
        int processedCount = 0;
        int processedThisFrame = 0;
        double frameBudgetMilliseconds = TilemapLoadFrameBudgetMilliseconds;
        double ticksPerMillisecond = System.Diagnostics.Stopwatch.Frequency / 1000d;
        long frameStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        for (int localY = 0; localY < Data.Height; localY++)
        {
            int previousTileCount = lastInitialRenderTileCount;
            if (!TryRenderTilemapRow(localY, ref blockingLayer, out string failureReason))
            {
                CompleteInitialTilemapBatch(blockingLayer);
                loadTileMapCoroutine = null;
                FailGeneration(activeGenerationContext, failureReason, null);
                activeGenerationContext = null;
                yield break;
            }
            int renderedInRow = lastInitialRenderTileCount - previousTileCount;
            processedCount += renderedInRow;
            processedThisFrame += Data.Width;

            double elapsedMilliseconds =
                (System.Diagnostics.Stopwatch.GetTimestamp() - frameStartTimestamp) / ticksPerMillisecond;
            if (processedThisFrame >= batchSize || elapsedMilliseconds >= frameBudgetMilliseconds)
            {
                processedThisFrame = 0;
                yield return null;
                frameStartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }

        CompleteInitialTilemapBatch(blockingLayer);
        yield return null;
        if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
        {
            Debug.Log($"✅ 完成加载 {processedCount} 个Tile到Tilemap");
            Debug.Log($"[WorldNav][Map] Tilemap加载完成 | {processedCount} 个Tile | Ready={WorldNavigationManager.Instance?.IsNavigationReady} | Map={name}");
        }
        loadTileMapCoroutine = null;
        FinalizeTilemapLoad();
    }

    private void EnsureTilemapRowBuffers(int width)
    {
        int safeWidth = Mathf.Max(1, width);
        if (groundTileRowBuffer == null || groundTileRowBuffer.Length != safeWidth)
            groundTileRowBuffer = new TileBase[safeWidth];
        if (blockingTileRowBuffer == null || blockingTileRowBuffer.Length != safeWidth)
            blockingTileRowBuffer = new TileBase[safeWidth];
    }

    private void CompleteInitialTilemapBatch(BlockingTilemapLayer blockingLayer)
    {
        tileMap?.RefreshAllTiles();
        blockingLayer?.BlockingTilemap?.RefreshAllTiles();

        TilemapCollider2D groundCollider = tileMap != null
            ? tileMap.GetComponent<TilemapCollider2D>()
            : null;
        if (groundCollider != null && groundCollider.hasTilemapChanges)
            groundCollider.ProcessTilemapChanges();

        blockingLayer?.CompleteBatch();
    }

    private bool TryRenderTilemapRow(
        int localY,
        ref BlockingTilemapLayer blockingLayer,
        out string failureReason)
    {
        failureReason = null;
        Array.Clear(groundTileRowBuffer, 0, groundTileRowBuffer.Length);
        Array.Clear(blockingTileRowBuffer, 0, blockingTileRowBuffer.Length);
        bool hasBlockingTile = false;

        for (int localX = 0; localX < Data.Width; localX++)
        {
            Vector2Int worldPosition = Data.position + new Vector2Int(localX, localY);
            if (!Data.TryGetStackView(worldPosition, out TileStackView stack) || stack.Count == 0)
                continue;

            TileData groundData = BlockingTilemapLayer.ResolveGroundTile(stack);
            if (groundData != null)
            {
                TileBase groundTile = GameRes.Instance?.GetTileBase(groundData.ID);
                if (groundTile == null)
                {
                    failureReason = $"Tilemap 最终化失败：找不到地面 TileBase '{groundData.ID}'，位置 {worldPosition}。";
                    return false;
                }

                groundTileRowBuffer[localX] = groundTile;
                lastInitialRenderTileCount++;
            }

            TileData topData = stack[^1];
            if (!BlockingTilemapLayer.IsBlockingTile(topData))
                continue;

            TileBase blockingTile = GameRes.Instance?.GetTileBase(topData.ID);
            if (blockingTile == null)
            {
                failureReason = $"Tilemap 最终化失败：找不到阻挡 TileBase '{topData.ID}'，位置 {worldPosition}。";
                return false;
            }

            blockingTileRowBuffer[localX] = blockingTile;
            hasBlockingTile = true;
        }

        tileMap.SetTilesBlock(
            new BoundsInt(Data.position.x, Data.position.y + localY, 0, Data.Width, 1, 1),
            groundTileRowBuffer);
        lastInitialRenderBatchCount++;

        if (!hasBlockingTile)
            return true;

        blockingLayer ??= BlockingTilemapLayer.EnsureBatchLayer(this);
        if (blockingLayer == null)
        {
            failureReason = "Tilemap 最终化失败：无法创建顶部阻挡层。";
            return false;
        }

        blockingLayer.WriteBatchRow(Data.position, localY, blockingTileRowBuffer);
        lastInitialRenderBatchCount++;
        return true;
    }
    #endregion

    #region 寻路权重烘焙方法
    [Button("异步烘焙地块寻路权重")]
    public void BackTilePenalty_Async()
    {
        if (!gameObject.activeInHierarchy || !enabled)
            return;

        QueuePendingNavigationChanges();
    }

    public void BackTilePenalty_Sync()
    {
        QueuePendingNavigationChanges();
    }

    private void QueuePendingNavigationChanges()
    {
        WorldNavigationManager navigation = WorldNavigationManager.Instance;
        if (navigation == null || Data == null)
            return;

        if (backTilePenaltyForceFull)
        {
            if (Data.Width > 0 && Data.Height > 0)
                navigation.RegisterMap(this);

            backTilePenaltyForceFull = false;
            backTilePenaltyDirtyCells.Clear();
            backTilePenaltyPending = false;
            return;
        }

        navigation.QueueNavigationCells(backTilePenaltyDirtyCells);

        backTilePenaltyDirtyCells.Clear();
        backTilePenaltyPending = false;
    }

    private void BakePenaltyForAllTilesSync()
    {
        WorldNavigationManager navigation = WorldNavigationManager.Instance;
        if (navigation == null)
        {
            Debug.LogError($"[WorldNav][Map] BakePenaltyForAllTilesSync 中止: manager=null | Map={name}");
            return;
        }

        // 处理所有节点数据 这个是根据地块数据进行烘焙的
        foreach (var (worldPos, tileDataList) in Data.EnumerateOccupiedCells())
        {
            // 获取最顶层 TileData（倒数第一个）
            TileData topTile = tileDataList[^1];
            bool walkable = BuildingOccupancyRegistry.GetEffectiveWalkable(worldPos, topTile.IsWalkable);

            navigation.SetNavigationCell(this, worldPos, topTile.Penalty, walkable);
        }

        Debug.Log($"✅ 完成同步烘焙 {Data.CountNonEmptyCells()} 个地块的寻路权重");
    }

    private void BakePenaltyForDirtyCellsSync()
    {
        WorldNavigationManager navigation = WorldNavigationManager.Instance;
        if (navigation == null)
            return;

        foreach (var worldPos in backTilePenaltyDirtyCells)
        {
            TileData topTile = GetTopTile(worldPos);
            if (topTile == null)
            {
                continue;
            }
            bool walkable = BuildingOccupancyRegistry.GetEffectiveWalkable(worldPos, topTile.IsWalkable);

            navigation.SetNavigationCell(this, worldPos, topTile.Penalty, walkable);
        }
    }

    /// <summary>
    /// 使用协程优化的烘焙地块寻路权重方法
    /// </summary>
    private IEnumerator BackTilePenaltyCoroutine()
    {
        WorldNavigationManager navigation = WorldNavigationManager.Instance;
        if (navigation == null)
        {
            Debug.LogError($"[WorldNav][Map] BackTilePenaltyCoroutine 中止: manager=null | Map={name}");
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

        int batchSize = Mathf.Max(1, backTilePenaltyTilesPerYield);
        object yieldToken = GetBackTilePenaltyYieldToken();
        int processed = 0;

        if (backTilePenaltyForceFull)
        {
            foreach (var (worldPos, tileDataList) in Data.EnumerateOccupiedCells())
            {
                TileData topTile = tileDataList[^1];
                bool walkable = BuildingOccupancyRegistry.GetEffectiveWalkable(worldPos, topTile.IsWalkable);

                navigation.SetNavigationCell(this, worldPos, topTile.Penalty, walkable);

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
                bool walkable = BuildingOccupancyRegistry.GetEffectiveWalkable(worldPos, topTile.IsWalkable);

                navigation.SetNavigationCell(this, worldPos, topTile.Penalty, walkable);

                processed++;
                if (processed % batchSize == 0)
                {
                    yield return yieldToken;
                }
            }
        }

        //        Debug.Log($"✅ 完成烘焙 {nodesToProcess.Count} 个地块的寻路权重");

        backTilePenaltyCoroutine = null;

//        Debug.Log($"[WorldNav][Map] BackTilePenaltyCoroutine 完成 | 烘焙了 {processed} 个地块 | pending={backTilePenaltyPending} forceFull={backTilePenaltyForceFull} | Map={name}");

        // 合并短时间内的重复请求：当前批次结束后补跑一次
        if (backTilePenaltyPending)
        {
            backTilePenaltyPending = false;
            MarkPenaltyDirtyFull();
            lastBackTilePenaltyTime = Time.unscaledTime;
            Debug.Log($"[WorldNav][Map] BackTilePenaltyCoroutine 检测到pending请求，立即重启协程 | Map={name}");
            backTilePenaltyCoroutine = StartCoroutine(BackTilePenaltyCoroutine());
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
        Vector2Int cell = new Vector2Int(Mathf.FloorToInt(position2D.x), Mathf.FloorToInt(position2D.y));
        MarkPenaltyDirty(cell);
        BackTilePenalty_Async();
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

                MarkPenaltyDirty(tilePos);
            }
        }

        BackTilePenalty_Async();
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

        BackTilePenalty_Async();
    }

    /// <summary>
    /// 烘焙指定区域（Bounds）内所有地块的寻路权重
    /// </summary>
    /// <param name="bounds">要烘焙的区域</param>
    public void BackTilePenalty_Bounds(Bounds bounds, bool useTilepenalty = false)
    {
        WorldNavigationManager navigation = WorldNavigationManager.Instance;
        if (navigation == null)
            return;

        int minX = Mathf.FloorToInt(bounds.min.x);
        int minY = Mathf.FloorToInt(bounds.min.y);
        int maxX = Mathf.CeilToInt(bounds.max.x);
        int maxY = Mathf.CeilToInt(bounds.max.y);

        if (useTilepenalty)
        {
            navigation.QueueNavigationRegion(new RectInt(minX, minY, maxX - minX, maxY - minY));
            return;
        }

        navigation.SetNavigationRegion(
            this,
            new RectInt(minX, minY, maxX - minX, maxY - minY),
            0u,
            false);
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
        Data.EnsureTileStorage((int)chunkSize.x, (int)chunkSize.y);
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

            Data.SetBaseTile(pos2D, tileData);
        }

        // 存档热路径不再为每个区块额外全量统计格子并输出调用栈。
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
        int height = Data.EnvironmentLayers != null ? Data.EnvironmentLayers.GridHeight : 0;
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
    public bool RemoveGrassAt(Vector2Int position)
    {
        GrassDetailLayer grassLayer = GetComponent<GrassDetailLayer>();
        return grassLayer != null && grassLayer.RemoveGrassAt(this, position);
    }

    public bool HasGrassAt(Vector2Int position)
    {
        GrassDetailLayer grassLayer = GetComponent<GrassDetailLayer>();
        return grassLayer != null && grassLayer.HasGrassAt(this, position);
    }

    public bool TryFindClosestGrass(Vector2 worldPosition, float searchRadius, out Vector2Int grassPosition)
    {
        GrassDetailLayer grassLayer = GetComponent<GrassDetailLayer>();
        if (grassLayer != null)
            return grassLayer.TryFindClosestGrass(this, worldPosition, searchRadius, out grassPosition);

        grassPosition = default;
        return false;
    }

    public bool PushTile(Vector2Int position, TileData tileData)
    {
        if (tileData == null)
            return false;

        tileData.position = (Vector3Int)position;
        if (!Data.PushTile(position, tileData))
            return false;
        UpdateTileBaseAtPosition(position);
        return true;
    }

    public bool SetBaseTile(Vector2Int position, TileData tileData, bool refreshVisual = true)
    {
        if (tileData == null)
            return false;

        tileData.position = (Vector3Int)position;
        if (!Data.SetBaseTile(position, tileData))
            return false;
        if (refreshVisual)
            UpdateTileBaseAtPosition(position);
        return true;
    }

    [Button("获取 TileData")]
    public TileData GetTile(Vector2Int position, int? index = null)
    {
        return index.HasValue
            ? Data.GetTileAt(position, index.Value)
            : Data.GetTopTile(position);
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
        var result = new List<TileData>(Data.GetLayerCount(position));
        Data.CopyStackTo(position, result);
        return result;
    }

    public bool RemoveTile(Vector2Int position, int? index = null)
    {
        if (!Data.RemoveTile(position, index))
            return false;

        UpdateTileBaseAtPosition(position);
        return true;
    }

    public bool UpdateTile(Vector2Int position, int index, TileData tileData)
    {
        if (tileData == null)
            return false;

        tileData.position = (Vector3Int)position;
        if (!Data.UpdateTileAt(position, index, tileData))
            return false;
        UpdateTileBaseAtPosition(position);
        return true;
    }

    public void UpdateTileBaseAtPosition(Vector2Int position)
    {
        WrappedTilemapCollisionProxy.MarkDirty(this);
        Vector3Int position3D = new Vector3Int(position.x, position.y, 0);

        if (!Data.TryGetStackView(position, out TileStackView stack) || stack.Count == 0)
        {
            tileMap.SetTile(position3D, null); // 清除该 Tile
            BlockingTilemapLayer.RefreshMapCell(this, position);
            GetComponent<GrassDetailLayer>()?.RefreshCell(this, position);
            Debug.Log($"清除了位置 {position} 上的 TileBase（无数据）");
            return;
        }

        TileData topTile = BlockingTilemapLayer.ResolveGroundTile(stack);
        if (topTile == null)
        {
            tileMap.SetTile(position3D, null);
            BlockingTilemapLayer.RefreshMapCell(this, position);
            GetComponent<GrassDetailLayer>()?.RefreshCell(this, position);
            return;
        }

        if (GameRes.Instance == null)
        {
            Debug.LogError("无法更新 TileBase：GameRes.Instance 为空");
            return;
        }

        // 通过位置拿到顶层 TileData.ID -> 通过 ID 找 Tile_Block SO -> 通过 SO 获取 TileBase
        Tile_Block tileBlock = GameRes.Instance.GetTileBlock(topTile.ID);
        if (tileBlock == null)
        {
            Debug.LogError($"无法加载 Tile_Block：{topTile.ID}，更新失败。");
            return;
        }

        TileBase tile = tileBlock.GetTileBaseAsset();

        if (tile == null)
        {
            Debug.LogError($"Tile_Block({tileBlock.name}) 的 TileBase 为空，更新失败。");
            return;
        }

        tileMap.SetTile(position3D, tile);
        BlockingTilemapLayer.RefreshMapCell(this, position);
        GetComponent<GrassDetailLayer>()?.RefreshCell(this, position);
        //Debug.Log($"已更新 TileBase 于位置 {position}，使用资源：{topTile.Name_TileBase}");
    }
    #endregion
}
