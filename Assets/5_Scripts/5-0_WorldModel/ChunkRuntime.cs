using System;

namespace FlatWorld.WorldModel
{
    public sealed class ChunkRuntime : IDisposable
    {
        private readonly WorldEventBus _events;
        private int _simulationLeaseCount;
        private int _presentationLeaseCount;
        private int _navigationLeaseCount;
        private bool _disposed;

        internal ChunkRuntime(WorldAddress address, WorldEventBus events)
        {
            Address = address;
            _events = events ?? throw new ArgumentNullException(nameof(events));
            Occupancy = new ChunkOccupancyData();
            DataStatus = ChunkDataStatus.Absent;
            SimulationStatus = ChunkSimulationStatus.Dormant;
            PresentationStatus = ChunkPresentationStatus.Unbound;
        }

        public WorldAddress Address { get; }
        public ChunkTerrainData Terrain { get; private set; }
        public ChunkOccupancyData Occupancy { get; }
        public ChunkDataStatus DataStatus { get; private set; }
        public ChunkSimulationStatus SimulationStatus { get; private set; }
        public ChunkPresentationStatus PresentationStatus { get; private set; }
        public long GenerationVersion { get; private set; }
        public string FailureReason { get; private set; }
        public int SimulationLeaseCount => _simulationLeaseCount;
        public int PresentationLeaseCount => _presentationLeaseCount;
        public int NavigationLeaseCount => _navigationLeaseCount;
        public bool HasNavigationLease => _navigationLeaseCount > 0;

        public ChunkLease AcquireLease(ChunkLeaseKind kind)
        {
            ThrowIfDisposed();
            switch (kind)
            {
                case ChunkLeaseKind.Simulation:
                    _simulationLeaseCount++;
                    SetSimulationStatus(ChunkSimulationStatus.Active);
                    break;
                case ChunkLeaseKind.Presentation:
                    _presentationLeaseCount++;
                    if (PresentationStatus == ChunkPresentationStatus.Unbound)
                        SetPresentationStatus(ChunkPresentationStatus.Binding);
                    break;
                case ChunkLeaseKind.Navigation:
                    _navigationLeaseCount++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
            return new ChunkLease(this, kind);
        }

        public void MarkPresentationBound()
        {
            ThrowIfDisposed();
            if (_presentationLeaseCount <= 0)
                throw new InvalidOperationException("A presentation lease is required before binding.");
            SetPresentationStatus(ChunkPresentationStatus.Bound);
        }

        public void MarkPresentationUnbound()
        {
            ThrowIfDisposed();
            if (_presentationLeaseCount > 0)
                SetPresentationStatus(ChunkPresentationStatus.Binding);
            else
                SetPresentationStatus(ChunkPresentationStatus.Unbound);
        }

        internal ChunkGenerationRequest BeginGeneration(long epoch, int seed,
            ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology = default)
        {
            ThrowIfDisposed();
            GenerationVersion++;
            FailureReason = null;
            SetDataStatus(ChunkDataStatus.Requested);
            SetDataStatus(ChunkDataStatus.Generating);
            return new ChunkGenerationRequest(epoch, Address, seed, GenerationVersion, profile, topology);
        }

        internal void InvalidateGeneration()
        {
            ThrowIfDisposed();
            GenerationVersion++;
            if (DataStatus == ChunkDataStatus.Generating || DataStatus == ChunkDataStatus.Requested)
                SetDataStatus(Terrain == null ? ChunkDataStatus.Absent : ChunkDataStatus.Ready);
        }

        internal void MarkGenerationFailed(long requestVersion, string reason)
        {
            ThrowIfDisposed();
            if (requestVersion != GenerationVersion)
                return;
            FailureReason = string.IsNullOrWhiteSpace(reason) ? "Chunk generation failed." : reason;
            SetDataStatus(ChunkDataStatus.Failed);
        }

        internal void ApplyGeneratedData(ChunkTerrainData terrain)
        {
            ThrowIfDisposed();
            if (terrain == null)
                throw new ArgumentNullException(nameof(terrain));

            ChunkTerrainData previous = Terrain;
            Terrain = terrain;
            Occupancy.Clear();
            FailureReason = null;
            SetDataStatus(ChunkDataStatus.Ready);
            previous?.Dispose();
        }

        internal void BeginEviction() => SetDataStatus(ChunkDataStatus.Evicting);

        internal void ReleaseLease(ChunkLeaseKind kind)
        {
            if (_disposed)
                return;
            switch (kind)
            {
                case ChunkLeaseKind.Simulation:
                    _simulationLeaseCount = Math.Max(0, _simulationLeaseCount - 1);
                    if (_simulationLeaseCount == 0)
                        SetSimulationStatus(ChunkSimulationStatus.Dormant);
                    break;
                case ChunkLeaseKind.Presentation:
                    _presentationLeaseCount = Math.Max(0, _presentationLeaseCount - 1);
                    if (_presentationLeaseCount == 0)
                        SetPresentationStatus(ChunkPresentationStatus.Unbound);
                    break;
                case ChunkLeaseKind.Navigation:
                    _navigationLeaseCount = Math.Max(0, _navigationLeaseCount - 1);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Terrain?.Dispose();
            Terrain = null;
            _simulationLeaseCount = 0;
            _presentationLeaseCount = 0;
            _navigationLeaseCount = 0;
        }

        private void SetDataStatus(ChunkDataStatus value)
        {
            if (DataStatus == value)
                return;
            ChunkDataStatus previous = DataStatus;
            DataStatus = value;
            _events.Publish(new ChunkDataStatusChanged(Address, previous, value));
        }

        private void SetSimulationStatus(ChunkSimulationStatus value)
        {
            if (SimulationStatus == value)
                return;
            ChunkSimulationStatus previous = SimulationStatus;
            SimulationStatus = value;
            _events.Publish(new ChunkSimulationStatusChanged(Address, previous, value));
        }

        private void SetPresentationStatus(ChunkPresentationStatus value)
        {
            if (PresentationStatus == value)
                return;
            ChunkPresentationStatus previous = PresentationStatus;
            PresentationStatus = value;
            _events.Publish(new ChunkPresentationStatusChanged(Address, previous, value));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ChunkRuntime));
        }
    }

    public sealed class ChunkLease : IDisposable
    {
        private ChunkRuntime _chunk;

        internal ChunkLease(ChunkRuntime chunk, ChunkLeaseKind kind)
        {
            _chunk = chunk;
            Kind = kind;
        }

        public ChunkLeaseKind Kind { get; }
        public WorldAddress Address => _chunk == null
            ? throw new ObjectDisposedException(nameof(ChunkLease))
            : _chunk.Address;

        public void Dispose()
        {
            if (_chunk == null)
                return;
            _chunk.ReleaseLease(Kind);
            _chunk = null;
        }
    }
}
