using System;
using System.Collections.Generic;
using System.Threading;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RuntimeChunkManager = FlatWorld.WorldModel.ChunkMgr;
using RuntimeAddress = FlatWorld.WorldModel.WorldAddress;

public static partial class GameplayTestSystemProbe
{
    #region 多观察者区块窗口

    // 预先提交独立空地形，再走正式联机窗口计划和纯模型，不访问当前游戏世界。
    private static JObject ServiceObserverWindows(JObject args)
    {
        var profile = new ChunkGenerationProfileSnapshot("observer-probe", 1, 4, 2);
        using var world = new WorldRuntime("observer-probe", 1);
        var generator = new ObserverWindowUnexpectedGenerator();
        using var manager = new RuntimeChunkManager(world, generator);
        for (int x = -3; x <= 5; x++)
            for (int y = -2; y <= 2; y++)
                ObserverWindowCommit(world, profile, x * 4, y * 2);
        ObserverWindowCommit(world, profile, 40, 0);
        int demandChanges = 0;
        using var subscription = world.Events.Subscribe<ChunkPresentationDemandChanged>(_ => demandChanges++);
        Vector3[] observers = { new(1, 1, 0), new(9, 1, 0), new(1, 1, 0) };
        List<ChunkWindowRequest> plan = ChunkMgr.CreateRuntimeObserverWindows(observers, "probe",
            new Vector2Int(2, 1), new Vector2Int(3, 2), new Vector2Int(4, 3), 1, profile);
        var result = new JObject { ["plan"] = ObserverWindowPlan(plan) };
        manager.RefreshWindows(plan);
        result["merged"] = ObserverWindowSnapshot(manager);
        result["sharedLeaseCount"] = world.Chunks[new RuntimeAddress("probe", new Int2(4, 0))].SimulationLeaseCount;
        int beforeRepeat = demandChanges;
        manager.RefreshWindows(plan);
        result["repeatDemandChanges"] = demandChanges - beforeRepeat;
        result["repeated"] = ObserverWindowSnapshot(manager);

        using ChunkLease navigation = manager.AcquireLease(new RuntimeAddress("probe", new Int2(-8, 0)),
            ChunkLeaseKind.Navigation);
        plan = ChunkMgr.CreateRuntimeObserverWindows(new[] { observers[1] }, "probe",
            new Vector2Int(2, 1), new Vector2Int(3, 2), new Vector2Int(4, 3), 1, profile);
        manager.RefreshWindows(plan);
        result["removed"] = ObserverWindowSnapshot(manager);
        result["retainedDormant"] = world.Chunks[new RuntimeAddress("probe", new Int2(-4, 0))].SimulationStatus.ToString();
        result["preloadSimulationCount"] = world.Chunks[new RuntimeAddress("probe", new Int2(0, 0))].SimulationLeaseCount;
        result["sharedAfterRemoval"] = world.Chunks[new RuntimeAddress("probe", new Int2(4, 0))].SimulationLeaseCount;
        result["navigationProtected"] = world.Chunks.ContainsKey(new RuntimeAddress("probe", new Int2(-8, 0)));
        manager.RefreshWindows(ChunkMgr.CreateRuntimeObserverWindows(Array.Empty<Vector3>(), "probe",
            Vector2Int.one, Vector2Int.one, Vector2Int.one, 1, profile));
        result["empty"] = ObserverWindowSnapshot(manager);
        navigation.Dispose();
        manager.RefreshWindows(Array.Empty<ChunkWindowRequest>());
        result["released"] = ObserverWindowSnapshot(manager);
        result["unexpectedGenerationCount"] = generator.Calls;

        List<ChunkWindowRequest> negative = ChunkMgr.CreateRuntimeObserverWindows(
            new[] { new Vector3(-.1f, -.1f, 0), new Vector3(4.1f, 2.1f, 0) }, "probe",
            Vector2Int.one, Vector2Int.one, Vector2Int.one, 1, profile);
        result["negative"] = ObserverWindowPlan(negative);
        result["invalidFiltered"] = ChunkMgr.CreateRuntimeObserverWindows(
            new[] { new Vector3(float.NaN, 0, 0), new Vector3(0, float.PositiveInfinity, 0), Vector3.zero }, "probe",
            Vector2Int.one, Vector2Int.one, Vector2Int.one, 1, profile).Count;
        result["wrapped"] = ServiceWrappedObserverWindows(profile);
        return result;
    }

