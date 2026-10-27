using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        private const double LavaBasinMaxReachFactor = 2.2d;

        #region 山顶小湖候选

        // 只缓存不可变盆地，不保存区块引用；多个区块、出生查询和邻区生态共享同一结果。
        private readonly ConcurrentDictionary<(long, string, int, ulong, ChunkGenerationTopologySnapshot, Int2),
            Lazy<LavaBasin>> lavaBasins = new();
        private readonly ConcurrentQueue<(long, string, int, ulong, ChunkGenerationTopologySnapshot, Int2)>
            lavaBasinOrder = new();

        private delegate bool PrepareRegionalBasinCandidate(in ChunkGenerationRequest request, Int2 region,
            in WorldTopologyDomain domain, double2 anchor, double2 span, double2 query, out double radius);
        private delegate LavaBasin FindRegionalBasinCandidate(ChunkGenerationRequest request, Int2 region,
            double2 origin, double2 span, double radius);
        private static readonly PrepareRegionalBasinCandidate PrepareLavaBasin = TryPrepareLavaBasinCandidate;
        private static readonly FindRegionalBasinCandidate FindLavaBasin = FindLavaPeak;
        private static readonly FindRegionalBasinCandidate FindOilBasin = FindCrudeOilLandBasin;

        private IReadOnlyList<LavaBasin> ResolveLavaBasins(ChunkGenerationRequest request,
            CancellationToken cancellation = default)
        {
            var settings = request.Profile.Settings;
            if (!settings.LavaLakeEnabled || settings.LavaLakeChance <= 0d)
                return Array.Empty<LavaBasin>();
            return ResolveRegionalBasins(request, cancellation, settings.LavaLakeRegionSize, settings.LavaLakeChance,
                0x1a7ab451u, lavaBasins, lavaBasinOrder, PrepareLavaBasin, FindLavaBasin);
        }

        // 两种盆地共用区域顺序和缓存，候选资格与选址仍由各自规则决定。
        private static IReadOnlyList<LavaBasin> ResolveRegionalBasins(ChunkGenerationRequest request,
            CancellationToken cancellation, int regionSize, double chance, uint chanceSalt,
            ConcurrentDictionary<(long, string, int, ulong, ChunkGenerationTopologySnapshot, Int2), Lazy<LavaBasin>> basins,
            ConcurrentQueue<(long, string, int, ulong, ChunkGenerationTopologySnapshot, Int2)> order,
            PrepareRegionalBasinCandidate prepare, FindRegionalBasinCandidate find)
        {
            var result = new List<LavaBasin>(4);
            var domain = request.Topology.ToDomain();
            bool wrapped = request.Topology.IsWrapped;
            int countX = wrapped ? Math.Max(1, request.Topology.Span.X / regionSize) : 0;
            int countY = wrapped ? Math.Max(1, request.Topology.Span.Y / regionSize) : 0;
            double stepX = wrapped ? request.Topology.Span.X / (double)countX : regionSize;
            double stepY = wrapped ? request.Topology.Span.Y / (double)countY : regionSize;
            double2 span = new double2(stepX, stepY);
            double2 anchor = wrapped ? new double2(request.Topology.Min.X, request.Topology.Min.Y) : default;
            double2 query = domain.Normalize(new double2(request.Address.ChunkOrigin.X + request.Profile.Width * 0.5d,
                request.Address.ChunkOrigin.Y + request.Profile.Height * 0.5d));
            int baseX = (int)Math.Floor((query.x - anchor.x) / stepX);
            int baseY = (int)Math.Floor((query.y - anchor.y) / stepY);
            int rangeX = 1 + (int)Math.Ceiling(request.Profile.Width * 0.5d / stepX);
            int rangeY = 1 + (int)Math.Ceiling(request.Profile.Height * 0.5d / stepY);
            var visited = new HashSet<Int2>();
            for (int dy = -rangeY; dy <= rangeY; dy++)
            for (int dx = -rangeX; dx <= rangeX; dx++)
            {
                cancellation.ThrowIfCancellationRequested();
                int rx = wrapped ? ((baseX + dx) % countX + countX) % countX : baseX + dx;
                int ry = wrapped ? ((baseY + dy) % countY + countY) % countY : baseY + dy;
                var region = new Int2(rx, ry);
                if (!visited.Add(region) || Hash01(request.WorldSeed, rx, ry, chanceSalt) >= chance)
                    continue;
                double radius = 0d;
                if (prepare != null && !prepare(in request, region, in domain, anchor, span, query, out radius))
                    continue;
                var key = (request.WorldEpoch, request.Address.DimensionId, request.WorldSeed,
                    request.Profile.GenerationFingerprint, request.Topology, region);
                if (!basins.TryGetValue(key, out var candidate))
                {
                    var created = new Lazy<LavaBasin>(() => find(request, region,
                        anchor + new double2(rx * stepX, ry * stepY), span, radius),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                    candidate = basins.GetOrAdd(key, created);
                    if (ReferenceEquals(candidate, created)) order.Enqueue(key);
                    while (basins.Count > 256 && order.TryDequeue(out var oldest))
                        basins.TryRemove(oldest, out _);
                }
                // 单个候选最多进行固定数量采样，不绑定任意一个区块的取消令牌。
                LavaBasin basin = candidate.Value;
                if (basin != null) result.Add(basin);
            }
            return result;
        }

        private static bool TryPrepareLavaBasinCandidate(in ChunkGenerationRequest request, Int2 region,
            in WorldTopologyDomain domain, double2 anchor, double2 span, double2 query, out double radius)
        {
            var settings = request.Profile.Settings;
            double regionalRareMaxRadius = Math.Min(settings.LavaLakeRareMaxRadius, Math.Min(span.x, span.y) * 0.29d);
            radius = ResolveLavaRadius(request.WorldSeed, region, settings, regionalRareMaxRadius);
            double2 regionCenter = anchor + new double2((region.X + 0.5d) * span.x, (region.Y + 0.5d) * span.y);
            double2 delta = domain.ShortestDelta(query, regionCenter);
            double reach = radius * LavaBasinMaxReachFactor + settings.LavaLakeShoreWidth;
            return !(Math.Abs(delta.x) > (span.x + request.Profile.Width) * 0.5d + reach ||
                     Math.Abs(delta.y) > (span.y + request.Profile.Height) * 0.5d + reach);
        }

        private static LavaBasin FindLavaPeak(ChunkGenerationRequest request, Int2 region,
            double2 origin, double2 span, double radius)
        {
            var settings = request.Profile.Settings;
            double margin = radius * 1.3d + settings.LavaLakeShoreWidth + 2d;
            if (span.x <= margin * 2d || span.y <= margin * 2d) return null;
            double2 low = origin + margin, high = origin + span - margin;
            double2 peak = default;
            double peakHeight = -1d;
            // 先在区域内找最高的石地，再沿局部高程收敛；不在每格重新搜索山峰。
            for (int y = 0; y < 9; y++)
            for (int x = 0; x < 9; x++)
            {
                double2 position = math.floor(low + (high - low) * new double2(x / 8d, y / 8d)) + 0.5d;
                if (TrySampleLavaMountain(request, position, out double height) && height > peakHeight)
                { peak = position; peakHeight = height; }
            }
            if (peakHeight < Math.Max(settings.MountainLevel, settings.LavaLakeMinimumHeight)) return null;
            for (int stride = 8; stride >= 1; stride /= 2)
            for (int iteration = 0; iteration < 16; iteration++)
            {
                double2 best = peak;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    double2 position = peak + new double2(x * stride, y * stride);
                    if (position.x < low.x || position.x > high.x || position.y < low.y || position.y > high.y) continue;
                    if (TrySampleLavaMountain(request, position, out double height) && height > peakHeight + 0.0000001d)
                    { best = position; peakHeight = height; }
                }
                if (math.all(best == peak)) break;
                peak = best;
            }
            int validRingSamples = 0;
            for (int i = 0; i < 16; i++)
            {
                double angle = i * Math.PI / 8d;
                if (TrySampleLavaMountain(request, peak + new double2(Math.Cos(angle), Math.Sin(angle)) *
                    (radius * 1.3d + settings.LavaLakeShoreWidth), out _))
                    validRingSamples++;
            }
            int requiredRingSamples = radius <= settings.LavaLakeMaxRadius + 0.0001d ? 16 : 12;
            if (validRingSamples < requiredRingSamples) return null;
            return new LavaBasin(request.Topology.ToDomain().Normalize(peak), radius,
                settings.LavaLakeShoreWidth, Hash01(request.WorldSeed, region.X, region.Y, 0x1a7ab453u) * Math.PI * 2d);
        }

        /// <summary>岩浆湖尺寸从冻结概率表抽取，地理边界只限制允许的半径档位。</summary>
        private static double ResolveLavaRadius(int worldSeed, Int2 region,
            ChunkGenerationSettingsSnapshot settings, double regionalRareMaxRadius)
        {
            double minimum = settings.LavaLakeMinRadius;
            double commonMaximum = Math.Max(minimum, settings.LavaLakeMaxRadius);
            double rareMaximum = Math.Max(commonMaximum, regionalRareMaxRadius);
            if (rareMaximum <= minimum + 0.0001d)
                return minimum;

            double scale = settings.WorldSpatialDistanceScale;
            return DiscreteProbability.SampleWithin(settings.ProbabilityTables.Require("generation.lava.radius"),
                Hash01(worldSeed, region.X, region.Y, 0x1a7ab452u), minimum / scale, rareMaximum / scale) * scale;
        }

        private static bool TrySampleLavaMountain(ChunkGenerationRequest request, double2 position, out double height)
        {
            SurfaceClimateSample climate = SampleSurfaceClimate(request, request.Profile.Settings,
                (int)Math.Floor(position.x), (int)Math.Floor(position.y));
            height = climate.Height;
            return ResolveSurfaceBiome(request.Profile.Settings, climate,
                Clamp01(climate.Precipitation * 0.78d + (1d - climate.Height) * 0.22d), false)
                == SurfaceBiomeKind.Stone;
        }

        #endregion

        #region 液面和伴生岸带

        private sealed class LavaBasin
        {
            private readonly double2 center;
            private readonly double radius, shoreWidth, phase, minimumSatelliteRadius;
            public LavaBasin(double2 center, double radius, double shoreWidth, double phase, double shapeScale = 1d)
            { this.center = center; this.radius = radius; this.shoreWidth = shoreWidth; this.phase = phase;
                minimumSatelliteRadius = 0.65d * shapeScale; }

            public bool TrySample(ChunkGenerationTopologySnapshot topology, int worldX, int worldY,
                out float depth, out float shore)
            {
                depth = 0f; shore = 0f;
                double2 delta = topology.ToDomain().ShortestDelta(center, new double2(worldX + 0.5d, worldY + 0.5d));
                if (Math.Abs(delta.x) > radius * LavaBasinMaxReachFactor + shoreWidth ||
                    Math.Abs(delta.y) > radius * LavaBasinMaxReachFactor + shoreWidth) return false;
                double angle = Math.Atan2(delta.y, delta.x);
                double edge = radius * (1d + 0.12d * Math.Sin(angle * 3d + phase));
                double distance = math.length(delta) - edge;
                bool sampled = false;
                if (distance <= shoreWidth)
                {
                    sampled = true;
                    if (distance >= 0d) shore = 1f;
                    else
                    {
                        depth = (float)(0.25d + 0.65d * Math.Sqrt(Math.Min(1d, -distance / radius)));
                        return true;
                    }
                }

                // 主池外追加确定性的浅小熔岩斑，让火山口边缘形成自然散落的卫星小池。
                int satelliteCount = 4 + Math.Min(3, (int)Math.Floor(Wave01(phase * 1.137d + 0.371d) * 4d));
                for (int i = 0; i < satelliteCount; i++)
                {
                    double radial = radius * (1.32d + 0.48d * Wave01(phase * 2.173d + i * 1.731d));
                    double satelliteAngle = phase + i * 2.399963229728653d +
                                            (Wave01(phase * 0.917d + i * 2.417d) - 0.5d) * 0.55d;
                    double satelliteRadius = Math.Max(minimumSatelliteRadius,
                        radius * (0.14d + 0.20d * Wave01(phase * 1.619d + i * 3.113d)));
                    double2 satelliteCenter = center + new double2(Math.Cos(satelliteAngle), Math.Sin(satelliteAngle)) * radial;
                    double2 satelliteDelta = topology.ToDomain().ShortestDelta(satelliteCenter,
                        new double2(worldX + 0.5d, worldY + 0.5d));
                    double localAngle = Math.Atan2(satelliteDelta.y, satelliteDelta.x);
                    double satelliteEdge = satelliteRadius *
                        (1d + 0.18d * Math.Sin(localAngle * 2d + phase + i * 0.73d));
                    double satelliteDistance = math.length(satelliteDelta) - satelliteEdge;
                    double satelliteShore = Math.Min(shoreWidth, Math.Max(0.75d, satelliteRadius * 0.8d));
                    if (satelliteDistance > satelliteShore) continue;

                    sampled = true;
                    if (satelliteDistance >= 0d)
                    {
                        shore = 1f;
                        continue;
                    }

                    depth = (float)(0.18d + 0.47d * Math.Sqrt(Math.Min(1d, -satelliteDistance / satelliteRadius)));
                    shore = 0f;
                    return true;
                }
                return sampled;
            }

            private static double Wave01(double value) => 0.5d + 0.5d * Math.Sin(value * 12.9898d + 78.233d);
        }

        #endregion
    }
}
