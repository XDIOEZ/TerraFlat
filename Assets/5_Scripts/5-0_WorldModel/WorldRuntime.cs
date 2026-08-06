using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// Engine-free authority for chunk data and chunk lifecycle only. Gameplay entities remain
    /// owned by the existing Item/Module runtime.
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

        public string WorldId { get; }
        public long Epoch { get; private set; }
        public WorldEventBus Events { get; }
        public IReadOnlyDictionary<WorldAddress, ChunkRuntime> Chunks => readOnlyChunks;
        public ulong TickIndex { get; private set; }

        public ChunkGenerationRequest BeginChunkGeneration(WorldAddress address, int worldSeed,
            ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology = default)
        {
            ThrowIfDisposed();
            return GetOrCreateChunk(address).BeginGeneration(Epoch, worldSeed, profile, topology);
        }

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

            chunk.ApplyGeneratedData(terrain);
            result.Dispose();
            Events.Publish(new ChunkCommitted(request.Address, request.RequestVersion,
                terrain.ComputeStableHash()));
            rejectionReason = null;
            return true;
        }

        public void RejectFailedGeneration(ChunkGenerationRequest request, Exception exception)
        {
            ThrowIfDisposed();
            if (request.WorldEpoch == Epoch && chunks.TryGetValue(request.Address, out ChunkRuntime chunk))
                chunk.MarkGenerationFailed(request.RequestVersion, exception?.Message);
        }

        public bool CancelChunkGeneration(WorldAddress address)
        {
            ThrowIfDisposed();
            return chunks.TryGetValue(address, out ChunkRuntime chunk) &&
                   CancelChunkGeneration(chunk);
        }

        public ChunkLease AcquireChunkLease(WorldAddress address, ChunkLeaseKind kind)
        {
            ThrowIfDisposed();
            return GetOrCreateChunk(address).AcquireLease(kind);
        }

        public bool TryGetChunk(WorldAddress address, out ChunkRuntime chunk)
        {
            ThrowIfDisposed();
            return chunks.TryGetValue(address, out chunk);
        }

        public bool TryGetChunkTerrain(WorldAddress address, out ChunkTerrainData terrain)
        {
            terrain = null;
            return TryGetChunk(address, out ChunkRuntime chunk) &&
                   chunk.DataStatus == ChunkDataStatus.Ready &&
                   (terrain = chunk.Terrain) != null;
        }

        public void Tick(float deltaSeconds)
        {
            ThrowIfDisposed();
            if (deltaSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            TickIndex++;
        }

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

        public bool TryCaptureChunkSnapshot(WorldAddress address, out ChunkRuntimeSnapshot snapshot)
        {
            snapshot = null;
            if (!TryGetChunk(address, out ChunkRuntime chunk) ||
                chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null)
                return false;
            snapshot = ChunkRuntimeSnapshot.Capture(chunk);
            return true;
        }

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

        public void BeginNewEpoch(long epoch)
        {
            ThrowIfDisposed();
            if (epoch <= Epoch)
                throw new ArgumentOutOfRangeException(nameof(epoch));
            Epoch = epoch;
            TickIndex = 0;
        }

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
