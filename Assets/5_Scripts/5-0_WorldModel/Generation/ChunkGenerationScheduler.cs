using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 区块生成任务的后台排队员。
    /// 并发上限可以在运行时调整：提高后会立刻领取更多排队任务，降低后不会中断已开始的任务，
    /// 只会等当前任务自然结束后再按新上限继续工作；即使上游请求“无限”，也会保留 CPU 安全上限。
    /// </summary>
    public sealed class ChunkGenerationScheduler : IDisposable
    {
        #region 数据结构

        /// <summary>排队中的一项工作：任务单、取消信号，以及把结果交回去的通道。</summary>
        private sealed class WorkItem
        {
            public ChunkGenerationRequest Request;
            public CancellationToken CancellationToken;
            public CancellationTokenRegistration CancellationRegistration;
            public TaskCompletionSource<ChunkGenerationResult> Completion;
            public long QueueSequence;
            public ChunkGenerationTiming Timing;
        }

        /// <summary>同一区域的河网准备独立于某一个区块；仍有邻区等待时继续完成。</summary>
        private sealed class ActiveGenerationGroup
        {
            public WorkItem Owner;
            public CancellationTokenSource Cancellation;
        }

        #endregion

        #region 运行时状态

        private readonly IChunkPureGenerator _generator;
        private readonly Queue<WorkItem> _queue = new Queue<WorkItem>();
        private readonly HashSet<Task> _runningTasks = new HashSet<Task>();
        private readonly Dictionary<Task, object> _runningTaskGroups = new Dictionary<Task, object>();
        private readonly HashSet<object> _activeGenerationGroups = new HashSet<object>();
        private readonly Dictionary<object, ActiveGenerationGroup> _activeGroupStates =
            new Dictionary<object, ActiveGenerationGroup>();
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
        }

        /// <summary>当前实际允许同时生成的区块数，已经过 CPU 安全上限约束。</summary>
        public int MaxConcurrency
        {
            get
            {
                lock (_gate)
                    return _maxConcurrency;
            }
        }

        /// <summary>等待后台线程领取的任务数量，供性能面板和验收查看。</summary>
        public int QueuedCount
        {
            get
            {
                lock (_gate)
                    return _queue.Count;
            }
        }

        /// <summary>当前正在执行生成算法的任务数量。</summary>
        public int ActiveCount
        {
            get
            {
                lock (_gate)
                    return _activeTaskCount;
            }
        }

        /// <summary>运行时调整并发上限，并立即把新增的空闲容量用于排队任务。</summary>
        public void SetMaxConcurrency(int maxConcurrency)
        {
            if (maxConcurrency <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

            lock (_gate)
            {
                ThrowIfDisposed();
                int normalized = NormalizeMaxConcurrency(maxConcurrency);
                if (_requestedMaxConcurrency == maxConcurrency &&
                    _maxConcurrency == normalized)
                    return;

                _requestedMaxConcurrency = maxConcurrency;
                _maxConcurrency = normalized;
                StartQueuedWorkLocked();
            }
        }

        #endregion

        #region 排队与执行

        /// <summary>
        /// 把一个生成任务放到队尾，并马上返回一个可等待的 Task。
        /// 区块取消后不再交回结果；共用的河网仍由同区域剩余请求决定是否继续。
        /// </summary>
        public Task<ChunkGenerationResult> ScheduleAsync(ChunkGenerationRequest request,
            CancellationToken cancellationToken = default, ChunkGenerationTiming timing = null)
        {
            lock (_gate)
            {
                ThrowIfDisposed();

                var completion = new TaskCompletionSource<ChunkGenerationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled();
                    return completion.Task;
                }

                var work = new WorkItem
                {
                    Request = request,
                    CancellationToken = cancellationToken,
                    Completion = completion,
                    Timing = timing,
                    QueueSequence = _nextQueueSequence++
                };
                _queue.Enqueue(work);
                // 取消通知只检查区域是否还有需求；河网计算不再直接继承单个区块的取消。
                work.CancellationRegistration = cancellationToken.Register(OnPendingDemandChanged);
                StartQueuedWorkLocked();
                return completion.Task;
            }
        }

        /// <summary>
        /// 按当前窗口优先级重新排列尚未开始的生成任务。
        /// priorityAddresses 越靠前优先级越高；不在列表里的任务保持原相对顺序并排在后面。
        /// </summary>
        public void PrioritizeQueuedWork(IReadOnlyList<WorldAddress> priorityAddresses)
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (_queue.Count <= 1)
                    return;

                var ranks = new Dictionary<WorldAddress, int>();
                if (priorityAddresses != null)
                {
                    for (int i = 0; i < priorityAddresses.Count; i++)
                    {
                        WorldAddress address = priorityAddresses[i];
                        if (!ranks.ContainsKey(address))
                            ranks.Add(address, i);
                    }
                }

                var pending = new List<WorkItem>(_queue.Count);
                while (_queue.Count > 0)
                {
                    WorkItem work = _queue.Dequeue();
                    if (work.CancellationToken.IsCancellationRequested)
                    {
                        work.Completion.TrySetCanceled();
                        continue;
                    }
                    pending.Add(work);
                }

                pending.Sort((left, right) =>
                {
                    bool leftRanked = ranks.TryGetValue(left.Request.Address, out int leftRank);
                    bool rightRanked = ranks.TryGetValue(right.Request.Address, out int rightRank);
                    if (leftRanked != rightRanked)
                        return leftRanked ? -1 : 1;
                    if (leftRanked)
                    {
                        int rank = leftRank.CompareTo(rightRank);
                        if (rank != 0)
                            return rank;
                    }
                    return left.QueueSequence.CompareTo(right.QueueSequence);
                });

                for (int i = 0; i < pending.Count; i++)
                    _queue.Enqueue(pending[i]);

                StartQueuedWorkLocked();
            }
        }

        /// <summary>在锁内按当前并发上限启动排队任务。</summary>
        private void StartQueuedWorkLocked()
        {
            while (!_disposed && _activeTaskCount < _maxConcurrency && _queue.Count > 0)
            {
                WorkItem work = DequeueRunnableWorkLocked(out object groupKey);
                if (work == null)
                    break;

                _activeTaskCount++;
                CancellationToken sharedCancellation = default;
                if (groupKey != null)
                {
                    var group = new ActiveGenerationGroup
                    {
                        Owner = work,
                        Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_shutdownToken)
                    };
                    _activeGenerationGroups.Add(groupKey);
                    _activeGroupStates.Add(groupKey, group);
                    sharedCancellation = group.Cancellation.Token;
                    CancelUnneededGroupLocked(groupKey, group);
                }
                Task execution = Task.Run(() => ExecuteWork(work, sharedCancellation));
                _runningTasks.Add(execution);
                _runningTaskGroups.Add(execution, groupKey);
                execution.ContinueWith(
                    HandleExecutionCompleted,
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }

        /// <summary>同一未就绪河网区域只占一个后台名额，让其它区域先利用空闲线程。</summary>
        private WorkItem DequeueRunnableWorkLocked(out object groupKey)
        {
            groupKey = null;
            int queuedCount = _queue.Count;
            for (int i = 0; i < queuedCount; i++)
            {
                WorkItem work = _queue.Dequeue();
                if (work.CancellationToken.IsCancellationRequested)
                {
                    work.Completion.TrySetCanceled();
                    continue;
                }

                object candidateGroup = _generator.GetPendingGenerationGroupKey(work.Request);
                if (candidateGroup != null && _activeGenerationGroups.Contains(candidateGroup))
                {
                    _queue.Enqueue(work);
                    continue;
                }

                groupKey = candidateGroup;
                return work;
            }
            return null;
        }

        /// <summary>任一区块取消时，只有整片区域已无人等待才停止正在进行的河网准备。</summary>
        private void OnPendingDemandChanged()
        {
            lock (_gate)
            {
                foreach (KeyValuePair<object, ActiveGenerationGroup> pair in _activeGroupStates)
                    CancelUnneededGroupLocked(pair.Key, pair.Value);
            }
        }

        /// <summary>检查仍在排队的同区域区块，避免取消一个区块就丢掉共享河网。</summary>
        private void CancelUnneededGroupLocked(object groupKey, ActiveGenerationGroup group)
        {
            if (!group.Owner.CancellationToken.IsCancellationRequested ||
                group.Cancellation.IsCancellationRequested)
                return;

            foreach (WorkItem pending in _queue)
            {
                if (!pending.CancellationToken.IsCancellationRequested &&
                    Equals(_generator.GetPendingGenerationGroupKey(pending.Request), groupKey))
                    return;
            }

            group.Cancellation.Cancel();
        }

        /// <summary>执行一个纯生成任务，并把结果、取消或异常交回等待者。</summary>
        private void ExecuteWork(WorkItem work, CancellationToken sharedCancellation)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    _shutdownToken, work.CancellationToken);
                CancellationToken generationCancellation = sharedCancellation.CanBeCanceled
                    ? sharedCancellation : linked.Token;
                generationCancellation.ThrowIfCancellationRequested();
                ChunkGenerationResult result = GenerateMeasured(work, generationCancellation);
                if (linked.IsCancellationRequested || generationCancellation.IsCancellationRequested)
                {
                    result?.Dispose();
                    work.Completion.TrySetCanceled();
                }
                else if (result == null)
                {
                    work.Completion.TrySetException(
                        new InvalidOperationException("Pure generator returned no result."));
                }
                else
                {
                    work.Completion.TrySetResult(result);
                }
            }
            catch (OperationCanceledException)
            {
                work.Completion.TrySetCanceled();
            }
            catch (Exception exception)
            {
                work.Completion.TrySetException(exception);
            }
            finally
            {
                work.CancellationRegistration.Dispose();
            }
        }

        /// <summary>在交回 Task 完成通知之前结束计时，避免主线程先读取到未写完的时间线。</summary>
        private ChunkGenerationResult GenerateMeasured(WorkItem work, CancellationToken cancellationToken)
        {
            work.Timing?.StartGeneration();
            try { return _generator.Generate(work.Request, cancellationToken, work.Timing); }
            finally { work.Timing?.FinishGeneration(); }
        }

        /// <summary>回收一个执行名额，并按最新上限继续领取排队任务。</summary>
        private void HandleExecutionCompleted(Task execution)
        {
            ActiveGenerationGroup completedGroup = null;
            lock (_gate)
            {
                _runningTasks.Remove(execution);
                if (_runningTaskGroups.TryGetValue(execution, out object groupKey))
                {
                    _runningTaskGroups.Remove(execution);
                    if (groupKey != null)
                    {
                        _activeGenerationGroups.Remove(groupKey);
                        if (_activeGroupStates.TryGetValue(groupKey, out completedGroup))
                            _activeGroupStates.Remove(groupKey);
                    }
                }
                _activeTaskCount = Math.Max(0, _activeTaskCount - 1);
                StartQueuedWorkLocked();
            }
            completedGroup?.Cancellation.Dispose();
        }

        #endregion

        #region 释放

        /// <summary>停止接收任务，取消排队及运行中的生成，并等待后台工作安全退出。</summary>
        public void Dispose()
        {
            Task[] running;
            var pendingRegistrations = new List<CancellationTokenRegistration>();
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _shutdown.Cancel();

                while (_queue.Count > 0)
                {
                    WorkItem pending = _queue.Dequeue();
                    pending.Completion.TrySetCanceled();
                    pendingRegistrations.Add(pending.CancellationRegistration);
                }

                running = new Task[_runningTasks.Count];
                _runningTasks.CopyTo(running);
            }

            for (int i = 0; i < pendingRegistrations.Count; i++)
                pendingRegistrations[i].Dispose();

            try
            {
                Task.WaitAll(running, TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // 每项工作会自己报告失败；这里即使关闭多次也要保持安全。
            }

            _shutdown.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ChunkGenerationScheduler));
        }

        /// <summary>最多占用约三分之一逻辑处理器，并硬限制为四个重型生成任务。</summary>
        private static int NormalizeMaxConcurrency(int requested)
        {
            int cpuCeiling = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 3));
            return Math.Max(1, Math.Min(requested, cpuCeiling));
        }

        #endregion
    }
}
