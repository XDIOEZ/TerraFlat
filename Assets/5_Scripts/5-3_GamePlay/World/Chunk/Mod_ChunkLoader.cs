using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// 区块加载器模块
/// 根据玩家位置动态加载、卸载和管理区块
/// </summary>
public class Mod_ChunkLoader : Module
{
    #region 数据结构
    /// <summary>
    /// 区块加载距离配置
    /// </summary>
    [System.Serializable]
    public struct ChunkDistanceConfig
    {
        [Tooltip("预取外圈距离；邻近一圈提前绑定画面，其余只预生成数据，不运行模拟")]
        public int UnActiveDistance;

        [Tooltip("区块销毁距离（超过此距离的区块将被销毁）")]
        public int DestroyChunkDistance;

        [Tooltip("区块加载距离（此距离内的区块将被加载）")]
        public int LoadChunkDistance;

        public ChunkDistanceConfig(int unActive = 7, int destroy = 8, int load = 5)
        {
            UnActiveDistance = unActive;
            DestroyChunkDistance = destroy;
            LoadChunkDistance = load;
        }
    }
    #endregion

    #region 序列化字段
    public Ex_ModData_MemoryPackable ModData;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }

    [Header("区块加载距离设置")]
    [SerializeField]
    private ChunkDistanceConfig distanceConfig = new ChunkDistanceConfig(7, 8, 5);

    [Header("性能节流")]
    [Tooltip("滑条调整区块范围后的窗口更新最小间隔（秒）；玩家跨区块时立即刷新")]
    [SerializeField, Min(0.01f)] private float chunkUpdateMinInterval = 0.08f;
    #endregion

    #region 运行时字段
    /// <summary>
    /// 是否需要更新区块
    /// </summary>
    private bool needsChunkUpdate = false;
    private float _lastChunkUpdateTime = -999f;
    
    /// <summary>当前用于流送的对称加载距离，由持久化区块配置直接决定。</summary>
    private Vector2Int _effectiveLoadDistance;
    private Vector2Int _effectivePrefetchDistance;
    private Vector2Int _effectiveDestroyDistance;
    private bool _effectiveDistancesInitialized;
    private bool _externalStreamingManaged;
    private Mod_Mover _movementSource;
    /// <summary>上次提交给区块窗口的中心；同一区块内移动只更新表现优先级。</summary>
    private Vector2Int _streamedChunkOrigin;
    private bool _hasStreamedChunkOrigin;
    #endregion

    #region 属性访问器
    private static bool CanStreamSurface => ChunkMgr.IsSurfaceStreamingScene;
    private int UnActiveDistance
    {
        get => distanceConfig.UnActiveDistance;
        set => distanceConfig.UnActiveDistance = value;
    }

    private int DestroyChunkDistance
    {
        get => distanceConfig.DestroyChunkDistance;
        set => distanceConfig.DestroyChunkDistance = value;
    }

    private int LoadChunkDistance
    {
        get => distanceConfig.LoadChunkDistance;
        set => distanceConfig.LoadChunkDistance = value;
    }

    public int CurrentLoadChunkDistance => Mathf.Max(EffectiveLoadDistance.x, EffectiveLoadDistance.y);
    public int CurrentUnActiveChunkDistance => Mathf.Max(EffectivePrefetchDistance.x, EffectivePrefetchDistance.y);
    public int CurrentDestroyChunkDistance => Mathf.Max(EffectiveDestroyDistance.x, EffectiveDestroyDistance.y);
    public Vector2Int CurrentLoadChunkDistanceXY => EffectiveLoadDistance;
    public Vector2Int CurrentUnActiveChunkDistanceXY => EffectivePrefetchDistance;
    public Vector2Int CurrentDestroyChunkDistanceXY => EffectiveDestroyDistance;

    private Vector2Int EffectiveLoadDistance => _effectiveDistancesInitialized
        ? _effectiveLoadDistance
        : new Vector2Int(LoadChunkDistance, LoadChunkDistance);
    private Vector2Int EffectivePrefetchDistance => _effectiveDistancesInitialized
        ? _effectivePrefetchDistance
        : new Vector2Int(UnActiveDistance, UnActiveDistance);
    private Vector2Int EffectiveDestroyDistance => _effectiveDistancesInitialized
        ? _effectiveDestroyDistance
        : new Vector2Int(DestroyChunkDistance, DestroyChunkDistance);
    #endregion

    #region 生命周期方法

    private void OnEnable()
    {
        GameManager.Event_PlayerEnterWorld += OnPlayerEnterWorld;
        BindMovementEvent();
    }

    private void OnDisable()
    {
        GameManager.Event_PlayerEnterWorld -= OnPlayerEnterWorld;
        UnbindMovementEvent();
        _hasStreamedChunkOrigin = false;
    }

    private void OnPlayerEnterWorld(Player player)
    {
        if (!CanStreamSurface) return;
        if (player != GetComponentInParent<Player>())
            return;
        if (_externalStreamingManaged)
            return;
        PrimeCenterChunkForWorldEntry();
    }

    private void OnValidate()
    {
        if (_Data != null)
            _Data.ID = ModText.ChunkLoader;
        NormalizeDistanceConfig();
    }

    protected override void OnLoad()
    {
        ModData.ReadData(ref distanceConfig);
        NormalizeDistanceConfig();
        ResetEffectiveDistancesFromConfig();
        _hasStreamedChunkOrigin = false;
        BindMovementEvent();
    }

    protected override void OnSave() => ModData.WriteData(distanceConfig);

    public override void ModUpdate(float deltaTime)
    {
        if (!CanStreamSurface)
        {
            needsChunkUpdate = false;
            _hasStreamedChunkOrigin = false;
            return;
        }
        if (_externalStreamingManaged)
            return;

        // 完整窗口刷新由 Mod_Mover 的跨区块事件驱动；圈内移动只更新表现队列的精确距离优先级。
        ChunkMgr.ExistingInstance?.RetargetRuntimePresentationQueue(
            transform.position, GetPresentationPreloadDistance());

        // 滑条连续修改区块距离时轻量节流，避免每一档都立即重建窗口。
        if (needsChunkUpdate && Time.unscaledTime - _lastChunkUpdateTime >= chunkUpdateMinInterval)
        {
            needsChunkUpdate = false;
            _lastChunkUpdateTime = Time.unscaledTime;
            UpdateChunks();
        }
    }

    #endregion

    #region 外部接口

    /// <summary>
    /// 联机模式由 NetworkChunkStreamingCoordinator 按所有玩家位置联合管理区块。
    /// </summary>
    public void SetExternalStreamingManaged(bool managed)
    {
        _externalStreamingManaged = managed;
        needsChunkUpdate = false;
        _hasStreamedChunkOrigin = false;
    }

    [Button("刷新周围区块")]
    public void RefreshChunksAroundPlayer()
    {
        needsChunkUpdate = false;
        if (!CanStreamSurface) return;

        if (ChunkMgr.Instance == null)
        {
            Debug.LogError("[区块加载器] ChunkMgr 未初始化，无法刷新区块", this);
            return;
        }

        UpdateChunks();
    }

    /// <summary>Immediately refreshes the canonical window after a local world wrap.</summary>
    public void RefreshAfterWorldWrap()
    {
        if (_externalStreamingManaged)
            return;

        RefreshChunksAroundPlayer();
    }

    /// <summary>
    /// 世界进入后按配置扩展区块窗口；相机缩放不改变加载距离。
    /// </summary>
    public void RefreshConfiguredChunkWindow()
    {
        RefreshChunksAroundPlayer();
    }

    /// <summary>
    /// 世界进入首阶段只请求玩家脚下区块，避免高并发地形生成让中心区块与外围区块争抢 CPU。
    /// GameManager 会在中心区块完整可用后再调用 RefreshConfiguredChunkWindow 扩展到配置范围。
    /// </summary>
    public void PrimeCenterChunkForWorldEntry()
    {
        if (_externalStreamingManaged || !CanStreamSurface)
            return;

        needsChunkUpdate = false;

        if (ChunkMgr.Instance == null)
        {
            Debug.LogError("[区块加载器] ChunkMgr 未初始化，无法准备玩家中心区块", this);
            return;
        }

        ChunkMgr.Instance.RefreshRuntimeWindow(
            transform.position,
            activeDistance: 1,
            destroyDistance: 1,
            includeLocalPresentation: true,
            prefetchDistance: 1);
    }

    #endregion

    #region 区块检测与更新

    /// <summary>绑定玩家唯一移动权威事件，避免区块加载器自己轮询或累计移动距离。</summary>
    private void BindMovementEvent()
    {
        Mod_Mover resolved = item?.itemMods?.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
        if (ReferenceEquals(_movementSource, resolved))
            return;

        UnbindMovementEvent();
        _movementSource = resolved;
        if (_movementSource != null)
            _movementSource.WorldUnitChanged += HandleWorldUnitChanged;
    }

    /// <summary>解除移动事件，避免玩家运行时重建后保留旧模块订阅。</summary>
    private void UnbindMovementEvent()
    {
        if (_movementSource == null)
            return;
        _movementSource.WorldUnitChanged -= HandleWorldUnitChanged;
        _movementSource = null;
    }

    /// <summary>玩家跨入新区块时立即刷新窗口；同一区块内只需逐帧更新画面任务的距离。</summary>
    private void HandleWorldUnitChanged(Vector2Int worldUnit)
    {
        if (_externalStreamingManaged || !CanStreamSurface)
            return;

        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager == null)
            return;
        Vector2Int currentOrigin = chunkManager.ResolveRuntimeChunkOrigin(
            new Vector2(worldUnit.x, worldUnit.y));
        if (_hasStreamedChunkOrigin && currentOrigin == _streamedChunkOrigin &&
            !needsChunkUpdate)
            return;

        needsChunkUpdate = false;
        _lastChunkUpdateTime = Time.unscaledTime;
        UpdateChunks(new Vector2(worldUnit.x + 0.5f, worldUnit.y + 0.5f));
    }

    /// <summary>可见圈外再提前绑定一圈画面；余下的一圈继续只预取数据。</summary>
    private Vector2Int GetPresentationPreloadDistance()
    {
        Vector2Int load = EffectiveLoadDistance;
        Vector2Int prefetch = EffectivePrefetchDistance;
        return new Vector2Int(
            Mathf.Min(load.x + 1, prefetch.x),
            Mathf.Min(load.y + 1, prefetch.y));
    }

    /// <summary>移动事件可传入权威坐标，避免物理插值中的 Transform 晚一帧跨区块。</summary>
    private void UpdateChunks(Vector2? eventCenter = null)
    {
        if (!CanStreamSurface) return;
        ChunkMgr chunkManager = ChunkMgr.Instance;
        if (chunkManager == null) return;
        Vector2 center = eventCenter ?? transform.position;

        chunkManager.RefreshRuntimeWindow(
            center,
            EffectiveLoadDistance,
            EffectiveDestroyDistance,
            includeLocalPresentation: true,
            prefetchDistance: EffectivePrefetchDistance,
            presentationDistance: GetPresentationPreloadDistance());
        _streamedChunkOrigin = chunkManager.ResolveRuntimeChunkOrigin(center);
        _hasStreamedChunkOrigin = true;
    }

    #endregion

    #region 工具方法

    [Button("调整加载距离")]
    public void AdjustLoadDistance(int adjustment)
    {
        SetLoadChunkDistance(LoadChunkDistance + adjustment);
    }

    /// <summary>设置玩家周围实际激活的区块圈数，预取与销毁圈保持原有间距。</summary>
    public void SetLoadChunkDistance(int distance)
    {
        int target = Mathf.Max(1, distance);
        if (target == LoadChunkDistance)
            return;

        int prefetchMargin = Mathf.Max(2, UnActiveDistance - LoadChunkDistance);
        int destroyMargin = Mathf.Max(1, DestroyChunkDistance - UnActiveDistance);
        LoadChunkDistance = target;
        UnActiveDistance = target + prefetchMargin;
        DestroyChunkDistance = UnActiveDistance + destroyMargin;
        ResetEffectiveDistancesFromConfig();
        needsChunkUpdate = true;
    }

    /// <summary>从持久化配置初始化对称流送窗口。</summary>
    private void ResetEffectiveDistancesFromConfig()
    {
        _effectiveLoadDistance = new Vector2Int(LoadChunkDistance, LoadChunkDistance);
        _effectivePrefetchDistance = new Vector2Int(UnActiveDistance, UnActiveDistance);
        _effectiveDestroyDistance = new Vector2Int(DestroyChunkDistance, DestroyChunkDistance);
        _effectiveDistancesInitialized = true;
    }

    /// <summary>保证可见、数据预取、保留三圈按顺序递增，并至少保留两圈数据预取。</summary>
    private void NormalizeDistanceConfig()
    {
        distanceConfig.LoadChunkDistance = Mathf.Max(1, distanceConfig.LoadChunkDistance);
        distanceConfig.UnActiveDistance = Mathf.Max(
            distanceConfig.LoadChunkDistance + 2,
            distanceConfig.UnActiveDistance);
        distanceConfig.DestroyChunkDistance = Mathf.Max(
            distanceConfig.UnActiveDistance + 1,
            distanceConfig.DestroyChunkDistance);
    }

    #endregion
}
