using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 区块水源距离与两层降水

        private static readonly ConcurrentDictionary<HeightDrivenRegionKey, Lazy<double>> precipitationCache = new();
        private static readonly ConcurrentQueue<HeightDrivenRegionKey> precipitationCacheOrder = new();
        private static readonly ConcurrentDictionary<HeightDrivenRegionKey, Lazy<bool>> oceanChunkCache = new();
        private static readonly ConcurrentQueue<HeightDrivenRegionKey> oceanChunkCacheOrder = new();

        private readonly struct PrecipitationLattice
        {
            private readonly ChunkGenerationTopologySnapshot topology;
            public readonly double StepX, StepY;
            public readonly int AnchorX, AnchorY, CountX, CountY;

            public PrecipitationLattice(ChunkGenerationRequest request, ChunkGenerationSettingsSnapshot settings)
            {
                topology = request.Topology;
                CountX = topology.IsWrapped ? Math.Max(1, topology.Span.X / settings.PrecipitationChunkWidth) : 0;
                CountY = topology.IsWrapped ? Math.Max(1, topology.Span.Y / settings.PrecipitationChunkHeight) : 0;
                StepX = CountX > 0 ? topology.Span.X / (double)CountX : settings.PrecipitationChunkWidth;
                StepY = CountY > 0 ? topology.Span.Y / (double)CountY : settings.PrecipitationChunkHeight;
                AnchorX = topology.IsWrapped ? topology.Min.X : 0;
                AnchorY = topology.IsWrapped ? topology.Min.Y : 0;
            }

            public Int2 Position(int x, int y)
            {
                if (CountX > 0) x = ((x % CountX) + CountX) % CountX;
                if (CountY > 0) y = ((y % CountY) + CountY) % CountY;
                return new Int2((int)Math.Floor(AnchorX + (x + 0.5d) * StepX),
                    (int)Math.Floor(AnchorY + (y + 0.5d) * StepY));
            }

            public Int2 Grid(int x, int y) => new(
                (int)Math.Floor((topology.NormalizeX(x) - (double)AnchorX) / StepX),
                (int)Math.Floor((topology.NormalizeY(y) - (double)AnchorY) / StepY));

            public double Interpolate(int x, int y, Func<int, int, double> node)
            {
                double gx = (topology.NormalizeX(x) - AnchorX) / StepX - 0.5d;
                double gy = (topology.NormalizeY(y) - AnchorY) / StepY - 0.5d;
                int ix = (int)Math.Floor(gx), iy = (int)Math.Floor(gy);
                double tx = Smooth(gx - ix), ty = Smooth(gy - iy);
                return Lerp(Lerp(node(ix, iy), node(ix + 1, iy), tx),
                    Lerp(node(ix, iy + 1), node(ix + 1, iy + 1), tx), ty);
            }
        }

        // 第一层只读海洋和固定地理温度，河湖生成不能读第二层结果。
        private static double SampleOceanPrecipitation(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            var lattice = new PrecipitationLattice(request, settings);
            return lattice.Interpolate(worldX, worldY, (x, y) => OceanNode(request, settings, lattice, x, y));
        }

        private static double OceanNode(ChunkGenerationRequest request, ChunkGenerationSettingsSnapshot settings,
            PrecipitationLattice lattice, int x, int y)
        {
            Int2 center = lattice.Position(x, y);
            var key = PrecipitationKey(request, center);
            if (precipitationCache.TryGetValue(key, out Lazy<double> existing)) return existing.Value;
            var candidate = new Lazy<double>(() =>
            {
                double distance = FindOceanDistance(request, settings, lattice, x, y);
                double celsius = SampleGeographicClimate(request, settings, center.X, center.Y).TemperatureCelsius;
                return ResolveDistanceRainfall(settings, distance, settings.PrecipitationOceanRadius, celsius);
            }, LazyThreadSafetyMode.ExecutionAndPublication);
            Lazy<double> cached = precipitationCache.GetOrAdd(key, candidate);
            if (ReferenceEquals(candidate, cached))
            {
                precipitationCacheOrder.Enqueue(key);
                while (precipitationCache.Count > 8192 && precipitationCacheOrder.TryDequeue(out var oldest))
                    precipitationCache.TryRemove(oldest, out _);
            }
            return cached.Value;
        }

        private static HeightDrivenRegionKey PrecipitationKey(ChunkGenerationRequest request, Int2 center) =>
            new(request.WorldEpoch, request.Address.DimensionId, request.WorldSeed,
                request.Profile.GenerationFingerprint, request.Topology, center, 0, 0);

        private static double ResolveDistanceRainfall(ChunkGenerationSettingsSnapshot settings,
            double distance, double radius, double celsius)
        {
            double proximity = 1d - Smooth(Clamp01(distance / radius));
            double warmth = Smooth(InverseLerp(settings.PrecipitationColdCelsius,
                settings.PrecipitationWarmCelsius, celsius));
            double temperatureFactor = Lerp(settings.PrecipitationColdMultiplier, 1d, warmth);
            return Lerp(settings.PrecipitationMinimum, settings.PrecipitationMaximum,
                proximity * temperatureFactor);
        }

        private static double FindOceanDistance(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, PrecipitationLattice lattice, int x, int y)
        {
            Int2 target = lattice.Position(x, y);
            double best = settings.PrecipitationOceanRadius * settings.PrecipitationOceanRadius;
            int rings = (int)Math.Ceiling(settings.PrecipitationOceanRadius / Math.Min(lattice.StepX, lattice.StepY) + 1d);
            var visited = new HashSet<Int2>();
            for (int ring = 0; ring <= rings; ring++)
            {
                for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (ring > 0 && Math.Abs(dx) != ring && Math.Abs(dy) != ring) continue;
                    Int2 source = lattice.Position(x + dx, y + dy);
                    if (!visited.Add(source)) continue;
                    int2 delta = request.Topology.ToDomain().ShortestDelta(
                        new int2(target.X, target.Y), new int2(source.X, source.Y));
                    // 有水的区块整个作为水源，距离从区块边缘量起，海岸小条水也不会被漏掉。
                    double sx = Math.Max(0d, Math.Abs(delta.x) - lattice.StepX * 0.5d);
                    double sy = Math.Max(0d, Math.Abs(delta.y) - lattice.StepY * 0.5d);
                    double squared = sx * sx + sy * sy;
                    if (squared >= best || !HasOcean(request, settings, lattice, source)) continue;
                    best = squared;
                }
                double nextMinimum = (ring + 0.5d) * Math.Min(lattice.StepX, lattice.StepY);
                if (best <= nextMinimum * nextMinimum) break;
                if (lattice.CountX > 0 && visited.Count >= lattice.CountX * lattice.CountY) break;
            }
            return Math.Sqrt(best);
        }

        private static bool HasOcean(ChunkGenerationRequest request, ChunkGenerationSettingsSnapshot settings,
            PrecipitationLattice lattice, Int2 center)
        {
            var key = PrecipitationKey(request, center);
            if (oceanChunkCache.TryGetValue(key, out Lazy<bool> existing)) return existing.Value;
            var candidate = new Lazy<bool>(() =>
            {
                if (SampleHeight(request, settings, center.X, center.Y) < settings.SeaLevel) return true;
                int minX = (int)Math.Ceiling(center.X - lattice.StepX * 0.5d);
                int minY = (int)Math.Ceiling(center.Y - lattice.StepY * 0.5d);
                int maxX = (int)Math.Ceiling(center.X + lattice.StepX * 0.5d);
                int maxY = (int)Math.Ceiling(center.Y + lattice.StepY * 0.5d);
                for (int y = minY; y < maxY; y++)
                for (int x = minX; x < maxX; x++)
                    if (SampleHeight(request, settings, request.Topology.NormalizeX(x),
                        request.Topology.NormalizeY(y)) < settings.SeaLevel) return true;
                return false;
            }, LazyThreadSafetyMode.ExecutionAndPublication);
            Lazy<bool> cached = oceanChunkCache.GetOrAdd(key, candidate);
            if (ReferenceEquals(candidate, cached))
            {
                oceanChunkCacheOrder.Enqueue(key);
                while (oceanChunkCache.Count > 32768 && oceanChunkCacheOrder.TryDequeue(out var oldest))
                    oceanChunkCache.TryRemove(oldest, out _);
            }
            return cached.Value;
        }

        private sealed class SurfacePrecipitationSampler
        {
            private readonly ChunkGenerationRequest request;
            private readonly ChunkGenerationSettingsSnapshot settings;
            private readonly PrecipitationLattice lattice;
            private readonly HashSet<Int2> freshwaterChunks = new();
            private readonly Dictionary<Int2, double> nodes = new();

            public SurfacePrecipitationSampler(ChunkGenerationRequest request,
                ChunkGenerationSettingsSnapshot settings, GeneratedHydrologyMap hydrology)
            {
                this.request = request;
                this.settings = settings;
                lattice = new PrecipitationLattice(request, settings);
                if (hydrology == null) return;
                foreach (var pair in hydrology.WaterCells)
                    if (pair.Value.Depth > 0d && pair.Value.Kind != GeneratedHydrologyKind.None)
                    {
                        Int2 grid = lattice.Grid(pair.Key.X, pair.Key.Y);
                        freshwaterChunks.Add(lattice.Position(grid.X, grid.Y));
                    }
            }

            public double Sample(int x, int y) => lattice.Interpolate(x, y, Node);

            private double Node(int x, int y)
            {
                Int2 center = lattice.Position(x, y);
                if (nodes.TryGetValue(center, out double cached)) return cached;
                double ocean = OceanNode(request, settings, lattice, x, y);
                double distanceSquared = settings.PrecipitationFreshwaterRadius * settings.PrecipitationFreshwaterRadius;
                foreach (Int2 source in freshwaterChunks)
                {
                    int2 delta = request.Topology.ToDomain().ShortestDelta(
                        new int2(center.X, center.Y), new int2(source.X, source.Y));
                    double dx = Math.Max(0d, Math.Abs(delta.x) - lattice.StepX * 0.5d);
                    double dy = Math.Max(0d, Math.Abs(delta.y) - lattice.StepY * 0.5d);
                    distanceSquared = Math.Min(distanceSquared, dx * dx + dy * dy);
                    if (distanceSquared <= 0d) break;
                }
                double celsius = SampleGeographicClimate(request, settings, center.X, center.Y).TemperatureCelsius;
                double freshwater = ResolveDistanceRainfall(settings, Math.Sqrt(distanceSquared),
                    settings.PrecipitationFreshwaterRadius, celsius);
                // 第二层只补河湖距离的影响，不重建水文，也不把多个水源无限叠加。
                double final = Math.Max(ocean, freshwater);
                nodes.Add(center, final);
                return final;
            }
        }

        #endregion

        #region 独立地理气候

        // 温度先独立合成，降水只消费地理温度，不反向改变地理温度。
        private static SurfaceClimateSample SampleGeographicClimate(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int worldX, int worldY)
        {
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
            SurfaceClimateSample sample;
            if (settings.SurfaceClimateAlgorithm == SurfaceClimateAlgorithm.LegacyLand)
            {
                LegacyClimateSample raw = LegacyTerrainClimateKernel.SampleClimate(request, settings, worldX, worldY);
                sample = new SurfaceClimateSample
                {
                    Height = raw.Height, Temperature = raw.Temperature, TemperatureCelsius = raw.TemperatureCelsius,
                    WindX = raw.WindX, WindY = raw.WindY
                };
            }
            else
            {
                double noise = Fractal(CreateSeed(request, 0x85ebca6bu), worldX, worldY,
                    settings.ClimateScale, settings.ClimateOctaves, 2.07d, 0.5d, request.Topology);
                double latitude = request.Topology.IsWrapped ? 0d : Math.Min(0.34d, Math.Abs(worldY) * 0.000025d);
                double temperature = Clamp01(noise - latitude);
                LegacyTerrainClimateKernel.SampleWind(request, settings, worldX, worldY, out float wx, out float wy);
                sample = new SurfaceClimateSample
                {
                    Height = SampleHeight(request, settings, worldX, worldY), Temperature = temperature,
                    TemperatureCelsius = -20d + temperature * 65d, WindX = wx, WindY = wy
                };
            }
            return FinishGeographicTemperature(request, settings, worldX, worldY, sample);
        }

        #endregion
    }
}
