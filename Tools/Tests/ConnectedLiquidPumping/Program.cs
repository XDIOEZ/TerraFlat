using System;
using System.Linq;
using FlatWorld.WorldModel;
using UnityEngine;

internal static class Program
{
    #region 正式扩散边界与数据生命周期回归
    private static int passed;
    private static void Check(bool value, string label)
    { if (!value) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
    private static ChunkRuntime River(int origin, int type = 1)
    {
        var chunk = new ChunkRuntime { Address = new("surface", new(origin, 0)) };
        for (int x = 0; x < 16; x++)
        {
            chunk.Terrain.Baseline[16 + x] = new(type, 100);
            chunk.Terrain.Set(x, 1, type, 100);
        }
        return chunk;
    }
    private static int Total(params ChunkRuntime[] chunks) => chunks.Sum(chunk => chunk.Terrain.Levels.Sum());
    private static void Advance(WorldLiquidBodySystem system)
    {
        system.SimulateFixedStep();
        system.AdvanceStreamingFrame();
    }
    private static void DrainPending(WorldLiquidBodySystem system, int maximum = 300)
    {
        for (int i = 0; i < maximum; i++)
        {
            Advance(system);
            if (system.PendingDiffusionCellCount == 0 && system.PendingDiffusionBoundaryChunkCount == 0) return;
        }
        throw new Exception("扩散队列未在有界步骤内停稳");
    }
    private static void Main()
    {
        var owner = new ChunkMgr(); using var body = new WorldLiquidBodySystem(owner, "surface");
        var left = River(-16); var right = River(0);
        body.Observe(left); body.Observe(right);
        body.TryGetBody(new(-.5f, 1.5f), out _);
        Advance(body); Check(owner.Requests == 0, "stable water and read-only body queries do not load neighbours");
        left.Terrain.Set(15, 1, 0, 0);
        body.AdvanceStreamingFrame();
        Check(left.Terrain.GetLiquidLevel(15, 1) == 0 && right.Terrain.GetLiquidLevel(0, 1) == 100,
            "streaming preparation does not simulate diffusion");
        body.SimulateFixedStep();
        Check(left.Terrain.GetLiquidLevel(15, 1) > 0 && Total(left, right) == 3100,
            "fixed step advances awakened diffusion without creating water");
        DrainPending(body);
        Check(left.Terrain.GetLiquidLevel(15, 1) > 0 && right.Terrain.GetLiquidLevel(0, 1) < 100 && Total(left, right) == 3100,
            "negative-coordinate cross-chunk neighbour diffusion conserves actual water");
        Check(body.PendingDiffusionCellCount == 0, "balanced water naturally sleeps");
        var damOwner = new ChunkMgr(); using var damBody = new WorldLiquidBodySystem(damOwner, "surface");
        var blocked = River(0); damBody.Observe(blocked);
        blocked.Terrain.Wall(7, 1, true); blocked.Terrain.Set(6, 1, 0, 0); DrainPending(damBody);
        Check(blocked.Terrain.GetLiquidLevel(8, 1) == 100 && Total(blocked) == 1500,
            "terrain dam blocks diffusion without changing water behind it");
        var foreignOwner = new ChunkMgr(); using var foreignBody = new WorldLiquidBodySystem(foreignOwner, "surface");
        var water = River(0); var lava = River(16, 2);
        foreignBody.Observe(water); foreignBody.Observe(lava);
        water.Terrain.Set(15, 1, 0, 0); DrainPending(foreignBody);
        Check(Total(lava) == 1600 && lava.Terrain.GetLiquidTypeIndex(0, 1) == 2,
            "foreign liquid is not borrowed across shared boundary");
        var remoteOwner = new ChunkMgr { DelayLoads = true };
        using var remoteBody = new WorldLiquidBodySystem(remoteOwner, "surface");
        var near = River(0); var far = River(16); remoteOwner.Remote.Add(far.Address, far);
        remoteBody.Observe(near); near.Terrain.Set(15, 1, 0, 0);
        Advance(remoteBody);
        Check(remoteOwner.Requests == 1 && remoteBody.LoadingDiffusionBoundaryChunkCount == 1 && remoteOwner.SimulationLeases == 0,
            "edge demand starts one data-only request without simulation lease");
        int before = Total(near);
        for (int i = 0; i < 5; i++) Advance(remoteBody);
        Check(Total(near) == before && remoteOwner.Requests == 1 && Total(far) == 1600,
            "missing neighbour is closed until ready and repeated edges deduplicate");
        remoteOwner.WaitingLoads[far.Address].SetResult(far); remoteOwner.DelayLoads = false;
        DrainPending(remoteBody);
        Check(Total(near, far) == 3100 && near.Terrain.GetLiquidLevel(15, 1) > 0 && Total(far) < 1600,
            "ready neighbour resumes adjacent exchange rather than selecting remote pump donor");
        Check(SaveDataMgr.Instance.RestoredTerrain > 0 && SaveDataMgr.Instance.RestoredBuildings == 0,
            "cold chunks restore terrain/liquids without instantiating buildings");
        Check(remoteOwner.Leases == 0 && remoteBody.RetainedDiffusionDataChunkCount == 0 && remoteOwner.Evictions > 0,
            "settled frontier returns all extra data leases for safe eviction");
        var cancelledOwner = new ChunkMgr { DelayLoads = true };
        var cancelledBody = new WorldLiquidBodySystem(cancelledOwner, "surface");
        var pending = River(0); cancelledBody.Observe(pending); pending.Terrain.Set(15, 1, 0, 0);
        Advance(cancelledBody); cancelledBody.Dispose();
        Check(cancelledOwner.Leases == 0 && cancelledOwner.WaitingLoads.Values.All(value => value.Task.IsCanceled),
            "world shutdown cancels late reads and releases all leases");
        var failedOwner = new ChunkMgr { DelayLoads = true }; using var failedBody = new WorldLiquidBodySystem(failedOwner, "surface");
        var failed = River(0); failedBody.Observe(failed); failed.Terrain.Set(15, 1, 0, 0); Advance(failedBody);
        failedOwner.WaitingLoads.Values.Single().SetException(new Exception("expected test failure"));
        DrainPending(failedBody);
        Check(Total(failed) == 1500 && failedOwner.Requests == 1 && failedOwner.Leases == 0,
            "failed neighbour load loses no water and does not retry indefinitely");
        WorldTopologyRuntime.WrapWidth = 32;
        var wrappedOwner = new ChunkMgr(); using var wrappedBody = new WorldLiquidBodySystem(wrappedOwner, "surface");
        var a = River(0); var b = River(16); wrappedBody.Observe(a); wrappedBody.Observe(b);
        a.Terrain.Set(0, 1, 0, 0); DrainPending(wrappedBody);
        Check(b.Terrain.GetLiquidLevel(15, 1) < 100 && Total(a, b) == 3100,
            "wrapped seam exchanges with canonical adjacent cell");
        WorldTopologyRuntime.WrapWidth = 0;
        var boundedOwner = new ChunkMgr { DelayLoads = true };
        var boundedBody = new WorldLiquidBodySystem(boundedOwner, "surface");
        for (int i = 0; i < 32; i++)
        {
            var chunk = River(i * 48); boundedBody.Observe(chunk); chunk.Terrain.Set(15, 1, 0, 0);
        }
        for (int i = 0; i < 40; i++) Advance(boundedBody);
        Check(boundedBody.RetainedDiffusionDataChunkCount <= 24 && boundedBody.LoadingDiffusionBoundaryChunkCount == 1 &&
              boundedBody.PendingDiffusionBoundaryChunkCount <= 64 && boundedOwner.SimulationLeases == 0,
            "many independent boundaries stay bounded and reserve a slot for forward progress");
        boundedBody.Dispose();
        Check(boundedOwner.Leases == 0, "budget saturation still releases every retained chunk on shutdown");
        ValidateSharedPumpSearches();
        Console.WriteLine($"{passed} assertions passed; storage/async completion use doubles, diffusion/streaming compile production source.");
    }

    private static ChunkRuntime ShallowWater(int origin, int first = 2, int last = 5)
    {
        var chunk = new ChunkRuntime { Address = new("surface", new(origin, 0)) };
        for (int x = first; x <= last; x++)
        {
            chunk.Terrain.Baseline[16 + x] = new(1, 1);
            chunk.Terrain.Set(x, 1, 1, 1);
        }
        return chunk;
    }

    private static void ValidateSharedPumpSearches()
    {
        var owner = new ChunkMgr(); using var system = new WorldLiquidBodySystem(owner, "surface");
        var shallow = ShallowWater(0); shallow.Terrain.Set(5, 1, 1, 2); system.Observe(shallow);
        var first = system.SelectConnectedPumpSource(new(2.5f, 1.5f), out var source);
        long builds = system.PumpSearchTopologyBuildCount;
        var second = system.SelectConnectedPumpSource(new(3.5f, 1.5f), out var sameSource);
        Check(first == WorldLiquidPumpSourceSelection.Surplus && second == first && source.WorldCell == sameSource.WorldCell &&
              source.WorldCell == new Vector2Int(5, 1) && system.SharedPumpSearchGroupCount == 1 && builds == system.PumpSearchTopologyBuildCount,
            "two pumps share one wet-body cache and bypass the 2-1-0 diffusion staircase");
        shallow.Terrain.Set(5, 1, 1, 1);
        Check(system.SelectConnectedPumpSource(new(2.5f, 1.5f), out var remainder) == WorldLiquidPumpSourceSelection.LocalRemainder &&
              remainder.WorldCell == new Vector2Int(2, 1) && remainder.Terrain.GetLiquidLevel(2, 1) == 1,
            "only an exhausted completed search permits the local final layer");
        shallow.Terrain.Set(5, 1, 1, 2);
        Check(system.SelectConnectedPumpSource(new(2.5f, 1.5f), out _) == WorldLiquidPumpSourceSelection.Surplus &&
              builds == system.PumpSearchTopologyBuildCount,
            "adding surplus reactivates the shared candidate without rebuilding wet topology");
        shallow.Terrain.Set(2, 1, 0, 0);
        Check(system.SelectConnectedPumpSource(new(2.5f, 1.5f), out var dryInletSource) == WorldLiquidPumpSourceSelection.Surplus &&
              dryInletSource.WorldCell == new Vector2Int(5, 1),
            "an already dry inlet can recover surplus through its directly adjacent wet water body");
        shallow.Terrain.Set(2, 1, 1, 1);
        shallow.Terrain.Wall(4, 1, true);
        Check(system.SelectConnectedPumpSource(new(2.5f, 1.5f), out _) == WorldLiquidPumpSourceSelection.LocalRemainder,
            "new wall invalidates the source path and separates the remaining surplus");
        shallow.Terrain.Wall(4, 1, false);
        Check(system.SelectConnectedPumpSource(new(2.5f, 1.5f), out _) == WorldLiquidPumpSourceSelection.Surplus,
            "removing wall reconnects shared source discovery");
        shallow.Terrain.Set(4, 1, 0, 0);
        Check(system.SelectConnectedPumpSource(new(2.5f, 1.5f), out _) == WorldLiquidPumpSourceSelection.LocalRemainder,
            "original riverbed that is currently dry cannot connect two wet pools");
        shallow.Terrain.Set(4, 1, 2, 1);
        Check(system.SelectConnectedPumpSource(new(2.5f, 1.5f), out _) == WorldLiquidPumpSourceSelection.LocalRemainder,
            "different liquid cannot bridge a cached water path");
        var pendingOwner = new ChunkMgr { DelayLoads = true };
        using var pendingSystem = new WorldLiquidBodySystem(pendingOwner, "surface");
        var near = ShallowWater(0, 14, 15); var far = ShallowWater(16, 0, 1);
        far.Terrain.Set(1, 1, 1, 2); pendingOwner.Remote.Add(far.Address, far); pendingSystem.Observe(near);
        Check(pendingSystem.SelectConnectedPumpSource(new(14.5f, 1.5f), out _) == WorldLiquidPumpSourceSelection.Searching,
            "unknown boundary does not count as proof that only one-level water remains");
        Advance(pendingSystem);
        Check(pendingOwner.Requests == 1 && pendingSystem.SelectConnectedPumpSource(new(15.5f, 1.5f), out _) ==
              WorldLiquidPumpSourceSelection.Searching && near.Terrain.GetLiquidLevel(15, 1) == 1,
            "two pumps reuse a single pending data read and preserve inlet water");
        pendingOwner.WaitingLoads[far.Address].SetResult(far); pendingOwner.DelayLoads = false;
        Advance(pendingSystem);
        Check(pendingSystem.SelectConnectedPumpSource(new(14.5f, 1.5f), out var farSource) == WorldLiquidPumpSourceSelection.Surplus &&
              farSource.WorldCell == new Vector2Int(17, 1) && pendingOwner.SimulationLeases == 0 &&
              pendingSystem.SharedPumpSearchGroupCount == 1,
            "cold neighbouring wet component joins the same shared body using data-only loading");
        long remoteBuilds = pendingSystem.PumpSearchTopologyBuildCount;
        pendingOwner.WorldRuntime.Events.Evict(far.Address);
        Check(pendingSystem.SelectConnectedPumpSource(new(14.5f, 1.5f), out _) == WorldLiquidPumpSourceSelection.Searching,
            "cached cold donor waits for authoritative data instead of withdrawing from a stale snapshot");
        Advance(pendingSystem); Advance(pendingSystem);
        Check(pendingSystem.SelectConnectedPumpSource(new(14.5f, 1.5f), out _) == WorldLiquidPumpSourceSelection.Surplus &&
              pendingSystem.PumpSearchTopologyBuildCount == remoteBuilds,
            "unchanged cold reload reuses topology and candidate cache");
        far.Terrain.Set(1, 1, 1, 1);
        pendingSystem.SelectConnectedPumpSource(new(14.5f, 1.5f), out _);
        Advance(pendingSystem);
        Check(pendingSystem.SelectConnectedPumpSource(new(14.5f, 1.5f), out _) == WorldLiquidPumpSourceSelection.LocalRemainder,
            "complete shared search enables local fallback only after all connected surplus is exhausted");
        Time.unscaledTimeAsDouble = 6; Advance(pendingSystem); Time.unscaledTimeAsDouble = 0;
        Check(pendingOwner.Leases == 0 && pendingSystem.PumpSearchCachedChunkCount > 0,
            "idle source data is released while lightweight topology remains cached");
        var mergingOwner = new ChunkMgr(); using var merging = new WorldLiquidBodySystem(mergingOwner, "surface");
        var west = ShallowWater(0, 14, 15); var east = ShallowWater(16, 0, 1);
        west.Terrain.Set(14, 1, 1, 2); east.Terrain.Set(1, 1, 1, 2);
        merging.Observe(west); merging.Observe(east);
        merging.SelectConnectedPumpSource(new(14.5f, 1.5f), out _);
        merging.SelectConnectedPumpSource(new(17.5f, 1.5f), out _);
        Advance(merging);
        Check(merging.SharedPumpSearchGroupCount == 1,
            "already-loaded adjoining water bodies merge caches even while both pumps have surplus");
        var separateOwner = new ChunkMgr(); using var separate = new WorldLiquidBodySystem(separateOwner, "surface");
        var pools = ShallowWater(0); pools.Terrain.Set(4, 1, 0, 0); separate.Observe(pools);
        separate.SelectConnectedPumpSource(new(2.5f, 1.5f), out _);
        separate.SelectConnectedPumpSource(new(5.5f, 1.5f), out _);
        Check(separate.SharedPumpSearchGroupCount == 2, "disconnected same-liquid pools keep independent caches");
        var badOwner = new ChunkMgr { DelayLoads = true }; using var bad = new WorldLiquidBodySystem(badOwner, "surface");
        var badNear = ShallowWater(0, 14, 15); bad.Observe(badNear);
        bad.SelectConnectedPumpSource(new(14.5f, 1.5f), out _); Advance(bad);
        badOwner.WaitingLoads.Values.Single().SetException(new Exception("expected shared-search load failure"));
        Advance(bad);
        Check(bad.SelectConnectedPumpSource(new(14.5f, 1.5f), out _) == WorldLiquidPumpSourceSelection.Searching &&
              badNear.Terrain.GetLiquidLevel(14, 1) == 1,
            "failed shared search never certifies exhaustion or consumes the final inlet layer");
    }
    #endregion
}
