using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.Diagnostics;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;
using Stopwatch = System.Diagnostics.Stopwatch;

public partial class ChunkMgr
{
    #region 性能分段
    // 只包本帧的启动和续步批次，不把协程等待时间计入绑定耗时。
    private static readonly PerformanceDebugPoint PresentationStartMarker = new("FlatWorld.ChunkStreaming.PresentationStartBatch");
    private static readonly PerformanceDebugPoint PresentationStepMarker = new("FlatWorld.ChunkStreaming.PresentationStepBatch");
    #endregion

    private sealed class RuntimeChunkBinding
    {
        public ChunkView View;
        public ChunkRuntime PendingChunk;
        // 地形差量与实体分别按区块数据实例恢复，避免窗口移动时重复覆盖运行中状态。
        public ChunkRuntime RestoredTerrainChunk;
        public ChunkRuntime RestoredEntitiesChunk;
        public ChunkRuntime ObservedLiquidChunk;
        public ChunkRuntime FailedRestoreChunk;
        public bool WantsPresentation;
        public bool PresentationQueued;
        public bool PresentationInProgress;
        public IEnumerator PresentationRoutine;
        // 诊断时间线：不影响排序或帧预算，重复窗口检查不能重置队龄。
        public long DiagnosticQueuedAt;
        public int DiagnosticPauseRevision;
        public bool DiagnosticBaseReported;
    }

    /// <summary>记录 ChunkView 的入池时刻与资源裁剪状态。</summary>
    private struct PooledChunkViewEntry
    {
        public PooledChunkViewEntry(ChunkView view, float pooledAt, bool completed = false)
        {
            View = view;
            PooledAt = pooledAt;
            ResourcesTrimmed = false;
            Completed = completed;
            RetainedForTransition = false;
            ResourceOwner = null;
            ResourceRevision = 0;
        }

        public ChunkView View;
        public float PooledAt;
        public bool ResourcesTrimmed;
        public bool Completed;
        public bool RetainedForTransition;
        public GameRes ResourceOwner;
        public ulong ResourceRevision;

        /// <summary>取出 View 并清除该池条目的时间与裁剪状态。</summary>
        public ChunkView TakeView()
        {
            ChunkView view = View;
            View = null;
            PooledAt = 0f;
            ResourcesTrimmed = false;
            return view;
        }
    }

    /// <summary>空闲阶段才提交的一项区块数据预取。</summary>
    private readonly struct RuntimePrefetchRequest
    {
        public RuntimePrefetchRequest(RuntimeWorldAddress address,
            ChunkGenerationProfileSnapshot profile, int seed,
            ChunkGenerationTopologySnapshot topology, int ring)
        {
            Address = address;
            Profile = profile;
            Seed = seed;
            Topology = topology;
            Ring = ring;
        }

        public RuntimeWorldAddress Address { get; }
        public ChunkGenerationProfileSnapshot Profile { get; }
        public int Seed { get; }
        public ChunkGenerationTopologySnapshot Topology { get; }
        public int Ring { get; }
    }

    [Header("区块表现分帧")]
    [Tooltip("主线程每帧最多启动多少个新区块；启动时优先完成基础地形 BRG，让扩大视距后先快速消除空白区块。")]
    [SerializeField, Range(1, MaxRuntimeChunkWorkCountPerFrame)] private int maxChunkPresentationsPerFrame = 64;
    [Tooltip("新区块基础地形每帧最多占用的主线程毫秒数；至少启动一个，单块绑定仍会完整执行。")]
    [SerializeField, Range(MinRuntimeChunkWorkMillisecondsPerFrame, MaxRuntimeChunkWorkMillisecondsPerFrame)]
    private float maxChunkPresentationStartMillisecondsPerFrame = 4f;
    [Tooltip("主线程每帧额外推进多少次已启动区块的后续表现绑定；与首层地形分离，避免草地/导航/自然物阻塞其它区块的基础地形。")]
    [SerializeField, Range(1, MaxRuntimeChunkWorkCountPerFrame)]
    private int maxChunkPresentationContinuationStepsPerFrame = 64;
    [Tooltip("已启动区块的后续表现每帧最多占用的主线程毫秒数；单个表现步骤仍会完整执行。")]
    [SerializeField, Range(MinRuntimeChunkWorkMillisecondsPerFrame, MaxRuntimeChunkWorkMillisecondsPerFrame)]
    private float maxChunkPresentationContinuationMillisecondsPerFrame = 3f;

    private const int MaxIdlePrefetchConcurrency = 1;
    private const int ChunkViewPoolSpareCapacity = 4;
    private const float PooledViewResourceTrimDelaySeconds = 7.5f;
    private const float ChunkViewPoolDestroyDelaySeconds = 10f;

    private readonly Dictionary<RuntimeWorldAddress, RuntimeChunkBinding> activeRuntimeBindings = new();
    private readonly HashSet<RuntimeWorldAddress> runtimeWindowTargets = new();
    private readonly HashSet<RuntimeWorldAddress> runtimeReadyTargets = new();
    private readonly HashSet<RuntimeWorldAddress> retiringRuntimeBindings = new();
    private readonly List<RuntimeWorldAddress> runtimeWindowRemovalBuffer = new();
    private readonly List<RuntimeWorldAddress> runtimePresentationQueue = new();
    private readonly List<RuntimeWorldAddress> runtimePresentationContinuationQueue = new();
    private readonly List<RuntimeWorldAddress> runtimePresentationRescheduleBuffer = new();
    private readonly Queue<RuntimePrefetchRequest> runtimePrefetchQueue = new();
    private readonly HashSet<RuntimeWorldAddress> runtimePrefetchTargets = new();
    private readonly List<RuntimePrefetchRequest> runtimePrefetchSortBuffer = new();
    private readonly Queue<PooledChunkViewEntry> chunkViewPool = new();
    private float nextChunkViewPoolMaintenanceAt;
    private int lastChunkViewPoolRetainedCapacity = -1;
    private Transform runtimeChunkViewRoot;
    private bool runtimeWindowUsesLocalPresentation;
    private bool runtimeWindowUsesObservers;
    private Rect runtimeActiveWindowBounds;
    private Rect runtimePresentationWindowBounds;
    private readonly ChunkWindowPresentationTransition runtimeWindowTransition = new();
    private int runtimeWindowTransitionFrame = -1;
    private string runtimeWindowDimensionId;
    private Vector2 runtimePresentationPriorityCenter;
    private Vector2Int runtimePresentationInterestOrigin;
    private Vector2Int runtimePresentationInterestDistance;
    private string runtimePresentationInterestDimensionId;
    private Coroutine runtimePresentationCoroutine;
    private int runtimePresentationInProgressCount;
    private Coroutine runtimePrefetchCoroutine;
    private RuntimeWorldAddress? runtimePrefetchInFlight;
    private int runtimePrefetchInFlightCount;

    /// <summary>仅所属地址的表现状态变化时通知实体系统，不要求实体每帧查询所有区块。</summary>
    public event Action<RuntimeWorldAddress> RuntimeEntityPresentationChanged;

    /// <summary>等待主线程绘制、碰撞和导航绑定的区块数量。</summary>
    public int PendingRuntimeChunkPresentationCount =>
        runtimePresentationQueue.Count + runtimePresentationInProgressCount;
    #region 区块表现预算

    /// <summary>每帧允许启动的基础地形表现数量。</summary>
    public int RuntimeChunkPresentationStartBudget
    {
        get => Mathf.Clamp(maxChunkPresentationsPerFrame, 1, MaxRuntimeChunkWorkCountPerFrame);
        set => maxChunkPresentationsPerFrame = Mathf.Clamp(value, 1, MaxRuntimeChunkWorkCountPerFrame);
    }

    /// <summary>每帧启动基础地形可用的主线程毫秒数。</summary>
    public float RuntimeChunkPresentationStartMillisecondsBudget
    {
        get => float.IsNaN(maxChunkPresentationStartMillisecondsPerFrame) ? 4f :
            Mathf.Clamp(maxChunkPresentationStartMillisecondsPerFrame,
                MinRuntimeChunkWorkMillisecondsPerFrame, MaxRuntimeChunkWorkMillisecondsPerFrame);
        set => maxChunkPresentationStartMillisecondsPerFrame = float.IsNaN(value) ? 4f :
            Mathf.Clamp(value, MinRuntimeChunkWorkMillisecondsPerFrame,
                MaxRuntimeChunkWorkMillisecondsPerFrame);
    }

    /// <summary>每帧允许推进的后续表现步骤数。</summary>
    public int RuntimeChunkPresentationContinuationBudget
    {
        get => Mathf.Clamp(maxChunkPresentationContinuationStepsPerFrame, 1, MaxRuntimeChunkWorkCountPerFrame);
        set => maxChunkPresentationContinuationStepsPerFrame = Mathf.Clamp(value, 1, MaxRuntimeChunkWorkCountPerFrame);
    }

    /// <summary>每帧后续表现步骤可用的主线程毫秒数。</summary>
    public float RuntimeChunkPresentationContinuationMillisecondsBudget
    {
        get => float.IsNaN(maxChunkPresentationContinuationMillisecondsPerFrame) ? 3f :
            Mathf.Clamp(maxChunkPresentationContinuationMillisecondsPerFrame,
                MinRuntimeChunkWorkMillisecondsPerFrame, MaxRuntimeChunkWorkMillisecondsPerFrame);
        set => maxChunkPresentationContinuationMillisecondsPerFrame = float.IsNaN(value) ? 3f :
            Mathf.Clamp(value, MinRuntimeChunkWorkMillisecondsPerFrame,
                MaxRuntimeChunkWorkMillisecondsPerFrame);
    }

