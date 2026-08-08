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
        private static bool _hydrologyWaterObserved;
        private static bool _hydrologyRiverObserved;
        private static bool _hydrologyRiverFlowObserved;
        private static bool _hydrologyFloodplainObserved;
        private static bool _hydrologyFloodplainPresentationObserved;
        private static bool _hydrologyRiverPresentationObserved;
        private static bool _hydrologyGrassObserved;
        private static bool _hydrologyGrassPresentationObserved;
        private static bool _biomeElevationRulesObserved;
        private static bool _hydrologyScenarioCompleted;

        private static void ResetHydrologyScenario()
        {
            HydrologyValidatedChunks.Clear();
            _hydrologyClimateObserved = false;
            _hydrologyRainShadowObserved = false;
            _hydrologyWaterObserved = false;
            _hydrologyRiverObserved = false;
            _hydrologyRiverFlowObserved = false;
            _hydrologyFloodplainObserved = false;
            _hydrologyFloodplainPresentationObserved = false;
            _hydrologyRiverPresentationObserved = false;
            _hydrologyGrassObserved = false;
            _hydrologyGrassPresentationObserved = false;
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
                                          _hydrologyWaterObserved &&
                                          _hydrologyRiverObserved &&
                                          _hydrologyRiverFlowObserved &&
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
                "height", "temperature", "temperature.celsius", "precipitation",
                "moisture", "riverDepth", "riverFlow", "riverFloodplain", "grass"
            };
            for (int i = 0; i < required.Length; i++)
            {
                if (!terrain.TryCopyEnvironmentLayer(required[i], out float[] values) ||
                    values.Length < terrain.CellCount)
                    throw new InvalidOperationException($"Chunk {chunk.Address} 缺少环境层 {required[i]}。");
            }

            float minPrecipitation = 1f;
            float maxPrecipitation = 0f;
            Tilemap groundTilemap = null;
            Tilemap grassTilemap = null;
            ChunkMgr manager = ChunkMgr.Instance;
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
                    !terrain.TryGetEnvironmentValue("precipitation", x, y, out float precipitation) ||
                    !terrain.TryGetEnvironmentValue("moisture", x, y, out float moisture) ||
                    float.IsNaN(height) || float.IsNaN(temperature) || float.IsNaN(precipitation) ||
                    float.IsNaN(moisture))
                    throw new InvalidOperationException($"Chunk {chunk.Address} 环境值无效：{x},{y}。");
                if (height < 0f || height > 1f || temperature < 0f || temperature > 1f ||
                    precipitation < 0f || precipitation > 1f || moisture < 0f || moisture > 1f)
                    throw new InvalidOperationException($"Chunk {chunk.Address} 环境值越界：{x},{y}。");
                minPrecipitation = Mathf.Min(minPrecipitation, precipitation);
                maxPrecipitation = Mathf.Max(maxPrecipitation, precipitation);
                _hydrologyWaterObserved |= (cell.Flags & TerrainCellFlags.Water) != 0;
                _biomeElevationRulesObserved |= cell.BiomeId is >= 0 and <= 6;

                Vector3Int localCell = new(x, y, 0);
                if (terrain.TryGetEnvironmentValue("riverDepth", x, y, out float riverDepth) &&
                    riverDepth > 0f)
                {
                    _hydrologyRiverObserved = true;
                    if (!terrain.TryGetEnvironmentValue("riverFlow", x, y, out float riverFlow) ||
                        riverFlow <= 0f)
                    {
                        throw new InvalidOperationException(
                            $"Chunk {chunk.Address} 河流格缺少高度汇流值：{x},{y}。");
                    }
                    _hydrologyRiverFlowObserved = true;
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
            _hydrologyRainShadowObserved |= maxPrecipitation - minPrecipitation > 0.0001f;
            HydrologyValidatedChunks.Add(chunk.Address);
        }

        private static void AssertHydrologyScenarioCompleted()
        {
            if (!_hydrologyScenarioCompleted)
                throw new InvalidOperationException(
                    $"完整移动前未完成模型水文验证：chunks={HydrologyValidatedChunks.Count}, " +
                    $"climate={_hydrologyClimateObserved}, variation={_hydrologyRainShadowObserved}, " +
                    $"water={_hydrologyWaterObserved}, river={_hydrologyRiverObserved}, " +
                    $"riverFlow={_hydrologyRiverFlowObserved}, " +
                    $"floodplain={_hydrologyFloodplainObserved}, " +
                    $"floodplainView={_hydrologyFloodplainPresentationObserved}, " +
                    $"riverView={_hydrologyRiverPresentationObserved}, grass={_hydrologyGrassObserved}, " +
                    $"grassView={_hydrologyGrassPresentationObserved}, " +
                    $"biome={_biomeElevationRulesObserved}。");
        }

        private static void CleanupHydrologyScenario() => HydrologyValidatedChunks.Clear();
    }
}
