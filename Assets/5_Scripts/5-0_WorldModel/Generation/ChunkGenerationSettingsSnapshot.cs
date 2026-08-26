using System;
using System.Collections.Generic;

namespace FlatWorld.WorldModel
{
    /// <summary>要生成普通地表，还是地下洞穴。</summary>
    public enum ChunkGenerationMode
    {
        Surface,
        Cave
    }

    /// <summary>地表河流使用新版高度汇流，还是迁移后的旧版区域水文。</summary>
    public enum RiverGenerationAlgorithm
    {
        HeightDriven,
        Legacy
    }

    /// <summary>地表气候继续使用新版简化噪声，还是复用旧版 Land 采样规则。</summary>
    public enum SurfaceClimateAlgorithm
    {
        Simple,
        LegacyLand
    }

    /// <summary>
    /// 地表群系的稳定编号。0—5 与旧版 MapCore 的有序 BiomeData 保持一致，
    /// 河流、雪地和二维石地山体使用新版扩展编号。
    /// </summary>
    public enum SurfaceBiomeKind
    {
        Ocean = 0,
        River = 1,
        Beach = 2,
        Desert = 3,
        Grassland = 4,
        Forest = 5,
        Snow = 6,
        Stone = 7
    }

    /// <summary>
    /// 纯数据地表群系判定器。LegacyLand 模式适配旧版“石地→沙漠→沙滩→草原→森林→海洋”
    /// 的优先级；河流仍作为新版水文覆盖层优先于陆地群系。
    /// </summary>
    public static class SurfaceBiomeClassifier
    {
        #region 判定

        /// <summary>根据同一份生成快照判定一个地表格的稳定群系编号。</summary>
        public static SurfaceBiomeKind Resolve(
            ChunkGenerationSettingsSnapshot settings,
            double height,
            double temperature,
            double precipitation,
            double moisture,
            bool river)
        {
            if (height < settings.SeaLevel)
                return SurfaceBiomeKind.Ocean;
            if (river)
                return SurfaceBiomeKind.River;
            if (temperature <= settings.SnowTemperature &&
                precipitation >= settings.SnowMinimumPrecipitation)
            {
                return SurfaceBiomeKind.Snow;
            }
            if (height >= settings.MountainLevel)
                return SurfaceBiomeKind.Stone;

            if (settings.SurfaceClimateAlgorithm == SurfaceClimateAlgorithm.LegacyLand)
            {
                if (height >= settings.DesertMinimumHeight &&
                    precipitation <= settings.DesertMaximumPrecipitation)
                {
                    return SurfaceBiomeKind.Desert;
                }

                if (height <= settings.BeachLevel)
                    return SurfaceBiomeKind.Beach;

                bool grassland =
                    temperature >= settings.GrasslandMinimumTemperature &&
                    temperature <= settings.GrasslandMaximumTemperature &&
                    precipitation >= settings.GrasslandMinimumPrecipitation &&
                    precipitation <= settings.GrasslandMaximumPrecipitation;
                return grassland ? SurfaceBiomeKind.Grassland : SurfaceBiomeKind.Forest;
            }

            if (height <= settings.BeachLevel)
                return SurfaceBiomeKind.Beach;
            if (precipitation < settings.DesertMaximumPrecipitation)
                return SurfaceBiomeKind.Desert;
            return moisture > 0.62d ? SurfaceBiomeKind.Forest : SurfaceBiomeKind.Grassland;
        }

        #endregion

        #region 兼容名称

        /// <summary>把稳定群系编号转换成旧玩法配置使用的中文名称。</summary>
        public static string GetLegacyName(int biomeId)
        {
            return (SurfaceBiomeKind)biomeId switch
            {
                SurfaceBiomeKind.Ocean => "海洋",
                SurfaceBiomeKind.River => "河流",
                SurfaceBiomeKind.Beach => "沙滩",
                SurfaceBiomeKind.Desert => "沙漠",
                SurfaceBiomeKind.Grassland => "温带草原",
                SurfaceBiomeKind.Forest => "森林",
                SurfaceBiomeKind.Snow => "雪地",
                SurfaceBiomeKind.Stone => "石地",
                _ => string.Empty
            };
        }

        #endregion
    }

