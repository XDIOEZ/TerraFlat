using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 地表高度与阴影采样

    private static void RegisterTerrainHeightShadowOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("rendering/terrain_height", ServiceTerrainHeight);
        operations.Add("rendering/terrain_height_save", ServiceTerrainHeightSave);
        operations.Add("rendering/terrain_height_neighbours", ServiceTerrainHeightNeighbours);
        operations.Add("terrain/natural_waterbed", ServiceNaturalWaterbed);
    }

    // 夹具只构造权威地形并采样正式高度规则，所有预期由 Lua 判断。
    private static JObject ServiceTerrainHeight(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(2, 1, LiquidTypeCatalog.BuiltIn);
        using ChunkTerrainData terrain = buffer.Seal();
        float height = (float)Number(args, "height", .55);
        terrain.SetCell(0, 0, new TerrainCell(1, 0, 0, 0, 1, TerrainCellFlags.Walkable));
        bool wall = Flag(args, "wall", false);
        terrain.SetCell(1, 0, new TerrainCell(4, 0, wall ? 2 : 0, 0, 1,
            wall ? TerrainCellFlags.Blocking : TerrainCellFlags.Walkable));
        if (!Flag(args, "missingHeight", false))
        {
            terrain.SetEnvironmentValue("height", 0, 0, height);
            terrain.SetEnvironmentValue("height", 1, 0, height);
        }
        TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, 0, 0, (int)Number(args, "depth", 0));
        TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, 1, 0, (int)Number(args, "neighbourDepth", 0));
        TerrainSurfaceHeightLayer.SetCoverLevels(terrain, 0, 0, (int)Number(args, "covers", 0));
        terrain.SetEnvironmentValue(TerrainSurfaceHeightLayer.NaturalDepthLevelsLayerId, 0, 0,
            (float)Number(args, "naturalDepth", 0));
        if (Flag(args, "liquid", false))
            terrain.SetLiquid(0, 0, LiquidTypeCatalog.BuiltIn.GetIndex(LiquidTypeCatalog.DirtyWaterId), 5);
        if (Flag(args, "support", false)) TerrainSupportLayer.Set(terrain, 0, 0, 23, 1);
        bool useGeneratedHeight = Flag(args, "surface", true);
        TerrainSurfaceHeightLayer.TryGetSurface(terrain, 0, 0, useGeneratedHeight, out TerrainSurfaceHeight current,
            (int)Number(args, "minimumDepth", 0));
        TerrainSurfaceHeightLayer.TryGetSurface(terrain, 1, 0, useGeneratedHeight, out TerrainSurfaceHeight neighbour);
        bool outside = TerrainSurfaceHeightLayer.TryGetSurface(terrain, -1, 0, useGeneratedHeight, out _);
        return new JObject
        {
            ["base"] = TerrainSurfaceHeightLayer.GetBaseLevel(terrain, 0, 0, useGeneratedHeight),
            ["level"] = current.Level, ["neighbour"] = neighbour.Level, ["top"] = neighbour.TopLevel,
            ["inner"] = TerrainSurfaceHeightLayer.GetShadowDelta(current, neighbour),
            ["outer"] = TerrainSurfaceHeightLayer.GetShadowDelta(neighbour, current),
            ["receiver"] = current.ReceivesShadow, ["neighbourReceiver"] = neighbour.ReceivesShadow,
            ["outside"] = outside, ["waterLevel"] = terrain.GetLiquidLevel(0, 0)
        };
    }

    // 正式生成与液体改写都在独立世界执行，Lua 检查底床不随抽水、倒水改变。
    private static JObject ServiceNaturalWaterbed(JObject args)
    {
        string scenario = Text(args, "scenario", "freshwater");
        bool cave = scenario == "cave";
        bool ocean = scenario == "ocean" || scenario == "frozen_ocean";
        bool frozen = scenario == "frozen_ocean";
        var numbers = SmallGenerationParameters();
        numbers["terrain.seaLevel"] = ocean ? 1d : 0d;
        numbers["terrain.beachLevel"] = ocean ? 1d : .01d;
        numbers["climate.polarBand.enabled"] = 0d;
        numbers["climate.temperature.altitudeCoolingStrength"] = 0d;
        numbers["climate.temperature.celsiusMin"] = frozen ? -20d : 20d;
        numbers["climate.temperature.celsiusMax"] = frozen ? -20d : 20d;
        numbers["climate.distance.minimumRainfall"] = 1d;
        numbers["climate.distance.maximumRainfall"] = 1d;
        numbers["river.enabled"] = ocean || cave ? 0d : 1d;
        numbers["river.startFlow"] = .0001d;
        numbers["river.tributaryStartFlow"] = .0001d;
        numbers["river.floodplainStartFlow"] = .0001d;
        numbers["lake.large.enabled"] = ocean || cave ? 0d : 1d;
        numbers["lake.large.regionSize"] = 32d;
        numbers["lake.large.chance"] = 1d;
        numbers["lake.large.minRadius"] = 8d;
        numbers["lake.large.maxRadius"] = 9d;
        numbers["lake.large.islandChance"] = 0d;
        numbers["cave.river.enabled"] = 1d;
        numbers["cave.river.connectionChance"] = 1d;
        numbers["cave.network.extraConnectionChance"] = 1d;
        var texts = new Dictionary<string, string>
        {
            ["terrain.mode"] = cave ? "cave" : "surface", ["climate.algorithm"] = "legacy"
        };
        int size = cave ? 64 : 32;
        var profile = new ChunkGenerationProfileSnapshot("probe:natural-waterbed", 1, size, size, numbers, texts);
        using var world = new WorldRuntime("probe:natural-waterbed", 1);
        var address = new FlatWorld.WorldModel.WorldAddress(cave ? "cave" : "surface",
            cave ? new Int2(-32, -32) : default);
        var generator = new DeterministicChunkGenerator();
        try
        {
            using ChunkGenerationResult generated = generator.Generate(
                world.BeginChunkGeneration(address, 17413, profile), System.Threading.CancellationToken.None);
            if (!world.TryCommit(generated, out string reason)) throw new InvalidOperationException(reason);
            world.TryGetChunkTerrain(address, out ChunkTerrainData terrain);
            var samples = new JArray();
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                int liquid = terrain.GetLiquidLevel(x, y);
                int naturalDepth = TerrainSurfaceHeightLayer.GetNaturalDepthLevels(terrain, x, y);
                TerrainSurfaceHeightLayer.TryGetSurface(terrain, x, y, !cave, out var initial);
                int baseline = TerrainSurfaceHeightLayer.GetBaseLevel(terrain, x, y, !cave);
                terrain.TryGetEnvironmentValue("height", x, y, out float height);
                terrain.TryGetEnvironmentValue("riverKind", x, y, out float kind);
                terrain.SetLiquid(x, y, 0, 0);
                TerrainSurfaceHeightLayer.TryGetSurface(terrain, x, y, !cave, out var drained);
                terrain.SetLiquid(x, y, terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 5);
                TerrainSurfaceHeightLayer.TryGetSurface(terrain, x, y, !cave, out var poured);
                terrain.TryGetEnvironmentValue("height", x, y, out float afterHeight);
                samples.Add(new JObject { ["liquid"] = liquid, ["naturalDepth"] = naturalDepth,
                    ["base"] = baseline, ["initial"] = initial.Level, ["drained"] = drained.Level,
                    ["poured"] = poured.Level, ["afterDepth"] = TerrainSurfaceHeightLayer.GetNaturalDepthLevels(terrain, x, y),
                    ["height"] = height, ["afterHeight"] = afterHeight, ["kind"] = kind });
            }
            return new JObject { ["samples"] = samples };
        }
        finally { generator.BeginNewWorldHydrologyEpoch(1); }
    }

    private static JObject ServiceTerrainHeightSave(JObject args)
    {
        using var buffer = new ChunkTerrainBuffer(1, 1);
        using ChunkTerrainData terrain = buffer.Seal();
        terrain.SetCell(0, 0, new TerrainCell(31, 0, 0, 0, 1, TerrainCellFlags.Walkable));
        terrain.SetEnvironmentValue("height", 0, 0, .55f);
        terrain.SetEnvironmentValue(TerrainSurfaceHeightLayer.NaturalDepthLevelsLayerId, 0, 0,
            (float)Number(args, "naturalDepth", 0));
        GroundTileLayerSystem.WriteDepth(terrain, Vector2Int.zero, 5);
        TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, 0, 0, 6);
        GroundLayerCellSaveData snapshot = GroundTileLayerSystem.Capture(terrain, Vector2Int.zero);
        GroundLayerCellSaveData serialized = MemoryPackSerializer.Deserialize<GroundLayerCellSaveData>(
            MemoryPackSerializer.Serialize(snapshot));
        GroundTileLayerSystem.WriteDepth(terrain, Vector2Int.zero, 1);
        TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, 0, 0, 1);
        GroundTileLayerSystem.Restore(terrain, serialized);
        terrain.TryGetEnvironmentValue("height", 0, 0, out float rawHeight);
        TerrainSurfaceHeightLayer.TryGetSurface(terrain, 0, 0, true, out TerrainSurfaceHeight surface);
        bool negativeRejected = false;
        try { TerrainSurfaceHeightLayer.SetExcavatedLevels(terrain, 0, 0, -1); }
        catch (ArgumentOutOfRangeException) { negativeRejected = true; }
        return new JObject
        {
            ["resourceDepth"] = GroundTileLayerSystem.ReadDepth(terrain, Vector2Int.zero),
            ["surfaceDepth"] = TerrainSurfaceHeightLayer.GetExcavatedLevels(terrain, 0, 0),
            ["savedDepth"] = serialized.ExcavatedSurfaceLevels, ["level"] = surface.Level,
            ["naturalDepth"] = TerrainSurfaceHeightLayer.GetNaturalDepthLevels(terrain, 0, 0),
            ["rawHeight"] = rawHeight, ["negativeRejected"] = negativeRejected
        };
    }

    private static JObject ServiceTerrainHeightNeighbours(JObject args)
    {
        using var world = new WorldRuntime("probe:terrain_height", 1);
        var profile = new ChunkGenerationProfileSnapshot("probe:terrain_height", 1, 16, 16);
        Vector2Int origin = WorldTopologyRuntime.NormalizeCell(new Vector2Int(1000000, -1000000));
        Vector2Int right = WorldTopologyRuntime.NormalizeCell(origin + new Vector2Int(16, 0));
        var address = new FlatWorld.WorldModel.WorldAddress("probe:terrain_height", new Int2(origin.x, origin.y));
        var neighbourAddress = new FlatWorld.WorldModel.WorldAddress("probe:terrain_height", new Int2(right.x, right.y));
        ChunkTerrainData Commit(FlatWorld.WorldModel.WorldAddress target)
        {
            ChunkGenerationRequest request = world.BeginChunkGeneration(target, 1, profile);
            using var buffer = new ChunkTerrainBuffer(16, 16);
            using var result = new ChunkGenerationResult(request, buffer);
            if (!world.TryCommit(result, out string reason)) throw new InvalidOperationException(reason);
            world.TryGetChunkTerrain(target, out ChunkTerrainData terrain);
            terrain.SetCell(0, 0, new TerrainCell(1, 0, 0, 0, 1, TerrainCellFlags.Walkable));
            return terrain;
        }
        Commit(address);
        bool missing = TerrainHeightShadowManager.TryGetSurfaceHeight(world, address, new Int2(16, 0), out _);
        ChunkTerrainData neighbour = Commit(neighbourAddress);
        bool available = TerrainHeightShadowManager.TryGetSurfaceHeight(world, address, new Int2(16, 0), out var first);
        TerrainSurfaceHeightLayer.SetExcavatedLevels(neighbour, 0, 0, 3);
        bool changed = TerrainHeightShadowManager.TryGetSurfaceHeight(world, address, new Int2(16, 0), out var lowered);
        bool evicted = world.EvictChunk(neighbourAddress);
        bool afterEviction = TerrainHeightShadowManager.TryGetSurfaceHeight(world, address, new Int2(16, 0), out _);
        Commit(neighbourAddress);
        bool reloaded = TerrainHeightShadowManager.TryGetSurfaceHeight(world, address, new Int2(16, 0), out var restored);
        return new JObject
        {
            ["missing"] = missing, ["available"] = available, ["initial"] = first.Level,
            ["changed"] = changed, ["lowered"] = lowered.Level, ["evicted"] = evicted,
            ["afterEviction"] = afterEviction, ["reloaded"] = reloaded, ["restored"] = restored.Level
        };
    }

    #endregion
}
