using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 独立陆地石油盆地

        // 缓存不可变候选并复用岩浆轮廓，石油不加入淡水水文与供湿链路。
        private readonly ConcurrentDictionary<(long, string, int, ulong, ChunkGenerationTopologySnapshot, Int2),
            Lazy<LavaBasin>> oilBasins = new();
        private readonly ConcurrentQueue<(long, string, int, ulong, ChunkGenerationTopologySnapshot, Int2)>
            oilBasinOrder = new();

        private IReadOnlyList<LavaBasin> ResolveCrudeOilLandBasins(ChunkGenerationRequest request,
            CancellationToken cancellation = default)
        {
            var settings = request.Profile.Settings;
            if (!settings.OilLakeEnabled || settings.OilLakeChance <= 0d)
                return Array.Empty<LavaBasin>();
            var result = new List<LavaBasin>(4);
            bool wrapped = request.Topology.IsWrapped;
            int countX = wrapped ? Math.Max(1, request.Topology.Span.X / settings.OilLakeRegionSize) : 0;
            int countY = wrapped ? Math.Max(1, request.Topology.Span.Y / settings.OilLakeRegionSize) : 0;
            double stepX = wrapped ? request.Topology.Span.X / (double)countX : settings.OilLakeRegionSize;
            double stepY = wrapped ? request.Topology.Span.Y / (double)countY : settings.OilLakeRegionSize;
            double2 anchor = wrapped ? new double2(request.Topology.Min.X, request.Topology.Min.Y) : default;
            double2 query = request.Topology.ToDomain().Normalize(new double2(
                request.Address.ChunkOrigin.X + request.Profile.Width * 0.5d,
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
                if (!visited.Add(region) || Hash01(request.WorldSeed, rx, ry, 0x0a114501u) >= settings.OilLakeChance)
                    continue;
                var key = (request.WorldEpoch, request.Address.DimensionId, request.WorldSeed,
                    request.Profile.GenerationFingerprint, request.Topology, region);
                if (!oilBasins.TryGetValue(key, out var candidate))
                {
                    var created = new Lazy<LavaBasin>(() => FindCrudeOilLandBasin(request, region,
                        anchor + new double2(rx * stepX, ry * stepY), new double2(stepX, stepY)),
                        LazyThreadSafetyMode.ExecutionAndPublication);
                    candidate = oilBasins.GetOrAdd(key, created);
                    if (ReferenceEquals(candidate, created)) oilBasinOrder.Enqueue(key);
                    while (oilBasins.Count > 256 && oilBasinOrder.TryDequeue(out var oldest))
                        oilBasins.TryRemove(oldest, out _);
                }
                if (candidate.Value is { } basin) result.Add(basin);
            }
            return result;
        }

        private static LavaBasin FindCrudeOilLandBasin(ChunkGenerationRequest request, Int2 region,
            double2 origin, double2 span)
        {
            var settings = request.Profile.Settings;
            double radiusScale = Math.Sqrt(settings.OilLakeAreaRatio);
            double radius = ResolveLavaRadius(request.WorldSeed ^ 0x0a114502, region, settings,
                Math.Min(settings.LavaLakeRareMaxRadius, Math.Min(span.x, span.y) * 0.29d)) * radiusScale;
            double margin = radius * LavaBasinMaxReachFactor + 1d;
            if (span.x <= margin * 2d || span.y <= margin * 2d) return null;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                double2 center = math.floor(origin + margin + (span - margin * 2d) * new double2(
                    Hash01(request.WorldSeed, region.X, region.Y, 0x0a114510u + (uint)attempt * 2u),
                    Hash01(request.WorldSeed, region.X, region.Y, 0x0a114511u + (uint)attempt * 2u))) + 0.5d;
                SurfaceClimateSample climate = SampleSurfaceClimate(request, settings, (int)center.x, (int)center.y);
                SurfaceBiomeKind biome = ResolveSurfaceBiome(settings, climate,
                    Clamp01(climate.Precipitation * 0.78d + (1d - climate.Height) * 0.22d), false);
                if (climate.Height < settings.SeaLevel || biome is SurfaceBiomeKind.Ocean or SurfaceBiomeKind.Beach)
                    continue;
                return new LavaBasin(request.Topology.ToDomain().Normalize(center), radius, 0d,
                    Hash01(request.WorldSeed, region.X, region.Y, 0x0a114503u) * Math.PI * 2d,
                    radiusScale);
            }
            return null;
        }

        #endregion
    }
}
