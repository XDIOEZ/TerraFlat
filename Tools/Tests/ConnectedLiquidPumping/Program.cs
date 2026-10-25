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
            chunk.Terrain.Baseline[16 + x] = new(type, 10);
            chunk.Terrain.Set(x, 1, type, 10);
        }
        return chunk;
    }
    private static int Total(params ChunkRuntime[] chunks) => chunks.Sum(chunk => chunk.Terrain.Levels.Sum());
    private static void DrainPending(WorldLiquidBodySystem system, int maximum = 300)
    {
        for (int i = 0; i < maximum; i++)
        {
            system.Advance();
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
        body.Advance(); Check(owner.Requests == 0, "stable water and read-only body queries do not load neighbours");
        left.Terrain.Set(15, 1, 0, 0); DrainPending(body);
        Check(left.Terrain.GetLiquidLevel(15, 1) > 0 && right.Terrain.GetLiquidLevel(0, 1) < 10 && Total(left, right) == 310,
            "negative-coordinate cross-chunk neighbour diffusion conserves actual water");
        Check(body.PendingDiffusionCellCount == 0, "balanced water naturally sleeps");
        var damOwner = new ChunkMgr(); using var damBody = new WorldLiquidBodySystem(damOwner, "surface");
        var blocked = River(0); damBody.Observe(blocked);
        blocked.Terrain.Wall(7, 1, true); blocked.Terrain.Set(6, 1, 0, 0); DrainPending(damBody);
        Check(blocked.Terrain.GetLiquidLevel(8, 1) == 10 && Total(blocked) == 150,
            "terrain dam blocks diffusion without changing water behind it");
        var foreignOwner = new ChunkMgr(); using var foreignBody = new WorldLiquidBodySystem(foreignOwner, "surface");
        var water = River(0); var lava = River(16, 2);
        foreignBody.Observe(water); foreignBody.Observe(lava);
        water.Terrain.Set(15, 1, 0, 0); DrainPending(foreignBody);
        Check(Total(lava) == 160 && lava.Terrain.GetLiquidTypeIndex(0, 1) == 2,
            "foreign liquid is not borrowed across shared boundary");
        var remoteOwner = new ChunkMgr { DelayLoads = true };
        using var remoteBody = new WorldLiquidBodySystem(remoteOwner, "surface");
        var near = River(0); var far = River(16); remoteOwner.Remote.Add(far.Address, far);
        remoteBody.Observe(near); near.Terrain.Set(15, 1, 0, 0);
        remoteBody.Advance();
        Check(remoteOwner.Requests == 1 && remoteBody.LoadingDiffusionBoundaryChunkCount == 1 && remoteOwner.SimulationLeases == 0,
            "edge demand starts one data-only request without simulation lease");
        int before = Total(near);
        for (int i = 0; i < 5; i++) remoteBody.Advance();
        Check(Total(near) == before && remoteOwner.Requests == 1 && Total(far) == 160,
            "missing neighbour is closed until ready and repeated edges deduplicate");
        remoteOwner.WaitingLoads[far.Address].SetResult(far); remoteOwner.DelayLoads = false;
        DrainPending(remoteBody);
        Check(Total(near, far) == 310 && near.Terrain.GetLiquidLevel(15, 1) > 0 && Total(far) < 160,
            "ready neighbour resumes adjacent exchange rather than selecting remote pump donor");
        Check(SaveDataMgr.Instance.RestoredTerrain > 0 && SaveDataMgr.Instance.RestoredBuildings == 0,
            "cold chunks restore terrain/liquids without instantiating buildings");
        Check(remoteOwner.Leases == 0 && remoteBody.RetainedDiffusionDataChunkCount == 0 && remoteOwner.Evictions > 0,
            "settled frontier returns all extra data leases for safe eviction");
        var cancelledOwner = new ChunkMgr { DelayLoads = true };
        var cancelledBody = new WorldLiquidBodySystem(cancelledOwner, "surface");
        var pending = River(0); cancelledBody.Observe(pending); pending.Terrain.Set(15, 1, 0, 0);
        cancelledBody.Advance(); cancelledBody.Dispose();
        Check(cancelledOwner.Leases == 0 && cancelledOwner.WaitingLoads.Values.All(value => value.Task.IsCanceled),
            "world shutdown cancels late reads and releases all leases");
        var failedOwner = new ChunkMgr { DelayLoads = true }; using var failedBody = new WorldLiquidBodySystem(failedOwner, "surface");
        var failed = River(0); failedBody.Observe(failed); failed.Terrain.Set(15, 1, 0, 0); failedBody.Advance();
        failedOwner.WaitingLoads.Values.Single().SetException(new Exception("expected test failure"));
        DrainPending(failedBody);
        Check(Total(failed) == 150 && failedOwner.Requests == 1 && failedOwner.Leases == 0,
            "failed neighbour load loses no water and does not retry indefinitely");
        WorldTopologyRuntime.WrapWidth = 32;
        var wrappedOwner = new ChunkMgr(); using var wrappedBody = new WorldLiquidBodySystem(wrappedOwner, "surface");
        var a = River(0); var b = River(16); wrappedBody.Observe(a); wrappedBody.Observe(b);
        a.Terrain.Set(0, 1, 0, 0); DrainPending(wrappedBody);
        Check(b.Terrain.GetLiquidLevel(15, 1) < 10 && Total(a, b) == 310,
            "wrapped seam exchanges with canonical adjacent cell");
        WorldTopologyRuntime.WrapWidth = 0;
        var boundedOwner = new ChunkMgr { DelayLoads = true };
        var boundedBody = new WorldLiquidBodySystem(boundedOwner, "surface");
        for (int i = 0; i < 32; i++)
        {
            var chunk = River(i * 48); boundedBody.Observe(chunk); chunk.Terrain.Set(15, 1, 0, 0);
        }
        for (int i = 0; i < 40; i++) boundedBody.Advance();
        Check(boundedBody.RetainedDiffusionDataChunkCount <= 24 && boundedBody.LoadingDiffusionBoundaryChunkCount == 1 &&
              boundedBody.PendingDiffusionBoundaryChunkCount <= 64 && boundedOwner.SimulationLeases == 0,
            "many independent boundaries stay bounded and reserve a slot for forward progress");
        boundedBody.Dispose();
        Check(boundedOwner.Leases == 0, "budget saturation still releases every retained chunk on shutdown");
        Console.WriteLine($"{passed} assertions passed; storage/async completion use doubles, diffusion/streaming compile production source.");
    }
    #endregion
}
