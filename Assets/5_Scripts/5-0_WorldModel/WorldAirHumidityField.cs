using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    /// <summary>只从已就绪的权威液体重建动态空气湿度，不保存第二份水量。</summary>
    public sealed class WorldAirHumidityField : IDisposable
    {
        #region 有界查询缓存

        private const int MaximumTargetBuckets = 64;
        private const int MaximumSourceBuckets = 1024;

        private readonly struct WaterSource
        {
            public WaterSource(Int2 cell, float strength) { Cell = cell; Strength = strength; }
            public Int2 Cell { get; }
            public float Strength { get; }
        }

        private sealed class SourceBucket
        {
            public ChunkTerrainData Terrain;
            public long Revision = -1;
            public long Version;
            public readonly List<WaterSource> Sources = new();
        }

        private sealed class Dependency
        {
            public Int2 Origin;
            public SourceBucket Source;
            public long Version = -1;
        }

        private sealed class TargetBucket
        {
            public float[] Values;
            public byte[] Status;
            public readonly List<Dependency> Dependencies = new();
        }

        private readonly WorldRuntime world;
        private readonly string dimensionId;
        private readonly int width;
        private readonly int height;
        private readonly WorldTopologyDomain domain;
        private readonly AirHumiditySettings settings;
        private readonly Func<string, bool> suppliesHumidity;
        private readonly Func<WorldAddress, ChunkTerrainData> resolveTerrain;
        private readonly Dictionary<Int2, SourceBucket> sourceBuckets = new();
        private readonly Dictionary<Int2, TargetBucket> targetBuckets = new();
        private readonly Dictionary<string, bool> liquidHumidity = new(StringComparer.OrdinalIgnoreCase);
        private long epoch;
        private bool disposed;

        public WorldAirHumidityField(WorldRuntime world, string dimensionId, int width, int height,
            ChunkGenerationTopologySnapshot topology, AirHumiditySettings settings,
            Func<string, bool> suppliesHumidity,
            Func<WorldAddress, ChunkTerrainData> resolveTerrain = null)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            if (string.IsNullOrWhiteSpace(dimensionId))
                throw new ArgumentException("Dimension id is required.", nameof(dimensionId));
            if (width <= 0 || height <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            this.dimensionId = dimensionId;
            this.width = width;
            this.height = height;
            this.settings = settings;
            domain = topology.ToDomain();
            this.suppliesHumidity = suppliesHumidity ?? throw new ArgumentNullException(nameof(suppliesHumidity));
            this.resolveTerrain = resolveTerrain ?? ResolveWorldTerrain;
            epoch = world.Epoch;
        }

        #endregion

        #region 动态查询与失效

        /// <summary>影响范围内缺少已恢复邻区时返回 false，不把未知水体猜成干地。</summary>
        public bool TrySample(Int2 worldCell, out float humidity)
        {
            humidity = 0f;
            if (disposed)
                return false;
            if (epoch != world.Epoch)
            {
                ClearCache();
                epoch = world.Epoch;
            }
            Int2 cell = Normalize(worldCell);
            Int2 origin = ResolveChunkOrigin(cell);
            ChunkTerrainData terrain = ResolveTerrain(origin);
            if (terrain == null)
                return false;
            int x = cell.X - origin.X, y = cell.Y - origin.Y;
            if ((uint)x >= (uint)terrain.Width || (uint)y >= (uint)terrain.Height)
                return false;
            float background = terrain.TryGetEnvironmentValue(AirHumidityKernel.BackgroundLayer, x, y,
                out float storedBackground) ? storedBackground : settings.Background;
            if (settings.WaterContribution <= 0f)
            {
                humidity = AirHumidityKernel.Combine(background, 0f);
                return true;
            }

            TargetBucket bucket = GetTargetBucket(origin);
            RefreshDependencies(bucket);
            int index = y * width + x;
            if (bucket.Status[index] != 0)
            {
                humidity = bucket.Status[index] == 1 ? bucket.Values[index] : 0f;
                return bucket.Status[index] == 1;
            }

            double radiusSquared = (double)settings.RadiusTiles * settings.RadiusTiles;
            float maximumInfluence = 0f;
            bool complete = true;
            foreach (Dependency dependency in bucket.Dependencies)
            {
                if (DistanceToChunkSquared(cell, dependency.Origin) >= radiusSquared)
                    continue;
                if (dependency.Source.Terrain == null)
                {
                    complete = false;
                    continue;
                }
                foreach (WaterSource source in dependency.Source.Sources)
                {
                    if (source.Strength <= maximumInfluence)
                        continue;
                    double2 delta = domain.ShortestDelta(new double2(cell.X, cell.Y),
                        new double2(source.Cell.X, source.Cell.Y));
                    float influence = AirHumidityKernel.GetInfluence(source.Strength,
                        delta.x * delta.x + delta.y * delta.y, settings);
                    maximumInfluence = Math.Max(maximumInfluence, influence);
                }
            }

            bucket.Status[index] = complete ? (byte)1 : (byte)2;
            if (!complete)
                return false;
            humidity = AirHumidityKernel.Combine(background, maximumInfluence);
            bucket.Values[index] = humidity;
            return true;
        }

        public void ClearCache()
        {
            targetBuckets.Clear();
            sourceBuckets.Clear();
            liquidHumidity.Clear();
        }

        public void Dispose()
        {
            ClearCache();
            disposed = true;
        }

        #endregion

        #region 来源快照与空间寻址

        private TargetBucket GetTargetBucket(Int2 origin)
        {
            if (targetBuckets.TryGetValue(origin, out TargetBucket cached))
                return cached;
            if (targetBuckets.Count >= MaximumTargetBuckets || sourceBuckets.Count >= MaximumSourceBuckets)
                ClearCache();
            var bucket = new TargetBucket { Values = new float[width * height], Status = new byte[width * height] };
            int padding = (int)Math.Ceiling(settings.RadiusTiles);
            // 先列出未环绕的邻域再规范化，避免接缝把遍历范围倒置。
            Int2 min = new(FloorOrigin(origin.X - padding, width), FloorOrigin(origin.Y - padding, height));
            Int2 max = new(FloorOrigin(origin.X + width - 1 + padding, width),
                FloorOrigin(origin.Y + height - 1 + padding, height));
            var visited = new HashSet<Int2>();
            for (int sourceY = min.Y; sourceY <= max.Y; sourceY += height)
            for (int sourceX = min.X; sourceX <= max.X; sourceX += width)
            {
                Int2 sourceOrigin = Normalize(new Int2(sourceX, sourceY));
                if (!visited.Add(sourceOrigin))
                    continue;
                if (!sourceBuckets.TryGetValue(sourceOrigin, out SourceBucket source))
                {
                    source = new SourceBucket();
                    sourceBuckets.Add(sourceOrigin, source);
                }
                bucket.Dependencies.Add(new Dependency { Origin = sourceOrigin, Source = source });
            }
            targetBuckets.Add(origin, bucket);
            return bucket;
        }

        private void RefreshDependencies(TargetBucket bucket)
        {
            bool changed = false;
            foreach (Dependency dependency in bucket.Dependencies)
            {
                SourceBucket source = dependency.Source;
                ChunkTerrainData terrain = ResolveTerrain(dependency.Origin);
                long revision = terrain?.Revision ?? -1;
                if (!ReferenceEquals(source.Terrain, terrain) || source.Revision != revision)
                {
                    source.Terrain = terrain;
                    source.Revision = revision;
                    source.Version++;
                    RebuildSources(dependency.Origin, source);
                }
                if (dependency.Version != source.Version)
                {
                    dependency.Version = source.Version;
                    changed = true;
                }
            }
            if (changed)
                Array.Clear(bucket.Status, 0, bucket.Status.Length);
        }

        private void RebuildSources(Int2 origin, SourceBucket source)
        {
            source.Sources.Clear();
            ChunkTerrainData terrain = source.Terrain;
            if (terrain == null)
                return;
            ReadOnlySpan<float> depths = terrain.LiquidDepth;
            ReadOnlySpan<int> types = terrain.LiquidTypeIndex;
            for (int i = 0; i < terrain.CellCount; i++)
            {
                if (depths[i] <= 0f)
                    continue;
                string id = terrain.LiquidTypes.GetId(types[i]);
                if (!liquidHumidity.TryGetValue(id, out bool supplies))
                {
                    supplies = suppliesHumidity(id);
                    liquidHumidity.Add(id, supplies);
                }
                float strength = AirHumidityKernel.GetWaterSourceStrength(supplies, depths[i], settings);
                if (strength > 0f)
                    source.Sources.Add(new WaterSource(new Int2(origin.X + i % terrain.Width,
                        origin.Y + i / terrain.Width), strength));
            }
        }

        private double DistanceToChunkSquared(Int2 cell, Int2 origin)
        {
            double2 center = new(origin.X + (width - 1) * 0.5d, origin.Y + (height - 1) * 0.5d);
            double2 delta = domain.ShortestDelta(center, new double2(cell.X, cell.Y));
            double dx = Math.Max(0d, Math.Abs(delta.x) - (width - 1) * 0.5d);
            double dy = Math.Max(0d, Math.Abs(delta.y) - (height - 1) * 0.5d);
            return dx * dx + dy * dy;
        }

        private ChunkTerrainData ResolveWorldTerrain(WorldAddress address)
            => world.TryGetChunkTerrain(address, out ChunkTerrainData terrain) ? terrain : null;

        private ChunkTerrainData ResolveTerrain(Int2 origin)
        {
            ChunkTerrainData terrain = resolveTerrain(new WorldAddress(dimensionId, origin));
            return terrain != null && !terrain.IsDisposed && terrain.Width == width && terrain.Height == height
                ? terrain : null;
        }

        private Int2 Normalize(Int2 cell) => new(domain.NormalizeX(cell.X), domain.NormalizeY(cell.Y));
        private Int2 ResolveChunkOrigin(Int2 cell)
            => Normalize(new Int2(FloorOrigin(cell.X, width), FloorOrigin(cell.Y, height)));
        private static int FloorOrigin(int cell, int size)
            => (int)(Math.Floor((double)cell / size) * size);

        #endregion
    }
}
