using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 鸟类前视弧线单轮查询

    private static void RegisterBirdFlightQueryOperations(Dictionary<string, Func<JObject, JObject>> operations)
        => operations.Add("ai/flight_traversal_batch", BirdFlightTraversalBatch);

    // 仅在自有世界调用正式飞行通行，Lua 检查连续分段、接缝与查询生命周期。
    private static JObject BirdFlightTraversalBatch(JObject args)
    {
        const string dimension = "probe:bird-flight";
        bool wrapped = Flag(args, "wrapped", false);
        WorldTopologyDomain topology = wrapped
            ? new WorldTopologyDomain(new int2(-4, -4), new int2(8, 8), true) : default;
        using var world = new WorldRuntime(dimension, 1);
        int width = (int)Number(args, "chunkWidth", 2), height = (int)Number(args, "chunkHeight", 2);
        var profile = new ChunkGenerationProfileSnapshot(dimension, 1, width, height);
        ChunkRuntime Commit(int x, int y)
        {
            var address = new FlatWorld.WorldModel.WorldAddress(dimension, new Int2(x, y));
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
            using var buffer = new ChunkTerrainBuffer(width, height);
            for (int row = 0; row < height; row++)
            for (int column = 0; column < width; column++)
                buffer.SetCell(column, row, new TerrainCell(1, 0, 0, 1, 1, TerrainCellFlags.Walkable));
            using var generated = new ChunkGenerationResult(request, buffer);
            if (!world.TryCommit(generated, out string reason)) throw new InvalidOperationException(reason);
            world.TryGetChunk(address, out ChunkRuntime chunk);
            return chunk;
        }

        var committed = new List<ChunkRuntime>();
        foreach (JObject coordinate in (JArray)args["chunks"])
            committed.Add(Commit((int)Number(coordinate, "x", 0), (int)Number(coordinate, "y", 0)));
        var navigation = new BirdFlightNavigationProfile { sampleSpacing = .25f, crossGroundObstacles = true };
        var query = new ChunkMgr.RuntimeTerrainPresenceQuery(world, topology, dimension, width, height);
        var cursor = new BirdFlightNavigationProfile.TraversalCursor();
        Vector2 origin = new((float)Number(args, "originX", .1), (float)Number(args, "originY", .1));
        var results = new JArray();
        foreach (JObject endpoint in (JArray)args["segments"])
        {
            Vector2 target = new((float)Number(endpoint, "x", 0), (float)Number(endpoint, "y", 0));
            bool accepted = navigation.CanTraverse(origin, target, ref query, ref cursor);
            results.Add(accepted);
            origin = target;
        }
        int batchQueries = cursor.TerrainQueryCount;
        Vector2 finalPoint = origin;
        bool freshBeforeChange = navigation.CanTraverse(finalPoint, finalPoint, ref query);
        foreach (ChunkRuntime chunk in committed) world.EvictChunk(chunk.Address);
        bool freshAfterEviction = navigation.CanTraverse(finalPoint, finalPoint, ref query);
        foreach (JObject coordinate in (JArray)args["chunks"])
            Commit((int)Number(coordinate, "x", 0), (int)Number(coordinate, "y", 0));
        bool freshAfterReload = navigation.CanTraverse(finalPoint, finalPoint, ref query);
        world.BeginNewEpoch(2);
        bool oldEpochAccepted = navigation.CanTraverse(finalPoint, finalPoint, ref query);
        world.Dispose();
        bool disposedAccepted = navigation.CanTraverse(finalPoint, finalPoint, ref query);
        return new JObject
        {
            ["results"] = results, ["batchQueries"] = batchQueries,
            ["freshBeforeChange"] = freshBeforeChange, ["freshAfterEviction"] = freshAfterEviction,
            ["freshAfterReload"] = freshAfterReload, ["oldEpochAccepted"] = oldEpochAccepted,
            ["disposedAccepted"] = disposedAccepted
        };
    }

    #endregion
}
