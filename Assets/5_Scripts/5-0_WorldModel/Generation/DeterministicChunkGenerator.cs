using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    /// <summary>
    /// 不使用 Unity、可以放到后台运行的区块生成器。
    /// 所有“随机”结果都来自世界种子和坐标，所以输入相同时，无论先生成哪个区块，结果都一样。
    /// </summary>
    public sealed class DeterministicChunkGenerator : IChunkPureGenerator
    {
        /// <summary>纯区块生成规则版本；气候、群系、河谷选路或河网筛选规则改变时递增。</summary>
        public const int CurrentGenerationSignature = 30;

        private readonly LegacyHydrologyKernel legacyHydrologyKernel = new();
        private readonly ConcurrentDictionary<HeightDrivenRegionKey, Lazy<GeneratedHydrologyMap>>
            heightDrivenRegionCache = new();
        private readonly ConcurrentQueue<HeightDrivenRegionKey> heightDrivenCacheOrder = new();

        /// <summary>供纯算法诊断确认新版水文缓存保持有界。</summary>
        internal int CachedHeightDrivenRegionCount => heightDrivenRegionCache.Count;

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
            CancellationToken cancellationToken)
        {
            ChunkGenerationProfileSnapshot profile = request.Profile;
            ChunkGenerationSettingsSnapshot settings = profile.Settings;
            var terrain = new ChunkTerrainBuffer(profile.Width, profile.Height);
            try
            {
                // 设置说是洞穴就生成洞穴；旧配置没有这个设置时，名字里有 cave 也当作洞穴。
                bool cave = settings.Mode == ChunkGenerationMode.Cave ||
                            request.Address.DimensionId.IndexOf("cave",
                                StringComparison.OrdinalIgnoreCase) >= 0;
                GeneratedHydrologyMap riverMap = null;
                if (!cave && settings.RiverEnabled)
                {
                    riverMap = settings.RiverAlgorithm == RiverGenerationAlgorithm.Legacy
                        ? legacyHydrologyKernel.Build(
                            request,
                            settings,
                            position => SampleHeight(
                                request, settings, position.X, position.Y),
                            position => SamplePrecipitation(
                                request, settings, position.X, position.Y),
                            cancellationToken)
                        : BuildHeightDrivenRiverMap(request, settings, cancellationToken);
                }
                for (int y = 0; y < profile.Height; y++)
                {
                    for (int x = 0; x < profile.Width; x++)
                    {
                        // 每处理 64 个格子看一次“是否取消”，既能及时停下，也不会每格都检查拖慢速度。
                        if (((y * profile.Width + x) & 63) == 0)
                            cancellationToken.ThrowIfCancellationRequested();

                        int worldX = request.Address.ChunkOrigin.X + x;
                        int worldY = request.Address.ChunkOrigin.Y + y;
                        if (cave)
                            GenerateCaveCell(request, settings, terrain, x, y, worldX, worldY);
                        else
                            GenerateSurfaceCell(
                                request, settings, terrain, riverMap, x, y, worldX, worldY);
                    }
                }

                // 地表全部铺好后再放遗迹等结构；洞穴不走这一步。
                if (!cave)
                {
                    ApplyStructures(request, settings, terrain, cancellationToken);
                }
                ChunkEcologyData ecology;
                if (cave)
                {
                    // 洞穴不走地表生态规则，改由洞穴布局的矿脉阶段输出纯 Item 放置记录。
                    ecology = CaveGenerationFeatureGenerator.GenerateCave(
                        request, terrain, cancellationToken);
                }
                else
                {
                    ChunkEcologyData surfaceEcology = ChunkEcologyGenerator.Generate(
                        request,
                        terrain,
                        profile.EcologyGlobalMultiplier,
                        profile.EcologyRules,
                        cancellationToken);
                    // 天然矿洞入口优先占用候选格，避免与树木、灌木等生态物重叠。
                    ecology = CaveGenerationFeatureGenerator.AppendSurfacePortals(
                        request, terrain, surfaceEcology);
                }
                return new ChunkGenerationResult(request, terrain, ecology);
            }
            catch
            {
                terrain.Dispose();
                throw;
            }
        }

        #region 地表位置查询

        /// <summary>
        /// 使用与正式区块完全相同的 Profile、气候、Biome、河流和结构结果寻找可走陆地。
        /// 搜索只创建临时纯数据，不注册运行时 Chunk；先按高度跳过海洋，再按需生成候选区块。
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
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
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
            var generatedTerrain = new Dictionary<Int2, ChunkTerrainData>();
            try
            {
                foreach (Int2 candidate in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Int2 origin = ResolveSearchChunkOrigin(candidate, profile, topology);
                    if (!requests.TryGetValue(origin, out ChunkGenerationRequest request))
                    {
                        request = new ChunkGenerationRequest(
                            1,
                            new WorldAddress(dimensionId, origin),
                            worldSeed == 0 ? 1 : worldSeed,
                            1,
                            profile,
                            topology);
                        requests.Add(origin, request);
                    }

                    if (SampleHeight(request, profile.Settings, candidate.X, candidate.Y) <
                        profile.Settings.SeaLevel)
                    {
                        continue;
                    }

                    if (!generatedTerrain.TryGetValue(origin, out ChunkTerrainData terrain))
                    {
                        using ChunkGenerationResult result = Generate(request, cancellationToken);
                        terrain = result.ConsumeTerrain();
                        generatedTerrain.Add(origin, terrain);
                    }

                    int localX = candidate.X - origin.X;
                    int localY = candidate.Y - origin.Y;
                    if ((uint)localX >= (uint)terrain.Width ||
                        (uint)localY >= (uint)terrain.Height)
                    {
                        continue;
                    }

                    TerrainCell cell = terrain.GetCell(localX, localY);
                    if ((cell.Flags & TerrainCellFlags.Water) != 0 ||
                        !terrain.IsWalkable(localX, localY))
                    {
                        continue;
                    }

                    worldCell = candidate;
                    return true;
                }

                return false;
            }
            finally
            {
                foreach (ChunkTerrainData terrain in generatedTerrain.Values)
                    terrain.Dispose();
            }
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

        /// <summary>根据高度、温度、降水和河流结果，生成一个地表格子的完整数据。</summary>
        private static void GenerateSurfaceCell(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            ChunkTerrainBuffer terrain,
            GeneratedHydrologyMap riverMap,
            int x,
            int y,
            int worldX,
            int worldY)
        {
            // 如果世界会绕回另一边，先把越界坐标换回世界内，保证两侧地形能严丝合缝。
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
            double height;
            double basePrecipitation;
            double precipitation;
            double windX;
            double windY;
            double temperature;
            double temperatureCelsius;
            if (settings.SurfaceClimateAlgorithm == SurfaceClimateAlgorithm.LegacyLand)
            {
                LegacyClimateSample climate = LegacyTerrainClimateKernel.SampleClimate(
                    request, settings, worldX, worldY);
                height = climate.Height;
                temperature = climate.Temperature;
                temperatureCelsius = climate.TemperatureCelsius;
                basePrecipitation = climate.BasePrecipitation;
                precipitation = climate.Precipitation;
                windX = climate.WindX;
                windY = climate.WindY;
            }
            else
            {
                height = SampleHeight(request, settings, worldX, worldY);
                precipitation = SamplePrecipitation(request, settings, worldX, worldY);
                basePrecipitation = precipitation;
                windX = 1d;
                windY = 0d;
                double temperatureNoise = Fractal(CreateSeed(request, 0x85ebca6bu),
                    worldX, worldY, settings.ClimateScale, settings.ClimateOctaves,
                    2.07d, 0.5d, request.Topology);
                // 两种气候算法都通过同一个海拔降温入口输出实际温度。
                double latitudeCooling = Math.Min(0.34d, Math.Abs(worldY) * 0.000025d);
                temperature = settings.ApplyAltitudeTemperatureCooling(
                    height, temperatureNoise - latitudeCooling);
                temperatureCelsius = -20d + temperature * 65d;
            }
            bool ocean = height < settings.SeaLevel;
            GeneratedHydrologyCell riverCell = default;
            bool river = !ocean && riverMap != null &&
                         riverMap.TryGet(worldX, worldY, out riverCell);
            double floodplain = !ocean && riverMap != null
                ? riverMap.GetFloodplainStrength(worldX, worldY)
                : 0d;
            double moisture = Clamp01(
                precipitation * 0.78d + (1d - height) * 0.22d + floodplain * 0.18d);
            SurfaceBiomeKind biome = SurfaceBiomeClassifier.Resolve(
                settings, height, temperature, precipitation, moisture, river);
            bool mountain = biome == SurfaceBiomeKind.Stone;
            bool alluvial =
                (biome is SurfaceBiomeKind.Grassland or SurfaceBiomeKind.Forest) &&
                floodplain >= settings.RiverAlluvialTileThreshold;

            // 一个格子可能同时符合几个条件，所以按顺序决定：先海洋、再河流，然后才是沙滩和气候地区。
            int biomeId;
            int groundTileId;
            TerrainCellFlags flags;
            short navigationCost = settings.DefaultNavigationCost;
            if (biome == SurfaceBiomeKind.Ocean)
            {
                biomeId = (int)biome;
                groundTileId = settings.SaltWaterTileId;
                flags = TerrainCellFlags.Water;
                navigationCost = short.MaxValue;
            }
            else if (biome == SurfaceBiomeKind.River)
            {
                biomeId = (int)biome;
                groundTileId = settings.FreshWaterTileId;
                // 河流只是高代价地形：有陆路时 A* 优先绕行，唯一通路是河时仍可渡河。
                flags = TerrainCellFlags.Water | TerrainCellFlags.Walkable;
                navigationCost = settings.RiverNavigationCost;
            }
            else if (biome == SurfaceBiomeKind.Stone)
            {
                // 旧版石地群系从 0.72 高度开始；二维地图直接用石头地面表达山体。
                biomeId = (int)biome;
                groundTileId = settings.StoneTileId;
                flags = TerrainCellFlags.Walkable;
            }
            else if (biome == SurfaceBiomeKind.Beach)
            {
                biomeId = (int)biome;
                groundTileId = settings.SandTileId;
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
                bool iceLake = height <= settings.BeachLevel + 0.1d &&
                               Hash01(request.WorldSeed, worldX, worldY, 0x7f4a7c15u) <
                               settings.SnowIceLakeChance;
                if (iceLake)
                {
                    groundTileId = settings.IceTileId;
                }
                else
                {
                    double snowVariant = Hash01(
                        request.WorldSeed,
                        worldX,
                        worldY,
                        0x9e3779b9u);
                    groundTileId = snowVariant < 0.33d
                        ? settings.SnowVariant2TileId
                        : snowVariant < 0.66d
                            ? settings.SnowVariant3TileId
                            : settings.SnowTileId;
                }
                flags = TerrainCellFlags.Walkable;
                navigationCost = (short)Math.Min(short.MaxValue, navigationCost + 1);

                // 雪地的基础气温固定为零下 10 度，季节、天气等运行时修正仍由环境温度系统叠加。
                temperatureCelsius = -10d;
            }
            else
            {
                biomeId = (int)biome;
                groundTileId = biome == SurfaceBiomeKind.Desert
                    ? settings.SandTileId
                    : settings.GroundTileId;
                flags = TerrainCellFlags.Walkable;
            }

            // 核心格子保存游戏马上要用的结果；高度、温度等详细数值另外保存，供画面和其他系统读取。
            terrain.SetCell(x, y, new TerrainCell(groundTileId, 0, 0, biomeId,
                navigationCost, flags));
            terrain.SetEnvironmentValue("height", x, y, (float)height);
            terrain.SetEnvironmentValue("temperature", x, y, (float)temperature);
            terrain.SetEnvironmentValue("temperature.celsius", x, y,
                (float)temperatureCelsius);
            terrain.SetEnvironmentValue("basePrecipitation", x, y, (float)basePrecipitation);
            terrain.SetEnvironmentValue("precipitation", x, y, (float)precipitation);
            terrain.SetEnvironmentValue("windX", x, y, (float)windX);
            terrain.SetEnvironmentValue("windY", x, y, (float)windY);
            terrain.SetEnvironmentValue("moisture", x, y, (float)moisture);
            terrain.SetEnvironmentValue("mountain", x, y, mountain ? 1f : 0f);
            terrain.SetEnvironmentValue("riverDepth", x, y, river ? (float)riverCell.Depth : 0f);
            terrain.SetEnvironmentValue("riverFlow", x, y, river ? (float)riverCell.Flow : 0f);
            terrain.SetEnvironmentValue("riverFloodplain", x, y, (float)floodplain);
            terrain.SetEnvironmentValue("riverSurfaceLevel", x, y,
                river ? (float)riverCell.SurfaceLevel : 0f);
            terrain.SetEnvironmentValue("riverKind", x, y,
                river ? (float)riverCell.Kind : 0f);
            terrain.SetEnvironmentValue("structure", x, y, 0f);

            // 草长不长只看世界种子、坐标和湿度，不用会变化的全局随机数，所以每次结果相同。
            bool snowSurface = biome == SurfaceBiomeKind.Snow &&
                               groundTileId != settings.IceTileId;
            double grassDensity = snowSurface
                ? settings.GrassDensity * settings.SnowGrassDensityMultiplier
                : settings.GrassDensity;
            bool grass = (flags & TerrainCellFlags.Walkable) != 0 &&
                         (groundTileId == settings.GroundTileId || snowSurface) &&
                         Hash01(request.WorldSeed, worldX, worldY, 0x165667b1u) <
                         grassDensity * (0.55d + moisture * 0.75d);
            terrain.SetGrass(x, y, grass ? GrassPresent : GrassEmpty);
            terrain.SetEnvironmentValue("grass", x, y, grass ? 1f : 0f);
        }

        /// <summary>按冻结参数采样不含河流覆盖的基础地表群系，并同时返回同格高度。</summary>
        internal static SurfaceBiomeKind SampleBaseSurfaceBiome(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            int worldX,
            int worldY,
            out double height)
        {
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
            double temperature;
            double precipitation;
            if (settings.SurfaceClimateAlgorithm == SurfaceClimateAlgorithm.LegacyLand)
            {
                LegacyClimateSample climate = LegacyTerrainClimateKernel.SampleClimate(
                    request, settings, worldX, worldY);
                height = climate.Height;
                temperature = climate.Temperature;
                precipitation = climate.Precipitation;
            }
            else
            {
                height = SampleHeight(request, settings, worldX, worldY);
                precipitation = SamplePrecipitation(request, settings, worldX, worldY);
                double temperatureNoise = Fractal(CreateSeed(request, 0x85ebca6bu),
                    worldX, worldY, settings.ClimateScale, settings.ClimateOctaves,
                    2.07d, 0.5d, request.Topology);
                double latitudeCooling = Math.Min(0.34d, Math.Abs(worldY) * 0.000025d);
                temperature = settings.ApplyAltitudeTemperatureCooling(
                    height, temperatureNoise - latitudeCooling);
            }

            double moisture = Clamp01(precipitation * 0.78d + (1d - height) * 0.22d);
            return SurfaceBiomeClassifier.Resolve(
                settings, height, temperature, precipitation, moisture, false);
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

        /// <summary>按世界种子和坐标采样降水量，供湿度和河流计算使用。</summary>
        private static double SamplePrecipitation(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            int worldX,
            int worldY)
        {
            if (settings.SurfaceClimateAlgorithm == SurfaceClimateAlgorithm.LegacyLand)
            {
                return LegacyTerrainClimateKernel.SamplePrecipitation(
                    request, settings, worldX, worldY);
            }
            worldX = request.Topology.NormalizeX(worldX);
            worldY = request.Topology.NormalizeY(worldY);
            return Fractal(
                CreateSeed(request, 0xc2b2ae35u),
                worldX,
                worldY,
                settings.ClimateScale * 0.83d,
                settings.ClimateOctaves,
                2.11d,
                0.53d,
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

        // 斜向河段已经补过正交格，因此这里用四邻域统计一整条连续河网。
        private static readonly Int2[] RiverCardinalNeighbors =
        {
            new Int2(-1, 0), new Int2(1, 0), new Int2(0, -1), new Int2(0, 1)
        };

        // D∞ 连续坡向按逆时针排序；连续偏转只在严格下坡候选中改变选路。
        private static readonly Int2[] RiverDirectionsCounterClockwise =
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
            CancellationToken cancellationToken)
        {
            HeightDrivenRegionDescriptor region = ResolveHeightDrivenRegion(request, settings);
            var key = new HeightDrivenRegionKey(
                request.WorldEpoch,
                request.Address.DimensionId,
                request.WorldSeed,
                request.Profile.GenerationFingerprint,
                request.Topology,
                region.Origin,
                region.Width,
                region.Height);
            var candidate = new Lazy<GeneratedHydrologyMap>(() =>
                BuildHeightDrivenRiverRegion(
                    CreateHeightDrivenRegionRequest(request, region),
                    settings,
                    cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication);
            Lazy<GeneratedHydrologyMap> shared = heightDrivenRegionCache.GetOrAdd(key, candidate);
            if (ReferenceEquals(shared, candidate))
            {
                heightDrivenCacheOrder.Enqueue(key);
                TrimHeightDrivenRegionCache(settings.RiverMaxCachedRegions);
            }

            try
            {
                return shared.Value;
            }
            catch
            {
                if (heightDrivenRegionCache.TryGetValue(key, out Lazy<GeneratedHydrologyMap> failed) &&
                    ReferenceEquals(failed, shared))
                    heightDrivenRegionCache.TryRemove(key, out _);
                throw;
            }
        }

        /// <summary>按世界固定区域单次构建河网；同区域相邻区块只会共享这一份结果。</summary>
        private static GeneratedHydrologyMap BuildHeightDrivenRiverRegion(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            CancellationToken cancellationToken)
        {
            var sampling = new HydrologySamplingContext(request, settings);
            var flowByCell = new Dictionary<Int2, double>();
            var terminalFlowByCell = new Dictionary<Int2, double>();
            var processedSourceOrigins = new HashSet<Int2>();
            int maximumRadius = Math.Max(0, (settings.RiverMaxWidth - 1) / 2);
            int padding = settings.RiverMaxTraceSteps + maximumRadius + 1;
            int sourceCellSize = settings.RiverRunoffCellSize;
            int anchorX = request.Topology.IsWrapped ? request.Topology.Min.X : 0;
            int anchorY = request.Topology.IsWrapped ? request.Topology.Min.Y : 0;
            int minX = request.Address.ChunkOrigin.X;
            int minY = request.Address.ChunkOrigin.Y;
            int maxX = minX + request.Profile.Width - 1;
            int maxY = minY + request.Profile.Height - 1;
            int minSourceX = FloorDiv(minX - padding - anchorX, sourceCellSize);
            int maxSourceX = FloorDiv(maxX + padding - anchorX, sourceCellSize);
            int minSourceY = FloorDiv(minY - padding - anchorY, sourceCellSize);
            int maxSourceY = FloorDiv(maxY + padding - anchorY, sourceCellSize);

            for (int sourceY = minSourceY; sourceY <= maxSourceY; sourceY++)
            {
                for (int sourceX = minSourceX; sourceX <= maxSourceX; sourceX++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Int2 sourceOrigin = sampling.Normalize(new Int2(
                        anchorX + sourceX * sourceCellSize,
                        anchorY + sourceY * sourceCellSize));
                    if (!processedSourceOrigins.Add(sourceOrigin))
                        continue;

                    ProcessRunoffSource(
                        sampling,
                        sourceOrigin,
                        flowByCell,
                        terminalFlowByCell,
                        cancellationToken);
                }
            }

            HashSet<Int2> visibleFlowCells = BuildVisibleFlowCells(
                sampling,
                flowByCell,
                settings.RiverStartFlow,
                settings.RiverTributaryStartFlow,
                settings.RiverMinimumVisibleCourseLength);
            var riverCells = new Dictionary<Int2, GeneratedHydrologyCell>();
            var floodplainCells = new Dictionary<Int2, double>();
            foreach (KeyValuePair<Int2, double> pair in flowByCell)
            {
                if (!visibleFlowCells.Contains(pair.Key))
                    continue;

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

                        Int2 waterPosition = sampling.Normalize(new Int2(
                            pair.Key.X + offsetX,
                            pair.Key.Y + offsetY));
                        if (!ContainsChunk(request, waterPosition) ||
                            sampling.Height(waterPosition) <= settings.SeaLevel)
                        {
                            continue;
                        }

                        double distance = Math.Sqrt(offsetX * offsetX + offsetY * offsetY);
                        double edgeStrength = radius == 0
                            ? 1d
                            : 1d - Clamp01(distance / (radius + 0.5d));
                        double depth = Lerp(settings.RiverDepthMin, centerDepth, edgeStrength);
                        SetRiverCell(riverCells, waterPosition, new GeneratedHydrologyCell(
                            GeneratedHydrologyKind.River, pair.Value, depth));
                    }
                }

                AddFloodplain(
                    request,
                    settings,
                    sampling,
                    pair.Key,
                    pair.Value,
                    radius,
                    floodplainCells);
            }

            AddHeightDrivenTerminalLakes(
                request,
                settings,
                sampling,
                terminalFlowByCell,
                riverCells,
                cancellationToken);

            return new GeneratedHydrologyMap(riverCells, floodplainCells);
        }

        /// <summary>把足够汇流的内陆低洼扩展为淡水湖，河流入海时不生成湖。</summary>
        private static void AddHeightDrivenTerminalLakes(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            HydrologySamplingContext sampling,
            IReadOnlyDictionary<Int2, double> terminalFlowByCell,
            Dictionary<Int2, GeneratedHydrologyCell> riverCells,
            CancellationToken cancellationToken)
        {
            foreach (KeyValuePair<Int2, double> terminal in terminalFlowByCell)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (terminal.Value < settings.RiverLakeMinFlow ||
                    Hash01(request.WorldSeed, terminal.Key.X, terminal.Key.Y, 0x6c8e9cf5u) >=
                    settings.RiverLakeChance)
                    continue;

                double sinkHeight = sampling.Height(terminal.Key);
                if (sinkHeight <= settings.SeaLevel)
                    continue;

                var basin = new HashSet<Int2> { terminal.Key };
                var frontier = new List<Int2>();
                for (int i = 0; i < RiverNeighbors.Length; i++)
                    frontier.Add(sampling.Normalize(terminal.Key + RiverNeighbors[i]));

                while (frontier.Count > 0 && basin.Count < settings.RiverMaxLakeCells)
                {
                    int lowestIndex = 0;
                    double lowestHeight = sampling.Height(frontier[0]);
                    for (int i = 1; i < frontier.Count; i++)
                    {
                        double candidateHeight = sampling.Height(frontier[i]);
                        if (candidateHeight >= lowestHeight)
                            continue;
                        lowestIndex = i;
                        lowestHeight = candidateHeight;
                    }

                    Int2 current = frontier[lowestIndex];
                    frontier.RemoveAt(lowestIndex);
                    if (basin.Contains(current) ||
                        lowestHeight - sinkHeight > settings.RiverMaxLakeLevelRise)
                        continue;
                    basin.Add(current);
                    for (int i = 0; i < RiverNeighbors.Length; i++)
                    {
                        Int2 neighbor = sampling.Normalize(current + RiverNeighbors[i]);
                        if (!basin.Contains(neighbor) && !frontier.Contains(neighbor))
                            frontier.Add(neighbor);
                    }
                }

                if (basin.Count < settings.RiverMinLakeCells)
                    continue;
                foreach (Int2 lakePosition in basin)
                {
                    if (!ContainsChunk(request, lakePosition) ||
                        sampling.Height(lakePosition) <= settings.SeaLevel)
                        continue;
                    double depthT = Clamp01((sinkHeight + settings.RiverMaxLakeLevelRise -
                                             sampling.Height(lakePosition)) /
                                            settings.RiverMaxLakeLevelRise);
                    SetRiverCell(riverCells, lakePosition, new GeneratedHydrologyCell(
                        GeneratedHydrologyKind.Lake,
                        terminal.Value,
                        Lerp(settings.RiverDepthMin, settings.RiverDepthMax,
                            Math.Max(0.15d, depthT)),
                        sinkHeight + settings.RiverMaxLakeLevelRise));
                }
            }
        }

        /// <summary>把缓存限制在配置上限内，优先移除最早加入的区域。</summary>
        private void TrimHeightDrivenRegionCache(int maximumRegions)
        {
            maximumRegions = Math.Max(1, maximumRegions);
            while (heightDrivenRegionCache.Count > maximumRegions &&
                   heightDrivenCacheOrder.TryDequeue(out HeightDrivenRegionKey oldest))
                heightDrivenRegionCache.TryRemove(oldest, out _);
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

        /// <summary>
        /// 先按主河门槛找出成熟长河，再补回真正汇入主河的低流量支流。
        /// 这只做视觉筛选，不改变高度图、坡向或水流模拟状态。
        /// </summary>
        private static HashSet<Int2> BuildVisibleFlowCells(
            HydrologySamplingContext sampling,
            IReadOnlyDictionary<Int2, double> flowByCell,
            double startFlow,
            double tributaryStartFlow,
            int minimumCourseLength)
        {
            var mainCandidates = new HashSet<Int2>();
            foreach (KeyValuePair<Int2, double> pair in flowByCell)
            {
                if (pair.Value >= startFlow)
                    mainCandidates.Add(pair.Key);
            }

            var visible = new HashSet<Int2>();
            var visited = new HashSet<Int2>();
            var queue = new Queue<Int2>();
            var component = new List<Int2>();
            foreach (Int2 start in mainCandidates)
            {
                if (!visited.Add(start))
                    continue;

                queue.Enqueue(start);
                component.Clear();
                while (queue.Count > 0)
                {
                    Int2 current = queue.Dequeue();
                    component.Add(current);
                    for (int i = 0; i < RiverCardinalNeighbors.Length; i++)
                    {
                        Int2 neighbor = sampling.Normalize(
                            current + RiverCardinalNeighbors[i]);
                        if (mainCandidates.Contains(neighbor) && visited.Add(neighbor))
                            queue.Enqueue(neighbor);
                    }
                }

                if (component.Count < Math.Max(1, minimumCourseLength))
                    continue;
                for (int i = 0; i < component.Count; i++)
                    visible.Add(component[i]);
            }

            if (visible.Count == 0 || tributaryStartFlow >= startFlow)
                return visible;

            // 支流必须通过低流量连通网接入成熟主河，不能作为孤立短水线单独出现。
            var tributaryCandidates = new HashSet<Int2>();
            foreach (KeyValuePair<Int2, double> pair in flowByCell)
            {
                if (pair.Value >= tributaryStartFlow)
                    tributaryCandidates.Add(pair.Key);
            }

            visited.Clear();
            foreach (Int2 start in tributaryCandidates)
            {
                if (!visited.Add(start))
                    continue;

                bool joinsMainRiver = false;
                queue.Enqueue(start);
                component.Clear();
                while (queue.Count > 0)
                {
                    Int2 current = queue.Dequeue();
                    component.Add(current);
                    joinsMainRiver |= visible.Contains(current);
                    for (int i = 0; i < RiverCardinalNeighbors.Length; i++)
                    {
                        Int2 neighbor = sampling.Normalize(
                            current + RiverCardinalNeighbors[i]);
                        if (tributaryCandidates.Contains(neighbor) && visited.Add(neighbor))
                            queue.Enqueue(neighbor);
                    }
                }

                if (!joinsMainRiver)
                    continue;
                for (int i = 0; i < component.Count; i++)
                    visible.Add(component[i]);
            }
            return visible;
        }

        /// <summary>汇总一个径流单元的降水，并从最高有效采样点开始沿高度图下行。</summary>
        private static void ProcessRunoffSource(
            HydrologySamplingContext sampling,
            Int2 sourceOrigin,
            Dictionary<Int2, double> flowByCell,
            Dictionary<Int2, double> terminalFlowByCell,
            CancellationToken cancellationToken)
        {
            ChunkGenerationSettingsSnapshot settings = sampling.Settings;
            int stride = settings.RiverRunoffSampleStride;
            double runoffSum = 0d;
            int sampleCount = 0;
            Int2 source = default;
            double sourceScore = double.MinValue;

            for (int localY = stride / 2; localY < settings.RiverRunoffCellSize; localY += stride)
            {
                for (int localX = stride / 2; localX < settings.RiverRunoffCellSize; localX += stride)
                {
                    Int2 position = sampling.Normalize(new Int2(
                        sourceOrigin.X + localX,
                        sourceOrigin.Y + localY));
                    double height = sampling.Height(position);
                    double precipitation = sampling.Precipitation(position);
                    sampleCount++;
                    if (height <= settings.SeaLevel)
                        continue;

                    double runoff = Clamp01(
                        (precipitation - settings.RiverInfiltrationFloor) /
                        Math.Max(0.0001d, 1d - settings.RiverInfiltrationFloor));
                    runoffSum += runoff;
                    double score = height + runoff * 0.05d +
                                   Hash01(sampling.Request.WorldSeed, position.X, position.Y,
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

            TraceRunoff(sampling, source, contribution, flowByCell, terminalFlowByCell,
                cancellationToken);
        }

        /// <summary>沿八邻域最低点追踪径流；到海洋或真实局部洼地后结束。</summary>
        private static void TraceRunoff(
            HydrologySamplingContext sampling,
            Int2 source,
            double contribution,
            Dictionary<Int2, double> flowByCell,
            Dictionary<Int2, double> terminalFlowByCell,
            CancellationToken cancellationToken)
        {
            var visited = new HashSet<Int2>();
            Int2 current = source;
            for (int step = 0; step < sampling.Settings.RiverMaxTraceSteps; step++)
            {
                if ((step & 31) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                current = sampling.Normalize(current);
                if (!visited.Add(current))
                    break;

                double currentHeight = sampling.Height(current);
                if (currentHeight <= sampling.Settings.SeaLevel)
                    break;
                AddFlow(flowByCell, current, contribution);

                if (!TryChooseDownhill(sampling, current, currentHeight, out Int2 next))
                {
                    AddFlow(terminalFlowByCell, current, contribution);
                    break;
                }
                AddDiagonalBridge(sampling, current, next, contribution, flowByCell);
                current = next;
            }
        }

        /// <summary>
        /// 从严格下坡邻格中选择能继续进入低谷的方向。
        /// 评分只读取同一高度图：细谷残差负责弯曲，前视高度负责避免短视锯齿。
        /// </summary>
        private static bool TryChooseDownhill(
            HydrologySamplingContext sampling,
            Int2 current,
            double currentHeight,
            out Int2 next)
        {
            if (sampling.TryGetCachedDownstream(current, out DownstreamChoice cached))
            {
                next = cached.Next;
                return cached.Found;
            }

            if (TryChooseDInfinityDownhill(sampling, current, currentHeight, out next))
            {
                sampling.CacheDownstream(current, new DownstreamChoice(true, next));
                return true;
            }

            next = default;
            double bestScore = double.MaxValue;
            bool found = false;
            for (int i = 0; i < RiverNeighbors.Length; i++)
            {
                Int2 candidate = sampling.Normalize(current + RiverNeighbors[i]);
                if (candidate == current)
                    continue;
                double height = sampling.Height(candidate);
                if (height >= currentHeight - DownhillEpsilon)
                    continue;

                Int2 direction = RiverNeighbors[i];
                Int2 lookAhead = sampling.Normalize(new Int2(
                    candidate.X + direction.X * sampling.Settings.RiverLookAheadDistance,
                    candidate.Y + direction.Y * sampling.Settings.RiverLookAheadDistance));
                double score = Lerp(
                                   sampling.RoutingHeight(candidate),
                                   sampling.RoutingHeight(lookAhead),
                                   sampling.Settings.RiverLookAheadWeight) +
                               Hash01(
                    sampling.Request.WorldSeed,
                    candidate.X,
                    candidate.Y,
                    0x9e3779b9u) * sampling.Settings.RiverMeanderTieTolerance;
                if (score >= bestScore)
                    continue;
                bestScore = score;
                next = candidate;
                found = true;
            }

            sampling.CacheDownstream(current, new DownstreamChoice(found, next));
            return found;
        }

        /// <summary>
        /// 用高度图的连续负梯度求 D∞ 坡向，再叠加世界坐标连续变化的轻微偏转。
        /// 偏转只负责在下坡方向间选路，不生成水体；每个实际河格仍严格低于上游。
        /// </summary>
        private static bool TryChooseDInfinityDownhill(
            HydrologySamplingContext sampling,
            Int2 current,
            double currentHeight,
            out Int2 next)
        {
            next = default;
            int gradientRadius = Math.Max(1, sampling.Settings.RiverLookAheadDistance / 2);
            double gradientX = sampling.RoutingHeight(new Int2(
                                   current.X + gradientRadius,
                                   current.Y)) -
                               sampling.RoutingHeight(new Int2(
                                   current.X - gradientRadius,
                                   current.Y));
            double gradientY = sampling.RoutingHeight(new Int2(
                                   current.X,
                                   current.Y + gradientRadius)) -
                               sampling.RoutingHeight(new Int2(
                                   current.X,
                                   current.Y - gradientRadius));
            double magnitudeSquared = gradientX * gradientX + gradientY * gradientY;
            if (magnitudeSquared <= DownhillEpsilon * DownhillEpsilon)
                return false;

            double angle = Math.Atan2(-gradientY, -gradientX);
            if (angle < 0d)
                angle += Math.PI * 2d;
            double directionPosition = angle / (Math.PI / 4d) +
                                       sampling.MeanderBias(current) *
                                       sampling.Settings.RiverMeanderStrength;
            int lowerIndex = PositiveMod((int)Math.Floor(directionPosition), 8);
            int upperIndex = (lowerIndex + 1) & 7;
            double upperWeight = directionPosition - Math.Floor(directionPosition);
            double dither = ResolveDirectionDither(sampling, current);
            bool preferUpper = upperWeight >= dither;

            Int2 firstDirection = RiverDirectionsCounterClockwise[
                preferUpper ? upperIndex : lowerIndex];
            Int2 secondDirection = RiverDirectionsCounterClockwise[
                preferUpper ? lowerIndex : upperIndex];
            if (TryUseDownhillDirection(
                    sampling, current, currentHeight, firstDirection, out next))
            {
                return true;
            }

            return TryUseDownhillDirection(
                sampling, current, currentHeight, secondDirection, out next);
        }

        /// <summary>尝试沿指定方向走到严格更低的格子，并返回新的位置。</summary>
        private static bool TryUseDownhillDirection(
            HydrologySamplingContext sampling,
            Int2 current,
            double currentHeight,
            Int2 direction,
            out Int2 next)
        {
            next = sampling.Normalize(current + direction);
            return next != current && sampling.Height(next) < currentHeight - DownhillEpsilon;
        }

        /// <summary>按世界种子与坐标分配相邻坡向，避免固定 4×4 图案形成周期直线。</summary>
        private static double ResolveDirectionDither(
            HydrologySamplingContext sampling,
            Int2 position)
        {
            position = sampling.Normalize(position);
            return Hash01(
                sampling.Request.WorldSeed,
                position.X,
                position.Y,
                0x27d4eb2fu);
        }

        /// <summary>只在低坡且已有明显汇流的主河两侧生成宽缓冲积带。</summary>
        private static void AddFloodplain(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            HydrologySamplingContext sampling,
            Int2 center,
            double flow,
            int channelRadius,
            Dictionary<Int2, double> floodplainCells)
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
                    if (!ContainsChunk(request, position) ||
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

        /// <summary>补齐斜向流动的一个正交格，避免 Tilemap 上出现仅角点接触的断河。</summary>
        private static void AddDiagonalBridge(
            HydrologySamplingContext sampling,
            Int2 current,
            Int2 next,
            double contribution,
            Dictionary<Int2, double> flowByCell)
        {
            int deltaX = ShortestDelta(current.X, next.X, sampling.Request.Topology, true);
            int deltaY = ShortestDelta(current.Y, next.Y, sampling.Request.Topology, false);
            if (deltaX == 0 || deltaY == 0)
                return;

            Int2 horizontal = sampling.Normalize(new Int2(current.X + deltaX, current.Y));
            Int2 vertical = sampling.Normalize(new Int2(current.X, current.Y + deltaY));
            Int2 bridge = sampling.Height(horizontal) <= sampling.Height(vertical)
                ? horizontal
                : vertical;
            AddFlow(flowByCell, bridge, contribution);
        }

        /// <summary>计算两个坐标之间的最短位移；环绕世界会优先选择跨边界的短路。</summary>
        private static int ShortestDelta(
            int from,
            int to,
            ChunkGenerationTopologySnapshot topology,
            bool horizontal)
        {
            int delta = to - from;
            if (!topology.IsWrapped)
                return delta;
            int span = horizontal ? topology.Span.X : topology.Span.Y;
            if (delta > span / 2)
                delta -= span;
            else if (delta < -span / 2)
                delta += span;
            return delta;
        }

        /// <summary>把一份径流量累加到指定格子。</summary>
        private static void AddFlow(
            Dictionary<Int2, double> flowByCell,
            Int2 position,
            double contribution)
        {
            flowByCell.TryGetValue(position, out double current);
            flowByCell[position] = current + contribution;
        }

        /// <summary>合并淡水格；湖泊优先于河道，同类则保留更深和更大流量。</summary>
        private static void SetRiverCell(
            Dictionary<Int2, GeneratedHydrologyCell> cells,
            Int2 position,
            GeneratedHydrologyCell candidate)
        {
            if (cells.TryGetValue(position, out GeneratedHydrologyCell current) &&
                current.Kind >= candidate.Kind &&
                current.Depth >= candidate.Depth && current.Flow >= candidate.Flow)
            {
                return;
            }

            GeneratedHydrologyKind kind = current.Kind == GeneratedHydrologyKind.Lake ||
                                           candidate.Kind == GeneratedHydrologyKind.Lake
                ? GeneratedHydrologyKind.Lake
                : GeneratedHydrologyKind.River;
            cells[position] = new GeneratedHydrologyCell(
                kind,
                Math.Max(current.Flow, candidate.Flow),
                Math.Max(current.Depth, candidate.Depth),
                Math.Max(current.SurfaceLevel, candidate.SurfaceLevel));
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

        private readonly struct DownstreamChoice
        {
            /// <summary>记录某个格子是否找到下游，以及找到的下游坐标。</summary>
            public DownstreamChoice(bool found, Int2 next)
            {
                Found = found;
                Next = next;
            }

            public bool Found { get; }
            public Int2 Next { get; }
        }

        /// <summary>复用单次区块水文计算中的高度和降水采样，避免重复计算同一格噪声。</summary>
        private sealed class HydrologySamplingContext
        {
            private readonly Dictionary<Int2, double> heightCache = new();
            private readonly Dictionary<Int2, double> precipitationCache = new();
            private readonly Dictionary<Int2, double> routingHeightCache = new();
            private readonly Dictionary<Int2, double> meanderBiasCache = new();
            private readonly Dictionary<Int2, DownstreamChoice> downstreamCache = new();

            /// <summary>创建一次水文采样上下文，并准备各类采样缓存。</summary>
            public HydrologySamplingContext(
                ChunkGenerationRequest request,
                ChunkGenerationSettingsSnapshot settings)
            {
                Request = request;
                Settings = settings;
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
                    value = SampleHeight(Request, Settings, position.X, position.Y);
                    heightCache.Add(position, value);
                }
                return value;
            }

            /// <summary>读取坐标降水量；第一次读取后缓存结果。</summary>
            public double Precipitation(Int2 position)
            {
                position = Normalize(position);
                if (!precipitationCache.TryGetValue(position, out double value))
                {
                    value = SamplePrecipitation(Request, Settings, position.X, position.Y);
                    precipitationCache.Add(position, value);
                }
                return value;
            }

            /// <summary>高度图细节相对邻域均值越低，越像天然谷底，河流评分也越低。</summary>
            public double RoutingHeight(Int2 position)
            {
                position = Normalize(position);
                if (routingHeightCache.TryGetValue(position, out double value))
                    return value;

                double center = Height(position);
                const int detailRadius = 2;
                double average = (center * 4d +
                                  Height(new Int2(position.X - detailRadius, position.Y)) +
                                  Height(new Int2(position.X + detailRadius, position.Y)) +
                                  Height(new Int2(position.X, position.Y - detailRadius)) +
                                  Height(new Int2(position.X, position.Y + detailRadius))) / 8d;
                value = center + (center - average) * Settings.RiverValleyDetailWeight;
                routingHeightCache.Add(position, value);
                return value;
            }

            /// <summary>
            /// 读取缓慢变化的确定性选路偏转。它不参与水量与水体绘制，
            /// 只让长距离坡向不再永久锁在水平、竖直或 45° 网格线上。
            /// </summary>
            public double MeanderBias(Int2 position)
            {
                position = Normalize(position);
                if (meanderBiasCache.TryGetValue(position, out double value))
                    return value;

                double scale = Settings.RiverMeanderScale;
                double broad = Fractal(
                    CreateSeed(Request, 0x632be59bu),
                    position.X,
                    position.Y,
                    1d / scale,
                    2,
                    2.03d,
                    0.52d,
                    Request.Topology);
                double detail = Fractal(
                    CreateSeed(Request, 0x85157af5u),
                    position.X,
                    position.Y,
                    1d / Math.Max(8d, scale * 0.43d),
                    2,
                    2.11d,
                    0.48d,
                    Request.Topology);
                value = Math.Max(-1d, Math.Min(1d,
                    (broad * 0.72d + detail * 0.28d - 0.5d) * 2.6d));
                meanderBiasCache.Add(position, value);
                return value;
            }

            /// <summary>查询是否已经算过该格子的下游方向。</summary>
            public bool TryGetCachedDownstream(Int2 position, out DownstreamChoice choice)
            {
                return downstreamCache.TryGetValue(Normalize(position), out choice);
            }

            /// <summary>缓存该格子的下游方向，后续河流追踪可以直接复用。</summary>
            public void CacheDownstream(Int2 position, DownstreamChoice choice)
            {
                downstreamCache[Normalize(position)] = choice;
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
            // 洞穴岩壁下保留石地；开放洞室可按世界区域形成跨 Chunk 连续的地下湖。
            bool open = CaveLayoutKernel.IsOpenAtWorld(
                request, settings, worldX, worldY, surfaceInfluence);
            double groundwaterDepth = open
                ? CaveLayoutKernel.SampleGroundwaterDepth(
                    request, settings, worldX, worldY, surfaceInfluence)
                : 0d;
            bool groundwater = groundwaterDepth > 0d;
            TerrainCellFlags flags = !open
                ? TerrainCellFlags.Blocking
                : groundwater ? TerrainCellFlags.Water : TerrainCellFlags.Walkable;
            int groundTileId = groundwater ? settings.FreshWaterTileId : settings.CaveFloorTileId;
            terrain.SetCell(x, y, new TerrainCell(groundTileId, 0,
                open ? 0 : settings.CaveWallTileId, 100,
                open && !groundwater ? settings.DefaultNavigationCost : short.MaxValue, flags));
            terrain.SetEnvironmentValue("height", x, y,
                groundwater ? (float)(1d - groundwaterDepth) : open ? 1f : 0f);
            terrain.SetEnvironmentValue("temperature", x, y, 0.38f);
            terrain.SetEnvironmentValue("temperature.celsius", x, y, 8f);
            terrain.SetEnvironmentValue("precipitation", x, y, 0f);
            terrain.SetEnvironmentValue("moisture", x, y, groundwater ? 1f : 0.3f);
            terrain.SetEnvironmentValue("riverDepth", x, y, (float)groundwaterDepth);
            terrain.SetEnvironmentValue("riverFlow", x, y, 0f);
            terrain.SetEnvironmentValue("riverKind", x, y, groundwater ? 2f : 0f);
            terrain.SetEnvironmentValue("groundwater", x, y, groundwater ? 1f : 0f);
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
                            if ((current.Flags & TerrainCellFlags.Water) != 0)
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
