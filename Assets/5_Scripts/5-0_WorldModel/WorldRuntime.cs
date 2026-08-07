using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 整个世界在运行时的“区块总管”。
    /// 它保存所有区块，接收生成结果，发放使用票，也负责保存副本和删除不用的区块。
    /// 玩家、怪物和物品不归它管理，仍由原来的 Item/Module 系统负责。
    /// </summary>
    public sealed class WorldRuntime : IDisposable
    {
        private readonly Dictionary<WorldAddress, ChunkRuntime> chunks =
            new Dictionary<WorldAddress, ChunkRuntime>();
        private readonly ReadOnlyDictionary<WorldAddress, ChunkRuntime> readOnlyChunks;
        private bool disposed;

        public WorldRuntime(string worldId, long epoch)
        {
            if (string.IsNullOrWhiteSpace(worldId))
                throw new ArgumentException("World id is required.", nameof(worldId));
            if (epoch <= 0)
                throw new ArgumentOutOfRangeException(nameof(epoch));
            WorldId = worldId;
            Epoch = epoch;
            Events = new WorldEventBus();
            readOnlyChunks = new ReadOnlyDictionary<WorldAddress, ChunkRuntime>(chunks);
        }

        /// <summary>这个世界的名字或唯一标识。</summary>
        public string WorldId { get; }
        /// <summary>当前世界的版本号。重新进入世界后会变大，用来识别上一个世界留下的旧任务。</summary>
        public long Epoch { get; private set; }
        /// <summary>区块状态改变、生成完成或被删除时，通过这里通知其他系统。</summary>
        public WorldEventBus Events { get; }
        /// <summary>查看当前世界里的所有区块；外部不能直接增删这个表。</summary>
        public IReadOnlyDictionary<WorldAddress, ChunkRuntime> Chunks => readOnlyChunks;
        /// <summary>这个世界的逻辑更新已经运行了多少次；它不一定等于画面帧数。</summary>
        public ulong TickIndex { get; private set; }

        /// <summary>
        /// 为某个区块准备一张新的生成任务单。
        /// 任务单会带上世界版本和任务编号，后台做完后才能确认结果是不是仍然有效。
        /// </summary>
        public ChunkGenerationRequest BeginChunkGeneration(WorldAddress address, int worldSeed,
            ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology = default)
        {
            ThrowIfDisposed();
            return GetOrCreateChunk(address).BeginGeneration(Epoch, worldSeed, profile, topology);
        }

        /// <summary>
        /// 尝试接收后台做好的地形。
        /// 只有世界版本、区块地址、任务编号和当前状态都对得上，结果才会正式生效；
        /// 对不上的旧结果会被丢掉，并告诉调用方为什么没接收。
        /// </summary>
        public bool TryCommit(ChunkGenerationResult result, out string rejectionReason)
        {
            ThrowIfDisposed();
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            ChunkGenerationRequest request = result.Request;
            if (request.WorldEpoch != Epoch)
                return Reject(result, "World epoch no longer matches.", out rejectionReason);
            if (!chunks.TryGetValue(request.Address, out ChunkRuntime chunk))
                return Reject(result, "Chunk request is no longer registered.", out rejectionReason);
            if (request.RequestVersion != chunk.GenerationVersion)
                return Reject(result, "Chunk request version is stale.", out rejectionReason);
            if (chunk.DataStatus != ChunkDataStatus.Generating)
                return Reject(result, $"Chunk is not generating ({chunk.DataStatus}).", out rejectionReason);

            // 先把临时地形整理成正式数据。这个过程如果出错，原来的区块不会被改坏。
            ChunkTerrainData terrain;
            try
            {
                terrain = result.ConsumeTerrain();
            }
            catch (Exception exception)
            {
                return Reject(result, $"Terrain materialization failed: {exception.Message}",
                    out rejectionReason);
            }

            // 新地形交给区块后，临时结果就不再负责保管这份数据。
            chunk.ApplyGeneratedData(terrain);
            result.Dispose();
            Events.Publish(new ChunkCommitted(request.Address, request.RequestVersion,
                terrain.ComputeStableHash()));
            rejectionReason = null;
            return true;
        }

        /// <summary>
        /// 记录后台任务为什么失败。来自旧世界或旧任务的失败消息会被忽略。
        /// </summary>
        public void RejectFailedGeneration(ChunkGenerationRequest request, Exception exception)
        {
            ThrowIfDisposed();
            if (request.WorldEpoch == Epoch && chunks.TryGetValue(request.Address, out ChunkRuntime chunk))
                chunk.MarkGenerationFailed(request.RequestVersion, exception?.Message);
        }

        /// <summary>取消这个区块当前的生成任务，让它之后即使完成也不能再生效。</summary>
        public bool CancelChunkGeneration(WorldAddress address)
        {
            ThrowIfDisposed();
            return chunks.TryGetValue(address, out ChunkRuntime chunk) &&
                   CancelChunkGeneration(chunk);
        }

        /// <summary>领取一张区块使用票；区块还不存在时会先创建一个空壳。</summary>
        public ChunkLease AcquireChunkLease(WorldAddress address, ChunkLeaseKind kind)
        {
            ThrowIfDisposed();
            return GetOrCreateChunk(address).AcquireLease(kind);
        }

        /// <summary>按地址查找区块，即使它还没做好或已经失败也能查到。</summary>
        public bool TryGetChunk(WorldAddress address, out ChunkRuntime chunk)
        {
            ThrowIfDisposed();
            return chunks.TryGetValue(address, out chunk);
        }

        /// <summary>只有区块确实准备好了，才返回当前正式使用的地形。</summary>
        public bool TryGetChunkTerrain(WorldAddress address, out ChunkTerrainData terrain)
        {
            terrain = null;
            return TryGetChunk(address, out ChunkRuntime chunk) &&
                   chunk.DataStatus == ChunkDataStatus.Ready &&
                   (terrain = chunk.Terrain) != null;
        }

        /// <summary>
        /// 让世界逻辑向前走一步。
        /// 目前这里只把次数加 1；deltaSeconds 表示这一步经过了多少秒，不能是负数。
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            ThrowIfDisposed();
            if (deltaSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            TickIndex++;
        }

        /// <summary>
        /// 把所有已经做好的区块完整复制一份，得到这个世界此刻的“照片”。
        /// 复制前会排好顺序，保证存档和测试每次得到相同排列。
        /// </summary>
        public WorldRuntimeSnapshot CaptureSnapshot()
        {
            ThrowIfDisposed();
            var snapshots = new List<ChunkRuntimeSnapshot>();
            var addresses = new List<WorldAddress>(chunks.Keys);
            addresses.Sort();
            for (int i = 0; i < addresses.Count; i++)
            {
                ChunkRuntime chunk = chunks[addresses[i]];
                if (chunk.DataStatus == ChunkDataStatus.Ready && chunk.Terrain != null)
                    snapshots.Add(ChunkRuntimeSnapshot.Capture(chunk));
            }
            return new WorldRuntimeSnapshot(WorldId, Epoch, snapshots);
        }

        /// <summary>尝试复制一个已经做好的区块；没做好时不会返回半成品。</summary>
        public bool TryCaptureChunkSnapshot(WorldAddress address, out ChunkRuntimeSnapshot snapshot)
        {
            snapshot = null;
            if (!TryGetChunk(address, out ChunkRuntime chunk) ||
                chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null)
                return false;
            snapshot = ChunkRuntimeSnapshot.Capture(chunk);
            return true;
        }

        /// <summary>
        /// 尝试删除一个区块。
        /// 只有运行逻辑、画面和寻路的使用票都已经归还，才能真正删除；否则返回 false。
        /// </summary>
        public bool EvictChunk(WorldAddress address)
        {
            ThrowIfDisposed();
            if (!chunks.TryGetValue(address, out ChunkRuntime chunk))
                return false;
            if (chunk.SimulationLeaseCount > 0 || chunk.PresentationLeaseCount > 0 ||
                chunk.NavigationLeaseCount > 0)
                return false;
            chunk.BeginEviction();
            chunks.Remove(address);
            chunk.Dispose();
            Events.Publish(new ChunkEvicted(address));
            return true;
        }

        /// <summary>
        /// 把世界版本换成一个更大的新编号，并把逻辑计数清零。
        /// 它不会自动删掉现有区块，但旧世界留下的生成结果从此不能再生效。
        /// </summary>
        public void BeginNewEpoch(long epoch)
        {
            ThrowIfDisposed();
            if (epoch <= Epoch)
                throw new ArgumentOutOfRangeException(nameof(epoch));
            Epoch = epoch;
            TickIndex = 0;
        }

        /// <summary>关闭世界，释放所有区块并清空通知；重复调用也不会出错。</summary>
        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            foreach (ChunkRuntime chunk in chunks.Values)
                chunk.Dispose();
            chunks.Clear();
            Events.Clear();
        }

        private ChunkRuntime GetOrCreateChunk(WorldAddress address)
        {
            // 只有这个总管能把新区块放进总表，外面只能查看。
            if (!chunks.TryGetValue(address, out ChunkRuntime chunk))
            {
                chunk = new ChunkRuntime(address, Events);
                chunks.Add(address, chunk);
            }
            return chunk;
        }

        private static bool CancelChunkGeneration(ChunkRuntime chunk)
        {
            chunk.InvalidateGeneration();
            return true;
        }

        private static bool Reject(ChunkGenerationResult result, string reason,
            out string rejectionReason)
        {
            result.Dispose();
            rejectionReason = reason;
            return false;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(WorldRuntime));
        }
    }
}
