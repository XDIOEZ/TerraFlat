using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlatWorld.WorldModel
{
    public sealed class ChunkGenerationScheduler : IDisposable
    {
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

        public int MaxConcurrency { get; }

        public Task<ChunkGenerationResult> ScheduleAsync(ChunkGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ChunkGenerationScheduler));

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

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _shutdown.Cancel();
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
                // Each worker reports failures through its work item. Shutdown must remain idempotent.
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

                if (work.CancellationToken.IsCancellationRequested)
                {
                    work.Completion.TrySetCanceled();
                    continue;
                }

                try
                {
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                               _shutdown.Token, work.CancellationToken))
                    {
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