    /// <summary>一条旧版地形噪声通道的纯数据快照。</summary>
    public readonly struct TerrainNoiseChannelSettings
    {
        /// <summary>保存坐标、频率、八度与偏移；所有值已由配置快照校验。</summary>
        public TerrainNoiseChannelSettings(double coordinateScale, double frequency, int octaves,
            double lacunarity, double persistence, double offsetX, double offsetY)
        {
            CoordinateScale = coordinateScale;
            Frequency = frequency;
            Octaves = octaves;
            Lacunarity = lacunarity;
            Persistence = persistence;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public double CoordinateScale { get; }
        public double Frequency { get; }
        public int Octaves { get; }
        public double Lacunarity { get; }
        public double Persistence { get; }
        public double OffsetX { get; }
        public double OffsetY { get; }
    }

    /// <summary>
    /// 把 Unity 面板里的零散设置整理成一份后台生成器能直接使用的配置。
    /// 这里会补上缺省值并限制不合理的数字。整理完以后，后台线程就不用再碰 Unity 对象。
    /// 原始设置字典仍然保留，方便 MOD 读取自己添加的参数。
    /// </summary>
    public sealed class ChunkGenerationSettingsSnapshot
    {
        private const double DefaultWorldCoordinateScale = 0.01d;
        private const double MinimumWorldDistanceScale = 0.25d;
        private const double MaximumWorldDistanceScale = 4d;

        /// <summary>把配置表里的原始参数整理成生成器可以直接使用的安全数值。</summary>
        internal ChunkGenerationSettingsSnapshot(IReadOnlyDictionary<string, double> numbers,
            IReadOnlyDictionary<string, string> texts)
        {
            // 所有默认值和数字范围都在这里一次处理好，后面生成每个格子时就不用反复检查。
            Mode = GetText(texts, "terrain.mode", "surface").Equals("cave",
                StringComparison.OrdinalIgnoreCase) ? ChunkGenerationMode.Cave : ChunkGenerationMode.Surface;
            GroundTileId = GetInt(numbers, "terrain.groundTileId", 1);
            FreshWaterTileId = GetInt(numbers, "terrain.waterTileId", 2);
            SaltWaterTileId = GetInt(numbers, "terrain.saltWaterTileId", FreshWaterTileId);
            SandTileId = GetInt(numbers, "terrain.sandTileId", GroundTileId);
            StoneTileId = GetInt(numbers, "terrain.stoneTileId", GroundTileId);
            SnowTileId = GetInt(numbers, "terrain.snowTileId", GroundTileId);
            IceTileId = GetInt(numbers, "terrain.iceTileId", SnowTileId);
            SnowVariant2TileId = GetInt(numbers, "terrain.snowVariant2TileId", SnowTileId);
            SnowVariant3TileId = GetInt(numbers, "terrain.snowVariant3TileId", SnowTileId);
            CaveFloorTileId = GetInt(numbers, "cave.floorTileId", StoneTileId);
            CaveWallTileId = GetInt(numbers, "cave.wallTileId", StoneTileId);
            SeaLevel = Clamp01(GetDouble(numbers, "terrain.seaLevel", 0.30d));
            BeachLevel = Clamp01(GetDouble(numbers, "terrain.beachLevel", SeaLevel + 0.055d));
            MountainLevel = Clamp(
                GetDouble(numbers, "terrain.mountainLevel", 0.72d),
                BeachLevel,
                1d);
            SnowTemperature = Clamp01(GetDouble(numbers, "terrain.snowTemperature", 0.18d));
            SnowMinimumPrecipitation = Clamp01(
                GetDouble(numbers, "terrain.snowMinimumPrecipitation", 0.55d));
            SnowIceLakeChance = Clamp01(
                GetDouble(numbers, "biome.snow.iceLakeChance", 0.08d));
            SnowGrassDensityMultiplier = Clamp01(
                GetDouble(numbers, "biome.snow.grassDensityMultiplier", 0.08d));
            DesertMinimumHeight = Clamp01(
                GetDouble(numbers, "biome.desert.minimumHeight", 0.51d));
            DesertMaximumPrecipitation = Clamp01(
                GetDouble(numbers, "biome.desert.maximumPrecipitation", 0.28d));
            GrasslandMinimumTemperature = Clamp01(
                GetDouble(numbers, "biome.grassland.minimumTemperature", 0.25d));
            GrasslandMaximumTemperature = Math.Max(
                GrasslandMinimumTemperature,
                Clamp01(GetDouble(numbers, "biome.grassland.maximumTemperature", 0.75d)));
            GrasslandMinimumPrecipitation = Clamp01(
                GetDouble(numbers, "biome.grassland.minimumPrecipitation", 0.25d));
            GrasslandMaximumPrecipitation = Math.Max(
                GrasslandMinimumPrecipitation,
                Clamp01(GetDouble(numbers, "biome.grassland.maximumPrecipitation", 0.75d)));
            WorldCoordinateScale = NonNegativeFinite(
                GetDouble(numbers, "world.coordinateScale", DefaultWorldCoordinateScale),
                DefaultWorldCoordinateScale);
            double worldFrequencyScale = WorldCoordinateScale / DefaultWorldCoordinateScale;
            WorldCoordinateDistanceScale = WorldCoordinateScale <= 0d
                ? MaximumWorldDistanceScale
                : Clamp(
                    DefaultWorldCoordinateScale / WorldCoordinateScale,
                    MinimumWorldDistanceScale,
                    MaximumWorldDistanceScale);
            TerrainScale = Positive(
                               GetDouble(numbers, "terrain.noiseScale", 0.0085d),
                               0.0085d) * worldFrequencyScale;
            ClimateScale = Positive(
                               GetDouble(numbers, "climate.noiseScale", 0.004d),
                               0.004d) * worldFrequencyScale;
            HeightOctaves = Clamp(GetInt(numbers, "terrain.octaves", 4), 1, 8);
            ClimateOctaves = Clamp(GetInt(numbers, "climate.octaves", 3), 1, 8);
            SurfaceClimateAlgorithm = ParseSurfaceClimateAlgorithm(
                GetText(texts, "climate.algorithm", "simple"));
            HeightNoise = CreateNoiseChannel(numbers, "terrain.height", 2d, 0.05d, 5,
                2d, 0.45d, 9000d, 0d);
            PrecipitationNoise = CreateNoiseChannel(numbers, "climate.precipitation", 10d,
                0.02d, 4, 2d, 0.55d, 0d, 0d);
            TemperatureNoise = CreateNoiseChannel(numbers, "climate.temperature", 10d,
                0.015d, 4, 2d, 0.55d, 0d, 0d);
            TemperatureCelsiusMin = Finite(
                GetDouble(numbers, "climate.temperature.celsiusMin", 0d), 0d);
            TemperatureCelsiusMax = Math.Max(
                TemperatureCelsiusMin,
                Finite(GetDouble(numbers, "climate.temperature.celsiusMax", 50d), 50d));
            TemperatureAltitudeCoolingStart = Clamp01(GetDouble(
                numbers, "climate.temperature.altitudeCoolingStart", SeaLevel));
            TemperatureAltitudeCoolingStrength = Clamp(
                GetDouble(numbers, "climate.temperature.altitudeCoolingStrength", 0.8d),
                0d,
                2d);
            HeightSecondaryBoostEnabled = GetBool(
                numbers, "terrain.height.secondaryBoostEnabled", true);
            HeightSecondaryBoostStrength = NonNegativeFinite(
                GetDouble(numbers, "terrain.height.secondaryBoostStrength", 1d), 1d);
            WindRegionSize = Math.Max(8d, FinitePositive(
                GetDouble(numbers, "climate.wind.regionSize", 256d), 256d));
            WindSeedSalt = GetInt(numbers, "climate.wind.seedSalt", 1779033703);
            OrographicSampleDistance = Math.Max(8d, FinitePositive(
                GetDouble(numbers, "climate.orographic.sampleDistance", 64d), 64d));
            OrographicSampleCount = Clamp(
                GetInt(numbers, "climate.orographic.sampleCount", 4), 1, 8);
            WindwardRainGain = NonNegativeFinite(
                GetDouble(numbers, "climate.orographic.windwardGain", 0.8d), 0.8d);
            LeewardRainLoss = NonNegativeFinite(
                GetDouble(numbers, "climate.orographic.leewardLoss", 0.6d), 0.6d);
            RiverEnabled = GetBool(numbers, "river.enabled", true);
            RiverAlgorithm = ParseRiverAlgorithm(
                GetText(texts, "river.algorithm", "heightDriven"));
            RiverHydrologyRegionSize = Clamp(
                GetInt(numbers, "river.hydrologyRegionSize", 256), 64, 1024);
            RiverRunoffCellSize = ScaleDistance(
                GetInt(numbers, "river.runoffCellSize", 64),
                WorldCoordinateDistanceScale,
                16,
                256);
            int runoffSampleStride = ScaleDistance(
                GetInt(numbers, "river.runoffSampleStride", 8),
                WorldCoordinateDistanceScale,
                1,
                RiverRunoffCellSize);
            while (RiverRunoffCellSize % runoffSampleStride != 0)
                runoffSampleStride--;
            RiverRunoffSampleStride = runoffSampleStride;
            RiverMaxTraceSteps = ScaleDistance(
                GetInt(numbers, "river.maxTraceSteps", 384),
                WorldCoordinateDistanceScale,
                32,
                2048);
            RiverMinimumVisibleCourseLength = ScaleDistance(
                GetInt(numbers, "river.minimumVisibleCourseLength", 96),
                WorldCoordinateDistanceScale,
                0,
                RiverMaxTraceSteps);
            RiverInfiltrationFloor = Clamp01(
                GetDouble(numbers, "river.infiltrationFloor", 0.25d));
            RiverStartFlow = Positive(GetDouble(numbers, "river.startFlow", 0.405d), 0.405d);
            RiverTributaryStartFlow = Math.Min(
                RiverStartFlow,
                Positive(GetDouble(numbers, "river.tributaryStartFlow", 0.195d), 0.195d));
            RiverFullWidthFlow = Math.Max(
                RiverStartFlow,
                Positive(GetDouble(numbers, "river.fullWidthFlow", 1.2d), 1.2d));
            double lateralDistanceScale = Math.Sqrt(WorldCoordinateDistanceScale);
            RiverMaxWidth = ScaleDistance(
                GetInt(numbers, "river.maxWidth", 7), lateralDistanceScale, 1, 15);
            RiverMeanderTieTolerance = Clamp(
                GetDouble(numbers, "river.meanderTieTolerance", 0d), 0d, 0.02d);
            RiverMeanderStrength = Clamp(
                GetDouble(numbers, "river.meanderStrength", 0.85d), 0d, 1.5d);
            RiverMeanderScale = Math.Max(8d, FinitePositive(
                GetDouble(numbers, "river.meanderScale", 48d), 48d) *
                WorldCoordinateDistanceScale);
            RiverValleyDetailWeight = Clamp(
                GetDouble(numbers, "river.valleyDetailWeight", 4d), 0d, 4d);
            RiverLookAheadWeight = Clamp(
                GetDouble(numbers, "river.lookAheadWeight", 0.55d), 0d, 0.8d);
            RiverLookAheadDistance = ScaleDistance(
                GetInt(numbers, "river.lookAheadDistance", 6),
                WorldCoordinateDistanceScale,
                1,
                24);
            RiverFloodplainStartFlow = Math.Max(
                RiverStartFlow,
                Positive(GetDouble(numbers, "river.floodplainStartFlow", 0.405d), 0.405d));
            RiverFloodplainMaxRadius = ScaleDistance(
                GetInt(numbers, "river.floodplainMaxRadius", 8),
                lateralDistanceScale,
                0,
                24);
            RiverFloodplainMaxSlope = Positive(
                GetDouble(numbers, "river.floodplainMaxSlope", 0.08d), 0.08d);
            RiverAlluvialTileThreshold = Clamp01(
                GetDouble(numbers, "river.alluvialTileThreshold", 0.62d));
            RiverDepthMin = Clamp01(GetDouble(numbers, "river.depthMin", 0.2d));
            RiverDepthMax = Math.Max(
                RiverDepthMin,
                Clamp01(GetDouble(numbers, "river.depthMax", 0.9d)));
            RiverMinLakeCells = Clamp(
                GetInt(numbers, "river.minLakeCells", 18), 1, 4096);
            RiverMaxLakeCells = Clamp(
                GetInt(numbers, "river.maxLakeCells", 220), RiverMinLakeCells, 4096);
            RiverMaxLakeLevelRise = Clamp(
                GetDouble(numbers, "river.maxLakeLevelRise", 0.045d), 0.001d, 0.25d);
            RiverLakeMinFlow = Positive(
                GetDouble(numbers, "river.lakeMinFlow", 0.35d), 0.35d);
            RiverLakeChance = Clamp01(
                GetDouble(numbers, "river.lakeChance", 0.75d));
            RiverMaxCachedRegions = Clamp(
                GetInt(numbers, "river.maxCachedRegions", 9), 1, 32);
            GrassDensity = Clamp01(GetDouble(numbers, "grass.density", 0.24d));
            StructureEnabled = GetBool(numbers, "structure.enabled", true);
            StructureRegionSize = Math.Max(8, GetInt(numbers, "structure.regionSize", 96));
            StructureChance = Clamp01(GetDouble(numbers, "structure.spawnChance", 0.18d));
            StructureRadius = Clamp(GetInt(numbers, "structure.radius", 2), 1, 12);
            StructureGroundTileId = GetInt(numbers, "structure.groundTileId", SandTileId);
            ResourceSpawnTypeId = GetText(texts, "resource.spawnTypeId",
                GetText(texts, "entity.spawnTypeId", string.Empty));
            ResourceDensity = Clamp01(GetDouble(numbers, "resource.density",
                GetDouble(numbers, "entity.spawnCount", 0d) / 256d));
            ResourceMinSpacing = Clamp(GetInt(numbers, "resource.minSpacing", 5), 1, 64);
            CaveOpenThreshold = Clamp01(GetDouble(numbers, "cave.openThreshold", 0.52d));
            CaveRegionSize = ScaleDistance(
                GetInt(numbers, "cave.regionSize", 32), WorldCoordinateDistanceScale, 8, 512);
            CaveRoomMinRadius = ScaleDistance(
                Positive(GetDouble(numbers, "cave.room.minRadius", 3.8d), 3.8d),
                WorldCoordinateDistanceScale, 0.75d, 128d);
            CaveRoomMaxRadius = Math.Max(
                CaveRoomMinRadius,
                ScaleDistance(Positive(
                        GetDouble(numbers, "cave.room.maxRadius", 6.8d), 6.8d),
                    WorldCoordinateDistanceScale, 0.75d, 128d));
            CaveTunnelMinRadius = ScaleDistance(Positive(
                    GetDouble(numbers, "cave.tunnel.minRadius", 1.35d), 1.35d),
                WorldCoordinateDistanceScale, 0.5d, 32d);
            CaveTunnelMaxRadius = Math.Max(
                CaveTunnelMinRadius,
                ScaleDistance(Positive(
                        GetDouble(numbers, "cave.tunnel.maxRadius", 2.15d), 2.15d),
                    WorldCoordinateDistanceScale, 0.5d, 32d));
            CaveNetworkExtraConnectionChance = Clamp01(GetDouble(
                numbers, "cave.network.extraConnectionChance", 0.28d));
            CaveBiomeBoundaryHalfWidth = ScaleDistance(NonNegativeFinite(GetDouble(
                    numbers, "cave.biomeBoundary.halfWidth", 1.5d), 1.5d),
                WorldCoordinateDistanceScale, 0d, 16d);
            CaveSpawnX = Finite(GetDouble(numbers, "cave.spawn.x", 0.5d), 0.5d);
            CaveSpawnY = Finite(GetDouble(numbers, "cave.spawn.y", 0.5d), 0.5d);
            CaveSpawnSafeRadius = NonNegativeFinite(
                GetDouble(numbers, "cave.spawn.safeRadius", 4d), 4d);
            CaveSurfaceOceanWallChance = Clamp01(GetDouble(
                numbers, "cave.surfaceInfluence.oceanWallChance", 0.85d));
            CaveGroundwaterEnabled = GetBool(numbers, "cave.groundwater.enabled", false);
            CaveGroundwaterRoomChance = Clamp01(
                GetDouble(numbers, "cave.groundwater.roomChance", 0.28d));
            CaveGroundwaterMinRadiusRatio = Clamp(
                GetDouble(numbers, "cave.groundwater.minRadiusRatio", 0.42d), 0.15d, 0.9d);
            CaveGroundwaterMaxRadiusRatio = Clamp(
                GetDouble(numbers, "cave.groundwater.maxRadiusRatio", 0.68d),
                CaveGroundwaterMinRadiusRatio, 0.95d);
            CaveGroundwaterMinDepth = Clamp01(
                GetDouble(numbers, "cave.groundwater.minDepth", 0.25d));
            CaveGroundwaterMaxDepth = Math.Max(CaveGroundwaterMinDepth, Clamp01(
                GetDouble(numbers, "cave.groundwater.maxDepth", 0.85d)));
            CaveVineEnabled = GetBool(numbers, "cave.vine.enabled", false);
            CaveVineWallChance = Clamp01(
                GetDouble(numbers, "cave.vine.wallChance", 0.06d));
            CaveVineWetMultiplier = Clamp(
                GetDouble(numbers, "cave.vine.wetMultiplier", 2.5d), 1d, 10d);
            CaveVineDryMultiplier = Clamp01(
                GetDouble(numbers, "cave.vine.dryMultiplier", 0.2d));
            CavePortalEnabled = GetBool(numbers, "cave.portal.enabled", true);
            CavePortalChunkChance = Clamp01(
                GetDouble(numbers, "cave.portal.chunkChance", 0d));
            CavePortalSafeRadius = Math.Max(1d, Positive(
                GetDouble(numbers, "cave.portal.safeRadius", 3d), 3d));
            // 为 0 时沿用当前 Profile 的区块尺寸；编辑器连续预览会显式保留正式区块尺寸。
            CavePortalChunkWidth = Math.Max(0,
                GetInt(numbers, "cave.portal.chunkWidth", 0));
            CavePortalChunkHeight = Math.Max(0,
                GetInt(numbers, "cave.portal.chunkHeight", 0));
            CavePortalBaseSeed = GetInt(numbers, "cave.portal.baseSeed", 0);
            CavePortalSeedSalt = GetInt(numbers, "cave.portal.seedSalt", 7919);
            CavePortalShrubEnabled = GetBool(numbers, "cave.portal.shrub.enabled", true);
            CavePortalShrubRadius = Clamp(
                GetInt(numbers, "cave.portal.shrub.radius", 7), 1, 32);
            CavePortalShrubChanceMultiplier = Math.Max(0d, Finite(
                GetDouble(numbers, "cave.portal.shrub.chanceMultiplier", 64d), 64d));
            CaveResourceDensity = Clamp01(
                GetDouble(numbers, "cave.resource.density", 0.042d));
            CaveLooseOreDensity = Clamp01(
                GetDouble(numbers, "cave.resource.looseDensity", 0.0012d));
            CavePortalItemId = GetText(texts, "cave.portal.itemId", "CaveExit");
            CavePortalTargetDimensionId = GetText(
                texts,
                "cave.portal.targetDimensionId",
                Mode == ChunkGenerationMode.Cave ? "surface" : "cave");
            DefaultNavigationCost = (short)Clamp(GetInt(numbers,
                "navigation.defaultCost", 1), 1, short.MaxValue);
            RiverNavigationCost = (short)Clamp(GetInt(numbers,
                "navigation.riverCost", 20000), DefaultNavigationCost, short.MaxValue);
        }

        /// <summary>生成地表还是洞穴。</summary>
        public ChunkGenerationMode Mode { get; }
        // 下面这些都是地块的数字编号，不直接保存 Unity 里的 Tile 图片资源。
        public int GroundTileId { get; }
        public int FreshWaterTileId { get; }
        public int SaltWaterTileId { get; }
        public int SandTileId { get; }
        public int StoneTileId { get; }
        public int SnowTileId { get; }
        public int IceTileId { get; }
        public int SnowVariant2TileId { get; }
        public int SnowVariant3TileId { get; }
        public int CaveFloorTileId { get; }
        public int CaveWallTileId { get; }
        /// <summary>高度低于这个数时生成海洋。</summary>
        public double SeaLevel { get; }
        /// <summary>高于海面但低于这个数时生成沙滩。</summary>
        public double BeachLevel { get; }
        /// <summary>高度达到这个数时使用可行走的石地表现二维山地。</summary>
        public double MountainLevel { get; }
        /// <summary>实际温度低于这个数时具备积雪条件。</summary>
        public double SnowTemperature { get; }
        /// <summary>降水高于这个数时才能形成雪地。</summary>
        public double SnowMinimumPrecipitation { get; }
        /// <summary>雪地低洼处生成冰面的基础概率。</summary>
        public double SnowIceLakeChance { get; }
        /// <summary>雪地草地相对于普通草地的生成密度倍率。</summary>
        public double SnowGrassDensityMultiplier { get; }
        /// <summary>旧版有序群系判定中沙漠允许的最低高度和最高降水。</summary>
        public double DesertMinimumHeight { get; }
        public double DesertMaximumPrecipitation { get; }
        /// <summary>旧版温带草原允许的温度与降水闭区间。</summary>
        public double GrasslandMinimumTemperature { get; }
        public double GrasslandMaximumTemperature { get; }
        public double GrasslandMinimumPrecipitation { get; }
        public double GrasslandMaximumPrecipitation { get; }
        /// <summary>玩家为当前世界选择的坐标倍率；默认 0.01。</summary>
        public double WorldCoordinateScale { get; }
        /// <summary>坐标倍率换算成格子距离后的反比倍率，限制在 0.25 到 4。</summary>
        public double WorldCoordinateDistanceScale { get; }
        /// <summary>控制山地变化有多快；数值越小，大片地形通常越平缓。</summary>
        public double TerrainScale { get; }
        /// <summary>控制温度和降水区域变化有多快。</summary>
        public double ClimateScale { get; }
        /// <summary>高度随机图叠加多少层细节；越多越细，但计算也更多。</summary>
        public int HeightOctaves { get; }
        /// <summary>气候随机图叠加多少层细节。</summary>
        public int ClimateOctaves { get; }
        /// <summary>地表高度和降水使用哪套采样规则。</summary>
        public SurfaceClimateAlgorithm SurfaceClimateAlgorithm { get; }
        /// <summary>旧版 Land 高度噪声通道。</summary>
        public TerrainNoiseChannelSettings HeightNoise { get; }
        /// <summary>旧版 Land 基础降水噪声通道。</summary>
        public TerrainNoiseChannelSettings PrecipitationNoise { get; }
        /// <summary>旧版 Land 温度噪声通道及归一化温度对应的摄氏范围。</summary>
        public TerrainNoiseChannelSettings TemperatureNoise { get; }
        public double TemperatureCelsiusMin { get; }
        public double TemperatureCelsiusMax { get; }
        /// <summary>从这个高度开始按海拔降低实际温度。</summary>
        public double TemperatureAltitudeCoolingStart { get; }
        /// <summary>高度每上升 1 对归一化温度的降温强度。</summary>
        public double TemperatureAltitudeCoolingStrength { get; }
        /// <summary>是否把高于和低于中值的高度差再次平方强化。</summary>
        public bool HeightSecondaryBoostEnabled { get; }
        public double HeightSecondaryBoostStrength { get; }
        /// <summary>区域风向插值网格的世界尺寸。</summary>
        public double WindRegionSize { get; }
        public int WindSeedSalt { get; }
        /// <summary>沿逆风方向检查地形的最远距离与采样数。</summary>
        public double OrographicSampleDistance { get; }
        public int OrographicSampleCount { get; }
        /// <summary>迎风坡增雨和背风坡雨影强度。</summary>
        public double WindwardRainGain { get; }
        public double LeewardRainLoss { get; }
        /// <summary>地表要不要生成河流。</summary>
        public bool RiverEnabled { get; }
        /// <summary>河流算法；默认保留新版高度汇流，正式地表可显式选择旧版区域水文。</summary>
        public RiverGenerationAlgorithm RiverAlgorithm { get; }
        /// <summary>旧版区域水文一次生成并缓存的正方形边长。</summary>
        public int RiverHydrologyRegionSize { get; }
        /// <summary>每隔多少格汇总一次降水径流，并选择该区域的高处作为支流源头。</summary>
        public int RiverRunoffCellSize { get; }
        /// <summary>径流区域内采样高度图与降水图的步长。</summary>
        public int RiverRunoffSampleStride { get; }
        /// <summary>一条支流沿高度图向下游追踪的最大格数。</summary>
        public int RiverMaxTraceSteps { get; }
        /// <summary>低于该连通河程的短小河网整条隐藏，避免只露出零碎水线。</summary>
        public int RiverMinimumVisibleCourseLength { get; }
        /// <summary>低于该值的降水被地表吸收，不形成有效径流。</summary>
        public double RiverInfiltrationFloor { get; }
        /// <summary>累计径流达到该值后形成可见河道。</summary>
        public double RiverStartFlow { get; }
        /// <summary>只有汇入成熟主河时，累计径流达到该值的细支流才会显示。</summary>
        public double RiverTributaryStartFlow { get; }
        /// <summary>累计径流达到该值后河道扩展到最大宽度。</summary>
        public double RiverFullWidthFlow { get; }
        /// <summary>河道允许扩展到的最大格宽。</summary>
        public int RiverMaxWidth { get; }
        /// <summary>等高邻格之间仅用于稳定选路的微小扰动，不负责绘制河流形状。</summary>
        public double RiverMeanderTieTolerance { get; }
        /// <summary>在严格下坡候选方向内施加的连续弯曲强度，单位为八方向扇区。</summary>
        public double RiverMeanderStrength { get; }
        /// <summary>连续弯曲场的世界格尺度；越大，河弯越舒缓。</summary>
        public double RiverMeanderScale { get; }
        /// <summary>放大高度图中的细谷，使主河优先贴着真实谷底弯曲。</summary>
        public double RiverValleyDetailWeight { get; }
        /// <summary>选下坡格时参考前方谷地的权重，减少短视直线和锯齿。</summary>
        public double RiverLookAheadWeight { get; }
        /// <summary>河流选路向前查看高度图的格数。</summary>
        public int RiverLookAheadDistance { get; }
        /// <summary>汇流达到该值后，低坡河段开始形成冲积平原。</summary>
        public double RiverFloodplainStartFlow { get; }
        /// <summary>主河两侧冲积平原允许扩展的最大半径。</summary>
        public int RiverFloodplainMaxRadius { get; }
        /// <summary>高于该局部坡度时不生成宽冲积平原。</summary>
        public double RiverFloodplainMaxSlope { get; }
        /// <summary>冲积强度超过该值时使用沙土 Tile 表现沉积带。</summary>
        public double RiverAlluvialTileThreshold { get; }
        public double RiverDepthMin { get; }
        public double RiverDepthMax { get; }
        /// <summary>盆地至少包含多少格才会表现成湖泊。</summary>
        public int RiverMinLakeCells { get; }
        /// <summary>盆地扩张与湖泊表现允许的最大格数。</summary>
        public int RiverMaxLakeCells { get; }
        /// <summary>盆地水面相对汇水洼地允许抬升的最大高度。</summary>
        public double RiverMaxLakeLevelRise { get; }
        /// <summary>累计径流达到该值后，合格盆地才会表现为湖泊。</summary>
        public double RiverLakeMinFlow { get; }
        /// <summary>高度驱动河网在内陆汇流终点形成淡水湖的确定性概率。</summary>
        public double RiverLakeChance { get; }
        /// <summary>每个纯生成器实例最多保留多少个已完成水文区域。</summary>
        public int RiverMaxCachedRegions { get; }
        /// <summary>合适的地面上长出草的基本概率。</summary>
        public double GrassDensity { get; }
        /// <summary>是否生成遗迹等结构；同样的种子会得到同样的位置。</summary>
        public bool StructureEnabled { get; }
        public int StructureRegionSize { get; }
        public double StructureChance { get; }
        public int StructureRadius { get; }
        public int StructureGroundTileId { get; }
        /// <summary>要生成哪种资源；空字符串表示不生成。</summary>
        public string ResourceSpawnTypeId { get; }
        public double ResourceDensity { get; }
        public int ResourceMinSpacing { get; }
        /// <summary>洞穴随机值高于这个数时挖成空地，否则保留岩壁。</summary>
        public double CaveOpenThreshold { get; }
        /// <summary>旧矿洞房间网络使用的逻辑区域边长。</summary>
        public int CaveRegionSize { get; }
        public double CaveRoomMinRadius { get; }
        public double CaveRoomMaxRadius { get; }
        public double CaveTunnelMinRadius { get; }
        public double CaveTunnelMaxRadius { get; }
        /// <summary>每个房间的固定主支路之外，再增加第二方向连接的概率。</summary>
        public double CaveNetworkExtraConnectionChance { get; }
        /// <summary>地表基础群系交界向两侧扩展的地下通道半宽；0 表示关闭。</summary>
        public double CaveBiomeBoundaryHalfWidth { get; }
        /// <summary>矿洞默认出生点及其不可放矿安全半径。</summary>
        public double CaveSpawnX { get; }
        public double CaveSpawnY { get; }
        public double CaveSpawnSafeRadius { get; }
        /// <summary>地表为海洋时，普通洞穴区域转为石墙的确定性概率。</summary>
        public double CaveSurfaceOceanWallChance { get; }
        /// <summary>洞室地下湖的确定性分布、水面半径与水深范围；高度带沿用地表海平面和山地线。</summary>
        public bool CaveGroundwaterEnabled { get; }
        public double CaveGroundwaterRoomChance { get; }
        public double CaveGroundwaterMinRadiusRatio { get; }
        public double CaveGroundwaterMaxRadiusRatio { get; }
        public double CaveGroundwaterMinDepth { get; }
        public double CaveGroundwaterMaxDepth { get; }
        /// <summary>洞壁藤蔓基础概率；临近地下水时使用湿润倍率。</summary>
        public bool CaveVineEnabled { get; }
        public double CaveVineWallChance { get; }
        public double CaveVineWetMultiplier { get; }
        /// <summary>远离地下水时的藤蔓概率倍率，默认保留原概率的 20%。</summary>
        public double CaveVineDryMultiplier { get; }
        /// <summary>跨维度天然入口的成对布局参数。</summary>
        public bool CavePortalEnabled { get; }
        public double CavePortalChunkChance { get; }
        public double CavePortalSafeRadius { get; }
        /// <summary>传送门概率格的宽高；0 表示跟随当前生成 Profile 的区块尺寸。</summary>
        public int CavePortalChunkWidth { get; }
        public int CavePortalChunkHeight { get; }
        public int CavePortalBaseSeed { get; }
        public int CavePortalSeedSalt { get; }
        /// <summary>是否在地表天然洞穴入口周围额外生成灌木。</summary>
        public bool CavePortalShrubEnabled { get; }
        /// <summary>洞穴入口灌木外圈半径；入口安全半径以内不放置灌木。</summary>
        public int CavePortalShrubRadius { get; }
        /// <summary>入口周边灌木的额外生成概率倍率；只作用于草原和森林的 Bush 规则。</summary>
        public double CavePortalShrubChanceMultiplier { get; }
        public string CavePortalItemId { get; }
        public string CavePortalTargetDimensionId { get; }
        /// <summary>洞壁矿脉与散落矿石的密度。</summary>
        public double CaveResourceDensity { get; }
        public double CaveLooseOreDensity { get; }
        /// <summary>普通地面默认有多难走；数字越大，寻路越不喜欢走。</summary>
        public short DefaultNavigationCost { get; }
        /// <summary>河流的有限寻路代价；高于陆地，但不能把河流变成不可通行障碍。</summary>
        public short RiverNavigationCost { get; }

        /// <summary>把气候通道的基础温度换算成受海拔影响的实际温度。</summary>
        public double ApplyAltitudeTemperatureCooling(double height, double baseTemperature)
        {
            double elevation = Math.Max(0d,
                Clamp01(height) - TemperatureAltitudeCoolingStart);
            return Clamp01(baseTemperature - elevation * TemperatureAltitudeCoolingStrength);
        }

        // 这些小方法只从当前这份设置里取值，不会偷偷读取全局设置。
        /// <summary>读取一个整数参数；找不到时返回默认值。</summary>
        private static int GetInt(IReadOnlyDictionary<string, double> values, string key, int fallback) =>
            values.TryGetValue(key, out double value) ? (int)value : fallback;

        /// <summary>读取一个小数参数；找不到时返回默认值。</summary>
        private static double GetDouble(IReadOnlyDictionary<string, double> values, string key,
            double fallback) => values.TryGetValue(key, out double value) ? value : fallback;

        /// <summary>读取一个开关参数；数值大于 0.5 时视为开启。</summary>
        private static bool GetBool(IReadOnlyDictionary<string, double> values, string key,
            bool fallback) => values.TryGetValue(key, out double value) ? value > 0.5d : fallback;

        /// <summary>读取一个文本参数；空文本或找不到时返回默认值。</summary>
        private static string GetText(IReadOnlyDictionary<string, string> values, string key,
            string fallback) => values.TryGetValue(key, out string value) &&
                               !string.IsNullOrWhiteSpace(value) ? value : fallback;

        /// <summary>严格解析河流算法，避免配置拼写错误时静默换成另一套世界生成规则。</summary>
        private static RiverGenerationAlgorithm ParseRiverAlgorithm(string value)
        {
            if (value.Equals("heightDriven", StringComparison.OrdinalIgnoreCase))
                return RiverGenerationAlgorithm.HeightDriven;
            if (value.Equals("legacy", StringComparison.OrdinalIgnoreCase))
                return RiverGenerationAlgorithm.Legacy;
            throw new ArgumentException($"Unknown river algorithm: {value}", nameof(value));
        }

        /// <summary>严格解析地表气候算法，避免配置拼写错误改变整张世界地图。</summary>
        private static SurfaceClimateAlgorithm ParseSurfaceClimateAlgorithm(string value)
        {
            if (value.Equals("simple", StringComparison.OrdinalIgnoreCase))
                return SurfaceClimateAlgorithm.Simple;
            if (value.Equals("legacy", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("legacyLand", StringComparison.OrdinalIgnoreCase))
            {
                return SurfaceClimateAlgorithm.LegacyLand;
            }
            throw new ArgumentException($"Unknown climate algorithm: {value}", nameof(value));
        }

        /// <summary>从数值表构造一条经过约束的旧版噪声通道。</summary>
        private static TerrainNoiseChannelSettings CreateNoiseChannel(
            IReadOnlyDictionary<string, double> values,
            string prefix,
            double defaultCoordinateScale,
            double defaultFrequency,
            int defaultOctaves,
            double defaultLacunarity,
            double defaultPersistence,
            double defaultOffsetX,
            double defaultOffsetY)
        {
            return new TerrainNoiseChannelSettings(
                FinitePositive(GetDouble(values, prefix + ".coordScale", defaultCoordinateScale),
                    defaultCoordinateScale),
                FinitePositive(GetDouble(values, prefix + ".frequency", defaultFrequency),
                    defaultFrequency),
                Clamp(GetInt(values, prefix + ".octaves", defaultOctaves), 1, 12),
                FinitePositive(GetDouble(values, prefix + ".lacunarity", defaultLacunarity),
                    defaultLacunarity),
                Clamp(Finite(GetDouble(values, prefix + ".persistence", defaultPersistence),
                    defaultPersistence), 0d, 1d),
                Finite(GetDouble(values, prefix + ".offsetX", defaultOffsetX), defaultOffsetX),
                Finite(GetDouble(values, prefix + ".offsetY", defaultOffsetY), defaultOffsetY));
        }

        /// <summary>把整数限制在指定的最小值和最大值之间。</summary>
        private static int Clamp(int value, int min, int max) =>
            value < min ? min : value > max ? max : value;

        /// <summary>把小数限制在指定的最小值和最大值之间。</summary>
        private static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;

        /// <summary>把小数限制在 0 到 1 之间。</summary>
        private static double Clamp01(double value) => value < 0d ? 0d : value > 1d ? 1d : value;
        /// <summary>返回正数参数；输入不合法时使用默认值。</summary>
        private static double Positive(double value, double fallback) => value > 0d ? value : fallback;

        /// <summary>距离类参数按世界坐标倍率反向换算，并保留安全上下限。</summary>
        private static int ScaleDistance(int value, double scale, int min, int max)
        {
            int scaled = (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
            return Clamp(scaled, min, max);
        }

        /// <summary>浮点距离参数按世界坐标倍率反向换算，并保留安全上下限。</summary>
        private static double ScaleDistance(double value, double scale, double min, double max)
        {
            return Clamp(value * scale, min, max);
        }

        /// <summary>过滤负数和无穷数，保证坐标缩放参数可以安全参与计算。</summary>
        private static double NonNegativeFinite(double value, double fallback)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0d
                ? fallback
                : value;
        }

        /// <summary>返回有限正数；非法值使用给定默认值。</summary>
        private static double FinitePositive(double value, double fallback)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value <= 0d
                ? fallback
                : value;
        }

        /// <summary>返回有限数；NaN 与无穷值使用给定默认值。</summary>
        private static double Finite(double value, double fallback)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
        }
    }
}
