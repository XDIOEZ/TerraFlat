using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlatWorld.WorldModel
{
    /// <summary>区块生成与邻区预热共享的后台调度器。</summary>
    public sealed class ChunkGenerationScheduler : IDisposable
    {
        #region 排队工作与区域需求

        private sealed class WorkItem
        {
            public ChunkGenerationRequest Request;
            public CancellationToken CancellationToken;
            public CancellationTokenRegistration CancellationRegistration;
            public TaskCompletionSource<ChunkGenerationResult> Completion;
            public long QueueSequence;
            public ChunkGenerationTiming Timing;
            public bool RemovedWithoutExecution;
        }

        private sealed class PrewarmWorkItem
        {
            public object GroupKey;
            public CancellationToken Lifetime;
            public Action<CancellationToken> Execute;
            public readonly List<PrewarmDemand> Sources = new List<PrewarmDemand>();
            public readonly List<CancellationTokenRegistration> Registrations =
                new List<CancellationTokenRegistration>();
            public LinkedListNode<PrewarmWorkItem> QueueNode;
            public bool Retired;
        }

        private sealed class PrewarmDemand
        {
            public WorldAddress Address;
            public CancellationToken Cancellation;
            public bool WindowControlled;
            public bool Removed;
        }

        private sealed class ActiveGenerationGroup
        {
            public WorkItem Owner;
            public PrewarmWorkItem Prewarm;
            public CancellationTokenSource Cancellation;
        }

        #endregion

        #region 运行时状态

        [ThreadStatic] private static ChunkGenerationScheduler currentGenerationScheduler;
        [ThreadStatic] private static CancellationToken currentGenerationDemand;
        [ThreadStatic] private static WorldAddress currentGenerationAddress;

        private readonly IChunkPureGenerator _generator;
        private readonly List<WorkItem> _queue = new List<WorkItem>();
        private readonly LinkedList<PrewarmWorkItem> _prewarmQueue = new LinkedList<PrewarmWorkItem>();
        private readonly Dictionary<object, PrewarmWorkItem> _prewarms =
            new Dictionary<object, PrewarmWorkItem>();
        private readonly HashSet<Task> _runningTasks = new HashSet<Task>();
        private readonly Dictionary<Task, object> _runningTaskGroups = new Dictionary<Task, object>();
        private readonly Dictionary<object, ActiveGenerationGroup> _activeGroupStates =
            new Dictionary<object, ActiveGenerationGroup>();
        private readonly List<CancellationTokenRegistration> _retiredRegistrations =
            new List<CancellationTokenRegistration>();
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private readonly CancellationToken _shutdownToken;
        private readonly object _gate = new object();
        private int _maxConcurrency;
        private int _activeTaskCount;
        private int _requestedMaxConcurrency;
        private long _nextQueueSequence;
        private bool _disposed;

        #endregion

        #region 生命周期与配置

        public ChunkGenerationScheduler(IChunkPureGenerator generator, int maxConcurrency = 2)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            if (maxConcurrency <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
            _requestedMaxConcurrency = maxConcurrency;
            _maxConcurrency = NormalizeMaxConcurrency(maxConcurrency);
            _shutdownToken = _shutdown.Token;
            if (generator is IChunkGenerationPrewarmSchedulerBinding binding)
                binding.BindChunkGenerationPrewarmScheduler(this);
        }

        public int MaxConcurrency
        {
            get { lock (_gate) return _maxConcurrency; }
        }

        /// <summary>后台队列包含尚未领取的正式区块和邻区预热。</summary>
        public int QueuedCount
        {
            get { lock (_gate) return _queue.Count + _prewarmQueue.Count; }
        }

        /// <summary>后台并发计数包含正式区块和预热。</summary>
        public int ActiveCount
        {
            get { lock (_gate) return _activeTaskCount; }
        }

        public void SetMaxConcurrency(int maxConcurrency)
        {
            if (maxConcurrency <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
            lock (_gate)
            {
                ThrowIfDisposed();
                int normalized = NormalizeMaxConcurrency(maxConcurrency);
                if (_requestedMaxConcurrency != maxConcurrency || _maxConcurrency != normalized)
                {
                    _requestedMaxConcurrency = maxConcurrency;
                    _maxConcurrency = normalized;
                    StartQueuedWorkLocked();
                }
            }
            DisposeRetiredRegistrations();
        }

        #endregion

        #region 正式区块与预热排队

        public Task<ChunkGenerationResult> ScheduleAsync(ChunkGenerationRequest request,
            CancellationToken cancellationToken = default, ChunkGenerationTiming timing = null)
        {
            Task<ChunkGenerationResult> result;
            lock (_gate)
            {
                ThrowIfDisposed();
                var completion = new TaskCompletionSource<ChunkGenerationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                result = completion.Task;
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled();
                }
                else
                {
                    var work = new WorkItem
                    {
                        Request = request,
                        CancellationToken = cancellationToken,
                        Completion = completion,
                        Timing = timing,
                        QueueSequence = _nextQueueSequence++
                    };
                    _queue.Add(work);
                    work.CancellationRegistration = cancellationToken.Register(OnPendingDemandChanged);
                    if (work.RemovedWithoutExecution)
                        _retiredRegistrations.Add(work.CancellationRegistration);
                    StartQueuedWorkLocked();
                }
            }
            DisposeRetiredRegistrations();
            return result;
        }

        /// <summary>只从当前正式生成线程提交预热，定位等独立同步查询不创建后台线程。</summary>
        public static bool TryQueuePrewarmForCurrentGeneration(object groupKey,
            CancellationToken worldLifetime, Action<CancellationToken> execute, bool allowNewWork = true)
        {
            if (groupKey == null) throw new ArgumentNullException(nameof(groupKey));
            if (execute == null) throw new ArgumentNullException(nameof(execute));
            ChunkGenerationScheduler scheduler = currentGenerationScheduler;
            return scheduler != null && scheduler.QueuePrewarmForGenerationDemand(
                groupKey, worldLifetime, currentGenerationAddress, currentGenerationDemand, true, execute, allowNewWork);
        }

        internal static bool IsExecutingScheduledChunkGeneration => currentGenerationScheduler != null;

        internal bool QueuePrewarmForGenerationDemand(object groupKey, CancellationToken lifetime,
            WorldAddress sourceAddress, CancellationToken source, bool windowControlled,
            Action<CancellationToken> execute, bool allowNewWork = true)
        {
            bool accepted = false;
            lock (_gate)
            {
                if (!_disposed && !lifetime.IsCancellationRequested && !source.IsCancellationRequested)
                {
                    if (_prewarms.TryGetValue(groupKey, out PrewarmWorkItem cancelled) &&
                        cancelled.QueueNode == null &&
                        _activeGroupStates.TryGetValue(groupKey, out ActiveGenerationGroup active) &&
                        active.Cancellation.IsCancellationRequested)
                        _prewarms.Remove(groupKey);
                    if (_prewarms.TryGetValue(groupKey, out PrewarmWorkItem existing))
                    {
                        AddPrewarmDemandLocked(existing, sourceAddress, source, windowControlled);
                        accepted = true;
                    }
                    else if (_activeGroupStates.TryGetValue(groupKey, out ActiveGenerationGroup running) &&
                             !running.Cancellation.IsCancellationRequested)
                    {
                        // 正式任务接纳新增预热需求，共享计算不再重复入队。
                        var work = new PrewarmWorkItem { GroupKey = groupKey, Lifetime = lifetime, Execute = execute };
                        _prewarms.Add(groupKey, work);
                        running.Prewarm = work;
                        AddPrewarmRegistrationLocked(work, lifetime);
                        AddPrewarmDemandLocked(work, sourceAddress, source, windowControlled);
                        accepted = !work.Retired;
                    }
                    else if (allowNewWork)
                    {
                        var work = new PrewarmWorkItem
                        {
                            GroupKey = groupKey,
                            Lifetime = lifetime,
                            Execute = execute
                        };
                        _prewarms.Add(groupKey, work);
                        work.QueueNode = _prewarmQueue.AddLast(work);
                        AddPrewarmRegistrationLocked(work, lifetime);
                        AddPrewarmDemandLocked(work, sourceAddress, source, windowControlled);
                        accepted = !work.Retired;
                        StartQueuedWorkLocked();
                    }
                }
            }
            DisposeRetiredRegistrations();
            return accepted;
        }

        private void AddPrewarmDemandLocked(PrewarmWorkItem work, WorldAddress address,
            CancellationToken source, bool windowControlled)
        {
            if (work.Retired) return;
            for (int i = 0; i < work.Sources.Count; i++)
            {
                PrewarmDemand existing = work.Sources[i];
                if (!existing.Removed && existing.Address.Equals(address) && existing.Cancellation.Equals(source) &&
                    existing.WindowControlled == windowControlled) return;
            }
            work.Sources.Add(new PrewarmDemand
                { Address = address, Cancellation = source, WindowControlled = windowControlled });
            AddPrewarmRegistrationLocked(work, source);
        }

        /// <summary>窗口撤销已完成区块的需求时，也撤销由该区块发起的邻区预热。</summary>
        public void CancelPrewarmDemandsForSource(WorldAddress address)
        {
            lock (_gate)
            {
                foreach (PrewarmWorkItem work in _prewarms.Values)
                    for (int i = 0; i < work.Sources.Count; i++)
                        if (work.Sources[i].WindowControlled && work.Sources[i].Address.Equals(address))
                            work.Sources[i].Removed = true;
            }
            OnPendingDemandChanged();
        }

        private void AddPrewarmRegistrationLocked(PrewarmWorkItem work, CancellationToken token)
        {
            if (!token.CanBeCanceled) return;
            CancellationTokenRegistration registration = token.Register(OnPendingDemandChanged);
            if (work.Retired) _retiredRegistrations.Add(registration);
            else work.Registrations.Add(registration);
        }

        public void PrioritizeQueuedWork(IReadOnlyList<WorldAddress> priorityAddresses)
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                var ranks = new Dictionary<WorldAddress, int>();
                if (priorityAddresses != null)
                    for (int i = 0; i < priorityAddresses.Count; i++)
                        if (!ranks.ContainsKey(priorityAddresses[i])) ranks.Add(priorityAddresses[i], i);

                _queue.Sort((left, right) =>
                {
                    bool leftRanked = ranks.TryGetValue(left.Request.Address, out int leftRank);
                    bool rightRanked = ranks.TryGetValue(right.Request.Address, out int rightRank);
                    if (leftRanked != rightRanked) return leftRanked ? -1 : 1;
                    if (leftRanked)
                    {
                        int rank = leftRank.CompareTo(rightRank);
                        if (rank != 0) return rank;
                    }
                    return left.QueueSequence.CompareTo(right.QueueSequence);
                });
                StartQueuedWorkLocked();
            }
            DisposeRetiredRegistrations();
        }

        #endregion

        #region 统一领取和执行

        private void StartQueuedWorkLocked()
        {
            while (!_disposed && _activeTaskCount < _maxConcurrency)
            {
                WorkItem work = DequeueRunnableChunkLocked(out object groupKey);
                PrewarmWorkItem prewarm = null;
                if (work != null)
                {
                    if (groupKey != null && _prewarms.TryGetValue(groupKey, out prewarm) &&
                        prewarm.QueueNode != null)
                    {
                        _prewarmQueue.Remove(prewarm.QueueNode);
                        prewarm.QueueNode = null;
                    }
                }
                else
                {
                    prewarm = DequeueRunnablePrewarmLocked();
                    if (prewarm == null) break;
                    groupKey = prewarm.GroupKey;
                }

                _activeTaskCount++;
                CancellationToken sharedCancellation = default;
                if (groupKey != null)
                {
                    var group = new ActiveGenerationGroup
                    {
                        Owner = work,
                        Prewarm = prewarm,
                        Cancellation = prewarm != null
                            ? CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken, prewarm.Lifetime)
                            : CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken)
                    };
                    _activeGroupStates.Add(groupKey, group);
                    sharedCancellation = group.Cancellation.Token;
                    CancelUnneededGroupLocked(groupKey, group);
                }
                Task execution = work != null
                    ? Task.Run(() => ExecuteChunkWork(work, sharedCancellation))
                    : Task.Run(() => ExecutePrewarmWork(prewarm, sharedCancellation));
                _runningTasks.Add(execution);
                _runningTaskGroups.Add(execution, groupKey);
                execution.ContinueWith(HandleExecutionCompleted, CancellationToken.None,
                    TaskContinuationOptions.None, TaskScheduler.Default);
            }
        }

        private WorkItem DequeueRunnableChunkLocked(out object groupKey)
        {
            groupKey = null;
            for (int i = 0; i < _queue.Count;)
            {
                WorkItem work = _queue[i];
                if (work.CancellationToken.IsCancellationRequested)
                {
                    _queue.RemoveAt(i);
                    work.Completion.TrySetCanceled();
                    work.RemovedWithoutExecution = true;
                    _retiredRegistrations.Add(work.CancellationRegistration);
                    continue;
                }
                object candidateGroup = _generator.GetPendingGenerationGroupKey(work.Request);
                if (candidateGroup != null && _activeGroupStates.ContainsKey(candidateGroup))
                {
                    i++;
                    continue;
                }
                _queue.RemoveAt(i);
                groupKey = candidateGroup;
                return work;
            }
            return null;
        }

        private PrewarmWorkItem DequeueRunnablePrewarmLocked()
        {
            LinkedListNode<PrewarmWorkItem> node = _prewarmQueue.First;
            while (node != null)
            {
                LinkedListNode<PrewarmWorkItem> next = node.Next;
                PrewarmWorkItem work = node.Value;
                if (!HasLivePrewarmDemand(work))
                    RetirePrewarmLocked(work);
                else if (!_activeGroupStates.ContainsKey(work.GroupKey))
                {
                    _prewarmQueue.Remove(node);
                    work.QueueNode = null;
                    return work;
                }
                node = next;
            }
            return null;
        }

        private void OnPendingDemandChanged()
        {
            lock (_gate)
            {
                if (!_disposed)
                {
                    LinkedListNode<PrewarmWorkItem> node = _prewarmQueue.First;
                    while (node != null)
                    {
                        LinkedListNode<PrewarmWorkItem> next = node.Next;
                        if (!HasLivePrewarmDemand(node.Value)) RetirePrewarmLocked(node.Value);
                        node = next;
                    }
                    foreach (KeyValuePair<object, ActiveGenerationGroup> pair in _activeGroupStates)
                        CancelUnneededGroupLocked(pair.Key, pair.Value);
                    StartQueuedWorkLocked();
                }
            }
            DisposeRetiredRegistrations();
        }

        private static bool HasLivePrewarmDemand(PrewarmWorkItem work)
        {
            if (work.Retired || work.Lifetime.IsCancellationRequested) return false;
            for (int i = 0; i < work.Sources.Count; i++)
                if (!work.Sources[i].Removed && !work.Sources[i].Cancellation.IsCancellationRequested) return true;
            return false;
        }

        private void CancelUnneededGroupLocked(object groupKey, ActiveGenerationGroup group)
        {
            if (group.Cancellation.IsCancellationRequested ||
                (group.Owner != null && !group.Owner.CancellationToken.IsCancellationRequested) ||
                (group.Prewarm != null && HasLivePrewarmDemand(group.Prewarm))) return;
            foreach (WorkItem pending in _queue)
                if (!pending.CancellationToken.IsCancellationRequested &&
                    Equals(_generator.GetPendingGenerationGroupKey(pending.Request), groupKey)) return;
            group.Cancellation.Cancel();
        }

        private void RetirePrewarmLocked(PrewarmWorkItem work)
        {
            if (work.Retired) return;
            work.Retired = true;
            if (work.QueueNode != null)
            {
                _prewarmQueue.Remove(work.QueueNode);
                work.QueueNode = null;
            }
            if (_prewarms.TryGetValue(work.GroupKey, out PrewarmWorkItem current) &&
                ReferenceEquals(current, work)) _prewarms.Remove(work.GroupKey);
            _retiredRegistrations.AddRange(work.Registrations);
            work.Registrations.Clear();
        }

        private void ExecuteChunkWork(WorkItem work, CancellationToken sharedCancellation)
        {
            ChunkGenerationScheduler previousScheduler = currentGenerationScheduler;
            CancellationToken previousDemand = currentGenerationDemand;
            WorldAddress previousAddress = currentGenerationAddress;
            currentGenerationScheduler = this;
            currentGenerationDemand = work.CancellationToken;
            currentGenerationAddress = work.Request.Address;
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    _shutdownToken, work.CancellationToken);
                CancellationToken generationCancellation = sharedCancellation.CanBeCanceled
                    ? sharedCancellation : linked.Token;
                generationCancellation.ThrowIfCancellationRequested();
                work.Timing?.StartGeneration();
                ChunkGenerationResult result;
                try { result = _generator.Generate(work.Request, generationCancellation, work.Timing); }
                finally { work.Timing?.FinishGeneration(); }
                if (linked.IsCancellationRequested || generationCancellation.IsCancellationRequested)
                {
                    result?.Dispose();
                    work.Completion.TrySetCanceled();
                }
                else if (result == null)
                    work.Completion.TrySetException(new InvalidOperationException("Pure generator returned no result."));
                else
                    work.Completion.TrySetResult(result);
            }
            catch (OperationCanceledException) { work.Completion.TrySetCanceled(); }
            catch (Exception exception) { work.Completion.TrySetException(exception); }
            finally
            {
                currentGenerationScheduler = previousScheduler;
                currentGenerationDemand = previousDemand;
                currentGenerationAddress = previousAddress;
                work.CancellationRegistration.Dispose();
            }
        }

        private static void ExecutePrewarmWork(PrewarmWorkItem work, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                work.Execute(cancellationToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }

        private void HandleExecutionCompleted(Task execution)
        {
            ActiveGenerationGroup completedGroup = null;
            lock (_gate)
            {
                _runningTasks.Remove(execution);
                if (_runningTaskGroups.TryGetValue(execution, out object groupKey))
                {
                    _runningTaskGroups.Remove(execution);
                    if (groupKey != null && _activeGroupStates.TryGetValue(groupKey, out completedGroup))
                    {
                        _activeGroupStates.Remove(groupKey);
                        if (completedGroup.Prewarm != null) RetirePrewarmLocked(completedGroup.Prewarm);
                    }
                }
                _activeTaskCount--;
                StartQueuedWorkLocked();
            }
            completedGroup?.Cancellation.Dispose();
            DisposeRetiredRegistrations();
        }

        // 注册的取消回调可能等待调度锁，因此只能在锁外等待释放。
        private void DisposeRetiredRegistrations()
        {
            if (Monitor.IsEntered(_gate)) return;
            CancellationTokenRegistration[] registrations;
            lock (_gate)
            {
                if (_retiredRegistrations.Count == 0) return;
                registrations = _retiredRegistrations.ToArray();
                _retiredRegistrations.Clear();
            }
            for (int i = 0; i < registrations.Length; i++) registrations[i].Dispose();
        }

        #endregion

        #region 释放

        public void Dispose()
        {
            Task[] running;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                foreach (WorkItem pending in _queue)
                {
                    pending.Completion.TrySetCanceled();
                    _retiredRegistrations.Add(pending.CancellationRegistration);
                }
                _queue.Clear();
                while (_prewarmQueue.First != null) RetirePrewarmLocked(_prewarmQueue.First.Value);
                running = new Task[_runningTasks.Count];
                _runningTasks.CopyTo(running);
            }
            if (_generator is IChunkGenerationPrewarmSchedulerBinding binding)
                binding.UnbindChunkGenerationPrewarmScheduler(this);
            _shutdown.Cancel();
            DisposeRetiredRegistrations();
            try { Task.WaitAll(running, TimeSpan.FromSeconds(5)); }
            catch (AggregateException) { }
            _shutdown.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ChunkGenerationScheduler));
        }

        private static int NormalizeMaxConcurrency(int requested)
        {
            int cpuCeiling = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 3));
            return Math.Max(1, Math.Min(requested, cpuCeiling));
        }

        #endregion
    }
}
