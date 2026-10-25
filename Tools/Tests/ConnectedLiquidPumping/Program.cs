using System;
using System.Diagnostics;
using FlatWorld.WorldModel;
using UnityEngine;

internal static class Program
{
    #region 正式连通图与供水缓存回归
    private static int passed;
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
    private static ChunkRuntime River(int origin, int cells = 16, int type = 1)
    {
        var chunk = new ChunkRuntime { Address = new("surface", new(origin, 0)) };
        for (int x = 0; x < cells; x++)
        {
            chunk.Terrain.Baseline[x] = new(type, 10);
            chunk.Terrain.Set(x, 0, type, 10);
        }
        return chunk;
    }
    private static int Drain(WorldLiquidBodySystem bodies, Vector2 inlet)
    {
        int removed = 0;
        while (bodies.TryGetPumpSource(inlet, out var source))
        {
            var t = source.Terrain; var p = source.LocalCell;
            t.Set(p.x, p.y, t.GetLiquidTypeIndex(p.x, p.y), t.GetLiquidLevel(p.x, p.y) - 1);
            if (++removed > 100000) throw new Exception("供水未结束");
        }
        return removed;
    }
    private static void Main()
    {
        var owner = new ChunkMgr();
        using var bodies = new WorldLiquidBodySystem(owner, "surface");
        var left = River(-16); var right = River(0);
        bodies.Observe(left); bodies.Observe(right);
        left.Terrain.Set(0, 0, 0, 0);
        Check(bodies.TryGetBody(new(-15.5f, .5f), out var initial) && initial.CellCount == 32 && initial.TotalLevel == 310,
            "negative coordinates and cross-chunk river aggregate");
        Check(Drain(bodies, new(-15.5f, .5f)) == 310, "empty intake drains all connected water including final level");
        Check(bodies.TryGetBody(new(-15.5f, .5f), out var dry) && dry.TotalLevel == 0 && dry.CellCount == 32,
            "empty basin preserves topology and cached total reaches zero");
        right.Terrain.Set(15, 0, 1, 1);
        Check(Drain(bodies, new(-15.5f, .5f)) == 1, "distant refill reactivates existing cached body");
        left.Terrain.Wall(8, 0, true); right.Terrain.Set(0, 0, 1, 10);
        Check(!bodies.TryGetPumpSource(new(-15.5f, .5f), out _), "terrain dam invalidates cached connectivity");
        left.Terrain.Wall(8, 0, false);
        Check(Drain(bodies, new(-15.5f, .5f)) == 10, "removing dam reconnects drained basin");
        WorldLiquidFlowObstacles.Set(new(-8, 0), true); right.Terrain.Set(0, 0, 1, 1);
        Check(!bodies.TryGetPumpSource(new(-15.5f, .5f), out _), "registered dam isolates supply");
        WorldLiquidFlowObstacles.Set(new(-8, 0), false);
        Check(Drain(bodies, new(-15.5f, .5f)) == 1, "registered dam removal reconnects supply");
        var foreign = River(16, type: 2); bodies.Observe(foreign);
        Check(!bodies.TryGetPumpSource(new(-15.5f, .5f), out _), "adjacent different liquids never share supply");
        right.Terrain.Set(1, 0, 1, 1); owner.WorldRuntime.Events.Evict(right.Address);
        Check(!bodies.TryGetPumpSource(new(-15.5f, .5f), out _), "evicted terrain cannot remain a cached donor");
        bodies.Observe(right);
        Check(Drain(bodies, new(-15.5f, .5f)) == 1, "restored chunk reconnects with current authoritative amount");
        var smallOwner = new ChunkMgr(); using var small = new WorldLiquidBodySystem(smallOwner, "surface");
        var puddle = River(0, 2); small.Observe(puddle);
        Check(Drain(small, new(.5f, .5f)) == 20, "two-cell body has no sixteen-cell threshold");
        var canalOwner = new ChunkMgr(); using var canal = new WorldLiquidBodySystem(canalOwner, "surface");
        var water = River(0, 1);
        for (int x = 1; x < 16; x++) water.Terrain.Excavated[x] = 1;
        canal.Observe(water);
        Check(Drain(canal, new(15.5f, .5f)) == 10, "empty excavated canal supplies from original basin");
        WorldTopologyRuntime.WrapWidth = 32;
        var wrappedOwner = new ChunkMgr(); using var wrapped = new WorldLiquidBodySystem(wrappedOwner, "surface");
        var a = River(0, 1); var b = River(16, 0);
        b.Terrain.Baseline[15] = new(1, 1); b.Terrain.Set(15, 0, 1, 1);
        wrapped.Observe(a); wrapped.Observe(b);
        Check(Drain(wrapped, new(.5f, .5f)) == 11, "wrapped world seam preserves same-body connectivity");
        WorldTopologyRuntime.WrapWidth = 0;
        var remoteOwner = new ChunkMgr(); using var remote = new WorldLiquidBodySystem(remoteOwner, "surface");
        var nearRiver = River(0, 1); var farRiver = River(16);
        // 干涸的原河床跨过视野边界，远端仍有真实水量。
        for (int x = 1; x < 16; x++) nearRiver.Terrain.Baseline[x] = new(1, 10);
        remote.Observe(nearRiver); remoteOwner.Remote.Add(farRiver.Address, farRiver);
        Check(Drain(remote, new(.5f, .5f)) == 10 && remote.IsPumpSupplyLoading && remoteOwner.Requests == 1,
            "depletion requests only one missing chunk instead of generating full river");
        int remoteRemoved = 0;
        for (int i = 0; i < 12; i++)
        {
            remote.Advance();
            remoteRemoved += Drain(remote, new(.5f, .5f));
            if (remoteRemoved == 160) break;
        }
        Check(remoteRemoved == 160 && SaveDataMgr.Instance.RestoredTerrain > 0 && SaveDataMgr.Instance.RestoredBuildings > 0,
            "demand frontier reaches unloaded river after authoritative terrain/building restoration");
        Check(remoteOwner.Leases > 0, "supply path is retained while pumping");
        Time.unscaledTimeAsDouble = 6; remote.Advance();
        Check(remoteOwner.Leases == 0 && !remote.IsPumpSupplyLoading, "stopped pump releases all extra leases and pending work");
        Time.unscaledTimeAsDouble = 0;
        var index = new WorldLiquidSupplyIndex<int>();
        for (int i = 0; i < 100000; i++) index.SetLevel(i, 10);
        for (int i = 0; i < 100; i++) { index.TryGetHighest(out int cell, out int level); index.SetLevel(cell, level - 1); }
        long before = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
        for (int i = 0; i < 100000; i++) { index.TryGetHighest(out int cell, out int level); index.SetLevel(cell, level - 1); }
        timer.Stop(); long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(index.TotalLevel == 899900 && allocated <= 128, "100k-cell warmed supply updates avoid per-operation allocations");
        Console.WriteLine($"{passed} assertions passed; 100k selections+updates: {timer.Elapsed.TotalMilliseconds:F2} ms, {allocated} bytes (standalone CLR, not Unity frame timing).");
    }
    #endregion
}
