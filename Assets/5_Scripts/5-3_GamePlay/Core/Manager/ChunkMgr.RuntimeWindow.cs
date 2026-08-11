using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class ChunkMgr
{
    private sealed class RuntimeChunkBinding
    {
        public ChunkView View;
        public ChunkRuntime PendingChunk;
        public bool WantsPresentation;
        public bool PresentationQueued;
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
    [Tooltip("主线程每帧最多完成的 ChunkView 绑定数量；数值越大，新区块出现越快，但单帧卡顿风险越高。")]
    [SerializeField, Min(1)] private int maxChunkPresentationsPerFrame = 1;

    private const int MaxIdlePrefetchConcurrency = 1;
    private const int ChunkViewPoolSpareCapacity = 4;
    private const float PooledViewResourceTrimDelaySeconds = 7.5f;
    private const float ChunkViewPoolDestroyDelaySeconds = 10f;

    private readonly Dictionary<RuntimeWorldAddress, RuntimeChunkBinding> activeRuntimeBindings = new();
    private readonly HashSet<RuntimeWorldAddress> runtimeWindowTargets = new();
    private readonly List<RuntimeWorldAddress> runtimeWindowRemovalBuffer = new();
    private readonly List<RuntimeWorldAddress> runtimePresentationQueue = new();
    private readonly Queue<RuntimePrefetchRequest> runtimePrefetchQueue = new();
    private readonly HashSet<RuntimeWorldAddress> runtimePrefetchTargets = new();
    private readonly Queue<PooledChunkViewEntry> chunkViewPool = new();
    private bool runtimeWindowUsesLocalPresentation;
    private Coroutine runtimePresentationCoroutine;
    private int runtimePresentationInProgressCount;
    private Coroutine runtimePrefetchCoroutine;
    private RuntimeWorldAddress? runtimePrefetchInFlight;
    private int runtimePrefetchInFlightCount;

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
        EnsureWorldRuntime();
        runtimeWindowUsesLocalPresentation = includeLocalPresentation;
        activeDistance = Mathf.Max(1, activeDistance);
        prefetchDistance = prefetchDistance <= 0
            ? activeDistance
            : Mathf.Max(activeDistance, prefetchDistance);
        destroyDistance = Mathf.Max(prefetchDistance, destroyDistance);
        string dimensionId = ResolveCurrentDimensionId();
        ChunkGenerationProfileSO profileAsset = DimensionManager.Instance?.GetActiveGenerationProfile();
        ChunkGenerationProfileSnapshot profile = profileAsset != null
            ? profileAsset.CreateSnapshot()
            : defaultGenerationSnapshot;
        profile = ApplyWorldCoordinateScale(profile);
        profile = WorldGenerationRuntimeHooks.ApplyBeforeWorldModelGeneration(profile);
        profile = ApplyPersistedEcologyConfiguration(profile);
        int baseSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        if (baseSeed == 0)
            baseSeed = 1;
        // 地表入口与矿洞出口必须共享同一份门户随机种子，不能使用各自维度派生种子。
        profile = profile.WithNumericParameter("cave.portal.baseSeed", baseSeed);
        // 矿洞额外带入地表冻结 Profile，后台才能复算“实际可放”的同一个入口候选。
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
            activeDistance, destroyDistance, includeLocalPresentation, seed, profile, topology,
            dataDistance: activeDistance));

        runtimeWindowTargets.Clear();
        int radius = activeDistance - 1;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
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

        RebuildRuntimePrefetchQueue(centerOrigin, dimensionId, activeDistance,
            prefetchDistance, stepX, stepY, profile, seed, topology);
    }

    #endregion

    #region 空闲数据预取

    /// <summary>重建均匀的外圈预取队列；不再根据玩家移动方向做预测。</summary>
    private void RebuildRuntimePrefetchQueue(Vector2Int centerOrigin, string dimensionId,
        int activeDistance, int prefetchDistance, int stepX, int stepY,
        ChunkGenerationProfileSnapshot profile, int seed,
        ChunkGenerationTopologySnapshot topology)
    {
        runtimePrefetchQueue.Clear();
        runtimePrefetchTargets.Clear();
        int activeRadius = activeDistance - 1;
        int prefetchRadius = prefetchDistance - 1;
        var requests = new List<RuntimePrefetchRequest>();
        for (int dx = -prefetchRadius; dx <= prefetchRadius; dx++)
        {
            for (int dy = -prefetchRadius; dy <= prefetchRadius; dy++)
            {
                int ring = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                if (ring <= activeRadius)
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

        binding.PendingChunk = chunk;
        if (!binding.PresentationQueued)
        {
            binding.PresentationQueued = true;
            runtimePresentationQueue.Add(address);
        }

        if (runtimePresentationCoroutine == null && isActiveAndEnabled)
            runtimePresentationCoroutine = StartCoroutine(ProcessRuntimePresentationQueue());
    }

    /// <summary>每帧按距离优先绘制有限数量的区块，给玩家移动和输入留出主线程时间。</summary>
    private IEnumerator ProcessRuntimePresentationQueue()
    {
        // 先等一帧收集同批后台结果，才能真正按玩家距离排序，而不是按任务回调顺序抢画。
        yield return null;
        while (runtimePresentationQueue.Count > 0)
        {
            int remainingBudget = Mathf.Max(1, maxChunkPresentationsPerFrame);
            while (remainingBudget-- > 0 &&
                   TryDequeueRuntimePresentation(out RuntimeWorldAddress address))
            {
                runtimePresentationInProgressCount++;
                yield return PresentRuntimeChunk(address);
                runtimePresentationInProgressCount = Mathf.Max(
                    0, runtimePresentationInProgressCount - 1);
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
        int bestIndex = -1;
        int bestPriority = int.MaxValue;
        for (int i = runtimePresentationQueue.Count - 1; i >= 0; i--)
        {
            RuntimeWorldAddress candidate = runtimePresentationQueue[i];
            if (!activeRuntimeBindings.TryGetValue(candidate, out RuntimeChunkBinding binding) ||
                !binding.PresentationQueued || !binding.WantsPresentation || binding.PendingChunk == null)
            {
                runtimePresentationQueue.RemoveAt(i);
                continue;
            }

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

    /// <summary>在主线程完成一个区块的 Tilemap、草地、碰撞和导航绑定。</summary>
    private IEnumerator PresentRuntimeChunk(RuntimeWorldAddress address)
    {
        if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
            yield break;

        ChunkRuntime chunk = binding.PendingChunk;
        binding.PendingChunk = null;
        if (!binding.WantsPresentation || chunk == null ||
            runtimeChunkManager == null ||
            !runtimeChunkManager.TryGetChunk(address, out ChunkRuntime current) ||
            !ReferenceEquals(current, chunk) || current.DataStatus != ChunkDataStatus.Ready ||
            current.Terrain == null)
            yield break;

        if (binding.View != null && binding.View.IsBound && ReferenceEquals(binding.View.Model, current))
            yield break;

        RecycleRuntimeChunkView(binding);
        ChunkView prefab = DimensionManager.Instance?.GetActiveChunkViewPrefab();
        if (prefab == null)
            yield break;

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
            yield break;
        }

        IEnumerator incremental = view.BindIncremental(
            WorldRuntime, current, includeNavigation: true, renderersPerFrame: 1);
        while (true)
        {
            bool moved;
            object yielded = null;
            try
            {
                moved = incremental.MoveNext();
                if (moved)
                    yielded = incremental.Current;
            }
            catch (Exception exception)
            {
                RecycleRuntimeChunkView(binding);
                Debug.LogError($"[ChunkMgr] 区块主线程分帧绑定失败 {address}: {exception}", this);
                yield break;
            }

            if (!moved)
                yield break;
            yield return yielded;
        }
    }

    /// <summary>优先从对象池取出 ChunkView，没有可用对象时才实例化新的。</summary>
    private ChunkView AcquireChunkView(ChunkView prefab)
    {
        while (chunkViewPool.Count > 0)
        {
            PooledChunkViewEntry entry = chunkViewPool.Dequeue();
            ChunkView pooled = entry.TakeView();
            if (pooled == null)
                continue;

            pooled.PrepareForPoolReuse();
            return pooled;
        }
        return Instantiate(prefab, transform);
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
        view.transform.SetParent(transform, false);
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

    /// <summary>解除指定区块的画面绑定，并把 ChunkView 放回对象池。</summary>
    private void DeactivateRuntimeBinding(RuntimeWorldAddress address)
    {
        if (!activeRuntimeBindings.TryGetValue(address, out RuntimeChunkBinding binding))
            return;
        activeRuntimeBindings.Remove(address);
        binding.PresentationQueued = false;
        binding.PendingChunk = null;
        runtimePresentationQueue.Remove(address);
        RecycleRuntimeChunkView(binding);
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

        runtimeWindowRemovalBuffer.Clear();
        runtimeWindowRemovalBuffer.AddRange(activeRuntimeBindings.Keys);
        for (int i = 0; i < runtimeWindowRemovalBuffer.Count; i++)
            DeactivateRuntimeBinding(runtimeWindowRemovalBuffer[i]);
        runtimeWindowRemovalBuffer.Clear();
        runtimeWindowTargets.Clear();
        runtimeWindowUsesLocalPresentation = false;
        runtimePresentationQueue.Clear();
        runtimePresentationInProgressCount = 0;
        runtimePrefetchQueue.Clear();
        runtimePrefetchTargets.Clear();
        if (runtimePrefetchInFlight is RuntimeWorldAddress inFlight)
            CancelChunkDataRequest(inFlight);
        runtimePrefetchInFlight = null;
        runtimePrefetchInFlightCount = 0;
        DestroyRuntimeChunkViewPool();
    }

    #endregion
}
