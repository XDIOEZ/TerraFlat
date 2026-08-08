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
            public TaskCompletionSource<ChunkGenerationResult> Completion;
        }

        #endregion

        #region 运行时状态

        private readonly IChunkPureGenerator _generator;
        private readonly Queue<WorkItem> _queue = new Queue<WorkItem>();
        private readonly HashSet<Task> _runningTasks = new HashSet<Task>();
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private readonly CancellationToken _shutdownToken;
        private readonly object _gate = new object();
        private int _maxConcurrency;
        private int _activeTaskCount;
        private int _requestedMaxConcurrency;
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
        /// 无论任务还在排队还是已经开始，都可以通过取消信号停止它。
        /// </summary>
        public Task<ChunkGenerationResult> ScheduleAsync(ChunkGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                ThrowIfDisposed();

                var completion = new TaskCompletionSource<ChunkGenerationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _queue.Enqueue(new WorkItem
                {
                    Request = request,
                    CancellationToken = cancellationToken,
                    Completion = completion
                });
                StartQueuedWorkLocked();
                return completion.Task;
            }
        }

        /// <summary>在锁内按当前并发上限启动排队任务。</summary>
        private void StartQueuedWorkLocked()
        {
            while (!_disposed && _activeTaskCount < _maxConcurrency && _queue.Count > 0)
            {
                WorkItem work = _queue.Dequeue();
                if (work.CancellationToken.IsCancellationRequested)
                {
                    work.Completion.TrySetCanceled();
                    continue;
                }

                _activeTaskCount++;
                Task execution = Task.Run(() => ExecuteWork(work));
                _runningTasks.Add(execution);
                execution.ContinueWith(
                    HandleExecutionCompleted,
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }

        /// <summary>执行一个纯生成任务，并把结果、取消或异常交回等待者。</summary>
        private void ExecuteWork(WorkItem work)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                    _shutdownToken, work.CancellationToken);
                linked.Token.ThrowIfCancellationRequested();
                ChunkGenerationResult result = _generator.Generate(work.Request, linked.Token);
                if (linked.IsCancellationRequested)
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
        }

        /// <summary>回收一个执行名额，并按最新上限继续领取排队任务。</summary>
        private void HandleExecutionCompleted(Task execution)
        {
            lock (_gate)
            {
                _runningTasks.Remove(execution);
                _activeTaskCount = Math.Max(0, _activeTaskCount - 1);
                StartQueuedWorkLocked();
            }
        }

        #endregion

        #region 释放

        /// <summary>停止接收任务，取消排队及运行中的生成，并等待后台工作安全退出。</summary>
        public void Dispose()
        {
            Task[] running;
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
                }

                running = new Task[_runningTasks.Count];
                _runningTasks.CopyTo(running);
            }

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
