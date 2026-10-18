using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Mathematics;
using Unity.Profiling;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region Chunk 水文走廊细化

        private static readonly ProfilerMarker RiverLocalRefineMarker = new("FlatWorld.Generation.River.LocalRefine");
        private static readonly ProfilerMarker RiverCenterlineMarker = new("FlatWorld.Generation.River.Centerline");
        private static readonly ProfilerMarker RiverRenderMarker = new("FlatWorld.Generation.River.Render");
        private static readonly ProfilerMarker RiverTerminalLakesMarker = new("FlatWorld.Generation.River.TerminalLakes");

        private static GeneratedHydrologyMap BuildMacroRiverChunk(
            ChunkGenerationRequest request, ChunkGenerationSettingsSnapshot settings,
            MacroHydrologyRegion region, CancellationToken cancellationToken)
        {
            using var refineScope = RiverLocalRefineMarker.Auto();
            ChunkGenerationTiming timing = ChunkGenerationTiming.CurrentOnThread;
            timing?.RecordStageCount("river.local_refine.invocations", 1, accumulate: true);
            var sampling = new HydrologySamplingContext(request, settings);
            var centers = new Dictionary<Int2, ChannelCenterSample>();
            var outputWindow = new HydrologyOutputWindow(request);
            int margin = settings.RiverMaxWidth + settings.RiverFloodplainMaxRadius +
                         settings.RiverRunoffCellSize / 3 + 3 + outputWindow.Padding;
            int minX = request.Address.ChunkOrigin.X - margin;
            int minY = request.Address.ChunkOrigin.Y - margin;
            int maxX = request.Address.ChunkOrigin.X + request.Profile.Width + margin;
            int maxY = request.Address.ChunkOrigin.Y + request.Profile.Height + margin;
            int chunkCenterX = request.Address.ChunkOrigin.X + request.Profile.Width / 2;
            int chunkCenterY = request.Address.ChunkOrigin.Y + request.Profile.Height / 2;
            int[] primaryUpstream = region.PrimaryUpstream;
            WorldTopologyDomain domain = request.Topology.ToDomain();
            long segments = 0, curveSamples = 0, acceptedCurveSamples = 0;

            using (RiverCenterlineMarker.Auto())
            using (timing?.MeasureStage("river.local_refine.centerline", accumulate: true) ?? default)
            {
                for (int i = 0; i < region.Count; i++)
                {
                    if ((i & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (!region.Visible[i] || region.Downstream[i] < 0) continue;
                    int next = region.Downstream[i];
                    Int2 source = ChannelPosition(region, i);
                    Int2 target = ChannelPosition(region, next);
                    int sourceX = ShiftNearChunk(source.X, chunkCenterX,
                        request.Topology.IsWrapped ? request.Topology.Span.X : 0);
                    int sourceY = ShiftNearChunk(source.Y, chunkCenterY,
                        request.Topology.IsWrapped ? request.Topology.Span.Y : 0);
                    int2 delta = domain.ShortestDelta(
                        new int2(source.X, source.Y), new int2(target.X, target.Y));
                    double endX = sourceX + delta.x;
                    double endY = sourceY + delta.y;
                    if (Math.Max(sourceX, endX) < minX || Math.Min(sourceX, endX) > maxX ||
                        Math.Max(sourceY, endY) < minY || Math.Min(sourceY, endY) > maxY)
                        continue;

                    double length = Math.Sqrt(delta.x * (double)delta.x + delta.y * (double)delta.y);
                    if (length <= 0.000001d) continue;
                    if (timing != null) segments++;
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
                        Math.Max(region.Flows[i], region.Flows[next]),
                        out double bedEdgeDepositStart);
                    int steps = Math.Max(1, (int)Math.Ceiling(length / 0.28d));
                    if (timing != null) curveSamples += (long)steps + 1;
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
                        if (timing != null) acceptedCurveSamples++;
                        // 汇合处的流量缓入缓出，主支流河宽不会在节点突然变速。
                        double flow = Lerp(region.Flows[i], region.Flows[next], Smooth(t));
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
            }
            timing?.RecordStageCount("river.local_refine.region_nodes", region.Count, accumulate: true);
            timing?.RecordStageCount("river.local_refine.output_padding_last", outputWindow.Padding);
            timing?.RecordStageCount("river.local_refine.segments", segments, accumulate: true);
            timing?.RecordStageCount("river.local_refine.curve_samples", curveSamples, accumulate: true);
            timing?.RecordStageCount("river.local_refine.accepted_curve_samples", acceptedCurveSamples, accumulate: true);
            timing?.RecordStageCount("river.local_refine.unique_centers", centers.Count, accumulate: true);

            var riverCells = new Dictionary<Int2, GeneratedHydrologyCell>();
            var floodplainCells = new Dictionary<Int2, double>();
            int maximumRadius = Math.Max(0, (settings.RiverMaxWidth - 1) / 2);
            using (RiverRenderMarker.Auto())
            using (timing?.MeasureStage("river.local_refine.render", accumulate: true) ?? default)
                RenderMacroCenters(settings, sampling, outputWindow, centers, maximumRadius,
                    riverCells, floodplainCells, cancellationToken, timing);

            using (RiverTerminalLakesMarker.Auto())
            using (timing?.MeasureStage("river.local_refine.terminal_lakes", accumulate: true) ?? default)
            {
                timing?.RecordStageCount("river.local_refine.terminal_candidates", 0, accumulate: true);
                if (settings.RiverLakeChance > 0d)
                {
                    var terminals = new Dictionary<Int2, double>();
                    for (int i = 0; i < region.Count; i++)
                    {
                        if (region.Terminals[i] >= settings.RiverLakeMinFlow)
                            terminals[region.Positions[i]] = region.Terminals[i];
                    }
                    AddHeightDrivenTerminalLakes(request, settings, sampling, outputWindow, terminals,
                        riverCells, cancellationToken);
                    timing?.RecordStageCount("river.local_refine.terminal_candidates", terminals.Count, accumulate: true);
                }
            }
            timing?.RecordStageCount("river.local_refine.water_cells", riverCells.Count, accumulate: true);
            timing?.RecordStageCount("river.local_refine.floodplain_cells", floodplainCells.Count, accumulate: true);
            timing?.RecordStageCount("river.local_refine.height_cache_hits", sampling.HeightHitCount, accumulate: true);
            timing?.RecordStageCount("river.local_refine.height_cache_misses", sampling.HeightMissCount, accumulate: true);
            timing?.RecordStageCount("river.local_refine.bend_cache_hits", sampling.ChannelCenterBiasHitCount, accumulate: true);
            timing?.RecordStageCount("river.local_refine.bend_cache_misses", sampling.ChannelCenterBiasMissCount, accumulate: true);
            timing?.RecordStageDuration("river.local_refine.height_compute",
                sampling.HeightComputeTicks * 1000d / Stopwatch.Frequency, accumulate: true);
            return new GeneratedHydrologyMap(riverCells, floodplainCells);
        }

        private static Int2 ChannelPosition(MacroHydrologyRegion region, int index) =>
            region.Positions[index];

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
                if (current < 0 || region.Flows[i] > region.Flows[current] ||
                    region.Flows[i] == region.Flows[current] && CompareWorldPosition(region.Positions[i], region.Positions[current]) < 0)
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
                int dominant = primaryUpstream[target];
                int2 incoming = dominant >= 0
                    ? request.Topology.ToDomain().ShortestDelta(
                        new int2(ChannelPosition(region, dominant).X, ChannelPosition(region, dominant).Y),
                        new int2(ChannelPosition(region, target).X, ChannelPosition(region, target).Y))
                    : new int2((int)deltaX, (int)deltaY);
                // 各支流靠近汇合点时共享主槽方向，与下游河段的起始切线保持一致。
                SetChannelTangent(incoming.x + outgoing.x, incoming.y + outgoing.y,
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

        private static void RenderMacroCenters(ChunkGenerationSettingsSnapshot settings,
            HydrologySamplingContext sampling, HydrologyOutputWindow outputWindow,
            IReadOnlyDictionary<Int2, ChannelCenterSample> centers, int maximumRadius,
            Dictionary<Int2, GeneratedHydrologyCell> riverCells,
            Dictionary<Int2, double> floodplainCells, CancellationToken cancellationToken,
            ChunkGenerationTiming timing)
        {
            int rendered = 0;
            long channelTicks = 0, floodplainTicks = 0;
            long candidates = 0, outsideWindow = 0, oceanRejected = 0, mergeAttempts = 0;
            long floodplainCandidates = 0, floodplainMergeAttempts = 0;
            using var floodplain = new FloodplainRaster(outputWindow, sampling, floodplainCells);
            foreach (KeyValuePair<Int2, ChannelCenterSample> pair in centers)
            {
                if ((rendered++ & 63) == 0) cancellationToken.ThrowIfCancellationRequested();
                long started = timing != null ? Stopwatch.GetTimestamp() : 0;
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
                    if (timing != null) candidates++;
                    Int2 water = sampling.Normalize(new Int2(center.X + offsetX,
                        center.Y + offsetY));
                    if (!outputWindow.Contains(water))
                    {
                        if (timing != null) outsideWindow++;
                        continue;
                    }
                    double height = sampling.Height(water);
                    if (height <= settings.SeaLevel)
                    {
                        if (timing != null) oceanRejected++;
                        continue;
                    }
                    double distance = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                    double edgeStrength = radius == 0 ? 1d :
                        1d - Clamp01(distance / (radius + 0.5d));
                    double depth = Lerp(settings.RiverDepthMin, centerDepth, edgeStrength);
                    if (timing != null) mergeAttempts++;
                    SetRiverCell(riverCells, water, new GeneratedHydrologyCell(
                        GeneratedHydrologyKind.River, sample.Flow, depth, height + depth * settings.RiverMaxLakeLevelRise,
                        sample.DirectionX, sample.DirectionY, edgeStrength,
                        sample.BedEdgeDeposit));
                }
                if (timing != null)
                {
                    channelTicks += Stopwatch.GetTimestamp() - started;
                    started = Stopwatch.GetTimestamp();
                }
                AddFloodplain(settings, sampling, center, sample.Flow,
                    radius, floodplain, timing != null,
                    ref floodplainCandidates, ref floodplainMergeAttempts);
                if (timing != null) floodplainTicks += Stopwatch.GetTimestamp() - started;
            }
            long flushStarted = timing != null ? Stopwatch.GetTimestamp() : 0;
            floodplain.CopyToOutput(cancellationToken);
            if (timing != null) floodplainTicks += Stopwatch.GetTimestamp() - flushStarted;
            // 循环内只累加数字，结束后一次写入本区块诊断，后台不输出日志。
            timing?.RecordStageDuration("river.local_refine.channel_fill", channelTicks * 1000d / Stopwatch.Frequency, accumulate: true);
            timing?.RecordStageDuration("river.local_refine.floodplain", floodplainTicks * 1000d / Stopwatch.Frequency, accumulate: true);
            timing?.RecordStageCount("river.local_refine.channel_candidates", candidates, accumulate: true);
            timing?.RecordStageCount("river.local_refine.channel_outside_window", outsideWindow, accumulate: true);
            timing?.RecordStageCount("river.local_refine.channel_ocean_rejected", oceanRejected, accumulate: true);
            timing?.RecordStageCount("river.local_refine.channel_merge_attempts", mergeAttempts, accumulate: true);
            timing?.RecordStageCount("river.local_refine.floodplain_candidates", floodplainCandidates, accumulate: true);
            timing?.RecordStageCount("river.local_refine.floodplain_merge_attempts", floodplainMergeAttempts, accumulate: true);
            timing?.RecordStageCount("river.local_refine.floodplain_height_queries", floodplain.HeightQueries, accumulate: true);
        }

        private static int GetHydrologyOutputPadding(ChunkGenerationRequest request) =>
            (int)Math.Ceiling(Math.Max(AirHumiditySettings.FromProfile(request.Profile).RadiusTiles,
                request.Profile.Settings.PrecipitationFreshwaterRadius +
                Math.Max(request.Profile.Settings.PrecipitationChunkWidth,
                    request.Profile.Settings.PrecipitationChunkHeight) * 2d +
                request.Profile.Settings.PeatStoneBoundaryRadius));

        /// <summary>每块只冻结一次输出边界，水格和冲积格不再反复解析相同配置。</summary>
        private readonly struct HydrologyOutputWindow
        {
            private readonly int originX, originY, centerX, centerY, spanX, spanY, maximumX, maximumY;
            private readonly int worldMinX, worldMinY;

            internal HydrologyOutputWindow(ChunkGenerationRequest request)
            {
                Padding = GetHydrologyOutputPadding(request);
                originX = request.Address.ChunkOrigin.X;
                originY = request.Address.ChunkOrigin.Y;
                centerX = originX + request.Profile.Width / 2;
                centerY = originY + request.Profile.Height / 2;
                spanX = request.Topology.IsWrapped ? request.Topology.Span.X : 0;
                spanY = request.Topology.IsWrapped ? request.Topology.Span.Y : 0;
                worldMinX = request.Topology.Min.X;
                worldMinY = request.Topology.Min.Y;
                maximumX = request.Profile.Width + Padding;
                maximumY = request.Profile.Height + Padding;
            }

            internal int Padding { get; }

            internal int Width => maximumX + Padding;
            internal int Height => maximumY + Padding;

            internal bool Contains(Int2 position)
            {
                int x = ShiftNearChunk(position.X, centerX, spanX) - originX;
                int y = ShiftNearChunk(position.Y, centerY, spanY) - originY;
                return x >= -Padding && y >= -Padding && x < maximumX && y < maximumY;
            }

            internal bool TryGetIndex(Int2 position, out int index)
            {
                int x = ShiftNearChunk(position.X, centerX, spanX) - originX;
                int y = ShiftNearChunk(position.Y, centerY, spanY) - originY;
                bool contains = x >= -Padding && y >= -Padding && x < maximumX && y < maximumY;
                index = contains ? (y + Padding) * Width + x + Padding : -1;
                return contains;
            }

            /// <summary>冲积带扫描只在越界时回绕坐标，避免每个重复候选格都执行取模。</summary>
            internal bool TryGetCandidateIndex(Int2 position, out int index)
            {
                return TryGetIndex(new Int2(
                    NormalizeWhenOutside(position.X, worldMinX, spanX),
                    NormalizeWhenOutside(position.Y, worldMinY, spanY)), out index);
            }

            private static int NormalizeWhenOutside(int position, int minimum, int span)
            {
                if (span <= 0) return position;
                long offset = (long)position - minimum;
                if (offset >= 0L && offset < span) return position;
                long wrapped = offset % span;
                if (wrapped < 0L) wrapped += span;
                return (int)(minimum + wrapped);
            }

            internal Int2 PositionAt(int index) => new(originX - Padding + index % Width,
                originY - Padding + index / Width);
        }

        /// <summary>每种半径只计算一次原始圆形偏移和衰减，复用平方根结果。</summary>
        private sealed class FloodplainKernel
        {
            private const int MaximumCachedRadius = 96;
            private static readonly FloodplainKernel[] cache = new FloodplainKernel[MaximumCachedRadius + 1];

            internal static FloodplainKernel ForRadius(int radius)
            {
                if ((uint)radius > MaximumCachedRadius) return new FloodplainKernel(radius);
                FloodplainKernel kernel = Volatile.Read(ref cache[radius]);
                if (kernel != null) return kernel;
                kernel = new FloodplainKernel(radius);
                return Interlocked.CompareExchange(ref cache[radius], kernel, null) ?? kernel;
            }

            internal readonly struct Sample
            {
                internal Sample(int x, int y, double strength)
                { X = x; Y = y; Strength = strength; }
                internal int X { get; }
                internal int Y { get; }
                internal double Strength { get; }
            }

            internal FloodplainKernel(int radius)
            {
                var samples = new List<Sample>((int)Math.Ceiling(Math.PI * (radius + 1d) * (radius + 1d)));
                for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    double distance = Math.Sqrt(x * x + y * y);
                    if (distance > radius + 0.25d) continue;
                    double edgeStrength = 1d - Clamp01(distance / (radius + 0.75d));
                    samples.Add(new Sample(x, y, Math.Sqrt(edgeStrength)));
                }
                Samples = samples.ToArray();
            }

            internal Sample[] Samples { get; }
        }

        /// <summary>重叠冲积格直接在池化窗口取最大值，每格只查询一次高度。</summary>
        private sealed class FloodplainRaster : IDisposable
        {
            private const int MaximumDenseCells = 1024 * 1024;
            private readonly HydrologyOutputWindow window;
            private readonly HydrologySamplingContext sampling;
            private readonly Dictionary<Int2, double> output;
            private readonly int denseLength;
            private double[] strengths;
            private List<int> visited;

            internal FloodplainRaster(HydrologyOutputWindow window,
                HydrologySamplingContext sampling, Dictionary<Int2, double> output)
            {
                this.window = window; this.sampling = sampling; this.output = output;
                long length = (long)window.Width * window.Height;
                denseLength = window.Width > 0 && window.Height > 0 && length <= MaximumDenseCells
                    ? (int)length : 0;
            }

            internal long HeightQueries { get; private set; }

            internal bool Merge(Int2 position, double strength)
            {
                if (!window.TryGetCandidateIndex(position, out int index)) return false;
                if (denseLength == 0)
                {
                    HeightQueries++;
                    if (sampling.Height(position) <= sampling.Settings.SeaLevel) return false;
                    SetFloodplainStrength(output, sampling.Normalize(position), strength);
                    return true;
                }
                if (strengths == null)
                {
                    strengths = ArrayPool<double>.Shared.Rent(denseLength);
                    Array.Clear(strengths, 0, denseLength);
                    visited = new List<int>();
                }
                double current = strengths[index];
                if (current == 0d)
                {
                    HeightQueries++;
                    if (sampling.Height(position) <= sampling.Settings.SeaLevel)
                    { strengths[index] = -1d; return false; }
                    visited.Add(index);
                    strengths[index] = Clamp01(strength);
                }
                else if (current < 0d) return false;
                else
                {
                    strength = Clamp01(strength);
                    if (strength > current) strengths[index] = strength;
                }
                return true;
            }

            internal void CopyToOutput(CancellationToken cancellation)
            {
                if (visited == null) return;
                output.EnsureCapacity(visited.Count);
                for (int i = 0; i < visited.Count; i++)
                {
                    if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
                    int index = visited[i];
                    output.Add(sampling.Normalize(window.PositionAt(index)), strengths[index]);
                }
            }

            public void Dispose()
            {
                if (strengths == null) return;
                ArrayPool<double>.Shared.Return(strengths);
                strengths = null;
            }
        }
        #endregion
    }
}
