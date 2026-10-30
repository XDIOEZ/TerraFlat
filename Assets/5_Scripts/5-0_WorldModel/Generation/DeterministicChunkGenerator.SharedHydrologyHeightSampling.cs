using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 宏观水文基础高度共享

        /// <summary>读取共享高度缓存的实际占用，不改变生成状态。</summary>
        public object CaptureSharedHydrologyHeightSamplingDiagnostics() =>
            hydrologyRegions.sharedHeightSamples.CaptureDiagnostics();

        // 宏观区域完成后才共享基础高度，每个输入身份和坐标均有固定容量。
        internal sealed class SharedHydrologyHeightSamplingCache
        {
            private const int MaximumPartitions = 4;
            private const int MaximumSamplesPerPartition = 32768;
            private readonly object gate = new();
            private readonly Dictionary<HeightSamplingIdentity, Partition> partitions = new();
            private readonly Queue<HeightSamplingIdentity> partitionOrder = new();
            private long retiredEpoch = long.MinValue;

            internal Partition GetPartition(ChunkGenerationRequest request, ChunkGenerationSettingsSnapshot settings)
            {
                var identity = new HeightSamplingIdentity(request, settings);
                lock (gate)
                {
                    if (request.WorldEpoch <= retiredEpoch) return null;
                    if (partitions.TryGetValue(identity, out Partition existing)) return existing;
                    if (partitions.Count == MaximumPartitions)
                    {
                        HeightSamplingIdentity oldest = partitionOrder.Dequeue();
                        Partition removed = partitions[oldest];
                        partitions.Remove(oldest);
                        removed.Retire();
                    }
                    var created = new Partition();
                    partitions.Add(identity, created);
                    partitionOrder.Enqueue(identity);
                    return created;
                }
            }

            internal void RetireWorld(long epoch)
            {
                lock (gate)
                {
                    retiredEpoch = Math.Max(retiredEpoch, epoch);
                    foreach (Partition partition in partitions.Values) partition.Retire();
                    partitions.Clear();
                    partitionOrder.Clear();
                }
            }

            internal object CaptureDiagnostics()
            {
                lock (gate)
                {
                    int samples = 0;
                    foreach (Partition partition in partitions.Values) samples += partition.Count;
                    return new { partitions = partitions.Count, samples,
                        maximumPartitions = MaximumPartitions, maximumSamplesPerPartition = MaximumSamplesPerPartition };
                }
            }

            internal sealed class Partition
            {
                private readonly ConcurrentDictionary<Int2, double> values = new();
                private readonly Queue<Int2> order = new();
                private readonly object publicationGate = new();
                private int retired;

                internal int Count => values.Count;
                internal bool TryGetValue(Int2 position, out double value) => values.TryGetValue(position, out value);
                internal void Retire() => Volatile.Write(ref retired, 1);

                internal void PublishCompletedSamples(List<KeyValuePair<Int2, double>> samples,
                    CancellationToken cancellationToken)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lock (publicationGate)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (Volatile.Read(ref retired) != 0) return;
                        foreach (KeyValuePair<Int2, double> sample in samples)
                        {
                            if (values.ContainsKey(sample.Key)) continue;
                            if (order.Count == MaximumSamplesPerPartition)
                                values.TryRemove(order.Dequeue(), out _);
                            if (values.TryAdd(sample.Key, sample.Value)) order.Enqueue(sample.Key);
                        }
                    }
                }
            }

            // 只使用 SampleHeight 的真实输入，临时输出尺寸和气候格网不参与身份。
            private readonly struct HeightSamplingIdentity : IEquatable<HeightSamplingIdentity>
            {
                private readonly long epoch;
                private readonly string dimension;
                private readonly int seed;
                private readonly bool wrapped;
                private readonly Int2 minimum;
                private readonly Int2 span;
                private readonly SurfaceClimateAlgorithm algorithm;
                private readonly double terrainScale, coordinateScale, noiseCoordinateScale, frequency;
                private readonly double lacunarity, persistence, offsetX, offsetY, boostStrength;
                private readonly int octaves;
                private readonly bool boostEnabled;

                internal HeightSamplingIdentity(ChunkGenerationRequest request, ChunkGenerationSettingsSnapshot settings)
                {
                    epoch = request.WorldEpoch;
                    dimension = request.Address.DimensionId;
                    seed = request.WorldSeed;
                    wrapped = request.Topology.IsWrapped;
                    minimum = request.Topology.Min;
                    span = request.Topology.Span;
                    algorithm = settings.SurfaceClimateAlgorithm;
                    TerrainNoiseChannelSettings noise = settings.HeightNoise;
                    bool legacy = algorithm == SurfaceClimateAlgorithm.LegacyLand;
                    terrainScale = legacy ? 0d : settings.TerrainScale;
                    coordinateScale = legacy ? settings.WorldCoordinateScale : 0d;
                    noiseCoordinateScale = legacy ? noise.CoordinateScale : 0d;
                    frequency = legacy ? noise.Frequency : 0d;
                    lacunarity = legacy ? noise.Lacunarity : 0d;
                    persistence = legacy ? noise.Persistence : 0d;
                    offsetX = legacy ? noise.OffsetX : 0d;
                    offsetY = legacy ? noise.OffsetY : 0d;
                    octaves = legacy ? noise.Octaves : settings.HeightOctaves;
                    boostEnabled = legacy && settings.HeightSecondaryBoostEnabled;
                    boostStrength = boostEnabled ? settings.HeightSecondaryBoostStrength : 0d;
                }

                public bool Equals(HeightSamplingIdentity other) => epoch == other.epoch &&
                    string.Equals(dimension, other.dimension, StringComparison.Ordinal) && seed == other.seed &&
                    wrapped == other.wrapped && minimum.Equals(other.minimum) && span.Equals(other.span) &&
                    algorithm == other.algorithm && terrainScale.Equals(other.terrainScale) &&
                    coordinateScale.Equals(other.coordinateScale) && noiseCoordinateScale.Equals(other.noiseCoordinateScale) &&
                    frequency.Equals(other.frequency) && lacunarity.Equals(other.lacunarity) &&
                    persistence.Equals(other.persistence) && offsetX.Equals(other.offsetX) && offsetY.Equals(other.offsetY) &&
                    boostStrength.Equals(other.boostStrength) && octaves == other.octaves && boostEnabled == other.boostEnabled;

                public override bool Equals(object obj) => obj is HeightSamplingIdentity other && Equals(other);

                public override int GetHashCode()
                {
                    unchecked
                    {
                        int hash = epoch.GetHashCode();
                        hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(dimension);
                        hash = hash * 397 ^ seed;
                        hash = hash * 397 ^ wrapped.GetHashCode();
                        hash = hash * 397 ^ minimum.GetHashCode();
                        hash = hash * 397 ^ span.GetHashCode();
                        hash = hash * 397 ^ (int)algorithm;
                        hash = hash * 397 ^ terrainScale.GetHashCode();
                        hash = hash * 397 ^ coordinateScale.GetHashCode();
                        hash = hash * 397 ^ noiseCoordinateScale.GetHashCode();
                        hash = hash * 397 ^ frequency.GetHashCode();
                        hash = hash * 397 ^ lacunarity.GetHashCode();
                        hash = hash * 397 ^ persistence.GetHashCode();
                        hash = hash * 397 ^ offsetX.GetHashCode();
                        hash = hash * 397 ^ offsetY.GetHashCode();
                        hash = hash * 397 ^ boostStrength.GetHashCode();
                        hash = hash * 397 ^ octaves;
                        return hash * 397 ^ boostEnabled.GetHashCode();
                    }
                }
            }
        }

        #endregion
    }
}
