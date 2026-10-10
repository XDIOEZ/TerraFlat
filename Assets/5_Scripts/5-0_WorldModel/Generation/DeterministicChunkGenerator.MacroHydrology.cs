using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region Chunk 水文走廊细化
        private static GeneratedHydrologyMap BuildMacroRiverChunk(
            ChunkGenerationRequest request, ChunkGenerationSettingsSnapshot settings,
            MacroHydrologyRegion region, CancellationToken cancellationToken)
        {
            var sampling = new HydrologySamplingContext(request, settings);
            var centers = new Dictionary<Int2, ChannelCenterSample>();
            int margin = settings.RiverMaxWidth + settings.RiverFloodplainMaxRadius +
                         settings.RiverRunoffCellSize / 3 + 3 + GetHydrologyOutputPadding(request);
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
                if (region.BasinByNode[i] != null) continue;
                int next = region.Downstream[i];
                Int2 source = ChannelPosition(region, i);
                Int2 target = ChannelPosition(region, next);
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
                double bendA = SampleChannelBend(sampling, source.X, source.Y,
                    source.X + delta.x, source.Y + delta.y, 1d / 3d, amplitude);
                double bendB = SampleChannelBend(sampling, source.X, source.Y,
                    source.X + delta.x, source.Y + delta.y, 2d / 3d, amplitude);
                bool bedEdgeDeposit = TryResolveBedEdgeDepositRange(
                    request, settings, source, target,
                    Math.Max(region.Outflows[i], region.Flows[next]),
                    out double bedEdgeDepositStart);
                int steps = Math.Max(1, (int)Math.Ceiling(length / 0.28d));
                double previousX = 0d;
                double previousY = 0d;
                for (int step = 0; step <= steps; step++)
                {
                    double t = step / (double)steps;
                    double t2 = t * t;
                    double t3 = t2 * t;
                    double startTangentWeight = t3 - 2d * t2 + t;
                    double endWeight = -2d * t3 + 3d * t2;
                    double endTangentWeight = t3 - t2;
                    double remaining = 1d - t;
                    double offset = 10d * t2 * remaining * remaining *
                                    (remaining * bendA + t * bendB);
                    // 在河段局部坐标计算曲线，环绕平移不会改变浮点取整后的水格。
                    double relativeX = startTangentWeight * startTangentX +
                               endWeight * delta.x + endTangentWeight * endTangentX +
                               normalX * offset;
                    double relativeY = startTangentWeight * startTangentY +
                               endWeight * delta.y + endTangentWeight * endTangentY +
                               normalY * offset;
                    double x = sourceX + relativeX;
                    double y = sourceY + relativeY;
                    if (x < minX || x > maxX || y < minY || y > maxY)
                    {
                        previousX = relativeX;
                        previousY = relativeY;
                        continue;
                    }
                    Int2 cell = sampling.Normalize(new Int2(source.X + (int)Math.Floor(relativeX), source.Y + (int)Math.Floor(relativeY)));
                    double flow = Lerp(region.Outflows[i], region.Flows[next], t);
                    double directionX = step == 0 ? startTangentX : relativeX - previousX;
                    double directionY = step == 0 ? startTangentY : relativeY - previousY;
                    double directionLength = Math.Sqrt(directionX * directionX +
                                                       directionY * directionY);
                    if (directionLength > 0.000001d)
                    {
                        directionX /= directionLength;
                        directionY /= directionLength;
                    }
                    SetChannelCenterSample(centers, cell,
                        new ChannelCenterSample(
                            flow,
                            directionX,
                            directionY,
                            bedEdgeDeposit && t >= bedEdgeDepositStart));
                    previousX = relativeX;
                    previousY = relativeY;
                }
            }
            RenderBasinOutletCenters(request, settings, region, sampling, centers, minX, minY, maxX, maxY);

            var riverCells = new Dictionary<Int2, GeneratedHydrologyCell>();
            var floodplainCells = new Dictionary<Int2, double>();
            int maximumRadius = Math.Max(0, (settings.RiverMaxWidth - 1) / 2);
            RenderMacroCenters(request, settings, sampling, centers, maximumRadius,
                riverCells, floodplainCells, cancellationToken);

            foreach (MacroBasin basin in region.Basins)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (basin.State.IsDry) continue;
                for (int i = 0; i < basin.Geometry.Count; i++)
                {
                    Int2 position = basin.Geometry.Cells[i];
                    double bottom = basin.Geometry.BottomHeights[i];
                    if (bottom >= basin.State.WaterLevel || !ContainsHydrologyOutput(request, position)) continue;
                    double depth = Clamp01((basin.State.WaterLevel - bottom) / Math.Max(0.000001d, settings.LakeMaxLevelRise));
                    if (riverCells.TryGetValue(position, out GeneratedHydrologyCell existing) &&
                        existing.Kind == GeneratedHydrologyKind.Lake && existing.SurfaceLevel >= basin.State.WaterLevel) continue;
                    // 静态湖面不携带河流方向，盆底高于水位的格子自然保持为干地。
                    riverCells[position] = new GeneratedHydrologyCell(GeneratedHydrologyKind.Lake, 0d, depth, basin.State.WaterLevel);
                }
            }
            return new GeneratedHydrologyMap(riverCells, floodplainCells);
        }

        private static Int2 ChannelPosition(MacroHydrologyRegion region, int index) =>
            region.BasinByNode[index]?.Geometry.Center ?? region.Positions[index];

        // 保留真实地格出口路径，粗格内的短出口河也不会因压缩而消失。
        private static void RenderBasinOutletCenters(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, MacroHydrologyRegion region,
            HydrologySamplingContext sampling, Dictionary<Int2, ChannelCenterSample> centers,
            int minX, int minY, int maxX, int maxY)
        {
            if (!settings.RiverEnabled) return;
            var lattice = new RunoffLattice(request, settings.RiverRunoffCellSize);
            double area = lattice.StepX * lattice.StepY;
            var domain = request.Topology.ToDomain();
            foreach (MacroBasin basin in region.Basins)
            {
                double flow = basin.State.OutletFlow / area;
                if (!basin.Geometry.HasOutlet || flow < settings.RiverStartFlow) continue;
                var path = new List<Int2>(basin.Geometry.OutletPath);
                int next = region.Downstream[basin.Node];
                if (next >= 0) path.Add(ChannelPosition(region, next));
                if (path.Count == 1) path.Add(path[0]);
                for (int segment = 0; segment + 1 < path.Count; segment++)
                {
                    Int2 from = path[segment], to = path[segment + 1];
                    int x = ShiftNearChunk(from.X, request.Address.ChunkOrigin.X + request.Profile.Width / 2,
                        request.Topology.IsWrapped ? request.Topology.Span.X : 0);
                    int y = ShiftNearChunk(from.Y, request.Address.ChunkOrigin.Y + request.Profile.Height / 2,
                        request.Topology.IsWrapped ? request.Topology.Span.Y : 0);
                    int2 delta = domain.ShortestDelta(new int2(from.X, from.Y), new int2(to.X, to.Y));
                    double length = Math.Sqrt(delta.x * (double)delta.x + delta.y * (double)delta.y);
                    int steps = Math.Max(1, (int)Math.Ceiling(length / 0.4d));
                    for (int step = 0; step <= steps; step++)
                    {
                        double t = step / (double)steps;
                        double px = x + delta.x * t, py = y + delta.y * t;
                        if (px < minX || px > maxX || py < minY || py > maxY) continue;
                        Int2 cell = sampling.Normalize(new Int2(from.X + (int)Math.Floor(delta.x * t), from.Y + (int)Math.Floor(delta.y * t)));
                        SetChannelCenterSample(centers, cell, new ChannelCenterSample(flow,
                            length > 0d ? delta.x / length : 0d, length > 0d ? delta.y / length : 0d, false));
                    }
                }
            }
        }

        /// <summary>少量偏下游宏观河段会得到一小段沉积带，出现率和长度都走确定性二次峰值分布。</summary>
        private static bool TryResolveBedEdgeDepositRange(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            Int2 source,
            Int2 target,
            double downstreamFlow,
            out double startT)
        {
            startT = 1d;
            if (downstreamFlow < settings.RiverBedEdgeDepositMinimumFlow)
                return false;

            double propensity = SampleQuadraticRiverValue(
                request.WorldSeed,
                source,
                target,
                0x51ed270bu,
                0d,
                0.12d,
                1d,
                0.9d);
            if (propensity < settings.RiverBedEdgeDepositActivation)
                return false;

            double lengthRadius = Math.Max(
                0.02d,
                (settings.RiverBedEdgeDepositPeakFraction -
                 settings.RiverBedEdgeDepositMinFraction) * 1.5d);
            double fraction = SampleQuadraticRiverValue(
                request.WorldSeed,
                source,
                target,
                0x94d049bdu,
                settings.RiverBedEdgeDepositMinFraction,
                settings.RiverBedEdgeDepositPeakFraction,
                settings.RiverBedEdgeDepositMaxFraction,
                lengthRadius);
            startT = Clamp01(1d - fraction);
            return true;
        }

        /// <summary>按抛物线权重抽样，峰值最常见，远端只保留很小长尾。</summary>
        private static double SampleQuadraticRiverValue(
            int worldSeed,
            Int2 source,
            Int2 target,
            uint salt,
            double minimum,
            double peak,
            double maximum,
            double quadraticRadius)
        {
            const int bucketCount = 32;
            const double tailWeight = 0.02d;
            if (maximum <= minimum + 0.000001d)
                return minimum;

            double radius = Math.Max(0.0001d, quadraticRadius);
            double totalWeight = 0d;
            for (int bucket = 0; bucket <= bucketCount; bucket++)
            {
                double candidate = Lerp(minimum, maximum, bucket / (double)bucketCount);
                double normalized = (candidate - peak) / radius;
                totalWeight += Math.Max(tailWeight, 1d - normalized * normalized);
            }

            uint segmentSalt = unchecked(
                salt ^
                (uint)target.X * 0x9e3779b9u ^
                (uint)target.Y * 0x85ebca6bu);
            double targetWeight =
                Hash01(worldSeed, source.X, source.Y, segmentSalt) * totalWeight;
            double accumulated = 0d;
            for (int bucket = 0; bucket <= bucketCount; bucket++)
            {
                double candidate = Lerp(minimum, maximum, bucket / (double)bucketCount);
                double normalized = (candidate - peak) / radius;
                accumulated += Math.Max(tailWeight, 1d - normalized * normalized);
                if (targetWeight <= accumulated)
                    return candidate;
            }
            return maximum;
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
                if (current < 0 || region.Outflows[i] > region.Outflows[current] ||
                    region.Outflows[i] == region.Outflows[current] && CompareWorldPosition(region.Positions[i], region.Positions[current]) < 0)
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
                    new int2(ChannelPosition(region, previous).X, ChannelPosition(region, previous).Y),
                    new int2(ChannelPosition(region, source).X, ChannelPosition(region, source).Y));
                SetChannelTangent(incoming.x + deltaX, incoming.y + deltaY,
                    deltaX, deltaY, length, out startX, out startY);
            }
            int following = region.Downstream[target];
            if (following >= 0 && region.Visible[target])
            {
                int2 outgoing = request.Topology.ToDomain().ShortestDelta(
                    new int2(ChannelPosition(region, target).X, ChannelPosition(region, target).Y),
                    new int2(ChannelPosition(region, following).X, ChannelPosition(region, following).Y));
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
                    if (!ContainsHydrologyOutput(request, water) || sampling.Height(water) <= settings.SeaLevel)
                        continue;
                    double distance = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                    double edgeStrength = radius == 0 ? 1d :
                        1d - Clamp01(distance / (radius + 0.5d));
                    double depth = Lerp(settings.RiverDepthMin, centerDepth, edgeStrength);
                    SetRiverCell(riverCells, water, new GeneratedHydrologyCell(
                        GeneratedHydrologyKind.River, sample.Flow, depth, sampling.Height(water) + depth * settings.LakeMaxLevelRise,
                        sample.DirectionX, sample.DirectionY, edgeStrength,
                        sample.BedEdgeDeposit));
                }
                AddFloodplain(request, settings, sampling, center, sample.Flow,
                    radius, floodplainCells, GetHydrologyOutputPadding(request));
            }
        }

        private static int GetHydrologyOutputPadding(ChunkGenerationRequest request) =>
            (int)Math.Ceiling(AirHumiditySettings.FromProfile(request.Profile).RadiusTiles);

        private static bool ContainsHydrologyOutput(ChunkGenerationRequest request, Int2 position)
        {
            int padding = GetHydrologyOutputPadding(request);
            int x = ShiftNearChunk(position.X, request.Address.ChunkOrigin.X + request.Profile.Width / 2,
                request.Topology.IsWrapped ? request.Topology.Span.X : 0) - request.Address.ChunkOrigin.X;
            int y = ShiftNearChunk(position.Y, request.Address.ChunkOrigin.Y + request.Profile.Height / 2,
                request.Topology.IsWrapped ? request.Topology.Span.Y : 0) - request.Address.ChunkOrigin.Y;
            return x >= -padding && y >= -padding && x < request.Profile.Width + padding && y < request.Profile.Height + padding;
        }
        #endregion
    }
}
