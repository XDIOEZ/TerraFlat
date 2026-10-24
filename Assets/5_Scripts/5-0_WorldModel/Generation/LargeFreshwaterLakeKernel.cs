using System;
using System.Buffers;
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
            Func<Int2, double> heightSampler, CancellationToken cancellation, int outputPadding = 0,
            Func<Int2, double> precipitationSampler = null)
        {
            List<Basin> basins = FindBasins(request, settings, heightSampler, cancellation, outputPadding,
                precipitationSampler);
            if (basins.Count == 0) return rivers;
            var cells = new Dictionary<Int2, GeneratedHydrologyCell>();
            var floodplains = new Dictionary<Int2, double>();
            WorldTopologyDomain domain = request.Topology.ToDomain();
            var output = new LakeOutputBounds(request, outputPadding);
            int visited = 0;
            if (rivers != null)
            {
                foreach (var pair in rivers.WaterCells)
                {
                    if ((visited++ & 63) == 0) cancellation.ThrowIfCancellationRequested();
                    if (output.Contains(pair.Key)) cells[pair.Key] = pair.Value;
                }
                foreach (var pair in rivers.FloodplainCells)
                {
                    if ((visited++ & 63) == 0) cancellation.ThrowIfCancellationRequested();
                    if (pair.Value > 0d && output.Contains(pair.Key)) floodplains[pair.Key] = pair.Value;
                }
            }

            // 按原候选顺序处理包围框，同格首个有效湖泊或岛屿仍遮断后续盆地。
            var resolved = new HashSet<Int2>();
            // 同一盆地的每列/每行只计算一次环绕坐标和最短位移。
            int[] normalizedX = ArrayPool<int>.Shared.Rent(Math.Max(1, output.MaxX - output.MinX));
            int[] normalizedY = ArrayPool<int>.Shared.Rent(Math.Max(1, output.MaxY - output.MinY));
            double[] deltaX = ArrayPool<double>.Shared.Rent(Math.Max(1, output.MaxX - output.MinX));
            double[] deltaY = ArrayPool<double>.Shared.Rent(Math.Max(1, output.MaxY - output.MinY));
            try
            {
                foreach (Basin basin in basins)
                {
                    double reach = basin.Reach;
                    output.GetImages(basin.Center, reach, out int minImageX, out int maxImageX,
                        out int minImageY, out int maxImageY);
                    for (int imageY = minImageY; imageY <= maxImageY; imageY++)
                    for (int imageX = minImageX; imageX <= maxImageX; imageX++)
                    {
                        double centerX = basin.Center.x + imageX * (double)output.SpanX;
                        double centerY = basin.Center.y + imageY * (double)output.SpanY;
                        int minX = Math.Max(output.MinX, (int)Math.Floor(centerX - reach) - 1);
                        int minY = Math.Max(output.MinY, (int)Math.Floor(centerY - reach) - 1);
                        int maxX = Math.Min(output.MaxX, (int)Math.Ceiling(centerX + reach) + 1);
                        int maxY = Math.Min(output.MaxY, (int)Math.Ceiling(centerY + reach) + 1);
                        for (int i = 0; i < maxX - minX; i++)
                        {
                            int canonical = request.Topology.NormalizeX(minX + i);
                            normalizedX[i] = canonical;
                            deltaX[i] = domain.ShortestDelta(basin.Center,
                                new double2(canonical + 0.5d, basin.Center.y)).x;
                        }
                        for (int i = 0; i < maxY - minY; i++)
                        {
                            int canonical = request.Topology.NormalizeY(minY + i);
                            normalizedY[i] = canonical;
                            deltaY[i] = domain.ShortestDelta(basin.Center,
                                new double2(basin.Center.x, canonical + 0.5d)).y;
                        }
                        for (int y = minY; y < maxY; y++)
                        for (int x = minX; x < maxX; x++)
                        {
                            if ((visited++ & 63) == 0) cancellation.ThrowIfCancellationRequested();
                            int ix = x - minX, iy = y - minY;
                            var position = new Int2(normalizedX[ix], normalizedY[iy]);
                            if (resolved.Contains(position) ||
                                !basin.TrySampleProjected(new double2(deltaX[ix], deltaY[iy]),
                                    out double depth, out bool island)) continue;
                            // 自然陆地岛不能再被旧汇流小水潭覆盖；盆地外保留原河流及冲积层。
                            if (island)
                            {
                                cells.Remove(position);
                                resolved.Add(position);
                                continue;
                            }
                            if (heightSampler(position) <= settings.SeaLevel + 0.01d) continue;
                            cells[position] = new GeneratedHydrologyCell(GeneratedHydrologyKind.Lake,
                                0d, depth, basin.SurfaceLevel);
                            resolved.Add(position);
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(normalizedX);
                ArrayPool<int>.Shared.Return(normalizedY);
                ArrayPool<double>.Shared.Return(deltaX);
                ArrayPool<double>.Shared.Return(deltaY);
            }
            return new GeneratedHydrologyMap(cells, floodplains, rivers?.EstuaryCells);
        }

        /// <summary>环绕外圈只保留一份规范坐标，并为盆地枚举与输出相交的镜像。</summary>
        private readonly struct LakeOutputBounds
        {
            internal readonly int MinX, MinY, MaxX, MaxY, SpanX, SpanY;

            internal LakeOutputBounds(ChunkGenerationRequest request, int padding)
            {
                MinX = request.Address.ChunkOrigin.X - padding;
                MinY = request.Address.ChunkOrigin.Y - padding;
                SpanX = request.Topology.IsWrapped ? request.Topology.Span.X : 0;
                SpanY = request.Topology.IsWrapped ? request.Topology.Span.Y : 0;
                int width = request.Profile.Width + padding * 2;
                int height = request.Profile.Height + padding * 2;
                MaxX = MinX + (SpanX > 0 ? Math.Min(width, SpanX) : width);
                MaxY = MinY + (SpanY > 0 ? Math.Min(height, SpanY) : height);
            }

            internal bool Contains(Int2 position)
            {
                int x = SpanX > 0 ? PositiveModulo(position.X - MinX, SpanX) : position.X - MinX;
                int y = SpanY > 0 ? PositiveModulo(position.Y - MinY, SpanY) : position.Y - MinY;
                return x >= 0 && y >= 0 && x < MaxX - MinX && y < MaxY - MinY;
            }

            internal void GetImages(double2 center, double reach,
                out int minX, out int maxX, out int minY, out int maxY)
            {
                minX = SpanX > 0 ? (int)Math.Ceiling((MinX - center.x - reach - 1d) / SpanX) : 0;
                maxX = SpanX > 0 ? (int)Math.Floor((MaxX - center.x + reach + 1d) / SpanX) : 0;
                minY = SpanY > 0 ? (int)Math.Ceiling((MinY - center.y - reach - 1d) / SpanY) : 0;
                maxY = SpanY > 0 ? (int)Math.Floor((MaxY - center.y + reach + 1d) / SpanY) : 0;
            }
        }

        #endregion

        #region 确定性盆地候选

        /// <summary>规范区域覆盖循环接缝；只检查与当前区块相交的盆地，不扫描或加载其它区块。</summary>
        private static List<Basin> FindBasins(ChunkGenerationRequest request, ChunkGenerationSettingsSnapshot settings,
            Func<Int2, double> heightSampler, CancellationToken cancellation, int outputPadding,
            Func<Int2, double> precipitationSampler)
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
            double candidateReach = Math.Min(settings.LargeLakeMaxRadius, Math.Min(stepX, stepY) * 0.28d) * 1.55d;
            int reachX = Math.Max(1, (int)Math.Ceiling((request.Profile.Width * 0.5d + outputPadding + candidateReach) / stepX));
            int reachY = Math.Max(1, (int)Math.Ceiling((request.Profile.Height * 0.5d + outputPadding + candidateReach) / stepY));
            var visited = new HashSet<Int2>();
            // 最终降水需要邻区水源，湖候选覆盖完整输出外圈而不是只看本区块中心附近。
            for (int dy = -reachY; dy <= reachY; dy++)
            for (int dx = -reachX; dx <= reachX; dx++)
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
                if (Math.Abs(delta.x) > reach + request.Profile.Width * 0.5d + outputPadding ||
                    Math.Abs(delta.y) > reach + request.Profile.Height * 0.5d + outputPadding) continue;
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
                // 第一层海洋降水只调整候选出现率，干燥内陆仍保留旧规则的一半湖泊机会。
                double rainfall = precipitationSampler?.Invoke(new Int2((int)Math.Floor(center.x),
                    (int)Math.Floor(center.y))) ?? 1d;
                if (Random01(request.WorldSeed, rx, ry, 1) >= settings.LargeLakeChance * (0.5d + 0.5d * rainfall))
                    continue;
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
            internal double2 Center => center;
            internal double Reach => radius * 1.55d;

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
            internal bool TrySampleProjected(double2 delta, out double depth, out bool island)
            {
                depth = 0d; island = false;
                if (Math.Abs(delta.x) > radius * 1.55d || Math.Abs(delta.y) > radius * 1.55d) return false;
                double2 local = new double2(delta.x * cosine + delta.y * sine,
                    -delta.x * sine + delta.y * cosine) / new double2(radius * 1.25d, radius * 0.8d);
                // 最远可能岸线只有 1.135 倍归一化半径，外侧无需计算反正切和两次正弦。
                const double maximumShore = 1d + 0.09d + 0.045d;
                if (math.lengthsq(local) > maximumShore * maximumShore) return false;
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
