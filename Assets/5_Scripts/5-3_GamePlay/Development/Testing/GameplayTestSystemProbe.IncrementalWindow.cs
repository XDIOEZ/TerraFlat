using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RuntimeAddress = FlatWorld.WorldModel.WorldAddress;

public static partial class GameplayTestSystemProbe
{
    #region 区块窗口增量规则采样

    // 直接比较正式增量结果与同一正式实现的冷建结果，Lua 验证具体边界和访问量。
    private static JObject ServiceIncrementalWindowUpdates(JObject args)
    {
        var profile = new ChunkGenerationProfileSnapshot("incremental-window", 1, 4, 2);
        var coverage = new ChunkWindowCoverage();
        ChunkWindowRequest At(int x, int y, int seed = 1, string dimension = "probe",
            ChunkGenerationProfileSnapshot chosen = null, ChunkGenerationTopologySnapshot topology = default)
            => new(new RuntimeAddress(dimension, new Int2(x, y)), new Int2(3, 2), new Int2(5, 4),
                true, seed, chosen ?? profile, topology, new Int2(4, 3), new Int2(4, 3));
        var start = At(0, 0);
        coverage.Update(new[] { start });
        var first = new JObject { ["active"] = coverage.Count(ChunkWindowCoverage.Layer.Active),
            ["data"] = coverage.Count(ChunkWindowCoverage.Layer.Data),
            ["retained"] = coverage.Count(ChunkWindowCoverage.Layer.Retained) };
        coverage.Update(new[] { At(4, 0) });
        var move = new JObject { ["activeAdded"] = coverage.Added(ChunkWindowCoverage.Layer.Active).Count,
            ["activeRemoved"] = coverage.Removed(ChunkWindowCoverage.Layer.Active).Count,
            ["dataAdded"] = coverage.Added(ChunkWindowCoverage.Layer.Data).Count,
            ["dataRemoved"] = coverage.Removed(ChunkWindowCoverage.Layer.Data).Count,
            ["retainedAdded"] = coverage.Added(ChunkWindowCoverage.Layer.Retained).Count,
            ["retainedRemoved"] = coverage.Removed(ChunkWindowCoverage.Layer.Retained).Count,
            ["visits"] = coverage.LastVisitedCells };
        coverage.Update(new[] { At(4, 0) });
        var same = new JObject { ["visits"] = coverage.LastVisitedCells };
        coverage.Update(new[] { start });
        bool reverseMatches = WindowCoverageMatchesFreshBuild(coverage, new[] { start });
        coverage.Update(new[] { At(4, 2) });
        int diagonalVisits = coverage.LastVisitedCells;
        bool diagonalMatches = WindowCoverageMatchesFreshBuild(coverage, new[] { At(4, 2) });
        coverage.Update(new[] { At(0, 0), At(8, 0) });
        coverage.Update(new[] { At(4, 0), At(8, 0) });
        bool overlapMatches = WindowCoverageMatchesFreshBuild(coverage, new[] { At(4, 0), At(8, 0) });
        coverage.Update(new[] { At(8, 0) });
        overlapMatches &= WindowCoverageMatchesFreshBuild(coverage, new[] { At(8, 0) });
        var replacement = new ChunkGenerationProfileSnapshot("incremental-window-2", 1, 4, 2);
        coverage.Update(new[] { At(8, 0, chosen: replacement) });
        bool configurationRebuilt = coverage.LastFullRebuild;
        coverage.Update(new[] { At(8, 0, seed: 2, chosen: replacement) });
        bool seedRebuilt = coverage.LastFullRebuild;
        coverage.Update(new[] { At(8, 0, seed: 2, dimension: "other", chosen: replacement) });
        bool dimensionRebuilt = coverage.LastFullRebuild;

        var topology = new ChunkGenerationTopologySnapshot(new Int2(0, 0), new Int2(16, 6));
        var normalizer = new IncrementalWindowTopologyNormalizer(topology);
        var wrapped = new ChunkWindowCoverage(normalizer);
        wrapped.Update(new[] { At(12, 0, topology: topology) });
        wrapped.Update(new[] { At(0, 0, topology: topology) });
        int wrapBoundaryVisits = wrapped.LastVisitedCells;
        bool wrappedMatches = WindowCoverageMatchesFreshBuild(wrapped, new[] { At(0, 0, topology: topology) }, normalizer);
        wrapped.Update(new[] { At(0, 0, topology: topology), At(4, 0, topology: topology) });
        wrapped.Update(new[] { At(4, 0, topology: topology) });
        wrappedMatches &= WindowCoverageMatchesFreshBuild(wrapped, new[] { At(4, 0, topology: topology) }, normalizer);

        var address = new RuntimeAddress("probe", new Int2(0, 0));
        ChunkMgr.RuntimePresentationBatchEntry Entry(float distance, bool needsBase, long round, int order)
            => new(address, distance, needsBase, round, order);
        var ordered = new List<ChunkMgr.RuntimePresentationBatchEntry>
            { Entry(1, false, 0, 0), Entry(9, true, 0, 1), Entry(1, true, 0, 2) };
        ordered.Sort(ChunkMgr.RuntimePresentationBatchEntry.CompareForStack);
        bool priorityBaseFirst = ordered[2].NeedsBase;
        bool priorityNearest = ordered[2].Distance == 1 && ordered[1].Distance == 9;
        ordered = new List<ChunkMgr.RuntimePresentationBatchEntry>
            { Entry(1, true, 1, 0), Entry(9, true, 0, 1) };
        ordered.Sort(ChunkMgr.RuntimePresentationBatchEntry.CompareForStack);
        bool priorityRoundRobin = ordered[1].Round == 0;
        ordered = new List<ChunkMgr.RuntimePresentationBatchEntry>
            { Entry(9, true, 0, 0), Entry(1, true, 0, 1) };
        ordered.Sort(ChunkMgr.RuntimePresentationBatchEntry.CompareForStack);
        bool priorityMovement = ordered[1].Order == 1;
        return new JObject { ["first"] = first, ["move"] = move, ["same"] = same,
            ["reverseMatches"] = reverseMatches, ["diagonalMatches"] = diagonalMatches,
            ["diagonalVisits"] = diagonalVisits, ["overlapMatches"] = overlapMatches,
            ["wrappedMatches"] = wrappedMatches, ["wrapBoundaryVisits"] = wrapBoundaryVisits,
            ["configurationRebuilt"] = configurationRebuilt, ["seedRebuilt"] = seedRebuilt,
            ["dimensionRebuilt"] = dimensionRebuilt, ["priorityBaseFirst"] = priorityBaseFirst,
            ["priorityNearest"] = priorityNearest, ["priorityRoundRobin"] = priorityRoundRobin,
            ["priorityMovement"] = priorityMovement,
            ["prefetchRetry"] = ChunkMgr.CaptureRuntimePrefetchRetryForWindowProbe() };
    }