    #endregion
    /// <summary>尚未完成的空闲预取总数，包含队列和正在运行的任务。</summary>
    public int PendingRuntimeChunkPrefetchCount =>
        runtimePrefetchQueue.Count + runtimePrefetchInFlightCount;
    /// <summary>当前真正进入纯生成管线的低优先级预取数量，正常不超过 1。</summary>
    public int RuntimeChunkPrefetchInFlightCount => runtimePrefetchInFlightCount;
    /// <summary>当前对象池实际保留的 ChunkView 数量。</summary>
    public int RetainedRuntimeChunkViewPoolCount => chunkViewPool.Count;
    /// <summary>按待展示区块数量动态计算的建议池容量。</summary>
    public int RecommendedRuntimeChunkViewPoolCapacity =>
        PendingRuntimeChunkPresentationCount + ChunkViewPoolSpareCapacity;

    /// <summary>
    /// 本地 WorldModel 区块窗口已经启用。掉落物等运行时实体在此状态下不得再触发旧 Chunk 加载。
    /// </summary>
    public bool IsWorldModelRuntimeActive =>
        IsSurfaceStreamingScene && runtimeChunkManager != null &&
        (runtimeWindowUsesLocalPresentation || runtimeWindowTargets.Count > 0);

    /// <summary>
    /// 当前活动视野内的区块是否都已经完成数据提交和 ChunkView 表现绑定。
    /// 维度切换使用它等待完整视野，不能只判断玩家脚下的中心区块。
    /// </summary>
    public bool AreRuntimeWindowPresentationsReady
    {
        get
        {
            if (!runtimeWindowUsesLocalPresentation || runtimeReadyTargets.Count == 0)
                return true;

            foreach (RuntimeWorldAddress address in runtimeReadyTargets)
            {
                if (!TryGetChunkRuntime(address, out ChunkRuntime chunk) ||
                    chunk == null ||
                    chunk.DataStatus != ChunkDataStatus.Ready ||
                    chunk.Terrain == null ||
                    !TryGetRuntimeChunkView(address, out _))
                    return false;
            }

            return true;
        }
    }

    #region 窗口刷新

    /// <summary>默认读取正式加载窗口，云雾可选择平滑表现窗口；队列重排不改变加载目标。</summary>
    public bool TryGetRuntimeWindowBounds(Vector2 presentationAnchor,
        out Rect activeBounds, out Rect presentationBounds, bool usePresentedBounds = false)
    {
        activeBounds = presentationBounds = default;
        if (!IsSurfaceStreamingScene || !runtimeWindowUsesLocalPresentation ||
            runtimeWindowTargets.Count == 0 || runtimeWindowDimensionId != ResolveCurrentDimensionId())
            return false;

        Rect active = usePresentedBounds && runtimeWindowTransition.IsInitialized
            ? runtimeWindowTransition.ActiveBounds : runtimeActiveWindowBounds;
        Rect presentation = usePresentedBounds && runtimeWindowTransition.IsInitialized
            ? runtimeWindowTransition.PresentationBounds : runtimePresentationWindowBounds;
        activeBounds = ProjectRuntimeWindowBounds(active, presentationAnchor);
        presentationBounds = new Rect(activeBounds.center - presentation.size * 0.5f, presentation.size);
        return true;
    }

    /// <summary>距离包含中心区块，窗口边长因此是 (2d-1) 个区块。</summary>
    public static Rect CalculateRuntimeWindowBounds(Vector2Int centerOrigin,
        Vector2Int chunkSize, Vector2Int distance)
    {
        Vector2Int radius = GetRuntimeWindowChunkRadius(distance);
        Vector2 size = new Vector2(Mathf.Max(1, chunkSize.x), Mathf.Max(1, chunkSize.y));
        Vector2 min = (Vector2)centerOrigin - Vector2.Scale(radius, size);
        return new Rect(min, Vector2.Scale(radius * 2 + Vector2Int.one, size));
    }

    /// <summary>整体投影窗口中心，跨世界接缝时不分别投影四角而把矩形翻转。</summary>
    public static Rect ProjectRuntimeWindowBounds(Rect logicalBounds, Vector2 presentationAnchor)
    {
        Vector2 center = WorldLocalPresentation.ProjectPosition(logicalBounds.center, presentationAnchor);
        return new Rect(center - logicalBounds.size * 0.5f, logicalBounds.size);
    }

    private static Vector2Int GetRuntimeWindowChunkRadius(Vector2Int distance) => new Vector2Int(
        Mathf.Max(0, distance.x - 1), Mathf.Max(0, distance.y - 1));

    /// <summary>尝试找到指定区块当前正在使用的画面对象。</summary>
    public bool TryGetRuntimeChunkView(RuntimeWorldAddress address, out ChunkView view)
    {
        view = null;
        if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
            return false;
        view = binding.View;
        return view != null && view.IsBound;
    }

    /// <summary>读取已经开始绑定的 ChunkView；允许返回仍在补草地、导航、自然物等后续表现的 View。</summary>
    public bool TryGetRuntimeChunkPresentationView(RuntimeWorldAddress address, out ChunkView view)
    {
        view = null;
        if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
            return false;
        view = binding.View;
        return view != null && (view.IsBinding || view.IsBound);
    }

    /// <summary>按世界坐标查找已绑定的新版区块画面。</summary>
    public bool TryGetRuntimeChunkView(Vector2 worldPosition, out ChunkView view)
    {
        view = null;
        if (!TryResolveRuntimeAddress(worldPosition, out RuntimeWorldAddress address))
            return false;
        return TryGetRuntimeChunkView(address, out view);
    }

    /// <summary>查找新版区块的临时物品节点，供掉落物统一归属。</summary>
    public bool TryGetRuntimeDropParent(Vector2 worldPosition,
        out ChunkNaturalItemRenderer renderer)
    {
        renderer = null;
        if (!TryGetRuntimeChunkView(worldPosition, out ChunkView view))
            return false;

        renderer = view.GetComponentInChildren<ChunkNaturalItemRenderer>(true);
        return renderer != null && renderer.isActiveAndEnabled;
    }

    /// <summary>当前位置的区块画面是否已完成绑定，可供本地实体安全显示。</summary>
    public bool IsRuntimeEntityPresentationReady(Vector2 worldPosition)
    {
        // 专用服务器或旧版区块流程不创建本地画面，不能因此暂停权威实体。
        if (!runtimeWindowUsesLocalPresentation || runtimeWindowTargets.Count == 0)
            return true;
        return TryGetRuntimeChunkView(worldPosition, out _);
    }

