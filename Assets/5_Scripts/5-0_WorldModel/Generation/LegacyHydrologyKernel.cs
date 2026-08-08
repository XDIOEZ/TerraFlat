using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    /// <summary>纯水文结果中的淡水类型；数值会写入 riverKind 环境层。</summary>
    internal enum GeneratedHydrologyKind : byte
    {
        None = 0,
        River = 1,
        Lake = 2
    }

    /// <summary>一个淡水格子的类型、汇流量、水深和湖面高度。</summary>
    internal readonly struct GeneratedHydrologyCell
    {
        internal GeneratedHydrologyCell(
            GeneratedHydrologyKind kind,
            double flow,
            double depth,
            double surfaceLevel = 0d)
        {
            Kind = kind;
            Flow = Math.Max(0d, flow);
            Depth = Clamp01(depth);
            SurfaceLevel = Clamp01(surfaceLevel);
        }

        internal GeneratedHydrologyKind Kind { get; }
        internal double Flow { get; }
        internal double Depth { get; }
        internal double SurfaceLevel { get; }

        private static double Clamp01(double value) =>
            value < 0d ? 0d : value > 1d ? 1d : value;
    }

    /// <summary>当前区块可查询的淡水格与冲积平原，只保存纯数据。</summary>
    internal sealed class GeneratedHydrologyMap
    {
        private readonly IReadOnlyDictionary<Int2, GeneratedHydrologyCell> cells;
        private readonly IReadOnlyDictionary<Int2, double> floodplainCells;

        internal GeneratedHydrologyMap(
            IReadOnlyDictionary<Int2, GeneratedHydrologyCell> cells,
            IReadOnlyDictionary<Int2, double> floodplainCells)
        {
            this.cells = cells ?? throw new ArgumentNullException(nameof(cells));
            this.floodplainCells = floodplainCells ??
                                   throw new ArgumentNullException(nameof(floodplainCells));
        }

        /// <summary>查询世界坐标上的淡水类型和水文数据。</summary>
        internal bool TryGet(int worldX, int worldY, out GeneratedHydrologyCell cell) =>
            cells.TryGetValue(new Int2(worldX, worldY), out cell);

        /// <summary>查询世界坐标上的最大冲积平原强度。</summary>
        internal double GetFloodplainStrength(int worldX, int worldY) =>
            floodplainCells.TryGetValue(new Int2(worldX, worldY), out double strength)
                ? strength
                : 0d;
    }

    /// <summary>
    /// 从旧 ChunkGenerator_River 提取的纯 C# 区域水文内核。
    /// 它按绝对世界坐标追踪径流、解析盆地、生成湖泊并从最低溢流口继续下游；
    /// 已完成区域仅缓存在当前生成器实例内，缓存可并发读取且不会访问 Unity 对象。
    /// </summary>
    internal sealed class LegacyHydrologyKernel
    {
        private const double DownhillEpsilon = 0.00001d;

        private static readonly Int2[] Neighbors =
        {
            new Int2(-1, -1), new Int2(0, -1), new Int2(1, -1),
            new Int2(-1, 0),                        new Int2(1, 0),
            new Int2(-1, 1),  new Int2(0, 1),  new Int2(1, 1)
        };

        private readonly ConcurrentDictionary<RegionKey, RegionResult> regionCache = new();
        private readonly ConcurrentQueue<RegionKey> cacheInsertionOrder = new();

        /// <summary>生成当前区块需要的旧版河流、湖泊与冲积平原查询表。</summary>
        internal GeneratedHydrologyMap Build(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            Func<Int2, double> heightSampler,
            Func<Int2, double> precipitationSampler,
            CancellationToken cancellationToken)
        {
            if (heightSampler == null)
                throw new ArgumentNullException(nameof(heightSampler));
            if (precipitationSampler == null)
                throw new ArgumentNullException(nameof(precipitationSampler));

            cancellationToken.ThrowIfCancellationRequested();
            ulong configurationHash = CalculateConfigurationHash(request, settings);
            var regions = new Dictionary<Int2, RegionResult>();
            var cells = new Dictionary<Int2, GeneratedHydrologyCell>();
            var floodplainCells = new Dictionary<Int2, double>();
            int width = request.Profile.Width;
            int height = request.Profile.Height;

            for (int localY = 0; localY < height; localY++)
            {
                for (int localX = 0; localX < width; localX++)
                {
                    int index = localY * width + localX;
                    if ((index & 63) == 0)
                        cancellationToken.ThrowIfCancellationRequested();

                    Int2 position = Normalize(request.Topology, new Int2(
                        request.Address.ChunkOrigin.X + localX,
                        request.Address.ChunkOrigin.Y + localY));
                    Int2 regionCoordinate = GetRegionCoordinate(
                        request.Topology, position, settings.RiverHydrologyRegionSize);
                    if (!regions.TryGetValue(regionCoordinate, out RegionResult region))
                    {
                        var key = new RegionKey(
                            request.Address.DimensionId,
                            request.WorldSeed,
                            request.Topology,
                            configurationHash,
                            regionCoordinate);
                        region = GetOrBuildRegion(
                            key,
                            request,
                            settings,
                            regionCoordinate,
                            heightSampler,
                            precipitationSampler,
                            cancellationToken);
                        regions.Add(regionCoordinate, region);
                    }

                    LegacyCellSample sample = region.Get(position, request.Topology);
                    if (sample.Kind != GeneratedHydrologyKind.None)
                    {
                        MergeCell(cells, position, new GeneratedHydrologyCell(
                            sample.Kind,
                            sample.Flow,
                            sample.Depth,
                            sample.SurfaceLevel));
                    }

                    double floodplain = region.GetFloodplain(position, request.Topology);
                    if (floodplain > 0d)
                        SetFloodplainStrength(floodplainCells, position, floodplain);
                }
            }

            return new GeneratedHydrologyMap(cells, floodplainCells);
        }

        /// <summary>供纯算法回归检查缓存是否保持有界。</summary>
        internal int CachedRegionCount => regionCache.Count;

        /// <summary>清空当前生成器实例的区域缓存。</summary>
        internal void ClearCache()
        {
            regionCache.Clear();
            while (cacheInsertionOrder.TryDequeue(out _))
            {
            }
        }

        #region 区域缓存

        private RegionResult GetOrBuildRegion(
            RegionKey key,
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            Int2 regionCoordinate,
            Func<Int2, double> heightSampler,
            Func<Int2, double> precipitationSampler,
            CancellationToken cancellationToken)
        {
            if (regionCache.TryGetValue(key, out RegionResult cached))
                return cached;

            // 只缓存完整结果。并发首次命中允许各自计算，避免一个请求的取消令另一个请求误取消。
            var builder = new RegionBuilder(
                request,
                settings,
                regionCoordinate,
                heightSampler,
                precipitationSampler,
                cancellationToken);
            RegionResult candidate = builder.Build();
            cancellationToken.ThrowIfCancellationRequested();
            RegionResult result = regionCache.GetOrAdd(key, candidate);
            if (ReferenceEquals(result, candidate))
            {
                cacheInsertionOrder.Enqueue(key);
                TrimCache(settings.RiverMaxCachedRegions);
            }
            return result;
        }

        private void TrimCache(int maximumCount)
        {
            maximumCount = Math.Max(1, maximumCount);
            while (regionCache.Count > maximumCount &&
                   cacheInsertionOrder.TryDequeue(out RegionKey oldest))
            {
                regionCache.TryRemove(oldest, out _);
            }
        }

        private readonly struct RegionKey : IEquatable<RegionKey>
        {
            private readonly string dimensionId;
            private readonly int worldSeed;
            private readonly bool wrapped;
            private readonly Int2 topologyMin;
            private readonly Int2 topologySpan;
            private readonly ulong configurationHash;
            private readonly Int2 coordinate;

            internal RegionKey(
                string dimensionId,
                int worldSeed,
                ChunkGenerationTopologySnapshot topology,
                ulong configurationHash,
                Int2 coordinate)
            {
                this.dimensionId = dimensionId ?? string.Empty;
                this.worldSeed = worldSeed;
                wrapped = topology.IsWrapped;
                topologyMin = topology.Min;
                topologySpan = topology.Span;
                this.configurationHash = configurationHash;
                this.coordinate = coordinate;
            }

            public bool Equals(RegionKey other) =>
                StringComparer.Ordinal.Equals(dimensionId, other.dimensionId) &&
                worldSeed == other.worldSeed &&
                wrapped == other.wrapped &&
                topologyMin == other.topologyMin &&
                topologySpan == other.topologySpan &&
                configurationHash == other.configurationHash &&
                coordinate == other.coordinate;

            public override bool Equals(object obj) => obj is RegionKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = StringComparer.Ordinal.GetHashCode(dimensionId);
                    hash = hash * 397 ^ worldSeed;
                    hash = hash * 397 ^ wrapped.GetHashCode();
                    hash = hash * 397 ^ topologyMin.GetHashCode();
                    hash = hash * 397 ^ topologySpan.GetHashCode();
                    hash = hash * 397 ^ configurationHash.GetHashCode();
                    return hash * 397 ^ coordinate.GetHashCode();
                }
            }
        }

        #endregion

        #region 区域构建

        /// <summary>逐个径流源构建一个完整水文区域。</summary>
        private sealed class RegionBuilder
        {
            private readonly ChunkGenerationRequest request;
            private readonly ChunkGenerationSettingsSnapshot settings;
            private readonly Func<Int2, double> heightSampler;
            private readonly Func<Int2, double> precipitationSampler;
            private readonly CancellationToken cancellationToken;
            private readonly Int2 regionOrigin;
            private readonly Dictionary<Int2, double> flow = new();
            private readonly Dictionary<Int2, BasinResult> basins = new();
            private readonly Dictionary<Int2, double> basinFlow = new();
            private readonly HashSet<Int2> processedSourceOrigins = new();
            private readonly Dictionary<Int2, double> heightCache = new();
            private readonly Dictionary<Int2, double> precipitationCache = new();

            internal RegionBuilder(
                ChunkGenerationRequest request,
                ChunkGenerationSettingsSnapshot settings,
                Int2 regionCoordinate,
                Func<Int2, double> heightSampler,
                Func<Int2, double> precipitationSampler,
                CancellationToken cancellationToken)
            {
                this.request = request;
                this.settings = settings;
                this.heightSampler = heightSampler;
                this.precipitationSampler = precipitationSampler;
                this.cancellationToken = cancellationToken;
                Int2 anchor = request.Topology.IsWrapped ? request.Topology.Min : default;
                regionOrigin = new Int2(
                    anchor.X + regionCoordinate.X * settings.RiverHydrologyRegionSize,
                    anchor.Y + regionCoordinate.Y * settings.RiverHydrologyRegionSize);
            }

            internal RegionResult Build()
            {
                ProcessSources();
                return BuildResult();
            }

            private void ProcessSources()
            {
                int channelRadius = Math.Max(0, (settings.RiverMaxWidth - 1) / 2);
                int outputMargin = Math.Max(channelRadius, settings.RiverFloodplainMaxRadius);
                int padding = settings.RiverMaxTraceSteps + outputMargin + 1;
                Int2 sourceAnchor = request.Topology.IsWrapped ? request.Topology.Min : default;
                int cellSize = settings.RiverRunoffCellSize;
                int minSourceX = FloorDiv(regionOrigin.X - padding - sourceAnchor.X, cellSize);
                int maxSourceX = FloorDiv(
                    regionOrigin.X + settings.RiverHydrologyRegionSize - 1 + padding - sourceAnchor.X,
                    cellSize);
                int minSourceY = FloorDiv(regionOrigin.Y - padding - sourceAnchor.Y, cellSize);
                int maxSourceY = FloorDiv(
                    regionOrigin.Y + settings.RiverHydrologyRegionSize - 1 + padding - sourceAnchor.Y,
                    cellSize);

                int processed = 0;
                for (int sourceY = minSourceY; sourceY <= maxSourceY; sourceY++)
                {
                    for (int sourceX = minSourceX; sourceX <= maxSourceX; sourceX++)
                    {
                        if ((processed++ & 7) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        ProcessRunoffCell(sourceX, sourceY, sourceAnchor);
                    }
                }
            }

            /// <summary>汇总一个旧版径流单元，并从最高有效采样点开始追踪。</summary>
            private void ProcessRunoffCell(int sourceCellX, int sourceCellY, Int2 sourceAnchor)
            {
                int cellSize = settings.RiverRunoffCellSize;
                int stride = settings.RiverRunoffSampleStride;
                Int2 cellOrigin = new Int2(
                    sourceAnchor.X + sourceCellX * cellSize,
                    sourceAnchor.Y + sourceCellY * cellSize);
                Int2 canonicalCellOrigin = Normalize(request.Topology, cellOrigin);
                if (!processedSourceOrigins.Add(canonicalCellOrigin))
                    return;

                double runoffSum = 0d;
                int sampleCount = 0;
                Int2 source = default;
                double sourceScore = double.MinValue;
                int visitedSamples = 0;
                for (int localY = stride / 2; localY < cellSize; localY += stride)
                {
                    for (int localX = stride / 2; localX < cellSize; localX += stride)
                    {
                        if ((visitedSamples++ & 31) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        Int2 position = Normalize(request.Topology, new Int2(
                            cellOrigin.X + localX,
                            cellOrigin.Y + localY));
                        double height = Height(position);
                        double precipitation = Precipitation(position);
                        sampleCount++;
                        if (height <= settings.SeaLevel)
                            continue;

                        double runoff = Clamp01(
                            (precipitation - settings.RiverInfiltrationFloor) /
                            Math.Max(0.0001d, 1d - settings.RiverInfiltrationFloor));
                        runoffSum += runoff;
                        double score = height + runoff * 0.05d +
                                       Hash01(request.WorldSeed, position.X, position.Y,
                                           0x51ed270bu) * 0.001d;
                        if (score <= sourceScore)
                            continue;
                        sourceScore = score;
                        source = position;
                    }
                }

                if (sampleCount == 0 || sourceScore == double.MinValue)
                    return;
                double contribution = runoffSum / sampleCount;
                if (contribution <= 0.0001d)
                    return;
                TraceRunoff(source, contribution);
            }

            /// <summary>沿严格下坡方向追踪；遇到洼地时生成盆地并从溢流口继续。</summary>
            private void TraceRunoff(Int2 source, double contribution)
            {
                Int2 current = Normalize(request.Topology, source);
                var visited = new HashSet<Int2>();
                for (int step = 0; step < settings.RiverMaxTraceSteps; step++)
                {
                    if ((step & 31) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    current = Normalize(request.Topology, current);
                    if (!visited.Add(current))
                        break;

                    double currentHeight = Height(current);
                    if (currentHeight <= settings.SeaLevel)
                        break;
                    AddFlow(current, contribution);

                    if (TryChooseDownhill(current, currentHeight, out Int2 next))
                    {
                        AddDiagonalBridge(current, next, contribution);
                        current = next;
                        continue;
                    }

                    BasinResult basin = ResolveBasin(current, currentHeight);
                    basinFlow.TryGetValue(current, out double existingBasinFlow);
                    basinFlow[current] = existingBasinFlow + contribution;
                    if (!basin.HasOutlet)
                        break;
                    current = basin.Outlet;
                }
            }

            /// <summary>按高度与固定坐标扰动选择最低的严格下坡邻格。</summary>
            private bool TryChooseDownhill(Int2 current, double currentHeight, out Int2 next)
            {
                next = default;
                double bestScore = double.MaxValue;
                bool found = false;
                for (int i = 0; i < Neighbors.Length; i++)
                {
                    Int2 candidate = Normalize(request.Topology, current + Neighbors[i]);
                    if (candidate == current)
                        continue;
                    double height = Height(candidate);
                    if (height >= currentHeight - DownhillEpsilon)
                        continue;
                    double score = height + Hash01(
                        request.WorldSeed,
                        candidate.X,
                        candidate.Y,
                        0x9e3779b9u) * settings.RiverMeanderTieTolerance;
                    if (score >= bestScore)
                        continue;
                    bestScore = score;
                    next = candidate;
                    found = true;
                }
                return found;
            }

            /// <summary>按最低边界逐格扩张盆地，找到最低溢流口时结束。</summary>
            private BasinResult ResolveBasin(Int2 sink, double sinkHeight)
            {
                if (basins.TryGetValue(sink, out BasinResult cached))
                    return cached;

                var basin = new HashSet<Int2> { sink };
                var queued = new HashSet<Int2>();
                var frontier = new List<FrontierCell>();
                AddFrontier(sink, basin, queued, frontier);
                double waterLevel = sinkHeight;
                Int2 outlet = default;
                bool hasOutlet = false;
                int iteration = 0;

                while (frontier.Count > 0 && basin.Count < settings.RiverMaxLakeCells)
                {
                    if ((iteration++ & 15) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    int minimumIndex = 0;
                    for (int i = 1; i < frontier.Count; i++)
                    {
                        if (frontier[i].CompareTo(frontier[minimumIndex]) < 0)
                            minimumIndex = i;
                    }

                    FrontierCell boundary = frontier[minimumIndex];
                    frontier.RemoveAt(minimumIndex);
                    if (boundary.Height - sinkHeight > settings.RiverMaxLakeLevelRise)
                        break;
                    waterLevel = Math.Max(waterLevel, boundary.Height);
                    basin.Add(boundary.Position);

                    for (int i = 0; i < Neighbors.Length; i++)
                    {
                        Int2 candidate = Normalize(
                            request.Topology, boundary.Position + Neighbors[i]);
                        if (basin.Contains(candidate))
                            continue;
                        if (Height(candidate) < waterLevel - DownhillEpsilon)
                        {
                            outlet = candidate;
                            hasOutlet = true;
                            break;
                        }
                    }

                    if (hasOutlet)
                        break;
                    AddFrontier(boundary.Position, basin, queued, frontier);
                }

                var result = new BasinResult(
                    new List<Int2>(basin), waterLevel, hasOutlet, outlet);
                basins[sink] = result;
                return result;
            }

            private void AddFrontier(
                Int2 center,
                HashSet<Int2> basin,
                HashSet<Int2> queued,
                List<FrontierCell> frontier)
            {
                for (int i = 0; i < Neighbors.Length; i++)
                {
                    Int2 candidate = Normalize(request.Topology, center + Neighbors[i]);
                    if (candidate == center || basin.Contains(candidate) || !queued.Add(candidate))
                        continue;
                    frontier.Add(new FrontierCell(candidate, Height(candidate)));
                }
            }

            /// <summary>斜向下游补一个较低的正交格，保证 Tilemap 四邻域连通。</summary>
            private void AddDiagonalBridge(Int2 current, Int2 next, double contribution)
            {
                int deltaX = ShortestDelta(
                    current.X, next.X, request.Topology, horizontal: true);
                int deltaY = ShortestDelta(
                    current.Y, next.Y, request.Topology, horizontal: false);
                if (deltaX == 0 || deltaY == 0)
                    return;

                Int2 horizontal = Normalize(
                    request.Topology, new Int2(current.X + deltaX, current.Y));
                Int2 vertical = Normalize(
                    request.Topology, new Int2(current.X, current.Y + deltaY));
                AddFlow(Height(horizontal) <= Height(vertical) ? horizontal : vertical, contribution);
            }

            private void AddFlow(Int2 position, double contribution)
            {
                position = Normalize(request.Topology, position);
                flow.TryGetValue(position, out double existing);
                flow[position] = existing + contribution;
            }

            private RegionResult BuildResult()
            {
                int size = settings.RiverHydrologyRegionSize;
                var cells = new LegacyCellSample[size * size];
                var floodplain = new float[size * size];
                int maximumRadius = Math.Max(0, (settings.RiverMaxWidth - 1) / 2);
                int outputMargin = Math.Max(maximumRadius, settings.RiverFloodplainMaxRadius);
                int processed = 0;

                foreach (KeyValuePair<Int2, double> pair in flow)
                {
                    if ((processed++ & 31) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    if (pair.Value < settings.RiverStartFlow ||
                        !ContainsExpanded(pair.Key, outputMargin))
                    {
                        continue;
                    }

                    double widthT = InverseLerp(
                        settings.RiverStartFlow,
                        Math.Max(settings.RiverStartFlow + 0.001d, settings.RiverFullWidthFlow),
                        pair.Value);
                    int width = 1 + (int)Math.Round(
                        widthT * (settings.RiverMaxWidth - 1),
                        MidpointRounding.AwayFromZero);
                    int radius = Math.Min(maximumRadius, Math.Max(0, width / 2));
                    double centerDepth = Lerp(
                        settings.RiverDepthMin,
                        settings.RiverDepthMax,
                        Math.Sqrt(widthT));

                    for (int offsetY = -radius; offsetY <= radius; offsetY++)
                    {
                        for (int offsetX = -radius; offsetX <= radius; offsetX++)
                        {
                            if (offsetX * offsetX + offsetY * offsetY > radius * radius + 1)
                                continue;
                            Int2 waterPosition = Normalize(request.Topology, new Int2(
                                pair.Key.X + offsetX,
                                pair.Key.Y + offsetY));
                            if (!ContainsCore(waterPosition) ||
                                Height(waterPosition) <= settings.SeaLevel)
                            {
                                continue;
                            }

                            double distance = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                            double edgeStrength = radius == 0
                                ? 1d
                                : 1d - Clamp01(distance / (radius + 0.5d));
                            double depth = Lerp(
                                settings.RiverDepthMin, centerDepth, edgeStrength);
                            SetCell(cells, waterPosition, new LegacyCellSample(
                                GeneratedHydrologyKind.River, pair.Value, depth, 0d));
                        }
                    }

                    AddFloodplain(floodplain, pair.Key, pair.Value, radius);
                }

                processed = 0;
                foreach (KeyValuePair<Int2, double> pair in basinFlow)
                {
                    if ((processed++ & 15) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    if (pair.Value < settings.RiverLakeMinFlow ||
                        !basins.TryGetValue(pair.Key, out BasinResult basin) ||
                        basin.Cells.Count < settings.RiverMinLakeCells ||
                        basin.Cells.Count > settings.RiverMaxLakeCells)
                    {
                        continue;
                    }

                    for (int i = 0; i < basin.Cells.Count; i++)
                    {
                        Int2 lakePosition = basin.Cells[i];
                        if (!ContainsCore(lakePosition))
                            continue;
                        double height = Height(lakePosition);
                        if (height <= settings.SeaLevel)
                            continue;
                        double depthT = Clamp01(
                            (basin.WaterLevel - height) /
                            Math.Max(0.0001d, settings.RiverMaxLakeLevelRise));
                        double depth = Lerp(
                            settings.RiverDepthMin,
                            settings.RiverDepthMax,
                            Math.Max(0.15d, depthT));
                        SetCell(cells, lakePosition, new LegacyCellSample(
                            GeneratedHydrologyKind.Lake,
                            pair.Value,
                            depth,
                            basin.WaterLevel));
                    }
                }

                return new RegionResult(regionOrigin, size, cells, floodplain);
            }

            /// <summary>在低坡成熟河段两侧保留新版数据契约所需的冲积平原。</summary>
            private void AddFloodplain(
                float[] output,
                Int2 center,
                double centerFlow,
                int channelRadius)
            {
                if (settings.RiverFloodplainMaxRadius <= channelRadius ||
                    centerFlow < settings.RiverFloodplainStartFlow)
                {
                    return;
                }

                double slope = EstimateLocalSlope(center);
                double flatness = 1d - Clamp01(slope / settings.RiverFloodplainMaxSlope);
                if (flatness <= 0.05d)
                    return;
                double flowT = InverseLerp(
                    settings.RiverFloodplainStartFlow,
                    Math.Max(settings.RiverFloodplainStartFlow + 0.001d,
                        settings.RiverFullWidthFlow),
                    centerFlow);
                int minimumRadius = Math.Min(
                    settings.RiverFloodplainMaxRadius, channelRadius + 2);
                int radius = minimumRadius + (int)Math.Round(
                    (settings.RiverFloodplainMaxRadius - minimumRadius) *
                    Math.Sqrt(flowT * flatness),
                    MidpointRounding.AwayFromZero);
                double centerStrength = Math.Sqrt(flatness) *
                                        (0.55d + Math.Sqrt(flowT) * 0.45d);

                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    for (int offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        double distance = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                        if (distance > radius + 0.25d)
                            continue;
                        Int2 position = Normalize(request.Topology, new Int2(
                            center.X + offsetX,
                            center.Y + offsetY));
                        if (!ContainsCore(position) || Height(position) <= settings.SeaLevel)
                            continue;
                        double edgeStrength = 1d - Clamp01(distance / (radius + 0.75d));
                        SetFloodplainValue(
                            output,
                            position,
                            centerStrength * Math.Sqrt(edgeStrength));
                    }
                }
            }

            private double EstimateLocalSlope(Int2 position)
            {
                double center = Height(position);
                double maximum = 0d;
                for (int i = 0; i < Neighbors.Length; i++)
                    maximum = Math.Max(maximum, Math.Abs(Height(position + Neighbors[i]) - center));
                return maximum;
            }

            private double Height(Int2 position)
            {
                position = Normalize(request.Topology, position);
                if (!heightCache.TryGetValue(position, out double value))
                {
                    value = Clamp01(heightSampler(position));
                    heightCache.Add(position, value);
                }
                return value;
            }

            private double Precipitation(Int2 position)
            {
                position = Normalize(request.Topology, position);
                if (!precipitationCache.TryGetValue(position, out double value))
                {
                    value = Clamp01(precipitationSampler(position));
                    precipitationCache.Add(position, value);
                }
                return value;
            }

            private void SetCell(
                LegacyCellSample[] output,
                Int2 worldPosition,
                LegacyCellSample sample)
            {
                int index = GetRegionIndex(worldPosition);
                LegacyCellSample existing = output[index];
                if (existing.Kind == GeneratedHydrologyKind.Lake &&
                    sample.Kind != GeneratedHydrologyKind.Lake)
                {
                    return;
                }
                if (existing.Kind == sample.Kind && existing.Depth > sample.Depth)
                    return;
                output[index] = sample;
            }

            private void SetFloodplainValue(float[] output, Int2 position, double strength)
            {
                int index = GetRegionIndex(position);
                float candidate = (float)Clamp01(strength);
                if (candidate > output[index])
                    output[index] = candidate;
            }

            private int GetRegionIndex(Int2 position)
            {
                position = ToNearestRegionImage(position);
                int localX = position.X - regionOrigin.X;
                int localY = position.Y - regionOrigin.Y;
                int size = settings.RiverHydrologyRegionSize;
                if ((uint)localX >= (uint)size || (uint)localY >= (uint)size)
                {
                    throw new InvalidOperationException(
                        $"Hydrology cell {position} is outside region {regionOrigin} size {size}.");
                }
                return localY * size + localX;
            }

            private bool ContainsCore(Int2 position)
            {
                position = ToNearestRegionImage(position);
                int size = settings.RiverHydrologyRegionSize;
                return (uint)(position.X - regionOrigin.X) < (uint)size &&
                       (uint)(position.Y - regionOrigin.Y) < (uint)size;
            }

            private bool ContainsExpanded(Int2 position, int margin)
            {
                position = ToNearestRegionImage(position);
                int size = settings.RiverHydrologyRegionSize;
                return position.X >= regionOrigin.X - margin &&
                       position.Y >= regionOrigin.Y - margin &&
                       position.X < regionOrigin.X + size + margin &&
                       position.Y < regionOrigin.Y + size + margin;
            }

            private Int2 ToNearestRegionImage(Int2 position)
            {
                if (!request.Topology.IsWrapped)
                    return position;
                int half = settings.RiverHydrologyRegionSize / 2;
                Int2 center = new Int2(regionOrigin.X + half, regionOrigin.Y + half);
                return new Int2(
                    center.X + ShortestDelta(
                        center.X, position.X, request.Topology, horizontal: true),
                    center.Y + ShortestDelta(
                        center.Y, position.Y, request.Topology, horizontal: false));
            }
        }

        private sealed class RegionResult
        {
            private readonly LegacyCellSample[] cells;
            private readonly float[] floodplain;

            internal RegionResult(
                Int2 origin,
                int size,
                LegacyCellSample[] cells,
                float[] floodplain)
            {
                Origin = origin;
                Size = size;
                this.cells = cells;
                this.floodplain = floodplain;
            }

            internal Int2 Origin { get; }
            internal int Size { get; }

            internal LegacyCellSample Get(
                Int2 worldPosition,
                ChunkGenerationTopologySnapshot topology)
            {
                int index = GetIndex(worldPosition, topology);
                return index >= 0 ? cells[index] : default;
            }

            internal double GetFloodplain(
                Int2 worldPosition,
                ChunkGenerationTopologySnapshot topology)
            {
                int index = GetIndex(worldPosition, topology);
                return index >= 0 ? floodplain[index] : 0d;
            }

            private int GetIndex(
                Int2 worldPosition,
                ChunkGenerationTopologySnapshot topology)
            {
                if (topology.IsWrapped)
                {
                    Int2 center = new Int2(Origin.X + Size / 2, Origin.Y + Size / 2);
                    worldPosition = new Int2(
                        center.X + ShortestDelta(
                            center.X, worldPosition.X, topology, horizontal: true),
                        center.Y + ShortestDelta(
                            center.Y, worldPosition.Y, topology, horizontal: false));
                }

                int localX = worldPosition.X - Origin.X;
                int localY = worldPosition.Y - Origin.Y;
                return (uint)localX < (uint)Size && (uint)localY < (uint)Size
                    ? localY * Size + localX
                    : -1;
            }
        }

        private readonly struct LegacyCellSample
        {
            internal LegacyCellSample(
                GeneratedHydrologyKind kind,
                double flow,
                double depth,
                double surfaceLevel)
            {
                Kind = kind;
                Flow = (float)Math.Max(0d, flow);
                Depth = (float)Clamp01(depth);
                SurfaceLevel = (float)Clamp01(surfaceLevel);
            }

            internal GeneratedHydrologyKind Kind { get; }
            internal float Flow { get; }
            internal float Depth { get; }
            internal float SurfaceLevel { get; }
        }

        private readonly struct BasinResult
        {
            internal BasinResult(
                List<Int2> cells,
                double waterLevel,
                bool hasOutlet,
                Int2 outlet)
            {
                Cells = cells;
                WaterLevel = waterLevel;
                HasOutlet = hasOutlet;
                Outlet = outlet;
            }

            internal List<Int2> Cells { get; }
            internal double WaterLevel { get; }
            internal bool HasOutlet { get; }
            internal Int2 Outlet { get; }
        }

        private readonly struct FrontierCell : IComparable<FrontierCell>
        {
            internal FrontierCell(Int2 position, double height)
            {
                Position = position;
                Height = height;
            }

            internal Int2 Position { get; }
            internal double Height { get; }

            public int CompareTo(FrontierCell other)
            {
                int heightComparison = Height.CompareTo(other.Height);
                if (heightComparison != 0)
                    return heightComparison;
                int xComparison = Position.X.CompareTo(other.Position.X);
                return xComparison != 0 ? xComparison : Position.Y.CompareTo(other.Position.Y);
            }
        }

        #endregion

        #region 纯工具

        private static Int2 GetRegionCoordinate(
            ChunkGenerationTopologySnapshot topology,
            Int2 worldPosition,
            int regionSize)
        {
            worldPosition = Normalize(topology, worldPosition);
            Int2 anchor = topology.IsWrapped ? topology.Min : default;
            return new Int2(
                FloorDiv(worldPosition.X - anchor.X, regionSize),
                FloorDiv(worldPosition.Y - anchor.Y, regionSize));
        }

        private static Int2 Normalize(ChunkGenerationTopologySnapshot topology, Int2 position) =>
            new Int2(topology.NormalizeX(position.X), topology.NormalizeY(position.Y));

        private static int ShortestDelta(
            int from,
            int to,
            ChunkGenerationTopologySnapshot topology,
            bool horizontal)
        {
            if (!topology.IsWrapped)
                return to - from;
            int normalizedFrom = horizontal
                ? topology.NormalizeX(from)
                : topology.NormalizeY(from);
            int normalizedTo = horizontal
                ? topology.NormalizeX(to)
                : topology.NormalizeY(to);
            int delta = normalizedTo - normalizedFrom;
            int span = horizontal ? topology.Span.X : topology.Span.Y;
            if (delta > span / 2)
                delta -= span;
            else if (delta < -span / 2)
                delta += span;
            return delta;
        }

        private static void MergeCell(
            Dictionary<Int2, GeneratedHydrologyCell> cells,
            Int2 position,
            GeneratedHydrologyCell candidate)
        {
            if (!cells.TryGetValue(position, out GeneratedHydrologyCell current))
            {
                cells.Add(position, candidate);
                return;
            }
            if (current.Kind == GeneratedHydrologyKind.Lake &&
                candidate.Kind != GeneratedHydrologyKind.Lake)
            {
                return;
            }
            if (current.Kind == candidate.Kind && current.Depth > candidate.Depth)
                return;
            cells[position] = candidate;
        }

        private static void SetFloodplainStrength(
            Dictionary<Int2, double> cells,
            Int2 position,
            double strength)
        {
            strength = Clamp01(strength);
            if (!cells.TryGetValue(position, out double current) || strength > current)
                cells[position] = strength;
        }

        private static ulong CalculateConfigurationHash(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings)
        {
            ulong hash = 14695981039346656037UL;
            AddHash(ref hash, request.Profile.Signature);
            AddHash(ref hash, settings.RiverHydrologyRegionSize);
            AddHash(ref hash, settings.RiverRunoffCellSize);
            AddHash(ref hash, settings.RiverRunoffSampleStride);
            AddHash(ref hash, settings.RiverMaxTraceSteps);
            AddHash(ref hash, settings.SeaLevel);
            AddHash(ref hash, settings.TerrainScale);
            AddHash(ref hash, settings.HeightOctaves);
            AddHash(ref hash, settings.ClimateScale);
            AddHash(ref hash, settings.ClimateOctaves);
            AddHash(ref hash, settings.RiverInfiltrationFloor);
            AddHash(ref hash, settings.RiverStartFlow);
            AddHash(ref hash, settings.RiverFullWidthFlow);
            AddHash(ref hash, settings.RiverMaxWidth);
            AddHash(ref hash, settings.RiverMeanderTieTolerance);
            AddHash(ref hash, settings.RiverFloodplainStartFlow);
            AddHash(ref hash, settings.RiverFloodplainMaxRadius);
            AddHash(ref hash, settings.RiverFloodplainMaxSlope);
            AddHash(ref hash, settings.RiverDepthMin);
            AddHash(ref hash, settings.RiverDepthMax);
            AddHash(ref hash, settings.RiverMinLakeCells);
            AddHash(ref hash, settings.RiverMaxLakeCells);
            AddHash(ref hash, settings.RiverMaxLakeLevelRise);
            AddHash(ref hash, settings.RiverLakeMinFlow);
            return hash;
        }

        private static void AddHash(ref ulong hash, int value) =>
            AddHash(ref hash, unchecked((ulong)(uint)value));

        private static void AddHash(ref ulong hash, double value) =>
            AddHash(ref hash, unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));

        private static void AddHash(ref ulong hash, ulong value)
        {
            const ulong prime = 1099511628211UL;
            unchecked
            {
                for (int shift = 0; shift < 64; shift += 8)
                {
                    hash ^= (byte)(value >> shift);
                    hash *= prime;
                }
            }
        }

        private static uint Hash(int seed, int x, int y, uint salt)
        {
            unchecked
            {
                ulong value = (ulong)(uint)seed ^ salt;
                value ^= (ulong)(uint)x * 0x9e3779b185ebca87UL;
                value ^= (ulong)(uint)y * 0xc2b2ae3d27d4eb4fUL;
                value ^= value >> 30;
                value *= 0xbf58476d1ce4e5b9UL;
                value ^= value >> 27;
                value *= 0x94d049bb133111ebUL;
                value ^= value >> 31;
                return (uint)(value >> 32);
            }
        }

        private static double Hash01(int seed, int x, int y, uint salt) =>
            Hash(seed, x, y, salt) / (double)uint.MaxValue;

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder != 0 && ((remainder < 0) != (divisor < 0))
                ? quotient - 1
                : quotient;
        }

        private static double InverseLerp(double from, double to, double value) =>
            Clamp01((value - from) / Math.Max(0.0001d, to - from));

        private static double Lerp(double from, double to, double value) =>
            from + (to - from) * value;

        private static double Clamp01(double value) =>
            value < 0d ? 0d : value > 1d ? 1d : value;

        #endregion
    }
}