    private static bool WindowCoverageMatchesFreshBuild(ChunkWindowCoverage actual,
        IReadOnlyList<ChunkWindowRequest> windows, IWorldAddressNormalizer normalizer = null)
    {
        var expected = new ChunkWindowCoverage(normalizer);
        expected.Update(windows);
        foreach (ChunkWindowCoverage.Layer layer in new[] { ChunkWindowCoverage.Layer.Active,
                     ChunkWindowCoverage.Layer.Data, ChunkWindowCoverage.Layer.Presentation, ChunkWindowCoverage.Layer.Retained })
        {
            if (actual.Count(layer) != expected.Count(layer)) return false;
            foreach (RuntimeAddress address in expected.Addresses(layer))
                if (!actual.Contains(layer, address)) return false;
        }
        return true;
    }

    private sealed class IncrementalWindowTopologyNormalizer : IWorldAddressNormalizer
    {
        private readonly ChunkGenerationTopologySnapshot topology;
        public IncrementalWindowTopologyNormalizer(ChunkGenerationTopologySnapshot topology) => this.topology = topology;
        public RuntimeAddress Normalize(RuntimeAddress address) => new(address.DimensionId,
            new Int2(topology.NormalizeX(address.ChunkOrigin.X), topology.NormalizeY(address.ChunkOrigin.Y)));
    }

