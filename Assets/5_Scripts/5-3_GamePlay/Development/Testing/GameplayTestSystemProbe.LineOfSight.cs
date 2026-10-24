using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public static partial class GameplayTestSystemProbe
{
    #region 单轮视线寻址回归

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterLineOfSightRules()
    {
        Register("ai", "line_of_sight_batch", LineOfSightBatch);
        Register("ai", "passable_building_sight", PassableBuildingSight);
        Register("ai", "unified_building_obstruction", UnifiedBuildingObstruction);
    }

    // 遍历完整设备目录，新增同类设施也必须经过正式遮挡筛选和整数格视线。
    private static JObject PassableBuildingSight(JObject args)
    {
        var definitions = new List<MachineDefinition>();
        var candidate = new ResourceReloadContext();
        MachineCatalog.ConfigureResourceReload(candidate);
        candidate.InCandidate(() =>
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in MachineCatalog.CaptureReloadSnapshot().Definitions)
                if (ids.Add(entry.Value.Id)) definitions.Add(entry.Value);
        });
        definitions.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
        int catalogCount = definitions.Count;
        // 任意名称及各覆盖层的可通行扩展不能依赖传送带白名单。
        for (int layer = 0; layer <= 6; layer++)
            definitions.Add(new MachineDefinition
            {
                Id = "test:passable_layer_" + layer, BlocksMovement = false, PlacementLayer = layer
            });
        for (int layer = 0; layer <= 15; layer++)
            definitions.Add(new MachineDefinition
            {
                Id = "test:solid_layer_" + layer, BlocksMovement = true, PlacementLayer = layer
            });
        using var world = new WorldRuntime("sight_probe", 1);
        ChunkRuntime chunk = SightProbeChunk(world, Vector2Int.zero);
        using var query = new GridLineOfSightQuery();
        query.Begin(world, default, "sight_probe", 4, 4, new Vector2(.5f, .5f));
        var samples = new JArray();
        foreach (MachineDefinition definition in definitions)
        {
            bool blocking = BuildingOccupancyRegistry.BlocksSight(definition);
            chunk.Terrain.SetDynamicSightBlockingCell(1, 0, blocking);
            bool visible = query.HasClearLineTo(new Vector2(3.5f, .5f));
            // 同格保留实体障碍时，可通行覆盖层不能抹掉原有地形墙。
            chunk.Terrain.SetCell(1, 0, SightProbeCell(true));
            bool wallVisible = query.HasClearLineTo(new Vector2(3.5f, .5f));
            chunk.Terrain.SetCell(1, 0, SightProbeCell(false));
            samples.Add(new JObject
            {
                ["id"] = definition.Id, ["blocksMovement"] = definition.BlocksMovement,
                ["blocksTraversal"] = definition.BlocksTraversal,
                ["layer"] = definition.Layer, ["blocksSight"] = blocking, ["visible"] = visible,
                ["wallVisible"] = wallVisible,
                ["afterWallRemovedVisible"] = query.HasClearLineTo(new Vector2(3.5f, .5f))
            });
        }
        chunk.Terrain.SetDynamicSightBlockingCell(1, 0, false);
        bool removedVisible = query.HasClearLineTo(new Vector2(3.5f, .5f));
        chunk.Terrain.SetCell(1, 0, SightProbeCell(true));
        return new JObject
        {
            ["catalogCount"] = catalogCount, ["extensionCount"] = definitions.Count - catalogCount,
            ["samples"] = samples, ["removedVisible"] = removedVisible,
            ["terrainWallVisible"] = query.HasClearLineTo(new Vector2(3.5f, .5f))
        };
    }

    // 同一声明分别经过普通建筑、数据编译和真实机械格索引，不启动当前世界。
    private static JObject UnifiedBuildingObstruction(JObject args)
    {
        var root = new GameObject("[UnifiedBuildingObstruction]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            Mod_Building building = root.AddComponent<Mod_Building>();
            var samples = new JArray();
            var graph = new MechanicalNetworkGraph(new Vector2Int(16, 16), Vector2Int.zero, default(WorldTopologyDomain));
            Vector2Int cell = new(3, 4);
            for (int layer = 0; layer <= 15; layer++)
            foreach (bool declaredBlocking in new[] { false, true })
            {
                building.PlacementLayer = (BuildingPlacementLayer)layer;
                building.BlocksMovementWhenPlaced = declaredBlocking;
                var parameters = new JObject
                {
                    [nameof(Mod_Building.PlacementLayer)] = layer,
                    [nameof(Mod_Building.BlocksMovementWhenPlaced)] = declaredBlocking
                };
                var configuration = new MachineModuleConfiguration(new RuntimeItemModuleDefinition(
                    "building", ModText.Building, "test:building", parameters.ToString(), true), building);
                var definition = new MachineDefinition { Id = "test:compiled", Ports = "none" };
                BuildingTraversalRules.ApplyToMachine(definition, configuration);
                var node = new MachineEntity { Id = 1, Cell = cell, Definition = definition };
                graph.Rebuild(new[] { node });
                samples.Add(new JObject
                {
                    ["layer"] = layer, ["declared"] = declaredBlocking,
                    ["gameObjectBlocking"] = building.BlocksMovement,
                    ["compiledLayer"] = definition.Layer,
                    ["compiledBlocking"] = definition.BlocksTraversal,
                    ["indexedBlocking"] = graph.IsMovementBlockedNormalized(cell),
                    ["sightBlocking"] = BuildingOccupancyRegistry.BlocksSight(definition),
                    ["highestLayer"] = graph.HighestOccupiedLayer,
                    ["placementRetained"] = ReferenceEquals(graph.At(cell, layer), node)
                });
            }

            var solid = new MachineEntity { Id = 1, Cell = cell,
                Definition = new MachineDefinition { Id = "test:upper", PlacementLayer = 15, Ports = "none" } };
            var overlay = new MachineEntity { Id = 2, Cell = cell,
                Definition = new MachineDefinition { Id = "test:overlay", PlacementLayer = 3, BlocksMovement = false, Ports = "none" } };
            graph.Rebuild(new[] { solid, overlay });
            bool stackedBlocked = graph.IsMovementBlockedNormalized(cell);
            graph.Rebuild(new[] { solid });
            bool overlayRemovedBlocked = graph.IsMovementBlockedNormalized(cell);
            graph.Rebuild(new[] { overlay });
            bool solidRemovedClear = !graph.IsMovementBlockedNormalized(cell) && graph.At(cell, 3) == overlay;
            graph.Rebuild(Array.Empty<MachineEntity>());
            bool emptyClear = !graph.IsMovementBlockedNormalized(cell);
            return new JObject
            {
                ["samples"] = samples, ["stackedBlocked"] = stackedBlocked,
                ["overlayRemovedBlocked"] = overlayRemovedBlocked, ["solidRemovedClear"] = solidRemovedClear,
                ["emptyClear"] = emptyClear, ["emptyHighestLayer"] = graph.HighestOccupiedLayer
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static ChunkRuntime SightProbeChunk(WorldRuntime world, Vector2Int origin,
        string dimension = "sight_probe", int width = 4, int height = 4)
    {
        var address = new RuntimeWorldAddress(dimension, new Int2(origin.x, origin.y));
        var profile = new ChunkGenerationProfileSnapshot(dimension, 1, width, height);
        ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
        using var buffer = new ChunkTerrainBuffer(width, height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            buffer.SetCell(x, y, new TerrainCell(1, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        using var generated = new ChunkGenerationResult(request, buffer);
        if (!world.TryCommit(generated, out string reason) || !world.TryGetChunk(address, out ChunkRuntime chunk))
            throw new InvalidOperationException("视线夹具提交失败：" + reason);
        return chunk;
    }

    private static TerrainCell SightProbeCell(bool blocked)
        => new(1, 0, blocked ? 1 : 0, 1, 1,
            blocked ? TerrainCellFlags.Blocking : TerrainCellFlags.Walkable);

    // 只在自有世界调用正式整数格 LOS 与权威遮挡写入，预期与性能边界由 Lua 判断。
    private static JObject LineOfSightBatch(JObject args)
    {
        using var world = new WorldRuntime("sight_probe", 1);
        using var query = new GridLineOfSightQuery();
        Vector2 observer = new(0.5f, 0.5f);
        query.Begin(world, default, "sight_probe", 4, 4, observer);
        var data = new JObject { ["unknownSameCell"] = query.HasClearLineTo(observer) };
        ChunkRuntime first = SightProbeChunk(world, Vector2Int.zero);
        SightProbeChunk(world, new Vector2Int(4, 0));
        ChunkRuntime verticalUp = SightProbeChunk(world, new Vector2Int(0, 4));
        SightProbeChunk(world, new Vector2Int(0, -4));
        query.Begin(world, default, "sight_probe", 4, 4, observer);
        data["crossChunkClear"] = query.HasClearLineTo(new Vector2(7.5f, 0.5f));
        data["firstLookupCount"] = query.ChunkLookupCount;
        data["secondRayClear"] = query.HasClearLineTo(new Vector2(6.5f, 1.5f));
        data["reusedLookupCount"] = query.ChunkLookupCount;
        data["cachedChunkCount"] = query.CachedChunkCount;

        data["verticalPositiveClear"] = query.HasClearLineTo(new Vector2(0.5f, 7.5f));
        data["verticalNegativeClear"] = query.HasClearLineTo(new Vector2(0.5f, -3.5f));
        first.Terrain.SetDynamicSightBlockingCell(0, 2, true);
        data["verticalIntermediateBlocked"] = query.HasClearLineTo(new Vector2(0.5f, 7.5f));
        int beforeVerticalUnknown = query.ChunkLookupCount;
        data["verticalBlockedBeforeUnknown"] = query.HasClearLineTo(new Vector2(0.5f, 8.5f));
        data["verticalSkippedUnknownLookup"] = query.ChunkLookupCount == beforeVerticalUnknown;
        first.Terrain.SetDynamicSightBlockingCell(0, 2, false);
        verticalUp.Terrain.SetDynamicSightBlockingCell(0, 3, true);
        data["verticalEndpointIgnored"] = query.HasClearLineTo(new Vector2(0.5f, 7.5f));
        verticalUp.Terrain.SetDynamicSightBlockingCell(0, 3, false);
        data["verticalUnknownEndpoint"] = query.HasClearLineTo(new Vector2(0.5f, 8.5f));

        query.HasClearLineTo(new Vector2(7.5f, 0.5f));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
            query.HasClearLineTo(new Vector2(7.5f, 0.5f));
        data["allocatedBytes"] = GC.GetAllocatedBytesForCurrentThread() - before;

        first.Terrain.SetDynamicSightBlockingCell(2, 0, true);
        data["dynamicBlocked"] = query.HasClearLineTo(new Vector2(7.5f, 0.5f));
        first.Terrain.SetDynamicSightBlockingCell(2, 0, false);
        data["dynamicRestored"] = query.HasClearLineTo(new Vector2(7.5f, 0.5f));
        long revision = first.Terrain.BlockingRevision;
        first.Terrain.SetCell(2, 0, SightProbeCell(true));
        data["terrainBlocked"] = query.HasClearLineTo(new Vector2(7.5f, 0.5f));
        first.Terrain.SetCell(2, 0, SightProbeCell(false));
        data["terrainRestored"] = query.HasClearLineTo(new Vector2(7.5f, 0.5f));
        data["terrainRevisionAdvanced"] = first.Terrain.BlockingRevision > revision;

        first.Terrain.SetDynamicSightBlockingCell(0, 0, true);
        first.Terrain.SetDynamicSightBlockingCell(3, 0, true);
        data["loadedSameCell"] = query.HasClearLineTo(observer);
        data["blockedEndpointsIgnored"] = query.HasClearLineTo(new Vector2(3.5f, 0.5f));
        first.Terrain.SetDynamicSightBlockingCell(0, 0, false);
        first.Terrain.SetDynamicSightBlockingCell(3, 0, false);
        Vector2 diagonalTarget = new(2.5f, 2.5f);
        data["diagonalClear"] = query.HasClearLineTo(diagonalTarget);
        first.Terrain.SetDynamicSightBlockingCell(1, 0, true);
        data["diagonalHorizontalBlocked"] = query.HasClearLineTo(diagonalTarget);
        first.Terrain.SetDynamicSightBlockingCell(1, 0, false);
        first.Terrain.SetDynamicSightBlockingCell(0, 1, true);
        data["diagonalVerticalBlocked"] = query.HasClearLineTo(diagonalTarget);
        first.Terrain.SetDynamicSightBlockingCell(0, 1, false);
        first.Terrain.SetDynamicSightBlockingCell(1, 1, true);
        data["diagonalCenterBlocked"] = query.HasClearLineTo(diagonalTarget);
        first.Terrain.SetDynamicSightBlockingCell(1, 1, false);

        int lookupsBeforeUnknown = query.ChunkLookupCount;
        first.Terrain.SetDynamicSightBlockingCell(2, 0, true);
        data["blockedBeforeUnknownEndpoint"] = query.HasClearLineTo(new Vector2(8.5f, 0.5f));
        data["blockedBeforeUnknownSkippedEndpointLookup"] = query.ChunkLookupCount == lookupsBeforeUnknown;
        first.Terrain.SetDynamicSightBlockingCell(2, 0, false);
        data["unknownEndpoint"] = query.HasClearLineTo(new Vector2(8.5f, 0.5f));
        data["unknownEndpointChecked"] = query.ChunkLookupCount > lookupsBeforeUnknown;
        SightProbeChunk(world, new Vector2Int(8, 0));
        world.EvictChunk(new RuntimeWorldAddress("sight_probe", new Int2(4, 0)));
        data["unknownIntermediate"] = query.HasClearLineTo(new Vector2(11.5f, 0.5f));
        SightProbeChunk(world, new Vector2Int(4, 0));
        data["reboundChunkRestored"] = query.HasClearLineTo(new Vector2(11.5f, 0.5f));
        world.BeginChunkGeneration(new RuntimeWorldAddress("sight_probe", new Int2(4, 0)), 1,
            new ChunkGenerationProfileSnapshot("sight_probe", 1, 4, 4));
        data["generatingChunkBlocked"] = query.HasClearLineTo(new Vector2(7.5f, 0.5f));
        SightProbeChunk(world, new Vector2Int(4, 0));
        data["replacedTerrainRestored"] = query.HasClearLineTo(new Vector2(7.5f, 0.5f));

        first.Terrain.SetCell(1, 0, SightProbeCell(true));
        data["oldObserverBlocked"] = query.HasClearLineTo(new Vector2(7.5f, 0.5f));
        query.Begin(world, default, "sight_probe", 4, 4, new Vector2(3.5f, 0.5f));
        data["newObserverClear"] = query.HasClearLineTo(new Vector2(7.5f, 0.5f));
        first.Terrain.SetCell(1, 0, SightProbeCell(false));
        ChunkRuntime otherDimension = SightProbeChunk(world, Vector2Int.zero, "sight_cave");
        otherDimension.Terrain.SetCell(1, 0, SightProbeCell(true));
        query.Begin(world, default, "sight_cave", 4, 4, observer);
        data["otherDimensionBlocked"] = query.HasClearLineTo(new Vector2(3.5f, 0.5f));
        query.Begin(world, default, "sight_probe", 4, 4, observer);
        data["originalDimensionClear"] = query.HasClearLineTo(new Vector2(3.5f, 0.5f));
        world.BeginNewEpoch(2);
        data["staleEpochBlocked"] = query.HasClearLineTo(new Vector2(3.5f, 0.5f));
        query.Begin(world, default, "sight_probe", 4, 4, observer);
        data["newEpochClear"] = query.HasClearLineTo(new Vector2(3.5f, 0.5f));

        ChunkRuntime wrappedLeft = SightProbeChunk(world, new Vector2Int(-4, 0));
        var wrapped = new WorldTopologyDomain(new int2(-4, -4), new int2(8, 8), true);
        query.Begin(world, default, "sight_probe", 4, 4, observer);
        data["horizontalNegativeClear"] = query.HasClearLineTo(new Vector2(-3.5f, 0.5f));
        query.Begin(world, wrapped, "sight_probe", 4, 4, new Vector2(3.5f, 3.5f));
        data["wrappedVerticalSeamClear"] = query.HasClearLineTo(new Vector2(3.5f, -3.5f));
        query.Begin(world, wrapped, "sight_probe", 4, 4, new Vector2(3.5f, 0.5f));
        first.Terrain.SetCell(0, 0, SightProbeCell(true));
        data["wrappedShortestClear"] = query.HasClearLineTo(new Vector2(-2.5f, 0.5f));
        data["wrappedEquivalentClear"] = query.HasClearLineTo(new Vector2(5.5f, 0.5f));
        wrappedLeft.Terrain.SetDynamicSightBlockingCell(0, 0, true);
        data["wrappedSeamBlocked"] = query.HasClearLineTo(new Vector2(-2.5f, 0.5f));
        wrappedLeft.Terrain.SetDynamicSightBlockingCell(0, 0, false);
        data["wrappedSeamRestored"] = query.HasClearLineTo(new Vector2(-2.5f, 0.5f));
        query.Dispose();
        data["clearedChunkCount"] = query.CachedChunkCount;
        data["disposedQueryBlocked"] = query.HasClearLineTo(new Vector2(-2.5f, 0.5f));
        query.Begin(world, wrapped, "sight_probe", 4, 4, new Vector2(3.5f, 0.5f));
        query.HasClearLineTo(new Vector2(-2.5f, 0.5f));
        world.Dispose();
        data["disposedWorldBlocked"] = query.HasClearLineTo(new Vector2(-2.5f, 0.5f));

        using var customWorld = new WorldRuntime("sight_custom", 1);
        SightProbeChunk(customWorld, new Vector2Int(-3, -5), "sight_custom", 3, 5);
        SightProbeChunk(customWorld, new Vector2Int(0, -5), "sight_custom", 3, 5);
        query.Begin(customWorld, default, "sight_custom", 3, 5, new Vector2(-2.5f, -4.5f));
        data["customNegativeChunksClear"] = query.HasClearLineTo(new Vector2(1.5f, -4.5f));
        return data;
    }

    #endregion
}
