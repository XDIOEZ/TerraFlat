using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
using FlatWorld.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeChunkMgr = FlatWorld.WorldModel.ChunkMgr;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class ChunkMgr
{
    public const int MaxRuntimeChunkWorkCountPerFrame = 1024;
    public const float MinRuntimeChunkWorkMillisecondsPerFrame = 0.5f;
    public const float MaxRuntimeChunkWorkMillisecondsPerFrame = 16f;

    #region 性能分段
    // 保留原推进标记并观察剩余提交队列，生成线程耗时仍由流送诊断负责。
    private static readonly PerformanceDebugPoint WorldRuntimeAdvanceMarker =
        new("FlatWorld.ChunkStreaming.CommitAndTick");
    #endregion
    [Header("无头世界模型")]
    [SerializeField] private ChunkGenerationProfileSO defaultGenerationProfile;
    [SerializeField, Min(1)] private int backgroundGenerationConcurrency = 2;
    [Tooltip("主线程每帧最多提交多少个后台生成结果；达到毫秒预算后仍会提前停止。")]
    [SerializeField, Range(1, MaxRuntimeChunkWorkCountPerFrame)] private int maxChunkCommitsPerFrame = 64;
    [Tooltip("后台结果提交每帧最多占用的主线程毫秒数；单个提交仍会完整执行。")]
    [SerializeField, Range(MinRuntimeChunkWorkMillisecondsPerFrame, MaxRuntimeChunkWorkMillisecondsPerFrame)]
    private float maxChunkCommitMillisecondsPerFrame = 3f;
    [SerializeField] private bool authoritativeSimulation = true;

    private RuntimeChunkMgr runtimeChunkManager;
    private DeterministicChunkGenerator runtimeGenerator;
    private ChunkGenerationProfileSnapshot defaultGenerationSnapshot;
    private ChunkGenerationProfileSnapshot runtimeTileCatalogSnapshot;
    private ChunkGenerationProfileSnapshot activeGenerationSnapshot;
    private long runtimeEpoch;
    private WorldRuntimeHost runtimeHost;
    private IDisposable sightBlockingChunkCommittedSubscription;
    /// <summary>管理器进入终止阶段后，不再保存生态快照或重新创建世界运行时。</summary>
    internal bool IsWorldRuntimeShuttingDown { get; private set; }
    // 太空坐标不能拿来激活地表区块或生成一颗临时星球。
    public static bool IsSurfaceStreamingScene => !SpaceMgr.IsSpaceScene(GameplayWorldRegistry.ExecutionWorldKey) &&
        !GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.ExecutionWorldKey);

    public WorldRuntime WorldRuntime => runtimeChunkManager?.World;
    public IReadOnlyDictionary<RuntimeWorldAddress, ChunkRuntime> Chunks =>
        runtimeChunkManager?.Chunks ?? EmptyChunkRuntimeDictionary.Instance;
    public bool HasPendingChunkDataLoads => runtimeChunkManager?.HasPendingChunkLoads == true;
    public RuntimeChunkMgr RuntimeChunks => runtimeChunkManager;
    #region 区块提交预算

    /// <summary>每帧允许提交的后台生成结果数，GM 修改后立即生效。</summary>
    public int RuntimeChunkCommitBudget
    {
        get => Mathf.Clamp(maxChunkCommitsPerFrame, 1, MaxRuntimeChunkWorkCountPerFrame);
        set => maxChunkCommitsPerFrame = Mathf.Clamp(value, 1, MaxRuntimeChunkWorkCountPerFrame);
    }

    /// <summary>每帧提交生成结果可用的主线程毫秒数。</summary>
    public float RuntimeChunkCommitMillisecondsBudget
    {
        get => float.IsNaN(maxChunkCommitMillisecondsPerFrame) ? 3f :
            Mathf.Clamp(maxChunkCommitMillisecondsPerFrame,
                MinRuntimeChunkWorkMillisecondsPerFrame, MaxRuntimeChunkWorkMillisecondsPerFrame);
        set => maxChunkCommitMillisecondsPerFrame = float.IsNaN(value) ? 3f :
            Mathf.Clamp(value, MinRuntimeChunkWorkMillisecondsPerFrame,
                MaxRuntimeChunkWorkMillisecondsPerFrame);
    }

    #endregion
    /// <summary>当前世界实际提交给后台区块生成器的完整参数快照。</summary>
    public ChunkGenerationProfileSnapshot ActiveGenerationProfile =>
        IsSurfaceStreamingScene || GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.ExecutionWorldKey)
            ? activeGenerationSnapshot ?? defaultGenerationSnapshot : null;
    /// <summary>当前版本用于运行时玩家建筑的 Tile 映射；不随旧存档的生成配置冻结。</summary>
    public ChunkGenerationProfileSnapshot RuntimeTileCatalogProfile =>
        runtimeTileCatalogSnapshot ?? defaultGenerationSnapshot;
    public bool IsAuthoritativeSimulation
    {
        get => authoritativeSimulation;
        set => authoritativeSimulation = value;
    }

    /// <summary>先绑定主线程宿主，完整资源就绪后按真实世界请求创建纯世界运行时。</summary>
    protected override void Awake()
    {
        base.Awake();
        if (instance != this)
            return;
        WorldStreamingPreferences.Changed -= ApplyWorldStreamingPreferences;
        WorldStreamingPreferences.Changed += ApplyWorldStreamingPreferences;
        runtimeHost = GetComponent<WorldRuntimeHost>();
        if (runtimeHost == null)
            runtimeHost = gameObject.AddComponent<WorldRuntimeHost>();
        runtimeHost.Bind(this);
    }

    /// <summary>销毁场景时释放后台生成任务、区块数据和画面绑定。</summary>
    protected override void OnDestroy()
    {
        WorldStreamingPreferences.Changed -= ApplyWorldStreamingPreferences;
        try
        {
            if (!IsWorldRuntimeShuttingDown)
                ShutdownWorldRuntime(destroyingOwner: true);
        }
        finally
        {
            base.OnDestroy();
        }
    }

    /// <summary>请求生成一个区块的数据；真正的计算会交给后台生成器。</summary>
    public Task<ChunkRuntime> RequestChunkDataAsync(RuntimeWorldAddress address, int worldSeed,
        ChunkGenerationProfileSnapshot profile = null,
        CancellationToken cancellationToken = default,
        ChunkGenerationTopologySnapshot topology = default)
    {
        EnsureWorldRuntime();
        return runtimeChunkManager.RequestChunkDataAsync(address, worldSeed,
            profile ?? defaultGenerationSnapshot, cancellationToken, topology);
    }

    #region 新玩家直线出生搜索

    /// <summary>以正式生成配置在后台沿一条线寻找出生陆地，不注册搜索用区块。</summary>
    public Task<Int2?> FindSurfaceSpawnAsync(Int2 anchor, int sampleBudget,
        CancellationToken cancellationToken = default)
    {
        if (!IsSurfaceStreamingScene) return Task.FromResult<Int2?>(null);
        EnsureWorldRuntime();
        ChunkGenerationProfileSnapshot profile = PrepareActiveGenerationSnapshot(out int baseSeed);
        ChunkGenerationTopologySnapshot topology = ResolveActiveGenerationTopology();
        string dimensionId = ResolveCurrentDimensionId();
        int seed = DimensionManager.Instance != null
            ? DimensionManager.Instance.GetActiveGenerationSeed(baseSeed)
            : baseSeed;
        long epoch = runtimeChunkManager.World.Epoch;
        DeterministicChunkGenerator generator = runtimeGenerator;
        return Task.Run(() => generator.TryFindWalkableSurfaceAlongLine(
                dimensionId, seed, profile, topology, anchor,
                sampleBudget, out Int2 found, cancellationToken, epoch)
            ? (Int2?)found : null, cancellationToken);
    }

    #endregion

    #region 群系调试定位

    /// <summary>在主线程冻结当前世界上下文，后台仅使用正式生成器和纯数据查询群系。</summary>
    public Task<DeterministicChunkGenerator.BiomeSearchResult> FindSurfaceBiomeAsync(
        Int2 anchor, SurfaceBiomeKind biome, CancellationToken cancellationToken,
        DeterministicChunkGenerator.BiomeSearchProgress progress = null)
    {
        EnsureWorldRuntime();
        ChunkGenerationProfileSnapshot profile = PrepareActiveGenerationSnapshot(out int baseSeed);
        ChunkGenerationTopologySnapshot topology = ResolveActiveGenerationTopology();
        string dimensionId = ResolveCurrentDimensionId();
        int seed = DimensionManager.Instance != null
            ? DimensionManager.Instance.GetActiveGenerationSeed(baseSeed)
            : baseSeed;
        long epoch = runtimeChunkManager.World.Epoch;
        DeterministicChunkGenerator generator = runtimeGenerator;
        return Task.Run(() => generator.FindSurfaceBiome(dimensionId, seed, profile,
            topology, anchor, biome, cancellationToken, epoch, progress), cancellationToken);
    }

    #endregion

    /// <summary>尝试从当前运行时缓存中找到指定地址的区块。</summary>
    public bool TryGetChunkRuntime(RuntimeWorldAddress address, out ChunkRuntime chunk)
    {
        chunk = null;
        return runtimeChunkManager != null && runtimeChunkManager.TryGetChunk(address, out chunk);
    }

    /// <summary>为指定区块领取一张使用票，防止它在使用期间被回收。</summary>
    public ChunkLease AcquireChunkLease(RuntimeWorldAddress address, ChunkLeaseKind kind)
    {
        EnsureWorldRuntime();
        return runtimeChunkManager.AcquireLease(address, kind);
    }

    /// <summary>取消指定区块尚未完成的后台生成请求。</summary>
    public bool CancelChunkDataRequest(RuntimeWorldAddress address)
    {
        return runtimeChunkManager != null && runtimeChunkManager.CancelChunkRequest(address);
    }

    /// <summary>从运行时缓存中逐出指定区块，释放它占用的地形数据。</summary>
    public bool EvictChunkRuntime(RuntimeWorldAddress address)
    {
        return runtimeChunkManager != null && runtimeChunkManager.EvictChunk(address);
    }

    /// <summary>把世界坐标换算成所属维度和区块原点组成的标准地址。</summary>
    public RuntimeWorldAddress ResolveWorldAddress(Vector2 worldPosition, string dimensionId = null)
    {
        if (!IsSurfaceStreamingScene && !GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.ExecutionWorldKey))
        {
            // 旧物品地址查询可安全返回太空标签，但不初始化生成器或地表拓扑。
            int stepX = Math.Max(1, defaultGenerationSnapshot?.Width ?? 16);
            int stepY = Math.Max(1, defaultGenerationSnapshot?.Height ?? 16);
            return new RuntimeWorldAddress("space", new Int2(
                Mathf.FloorToInt(worldPosition.x / stepX) * stepX,
                Mathf.FloorToInt(worldPosition.y / stepY) * stepY));
        }
        // 已核对所有权的卸载阶段只用现有 Profile 换算坐标，不能重新初始化正在终止的世界。
        if (!isReleasingOwnedWorldContext) EnsureWorldRuntime();
        ChunkGenerationProfileSnapshot profile = ActiveGenerationProfile ?? defaultGenerationSnapshot;
        int width = Math.Max(1, profile.Width);
        int height = Math.Max(1, profile.Height);
        var origin = NormalizeChunkPosition(new Vector2Int(
            Mathf.FloorToInt(worldPosition.x / width) * width,
            Mathf.FloorToInt(worldPosition.y / height) * height));
        return new RuntimeWorldAddress(
            string.IsNullOrWhiteSpace(dimensionId) ? ResolveCurrentDimensionId() : dimensionId,
            new Int2(origin.x, origin.y));
    }

    /// <summary>把世界坐标换算成所属区块的左下角原点。</summary>
    public Vector2Int ResolveRuntimeChunkOrigin(Vector2 worldPosition)
    {
        RuntimeWorldAddress address = ResolveWorldAddress(worldPosition);
        return new Vector2Int(address.ChunkOrigin.X, address.ChunkOrigin.Y);
    }

    /// <summary>等待当前所有后台区块生成任务完成并提交结果。</summary>
    public Task SettleGenerationTasksAsync()
    {
        if (runtimeChunkManager == null) return Task.CompletedTask;
        return runtimeChunkManager.SettleGenerationTasksAsync();
    }

    /// <summary>取消所有后台生成任务，并逐帧等待它们安全结束。</summary>
    public System.Collections.IEnumerator CancelAndSettleGenerationCoroutine()
    {
        if (runtimeChunkManager == null)
            yield break;
        runtimeChunkManager.CancelAllRequests();
        while (runtimeChunkManager.HasUnsettledGenerationTasks)
        {
            runtimeChunkManager.CommitCompleted();
            if (runtimeChunkManager.HasUnsettledGenerationTasks)
                yield return null;
        }
        runtimeChunkManager.CommitCompleted();
    }

    /// <summary>按画面帧提交后台结果并维护数据与表现绑定。</summary>
    internal void AdvanceWorldStreamingFrame()
    {
        // 渲染资源先于对象销毁结束，退出窗口不再提交会触发表现绑定的新区块。
        if (IsWorldRuntimeShuttingDown || SharedSpriteMeshCache.IsSessionEnding)
            return;

        if (GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.ExecutionWorldKey))
        {
            runtimeChunkManager?.CommitCompleted(RuntimeChunkCommitBudget, RuntimeChunkCommitMillisecondsBudget);
            RestorePendingLocalWorldChunks();
            AdvanceLiquidFlowStreamingFrame();
            liquidBodies?.AdvanceStreamingFrame();
            return;
        }
        if (!IsSurfaceStreamingScene) return;

        RecordStreamingAdvance();
        using (WorldRuntimeAdvanceMarker.Measure())
        {
            runtimeChunkManager?.CommitCompleted(RuntimeChunkCommitBudget, RuntimeChunkCommitMillisecondsBudget);
            WorldRuntimeAdvanceMarker.SetGauge(runtimeChunkManager?.PendingCommitCount ?? 0);
        }
        ReconcileRuntimeWindowBindings();
        AdvanceLiquidFlowStreamingFrame();
        liquidBodies?.AdvanceStreamingFrame();
    }

    /// <summary>只在固定步推进世界和液体权威模拟。</summary>
    internal void SimulateWorldFixedStep(float deltaSeconds)
    {
        if (IsWorldRuntimeShuttingDown || SharedSpriteMeshCache.IsSessionEnding ||
            !GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.ExecutionWorldKey) && !IsSurfaceStreamingScene)
            return;
        if (authoritativeSimulation) runtimeChunkManager?.World.Tick(deltaSeconds);
        SimulateLiquidFlowFixedStep(deltaSeconds);
        liquidBodies?.SimulateFixedStep();
    }

    /// <summary>玩家修改性能模式后，立即调整后台线程数量。</summary>
    private void ApplyWorldStreamingPreferences()
    {
        ApplyChunkLoadSpeedToRuntimeScheduler();
    }

    #region 资源会话与世界生命周期

    /// <summary>创建世界模型、确定性生成器和后台任务调度器。</summary>
    private void InitializeWorldRuntime()
    {
        if (runtimeChunkManager != null || !IsSurfaceStreamingScene)
            return;

        LiquidTypeCatalog liquidTypes = GameRes.RequireWorldLiquidTypes();
        runtimeEpoch = Math.Max(1, runtimeEpoch + 1);
        defaultGenerationSnapshot = defaultGenerationProfile != null
            ? defaultGenerationProfile.CreateSnapshot()
            : CreateFallbackGenerationSnapshot();
        defaultGenerationSnapshot = ApplyWorldChunkSize(defaultGenerationSnapshot);
        runtimeTileCatalogSnapshot = defaultGenerationSnapshot;
        activeGenerationSnapshot = defaultGenerationSnapshot;
        string worldId = ResolveOwnedWorldRuntimeIdentity(OwnedWorldKey, GameplayWorldRegistry.ExecutionWorldKey);
        WorldRuntime world = GameplayWorldRegistry.GetOrCreateOwnedWorldModel(worldId, runtimeEpoch);
        var generator = new DeterministicChunkGenerator(liquidTypes,
            WorldGenerationRuntimeHooks.CaptureWorldModelTerrainDecorator(
                FlatWorld.Spaceflight.SpaceSurfaceTerrain.Apply), ChunkWeatherConfiguration.Load());
        AttachOwnedWorldRuntime(world, generator);
        try
        {
            // 重绑已有数据时不会再发布生成完成事件，主动重建当前世界的遮挡层。
            foreach (ChunkRuntime chunk in world.Chunks.Values)
                BuildingOccupancyRegistry.InitializeSightBlockingForChunk(chunk);
            liquidBodies = new WorldLiquidBodySystem(this, ResolveCurrentDimensionId());
        }
        catch
        {
            DetachWorldRuntimeFromScene();
            throw;
        }
    }

    // 管理器只借用会话世界；重新附着时始终使用新的生成器与调度器。
    internal void AttachOwnedWorldRuntime(WorldRuntime world, IChunkPureGenerator generator)
    {
        if (world == null || generator == null) throw new ArgumentNullException(world == null ? nameof(world) : nameof(generator));
        if (IsWorldRuntimeShuttingDown) throw new ObjectDisposedException(nameof(ChunkMgr));
        if (runtimeChunkManager != null) throw new InvalidOperationException("区块管理器已经附着世界模型。");
        if (!string.IsNullOrEmpty(OwnedWorldKey) && !string.Equals(OwnedWorldKey, world.WorldId, StringComparison.Ordinal))
            throw new InvalidOperationException("区块管理器与世界模型身份不一致。");
        GameplayWorldRegistry.ClaimOwnedWorldModel(world.WorldId, world, this);
        runtimeEpoch = world.Epoch;
        try
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            slowChunkLogCount = 0;
            slowChunkLogWindowStart = Time.realtimeSinceStartup;
            world.StreamingDiagnostics.SlowGenerationCompleted += LogSlowChunkGeneration;
#endif
            runtimeGenerator = generator as DeterministicChunkGenerator;
            runtimeChunkManager = new RuntimeChunkMgr(world, generator,
                EffectiveBackgroundGenerationConcurrency, GameplayWorldRegistry.UsesLocalCoordinates(world.WorldId)
                    ? IdentityWorldAddressNormalizer.Instance : new UnityWorldAddressNormalizer());
            sightBlockingChunkCommittedSubscription =
                world.Events.Subscribe<ChunkCommitted>(HandleSightBlockingChunkCommitted);
            SharedSpriteMeshCache.Clearing += MarkRuntimePresentationBackendsInvalidated;
        }
        catch
        {
            try
            {
                SharedSpriteMeshCache.Clearing -= MarkRuntimePresentationBackendsInvalidated;
                try { sightBlockingChunkCommittedSubscription?.Dispose(); }
                finally { runtimeChunkManager?.Dispose(); }
            }
            finally
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                world.StreamingDiagnostics.SlowGenerationCompleted -= LogSlowChunkGeneration;
#endif
                runtimeChunkManager = null;
                runtimeGenerator = null;
                sightBlockingChunkCommittedSubscription = null;
                GameplayWorldRegistry.DetachOwnedWorldModel(world.WorldId, world, this);
            }
            throw;
        }
    }

    // 世界模型优先沿用管理器已绑定的逻辑身份，场景只作为未绑定时的上下文来源。
    internal static string ResolveOwnedWorldRuntimeIdentity(string ownedWorldKey, string executionWorldKey)
    {
        if (!string.IsNullOrWhiteSpace(ownedWorldKey)) return ownedWorldKey;
        return string.IsNullOrWhiteSpace(executionWorldKey) ? "world" : executionWorldKey;
    }

    #region 视线遮挡层接入

    /// <summary>区块正式就绪后一次性接入已登记的动态遮挡格。</summary>
    private void HandleSightBlockingChunkCommitted(ChunkCommitted committed)
    {
        if (TryGetChunkRuntime(committed.Address, out ChunkRuntime chunk))
            BuildingOccupancyRegistry.InitializeSightBlockingForChunk(chunk);
        if (!GameplayWorldRegistry.UsesLocalCoordinates(OwnedWorldKey))
            EnqueueCommittedRuntimeChunkForWindowReconciliation(committed);
    }

    #endregion

    // 先保存并休眠生物，失败时保留原世界和场景，避免下一次进入丢失实体。
    private void PrepareRuntimeSceneUnbinding()
    {
        string worldKey = runtimeChunkManager?.World.WorldId;
        if (string.IsNullOrEmpty(worldKey)) worldKey = OwnedWorldKey;
        if (string.IsNullOrEmpty(worldKey)) worldKey = GameplayWorldRegistry.ExecutionWorldKey;
        if (string.IsNullOrEmpty(worldKey)) return;
        SaveDataMgr saveData = SaveDataMgr.Instance;
        if (saveData == null || !saveData.PrepareRuntimeWorldForSceneUnbinding(worldKey))
            throw new InvalidOperationException("[ChunkMgr] 世界实体保存失败，不能解绑场景。");
    }

    private void EnsureRuntimeSceneBindingsWereReleased()
    {
        if (activeRuntimeBindings.Count != 0)
            throw new InvalidOperationException("[ChunkMgr] 建筑快照或区块解绑失败，不能切换场景。");
    }

    private void CaptureTerrainAfterSceneUnbindingWork()
    {
        // View 解绑会结清天气地表工作，必须在世界管理器仍附着时再次采集地形。
        if (runtimeChunkManager != null &&
            !SaveDataMgr.Instance.CaptureRuntimeTerrainAfterSceneViewUnbinding(runtimeChunkManager.World.WorldId))
            throw new InvalidOperationException("[ChunkMgr] 地形快照失败，不能解绑场景。");
    }

    // 切换失败时让旧窗口重新恢复已经休眠的实体和液体观察。
    private void RestoreRuntimeSceneAfterUnbindingFailure()
    {
        foreach (RuntimeChunkBinding binding in activeRuntimeBindings.Values)
        {
            binding.RestoredEntitiesChunk = null;
            binding.ObservedLiquidChunk = null;
            binding.FailedRestoreChunk = null;
        }
        if (runtimeChunkManager == null || liquidBodies != null) return;
        try { liquidBodies = new WorldLiquidBodySystem(this, ResolveCurrentDimensionId()); }
        catch (Exception exception) { Debug.LogException(exception, this); }
    }

    /// <summary>切换场景时释放表现和生成器，世界数据继续由游戏会话持有。</summary>
    private void ResetWorldRuntimeForSceneChange(bool retainPresentationPool = false)
    {
        using var scope = runtimeChunkManager != null ? RequireRuntimeWorldContext() : default;
        try
        {
            PrepareRuntimeSceneUnbinding();
            ClearRuntimeWindowBindings(retainPresentationPool);
            EnsureRuntimeSceneBindingsWereReleased();
            StopLiquidBodies();
            StopLiquidFlowExperiment();
            CaptureTerrainAfterSceneUnbindingWork();
        }
        catch
        {
            RestoreRuntimeSceneAfterUnbindingFailure();
            throw;
        }
        WorldLiquidFlowObstacles.ClearWorld();
        WorldEntityRuntime.ReleaseWorld();
        ReleaseAirHumidityField();
        runtimeChunkManager?.ClearWindow();
        runtimeChunkManager?.CancelAllRequests();
        runtimeGenerator?.BeginNewWorldHydrologyEpoch(runtimeEpoch);
        runtimeChunkManager?.CommitCompleted();
        DetachWorldRuntimeFromScene();
    }

    /// <summary>关闭场景侧管理器，纯世界由会话继续持有。</summary>
    private void ShutdownWorldRuntime(bool destroyingOwner = false, bool presentationSceneUnloaded = false)
    {
        if (IsWorldRuntimeShuttingDown)
            return;

        GameplayWorldRegistry.WorldExecutionScope scope = default;
        bool canReleaseWorld = !destroyingOwner;
        try
        {
            if (!destroyingOwner)
                scope = runtimeChunkManager != null ? RequireRuntimeWorldContext() : default;
            else if (!presentationSceneUnloaded)
            {
                string worldKey = !string.IsNullOrEmpty(OwnedWorldKey) ? OwnedWorldKey : WorldRuntime?.WorldId;
                canReleaseWorld = GameplayWorldRegistry.TryUseExistingWorldForOwnerRelease(worldKey, this, out scope) ||
                    string.IsNullOrEmpty(OwnedWorldKey) && TryUseRuntimeWorldContext(out scope);
                if (canReleaseWorld)
                {
                    Scene scene = GameplayWorldRegistry.CurrentScene;
                    canReleaseWorld = scene.IsValid() && scene.isLoaded;
                }
            }

            isReleasingOwnedWorldContext = canReleaseWorld;
            if (canReleaseWorld)
            {
                try
                {
                    PrepareRuntimeSceneUnbinding();
                    ClearRuntimeWindowBindings();
                    EnsureRuntimeSceneBindingsWereReleased();
                    StopLiquidBodies();
                    StopLiquidFlowExperiment();
                    CaptureTerrainAfterSceneUnbindingWork();
                }
                catch (Exception exception)
                {
                    if (!destroyingOwner)
                    {
                        RestoreRuntimeSceneAfterUnbindingFailure();
                        throw;
                    }
                    // Unity 已开始销毁宿主时无法取消销毁，保留纯数据并只回收宿主自己的对象。
                    Debug.LogException(exception, this);
                    canReleaseWorld = false;
                    isReleasingOwnedWorldContext = false;
                }
            }

            IsWorldRuntimeShuttingDown = true;
            try
            {
                ReleaseAirHumidityField();
                if (!canReleaseWorld)
                {
                    StopLiquidBodies();
                    StopLiquidFlowExperiment();
                }
                if (runtimeHost != null)
                    runtimeHost.Bind(null);
                runtimeChunkManager?.CancelAllRequests();
                runtimeGenerator?.BeginNewWorldHydrologyEpoch(runtimeEpoch);
                if (canReleaseWorld)
                    WorldLiquidFlowObstacles.ClearWorld();
                else
                    ReleaseOrphanedRuntimeWindowBindings();
            }
            finally
            {
                // 即使表现清理异常，也必须让旧调度器停止并归还会话中的模型。
                try { if (canReleaseWorld) WorldEntityRuntime.ReleaseWorld(); }
                finally { DetachWorldRuntimeFromScene(); }
            }
        }
        finally
        {
            isReleasingOwnedWorldContext = false;
            scope.Dispose();
        }
    }

    // Scene 已消失时仅清理旧管理器拥有的对象，不补建场景也不销毁会话世界。
    internal void DetachAfterPresentationSceneUnloaded()
        => ShutdownWorldRuntime(destroyingOwner: true, presentationSceneUnloaded: true);

    /// <summary>释放场景侧索引和调度器，模型由会话在真正退出时释放。</summary>
    private void DetachWorldRuntimeFromScene()
    {
        RuntimeChunkMgr manager = runtimeChunkManager;
        SharedSpriteMeshCache.Clearing -= MarkRuntimePresentationBackendsInvalidated;
        try
        {
            try { StopLiquidBodies(); }
            finally
            {
                try
                {
                    runtimeBuildings.Clear();
                    ReleaseIndependentNaturalEntities();
                    ReleaseSharedNaturalEntities();
                }
                finally { sightBlockingChunkCommittedSubscription?.Dispose(); }
            }
        }
        finally
        {
            runtimeChunkManager = null;
            runtimeGenerator = null;
            sightBlockingChunkCommittedSubscription = null;
            activeGenerationSnapshot = null;
            runtimeTileCatalogSnapshot = null;
            defaultGenerationSnapshot = null;
            activeWorldGenerationCache.Clear();
            portalSourceGenerationCache.Clear();
            pairedCaveSource = pairedSurfaceSource = pairedGenerationProfile = null;
            pairedGenerator = null;
            if (manager != null)
            {
                WorldRuntime world = manager.World;
                try
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    world.StreamingDiagnostics.SlowGenerationCompleted -= LogSlowChunkGeneration;
#endif
                    manager.Dispose();
                }
                finally { GameplayWorldRegistry.DetachOwnedWorldModel(world.WorldId, world, this); }
            }
        }
    }

    /// <summary>确保世界运行时已经创建；未创建时立即初始化。</summary>
    private void EnsureWorldRuntime()
    {
        if (IsWorldRuntimeShuttingDown)
            throw new ObjectDisposedException(nameof(ChunkMgr));
        if (!IsSurfaceStreamingScene && !GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.ExecutionWorldKey))
            throw new InvalidOperationException("太空场景没有活动地表运行时，请按星球地址查询独立地表");
        if (runtimeChunkManager == null)
            InitializeWorldRuntime();
    }
    #endregion

    /// <summary>当正式配置不可用时创建一份最小的备用生成配置。</summary>
    private static ChunkGenerationProfileSnapshot CreateFallbackGenerationSnapshot()
    {
        Vector2 size = GetChunkSize();
        var numbers = new Dictionary<string, double>(StringComparer.Ordinal) { ["entity.spawnCount"] = 1d };
        DiscreteProbabilityConfiguration.Capture().WriteGenerationParameters(numbers);
        return new ChunkGenerationProfileSnapshot(
            "surface.fallback",
            DeterministicChunkGenerator.CurrentGenerationSignature,
            Mathf.Max(1, Mathf.RoundToInt(size.x)),
            Mathf.Max(1, Mathf.RoundToInt(size.y)),
            numbers);
    }

    /// <summary>把存档中的区块尺寸覆盖到生成 Profile，避免资源 Profile 与世界网格出现双真源。</summary>
    private static ChunkGenerationProfileSnapshot ApplyWorldChunkSize(
        ChunkGenerationProfileSnapshot profile, PlanetData planet = null)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        planet ??= SaveDataMgr.Instance?.GetCurrentPlanetData();
        if (planet == null)
            return profile;

        Vector2Int chunkSize = PlanetData.NormalizeChunkSize(planet.ChunkSize);
        return profile.WithChunkSize(chunkSize.x, chunkSize.y);
    }

    /// <summary>按指定维度 PlanetData 写入空间距离倍率；矿洞复核地表时不能误用当前矿洞数值。</summary>
    private static ChunkGenerationProfileSnapshot ApplyWorldSpatialDistanceScale(
        ChunkGenerationProfileSnapshot profile, PlanetData planet)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));
        float spatialDistanceScale = PlanetData.NormalizeSpatialDistanceScale(
            planet?.SpatialDistanceScale ?? PlanetData.DefaultSpatialDistanceScale);
        return profile.WithNumericParameter("world.spatialDistanceScale", spatialDistanceScale);
    }

    /// <summary>获取当前活动维度编号；没有维度管理器时退回逻辑世界身份。</summary>
    private static string ResolveCurrentDimensionId()
    {
        string current = GameplayWorldRegistry.ExecutionWorldKey;
        if (GameplayWorldRegistry.UsesLocalCoordinates(current)) return current;
        DimensionManager dimensionManager = DimensionManager.Instance;
        if (dimensionManager != null && dimensionManager.ActiveAddress.IsValid)
            return dimensionManager.ActiveAddress.DimensionId;
        string worldKey = GameplayWorldRegistry.ExecutionWorldKey;
        return string.IsNullOrWhiteSpace(worldKey) ? "surface" : worldKey;
    }

    /// <summary>读取当前有限世界边界，并转换成纯生成器可用的拓扑快照。</summary>
    private static ChunkGenerationTopologySnapshot ResolveActiveGenerationTopology()
    {
        if (GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.ExecutionWorldKey)) return default;
        PlanetData planet = SaveDataMgr.Instance?.GetCurrentPlanetData();
        return ResolveGenerationTopology(planet);
    }

    /// <summary>将指定维度保存的有限世界范围转换为后台生成器可用的纯拓扑快照。</summary>
    internal static ChunkGenerationTopologySnapshot ResolveGenerationTopology(PlanetData planet)
    {
        return WorldTopologyBounds.TryCreate(planet, out WorldTopologyBounds bounds)
            ? new ChunkGenerationTopologySnapshot(
                new Int2(bounds.Min.x, bounds.Min.y),
                new Int2(bounds.Span.x, bounds.Span.y))
            : default;
    }

    private sealed class EmptyChunkRuntimeDictionary :
        IReadOnlyDictionary<RuntimeWorldAddress, ChunkRuntime>
    {
        public static readonly EmptyChunkRuntimeDictionary Instance = new();
        public int Count => 0;
        public IEnumerable<RuntimeWorldAddress> Keys => Array.Empty<RuntimeWorldAddress>();
        public IEnumerable<ChunkRuntime> Values => Array.Empty<ChunkRuntime>();
        public ChunkRuntime this[RuntimeWorldAddress key] => throw new KeyNotFoundException();
        /// <summary>空缓存中不存在任何区块地址。</summary>
        public bool ContainsKey(RuntimeWorldAddress key) => false;
        /// <summary>空缓存始终查询失败，用于避免返回 null 字典。</summary>
        public bool TryGetValue(RuntimeWorldAddress key, out ChunkRuntime value)
        { value = null; return false; }
        /// <summary>返回一个没有元素的枚举器。</summary>
        public IEnumerator<KeyValuePair<RuntimeWorldAddress, ChunkRuntime>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<RuntimeWorldAddress, ChunkRuntime>>)
                Array.Empty<KeyValuePair<RuntimeWorldAddress, ChunkRuntime>>()).GetEnumerator();
        /// <summary>以非泛型方式返回一个没有元素的枚举器。</summary>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class UnityWorldAddressNormalizer : IWorldAddressNormalizer
    {
        /// <summary>把区块地址原点归一化到有限世界的标准坐标。</summary>
        public RuntimeWorldAddress Normalize(RuntimeWorldAddress address)
        {
            Vector2Int normalized = NormalizeChunkPosition(new Vector2Int(
                address.ChunkOrigin.X, address.ChunkOrigin.Y));
            return new RuntimeWorldAddress(address.DimensionId,
                new Int2(normalized.x, normalized.y));
        }
    }
}
