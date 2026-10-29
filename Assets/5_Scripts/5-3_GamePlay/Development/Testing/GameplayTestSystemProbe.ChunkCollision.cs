using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 区块物理与视觉解耦

    // 在自有原生场景实走生成、租约和回收，碰撞命中由正式 PhysicsScene2D 返回。
    private static JObject ServiceChunkPhysicsLifecycle(JObject args)
    {
        const string key = "probe:chunk-physics";
        using var world = new WorldRuntime(key, 1);
        using var replacement = new WorldRuntime(key, 2);
        using var foreign = new WorldRuntime("probe:chunk-physics-other", 1);
        using var physics = new WorldPhysicsService2D(key, WorldPhysicsScene2D.CreateIsolated(key));
        using var otherPhysics = new WorldPhysicsService2D(foreign.WorldId,
            WorldPhysicsScene2D.CreateIsolated(foreign.WorldId));
        var address = new FlatWorld.WorldModel.WorldAddress("probe:terrain", new Int2(0, 0));
        var profile = new ChunkGenerationProfileSnapshot("probe:terrain", 1, 4, 4);
        TerrainCell Wall() => new(1, 0, 2, 1, 1, TerrainCellFlags.Blocking);
        TerrainCell Floor() => new(1, 0, 0, 1, 1, TerrainCellFlags.Walkable);
        ChunkRuntime Commit(WorldRuntime model, bool wall)
        {
            ChunkGenerationRequest request = model.BeginChunkGeneration(address, 1, profile);
            using var buffer = new ChunkTerrainBuffer(4, 4);
            buffer.SetCell(0, 0, wall ? Wall() : Floor());
            using var generated = new ChunkGenerationResult(request, buffer);
            if (!model.TryCommit(generated, out string reason)) throw new InvalidOperationException(reason);
            model.TryGetChunk(address, out ChunkRuntime chunk);
            return chunk;
        }
        Collider2D Hit(WorldPhysicsService2D service)
        {
            Physics2D.SyncTransforms();
            return service.PrimarySpace.PhysicsScene.Raycast(new Vector2(-.5f, .5f), Vector2.right, 2f).collider;
        }

        physics.BindChunkWorld(world);
        var result = new JObject();
        using ChunkLease simulation = world.AcquireChunkLease(address, ChunkLeaseKind.Simulation);
        result["unreadyHasNoPhysics"] = physics.ChunkPhysicsCount == 0;
        ChunkRuntime chunk = Commit(world, true);
        if (!physics.TryGetChunkPhysics(address, out WorldChunkPhysicsProjection2D projection))
            throw new InvalidOperationException("就绪模拟区块没有创建世界物理投影。");
        Collider2D original = projection.SourceCollider;
        result["noViewNativeCollision"] = chunk.PresentationLeaseCount == 0 && Hit(physics) == original &&
            projection.GetComponentInParent<ChunkView>() == null &&
            projection.GetComponentsInChildren<Renderer>(true).Length == 0;
        result["notViewRenderer"] = !typeof(IChunkViewRenderer).IsAssignableFrom(projection.GetType());
        result["worldOwnsCollider"] = WorldPhysicsService2D.TryResolveCollider(original, out var binding) &&
            binding.World == physics && ReferenceEquals(binding.Entity, chunk);

        using ChunkLease presentation = chunk.AcquireLease(ChunkLeaseKind.Presentation);
        chunk.MarkPresentationBound();
        presentation.Dispose();
        result["presentationReleaseKeepsPhysics"] = physics.ChunkPhysicsCount == 1 && Hit(physics) == original;
        chunk.Terrain.SetCell(0, 0, Floor());
        result["terrainRemovalUpdatesPhysics"] = Hit(physics) == null;
        chunk.Terrain.SetCell(0, 0, Wall());
        result["terrainRestoreUpdatesPhysics"] = Hit(physics) == original;
        ChunkTerrainData oldTerrain = chunk.Terrain;
        chunk = Commit(world, false);
        result["recommitRebindsTerrain"] = oldTerrain.IsDisposed && Hit(physics) == null &&
            ReferenceEquals(projection.BoundTerrain, chunk.Terrain);
        chunk.Terrain.SetCell(0, 0, Wall());
        result["replacementTerrainUpdatesPhysics"] = Hit(physics) == original;

        using ChunkLease retainedPresentation = chunk.AcquireLease(ChunkLeaseKind.Presentation);
        using ChunkLease secondSimulation = chunk.AcquireLease(ChunkLeaseKind.Simulation);
        simulation.Dispose();
        result["otherSimulationKeepsPhysics"] = physics.ChunkPhysicsCount == 1 && Hit(physics) == original;
        secondSimulation.Dispose();
        result["dormantPresentationHasNoPhysics"] = chunk.PresentationLeaseCount == 1 &&
            physics.ChunkPhysicsCount == 0 && Hit(physics) == null &&
            !WorldPhysicsService2D.TryResolveCollider(original, out _);
        using ChunkLease resumed = chunk.AcquireLease(ChunkLeaseKind.Simulation);
        result["pooledPhysicsReused"] = physics.TryGetChunkPhysics(address, out var reused) && reused == projection &&
            reused.SourceCollider == original && Hit(physics) == original;

        using ChunkLease foreignSimulation = foreign.AcquireChunkLease(address, ChunkLeaseKind.Simulation);
        ChunkRuntime foreignChunk = Commit(foreign, true);
        otherPhysics.BindChunkWorld(foreign);
        result["lateBindAndWorldIsolation"] = otherPhysics.ChunkPhysicsCount == 1 &&
            Hit(otherPhysics) != original && WorldPhysicsService2D.TryResolveCollider(Hit(otherPhysics), out var otherBinding) &&
            otherBinding.World == otherPhysics && ReferenceEquals(otherBinding.Entity, foreignChunk);
        bool rejected = false;
        try { physics.BindChunkWorld(foreign); }
        catch (ArgumentException) { rejected = true; }
        result["foreignModelRejected"] = rejected && Hit(physics) == original;

        using ChunkLease replacementSimulation = replacement.AcquireChunkLease(address, ChunkLeaseKind.Simulation);
        Commit(replacement, true);
        physics.BindChunkWorld(replacement);
        world.TryGetChunk(address, out var oldChunk);
        oldChunk.Terrain.SetCell(0, 0, Floor());
        resumed.Dispose();
        retainedPresentation.Dispose();
        bool evicted = world.EvictChunk(address);
        result["oldWorldEventsDetached"] = evicted && physics.ChunkPhysicsCount == 1 && Hit(physics) != null;
        physics.UnbindChunkWorld(world);
        result["staleOwnerCannotUnbind"] = physics.ChunkPhysicsCount == 1;
        physics.UnbindChunkWorld(replacement);
        Commit(replacement, true);
        result["detachedWorldCannotRecreate"] = physics.ChunkPhysicsCount == 0 && Hit(physics) == null;
        physics.BindChunkWorld(replacement);
        Collider2D disposedCollider = Hit(physics);
        physics.Dispose();
        result["worldDisposeReleasesPhysics"] = physics.ChunkPhysicsCount == 0 && !physics.IsValid &&
            !WorldPhysicsService2D.TryResolveCollider(disposedCollider, out _);

#if UNITY_EDITOR
        GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/2_Prefabs/World/WorldModel/ChunkView.prefab");
        result["visualPrefabHasNoPhysics"] = prefab != null && prefab.transform.Find("Blocking") == null &&
            prefab.GetComponentsInChildren<Collider2D>(true).Length == 0 &&
            prefab.GetComponentsInChildren<Rigidbody2D>(true).Length == 0 &&
            prefab.GetComponentsInChildren<WorldChunkPhysicsProjection2D>(true).Length == 0;
#endif
        return result;
    }

    #endregion

    #region 世界碰撞脏批次

    // 独立实例实走正式队列，停用的空组件只作身份，不创建物理几何或访问用户世界。
    private static JObject ServiceCollisionDirtyQueue(JObject args)
    {
        const string firstWorld = "probe:collision-a", secondWorld = "probe:collision-b";
        var queue = new WorldChunkPhysicsProjection2D.ObstacleDirtyQueue();
        var batch = new List<WorldChunkPhysicsProjection2D>();
        var firstRoot = new GameObject("Collision queue probe A");
        var secondRoot = new GameObject("Collision queue probe B");
        var staleRoot = new GameObject("Collision queue probe destroyed");
        firstRoot.SetActive(false);
        secondRoot.SetActive(false);
        staleRoot.SetActive(false);
        try
        {
            var first = firstRoot.AddComponent<WorldChunkPhysicsProjection2D>();
            var second = secondRoot.AddComponent<WorldChunkPhysicsProjection2D>();
            var stale = staleRoot.AddComponent<WorldChunkPhysicsProjection2D>();
            bool emptyInitially = !queue.HasPending(firstWorld) && !queue.HasPending(secondWorld) && !queue.HasPending(null);
            queue.Add(firstWorld, first);
            queue.Add(firstWorld, first);
            queue.Add(secondWorld, second);
            bool bothPending = queue.HasPending(firstWorld) && queue.HasPending(secondWorld);
            queue.Take(firstWorld, batch);
            bool deduplicated = batch.Count == 1 && batch[0] == first;
            bool isolated = !queue.HasPending(firstWorld) && queue.HasPending(secondWorld);

            queue.Add(firstWorld, first);
            bool requeued = queue.HasPending(firstWorld) && batch.Count == 1 && batch[0] == first;
            queue.Take(firstWorld, batch);
            bool nextBatch = batch.Count == 1 && batch[0] == first && !queue.HasPending(firstWorld);

            queue.Add(firstWorld, first);
            queue.Remove(firstWorld, first);
            queue.Add(secondWorld, first);
            queue.Take(firstWorld, batch);
            bool rebound = batch.Count == 0 && queue.HasPending(secondWorld);
            queue.Take(secondWorld, batch);
            rebound &= batch.Count == 2 && batch.Contains(first) && batch.Contains(second);

            queue.Add(firstWorld, stale);
            UnityEngine.Object.DestroyImmediate(staleRoot);
            queue.Take(firstWorld, batch);
            bool destroyedReleased = batch.Count == 0 && !queue.HasPending(firstWorld);

            queue.Add(firstWorld, first);
            queue.Add(secondWorld, second);
            queue.Clear();
            bool cleared = !queue.HasPending(firstWorld) && !queue.HasPending(secondWorld);
            for (int i = 0; i < 8; i++) { queue.Add(firstWorld, first); queue.Take(firstWorld, batch); }
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool stable = true;
            for (int i = 0; i < 1024; i++)
            {
                stable &= !queue.HasPending(firstWorld) && !queue.HasPending(secondWorld);
                queue.Add(firstWorld, first);
                queue.Take(firstWorld, batch);
                stable &= batch.Count == 1 && batch[0] == first;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            return new JObject
            {
                ["emptyInitially"] = emptyInitially, ["bothPending"] = bothPending,
                ["deduplicated"] = deduplicated, ["isolated"] = isolated,
                ["requeued"] = requeued, ["nextBatch"] = nextBatch, ["rebound"] = rebound,
                ["destroyedReleased"] = destroyedReleased, ["cleared"] = cleared,
                ["stable"] = stable, ["iterations"] = 1024, ["allocatedBytes"] = allocated
            };
        }
        finally
        {
            queue.Clear();
            if (firstRoot != null) UnityEngine.Object.DestroyImmediate(firstRoot);
            if (secondRoot != null) UnityEngine.Object.DestroyImmediate(secondRoot);
            if (staleRoot != null) UnityEngine.Object.DestroyImmediate(staleRoot);
        }
    }

    #endregion
}
