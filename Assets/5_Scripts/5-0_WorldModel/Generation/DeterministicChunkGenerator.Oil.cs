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
            return ResolveRegionalBasins(request, cancellation, settings.OilLakeRegionSize, settings.OilLakeChance,
                0x0a114501u, oilBasins, oilBasinOrder, null, FindOilBasin);
        }

        private static LavaBasin FindCrudeOilLandBasin(ChunkGenerationRequest request, Int2 region,
            double2 origin, double2 span, double _)
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
