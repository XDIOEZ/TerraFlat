using System;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 一个正在游戏中使用的区块。
    /// 它分别记录三件事：地图数据有没有做好、游戏逻辑要不要运行、画面有没有显示出来。
    /// 这三件事互不捆绑，例如一个区块可以有数据，但暂时不运行也不显示。
    /// </summary>
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

        /// <summary>这个区块在世界里的地址，创建后不会改变。</summary>
        public WorldAddress Address { get; }
        /// <summary>当前正式使用的地形数据；还没生成好时是 null。</summary>
        public ChunkTerrainData Terrain { get; private set; }
        /// <summary>当前区块的自然物品确定性放置结果。</summary>
        public ChunkEcologyData Ecology { get; private set; } = ChunkEcologyData.Empty;
        /// <summary>哪些格子被建筑或物品占用了。</summary>
        public ChunkOccupancyData Occupancy { get; }
        /// <summary>地形现在处于“未请求、生成中、可使用、失败或删除中”的哪个阶段。</summary>
        public ChunkDataStatus DataStatus { get; private set; }
        /// <summary>这个区块里的游戏逻辑现在是运行还是休眠。</summary>
        public ChunkSimulationStatus SimulationStatus { get; private set; }
        /// <summary>Unity 画面现在是未连接、连接中还是已经连接。</summary>
        public ChunkPresentationStatus PresentationStatus { get; private set; }
        /// <summary>最新生成任务的编号；旧任务晚回来时，靠这个编号认出并丢掉它。</summary>
        public long GenerationVersion { get; private set; }
        /// <summary>最近一次生成失败的原因；重新生成或成功后会清空。</summary>
        public string FailureReason { get; private set; }
        /// <summary>现在有多少地方要求这个区块继续运行游戏逻辑。</summary>
        public int SimulationLeaseCount => _simulationLeaseCount;
        /// <summary>现在有多少地方要求这个区块继续显示画面。</summary>
        public int PresentationLeaseCount => _presentationLeaseCount;
        /// <summary>现在有多少寻路任务正在使用这个区块。</summary>
        public int NavigationLeaseCount => _navigationLeaseCount;
        /// <summary>是否还有寻路系统正在使用这个区块。</summary>
        public bool HasNavigationLease => _navigationLeaseCount > 0;

        /// <summary>
        /// 领取一张“我还在使用这个区块”的票。
        /// 用完后必须归还。只有同类票全部归还，区块才会停止运行或隐藏画面。
        /// </summary>
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

        /// <summary>地图、碰撞等画面内容都接好后，用这个方法告诉区块“已经显示好了”。</summary>
        public void MarkPresentationBound()
        {
            ThrowIfDisposed();
            if (_presentationLeaseCount <= 0)
                throw new InvalidOperationException("A presentation lease is required before binding.");
            SetPresentationStatus(ChunkPresentationStatus.Bound);
        }

        /// <summary>
        /// 告诉区块“原来的画面已经拆掉了”。
        /// 如果仍有人需要画面，就等待重新连接；没人需要，就保持隐藏。
        /// </summary>
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
            // 先换一个新的任务编号。这样之前还没做完的旧任务回来时，就不会被误用。
            GenerationVersion++;
            FailureReason = null;
            SetDataStatus(ChunkDataStatus.Requested);
            SetDataStatus(ChunkDataStatus.Generating);
            return new ChunkGenerationRequest(epoch, Address, seed, GenerationVersion, profile, topology);
        }

        internal void InvalidateGeneration()
        {
            ThrowIfDisposed();
            // 即使眼下看不到任务，也换掉任务编号，确保外面拿着的旧结果不能再交回来。
            GenerationVersion++;
            if (DataStatus == ChunkDataStatus.Generating || DataStatus == ChunkDataStatus.Requested)
                SetDataStatus(Terrain == null ? ChunkDataStatus.Absent : ChunkDataStatus.Ready);
        }

        internal void MarkGenerationFailed(long requestVersion, string reason)
        {
            ThrowIfDisposed();
            // 如果失败消息来自旧任务，就忽略它，不能让它盖掉新任务的状态。
            if (requestVersion != GenerationVersion)
                return;
            FailureReason = string.IsNullOrWhiteSpace(reason) ? "Chunk generation failed." : reason;
            SetDataStatus(ChunkDataStatus.Failed);
        }

        internal void ApplyGeneratedData(ChunkTerrainData terrain,
            ChunkEcologyData ecology = null)
        {
            ThrowIfDisposed();
            if (terrain == null)
                throw new ArgumentNullException(nameof(terrain));

            // 先把新地形设为正式数据，再释放旧地形，避免同时占着两份大地图内存。
            ChunkTerrainData previous = Terrain;
            Terrain = terrain;
            Ecology = ecology ?? ChunkEcologyData.Empty;
            Occupancy.Clear();
            FailureReason = null;
            SetDataStatus(ChunkDataStatus.Ready);
            previous?.Dispose();
        }

        /// <summary>世界准备删除这个区块时，先把状态改成“删除中”。</summary>
        internal void BeginEviction() => SetDataStatus(ChunkDataStatus.Evicting);

        /// <summary>归还一张使用票；如果这是最后一张，就让对应功能休眠或隐藏。</summary>
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
            Ecology = ChunkEcologyData.Empty;
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

    /// <summary>
    /// 一张“我还在使用这个区块”的票。
    /// 它不拥有地图数据，只负责在用完时把计数减回去；重复归还也不会出错。
    /// </summary>
    public sealed class ChunkLease : IDisposable
    {
        private ChunkRuntime _chunk;

        internal ChunkLease(ChunkRuntime chunk, ChunkLeaseKind kind)
        {
            _chunk = chunk;
            Kind = kind;
        }

        /// <summary>这张票是为了运行逻辑、显示画面，还是寻路。</summary>
        public ChunkLeaseKind Kind { get; }
        /// <summary>这张票属于哪个区块；票归还后就不能再读取。</summary>
        public WorldAddress Address => _chunk == null
            ? throw new ObjectDisposedException(nameof(ChunkLease))
            : _chunk.Address;

        /// <summary>归还这张使用票；重复归还不会出错。</summary>
        public void Dispose()
        {
            if (_chunk == null)
                return;
            _chunk.ReleaseLease(Kind);
            _chunk = null;
        }
    }
}
