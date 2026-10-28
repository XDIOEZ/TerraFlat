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

public partial class Map : Item
{
    #region 属性和字段
    [Header("地图配置")]
    [SerializeField, FormerlySerializedAs("Data")]
    private Data_TileMap data = new Data_TileMap();

    public Data_TileMap Data => data;

    /// <summary>地图作为世界管理实体始终运行，区块生命周期独立于距离档。</summary>
    public override bool ShouldUseSimulationRange() => false;

    [Header("Tilemap 组件")]
    [SerializeField]
    public Tilemap tileMap;

    public UltEvent OnMapGenerated_Start = new UltEvent();
    public Chunk chunk;

    public GameObject ParentObject;

    // 协程引用管理，避免协程叠加
    public Coroutine loadTileMapCoroutine;
    public Coroutine loadOrGenerateCoroutine;

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

    private bool backTilePenaltyForceFull = true;
    [NonSerialized] private bool tilemapVisualReady;
    private readonly HashSet<Vector2Int> backTilePenaltyDirtyCells = new HashSet<Vector2Int>();

    private readonly MapTilemapRowPresenter tilemapPresenter = new MapTilemapRowPresenter();

    [SerializeReference]
    public List<ChunkGeneratorBase> mapGenerators = new List<ChunkGeneratorBase>();

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
    public int LastInitialRenderBatchCount => tilemapPresenter.BatchCount;
    public int LastInitialRenderTileCount => tilemapPresenter.TileCount;
    public event Action<Map, string> OnGenerationFailed;

    /// <summary>Tilemap 就绪、失效、增量刷新及启停的通用表现通知；不依赖 Item 实例化路径。</summary>
    internal static event Action<Map> TilemapPresentationChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPresentationEvents() => TilemapPresentationChanged = null;

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
        TilemapPresentationChanged?.Invoke(this);
    }

    protected void BeginMapLoad()
    {
        tilemapVisualReady = false;
        lastGenerationFailure = null;
        chunk?.BeginMapLoad();
        TilemapPresentationChanged?.Invoke(this);
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
        RebuildGeneratedLiquidVisuals();
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
            OnTilemapLoaded();
            TilemapPresentationChanged?.Invoke(this);
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
        TilemapPresentationChanged?.Invoke(this);
    }

    private void OnDisable()
    {
        WorldNavigationManager.ExistingInstance?.UnregisterMap(this);
        StopMapCoroutines();
        TilemapPresentationChanged?.Invoke(this);
    }

    private new void OnDestroy()
    {
        DisposeGeneratedLiquids();
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

        if (loadOrGenerateCoroutine != null)
        {
            StopCoroutine(loadOrGenerateCoroutine);
            loadOrGenerateCoroutine = null;
        }

        ReleaseProceduralGenerationSlot();

        activeGenerationContext = null;
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

            return;
        }

        navigation.QueueNavigationCells(backTilePenaltyDirtyCells);

        backTilePenaltyDirtyCells.Clear();

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
    /// <summary>消费旧地图草层中的一格草。</summary>
    public bool TryConsumeGrassAt(Vector2Int position)
    {
        GrassDetailLayer grassLayer = GetComponent<GrassDetailLayer>();
        return grassLayer != null && grassLayer.TryConsumeGrassAt(this, position);
    }

    /// <summary>兼容旧调用方；草不再作为 Item 实体处理。</summary>
    public bool RemoveGrassAt(Vector2Int position)
    {
        return TryConsumeGrassAt(position);
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
        TilemapPresentationChanged?.Invoke(this);
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

        // 旧地图同样通过稳定 ID 获取 JSON 运行时定义，不再读取 SO 中的配置。
        RuntimeTileDefinition tileBlock = GameRes.Instance.GetTileBlock(topTile.ID);
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
