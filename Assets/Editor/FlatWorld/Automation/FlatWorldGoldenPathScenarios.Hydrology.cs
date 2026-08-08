using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Tilemaps;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

namespace FlatWorld.Automation
{
    /// <summary>Validates climate, hydrology, biome and terrain from the headless model only.</summary>
    internal static partial class FlatWorldGoldenPathScenarios
    {
        private static readonly HashSet<RuntimeWorldAddress> HydrologyValidatedChunks = new();
        private static bool _hydrologyClimateObserved;
        private static bool _hydrologyRainShadowObserved;
        private static bool _hydrologyWindObserved;
        private static bool _hydrologyWaterObserved;
        private static bool _hydrologyRiverObserved;
        private static bool _hydrologyRiverFlowObserved;
        private static bool _hydrologyWaterKindObserved;
        private static bool _hydrologyLakeObserved;
        private static bool _hydrologyLakeSurfaceObserved;
        private static bool _hydrologyFloodplainObserved;
        private static bool _hydrologyFloodplainPresentationObserved;
        private static bool _hydrologyRiverPresentationObserved;
        private static bool _hydrologyGrassObserved;
        private static bool _hydrologyGrassPresentationObserved;
        private static bool _hydrologyMountainObserved;
        private static bool _biomeElevationRulesObserved;
        private static bool _hydrologyScenarioCompleted;

        private static void ResetHydrologyScenario()
        {
            HydrologyValidatedChunks.Clear();
            _hydrologyClimateObserved = false;
            _hydrologyRainShadowObserved = false;
            _hydrologyWindObserved = false;
            _hydrologyWaterObserved = false;
            _hydrologyRiverObserved = false;
            _hydrologyRiverFlowObserved = false;
            _hydrologyWaterKindObserved = false;
            _hydrologyLakeObserved = false;
            _hydrologyLakeSurfaceObserved = false;
            _hydrologyFloodplainObserved = false;
            _hydrologyFloodplainPresentationObserved = false;
            _hydrologyRiverPresentationObserved = false;
            _hydrologyGrassObserved = false;
            _hydrologyGrassPresentationObserved = false;
            _hydrologyMountainObserved = false;
            _biomeElevationRulesObserved = false;
            _hydrologyScenarioCompleted = false;
        }

        private static void BeginHydrologyScenario(FlatWorldGoldenPathScenarioContext context)
        {
            ChunkMgr manager = ChunkMgr.Instance;
            if (manager?.WorldRuntime == null || manager.Chunks.Count == 0)
                throw new InvalidOperationException("水文黄金路径开始时没有已提交模型区块。");
            foreach (ChunkRuntime chunk in manager.Chunks.Values)
            {
                if (chunk.DataStatus == ChunkDataStatus.Ready &&
                    chunk.SimulationStatus == ChunkSimulationStatus.Active)
                    ValidateHydrologyChunk(chunk);
            }
            if (!_hydrologyClimateObserved)
                throw new InvalidOperationException("初始模型区块没有完整气候层。");
            Debug.Log($"[GoldenPath][Hydrology] 已验证 {HydrologyValidatedChunks.Count} 个模型区块。");
        }

        private static void VerifyHydrologyAtChunkReady(FlatWorldGoldenPathScenarioContext context)
        {
            ChunkMgr manager = ChunkMgr.Instance;
            RuntimeWorldAddress address = manager.ResolveWorldAddress(context.ExpectedChunk);
            if (!manager.TryGetChunkRuntime(address, out ChunkRuntime chunk) ||
                chunk.DataStatus != ChunkDataStatus.Ready)
                throw new InvalidOperationException($"水文黄金路径找不到模型区块：{address}");
            ValidateHydrologyChunk(chunk);
            _hydrologyScenarioCompleted = HydrologyValidatedChunks.Count >= 2 &&
                                          _hydrologyClimateObserved &&
                                          _hydrologyRainShadowObserved &&
                                          _hydrologyWindObserved &&
                                          _hydrologyWaterObserved &&
                                          _hydrologyRiverObserved &&
                                          _hydrologyRiverFlowObserved &&
                                          _hydrologyWaterKindObserved &&
                                          _hydrologyRiverPresentationObserved &&
                                          _hydrologyGrassObserved &&
                                          _hydrologyGrassPresentationObserved &&
                                          _biomeElevationRulesObserved;
        }

