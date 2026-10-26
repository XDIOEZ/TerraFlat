using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 石油地形正式生成采样

    // 只采集正式生成输出，油量、底床、结冰和覆盖优先级由 Lua 断言。
    private static JObject WorldPetroleumLandBasins(JObject args)
    {
        string scenario = Text(args, "scenario", "land");
        bool wrapped = scenario == "wrapped";
        var topology = wrapped ? new ChunkGenerationTopologySnapshot(new Int2(0, 0), new Int2(64, 64)) : default;
        var numbers = new Dictionary<string, double>
        {
            ["terrain.seaLevel"] = scenario == "ocean" ? 1d : 0d,
            ["terrain.mountainLevel"] = 0d, ["terrain.stoneTileId"] = 3d,
            ["river.enabled"] = 0d, ["lake.large.enabled"] = 0d, ["structure.enabled"] = 0d,
            ["lake.lava.enabled"] = scenario == "lava" ? 1d : 0d,
            ["lake.lava.regionSize"] = 64d, ["lake.lava.chance"] = 1d,
            ["lake.lava.minRadius"] = 8d, ["lake.lava.maxRadius"] = 8d,
            ["lake.lava.rareMaxRadius"] = 8d, ["lake.lava.minimumHeight"] = 0d,
            ["lake.oil.enabled"] = scenario == "disabled" ? 0d : 1d,
            ["lake.oil.regionSize"] = 64d, ["lake.oil.chance"] = 1d,
            ["lake.oil.areaRatio"] = .2d,
            ["climate.temperature.celsiusMin"] = scenario == "cold" ? -40d : 30d,
            ["climate.temperature.celsiusMax"] = scenario == "cold" ? -40d : 30d
        };
        var texts = new Dictionary<string, string> { ["climate.algorithm"] = "simple" };
        var output = new Dictionary<(int, int), (int Liquid, int Level, int Ground, float Celsius, int Bed)>();
        void Capture(int size, int origin, bool oil, Dictionary<(int, int), (int, int, int, float, int)> target)
        {
            var selected = new Dictionary<string, double>(numbers) { ["lake.oil.enabled"] = oil ? 1d : 0d };
            var profile = new ChunkGenerationProfileSnapshot("probe:petroleum", 81, size, size, selected, texts);
            var generator = new DeterministicChunkGenerator();
            using var world = new WorldRuntime("probe:petroleum", 1);
            for (int originY = origin; originY < origin + 64; originY += size)
            for (int originX = origin; originX < origin + 64; originX += size)
            {
                var address = new FlatWorld.WorldModel.WorldAddress("surface", new Int2(originX, originY));
                ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1729, profile, topology);
                using var result = generator.Generate(request, CancellationToken.None);
                if (!world.TryCommit(result, out string error)) throw new InvalidOperationException(error);
                if (!world.TryGetChunk(address, out ChunkRuntime chunk))
                    throw new InvalidOperationException("石油采样区块提交后不可用。");
                ChunkTerrainData terrain = chunk.Terrain;
                for (int y = 0; y < terrain.Height; y++)
                for (int x = 0; x < terrain.Width; x++)
                {
                    terrain.TryGetEnvironmentValue("temperature.celsius", x, y, out float temperature);
                    terrain.TryGetEnvironmentValue(TerrainSurfaceHeightLayer.NaturalDepthLevelsLayerId,
                        x, y, out float bed);
                    target[(topology.NormalizeX(request.Address.ChunkOrigin.X + x),
                        topology.NormalizeY(request.Address.ChunkOrigin.Y + y))] =
                        (terrain.GetLiquidTypeIndex(x, y), terrain.GetLiquidLevel(x, y),
                            terrain.GetCell(x, y).GroundTileId, temperature, (int)bed);
                }
            }
        }
        int start = wrapped ? 0 : -64;
        bool enabled = scenario != "disabled";
        Capture(16, start, enabled, output);
        var repeated = new Dictionary<(int, int), (int, int, int, float, int)>();
        Capture(32, wrapped ? 64 : start, enabled, repeated);
        var baseline = new Dictionary<(int, int), (int, int, int, float, int)>();
        Capture(16, start, false, baseline);
        int fullAreaOilCells = 0;
        if (scenario == "land")
        {
            numbers["lake.oil.areaRatio"] = 1d;
            var fullArea = new Dictionary<(int, int), (int, int, int, float, int)>();
            Capture(32, start, true, fullArea);
            foreach (var value in fullArea.Values)
                if (value.Item1 == LiquidTypeCatalog.BuiltIn.GetIndex(LiquidTypeCatalog.CrudeOilId)) fullAreaOilCells++;
            numbers["lake.oil.areaRatio"] = .2d;
        }
        int mismatches = 0, oilCells = 0, baselineWetCells = 0, changedWetCells = 0;
        int oilIndex = LiquidTypeCatalog.BuiltIn.GetIndex(LiquidTypeCatalog.CrudeOilId);
        var rows = new JArray();
        foreach (var cell in output)
        {
            if (!repeated.TryGetValue(cell.Key, out var other) || !other.Equals(cell.Value)) mismatches++;
            var before = baseline[cell.Key];
            if (before.Item2 > 0)
            {
                baselineWetCells++;
                if (!before.Equals(cell.Value)) changedWetCells++;
            }
            if (cell.Value.Liquid != oilIndex) continue;
            oilCells++;
            rows.Add(new JObject { ["level"] = cell.Value.Level, ["ground"] = cell.Value.Ground,
                ["previousGround"] = before.Item3, ["previousLevel"] = before.Item2,
                ["celsius"] = cell.Value.Celsius, ["bed"] = cell.Value.Bed });
        }
        var settings = new ChunkGenerationProfileSnapshot("probe:petroleum", 81, 16, 16, numbers, texts).Settings;
        return new JObject { ["areaRatio"] = settings.OilLakeAreaRatio, ["cellCount"] = output.Count,
            ["fullAreaOilCells"] = fullAreaOilCells,
            ["oilCells"] = oilCells, ["partitionMismatches"] = mismatches,
            ["baselineWetCells"] = baselineWetCells, ["changedWetCells"] = changedWetCells,
            ["samples"] = rows };
    }

    #endregion
}
