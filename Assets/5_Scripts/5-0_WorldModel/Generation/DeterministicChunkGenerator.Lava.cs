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
        private const double LavaRadiusTailWeight = 0.003d;
        private const double LavaRadiusBucketSize = 1d;

        #region 山顶小湖候选

        // 只缓存不可变盆地，不保存区块引用；多个区块、出生查询和邻区生态共享同一结果。
        private readonly ConcurrentDictionary<(long, string, int, ulong, ChunkGenerationTopologySnapshot, Int2),
            Lazy<LavaBasin>> lavaBasins = new();
        private readonly ConcurrentQueue<(long, string, int, ulong, ChunkGenerationTopologySnapshot, Int2)>
            lavaBasinOrder = new();

        private IReadOnlyList<LavaBasin> ResolveLavaBasins(ChunkGenerationRequest request,
            CancellationToken cancellation = default)
        {
            var settings = request.Profile.Settings;
            if (!settings.LavaLakeEnabled || settings.LavaLakeChance <= 0d)
                return Array.Empty<LavaBasin>();
            var result = new List<LavaBasin>(4);
            var domain = request.Topology.ToDomain();
            bool wrapped = request.Topology.IsWrapped;
            int countX = wrapped ? Math.Max(1, request.Topology.Span.X / settings.LavaLakeRegionSize) : 0;
            int countY = wrapped ? Math.Max(1, request.Topology.Span.Y / settings.LavaLakeRegionSize) : 0;
            double stepX = wrapped ? request.Topology.Span.X / (double)countX : settings.LavaLakeRegionSize;
            double stepY = wrapped ? request.Topology.Span.Y / (double)countY : settings.LavaLakeRegionSize;
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
                if (!visited.Add(region) || Hash01(request.WorldSeed, rx, ry, 0x1a7ab451u) >= settings.LavaLakeChance)
                    continue;
                double regionalRareMaxRadius = Math.Min(settings.LavaLakeRareMaxRadius,
                    Math.Min(stepX, stepY) * 0.29d);
                double radius = ResolveLavaRadius(request.WorldSeed, region, settings, regionalRareMaxRadius);
                double2 regionCenter = anchor + new double2((rx + 0.5d) * stepX, (ry + 0.5d) * stepY);
                double2 delta = domain.ShortestDelta(query, regionCenter);
                double reach = radius * LavaBasinMaxReachFactor + settings.LavaLakeShoreWidth;
                if (Math.Abs(delta.x) > (stepX + request.Profile.Width) * 0.5d + reach ||
                    Math.Abs(delta.y) > (stepY + request.Profile.Height) * 0.5d + reach) continue;
                var key = (request.WorldEpoch, request.Address.DimensionId, request.WorldSeed,
                    request.Profile.GenerationFingerprint, request.Topology, region);
                if (!lavaBasins.TryGetValue(key, out var candidate))
                {
                    var created = new Lazy<LavaBasin>(() => FindLavaPeak(request, region,
                        anchor + new double2(rx * stepX, ry * stepY), new double2(stepX, stepY), radius),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                    candidate = lavaBasins.GetOrAdd(key, created);
                    if (ReferenceEquals(candidate, created)) lavaBasinOrder.Enqueue(key);
                    while (lavaBasins.Count > 256 && lavaBasinOrder.TryDequeue(out var oldest))
                        lavaBasins.TryRemove(oldest, out _);
                }
                // 单个候选最多进行固定数量采样，不绑定任意一个区块的取消令牌。
                LavaBasin basin = candidate.Value;
                if (basin != null) result.Add(basin);
            }
            return result;
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

        /// <summary>普通半径占绝大多数，超过常规上限后只保留二次曲线的极低概率长尾。</summary>
        private static double ResolveLavaRadius(int worldSeed, Int2 region,
            ChunkGenerationSettingsSnapshot settings, double regionalRareMaxRadius)
        {
            double minimum = settings.LavaLakeMinRadius;
            double commonMaximum = Math.Max(minimum, settings.LavaLakeMaxRadius);
            double rareMaximum = Math.Max(commonMaximum, regionalRareMaxRadius);
            if (rareMaximum <= minimum + 0.0001d)
                return minimum;

            double peak = (minimum + commonMaximum) * 0.5d;
            double quadraticRadius = Math.Max(LavaRadiusBucketSize,
                (commonMaximum - minimum) * 0.75d);
            int bucketCount = Math.Max(1,
                (int)Math.Ceiling((rareMaximum - minimum) / LavaRadiusBucketSize) + 1);

            double totalWeight = 0d;
            for (int i = 0; i < bucketCount; i++)
            {
                double candidate = Math.Min(rareMaximum, minimum + i * LavaRadiusBucketSize);
                totalWeight += ResolveLavaRadiusWeight(candidate, peak, quadraticRadius);
            }

            double target = Hash01(worldSeed, region.X, region.Y, 0x1a7ab452u) * totalWeight;
            double accumulated = 0d;
            for (int i = 0; i < bucketCount; i++)
            {
                double candidate = Math.Min(rareMaximum, minimum + i * LavaRadiusBucketSize);
                accumulated += ResolveLavaRadiusWeight(candidate, peak, quadraticRadius);
                if (target <= accumulated)
                    return candidate;
            }

            return rareMaximum;
        }

        private static double ResolveLavaRadiusWeight(double radius, double peak, double quadraticRadius)
        {
            double normalized = (radius - peak) / quadraticRadius;
            return Math.Max(LavaRadiusTailWeight, 1d - normalized * normalized);
        }

        private static bool TrySampleLavaMountain(ChunkGenerationRequest request, double2 position, out double height)
        {
            SurfaceClimateSample climate = SampleSurfaceClimate(request, request.Profile.Settings,
                (int)Math.Floor(position.x), (int)Math.Floor(position.y));
            height = climate.Height;
            return SurfaceBiomeClassifier.Resolve(request.Profile.Settings, climate.Height, climate.Temperature,
                climate.Precipitation, Clamp01(climate.Precipitation * 0.78d + (1d - climate.Height) * 0.22d),
                false, climate.SnowAllowed)
                == SurfaceBiomeKind.Stone;
        }

        #endregion

        #region 液面和伴生岸带

        private sealed class LavaBasin
        {
            private readonly double2 center;
            private readonly double radius, shoreWidth, phase;
            public LavaBasin(double2 center, double radius, double shoreWidth, double phase)
            { this.center = center; this.radius = radius; this.shoreWidth = shoreWidth; this.phase = phase; }

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
                    double satelliteRadius = Math.Max(0.65d,
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