    /// <summary>只在世界进入超时等低频诊断路径生成中心区块状态，不进入正常帧热路径。</summary>
    public string DescribeRuntimeEntityPresentationWait(Vector2 worldPosition)
    {
        if (!runtimeWindowUsesLocalPresentation || runtimeWindowTargets.Count == 0)
            return "localPresentation=off";
        if (!TryResolveRuntimeAddress(worldPosition, out RuntimeWorldAddress address))
            return "address=unresolved";

        string chunkState = "missing";
        if (TryGetChunkRuntime(address, out ChunkRuntime runtimeChunk) && runtimeChunk != null)
            chunkState = $"{runtimeChunk.DataStatus}/terrain={(runtimeChunk.Terrain != null ? "ready" : "null")}";

        string bindingState = "missing";
        if (activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
        {
            string viewState = binding.View == null
                ? "none"
                : binding.View.IsBound ? "bound" : binding.View.IsBinding ? "binding" : "idle";
            ChunkGenerationProfileSnapshot profile = ActiveGenerationProfile;
            float priority = GetRuntimePresentationPriority(runtimePresentationPriorityCenter,
                address, profile.Width, profile.Height);
            bindingState = $"queued={binding.PresentationQueued},inProgress={binding.PresentationInProgress}," +
                           $"pendingChunk={(binding.PendingChunk != null)},priority={priority},view={viewState}";
        }

        return $"address={address},chunk={chunkState},binding=[{bindingState}]," +
               $"presentationPending={PendingRuntimeChunkPresentationCount}," +
               $"generationQueued={runtimeChunkManager?.QueuedGenerationCount ?? 0}," +
               $"generationActive={runtimeChunkManager?.ActiveGenerationCount ?? 0}," +
               $"commitPending={runtimeChunkManager?.PendingCommitCount ?? 0}";
    }

    /// <summary>使用当前生成 Profile 将世界坐标换算为新版区块地址。</summary>
    private bool TryResolveRuntimeAddress(Vector2 worldPosition,
        out RuntimeWorldAddress address)
    {
        address = default;
        if (runtimeChunkManager == null ||
            !runtimeWindowUsesLocalPresentation || runtimeWindowTargets.Count == 0)
            return false;

        ChunkGenerationProfileSnapshot profile = ActiveGenerationProfile;
        int stepX = Math.Max(1, profile?.Width ?? Mathf.RoundToInt(GetChunkSize().x));
        int stepY = Math.Max(1, profile?.Height ?? Mathf.RoundToInt(GetChunkSize().y));
        Vector2Int origin = NormalizeChunkPosition(new Vector2Int(
            Mathf.FloorToInt(worldPosition.x / stepX) * stepX,
            Mathf.FloorToInt(worldPosition.y / stepY) * stepY));
        address = new RuntimeWorldAddress(
            ResolveCurrentDimensionId(), new Int2(origin.x, origin.y));
        return true;
    }

    /// <summary>以玩家附近位置刷新区块窗口，启动生成、绑定画面并回收远处区块。</summary>
    public void RefreshRuntimeWindow(Vector2 center, int activeDistance, int destroyDistance,
        bool includeLocalPresentation, int prefetchDistance = 0, int presentationDistance = 0)
    {
        activeDistance = Mathf.Max(1, activeDistance);
        presentationDistance = presentationDistance <= 0
            ? activeDistance
            : Mathf.Max(activeDistance, presentationDistance);
        prefetchDistance = prefetchDistance <= 0
            ? presentationDistance
            : Mathf.Max(presentationDistance, prefetchDistance);
        destroyDistance = Mathf.Max(prefetchDistance, destroyDistance);
        RefreshRuntimeWindow(
            center,
            new Vector2Int(activeDistance, activeDistance),
            new Vector2Int(destroyDistance, destroyDistance),
            includeLocalPresentation,
            new Vector2Int(prefetchDistance, prefetchDistance),
            new Vector2Int(presentationDistance, presentationDistance));
    }

    /// <summary>按 X/Y 独立距离刷新矩形区块窗口，避免宽屏相机按最大边构造巨大正方形。</summary>
    public void RefreshRuntimeWindow(Vector2 center, Vector2Int activeDistance,
        Vector2Int destroyDistance, bool includeLocalPresentation,
        Vector2Int? prefetchDistance = null, Vector2Int? presentationDistance = null)
    {
        RefreshRuntimeWindows(center, activeDistance, destroyDistance, includeLocalPresentation,
            prefetchDistance, presentationDistance, null);
    }

    /// <summary>单人和多人共用窗口提交、存档恢复与表现调度。</summary>
    private void RefreshRuntimeWindows(Vector2 center, Vector2Int activeDistance,
        Vector2Int destroyDistance, bool includeLocalPresentation,
        Vector2Int? prefetchDistance, Vector2Int? presentationDistance,
        IReadOnlyList<Vector3> observerPositions)
    {
        if (!IsSurfaceStreamingScene) return;
        EnsureWorldRuntime();
        using var windowTiming = WorldRuntime.StreamingDiagnostics.Measure("window.refresh");
        WorldRuntime.StreamingDiagnostics.Count("window.refreshes");
        runtimeWindowUsesLocalPresentation = includeLocalPresentation;
        runtimeWindowUsesObservers = observerPositions != null;
        activeDistance = new Vector2Int(
            Mathf.Max(1, activeDistance.x),
            Mathf.Max(1, activeDistance.y));
        Vector2Int resolvedPresentation = presentationDistance ?? activeDistance;
        resolvedPresentation = new Vector2Int(
            Mathf.Max(activeDistance.x, resolvedPresentation.x),
            Mathf.Max(activeDistance.y, resolvedPresentation.y));
        Vector2Int resolvedPrefetch = prefetchDistance ?? resolvedPresentation;
        resolvedPrefetch = new Vector2Int(
            Mathf.Max(resolvedPresentation.x, resolvedPrefetch.x),
            Mathf.Max(resolvedPresentation.y, resolvedPrefetch.y));
        destroyDistance = new Vector2Int(
            Mathf.Max(resolvedPrefetch.x, destroyDistance.x),
            Mathf.Max(resolvedPrefetch.y, destroyDistance.y));
        string dimensionId = ResolveCurrentDimensionId();
        ChunkGenerationProfileSnapshot profile = PrepareActiveGenerationSnapshot(out int baseSeed);
        ChunkGenerationTopologySnapshot topology = ResolveActiveGenerationTopology();
        int stepX = profile.Width;
        int stepY = profile.Height;
        Vector2Int centerOrigin = NormalizeChunkPosition(new Vector2Int(
            Mathf.FloorToInt(center.x / stepX) * stepX,
            Mathf.FloorToInt(center.y / stepY) * stepY));
        DimensionManager dimensionManager = DimensionManager.Instance;
        int seed = dimensionManager != null
            ? dimensionManager.GetActiveGenerationSeed(baseSeed)
            : baseSeed;
        var centerAddress = new RuntimeWorldAddress(dimensionId,
            new Int2(centerOrigin.x, centerOrigin.y));
        runtimePresentationPriorityCenter = center;
        runtimePresentationInterestOrigin = centerOrigin;
        runtimePresentationInterestDistance = resolvedPresentation;
        runtimePresentationInterestDimensionId = dimensionId;
        if (runtimeWindowDimensionId != dimensionId || !includeLocalPresentation)
            runtimeWindowTransition.Reset();
        Vector2Int chunkSize = new Vector2Int(stepX, stepY);
        runtimeActiveWindowBounds = CalculateRuntimeWindowBounds(centerOrigin, chunkSize, activeDistance);
        runtimePresentationWindowBounds = CalculateRuntimeWindowBounds(centerOrigin, chunkSize, resolvedPresentation);
        runtimeWindowDimensionId = dimensionId;
        SampleRuntimeWindowTransition();

        IReadOnlyList<ChunkWindowRequest> windows = observerPositions != null
            ? CreateRuntimeObserverWindows(observerPositions, dimensionId, activeDistance,
                resolvedPresentation, destroyDistance, seed, profile, topology)
            : new[] { new ChunkWindowRequest(centerAddress,
                new Int2(activeDistance.x, activeDistance.y),
                new Int2(destroyDistance.x, destroyDistance.y), includeLocalPresentation,
                seed, profile, topology,
                dataDistance: new Int2(resolvedPresentation.x, resolvedPresentation.y),
                presentationDistance: new Int2(resolvedPresentation.x, resolvedPresentation.y)) };
        runtimeWindowTargets.Clear();
        runtimeReadyTargets.Clear();
        Vector2Int radius = GetRuntimeWindowChunkRadius(resolvedPresentation);
        for (int windowIndex = 0; windowIndex < windows.Count; windowIndex++)
        {
            Int2 windowOrigin = windows[windowIndex].Center.ChunkOrigin;
            for (int dx = -radius.x; dx <= radius.x; dx++)
            {
                for (int dy = -radius.y; dy <= radius.y; dy++)
                {
                    Vector2Int origin = NormalizeChunkPosition(new Vector2Int(
                        windowOrigin.X + dx * stepX, windowOrigin.Y + dy * stepY));
                    var address = new RuntimeWorldAddress(dimensionId, new Int2(origin.x, origin.y));
                    runtimeWindowTargets.Add(address);
                    if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
                    {
                        binding = new RuntimeChunkBinding();
                        activeRuntimeBindings.Add(address, binding);
                    }
                    binding.WantsPresentation = includeLocalPresentation;
                    retiringRuntimeBindings.Remove(address);
                }
            }
        }
        // 加载页只等待本机活动圈，远处玩家不扩大本机蒙版或重生等待范围。
        for (int dx = 1 - activeDistance.x; dx < activeDistance.x; dx++)
            for (int dy = 1 - activeDistance.y; dy < activeDistance.y; dy++)
            {
                Vector2Int origin = NormalizeChunkPosition(new Vector2Int(
                    centerOrigin.x + dx * stepX, centerOrigin.y + dy * stepY));
                runtimeReadyTargets.Add(new RuntimeWorldAddress(dimensionId, new Int2(origin.x, origin.y)));
            }

        runtimeWindowRemovalBuffer.Clear();
        foreach (KeyValuePair<RuntimeWorldAddress, RuntimeChunkBinding> pair in activeRuntimeBindings)
            if (!runtimeWindowTargets.Contains(pair.Key))
                runtimeWindowRemovalBuffer.Add(pair.Key);
        for (int i = 0; i < runtimeWindowRemovalBuffer.Count; i++)
            RetireRuntimeBinding(runtimeWindowRemovalBuffer[i]);

        runtimeChunkManager.RefreshWindows(windows);
        ReconcileRuntimeWindowBindings();
        ReprojectRuntimeChunkViewsToLocalAnchor();
        // 只在流送窗口发生变化时校验一次现有表现，不做定时轮询。
        RepairRuntimeWindowPresentationBackends();
        RebuildRuntimePrefetchQueue(centerOrigin, dimensionId, resolvedPresentation,
            resolvedPrefetch, stepX, stepY, profile, seed, topology, windows);
    }

    /// <summary>联机观察者在同区块内移动时只更新调度优先级，不裁剪其他玩家的窗口。</summary>
    public void UpdateRuntimePresentationPriority(Vector2 center) => runtimePresentationPriorityCenter = center;

    /// <summary>玩家跨区块时取消未出画面的旧任务，已呈现的旧地面等待雾交接后回收。</summary>
    public void RetargetRuntimePresentationQueue(Vector2 center, Vector2Int presentationDistance)
    {
        runtimePresentationPriorityCenter = center;
        if (!runtimeWindowUsesLocalPresentation || activeRuntimeBindings.Count == 0)
            return;

        ChunkGenerationProfileSnapshot profile = ActiveGenerationProfile;
        int stepX = profile.Width;
        int stepY = profile.Height;
        Vector2Int centerOrigin = NormalizeChunkPosition(new Vector2Int(
            Mathf.FloorToInt(center.x / stepX) * stepX,
            Mathf.FloorToInt(center.y / stepY) * stepY));
        string dimensionId = ResolveCurrentDimensionId();
        if (runtimePresentationInterestOrigin == centerOrigin &&
            runtimePresentationInterestDistance == presentationDistance &&
            runtimePresentationInterestDimensionId == dimensionId)
            return;

        runtimeWindowRemovalBuffer.Clear();
        foreach (KeyValuePair<RuntimeWorldAddress, RuntimeChunkBinding> pair in activeRuntimeBindings)
        {
            RuntimeWorldAddress address = pair.Key;
            Vector2Int origin = new Vector2Int(address.ChunkOrigin.X, address.ChunkOrigin.Y);
            Vector2Int delta = WorldTopologyRuntime.ShortestDelta(centerOrigin, origin);
            if (address.DimensionId != dimensionId ||
                Mathf.Abs(delta.x) / stepX >= presentationDistance.x ||
                Mathf.Abs(delta.y) / stepY >= presentationDistance.y)
            {
                runtimeWindowRemovalBuffer.Add(address);
                continue;
            }
        }

        for (int i = 0; i < runtimeWindowRemovalBuffer.Count; i++)
            RetireRuntimeBinding(runtimeWindowRemovalBuffer[i]);
        runtimeWindowRemovalBuffer.Clear();
        runtimePresentationInterestOrigin = centerOrigin;
        runtimePresentationInterestDistance = presentationDistance;
        runtimePresentationInterestDimensionId = dimensionId;
        ReprojectRuntimeChunkViewsToLocalAnchor();
    }

    /// <summary>只移动 ChunkView 表现根节点，ChunkRuntime/WorldAddress 完全不变。</summary>
    public void ReprojectRuntimeChunkViewsToLocalAnchor()
    {
        foreach (RuntimeChunkBinding binding in activeRuntimeBindings.Values)
        {
            if (binding.View != null)
                binding.View.RefreshLocalPresentationPosition();
        }
    }

    /// <summary>按玩家到区块中心的世界距离计算表现优先级，环绕世界取最短距离。</summary>
    private static float GetRuntimePresentationPriority(Vector2 center,
        RuntimeWorldAddress address, int stepX, int stepY)
    {
        var chunkCenter = new Vector2(address.ChunkOrigin.X + stepX * 0.5f,
            address.ChunkOrigin.Y + stepY * 0.5f);
        return WorldTopologyRuntime.ShortestDelta(center, chunkCenter).sqrMagnitude;
    }

    /// <summary>统一准备出生搜索与区块流送的生成快照，保证二者共享水文缓存键。</summary>
    internal ChunkGenerationProfileSnapshot PrepareActiveGenerationSnapshot(out int baseSeed)
    {
        EnsureWorldRuntime();
        ChunkGenerationProfileSO profileAsset = DimensionManager.Instance?.GetActiveGenerationProfile();
        ChunkGenerationProfileSnapshot profile = profileAsset != null
            ? profileAsset.CreateSnapshot()
            : defaultGenerationSnapshot;
        profile = ApplyWorldChunkSize(profile);
        profile = ApplyWorldSpatialDistanceScale(profile);
        profile = WorldGenerationRuntimeHooks.ApplyBeforeWorldModelGeneration(profile);
        // 玩家可建造 Tile 属于当前内容目录，不应被旧存档冻结的世界生成参数锁死。
        runtimeTileCatalogSnapshot = profile;
        profile = ApplyPersistedEcologyConfiguration(profile);
        baseSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        if (baseSeed == 0)
            baseSeed = 1;
        // 地表入口与矿洞出口共享门户随机种子；矿洞还需配对冻结后的地表 Profile。
        profile = profile.WithNumericParameter("cave.portal.baseSeed", baseSeed);
        profile = AttachCavePortalPairing(profile, baseSeed);
        activeGenerationSnapshot = profile;
        return profile;
    }

    #endregion

    #region 加载窗口表现交接

    private void SampleRuntimeWindowTransition()
    {
        if (!runtimeWindowUsesLocalPresentation)
        {
            runtimeWindowTransition.Reset();
            return;
        }

        Rect active = runtimeActiveWindowBounds;
        Rect presentation = runtimePresentationWindowBounds;
        if (runtimeWindowTransition.IsInitialized)
        {
            // 连续窗口先选同一世界镜像，环绕接缝不会把雾横扫整个地图。
            active = ProjectRuntimeWindowBounds(active, runtimeWindowTransition.ActiveBounds.center);
            presentation = new Rect(active.center - presentation.size * 0.5f, presentation.size);
        }
        float seconds = runtimeWindowTransitionFrame == Time.frameCount ? 0f : Mathf.Max(0f, Time.deltaTime);
        runtimeWindowTransition.Sample(active, presentation, seconds,
            WorldRenderingConfigCatalog.Default.zoomClouds.windowTransitionSeconds);
        runtimeWindowTransitionFrame = Time.frameCount;
    }

    private bool KeepRuntimeBindingForTransition(RuntimeWorldAddress address, RuntimeChunkBinding binding)
    {
        if (!runtimeWindowUsesLocalPresentation || !runtimeWindowTransition.IsInitialized ||
            address.DimensionId != runtimeWindowDimensionId || binding.View == null ||
            (!binding.View.IsBound && !binding.View.IsBaseTerrainPresented))
            return false;
        ChunkGenerationProfileSnapshot profile = ActiveGenerationProfile;
        Rect chunkBounds = new Rect(address.ChunkOrigin.X, address.ChunkOrigin.Y, profile.Width, profile.Height);
        chunkBounds = ProjectRuntimeWindowBounds(chunkBounds, runtimeWindowTransition.PresentationBounds.center);
        return runtimeWindowTransition.IntersectsPresentation(chunkBounds);
    }

    private void RetireRuntimeBinding(RuntimeWorldAddress address)
    {
        if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
            return;
        if (KeepRuntimeBindingForTransition(address, binding))
            retiringRuntimeBindings.Add(address);
        else
            DeactivateRuntimeBinding(address);
    }

    private void ReleaseRetiredRuntimeBindings()
    {
        if (retiringRuntimeBindings.Count == 0)
            return;
        runtimeWindowRemovalBuffer.Clear();
        foreach (RuntimeWorldAddress address in retiringRuntimeBindings)
        {
            if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding) ||
                !KeepRuntimeBindingForTransition(address, binding))
                runtimeWindowRemovalBuffer.Add(address);
        }
        for (int i = 0; i < runtimeWindowRemovalBuffer.Count; i++)
            DeactivateRuntimeBinding(runtimeWindowRemovalBuffer[i]);
        runtimeWindowRemovalBuffer.Clear();
    }

    #endregion

    #region 空闲数据预取

    /// <summary>重建均匀的外圈预取队列；不再根据玩家移动方向做预测。</summary>
    private void RebuildRuntimePrefetchQueue(Vector2Int centerOrigin, string dimensionId,
        Vector2Int activeDistance, Vector2Int prefetchDistance, int stepX, int stepY,
        ChunkGenerationProfileSnapshot profile, int seed,
        ChunkGenerationTopologySnapshot topology, IReadOnlyList<ChunkWindowRequest> windows)
    {
        runtimePrefetchQueue.Clear();
        runtimePrefetchTargets.Clear();
        int activeRadiusX = activeDistance.x - 1;
        int activeRadiusY = activeDistance.y - 1;
        int prefetchRadiusX = prefetchDistance.x - 1;
        int prefetchRadiusY = prefetchDistance.y - 1;
        // 视野移动会反复重排，复用临时数组避免每次创建排序列表。
        runtimePrefetchSortBuffer.Clear();
        for (int windowIndex = 0; windowIndex < windows.Count; windowIndex++)
        {
            Int2 windowOrigin = windows[windowIndex].Center.ChunkOrigin;
            for (int dx = -prefetchRadiusX; dx <= prefetchRadiusX; dx++)
            {
                for (int dy = -prefetchRadiusY; dy <= prefetchRadiusY; dy++)
                {
                    if (Mathf.Abs(dx) <= activeRadiusX && Mathf.Abs(dy) <= activeRadiusY)
                        continue;
                    Vector2Int origin = NormalizeChunkPosition(new Vector2Int(
                        windowOrigin.X + dx * stepX, windowOrigin.Y + dy * stepY));
                    var address = new RuntimeWorldAddress(dimensionId, new Int2(origin.x, origin.y));
                    if (runtimeWindowTargets.Contains(address) || !runtimePrefetchTargets.Add(address) ||
                        runtimePrefetchInFlight == address ||
                        TryGetChunkRuntime(address, out ChunkRuntime chunk) && chunk.DataStatus == ChunkDataStatus.Ready)
                        continue;
                    Vector2Int delta = WorldTopologyRuntime.ShortestDelta(centerOrigin, origin);
                    int ring = Mathf.Max(Mathf.Abs(delta.x) / stepX, Mathf.Abs(delta.y) / stepY);
                    runtimePrefetchSortBuffer.Add(new RuntimePrefetchRequest(address, profile, seed, topology, ring));
                }
            }
        }

        runtimePrefetchSortBuffer.Sort((left, right) =>
        {
            int ring = left.Ring.CompareTo(right.Ring);
            return ring != 0 ? ring : left.Address.CompareTo(right.Address);
        });
        for (int i = 0; i < runtimePrefetchSortBuffer.Count; i++)
            runtimePrefetchQueue.Enqueue(runtimePrefetchSortBuffer[i]);
        runtimePrefetchSortBuffer.Clear();

        if (runtimePrefetchInFlight is RuntimeWorldAddress inFlight &&
            !runtimePrefetchTargets.Contains(inFlight) &&
            !runtimeWindowTargets.Contains(inFlight))
            CancelChunkDataRequest(inFlight);

        if (runtimePrefetchQueue.Count > 0 && runtimePrefetchCoroutine == null &&
            isActiveAndEnabled)
            runtimePrefetchCoroutine = StartCoroutine(ProcessRuntimePrefetchQueue());
    }

    /// <summary>可见区块的数据与基础地面就绪后，领取少量外圈预取任务。</summary>
    private IEnumerator ProcessRuntimePrefetchQueue()
    {
        while (runtimePrefetchQueue.Count > 0 || runtimePrefetchInFlightCount > 0)
        {
            while (runtimePrefetchQueue.Count > 0 &&
                   runtimePrefetchInFlightCount < MaxIdlePrefetchConcurrency &&
                   !HasUrgentRuntimeChunkWork())
            {
                RuntimePrefetchRequest request = runtimePrefetchQueue.Dequeue();
                if (!runtimePrefetchTargets.Contains(request.Address) ||
                    TryGetChunkRuntime(request.Address, out ChunkRuntime chunk) &&
                    chunk.DataStatus == ChunkDataStatus.Ready)
                    continue;
                BeginRuntimePrefetch(request);
            }
            yield return null;
        }

        runtimePrefetchCoroutine = null;
    }

    /// <summary>活动圈的数据或基础地面仍未就绪时，暂停外圈预取；画面预加载圈可继续轮转。</summary>
    private bool HasUrgentRuntimeChunkWork()
    {
        foreach (RuntimeWorldAddress address in runtimeReadyTargets)
        {
            if (!TryGetChunkRuntime(address, out ChunkRuntime chunk) ||
                chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null)
                return true;
            if (runtimeWindowUsesLocalPresentation &&
                (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding) ||
                 binding.View == null || !binding.View.IsBaseTerrainPresented))
                return true;
        }
        return false;
    }

    /// <summary>启动一项低优先级预取；同一时刻默认只保留一个，避免抢占可见区块。</summary>
    private async void BeginRuntimePrefetch(RuntimePrefetchRequest request)
    {
        runtimePrefetchInFlight = request.Address;
        runtimePrefetchInFlightCount++;
        try
        {
            await RequestChunkDataAsync(request.Address, request.Seed, request.Profile,
                topology: request.Topology);
        }
        catch (OperationCanceledException)
        {
            // 玩家窗口变化后，过期预取可以直接取消。
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[ChunkMgr] 区块预取失败 {request.Address}: {exception.Message}", this);
        }
        finally
        {
            runtimePrefetchInFlightCount = Mathf.Max(0, runtimePrefetchInFlightCount - 1);
            if (runtimePrefetchInFlight == request.Address)
                runtimePrefetchInFlight = null;
        }
    }

    #endregion

    #region 分帧表现队列

    /// <summary>把已生成区块放入主线程表现队列，避免后台任务同时完成时集中绘制。</summary>
    private void QueueRuntimeChunkPresentation(RuntimeWorldAddress address,
        RuntimeChunkBinding binding, ChunkRuntime chunk)
    {
        if (chunk == null || chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null ||
            !activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding current) ||
            !ReferenceEquals(current, binding) || !binding.WantsPresentation)
            return;
        if (binding.View != null && binding.View.IsBound && ReferenceEquals(binding.View.Model, chunk))
            return;
        if (binding.PresentationInProgress && binding.View != null &&
            binding.View.IsBinding && ReferenceEquals(binding.View.Model, chunk))
            return;

        binding.PendingChunk = chunk;
        if (!binding.PresentationQueued)
        {
            binding.PresentationQueued = true;
            binding.DiagnosticQueuedAt = Stopwatch.GetTimestamp();
            binding.DiagnosticPauseRevision = WorldRuntime.StreamingDiagnostics.PauseRevision;
            binding.DiagnosticBaseReported = false;
            runtimePresentationQueue.Add(address);
        }

        if (runtimePresentationCoroutine == null && isActiveAndEnabled)
            runtimePresentationCoroutine = StartCoroutine(ProcessRuntimePresentationQueue());
    }

    /// <summary>
    /// 两阶段推进区块表现：先按距离启动新区块，再优先分步补齐基础地形和其它表现器。
    /// 这样高视距下不会因为单个区块的草地、导航、自然物等后续工作，长期阻塞其它已生成区块的地面显示。
    /// </summary>
    private IEnumerator ProcessRuntimePresentationQueue()
    {
        // 先等一帧收集同批后台结果，才能真正按玩家距离排序，而不是按任务回调顺序抢画。
        yield return null;
        try
        {
            while (!IsWorldRuntimeShuttingDown && !SharedSpriteMeshCache.IsSessionEnding &&
                   (runtimePresentationQueue.Count > 0 || runtimePresentationContinuationQueue.Count > 0))
            {
                if (!ProcessRuntimePresentationFrame()) break;
                // 同步世界作用域在本帧工作返回时结束，不能挂到下一次协程恢复。
                yield return null;
            }
        }
        finally { runtimePresentationCoroutine = null; }
    }

    /// <summary>每帧按管理器归属恢复查询上下文，启动、续步和取消共用同一同步边界。</summary>
    private bool ProcessRuntimePresentationFrame()
    {
        if (!TryUseRuntimeWorldContext(out IDisposable scope)) return false;
        using (scope)
        {
            int startBudget = RuntimeChunkPresentationStartBudget;
            long startDeadline = Stopwatch.GetTimestamp() + (long)(
                Stopwatch.Frequency * (double)RuntimeChunkPresentationStartMillisecondsBudget / 1000d);
            using (PresentationStartMarker.Measure())
            {
                int startedChunks = 0;
                while (startBudget-- > 0 &&
                       TryDequeueRuntimePresentation(out RuntimeWorldAddress address))
                {
                    StartRuntimeChunkPresentation(address);
                    startedChunks++;
                    if (Stopwatch.GetTimestamp() >= startDeadline)
                        break;
                }
                PresentationStartMarker.AddWork(startedChunks);
                PresentationStartMarker.SetGauge(runtimePresentationQueue.Count);
            }

            int continuationBudget = RuntimeChunkPresentationContinuationBudget;
            if (runtimePresentationQueue.Count > 0)
            {
                if (startBudget <= 0) WorldRuntime.StreamingDiagnostics.Count("view.start_count_limit_hits");
                if (Stopwatch.GetTimestamp() >= startDeadline) WorldRuntime.StreamingDiagnostics.Count("view.start_time_limit_hits");
            }
            long continuationDeadline = Stopwatch.GetTimestamp() + (long)(
                Stopwatch.Frequency * (double)RuntimeChunkPresentationContinuationMillisecondsBudget / 1000d);
            using (PresentationStepMarker.Measure())
            {
                int advancedChunks = 0;
                runtimePresentationRescheduleBuffer.Clear();
                while (continuationBudget-- > 0 && Stopwatch.GetTimestamp() < continuationDeadline &&
                       TryDequeueRuntimePresentationContinuation(out RuntimeWorldAddress continuationAddress))
                {
                    if (AdvanceRuntimeChunkPresentation(continuationAddress))
                        runtimePresentationRescheduleBuffer.Add(continuationAddress);
                    advancedChunks++;
                }
                // 本帧执行过的区块下帧才重入队列，防止一个耗时表现器连续吃满全部步骤。
                for (int i = 0; i < runtimePresentationRescheduleBuffer.Count; i++)
                {
                    RuntimeWorldAddress continuationAddress = runtimePresentationRescheduleBuffer[i];
                    if (activeRuntimeBindings.TryGetValue(continuationAddress, out RuntimeChunkBinding binding) &&
                        binding.PresentationInProgress && binding.PresentationRoutine != null &&
                        binding.WantsPresentation && binding.View != null)
                        runtimePresentationContinuationQueue.Add(continuationAddress);
                }
                runtimePresentationRescheduleBuffer.Clear();
                PresentationStepMarker.AddWork(advancedChunks);
                PresentationStepMarker.SetGauge(runtimePresentationContinuationQueue.Count);
            }

            // 即使本轮刚好清空也保留到下一帧，防止同帧晚到结果重新启动协程绕过预算。
            if (runtimePresentationContinuationQueue.Count > 0)
            {
                if (continuationBudget <= 0) WorldRuntime.StreamingDiagnostics.Count("view.step_count_limit_hits");
                if (Stopwatch.GetTimestamp() >= continuationDeadline) WorldRuntime.StreamingDiagnostics.Count("view.step_time_limit_hits");
            }
        }
        return true;
    }

    /// <summary>取出仍然有效且距离玩家最近的待绘制区块。</summary>
    private bool TryDequeueRuntimePresentation(out RuntimeWorldAddress address)
    {
        address = default;
        // 先移除失效条目，再选择优先项。倒序清理过程中删除较小索引会移动已选项，
        // 两件事混在同一遍历中会留下过期 bestIndex，导致队列越界并中断表现协程。
        for (int i = runtimePresentationQueue.Count - 1; i >= 0; i--)
        {
            RuntimeWorldAddress candidate = runtimePresentationQueue[i];
            if (!activeRuntimeBindings.TryGetValue(candidate, out RuntimeChunkBinding binding) ||
                !binding.PresentationQueued || !binding.WantsPresentation || binding.PendingChunk == null)
                runtimePresentationQueue.RemoveAt(i);
        }

        int bestIndex = -1;
        float bestPriority = float.MaxValue;
        ChunkGenerationProfileSnapshot profile = ActiveGenerationProfile;
        for (int i = 0; i < runtimePresentationQueue.Count; i++)
        {
            RuntimeWorldAddress candidate = runtimePresentationQueue[i];
            float priority = GetRuntimePresentationPriority(runtimePresentationPriorityCenter,
                candidate, profile.Width, profile.Height);
            if (priority < bestPriority)
            {
                bestIndex = i;
                bestPriority = priority;
            }
        }

        if (bestIndex < 0)
            return false;

        address = runtimePresentationQueue[bestIndex];
        runtimePresentationQueue.RemoveAt(bestIndex);
        if (activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding selected))
            selected.PresentationQueued = false;
        return true;
    }

    /// <summary>启动一个区块的增量表现，并立即推进首个表现器（ChunkTilemapRenderer）。</summary>
    private void StartRuntimeChunkPresentation(RuntimeWorldAddress address)
    {
        if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
            return;

        ChunkRuntime chunk = binding.PendingChunk;
        binding.PendingChunk = null;
        if (!binding.WantsPresentation || chunk == null ||
            runtimeChunkManager == null ||
            !runtimeChunkManager.TryGetChunk(address, out ChunkRuntime current) ||
            !ReferenceEquals(current, chunk) || current.DataStatus != ChunkDataStatus.Ready ||
            current.Terrain == null)
            return;

        if (binding.View != null && binding.View.IsBound && ReferenceEquals(binding.View.Model, current))
            return;

        using var startTiming = WorldRuntime.StreamingDiagnostics.Measure("view.start");
        if (binding.DiagnosticPauseRevision == WorldRuntime.StreamingDiagnostics.PauseRevision)
            WorldRuntime.StreamingDiagnostics.Record("view.queue_wait",
                ChunkStreamingDiagnostics.ElapsedMs(binding.DiagnosticQueuedAt));
        RecycleRuntimeChunkView(binding);
        ChunkView prefab = DimensionManager.Instance?.GetActiveChunkViewPrefab();
        if (prefab == null)
        {
            WorldRuntime.StreamingDiagnostics.Count("view.missing_prefab");
            return;
        }

        ChunkView view;
        using (WorldRuntime.StreamingDiagnostics.Measure("view.acquire"))
            view = AcquireChunkView(prefab);
        binding.View = view;
        try
        {
            view.gameObject.SetActive(true);
        }
        catch (Exception exception)
        {
            WorldRuntime.StreamingDiagnostics.Count("view.failed");
            RecycleRuntimeChunkView(binding);
            Debug.LogError($"[ChunkMgr] 区块主线程表现绑定失败 {address}: {exception}", this);
            return;
        }

        binding.PresentationRoutine = view.BindIncremental(
            WorldRuntime, current, includeNavigation: true, renderersPerFrame: 1);
        binding.PresentationInProgress = true;
        runtimePresentationInProgressCount++;
        WorldRuntime.StreamingDiagnostics.Count("view.started");

        // 第一次 MoveNext 建立基础地形绑定；扫描和上传继续使用同一个有界步骤队列。
        // 后续表现器按玩家实时距离继续推进，避免旧位置任务占住预算。
        if (AdvanceRuntimeChunkPresentation(address))
            runtimePresentationContinuationQueue.Add(address);
    }

    /// <summary>推进一个已启动区块的一步后续表现；返回 true 表示仍需继续。</summary>
    private bool AdvanceRuntimeChunkPresentation(RuntimeWorldAddress address)
    {
        if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding) ||
            !binding.PresentationInProgress || binding.PresentationRoutine == null)
            return false;

        if (IsWorldRuntimeShuttingDown || SharedSpriteMeshCache.IsSessionEnding ||
            !binding.WantsPresentation || binding.View == null)
        {
            FinishRuntimeChunkPresentation(binding);
            return false;
        }

        try
        {
            bool hasNext;
            using (WorldRuntime.StreamingDiagnostics.Measure("view.step"))
                hasNext = binding.PresentationRoutine.MoveNext();
            if (!binding.DiagnosticBaseReported && binding.View != null && binding.View.IsBaseTerrainPresented)
            {
                binding.DiagnosticBaseReported = true;
                WorldRuntime.StreamingDiagnostics.Count("view.base_ready");
                if (binding.DiagnosticPauseRevision == WorldRuntime.StreamingDiagnostics.PauseRevision)
                    WorldRuntime.StreamingDiagnostics.Record("view.base_latency",
                        ChunkStreamingDiagnostics.ElapsedMs(binding.DiagnosticQueuedAt));
            }
            if (hasNext)
                return true;
        }
        catch (Exception exception)
        {
            WorldRuntime.StreamingDiagnostics.Count("view.failed");
            FinishRuntimeChunkPresentation(binding);
            RecycleRuntimeChunkView(binding);
            Debug.LogError($"[ChunkMgr] 区块主线程分帧绑定失败 {address}: {exception}", this);
            return false;
        }

        FinishRuntimeChunkPresentation(binding);
        return false;
    }

    /// <summary>先清理过期绑定，再从后续表现队列取当前离玩家最近的区块。</summary>
    private bool TryDequeueRuntimePresentationContinuation(out RuntimeWorldAddress address)
    {
        address = default;
        for (int i = runtimePresentationContinuationQueue.Count - 1; i >= 0; i--)
        {
            RuntimeWorldAddress candidate = runtimePresentationContinuationQueue[i];
            if (!activeRuntimeBindings.TryGetValue(candidate, out RuntimeChunkBinding binding))
            {
                runtimePresentationContinuationQueue.RemoveAt(i);
                continue;
            }
            if (!binding.PresentationInProgress || binding.PresentationRoutine == null ||
                !binding.WantsPresentation || binding.View == null)
            {
                FinishRuntimeChunkPresentation(binding);
                runtimePresentationContinuationQueue.RemoveAt(i);
            }
        }

        int bestIndex = -1;
        float bestPriority = float.MaxValue;
        bool bestNeedsBase = false;
        ChunkGenerationProfileSnapshot profile = ActiveGenerationProfile;
        for (int i = 0; i < runtimePresentationContinuationQueue.Count; i++)
        {
            RuntimeWorldAddress candidate = runtimePresentationContinuationQueue[i];
            bool needsBase = !activeRuntimeBindings[candidate].View.IsBaseTerrainPresented;
            float priority = GetRuntimePresentationPriority(runtimePresentationPriorityCenter,
                candidate, profile.Width, profile.Height);
            // 基础地形拆步后仍先补地面，再让已完成地面的区块继续草地、导航和自然物。
            if (bestIndex < 0 || needsBase && !bestNeedsBase || needsBase == bestNeedsBase && priority < bestPriority)
            {
                bestIndex = i;
                bestPriority = priority;
                bestNeedsBase = needsBase;
            }
        }

        if (bestIndex < 0)
            return false;

        address = runtimePresentationContinuationQueue[bestIndex];
        runtimePresentationContinuationQueue.RemoveAt(bestIndex);
        return true;
    }

    /// <summary>结束一个增量绑定并释放枚举器；计数只允许回收一次。</summary>
    private void FinishRuntimeChunkPresentation(RuntimeChunkBinding binding)
    {
        if (binding == null)
            return;

        using var scope = binding.PresentationRoutine != null ? RequireRuntimeWorldContext() : null;
        if (binding.PresentationRoutine is IDisposable disposable)
            disposable.Dispose();
        binding.PresentationRoutine = null;
        if (!binding.PresentationInProgress)
            return;

        binding.PresentationInProgress = false;
        bool completed = binding.View != null && binding.View.IsBound;
        WorldRuntime?.StreamingDiagnostics.Count(completed ? "view.completed" : "view.cancelled");
        if (completed && WorldRuntime != null &&
            binding.DiagnosticPauseRevision == WorldRuntime.StreamingDiagnostics.PauseRevision)
            WorldRuntime.StreamingDiagnostics.Record("view.ready_latency",
                ChunkStreamingDiagnostics.ElapsedMs(binding.DiagnosticQueuedAt));
        runtimePresentationInProgressCount = Mathf.Max(0, runtimePresentationInProgressCount - 1);
    }

    /// <summary>优先从对象池取出 ChunkView，没有可用对象时才实例化新的。</summary>
    private ChunkView AcquireChunkView(ChunkView prefab)
    {
        Transform viewRoot = GetRuntimeChunkViewRoot();
        while (chunkViewPool.Count > 0)
        {
            // 取走后立即刷新下一次维护的截止时间，避免使用过时的队列元信息。
            nextChunkViewPoolMaintenanceAt = 0f;
            PooledChunkViewEntry entry = chunkViewPool.Dequeue();
            ChunkView pooled = entry.TakeView();
            if (pooled == null)
                continue;

            if (entry.RetainedForTransition &&
                (entry.ResourceOwner != GameRes.ExistingInstance ||
                 entry.ResourceRevision != GameRes.ExistingInstance?.ItemDefinitionRevision))
            {
                DestroyRuntimeChunkView(pooled);
                continue;
            }
            if (entry.RetainedForTransition) RuntimeChunkViewTransitionHits++;

            pooled.transform.SetParent(null, false);
            SceneManager.MoveGameObjectToScene(pooled.gameObject, viewRoot.gameObject.scene);
            pooled.transform.SetParent(viewRoot, false);
            pooled.PrepareForPoolReuse();
            pooled.PresentationChanged -= HandleRuntimeEntityPresentationChanged;
            pooled.PresentationChanged += HandleRuntimeEntityPresentationChanged;
            return pooled;
        }
        ChunkView created = Instantiate(prefab, viewRoot);
        RuntimeChunkViewCreatedCount++;
        created.PresentationChanged += HandleRuntimeEntityPresentationChanged;
        return created;
    }

    private void HandleRuntimeEntityPresentationChanged(RuntimeWorldAddress address)
    {
        if (activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding) && binding.View != null)
            GameplayWorldRegistry.RegisterPresentation(binding.View.gameObject);
        RuntimeEntityPresentationChanged?.Invoke(address);
    }

    /// <summary>
    /// 区块表现必须属于当前世界场景；ChunkMgr 随 WorldManager 常驻，但不能把自然物带入 DDOL 场景。
    /// </summary>
    private Transform GetRuntimeChunkViewRoot()
    {
        Scene activeScene = GameplayWorldRegistry.CurrentScene;
        if (!activeScene.IsValid() || !activeScene.isLoaded)
            throw new InvalidOperationException("[ChunkMgr] 当前没有可承载区块表现的已加载场景。");

        if (runtimeChunkViewRoot != null &&
            runtimeChunkViewRoot.gameObject.scene == activeScene)
        {
            return runtimeChunkViewRoot;
        }

        GameObject rootObject = new GameObject("RuntimeChunkViews");
        SceneManager.MoveGameObjectToScene(rootObject, activeScene);
        runtimeChunkViewRoot = rootObject.transform;
        return runtimeChunkViewRoot;
    }

    /// <summary>后台生成完成后修复画面绑定，处理旧任务晚到或区块被回收的情况。</summary>
    private void ReconcileRuntimeWindowBindings()
    {
        using var reconcileTiming = WorldRuntime != null
            ? WorldRuntime.StreamingDiagnostics.Measure("window.reconcile") : default;
        MaintainRuntimeChunkViewPool();
        SampleRuntimeWindowTransition();
        ReleaseRetiredRuntimeBindings();
        if (runtimeChunkManager == null || activeRuntimeBindings.Count == 0)
            return;
        foreach (KeyValuePair<RuntimeWorldAddress, RuntimeChunkBinding> pair in activeRuntimeBindings)
        {
            RuntimeChunkBinding binding = pair.Value;
            if (retiringRuntimeBindings.Contains(pair.Key))
                continue;
            if (!runtimeChunkManager.TryGetChunk(pair.Key, out ChunkRuntime current) ||
                current.DataStatus != ChunkDataStatus.Ready || current.Terrain == null)
                continue;

            if (ReferenceEquals(binding.FailedRestoreChunk, current))
                continue;
            try
            {
                if (!RestoreRuntimeChunkState(pair.Key, binding, current))
                    continue;
            }
            catch (Exception exception)
            {
                WorldRuntime.StreamingDiagnostics.Count("restore.failed");
                // 单个存档区块异常不能中断其它区块的主线程提交和表现。
                binding.FailedRestoreChunk = current;
                Debug.LogError($"[ChunkMgr] 区块状态恢复失败 {pair.Key}: {exception}", this);
                continue;
            }
            if (!binding.WantsPresentation)
                continue;

            if (binding.View != null && binding.View.IsBound &&
                ReferenceEquals(binding.View.Model, current))
                continue;
            if (binding.View != null && binding.View.IsBinding &&
                ReferenceEquals(binding.View.Model, current))
                continue;
            if (binding.PresentationQueued && ReferenceEquals(binding.PendingChunk, current))
                continue;

            QueueRuntimeChunkPresentation(pair.Key, binding, current);
        }
    }

    /// <summary>区块首次就绪时恢复地形，待物品管理器可用后恢复实体；同一数据实例只执行一次。</summary>
    private bool RestoreRuntimeChunkState(RuntimeWorldAddress address,
        RuntimeChunkBinding binding, ChunkRuntime chunk)
    {
        SaveDataMgr saveData = SaveDataMgr.Instance;
        if (saveData == null ||
            (GameNetwork.HasStateAuthority && saveData.SaveData == null))
            return false;

        if (!ReferenceEquals(binding.RestoredTerrainChunk, chunk))
        {
            using (WorldRuntime.StreamingDiagnostics.Measure("restore.terrain"))
                saveData.RestoreRuntimeTerrainForChunk(address, chunk);
            binding.RestoredTerrainChunk = chunk;
        }

        if (!ReferenceEquals(binding.RestoredEntitiesChunk, chunk) && ItemMgr.Instance != null)
        {
            bool restoredBuildings;
            using (WorldRuntime.StreamingDiagnostics.Measure("restore.buildings"))
                restoredBuildings = saveData.RestoreRuntimeBuildingsForChunk(address);
            if (!restoredBuildings) return false;
            bool restoredAi;
            using (WorldRuntime.StreamingDiagnostics.Measure("restore.entities"))
                restoredAi = saveData.RestoreRuntimeAiEntitiesForChunk(address);
            if (!restoredAi) return false;
            binding.RestoredEntitiesChunk = chunk;
        }

        if (ReferenceEquals(binding.RestoredEntitiesChunk, chunk) &&
            !ReferenceEquals(binding.ObservedLiquidChunk, chunk))
        {
            NotifyLiquidFlowChunkReady(chunk);
            binding.ObservedLiquidChunk = chunk;
        }

        return true;
    }

    /// <summary>
    /// 流送窗口刷新时检查已绑定区块的共享 BRG 登记；只处理当前可见窗口，不按时间轮询。
    /// </summary>
    private void RepairRuntimeWindowPresentationBackends()
    {
        foreach (RuntimeWorldAddress address in runtimeWindowTargets)
        {
            if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding) ||
                binding.View == null || !binding.View.IsBound)
                continue;
            binding.View.RepairPresentationBackendIfNeeded();
        }
    }

    #endregion

    #region 对象池与清理

    /// <summary>裁剪长期禁用 View 的历史资源，并延迟销毁超出动态容量的最早闲置项。</summary>
    private void MaintainRuntimeChunkViewPool()
    {
        if (chunkViewPool.Count == 0)
            return;

        float now = Time.realtimeSinceStartup;
        int retainedCapacity = RecommendedRuntimeChunkViewPoolCapacity;
        // 只在池内容、目标容量变化或真实裁剪/销毁期限到达时遍历。
        // 这是按资源生命周期触发的维护，不改变任何玩法更新频率。
        if (retainedCapacity == lastChunkViewPoolRetainedCapacity &&
            now < nextChunkViewPoolMaintenanceAt && chunkViewPool.Peek().View != null)
            return;

        int entryCount = chunkViewPool.Count;
        for (int i = 0; i < entryCount; i++)
        {
            PooledChunkViewEntry entry = chunkViewPool.Dequeue();
            if (entry.View != null)
                chunkViewPool.Enqueue(entry);
        }

        while (chunkViewPool.Count > retainedCapacity)
        {
            PooledChunkViewEntry oldest = chunkViewPool.Peek();
            if (oldest.View == null)
            {
                chunkViewPool.Dequeue();
                continue;
            }
            if (now - oldest.PooledAt < ChunkViewPoolDestroyDelaySeconds)
                break;

            oldest = chunkViewPool.Dequeue();
            DestroyRuntimeChunkView(oldest.TakeView());
        }

        entryCount = chunkViewPool.Count;
        for (int i = 0; i < entryCount; i++)
        {
            PooledChunkViewEntry entry = chunkViewPool.Dequeue();
            if (!entry.ResourcesTrimmed &&
                now - entry.PooledAt >= PooledViewResourceTrimDelaySeconds)
            {
                entry.View.TrimPooledResources();
                entry.ResourcesTrimmed = true;
            }

            chunkViewPool.Enqueue(entry);
        }

        lastChunkViewPoolRetainedCapacity = retainedCapacity;
        nextChunkViewPoolMaintenanceAt = float.PositiveInfinity;
        foreach (PooledChunkViewEntry entry in chunkViewPool)
        {
            if (!entry.ResourcesTrimmed)
                nextChunkViewPoolMaintenanceAt = Mathf.Min(nextChunkViewPoolMaintenanceAt,
                    entry.PooledAt + PooledViewResourceTrimDelaySeconds);
        }
        if (chunkViewPool.Count > retainedCapacity)
            nextChunkViewPoolMaintenanceAt = Mathf.Min(nextChunkViewPoolMaintenanceAt,
                chunkViewPool.Peek().PooledAt + ChunkViewPoolDestroyDelaySeconds);
    }

    /// <summary>解除当前 ChunkView 的全部租约并送回对象池。</summary>
    private void RecycleRuntimeChunkView(RuntimeChunkBinding binding)
    {
        if (binding?.View == null)
            return;

        ChunkView view = binding.View;
        using var scope = view.Model != null ? RequireRuntimeWorldContext() : null;
        binding.View = null;
        bool completed = view.IsBound;
        view.Unbind();
        view.gameObject.SetActive(false);
        // View 已属于创建时的世界根节点，回收不创建根节点，也不迁移到其它场景。
        chunkViewPool.Enqueue(new PooledChunkViewEntry(view, Time.realtimeSinceStartup, completed));
        nextChunkViewPoolMaintenanceAt = 0f;
    }

    /// <summary>销毁 View 前再次确保表现、导航等全部租约已经释放。</summary>
    private void DestroyRuntimeChunkView(ChunkView view)
    {
        if (view == null)
            return;

        using var scope = view.Model != null ? RequireRuntimeWorldContext() : null;
        view.Unbind();
        if (view.gameObject.activeSelf)
            view.gameObject.SetActive(false);
        if (Application.isPlaying)
            UnityEngine.Object.Destroy(view.gameObject);
        else
            UnityEngine.Object.DestroyImmediate(view.gameObject);
    }

    /// <summary>世界退出或管理器销毁时释放池内全部 View。</summary>
    private void DestroyRuntimeChunkViewPool()
    {
        nextChunkViewPoolMaintenanceAt = 0f;
        lastChunkViewPoolRetainedCapacity = -1;
        while (chunkViewPool.Count > 0)
        {
            PooledChunkViewEntry entry = chunkViewPool.Dequeue();
            DestroyRuntimeChunkView(entry.TakeView());
        }
        DestroySceneTransitionChunkPoolRoot();
    }

    /// <summary>先移除区块登记；正常流送回池，整个窗口关闭时直接销毁 View。</summary>
    private void DeactivateRuntimeBinding(RuntimeWorldAddress address, bool recycleView = true)
    {
        if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
        {
            retiringRuntimeBindings.Remove(address);
            return;
        }
        using var scope = binding.RestoredTerrainChunk != null || binding.PresentationRoutine != null ||
                          binding.View?.Model != null ? RequireRuntimeWorldContext() : null;
        // 建筑与地形共同离开表现窗；快照失败时保留绑定，不能留下无地形的半艘船。
        if (SaveDataMgr.Instance != null && !SaveDataMgr.Instance.TryUnloadRuntimeBuildingsForChunk(address))
        {
            retiringRuntimeBindings.Add(address);
            return;
        }
        retiringRuntimeBindings.Remove(address);
        if (binding.RestoredTerrainChunk != null)
            WeatherMgr.ExistingInstance?.FlushSurfaceWork(binding.RestoredTerrainChunk);
        FinishRuntimeChunkPresentation(binding);
        activeRuntimeBindings.Remove(address);
        binding.PresentationQueued = false;
        binding.PendingChunk = null;
        runtimePresentationQueue.Remove(address);
        runtimePresentationContinuationQueue.Remove(address);
        if (recycleView)
        {
            RecycleRuntimeChunkView(binding);
        }
        else
        {
            ChunkView view = binding.View;
            binding.View = null;
            DestroyRuntimeChunkView(view);
        }
    }

    /// <summary>所属注册已失效时只释放本管理器持有的画面、租约与队列。</summary>
    private void ReleaseOrphanedRuntimeWindowBindings()
    {
        // 注册记录已失效时仅撤本管理器持有的实例和租约，不按世界地址卸载其它管理器的建筑。
        Exception failure = null;
        foreach (RuntimeChunkBinding binding in activeRuntimeBindings.Values)
        {
            try { (binding.PresentationRoutine as IDisposable)?.Dispose(); }
            catch (Exception exception) { failure ??= exception; }
            binding.PresentationRoutine = null;
            ChunkView view = binding.View;
            binding.View = null;
            try { DestroyOrphanedRuntimeChunkView(view); }
            catch (Exception exception) { failure ??= exception; }
        }
        activeRuntimeBindings.Clear();
        while (chunkViewPool.Count > 0)
        {
            try { DestroyOrphanedRuntimeChunkView(chunkViewPool.Dequeue().TakeView()); }
            catch (Exception exception) { failure ??= exception; }
        }
        ClearRuntimeWindowBindings();
        if (failure != null) throw failure;
    }

    private void DestroyOrphanedRuntimeChunkView(ChunkView view)
    {
        if (view == null) return;
        try { view.Unbind(); }
        finally
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(view.gameObject);
            else UnityEngine.Object.DestroyImmediate(view.gameObject);
        }
    }

    /// <summary>清空全部区块画面绑定和窗口目标记录。</summary>
    private void ClearRuntimeWindowBindings(bool retainPresentationPool = false)
    {
        if (runtimePresentationCoroutine != null)
        {
            StopCoroutine(runtimePresentationCoroutine);
            runtimePresentationCoroutine = null;
        }
        if (runtimePrefetchCoroutine != null)
        {
            StopCoroutine(runtimePrefetchCoroutine);
            runtimePrefetchCoroutine = null;
        }

        // 太空往返只保留已完成的 View 壳，所有世界租约仍走正式解绑。
        runtimeWindowRemovalBuffer.Clear();
        runtimeWindowRemovalBuffer.AddRange(activeRuntimeBindings.Keys);
        for (int i = 0; i < runtimeWindowRemovalBuffer.Count; i++)
        {
            RuntimeWorldAddress address = runtimeWindowRemovalBuffer[i];
            bool reusable = retainPresentationPool && activeRuntimeBindings[address].View?.IsBound == true;
            DeactivateRuntimeBinding(address, recycleView: reusable);
        }
        runtimeWindowRemovalBuffer.Clear();
        runtimeWindowTargets.Clear();
        runtimeReadyTargets.Clear();
        retiringRuntimeBindings.Clear();
        runtimeWindowUsesLocalPresentation = false;
        runtimeWindowUsesObservers = false;
        runtimeActiveWindowBounds = runtimePresentationWindowBounds = default;
        runtimeWindowTransition.Reset();
        runtimeWindowTransitionFrame = -1;
        runtimeWindowDimensionId = null;
        runtimePresentationInterestDimensionId = null;
        runtimePresentationQueue.Clear();
        runtimePresentationContinuationQueue.Clear();
        runtimePresentationRescheduleBuffer.Clear();
        runtimePresentationInProgressCount = 0;
        runtimePrefetchQueue.Clear();
        runtimePrefetchTargets.Clear();
        runtimePrefetchSortBuffer.Clear();
        if (runtimePrefetchInFlight is RuntimeWorldAddress inFlight)
            CancelChunkDataRequest(inFlight);
        runtimePrefetchInFlight = null;
        runtimePrefetchInFlightCount = 0;
        if (retainPresentationPool) RetainSceneTransitionChunkViews();
        else DestroyRuntimeChunkViewPool();
        ChunkBatchRendererGroupService.ReleaseUnusedBackend();
    }

    #endregion
}

