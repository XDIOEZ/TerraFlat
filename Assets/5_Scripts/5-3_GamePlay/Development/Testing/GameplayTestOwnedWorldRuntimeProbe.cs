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
    }

    private static JObject Capture(JObject arguments)
    {
        // 复用生产初始化的身份选择，让逻辑世界与承载场景可以分别取值。
        string owner = ChunkMgr.ResolveOwnedWorldRuntimeIdentity("ship:probe", "surface:probe");
        string context = ChunkMgr.ResolveOwnedWorldRuntimeIdentity(null, "surface:probe");
        string fallback = ChunkMgr.ResolveOwnedWorldRuntimeIdentity(null, null);
        using var world = new WorldRuntime(owner, 1);
        return new JObject
        {
            ["owner"] = owner,
            ["worldId"] = world.WorldId,
            ["context"] = context,
            ["fallback"] = fallback
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
        newScheduler.Advance(0f);
        bool found = world.TryGetChunk(address, out ChunkRuntime after);
        return new JObject
        {
            ["committed"] = committed,
            ["viewReleased"] = viewReleased,
            ["sameWorld"] = ReferenceEquals(newScheduler.World, world),
            ["sameChunk"] = found && ReferenceEquals(before, after),
            ["terrainPreserved"] = found && after.Terrain != null && after.Terrain.GetCell(0, 0).GroundTileId == 17,
            ["retentionCount"] = found ? after.DataRetentionLeaseCount : -1,
            ["newSchedulerTick"] = world.TickIndex == 1
        };
    }

    #endregion
}