        private static void ValidateHydrologyChunk(ChunkRuntime chunk)
        {
            ChunkTerrainData terrain = chunk?.Terrain;
            if (terrain == null)
                throw new InvalidOperationException($"Chunk {chunk?.Address} 缺少地形模型。");
            string[] required =
            {
                "height", "temperature", "temperature.celsius", "basePrecipitation",
                "precipitation", "windX", "windY",
                "moisture", "mountain", "riverDepth", "riverFlow", "riverFloodplain",
                "riverSurfaceLevel", "riverKind", "grass"
            };
            for (int i = 0; i < required.Length; i++)
            {
                if (!terrain.TryCopyEnvironmentLayer(required[i], out float[] values) ||
                    values.Length < terrain.CellCount)
                    throw new InvalidOperationException($"Chunk {chunk.Address} 缺少环境层 {required[i]}。");
            }

            Tilemap groundTilemap = null;
            Tilemap grassTilemap = null;
            ChunkMgr manager = ChunkMgr.Instance;
            ChunkGenerationSettingsSnapshot settings = manager?.ActiveGenerationProfile?.Settings;
            if (settings == null)
                throw new InvalidOperationException("水文黄金路径缺少活动地表生成配置。");
            if (manager != null && manager.TryGetRuntimeChunkView(chunk.Address, out ChunkView view))
            {
                groundTilemap = view.transform.Find("Ground")?.GetComponent<Tilemap>();
                grassTilemap = view.transform.Find("Grass")?.GetComponent<Tilemap>();
            }
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                TerrainCell cell = terrain.GetCell(x, y);
                if (!terrain.TryGetEnvironmentValue("height", x, y, out float height) ||
                    !terrain.TryGetEnvironmentValue("temperature", x, y, out float temperature) ||
                    !terrain.TryGetEnvironmentValue(
                        "temperature.celsius", x, y, out float temperatureCelsius) ||
                    !terrain.TryGetEnvironmentValue(
                        "basePrecipitation", x, y, out float basePrecipitation) ||
                    !terrain.TryGetEnvironmentValue("precipitation", x, y, out float precipitation) ||
                    !terrain.TryGetEnvironmentValue("windX", x, y, out float windX) ||
                    !terrain.TryGetEnvironmentValue("windY", x, y, out float windY) ||
                    !terrain.TryGetEnvironmentValue("moisture", x, y, out float moisture) ||
                    !terrain.TryGetEnvironmentValue("mountain", x, y, out float mountain) ||
                    !terrain.TryGetEnvironmentValue("riverDepth", x, y, out float riverDepth) ||
                    float.IsNaN(height) || float.IsNaN(temperature) ||
                    float.IsNaN(temperatureCelsius) ||
                    float.IsNaN(basePrecipitation) || float.IsNaN(precipitation) ||
                    float.IsNaN(windX) || float.IsNaN(windY) || float.IsNaN(moisture) ||
                    float.IsNaN(mountain))
                    throw new InvalidOperationException($"Chunk {chunk.Address} 环境值无效：{x},{y}。");
                if (height < 0f || height > 1f || temperature < 0f || temperature > 1f ||
                    basePrecipitation < 0f || basePrecipitation > 1f ||
                    precipitation < 0f || precipitation > 1f || moisture < 0f || moisture > 1f ||
                    mountain < 0f || mountain > 1f)
                    throw new InvalidOperationException($"Chunk {chunk.Address} 环境值越界：{x},{y}。");
                float windLength = Mathf.Sqrt(windX * windX + windY * windY);
                if (Mathf.Abs(windLength - 1f) > 0.001f)
                    throw new InvalidOperationException(
                        $"Chunk {chunk.Address} 风向不是单位向量：{x},{y}, length={windLength}。");
                _hydrologyWindObserved = true;
                _hydrologyRainShadowObserved |=
                    Mathf.Abs(basePrecipitation - precipitation) > 0.000001f;
                if (settings.SurfaceClimateAlgorithm == SurfaceClimateAlgorithm.LegacyLand)
                {
                    float expectedCelsius = Mathf.Lerp(
                        (float)settings.TemperatureCelsiusMin,
                        (float)settings.TemperatureCelsiusMax,
                        temperature);
                    if (Mathf.Abs(temperatureCelsius - expectedCelsius) > 0.0001f)
                        throw new InvalidOperationException(
                            $"Chunk {chunk.Address} 旧版温度摄氏映射不一致：{x},{y}。");
                }
                bool water = (cell.Flags & TerrainCellFlags.Water) != 0;
                _hydrologyWaterObserved |= water;
                if (cell.BiomeId is < 0 or > 7)
                    throw new InvalidOperationException(
                        $"Chunk {chunk.Address} 群系编号越界：{x},{y}, biome={cell.BiomeId}。");
                _biomeElevationRulesObserved = true;
                SurfaceBiomeKind expectedBiome = SurfaceBiomeClassifier.Resolve(
                    settings,
                    height,
                    temperature,
                    precipitation,
                    moisture,
                    riverDepth > 0f);
                if (cell.BiomeId != (int)expectedBiome)
                    throw new InvalidOperationException(
                        $"Chunk {chunk.Address} 群系顺序判定不一致：{x},{y}, " +
                        $"expected={expectedBiome}, actual={cell.BiomeId}。");

                bool expectedMountain = !water && height + 0.0001f >= settings.MountainLevel;
                if ((mountain > 0.5f) != expectedMountain)
                    throw new InvalidOperationException(
                        $"Chunk {chunk.Address} 山地高度分类不一致：{x},{y}, height={height}, mountain={mountain}。");
                if (expectedMountain)
                {
                    bool structureOverride = cell.GroundTileId == settings.StructureGroundTileId;
                    if (cell.GroundTileId != settings.StoneTileId && !structureOverride)
                        throw new InvalidOperationException(
                            $"Chunk {chunk.Address} 山地未使用石头地面：{x},{y}, tile={cell.GroundTileId}。");
                    _hydrologyMountainObserved |= cell.GroundTileId == settings.StoneTileId;
                }

                Vector3Int localCell = new(x, y, 0);
                if (riverDepth > 0f)
                {
                    _hydrologyRiverObserved = true;
                    if (!terrain.TryGetEnvironmentValue("riverFlow", x, y, out float riverFlow) ||
                        riverFlow <= 0f)
                    {
                        throw new InvalidOperationException(
                            $"Chunk {chunk.Address} 河流格缺少高度汇流值：{x},{y}。");
                    }
                    _hydrologyRiverFlowObserved = true;
                    if (!terrain.TryGetEnvironmentValue("riverKind", x, y, out float riverKind) ||
                        (Mathf.Abs(riverKind - 1f) > 0.001f &&
                         Mathf.Abs(riverKind - 2f) > 0.001f))
                    {
                        throw new InvalidOperationException(
                            $"Chunk {chunk.Address} 淡水格缺少有效 riverKind：{x},{y}。 ");
                    }
                    _hydrologyWaterKindObserved = true;
                    if (Mathf.Abs(riverKind - 2f) <= 0.001f)
                    {
                        _hydrologyLakeObserved = true;
                        if (!terrain.TryGetEnvironmentValue(
                                "riverSurfaceLevel", x, y, out float surfaceLevel) ||
                            surfaceLevel <= 0f || surfaceLevel + 0.0001f < height)
                        {
                            throw new InvalidOperationException(
                                $"Chunk {chunk.Address} 湖泊格缺少有效水面高度：{x},{y}。 ");
                        }
                        _hydrologyLakeSurfaceObserved = true;
                    }
                    _hydrologyRiverPresentationObserved |=
                        groundTilemap != null && groundTilemap.GetTile(localCell) != null;
                }

                if (terrain.TryGetEnvironmentValue("riverFloodplain", x, y,
                        out float floodplain) && floodplain > 0f &&
                    (cell.Flags & TerrainCellFlags.Water) == 0)
                {
                    _hydrologyFloodplainObserved = true;
                    _hydrologyFloodplainPresentationObserved |=
                        groundTilemap != null && groundTilemap.GetTile(localCell) != null;
                }

                if (terrain.GetGrass(x, y) == 2)
                {
                    _hydrologyGrassObserved = true;
                    _hydrologyGrassPresentationObserved |=
                        grassTilemap != null && grassTilemap.GetTile(localCell) != null;
                }
            }
            _hydrologyClimateObserved = true;
            HydrologyValidatedChunks.Add(chunk.Address);
        }

