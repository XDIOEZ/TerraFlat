using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 不使用 Unity、可以放到后台运行的区块生成器。
    /// 所有“随机”结果都来自世界种子和坐标，所以输入相同时，无论先生成哪个区块，结果都一样。
    /// </summary>
    public sealed partial class DeterministicChunkGenerator : IChunkPureGenerator,
        IChunkEcologyNeighborhoodTagResolver
    {
        /// <summary>纯区块生成规则版本；气候、群系、河流或生态空间分布规则改变时递增。</summary>
        public const int CurrentGenerationSignature = 73;

        private readonly LiquidTypeCatalog liquidTypes;
        private readonly Action<ChunkGenerationRequest, ChunkTerrainBuffer, CancellationToken> terrainDecorator;
        /// <summary>资源就绪后注入会话液体表；离线纯算法测试可以使用本体最小目录。</summary>
        public DeterministicChunkGenerator(LiquidTypeCatalog liquidTypes = null,
            Action<ChunkGenerationRequest, ChunkTerrainBuffer, CancellationToken> terrainDecorator = null)
        {
            this.liquidTypes = liquidTypes ?? LiquidTypeCatalog.BuiltIn;
            this.terrainDecorator = terrainDecorator;
        }

        #region 水文区域共享缓存与调度

        private readonly ConcurrentDictionary<HeightDrivenRegionKey, Lazy<MacroHydrologyRegion>>
            heightDrivenRegionCache = new();
        private readonly ConcurrentQueue<HeightDrivenRegionKey> heightDrivenCacheOrder = new();
        private CancellationTokenSource sharedHydrologyCancellation = new();
        private long retiredWorldEpoch = long.MinValue;

        /// <summary>切换世界时取消当前宏观水文并释放旧纪元缓存。</summary>
        public void BeginNewWorldHydrologyEpoch(long retiredEpoch)
        {
            Volatile.Write(ref retiredWorldEpoch, retiredEpoch);
            CancellationTokenSource previous = Interlocked.Exchange(ref sharedHydrologyCancellation,
                new CancellationTokenSource());
            previous.Cancel();
            heightDrivenRegionCache.Clear();
            precipitationCache.Clear();
            while (precipitationCacheOrder.TryDequeue(out _)) { }
            lavaBasins.Clear();
            while (lavaBasinOrder.TryDequeue(out _)) { }
            while (heightDrivenCacheOrder.TryDequeue(out _)) { }
        }

        /// <summary>供纯算法诊断确认新版水文缓存保持有界。</summary>
        internal int CachedHeightDrivenRegionCount => heightDrivenRegionCache.Count;

        /// <summary>冷河网区域串行领取一个生成名额；缓存就绪后同区域区块可并行完成。</summary>
        public object GetPendingGenerationGroupKey(ChunkGenerationRequest request)
        {
            ChunkGenerationSettingsSnapshot settings = request.Profile.Settings;
            if ((!settings.RiverEnabled && !settings.LakeBasinEnabled) ||
                settings.Mode == ChunkGenerationMode.Cave ||
                request.Address.DimensionId.IndexOf("cave", StringComparison.OrdinalIgnoreCase) >= 0)
                return null;

            HeightDrivenRegionDescriptor region = ResolveHeightDrivenRegion(request, settings);
            HeightDrivenRegionKey key = CreateHeightDrivenRegionKey(request, region);
            return heightDrivenRegionCache.TryGetValue(key, out Lazy<MacroHydrologyRegion> cached) &&
                   cached.IsValueCreated ? null : key;
        }

        #endregion

        // 草地状态用 1 和 2；数字 0 专门表示“这个格子还没处理过”，方便排查问题。
        private const byte GrassEmpty = ChunkTerrainData.GrassEmpty;
        private const byte GrassPresent = ChunkTerrainData.GrassPresent;
        // 这个数字表示“随机地形算法的版本”。只有确实要让旧世界换一种地形排列时才增加它。
        // 普通设置变化不应该改它，否则同一个旧世界的山川和气候会被整个重新随机。
        private const uint NoiseLayoutVersion = 5u;

        /// <summary>
        /// 从头生成一个完整区块。通常由后台工作人员调用。
        /// 如果中途取消或出错，会把临时内存清理掉，不留下垃圾数据。
        /// </summary>
        public ChunkGenerationResult Generate(ChunkGenerationRequest request,
            CancellationToken cancellationToken, ChunkGenerationTiming timing = null)
        {
            ChunkGenerationProfileSnapshot profile = request.Profile;
            var terrain = new ChunkTerrainBuffer(profile.Width, profile.Height, liquidTypes);
            ChunkGenerationTiming previousTiming = ChunkGenerationTiming.CurrentOnThread;
            ChunkGenerationTiming.CurrentOnThread = timing;
            try
            {
                bool cave = IsCaveGeneration(request);
                PopulateTerrain(request, terrain, cave, cancellationToken, timing);
                ChunkEcologyData ecology;
                using (timing?.MeasureStage("ecology") ?? default)
                {
                    if (cave)
                    {
                        // 洞穴阶段合并当前 Profile 的植物规则、入口、藤蔓与矿物放置记录。
                        ecology = CaveGenerationFeatureGenerator.GenerateCave(
                            request, terrain, cancellationToken, this);
                    }
                    else
                    {
                        ChunkEcologyData surfaceEcology;
                        using (timing?.MeasureStage("ecology.input") ?? default)
                            surfaceEcology = ChunkEcologyGenerator.Generate(
                                request, terrain, profile.EcologyGlobalMultiplier,
                                profile.EcologyRules, cancellationToken, this);
                        // 天然矿洞入口优先占用候选格，避免与树木、灌木等生态物重叠。
                        ecology = CaveGenerationFeatureGenerator.AppendSurfacePortals(
                            request, terrain, surfaceEcology);
                    }
                }
                return new ChunkGenerationResult(request, terrain, ecology);
            }
            catch
            {
                terrain.Dispose();
                throw;
            }
            finally
            {
                ChunkGenerationTiming.CurrentOnThread = previousTiming;
            }
        }

        #region 自然物邻区查询

        /// <summary>仅重算邻区地形并查询天然宿主标签，不依赖邻区是否加载，也不会递归生成伴生物。</summary>
        public bool HasHostTagInChunk(ChunkGenerationRequest request, string tag,
            CancellationToken cancellationToken)
        {
            var terrain = new ChunkTerrainBuffer(request.Profile.Width,
                request.Profile.Height, liquidTypes);
            try
            {
                PopulateTerrain(request, terrain, IsCaveGeneration(request),
                    cancellationToken, null);
                return ChunkEcologyGenerator.HasHostTagInChunk(request, terrain,
                    request.Profile.EcologyGlobalMultiplier, request.Profile.EcologyRules,
                    tag, cancellationToken);
            }
            finally { terrain.Dispose(); }
        }

        /// <summary>复用正式生成的地形、水文与结构顺序，保证邻区花朵查询与实际结果一致。</summary>
        private void PopulateTerrain(ChunkGenerationRequest request,
            ChunkTerrainBuffer terrain, bool cave, CancellationToken cancellationToken,
            ChunkGenerationTiming timing)
        {
            ChunkGenerationProfileSnapshot profile = request.Profile;
            ChunkGenerationSettingsSnapshot settings = profile.Settings;
            GeneratedHydrologyMap riverMap = cave ? null :
                BuildSurfaceHydrologyMap(request, settings, cancellationToken, timing);
            using (timing?.MeasureStage("terrain.cells") ?? default)
            {
                if (cave)
                {
                    for (int y = 0; y < profile.Height; y++)
                    for (int x = 0; x < profile.Width; x++)
                    {
                        if (((y * profile.Width + x) & 63) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        int worldX = request.Address.ChunkOrigin.X + x;
                        int worldY = request.Address.ChunkOrigin.Y + y;
                        GenerateCaveCell(request, settings, terrain, x, y, worldX, worldY);
                    }
                }
                else
                    GenerateSurfaceChunk(request, settings, terrain, riverMap,
                        cancellationToken, timing);
            }
            if (!cave)
            {
                using (timing?.MeasureStage("terrain.structures") ?? default)
                    ApplyStructures(request, settings, terrain, cancellationToken);
            }
            // 正式生成与邻区查询共用扩展，生态只读取最终地形。
            terrainDecorator?.Invoke(request, terrain, cancellationToken);
        }

        /// <summary>洞穴由 Profile 模式或维度标识决定，与正式区块生成保持同一判断。</summary>
        private static bool IsCaveGeneration(ChunkGenerationRequest request) =>
            request.Profile.Settings.Mode == ChunkGenerationMode.Cave ||
            request.Address.DimensionId.IndexOf("cave", StringComparison.OrdinalIgnoreCase) >= 0;

        #endregion

        #region 地表位置查询

        public readonly struct BiomeSearchResult
        {
            public BiomeSearchResult(bool found, Int2 cell, long sampledCount)
            {
                Found = found;
                Cell = cell;
                SampledCount = sampledCount;
            }

            public bool Found { get; }
            public Int2 Cell { get; }
            public long SampledCount { get; }
        }

        /// <summary>
        /// 使用与正式区块相同的 Profile、水文和单格地形规则寻找可走陆地。
        /// 结构不修改液体和可走标记，生态只生成物品放置记录，因此无需生成完整区块。
        /// </summary>
        public bool TryFindWalkableSurfaceNear(
            string dimensionId,
            int worldSeed,
            ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology,
            Int2 anchor,
            int maxRadius,
            int sampleBudget,
            out Int2 worldCell,
            CancellationToken cancellationToken = default)
        {
            return TryFindWalkableSurfaceNear(dimensionId, worldSeed, profile,
                topology, anchor, maxRadius, sampleBudget, out worldCell,
                cancellationToken, 1);
        }

        /// <summary>指定正式世界纪元，允许出生搜索与随后的区块生成复用同一水文区域缓存。</summary>
        public bool TryFindWalkableSurfaceNear(
            string dimensionId,
            int worldSeed,
            ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology,
            Int2 anchor,
            int maxRadius,
            int sampleBudget,
            out Int2 worldCell,
            CancellationToken cancellationToken,
            long worldEpoch)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            if (worldEpoch <= 0)
                throw new ArgumentOutOfRangeException(nameof(worldEpoch));
            if (profile.Settings.Mode != ChunkGenerationMode.Surface)
            {
                worldCell = anchor;
                return false;
            }

            dimensionId = string.IsNullOrWhiteSpace(dimensionId) ? "surface" : dimensionId;
            maxRadius = Math.Max(0, maxRadius);
            sampleBudget = Math.Max(1, sampleBudget);
            worldCell = anchor;
            IReadOnlyList<Int2> candidates = BuildSurfaceSearchCandidates(
                anchor, topology, maxRadius, sampleBudget);
            var requests = new Dictionary<Int2, ChunkGenerationRequest>();
            var hydrologyMaps = new Dictionary<Int2, GeneratedHydrologyMap>();
            using var sampledCell = new ChunkTerrainBuffer(1, 1, liquidTypes);
            foreach (Int2 candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Int2 origin = ResolveSearchChunkOrigin(candidate, profile, topology);
                int localX = candidate.X - origin.X;
                int localY = candidate.Y - origin.Y;
                if ((uint)localX >= (uint)profile.Width ||
                    (uint)localY >= (uint)profile.Height)
                    continue;

                if (!requests.TryGetValue(origin, out ChunkGenerationRequest request))
                {
                    request = new ChunkGenerationRequest(
                        worldEpoch,
                        new WorldAddress(dimensionId, origin),
                        worldSeed == 0 ? 1 : worldSeed,
                        1,
                        profile,
                        topology);
                    requests.Add(origin, request);
                }

                if (SampleHeight(request, profile.Settings, candidate.X, candidate.Y) <
                    profile.Settings.SeaLevel)
                    continue;

                if (!hydrologyMaps.TryGetValue(origin, out GeneratedHydrologyMap riverMap))
                {
                    riverMap = BuildSurfaceHydrologyMap(request, profile.Settings,
                        cancellationToken);
                    hydrologyMaps.Add(origin, riverMap);
                }

                // 单格沿用正式地形生成入口，避免为出生查询重复实现气候、结冰与液体规则。
                GenerateSurfaceCell(request, profile.Settings, sampledCell, riverMap,
                    0, 0, candidate.X, candidate.Y);
                if (sampledCell.GetLiquidDepth(0, 0) > 0f ||
                    !sampledCell.GetCell(0, 0).IsWalkable)
                    continue;

                worldCell = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 先密集检查锚点附近，再把剩余预算均匀铺满完整半径，避免预算被近处海面耗尽。
        /// </summary>
        private static IReadOnlyList<Int2> BuildSurfaceSearchCandidates(
            Int2 anchor,
            ChunkGenerationTopologySnapshot topology,
            int maxRadius,
            int sampleBudget)
        {
            var candidates = new List<Int2>(sampleBudget);
            var visited = new HashSet<Int2>();

            void AddCandidate(int offsetX, int offsetY)
            {
                if (candidates.Count >= sampleBudget)
                    return;

                var candidate = new Int2(
                    topology.NormalizeX(anchor.X + offsetX),
                    topology.NormalizeY(anchor.Y + offsetY));
                if (visited.Add(candidate))
                    candidates.Add(candidate);
            }

            int localRadius = Math.Min(maxRadius, 8);
            for (int radius = 0; radius <= localRadius && candidates.Count < sampleBudget; radius++)
            {
                for (int offsetY = -radius;
                     offsetY <= radius && candidates.Count < sampleBudget;
                     offsetY++)
                for (int offsetX = -radius;
                     offsetX <= radius && candidates.Count < sampleBudget;
                     offsetX++)
                {
                    if (radius > 0 && Math.Abs(offsetX) != radius &&
                        Math.Abs(offsetY) != radius)
                    {
                        continue;
                    }

                    AddCandidate(offsetX, offsetY);
                }
            }

            int remainingBudget = sampleBudget - candidates.Count;
            if (maxRadius <= localRadius || remainingBudget <= 0)
                return candidates;

            int gridSize = Math.Max(1, (int)Math.Floor(Math.Sqrt(remainingBudget)));
            var gridOffsets = new List<Int2>(gridSize * gridSize);
            for (int gridY = 0; gridY < gridSize; gridY++)
            for (int gridX = 0; gridX < gridSize; gridX++)
            {
                int offsetX = (int)Math.Round(
                    -maxRadius + 2d * maxRadius * (gridX + 0.5d) / gridSize,
                    MidpointRounding.AwayFromZero);
                int offsetY = (int)Math.Round(
                    -maxRadius + 2d * maxRadius * (gridY + 0.5d) / gridSize,
                    MidpointRounding.AwayFromZero);
                gridOffsets.Add(new Int2(offsetX, offsetY));
            }

            // 全域网格也按距离由近到远检查，避免明明近处有陆地却先选中远端格子。
            gridOffsets.Sort((left, right) =>
            {
                int leftDistance = Math.Max(Math.Abs(left.X), Math.Abs(left.Y));
                int rightDistance = Math.Max(Math.Abs(right.X), Math.Abs(right.Y));
                int distanceComparison = leftDistance.CompareTo(rightDistance);
                if (distanceComparison != 0)
                    return distanceComparison;
                int yComparison = left.Y.CompareTo(right.Y);
                return yComparison != 0 ? yComparison : left.X.CompareTo(right.X);
            });
            foreach (Int2 offset in gridOffsets)
                AddCandidate(offset.X, offset.Y);

            return candidates;
        }

        /// <summary>按世界拓扑和 Profile 尺寸计算候选格所属的规范区块原点。</summary>
        private static Int2 ResolveSearchChunkOrigin(Int2 position,
            ChunkGenerationProfileSnapshot profile,
            ChunkGenerationTopologySnapshot topology)
        {
            int anchorX = topology.IsWrapped ? topology.Min.X : 0;
            int anchorY = topology.IsWrapped ? topology.Min.Y : 0;
            int originX = anchorX + FloorDiv(position.X - anchorX, profile.Width) * profile.Width;
            int originY = anchorY + FloorDiv(position.Y - anchorY, profile.Height) * profile.Height;
            return new Int2(topology.NormalizeX(originX), topology.NormalizeY(originY));
        }

        #endregion

        #region 地表与高度图采样

        /// <summary>正式区块与出生查询共用统一河网、湖盆及其缓存键。</summary>
        private GeneratedHydrologyMap BuildSurfaceHydrologyMap(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            CancellationToken cancellationToken, ChunkGenerationTiming timing = null)
        {
            if (!settings.RiverEnabled && !settings.LakeBasinEnabled) return null;
            // 湖盆与河流共用降水汇流图，不再另行随机放置大湖或河流源点。
            using (timing?.MeasureStage("hydrology.runoff_basins") ?? default)
                return BuildHeightDrivenRiverMap(request, settings, cancellationToken, timing);
        }

        #region 地表区块批量生成

        /// <summary>气候输入先按区块采样，再批量判群系并写入连续地形数组。</summary>
        private void GenerateSurfaceChunk(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, ChunkTerrainBuffer terrain,
            GeneratedHydrologyMap riverMap, CancellationToken cancellationToken,
            ChunkGenerationTiming timing)
        {
            using var batch = new SurfaceChunkBatch(request, settings);
            using (timing?.MeasureStage("terrain.noise") ?? default)
                batch.SampleCore(cancellationToken);
            int seaWater = terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.SeaWaterId);
            int dirtyWater = terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
            IReadOnlyList<LavaBasin> volcanic = ResolveLavaBasins(
                ExpandGenerationRequest(request, GetHydrologyOutputPadding(request)), cancellationToken);
            int lava = volcanic.Count > 0 ? terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.LavaId) : 0;
            var airHumidity = new GeographicAirHumiditySampler(request, settings, riverMap, volcanic, cancellationToken);
            float humidityBackground = AirHumiditySettings.FromProfile(request.Profile).Background;
            using (timing?.MeasureStage("terrain.biome") ?? default)
            {
                batch.ClassifyCore(cancellationToken);
                for (int y = 0; y < terrain.Height; y++)
                for (int x = 0; x < terrain.Width; x++)
                {
                    int index = y * terrain.Width + x;
                    if ((index & 63) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    batch.Output[index] = BuildSurfaceCell(request, settings, riverMap,
                        batch.GetCore(x, y), batch, x, y,
                        request.Address.ChunkOrigin.X + x,
                        request.Address.ChunkOrigin.Y + y, seaWater, dirtyWater, lava, volcanic);
                    batch.Output[index].AirHumidity = airHumidity.Sample(
                        request.Address.ChunkOrigin.X + x, request.Address.ChunkOrigin.Y + y);
                    batch.Output[index].AirHumidityBackground = humidityBackground;
                }
            }
            using (timing?.MeasureStage("terrain.environment_write") ?? default)
            {
                var layers = new SurfaceEnvironmentWriter(terrain);
                for (int y = 0; y < terrain.Height; y++)
                for (int x = 0; x < terrain.Width; x++)
                {
                    int index = y * terrain.Width + x;
                    if ((index & 63) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    SurfaceCellOutput output = batch.Output[index];
                    terrain.SetCell(x, y, output.Cell);
                    terrain.SetLiquid(x, y, output.LiquidTypeIndex, output.LiquidDepth);
                    terrain.SetGrass(x, y, output.Grass);
                    layers.Write(index, output);
                }
            }
        }

        private struct SurfaceClimateSample
        {
            public double Height;
            public double Temperature;
            public double TemperatureCelsius;
            public bool IsPolarBand;
            public double Precipitation;
            public double WindX;
            public double WindY;
            public SurfaceBiomeKind BaseBiome;
            public bool Classified;
            public bool SnowAllowed;
        }

        private struct SurfaceCellOutput
        {
            public TerrainCell Cell;
            public int LiquidTypeIndex;
            public float LiquidDepth;
            public float SnowDepth;
            public byte Grass;
            public float Height;
            public float Temperature;
            public float TemperatureCelsius;
            public float Precipitation;
            public float WindX;
            public float WindY;
            public float Moisture;
            public float AirHumidity;
            public float AirHumidityBackground;
            public float Mountain;
            public float RiverFlow;
            public float RiverFlowX;
            public float RiverFlowY;
            public float RiverFloodplain;
            public float RiverSurfaceLevel;
            public float RiverKind;
            public float LavaShore;
        }

        /// <summary>固定环境层布局只解析一次名称，热循环只写数组索引。</summary>
        private readonly struct SurfaceEnvironmentWriter
        {
            private readonly float[] height;
            private readonly float[] temperature;
            private readonly float[] temperatureCelsius;
            private readonly float[] precipitation;
            private readonly float[] windX;
            private readonly float[] windY;
            private readonly float[] moisture;
            private readonly float[] airHumidity;
            private readonly float[] airHumidityBackground;
            private readonly float[] mountain;
            private readonly float[] riverFlow;
            private readonly float[] riverFlowX;
            private readonly float[] riverFlowY;
            private readonly float[] riverFloodplain;
            private readonly float[] riverSurfaceLevel;
            private readonly float[] riverKind;
            private readonly float[] structure;
            private readonly float[] lavaShore;
            private readonly float[] grass;
            private readonly float[] snowDepth;

            public SurfaceEnvironmentWriter(ChunkTerrainBuffer terrain)
            {
                height = terrain.GetOrCreateEnvironmentLayer("height");
                temperature = terrain.GetOrCreateEnvironmentLayer("temperature");
                temperatureCelsius = terrain.GetOrCreateEnvironmentLayer("temperature.celsius");
                precipitation = terrain.GetOrCreateEnvironmentLayer("precipitation");
                windX = terrain.GetOrCreateEnvironmentLayer("windX");
                windY = terrain.GetOrCreateEnvironmentLayer("windY");
                moisture = terrain.GetOrCreateEnvironmentLayer("moisture");
                airHumidity = terrain.GetOrCreateEnvironmentLayer(AirHumidityKernel.GeographicLayer);
                airHumidityBackground = terrain.GetOrCreateEnvironmentLayer(AirHumidityKernel.BackgroundLayer);
                mountain = terrain.GetOrCreateEnvironmentLayer("mountain");
                riverFlow = terrain.GetOrCreateEnvironmentLayer("riverFlow");
                riverFlowX = terrain.GetOrCreateEnvironmentLayer("riverFlowX");
                riverFlowY = terrain.GetOrCreateEnvironmentLayer("riverFlowY");
                riverFloodplain = terrain.GetOrCreateEnvironmentLayer("riverFloodplain");
                riverSurfaceLevel = terrain.GetOrCreateEnvironmentLayer("riverSurfaceLevel");
                riverKind = terrain.GetOrCreateEnvironmentLayer("riverKind");
                structure = terrain.GetOrCreateEnvironmentLayer("structure");
                lavaShore = terrain.GetOrCreateEnvironmentLayer("lava.shore");
                grass = terrain.GetOrCreateEnvironmentLayer("grass");
                snowDepth = terrain.GetOrCreateEnvironmentLayer(SnowDepthLayer.LayerId);
            }

            public void Write(int index, SurfaceCellOutput value)
            {
                height[index] = value.Height;
                temperature[index] = value.Temperature;
                temperatureCelsius[index] = value.TemperatureCelsius;
                precipitation[index] = value.Precipitation;
                windX[index] = value.WindX;
                windY[index] = value.WindY;
                moisture[index] = value.Moisture;
                airHumidity[index] = value.AirHumidity;
                airHumidityBackground[index] = value.AirHumidityBackground;
                mountain[index] = value.Mountain;
                riverFlow[index] = value.RiverFlow;
                riverFlowX[index] = value.RiverFlowX;
                riverFlowY[index] = value.RiverFlowY;
                riverFloodplain[index] = value.RiverFloodplain;
                riverSurfaceLevel[index] = value.RiverSurfaceLevel;
                riverKind[index] = value.RiverKind;
                structure[index] = 0f;
                lavaShore[index] = value.LavaShore;
                grass[index] = value.Grass == GrassPresent ? 1f : 0f;
                snowDepth[index] = SnowDepthLayer.Quantize(value.SnowDepth);
            }
        }

        /// <summary>核心与邻域共用局部气候窗口，温度过渡和泥炭边界不重复计算噪声。</summary>
        private sealed class SurfaceChunkBatch : IDisposable
        {
            private readonly ChunkGenerationRequest request;
            private readonly ChunkGenerationSettingsSnapshot settings;
            private readonly int radius;
            private readonly int peatRadius;
            private readonly int stride;
            private readonly SurfaceClimateSample[] samples;
            public readonly SurfaceCellOutput[] Output;

            public SurfaceChunkBatch(ChunkGenerationRequest request,
                ChunkGenerationSettingsSnapshot settings)
            {
                this.request = request;
                this.settings = settings;
                peatRadius = settings.PeatTileId > 0 && settings.PeatSpawnChance > 0d
                    ? settings.PeatStoneBoundaryRadius : 0;
                radius = Math.Max(peatRadius, settings.BiomeTemperatureBlendRadius);
                stride = request.Profile.Width + radius * 2;
                int sampleCount = stride * (request.Profile.Height + radius * 2);
                samples = ArrayPool<SurfaceClimateSample>.Shared.Rent(sampleCount);
                Array.Clear(samples, 0, sampleCount);
                Output = ArrayPool<SurfaceCellOutput>.Shared.Rent(
                    request.Profile.Width * request.Profile.Height);
            }

            public void SampleCore(CancellationToken cancellationToken)
            {
                if (settings.SurfaceClimateAlgorithm == SurfaceClimateAlgorithm.LegacyLand)
                {
                    SurfaceClimateBurstKernel.ClimateSample[] batch =
                        SurfaceClimateBurstKernel.RentAndSample(request, settings, radius);
                    try
                    {
                        for (int y = 0; y < request.Profile.Height + radius * 2; y++)
                        for (int x = 0; x < stride; x++)
                        {
                            int index = y * stride + x;
                            if ((index & 63) == 0)
                                cancellationToken.ThrowIfCancellationRequested();
                            SurfaceClimateBurstKernel.ClimateSample value = batch[index];
                            int worldX = request.Address.ChunkOrigin.X + x - radius;
                            int worldY = request.Address.ChunkOrigin.Y + y - radius;
                            SurfaceClimateSample sample = FinishGeographicTemperature(request, settings,
                                worldX, worldY, new SurfaceClimateSample
                            {
                                Height = value.Height,
                                Temperature = value.Temperature,
                                TemperatureCelsius = settings.ResolveTemperatureCelsius(value.Temperature),
                                WindX = value.WindX,
                                WindY = value.WindY
                            });
                            sample.Precipitation = SampleTransportPrecipitation(request, settings, worldX, worldY);
                            samples[index] = FinishSurfaceClimate(request, settings, worldX, worldY, sample);
                        }
                    }
                    finally
                    {
                        SurfaceClimateBurstKernel.Return(batch);
                    }
                    return;
                }
                for (int y = 0; y < request.Profile.Height + radius * 2; y++)
                for (int x = 0; x < stride; x++)
                {
                    int index = y * stride + x;
                    if ((index & 63) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    samples[index] =
                        SampleSurfaceClimate(request, settings,
                            request.Address.ChunkOrigin.X + x - radius,
                            request.Address.ChunkOrigin.Y + y - radius);
                }
            }

            public void ClassifyCore(CancellationToken cancellationToken)
            {
                for (int y = 0; y < request.Profile.Height + radius * 2; y++)
                for (int x = 0; x < stride; x++)
                {
                    int index = y * stride + x;
                    if ((index & 63) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    int sampleIndex = index;
                    SurfaceClimateSample sample = samples[sampleIndex];
                    ClassifyBase(ref sample);
                    samples[sampleIndex] = sample;
                }
            }

            public SurfaceClimateSample GetCore(int x, int y) =>
                samples[(y + radius) * stride + x + radius];

            public bool HasStoneNeighbor(int x, int y)
            {
                for (int offsetY = -peatRadius; offsetY <= peatRadius; offsetY++)
                for (int offsetX = -peatRadius; offsetX <= peatRadius; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0)
                        continue;
                    int sampleIndex = (y + offsetY + radius) * stride +
                                      x + offsetX + radius;
                    SurfaceClimateSample sample = samples[sampleIndex];
                    if (!sample.Classified)
                    {
                        sample = SampleSurfaceClimate(request, settings,
                            request.Address.ChunkOrigin.X + x + offsetX,
                            request.Address.ChunkOrigin.Y + y + offsetY);
                        ClassifyBase(ref sample);
                        samples[sampleIndex] = sample;
                    }
                    if (sample.BaseBiome == SurfaceBiomeKind.Stone)
                        return true;
                }
                return false;
            }

            private void ClassifyBase(ref SurfaceClimateSample sample)
            {
                double moisture = Clamp01(sample.Precipitation * 0.78d +
                                          (1d - sample.Height) * 0.22d);
                sample.BaseBiome = ResolveSurfaceBiome(settings, sample, moisture, false);
                sample.Classified = true;
            }

            public void Dispose()
            {
                ArrayPool<SurfaceClimateSample>.Shared.Return(samples);
                ArrayPool<SurfaceCellOutput>.Shared.Return(Output);
            }

            // 固定行列顺序求和，批量生成与单格查询得到相同的浮点基温和结冰判断。
            public double GetBlendedTemperature(int x, int y)
            {
                int blendRadius = settings.BiomeTemperatureBlendRadius;
                int diameter = blendRadius * 2 + 1;
                double total = 0d;
                for (int oy = -blendRadius; oy <= blendRadius; oy++)
                for (int ox = -blendRadius; ox <= blendRadius; ox++)
                    total += samples[(y + oy + radius) * stride + x + ox + radius].TemperatureCelsius;
                return total / (diameter * diameter);
            }
        }

        private static SurfaceClimateSample SampleSurfaceClimate(
            ChunkGenerationRequest request, ChunkGenerationSettingsSnapshot settings,
            int worldX, int worldY)
        {
            SurfaceClimateSample sample = SampleGeographicClimate(request, settings, worldX, worldY);
            sample.Precipitation = SampleTransportPrecipitation(request, settings, worldX, worldY);
            return FinishSurfaceClimate(request, settings, worldX, worldY, sample);
        }

        #endregion

        /// <summary>根据高度、温度、降水和河流结果，生成一个地表格子的完整数据。</summary>
        private void GenerateSurfaceCell(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            ChunkTerrainBuffer terrain,
            GeneratedHydrologyMap riverMap,
            int x,
            int y,
            int worldX,
            int worldY)
        {
            SurfaceCellOutput output = SampleSurfaceCellOutput(request, settings, riverMap,
                x, y, worldX, worldY, terrain.LiquidTypes);
            terrain.SetCell(x, y, output.Cell);
            terrain.SetLiquid(x, y, output.LiquidTypeIndex, output.LiquidDepth);
            terrain.SetGrass(x, y, output.Grass);
            new SurfaceEnvironmentWriter(terrain).Write(y * terrain.Width + x, output);
        }

        /// <summary>正式单格生成与群系查询共享最终分类和液体结果。</summary>
        private SurfaceCellOutput SampleSurfaceCellOutput(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, GeneratedHydrologyMap riverMap,
            int x, int y, int worldX, int worldY, LiquidTypeCatalog types)
        {
            SurfaceClimateSample climate = SampleSurfaceClimate(
                request, settings, worldX, worldY);
            double baseMoisture = Clamp01(climate.Precipitation * 0.78d +
                                          (1d - climate.Height) * 0.22d);
            climate.BaseBiome = ResolveSurfaceBiome(settings, climate, baseMoisture, false);
            climate.Classified = true;
            IReadOnlyList<LavaBasin> volcanic = ResolveLavaBasins(ExpandGenerationRequest(request, GetHydrologyOutputPadding(request)));
            SurfaceCellOutput output = BuildSurfaceCell(request, settings, riverMap,
                climate, null, x, y, worldX, worldY,
                types.GetIndex(LiquidTypeCatalog.SeaWaterId),
                types.GetIndex(LiquidTypeCatalog.DirtyWaterId),
                volcanic.Count > 0 ? types.GetIndex(LiquidTypeCatalog.LavaId) : 0, volcanic);
            output.AirHumidity = new GeographicAirHumiditySampler(request, settings, riverMap, volcanic).Sample(worldX, worldY);
            output.AirHumidityBackground = AirHumiditySettings.FromProfile(request.Profile).Background;
            if (terrainDecorator == null) return output;
            // 单格查询同样调用纯地形扩展，出生与群系定位不能看到扩展前的地表。
            using var sample = new ChunkTerrainBuffer(1, 1, types);
            sample.SetCell(0, 0, output.Cell); sample.SetLiquid(0, 0, output.LiquidTypeIndex, output.LiquidDepth);
            sample.SetGrass(0, 0, output.Grass); new SurfaceEnvironmentWriter(sample).Write(0, output);
            var cellRequest = new ChunkGenerationRequest(request.WorldEpoch,
                new WorldAddress(request.Address.DimensionId, new Int2(worldX, worldY)), request.WorldSeed,
                request.RequestVersion, request.Profile, request.Topology);
            terrainDecorator(cellRequest, sample, CancellationToken.None);
            using ChunkTerrainData decorated = sample.Seal();
            output.Cell = decorated.GetCell(0, 0); output.Grass = decorated.GetGrass(0, 0);
            output.LiquidTypeIndex = decorated.GetLiquidTypeIndex(0, 0); output.LiquidDepth = decorated.GetLiquidDepth(0, 0);
            if (decorated.TryGetEnvironmentValue("height", 0, 0, out float height)) output.Height = height;
            if (decorated.TryGetEnvironmentValue("temperature", 0, 0, out float temperature)) output.Temperature = temperature;
            if (decorated.TryGetEnvironmentValue("temperature.celsius", 0, 0, out float celsius)) output.TemperatureCelsius = celsius;
            if (decorated.TryGetEnvironmentValue("precipitation", 0, 0, out float rain)) output.Precipitation = rain;
            if (decorated.TryGetEnvironmentValue("moisture", 0, 0, out float moisture)) output.Moisture = moisture;
            if (decorated.TryGetEnvironmentValue(SnowDepthLayer.LayerId, 0, 0, out float snow)) output.SnowDepth = snow;
            return output;
        }

        private static SurfaceCellOutput BuildSurfaceCell(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            GeneratedHydrologyMap riverMap,
            SurfaceClimateSample climate,
            SurfaceChunkBatch batch,
            int x,
            int y,
            int worldX,
            int worldY,
            int seaWater,
            int dirtyWater,
            int lava,
            IReadOnlyList<LavaBasin> volcanic)
        {
            // 如果世界会绕回另一边，先把越界坐标换回世界内，保证两侧地形能严丝合缝。
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
            double height = climate.Height;
            double precipitation = climate.Precipitation;
            double windX = climate.WindX;
            double windY = climate.WindY;
            // 摄氏基温先完成群系过渡，结冰和雪厚共用实际写入环境层的浮点值。
            float temperatureCelsius = (float)(batch != null ? batch.GetBlendedTemperature(x, y) :
                SampleBlendedBiomeTemperature(request, settings, worldX, worldY));
            double temperature = settings.NormalizeTemperatureCelsius(temperatureCelsius);
            bool ocean = height < settings.SeaLevel;
            GeneratedHydrologyCell riverCell = default;
            bool river = !ocean && riverMap != null &&
                         riverMap.TryGet(worldX, worldY, out riverCell);
            double floodplain = !ocean && riverMap != null
                ? riverMap.GetFloodplainStrength(worldX, worldY)
                : 0d;
            double moisture = Clamp01(
                precipitation * 0.78d + (1d - height) * 0.22d + floodplain * 0.18d);
            if (ocean)
                moisture = Math.Max(moisture, settings.OceanMoistureFloor);
            SurfaceBiomeRuleSnapshot biomeRule = ResolveSurfaceBiomeRule(settings, climate, moisture, river, temperatureCelsius);
            SurfaceBiomeKind biome = biomeRule?.Biome ?? settings.SurfaceFallbackBiome;
            bool frozenRiver = river && temperatureCelsius < 0f;
            bool mountain = height >= settings.MountainLevel &&
                            (biome is SurfaceBiomeKind.Stone or SurfaceBiomeKind.Snow);
            bool alluvial =
                (biome is SurfaceBiomeKind.Grassland or SurfaceBiomeKind.Forest) &&
                floodplain >= settings.RiverAlluvialTileThreshold;

            // 一个格子可能同时符合几个条件，所以按顺序决定：先海洋、再河流，然后才是沙滩和气候地区。
            int biomeId;
            int groundTileId;
            float snowDepth = 0f;
            TerrainCellFlags flags;
            short navigationCost = settings.DefaultNavigationCost;
            if (biome == SurfaceBiomeKind.Ocean)
            {
                biomeId = (int)biome;
                // 海底底材只看生成高度：低于浅海沙底阈值铺石地，靠岸浅海保留沙地。
                groundTileId = height < settings.OceanSandMinimumHeight
                    ? settings.StoneTileId
                    : settings.SandTileId;
                flags = TerrainCellFlags.Walkable;
                // 液体导航代价由消费层单独叠加，底部 Ground 保留陆地成本。
            }
            else if (biome == SurfaceBiomeKind.River)
            {
                biomeId = (int)biome;
                if (frozenRiver)
                {
                    // 河流和湖泊在零下改用冰地块并清空液体，群系和水文身份继续保留。
                    groundTileId = settings.IceTileId;
                    flags = TerrainCellFlags.Walkable;
                    navigationCost = (short)Math.Min(short.MaxValue, navigationCost + 1);
                }
                else
                {
                    groundTileId = ResolveRiverbedTileId(settings, riverCell);
                    // 水域只是高代价地形：有陆路时 A* 优先绕行，唯一通路是水面时仍可通过。
                    flags = TerrainCellFlags.Walkable;
                    // 液体导航代价由消费层单独叠加，底部 Ground 保留陆地成本。
                }
            }
            else if (biome == SurfaceBiomeKind.Stone)
            {
                // 山地继续保留石地群系身份，湿润区域只把表层底材换成泥土。
                biomeId = (int)biome;
                groundTileId = moisture >= settings.MountainDirtMinimumMoisture
                    ? settings.DirtTileId
                    : settings.StoneTileId;
                flags = TerrainCellFlags.Walkable;

            }
            else if (biome == SurfaceBiomeKind.Beach)
            {
                biomeId = (int)biome;
                groundTileId = settings.SandTileId;
                flags = TerrainCellFlags.Walkable;
            }
            else if (ShouldGeneratePeat(
                         request, settings, biome, floodplain, worldX, worldY,
                         batch, x, y))
            {
                // 泥炭只铺在草原一侧的石地交界带，不再跟随河谷湿度向河岸扩张。
                biomeId = (int)biome;
                groundTileId = settings.PeatTileId;
                flags = TerrainCellFlags.Walkable;
            }
            else if (alluvial)
            {
                // 主河低坡两侧的浅色沙土带用来表现反复沉积形成的冲积平原。
                biomeId = (int)biome;
                groundTileId = settings.SandTileId;
                flags = TerrainCellFlags.Walkable;
            }
            else if (biome == SurfaceBiomeKind.Snow)
            {
                biomeId = (int)biome;
                // 积雪独立覆盖底材，寒冷陆地的石地和泥土由命中的群系配置选择。
                groundTileId = settings.StoneTileId;
                snowDepth = 1f;
                flags = TerrainCellFlags.Walkable;
                navigationCost = (short)Math.Min(short.MaxValue, navigationCost + 1);

            }
            else
            {
                biomeId = (int)biome;
                groundTileId = biome == SurfaceBiomeKind.Desert
                    ? settings.SandTileId
                    : settings.GroundTileId;
                flags = TerrainCellFlags.Walkable;
            }

            // 陆地统一读取群系底材配置，海洋和河湖继续保留各自的真实水文底材。
            if (!ocean && !river && biomeRule != null)
            {
                int configuredGround = biomeRule.ResolveGroundTileId(moisture);
                if (configuredGround > 0) groundTileId = configuredGround;
            }

            // 地面与液体在同一批次提交，height 继续随权威区块移交。
            float initialLiquidDepth = ocean
                ? (float)(1d - Math.Pow(Clamp01(height / Math.Max(0.0001d, settings.SeaLevel)), 2d))
                : river && !frozenRiver ? (float)riverCell.Depth : 0f;
            bool lavaCell = false;
            float lavaShore = 0f;
            if (climate.BaseBiome == SurfaceBiomeKind.Stone)
            {
                foreach (LavaBasin basin in volcanic)
                {
                    if (!basin.TrySample(request.Topology, worldX, worldY, out float depth, out float shore)) continue;
                    if (depth > 0f)
                    {
                        lavaCell = true;
                        initialLiquidDepth = depth;
                        groundTileId = settings.StoneTileId;
                        biome = SurfaceBiomeKind.Stone;
                        biomeId = (int)biome;
                        flags = TerrainCellFlags.Walkable;
                        navigationCost = settings.DefaultNavigationCost;
                        mountain = true;
                        river = false;
                        floodplain = 0d;
                        lavaShore = 0f;
                        break;
                    }
                    if (!river && initialLiquidDepth <= 0f) lavaShore = Math.Max(lavaShore, shore);
                }
            }
            initialLiquidDepth = QuantizeGeneratedLiquidDepth(initialLiquidDepth);

            // 海水同样按零下基温结冰，岩浆不参加水体结冰规则。
            if (!lavaCell && initialLiquidDepth > 0f && temperatureCelsius < 0f)
            {
                groundTileId = settings.IceTileId;
                initialLiquidDepth = 0f;
                snowDepth = 0f;
                flags = TerrainCellFlags.Walkable;
                navigationCost = (short)Math.Min(short.MaxValue, settings.DefaultNavigationCost + 1);
            }
            if (snowDepth > 0f && climate.IsPolarBand)
                snowDepth = ResolvePolarSnowDepth(temperatureCelsius);

            // 草先过较宽松的气候门槛，再由湿度决定局部密度；全程只依赖种子和环境层。
            bool snowSurface = biome == SurfaceBiomeKind.Snow &&
                               snowDepth > 0f &&
                               groundTileId == settings.GroundTileId;
            double grassDensity = snowSurface
                ? settings.GrassDensity * settings.SnowGrassDensityMultiplier
                : settings.GrassDensity;
            bool grassClimateSuitable =
                temperature >= settings.GrassMinimumTemperature &&
                temperature <= settings.GrassMaximumTemperature &&
                precipitation >= settings.GrassMinimumPrecipitation &&
                height <= settings.GrassMaximumHeight;
            bool grass = (flags & TerrainCellFlags.Walkable) != 0 &&
                         groundTileId == settings.GroundTileId &&
                         grassClimateSuitable &&
                         Hash01(request.WorldSeed, worldX, worldY, 0x165667b1u) <
                         grassDensity * (0.55d + moisture * 0.75d);
            return new SurfaceCellOutput
            {
                Cell = new TerrainCell(groundTileId, 0, 0, biomeId,
                    navigationCost, flags),
                LiquidTypeIndex = initialLiquidDepth > 0f
                    ? lavaCell ? lava : ocean ? seaWater : dirtyWater : 0,
                LiquidDepth = initialLiquidDepth,
                SnowDepth = snowDepth,
                Grass = grass ? GrassPresent : GrassEmpty,
                Height = (float)height,
                Temperature = (float)temperature,
                TemperatureCelsius = temperatureCelsius,
                Precipitation = (float)precipitation,
                WindX = (float)windX,
                WindY = (float)windY,
                Moisture = (float)moisture,
                Mountain = mountain ? 1f : 0f,
                RiverFlow = river ? (float)riverCell.Flow : 0f,
                RiverFlowX = river ? (float)riverCell.FlowDirectionX : 0f,
                RiverFlowY = river ? (float)riverCell.FlowDirectionY : 0f,
                RiverFloodplain = (float)floodplain,
                RiverSurfaceLevel = river ? (float)riverCell.SurfaceLevel : 0f,
                RiverKind = river ? (float)riverCell.Kind : 0f,
                LavaShore = lavaShore
            };
        }

        /// <summary>河流主体保持中央底材，只有被水文标记的局部沉积段才在两侧换成边缘底材。</summary>
        private static int ResolveRiverbedTileId(
            ChunkGenerationSettingsSnapshot settings,
            GeneratedHydrologyCell riverCell)
        {
            if (riverCell.Kind != GeneratedHydrologyKind.River)
                return settings.RiverbedTileId;
            if (riverCell.BedCenterStrength >= settings.RiverBedCenterStrengthThreshold)
                return settings.RiverBedCenterTileId;
            return riverCell.BedEdgeDeposit
                ? settings.RiverBedEdgeTileId
                : settings.RiverBedCenterTileId;
        }

        /// <summary>判定泥炭斑块：草原石地交界、远离河漫滩，并按斑块区域概率稀疏生成。</summary>
        private static bool ShouldGeneratePeat(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            SurfaceBiomeKind biome,
            double floodplain,
            int worldX,
            int worldY,
            SurfaceChunkBatch batch,
            int x,
            int y)
        {
            if (settings.PeatTileId <= 0 ||
                settings.PeatSpawnChance <= 0d ||
                biome != SurfaceBiomeKind.Grassland ||
                floodplain > 0.000001d)
            {
                return false;
            }

            // 概率按整个泥炭斑块区域抽取，避免把连续泥炭打散成零星单格。
            int patchRegionSize = Math.Max(1, (int)Math.Round(
                1d / settings.PeatPatchScale, MidpointRounding.AwayFromZero));
            int patchRegionX = FloorDiv(worldX - request.Topology.Min.X, patchRegionSize);
            int patchRegionY = FloorDiv(worldY - request.Topology.Min.Y, patchRegionSize);
            if (settings.PeatSpawnChance < 1d &&
                Hash01(CreateSeed(request, 0x3f84d5b5u), patchRegionX, patchRegionY) >=
                settings.PeatSpawnChance)
            {
                return false;
            }

            if (Fractal(CreateSeed(request, 0x4c7de19bu), worldX, worldY,
                    settings.PeatPatchScale, 2, 2d, 0.5d, request.Topology) <
                settings.PeatPatchThreshold)
            {
                return false;
            }

            return batch != null
                ? batch.HasStoneNeighbor(x, y)
                : IsGrasslandStoneBoundary(request, settings, worldX, worldY);
        }

        /// <summary>只接受草原侧指定半径内能直接采样到石地的过渡格。</summary>
        private static bool IsGrasslandStoneBoundary(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            int worldX,
            int worldY)
        {
            int radius = settings.PeatStoneBoundaryRadius;
            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    if (offsetX == 0 && offsetY == 0)
                        continue;

                    SurfaceBiomeKind nearbyBiome = SampleBaseSurfaceBiome(
                        request,
                        settings,
                        worldX + offsetX,
                        worldY + offsetY,
                        out _,
                        out _);
                    if (nearbyBiome == SurfaceBiomeKind.Stone)
                        return true;
                }
            }

            return false;
        }

        /// <summary>按冻结参数采样不含河流覆盖的基础地表群系，并同时返回同格高度与最终降水。</summary>
        internal static SurfaceBiomeKind SampleBaseSurfaceBiome(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            int worldX,
            int worldY,
            out double height,
            out double precipitation)
        {
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
            SurfaceClimateSample climate = SampleSurfaceClimate(request, settings, worldX, worldY);
            height = climate.Height;
            precipitation = climate.Precipitation;

            double moisture = Clamp01(precipitation * 0.78d + (1d - height) * 0.22d);
            return ResolveSurfaceBiome(settings, climate, moisture, false);
        }

        /// <summary>按世界种子和坐标采样地形高度，供地表生成与洞穴地表参考共同复用。</summary>
        internal static double SampleHeight(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            int worldX,
            int worldY)
        {
            if (settings.SurfaceClimateAlgorithm == SurfaceClimateAlgorithm.LegacyLand)
            {
                return LegacyTerrainClimateKernel.SampleHeight(
                    request, settings, worldX, worldY);
            }
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
            return Fractal(
                CreateSeed(request, 0x9e3779b9u),
                worldX,
                worldY,
                settings.TerrainScale,
                settings.HeightOctaves,
                2.03d,
                0.51d,
                request.Topology);
        }

        #endregion

        #region 高度图水文

        private const double DownhillEpsilon = 0.00001d;

        private static readonly Int2[] RiverNeighbors =
        {
            new Int2(-1, -1), new Int2(0, -1), new Int2(1, -1),
            new Int2(-1, 0),                       new Int2(1, 0),
            new Int2(-1, 1),  new Int2(0, 1),  new Int2(1, 1)
        };

        // D∞ 的八条栅格边，按数学正方向逆时针排列；相邻两条边共同组成一个三角坡面。
        private static readonly Int2[] RiverFlowDirections =
        {
            new Int2(1, 0), new Int2(1, 1), new Int2(0, 1), new Int2(-1, 1),
            new Int2(-1, 0), new Int2(-1, -1), new Int2(0, -1), new Int2(1, -1)
        };

        /// <summary>
        /// 从与地表完全相同的高度图和降水图构建当前区块的河道。
        /// 河流只负责沿低处汇流；不再使用独立噪声场或正弦函数直接绘制河带。
        /// </summary>
        private GeneratedHydrologyMap BuildHeightDrivenRiverMap(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            CancellationToken cancellationToken, ChunkGenerationTiming timing)
        {
            MacroHydrologyRegion macro = GetHeightDrivenMacroRegion(request, settings,
                cancellationToken, timing);
            if (!isBiomeSearchGenerator)
                QueueNeighbourHydrologyPrewarm(request, settings, ResolveHeightDrivenRegion(request, settings));
            using (timing?.MeasureStage("river.local_refine") ?? default)
                return BuildMacroRiverChunk(request, settings, macro, cancellationToken);
        }

        /// <summary>河流定位和正式区块生成共享宏观河网入口。</summary>
        private MacroHydrologyRegion GetHeightDrivenMacroRegion(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, CancellationToken cancellationToken,
            ChunkGenerationTiming timing = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.WorldEpoch <= Volatile.Read(ref retiredWorldEpoch))
                throw new OperationCanceledException("旧世界的水文请求已经失效。");
            HeightDrivenRegionDescriptor region = ResolveHeightDrivenRegion(request, settings);
            HeightDrivenRegionKey key = CreateHeightDrivenRegionKey(request, region);
            CancellationToken sharedCancellation = sharedHydrologyCancellation.Token;
            var candidate = new Lazy<MacroHydrologyRegion>(() =>
            {
                using (ChunkGenerationTiming.CurrentOnThread?.MeasureStage("river.macro_compute") ?? default)
                    return BuildMacroHydrologyRegion(
                        CreateHeightDrivenRegionRequest(request, region), settings, sharedCancellation);
            },
                LazyThreadSafetyMode.ExecutionAndPublication);
            Lazy<MacroHydrologyRegion> shared = heightDrivenRegionCache.GetOrAdd(key, candidate);
            if (ReferenceEquals(shared, candidate))
            {
                heightDrivenCacheOrder.Enqueue(key);
                TrimHeightDrivenRegionCache(settings.RiverMaxCachedRegions);
            }

            try
            {
                // region_get 包含共享等待；有 region_compute 的请求才真正执行了区域计算。
                MacroHydrologyRegion macro;
                using (timing?.MeasureStage("river.macro_get") ?? default)
                    macro = shared.Value;
                cancellationToken.ThrowIfCancellationRequested();
                if (request.WorldEpoch <= Volatile.Read(ref retiredWorldEpoch))
                    throw new OperationCanceledException("旧世界的水文请求已经失效。");
                return macro;
            }
            catch
            {
                if (!shared.IsValueCreated &&
                    heightDrivenRegionCache.TryGetValue(key, out Lazy<MacroHydrologyRegion> failed) &&
                    ReferenceEquals(failed, shared))
                    heightDrivenRegionCache.TryRemove(key, out _);
                throw;
            }
        }

        /// <summary>只在靠近区域边界时预热相邻低分辨率图，后台任务共享世界纪元取消。</summary>
        private void QueueNeighbourHydrologyPrewarm(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, HeightDrivenRegionDescriptor region)
        {
            int x = request.Address.ChunkOrigin.X;
            int y = request.Address.ChunkOrigin.Y;
            int distance = Math.Max(request.Profile.Width, request.Profile.Height);
            if (x - region.Origin.X <= distance)
                TryQueueHydrologyPrewarm(request, settings, region, -1, 0);
            if (region.Origin.X + region.Width - x - request.Profile.Width <= distance)
                TryQueueHydrologyPrewarm(request, settings, region, 1, 0);
            if (y - region.Origin.Y <= distance)
                TryQueueHydrologyPrewarm(request, settings, region, 0, -1);
            if (region.Origin.Y + region.Height - y - request.Profile.Height <= distance)
                TryQueueHydrologyPrewarm(request, settings, region, 0, 1);
        }

        private void TryQueueHydrologyPrewarm(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, HeightDrivenRegionDescriptor region,
            int offsetX, int offsetY)
        {
            if (request.WorldEpoch <= Volatile.Read(ref retiredWorldEpoch)) return;
            if (heightDrivenRegionCache.Count >= settings.RiverMaxCachedRegions)
                return;
            int nextX = request.Topology.NormalizeX(region.Origin.X + offsetX *
                settings.RiverHydrologyRegionSize);
            int nextY = request.Topology.NormalizeY(region.Origin.Y + offsetY *
                settings.RiverHydrologyRegionSize);
            var adjacentRequest = new ChunkGenerationRequest(request.WorldEpoch,
                new WorldAddress(request.Address.DimensionId, new Int2(nextX, nextY)),
                request.WorldSeed, request.RequestVersion, request.Profile, request.Topology);
            HeightDrivenRegionDescriptor adjacent = ResolveHeightDrivenRegion(adjacentRequest, settings);
            HeightDrivenRegionKey key = CreateHeightDrivenRegionKey(adjacentRequest, adjacent);
            if (heightDrivenRegionCache.ContainsKey(key)) return;
            CancellationToken token = sharedHydrologyCancellation.Token;
            var candidate = new Lazy<MacroHydrologyRegion>(() =>
            {
                if (request.WorldEpoch <= Volatile.Read(ref retiredWorldEpoch))
                    throw new OperationCanceledException("旧世界的水文预热已经失效。");
                return BuildMacroHydrologyRegion(
                    CreateHeightDrivenRegionRequest(adjacentRequest, adjacent), settings, token);
            },
                LazyThreadSafetyMode.ExecutionAndPublication);
            if (!heightDrivenRegionCache.TryAdd(key, candidate)) return;
            heightDrivenCacheOrder.Enqueue(key);
            _ = Task.Run(() =>
            {
                try { _ = candidate.Value; }
                catch
                {
                    if (heightDrivenRegionCache.TryGetValue(key, out Lazy<MacroHydrologyRegion> current) &&
                        ReferenceEquals(current, candidate))
                        heightDrivenRegionCache.TryRemove(key, out _);
                }
            });
        }

        /// <summary>同一栅格被多条曲线经过时保留水量更大的主槽样本。</summary>
        private static void SetChannelCenterSample(
            Dictionary<Int2, ChannelCenterSample> samples,
            Int2 position,
            ChannelCenterSample candidate)
        {
            if (samples.TryGetValue(position, out ChannelCenterSample existing))
            {
                if (existing.Flow >= candidate.Flow)
                {
                    if (!candidate.BedEdgeDeposit || existing.BedEdgeDeposit)
                        return;
                    samples[position] = new ChannelCenterSample(
                        existing.Flow,
                        existing.DirectionX,
                        existing.DirectionY,
                        true);
                    return;
                }
                if (existing.BedEdgeDeposit && !candidate.BedEdgeDeposit)
                {
                    candidate = new ChannelCenterSample(
                        candidate.Flow,
                        candidate.DirectionX,
                        candidate.DirectionY,
                        true);
                }
            }
            samples[position] = candidate;
        }

        /// <summary>把缓存限制在配置上限内，优先移除最早加入的区域。</summary>
        private void TrimHeightDrivenRegionCache(int maximumRegions)
        {
            maximumRegions = Math.Max(1, maximumRegions);
            while (heightDrivenRegionCache.Count > maximumRegions &&
                   heightDrivenCacheOrder.TryDequeue(out HeightDrivenRegionKey oldest))
            {
                if (!heightDrivenRegionCache.TryGetValue(oldest, out Lazy<MacroHydrologyRegion> entry))
                    continue;
                if (!entry.IsValueCreated)
                {
                    heightDrivenCacheOrder.Enqueue(oldest);
                    break;
                }
                heightDrivenRegionCache.TryRemove(oldest, out _);
            }
        }

        /// <summary>按有限世界左下角或无限世界原点，把区块归入稳定的水文区域。</summary>
        private static HeightDrivenRegionDescriptor ResolveHeightDrivenRegion(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings)
        {
            int size = settings.RiverHydrologyRegionSize;
            int anchorX = request.Topology.IsWrapped ? request.Topology.Min.X : 0;
            int anchorY = request.Topology.IsWrapped ? request.Topology.Min.Y : 0;
            int normalizedX = request.Topology.NormalizeX(request.Address.ChunkOrigin.X);
            int normalizedY = request.Topology.NormalizeY(request.Address.ChunkOrigin.Y);
            int originX = anchorX + FloorDiv(normalizedX - anchorX, size) * size;
            int originY = anchorY + FloorDiv(normalizedY - anchorY, size) * size;
            int width = size;
            int height = size;
            if (request.Topology.IsWrapped)
            {
                width = Math.Min(size,
                    request.Topology.Min.X + request.Topology.Span.X - originX);
                height = Math.Min(size,
                    request.Topology.Min.Y + request.Topology.Span.Y - originY);
            }
            return new HeightDrivenRegionDescriptor(
                new Int2(originX, originY), Math.Max(1, width), Math.Max(1, height));
        }

        /// <summary>调度分组与实际河网缓存共用完全相同的区域身份。</summary>
        private static HeightDrivenRegionKey CreateHeightDrivenRegionKey(
            ChunkGenerationRequest request, HeightDrivenRegionDescriptor region)
        {
            return new HeightDrivenRegionKey(
                request.WorldEpoch, request.Address.DimensionId, request.WorldSeed,
                request.Profile.GenerationFingerprint, request.Topology,
                region.Origin, region.Width, region.Height);
        }

        /// <summary>复制原配置，仅把生成范围扩大为水文区域，不读取任何 Unity 对象。</summary>
        private static ChunkGenerationRequest CreateHeightDrivenRegionRequest(
            ChunkGenerationRequest source,
            HeightDrivenRegionDescriptor region)
        {
            var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, double> pair in source.Profile.NumericParameters)
                numbers.Add(pair.Key, pair.Value);
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in source.Profile.TextParameters)
                texts.Add(pair.Key, pair.Value);
            var profile = new ChunkGenerationProfileSnapshot(
                source.Profile.ProfileId,
                source.Profile.Signature,
                region.Width,
                region.Height,
                numbers,
                texts);
            return new ChunkGenerationRequest(
                source.WorldEpoch,
                new WorldAddress(source.Address.DimensionId, region.Origin),
                source.WorldSeed,
                source.RequestVersion,
                profile,
                source.Topology);
        }

        /// <summary>只在低坡且已有明显汇流的主河两侧生成宽缓冲积带。</summary>
        private static void AddFloodplain(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            HydrologySamplingContext sampling,
            Int2 center,
            double flow,
            int channelRadius,
            Dictionary<Int2, double> floodplainCells,
            int outputPadding = 0)
        {
            if (settings.RiverFloodplainMaxRadius <= channelRadius ||
                flow < settings.RiverFloodplainStartFlow)
            {
                return;
            }

            double slope = EstimateLocalSlope(sampling, center);
            double flatness = 1d - Clamp01(slope / settings.RiverFloodplainMaxSlope);
            if (flatness <= 0.05d)
                return;

            double flowT = InverseLerp(
                settings.RiverFloodplainStartFlow,
                Math.Max(settings.RiverFloodplainStartFlow + 0.001d,
                    settings.RiverFullWidthFlow),
                flow);
            int minimumRadius = Math.Min(
                settings.RiverFloodplainMaxRadius,
                channelRadius + 2);
            int radius = minimumRadius + (int)Math.Round(
                (settings.RiverFloodplainMaxRadius - minimumRadius) *
                Math.Sqrt(flowT * flatness),
                MidpointRounding.AwayFromZero);
            double centerStrength = Math.Sqrt(flatness) * (0.55d + Math.Sqrt(flowT) * 0.45d);

            for (int offsetY = -radius; offsetY <= radius; offsetY++)
            {
                for (int offsetX = -radius; offsetX <= radius; offsetX++)
                {
                    double distance = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                    if (distance > radius + 0.25d)
                        continue;

                    Int2 position = sampling.Normalize(new Int2(
                        center.X + offsetX,
                        center.Y + offsetY));
                    if (!(outputPadding > 0 ? ContainsHydrologyOutput(request, position) : ContainsChunk(request, position)) ||
                        sampling.Height(position) <= settings.SeaLevel)
                    {
                        continue;
                    }

                    double edgeStrength = 1d - Clamp01(distance / (radius + 0.75d));
                    SetFloodplainStrength(
                        floodplainCells,
                        position,
                        centerStrength * Math.Sqrt(edgeStrength));
                }
            }
        }

        /// <summary>以八邻域最大高差估计格子附近坡度。</summary>
        private static double EstimateLocalSlope(HydrologySamplingContext sampling, Int2 position)
        {
            double center = sampling.Height(position);
            double maximum = 0d;
            for (int i = 0; i < RiverNeighbors.Length; i++)
            {
                double delta = Math.Abs(
                    sampling.Height(position + RiverNeighbors[i]) - center);
                maximum = Math.Max(maximum, delta);
            }
            return maximum;
        }

        /// <summary>合并淡水格；湖泊优先于河道，同类则保留更深和更大流量。</summary>
        private static void SetRiverCell(
            Dictionary<Int2, GeneratedHydrologyCell> cells,
            Int2 position,
            GeneratedHydrologyCell candidate)
        {
            if (!cells.TryGetValue(position, out GeneratedHydrologyCell current))
            {
                cells[position] = candidate;
                return;
            }
            if (current.Kind == GeneratedHydrologyKind.Lake &&
                candidate.Kind != GeneratedHydrologyKind.Lake)
            {
                return;
            }
            if (current.Kind == candidate.Kind &&
                current.Depth >= candidate.Depth &&
                current.Flow >= candidate.Flow &&
                current.BedCenterStrength >= candidate.BedCenterStrength &&
                (!candidate.BedEdgeDeposit || current.BedEdgeDeposit))
            {
                return;
            }

            GeneratedHydrologyKind kind = current.Kind == GeneratedHydrologyKind.Lake ||
                                           candidate.Kind == GeneratedHydrologyKind.Lake
                ? GeneratedHydrologyKind.Lake
                : GeneratedHydrologyKind.River;
            double flowDirectionX = current.FlowDirectionX;
            double flowDirectionY = current.FlowDirectionY;
            bool currentHasDirection = Math.Abs(flowDirectionX) > 0.000001d ||
                                       Math.Abs(flowDirectionY) > 0.000001d;
            if (kind == GeneratedHydrologyKind.Lake)
            {
                flowDirectionX = 0d;
                flowDirectionY = 0d;
            }
            else if (!currentHasDirection || candidate.Flow > current.Flow)
            {
                flowDirectionX = candidate.FlowDirectionX;
                flowDirectionY = candidate.FlowDirectionY;
            }
            cells[position] = new GeneratedHydrologyCell(
                kind,
                Math.Max(current.Flow, candidate.Flow),
                Math.Max(current.Depth, candidate.Depth),
                Math.Max(current.SurfaceLevel, candidate.SurfaceLevel),
                flowDirectionX,
                flowDirectionY,
                Math.Max(current.BedCenterStrength, candidate.BedCenterStrength),
                current.BedEdgeDeposit || candidate.BedEdgeDeposit);
        }

        /// <summary>记录格子的最大冲积带强度，重复计算时只保留更明显的一次。</summary>
        private static void SetFloodplainStrength(
            Dictionary<Int2, double> cells,
            Int2 position,
            double strength)
        {
            strength = Clamp01(strength);
            if (!cells.TryGetValue(position, out double current) || strength > current)
                cells[position] = strength;
        }

        /// <summary>判断一个世界坐标是否落在当前区块范围内。</summary>
        private static bool ContainsChunk(ChunkGenerationRequest request, Int2 position)
        {
            int localX = position.X - request.Address.ChunkOrigin.X;
            int localY = position.Y - request.Address.ChunkOrigin.Y;
            return (uint)localX < (uint)request.Profile.Width &&
                   (uint)localY < (uint)request.Profile.Height;
        }

        /// <summary>把数值映射到两个边界之间的 0 到 1 比例。</summary>
        private static double InverseLerp(double from, double to, double value)
        {
            return Clamp01((value - from) / Math.Max(0.0001d, to - from));
        }

        /// <summary>水文区域的绝对原点和实际尺寸。</summary>
        private readonly struct HeightDrivenRegionDescriptor
        {
            public HeightDrivenRegionDescriptor(Int2 origin, int width, int height)
            {
                Origin = origin;
                Width = width;
                Height = height;
            }

            public Int2 Origin { get; }
            public int Width { get; }
            public int Height { get; }
        }

        /// <summary>隔离世界、种子、配置、拓扑和区域的新版水文缓存键。</summary>
        private readonly struct HeightDrivenRegionKey : IEquatable<HeightDrivenRegionKey>
        {
            public HeightDrivenRegionKey(long epoch, string dimensionId, int worldSeed,
                ulong profileFingerprint, ChunkGenerationTopologySnapshot topology,
                Int2 origin, int width, int height)
            {
                Epoch = epoch;
                DimensionId = dimensionId;
                WorldSeed = worldSeed;
                ProfileFingerprint = profileFingerprint;
                IsWrapped = topology.IsWrapped;
                TopologyMin = topology.Min;
                TopologySpan = topology.Span;
                Origin = origin;
                Width = width;
                Height = height;
            }

            private long Epoch { get; }
            private string DimensionId { get; }
            private int WorldSeed { get; }
            private ulong ProfileFingerprint { get; }
            private bool IsWrapped { get; }
            private Int2 TopologyMin { get; }
            private Int2 TopologySpan { get; }
            private Int2 Origin { get; }
            private int Width { get; }
            private int Height { get; }

            public bool Equals(HeightDrivenRegionKey other) =>
                Epoch == other.Epoch &&
                string.Equals(DimensionId, other.DimensionId, StringComparison.Ordinal) &&
                WorldSeed == other.WorldSeed &&
                ProfileFingerprint == other.ProfileFingerprint &&
                IsWrapped == other.IsWrapped &&
                TopologyMin.Equals(other.TopologyMin) &&
                TopologySpan.Equals(other.TopologySpan) &&
                Origin.Equals(other.Origin) &&
                Width == other.Width &&
                Height == other.Height;

            public override bool Equals(object obj) =>
                obj is HeightDrivenRegionKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Epoch.GetHashCode();
                    hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(DimensionId);
                    hash = hash * 397 ^ WorldSeed;
                    hash = hash * 397 ^ ProfileFingerprint.GetHashCode();
                    hash = hash * 397 ^ IsWrapped.GetHashCode();
                    hash = hash * 397 ^ TopologyMin.GetHashCode();
                    hash = hash * 397 ^ TopologySpan.GetHashCode();
                    hash = hash * 397 ^ Origin.GetHashCode();
                    hash = hash * 397 ^ Width;
                    return hash * 397 ^ Height;
                }
            }
        }

        /// <summary>平滑中心线重新栅格化后的河槽中心样本。</summary>
        private readonly struct ChannelCenterSample
        {
            public ChannelCenterSample(
                double flow,
                double directionX,
                double directionY,
                bool bedEdgeDeposit = false)
            {
                Flow = flow;
                DirectionX = directionX;
                DirectionY = directionY;
                BedEdgeDeposit = bedEdgeDeposit;
            }

            public double Flow { get; }
            public double DirectionX { get; }
            public double DirectionY { get; }
            public bool BedEdgeDeposit { get; }
        }

        /// <summary>复用单次区块水文计算中的高度和降水采样，避免重复计算同一格噪声。</summary>
        private sealed class HydrologySamplingContext
        {
            private readonly Dictionary<Int2, double> heightCache = new();
            private readonly Dictionary<Int2, double> precipitationCache = new();
            private readonly Dictionary<Int2, double> channelCenterBiasCache = new();
            private readonly bool measureSampling;

            /// <summary>仅统计缓存未命中时的真实高度计算。</summary>
            public long HeightComputeTicks { get; private set; }
            public long HeightMissCount { get; private set; }

            /// <summary>创建一次水文采样上下文，并准备各类采样缓存。</summary>
            public HydrologySamplingContext(
                ChunkGenerationRequest request,
                ChunkGenerationSettingsSnapshot settings)
            {
                Request = request;
                Settings = settings;
                measureSampling = ChunkGenerationTiming.CurrentOnThread != null;
            }

            public ChunkGenerationRequest Request { get; }
            public ChunkGenerationSettingsSnapshot Settings { get; }

            /// <summary>把坐标归一化到当前世界范围，处理环绕世界边界。</summary>
            public Int2 Normalize(Int2 position)
            {
                return new Int2(
                    Request.Topology.NormalizeX(position.X),
                    Request.Topology.NormalizeY(position.Y));
            }

            /// <summary>读取坐标高度；第一次读取后缓存结果，避免重复计算噪声。</summary>
            public double Height(Int2 position)
            {
                position = Normalize(position);
                if (!heightCache.TryGetValue(position, out double value))
                {
                    long started = measureSampling ? Stopwatch.GetTimestamp() : 0;
                    value = SampleHeight(Request, Settings, position.X, position.Y);
                    heightCache.Add(position, value);
                    if (measureSampling)
                    {
                        HeightComputeTicks += Stopwatch.GetTimestamp() - started;
                        HeightMissCount++;
                    }
                }
                return value;
            }

            /// <summary>读取风携水汽形成的长期降水量，缓存同一世界坐标的结果。</summary>
            public double Precipitation(Int2 position)
            {
                position = Normalize(position);
                if (!precipitationCache.TryGetValue(position, out double value))
                {
                    value = SampleTransportPrecipitation(Request, Settings, position.X, position.Y);
                    precipitationCache.Add(position, value);
                }
                return value;
            }

            /// <summary>
            /// 河槽中心线使用的较短尺度横向偏移场。
            /// 它只改变连续中心线在格子里的亚格位置，不参与汇流和水量计算；
            /// 尺度比主蜿蜒场更短，用来打散长距离同方向栅格锁定，但仍保持连续平滑。
            /// </summary>
            public double ChannelCenterBias(Int2 position)
            {
                position = Normalize(position);
                if (channelCenterBiasCache.TryGetValue(position, out double value))
                    return value;

                double scale = Math.Max(7d, Settings.RiverMeanderScale * 0.28d);
                double broad = Fractal(
                    CreateSeed(Request, 0xa24baed5u),
                    position.X,
                    position.Y,
                    1d / scale,
                    2,
                    2.07d,
                    0.52d,
                    Request.Topology);
                double detail = Fractal(
                    CreateSeed(Request, 0x9fb21c65u),
                    position.X,
                    position.Y,
                    1d / Math.Max(5d, scale * 0.52d),
                    2,
                    2.13d,
                    0.46d,
                    Request.Topology);
                value = Math.Max(-1d, Math.Min(1d,
                    (broad * 0.68d + detail * 0.32d - 0.5d) * 2.75d));
                channelCenterBiasCache.Add(position, value);
                return value;
            }

        }

        #endregion

        /// <summary>根据群系交界主通道、稀疏支路和入口安全网络生成洞穴地面或岩壁格。</summary>
        private static void GenerateCaveCell(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, ChunkTerrainBuffer terrain,
            int x, int y, int worldX, int worldY)
        {
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
            CaveSurfaceInfluenceSample surfaceInfluence =
                CaveLayoutKernel.SampleSurfaceInfluence(request, worldX, worldY);
            // 洞穴岩壁下保留石地；开放洞室可按世界区域形成跨 Chunk 连续的地下河与地下湖。
            bool open = CaveLayoutKernel.IsOpenAtWorld(
                request, settings, worldX, worldY, surfaceInfluence);
            CaveRiverSample caveRiver = open
                ? CaveLayoutKernel.SampleRiver(request, settings, worldX, worldY)
                : default;
            double groundliquidDepth = open
                ? CaveLayoutKernel.SampleGroundliquidDepth(
                    request, settings, worldX, worldY, surfaceInfluence)
                : 0d;
            bool groundwater = groundliquidDepth > 0d;
            bool river = caveRiver.IsRiver;
            float liquidDepth = QuantizeGeneratedLiquidDepth(Math.Max(groundliquidDepth, caveRiver.Depth));
            bool water = liquidDepth > 0f;
            bool dirtWall = open && !water && CaveLayoutKernel.ShouldPlaceDirtWall(
                request, settings, worldX, worldY);
            TerrainCellFlags flags = !open || dirtWall
                ? TerrainCellFlags.Blocking
                : TerrainCellFlags.Walkable;
            int groundTileId = settings.CaveFloorTileId;
            int blockingTileId = !open
                ? settings.CaveWallTileId
                : dirtWall ? settings.CaveDirtWallTileId : 0;
            short navigationCost = !open || dirtWall
                ? short.MaxValue
                : settings.DefaultNavigationCost;
            terrain.SetCell(x, y, new TerrainCell(groundTileId, 0,
                blockingTileId, 100,
                navigationCost, flags));
            terrain.SetLiquid(x, y, water ? terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId) : 0,
                liquidDepth);
            terrain.SetEnvironmentValue("temperature", x, y, 0.38f);
            terrain.SetEnvironmentValue("temperature.celsius", x, y, 8f);
            terrain.SetEnvironmentValue("precipitation", x, y, 0f);
            terrain.SetEnvironmentValue("moisture", x, y, water ? 1f : dirtWall ? 0.72f : 0.3f);
            terrain.SetEnvironmentValue("riverFlow", x, y, river ? (float)caveRiver.Flow : 0f);
            terrain.SetEnvironmentValue("riverFlowX", x, y, river ? (float)caveRiver.FlowX : 0f);
            terrain.SetEnvironmentValue("riverFlowY", x, y, river ? (float)caveRiver.FlowY : 0f);
            terrain.SetEnvironmentValue("riverKind", x, y, river ? 1f : groundwater ? 2f : 0f);
            terrain.SetEnvironmentValue("groundwater", x, y, groundwater ? 1f : 0f);
            terrain.SetEnvironmentValue("caveDirtWall", x, y, dirtWall ? 1f : 0f);
            terrain.SetEnvironmentValue("grass", x, y, 0f);
            terrain.SetGrass(x, y, GrassEmpty);
        }

        /// <summary>在地表生成完成后，按种子在区块中放置确定性的简化结构区域。</summary>
        private static void ApplyStructures(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, ChunkTerrainBuffer terrain,
            CancellationToken cancellationToken)
        {
            if (!settings.StructureEnabled || settings.StructureChance <= 0d)
                return;

            // 找出所有可能延伸进当前区块的遗迹区域，包括中心点落在区块外面的遗迹。
            int minWorldX = request.Address.ChunkOrigin.X;
            int minWorldY = request.Address.ChunkOrigin.Y;
            int maxWorldX = minWorldX + request.Profile.Width - 1;
            int maxWorldY = minWorldY + request.Profile.Height - 1;
            int region = settings.StructureRegionSize;
            int radius = settings.StructureRadius;
            int minRegionX = FloorDiv(minWorldX - radius, region);
            int maxRegionX = FloorDiv(maxWorldX + radius, region);
            int minRegionY = FloorDiv(minWorldY - radius, region);
            int maxRegionY = FloorDiv(maxWorldY + radius, region);
            for (int regionX = minRegionX; regionX <= maxRegionX; regionX++)
            {
                for (int regionY = minRegionY; regionY <= maxRegionY; regionY++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // 每片大区域自己决定是否生成遗迹；世界种子相同，决定就永远相同。
                    if (Hash01(request.WorldSeed, regionX, regionY, 0x94d049bbu) >=
                        settings.StructureChance)
                        continue;
                    // 遗迹中心离区域边缘留出足够空间，避免主体跑到自己负责的区域外。
                    int span = Math.Max(1, region - radius * 2);
                    int anchorX = regionX * region + radius +
                                  (int)(Hash(request.WorldSeed, regionX, regionY, 0x369dea0fu) % (uint)span);
                    int anchorY = regionY * region + radius +
                                  (int)(Hash(request.WorldSeed, regionX, regionY, 0xdb4f0b91u) % (uint)span);
                    for (int worldY = anchorY - radius; worldY <= anchorY + radius; worldY++)
                    {
                        for (int worldX = anchorX - radius; worldX <= anchorX + radius; worldX++)
                        {
                            if (worldX < minWorldX || worldX > maxWorldX ||
                                worldY < minWorldY || worldY > maxWorldY)
                                continue;
                            int x = worldX - minWorldX;
                            int y = worldY - minWorldY;
                            TerrainCell current = terrain.GetCell(x, y);
                            // 这个简化版遗迹只更换陆地表面，不填河海，也不改变原来的障碍和走路规则。
                            if (terrain.GetLiquidDepth(x, y) > 0f)
                                continue;
                            terrain.SetCell(x, y, new TerrainCell(settings.StructureGroundTileId,
                                current.BackTileId, current.BlockingTileId, current.BiomeId,
                                current.NavigationCost, current.Flags));
                            terrain.SetGrass(x, y, GrassEmpty);
                            terrain.SetEnvironmentValue("grass", x, y, 0f);
                            terrain.SetEnvironmentValue("structure", x, y, 1f);
                        }
                    }

                }
            }
        }

        /// <summary>叠加多层不同频率的噪声，生成平滑的地形或气候数值。</summary>
        private static double Fractal(ulong seed, int worldX, int worldY, double scale,
            int octaves, double lacunarity, double persistence,
            ChunkGenerationTopologySnapshot topology)
        {
            // 把几张“大小起伏不同的随机地图”叠在一起：大图决定山势，小图补充细节。
            // 最后把结果缩放到大约 0 到 1，方便后续用统一阈值判断。
            double value = 0d;
            double amplitude = 1d;
            double frequency = scale;
            double total = 0d;
            for (int octave = 0; octave < octaves; octave++)
            {
                ulong octaveSeed = seed + (ulong)octave * 0x9e3779b97f4a7c15UL;
                double sample;
                if (topology.IsWrapped)
                {
                    // 有限世界把随机图也做成首尾相接，保证左右、上下和四个角都没有断缝。
                    int repeatX = Math.Max(1, (int)Math.Round(
                        topology.Span.X * frequency, MidpointRounding.AwayFromZero));
                    int repeatY = Math.Max(1, (int)Math.Round(
                        topology.Span.Y * frequency, MidpointRounding.AwayFromZero));
                    double periodicX = (worldX - topology.Min.X) /
                                       (double)topology.Span.X * repeatX;
                    double periodicY = (worldY - topology.Min.Y) /
                                       (double)topology.Span.Y * repeatY;
                    sample = ValueNoisePeriodic(octaveSeed, periodicX, periodicY,
                        repeatX, repeatY);
                }
                else
                {
                    sample = ValueNoise(octaveSeed, worldX * frequency, worldY * frequency);
                }
                value += sample * amplitude;
                total += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            return total <= 0d ? 0d : value / total;
        }

        /// <summary>计算普通二维值噪声，并在四个随机角点之间平滑插值。</summary>
        private static double ValueNoise(ulong seed, double x, double y)
        {
            // 先算采样点四个角的随机值，再在它们之间平滑过渡，避免地形一格一格突然跳变。
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            double tx = Smooth(x - x0);
            double ty = Smooth(y - y0);
            double a = Hash01(seed, x0, y0);
            double b = Hash01(seed, x0 + 1, y0);
            double c = Hash01(seed, x0, y0 + 1);
            double d = Hash01(seed, x0 + 1, y0 + 1);
            return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
        }

        /// <summary>计算首尾相接的二维值噪声，保证环绕世界边界没有断缝。</summary>
        private static double ValueNoisePeriodic(ulong seed, double x, double y,
            int repeatX, int repeatY)
        {
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            double tx = Smooth(x - x0);
            double ty = Smooth(y - y0);
            // 超过随机图边界的角会绕回开头，这样首尾两边能自然接上。
            int x1 = PositiveMod(x0 + 1, repeatX);
            int y1 = PositiveMod(y0 + 1, repeatY);
            x0 = PositiveMod(x0, repeatX);
            y0 = PositiveMod(y0, repeatY);
            double a = Hash01(seed, x0, y0);
            double b = Hash01(seed, x1, y0);
            double c = Hash01(seed, x0, y1);
            double d = Hash01(seed, x1, y1);
            return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
        }

        /// <summary>组合世界种子、维度、算法版本和用途编号，生成本步骤专用种子。</summary>
        private static ulong CreateSeed(ChunkGenerationRequest request, uint salt)
        {
            // 把世界种子、算法版本、本步骤编号和世界层名字混在一起，得到本步骤专用的随机种子。
            // 这样不同世界、地表和洞穴、温度和高度之间不会误用同一套随机图。
            ulong value = 14695981039346656037UL;
            unchecked
            {
                value = (value ^ (uint)request.WorldSeed) * 1099511628211UL;
                value = (value ^ NoiseLayoutVersion) * 1099511628211UL;
                value = (value ^ salt) * 1099511628211UL;
                for (int i = 0; i < request.Address.DimensionId.Length; i++)
                    value = (value ^ request.Address.DimensionId[i]) * 1099511628211UL;
            }
            return value == 0 ? 0xd1b54a32d192ed03UL : value;
        }

        /// <summary>带用途编号的整数哈希，方便旧式整数种子调用。</summary>
        private static uint Hash(int seed, int x, int y, uint salt) =>
            Hash((ulong)(uint)seed ^ salt, x, y);

        /// <summary>把种子和坐标混合成稳定的伪随机整数。</summary>
        private static uint Hash(ulong seed, int x, int y)
        {
            // 用固定的整数运算把种子和坐标打乱成随机数字；不开游戏自带随机数，所以每次运行都一致。
            unchecked
            {
                ulong value = seed;
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

        /// <summary>返回带用途编号的 0 到 1 伪随机数。</summary>
        private static double Hash01(int seed, int x, int y, uint salt) =>
            Hash(seed, x, y, salt) / (double)uint.MaxValue;
        /// <summary>返回基于长整数种子和坐标的 0 到 1 伪随机数。</summary>
        private static double Hash01(ulong seed, int x, int y) =>
            Hash(seed, x, y) / (double)uint.MaxValue;
        /// <summary>把插值比例平滑处理，让噪声变化更自然。</summary>
        private static double Smooth(double value) => value * value * (3d - 2d * value);
        /// <summary>按比例计算两个数之间的中间值。</summary>
        private static double Lerp(double left, double right, double t) => left + (right - left) * t;
        /// <summary>天然液深统一向上量化为十分位；只要原始值大于零就至少保留 0.1。</summary>
        private static float QuantizeGeneratedLiquidDepth(double depth)
        {
            if (depth <= 0d)
                return 0f;

            double clamped = Clamp01(depth);
            double roundedUp = Math.Ceiling(clamped * 10d - 0.000000001d) / 10d;
            return (float)Math.Max(0.1d, roundedUp);
        }

        /// <summary>把数值限制在 0 到 1 之间。</summary>
        private static double Clamp01(double value) => value < 0d ? 0d : value > 1d ? 1d : value;
        /// <summary>执行真正的向下取整除法，确保负坐标也能正确划分区域。</summary>
        private static int FloorDiv(int value, int divisor)
        {
            // C# 对负数除法会朝 0 取整，但地图区域需要永远向下取整，所以这里手动补正。
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder != 0 && ((remainder < 0) != (divisor < 0)) ? quotient - 1 : quotient;
        }
        /// <summary>计算始终为非负数的取模结果，用于环绕坐标和方向表索引。</summary>
        private static int PositiveMod(int value, int modulus)
        {
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }
    }
}
