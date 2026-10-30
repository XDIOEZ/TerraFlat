using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

namespace FlatWorld.WorldModel
{
    public sealed partial class DeterministicChunkGenerator
    {
        #region 地表与高度图采样

        /// <summary>先按海洋距离降水生成河网，再恢复独立内陆湖泊。</summary>
        private GeneratedHydrologyMap BuildSurfaceHydrologyMap(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            CancellationToken cancellationToken, ChunkGenerationTiming timing = null)
        {
            GeneratedHydrologyMap rivers = settings.RiverEnabled
                ? BuildHeightDrivenRiverMap(request, settings, cancellationToken, timing) : null;
            if (settings.LargeLakeEnabled)
                using (timing?.MeasureStage("hydrology.large_lake") ?? default)
                    rivers = LargeFreshwaterLakeKernel.Build(request, settings, rivers,
                        p => SampleHeight(request, settings, p.X, p.Y), cancellationToken,
                        GetHydrologyOutputPadding(request),
                        p => SampleOceanPrecipitation(request, settings, p.X, p.Y));
            return rivers;
        }

        /// <summary>
        /// 从与地表完全相同的高度图和降水图构建当前区块的河道。
        /// 河流只负责沿低处汇流；不再使用独立噪声场或正弦函数直接绘制河带。
        /// </summary>
        private GeneratedHydrologyMap BuildHeightDrivenRiverMap(
            ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings,
            CancellationToken cancellationToken, ChunkGenerationTiming timing)
        {
            MacroHydrologyRegion macro = hydrologyRegions.GetOrGenerateRegion(request, settings,
                cancellationToken, timing, ChunkGenerationScheduler.IsExecutingScheduledChunkGeneration);
            if (!isBiomeSearchGenerator)
                hydrologyRegions.QueueNeighbourPrewarm(request, settings, ResolveHeightDrivenRegion(request, settings),
                    cancellationToken);
            // 邻区宿主查询复用外层请求诊断，父子细化阶段统一累计而非互相覆盖。
            using ((timing ?? ChunkGenerationTiming.CurrentOnThread)?.MeasureStage(
                       "river.local_refine", accumulate: true) ?? default)
                return BuildMacroRiverChunk(request, settings, macro, cancellationToken);
        }

        #region 地表区块批量生成

