using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;

internal static class Program
{
    private sealed class Normalizer : IWorldAddressNormalizer
    {
        public ChunkGenerationTopologySnapshot Topology;
        public WorldAddress Normalize(WorldAddress address) => new WorldAddress(address.DimensionId,
            new Int2(Topology.NormalizeX(address.ChunkOrigin.X), Topology.NormalizeY(address.ChunkOrigin.Y)));
    }

    private static HashSet<WorldAddress> Brute(IReadOnlyList<ChunkWindowRequest> windows,
        ChunkWindowCoverage.Layer layer, IWorldAddressNormalizer normalizer)
    {
        var result = new HashSet<WorldAddress>();
        foreach (var request in windows)
        {
            if (layer == ChunkWindowCoverage.Layer.Presentation && !request.RequestPresentation) continue;
            int rx = layer == ChunkWindowCoverage.Layer.Active ? request.ActiveDistanceX - 1 :
                layer == ChunkWindowCoverage.Layer.Data ? request.DataDistanceX - 1 :
                layer == ChunkWindowCoverage.Layer.Presentation ? request.PresentationDistanceX - 1 : request.DestroyDistanceX - 1;
            int ry = layer == ChunkWindowCoverage.Layer.Active ? request.ActiveDistanceY - 1 :
                layer == ChunkWindowCoverage.Layer.Data ? request.DataDistanceY - 1 :
                layer == ChunkWindowCoverage.Layer.Presentation ? request.PresentationDistanceY - 1 : request.DestroyDistanceY - 1;
            var center = normalizer.Normalize(request.Center);
            for (int x = -rx; x <= rx; x++)
                for (int y = -ry; y <= ry; y++)
                    result.Add(normalizer.Normalize(new WorldAddress(center.DimensionId,
                        new Int2(center.ChunkOrigin.X + x * request.Profile.Width,
                            center.ChunkOrigin.Y + y * request.Profile.Height))));
        }
        return result;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void CheckChanges(ChunkWindowCoverage coverage, IReadOnlyList<ChunkWindowRequest> windows,
        IWorldAddressNormalizer normalizer, Dictionary<ChunkWindowCoverage.Layer, HashSet<WorldAddress>> previous)
    {
        coverage.Update(windows);
        foreach (ChunkWindowCoverage.Layer layer in Enum.GetValues(typeof(ChunkWindowCoverage.Layer)))
        {
            var expected = Brute(windows, layer, normalizer);
            var actual = new HashSet<WorldAddress>(coverage.Addresses(layer));
            Check(expected.SetEquals(actual), $"{layer}: coverage mismatch");
            var added = new HashSet<WorldAddress>(expected); added.ExceptWith(previous[layer]);
            var removed = new HashSet<WorldAddress>(previous[layer]); removed.ExceptWith(expected);
            Check(added.SetEquals(coverage.Added(layer)), $"{layer}: additions mismatch");
            Check(removed.SetEquals(coverage.Removed(layer)), $"{layer}: removals mismatch");
            previous[layer] = expected;
        }
    }

    private static void RunRandomized(bool wrapped)
    {
        var normalizer = new Normalizer { Topology = wrapped
            ? new ChunkGenerationTopologySnapshot(new Int2(0, 0), new Int2(16, 6)) : default };
        var profile = new ChunkGenerationProfileSnapshot("window-random", 1, 4, 2);
        var replacement = new ChunkGenerationProfileSnapshot("window-replacement", 1, 4, 2);
        var coverage = new ChunkWindowCoverage(normalizer);
        var prior = new Dictionary<ChunkWindowCoverage.Layer, HashSet<WorldAddress>>();
        foreach (ChunkWindowCoverage.Layer layer in Enum.GetValues(typeof(ChunkWindowCoverage.Layer)))
            prior.Add(layer, new HashSet<WorldAddress>());
        var random = new Random(483 + (wrapped ? 1 : 0));
        var centers = new List<Int2> { new Int2(0, 0) };
        for (int step = 0; step < 500; step++)
        {
            if (step % 31 == 0 && centers.Count < 3) centers.Add(new Int2(random.Next(-3, 4) * 4, random.Next(-3, 4) * 2));
            else if (step % 43 == 0 && centers.Count > 1) centers.RemoveAt(random.Next(centers.Count));
            else
            {
                int index = random.Next(centers.Count);
                Int2 current = centers[index];
                centers[index] = new Int2(current.X + random.Next(-1, 2) * 4, current.Y + random.Next(-1, 2) * 2);
            }
            int activeX = 1 + (step / 50) % 4, activeY = 1 + (step / 100) % 2;
            int dataX = activeX + 1, dataY = activeY + 1;
            var windows = new List<ChunkWindowRequest>();
            foreach (Int2 center in centers)
                windows.Add(new ChunkWindowRequest(new WorldAddress((step / 100) % 2 == 0 ? "other" : "probe", center),
                    new Int2(activeX, activeY), new Int2(dataX + 1, dataY + 1), (step / 150) % 2 != 0,
                    (step / 200) % 2 == 0 ? 2 : 1, (step / 250) % 2 == 0 ? replacement : profile, normalizer.Topology,
                    new Int2(dataX, dataY), new Int2(dataX, dataY)));
            CheckChanges(coverage, windows, normalizer, prior);
        }
    }

    private static void RunBoundaryVisits()
    {
        var profile = new ChunkGenerationProfileSnapshot("window-boundary", 1, 4, 2);
        var coverage = new ChunkWindowCoverage();
        ChunkWindowRequest At(int x, int y) => new ChunkWindowRequest(new WorldAddress("probe", new Int2(x, y)),
            new Int2(3, 2), new Int2(5, 4), true, 1, profile, default, new Int2(4, 3), new Int2(4, 3));
        coverage.Update(new[] { At(0, 0) });
        Check(coverage.LastVisitedCells == 148, "initial area count");
        coverage.Update(new[] { At(4, 0) });
        Check(coverage.LastVisitedCells == 40, "horizontal move must visit only boundary");
        coverage.Update(new[] { At(4, 0) });
        Check(coverage.LastVisitedCells == 0, "unchanged window must not visit cells");
        coverage.Update(new[] { At(0, 0) });
        Check(coverage.LastVisitedCells == 40, "reverse move boundary");
        coverage.Update(new[] { At(4, 2) });
        Check(coverage.LastVisitedCells == 88, "diagonal move boundary");
    }

    private sealed class UnexpectedGenerator : IChunkPureGenerator
    {
        public int Calls;
        public object GetPendingGenerationGroupKey(ChunkGenerationRequest request) => null;
        public ChunkGenerationResult Generate(ChunkGenerationRequest request, System.Threading.CancellationToken cancellationToken,
            ChunkGenerationTiming timing = null)
        {
            System.Threading.Interlocked.Increment(ref Calls);
            throw new Exception("All requested chunks were already committed.");
        }
    }

    private static void RunManagerLeasesAndDeferredEviction()
    {
        var profile = new ChunkGenerationProfileSnapshot("window-manager", 1, 4, 2);
        using var world = new WorldRuntime("window-manager", 1);
        for (int x = -4; x <= 4; x++)
            for (int y = -3; y <= 3; y++)
            {
                var address = new WorldAddress("probe", new Int2(x * 4, y * 2));
                var request = world.BeginChunkGeneration(address, 1, profile);
                using var terrain = new ChunkTerrainBuffer(4, 2);
                using var result = new ChunkGenerationResult(request, terrain);
                Check(world.TryCommit(result, out _), "precommit");
            }
        var generator = new UnexpectedGenerator();
        using var manager = new FlatWorld.WorldModel.ChunkMgr(world, generator);
        ChunkWindowRequest At(int x) => new ChunkWindowRequest(new WorldAddress("probe", new Int2(x, 0)),
            new Int2(2, 1), new Int2(4, 3), true, 1, profile, default, new Int2(3, 2), new Int2(3, 2));
        int changes = 0;
        using var sub = world.Events.Subscribe<ChunkPresentationDemandChanged>(_ => changes++);
        manager.RefreshWindow(At(0));
        Check(changes == 15 && world.Chunks.Count == 35, "initial demands and retained area");
        var protectedAddress = new WorldAddress("probe", new Int2(-12, 0));
        using var navigation = manager.AcquireLease(protectedAddress, ChunkLeaseKind.Navigation);
        manager.RefreshWindow(At(4));
        Check(changes == 21 && world.Chunks.ContainsKey(protectedAddress), "boundary demand changes and navigation protection");
        Check(world.Chunks[new WorldAddress("probe", new Int2(0, 0))].SimulationLeaseCount == 1 &&
              world.Chunks[new WorldAddress("probe", new Int2(8, 0))].SimulationLeaseCount == 1 &&
              world.Chunks[new WorldAddress("probe", new Int2(-4, 0))].SimulationLeaseCount == 0,
              "movement must change only entering and leaving simulation leases");
        navigation.Dispose();
        manager.RefreshWindow(At(4));
        Check(!world.Chunks.ContainsKey(protectedAddress) && changes == 21, "retry deferred eviction without duplicate demand events");
        var activeAddress = new WorldAddress("probe", new Int2(0, 0));
        using var activeNavigation = manager.AcquireLease(activeAddress, ChunkLeaseKind.Navigation);
        Check(!manager.EvictChunk(activeAddress), "external navigation lease must block forced eviction");
        manager.RefreshWindow(At(4));
        Check(world.Chunks[activeAddress].SimulationLeaseCount == 1 && changes == 23,
            "window refresh must restore demand after explicit eviction attempt");
        Check(generator.Calls == 0, "unexpected background generation");
        manager.ClearWindow();
        Check(changes == 38, "clear presentation demand");
        foreach (var chunk in world.Chunks.Values)
            Check(chunk.SimulationLeaseCount == 0, "clear simulation leases");
    }

    private static void RunDimensionTopologyChange()
    {
        var profile = new ChunkGenerationProfileSnapshot("window-topology-switch", 1, 4, 2);
        var normalizer = new Normalizer();
        using var world = new WorldRuntime("window-topology-switch", 1);
        void Commit(WorldAddress address)
        {
            var request = world.BeginChunkGeneration(address, 1, profile);
            using var terrain = new ChunkTerrainBuffer(4, 2);
            using var result = new ChunkGenerationResult(request, terrain);
            Check(world.TryCommit(result, out _), "topology fixture commit");
        }
        var oldAddress = new WorldAddress("old", new Int2(-4, 0));
        var newAddress = new WorldAddress("new", new Int2(12, 0));
        Commit(oldAddress);
        using var manager = new FlatWorld.WorldModel.ChunkMgr(world, new UnexpectedGenerator(), normalizer: normalizer);
        manager.RefreshWindow(new ChunkWindowRequest(oldAddress, 1, 1, true, 1, profile));
        using var oldNavigation = manager.AcquireLease(oldAddress, ChunkLeaseKind.Navigation);
        normalizer.Topology = new ChunkGenerationTopologySnapshot(new Int2(0, 0), new Int2(16, 6));
        Commit(newAddress);
        manager.RefreshWindow(new ChunkWindowRequest(newAddress, 1, 1, true, 1, profile, normalizer.Topology));
        Check(world.Chunks.ContainsKey(oldAddress) && world.Chunks.ContainsKey(newAddress), "old external lease survives topology change");
        oldNavigation.Dispose();
        manager.RefreshWindow(new ChunkWindowRequest(newAddress, 1, 1, true, 1, profile, normalizer.Topology));
        Check(!world.Chunks.ContainsKey(oldAddress) && world.Chunks.ContainsKey(newAddress),
            "old address must be evicted in its original topology after the external lease is released");
    }

    public static void Main()
    {
        RunBoundaryVisits();
        RunRandomized(false);
        RunRandomized(true);
        RunManagerLeasesAndDeferredEviction();
        RunDimensionTopologyChange();
        Console.WriteLine("Chunk window boundary, 1000 randomized union/delta checks and manager lease checks passed.");
    }
}
