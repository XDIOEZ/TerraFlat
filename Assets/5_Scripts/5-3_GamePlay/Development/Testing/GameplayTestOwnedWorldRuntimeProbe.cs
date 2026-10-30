using System;
using System.Threading;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RuntimeChunkManager = FlatWorld.WorldModel.ChunkMgr;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>采样区块管理器创建世界时使用的逻辑所有者身份。</summary>
public static class GameplayTestOwnedWorldRuntimeProbe
{
    #region 世界模型所有者身份采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register()
    {
        GameplayTestSystemProbe.Register("world_streaming", "runtime_owner_identity", Capture);
        GameplayTestSystemProbe.Register("world_streaming", "runtime_owner_handoff", CaptureHandoff);
        GameplayTestSystemProbe.Register("world_streaming", "runtime_scene_detach", CaptureSceneDetachAndReattach);
    }

    private static JObject Capture(JObject arguments)
    {
        // 复用生产初始化的身份选择，让逻辑世界与承载场景可以分别取值。
        string owner = ChunkMgr.ResolveOwnedWorldRuntimeIdentity("ship:probe", "surface:probe");
        string context = ChunkMgr.ResolveOwnedWorldRuntimeIdentity(null, "surface:probe");
        string fallback = ChunkMgr.ResolveOwnedWorldRuntimeIdentity(null, null);
        string initialWorld = GameplayWorldRegistry.ExecutionWorldKey;
        var emptyScope = default(GameplayWorldRegistry.WorldExecutionScope);
        emptyScope.Dispose();
        using var world = new WorldRuntime(owner, 1);
        return new JObject
        {
            ["owner"] = owner,
            ["worldId"] = world.WorldId,
            ["context"] = context,
            ["fallback"] = fallback,
            ["valueExecutionScope"] = typeof(GameplayWorldRegistry.WorldExecutionScope).IsValueType,
            ["valueMachineScope"] = typeof(MachineWorld.ScopeLease).IsValueType,
            ["emptyScopePreservedContext"] = !emptyScope.IsActive && GameplayWorldRegistry.ExecutionWorldKey == initialWorld
        };
    }

    private sealed class NoGenerationExpected : IChunkPureGenerator
    {
        public object GetPendingGenerationGroupKey(ChunkGenerationRequest request) => null;
        public ChunkGenerationResult Generate(ChunkGenerationRequest request, CancellationToken cancellationToken,
            ChunkGenerationTiming timing = null) => throw new InvalidOperationException("所有权采样不生成区块。");
    }

    private static JObject CaptureHandoff(JObject arguments)
    {
        // 外部世界实际传给新旧调度器，表现租约释放不销毁其正式地形。
        using var world = new WorldRuntime("ship:owner-handoff", 1);
        var address = new RuntimeWorldAddress("interior", new Int2(0, 0));
        var profile = new ChunkGenerationProfileSnapshot("owner-handoff", 1, 2, 2);
        ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
        using var buffer = new ChunkTerrainBuffer(2, 2);
        buffer.SetCell(0, 0, new TerrainCell(17, 0, 0, 1, 1, TerrainCellFlags.Walkable));
        using var generated = new ChunkGenerationResult(request, buffer);
        bool committed = world.TryCommit(generated, out _);
        world.TryGetChunk(address, out ChunkRuntime before);
        using ChunkLease retention = world.AcquireChunkLease(address, ChunkLeaseKind.DataRetention);
        using (ChunkLease view = world.AcquireChunkLease(address, ChunkLeaseKind.Presentation))
            before.MarkPresentationBound();
        bool viewReleased = before.PresentationStatus == ChunkPresentationStatus.Unbound;
        var oldScheduler = new RuntimeChunkManager(world, new NoGenerationExpected());
        oldScheduler.Dispose();
        using var newScheduler = new RuntimeChunkManager(world, new NoGenerationExpected());
        newScheduler.CommitCompleted();
        bool streamingOnlyDidNotAdvance = world.TickIndex == 0;
        world.Tick(1f / 60f);
        bool newSchedulerTick = world.TickIndex == 1;
        world.Tick(1f / 60f);
        bool found = world.TryGetChunk(address, out ChunkRuntime after);
        return new JObject
        {
            ["committed"] = committed,
            ["viewReleased"] = viewReleased,
            ["sameWorld"] = ReferenceEquals(newScheduler.World, world),
            ["sameChunk"] = found && ReferenceEquals(before, after),
            ["terrainPreserved"] = found && after.Terrain != null && after.Terrain.GetCell(0, 0).GroundTileId == 17,
            ["retentionCount"] = found ? after.DataRetentionLeaseCount : -1,
            ["newSchedulerTick"] = newSchedulerTick,
            ["streamingOnlyDidNotAdvance"] = streamingOnlyDidNotAdvance,
            ["fixedStepsAdvanced"] = world.TickIndex == 2
        };
    }