    private static JObject ServiceWrappedObserverWindows(ChunkGenerationProfileSnapshot profile)
    {
        var topology = new ChunkGenerationTopologySnapshot(new Int2(0, 0), new Int2(16, 6));
        using var world = new WorldRuntime("wrapped-observer-probe", 1);
        var generator = new ObserverWindowUnexpectedGenerator();
        using var manager = new RuntimeChunkManager(world, generator,
            normalizer: new ObserverWindowAddressNormalizer(topology));
        for (int x = 0; x < 4; x++)
            for (int y = 0; y < 3; y++)
                ObserverWindowCommit(world, profile, x * 4, y * 2);
        List<ChunkWindowRequest> plan = ChunkMgr.CreateRuntimeObserverWindows(
            new[] { Vector3.zero, new Vector3(16, 6, 0) }, "probe",
            new Vector2Int(2, 1), new Vector2Int(3, 2), new Vector2Int(3, 2), 1, profile, topology);
        manager.RefreshWindows(plan);
        var result = new JObject
        {
            ["plan"] = ObserverWindowPlan(plan),
            ["merged"] = ObserverWindowSnapshot(manager),
            ["seamLeaseCount"] = world.Chunks[new RuntimeAddress("probe", new Int2(12, 0))].SimulationLeaseCount
        };
        manager.RefreshWindows(new[] { plan[0] });
        result["single"] = ObserverWindowSnapshot(manager);
        result["unexpectedGenerationCount"] = generator.Calls;
        return result;
    }

    private static void ObserverWindowCommit(WorldRuntime world, ChunkGenerationProfileSnapshot profile, int x, int y)
    {
        var address = new RuntimeAddress("probe", new Int2(x, y));
        ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
        using var terrain = new ChunkTerrainBuffer(profile.Width, profile.Height);
        using var generated = new ChunkGenerationResult(request, terrain);
        if (!world.TryCommit(generated, out string reason))
            throw new InvalidOperationException("观察者夹具地形提交失败：" + reason);
    }

    private static JArray ObserverWindowPlan(IReadOnlyList<ChunkWindowRequest> plan)
    {
        var rows = new JArray();
        for (int i = 0; i < plan.Count; i++)
            rows.Add(new JObject
            {
                ["x"] = plan[i].Center.ChunkOrigin.X, ["y"] = plan[i].Center.ChunkOrigin.Y,
                ["activeX"] = plan[i].ActiveDistanceX, ["activeY"] = plan[i].ActiveDistanceY,
                ["presentationX"] = plan[i].PresentationDistanceX, ["presentationY"] = plan[i].PresentationDistanceY
            });
        return rows;
    }

    private static JObject ObserverWindowSnapshot(RuntimeChunkManager manager)
    {
        int active = 0, leases = 0, presentation = 0;
        foreach (ChunkRuntime chunk in manager.Chunks.Values)
        {
            if (chunk.SimulationStatus == ChunkSimulationStatus.Active) active++;
            leases += chunk.SimulationLeaseCount;
        }
        foreach (RuntimeAddress address in manager.PresentationDemand) presentation++;
        return new JObject { ["chunks"] = manager.Chunks.Count, ["active"] = active,
            ["simulationLeases"] = leases, ["presentation"] = presentation,
            ["pending"] = manager.HasPendingChunkLoads };
    }

    private sealed class ObserverWindowAddressNormalizer : IWorldAddressNormalizer
    {
        private readonly ChunkGenerationTopologySnapshot topology;
        public ObserverWindowAddressNormalizer(ChunkGenerationTopologySnapshot topology) => this.topology = topology;
        public RuntimeAddress Normalize(RuntimeAddress address) => new(address.DimensionId,
            new Int2(topology.NormalizeX(address.ChunkOrigin.X), topology.NormalizeY(address.ChunkOrigin.Y)));
    }

    private sealed class ObserverWindowUnexpectedGenerator : IChunkPureGenerator
    {
        public int Calls;
        public object GetPendingGenerationGroupKey(ChunkGenerationRequest request) => null;
        public ChunkGenerationResult Generate(ChunkGenerationRequest request, CancellationToken cancellationToken,
            ChunkGenerationTiming timing = null)
        {
            Interlocked.Increment(ref Calls);
            throw new InvalidOperationException("预提交地形的观察者规则不应启动后台生成");
        }
    }

    #endregion
}
