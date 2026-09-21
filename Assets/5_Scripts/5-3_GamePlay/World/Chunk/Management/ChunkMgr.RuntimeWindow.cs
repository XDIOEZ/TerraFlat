using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class ChunkMgr
{
    private sealed class RuntimeChunkBinding
    {
        public ChunkView View;
        public ChunkRuntime PendingChunk;
        public bool WantsPresentation;
        public bool PresentationQueued;
        public bool PresentationInProgress;
        public IEnumerator PresentationRoutine;
        public int PresentationPriority;
    }

    /// <summary>记录 ChunkView 的入池时刻与资源裁剪状态。</summary>
    private struct PooledChunkViewEntry
    {
        public PooledChunkViewEntry(ChunkView view, float pooledAt)
        {
            View = view;
            PooledAt = pooledAt;
            ResourcesTrimmed = false;
        }

        public ChunkView View;
        public float PooledAt;
        public bool ResourcesTrimmed;

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
    [SerializeField, Min(1)] private int maxChunkPresentationsPerFrame = 1;
    [Tooltip("主线程每帧额外推进多少次已启动区块的后续表现绑定；与首层地形分离，避免草地/导航/自然物阻塞其它区块的基础地形。")]
    [SerializeField, Min(1)] private int maxChunkPresentationContinuationStepsPerFrame = 2;

    private const int MaxIdlePrefetchConcurrency = 1;
    private const int ChunkViewPoolSpareCapacity = 4;
    private const float PooledViewResourceTrimDelaySeconds = 7.5f;
    private const float ChunkViewPoolDestroyDelaySeconds = 10f;

    private readonly Dictionary<RuntimeWorldAddress, RuntimeChunkBinding> activeRuntimeBindings = new();
    private readonly HashSet<RuntimeWorldAddress> runtimeWindowTargets = new();
    private readonly List<RuntimeWorldAddress> runtimeWindowRemovalBuffer = new();
    private readonly List<RuntimeWorldAddress> runtimePresentationQueue = new();
    private readonly Queue<RuntimeWorldAddress> runtimePresentationContinuationQueue = new();
    private readonly Queue<RuntimePrefetchRequest> runtimePrefetchQueue = new();
    private readonly HashSet<RuntimeWorldAddress> runtimePrefetchTargets = new();
    private readonly Queue<PooledChunkViewEntry> chunkViewPool = new();
    private Transform runtimeChunkViewRoot;
    private bool runtimeWindowUsesLocalPresentation;
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
        runtimeChunkManager != null &&
        (runtimeWindowUsesLocalPresentation || runtimeWindowTargets.Count > 0);

    /// <summary>
    /// 当前活动视野内的区块是否都已经完成数据提交和 ChunkView 表现绑定。
    /// 维度切换使用它等待完整视野，不能只判断玩家脚下的中心区块。
    /// </summary>
    public bool AreRuntimeWindowPresentationsReady
    {
        get
        {
            if (!runtimeWindowUsesLocalPresentation || runtimeWindowTargets.Count == 0)
                return true;
            if (PendingRuntimeChunkPresentationCount > 0)
                return false;

            foreach (RuntimeWorldAddress address in runtimeWindowTargets)
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
        bool includeLocalPresentation, int prefetchDistance = 0)
    {
        activeDistance = Mathf.Max(1, activeDistance);
        prefetchDistance = prefetchDistance <= 0
            ? activeDistance
            : Mathf.Max(activeDistance, prefetchDistance);
        destroyDistance = Mathf.Max(prefetchDistance, destroyDistance);
        RefreshRuntimeWindow(
            center,
            new Vector2Int(activeDistance, activeDistance),
            new Vector2Int(destroyDistance, destroyDistance),
            includeLocalPresentation,
            new Vector2Int(prefetchDistance, prefetchDistance));
    }

    /// <summary>按 X/Y 独立距离刷新矩形区块窗口，避免宽屏相机按最大边构造巨大正方形。</summary>
    public void RefreshRuntimeWindow(Vector2 center, Vector2Int activeDistance,
        Vector2Int destroyDistance, bool includeLocalPresentation,
        Vector2Int? prefetchDistance = null)
    {
        EnsureWorldRuntime();
        runtimeWindowUsesLocalPresentation = includeLocalPresentation;
        activeDistance = new Vector2Int(
            Mathf.Max(1, activeDistance.x),
            Mathf.Max(1, activeDistance.y));
        Vector2Int resolvedPrefetch = prefetchDistance ?? activeDistance;
        resolvedPrefetch = new Vector2Int(
            Mathf.Max(activeDistance.x, resolvedPrefetch.x),
            Mathf.Max(activeDistance.y, resolvedPrefetch.y));
        destroyDistance = new Vector2Int(
            Mathf.Max(resolvedPrefetch.x, destroyDistance.x),
            Mathf.Max(resolvedPrefetch.y, destroyDistance.y));
        string dimensionId = ResolveCurrentDimensionId();
        ChunkGenerationProfileSO profileAsset = DimensionManager.Instance?.GetActiveGenerationProfile();
        ChunkGenerationProfileSnapshot profile = profileAsset != null
            ? profileAsset.CreateSnapshot()
            : defaultGenerationSnapshot;
        profile = ApplyWorldCoordinateScale(profile);
        profile = WorldGenerationRuntimeHooks.ApplyBeforeWorldModelGeneration(profile);
        // 玩家可建造 Tile 属于当前内容目录，不应被旧存档冻结的世界生成参数锁死。
        // 先保留当前版本映射，再恢复冻结生成配置；这样老世界也能使用后来新增的地板/建筑 Tile。
        runtimeTileCatalogSnapshot = profile;
        profile = ApplyPersistedEcologyConfiguration(profile);
        int baseSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        if (baseSeed == 0)
            baseSeed = 1;
        // 地表入口与矿洞出口必须共享同一份门户随机种子，不能使用各自维度派生种子。
        profile = profile.WithNumericParameter("cave.portal.baseSeed", baseSeed);
        // 矿洞额外带入地表冻结 Profile，后台才能独立复算入口候选与地表高度。
        profile = AttachCavePortalPairing(profile, baseSeed);
        activeGenerationSnapshot = profile;
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
        runtimeChunkManager.RefreshWindow(new ChunkWindowRequest(centerAddress,
            new Int2(activeDistance.x, activeDistance.y),
            new Int2(destroyDistance.x, destroyDistance.y),
            includeLocalPresentation, seed, profile, topology,
            dataDistance: new Int2(activeDistance.x, activeDistance.y)));

        runtimeWindowTargets.Clear();
        int radiusX = activeDistance.x - 1;
        int radiusY = activeDistance.y - 1;
        for (int dx = -radiusX; dx <= radiusX; dx++)
        {
            for (int dy = -radiusY; dy <= radiusY; dy++)
            {
                Vector2Int origin = NormalizeChunkPosition(new Vector2Int(
                    centerOrigin.x + dx * stepX,
                    centerOrigin.y + dy * stepY));
                var address = new RuntimeWorldAddress(dimensionId, new Int2(origin.x, origin.y));
                runtimeWindowTargets.Add(address);
                if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
                {
                    binding = new RuntimeChunkBinding();
                    activeRuntimeBindings.Add(address, binding);
                }

                binding.WantsPresentation = includeLocalPresentation;
                binding.PresentationPriority = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                BeginRuntimeChunkBinding(address, binding, profile, seed, topology);
            }
        }

        runtimeWindowRemovalBuffer.Clear();
        foreach (KeyValuePair<RuntimeWorldAddress, RuntimeChunkBinding> pair in activeRuntimeBindings)
        {
            if (!runtimeWindowTargets.Contains(pair.Key))
                runtimeWindowRemovalBuffer.Add(pair.Key);
        }
        for (int i = 0; i < runtimeWindowRemovalBuffer.Count; i++)
            DeactivateRuntimeBinding(runtimeWindowRemovalBuffer[i]);

        // 只在流送窗口发生变化时校验一次现有表现，不做定时轮询。
        RepairRuntimeWindowPresentationBackends();
        RebuildRuntimePrefetchQueue(centerOrigin, dimensionId, activeDistance,
            resolvedPrefetch, stepX, stepY, profile, seed, topology);
    }

    #endregion

    #region 空闲数据预取

    /// <summary>重建均匀的外圈预取队列；不再根据玩家移动方向做预测。</summary>
    private void RebuildRuntimePrefetchQueue(Vector2Int centerOrigin, string dimensionId,
        Vector2Int activeDistance, Vector2Int prefetchDistance, int stepX, int stepY,
        ChunkGenerationProfileSnapshot profile, int seed,
        ChunkGenerationTopologySnapshot topology)
    {
        runtimePrefetchQueue.Clear();
        runtimePrefetchTargets.Clear();
        int activeRadiusX = activeDistance.x - 1;
        int activeRadiusY = activeDistance.y - 1;
        int prefetchRadiusX = prefetchDistance.x - 1;
        int prefetchRadiusY = prefetchDistance.y - 1;
        var requests = new List<RuntimePrefetchRequest>();
        for (int dx = -prefetchRadiusX; dx <= prefetchRadiusX; dx++)
        {
            for (int dy = -prefetchRadiusY; dy <= prefetchRadiusY; dy++)
            {
                int ring = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                if (Mathf.Abs(dx) <= activeRadiusX && Mathf.Abs(dy) <= activeRadiusY)
                    continue;

                Vector2Int origin = NormalizeChunkPosition(new Vector2Int(
                    centerOrigin.x + dx * stepX,
                    centerOrigin.y + dy * stepY));
                var address = new RuntimeWorldAddress(dimensionId,
                    new Int2(origin.x, origin.y));
                if (!runtimePrefetchTargets.Add(address) ||
                    runtimePrefetchInFlight == address ||
                    TryGetChunkRuntime(address, out ChunkRuntime chunk) &&
                    chunk.DataStatus == ChunkDataStatus.Ready)
                    continue;
                requests.Add(new RuntimePrefetchRequest(address, profile, seed, topology, ring));
            }
        }

        requests.Sort((left, right) =>
        {
            int ring = left.Ring.CompareTo(right.Ring);
            return ring != 0 ? ring : left.Address.CompareTo(right.Address);
        });
        for (int i = 0; i < requests.Count; i++)
            runtimePrefetchQueue.Enqueue(requests[i]);

        if (runtimePrefetchInFlight is RuntimeWorldAddress inFlight &&
            !runtimePrefetchTargets.Contains(inFlight) &&
            !runtimeWindowTargets.Contains(inFlight))
            CancelChunkDataRequest(inFlight);

        if (runtimePrefetchQueue.Count > 0 && runtimePrefetchCoroutine == null &&
            isActiveAndEnabled)
            runtimePrefetchCoroutine = StartCoroutine(ProcessRuntimePrefetchQueue());
    }

    /// <summary>只有可见区块没有数据缺口且表现队列为空时，才领取少量预取任务。</summary>
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

    /// <summary>可见区块数据或画面仍未就绪时，暂停一切外圈预取。</summary>
    private bool HasUrgentRuntimeChunkWork()
    {
        if (PendingRuntimeChunkPresentationCount > 0)
            return true;
        foreach (RuntimeWorldAddress address in runtimeWindowTargets)
        {
            if (!TryGetChunkRuntime(address, out ChunkRuntime chunk) ||
                chunk.DataStatus != ChunkDataStatus.Ready)
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

    /// <summary>等待区块数据生成完成，再把区块绑定到可复用的 ChunkView。</summary>
    private async void BeginRuntimeChunkBinding(RuntimeWorldAddress address,
        RuntimeChunkBinding binding, ChunkGenerationProfileSnapshot snapshot, int seed,
        ChunkGenerationTopologySnapshot topology)
    {
        try
        {
            ChunkRuntime chunk = await RequestChunkDataAsync(address, seed, snapshot,
                topology: topology);
            if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding current) ||
                !ReferenceEquals(current, binding))
                return;
            SaveDataMgr.Instance?.RestoreRuntimeTerrainForChunk(address, chunk);
            SaveDataMgr.Instance?.RestoreRuntimeBuildingsForChunk(address);
            SaveDataMgr.Instance?.RestoreRuntimeAiEntitiesForChunk(address);
            if (!binding.WantsPresentation)
                return;
            QueueRuntimeChunkPresentation(address, binding, chunk);
        }
        catch (OperationCanceledException)
        {
            // Window moved before generation finished.
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ChunkMgr] 无头区块绑定失败 {address}: {exception}", this);
        }
    }

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
            runtimePresentationQueue.Add(address);
        }

        if (runtimePresentationCoroutine == null && isActiveAndEnabled)
            runtimePresentationCoroutine = StartCoroutine(ProcessRuntimePresentationQueue());
    }

    /// <summary>
    /// 两阶段推进区块表现：先按距离启动新区块并立即绑定基础地形，再轮转补齐其它表现器。
    /// 这样高视距下不会因为单个区块的草地、导航、自然物等后续工作，长期阻塞其它已生成区块的地面显示。
    /// </summary>
    private IEnumerator ProcessRuntimePresentationQueue()
    {
        // 先等一帧收集同批后台结果，才能真正按玩家距离排序，而不是按任务回调顺序抢画。
        yield return null;
        while (runtimePresentationQueue.Count > 0 || runtimePresentationContinuationQueue.Count > 0)
        {
            int startBudget = Mathf.Max(1, maxChunkPresentationsPerFrame);
            while (startBudget-- > 0 &&
                   TryDequeueRuntimePresentation(out RuntimeWorldAddress address))
            {
                StartRuntimeChunkPresentation(address);
            }

            int continuationBudget = Mathf.Max(1, maxChunkPresentationContinuationStepsPerFrame);
            while (continuationBudget-- > 0 &&
                   TryDequeueRuntimePresentationContinuation(out RuntimeWorldAddress continuationAddress))
            {
                if (AdvanceRuntimeChunkPresentation(continuationAddress))
                    runtimePresentationContinuationQueue.Enqueue(continuationAddress);
            }

            // 即使本轮刚好清空也保留到下一帧，防止同帧晚到结果重新启动协程绕过预算。
            yield return null;
        }

        runtimePresentationCoroutine = null;
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
        int bestPriority = int.MaxValue;
        for (int i = runtimePresentationQueue.Count - 1; i >= 0; i--)
        {
            RuntimeChunkBinding binding = activeRuntimeBindings[runtimePresentationQueue[i]];
            if (binding.PresentationPriority <= bestPriority)
            {
                bestIndex = i;
                bestPriority = binding.PresentationPriority;
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

        RecycleRuntimeChunkView(binding);
        ChunkView prefab = DimensionManager.Instance?.GetActiveChunkViewPrefab();
        if (prefab == null)
            return;

        ChunkView view = AcquireChunkView(prefab);
        binding.View = view;
        try
        {
            view.gameObject.SetActive(true);
        }
        catch (Exception exception)
        {
            RecycleRuntimeChunkView(binding);
            Debug.LogError($"[ChunkMgr] 区块主线程表现绑定失败 {address}: {exception}", this);
            return;
        }

        binding.PresentationRoutine = view.BindIncremental(
            WorldRuntime, current, includeNavigation: true, renderersPerFrame: 1);
        binding.PresentationInProgress = true;
        runtimePresentationInProgressCount++;

        // 第一次 MoveNext 会完成最高优先级的基础地形绑定后才 yield。
        // 后续表现器改由轮转队列推进，避免当前区块长期独占整个表现流水线。
        if (AdvanceRuntimeChunkPresentation(address))
            runtimePresentationContinuationQueue.Enqueue(address);
    }

    /// <summary>轮转推进一个已启动区块的一步后续表现；返回 true 表示仍需继续。</summary>
    private bool AdvanceRuntimeChunkPresentation(RuntimeWorldAddress address)
    {
        if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding) ||
            !binding.PresentationInProgress || binding.PresentationRoutine == null)
            return false;

        if (!binding.WantsPresentation || binding.View == null)
        {
            FinishRuntimeChunkPresentation(binding);
            return false;
        }

        try
        {
            if (binding.PresentationRoutine.MoveNext())
                return true;
        }
        catch (Exception exception)
        {
            FinishRuntimeChunkPresentation(binding);
            RecycleRuntimeChunkView(binding);
            Debug.LogError($"[ChunkMgr] 区块主线程分帧绑定失败 {address}: {exception}", this);
            return false;
        }

        FinishRuntimeChunkPresentation(binding);
        return false;
    }

    /// <summary>从轮转队列取一个仍有效的增量绑定；过期条目直接丢弃。</summary>
    private bool TryDequeueRuntimePresentationContinuation(out RuntimeWorldAddress address)
    {
        address = default;
        int remaining = runtimePresentationContinuationQueue.Count;
        while (remaining-- > 0)
        {
            RuntimeWorldAddress candidate = runtimePresentationContinuationQueue.Dequeue();
            if (!activeRuntimeBindings.TryGetValue(candidate, out RuntimeChunkBinding binding))
                continue;
            if (!binding.PresentationInProgress || binding.PresentationRoutine == null ||
                !binding.WantsPresentation || binding.View == null)
            {
                FinishRuntimeChunkPresentation(binding);
                continue;
            }

            address = candidate;
            return true;
        }

        return false;
    }

    /// <summary>结束一个增量绑定并释放枚举器；计数只允许回收一次。</summary>
    private void FinishRuntimeChunkPresentation(RuntimeChunkBinding binding)
    {
        if (binding == null)
            return;

        if (binding.PresentationRoutine is IDisposable disposable)
            disposable.Dispose();
        binding.PresentationRoutine = null;
        if (!binding.PresentationInProgress)
            return;

        binding.PresentationInProgress = false;
        runtimePresentationInProgressCount = Mathf.Max(0, runtimePresentationInProgressCount - 1);
    }

    /// <summary>优先从对象池取出 ChunkView，没有可用对象时才实例化新的。</summary>
    private ChunkView AcquireChunkView(ChunkView prefab)
    {
        Transform viewRoot = GetRuntimeChunkViewRoot();
        while (chunkViewPool.Count > 0)
        {
            PooledChunkViewEntry entry = chunkViewPool.Dequeue();
            ChunkView pooled = entry.TakeView();
            if (pooled == null)
                continue;

            pooled.transform.SetParent(viewRoot, false);
            pooled.PrepareForPoolReuse();
            pooled.PresentationChanged -= HandleRuntimeEntityPresentationChanged;
            pooled.PresentationChanged += HandleRuntimeEntityPresentationChanged;
            return pooled;
        }
        ChunkView created = Instantiate(prefab, viewRoot);
        created.PresentationChanged += HandleRuntimeEntityPresentationChanged;
        return created;
    }

    private void HandleRuntimeEntityPresentationChanged(RuntimeWorldAddress address) =>
        RuntimeEntityPresentationChanged?.Invoke(address);

    /// <summary>
    /// 区块表现必须属于当前世界场景；ChunkMgr 随 WorldManager 常驻，但不能把自然物带入 DDOL 场景。
    /// </summary>
    private Transform GetRuntimeChunkViewRoot()
    {
        Scene activeScene = SceneManager.GetActiveScene();
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
        MaintainRuntimeChunkViewPool();
        if (runtimeChunkManager == null || activeRuntimeBindings.Count == 0)
            return;
        foreach (KeyValuePair<RuntimeWorldAddress, RuntimeChunkBinding> pair in activeRuntimeBindings)
        {
            RuntimeChunkBinding binding = pair.Value;
            if (!runtimeChunkManager.TryGetChunk(pair.Key, out ChunkRuntime current) ||
                current.DataStatus != ChunkDataStatus.Ready || current.Terrain == null)
                continue;

            SaveDataMgr.Instance?.RestoreRuntimeBuildingsForChunk(pair.Key);
            SaveDataMgr.Instance?.RestoreRuntimeAiEntitiesForChunk(pair.Key);
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
        int entryCount = chunkViewPool.Count;
        for (int i = 0; i < entryCount; i++)
        {
            PooledChunkViewEntry entry = chunkViewPool.Dequeue();
            if (entry.View != null)
                chunkViewPool.Enqueue(entry);
        }

        int retainedCapacity = RecommendedRuntimeChunkViewPoolCapacity;
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
    }

    /// <summary>解除当前 ChunkView 的全部租约并送回对象池。</summary>
    private void RecycleRuntimeChunkView(RuntimeChunkBinding binding)
    {
        if (binding?.View == null)
            return;

        ChunkView view = binding.View;
        binding.View = null;
        view.Unbind();
        view.gameObject.SetActive(false);
        // View 已属于创建时的世界根节点，回收不创建根节点，也不迁移到其它场景。
        chunkViewPool.Enqueue(new PooledChunkViewEntry(view, Time.realtimeSinceStartup));
    }

    /// <summary>销毁 View 前再次确保表现、导航等全部租约已经释放。</summary>
    private static void DestroyRuntimeChunkView(ChunkView view)
    {
        if (view == null)
            return;

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
        while (chunkViewPool.Count > 0)
        {
            PooledChunkViewEntry entry = chunkViewPool.Dequeue();
            DestroyRuntimeChunkView(entry.TakeView());
        }
    }

    /// <summary>先移除区块登记；正常流送回池，整个窗口关闭时直接销毁 View。</summary>
    private void DeactivateRuntimeBinding(RuntimeWorldAddress address, bool recycleView = true)
    {
        if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
            return;
        FinishRuntimeChunkPresentation(binding);
        activeRuntimeBindings.Remove(address);
        binding.PresentationQueued = false;
        binding.PendingChunk = null;
        runtimePresentationQueue.Remove(address);
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

    /// <summary>清空全部区块画面绑定和窗口目标记录。</summary>
    private void ClearRuntimeWindowBindings()
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

        // 整个窗口关闭后不会复用这些 View，直接销毁，禁止先入池再申请场景根节点。
        runtimeWindowRemovalBuffer.Clear();
        runtimeWindowRemovalBuffer.AddRange(activeRuntimeBindings.Keys);
        for (int i = 0; i < runtimeWindowRemovalBuffer.Count; i++)
            DeactivateRuntimeBinding(runtimeWindowRemovalBuffer[i], recycleView: false);
        runtimeWindowRemovalBuffer.Clear();
        runtimeWindowTargets.Clear();
        runtimeWindowUsesLocalPresentation = false;
        runtimePresentationQueue.Clear();
        runtimePresentationContinuationQueue.Clear();
        runtimePresentationInProgressCount = 0;
        runtimePrefetchQueue.Clear();
        runtimePrefetchTargets.Clear();
        if (runtimePrefetchInFlight is RuntimeWorldAddress inFlight)
            CancelChunkDataRequest(inFlight);
        runtimePrefetchInFlight = null;
        runtimePrefetchInFlightCount = 0;
        DestroyRuntimeChunkViewPool();
        ChunkBatchRendererGroupService.ReleaseUnusedBackend();
    }

    #endregion
}