        private static void AssertHydrologyScenarioCompleted()
        {
            if (!_hydrologyScenarioCompleted)
                throw new InvalidOperationException(
                    $"完整移动前未完成模型水文验证：chunks={HydrologyValidatedChunks.Count}, " +
                    $"climate={_hydrologyClimateObserved}, orographic={_hydrologyRainShadowObserved}, " +
                    $"wind={_hydrologyWindObserved}, " +
                    $"water={_hydrologyWaterObserved}, river={_hydrologyRiverObserved}, " +
                    $"riverFlow={_hydrologyRiverFlowObserved}, " +
                    $"riverKind={_hydrologyWaterKindObserved}, lake={_hydrologyLakeObserved}, " +
                    $"lakeSurface={_hydrologyLakeSurfaceObserved}, " +
                    $"floodplain={_hydrologyFloodplainObserved}, " +
                    $"floodplainView={_hydrologyFloodplainPresentationObserved}, " +
                    $"riverView={_hydrologyRiverPresentationObserved}, grass={_hydrologyGrassObserved}, " +
                    $"grassView={_hydrologyGrassPresentationObserved}, " +
                    $"mountain={_hydrologyMountainObserved}, biome={_biomeElevationRulesObserved}。");
        }

        private static void CleanupHydrologyScenario() => HydrologyValidatedChunks.Clear();
    }
}
