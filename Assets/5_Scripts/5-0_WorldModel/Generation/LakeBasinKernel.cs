using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    /// <summary>先确定天然湖盆及最低岸口，再用长期入水与蒸发计算蓄水；不直接创建液体。</summary>
    internal static class LakeBasinKernel
    {
        #region 确定性湖盆几何

        private const int MaximumBasinCells = 65536;
        private const int MaximumOutletCandidates = 128;
        private const int MaximumOutletTraceSteps = 512;
        private const double HeightEpsilon = 0.000000001d;
        private static readonly Int2[] NeighborOffsets =
        {
            new(-1, -1), new(0, -1), new(1, -1), new(-1, 0),
            new(1, 0), new(-1, 1), new(0, 1), new(1, 1)
        };

        /// <summary>输入和输出均为规范世界地格坐标；无法找到有界内陆低洼处时返回 null。</summary>
        internal static Basin Build(ChunkGenerationRequest request, ChunkGenerationSettingsSnapshot settings,
            Int2 sink, Func<Int2, double> heightSampler, CancellationToken cancellation)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (heightSampler == null) throw new ArgumentNullException(nameof(heightSampler));
            cancellation.ThrowIfCancellationRequested();

            ChunkGenerationTopologySnapshot topology = request.Topology;
            WorldTopologyDomain domain = topology.ToDomain();
            var sampledHeights = new Dictionary<Int2, double>();
            double Height(Int2 position)
            {
                if (sampledHeights.TryGetValue(position, out double height)) return height;
                height = heightSampler(position);
                RequireFinite(height, nameof(heightSampler));
                sampledHeights.Add(position, height);
                return height;
            }

            Int2 anchor = Normalize(topology, sink);
            double searchRadius = Math.Max(settings.LakeMaxRadius, settings.LakeRareMaxRadius);
            Int2 center = anchor;
            bool foundSink = false;
            for (int step = 0; step < MaximumBasinCells; step++)
            {
                if ((step & 63) == 0) cancellation.ThrowIfCancellationRequested();
                double centerHeight = Height(center);
                if (centerHeight <= settings.SeaLevel) return null;
                Int2 lower = center;
                double lowerHeight = centerHeight;
                foreach (Int2 offset in NeighborOffsets)
                {
                    Int2 candidate = Normalize(topology, center + offset);
                    double candidateHeight = Height(candidate);
                    if (candidateHeight < lowerHeight - HeightEpsilon)
                    {
                        lower = candidate;
                        lowerHeight = candidateHeight;
                    }
                }
                if (lower == center) { foundSink = true; break; }
                if (DistanceSquared(domain, anchor, lower) > searchRadius * searchRadius) return null;
                center = lower;
            }
            if (!foundSink) return null;

            int radius = SampleRadius(request.WorldSeed, center, settings);
            if (topology.IsWrapped)
                radius = Math.Min(radius, Math.Max(1, Math.Min(topology.Span.X, topology.Span.Y) / 2 - 1));
            double minimum = Height(center);
            double maximumLevel = Math.Min(1d, minimum + settings.LakeMaxLevelRise);
            var cells = new List<Int2>();
            var bottoms = new List<double>();
            var queued = new HashSet<Int2> { center };
            var frontier = new SortedSet<FloodCell> { new(center, minimum, minimum) };
            double spillLevel = maximumLevel;
            Int2 outlet = center;
            bool hasOutlet = false;
            List<Int2> outletPath = null;
            int outletCandidates = 0;

            while (frontier.Count > 0)
            {
                if ((cells.Count & 63) == 0) cancellation.ThrowIfCancellationRequested();
                FloodCell next = frontier.Min;
                frontier.Remove(next);
                double floodLimit = hasOutlet ? spillLevel : maximumLevel;
                if (next.Level > floodLimit + HeightEpsilon)
                    break;
                if (cells.Count >= MaximumBasinCells)
                    break;

                // 外圈只提供岸口候选，必须确认天然下坡通路才允许溢流。
                if (next.Position != center &&
                    (DistanceSquared(domain, center, next.Position) >= (double)radius * radius ||
                     next.Height <= settings.SeaLevel))
                {
                    if (!hasOutlet && outletCandidates++ < MaximumOutletCandidates &&
                        TryTraceOutlet(topology, domain, center, radius, minimum, next.Position,
                            next.Level, settings, Height, cancellation, out List<Int2> path))
                    {
                        spillLevel = next.Level;
                        outlet = next.Position;
                        hasOutlet = true;
                        outletPath = path;
                        // 出口水位确定后继续收齐盆内同水位连通格，避免按坐标提前截断湖面。
                    }
                    continue;
                }

                cells.Add(next.Position);
                bottoms.Add(next.Height);
                foreach (Int2 offset in NeighborOffsets)
                {
                    Int2 candidate = Normalize(topology, next.Position + offset);
                    if (!queued.Add(candidate)) continue;
                    double height = Height(candidate);
                    frontier.Add(new FloodCell(candidate, height, Math.Max(next.Level, height)));
                }
            }

            return new Basin(center, radius, cells, bottoms, spillLevel, outlet, hasOutlet, outletPath);
        }

        private static Int2 Normalize(ChunkGenerationTopologySnapshot topology, Int2 position)
            => new(topology.NormalizeX(position.X), topology.NormalizeY(position.Y));

        private static double DistanceSquared(WorldTopologyDomain domain, Int2 from, Int2 to)
        {
            double2 delta = domain.ShortestDelta(new double2(from.X, from.Y), new double2(to.X, to.Y));
            return delta.x * delta.x + delta.y * delta.y;
        }

        #endregion

        #region 天然溢流通路

        private static bool TryTraceOutlet(ChunkGenerationTopologySnapshot topology, WorldTopologyDomain domain,
            Int2 center, int radius, double minimumHeight, Int2 shore, double spillLevel,
            ChunkGenerationSettingsSnapshot settings, Func<Int2, double> heightSampler,
            CancellationToken cancellation, out List<Int2> path)
        {
            path = null;
            var traced = new List<Int2>();
            var visited = new HashSet<Int2>();
            double traceRadius = Math.Max(radius * 4d, settings.LakeRareMaxRadius * 2d);
            double basinRadiusSquared = (double)radius * radius;
            Int2 current = shore;
            for (int step = 0; step < MaximumOutletTraceSteps; step++)
            {
                if ((step & 31) == 0) cancellation.ThrowIfCancellationRequested();
                if (!visited.Add(current)) return false;
                double height = heightSampler(current);
                traced.Add(current);
                if (height <= settings.SeaLevel)
                {
                    path = traced;
                    return true;
                }
                double distanceSquared = DistanceSquared(domain, center, current);
                if (distanceSquared < basinRadiusSquared || distanceSquared > traceRadius * traceRadius)
                    return false;
                if (height < minimumHeight - HeightEpsilon)
                {
                    path = traced;
                    return true;
                }

                Int2 lower = current;
                double lowerHeight = height;
                bool hasHigherNeighbor = false;
                foreach (Int2 offset in NeighborOffsets)
                {
                    Int2 candidate = Normalize(topology, current + offset);
                    double candidateHeight = heightSampler(candidate);
                    if (candidateHeight > height + HeightEpsilon) hasHigherNeighbor = true;
                    if (candidateHeight >= height - HeightEpsilon) continue;
                    if (lower == current || candidateHeight < lowerHeight - HeightEpsilon ||
                        (Math.Abs(candidateHeight - lowerHeight) <= HeightEpsilon &&
                         ComparePosition(candidate, lower) < 0))
                    {
                        lower = candidate;
                        lowerHeight = candidateHeight;
                    }
                }
                if (lower == current)
                {
                    // 只有盆外真实低洼点能接收出流，截断或完全平坦地形不能冒充出口终点。
                    if (traced.Count < 2 || !hasHigherNeighbor || height >= spillLevel - HeightEpsilon)
                        return false;
                    path = traced;
                    return true;
                }

                // 读取天然最低邻格；不能绕开回盆路线来人为制造出口。
                if (DistanceSquared(domain, center, lower) < basinRadiusSquared || visited.Contains(lower))
                    return false;
                current = lower;
            }
            return false;
        }

        private static int ComparePosition(Int2 left, Int2 right)
        {
            int x = left.X.CompareTo(right.X);
            return x != 0 ? x : left.Y.CompareTo(right.Y);
        }

        #endregion

        #region 长期蓄水与蒸发

        /// <summary>降水使用盆地的长期平均相对量，汇流使用河网流量；二者只决定水位和出流。</summary>
        internal static State ResolveState(Basin basin, double inflow, double precipitation, double celsius,
            ChunkGenerationSettingsSnapshot settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            RequireFinite(inflow, nameof(inflow));
            RequireFinite(precipitation, nameof(precipitation));
            RequireFinite(celsius, nameof(celsius));
            if (basin == null || basin.Count == 0)
                return new State(0d, 0d, true);

            // 沿用 Azgaar 的温度与海拔蒸发关系，再以配置倍率适配本项目的相对降水单位。
            double altitudeCorrection = -settings.GetAltitudeTemperatureOffsetCelsius(basin.SpillLevel);
            double evaporationPerCell = Math.Max(0d,
                (14d * (celsius + altitudeCorrection) + 75d) / Math.Max(1d, 80d - celsius)) *
                settings.LakeEvaporationScale;
            double incoming = Math.Max(0d, inflow) * settings.LakeInflowScale +
                              Math.Max(0d, precipitation) * basin.Count;
            double remaining = incoming - evaporationPerCell * basin.Count;
            double heightUnit = Math.Max(0.000001d, settings.LakeMaxLevelRise);
            double volume = remaining * heightUnit;
            double capacity = basin.MeasureVolume(basin.SpillLevel);
            if (volume <= HeightEpsilon || capacity <= HeightEpsilon)
                return new State(basin.MinimumHeight, 0d, true);

            if (volume >= capacity)
            {
                double outflow = basin.HasOutlet ? Math.Max(0d, (volume - capacity) / heightUnit) : 0d;
                return new State(basin.SpillLevel, outflow, false);
            }

            // 反解实际盆底容积，水不足时缩小湖面，高于水位的天然地格保持干燥。
            double lower = basin.MinimumHeight;
            double upper = basin.SpillLevel;
            for (int iteration = 0; iteration < 28; iteration++)
            {
                double middle = (lower + upper) * 0.5d;
                if (basin.MeasureVolume(middle) < volume) lower = middle;
                else upper = middle;
            }
            return new State((lower + upper) * 0.5d, 0d, false);
        }

        private static void RequireFinite(double value, string parameter)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameter, "湖盆输入必须是有限数值。");
        }

        #endregion

        #region 半径二次峰值与稀有长尾

        private static int SampleRadius(int seed, Int2 center, ChunkGenerationSettingsSnapshot settings)
        {
            bool rare = Random01(seed, center, 0x382c91e7u) < settings.LakeRareChance;
            double minimum = rare ? settings.LakeMaxRadius : settings.LakeMinRadius;
            double maximum = rare ? Math.Max(minimum, settings.LakeRareMaxRadius) : settings.LakeMaxRadius;
            double peak = rare ? minimum : Math.Max(minimum, Math.Min(maximum, settings.LakePeakRadius));
            if (maximum <= minimum + HeightEpsilon) return Math.Max(1, (int)Math.Ceiling(minimum));
            double radius = Math.Max(peak - minimum, maximum - peak);
            double lower = (minimum - peak) / radius;
            double upper = (maximum - peak) / radius;
            double lowerIntegral = lower - lower * lower * lower / 3d;
            double upperIntegral = upper - upper * upper * upper / 3d;
            double integral = lowerIntegral + (upperIntegral - lowerIntegral) *
                              Random01(seed, center, 0xc5718a43u);
            double normalized = 2d * Math.Sin(Math.Asin(Math.Max(-1d, Math.Min(1d, integral * 1.5d))) / 3d);
            return Math.Max(1, (int)Math.Ceiling(Math.Max(minimum, Math.Min(maximum, peak + radius * normalized))));
        }

        private static double Random01(int seed, Int2 center, uint salt)
        {
            unchecked
            {
                uint value = (uint)seed ^ (uint)center.X * 0x9E3779B9u ^ (uint)center.Y * 0x85EBCA6Bu ^ salt;
                value ^= value >> 16; value *= 0x7FEB352Du; value ^= value >> 15;
                value *= 0x846CA68Bu; value ^= value >> 16;
                return value / 4294967296d;
            }
        }

        #endregion

        #region 不可变湖盆与水文结果

        internal sealed class Basin
        {
            private readonly Dictionary<Int2, double> bottomByPosition;
            private readonly double[] sortedBottoms;
            private readonly double[] bottomPrefix;

            internal Basin(Int2 center, int radius, List<Int2> cells, List<double> bottomHeights,
                double spillLevel, Int2 outlet, bool hasOutlet, List<Int2> outletPath)
            {
                Center = center;
                Radius = radius;
                Cells = Array.AsReadOnly(cells.ToArray());
                BottomHeights = Array.AsReadOnly(bottomHeights.ToArray());
                SpillLevel = spillLevel;
                Outlet = outlet;
                HasOutlet = hasOutlet;
                OutletPath = Array.AsReadOnly(outletPath?.ToArray() ?? Array.Empty<Int2>());
                OutletEndpoint = OutletPath.Count > 0 ? OutletPath[OutletPath.Count - 1] : outlet;
                bottomByPosition = new Dictionary<Int2, double>(cells.Count);
                for (int index = 0; index < cells.Count; index++)
                    bottomByPosition.Add(cells[index], bottomHeights[index]);
                sortedBottoms = bottomHeights.ToArray();
                Array.Sort(sortedBottoms);
                bottomPrefix = new double[sortedBottoms.Length + 1];
                for (int index = 0; index < sortedBottoms.Length; index++)
                    bottomPrefix[index + 1] = bottomPrefix[index] + sortedBottoms[index];
                MinimumHeight = sortedBottoms.Length == 0 ? spillLevel : sortedBottoms[0];
            }

            internal IReadOnlyList<Int2> Cells { get; }
            internal IReadOnlyList<double> BottomHeights { get; }
            internal Int2 Center { get; }
            internal Int2 Outlet { get; }
            internal IReadOnlyList<Int2> OutletPath { get; }
            internal Int2 OutletEndpoint { get; }
            internal int Radius { get; }
            internal int Count => Cells.Count;
            internal double SpillLevel { get; }
            internal double MinimumHeight { get; }
            internal bool HasOutlet { get; }

            internal bool TryGetBottomHeight(Int2 position, out double height)
                => bottomByPosition.TryGetValue(position, out height);

            internal double MeasureVolume(double level)
            {
                int lower = 0;
                int upper = sortedBottoms.Length;
                while (lower < upper)
                {
                    int middle = lower + (upper - lower) / 2;
                    if (sortedBottoms[middle] < level) lower = middle + 1;
                    else upper = middle;
                }
                return Math.Max(0d, lower * level - bottomPrefix[lower]);
            }
        }

        internal readonly struct State
        {
            internal State(double waterLevel, double outletFlow, bool isDry)
            {
                WaterLevel = waterLevel;
                OutletFlow = outletFlow;
                IsDry = isDry;
            }

            internal double WaterLevel { get; }
            internal double OutletFlow { get; }
            internal bool IsDry { get; }
            internal bool Dry => IsDry;
        }

        private readonly struct FloodCell : IComparable<FloodCell>
        {
            internal FloodCell(Int2 position, double height, double level)
            {
                Position = position;
                Height = height;
                Level = level;
            }

            internal Int2 Position { get; }
            internal double Height { get; }
            internal double Level { get; }

            public int CompareTo(FloodCell other)
            {
                int level = Level.CompareTo(other.Level);
                if (level != 0) return level;
                int x = Position.X.CompareTo(other.Position.X);
                return x != 0 ? x : Position.Y.CompareTo(other.Position.Y);
            }
        }

        #endregion
    }
}