    #endregion
}

public partial class ChunkMgr
{
    #region 预取失败与窗口刷新规则采样

    // 非激活管理器调用正式预取队列，避免进入场景、创建世界或启动协程。
    internal static JObject CaptureRuntimePrefetchRetryForWindowProbe()
    {
        var holder = new GameObject("prefetch-retry-probe");
        holder.SetActive(false);
        ChunkMgr manager = holder.AddComponent<ChunkMgr>();
        try
        {
            var profile = new ChunkGenerationProfileSnapshot("prefetch-retry-probe", 1, 4, 2);
            var address = new RuntimeAddress("probe", new Int2(0, 0));
            var moved = new RuntimeAddress("probe", new Int2(4, 0));
            ChunkWindowRequest Window(RuntimeAddress center) => new(center, 1, 1, false, 1, profile);
            void Refresh(RuntimeAddress center)
            {
                manager.RebuildRuntimePrefetchQueue(new Vector2Int(center.ChunkOrigin.X, center.ChunkOrigin.Y),
                    center.DimensionId, Vector2Int.one, Vector2Int.one, profile.Width, profile.Height,
                    profile, 1, default, new[] { Window(center) });
            }
            void TakeQueuedAddress()
            {
                RuntimePrefetchRequest request = manager.runtimePrefetchQueue.Dequeue();
                manager.runtimePrefetchQueuedAddresses.Remove(request.Address);
            }
            Refresh(address);
            bool initialQueued = manager.runtimePrefetchQueue.Count == 1 &&
                manager.runtimePrefetchQueue.Peek().Address == address;
            TakeQueuedAddress();
            manager.RememberFailedRuntimePrefetchUntilNextWindowRefresh(address);
            bool failureRetained = manager.runtimePrefetchQueuedAddresses.Contains(address);
            bool noImmediateRetry = manager.runtimePrefetchQueue.Count == 0;
            Refresh(address);
            bool failureRetriedOnRefresh = manager.runtimePrefetchQueue.Count == 1 &&
                manager.runtimePrefetchQueue.Peek().Address == address;
            TakeQueuedAddress();
            manager.RememberFailedRuntimePrefetchUntilNextWindowRefresh(address);
            bool cancellationRetained = manager.runtimePrefetchQueuedAddresses.Contains(address) &&
                manager.runtimePrefetchQueue.Count == 0;
            Refresh(address);
            bool cancellationRetriedOnRefresh = manager.runtimePrefetchQueue.Count == 1 &&
                manager.runtimePrefetchQueue.Peek().Address == address;
            TakeQueuedAddress();
            manager.RememberFailedRuntimePrefetchUntilNextWindowRefresh(address);
            Refresh(moved);
            manager.RememberFailedRuntimePrefetchUntilNextWindowRefresh(address);
            bool leavingDiscarded = !manager.runtimePrefetchTargets.Contains(address) &&
                !manager.runtimePrefetchQueuedAddresses.Contains(address);
            manager.runtimeWindowCoverage.Update(new[] { Window(address) });
            manager.runtimeWindowTargets.Add(address);
            Refresh(address);
            manager.RememberFailedRuntimePrefetchUntilNextWindowRefresh(address);
            bool presentationDiscarded = !manager.runtimePrefetchTargets.Contains(address) &&
                !manager.runtimePrefetchQueuedAddresses.Contains(address);
            return new JObject { ["initialQueued"] = initialQueued, ["failureRetained"] = failureRetained,
                ["noImmediateRetry"] = noImmediateRetry, ["failureRetriedOnRefresh"] = failureRetriedOnRefresh,
                ["cancellationRetained"] = cancellationRetained, ["cancellationRetriedOnRefresh"] = cancellationRetriedOnRefresh,
                ["leavingDiscarded"] = leavingDiscarded, ["presentationDiscarded"] = presentationDiscarded };
        }
        finally
        {
            manager.IsWorldRuntimeShuttingDown = true;
            UnityEngine.Object.DestroyImmediate(holder);
        }
    }

    #endregion
}
