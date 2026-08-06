using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace FlatWorld.WorldModel
{
    /// <summary>Engine-free generation domain. A default value keeps infinite-world sampling.</summary>
    public readonly struct ChunkGenerationTopologySnapshot
    {
        public ChunkGenerationTopologySnapshot(Int2 min, Int2 span)
        {
            if (span.X <= 0)
                throw new ArgumentOutOfRangeException(nameof(span));
            if (span.Y <= 0)
                throw new ArgumentOutOfRangeException(nameof(span));
            Min = min;
            Span = span;
            IsWrapped = true;
        }

        public bool IsWrapped { get; }
        public Int2 Min { get; }
        public Int2 Span { get; }

        public int NormalizeX(int value) => IsWrapped ? Wrap(value, Min.X, Span.X) : value;
        public int NormalizeY(int value) => IsWrapped ? Wrap(value, Min.Y, Span.Y) : value;

        private static int Wrap(int value, int min, int span)
        {
            long offset = (long)value - min;
            long wrapped = offset % span;
            if (wrapped < 0L)
                wrapped += span;
            return (int)(min + wrapped);
        }
    }

    public sealed class ChunkGenerationProfileSnapshot
    {
        private readonly IReadOnlyDictionary<string, double> numericParameters;
        private readonly IReadOnlyDictionary<string, string> textParameters;

        public ChunkGenerationProfileSnapshot(string profileId, int signature, int width, int height,
            IDictionary<string, double> numericParameters = null,
            IDictionary<string, string> textParameters = null)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                throw new ArgumentException("Profile id is required.", nameof(profileId));
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
            ProfileId = profileId;
            Signature = signature;
            Width = width;
            Height = height;
            this.numericParameters = new ReadOnlyDictionary<string, double>(
                numericParameters == null
                    ? new Dictionary<string, double>(StringComparer.Ordinal)
                    : new Dictionary<string, double>(numericParameters, StringComparer.Ordinal));
            this.textParameters = new ReadOnlyDictionary<string, string>(
                textParameters == null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(textParameters, StringComparer.Ordinal));
            Settings = new ChunkGenerationSettingsSnapshot(this.numericParameters, this.textParameters);
        }

        public string ProfileId { get; }
        public int Signature { get; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyDictionary<string, double> NumericParameters => numericParameters;
        public IReadOnlyDictionary<string, string> TextParameters => textParameters;
        public ChunkGenerationSettingsSnapshot Settings { get; }
    }

    public readonly struct ChunkGenerationRequest
    {
        public ChunkGenerationRequest(long worldEpoch, WorldAddress address, int worldSeed,
            long requestVersion, ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology = default)
        {
            if (worldEpoch <= 0)
                throw new ArgumentOutOfRangeException(nameof(worldEpoch));
            if (requestVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(requestVersion));
            WorldEpoch = worldEpoch;
            Address = address;
            WorldSeed = worldSeed;
            RequestVersion = requestVersion;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Topology = topology;
        }

        public long WorldEpoch { get; }
        public WorldAddress Address { get; }
        public int WorldSeed { get; }
        public long RequestVersion { get; }
        public ChunkGenerationProfileSnapshot Profile { get; }
        public ChunkGenerationTopologySnapshot Topology { get; }
    }

    /// <summary>Owns only generated chunk data. Item/Module spawning remains in GamePlay.</summary>
    public sealed class ChunkGenerationResult : IDisposable
    {
        private ChunkTerrainBuffer terrain;
        private bool disposed;

        public ChunkGenerationResult(ChunkGenerationRequest request, ChunkTerrainBuffer terrain)
        {
            Request = request;
            this.terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        }

        public ChunkGenerationRequest Request { get; }
        public bool WasConsumed { get; private set; }
        public bool IsDisposed => disposed;

        internal ChunkTerrainData ConsumeTerrain()
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ChunkGenerationResult));
            if (WasConsumed)
                throw new InvalidOperationException("Generation result was already consumed.");
            WasConsumed = true;
            ChunkTerrainData result = terrain.Seal();
            terrain = null;
            return result;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            terrain?.Dispose();
            terrain = null;
        }
    }

    public interface IChunkPureGenerator
    {
        ChunkGenerationResult Generate(ChunkGenerationRequest request, CancellationToken cancellationToken);
    }
}
