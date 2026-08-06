using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace FlatWorld.WorldModel
{
    public interface IWorldAddressNormalizer
    {
        WorldAddress Normalize(WorldAddress address);
    }

    public sealed class IdentityWorldAddressNormalizer : IWorldAddressNormalizer
    {
        public static readonly IdentityWorldAddressNormalizer Instance =
            new IdentityWorldAddressNormalizer();
        private IdentityWorldAddressNormalizer() { }
        public WorldAddress Normalize(WorldAddress address) => address;
    }

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
        public WorldAddress Center { get; }
        public int ActiveDistance { get; }
        public int DestroyDistance { get; }
        public bool RequestPresentation { get; }
        public int WorldSeed { get; }
        public ChunkGenerationProfileSnapshot Profile { get; }
        public ChunkGenerationTopologySnapshot Topology { get; }
    }

    public sealed class ChunkMgr : IDisposable
    {
        private sealed class PendingGeneration
        {
            public ChunkGenerationRequest Request;
            public CancellationTokenSource Cancellation;
            public TaskCompletionSource<ChunkRuntime> Completion;
        }

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

        public WorldRuntime World { get; }
        public IReadOnlyDictionary<WorldAddress, ChunkRuntime> Chunks => World.Chunks;
        public bool HasPendingChunkLoads => _pending.Count > 0;
        public bool HasUnsettledGenerationTasks => _generationTasks.Count > 0;
        public int MaxGenerationConcurrency => _scheduler.MaxConcurrency;
        public IEnumerable<WorldAddress> PresentationDemand =>
            _presentationDemand.Count == 0 ? _emptyAddresses : _presentationDemand;

        public bool TryGetChunk(WorldAddress address, out ChunkRuntime chunk) =>
            World.TryGetChunk(_normalizer.Normalize(address), out chunk);

        public ChunkLease AcquireLease(WorldAddress address, ChunkLeaseKind kind) =>
            World.AcquireChunkLease(_normalizer.Normalize(address), kind);

        public Task<ChunkRuntime> RequestChunkDataAsync(WorldAddress address, int worldSeed,
            ChunkGenerationProfileSnapshot profile, CancellationToken cancellationToken = default,
            ChunkGenerationTopologySnapshot topology = default)
        {
            ThrowIfDisposed();
            address = _normalizer.Normalize(address);
            if (World.TryGetChunk(address, out ChunkRuntime existing) &&
                existing.DataStatus == ChunkDataStatus.Ready)
                return Task.FromResult(existing);
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
            task.ContinueWith(completed => _completed.Enqueue(
                    new GenerationCompletion(address, pending, completed)),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return pending.Completion.Task;
        }

        public void RefreshWindow(ChunkWindowRequest request)
        {
            RefreshWindows(new[] { request });
        }

        public void RefreshWindows(IReadOnlyList<ChunkWindowRequest> requests)
        {
            ThrowIfDisposed();
            if (requests == null)
                throw new ArgumentNullException(nameof(requests));
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

        public int CommitCompleted()
        {
            ThrowIfDisposed();
            int count = 0;
            while (_completed.TryDequeue(out GenerationCompletion completion))
            {
                count++;
                _generationTasks.Remove(completion.Task);
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

        public void Advance(float deltaSeconds, bool authoritativeSimulation = true)
        {
            CommitCompleted();
            if (authoritativeSimulation)
                World.Tick(deltaSeconds);
        }

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

        public void CancelAllRequests()
        {
            ThrowIfDisposed();
            var addresses = new List<WorldAddress>(_pending.Keys);
            addresses.Sort();
            for (int i = 0; i < addresses.Count; i++)
                CancelChunkRequest(addresses[i]);
        }

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

    public readonly struct ChunkPresentationDemandChanged
    {
        public ChunkPresentationDemandChanged(WorldAddress address, bool requested)
        { Address = address; Requested = requested; }
        public WorldAddress Address { get; }
        public bool Requested { get; }
    }

    public readonly struct ChunkGenerationFailed
    {
        public ChunkGenerationFailed(WorldAddress address, string reason)
        { Address = address; Reason = reason; }
        public WorldAddress Address { get; }
        public string Reason { get; }
    }
}
