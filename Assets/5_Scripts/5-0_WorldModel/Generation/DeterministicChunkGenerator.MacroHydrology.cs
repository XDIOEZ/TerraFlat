using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 宏观水文图
        /// <summary>区域只缓存低分辨率汇水节点，具体水格由请求的 Chunk 单独细化。</summary>
        private sealed class MacroHydrologyRegion
        {
            public MacroHydrologyRegion(Int2[] positions, double[] heights, double[] flows,
                double[] terminals, int[] downstream, bool[] visible)
            {
                Positions = positions;
                Heights = heights;
                Flows = flows;
                Terminals = terminals;
                Downstream = downstream;
                Visible = visible;
            }

            public Int2[] Positions { get; }
            public double[] Heights { get; }
            public double[] Flows { get; }
            public double[] Terminals { get; }
            public int[] Downstream { get; }
            public bool[] Visible { get; }
            public int Count => Positions.Length;
        }

        private static MacroHydrologyRegion BuildMacroHydrologyRegion(
            ChunkGenerationRequest request, ChunkGenerationSettingsSnapshot settings,
            CancellationToken cancellationToken)
        {
            var sampling = new HydrologySamplingContext(request, settings);
            int cellSize = settings.RiverRunoffCellSize;
            int padding = settings.RiverMaxTraceSteps + cellSize +
                          settings.RiverMaxWidth + settings.RiverFloodplainMaxRadius;
            int anchorX = request.Topology.IsWrapped ? request.Topology.Min.X : 0;
            int anchorY = request.Topology.IsWrapped ? request.Topology.Min.Y : 0;
            int minX = FloorDiv(request.Address.ChunkOrigin.X - padding - anchorX, cellSize);
            int maxX = FloorDiv(request.Address.ChunkOrigin.X + request.Profile.Width - 1 + padding - anchorX,
                cellSize);
            int minY = FloorDiv(request.Address.ChunkOrigin.Y - padding - anchorY, cellSize);
            int maxY = FloorDiv(request.Address.ChunkOrigin.Y + request.Profile.Height - 1 + padding - anchorY,
                cellSize);
            var positions = new List<Int2>();
            var heights = new List<double>();
            var runoff = new List<double>();
            var byPosition = new Dictionary<Int2, int>();

            for (int gridY = minY; gridY <= maxY; gridY++)
            for (int gridX = minX; gridX <= maxX; gridX++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Int2 origin = sampling.Normalize(new Int2(
                    anchorX + gridX * cellSize, anchorY + gridY * cellSize));
                Int2 position = sampling.Normalize(new Int2(
                    origin.X + cellSize / 2, origin.Y + cellSize / 2));
                if (byPosition.ContainsKey(position)) continue;
                double height = sampling.Height(position);
                double source = 0d;
                if (height > settings.SeaLevel && ShouldTraceRunoffSource(
                        request.WorldSeed, origin, anchorX, anchorY, cellSize))
                {
                    double precipitation = sampling.Precipitation(position, height);
                    source = Clamp01((precipitation - settings.RiverInfiltrationFloor) /
                                     Math.Max(0.0001d, 1d - settings.RiverInfiltrationFloor));
                }
                byPosition.Add(position, positions.Count);
                positions.Add(position);
                heights.Add(height);
                runoff.Add(source);
            }

            int count = positions.Count;
            int[] downstream = new int[count];
            double[] flows = new double[count];
            double[] terminals = new double[count];
            bool[] visible = new bool[count];
            for (int i = 0; i < count; i++)
            {
                if ((i & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                downstream[i] = -1;
                if (heights[i] <= settings.SeaLevel) continue;
                double bestScore = 0d;
                Int2 position = positions[i];
                for (int direction = 0; direction < RiverFlowDirections.Length; direction++)
                {
                    Int2 offset = RiverFlowDirections[direction];
                    Int2 next = sampling.Normalize(new Int2(position.X + offset.X * cellSize,
                        position.Y + offset.Y * cellSize));
                    if (!byPosition.TryGetValue(next, out int nextIndex) || nextIndex == i)
                        continue;
                    double drop = heights[i] - heights[nextIndex];
                    if (drop <= DownhillEpsilon) continue;
                    double inverseDistance = offset.X != 0 && offset.Y != 0
                        ? 0.7071067811865476d : 1d;
                    double variation = 0.85d + Hash01(request.WorldSeed, next.X, next.Y,
                        0x94d049bbu) * 0.3d;
                    double score = drop * inverseDistance * variation;
                    if (score <= bestScore) continue;
                    bestScore = score;
                    downstream[i] = nextIndex;
                }
            }

            int maxSteps = Math.Max(1, (int)Math.Ceiling(
                settings.RiverMaxTraceSteps / (double)cellSize));
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
                    if (step == maxSteps || next < 0)
                    {
                        terminals[current] += contribution;
                        break;
                    }
                    current = next;
                }
            }

            int minimumCourseNodes = Math.Max(1, (int)Math.Ceiling(
                settings.RiverMinimumVisibleCourseLength / (double)cellSize));
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
            for (int i = 0; i < count; i++)
            {
                if (visible[i] || flows[i] < settings.RiverTributaryStartFlow) continue;
                int current = downstream[i];
                for (int step = 0; current >= 0 && step < maxSteps; step++)
                {
                    if (visible[current]) { visible[i] = true; break; }
                    current = downstream[current];
                }
            }
            return new MacroHydrologyRegion(positions.ToArray(), heights.ToArray(), flows,
                terminals, downstream, visible);
        }
        #endregion

        #region Chunk 水文走廊细化
        private static GeneratedHydrologyMap BuildMacroRiverChunk(
            ChunkGenerationRequest request, ChunkGenerationSettingsSnapshot settings,
            MacroHydrologyRegion region, CancellationToken cancellationToken)
        {
            var sampling = new HydrologySamplingContext(request, settings);
            var centers = new Dictionary<Int2, ChannelCenterSample>();
            int margin = settings.RiverMaxWidth + settings.RiverFloodplainMaxRadius +
                         settings.RiverRunoffCellSize / 3 + 3;
            int minX = request.Address.ChunkOrigin.X - margin;
            int minY = request.Address.ChunkOrigin.Y - margin;
            int maxX = request.Address.ChunkOrigin.X + request.Profile.Width + margin;
            int maxY = request.Address.ChunkOrigin.Y + request.Profile.Height + margin;
            int chunkCenterX = request.Address.ChunkOrigin.X + request.Profile.Width / 2;
            int chunkCenterY = request.Address.ChunkOrigin.Y + request.Profile.Height / 2;
            int[] primaryUpstream = ResolvePrimaryUpstream(region);

            for (int i = 0; i < region.Count; i++)
            {
                if ((i & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                if (!region.Visible[i] || region.Downstream[i] < 0) continue;
                int next = region.Downstream[i];
                Int2 source = region.Positions[i];
                Int2 target = region.Positions[next];
                int sourceX = ShiftNearChunk(source.X, chunkCenterX,
                    request.Topology.IsWrapped ? request.Topology.Span.X : 0);
                int sourceY = ShiftNearChunk(source.Y, chunkCenterY,
                    request.Topology.IsWrapped ? request.Topology.Span.Y : 0);
                int2 delta = request.Topology.ToDomain().ShortestDelta(
                    new int2(source.X, source.Y), new int2(target.X, target.Y));
                double endX = sourceX + delta.x;
                double endY = sourceY + delta.y;
                if (Math.Max(sourceX, endX) < minX || Math.Min(sourceX, endX) > maxX ||
                    Math.Max(sourceY, endY) < minY || Math.Min(sourceY, endY) > maxY)
                    continue;

                double length = Math.Sqrt(delta.x * (double)delta.x + delta.y * (double)delta.y);
                if (length <= 0.000001d) continue;
                ResolveChannelTangents(request, region, primaryUpstream, i, next,
                    delta.x, delta.y, length, out double startTangentX,
                    out double startTangentY, out double endTangentX,
                    out double endTangentY);
                double normalX = -delta.y / length;
                double normalY = delta.x / length;
                double amplitude = Math.Min(length * 0.28d,
                    Math.Min(settings.RiverMeanderScale * 0.42d,
                        Math.Max(3d, settings.RiverMaxWidth * 2.7d))) *
                    settings.RiverMeanderStrength;
                double bendA = SampleChannelBend(sampling, sourceX, sourceY,
                    endX, endY, 1d / 3d, amplitude);
                double bendB = SampleChannelBend(sampling, sourceX, sourceY,
                    endX, endY, 2d / 3d, amplitude);
                int steps = Math.Max(1, (int)Math.Ceiling(length / 0.28d));
                double previousX = sourceX;
                double previousY = sourceY;
                for (int step = 0; step <= steps; step++)
                {
                    double t = step / (double)steps;
                    double t2 = t * t;
                    double t3 = t2 * t;
                    double startWeight = 2d * t3 - 3d * t2 + 1d;
                    double startTangentWeight = t3 - 2d * t2 + t;
                    double endWeight = -2d * t3 + 3d * t2;
                    double endTangentWeight = t3 - t2;
                    double remaining = 1d - t;
                    double offset = 10d * t2 * remaining * remaining *
                                    (remaining * bendA + t * bendB);
                    double x = startWeight * sourceX + startTangentWeight * startTangentX +
                               endWeight * endX + endTangentWeight * endTangentX +
                               normalX * offset;
                    double y = startWeight * sourceY + startTangentWeight * startTangentY +
                               endWeight * endY + endTangentWeight * endTangentY +
                               normalY * offset;
                    if (x < minX || x > maxX || y < minY || y > maxY)
                    {
                        previousX = x;
                        previousY = y;
                        continue;
                    }
                    Int2 cell = sampling.Normalize(new Int2((int)Math.Floor(x), (int)Math.Floor(y)));
                    double flow = Lerp(region.Flows[i], region.Flows[next], t);
                    double directionX = step == 0 ? startTangentX : x - previousX;
                    double directionY = step == 0 ? startTangentY : y - previousY;
                    double directionLength = Math.Sqrt(directionX * directionX +
                                                       directionY * directionY);
                    if (directionLength > 0.000001d)
                    {
                        directionX /= directionLength;
                        directionY /= directionLength;
                    }
                    SetChannelCenterSample(centers, cell,
                        new ChannelCenterSample(flow, directionX, directionY));
                    previousX = x;
                    previousY = y;
                }
            }

            var riverCells = new Dictionary<Int2, GeneratedHydrologyCell>();
            var floodplainCells = new Dictionary<Int2, double>();
            int maximumRadius = Math.Max(0, (settings.RiverMaxWidth - 1) / 2);
            RenderMacroCenters(request, settings, sampling, centers, maximumRadius,
                riverCells, floodplainCells, cancellationToken);

            if (settings.RiverLakeChance > 0d)
            {
                var terminals = new Dictionary<Int2, double>();
                int lakeReach = settings.RiverMaxLakeCells + 2;
                for (int i = 0; i < region.Count; i++)
                {
                    if (region.Terminals[i] < settings.RiverLakeMinFlow) continue;
                    Int2 position = region.Positions[i];
                    int nearX = ShiftNearChunk(position.X, chunkCenterX,
                        request.Topology.IsWrapped ? request.Topology.Span.X : 0);
                    int nearY = ShiftNearChunk(position.Y, chunkCenterY,
                        request.Topology.IsWrapped ? request.Topology.Span.Y : 0);
                    if (nearX < minX - lakeReach || nearX > maxX + lakeReach ||
                        nearY < minY - lakeReach || nearY > maxY + lakeReach) continue;
                    terminals[position] = region.Terminals[i];
                }
                AddHeightDrivenTerminalLakes(request, settings, sampling, terminals,
                    riverCells, cancellationToken);
            }
            return new GeneratedHydrologyMap(riverCells, floodplainCells);
        }

        /// <summary>用汇水最多的上游支流确定主河槽在汇合点的切线。</summary>
        private static int[] ResolvePrimaryUpstream(MacroHydrologyRegion region)
        {
            var upstream = new int[region.Count];
            for (int i = 0; i < upstream.Length; i++) upstream[i] = -1;
            for (int i = 0; i < region.Count; i++)
            {
                int next = region.Downstream[i];
                if (!region.Visible[i] || next < 0) continue;
                int current = upstream[next];
                if (current < 0 || region.Flows[i] > region.Flows[current])
                    upstream[next] = i;
            }
            return upstream;
        }

        /// <summary>相邻河段共用主河槽方向，让宏观节点的转角自然过渡。</summary>
        private static void ResolveChannelTangents(ChunkGenerationRequest request,
            MacroHydrologyRegion region, int[] primaryUpstream, int source, int target,
            double deltaX, double deltaY, double length,
            out double startX, out double startY, out double endX, out double endY)
        {
            startX = deltaX;
            startY = deltaY;
            endX = deltaX;
            endY = deltaY;
            int previous = primaryUpstream[source];
            if (previous >= 0)
            {
                int2 incoming = request.Topology.ToDomain().ShortestDelta(
                    new int2(region.Positions[previous].X, region.Positions[previous].Y),
                    new int2(region.Positions[source].X, region.Positions[source].Y));
                SetChannelTangent(incoming.x + deltaX, incoming.y + deltaY,
                    deltaX, deltaY, length, out startX, out startY);
            }
            int following = region.Downstream[target];
            if (following >= 0 && region.Visible[target])
            {
                int2 outgoing = request.Topology.ToDomain().ShortestDelta(
                    new int2(region.Positions[target].X, region.Positions[target].Y),
                    new int2(region.Positions[following].X, region.Positions[following].Y));
                SetChannelTangent(deltaX + outgoing.x, deltaY + outgoing.y,
                    deltaX, deltaY, length, out endX, out endY);
            }
        }

        private static void SetChannelTangent(double candidateX, double candidateY,
            double deltaX, double deltaY, double length, out double x, out double y)
        {
            double candidateLength = Math.Sqrt(candidateX * candidateX +
                                               candidateY * candidateY);
            if (candidateLength <= 0.000001d ||
                candidateX * deltaX + candidateY * deltaY < candidateLength * length * 0.45d)
            {
                x = deltaX;
                y = deltaY;
                return;
            }
            x = candidateX / candidateLength * length;
            y = candidateY / candidateLength * length;
        }

        /// <summary>世界坐标上的低频偏移只采两次，河段两端仍固定在汇流节点。</summary>
        private static double SampleChannelBend(HydrologySamplingContext sampling,
            double sourceX, double sourceY, double targetX, double targetY,
            double t, double amplitude)
        {
            if (amplitude <= 0d) return 0d;
            Int2 probe = sampling.Normalize(new Int2(
                (int)Math.Floor(Lerp(sourceX, targetX, t)),
                (int)Math.Floor(Lerp(sourceY, targetY, t))));
            double bias = sampling.ChannelCenterBias(probe);
            return (bias < 0d ? -1d : 1d) *
                   amplitude * (0.75d + Math.Abs(bias) * 0.25d);
        }

        private static int ShiftNearChunk(int position, int chunkCenter, int span)
        {
            if (span <= 0) return position;
            int difference = position - chunkCenter;
            if (difference > span / 2) return position - span;
            if (difference < -span / 2) return position + span;
            return position;
        }

        private static void RenderMacroCenters(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, HydrologySamplingContext sampling,
            IReadOnlyDictionary<Int2, ChannelCenterSample> centers, int maximumRadius,
            Dictionary<Int2, GeneratedHydrologyCell> riverCells,
            Dictionary<Int2, double> floodplainCells, CancellationToken cancellationToken)
        {
            int rendered = 0;
            foreach (KeyValuePair<Int2, ChannelCenterSample> pair in centers)
            {
                if ((rendered++ & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                Int2 center = pair.Key;
                ChannelCenterSample sample = pair.Value;
                double widthT = InverseLerp(settings.RiverStartFlow,
                    Math.Max(settings.RiverStartFlow + 0.001d, settings.RiverFullWidthFlow),
                    sample.Flow);
                int width = 1 + (int)Math.Round(widthT * (settings.RiverMaxWidth - 1),
                    MidpointRounding.AwayFromZero);
                int radius = Math.Min(maximumRadius, Math.Max(0, width / 2));
                double centerDepth = Lerp(settings.RiverDepthMin, settings.RiverDepthMax,
                    Math.Sqrt(widthT));
                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (offsetX * offsetX + offsetY * offsetY > radius * radius + 1) continue;
                    Int2 water = sampling.Normalize(new Int2(center.X + offsetX,
                        center.Y + offsetY));
                    if (!ContainsChunk(request, water) || sampling.Height(water) <= settings.SeaLevel)
                        continue;
                    double distance = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                    double edgeStrength = radius == 0 ? 1d :
                        1d - Clamp01(distance / (radius + 0.5d));
                    double depth = Lerp(settings.RiverDepthMin, centerDepth, edgeStrength);
                    SetRiverCell(riverCells, water, new GeneratedHydrologyCell(
                        GeneratedHydrologyKind.River, sample.Flow, depth, 0d,
                        sample.DirectionX, sample.DirectionY));
                }
                AddFloodplain(request, settings, sampling, center, sample.Flow,
                    radius, floodplainCells);
            }
        }
        #endregion
    }
}
