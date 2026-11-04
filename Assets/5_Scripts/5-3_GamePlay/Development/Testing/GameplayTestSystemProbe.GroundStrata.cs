using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using MemoryPack;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 三维地层规则采样
    // 高度表现夹具显式构造列坐标，不再写旧XY深度计数。
    private static void SetTerrainProbeExcavatedLevels(ChunkTerrainData terrain, int x, int y, int levels)
    {
        if (levels < 0) throw new ArgumentOutOfRangeException(nameof(levels));
        var column = terrain.CaptureGroundColumn(x, y);
        int covers = 0;
        foreach (GroundStratum layer in column.Layers) if (layer.PlayerPlaced) covers++;
        int top = TerrainSurfaceHeightLayer.GetBaseLevel(terrain, x, y, terrain.GenerationMode == ChunkGenerationMode.Surface) -
            TerrainSurfaceHeightLayer.GetNaturalDepthLevels(terrain, x, y) - levels + covers;
        column.BottomZ += top - column.TopZ;
        terrain.RestoreGroundColumn(x, y, column);
    }
    private static void SetTerrainProbeCoverLevels(ChunkTerrainData terrain, int x, int y, int count)
    {
        while (terrain.GetGroundStratum(x, y, terrain.GetTopGroundZ(x, y)).PlayerPlaced)
            if (!terrain.TryRemoveTopGroundStratum(x, y)) break;
        int tile = terrain.GetCell(x, y).GroundTileId;
        for (int i = 0; i < count; i++) terrain.PushGroundStratum(x, y, tile);
    }

    private static JObject TerrainGroundStrataRules(JObject args)
    {
        var texts = new Dictionary<string, string> { [GroundStrataCatalog.ParameterId] = "1,1,2,0;2,5,2,3;3,0,0,0;4,1,2,0" };
        var profile = new ChunkGenerationProfileSnapshot("strata.probe", 1, 2, 1, null, texts);
        using var buffer = new ChunkTerrainBuffer(2, 1);
        buffer.SetCell(0, 0, new TerrainCell(1, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        buffer.SetCell(1, 0, new TerrainCell(2, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        using var terrain = buffer.Seal();
        terrain.InitializeGroundStrata(profile.GroundStrata);
        int initial = terrain.CaptureGroundColumn(0, 0).Layers.Length;
        int initialZ = terrain.GetTopGroundZ(0, 0);
        terrain.SetGroundHarvestProgress(0, 0, .4f);
        terrain.SetGrass(0, 0, ChunkTerrainData.GrassPresent);
        var original = terrain.CaptureGroundColumn(0, 0);
        terrain.PushGroundStratum(0, 0, 4);
        terrain.PushGroundStratum(0, 0, 1);
        int filledZ = terrain.GetTopGroundZ(0, 0);
        terrain.TryRemoveTopGroundStratum(0, 0);
        int intermediate = terrain.GetCell(0, 0).GroundTileId;
        terrain.TryRemoveTopGroundStratum(0, 0);
        float restoredProgress = terrain.GetGroundHarvestProgress(0, 0);
        byte restoredGrass = terrain.GetGrass(0, 0);
        int removed = 0;
        while (terrain.TryRemoveTopGroundStratum(0, 0)) removed++;
        int bedrockZ = terrain.GetTopGroundZ(0, 0);
        var saved = GroundLayerCellSaveData.CaptureColumn(terrain, new UnityEngine.Vector2Int(0, 0));
        var decoded = MemoryPackSerializer.Deserialize<GroundLayerCellSaveData>(MemoryPackSerializer.Serialize(saved));
        terrain.RestoreGroundColumn(0, 0, original);
        GroundTileLayerSystem.Restore(terrain, decoded);
        bool reloaded = terrain.GetTopGroundZ(0, 0) == bedrockZ && terrain.GetCell(0, 0).GroundTileId == 3;
        var network = TerrainBuildingCellState.Capture(terrain, 0);
        terrain.RestoreGroundColumn(0, 0, original);
        network.Apply(terrain);
        bool replicated = terrain.GetTopGroundZ(0, 0) == bedrockZ && terrain.GetCell(0, 0).GroundTileId == 3;
        terrain.RestoreGroundColumn(0, 0, original);
        ulong before = terrain.ComputeStableHash();
        terrain.SetGroundStratum(0, 0, initialZ - 1, new GroundStratum { TileId = 4 });
        bool hiddenHash = before != terrain.ComputeStableHash();
        terrain.RestoreGroundColumn(0, 0, original);
        terrain.SetCell(0, 0, new TerrainCell(4, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        bool replacement = terrain.GetTopGroundZ(0, 0) == initialZ && terrain.GetGroundStratum(0, 0, initialZ - 1).TileId == 2;
        for (int i = 0; i < 65; i++) terrain.PushGroundStratum(0, 0, 4);
        bool growth = terrain.GetTopGroundZ(0, 0) == initialZ + 65 && terrain.GetCell(1, 0).GroundTileId == 2;
        terrain.RestoreGroundColumn(0, 0, original);
        using var emptyBuffer = new ChunkTerrainBuffer(1, 1);
        using var empty = emptyBuffer.Seal();
        bool noAllocation = empty.Depth == 0;
        TerrainSupportLayer.Set(empty, 0, 0, 91, 7);
        empty.PushGroundStratum(0, 0, 4, TerrainSupportLayer.GetSurfaceCell(empty, 0, 0).NavigationCost);
        bool filledWalkable = empty.GetCell(0, 0).IsWalkable && empty.GetCell(0, 0).NavigationCost == 7;
        bool emptyCoverRemoved = empty.TryRemoveTopGroundStratum(0, 0) && empty.GetCell(0, 0).GroundTileId == 0;
        bool emptyNotWalkable = !empty.GetCell(0, 0).IsWalkable &&
            TerrainSupportLayer.GetSurfaceCell(empty, 0, 0).IsWalkable && TerrainSupportLayer.GetSurfaceCell(empty, 0, 0).NavigationCost == 7;
        bool cellClearRejected = false, stackClearRejected = false;
        try { terrain.SetCell(0, 0, default); } catch (InvalidOperationException) { cellClearRejected = true; }
        terrain.ReplaceTileStack(1, 0, new[] { 2, 7, 8, 9 });
        try { terrain.ReplaceTileStack(1, 0, Array.Empty<int>()); }
        catch (InvalidOperationException) { stackClearRejected = true; }
        bool clearGuarded = cellClearRejected && stackClearRejected && terrain.GetTileLayerCount(1, 0) == 4 &&
            terrain.CaptureGroundColumn(0, 0).Layers.Length == original.Layers.Length;
        return new JObject { ["clearGuarded"] = clearGuarded, ["filledWalkable"] = filledWalkable, ["emptyNotWalkable"] = emptyNotWalkable, ["emptyCoverRemoved"] = emptyCoverRemoved, ["emptyUnallocated"] = noAllocation, ["initialLayers"] = initial, ["fillDelta"] = filledZ - initialZ,
            ["intermediate"] = intermediate, ["progress"] = restoredProgress, ["grass"] = restoredGrass,
            ["removed"] = removed, ["depth"] = initialZ - bedrockZ, ["reloaded"] = reloaded,
            ["replicated"] = replicated, ["hiddenHash"] = hiddenHash, ["replacement"] = replacement,
            ["growth"] = growth, ["rollback"] = terrain.GetTopGroundZ(0, 0) == initialZ && terrain.GetGroundHarvestProgress(0, 0) == .4f };
    }
    private static JObject TerrainLegacyGroundStrataRules(JObject args)
    {
        const int soil = 1000401, stone = 1000402, bedrock = 1000403;
        string encoded = $"{soil},1,{stone},0;{stone},5,{stone},{bedrock};{bedrock},0,0,0";
        var profile = new ChunkGenerationProfileSnapshot("strata.legacy", 1, 1, 1, null,
            new Dictionary<string, string> { [GroundStrataCatalog.ParameterId] = encoded });
        using var buffer = new ChunkTerrainBuffer(1, 1);
        buffer.SetCell(0, 0, new TerrainCell(soil, 0, 0, 0, 1, TerrainCellFlags.Walkable));
        using var terrain = buffer.Seal();
        terrain.InitializeGroundStrata(profile.GroundStrata);
        terrain.SetCell(0, 0, new TerrainCell(stone, 0, 0, 0, 1, TerrainCellFlags.Walkable));
        terrain.SetEnvironmentValue(TileBuildingSystem.RuntimeDamageLayerId, 0, 0, .3f);
        var legacy = new GroundLayerCellSaveData { LocalPosition = UnityEngine.Vector2Int.zero,
            ExcavatedLayers = 2, ExcavatedSurfaceLevels = 3, SurfaceGrass = ChunkTerrainData.GrassEmpty };
        var record = new ChunkSaveRecord { GroundLayerCells = new List<GroundLayerCellSaveData> { legacy } };
        byte[] payload = MemoryPackSerializer.Serialize(new SqliteChunkSnapshot { Terrain = MemoryPackSerializer.Serialize(record) });
        byte[] upgraded = new WorldGroundStrataSaveMigration().Migrate(payload);
        var migrated = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(MemoryPackSerializer.Deserialize<SqliteChunkSnapshot>(upgraded).Terrain);
        GroundTileLayerSystem.Restore(terrain, migrated.GroundLayerCells[0]);
        GroundColumnSnapshot column = terrain.CaptureGroundColumn(0, 0);
        var frozen = new WorldGenerationProfileSaveData();
        frozen.Capture(profile);
        var changed = profile.WithGenerationConfiguration(new Dictionary<string, double>(profile.NumericParameters),
            new Dictionary<string, string> { [GroundStrataCatalog.ParameterId] = $"{soil},1,{stone},0;{stone},9,{stone},{bedrock};{bedrock},0,0,0" }, null);
        var restoredProfile = frozen.Apply(changed);
        bool freezeKept = restoredProfile.GroundStrata.GetNaturalColumn(stone).Count == 6;
        return new JObject { ["schema"] = SaveDataMgr.ArchiveSchemas.GetCurrentVersion("flatworld.chunk"),
            ["deferred"] = migrated.GroundColumns.Count == 0 && !migrated.GroundLayerCells[0].BottomZ.HasValue,
            ["topZ"] = column.TopZ, ["remainingLayers"] = column.Layers.Length,
            ["topTile"] = column.Layers[column.Layers.Length - 1].TileId == stone,
            ["bedrock"] = column.Layers[0].TileId == bedrock, ["progress"] = terrain.GetGroundHarvestProgress(0, 0),
            ["frozen"] = freezeKept };
    }
    #endregion
}
