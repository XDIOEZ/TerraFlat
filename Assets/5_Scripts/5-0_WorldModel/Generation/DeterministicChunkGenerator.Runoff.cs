using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 固定世界格网与湖盆节点

        private sealed class MacroBasin
        {
            public LakeBasinKernel.Basin Geometry;
            public LakeBasinKernel.State State;
            public int Node;
            public double Precipitation;
            public double Celsius;
            public readonly List<int> SinkNodes = new();
        }

        private sealed class MacroHydrologyRegion
        {
            public Int2[] Positions;
            public double[] Heights;
            public double[] Flows;
            public double[] Outflows;
            public int[] Downstream;
            public bool[] Visible;
            public MacroBasin[] Basins;
            public MacroBasin[] BasinByNode;
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

        #region 全陆地径流与固定范围累积

        private static MacroHydrologyRegion BuildMacroHydrologyRegion(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, CancellationToken cancellationToken)
        {
            var sampling = new HydrologySamplingContext(request, settings);
            var lattice = new RunoffLattice(request, settings.RiverRunoffCellSize);
            int budget = Math.Min(64, Math.Max(1, (int)Math.Ceiling(settings.RiverMaxTraceSteps / Math.Min(lattice.StepX, lattice.StepY))));
            // 外圈同时覆盖最长天然出口、出口盆的直接竞争证据及上游预算，不能只覆盖湖面半径。
            int padding = (int)Math.Ceiling(budget * Math.Max(lattice.StepX, lattice.StepY) +
                settings.LakeRareMaxRadius * 8d + Math.Max(lattice.StepX, lattice.StepY) * 4d +
                AirHumiditySettings.FromProfile(request.Profile).RadiusTiles + settings.RiverMaxWidth + settings.RiverFloodplainMaxRadius);
            int minX = (int)Math.Floor((request.Address.ChunkOrigin.X - padding - (double)lattice.AnchorX) / lattice.StepX);
            int minY = (int)Math.Floor((request.Address.ChunkOrigin.Y - padding - (double)lattice.AnchorY) / lattice.StepY);
            int maxX = (int)Math.Floor((request.Address.ChunkOrigin.X + request.Profile.Width - 1 + padding - (double)lattice.AnchorX) / lattice.StepX);
            int maxY = (int)Math.Floor((request.Address.ChunkOrigin.Y + request.Profile.Height - 1 + padding - (double)lattice.AnchorY) / lattice.StepY);
            var positions = new List<Int2>();
            var heights = new List<double>();
            var byPosition = new Dictionary<Int2, int>();
            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Int2 position = lattice.Position(x, y);
                if (byPosition.ContainsKey(position)) continue;
                byPosition.Add(position, positions.Count);
                positions.Add(position);
                heights.Add(sampling.Height(position));
            }
            positions.Sort(CompareWorldPosition);
            heights.Clear();
            byPosition.Clear();
            foreach (Int2 position in positions)
            {
                byPosition.Add(position, heights.Count);
                heights.Add(sampling.Height(position));
            }

            int count = positions.Count;
            var downstream = new int[count];
            var sinks = new List<Int2>();
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
                    double score = drop * (offset.X != 0 && offset.Y != 0 ? 0.7071067811865476d : 1d);
                    if (score > bestScore || score == bestScore && CompareWorldPosition(next, receiver) < 0)
                    {
                        bestScore = score;
                        receiver = next;
                    }
                }
                // 完整八邻域决定流向；窗口外的接收点只是未缓存，不能冒充天然低洼终点。
                if (bestScore > 0d)
                    downstream[i] = byPosition.TryGetValue(receiver, out int receiverIndex) ? receiverIndex : -2;
                else if (settings.LakeBasinEnabled)
                    sinks.Add(positions[i]);
            }

            var basins = new List<MacroBasin>();
            var basinCenters = new Dictionary<Int2, MacroBasin>();
            foreach (Int2 sink in sinks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LakeBasinKernel.Basin basin = LakeBasinKernel.Build(request, settings, sink, sampling.Height, cancellationToken);
                if (basin == null) continue;
                if (basinCenters.TryGetValue(basin.Center, out MacroBasin existingBasin))
                {
                    existingBasin.SinkNodes.Add(byPosition[sink]);
                    continue;
                }
                Int2 grid = lattice.Grid(basin.Center);
                if (!byPosition.TryGetValue(lattice.Position(grid.X, grid.Y), out int node)) continue;
                var candidate = new MacroBasin { Geometry = basin, Node = node };
                candidate.SinkNodes.Add(byPosition[sink]);
                basins.Add(candidate);
                basinCenters.Add(basin.Center, candidate);
            }
            basins.Sort((a, b) =>
            {
                int height = a.Geometry.MinimumHeight.CompareTo(b.Geometry.MinimumHeight);
                return height != 0 ? height : CompareWorldPosition(a.Geometry.Center, b.Geometry.Center);
            });
            var basinByNode = new MacroBasin[count];
            var candidateByNode = new MacroBasin[count];
            var candidateByTile = new Dictionary<Int2, MacroBasin>();
            var blockedBasins = new HashSet<MacroBasin>();
            // 原始候选直接竞争固定邻域，被抑制的候选也保留声明，避免接受顺序沿重叠链传播。
            foreach (MacroBasin basin in basins)
            {
                ClaimBasinNode(basin, basin.Node);
                foreach (int sink in basin.SinkNodes) ClaimBasinNode(basin, sink);
                foreach (Int2 tile in basin.Geometry.Cells)
                {
                    if (candidateByTile.TryGetValue(tile, out MacroBasin owner))
                    {
                        if (!ReferenceEquals(owner, basin)) blockedBasins.Add(basin);
                    }
                    else candidateByTile.Add(tile, basin);
                    Int2 grid = lattice.Grid(tile);
                    if (byPosition.TryGetValue(lattice.Position(grid.X, grid.Y), out int member) &&
                        basin.Geometry.TryGetBottomHeight(positions[member], out _))
                        ClaimBasinNode(basin, member);
                }
            }

            void ClaimBasinNode(MacroBasin basin, int node)
            {
                MacroBasin owner = candidateByNode[node];
                if (owner == null) candidateByNode[node] = basin;
                else if (!ReferenceEquals(owner, basin)) blockedBasins.Add(basin);
            }

            var basinTileCounts = new int[count];
            var countedBasinTiles = new HashSet<Int2>();
            var accepted = new List<MacroBasin>();
            foreach (MacroBasin basin in basins)
            {
                if (blockedBasins.Contains(basin)) continue;
                basinByNode[basin.Node] = basin;
                accepted.Add(basin);
                foreach (int sink in basin.SinkNodes)
                {
                    if (sink == basin.Node) continue;
                    downstream[sink] = basin.Node;
                }
                foreach (Int2 tile in basin.Geometry.Cells)
                {
                    Int2 grid = lattice.Grid(tile);
                    if (!byPosition.TryGetValue(lattice.Position(grid.X, grid.Y), out int member)) continue;
                    if (countedBasinTiles.Add(tile)) basinTileCounts[member]++;
                    if (member == basin.Node ||
                        !basin.Geometry.TryGetBottomHeight(positions[member], out _)) continue;
                    downstream[member] = basin.Node;
                }
                downstream[basin.Node] = -1;
                if (basin.Geometry.HasOutlet)
                {
                    Int2 grid = lattice.Grid(basin.Geometry.OutletEndpoint);
                    Int2 receiver = default;
                    double bestDistance = double.MaxValue;
                    bool found = false;
                    for (int y = -1; y <= 1; y++)
                    for (int x = -1; x <= 1; x++)
                    {
                        Int2 candidate = lattice.Position(grid.X + x, grid.Y + y);
                        if (sampling.Height(candidate) >= basin.Geometry.MinimumHeight - DownhillEpsilon ||
                            basin.Geometry.TryGetBottomHeight(candidate, out _)) continue;
                        int2 delta = request.Topology.ToDomain().ShortestDelta(
                            new int2(candidate.X, candidate.Y), new int2(basin.Geometry.OutletEndpoint.X, basin.Geometry.OutletEndpoint.Y));
                        double distance = delta.x * (double)delta.x + delta.y * (double)delta.y;
                        if (!found || distance < bestDistance || distance == bestDistance && CompareWorldPosition(candidate, receiver) < 0)
                        {
                            found = true;
                            bestDistance = distance;
                            receiver = candidate;
                        }
                    }
                    // 宏网只接更低的接收节点，避免粗格压缩把天然外流重新接回本盆。
                    if (found && receiver != positions[basin.Node])
                        downstream[basin.Node] = byPosition.TryGetValue(receiver, out int outlet) ? outlet : -2;
                }
            }

            double area = lattice.StepX * lattice.StepY;
            var runoff = new double[count];
            for (int i = 0; i < count; i++)
            {
                if ((i & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (heights[i] <= settings.SeaLevel) continue;
                double precipitation = sampling.Precipitation(positions[i]);
                runoff[i] = Math.Max(0d, precipitation - settings.RiverInfiltrationFloor) /
                    Math.Max(0.0001d, 1d - settings.RiverInfiltrationFloor) * settings.RiverRunoffScale;
                runoff[i] *= Math.Max(0d, 1d - basinTileCounts[i] / area);
            }
            foreach (MacroBasin basin in accepted)
            {
                double precipitation = 0d;
                foreach (Int2 cell in basin.Geometry.Cells) precipitation += sampling.Precipitation(cell);
                basin.Precipitation = precipitation / Math.Max(1, basin.Geometry.Count);
                basin.Celsius = SampleGeographicClimate(request, settings, basin.Geometry.Center.X, basin.Geometry.Center.Y).TemperatureCelsius;
            }

            var costs = new int[count];
            for (int i = 0; i < count; i++)
                costs[i] = downstream[i] >= 0 ? lattice.Cost(positions[i], positions[downstream[i]]) : 1;
            var transferred = new double[budget + 1][];
            double[] flows = null;
            // 参考 Azgaar 全陆地产流；按固定传播预算求 F=本地径流+上游出流，不用窗口边缘造终点。
            for (int distance = 0; distance <= budget; distance++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                flows = (double[])runoff.Clone();
                for (int source = 0; source < count; source++)
                {
                    int target = downstream[source];
                    int previous = distance - costs[source];
                    if (target >= 0 && previous >= 0) flows[target] += transferred[previous][source];
                }
                var next = new double[count];
                for (int i = 0; i < count; i++)
                {
                    MacroBasin basin = basinByNode[i];
                    if (basin == null) { next[i] = flows[i]; continue; }
                    basin.State = LakeBasinKernel.ResolveState(basin.Geometry, flows[i] * area,
                        basin.Precipitation, basin.Celsius, settings);
                    next[i] = downstream[i] >= 0 ? basin.State.OutletFlow / area : 0d;
                }
                transferred[distance] = next;
            }
            var visible = new bool[count];
            for (int i = 0; i < count; i++)
                visible[i] = settings.RiverEnabled && transferred[budget][i] >= settings.RiverStartFlow && downstream[i] >= 0;
            return new MacroHydrologyRegion
            {
                Positions = positions.ToArray(), Heights = heights.ToArray(), Flows = flows,
                Outflows = transferred[budget], Downstream = downstream, Visible = visible,
                Basins = accepted.ToArray(), BasinByNode = basinByNode
            };
        }

        private static int CompareWorldPosition(Int2 a, Int2 b)
        {
            int x = a.X.CompareTo(b.X);
            return x != 0 ? x : a.Y.CompareTo(b.Y);
        }

        #endregion
    }
}
