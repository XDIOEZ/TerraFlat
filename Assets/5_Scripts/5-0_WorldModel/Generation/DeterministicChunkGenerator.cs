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
        /// <summary>纯区块生成规则版本；气候、群系、河流或生态空间分布规则改变时递增。</summary>
        public const int CurrentGenerationSignature = 44;

        private readonly LiquidTypeCatalog liquidTypes;
        /// <summary>资源就绪后注入会话液体表；离线纯算法测试可以使用本体最小目录。</summary>
        public DeterministicChunkGenerator(LiquidTypeCatalog liquidTypes = null)
        {
            this.liquidTypes = liquidTypes ?? LiquidTypeCatalog.BuiltIn;
        }

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
            var terrain = new ChunkTerrainBuffer(profile.Width, profile.Height, liquidTypes);
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
                if (!cave && settings.LargeLakeEnabled)
                    riverMap = LargeFreshwaterLakeKernel.Build(request, settings, riverMap,
                        position => SampleHeight(request, settings, position.X, position.Y), cancellationToken);
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
                    // 洞穴阶段合并当前 Profile 的植物规则、入口、藤蔓与矿物放置记录。
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

                    if (terrain.GetLiquidDepth(localX, localY) > 0f ||
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
            if (ocean)
                moisture = Math.Max(moisture, settings.OceanMoistureFloor);
            SurfaceBiomeKind biome = SurfaceBiomeClassifier.Resolve(
                settings, height, temperature, precipitation, moisture, river);
            bool frozenRiver = river && SurfaceBiomeClassifier.IsSnowClimate(
                settings, temperature, precipitation);
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
                groundTileId = settings.SeabedTileId;
                flags = TerrainCellFlags.Walkable;
                // 液体导航代价由消费层单独叠加，底部 Ground 保留陆地成本。
            }
            else if (biome == SurfaceBiomeKind.River)
            {
                biomeId = (int)biome;
                if (frozenRiver)
                {
                    // 河流仍保留 River 群系编号和水文数据，但雪地气候下改用真正的冰地块；
                    // 生成冰面时不写入液体，避免冰块继续触发水面表现和液体玩法效果。
                    groundTileId = settings.IceTileId;
                    flags = TerrainCellFlags.Walkable;
                    navigationCost = (short)Math.Min(short.MaxValue, navigationCost + 1);
                    temperatureCelsius = -10d;
                }
                else
                {
                    groundTileId = settings.RiverbedTileId;
                    // 水域只是高代价地形：有陆路时 A* 优先绕行，唯一通路是水面时仍可通过。
                    flags = TerrainCellFlags.Walkable;
                    // 液体导航代价由消费层单独叠加，底部 Ground 保留陆地成本。
                }
            }
            else if (biome == SurfaceBiomeKind.Stone)
            {
                // 旧版石地群系从 0.72 高度开始；二维地图直接用石头地面表达山体。
                biomeId = (int)biome;
                groundTileId = settings.StoneTileId;
                flags = TerrainCellFlags.Walkable;

                // 山地基础气温固定为 10℃，天气与局部冷热源仍由环境温度系统叠加。
                temperatureCelsius = 10d;
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
                    // 雪山地表统一使用纯白雪地；视觉差异不能再由随机哈希决定。
                    groundTileId = settings.SnowTileId;
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

            // 地面与液体独立写入；height 先供生成期生态筛选，Seal 时原数组直接移交为只读地表海拔表现数据。
            // 液体仍只读取独立 LiquidDepth，禁止用海拔反算液深。
            terrain.SetCell(x, y, new TerrainCell(groundTileId, 0, 0, biomeId,
                navigationCost, flags));
            terrain.SetEnvironmentValue("height", x, y, (float)height);
            float initialLiquidDepth = ocean
                ? (float)(1d - Math.Pow(Clamp01(height / Math.Max(0.0001d, settings.SeaLevel)), 2d))
                : river && !frozenRiver ? (float)riverCell.Depth : 0f;
            terrain.SetLiquid(x, y, initialLiquidDepth > 0f
                ? terrain.LiquidTypes.GetIndex(ocean ? LiquidTypeCatalog.SeaWaterId : LiquidTypeCatalog.DirtyWaterId) : 0,
                initialLiquidDepth);
            terrain.SetEnvironmentValue("temperature", x, y, (float)temperature);
            terrain.SetEnvironmentValue("temperature.celsius", x, y,
                (float)temperatureCelsius);
            terrain.SetEnvironmentValue("basePrecipitation", x, y, (float)basePrecipitation);
            terrain.SetEnvironmentValue("precipitation", x, y, (float)precipitation);
            terrain.SetEnvironmentValue("windX", x, y, (float)windX);
            terrain.SetEnvironmentValue("windY", x, y, (float)windY);
            terrain.SetEnvironmentValue("moisture", x, y, (float)moisture);
            terrain.SetEnvironmentValue("mountain", x, y, mountain ? 1f : 0f);
            terrain.SetEnvironmentValue("riverFlow", x, y, river ? (float)riverCell.Flow : 0f);
            terrain.SetEnvironmentValue("riverFlowX", x, y,
                river ? (float)riverCell.FlowDirectionX : 0f);
            terrain.SetEnvironmentValue("riverFlowY", x, y,
                river ? (float)riverCell.FlowDirectionY : 0f);
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
            double temperature;
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
            var sourceFlowByCell = new Dictionary<Int2, double>();
            var flowByCell = new Dictionary<Int2, double>();
            var terminalFlowByCell = new Dictionary<Int2, double>();
            var flowDirectionByCell = new Dictionary<Int2, FlowDirectionSample>();
            var dominantDownstreamByCell = new Dictionary<Int2, Int2>();
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

                    CollectRunoffSource(
                        sampling,
                        sourceOrigin,
                        sourceFlowByCell);
                }
            }

            RouteRunoffNetwork(
                sampling,
                sourceFlowByCell,
                flowByCell,
                terminalFlowByCell,
                flowDirectionByCell,
                dominantDownstreamByCell,
                cancellationToken);

            HashSet<Int2> visibleFlowCells = BuildVisibleFlowCells(
                sampling,
                flowByCell,
                settings.RiverStartFlow,
                settings.RiverTributaryStartFlow,
                settings.RiverMinimumVisibleCourseLength);
            var riverCells = new Dictionary<Int2, GeneratedHydrologyCell>();
            var floodplainCells = new Dictionary<Int2, double>();
            RenderSmoothedRiverChannels(
                request,
                settings,
                sampling,
                flowByCell,
                flowDirectionByCell,
                dominantDownstreamByCell,
                visibleFlowCells,
                maximumRadius,
                riverCells,
                floodplainCells,
                cancellationToken);

            AddHeightDrivenTerminalLakes(
                request,
                settings,
                sampling,
                terminalFlowByCell,
                riverCells,
                cancellationToken);

            return new GeneratedHydrologyMap(riverCells, floodplainCells);
        }

        /// <summary>
        /// 把离散水文图转换成连续河道中心线后再栅格化。
        /// 水量与汇流仍由 D∞ 区域水文决定；这里仅负责把“格子链”重建为平滑曲线，
        /// 从表现根源消除跨屏水平、垂直或 45° 的栅格锁向直线。
        /// </summary>
        private static void RenderSmoothedRiverChannels(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            HydrologySamplingContext sampling,
            IReadOnlyDictionary<Int2, double> flowByCell,
            IReadOnlyDictionary<Int2, FlowDirectionSample> flowDirectionByCell,
            IReadOnlyDictionary<Int2, Int2> dominantDownstreamByCell,
            IReadOnlyCollection<Int2> visibleFlowCells,
            int maximumRadius,
            Dictionary<Int2, GeneratedHydrologyCell> riverCells,
            Dictionary<Int2, double> floodplainCells,
            CancellationToken cancellationToken)
        {
            var visible = new HashSet<Int2>();
            foreach (Int2 cell in visibleFlowCells)
            {
                if (ContainsChunk(request, cell))
                    visible.Add(cell);
            }
            if (visible.Count == 0)
                return;

            var incoming = new Dictionary<Int2, int>();
            foreach (Int2 cell in visible)
            {
                if (!dominantDownstreamByCell.TryGetValue(cell, out Int2 next) ||
                    !visible.Contains(next))
                {
                    continue;
                }

                incoming.TryGetValue(next, out int count);
                incoming[next] = count + 1;
            }

            var starts = new List<Int2>();
            foreach (Int2 cell in visible)
            {
                if (!incoming.ContainsKey(cell))
                    starts.Add(cell);
            }
            starts.Sort(CompareInt2);

            var claimed = new HashSet<Int2>();
            var centerSamples = new Dictionary<Int2, ChannelCenterSample>();
            for (int i = 0; i < starts.Count; i++)
            {
                if ((i & 31) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                TraceAndRasterizeChannel(
                    starts[i],
                    settings,
                    sampling,
                    flowByCell,
                    flowDirectionByCell,
                    dominantDownstreamByCell,
                    visible,
                    claimed,
                    centerSamples);
            }

            // 理论上严格下坡图不会成环；这里仍把未被头节点覆盖的孤立段补上，
            // 避免环绕世界边界或阈值裁剪后漏掉合法河段。
            if (claimed.Count < visible.Count)
            {
                var remaining = new List<Int2>();
                foreach (Int2 cell in visible)
                {
                    if (!claimed.Contains(cell))
                        remaining.Add(cell);
                }
                remaining.Sort(CompareInt2);
                for (int i = 0; i < remaining.Count; i++)
                {
                    if (claimed.Contains(remaining[i]))
                        continue;
                    TraceAndRasterizeChannel(
                        remaining[i],
                        settings,
                        sampling,
                        flowByCell,
                        flowDirectionByCell,
                        dominantDownstreamByCell,
                        visible,
                        claimed,
                        centerSamples);
                }
            }

            int rendered = 0;
            foreach (KeyValuePair<Int2, ChannelCenterSample> pair in centerSamples)
            {
                if ((rendered++ & 63) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                Int2 center = pair.Key;
                ChannelCenterSample sample = pair.Value;
                double widthT = InverseLerp(
                    settings.RiverStartFlow,
                    Math.Max(settings.RiverStartFlow + 0.001d, settings.RiverFullWidthFlow),
                    sample.Flow);
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
                            center.X + offsetX,
                            center.Y + offsetY));
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
                            GeneratedHydrologyKind.River,
                            sample.Flow,
                            depth,
                            0d,
                            sample.DirectionX,
                            sample.DirectionY));
                    }
                }

                AddFloodplain(
                    request,
                    settings,
                    sampling,
                    center,
                    sample.Flow,
                    radius,
                    floodplainCells);
            }
        }

        /// <summary>沿主下游图提取一条折线，做横向连续偏移和两轮 Chaikin 圆角后栅格化。</summary>
        private static void TraceAndRasterizeChannel(
            Int2 start,
            ChunkGenerationSettingsSnapshot settings,
            HydrologySamplingContext sampling,
            IReadOnlyDictionary<Int2, double> flowByCell,
            IReadOnlyDictionary<Int2, FlowDirectionSample> flowDirectionByCell,
            IReadOnlyDictionary<Int2, Int2> dominantDownstreamByCell,
            HashSet<Int2> visible,
            HashSet<Int2> claimed,
            Dictionary<Int2, ChannelCenterSample> centerSamples)
        {
            var path = new List<ChannelPoint>();
            var localVisited = new HashSet<Int2>();
            Int2 current = start;
            while (visible.Contains(current) && localVisited.Add(current))
            {
                bool alreadyClaimed = !claimed.Add(current);
                flowByCell.TryGetValue(current, out double flow);
                ResolveFlowDirection(
                    flowDirectionByCell,
                    current,
                    out double directionX,
                    out double directionY);
                if (Math.Abs(directionX) <= 0.000001d &&
                    Math.Abs(directionY) <= 0.000001d &&
                    dominantDownstreamByCell.TryGetValue(current, out Int2 fallbackNext))
                {
                    int dx = fallbackNext.X - current.X;
                    int dy = fallbackNext.Y - current.Y;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    if (length > 0.000001d)
                    {
                        directionX = dx / length;
                        directionY = dy / length;
                    }
                }

                double widthT = InverseLerp(
                    settings.RiverStartFlow,
                    Math.Max(settings.RiverStartFlow + 0.001d, settings.RiverFullWidthFlow),
                    flow);
                // 窄河允许更明显的亚格偏移；宽河保持主槽稳定，避免整条大河左右抖动。
                double lateralAmplitude = Lerp(0.72d, 0.42d, Math.Sqrt(widthT));
                double lateral = sampling.ChannelCenterBias(current) * lateralAmplitude;
                double pointX = current.X + 0.5d - directionY * lateral;
                double pointY = current.Y + 0.5d + directionX * lateral;
                path.Add(new ChannelPoint(pointX, pointY, flow));

                if (alreadyClaimed ||
                    !dominantDownstreamByCell.TryGetValue(current, out Int2 next) ||
                    !visible.Contains(next))
                {
                    break;
                }
                current = next;
            }

            if (path.Count == 0)
                return;

            List<ChannelPoint> curved = ApplyChannelCurvatureRelaxation(
                path,
                sampling,
                settings);
            List<ChannelPoint> smoothed = SmoothChannelPath(curved);
            List<ChannelPoint> finalCurve = ApplyChannelCurvatureRelaxation(
                smoothed,
                sampling,
                settings);
            RasterizeChannelPath(finalCurve, sampling, centerSamples);
        }

        /// <summary>
        /// 对超长同方向中心线做地形感知的曲率松弛。
        /// D∞ 负责真实汇流，但离散到方格后仍可能长时间落在同一条 8 邻域边；
        /// 这里把这种“数学正确、视觉机械”的直段轻推向更低的一侧，再由 Chaikin 圆角成连续弧线。
        /// </summary>
        private static List<ChannelPoint> ApplyChannelCurvatureRelaxation(
            List<ChannelPoint> source,
            HydrologySamplingContext sampling,
            ChunkGenerationSettingsSnapshot settings)
        {
            if (source.Count < 3)
                return source;

            double minimumStraightLength = Math.Max(
                8d,
                settings.RiverLookAheadDistance * 2d);
            var output = new List<ChannelPoint>(source);
            int runStart = 0;
            Int2 runDirection = QuantizeChannelDirection(source[0], source[1]);
            double runLength = ChannelSegmentLength(source[0], source[1]);
            for (int segment = 1; segment <= source.Count - 1; segment++)
            {
                bool end = segment == source.Count - 1;
                Int2 direction = end
                    ? default
                    : QuantizeChannelDirection(source[segment], source[segment + 1]);
                if (!end && direction.Equals(runDirection))
                {
                    runLength += ChannelSegmentLength(
                        source[segment],
                        source[segment + 1]);
                    continue;
                }

                int runEndPoint = segment;
                if ((runDirection.X != 0 || runDirection.Y != 0) &&
                    runLength >= minimumStraightLength)
                {
                    RelaxStraightChannelRun(
                        output,
                        runStart,
                        runEndPoint,
                        runLength,
                        sampling,
                        minimumStraightLength);
                }

                runStart = segment;
                runDirection = direction;
                runLength = end
                    ? 0d
                    : ChannelSegmentLength(
                        source[segment],
                        source[segment + 1]);
            }
            return output;
        }

        /// <summary>把一段过直的中心线压成一条向低地侧弯出的平滑弧。</summary>
        private static void RelaxStraightChannelRun(
            List<ChannelPoint> points,
            int start,
            int end,
            double straightLength,
            HydrologySamplingContext sampling,
            double minimumStraightLength)
        {
            ChannelPoint first = points[start];
            ChannelPoint last = points[end];
            double tangentX = last.X - first.X;
            double tangentY = last.Y - first.Y;
            double length = Math.Sqrt(tangentX * tangentX + tangentY * tangentY);
            if (length <= 0.000001d)
                return;

            tangentX /= length;
            tangentY /= length;
            double normalX = -tangentY;
            double normalY = tangentX;
            double middleX = (first.X + last.X) * 0.5d;
            double middleY = (first.Y + last.Y) * 0.5d;
            var middleCell = sampling.Normalize(new Int2(
                (int)Math.Floor(middleX),
                (int)Math.Floor(middleY)));

            const double sideProbeDistance = 1.25d;
            Int2 leftProbe = sampling.Normalize(new Int2(
                (int)Math.Floor(middleX + normalX * sideProbeDistance),
                (int)Math.Floor(middleY + normalY * sideProbeDistance)));
            Int2 rightProbe = sampling.Normalize(new Int2(
                (int)Math.Floor(middleX - normalX * sideProbeDistance),
                (int)Math.Floor(middleY - normalY * sideProbeDistance)));
            double leftHeight = sampling.RoutingHeight(leftProbe);
            double rightHeight = sampling.RoutingHeight(rightProbe);
            double sideSign;
            if (Math.Abs(leftHeight - rightHeight) > 0.0005d)
                sideSign = leftHeight < rightHeight ? 1d : -1d;
            else
                sideSign = sampling.ChannelCenterBias(middleCell) >= 0d ? 1d : -1d;

            double excess = straightLength - minimumStraightLength;
            double amplitude = Math.Min(2d, 0.6d + excess * 0.09d);
            for (int i = start + 1; i < end; i++)
            {
                double t = (i - start) / (double)(end - start);
                double envelope = Math.Sin(Math.PI * t);
                ChannelPoint point = points[i];
                double offset = sideSign * amplitude * envelope;
                points[i] = new ChannelPoint(
                    point.X + normalX * offset,
                    point.Y + normalY * offset,
                    point.Flow);
            }
        }

        /// <summary>读取中心线相邻控制点的欧氏距离。</summary>
        private static double ChannelSegmentLength(ChannelPoint from, ChannelPoint to)
        {
            double x = to.X - from.X;
            double y = to.Y - from.Y;
            return Math.Sqrt(x * x + y * y);
        }

        /// <summary>把连续线段方向量化为 8 邻域，仅用于识别机械直段。</summary>
        private static Int2 QuantizeChannelDirection(ChannelPoint from, ChannelPoint to)
        {
            double deltaX = to.X - from.X;
            double deltaY = to.Y - from.Y;
            if (deltaX * deltaX + deltaY * deltaY <= 0.000001d)
                return default;

            double angle = Math.Atan2(deltaY, deltaX);
            int sector = PositiveMod(
                (int)Math.Round(angle / (Math.PI / 4d), MidpointRounding.AwayFromZero),
                8);
            return RiverFlowDirections[sector];
        }

        /// <summary>两轮 Chaikin 圆角，把格子折线变成连续河槽曲线，同时插值保留水量。</summary>
        private static List<ChannelPoint> SmoothChannelPath(List<ChannelPoint> source)
        {
            if (source.Count < 3)
                return source;

            List<ChannelPoint> current = source;
            for (int iteration = 0; iteration < 2; iteration++)
            {
                var next = new List<ChannelPoint>(current.Count * 2);
                next.Add(current[0]);
                for (int i = 0; i < current.Count - 1; i++)
                {
                    ChannelPoint a = current[i];
                    ChannelPoint b = current[i + 1];
                    next.Add(ChannelPoint.Lerp(a, b, 0.25d));
                    next.Add(ChannelPoint.Lerp(a, b, 0.75d));
                }
                next.Add(current[current.Count - 1]);
                current = next;
            }
            return current;
        }

        /// <summary>以亚格步长采样平滑曲线，并把连续切线写回最终河水格。</summary>
        private static void RasterizeChannelPath(
            IReadOnlyList<ChannelPoint> path,
            HydrologySamplingContext sampling,
            Dictionary<Int2, ChannelCenterSample> centerSamples)
        {
            if (path.Count == 1)
            {
                Int2 only = sampling.Normalize(new Int2(
                    (int)Math.Floor(path[0].X),
                    (int)Math.Floor(path[0].Y)));
                SetChannelCenterSample(centerSamples, only,
                    new ChannelCenterSample(path[0].Flow, 0d, 0d));
                return;
            }

            const double sampleSpacing = 0.28d;
            for (int i = 0; i < path.Count - 1; i++)
            {
                ChannelPoint a = path[i];
                ChannelPoint b = path[i + 1];
                double deltaX = b.X - a.X;
                double deltaY = b.Y - a.Y;
                double distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
                if (distance <= 0.000001d)
                    continue;

                double directionX = deltaX / distance;
                double directionY = deltaY / distance;
                int steps = Math.Max(1, (int)Math.Ceiling(distance / sampleSpacing));
                for (int step = 0; step <= steps; step++)
                {
                    double t = step / (double)steps;
                    double x = Lerp(a.X, b.X, t);
                    double y = Lerp(a.Y, b.Y, t);
                    Int2 cell = sampling.Normalize(new Int2(
                        (int)Math.Floor(x),
                        (int)Math.Floor(y)));
                    double flow = Lerp(a.Flow, b.Flow, t);
                    SetChannelCenterSample(centerSamples, cell,
                        new ChannelCenterSample(flow, directionX, directionY));
                }
            }
        }

        /// <summary>同一栅格被多条曲线经过时保留水量更大的主槽样本。</summary>
        private static void SetChannelCenterSample(
            Dictionary<Int2, ChannelCenterSample> samples,
            Int2 position,
            ChannelCenterSample candidate)
        {
            if (samples.TryGetValue(position, out ChannelCenterSample existing) &&
                existing.Flow >= candidate.Flow)
            {
                return;
            }
            samples[position] = candidate;
        }

        /// <summary>稳定排序水文格，保证中心线提取不依赖 Dictionary/HashSet 枚举顺序。</summary>
        private static int CompareInt2(Int2 left, Int2 right)
        {
            int y = left.Y.CompareTo(right.Y);
            return y != 0 ? y : left.X.CompareTo(right.X);
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
                    for (int i = 0; i < RiverNeighbors.Length; i++)
                    {
                        Int2 neighbor = sampling.Normalize(
                            current + RiverNeighbors[i]);
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
                    for (int i = 0; i < RiverNeighbors.Length; i++)
                    {
                        Int2 neighbor = sampling.Normalize(
                            current + RiverNeighbors[i]);
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

        /// <summary>汇总一个径流单元的降水，并把贡献挂到该单元最高的有效源点。</summary>
        private static void CollectRunoffSource(
            HydrologySamplingContext sampling,
            Int2 sourceOrigin,
            Dictionary<Int2, double> sourceFlowByCell)
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

            AddFlow(sourceFlowByCell, source, contribution);
        }

        /// <summary>
        /// 在整个水文区域内用多流向累计径流。
        /// 每个格子的水量按坡降分给多个真实下坡邻格，不再强制挑一个 D8/D∞ 接收格；
        /// 宽缓坡面的水会自然分散并在谷底重新汇聚，从算法根上消除规则网格造成的长直斜线。
        /// </summary>
        private static void RouteRunoffNetwork(
            HydrologySamplingContext sampling,
            IReadOnlyDictionary<Int2, double> sourceFlowByCell,
            Dictionary<Int2, double> flowByCell,
            Dictionary<Int2, double> terminalFlowByCell,
            Dictionary<Int2, FlowDirectionSample> flowDirectionByCell,
            Dictionary<Int2, Int2> dominantDownstreamByCell,
            CancellationToken cancellationToken)
        {
            if (sourceFlowByCell.Count == 0)
                return;

            const double flowEpsilon = 0.00001d;
            var pendingFlow = new Dictionary<Int2, double>(sourceFlowByCell);
            var minimumSteps = new Dictionary<Int2, int>();
            var queued = new HashSet<Int2>();
            var processed = new HashSet<Int2>();
            var queue = new FlowRoutingMaxHeap();

            foreach (KeyValuePair<Int2, double> source in sourceFlowByCell)
            {
                Int2 position = sampling.Normalize(source.Key);
                minimumSteps[position] = 0;
                if (queued.Add(position))
                    queue.Push(position, sampling.Height(position));
            }

            var receivers = new FlowReceiver[RiverNeighbors.Length];
            int processedCount = 0;
            while (queue.TryPop(out Int2 current, out _))
            {
                if ((processedCount++ & 63) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                current = sampling.Normalize(current);
                if (!processed.Add(current) ||
                    !pendingFlow.TryGetValue(current, out double flow) ||
                    flow <= flowEpsilon)
                {
                    continue;
                }

                double currentHeight = sampling.Height(current);
                if (currentHeight <= sampling.Settings.SeaLevel)
                    continue;

                flowByCell[current] = flow;
                int step = minimumSteps.TryGetValue(current, out int routedSteps)
                    ? routedSteps
                    : 0;
                if (step >= sampling.Settings.RiverMaxTraceSteps)
                {
                    AddFlow(terminalFlowByCell, current, flow);
                    continue;
                }

                int receiverCount = ResolveFlowReceivers(
                    sampling,
                    current,
                    currentHeight,
                    receivers,
                    out double weightSum,
                    out double directionX,
                    out double directionY);
                if (receiverCount == 0 || weightSum <= flowEpsilon)
                {
                    AddFlow(terminalFlowByCell, current, flow);
                    continue;
                }

                flowDirectionByCell[current] = new FlowDirectionSample(directionX, directionY);
                int dominantReceiverIndex = ResolveChannelReceiverIndex(
                    sampling,
                    current,
                    receivers,
                    receiverCount,
                    weightSum);
                dominantDownstreamByCell[current] = receivers[dominantReceiverIndex].Position;
                int nextStep = step + 1;
                for (int i = 0; i < receiverCount; i++)
                {
                    FlowReceiver receiver = receivers[i];
                    double routedFlow = flow * (receiver.Weight / weightSum);
                    if (routedFlow <= flowEpsilon)
                        continue;

                    AddFlow(pendingFlow, receiver.Position, routedFlow);
                    if (!minimumSteps.TryGetValue(receiver.Position, out int existingStep) ||
                        nextStep < existingStep)
                    {
                        minimumSteps[receiver.Position] = nextStep;
                    }

                    if (processed.Contains(receiver.Position) || !queued.Add(receiver.Position))
                        continue;
                    queue.Push(receiver.Position, receiver.Height);
                }
            }
        }

        /// <summary>
        /// 把 D∞ 的两个接收方向确定性栅格化成一条主槽路径。
        /// 不能永远选择权重更大的那一格，否则 55/45 之类的连续坡向仍会永久锁死在同一条 45° 边；
        /// 这里按真实分流比例做无周期坐标抖动，再交给中心线平滑层消除细碎像素感。
        /// </summary>
        private static int ResolveChannelReceiverIndex(
            HydrologySamplingContext sampling,
            Int2 current,
            FlowReceiver[] receivers,
            int receiverCount,
            double weightSum)
        {
            if (receiverCount <= 1 || weightSum <= 0.000001d)
                return 0;

            double firstShare = Clamp01(receivers[0].Weight / weightSum);
            double dither = Hash01(
                sampling.Request.WorldSeed,
                current.X,
                current.Y,
                0x94d049bbu);
            return dither < firstShare ? 0 : 1;
        }

        /// <summary>
        /// 按 D∞ 连续坡向把水量分给夹住该方向的两条相邻栅格边。
        /// 不再让一次径流同时扩散到整个八邻域，因此既减少 D8 的方向锁定，也避免 MFD 的扇形扩散。
        /// </summary>
        private static int ResolveFlowReceivers(
            HydrologySamplingContext sampling,
            Int2 current,
            double currentHeight,
            FlowReceiver[] receivers,
            out double weightSum,
            out double directionX,
            out double directionY)
        {
            weightSum = 0d;
            directionX = 0d;
            directionY = 0d;
            if (!TryResolvePreferredFlowDirection(
                    sampling,
                    current,
                    out double preferredX,
                    out double preferredY))
            {
                return ResolveFallbackDownhillReceivers(
                    sampling,
                    current,
                    currentHeight,
                    receivers,
                    out weightSum,
                    out directionX,
                    out directionY);
            }

            double angle = Math.Atan2(preferredY, preferredX);
            if (angle < 0d)
                angle += Math.PI * 2d;
            double sectorPosition = angle / (Math.PI / 4d);
            int lowerIndex = PositiveMod((int)Math.Floor(sectorPosition), 8);
            int upperIndex = (lowerIndex + 1) & 7;
            double upperShare = sectorPosition - Math.Floor(sectorPosition);
            double lowerShare = 1d - upperShare;

            int count = 0;
            if (lowerShare > 0.000001d && TryCreateFlowReceiver(
                    sampling,
                    current,
                    currentHeight,
                    RiverFlowDirections[lowerIndex],
                    lowerShare,
                    out FlowReceiver lower))
            {
                receivers[count++] = lower;
            }
            if (upperShare > 0.000001d && TryCreateFlowReceiver(
                    sampling,
                    current,
                    currentHeight,
                    RiverFlowDirections[upperIndex],
                    upperShare,
                    out FlowReceiver upper))
            {
                receivers[count++] = upper;
            }

            if (count == 0)
            {
                return ResolveFallbackDownhillReceivers(
                    sampling,
                    current,
                    currentHeight,
                    receivers,
                    out weightSum,
                    out directionX,
                    out directionY);
            }

            for (int i = 0; i < count; i++)
            {
                FlowReceiver receiver = receivers[i];
                weightSum += receiver.Weight;
                directionX += receiver.UnitX * receiver.Weight;
                directionY += receiver.UnitY * receiver.Weight;
            }

            double length = Math.Sqrt(directionX * directionX + directionY * directionY);
            if (length > 0.000001d)
            {
                directionX /= length;
                directionY /= length;
            }
            else
            {
                directionX = 0d;
                directionY = 0d;
            }

            return count;
        }

        /// <summary>把一个 D∞ 方向对应到真实下坡邻格，并结合谷底/前视坡降计算分流权重。</summary>
        private static bool TryCreateFlowReceiver(
            HydrologySamplingContext sampling,
            Int2 current,
            double currentHeight,
            Int2 offset,
            double angularShare,
            out FlowReceiver receiver)
        {
            receiver = default;
            Int2 candidate = sampling.Normalize(current + offset);
            if (candidate == current)
                return false;

            double height = sampling.Height(candidate);
            double rawDrop = currentHeight - height;
            if (rawDrop <= DownhillEpsilon)
                return false;

            double distance = offset.X != 0 && offset.Y != 0
                ? Math.Sqrt(2d)
                : 1d;
            double localSlope = rawDrop / distance;
            double routingHeight = sampling.RoutingHeight(current);
            double valleyDrop = Math.Max(
                0d,
                routingHeight - sampling.RoutingHeight(candidate));
            double valleySlope = valleyDrop / distance;
            Int2 lookAhead = sampling.Normalize(new Int2(
                candidate.X + offset.X * sampling.Settings.RiverLookAheadDistance,
                candidate.Y + offset.Y * sampling.Settings.RiverLookAheadDistance));
            double lookAheadDistance = distance *
                                       (sampling.Settings.RiverLookAheadDistance + 1d);
            double lookAheadSlope = Math.Max(
                0d,
                routingHeight - sampling.RoutingHeight(lookAhead)) /
                Math.Max(1d, lookAheadDistance);
            double guidedSlope = Lerp(
                valleySlope,
                lookAheadSlope,
                sampling.Settings.RiverLookAheadWeight);
            double effectiveSlope = Math.Max(
                localSlope * 0.45d + guidedSlope * 0.55d,
                localSlope * 0.1d);
            double weight = angularShare * Math.Sqrt(
                Math.Max(effectiveSlope, DownhillEpsilon));
            if (weight <= 0.000001d)
                return false;

            receiver = new FlowReceiver(
                candidate,
                height,
                weight,
                offset.X / distance,
                offset.Y / distance);
            return true;
        }

        /// <summary>
        /// D∞ 两个夹角邻格都不可走时，按真实坡降收集候选，再收敛为最陡方向和一个相邻次方向。
        /// </summary>
        private static int ResolveFallbackDownhillReceivers(
            HydrologySamplingContext sampling,
            Int2 current,
            double currentHeight,
            FlowReceiver[] receivers,
            out double weightSum,
            out double directionX,
            out double directionY)
        {
            int count = 0;
            for (int i = 0; i < RiverFlowDirections.Length; i++)
            {
                Int2 offset = RiverFlowDirections[i];
                Int2 candidate = sampling.Normalize(current + offset);
                if (candidate == current)
                    continue;
                double height = sampling.Height(candidate);
                double drop = currentHeight - height;
                if (drop <= DownhillEpsilon)
                    continue;
                double distance = offset.X != 0 && offset.Y != 0
                    ? Math.Sqrt(2d)
                    : 1d;
                receivers[count++] = new FlowReceiver(
                    candidate,
                    height,
                    drop / distance,
                    offset.X / distance,
                    offset.Y / distance);
            }

            return KeepDominantFlowReceivers(
                receivers,
                count,
                out weightSum,
                out directionX,
                out directionY);
        }

        /// <summary>
        /// 把兜底候选收敛为最多两个相邻主方向，防止局部凹凸重新把水撒向整个八邻域。
        /// </summary>
        private static int KeepDominantFlowReceivers(
            FlowReceiver[] receivers,
            int count,
            out double weightSum,
            out double directionX,
            out double directionY)
        {
            weightSum = 0d;
            directionX = 0d;
            directionY = 0d;
            if (count <= 0)
                return 0;

            int firstIndex = 0;
            for (int i = 1; i < count; i++)
            {
                if (receivers[i].Weight > receivers[firstIndex].Weight)
                    firstIndex = i;
            }

            FlowReceiver first = receivers[firstIndex];
            int secondIndex = -1;
            double secondScore = double.MinValue;
            for (int i = 0; i < count; i++)
            {
                if (i == firstIndex)
                    continue;

                FlowReceiver candidate = receivers[i];
                double directionDot = first.UnitX * candidate.UnitX +
                                      first.UnitY * candidate.UnitY;
                // D∞ 只允许把一次连续坡向分配给夹住该方向的相邻两条 D8 边。
                // 两个离散方向若相隔超过 45°，就会把河水横向撕成扇形，重新制造栅格伪影。
                if (directionDot < 0.7071067811865476d - 0.0001d)
                    continue;

                // 邻近主方向优先；同等坡降下避免把水拆到相隔过远的两个方向。
                double score = candidate.Weight * (0.75d + Math.Max(0d, directionDot) * 0.25d);
                if (score <= secondScore)
                    continue;
                secondScore = score;
                secondIndex = i;
            }

            receivers[0] = first;
            int outputCount = 1;
            if (secondIndex >= 0)
            {
                FlowReceiver second = receivers[secondIndex];
                // 极弱的第二方向只会制造一格宽的毛刺，直接并回主流。
                if (second.Weight >= first.Weight * 0.12d)
                {
                    receivers[1] = second;
                    outputCount = 2;
                }
            }

            for (int i = 0; i < outputCount; i++)
            {
                FlowReceiver receiver = receivers[i];
                weightSum += receiver.Weight;
                directionX += receiver.UnitX * receiver.Weight;
                directionY += receiver.UnitY * receiver.Weight;
            }

            return outputCount;
        }

        /// <summary>
        /// 从 3×3 邻域的八个三角坡面求 D∞ 最陡连续下坡方向，再叠加低坡增强的平滑横向曲率。
        /// 曲率只旋转连续方向，真正接收格仍必须满足真实高度严格下降。
        /// </summary>
        private static bool TryResolvePreferredFlowDirection(
            HydrologySamplingContext sampling,
            Int2 current,
            out double directionX,
            out double directionY)
        {
            if (!TryResolveDInfinityDirection(
                    sampling,
                    current,
                    out double baseX,
                    out double baseY,
                    out double baseSlope))
            {
                directionX = 0d;
                directionY = 0d;
                return false;
            }

            int steeringRadius = Math.Max(1, sampling.Settings.RiverLookAheadDistance / 3);
            double curlX = sampling.MeanderBias(new Int2(
                               current.X,
                               current.Y + steeringRadius)) -
                           sampling.MeanderBias(new Int2(
                               current.X,
                               current.Y - steeringRadius));
            double curlY = -(sampling.MeanderBias(new Int2(
                                current.X + steeringRadius,
                                current.Y)) -
                            sampling.MeanderBias(new Int2(
                                current.X - steeringRadius,
                                current.Y)));
            double curlLength = Math.Sqrt(curlX * curlX + curlY * curlY);
            if (curlLength > 0.000001d)
            {
                curlX /= curlLength;
                curlY /= curlLength;
            }
            else
            {
                curlX = 0d;
                curlY = 0d;
            }

            double slopeReference = Math.Max(
                0.005d,
                sampling.Settings.RiverFloodplainMaxSlope);
            double flatness = 1d - Clamp01(baseSlope / slopeReference);
            double perpendicularX = -baseY;
            double perpendicularY = baseX;
            double curlSide = curlX * perpendicularX + curlY * perpendicularY;
            double smoothBias = sampling.MeanderBias(current);
            double lateral = Math.Max(-1d, Math.Min(1d,
                curlSide * 0.72d + smoothBias * 0.28d));
            double maximumTurn = sampling.Settings.RiverMeanderStrength *
                                 (0.20d + flatness * 0.35d);
            double turn = lateral * maximumTurn;
            double angle = Math.Atan2(baseY, baseX) + turn;

            // 栅格方向排斥：连续坡向落在 0/45/90° 等固定方向附近时，
            // 用同一条平滑蜿蜒场把它轻推离精确栅格角，避免几十格连续锁成一条直线。
            // 真正落格仍在 ResolveFlowReceivers 中接受“严格下坡”检查，因此不会逆坡造河。
            const double gridDirectionStep = Math.PI / 4d;
            double nearestGridAngle = Math.Round(angle / gridDirectionStep) * gridDirectionStep;
            double gridDelta = Math.Atan2(
                Math.Sin(angle - nearestGridAngle),
                Math.Cos(angle - nearestGridAngle));
            double lockStrength = 1d - Clamp01(
                Math.Abs(gridDelta) / (Math.PI / 16d));
            if (lockStrength > 0d)
            {
                double side = Math.Abs(lateral) > 0.05d
                    ? Math.Sign(lateral)
                    : (smoothBias >= 0d ? 1d : -1d);
                angle += side * lockStrength * (Math.PI / 18d) *
                         (0.55d + flatness * 0.45d);
            }

            directionX = Math.Cos(angle);
            directionY = Math.Sin(angle);
            return true;
        }

        /// <summary>按 D∞ 思路，在八个三角坡面中寻找最陡的连续下降向量。</summary>
        private static bool TryResolveDInfinityDirection(
            HydrologySamplingContext sampling,
            Int2 current,
            out double directionX,
            out double directionY,
            out double slope)
        {
            directionX = 0d;
            directionY = 0d;
            slope = 0d;
            double centerHeight = sampling.RoutingHeight(current);
            for (int i = 0; i < RiverFlowDirections.Length; i++)
            {
                Int2 first = RiverFlowDirections[i];
                Int2 second = RiverFlowDirections[(i + 1) & 7];
                if (!TryResolveFacetDownhill(
                        sampling,
                        current,
                        centerHeight,
                        first,
                        second,
                        out double candidateX,
                        out double candidateY,
                        out double candidateSlope) ||
                    candidateSlope <= slope)
                {
                    continue;
                }

                directionX = candidateX;
                directionY = candidateY;
                slope = candidateSlope;
            }

            return slope > DownhillEpsilon;
        }

        /// <summary>求中心格与两条相邻 D8 边构成的三角坡面上的最陡下降方向。</summary>
        private static bool TryResolveFacetDownhill(
            HydrologySamplingContext sampling,
            Int2 current,
            double centerHeight,
            Int2 first,
            Int2 second,
            out double directionX,
            out double directionY,
            out double slope)
        {
            directionX = 0d;
            directionY = 0d;
            slope = 0d;
            double firstDelta = sampling.RoutingHeight(current + first) - centerHeight;
            double secondDelta = sampling.RoutingHeight(current + second) - centerHeight;
            double determinant = first.X * second.Y - first.Y * second.X;
            if (Math.Abs(determinant) <= 0.000001d)
                return false;

            double gradientX = (firstDelta * second.Y - first.Y * secondDelta) / determinant;
            double gradientY = (first.X * secondDelta - firstDelta * second.X) / determinant;
            double downhillX = -gradientX;
            double downhillY = -gradientY;
            double downhillLength = Math.Sqrt(downhillX * downhillX + downhillY * downhillY);
            if (downhillLength > DownhillEpsilon)
            {
                double firstCoefficient =
                    (downhillX * second.Y - downhillY * second.X) / determinant;
                double secondCoefficient =
                    (first.X * downhillY - first.Y * downhillX) / determinant;
                if (firstCoefficient >= -0.000001d && secondCoefficient >= -0.000001d)
                {
                    directionX = downhillX / downhillLength;
                    directionY = downhillY / downhillLength;
                    slope = downhillLength;
                    return true;
                }
            }

            double firstLength = first.X != 0 && first.Y != 0 ? Math.Sqrt(2d) : 1d;
            double secondLength = second.X != 0 && second.Y != 0 ? Math.Sqrt(2d) : 1d;
            double firstSlope = Math.Max(0d, -firstDelta / firstLength);
            double secondSlope = Math.Max(0d, -secondDelta / secondLength);
            if (firstSlope <= DownhillEpsilon && secondSlope <= DownhillEpsilon)
                return false;

            Int2 edge = firstSlope >= secondSlope ? first : second;
            double edgeLength = edge.X != 0 && edge.Y != 0 ? Math.Sqrt(2d) : 1d;
            directionX = edge.X / edgeLength;
            directionY = edge.Y / edgeLength;
            slope = Math.Max(firstSlope, secondSlope);
            return true;
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

        /// <summary>读取 MFD 累计阶段已经求出的真实下游单位方向。</summary>
        private static void ResolveFlowDirection(
            IReadOnlyDictionary<Int2, FlowDirectionSample> flowDirections,
            Int2 current,
            out double directionX,
            out double directionY)
        {
            directionX = 0d;
            directionY = 0d;
            if (!flowDirections.TryGetValue(current, out FlowDirectionSample sample))
                return;
            directionX = sample.X;
            directionY = sample.Y;
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
                flowDirectionY);
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

        /// <summary>连续河道中心线上的一个控制点。</summary>
        private readonly struct ChannelPoint
        {
            public ChannelPoint(double x, double y, double flow)
            {
                X = x;
                Y = y;
                Flow = flow;
            }

            public double X { get; }
            public double Y { get; }
            public double Flow { get; }

            public static ChannelPoint Lerp(ChannelPoint a, ChannelPoint b, double t) =>
                new(
                    DeterministicChunkGenerator.Lerp(a.X, b.X, t),
                    DeterministicChunkGenerator.Lerp(a.Y, b.Y, t),
                    DeterministicChunkGenerator.Lerp(a.Flow, b.Flow, t));
        }

        /// <summary>平滑中心线重新栅格化后的河槽中心样本。</summary>
        private readonly struct ChannelCenterSample
        {
            public ChannelCenterSample(
                double flow,
                double directionX,
                double directionY)
            {
                Flow = flow;
                DirectionX = directionX;
                DirectionY = directionY;
            }

            public double Flow { get; }
            public double DirectionX { get; }
            public double DirectionY { get; }
        }

        /// <summary>一个下坡接收格及其 MFD 分配权重。</summary>
        private readonly struct FlowReceiver
        {
            public FlowReceiver(
                Int2 position,
                double height,
                double weight,
                double unitX,
                double unitY)
            {
                Position = position;
                Height = height;
                Weight = weight;
                UnitX = unitX;
                UnitY = unitY;
            }

            public Int2 Position { get; }
            public double Height { get; }
            public double Weight { get; }
            public double UnitX { get; }
            public double UnitY { get; }
        }

        /// <summary>MFD 输出给水流玩法的连续单位方向。</summary>
        private readonly struct FlowDirectionSample
        {
            public FlowDirectionSample(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double X { get; }
            public double Y { get; }
        }

        /// <summary>按高度从高到低处理流量，保证格子出队时所有更高上游都已经汇入。</summary>
        private sealed class FlowRoutingMaxHeap
        {
            private readonly List<FlowRoutingQueueEntry> entries = new();

            public void Push(Int2 position, double height)
            {
                FlowRoutingQueueEntry entry = new(position, height);
                entries.Add(entry);
                int index = entries.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (entries[parent].Height >= height)
                        break;
                    entries[index] = entries[parent];
                    index = parent;
                }
                entries[index] = entry;
            }

            public bool TryPop(out Int2 position, out double height)
            {
                if (entries.Count == 0)
                {
                    position = default;
                    height = 0d;
                    return false;
                }

                FlowRoutingQueueEntry root = entries[0];
                int lastIndex = entries.Count - 1;
                FlowRoutingQueueEntry last = entries[lastIndex];
                entries.RemoveAt(lastIndex);
                if (entries.Count > 0)
                {
                    int index = 0;
                    while (true)
                    {
                        int left = index * 2 + 1;
                        if (left >= entries.Count)
                            break;
                        int right = left + 1;
                        int child = right < entries.Count &&
                                    entries[right].Height > entries[left].Height
                            ? right
                            : left;
                        if (entries[child].Height <= last.Height)
                            break;
                        entries[index] = entries[child];
                        index = child;
                    }
                    entries[index] = last;
                }

                position = root.Position;
                height = root.Height;
                return true;
            }
        }

        private readonly struct FlowRoutingQueueEntry
        {
            public FlowRoutingQueueEntry(Int2 position, double height)
            {
                Position = position;
                Height = height;
            }

            public Int2 Position { get; }
            public double Height { get; }
        }

        /// <summary>复用单次区块水文计算中的高度和降水采样，避免重复计算同一格噪声。</summary>
        private sealed class HydrologySamplingContext
        {
            private readonly Dictionary<Int2, double> heightCache = new();
            private readonly Dictionary<Int2, double> precipitationCache = new();
            private readonly Dictionary<Int2, double> routingHeightCache = new();
            private readonly Dictionary<Int2, double> meanderBiasCache = new();
            private readonly Dictionary<Int2, double> channelCenterBiasCache = new();

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
            double liquidDepth = Math.Max(groundliquidDepth, caveRiver.Depth);
            bool water = liquidDepth > 0d;
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
                (float)liquidDepth);
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
