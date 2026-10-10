using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Unity.Mathematics;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 群系定位进度与任务隔离

        private bool isBiomeSearchGenerator;

        public sealed class BiomeSearchSnapshot
        {
            internal BiomeSearchSnapshot(string stage, long sampledCount, int stride)
            {
                Stage = stage;
                SampledCount = sampledCount;
                Stride = stride;
            }
            public string Stage { get; }
            public long SampledCount { get; }
            public int Stride { get; }
        }

        // 后台只发布不可变快照，UI 在主线程读取最新进度。
        public sealed class BiomeSearchProgress
        {
            private BiomeSearchSnapshot latest = new("准备搜索", 0, 0);
            public BiomeSearchSnapshot Latest => Volatile.Read(ref latest);
            internal void Report(string stage, long sampledCount, int stride) =>
                Volatile.Write(ref latest, new BiomeSearchSnapshot(stage, sampledCount, stride));
        }

        /// <summary>持续搜索到命中、完整扫描有限世界或被取消，不使用时间和采样数量上限。</summary>
        public BiomeSearchResult FindSurfaceBiome(string dimensionId, int worldSeed,
            ChunkGenerationProfileSnapshot profile, ChunkGenerationTopologySnapshot topology,
            Int2 anchor, SurfaceBiomeKind biome, CancellationToken cancellationToken,
            long worldEpoch, BiomeSearchProgress progress = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (worldEpoch <= 0) throw new ArgumentOutOfRangeException(nameof(worldEpoch));
            if (!Enum.IsDefined(typeof(SurfaceBiomeKind), biome))
                throw new ArgumentOutOfRangeException(nameof(biome));
            dimensionId = string.IsNullOrWhiteSpace(dimensionId) ? "surface" : dimensionId;
            if (profile.Settings.Mode != ChunkGenerationMode.Surface ||
                dimensionId.IndexOf("cave", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                progress?.Report("当前维度不是地表", 0, 0);
                return new BiomeSearchResult(false, anchor, 0);
            }

            // 定位复用已完成的河网，但新计算独立取消，不能留下搜索专用预热任务。
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, sharedHydrologyCancellation.Token);
            var queryGenerator = new DeterministicChunkGenerator(liquidTypes, terrainDecorator) { isBiomeSearchGenerator = true };
            queryGenerator.sharedHydrologyCancellation.Dispose();
            queryGenerator.sharedHydrologyCancellation = lifetime;
            foreach (KeyValuePair<HeightDrivenRegionKey, Lazy<MacroHydrologyRegion>> pair in heightDrivenRegionCache)
            {
                if (!pair.Value.IsValueCreated) continue;
                queryGenerator.heightDrivenRegionCache.TryAdd(pair.Key, pair.Value);
                queryGenerator.heightDrivenCacheOrder.Enqueue(pair.Key);
            }
            return new BiomeLocator(queryGenerator, dimensionId, worldSeed == 0 ? 1 : worldSeed,
                profile, topology, anchor, biome, lifetime.Token, worldEpoch, progress).Run();
        }

        #endregion

        #region 分级覆盖与特征定位

        private sealed class BiomeLocator
        {
            private readonly DeterministicChunkGenerator generator;
            private readonly string dimension;
            private readonly int seed;
            private readonly ChunkGenerationProfileSnapshot profile;
            private readonly ChunkGenerationTopologySnapshot topology;
            private readonly Int2 anchor;
            private readonly SurfaceBiomeKind biome;
            private readonly CancellationToken token;
            private readonly long epoch;
            private readonly BiomeSearchProgress progress;
            private readonly Stopwatch timer = Stopwatch.StartNew();
            private readonly Dictionary<Int2, GeneratedHydrologyMap> maps = new();
            private readonly Queue<Int2> mapOrder = new();
            private readonly HashSet<Int2> rejectedRiverChunks = new();
            private long sampledCount;
            private long lastReport;
            private string stage;
            private int stride;
            private Int2 found;

            internal BiomeLocator(DeterministicChunkGenerator generator, string dimension,
                int seed, ChunkGenerationProfileSnapshot profile, ChunkGenerationTopologySnapshot topology,
                Int2 anchor, SurfaceBiomeKind biome, CancellationToken token, long epoch,
                BiomeSearchProgress progress)
            {
                this.generator = generator;
                this.dimension = dimension;
                this.seed = seed;
                this.profile = profile;
                this.topology = topology;
                this.anchor = new Int2(topology.NormalizeX(anchor.X), topology.NormalizeY(anchor.Y));
                this.biome = biome;
                this.token = token;
                this.epoch = epoch;
                this.progress = progress;
            }

            internal BiomeSearchResult Run()
            {
                token.ThrowIfCancellationRequested();
                if (biome == SurfaceBiomeKind.River && !profile.Settings.RiverEnabled)
                    return Finish(false, "当前生成配置已关闭河流");
                if (biome == SurfaceBiomeKind.Snow && profile.Settings.SnowRegionsEnabled &&
                    profile.Settings.SnowRegionChance <= 0d)
                    return Finish(false, "当前生成配置的雪原出现概率为零");

                SetStage("搜索附近", 16);
                if (biome != SurfaceBiomeKind.River && SearchGrid((long)anchor.X - 256, (long)anchor.Y - 256,
                        (long)anchor.X + 256, (long)anchor.Y + 256, 16, 0))
                    return Finish(true, "已找到目标群系");

                if (topology.IsWrapped)
                {
                    long maxX = (long)topology.Min.X + topology.Span.X - 1;
                    long maxY = (long)topology.Min.Y + topology.Span.Y - 1;
                    if (SearchArea(topology.Min.X, topology.Min.Y, maxX, maxY))
                        return Finish(true, "已找到目标群系");
                    return Finish(false, "已完整检查当前世界的目标群系生成位置");
                }

                // 无限世界按距离扩大范围，每一轮都会细查，远处也不会受旧 8192 格限制。
                for (long radius = 1024; ; radius = Math.Min(radius * 4, 1L << 32))
                {
                    token.ThrowIfCancellationRequested();
                    long minX = Math.Max(int.MinValue, (long)anchor.X - radius);
                    long minY = Math.Max(int.MinValue, (long)anchor.Y - radius);
                    long maxX = Math.Min(int.MaxValue, (long)anchor.X + radius);
                    long maxY = Math.Min(int.MaxValue, (long)anchor.Y + radius);
                    if (SearchArea(minX, minY, maxX, maxY))
                        return Finish(true, "已找到目标群系");
                    if (radius == 1L << 32)
                        return Finish(false, "已完整检查可表示的世界坐标范围");
                }
            }

            private bool SearchArea(long minX, long minY, long maxX, long maxY)
            {
                if (biome == SurfaceBiomeKind.Snow && profile.Settings.SnowRegionsEnabled)
                    return SearchSnowRegions(minX, minY, maxX, maxY);
                if (biome == SurfaceBiomeKind.River &&
                    (profile.Settings.RiverEnabled || profile.Settings.LakeBasinEnabled) &&
                    SearchRiverRegions(minX, minY, maxX, maxY))
                    return true;

                int previous = 0;
                foreach (int step in new[] { 128, 32, 8, 1 })
                {
                    SetStage($"覆盖搜索范围 {maxX - minX + 1} × {maxY - minY + 1}", step);
                    if (SearchGrid(minX, minY, maxX, maxY, step, previous)) return true;
                    previous = step;
                }
                return false;
            }

            private bool SearchGrid(long minX, long minY, long maxX, long maxY, int step, int previous)
            {
                foreach (Int2 candidate in Grid(minX, minY, maxX, maxY, anchor, step, previous, token))
                    if (CheckCandidate(candidate)) return true;
                return false;
            }

            private bool CheckCandidate(Int2 candidate)
            {
                token.ThrowIfCancellationRequested();
                candidate = new Int2(topology.NormalizeX(candidate.X), topology.NormalizeY(candidate.Y));
                sampledCount++;
                Report();
                ChunkGenerationRequest request = Request(candidate);
                SurfaceBiomeKind baseBiome = SampleBaseSurfaceBiome(request, profile.Settings,
                    candidate.X, candidate.Y, out _, out _);
                if (biome == SurfaceBiomeKind.River)
                {
                    if (baseBiome == SurfaceBiomeKind.Ocean) return false;
                    return CheckRiverChunk(request);
                }
                if (generator.terrainDecorator == null && baseBiome != biome &&
                    !(biome is SurfaceBiomeKind.Grassland or SurfaceBiomeKind.Forest &&
                      baseBiome is SurfaceBiomeKind.Grassland or SurfaceBiomeKind.Forest)) return false;
                return CheckFinalCell(request, Map(request), candidate);
            }

            private bool CheckFinalCell(ChunkGenerationRequest request, GeneratedHydrologyMap map, Int2 cell)
            {
                token.ThrowIfCancellationRequested();
                SurfaceCellOutput output = generator.SampleSurfaceCellOutput(request, profile.Settings,
                    map, 0, 0, cell.X, cell.Y, generator.liquidTypes);
                if (output.Cell.BiomeId != (int)biome || !output.Cell.IsWalkable ||
                    generator.liquidTypes.GetId(output.LiquidTypeIndex) == LiquidTypeCatalog.LavaId)
                    return false;
                found = cell;
                return true;
            }

            private bool CheckRiverChunk(ChunkGenerationRequest request)
            {
                Int2 origin = request.Address.ChunkOrigin;
                if (rejectedRiverChunks.Contains(origin)) return false;
                GeneratedHydrologyMap map = Map(request);
                if (map == null)
                {
                    rejectedRiverChunks.Add(origin);
                    return false;
                }
                for (int y = 0; y < profile.Height; y++)
                for (int x = 0; x < profile.Width; x++)
                {
                    token.ThrowIfCancellationRequested();
                    var cell = new Int2(topology.NormalizeX(origin.X + x), topology.NormalizeY(origin.Y + y));
                    if (!map.TryGet(cell.X, cell.Y, out GeneratedHydrologyCell water) ||
                        water.Kind != GeneratedHydrologyKind.River) continue;
                    sampledCount++;
                    Report();
                    if (CheckFinalCell(request, map, cell)) return true;
                }
                rejectedRiverChunks.Add(origin);
                return false;
            }

            private ChunkGenerationRequest Request(Int2 cell) => new(epoch,
                new WorldAddress(dimension, ResolveSearchChunkOrigin(cell, profile, topology)),
                seed, 1, profile, topology);

            // 局部水文缓存有界，长时间搜索不能把整个世界的区块水文留在内存。
            private GeneratedHydrologyMap Map(ChunkGenerationRequest request)
            {
                Int2 origin = request.Address.ChunkOrigin;
                if (maps.TryGetValue(origin, out GeneratedHydrologyMap map)) return map;
                map = generator.BuildSurfaceHydrologyMap(request, profile.Settings, token);
                if (maps.Count >= 64)
                {
                    Int2 oldest = mapOrder.Dequeue();
                    maps.Remove(oldest);
                    rejectedRiverChunks.Remove(oldest);
                }
                maps.Add(origin, map);
                mapOrder.Enqueue(origin);
                return map;
            }

            private bool SearchSnowRegions(long minX, long minY, long maxX, long maxY)
            {
                ChunkGenerationSettingsSnapshot settings = profile.Settings;
                double stepX = settings.SnowRegionSize, stepY = settings.SnowRegionSize;
                long regionMinX, regionMinY, regionMaxX, regionMaxY;
                var reference = Request(anchor);
                if (topology.IsWrapped)
                {
                    regionMinX = regionMinY = 0;
                    regionMaxX = Math.Max(1, topology.Span.X / settings.SnowRegionSize) - 1;
                    regionMaxY = Math.Max(1, topology.Span.Y / settings.SnowRegionSize) - 1;
                    stepX = topology.Span.X / (double)(regionMaxX + 1);
                    stepY = topology.Span.Y / (double)(regionMaxY + 1);
                }
                else
                {
                    regionMinX = (long)Math.Floor(minX / stepX) - 1;
                    regionMinY = (long)Math.Floor(minY / stepY) - 1;
                    regionMaxX = (long)Math.Floor(maxX / stepX) + 1;
                    regionMaxY = (long)Math.Floor(maxY / stepY) + 1;
                }
                var regionAnchor = new Int2((int)Math.Floor((anchor.X - (topology.IsWrapped ? topology.Min.X : 0)) / stepX),
                    (int)Math.Floor((anchor.Y - (topology.IsWrapped ? topology.Min.Y : 0)) / stepY));
                int previous = 0;
                foreach (int step in new[] { 32, 8, 1 })
                {
                    SetStage("按种子定位雪原区域", step);
                    foreach (Int2 region in Grid(regionMinX, regionMinY, regionMaxX, regionMaxY,
                                 regionAnchor, 1, 0, token))
                    {
                        token.ThrowIfCancellationRequested();
                        sampledCount++;
                        Report();
                        if (!TryResolveSnowRegion(reference, settings, region.X, region.Y,
                                out double2 center, out double radius, out _)) continue;
                        long left = Math.Max(int.MinValue, (long)Math.Floor(center.x - radius * 1.08d));
                        long bottom = Math.Max(int.MinValue, (long)Math.Floor(center.y - radius * 1.08d));
                        long right = Math.Min(int.MaxValue, (long)Math.Ceiling(center.x + radius * 1.08d));
                        long top = Math.Min(int.MaxValue, (long)Math.Ceiling(center.y + radius * 1.08d));
                        if (SearchGrid(left, bottom, right, top, step, previous)) return true;
                    }
                    previous = step;
                }
                return false;
            }

            private bool SearchRiverRegions(long minX, long minY, long maxX, long maxY)
            {
                ChunkGenerationSettingsSnapshot settings = profile.Settings;
                int size = settings.RiverHydrologyRegionSize;
                int startX = topology.IsWrapped ? topology.Min.X : 0;
                int startY = topology.IsWrapped ? topology.Min.Y : 0;
                var regionAnchor = new Int2(FloorDiv(anchor.X - startX, size), FloorDiv(anchor.Y - startY, size));
                long regionMinX = (long)Math.Floor((minX - startX) / (double)size);
                long regionMinY = (long)Math.Floor((minY - startY) / (double)size);
                long regionMaxX = (long)Math.Floor((maxX - startX) / (double)size);
                long regionMaxY = (long)Math.Floor((maxY - startY) / (double)size);
                SetStage("沿正式河网定位河道", 0);
                foreach (Int2 region in Grid(regionMinX, regionMinY, regionMaxX, regionMaxY,
                             regionAnchor, 1, 0, token))
                {
                    token.ThrowIfCancellationRequested();
                    long x = (long)startX + (long)region.X * size;
                    long y = (long)startY + (long)region.Y * size;
                    if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue) continue;
                    var request = Request(new Int2((int)x, (int)y));
                    sampledCount++;
                    Report(true);
                    MacroHydrologyRegion macro = generator.GetHeightDrivenMacroRegion(request, settings, token);
                    var nodes = new List<int>();
                    for (int i = 0; i < macro.Count; i++)
                        if (macro.Visible[i] && macro.Downstream[i] >= 0) nodes.Add(i);
                    nodes.Sort((a, b) => Distance(macro.Positions[a]).CompareTo(Distance(macro.Positions[b])));
                    var checkedChunks = new HashSet<Int2>();
                    int margin = (int)Math.Ceiling(settings.RiverMeanderScale * 0.42d * settings.RiverMeanderStrength + settings.RiverMaxWidth +
                        settings.RiverRunoffCellSize / 3d + Math.Max(profile.Width, profile.Height));
                    foreach (int index in nodes)
                    {
                        token.ThrowIfCancellationRequested();
                        Int2 source = macro.Positions[index];
                        Int2 target = macro.Positions[macro.Downstream[index]];
                        int2 delta = topology.ToDomain().ShortestDelta(new int2(source.X, source.Y), new int2(target.X, target.Y));
                        var center = new Int2(topology.NormalizeX(source.X + delta.x / 2),
                            topology.NormalizeY(source.Y + delta.y / 2));
                        foreach (Int2 cell in Grid((long)center.X - margin, (long)center.Y - margin,
                                     (long)center.X + margin, (long)center.Y + margin, center,
                                     Math.Min(profile.Width, profile.Height), 0, token))
                        {
                            ChunkGenerationRequest cellRequest = Request(new Int2(topology.NormalizeX(cell.X), topology.NormalizeY(cell.Y)));
                            if (!checkedChunks.Add(cellRequest.Address.ChunkOrigin)) continue;
                            sampledCount++;
                            Report();
                            if (CheckRiverChunk(cellRequest)) return true;
                        }
                    }
                }
                return false;
            }

            private double Distance(Int2 cell)
            {
                int2 delta = topology.ToDomain().ShortestDelta(new int2(anchor.X, anchor.Y), new int2(cell.X, cell.Y));
                return delta.x * (double)delta.x + delta.y * (double)delta.y;
            }

            private void SetStage(string value, int step)
            {
                stage = value;
                stride = step;
                Report(true);
            }
            private void Report(bool force = false)
            {
                if (!force && timer.ElapsedMilliseconds - lastReport < 200) return;
                lastReport = timer.ElapsedMilliseconds;
                progress?.Report(stage, sampledCount, stride);
            }
            private BiomeSearchResult Finish(bool success, string message)
            {
                SetStage(message, 0);
                return new BiomeSearchResult(success, success ? found : anchor, sampledCount);
            }
        }

        #endregion

        #region 无遗漏网格遍历

        // 由近到远遍历网格外围，细化时跳过上一轮已查的点，最后逐格覆盖且不积累候选列表。
        private static IEnumerable<Int2> Grid(long minX, long minY, long maxX, long maxY,
            Int2 anchor, int stride, int previousStride, CancellationToken token)
        {
            minX = Math.Max(int.MinValue, minX);
            minY = Math.Max(int.MinValue, minY);
            maxX = Math.Min(int.MaxValue, maxX);
            maxY = Math.Min(int.MaxValue, maxY);
            if (maxX < minX || maxY < minY) yield break;
            long countX = (maxX - minX) / stride + 1;
            long countY = (maxY - minY) / stride + 1;
            long cx = Math.Max(0, Math.Min(countX - 1, ((long)anchor.X - minX) / stride));
            long cy = Math.Max(0, Math.Min(countY - 1, ((long)anchor.Y - minY) / stride));
            long maxRing = Math.Max(Math.Max(cx, countX - 1 - cx), Math.Max(cy, countY - 1 - cy));
            for (long ring = 0; ring <= maxRing; ring++)
            {
                token.ThrowIfCancellationRequested();
                long left = Math.Max(0, cx - ring), right = Math.Min(countX - 1, cx + ring);
                long bottom = cy - ring, top = cy + ring;
                if (bottom >= 0)
                    for (long x = left; x <= right; x++)
                        if (Include(x, bottom)) yield return Cell(x, bottom);
                if (ring > 0 && top < countY)
                    for (long x = left; x <= right; x++)
                        if (Include(x, top)) yield return Cell(x, top);
                long low = Math.Max(0, bottom + 1), high = Math.Min(countY - 1, top - 1);
                if (cx - ring >= 0)
                    for (long y = low; y <= high; y++)
                        if (Include(cx - ring, y)) yield return Cell(cx - ring, y);
                if (ring > 0 && cx + ring < countX)
                    for (long y = low; y <= high; y++)
                        if (Include(cx + ring, y)) yield return Cell(cx + ring, y);
            }
            bool Include(long x, long y)
            {
                token.ThrowIfCancellationRequested();
                return previousStride <= 0 ||
                    (x * stride % previousStride != 0 || y * stride % previousStride != 0);
            }
            Int2 Cell(long x, long y) => new((int)(minX + x * stride), (int)(minY + y * stride));
        }

        #endregion
    }
}
