using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 固定世界格网与稀疏河源

        private sealed class MacroHydrologyRegion
        {
            public Int2[] Positions;
            public double[] Heights;
            public double[] Flows;
            public double[] Terminals;
            public int[] Downstream;
            public bool[] Visible;
            public int[] PrimaryUpstream;
            public int Count => Positions.Length;
        }

        // 环世界按完整跨度等分格网，避免跨度不能整除格宽时在接缝重复或遗漏节点。
        private readonly struct RunoffLattice
        {
            private readonly ChunkGenerationTopologySnapshot topology;
            public readonly double StepX, StepY;
            public readonly int AnchorX, AnchorY, CountX, CountY;
            public RunoffLattice(ChunkGenerationRequest request, int stride)
            {
                topology = request.Topology;
                CountX = topology.IsWrapped ? Math.Max(1, topology.Span.X / stride) : 0;
                CountY = topology.IsWrapped ? Math.Max(1, topology.Span.Y / stride) : 0;
                StepX = CountX > 0 ? topology.Span.X / (double)CountX : stride;
                StepY = CountY > 0 ? topology.Span.Y / (double)CountY : stride;
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

            public Int2 Grid(Int2 position) => new(
                (int)Math.Floor((topology.NormalizeX(position.X) - (double)AnchorX) / StepX),
                (int)Math.Floor((topology.NormalizeY(position.Y) - (double)AnchorY) / StepY));

            public int Cost(Int2 from, Int2 to)
            {
                int2 delta = topology.ToDomain().ShortestDelta(new int2(from.X, from.Y), new int2(to.X, to.Y));
                return Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(delta.x) / StepX, Math.Abs(delta.y) / StepY) - 0.000001d));
            }
        }

        #endregion

        #region 稀疏河源与下坡汇流

        // 恢复旧版每组一个河源，海洋距离降水只计算河源水量，不参与湖盆蒸发闭环。
        private static MacroHydrologyRegion BuildMacroHydrologyRegion(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, CancellationToken cancellationToken,
            SharedHydrologyHeightSamplingCache sharedHeightSamples)
        {
            var sampling = new HydrologySamplingContext(request, settings, sharedHeightSamples);
            var lattice = new RunoffLattice(request, settings.RiverRunoffCellSize);
            int maxSteps = Math.Max(1, (int)Math.Ceiling(
                settings.RiverMaxTraceSteps / Math.Min(lattice.StepX, lattice.StepY)));
            int padding = (int)Math.Ceiling(maxSteps * 2d * Math.Max(lattice.StepX, lattice.StepY) +
                Math.Max(lattice.StepX, lattice.StepY)) + settings.RiverMaxWidth +
                settings.RiverFloodplainMaxRadius + GetHydrologyOutputPadding(request);
            int minX = (int)Math.Floor((request.Address.ChunkOrigin.X - padding - (double)lattice.AnchorX) / lattice.StepX);
            int minY = (int)Math.Floor((request.Address.ChunkOrigin.Y - padding - (double)lattice.AnchorY) / lattice.StepY);
            int maxX = (int)Math.Floor((request.Address.ChunkOrigin.X + request.Profile.Width - 1 + padding - (double)lattice.AnchorX) / lattice.StepX);
            int maxY = (int)Math.Floor((request.Address.ChunkOrigin.Y + request.Profile.Height - 1 + padding - (double)lattice.AnchorY) / lattice.StepY);
            var positions = new List<Int2>();
            var byPosition = new Dictionary<Int2, int>();
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Int2 position = lattice.Position(x, y);
                if (byPosition.ContainsKey(position)) continue;
                byPosition.Add(position, positions.Count);
                positions.Add(position);
            }
            // 固定坐标排序保证环绕接缝两侧按同一顺序累加上游流量。
            positions.Sort(CompareWorldPosition);
            byPosition.Clear();
            int count = positions.Count;
            var heights = new double[count];
            var runoff = new double[count];
            var downstream = new int[count];
            var flows = new double[count];
            var terminals = new double[count];
            var visible = new bool[count];
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Int2 position = positions[i];
                byPosition.Add(position, i);
                heights[i] = sampling.Height(position);
                if (settings.RiverEnabled && heights[i] > settings.SeaLevel &&
                    ShouldTraceRunoffSource(request.WorldSeed, lattice.Grid(position)) &&
                    ShouldGeneratePolarRiverSource(request, settings, position))
                {
                    double precipitation = sampling.Precipitation(position);
                    runoff[i] = Clamp01((precipitation - settings.RiverInfiltrationFloor) /
                        Math.Max(0.0001d, 1d - settings.RiverInfiltrationFloor));
                }
            }
            for (int i = 0; i < count; i++)
            {
                if ((i & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                downstream[i] = -1;
                if (heights[i] <= settings.SeaLevel) continue;
                Int2 grid = lattice.Grid(positions[i]);
                double bestScore = 0d;
                Int2 receiver = positions[i];
                foreach (Int2 offset in RiverFlowDirections)
                {
                    Int2 next = lattice.Position(grid.X + offset.X, grid.Y + offset.Y);
                    if (next == positions[i]) continue;
                    double drop = heights[i] - sampling.Height(next);
                    if (drop <= DownhillEpsilon) continue;
                    double variation = 0.85d + Hash01(request.WorldSeed, next.X, next.Y,
                        0x94d049bbu) * 0.3d;
                    double score = drop * (offset.X != 0 && offset.Y != 0 ? 0.7071067811865476d : 1d) * variation;
                    if (score > bestScore || score == bestScore && CompareWorldPosition(next, receiver) < 0)
                    {
                        bestScore = score;
                        receiver = next;
                    }
                }
                // 窗口外的更低接收点只是没有缓存，不能被当成天然终点扩湖。
                if (bestScore > 0d)
                    downstream[i] = byPosition.TryGetValue(receiver, out int nextIndex) ? nextIndex : -2;
            }
            for (int source = 0; source < count; source++)
            {
                if ((source & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                double contribution = runoff[source];
                if (contribution <= 0.0001d) continue;
                int current = source;
                for (int step = 0; step <= maxSteps && current >= 0; step++)
                {
                    if (heights[current] <= settings.SeaLevel) break;
                    flows[current] += contribution;
                    int next = downstream[current];
                    if (next == -1) terminals[current] += contribution;
                    if (step == maxSteps || next < 0) break;
                    current = next;
                }
            }
            int minimumCourseNodes = Math.Max(1, (int)Math.Ceiling(
                settings.RiverMinimumVisibleCourseLength / Math.Min(lattice.StepX, lattice.StepY)));
            for (int i = 0; i < count; i++)
            {
                if (flows[i] < settings.RiverStartFlow) continue;
                int current = i;
                int length = 0;
                while (current >= 0 && length < minimumCourseNodes)
                {
                    current = downstream[current];
                    length++;
                }
                visible[i] = length >= minimumCourseNodes && current >= 0;
            }
            // 成熟主河延续到真实终点，河口最后一段不能再被最短河程门槛隐藏。
            bool[] mainVisible = (bool[])visible.Clone();
            for (int i = 0; i < count; i++)
            {
                if (!mainVisible[i]) continue;
                int current = downstream[i];
                for (int step = 0; current >= 0 && step <= maxSteps; step++)
                {
                    if (flows[current] <= 0.0001d) break;
                    visible[current] = heights[current] > settings.SeaLevel && downstream[current] >= 0;
                    current = downstream[current];
                }
            }
            for (int i = 0; i < count; i++)
            {
                if (visible[i] || flows[i] < settings.RiverTributaryStartFlow) continue;
                int current = downstream[i];
                for (int step = 0; current >= 0 && step < maxSteps; step++)
                {
                    // 支流只接入原始成熟主河，补显节点不能反复传播并重置距离预算。
                    if (mainVisible[current]) { visible[i] = true; break; }
                    current = downstream[current];
                }
            }
            var region = new MacroHydrologyRegion
            {
                Positions = positions.ToArray(), Heights = heights, Flows = flows,
                Terminals = terminals, Downstream = downstream, Visible = visible
            };
            // 区域发布后不再改变主支流关系，所有区块复用同一份确定性选择。
            region.PrimaryUpstream = ResolvePrimaryUpstream(region);
            sampling.PublishCompletedHeightSamples(cancellationToken);
            ChunkGenerationTiming timing = ChunkGenerationTiming.CurrentOnThread;
            timing?.RecordStageCount("river.macro.height_computes", sampling.HeightMissCount, accumulate: true);
            timing?.RecordStageCount("river.macro.height_shared_hits", sampling.HeightSharedHitCount, accumulate: true);
            timing?.RecordStageCount("river.macro.height_local_hits", sampling.HeightHitCount, accumulate: true);
            return region;
        }

        private static int CompareWorldPosition(Int2 a, Int2 b)
        {
            int x = a.X.CompareTo(b.X);
            return x != 0 ? x : a.Y.CompareTo(b.Y);
        }

        #endregion
    }
}
