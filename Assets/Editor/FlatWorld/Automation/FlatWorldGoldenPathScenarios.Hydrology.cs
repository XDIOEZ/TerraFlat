using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Automation
{
    /// <summary>在真实世界中验证静态风雨层、水文查询和 Chunk 流送使用同一权威结果。</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private const float ClimateTolerance = 0.0001f;

        private static readonly HashSet<Vector2Int> HydrologyValidatedChunks = new();
        private static bool _hydrologyClimateObserved;
        private static bool _hydrologyRainShadowObserved;
        private static bool _hydrologyWaterObserved;
        private static bool _biomeElevationRulesObserved;
        private static bool _hydrologyScenarioCompleted;

        private static void ResetHydrologyScenario()
        {
            HydrologyValidatedChunks.Clear();
            _hydrologyClimateObserved = false;
            _hydrologyRainShadowObserved = false;
            _hydrologyWaterObserved = false;
            _biomeElevationRulesObserved = false;
            _hydrologyScenarioCompleted = false;
            ChunkGenerator_River.ClearHydrologyCache();
        }

        private static void BeginHydrologyScenario(FlatWorldGoldenPathScenarioContext context)
        {
            if (context.SaveDataManager?.SaveData == null)
                throw new InvalidOperationException("水文黄金路径找不到当前世界存档。");
            if (ChunkMgr.Instance == null || ChunkMgr.Instance.Chunk_Dic_Active_ByPos.Count == 0)
                throw new InvalidOperationException("水文黄金路径开始时没有 Ready Chunk。");

            foreach (KeyValuePair<Vector2Int, Chunk> entry in ChunkMgr.Instance.Chunk_Dic_Active_ByPos)
                ValidateHydrologyChunk(entry.Key, entry.Value, context);

            if (!_hydrologyClimateObserved)
                throw new InvalidOperationException("初始 Chunk 没有生成有效风雨高度层。");
            Debug.Log(
                $"[GoldenPath][Hydrology] 初始风雨层已验证，缓存 " +
                $"{ChunkGenerator_River.CompletedCachedRegionCount}/{ChunkGenerator_River.CachedRegionCount}。");
        }

        private static void VerifyHydrologyAtChunkReady(FlatWorldGoldenPathScenarioContext context)
        {
            if (ChunkMgr.Instance == null ||
                !ChunkMgr.Instance.TryGetActiveChunkByPos(context.ExpectedChunk, out Chunk chunk) ||
                chunk == null || !chunk.IsReady)
            {
                throw new InvalidOperationException($"水文黄金路径找不到 Ready Chunk：{context.ExpectedChunk}");
            }

            ValidateHydrologyChunk(context.ExpectedChunk, chunk, context);
            _hydrologyScenarioCompleted =
                HydrologyValidatedChunks.Count >= 2 &&
                _hydrologyClimateObserved &&
                _hydrologyRainShadowObserved &&
                _hydrologyWaterObserved &&
                _biomeElevationRulesObserved &&
                ChunkGenerator_River.CompletedCachedRegionCount > 0;
        }

        private static void ValidateHydrologyChunk(
            Vector2Int chunkCoordinate,
            Chunk chunk,
            FlatWorldGoldenPathScenarioContext context)
        {
            global::Map map = chunk?.Map;
            if (map?.Data == null || !map.Data.EnvironmentLayers.IsValidSize(map.Data.Width, map.Data.Height))
                throw new InvalidOperationException($"Chunk {chunkCoordinate} 缺少完整环境层。");

            ChunkGenerator_Land land = map.LandGenerator;
            ChunkGenerator_River river = map.GetGenerator<ChunkGenerator_River>();
            if (land == null || river == null)
                throw new InvalidOperationException($"Chunk {chunkCoordinate} 缺少地形或水文生成器。");

            if (!_biomeElevationRulesObserved)
                VerifyBiomeElevationRules(land);

            int baseSeed = context.SaveDataManager.SaveData.Seed;
            int worldSeed = DimensionManager.Instance != null
                ? DimensionManager.Instance.GetActiveGenerationSeed(baseSeed)
                : baseSeed;
            PlanetData planet = context.SaveDataManager.GetCurrentPlanetData();
            if (worldSeed == 0 || planet == null)
                throw new InvalidOperationException("水文黄金路径缺少有效世界种子或星球数据。");

            int[] sampleX = { 0, map.Data.Width / 2, map.Data.Width - 1 };
            int[] sampleY = { 0, map.Data.Height / 2, map.Data.Height - 1 };
            foreach (int localX in sampleX)
            {
                foreach (int localY in sampleY)
                {
                    Vector2Int world = map.Data.position + new Vector2Int(localX, localY);
                    ClimateSample climate = land.SampleClimateAtWorld(world, worldSeed, planet);
                    EnvironmentLayers layers = map.Data.EnvironmentLayers;
                    AssertClose(layers.Temperature[localX, localY], climate.Environment.Temperature, world, "温度");
                    AssertClose(layers.Precipitation[localX, localY], climate.Environment.Precipitation, world, "最终降雨");
                    AssertClose(layers.Height[localX, localY], climate.Environment.Height, world, "高度");
                    AssertClose(layers.WindX[localX, localY], climate.Wind.Direction.x, world, "风向 X");
                    AssertClose(layers.WindY[localX, localY], climate.Wind.Direction.y, world, "风向 Y");
                    if (!land.TryGetBiomeAtWorld(world, out BiomeData generatedBiome))
                        throw new InvalidOperationException($"{world} 缺少运行时群系结果。");
                    if (generatedBiome.BiomeId == "stone" && climate.Environment.Height < 0.72f)
                        throw new InvalidOperationException($"{world} 的石地生成在低海拔。");
                    if (generatedBiome.BiomeId == "desert" &&
                        (climate.Environment.Height >= 0.72f ||
                         climate.Environment.Precipitation > 0.28f))
                    {
                        throw new InvalidOperationException($"{world} 的沙漠不符合低雨、非山顶规则。");
                    }

                    Vector2 wind = layers.GetWind(localX, localY);
                    if (Mathf.Abs(wind.magnitude - 1f) > ClimateTolerance)
                        throw new InvalidOperationException($"{world} 的风向不是单位向量：{wind}");
                    _hydrologyClimateObserved = true;
                    _hydrologyRainShadowObserved |=
                        Mathf.Abs(climate.BasePrecipitation - climate.Environment.Precipitation) > ClimateTolerance;

                    bool firstQuery = river.TrySampleHydrologyCell(world, worldSeed, out HydrologyCellSample first);
                    bool repeatedQuery = river.TrySampleHydrologyCell(world, worldSeed, out HydrologyCellSample repeated);
                    if (firstQuery != repeatedQuery || first.WaterKind != repeated.WaterKind ||
                        Mathf.Abs(first.Flow - repeated.Flow) > ClimateTolerance ||
                        Mathf.Abs(first.Depth - repeated.Depth) > ClimateTolerance)
                    {
                        throw new InvalidOperationException($"{world} 的水文查询不确定。");
                    }

                    if (!firstQuery)
                        continue;
                    _hydrologyWaterObserved = true;
                    TileData top = map.Data.GetTopTile(world);
                    if (top is not TileData_Water)
                        throw new InvalidOperationException($"{world} 的权威水文结果未写入水体 Tile。");
                }
            }

            if (!_hydrologyWaterObserved)
            {
                for (int localY = 0; localY < map.Data.Height && !_hydrologyWaterObserved; localY++)
                {
                    for (int localX = 0; localX < map.Data.Width; localX++)
                    {
                        Vector2Int world = map.Data.position + new Vector2Int(localX, localY);
                        if (!river.TrySampleHydrologyCell(world, worldSeed, out _))
                            continue;
                        if (map.Data.GetTopTile(world) is not TileData_Water)
                            throw new InvalidOperationException($"{world} 的权威水文结果未写入水体 Tile。");
                        _hydrologyWaterObserved = true;
                        break;
                    }
                }
            }

            HydrologyValidatedChunks.Add(chunkCoordinate);
        }

        private static void AssertHydrologyScenarioCompleted()
        {
            if (!_hydrologyScenarioCompleted)
            {
                throw new InvalidOperationException(
                    $"完整移动前未完成水文验证：chunks={HydrologyValidatedChunks.Count}, " +
                    $"climate={_hydrologyClimateObserved}, rainShadow={_hydrologyRainShadowObserved}, " +
                    $"water={_hydrologyWaterObserved}, biomeRules={_biomeElevationRulesObserved}, " +
                    $"cache={ChunkGenerator_River.CompletedCachedRegionCount}。");
            }
        }

        private static void VerifyBiomeElevationRules(ChunkGenerator_Land land)
        {
            float[] temperatures = { 0.1f, 0.5f, 0.9f };
            float[] precipitationValues = { 0.1f, 0.5f, 0.9f };
            foreach (float temperature in temperatures)
            {
                foreach (float precipitation in precipitationValues)
                {
                    var mountain = new EnvironmentSample(temperature, 0f, precipitation, 0.8f);
                    if (!land.TryResolveBiome(mountain, out BiomeData mountainBiome) ||
                        mountainBiome.BiomeId != "stone")
                    {
                        throw new InvalidOperationException(
                            $"高地群系规则异常：T={temperature}, P={precipitation}, " +
                            $"biome={mountainBiome?.BiomeId ?? "null"}。");
                    }
                }

                var dryLowland = new EnvironmentSample(temperature, 0f, 0.1f, 0.6f);
                if (!land.TryResolveBiome(dryLowland, out BiomeData desertBiome) ||
                    desertBiome.BiomeId != "desert")
                {
                    throw new InvalidOperationException(
                        $"低雨群系规则异常：T={temperature}, biome={desertBiome?.BiomeId ?? "null"}。");
                }
            }

            _biomeElevationRulesObserved = true;
        }

        private static void CleanupHydrologyScenario()
        {
            HydrologyValidatedChunks.Clear();
        }

        private static void AssertClose(float actual, float expected, Vector2Int world, string channel)
        {
            if (float.IsNaN(actual) || float.IsInfinity(actual) ||
                Mathf.Abs(actual - expected) > ClimateTolerance)
            {
                throw new InvalidOperationException(
                    $"{world} 的{channel}与权威采样不一致：stored={actual}, expected={expected}。");
            }
        }
    }
}
