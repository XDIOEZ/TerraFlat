using System;
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
        => Register("ai", "line_of_sight_batch", LineOfSightBatch);

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
        query.Begin(world, default, "sight_probe", 4, 4, observer);
        data["crossChunkClear"] = query.HasClearLineTo(new Vector2(7.5f, 0.5f));
        data["firstLookupCount"] = query.ChunkLookupCount;
        data["secondRayClear"] = query.HasClearLineTo(new Vector2(6.5f, 1.5f));
        data["reusedLookupCount"] = query.ChunkLookupCount;
        data["cachedChunkCount"] = query.CachedChunkCount;
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