        /// <summary>气候输入先按区块采样，再批量判群系并写入连续地形数组。</summary>
        private void GenerateSurfaceChunk(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, ChunkTerrainBuffer terrain,
            GeneratedHydrologyMap riverMap, CancellationToken cancellationToken,
            ChunkGenerationTiming timing)
        {
            using var batch = new SurfaceChunkBatch(request, settings, riverMap);
            using (timing?.MeasureStage("terrain.noise") ?? default)
                batch.SampleCore(cancellationToken);
            int seaWater = terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.SeaWaterId);
            int dirtyWater = terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
            IReadOnlyList<LavaBasin> volcanic = ResolveLavaBasins(
                ExpandGenerationRequest(request, GetHydrologyOutputPadding(request)), cancellationToken);
            int lava = volcanic.Count > 0 ? terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.LavaId) : 0;
            IReadOnlyList<LavaBasin> petroleum = ResolveCrudeOilLandBasins(request, cancellationToken);
            int crudeOil = petroleum.Count > 0 ? terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.CrudeOilId) : 0;
            using var airHumidity = new GeographicAirHumiditySampler(request, settings, riverMap,
                volcanic, cancellationToken, batch.SampleCachedHeight);
            float humidityBackground = AirHumiditySettings.FromProfile(request.Profile).Background;
            bool hasEstuary = false;
            bool hasOceanCurrent = false;
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
                        request.Address.ChunkOrigin.Y + y, seaWater, dirtyWater, lava, volcanic, airHumidity,
                        crudeOil, petroleum);
                    hasEstuary |= batch.Output[index].Estuary.Influence > 0d;
                    hasOceanCurrent |= batch.Output[index].HasOceanCurrent;
                    batch.Output[index].AirHumidity = airHumidity.Sample(
                        request.Address.ChunkOrigin.X + x, request.Address.ChunkOrigin.Y + y);
                    batch.Output[index].AirHumidityBackground = humidityBackground;
                }
            }
            using (timing?.MeasureStage("terrain.environment_write") ?? default)
            {
                var layers = new SurfaceEnvironmentWriter(terrain, hasEstuary, hasOceanCurrent);
                for (int y = 0; y < terrain.Height; y++)
                for (int x = 0; x < terrain.Width; x++)
                {
                    int index = y * terrain.Width + x;
                    if ((index & 63) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    SurfaceCellOutput output = batch.Output[index];
                    terrain.SetCell(x, y, output.Cell);
                    terrain.SetLiquid(x, y, output.LiquidTypeIndex, output.LiquidLevel);
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
            public double BiomeRegionProbability;
        }

        private struct SurfaceCellOutput
        {
            public TerrainCell Cell;
            public int LiquidTypeIndex;
            public int LiquidLevel;
            public int NaturalDepthLevels;
            public FrozenSurfaceWaterSource FrozenWater;
            public float SnowDepth;
            public byte Grass;
            public float Height;
            public float Temperature;
            public float TemperatureCelsius;
            public float Precipitation;
            public float WindX;
            public float WindY;
            public bool HasOceanCurrent;
            public OceanShoreCurrentSample OceanCurrent;
            public float Moisture;
            public float AirHumidity;
            public float AirHumidityBackground;
            public float Mountain;
            public float RiverFlow;
            public float RiverFlowX;
            public float RiverFlowY;
            public EstuaryCurrentSample Estuary;
            public float RiverFloodplain;
            public float RiverSurfaceLevel;
            public float RiverKind;
            public float LavaShore;
        }

        /// <summary>固定环境层布局只解析一次名称，热循环只写数组索引。</summary>
        private readonly struct SurfaceEnvironmentWriter
        {
            private readonly ChunkTerrainBuffer terrain;
            private readonly float[] height;
            private readonly float[] temperature;
            private readonly float[] temperatureCelsius;
            private readonly float[] precipitation;
            private readonly float[] windX;
            private readonly float[] windY;
            private readonly float[] oceanCurrentX;
            private readonly float[] oceanCurrentY;
            private readonly float[] moisture;
            private readonly float[] airHumidity;
            private readonly float[] airHumidityBackground;
            private readonly float[] mountain;
            private readonly float[] riverFlow;
            private readonly float[] riverFlowX;
            private readonly float[] riverFlowY;
            private readonly float[] estuaryX;
            private readonly float[] estuaryY;
            private readonly float[] estuaryInfluence;
            private readonly float[] riverFloodplain;
            private readonly float[] riverSurfaceLevel;
            private readonly float[] riverKind;
            private readonly float[] structure;
            private readonly float[] lavaShore;
            private readonly float[] grass;
            private readonly float[] snowDepth;
            private readonly float[] naturalDepthLevels;

            public SurfaceEnvironmentWriter(ChunkTerrainBuffer terrain, bool hasEstuary = false, bool hasOceanCurrent = false)
            {
                this.terrain = terrain;
                height = terrain.GetOrCreateEnvironmentLayer("height");
                temperature = terrain.GetOrCreateEnvironmentLayer("temperature");
                temperatureCelsius = terrain.GetOrCreateEnvironmentLayer("temperature.celsius");
                precipitation = terrain.GetOrCreateEnvironmentLayer("precipitation");
                windX = terrain.GetOrCreateEnvironmentLayer("windX");
                windY = terrain.GetOrCreateEnvironmentLayer("windY");
                oceanCurrentX = hasOceanCurrent ? terrain.GetOrCreateEnvironmentLayer(OceanShoreCurrentKernel.DirectionXLayer) : null;
                oceanCurrentY = hasOceanCurrent ? terrain.GetOrCreateEnvironmentLayer(OceanShoreCurrentKernel.DirectionYLayer) : null;
                moisture = terrain.GetOrCreateEnvironmentLayer("moisture");
                airHumidity = terrain.GetOrCreateEnvironmentLayer(AirHumidityKernel.GeographicLayer);
                airHumidityBackground = terrain.GetOrCreateEnvironmentLayer(AirHumidityKernel.BackgroundLayer);
                mountain = terrain.GetOrCreateEnvironmentLayer("mountain");
                riverFlow = terrain.GetOrCreateEnvironmentLayer("riverFlow");
                riverFlowX = terrain.GetOrCreateEnvironmentLayer("riverFlowX");
                riverFlowY = terrain.GetOrCreateEnvironmentLayer("riverFlowY");
                // 普通陆地、湖泊和远洋区块不为局部河口分配整层数组。
                estuaryX = hasEstuary ? terrain.GetOrCreateEnvironmentLayer(EstuaryCurrentKernel.DirectionXLayer) : null;
                estuaryY = hasEstuary ? terrain.GetOrCreateEnvironmentLayer(EstuaryCurrentKernel.DirectionYLayer) : null;
                estuaryInfluence = hasEstuary ? terrain.GetOrCreateEnvironmentLayer(EstuaryCurrentKernel.InfluenceLayer) : null;
                riverFloodplain = terrain.GetOrCreateEnvironmentLayer("riverFloodplain");
                riverSurfaceLevel = terrain.GetOrCreateEnvironmentLayer("riverSurfaceLevel");
                riverKind = terrain.GetOrCreateEnvironmentLayer("riverKind");
                structure = terrain.GetOrCreateEnvironmentLayer("structure");
                lavaShore = terrain.GetOrCreateEnvironmentLayer("lava.shore");
                grass = terrain.GetOrCreateEnvironmentLayer("grass");
                snowDepth = terrain.GetOrCreateEnvironmentLayer(SnowDepthLayer.LayerId);
                naturalDepthLevels = terrain.GetOrCreateEnvironmentLayer(TerrainSurfaceHeightLayer.NaturalDepthLevelsLayerId);
            }

            public void Write(int index, SurfaceCellOutput value)
            {
                height[index] = value.Height;
                temperature[index] = value.Temperature;
                temperatureCelsius[index] = value.TemperatureCelsius;
                precipitation[index] = value.Precipitation;
                windX[index] = value.WindX;
                windY[index] = value.WindY;
                if (oceanCurrentX != null)
                {
                    oceanCurrentX[index] = (float)value.OceanCurrent.X;
                    oceanCurrentY[index] = (float)value.OceanCurrent.Y;
                }
                moisture[index] = value.Moisture;
                airHumidity[index] = value.AirHumidity;
                airHumidityBackground[index] = value.AirHumidityBackground;
                mountain[index] = value.Mountain;
                riverFlow[index] = value.RiverFlow;
                riverFlowX[index] = value.RiverFlowX;
                riverFlowY[index] = value.RiverFlowY;
                if (estuaryInfluence != null)
                {
                    estuaryX[index] = (float)value.Estuary.X;
                    estuaryY[index] = (float)value.Estuary.Y;
                    estuaryInfluence[index] = (float)value.Estuary.Influence;
                }
                riverFloodplain[index] = value.RiverFloodplain;
                riverSurfaceLevel[index] = value.RiverSurfaceLevel;
                riverKind[index] = value.RiverKind;
                structure[index] = 0f;
                lavaShore[index] = value.LavaShore;
                grass[index] = value.Grass == GrassPresent ? 1f : 0f;
                snowDepth[index] = SnowDepthLayer.Quantize(value.SnowDepth);
                naturalDepthLevels[index] = value.NaturalDepthLevels;
                FrozenSurfaceWaterLayer.WriteGenerationSource(terrain, index, value.FrozenWater);
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
            private readonly SurfacePrecipitationSampler precipitation;
            public readonly SurfaceCellOutput[] Output;

            public SurfaceChunkBatch(ChunkGenerationRequest request,
                ChunkGenerationSettingsSnapshot settings, GeneratedHydrologyMap riverMap = null)
            {
                this.request = request;
                this.settings = settings;
                precipitation = new SurfacePrecipitationSampler(request, settings, riverMap);
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
                            sample.Precipitation = precipitation.Sample(worldX, worldY);
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
                            request.Address.ChunkOrigin.Y + y - radius, precipitation);
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

            // 离岸窗口复用已有气候高度，外圈不重复计算温度、风向和降水。
            public double SampleCachedHeight(int worldX, int worldY)
            {
                int x = worldX - request.Address.ChunkOrigin.X + radius;
                int y = worldY - request.Address.ChunkOrigin.Y + radius;
                if ((uint)x < (uint)stride && (uint)y < (uint)(request.Profile.Height + radius * 2))
                    return samples[y * stride + x].Height;
                return SampleHeight(request, settings, worldX, worldY);
            }

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
                            request.Address.ChunkOrigin.Y + y + offsetY, precipitation);
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
            int worldX, int worldY, SurfacePrecipitationSampler precipitation = null)
        {
            SurfaceClimateSample sample = SampleGeographicClimate(request, settings, worldX, worldY);
            sample.Precipitation = precipitation?.Sample(worldX, worldY) ??
                SampleOceanPrecipitation(request, settings, worldX, worldY);
            return FinishSurfaceClimate(request, settings, worldX, worldY, sample);
        }

        #endregion

        /// <summary>正式单格生成与群系查询共享最终分类和液体结果。</summary>
        private SurfaceCellOutput SampleSurfaceCellOutput(ChunkGenerationRequest request,
            ChunkGenerationSettingsSnapshot settings, GeneratedHydrologyMap riverMap,
            int x, int y, int worldX, int worldY, LiquidTypeCatalog types)
        {
            SurfaceClimateSample climate = SampleSurfaceClimate(
                request, settings, worldX, worldY, new SurfacePrecipitationSampler(request, settings, riverMap));
            double baseMoisture = Clamp01(climate.Precipitation * 0.78d +
                                          (1d - climate.Height) * 0.22d);
            climate.BaseBiome = ResolveSurfaceBiome(settings, climate, baseMoisture, false);
            climate.Classified = true;
            IReadOnlyList<LavaBasin> volcanic = ResolveLavaBasins(ExpandGenerationRequest(request, GetHydrologyOutputPadding(request)));
            IReadOnlyList<LavaBasin> petroleum = ResolveCrudeOilLandBasins(request);
            using var airHumidity = new GeographicAirHumiditySampler(request, settings, riverMap, volcanic);
            SurfaceCellOutput output = BuildSurfaceCell(request, settings, riverMap,
                climate, null, x, y, worldX, worldY,
                types.GetIndex(LiquidTypeCatalog.SeaWaterId),
                types.GetIndex(LiquidTypeCatalog.DirtyWaterId),
                volcanic.Count > 0 ? types.GetIndex(LiquidTypeCatalog.LavaId) : 0, volcanic, airHumidity,
                petroleum.Count > 0 ? types.GetIndex(LiquidTypeCatalog.CrudeOilId) : 0, petroleum);
            output.AirHumidity = airHumidity.Sample(worldX, worldY);
            output.AirHumidityBackground = AirHumiditySettings.FromProfile(request.Profile).Background;
            if (terrainDecorator == null) return output;
            // 单格查询同样调用纯地形扩展，出生与群系定位不能看到扩展前的地表。
            using var sample = new ChunkTerrainBuffer(1, 1, types);
            sample.SetCell(0, 0, output.Cell); sample.SetLiquid(0, 0, output.LiquidTypeIndex, output.LiquidLevel);
            sample.SetGrass(0, 0, output.Grass);
            new SurfaceEnvironmentWriter(sample, output.Estuary.Influence > 0d, output.HasOceanCurrent).Write(0, output);
            var cellRequest = new ChunkGenerationRequest(request.WorldEpoch,
                new WorldAddress(request.Address.DimensionId, new Int2(worldX, worldY)), request.WorldSeed,
                request.RequestVersion, request.Profile, request.Topology);
            terrainDecorator(cellRequest, sample, CancellationToken.None);
            using ChunkTerrainData decorated = sample.Seal();
            output.Cell = decorated.GetCell(0, 0); output.Grass = decorated.GetGrass(0, 0);
            output.LiquidTypeIndex = decorated.GetLiquidTypeIndex(0, 0); output.LiquidLevel = decorated.GetLiquidLevel(0, 0);
            if (decorated.TryGetEnvironmentValue("height", 0, 0, out float height)) output.Height = height;
            if (decorated.TryGetEnvironmentValue("temperature", 0, 0, out float temperature)) output.Temperature = temperature;
            if (decorated.TryGetEnvironmentValue("temperature.celsius", 0, 0, out float celsius)) output.TemperatureCelsius = celsius;
            if (decorated.TryGetEnvironmentValue("precipitation", 0, 0, out float rain)) output.Precipitation = rain;
            if (decorated.TryGetEnvironmentValue("moisture", 0, 0, out float moisture)) output.Moisture = moisture;
            if (decorated.TryGetEnvironmentValue(SnowDepthLayer.LayerId, 0, 0, out float snow)) output.SnowDepth = snow;
            output.NaturalDepthLevels = TerrainSurfaceHeightLayer.GetNaturalDepthLevels(decorated, 0, 0);
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
            IReadOnlyList<LavaBasin> volcanic,
            GeographicAirHumiditySampler airHumidity,
            int crudeOil,
            IReadOnlyList<LavaBasin> petroleum)
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
                groundTileId = ResolveRiverbedTileId(settings, riverCell);
                // 先保留真实河床，最终结冰时统一保存可恢复的底材和水量。
                flags = TerrainCellFlags.Walkable;
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
                ? airHumidity.GetOceanInitialLiquidDepth(worldX, worldY)
                : river ? (float)riverCell.Depth : 0f;
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
            bool oilCell = false;
            // 油只覆盖原本干燥的陆地，不替换河湖、海水与岩浆，也不参加水的结冰规则。
            if (!lavaCell && !ocean && !river && initialLiquidDepth <= 0f && biome != SurfaceBiomeKind.Beach)
            {
                foreach (LavaBasin basin in petroleum)
                {
                    if (!basin.TrySample(request.Topology, worldX, worldY, out float depth, out _) || depth <= 0f)
                        continue;
                    oilCell = true;
                    initialLiquidDepth = depth;
                    snowDepth = 0f;
                    lavaShore = 0f;
                    break;
                }
            }
            int initialLiquidLevel = QuantizeGeneratedLiquidLevel(initialLiquidDepth);
            // 先生成真实底床再冻结液体，水被抽干或结冰后凹陷仍然保留。
            int naturalDepthLevels = ocean && !lavaCell
                ? OceanShoreDistanceBathymetryKernel.ResolveNaturalOceanBedDepthLevels(initialLiquidLevel)
                : ResolveNaturalBedDepth(initialLiquidLevel);
            bool hasNaturalWater = !lavaCell && !oilCell && initialLiquidLevel > 0;
            FrozenSurfaceWaterSource frozenWater = default;

            // 海水同样按零下基温结冰，岩浆不参加水体结冰规则。
            if (!lavaCell && !oilCell && initialLiquidLevel > 0 && temperatureCelsius < 0f)
            {
                frozenWater = new FrozenSurfaceWaterSource(new TerrainCell(groundTileId, 0, 0, biomeId,
                    navigationCost, flags), settings.IceTileId, ocean ? seaWater : dirtyWater, initialLiquidLevel);
                groundTileId = settings.IceTileId;
                initialLiquidLevel = 0;
                snowDepth = 0f;
                flags = TerrainCellFlags.Walkable;
                navigationCost = (short)Math.Min(short.MaxValue, settings.DefaultNavigationCost + 1);
            }
            if (snowDepth > 0f && climate.IsPolarBand)
                snowDepth = ResolvePolarSnowDepth(temperatureCelsius);

            // 降水只连续调稀密，底材能力和摄氏温度决定资格，避免气候标尺变动清空整片草地。
            bool snowSurface = snowDepth > 0f;
            double grassDensity = snowSurface
                ? settings.GrassDensity * settings.SnowGrassDensityMultiplier
                : settings.GrassDensity;
            grassDensity *= settings.ResolveGrassTemperatureMultiplier(temperatureCelsius);
            double rainfallDensity = settings.GrassDryDensityMultiplier +
                (1d - settings.GrassDryDensityMultiplier) *
                Clamp01(precipitation / settings.GrassRainfallForFullDensity);
            bool grassClimateSuitable =
                temperatureCelsius >= settings.GrassMinimumCelsius &&
                temperatureCelsius <= settings.GrassMaximumCelsius &&
                height <= settings.GrassMaximumHeight;
            bool grass = (flags & TerrainCellFlags.Walkable) != 0 &&
                         (groundTileId == settings.GroundTileId || settings.IsNaturalPlantableGround(groundTileId)) &&
                         initialLiquidLevel == 0 &&
                         grassClimateSuitable &&
                         Hash01(request.WorldSeed, worldX, worldY, 0x165667b1u) <
                         grassDensity * rainfallDensity * (0.55d + moisture * 0.75d);
            return new SurfaceCellOutput
            {
                Cell = new TerrainCell(groundTileId, 0, 0, biomeId,
                    navigationCost, flags),
                LiquidTypeIndex = initialLiquidLevel > 0
                    ? lavaCell ? lava : oilCell ? crudeOil : ocean ? seaWater : dirtyWater : 0,
                LiquidLevel = initialLiquidLevel,
                NaturalDepthLevels = naturalDepthLevels,
                FrozenWater = frozenWater,
                SnowDepth = snowDepth,
                Grass = grass ? GrassPresent : GrassEmpty,
                Height = (float)height,
                Temperature = (float)temperature,
                TemperatureCelsius = temperatureCelsius,
                Precipitation = (float)precipitation,
                WindX = (float)windX,
                WindY = (float)windY,
                HasOceanCurrent = ocean && hasNaturalWater,
                OceanCurrent = ocean && hasNaturalWater
                    ? airHumidity.GetOceanCurrent(worldX, worldY, windX, windY) : default,
                Moisture = (float)moisture,
                Mountain = mountain ? 1f : 0f,
                RiverFlow = river ? (float)riverCell.Flow : 0f,
                RiverFlowX = river ? (float)riverCell.FlowDirectionX : 0f,
                RiverFlowY = river ? (float)riverCell.FlowDirectionY : 0f,
                Estuary = ocean && hasNaturalWater && riverMap != null
                    ? riverMap.GetEstuary(worldX, worldY) : default,
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
    }
}
