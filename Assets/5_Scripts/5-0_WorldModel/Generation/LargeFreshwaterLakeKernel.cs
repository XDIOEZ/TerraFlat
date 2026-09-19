using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 大型内陆淡水盆地。种子、规范区域坐标和地形决定湖岸及岛屿，与生成顺序无关。
    /// 不降低海平面、不把海洋重新标为淡水；只覆盖通过内陆检查的有限盆地，湖泊流速保持零。
    /// </summary>
    internal static class LargeFreshwaterLakeKernel
    {
        #region 区块覆盖

        internal static GeneratedHydrologyMap Build(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, GeneratedHydrologyMap rivers,
            Func<Int2, double> heightSampler, CancellationToken cancellation)
        {
            List<Basin> basins = FindBasins(request, settings, heightSampler, cancellation);
            if (basins.Count == 0) return rivers;
            var cells = new Dictionary<Int2, GeneratedHydrologyCell>();
            var floodplains = new Dictionary<Int2, double>();
            WorldTopologyDomain domain = request.Topology.ToDomain();
            for (int y = 0; y < request.Profile.Height; y++)
            for (int x = 0; x < request.Profile.Width; x++)
            {
                if (((y * request.Profile.Width + x) & 63) == 0) cancellation.ThrowIfCancellationRequested();
                var position = new Int2(request.Topology.NormalizeX(request.Address.ChunkOrigin.X + x),
                    request.Topology.NormalizeY(request.Address.ChunkOrigin.Y + y));
                bool hasWater = rivers != null && rivers.TryGet(position.X, position.Y, out _);
                GeneratedHydrologyCell cell = default;
                if (hasWater) rivers.TryGet(position.X, position.Y, out cell);
                foreach (Basin basin in basins)
                {
                    if (!basin.TrySample(domain, position, out double depth, out bool island)) continue;
                    // 自然陆地岛不能再被旧汇流小水潭覆盖；盆地外保留原河流及冲积层。
                    if (island) { hasWater = false; break; }
                    if (heightSampler(position) <= settings.SeaLevel + 0.01d) continue;
                    hasWater = true;
                    cell = new GeneratedHydrologyCell(GeneratedHydrologyKind.Lake, 0d, depth, basin.SurfaceLevel);
                    break;
                }
                if (hasWater) cells[position] = cell;
                double floodplain = rivers?.GetFloodplainStrength(position.X, position.Y) ?? 0d;
                if (floodplain > 0d) floodplains[position] = floodplain;
            }
            return new GeneratedHydrologyMap(cells, floodplains);
        }

        #endregion

        #region 确定性盆地候选

        /// <summary>规范区域覆盖循环接缝；只检查与当前区块相交的盆地，不扫描或加载其它区块。</summary>
        private static List<Basin> FindBasins(ChunkGenerationRequest request, ChunkGenerationSettingsSnapshot settings,
            Func<Int2, double> heightSampler, CancellationToken cancellation)
        {
            var result = new List<Basin>(4);
            WorldTopologyDomain domain = request.Topology.ToDomain();
            bool wrapped = request.Topology.IsWrapped;
            int countX = wrapped ? Math.Max(1, request.Topology.Span.X / settings.LargeLakeRegionSize) : 0;
            int countY = wrapped ? Math.Max(1, request.Topology.Span.Y / settings.LargeLakeRegionSize) : 0;
            double stepX = wrapped ? request.Topology.Span.X / (double)countX : settings.LargeLakeRegionSize;
            double stepY = wrapped ? request.Topology.Span.Y / (double)countY : settings.LargeLakeRegionSize;
            double2 anchor = wrapped ? new double2(request.Topology.Min.X, request.Topology.Min.Y) : default;
            double2 query = domain.Normalize(new double2(request.Address.ChunkOrigin.X + request.Profile.Width * 0.5d,
                request.Address.ChunkOrigin.Y + request.Profile.Height * 0.5d));
            int baseX = (int)Math.Floor((query.x - anchor.x) / stepX);
            int baseY = (int)Math.Floor((query.y - anchor.y) / stepY);
            var visited = new HashSet<Int2>();
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                cancellation.ThrowIfCancellationRequested();
                int rx = wrapped ? PositiveModulo(baseX + dx, countX) : baseX + dx;
                int ry = wrapped ? PositiveModulo(baseY + dy, countY) : baseY + dy;
                if (!visited.Add(new Int2(rx, ry)) || Random01(request.WorldSeed, rx, ry, 1) >= settings.LargeLakeChance) continue;
                double maximum = Math.Min(settings.LargeLakeMaxRadius, Math.Min(stepX, stepY) * 0.28d);
                double minimum = Math.Min(settings.LargeLakeMinRadius, maximum * 0.75d);
                double radius = minimum + (maximum - minimum) * Random01(request.WorldSeed, rx, ry, 2);
                double2 center = domain.Normalize(anchor + new double2(
                    (rx + 0.5d + (Random01(request.WorldSeed, rx, ry, 3) - 0.5d) * 0.16d) * stepX,
                    (ry + 0.5d + (Random01(request.WorldSeed, rx, ry, 4) - 0.5d) * 0.16d) * stepY));
                double2 delta = domain.ShortestDelta(query, center);
                double reach = radius * 1.55d;
                if (Math.Abs(delta.x) > reach + request.Profile.Width * 0.5d ||
                    Math.Abs(delta.y) > reach + request.Profile.Height * 0.5d) continue;
                double height = SampleHeight(domain, center, heightSampler);
                if (height <= settings.BeachLevel + 0.02d) continue;
                bool inland = true;
                for (int i = 0; i < 12; i++)
                {
                    double angle = i * Math.PI / 6d;
                    if (SampleHeight(domain, center + new double2(Math.Cos(angle), Math.Sin(angle)) * reach, heightSampler)
                        <= settings.SeaLevel + 0.015d) { inland = false; break; }
                }
                if (!inland) continue;
                result.Add(new Basin(center, radius, height, request.WorldSeed, rx, ry, settings.LargeLakeIslandChance));
            }
            return result;
        }

        private static double SampleHeight(WorldTopologyDomain domain, double2 position, Func<Int2, double> sample)
        {
            position = domain.Normalize(position);
            return sample(new Int2((int)Math.Floor(position.x), (int)Math.Floor(position.y)));
        }

        private static int PositiveModulo(int value, int modulus) => (value % modulus + modulus) % modulus;

        private static double Random01(int seed, int x, int y, uint salt)
        {
            unchecked
            {
                uint value = (uint)seed ^ (uint)x * 0x9E3779B9u ^ (uint)y * 0x85EBCA6Bu ^ salt * 0xC2B2AE35u;
                value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15;
                value *= 0x846CA68Bu; value ^= value >> 16;
                return value / 4294967296d;
            }
        }

        #endregion

        #region 湖岸与岛屿

        private sealed class Basin
        {
            private readonly double2 center;
            private readonly double radius;
            private readonly double cosine, sine, phase;
            private readonly double3[] islands; // 盆地局部归一化坐标 x/y 与岛屿半径。
            internal double SurfaceLevel { get; }

            internal Basin(double2 center, double radius, double height, int seed, int x, int y, double islandChance)
            {
                this.center = center; this.radius = radius;
                double angle = Random01(seed, x, y, 5) * Math.PI * 2d;
                cosine = Math.Cos(angle); sine = Math.Sin(angle);
                phase = Random01(seed, x, y, 6) * Math.PI * 2d;
                SurfaceLevel = Math.Min(1d, height + 0.018d);
                int count = Random01(seed, x, y, 7) < islandChance ? 1 + (int)(Random01(seed, x, y, 8) * 3d) : 0;
                islands = new double3[count];
                for (int i = 0; i < count; i++)
                {
                    double a = Random01(seed, x, y, (uint)(10 + i * 3)) * Math.PI * 2d;
                    double distance = 0.15d + Random01(seed, x, y, (uint)(11 + i * 3)) * 0.42d;
                    islands[i] = new double3(Math.Cos(a) * distance, Math.Sin(a) * distance,
                        0.08d + Random01(seed, x, y, (uint)(12 + i * 3)) * 0.12d);
                }
            }

            /// <summary>不规则岸线和岛岸共享连续距离水深，避免切块边界或湖心突然变深。</summary>
            internal bool TrySample(WorldTopologyDomain domain, Int2 position, out double depth, out bool island)
            {
                depth = 0d; island = false;
                double2 delta = domain.ShortestDelta(center, new double2(position.X + 0.5d, position.Y + 0.5d));
                if (Math.Abs(delta.x) > radius * 1.55d || Math.Abs(delta.y) > radius * 1.55d) return false;
                double2 local = new double2(delta.x * cosine + delta.y * sine,
                    -delta.x * sine + delta.y * cosine) / new double2(radius * 1.25d, radius * 0.8d);
                double angle = Math.Atan2(local.y, local.x);
                double shore = 1d + Math.Sin(angle * 3d + phase) * 0.09d + Math.Sin(angle * 5d - phase) * 0.045d;
                double distance = (shore - math.length(local)) * radius * 0.8d;
                if (distance <= 0d) return false;
                foreach (double3 patch in islands)
                {
                    double islandDistance = (math.length(local - patch.xy) - patch.z) * radius * 0.8d;
                    if (islandDistance <= 0d) { island = true; return true; }
                    distance = Math.Min(distance, islandDistance);
                }
                depth = 0.08d + 0.87d * Math.Sqrt(Math.Min(1d, distance / Math.Max(1d, radius * 0.45d)));
                return true;
            }
        }

        #endregion
    }
}
