using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Stopwatch = System.Diagnostics.Stopwatch;

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
    /// 近处的区块会运行，外圈可提前准备画面，再外圈只保留数据；最远处才删除。
    /// 这里的距离按区块计算，并且把中心区块自己也算在内。
    /// </summary>
    public readonly struct ChunkWindowRequest
    {
        public ChunkWindowRequest(WorldAddress center, int activeDistance, int destroyDistance,
            bool requestPresentation, int worldSeed, ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology = default, int dataDistance = 0,
            int presentationDistance = 0)
            : this(center,
                new Int2(activeDistance, activeDistance),
                new Int2(destroyDistance, destroyDistance),
                requestPresentation,
                worldSeed,
                profile,
                topology,
                dataDistance > 0
                    ? new Int2(dataDistance, dataDistance)
                    : default,
                presentationDistance > 0
                    ? new Int2(presentationDistance, presentationDistance)
                    : default)
        {
        }

        /// <summary>按 X/Y 独立距离创建矩形区块窗口，适配宽屏或超宽屏相机。</summary>
        public ChunkWindowRequest(WorldAddress center, Int2 activeDistance, Int2 destroyDistance,
            bool requestPresentation, int worldSeed, ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology = default, Int2 dataDistance = default,
            Int2 presentationDistance = default)
        {
            if (activeDistance.X <= 0 || activeDistance.Y <= 0)
                throw new ArgumentOutOfRangeException(nameof(activeDistance));
            if (presentationDistance.X <= 0 || presentationDistance.Y <= 0)
                presentationDistance = activeDistance;
            if (dataDistance.X <= 0 || dataDistance.Y <= 0)
                dataDistance = presentationDistance;
            if (presentationDistance.X < activeDistance.X || presentationDistance.Y < activeDistance.Y)
                throw new ArgumentOutOfRangeException(nameof(presentationDistance));
            if (dataDistance.X < presentationDistance.X || dataDistance.Y < presentationDistance.Y)
                throw new ArgumentOutOfRangeException(nameof(dataDistance));
            if (destroyDistance.X < activeDistance.X || destroyDistance.Y < activeDistance.Y)
                throw new ArgumentOutOfRangeException(nameof(destroyDistance));
            if (destroyDistance.X < dataDistance.X || destroyDistance.Y < dataDistance.Y)
                throw new ArgumentOutOfRangeException(nameof(destroyDistance));
            Center = center;
            ActiveDistanceX = activeDistance.X;
            ActiveDistanceY = activeDistance.Y;
            PresentationDistanceX = presentationDistance.X;
            PresentationDistanceY = presentationDistance.Y;
            DataDistanceX = dataDistance.X;
            DataDistanceY = dataDistance.Y;
            DestroyDistanceX = destroyDistance.X;
            DestroyDistanceY = destroyDistance.Y;
            ActiveDistance = Math.Max(ActiveDistanceX, ActiveDistanceY);
            PresentationDistance = Math.Max(PresentationDistanceX, PresentationDistanceY);
            DataDistance = Math.Max(DataDistanceX, DataDistanceY);
            DestroyDistance = Math.Max(DestroyDistanceX, DestroyDistanceY);
            RequestPresentation = requestPresentation;
            WorldSeed = worldSeed;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Topology = topology;
        }
        /// <summary>玩家或观察者当前所在的中心区块。</summary>
        public WorldAddress Center { get; }
        /// <summary>中心周围多远以内的区块需要加载并运行。</summary>
        public int ActiveDistance { get; }
        /// <summary>横向活动区块距离，包含中心区块。</summary>
        public int ActiveDistanceX { get; }
        /// <summary>纵向活动区块距离，包含中心区块。</summary>
        public int ActiveDistanceY { get; }
        /// <summary>横纵方向完整准备画面的距离，可大于实际运行圈。</summary>
        public int PresentationDistance { get; }
        public int PresentationDistanceX { get; }
        public int PresentationDistanceY { get; }
        /// <summary>中心周围多远以内需要数据；超出表现圈的部分不领取模拟和表现租约。</summary>
        public int DataDistance { get; }
        /// <summary>横向数据生成距离。</summary>
        public int DataDistanceX { get; }
        /// <summary>纵向数据生成距离。</summary>
        public int DataDistanceY { get; }
        /// <summary>中心周围多远以内的区块暂时不要删除，不能比数据预取范围更小。</summary>
        public int DestroyDistance { get; }
        /// <summary>横向保留距离。</summary>
        public int DestroyDistanceX { get; }
        /// <summary>纵向保留距离。</summary>
        public int DestroyDistanceY { get; }
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
            public ChunkGenerationTiming Timing;
            public int Waiters;
        }

        private sealed class PendingPersistence
        {
            public PendingGeneration Generation;
            public TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
                pending.Timing?.EnqueueCompletion();
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
        private readonly HashSet<WorldAddress> _windowDataDemand = new HashSet<WorldAddress>();
        private readonly HashSet<WorldAddress> _windowRetryTargets = new HashSet<WorldAddress>();
        private readonly HashSet<WorldAddress> _deferredWindowEvictions = new HashSet<WorldAddress>();
        private readonly List<WorldAddress> _windowWorkBuffer = new List<WorldAddress>();
        private readonly ChunkWindowCoverage _windowCoverage;
        private readonly IDisposable _windowCommitSubscription;
        private bool _hasWindow;
        private readonly List<WorldAddress> _windowDataCancellationBuffer =
            new List<WorldAddress>();
        private readonly HashSet<Task<ChunkGenerationResult>> _generationTasks =
            new HashSet<Task<ChunkGenerationResult>>();
        private readonly ReadOnlyCollection<WorldAddress> _emptyAddresses =
            new List<WorldAddress>().AsReadOnly();
        private readonly ConcurrentQueue<GenerationCompletion> _completed =
            new ConcurrentQueue<GenerationCompletion>();
        private readonly ConcurrentQueue<(WorldAddress Address, PendingGeneration Pending)> _releasedWaiters = new();
        private readonly HashSet<WorldAddress> _unclaimedRequests = new();
        private readonly List<WorldAddress> _unclaimedWorkBuffer = new();
        private readonly object _completionLifecycleGate = new object();
        private readonly ChunkGenerationScheduler _scheduler;
        private readonly IWorldAddressNormalizer _normalizer;
        private readonly Func<WorldAddress, CancellationToken, Task> _preparePersistence;
        private readonly Action<WorldAddress> _releasePersistence;
        private readonly List<PendingPersistence> _persistenceWaiting = new();
        private readonly ConcurrentQueue<bool> _persistenceFinished = new();
        private int _persistenceRunning;
        private bool _disposed;
        private bool _completionCleanupFinished;

        public ChunkMgr(WorldRuntime world, IChunkPureGenerator generator,
            int maxGenerationConcurrency = 2, IWorldAddressNormalizer normalizer = null,
            Func<WorldAddress, CancellationToken, Task> preparePersistence = null,
            Action<WorldAddress> releasePersistence = null)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            _scheduler = new ChunkGenerationScheduler(generator ??
                throw new ArgumentNullException(nameof(generator)), maxGenerationConcurrency);
            _normalizer = normalizer ?? IdentityWorldAddressNormalizer.Instance;
            _preparePersistence = preparePersistence;
            _releasePersistence = releasePersistence;
            _windowCoverage = new ChunkWindowCoverage(_normalizer);
            _windowCommitSubscription = World.Events.Subscribe<ChunkCommitted>(TrackCommittedChunkOutsideRetentionWindow);
        }

        /// <summary>这个管理员正在照顾哪个世界。</summary>
        public WorldRuntime World { get; }
        /// <summary>查看世界里现有的全部区块。</summary>
        public IReadOnlyDictionary<WorldAddress, ChunkRuntime> Chunks => World.Chunks;
        /// <summary>是否还有人正在等待区块加载完成。</summary>
        public bool HasPendingChunkLoads => _pending.Count > 0;
        public int LastWindowVisitedCells => _windowCoverage.LastVisitedCells;
        /// <summary>是否还有后台任务没有被取回处理。</summary>
        public bool HasUnsettledGenerationTasks => _generationTasks.Count > 0;
        /// <summary>最多允许几个区块同时在后台生成。</summary>
        public int MaxGenerationConcurrency => _scheduler.MaxConcurrency;
        /// <summary>仍在调度器队列中等待线程的生成任务数量。</summary>
        public int QueuedGenerationCount => _scheduler.QueuedCount;
        /// <summary>当前真正占用后台线程执行生成算法的任务数量。</summary>
        public int ActiveGenerationCount => _scheduler.ActiveCount;
        /// <summary>已经完成、等待主线程提交给世界的结果数量。</summary>
        public int PendingCommitCount => _completed.Count;
        /// <summary>当前哪些区块需要在 Unity 画面中显示。</summary>
        public IEnumerable<WorldAddress> PresentationDemand =>
            _presentationDemand.Count == 0 ? _emptyAddresses : _presentationDemand;

        /// <summary>先把地址换算正确，再查找区块。</summary>
        public bool TryGetChunk(WorldAddress address, out ChunkRuntime chunk) =>
            World.TryGetChunk(_normalizer.Normalize(address), out chunk);

        /// <summary>先把地址换算正确，再领取一张区块使用票。</summary>
        public ChunkLease AcquireLease(WorldAddress address, ChunkLeaseKind kind) =>
            World.AcquireChunkLease(_normalizer.Normalize(address), kind);

        /// <summary>运行时调整纯区块生成并发；降低上限不会中断已经开始的生成。</summary>
        public void SetMaxGenerationConcurrency(int maxConcurrency)
        {
            ThrowIfDisposed();
            _scheduler.SetMaxConcurrency(maxConcurrency);
        }

        #region 窗口数据优先级

        /// <summary>一个数据加载目标及其相对玩家的调度优先级。</summary>
        private readonly struct WindowDataTarget
        {
            public WindowDataTarget(ChunkWindowRequest request, bool active, int ring)
            {
                Request = request;
                Active = active;
                Ring = ring;
            }

            public ChunkWindowRequest Request { get; }
            public bool Active { get; }
            public int Ring { get; }

            /// <summary>判断候选目标是否比已记录目标更值得先生成。</summary>
            public bool IsHigherPriorityThan(WindowDataTarget other)
            {
                if (Active != other.Active)
                    return Active;
                if (Ring != other.Ring)
                    return Ring < other.Ring;
                return false;
            }
        }

        #endregion

        /// <summary>
        /// 请求加载一个区块。
        /// 已经做好就直接返回；正在生成就一起等同一个任务；还没开始才新建后台任务。
        /// </summary>
        public Task<ChunkRuntime> RequestChunkDataAsync(WorldAddress address, int worldSeed,
            ChunkGenerationProfileSnapshot profile, CancellationToken cancellationToken = default,
            ChunkGenerationTopologySnapshot topology = default)
            => RequestChunkDataCore(address, worldSeed, profile, cancellationToken, topology, false);

        private Task<ChunkRuntime> RequestChunkDataCore(WorldAddress address, int worldSeed,
            ChunkGenerationProfileSnapshot profile, CancellationToken cancellationToken,
            ChunkGenerationTopologySnapshot topology, bool windowDemand)
        {
            ThrowIfDisposed();
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<ChunkRuntime>(cancellationToken);
            address = _normalizer.Normalize(address);
            if (World.TryGetChunk(address, out ChunkRuntime existing) &&
                existing.DataStatus == ChunkDataStatus.Ready)
                return Task.FromResult(existing);
            // 同一个区块只生成一次，后来的人一起等待，避免重复工作和结果互相打架。
            if (_pending.TryGetValue(address, out PendingGeneration current))
                return windowDemand ? current.Completion.Task : AwaitSharedChunkForCallerAsync(current, cancellationToken);

            ChunkGenerationRequest request = World.BeginChunkGeneration(address, worldSeed, profile, topology);
            var linked = new CancellationTokenSource();
            var pending = new PendingGeneration
            {
                Request = request,
                Timing = World.StreamingDiagnostics.BeginRequest(request),
                Cancellation = linked,
                Completion = new TaskCompletionSource<ChunkRuntime>(
                    TaskCreationOptions.RunContinuationsAsynchronously)
            };
            _pending.Add(address, pending);
            Task<ChunkGenerationResult> generation = _scheduler.ScheduleAsync(request, linked.Token, pending.Timing);
            Task persistence = Task.CompletedTask;
            if (_preparePersistence != null)
            {
                var work = new PendingPersistence { Generation = pending };
                _persistenceWaiting.Add(work);
                persistence = work.Completion.Task;
                PumpPersistence();
            }
            Task<ChunkGenerationResult> task = AwaitGenerationAndPersistenceAsync(generation, persistence);
            _generationTasks.Add(task);
            // 后台线程不直接修改世界数据，只放一张完成通知；主线程之后再来安全处理。
            task.ContinueWith(completed => EnqueueOrReleaseGenerationCompletion(
                    new GenerationCompletion(address, pending, completed)),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return windowDemand ? pending.Completion.Task : AwaitSharedChunkForCallerAsync(pending, cancellationToken);
        }

        #region 生成与持久数据提交门禁
        // 读盘只启动有限并发；未启动的窗口请求可随观察者移动重新排序。
        private void PumpPersistence()
        {
            while (_persistenceFinished.TryDequeue(out _)) _persistenceRunning--;
            for (int i = _persistenceWaiting.Count - 1; i >= 0; i--)
                if (_persistenceWaiting[i].Generation.Cancellation.IsCancellationRequested)
                {
                    _persistenceWaiting[i].Completion.TrySetCanceled();
                    _persistenceWaiting.RemoveAt(i);
                }
            while (_persistenceRunning < _scheduler.MaxConcurrency && _persistenceWaiting.Count != 0)
            {
                PendingPersistence work = _persistenceWaiting[0];
                _persistenceWaiting.RemoveAt(0);
                Task task;
                try { task = _preparePersistence(work.Generation.Request.Address, work.Generation.Cancellation.Token) ?? Task.CompletedTask; }
                catch (Exception error) { task = Task.FromException(error); }
                _persistenceRunning++;
                task.ContinueWith(completed =>
                {
                    if (completed.IsCanceled) work.Completion.TrySetCanceled();
                    else if (completed.IsFaulted) work.Completion.TrySetException(completed.Exception.InnerExceptions);
                    else work.Completion.TrySetResult(true);
                    _persistenceFinished.Enqueue(true);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }

        private void PrioritizeQueuedPersistence(IReadOnlyList<WorldAddress> addresses)
        {
            var ranks = new Dictionary<WorldAddress, int>();
            for (int i = 0; i < addresses.Count; i++) ranks[addresses[i]] = i;
            _persistenceWaiting.Sort((a, b) =>
            {
                int left = ranks.TryGetValue(a.Generation.Request.Address, out int x) ? x : int.MaxValue;
                int right = ranks.TryGetValue(b.Generation.Request.Address, out int y) ? y : int.MaxValue;
                int result = left.CompareTo(right);
                return result != 0 ? result : a.Generation.Request.Address.CompareTo(b.Generation.Request.Address);
            });
        }

        // 单个等待者取消不会终止仍被窗口或其他调用者使用的共享生成。
        private async Task<ChunkRuntime> AwaitSharedChunkForCallerAsync(PendingGeneration pending, CancellationToken cancellation)
        {
            Interlocked.Increment(ref pending.Waiters);
            try
            {
                Task<ChunkRuntime> shared = pending.Completion.Task;
                if (!cancellation.CanBeCanceled) return await shared.ConfigureAwait(false);
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellation.Register(() => cancelled.TrySetCanceled()))
                {
                    if (await Task.WhenAny(shared, cancelled.Task).ConfigureAwait(false) != shared)
                        await cancelled.Task.ConfigureAwait(false);
                    return await shared.ConfigureAwait(false);
                }
            }
            finally
            {
                if (Interlocked.Decrement(ref pending.Waiters) == 0)
                    _releasedWaiters.Enqueue((pending.Request.Address, pending));
            }
        }

        // 最后一个独立等待者退出后，由主线程确认窗口与其他租约再取消共享工作。
        private void CancelRequestsWithoutDemand()
        {
            while (_releasedWaiters.TryDequeue(out var released))
                if (_pending.TryGetValue(released.Address, out PendingGeneration current) && ReferenceEquals(current, released.Pending))
                    _unclaimedRequests.Add(released.Address);
            _unclaimedWorkBuffer.Clear();
            _unclaimedWorkBuffer.AddRange(_unclaimedRequests);
            foreach (WorldAddress address in _unclaimedWorkBuffer)
            {
                if (!_pending.TryGetValue(address, out PendingGeneration pending)) { _unclaimedRequests.Remove(address); continue; }
                if (_windowDataDemand.Contains(address) || Volatile.Read(ref pending.Waiters) != 0)
                { _unclaimedRequests.Remove(address); continue; }
                if (World.TryGetChunk(address, out ChunkRuntime chunk) &&
                    (chunk.DataRetentionLeaseCount > 0 || chunk.SimulationLeaseCount > 0 ||
                     chunk.PresentationLeaseCount > 0 || chunk.NavigationLeaseCount > 0)) continue;
                _unclaimedRequests.Remove(address);
                CancelCanonicalChunkRequest(address);
            }
            _unclaimedWorkBuffer.Clear();
        }

        private bool HasIndependentChunkLease(WorldAddress address)
        {
            if (!World.TryGetChunk(address, out ChunkRuntime chunk)) return false;
            int windowSimulation = _simulationLeases.ContainsKey(address) ? 1 : 0;
            return chunk.DataRetentionLeaseCount > 0 || chunk.NavigationLeaseCount > 0 || chunk.PresentationLeaseCount > 0 ||
                chunk.SimulationLeaseCount > windowSimulation;
        }

        // 生成与读盘并行，任一失败时归还已经生成但不能提交的地形。
        private static async Task<ChunkGenerationResult> AwaitGenerationAndPersistenceAsync(
            Task<ChunkGenerationResult> generation, Task persistence)
        {
            try
            {
                await Task.WhenAll(generation, persistence).ConfigureAwait(false);
                return await generation.ConfigureAwait(false);
            }
            catch
            {
                if (generation.Status == TaskStatus.RanToCompletion) generation.Result?.Dispose();
                throw;
            }
        }
        #endregion

        // 调度器释放后迟到的完成通知只归还载荷，不再进入已关闭的世界队列。
        private void EnqueueOrReleaseGenerationCompletion(GenerationCompletion completion)
        {
            lock (_completionLifecycleGate)
            {
                if (_completionCleanupFinished) ReleaseUnclaimedGenerationCompletion(completion);
                else _completed.Enqueue(completion);
            }
        }

        private static void ReleaseUnclaimedGenerationCompletion(GenerationCompletion completion)
        {
            if (completion.Task.Status == TaskStatus.RanToCompletion)
                completion.Task.Result?.Dispose();
            else if (completion.Task.IsFaulted)
                _ = completion.Task.Exception;
            completion.Pending.Cancellation.Dispose();
        }

        /// <summary>根据一个玩家或观察者的位置，更新周围要加载的区块。</summary>
        public void RefreshWindow(ChunkWindowRequest request)
        {
            RefreshWindows(new[] { request });
        }

        /// <summary>根据观察者的窗口边界增减租约、数据请求和表现需求。</summary>
        public void RefreshWindows(IReadOnlyList<ChunkWindowRequest> requests)
        {
            ThrowIfDisposed();
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            _windowCoverage.Update(requests);
            _hasWindow = true;

            // 先取消离开数据范围的任务，再发布新增的模拟和表现需求。
            foreach (WorldAddress address in _windowCoverage.Removed(ChunkWindowCoverage.Layer.Data))
            {
                _windowDataDemand.Remove(address);
                _windowRetryTargets.Remove(address);
                _scheduler.CancelPrewarmDemandsForSource(address);
                if (_pending.TryGetValue(address, out PendingGeneration held) &&
                    (Volatile.Read(ref held.Waiters) != 0 || HasIndependentChunkLease(address)))
                    _unclaimedRequests.Add(address);
                else CancelCanonicalChunkRequest(address);
            }
            foreach (WorldAddress address in _windowCoverage.Added(ChunkWindowCoverage.Layer.Data))
                _windowDataDemand.Add(address);
            foreach (WorldAddress address in _windowCoverage.Added(ChunkWindowCoverage.Layer.Active))
                if (!_simulationLeases.ContainsKey(address))
                    _simulationLeases.Add(address, World.AcquireChunkLease(address, ChunkLeaseKind.Simulation));
            foreach (WorldAddress address in _windowCoverage.Added(ChunkWindowCoverage.Layer.Presentation))
                SetPresentationDemand(address, true);
            foreach (WorldAddress address in _windowCoverage.Removed(ChunkWindowCoverage.Layer.Presentation))
                SetPresentationDemand(address, false);

            // 配置变化时撤销仍使用旧种子或旧快照的后台任务，已提交数据仍由世界管理。
            if (_windowCoverage.LastFullRebuild)
            {
                _windowWorkBuffer.Clear();
                foreach (KeyValuePair<WorldAddress, PendingGeneration> pair in _pending)
                {
                    if (!_windowDataDemand.Contains(pair.Key)) continue;
                    ChunkWindowRequest current = FindHighestPriorityWindowForAddress(pair.Key, requests);
                    ChunkGenerationRequest pending = pair.Value.Request;
                    if (pending.WorldSeed != current.WorldSeed || !ReferenceEquals(pending.Profile, current.Profile) ||
                        pending.Topology.IsWrapped != current.Topology.IsWrapped || pending.Topology.Min != current.Topology.Min ||
                        pending.Topology.Span != current.Topology.Span)
                        _windowWorkBuffer.Add(pair.Key);
                }
                foreach (WorldAddress address in _windowWorkBuffer)
                {
                    _scheduler.CancelPrewarmDemandsForSource(address);
                    CancelChunkRequest(address);
                }
            }
            _windowWorkBuffer.Clear();
            _windowWorkBuffer.AddRange(_windowCoverage.Added(ChunkWindowCoverage.Layer.Data));
            foreach (WorldAddress address in _windowCoverage.Added(ChunkWindowCoverage.Layer.Data))
                _windowRetryTargets.Remove(address);
            foreach (WorldAddress address in _windowRetryTargets)
            {
                if (!_windowDataDemand.Contains(address)) continue;
                if (_windowCoverage.Contains(ChunkWindowCoverage.Layer.Active, address) && !_simulationLeases.ContainsKey(address))
                    _simulationLeases.Add(address, World.AcquireChunkLease(address, ChunkLeaseKind.Simulation));
                if (_windowCoverage.Contains(ChunkWindowCoverage.Layer.Presentation, address))
                    SetPresentationDemand(address, true);
                _windowWorkBuffer.Add(address);
            }
            _windowRetryTargets.Clear();
            _windowWorkBuffer.Sort((left, right) => CompareCurrentWindowDataPriority(left, right, requests));
            foreach (WorldAddress address in _windowWorkBuffer)
            {
                ChunkWindowRequest request = FindHighestPriorityWindowForAddress(address, requests);
                _ = RequestChunkDataCore(address, request.WorldSeed, request.Profile, default, request.Topology, true);
            }

            // 只重排仍在等待生成的目标，距离随最新观察者位置即时更新。
            _windowWorkBuffer.Clear();
            foreach (WorldAddress address in _pending.Keys)
                if (_windowDataDemand.Contains(address)) _windowWorkBuffer.Add(address);
            _windowWorkBuffer.Sort((left, right) => CompareCurrentWindowDataPriority(left, right, requests));
            _scheduler.PrioritizeQueuedWork(_windowWorkBuffer);
            PrioritizeQueuedPersistence(_windowWorkBuffer);
            PumpPersistence();
            _windowWorkBuffer.Clear();

            foreach (WorldAddress address in _windowCoverage.Removed(ChunkWindowCoverage.Layer.Active))
                if (_simulationLeases.TryGetValue(address, out ChunkLease lease))
                {
                    lease.Dispose();
                    _simulationLeases.Remove(address);
                }
            foreach (WorldAddress address in _windowCoverage.Removed(ChunkWindowCoverage.Layer.Retained))
                _deferredWindowEvictions.Add(address);
            if (_windowCoverage.LastFullRebuild)
                foreach (WorldAddress address in World.Chunks.Keys)
                    if (!_windowCoverage.Contains(ChunkWindowCoverage.Layer.Retained, address))
                        _deferredWindowEvictions.Add(address);
            _windowWorkBuffer.AddRange(_deferredWindowEvictions);
            _windowWorkBuffer.Sort();
            foreach (WorldAddress address in _windowWorkBuffer)
            {
                if (_windowCoverage.Contains(ChunkWindowCoverage.Layer.Retained, address) ||
                    EvictCanonicalChunk(address) || !World.Chunks.ContainsKey(address))
                    _deferredWindowEvictions.Remove(address);
            }
            _windowWorkBuffer.Clear();
        }

        // 距离只对待生成地址重算，重叠观察者选可见且最近的请求。
        private int CompareCurrentWindowDataPriority(WorldAddress left, WorldAddress right,
            IReadOnlyList<ChunkWindowRequest> requests)
        {
            WindowDataTarget a = FindHighestPriorityWindowTarget(left, requests);
            WindowDataTarget b = FindHighestPriorityWindowTarget(right, requests);
            if (a.Active != b.Active) return a.Active ? -1 : 1;
            int ring = a.Ring.CompareTo(b.Ring);
            return ring != 0 ? ring : left.CompareTo(right);
        }

        private ChunkWindowRequest FindHighestPriorityWindowForAddress(WorldAddress address,
            IReadOnlyList<ChunkWindowRequest> requests) => FindHighestPriorityWindowTarget(address, requests).Request;

        private WindowDataTarget FindHighestPriorityWindowTarget(WorldAddress address,
            IReadOnlyList<ChunkWindowRequest> requests)
        {
            WindowDataTarget best = default;
            bool found = false;
            for (int i = 0; i < requests.Count; i++)
            {
                ChunkWindowRequest request = requests[i];
                WorldAddress center = _normalizer.Normalize(request.Center);
                if (center.DimensionId != address.DimensionId) continue;
                int dx = address.ChunkOrigin.X - center.ChunkOrigin.X;
                int dy = address.ChunkOrigin.Y - center.ChunkOrigin.Y;
                if (request.Topology.IsWrapped)
                {
                    dx = ShortestWindowCoordinateDelta(dx, request.Topology.Span.X);
                    dy = ShortestWindowCoordinateDelta(dy, request.Topology.Span.Y);
                }
                int x = Math.Abs(dx / request.Profile.Width), y = Math.Abs(dy / request.Profile.Height);
                if (x >= request.DataDistanceX || y >= request.DataDistanceY) continue;
                var candidate = new WindowDataTarget(request,
                    x < request.ActiveDistanceX && y < request.ActiveDistanceY, Math.Max(x, y));
                if (!found || candidate.IsHigherPriorityThan(best)) { best = candidate; found = true; }
            }
            if (!found) throw new InvalidOperationException("窗口数据地址没有对应的观察者。");
            return best;
        }

        private static int ShortestWindowCoordinateDelta(int delta, int span)
        {
            delta %= span;
            if (delta > span / 2) delta -= span;
            else if (delta < -span / 2) delta += span;
            return delta;
        }

        private void TrackCommittedChunkOutsideRetentionWindow(ChunkCommitted committed)
        {
            if (_hasWindow && !_windowCoverage.Contains(ChunkWindowCoverage.Layer.Retained, committed.Address))
                _deferredWindowEvictions.Add(committed.Address);
        }

        /// <summary>窗口关闭时取消仍未完成的窗口生成请求，不影响独立的显式数据请求。</summary>
        private void ClearWindowDataDemand()
        {
            _windowDataCancellationBuffer.Clear();
            foreach (WorldAddress address in _windowDataDemand)
            {
                _scheduler.CancelPrewarmDemandsForSource(address);
                if (!_pending.TryGetValue(address, out PendingGeneration pending)) continue;
                if (Volatile.Read(ref pending.Waiters) != 0 || HasIndependentChunkLease(address))
                    _unclaimedRequests.Add(address);
                else _windowDataCancellationBuffer.Add(address);
            }

            _windowDataCancellationBuffer.Sort();
            for (int i = 0; i < _windowDataCancellationBuffer.Count; i++)
                CancelCanonicalChunkRequest(_windowDataCancellationBuffer[i]);

            _windowDataCancellationBuffer.Clear();
            _windowDataDemand.Clear();
        }

        /// <summary>
        /// 取回所有已经做完的后台任务，并逐个处理成功、取消或失败。
        /// 有效结果会正式交给世界；返回值表示这次一共处理了几个任务。
        /// </summary>
        public int CommitCompleted(int maxCount = int.MaxValue,
            double maxMilliseconds = double.PositiveInfinity)
        {
            ThrowIfDisposed();
            if (maxCount <= 0)
                return 0;
            CancelRequestsWithoutDemand();
            PumpPersistence();
            World.StreamingDiagnostics.Count("commit.pumps");
            using var batchTiming = World.StreamingDiagnostics.Measure("commit.batch");
            int count = 0;
            bool hasTimeLimit = maxMilliseconds > 0d && !double.IsInfinity(maxMilliseconds);
            long startedAt = hasTimeLimit ? Stopwatch.GetTimestamp() : 0L;
            double allowedTicks = hasTimeLimit
                ? maxMilliseconds * Stopwatch.Frequency / 1000d
                : double.PositiveInfinity;
            while (count < maxCount &&
                   _completed.TryDequeue(out GenerationCompletion completion))
            {
                count++;
                using var notificationTiming = World.StreamingDiagnostics.Measure("commit.process");
                World.StreamingDiagnostics.Count("commit.notifications");
                completion.Pending.Timing?.StartCommit();
                string diagnosticOutcome = "failed";
                bool retryWindowRequest = false;
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
                        if (current)
                        {
                            // 调度器完成为取消时也必须把 WorldRuntime 的 Generating 状态收口。
                            // 否则窗口仍需要该区块时会留下“没有任务的 Generating”死状态，加载页永远等不到地形。
                            retryWindowRequest = World.CancelChunkGeneration(completion.Pending.Request) &&
                                _windowDataDemand.Contains(completion.Address);
                        }
                        completion.Pending.Completion.TrySetCanceled();
                        diagnosticOutcome = current ? "cancelled" : "stale";
                    }
                    else if (World.TryCommit(result, out string rejection) &&
                             World.TryGetChunk(completion.Address, out ChunkRuntime chunk))
                    {
                        completion.Pending.Completion.TrySetResult(chunk);
                        diagnosticOutcome = "applied";
                    }
                    else
                    {
                        var failure = new InvalidOperationException(rejection);
                        // 有效任务提交失败时不能继续假装处于 Generating；记录失败后，下一次窗口刷新可以重新请求。
                        World.RejectFailedGeneration(completion.Pending.Request, failure);
                        completion.Pending.Completion.TrySetException(failure);
                        if (_windowDataDemand.Contains(completion.Address)) _windowRetryTargets.Add(completion.Address);
                        World.Events.Publish(new ChunkGenerationFailed(completion.Address, failure.Message));
                        diagnosticOutcome = "rejected";
                    }
                }
                else if (completion.Task.IsCanceled)
                {
                    if (current)
                    {
                        retryWindowRequest = World.CancelChunkGeneration(completion.Pending.Request) &&
                            _windowDataDemand.Contains(completion.Address);
                    }
                    completion.Pending.Completion.TrySetCanceled();
                    diagnosticOutcome = "cancelled";
                }
                else
                {
                    // 只有当前任务的失败才写进区块；等待的人仍然会收到真正的错误原因。
                    Exception failure = completion.Task.Exception?.GetBaseException() ??
                                        new InvalidOperationException("Chunk generation failed.");
                    bool ownsWorldRequest = current && World.IsCurrentChunkGeneration(completion.Pending.Request);
                    if (ownsWorldRequest)
                        World.RejectFailedGeneration(completion.Pending.Request, failure);
                    completion.Pending.Completion.TrySetException(failure);
                    if (ownsWorldRequest)
                    {
                        if (_windowDataDemand.Contains(completion.Address)) _windowRetryTargets.Add(completion.Address);
                        World.Events.Publish(new ChunkGenerationFailed(completion.Address, failure.Message));
                    }
                }
                // 未提交的当前请求需要通知持久化门禁归还已准备的数据。
                if (current && diagnosticOutcome != "applied") completion.Pending.Cancellation.Cancel();
                completion.Pending.Cancellation.Dispose();
                World.StreamingDiagnostics.Complete(completion.Pending.Timing, diagnosticOutcome);
                if (retryWindowRequest)
                {
                    // 单个调用者的取消不能永久取消仍在当前数据窗口里的共享区块需求。
                    ChunkGenerationRequest request = completion.Pending.Request;
                    World.StreamingDiagnostics.Count("generation.window_retry");
                    _ = RequestChunkDataAsync(completion.Address, request.WorldSeed,
                        request.Profile, topology: request.Topology);
                }
                // 单个提交完整执行，之后再按真实耗时决定本帧是否继续。
                if (hasTimeLimit && Stopwatch.GetTimestamp() - startedAt >= allowedTicks)
                {
                    if (!_completed.IsEmpty)
                        World.StreamingDiagnostics.Count("commit.time_limit_hits");
                    break;
                }
            }
            if (count == maxCount && !_completed.IsEmpty)
                World.StreamingDiagnostics.Count("commit.count_limit_hits");
            return count;
        }

        /// <summary>
        /// 取消某个区块当前的生成任务。
        /// 后台计算可能不会立刻停下，但它之后交回来的结果已经不能再生效。
        /// </summary>
        public bool CancelChunkRequest(WorldAddress address)
        {
            ThrowIfDisposed();
            return CancelCanonicalChunkRequest(_normalizer.Normalize(address));
        }

        // 已保存的窗口地址属于原维度，切维度时不能用新拓扑再次归一化。
        private bool CancelCanonicalChunkRequest(WorldAddress address)
        {
            if (!_pending.TryGetValue(address, out PendingGeneration pending))
                return false;
            _pending.Remove(address);
            if (_windowDataDemand.Contains(address)) _windowRetryTargets.Add(address);
            World.CancelChunkGeneration(pending.Request);
            pending.Timing?.Cancel();
            World.StreamingDiagnostics.Count("generation.cancel_requested");
            pending.Cancellation.Cancel();
            pending.Completion.TrySetCanceled();
            return true;
        }

        /// <summary>归还管理员自己的使用票、取消生成任务，然后尝试删除这个区块。</summary>
        public bool EvictChunk(WorldAddress address)
        {
            ThrowIfDisposed();
            return EvictCanonicalChunk(_normalizer.Normalize(address));
        }

        private bool EvictCanonicalChunk(WorldAddress address)
        {
            if (_windowDataDemand.Contains(address)) _windowRetryTargets.Add(address);
            if (_pending.TryGetValue(address, out PendingGeneration waiting) && Volatile.Read(ref waiting.Waiters) != 0)
                return false;
            if (HasIndependentChunkLease(address))
                return false;
            if (_simulationLeases.TryGetValue(address, out ChunkLease lease))
            {
                lease.Dispose();
                _simulationLeases.Remove(address);
            }
            SetPresentationDemand(address, false);
            CancelCanonicalChunkRequest(address);
            bool evicted = World.EvictChunk(address);
            if (evicted) _releasePersistence?.Invoke(address);
            return evicted;
        }

        /// <summary>后台数据用完后仅回收无窗口需求且无任何租约的区块，不撤销其它系统的使用票。</summary>
        public bool TryEvictUnleasedChunkOutsideRuntimeWindows(WorldAddress address)
        {
            ThrowIfDisposed();
            address = _normalizer.Normalize(address);
            if (_windowDataDemand.Contains(address) || _pending.ContainsKey(address) || !World.EvictChunk(address)) return false;
            _releasePersistence?.Invoke(address);
            return true;
        }

        /// <summary>让当前窗口里的区块全部停止运行和显示，但暂时保留已经生成的数据。</summary>
        public void ClearWindow()
        {
            ThrowIfDisposed();
            ClearWindowDataDemand();
            _windowCoverage.Clear();
            _windowRetryTargets.Clear();
            _deferredWindowEvictions.Clear();
            _hasWindow = false;
            var addresses = new List<WorldAddress>(_simulationLeases.Keys);
            addresses.Sort();
            for (int i = 0; i < addresses.Count; i++)
                _simulationLeases[addresses[i]].Dispose();
            _simulationLeases.Clear();
            addresses.Clear();
            addresses.AddRange(_presentationDemand);
            addresses.Sort();
            for (int i = 0; i < addresses.Count; i++)
                SetPresentationDemand(addresses[i], false);
        }

        /// <summary>取消所有还没完成的区块生成任务。</summary>
        public void CancelAllRequests()
        {
            ThrowIfDisposed();
            var addresses = new List<WorldAddress>(_pending.Keys);
            addresses.Sort();
            foreach (WorldAddress address in _windowDataDemand)
                _scheduler.CancelPrewarmDemandsForSource(address);
            for (int i = 0; i < addresses.Count; i++)
                CancelCanonicalChunkRequest(addresses[i]);
            _windowDataDemand.Clear();
            _windowDataCancellationBuffer.Clear();
            _unclaimedRequests.Clear();
            _windowRetryTargets.Clear();
            _windowCoverage.Clear();
            _hasWindow = false;
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
            _windowCommitSubscription.Dispose();
            _windowCoverage.Clear();
            _windowRetryTargets.Clear();
            _deferredWindowEvictions.Clear();
            foreach (ChunkLease lease in _simulationLeases.Values)
                lease.Dispose();
            _simulationLeases.Clear();
            var pendingRequests = new List<PendingGeneration>(_pending.Values);
            _pending.Clear();
            foreach (PendingGeneration pending in pendingRequests)
            {
                World.CancelChunkGeneration(pending.Request);
                pending.Cancellation.Cancel();
                pending.Completion.TrySetCanceled();
            }
            foreach (PendingPersistence work in _persistenceWaiting) work.Completion.TrySetCanceled();
            _persistenceWaiting.Clear();
            _generationTasks.Clear();
            _presentationDemand.Clear();
            _windowDataDemand.Clear();
            _windowDataCancellationBuffer.Clear();
            try { _scheduler.Dispose(); }
            finally
            {
                lock (_completionLifecycleGate)
                {
                    _completionCleanupFinished = true;
                    while (_completed.TryDequeue(out GenerationCompletion completion))
                        ReleaseUnclaimedGenerationCompletion(completion);
                }
            }
        }

        #region 流送诊断

        /// <summary>只读区分真正生成中与已完成待提交；旧/取消通知单独统计，不把它们称为可用区块。</summary>
        public object CapturePendingStreamingDiagnostics(int maxSamples = 16)
        {
            var samples = new List<ChunkGenerationDiagnostic>();
            var stages = new Dictionary<string, int>();
            foreach (PendingGeneration pending in _pending.Values)
            {
                ChunkGenerationDiagnostic sample = pending.Timing?.Capture();
                if (sample == null) continue;
                samples.Add(sample);
                stages.TryGetValue(sample.Stage, out int count);
                stages[sample.Stage] = count + 1;
            }
            samples.Sort((a, b) => b.AgeMs.CompareTo(a.AgeMs));
            int limit = Math.Max(1, Math.Min(32, maxSamples));
            if (samples.Count > limit) samples.RemoveRange(limit, samples.Count - limit);
            int valid = 0, stale = 0, cancelled = 0, failed = 0;
            foreach (GenerationCompletion completion in _completed.ToArray())
            {
                bool current = _pending.TryGetValue(completion.Address, out PendingGeneration pending) &&
                    ReferenceEquals(pending, completion.Pending);
                if (!current) stale++;
                else if (completion.Pending.Cancellation.IsCancellationRequested || completion.Task.IsCanceled) cancelled++;
                else if (completion.Task.IsFaulted) failed++;
                else valid++;
            }
            return new { pendingStages = stages, oldestRequests = samples,
                completedValid = valid, completedStale = stale, completedCancelled = cancelled, completedFailed = failed };
        }

        #endregion

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
