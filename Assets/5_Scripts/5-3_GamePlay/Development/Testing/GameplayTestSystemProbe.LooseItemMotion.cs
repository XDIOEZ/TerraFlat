using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 散物共用运动内核采样
    private const string MotionTestWorld = "test:loose-item-motion";

    private static void RegisterLooseItemMotionOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("mechanical/loose_item_motion", LooseItemMotionValues);
        operations.Add("mechanical/loose_item_sources", LooseItemSourceValues);
        operations.Add("mechanical/loose_item_terrain_batch", LooseItemTerrainBatchValues);
    }

    // 夹具只提供地形、墙、环绕与带形，时间积分与力合成直接调用正式生产内核。
    private struct MotionField : LooseItemMotion.IField
    {
        internal bool Belt, Water, External, MissingTerrain;
        internal ConveyorPath Route;
        internal float Speed, Shore, Wall, Wrap;
        internal int Moves;
        public bool WaterOnly => Water;
        public Vector2 CarrierVelocity(Vector2 position, float seconds)
        {
            if (!Belt) return Vector2.right * Speed;
            return Route.TryMove(position, Speed * seconds, .4f, out Vector2 destination)
                ? (destination - position) / seconds : Vector2.zero;
        }
        public Vector2 ExternalVelocity(Vector2 position) => External
            ? LooseItemMotion.SampleAdditionalVelocity(new LooseItemMotionContext(MotionTestWorld, null, position, Water))
            : Vector2.zero;
        public bool IsWater(Vector2 position) => position.x < Shore;
        public Vector2 Move(Vector2 position, Vector2 displacement)
        {
            Moves++;
            if (MissingTerrain) return position;
            Vector2 destination = position + displacement;
            if (destination.x >= Wall) return position;
            if (Wrap > 0f) destination.x = Mathf.Repeat(destination.x, Wrap);
            return destination;
        }
        public Vector2 Delta(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            if (Wrap > 0f) delta.x = Mathf.Repeat(delta.x + Wrap * .5f, Wrap) - Wrap * .5f;
            return delta;
        }
    }

    private static JObject LooseItemMotionValues(JObject args)
    {
        Vector2 force = new((float)Number(args, "forceX", 0f), (float)Number(args, "forceY", 0f));
        if (Flag(args, "invalidForce", false)) force.x = float.NaN;
        using var source = LooseItemMotion.RegisterVelocitySource("test:" + Guid.NewGuid().ToString("N"),
            context => context.WorldKey == MotionTestWorld ? force : Vector2.zero);
        var field = new MotionField
        {
            Belt = Flag(args, "belt", false), Water = Flag(args, "water", false),
            External = !Flag(args, "submerged", false), MissingTerrain = Flag(args, "missing", false),
            Route = Flag(args, "curve", false) ? new ConveyorPath(2, 1) : ConveyorPath.Straight(0),
            Speed = (float)Number(args, "speed", 1f), Shore = (float)Number(args, "shore", 100f),
            Wall = (float)Number(args, "wall", 100f), Wrap = (float)Number(args, "wrap", 0f)
        };
        Vector2 start = new((float)Number(args, "x", 0f), (float)Number(args, "y", 0f));
        float seconds = (float)Number(args, "seconds", .2f);
        if (Flag(args, "invalidTime", false)) seconds = float.PositiveInfinity;
        Vector2 whole = LooseItemMotion.Integrate(start, seconds, ref field);
        int moves = field.Moves;
        Vector2 split = start;
        for (int i = 0; i < 20; i++) split = LooseItemMotion.Integrate(split, seconds / 20f, ref field);
        return new JObject
        {
            ["x"] = whole.x, ["y"] = whole.y, ["splitX"] = split.x, ["splitY"] = split.y, ["moves"] = moves
        };
    }

    private static JObject LooseItemSourceValues(JObject args)
    {
        var context = new LooseItemMotionContext(MotionTestWorld, null, Vector2.zero, false);
        Vector2 baseline = LooseItemMotion.SampleAdditionalVelocity(context);
        string id = "test:" + Guid.NewGuid().ToString("N");
        using var first = LooseItemMotion.RegisterVelocitySource(id,
            value => value.WorldKey == MotionTestWorld ? Vector2.right : Vector2.zero);
        using var second = LooseItemMotion.RegisterVelocitySource(id + ":second",
            value => value.WorldKey == MotionTestWorld ? Vector2.up * 2f : Vector2.zero);
        Vector2 combined = LooseItemMotion.SampleAdditionalVelocity(context) - baseline;
        string duplicate = string.Empty;
        try { using var rejected = LooseItemMotion.RegisterVelocitySource(id, _ => Vector2.zero); }
        catch (InvalidOperationException exception) { duplicate = exception.GetType().Name; }
        second.Dispose(); second.Dispose();
        Vector2 removed = LooseItemMotion.SampleAdditionalVelocity(context) - baseline;
        first.Dispose();
        Vector2 cleared = LooseItemMotion.SampleAdditionalVelocity(context) - baseline;
        IDisposable self = null;
        try
        {
            self = LooseItemMotion.RegisterVelocitySource(id + ":self", value =>
            { self.Dispose(); return Vector2.up; });
            Vector2 once = LooseItemMotion.SampleAdditionalVelocity(context) - baseline;
            Vector2 twice = LooseItemMotion.SampleAdditionalVelocity(context) - baseline;
            return new JObject
            {
                ["combinedX"] = combined.x, ["combinedY"] = combined.y, ["removedX"] = removed.x,
                ["removedY"] = removed.y, ["cleared"] = cleared.sqrMagnitude, ["duplicate"] = duplicate,
                ["selfOnceY"] = once.y, ["selfTwice"] = twice.sqrMagnitude
            };
        }
        finally { self?.Dispose(); }
    }

    // 自有世界只准备地形，水态、移动阻挡、环绕与生命周期均调用正式运动采样。
    private static JObject LooseItemTerrainBatchValues(JObject args)
    {
        const string dimension = "probe:loose-motion-terrain";
        using var world = new WorldRuntime(dimension, 1);
        var profile = new ChunkGenerationProfileSnapshot(dimension, 1, 2, 2);
        ChunkRuntime Commit(int x)
        {
            var address = new FlatWorld.WorldModel.WorldAddress(dimension, new Int2(x, 0));
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
            using var buffer = new ChunkTerrainBuffer(2, 2);
            for (int row = 0; row < 2; row++)
            for (int column = 0; column < 2; column++)
                buffer.SetCell(column, row, new TerrainCell(1, 0, 0, 1, 1, TerrainCellFlags.Walkable));
            using var generated = new ChunkGenerationResult(request, buffer);
            if (!world.TryCommit(generated, out string reason)) throw new InvalidOperationException(reason);
            world.TryGetChunk(address, out ChunkRuntime chunk);
            return chunk;
        }

        ChunkRuntime first = Commit(0);
        Commit(-4);
        ChunkRuntime neighbour = Commit(2);
        WorldTopologyDomain topology = new(new int2(-4, -4), new int2(8, 8), true);
        var field = new LooseItemMotion.WorldField
        {
            World = MotionTestWorld, SamplingInitialized = true, HasTerrainQuery = true, Topology = topology,
            TerrainQuery = new ChunkMgr.RuntimeTerrainPresenceQuery(world, topology, dimension, 2, 2)
        };
        Vector2 origin = new(.25f, .25f);
        var idle = new LooseItemMotion.WorldField { World = MotionTestWorld };
        Vector2 idlePosition = LooseItemMotion.Integrate(origin, .2f, ref idle);
        bool initialDry = !field.IsWater(origin);
        int water = first.Terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId);
        first.Terrain.SetLiquid(0, 0, water, 6);
        bool wet = field.IsWater(origin);
        TerrainSupportLayer.Set(first.Terrain, 0, 0, 90, 1);
        bool covered = !field.IsWater(origin);
        TerrainSupportLayer.Set(first.Terrain, 0, 0, 0, 1);
        bool uncovered = field.IsWater(origin);
        first.Terrain.SetLiquid(0, 0, 0, 0);
        bool drained = !field.IsWater(origin);
        Vector2 neighbouringPoint = new(2.25f, .25f);
        bool nextDry = !field.IsWater(neighbouringPoint);
        neighbour.Terrain.SetLiquid(0, 0, neighbour.Terrain.LiquidTypes.GetIndex(LiquidTypeCatalog.DirtyWaterId), 6);
        bool nextWet = field.IsWater(neighbouringPoint);
        neighbour.Terrain.SetLiquid(0, 0, 0, 0);
        bool nextDrained = !field.IsWater(neighbouringPoint);
        Vector2 moved = field.Move(origin, Vector2.right * .1f);
        first.Terrain.SetCell(0, 0, new TerrainCell(1, 0, 2, 1, 1, TerrainCellFlags.Blocking));
        Vector2 blocked = field.Move(origin, Vector2.right * .1f);
        first.Terrain.SetLiquid(0, 0, water, 6);
        Vector2 wetBlockedCell = field.Move(origin, Vector2.right * .1f);
        Vector2 seam = field.Move(new Vector2(3.95f, .25f), Vector2.right * .1f);
        Vector2 seamDelta = field.Delta(new Vector2(3.95f, .25f), seam);
        field.Topology = default;
        Vector2 independentMotion = field.Move(new Vector2(3.95f, .25f), Vector2.right * .1f);
        field.Topology = topology;
        Vector2 missing = field.Move(new Vector2(.25f, 1.95f), Vector2.up * .1f);
        field.IsWater(origin);
        world.EvictChunk(first.Address);
        bool evictedWater = field.IsWater(origin);
        Vector2 evictedMove = field.Move(origin, Vector2.right * .1f);
        Commit(0);
        Vector2 reloaded = field.Move(origin, Vector2.right * .1f);
        world.BeginNewEpoch(2);
        Vector2 oldEpoch = field.Move(origin, Vector2.right * .1f);
        return new JObject
        {
            ["initialDry"] = initialDry, ["wet"] = wet, ["covered"] = covered,
            ["idleX"] = idlePosition.x, ["idleSampling"] = idle.SamplingInitialized,
            ["uncovered"] = uncovered, ["drained"] = drained,
            ["nextDry"] = nextDry, ["nextWet"] = nextWet, ["nextDrained"] = nextDrained,
            ["movedX"] = moved.x, ["blockedX"] = blocked.x, ["wetBlockedX"] = wetBlockedCell.x,
            ["seamX"] = seam.x, ["seamDeltaX"] = seamDelta.x,
            ["independentMotionX"] = independentMotion.x, ["missingY"] = missing.y,
            ["evictedWater"] = evictedWater, ["evictedX"] = evictedMove.x,
            ["reloadedX"] = reloaded.x, ["oldEpochX"] = oldEpoch.x
        };
    }
    #endregion
}
