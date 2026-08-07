using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 区块生成任务的后台排队员。
    /// 任务按进入队列的先后顺序分给几个后台工作人员，每个任务最后都会报告成功、失败或取消。
    /// </summary>
    public sealed class ChunkGenerationScheduler : IDisposable
    {
        /// <summary>排队中的一项工作：任务单、取消信号，以及把结果交回去的通道。</summary>
        private sealed class WorkItem
        {
            public ChunkGenerationRequest Request;
            public CancellationToken CancellationToken;
            public TaskCompletionSource<ChunkGenerationResult> Completion;
        }

        private readonly IChunkPureGenerator _generator;
        private readonly Queue<WorkItem> _queue = new Queue<WorkItem>();
        private readonly SemaphoreSlim _workAvailable = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
        private readonly Task[] _workers;
        private readonly object _gate = new object();
        private bool _disposed;

        public ChunkGenerationScheduler(IChunkPureGenerator generator, int maxConcurrency = 2)
        {
            _generator = generator ?? throw new ArgumentNullException(nameof(generator));
            if (maxConcurrency <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

            MaxConcurrency = maxConcurrency;
            _workers = new Task[maxConcurrency];
            for (int i = 0; i < _workers.Length; i++)
                _workers[i] = Task.Run(WorkerLoopAsync);
        }

        /// <summary>一共有几个后台工作人员可以同时生成区块。</summary>
        public int MaxConcurrency { get; }

        /// <summary>
        /// 把一个生成任务放到队尾，并马上返回一个可等待的 Task。
        /// 无论任务还在排队还是已经开始，都可以通过取消信号停止它。
        /// </summary>
        public Task<ChunkGenerationResult> ScheduleAsync(ChunkGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ChunkGenerationScheduler));

                // 让等待结果的后续代码稍后单独运行，避免它在锁还没放开时卡住排队员。
                var completion = new TaskCompletionSource<ChunkGenerationResult>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _queue.Enqueue(new WorkItem
                {
                    Request = request,
                    CancellationToken = cancellationToken,
                    Completion = completion
                });
                _workAvailable.Release();
                return completion.Task;
            }
        }

        /// <summary>
        /// 关闭排队员：不再接新任务，通知后台人员停工，并取消仍在队列里的工作。
        /// 已经交出去的结果仍由收到它的人负责清理。
        /// </summary>
        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _shutdown.Cancel();
                // 后台人员可能正在睡觉等任务，所以逐个叫醒，让它们看见“要关闭了”。
                for (int i = 0; i < _workers.Length; i++)
                    _workAvailable.Release();

                while (_queue.Count > 0)
                {
                    WorkItem pending = _queue.Dequeue();
                    pending.Completion.TrySetCanceled();
                }
            }

            try
            {
                Task.WaitAll(_workers, TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // 每项工作会自己报告失败；这里即使关闭多次也要保持安全。
            }

            _shutdown.Dispose();
            _workAvailable.Dispose();
        }

        private async Task WorkerLoopAsync()
        {
            while (true)
            {
                try
                {
                    await _workAvailable.WaitAsync(_shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                // 这个信号只表示“可能有任务，或者要下班了”；真正拿队列时仍要加锁保护。
                WorkItem work = null;
                lock (_gate)
                {
                    if (_queue.Count > 0)
                        work = _queue.Dequeue();
                    else if (_disposed)
                        return;
                }

                if (work == null)
                    continue;

                // 还在排队时就取消了，直接报告取消，不再浪费时间生成。
                if (work.CancellationToken.IsCancellationRequested)
                {
                    work.Completion.TrySetCanceled();
                    continue;
                }

                try
                {
                    // 无论是整个排队员关闭，还是这一个任务被取消，生成器都会收到停止信号。
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                               _shutdown.Token, work.CancellationToken))
                    {
                        ChunkGenerationResult result = _generator.Generate(work.Request, linked.Token);
                        if (linked.IsCancellationRequested)
                        {
                            // 有时结果刚做好，取消信号才到；这种没人使用的结果要马上释放。
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
        }
    }
}