    // 调用正式会话登记和 ChunkMgr 附着接口，验证场景宿主释放后同一世界仍可被重新接管。
    private static JObject CaptureSceneDetachAndReattach(JObject arguments)
    {
        string key = "probe:world-owner-" + Guid.NewGuid().ToString("N");
        var firstRoot = new GameObject("GameplayTest World Owner A") { hideFlags = HideFlags.HideAndDontSave };
        var secondRoot = new GameObject("GameplayTest World Owner B") { hideFlags = HideFlags.HideAndDontSave };
        firstRoot.SetActive(false);
        secondRoot.SetActive(false);
        ChunkMgr first = firstRoot.AddComponent<ChunkMgr>();
        ChunkMgr second = secondRoot.AddComponent<ChunkMgr>();
        first.BindNetworkWorld(key);
        second.BindNetworkWorld(key);
        WorldRuntime world = null;
        WorldRuntime replacement = null;
        try
        {
            world = GameplayWorldRegistry.GetOrCreateOwnedWorldModel(key, 7);
            var address = new RuntimeWorldAddress("probe", new Int2(0, 0));
            var profile = new ChunkGenerationProfileSnapshot("owner-scene-probe", 1, 2, 2);
            ChunkGenerationRequest request = world.BeginChunkGeneration(address, 1, profile);
            using var buffer = new ChunkTerrainBuffer(2, 2);
            buffer.SetCell(0, 0, new TerrainCell(23, 0, 0, 1, 1, TerrainCellFlags.Walkable));
            using var generated = new ChunkGenerationResult(request, buffer);
            bool committed = world.TryCommit(generated, out _);
            world.TryGetChunk(address, out ChunkRuntime before);
            first.AttachOwnedWorldRuntime(world, new DeterministicChunkGenerator());
            bool competingOwnerRejected = false;
            try { second.AttachOwnedWorldRuntime(world, new DeterministicChunkGenerator()); }
            catch (InvalidOperationException) { competingOwnerRejected = true; }
            bool attachedReleaseRejected = !GameplayWorldRegistry.ReleaseOwnedWorldModel(key, world);
            first.DetachAfterPresentationSceneUnloaded();
            bool oldSchedulerDetached = first.RuntimeChunks == null;
            WorldRuntime returned = GameplayWorldRegistry.GetOrCreateOwnedWorldModel(key, 99);
            second.AttachOwnedWorldRuntime(returned, new DeterministicChunkGenerator());
            first.DetachAfterPresentationSceneUnloaded();
            bool found = returned.TryGetChunk(address, out ChunkRuntime after);
            bool sameData = ReferenceEquals(world, returned) && ReferenceEquals(before, after) && found &&
                            after.Terrain != null && after.Terrain.GetCell(0, 0).GroundTileId == 23;
            bool epochPreserved = returned.Epoch == 7;
            bool newOwnerIntact = ReferenceEquals(second.WorldRuntime, returned);
            second.DetachAfterPresentationSceneUnloaded();
            bool explicitlyReleased = GameplayWorldRegistry.ReleaseOwnedWorldModel(key, returned);
            bool disposed = false;
            try { returned.Tick(0); }
            catch (ObjectDisposedException) { disposed = true; }
            replacement = GameplayWorldRegistry.GetOrCreateOwnedWorldModel(key, 8);
            bool staleReleaseRejected = !GameplayWorldRegistry.ReleaseOwnedWorldModel(key, returned);
            replacement.Tick(0);
            bool replacementIntact = replacement.TickIndex == 1;
            bool replacementReleased = GameplayWorldRegistry.ReleaseOwnedWorldModel(key, replacement);
            return new JObject
            {
                ["committed"] = committed,
                ["competingOwnerRejected"] = competingOwnerRejected,
                ["attachedReleaseRejected"] = attachedReleaseRejected,
                ["oldSchedulerDetached"] = oldSchedulerDetached,
                ["sameData"] = sameData,
                ["epochPreserved"] = epochPreserved,
                ["newOwnerIntact"] = newOwnerIntact,
                ["explicitlyReleased"] = explicitlyReleased,
                ["disposed"] = disposed,
                ["staleReleaseRejected"] = staleReleaseRejected,
                ["replacementIntact"] = replacementIntact,
                ["replacementReleased"] = replacementReleased
            };
        }
        finally
        {
            if (first.RuntimeChunks != null) first.DetachAfterPresentationSceneUnloaded();
            if (second.RuntimeChunks != null) second.DetachAfterPresentationSceneUnloaded();
            if (replacement != null) GameplayWorldRegistry.ReleaseOwnedWorldModel(key, replacement);
            if (world != null) GameplayWorldRegistry.ReleaseOwnedWorldModel(key, world);
            UnityEngine.Object.DestroyImmediate(firstRoot);
            UnityEngine.Object.DestroyImmediate(secondRoot);
        }
    }

    #endregion
}