/// <summary>加载目标与可见窗口分离，雾边平滑交接期间保留仍会露出的旧画面。</summary>
public sealed class ChunkWindowPresentationTransition
{
    #region 窗口交接状态

    public bool IsInitialized { get; private set; }
    public Rect ActiveBounds { get; private set; }
    public Rect PresentationBounds { get; private set; }
    private Rect startActive;
    private Rect startPresentation;
    private Rect targetActive;
    private Rect targetPresentation;
    private float elapsedSeconds;

    public void Reset()
    {
        IsInitialized = false;
        ActiveBounds = PresentationBounds = startActive = startPresentation =
            targetActive = targetPresentation = default;
        elapsedSeconds = 0f;
    }

    public void Sample(Rect active, Rect presentation, float deltaSeconds, float transitionSeconds)
    {
        if (!IsInitialized || transitionSeconds <= 0f || !PresentationBounds.Overlaps(presentation))
        {
            ActiveBounds = startActive = targetActive = active;
            PresentationBounds = startPresentation = targetPresentation = presentation;
            elapsedSeconds = transitionSeconds;
            IsInitialized = true;
            return;
        }
        if (targetActive != active || targetPresentation != presentation)
        {
            // 折返或再次跨块从当前雾边接续，起点不退回上一次的区块边界。
            startActive = ActiveBounds;
            startPresentation = PresentationBounds;
            targetActive = active;
            targetPresentation = presentation;
            elapsedSeconds = 0f;
            return;
        }

        elapsedSeconds = Mathf.Min(transitionSeconds, elapsedSeconds + Mathf.Max(0f, deltaSeconds));
        float progress = Mathf.SmoothStep(0f, 1f, elapsedSeconds / transitionSeconds);
        ActiveBounds = Interpolate(startActive, targetActive, progress);
        PresentationBounds = Interpolate(startPresentation, targetPresentation, progress);
    }

    public bool IntersectsPresentation(Rect chunkBounds)
        => IsInitialized && PresentationBounds.Overlaps(chunkBounds);

    private static Rect Interpolate(Rect from, Rect to, float progress)
        => new Rect(Vector2.Lerp(from.position, to.position, progress), Vector2.Lerp(from.size, to.size, progress));

    #endregion
}
