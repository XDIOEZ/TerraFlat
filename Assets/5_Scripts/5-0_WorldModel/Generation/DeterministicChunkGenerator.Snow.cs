using System;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 天然雪原区域

        // 先按稳定区域决定出现概率，再抽大小；同一片雪原跨区块和环绕边界时共用种子。
        private static bool IsSnowRegionAllowed(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, double height, double temperatureCelsius,
            double precipitation, int worldX, int worldY)
        {
            if (!settings.SnowRegionsEnabled)
                return true;
            if (height < settings.SeaLevel || settings.SnowRegionChance <= 0d ||
                !SurfaceBiomeClassifier.IsSnowClimate(settings, temperatureCelsius, precipitation))
                return false;

            var domain = request.Topology.ToDomain();
            bool wrapped = request.Topology.IsWrapped;
            int countX = wrapped ? Math.Max(1, request.Topology.Span.X / settings.SnowRegionSize) : 0;
            int countY = wrapped ? Math.Max(1, request.Topology.Span.Y / settings.SnowRegionSize) : 0;
            double stepX = wrapped ? request.Topology.Span.X / (double)countX : settings.SnowRegionSize;
            double stepY = wrapped ? request.Topology.Span.Y / (double)countY : settings.SnowRegionSize;
            double2 anchor = wrapped ? new double2(request.Topology.Min.X, request.Topology.Min.Y) : default;
            double2 query = domain.Normalize(new double2(worldX, worldY));
            int baseX = (int)Math.Floor((query.x - anchor.x) / stepX);
            int baseY = (int)Math.Floor((query.y - anchor.y) / stepY);

            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int rx = wrapped ? ((baseX + dx) % countX + countX) % countX : baseX + dx;
                int ry = wrapped ? ((baseY + dy) % countY + countY) % countY : baseY + dy;
                if (!TryResolveSnowRegion(request, settings, rx, ry,
                        out double2 center, out double radius, out bool large))
                    continue;
                if (!large && height < settings.SnowPeakMinimumHeight)
                    continue;
                double2 delta = domain.ShortestDelta(query, center);
                if (Math.Abs(delta.x) > radius * 1.08d || Math.Abs(delta.y) > radius * 1.08d)
                    continue;
                double phase = Hash01(request.WorldSeed, rx, ry, 0x5a0f0016u) * Math.PI * 2d;
                double edge = 1d + 0.08d * Math.Sin(delta.x / radius * 4d + phase) *
                    Math.Sin(delta.y / radius * 3d - phase);
                if (delta.x * delta.x + delta.y * delta.y <= radius * radius * edge * edge)
                    return true;
            }
            return false;
        }

        // 正式积雪判定与调试定位共用区域计划，避免复制一套概率和中心算法。
        private static bool TryResolveSnowRegion(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, int rx, int ry,
            out double2 center, out double radius, out bool large)
        {
            center = default;
            radius = 0d;
            large = false;
            if (Hash01(request.WorldSeed, rx, ry, 0x5a0f0011u) >= settings.SnowRegionChance)
                return false;
            bool wrapped = request.Topology.IsWrapped;
            int countX = wrapped ? Math.Max(1, request.Topology.Span.X / settings.SnowRegionSize) : 0;
            int countY = wrapped ? Math.Max(1, request.Topology.Span.Y / settings.SnowRegionSize) : 0;
            double stepX = wrapped ? request.Topology.Span.X / (double)countX : settings.SnowRegionSize;
            double stepY = wrapped ? request.Topology.Span.Y / (double)countY : settings.SnowRegionSize;
            double2 anchor = wrapped ? new double2(request.Topology.Min.X, request.Topology.Min.Y) : default;
            large = Hash01(request.WorldSeed, rx, ry, 0x5a0f0012u) < settings.SnowLargeRegionRatio;
            radius = Math.Min(Math.Min(stepX, stepY) * 0.42d, SampleSnowQuadraticRadius(
                large ? settings.SnowLargeMinRadius : settings.SnowPeakMinRadius,
                large ? settings.SnowLargeMaxRadius : settings.SnowPeakMaxRadius,
                Hash01(request.WorldSeed, rx, ry, 0x5a0f0013u)));
            center = anchor + new double2(
                (rx + 0.15d + Hash01(request.WorldSeed, rx, ry, 0x5a0f0014u) * 0.7d) * stepX,
                (ry + 0.15d + Hash01(request.WorldSeed, rx, ry, 0x5a0f0015u) * 0.7d) * stepY);
            return true;
        }

        // 权重为 1-t²，反解累计概率可直接抽样，尺寸更常落在区间中间而不是两端。
        private static double SampleSnowQuadraticRadius(double minimum, double maximum, double probability)
        {
            double t = 2d * Math.Sin(Math.Asin(Clamp01(probability) * 2d - 1d) / 3d);
            return (minimum + maximum) * 0.5d + (maximum - minimum) * 0.5d * t;
        }

        #endregion
    }
}
