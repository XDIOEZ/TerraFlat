using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 把区块地址换算成世界真正使用的统一地址。
    /// 无限世界不用换；会绕回另一边的有限世界，需要把边界外的位置换回世界范围内。
    /// </summary>
    public interface IWorldAddressNormalizer
    {
        WorldAddress Normalize(WorldAddress address);
    }

    /// <summary>无限世界用的地址转换器：传进什么地址，就原样返回什么地址。</summary>
    public sealed class IdentityWorldAddressNormalizer : IWorldAddressNormalizer
    {
        /// <summary>大家共用这一个实例即可，不需要反复创建。</summary>
        public static readonly IdentityWorldAddressNormalizer Instance =
            new IdentityWorldAddressNormalizer();
        private IdentityWorldAddressNormalizer() { }
        public WorldAddress Normalize(WorldAddress address) => address;
    }

    /// <summary>
    /// 描述“玩家或其他观察者周围要保留多大一片区块”。
    /// 近处的区块会加载并运行；稍远的区块暂时留在内存里；再远才会删除。
    /// 这里的距离按区块计算，并且把中心区块自己也算在内。
    /// </summary>
    public readonly struct ChunkWindowRequest
    {
        public ChunkWindowRequest(WorldAddress center, int activeDistance, int destroyDistance,
            bool requestPresentation, int worldSeed, ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology = default)
        {
            if (activeDistance <= 0)
                throw new ArgumentOutOfRangeException(nameof(activeDistance));
            if (destroyDistance < activeDistance)
                throw new ArgumentOutOfRangeException(nameof(destroyDistance));
            Center = center;
            ActiveDistance = activeDistance;
            DestroyDistance = destroyDistance;
            RequestPresentation = requestPresentation;
            WorldSeed = worldSeed;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Topology = topology;
        }
        /// <summary>玩家或观察者当前所在的中心区块。</summary>
        public WorldAddress Center { get; }
        /// <summary>中心周围多远以内的区块需要加载并运行。</summary>
        public int ActiveDistance { get; }
        /// <summary>中心周围多远以内的区块暂时不要删除，不能比激活范围更小。</summary>
        public int DestroyDistance { get; }
        /// <summary>这些区块除了运行逻辑外，是否还要在 Unity 里显示出来。</summary>
        public bool RequestPresentation { get; }
        /// <summary>需要生成新区块时使用的世界随机种子。</summary>
        public int WorldSeed { get; }
        /// <summary>需要生成新区块时使用的那一份设置。</summary>
        public ChunkGenerationProfileSnapshot Profile { get; }
        /// <summary>告诉生成器这个世界是无限的，还是走到边界会绕回另一边。</summary>
        public ChunkGenerationTopologySnapshot Topology { get; }
    }

    /// <summary>
    /// 负责“玩家走动时，周围区块该加载、保留还是删除”的管理员。
    /// 它可以同时照顾多个玩家或观察者，还会安排后台生成任务。
    /// 后台任务做完后，要定期调用 CommitCompleted，把结果安全地交回世界。
    /// </summary>
    public sealed class ChunkMgr : IDisposable
    {
        /// <summary>某个地址当前正在做的那一个生成任务，以及等待结果的人。</summary>
        private sealed class PendingGeneration
        {
            public ChunkGenerationRequest Request;
            public CancellationTokenSource Cancellation;
            public TaskCompletionSource<ChunkRuntime> Completion;
        }

        /// <summary>后台任务做完后放进安全队列的一张“完成通知单”。</summary>
        private readonly struct GenerationCompletion
        {
            public GenerationCompletion(WorldAddress address, PendingGeneration pending,
                Task<ChunkGenerationResult> task)
            {
                Address = address;
                Pending = pending;
                Task = task;
            }
            public WorldAddress Address { get; }
            public PendingGeneration Pending { get; }
            public Task<ChunkGenerationResult> Task { get; }
        }

        private readonly Dictionary<WorldAddress, PendingGeneration> _pending =
            new Dictionary<WorldAddress, PendingGeneration>();
        private readonly Dictionary<WorldAddress, ChunkLease> _simulationLeases =
            new Dictionary<WorldAddress, ChunkLease>();
        private readonly HashSet<WorldAddress> _presentationDemand = new HashSet<WorldAddress>();
        private readonly HashSet<Task<ChunkGenerationResult>> _generationTasks =
            new HashSet<Task<ChunkGenerationResult>>();
        private readonly ReadOnlyCollection<WorldAddress> _emptyAddresses =
            new List<WorldAddress>().AsReadOnly();
        private readonly ConcurrentQueue<GenerationCompletion> _completed =
            new ConcurrentQueue<GenerationCompletion>();
        private readonly ChunkGenerationScheduler _scheduler;
        private readonly IWorldAddressNormalizer _normalizer;
        private bool _disposed;

        public ChunkMgr(WorldRuntime world, IChunkPureGenerator generator,
            int maxGenerationConcurrency = 2, IWorldAddressNormalizer normalizer = null)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            _scheduler = new ChunkGenerationScheduler(generator ??
                throw new ArgumentNullException(nameof(generator)), maxGenerationConcurrency);
            _normalizer = normalizer ?? IdentityWorldAddressNormalizer.Instance;
        }

        /// <summary>这个管理员正在照顾哪个世界。</summary>
        public WorldRuntime World { get; }
        /// <summary>查看世界里现有的全部区块。</summary>
        public IReadOnlyDictionary<WorldAddress, ChunkRuntime> Chunks => World.Chunks;
        /// <summary>是否还有人正在等待区块加载完成。</summary>
        public bool HasPendingChunkLoads => _pending.Count > 0;
        /// <summary>是否还有后台任务没有被取回处理。</summary>
        public bool HasUnsettledGenerationTasks => _generationTasks.Count > 0;
        /// <summary>最多允许几个区块同时在后台生成。</summary>
        public int MaxGenerationConcurrency => _scheduler.MaxConcurrency;
        /// <summary>当前哪些区块需要在 Unity 画面中显示。</summary>
        public IEnumerable<WorldAddress> PresentationDemand =>
            _presentationDemand.Count == 0 ? _emptyAddresses : _presentationDemand;

        /// <summary>先把地址换算正确，再查找区块。</summary>
        public bool TryGetChunk(WorldAddress address, out ChunkRuntime chunk) =>
            World.TryGetChunk(_normalizer.Normalize(address), out chunk);

        /// <summary>先把地址换算正确，再领取一张区块使用票。</summary>
        public ChunkLease AcquireLease(WorldAddress address, ChunkLeaseKind kind) =>
            World.AcquireChunkLease(_normalizer.Normalize(address), kind);

        /// <summary>
        /// 请求加载一个区块。
        /// 已经做好就直接返回；正在生成就一起等同一个任务；还没开始才新建后台任务。
        /// </summary>
        public Task<ChunkRuntime> RequestChunkDataAsync(WorldAddress address, int worldSeed,
            ChunkGenerationProfileSnapshot profile, CancellationToken cancellationToken = default,
            ChunkGenerationTopologySnapshot topology = default)
        {
            ThrowIfDisposed();
            address = _normalizer.Normalize(address);
            if (World.TryGetChunk(address, out ChunkRuntime existing) &&
                existing.DataStatus == ChunkDataStatus.Ready)
                return Task.FromResult(existing);
            // 同一个区块只生成一次，后来的人一起等待，避免重复工作和结果互相打架。
            if (_pending.TryGetValue(address, out PendingGeneration current))
                return current.Completion.Task;

            ChunkGenerationRequest request = World.BeginChunkGeneration(address, worldSeed, profile, topology);
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var pending = new PendingGeneration
            {
                Request = request,
                Cancellation = linked,
                Completion = new TaskCompletionSource<ChunkRuntime>(
                    TaskCreationOptions.RunContinuationsAsynchronously)
            };
            _pending.Add(address, pending);
            Task<ChunkGenerationResult> task = _scheduler.ScheduleAsync(request, linked.Token);
            _generationTasks.Add(task);
            // 后台线程不直接修改世界数据，只放一张完成通知；主线程之后再来安全处理。
            task.ContinueWith(completed => _completed.Enqueue(
                    new GenerationCompletion(address, pending, completed)),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return pending.Completion.Task;
        }

        /// <summary>根据一个玩家或观察者的位置，更新周围要加载的区块。</summary>
        public void RefreshWindow(ChunkWindowRequest request)
        {
            RefreshWindows(new[] { request });
        }

        /// <summary>
        /// 根据多个玩家或观察者的位置，一次更新所有区块。
        /// 先加载近处区块，再让离开的区块休眠，最后删除所有人都离得很远的区块。
        /// </summary>
        public void RefreshWindows(IReadOnlyList<ChunkWindowRequest> requests)
        {
            ThrowIfDisposed();
            if (requests == null)
                throw new ArgumentNullException(nameof(requests));
            // 分别记下“要运行”“只保留”“要显示”的区块。绕回世界另一边后，重复地址会自动合并。
            var targets = new HashSet<WorldAddress>();
            var retainedTargets = new HashSet<WorldAddress>();
            var presentationTargets = new HashSet<WorldAddress>();
            var targetRequests = new Dictionary<WorldAddress, ChunkWindowRequest>();
            for (int requestIndex = 0; requestIndex < requests.Count; requestIndex++)
            {
                ChunkWindowRequest request = requests[requestIndex];
                WorldAddress center = _normalizer.Normalize(request.Center);
                int activeRadius = request.ActiveDistance - 1;
                int destroyRadius = request.DestroyDistance - 1;
                // 先算较大的保留范围，避免玩家刚走开一点，区块就马上被删除。
                for (int x = -destroyRadius; x <= destroyRadius; x++)
                {
                    for (int y = -destroyRadius; y <= destroyRadius; y++)
                    {
                        retainedTargets.Add(_normalizer.Normalize(new WorldAddress(
                            center.DimensionId,
                            new Int2(center.ChunkOrigin.X + x * request.Profile.Width,
                                center.ChunkOrigin.Y + y * request.Profile.Height))));
                    }
                }
                for (int x = -activeRadius; x <= activeRadius; x++)
                {
                    for (int y = -activeRadius; y <= activeRadius; y++)
                    {
                        var address = _normalizer.Normalize(new WorldAddress(center.DimensionId,
                            new Int2(center.ChunkOrigin.X + x * request.Profile.Width,
                                center.ChunkOrigin.Y + y * request.Profile.Height)));
                        targets.Add(address);
                        targetRequests[address] = request;
                        if (request.RequestPresentation)
                            presentationTargets.Add(address);
                    }
                }
            }

            // 先把新范围里的区块都启用，再统一停掉旧区块；这样更新过程更安全、顺序也固定。
            var orderedTargets = new List<WorldAddress>(targets);
            orderedTargets.Sort();
            for (int i = 0; i < orderedTargets.Count; i++)
            {
                WorldAddress address = orderedTargets[i];
                if (!_simulationLeases.ContainsKey(address))
                    _simulationLeases.Add(address,
                        World.AcquireChunkLease(address, ChunkLeaseKind.Simulation));
                SetPresentationDemand(address, presentationTargets.Contains(address));
                ChunkWindowRequest targetRequest = targetRequests[address];
                _ = RequestChunkDataAsync(address, targetRequest.WorldSeed, targetRequest.Profile,
                    topology: targetRequest.Topology);
            }

            var deactivate = new List<WorldAddress>();
            foreach (WorldAddress address in _simulationLeases.Keys)
            {
                if (!targets.Contains(address))
                    deactivate.Add(address);
            }
            deactivate.Sort();
            for (int i = 0; i < deactivate.Count; i++)
            {
                WorldAddress address = deactivate[i];
                _simulationLeases[address].Dispose();
                _simulationLeases.Remove(address);
                SetPresentationDemand(address, false);
            }

            // 真正删除前，世界还会再检查一次：只要还有人拿着使用票，就不会误删。
            var evictions = new List<WorldAddress>();
            foreach (WorldAddress address in World.Chunks.Keys)
            {
                if (!retainedTargets.Contains(address))
                    evictions.Add(address);
            }
            evictions.Sort();
            for (int i = 0; i < evictions.Count; i++)
                EvictChunk(evictions[i]);
        }

        /// <summary>
        /// 取回所有已经做完的后台任务，并逐个处理成功、取消或失败。
        /// 有效结果会正式交给世界；返回值表示这次一共处理了几个任务。
        /// </summary>
        public int CommitCompleted()
        {
            ThrowIfDisposed();
            int count = 0;
            while (_completed.TryDequeue(out GenerationCompletion completion))
            {
                count++;
                _generationTasks.Remove(completion.Task);
                // 同一个地址可能后来又开了新任务，所以还要确认这张通知确实属于当前任务。
                bool current = _pending.TryGetValue(completion.Address,
                                   out PendingGeneration pending) &&
                               ReferenceEquals(pending, completion.Pending);
                if (current)
                    _pending.Remove(completion.Address);

                if (completion.Task.Status == TaskStatus.RanToCompletion)
                {
                    ChunkGenerationResult result = completion.Task.Result;
                    if (!current || completion.Pending.Cancellation.IsCancellationRequested)
                    {
                        // 旧任务迟到或已经取消，它的结果不会再使用，要在这里把内存释放掉。
                        result?.Dispose();
                        completion.Pending.Completion.TrySetCanceled();
                    }
                    else if (World.TryCommit(result, out string rejection) &&
                             World.TryGetChunk(completion.Address, out ChunkRuntime chunk))
                    {
                        completion.Pending.Completion.TrySetResult(chunk);
                    }
                    else
                    {
                        completion.Pending.Completion.TrySetException(
                            new InvalidOperationException(rejection));
                    }
                }
                else if (completion.Task.IsCanceled)
                {
                    completion.Pending.Completion.TrySetCanceled();
                }
                else
                {
                    // 只有当前任务的失败才写进区块；等待的人仍然会收到真正的错误原因。
                    Exception failure = completion.Task.Exception?.GetBaseException() ??
                                        new InvalidOperationException("Chunk generation failed.");
                    if (current)
                        World.RejectFailedGeneration(completion.Pending.Request, failure);
                    completion.Pending.Completion.TrySetException(failure);
                    World.Events.Publish(new ChunkGenerationFailed(completion.Address, failure.Message));
                }
                completion.Pending.Cancellation.Dispose();
            }
            return count;
        }

        /// <summary>先处理后台结果，再让世界逻辑向前运行一步。</summary>
        public void Advance(float deltaSeconds, bool authoritativeSimulation = true)
        {
            CommitCompleted();
            if (authoritativeSimulation)
                World.Tick(deltaSeconds);
        }

        /// <summary>
        /// 取消某个区块当前的生成任务。
        /// 后台计算可能不会立刻停下，但它之后交回来的结果已经不能再生效。
        /// </summary>
        public bool CancelChunkRequest(WorldAddress address)
        {
            ThrowIfDisposed();
            address = _normalizer.Normalize(address);
            bool invalidated = World.CancelChunkGeneration(address);
            if (!_pending.TryGetValue(address, out PendingGeneration pending))
                return invalidated;
            _pending.Remove(address);
            pending.Cancellation.Cancel();
            pending.Completion.TrySetCanceled();
            return true;
        }

        /// <summary>归还管理员自己的使用票、取消生成任务，然后尝试删除这个区块。</summary>
        public bool EvictChunk(WorldAddress address)
        {
            ThrowIfDisposed();
            address = _normalizer.Normalize(address);
            if (_simulationLeases.TryGetValue(address, out ChunkLease lease))
            {
                lease.Dispose();
                _simulationLeases.Remove(address);
            }
            SetPresentationDemand(address, false);
            CancelChunkRequest(address);
            return World.EvictChunk(address);
        }

        /// <summary>让当前窗口里的区块全部停止运行和显示，但暂时保留已经生成的数据。</summary>
        public void ClearWindow()
        {
            ThrowIfDisposed();
            var addresses = new List<WorldAddress>(_simulationLeases.Keys);
            addresses.Sort();
            for (int i = 0; i < addresses.Count; i++)
            {
                _simulationLeases[addresses[i]].Dispose();
                SetPresentationDemand(addresses[i], false);
            }
            _simulationLeases.Clear();
        }

        /// <summary>取消所有还没完成的区块生成任务。</summary>
        public void CancelAllRequests()
        {
            ThrowIfDisposed();
            var addresses = new List<WorldAddress>(_pending.Keys);
            addresses.Sort();
            for (int i = 0; i < addresses.Count; i++)
                CancelChunkRequest(addresses[i]);
        }

        /// <summary>
        /// 等待所有已经安排的生成任务处理完。
        /// 主要在测试、退出世界，或者必须确认全部完成时使用。
        /// </summary>
        public async Task SettleGenerationTasksAsync()
        {
            ThrowIfDisposed();
            while (_generationTasks.Count > 0)
            {
                CommitCompleted();
                if (_generationTasks.Count > 0)
                    await Task.Yield();
            }
            CommitCompleted();
        }

        /// <summary>
        /// 关闭这个管理员：归还使用票、取消任务、停止后台工作人员，并清理没人接收的结果。
        /// 它不会顺便关闭外面传进来的 WorldRuntime。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (ChunkLease lease in _simulationLeases.Values)
                lease.Dispose();
            _simulationLeases.Clear();
            foreach (PendingGeneration pending in _pending.Values)
            {
                pending.Cancellation.Cancel();
                pending.Completion.TrySetCanceled();
            }
            _pending.Clear();
            _generationTasks.Clear();
            _presentationDemand.Clear();
            _scheduler.Dispose();
            while (_completed.TryDequeue(out GenerationCompletion completion))
            {
                if (completion.Task.Status == TaskStatus.RanToCompletion)
                    completion.Task.Result?.Dispose();
                completion.Pending.Cancellation.Dispose();
            }
        }

        private void SetPresentationDemand(WorldAddress address, bool requested)
        {
            // 只有“要不要显示”真的变了才发通知，避免 Unity 反复连接同一个画面。
            bool changed = requested
                ? _presentationDemand.Add(address)
                : _presentationDemand.Remove(address);
            if (changed)
                World.Events.Publish(new ChunkPresentationDemandChanged(address, requested));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ChunkMgr));
        }
    }

    /// <summary>通知 Unity：某个区块现在需要显示，或者可以隐藏了。</summary>
    public readonly struct ChunkPresentationDemandChanged
    {
        public ChunkPresentationDemandChanged(WorldAddress address, bool requested)
        { Address = address; Requested = requested; }
        /// <summary>哪个区块的显示需求变了。</summary>
        public WorldAddress Address { get; }
        /// <summary>true 表示要显示，false 表示可以隐藏。</summary>
        public bool Requested { get; }
    }

    /// <summary>通知其他系统：某个区块在后台生成时失败了。</summary>
    public readonly struct ChunkGenerationFailed
    {
        public ChunkGenerationFailed(WorldAddress address, string reason)
        { Address = address; Reason = reason; }
        /// <summary>哪个区块生成失败了。</summary>
        public WorldAddress Address { get; }
        /// <summary>失败原因。</summary>
        public string Reason { get; }
    }
}
