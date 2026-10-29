using System;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 地形复用通用实体代理
    private static JObject ServiceTerrainPhysicsSource(JObject args)
    {
        const string key = "probe:common-physics";
        using var world = new WorldRuntime(key, 1);
        using var replacement = new WorldRuntime(key, 2);
        using var foreign = new WorldRuntime(key + "-other", 1);
        using var physics = new WorldPhysicsService2D(key, WorldPhysicsScene2D.CreateIsolated(key), fixedBatchCellSize: 8);
        using var otherPhysics = new WorldPhysicsService2D(foreign.WorldId, WorldPhysicsScene2D.CreateIsolated(foreign.WorldId));
        using var terrain = new WorldTerrainPhysicsSource2D(physics);
        using var otherTerrain = new WorldTerrainPhysicsSource2D(otherPhysics);
        var address = new FlatWorld.WorldModel.WorldAddress("probe:terrain", new Int2(0, 0));
        var profile = new ChunkGenerationProfileSnapshot("probe:terrain", 1, 4, 4);
        TerrainCell Wall() => new(1, 0, 2, 1, 1, TerrainCellFlags.Blocking);
        TerrainCell Floor() => new(1, 0, 0, 1, 1, TerrainCellFlags.Walkable);
        ChunkRuntime Commit(WorldRuntime model, bool wall)
        {
            var request = model.BeginChunkGeneration(address, 1, profile);
            using var buffer = new ChunkTerrainBuffer(4, 4);
            buffer.SetCell(0, 0, wall ? Wall() : Floor());
            using var generated = new ChunkGenerationResult(request, buffer);
            if (!model.TryCommit(generated, out string reason)) throw new InvalidOperationException(reason);
            model.TryGetChunk(address, out var chunk);
            return chunk;
        }
        Collider2D Hit(WorldPhysicsService2D owner)
        {
            owner.FlushPendingGeometry();
            Physics2D.SyncTransforms();
            return owner.PrimarySpace.PhysicsScene.Raycast(new Vector2(-.5f, .5f), Vector2.right, 2f).collider;
        }

        terrain.Bind(world);
        var result = new JObject();
        using var simulation = world.AcquireChunkLease(address, ChunkLeaseKind.Simulation);
        result["unreadyHasNoPhysics"] = terrain.BoundTerrainCount == 0 && physics.ColliderCount == 0;
        ChunkRuntime chunk = Commit(world, true);
        Collider2D original = Hit(physics);
        if (!terrain.TryGetSource(address, out var source) || source.Proxy?.IsValid != true)
            throw new InvalidOperationException("地形没有通过通用固定代理提交几何。");
        var initialProxy = source.Proxy;
        result["noViewNativeCollision"] = chunk.PresentationLeaseCount == 0 && original != null &&
            original.GetComponentInParent<ChunkView>() == null &&
            initialProxy.Shell.Root.GetComponentsInChildren<Renderer>(true).Length == 0;
        result["notViewRenderer"] = !typeof(MonoBehaviour).IsAssignableFrom(typeof(WorldTerrainPhysicsSource2D));
        result["worldOwnsCollider"] = WorldPhysicsService2D.TryResolveCollider(original, out var binding) &&
            binding.World == physics && ReferenceEquals(binding.Entity, chunk);
        result["genericProxyRegistry"] = physics.TryGetEntityProxy(chunk, out var registered) && ReferenceEquals(registered, initialProxy);
        result["noNativeTilemap"] = initialProxy.Shell.Root.GetComponentsInChildren<UnityEngine.Tilemaps.Tilemap>(true).Length == 0;

        var plainIdentity = new object();
        var snapshot = new Data_GeneralItem(); snapshot.transform.position = new Vector3(2.5f, .5f);
        using var plain = physics.AllocateEntityProxy(plainIdentity, snapshot, new ItemPhysicsDefinitionDto(),
            new[] { new WorldPhysicsEntityRegion2D("body", WorldPhysicsPurpose2D.Blocking,
                new ItemColliderDefinitionDto { Type = nameof(BoxCollider2D), Size = Vector2.one, Enabled = true }) }, null);
        Physics2D.SyncTransforms();
        result["fixedEntityWithoutChunkBackend"] = plain.Colliders.Count == 1 && plain.Colliders[0].OverlapPoint(new Vector2(2.5f, .5f));
        result["sharedGeneralBody"] = plain.Body == initialProxy.Body;
        result["batchSizeIndependent"] = physics.FixedBatchCellSize == 8 &&
            physics.ResolveFixedBatchAddress(new Vector2(-.1f, 7.9f)) == new Vector2Int(-1, 0) &&
            physics.ResolveFixedBatchAddress(new Vector2(8, 0)) == new Vector2Int(1, 0);

        using var presentation = chunk.AcquireLease(ChunkLeaseKind.Presentation);
        chunk.MarkPresentationBound(); presentation.Dispose();
        result["presentationReleaseKeepsPhysics"] = terrain.BoundTerrainCount == 1 && Hit(physics) == original;
        chunk.Terrain.SetCell(0, 0, Floor());
        result["terrainRemovalUpdatesPhysics"] = Hit(physics) == null && !physics.TryGetEntityProxy(chunk, out _);
        chunk.Terrain.SetCell(0, 0, Wall());
        result["terrainRestoreUpdatesPhysics"] = Hit(physics) == original;
        ChunkTerrainData oldTerrain = chunk.Terrain;
        chunk = Commit(world, false);
        result["recommitRebindsTerrain"] = oldTerrain.IsDisposed && terrain.IsBound(chunk) && Hit(physics) == null;
        chunk.Terrain.SetCell(0, 0, Wall());
        result["replacementTerrainUpdatesPhysics"] = Hit(physics) == original;
        using var retained = chunk.AcquireLease(ChunkLeaseKind.Presentation);
        using var second = chunk.AcquireLease(ChunkLeaseKind.Simulation);
        simulation.Dispose();
        result["otherSimulationKeepsPhysics"] = terrain.BoundTerrainCount == 1 && Hit(physics) == original;
        second.Dispose();
        result["dormantPresentationHasNoPhysics"] = chunk.PresentationLeaseCount == 1 && terrain.BoundTerrainCount == 0 &&
            Hit(physics) == null && !WorldPhysicsService2D.TryResolveCollider(original, out _);
        using var resumed = chunk.AcquireLease(ChunkLeaseKind.Simulation);
        result["pooledPhysicsReused"] = Hit(physics) == original && !initialProxy.IsValid && plain.IsValid;

        using var foreignSimulation = foreign.AcquireChunkLease(address, ChunkLeaseKind.Simulation);
        ChunkRuntime foreignChunk = Commit(foreign, true); otherTerrain.Bind(foreign);
        Collider2D foreignHit = Hit(otherPhysics);
        result["lateBindAndWorldIsolation"] = foreignHit != null && foreignHit != original &&
            WorldPhysicsService2D.TryResolveCollider(foreignHit, out var otherBinding) &&
            otherBinding.World == otherPhysics && ReferenceEquals(otherBinding.Entity, foreignChunk);
        bool rejected = false;
        try { terrain.Bind(foreign); } catch (ArgumentException) { rejected = true; }
        result["foreignModelRejected"] = rejected && Hit(physics) == original;
        using var replacementSimulation = replacement.AcquireChunkLease(address, ChunkLeaseKind.Simulation);
        Commit(replacement, true); terrain.Bind(replacement);
        chunk.Terrain.SetCell(0, 0, Floor()); resumed.Dispose(); retained.Dispose();
        result["oldWorldEventsDetached"] = world.EvictChunk(address) && terrain.BoundTerrainCount == 1 && Hit(physics) != null;
        terrain.Unbind(world);
        result["staleOwnerCannotUnbind"] = terrain.BoundTerrainCount == 1;
        terrain.Unbind(replacement); Commit(replacement, true);
        result["detachedWorldCannotRecreate"] = terrain.BoundTerrainCount == 0 && Hit(physics) == null;
        terrain.Bind(replacement);
        Collider2D closing = Hit(physics); physics.Dispose();
        result["worldDisposeReleasesPhysics"] = terrain.BoundTerrainCount == 0 && !physics.IsValid &&
            !WorldPhysicsService2D.TryResolveCollider(closing, out _) && !plain.IsValid;
#if UNITY_EDITOR
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2_Prefabs/World/WorldModel/ChunkView.prefab");
        result["visualPrefabHasNoPhysics"] = prefab != null && prefab.transform.Find("Blocking") == null &&
            prefab.GetComponentsInChildren<Collider2D>(true).Length == 0 && prefab.GetComponentsInChildren<Rigidbody2D>(true).Length == 0;
#endif
        return result;
    }
    #endregion

    #region 通用几何提交队列
    private static JObject ServicePhysicsGeometryQueue(JObject args)
    {
        using var first = new WorldPhysicsService2D("probe:queue-a", WorldPhysicsScene2D.CreateIsolated("probe:queue-a"));
        using var second = new WorldPhysicsService2D("probe:queue-b", WorldPhysicsScene2D.CreateIsolated("probe:queue-b"));
        int calls = 0, otherCalls = 0;
        bool reenter = false;
        Action update = null;
        update = () => { calls++; if (reenter) { reenter = false; first.ScheduleGeometryUpdate(update); } };
        Action other = () => otherCalls++;
        var result = new JObject { ["emptyInitially"] = first.PendingGeometryUpdateCount == 0 && second.PendingGeometryUpdateCount == 0 };
        first.ScheduleGeometryUpdate(update); first.ScheduleGeometryUpdate(update); second.ScheduleGeometryUpdate(other);
        result["bothPending"] = first.PendingGeometryUpdateCount == 1 && second.PendingGeometryUpdateCount == 1;
        first.FlushPendingGeometry();
        result["deduplicated"] = calls == 1;
        result["isolated"] = otherCalls == 0 && second.PendingGeometryUpdateCount == 1;
        reenter = true; first.ScheduleGeometryUpdate(update); first.FlushPendingGeometry();
        result["requeued"] = calls == 2 && first.PendingGeometryUpdateCount == 1;
        first.FlushPendingGeometry();
        result["nextBatch"] = calls == 3 && first.PendingGeometryUpdateCount == 0;
        first.ScheduleGeometryUpdate(update); first.CancelGeometryUpdate(update); second.ScheduleGeometryUpdate(update);
        first.FlushPendingGeometry(); second.FlushPendingGeometry();
        result["rebound"] = calls == 4 && otherCalls == 1;
        using (var cancelled = first.CreateFixedRectangleSource(new object(), Vector2.zero))
            cancelled.SetRectangle(1, Vector2.one * .5f, Vector2.one);
        result["destroyedReleased"] = first.PendingGeometryUpdateCount == 0 && first.ColliderCount == 0;
        int retryCalls = 0;
        Action retry = () => { if (++retryCalls == 1) throw new InvalidOperationException("probe:retry"); };
        first.ScheduleGeometryUpdate(retry);
        bool rejected = false;
        try { first.FlushPendingGeometry(); } catch (InvalidOperationException) { rejected = true; }
        bool retained = first.PendingGeometryUpdateCount == 1;
        first.FlushPendingGeometry();
        result["failedSubmissionRetried"] = rejected && retained && retryCalls == 2 && first.PendingGeometryUpdateCount == 0;
        object identity = new();
        using (var source = first.CreateFixedRectangleSource(identity, Vector2.zero))
        {
            var occupied = first.AllocateEntityProxy(identity, new Data_GeneralItem(), new ItemPhysicsDefinitionDto(), null, null);
            source.SetRectangle(1, Vector2.one * .5f, Vector2.one);
            bool collisionRejected = false;
            try { first.FlushPendingGeometry(); } catch (InvalidOperationException) { collisionRejected = true; }
            occupied.Dispose();
            first.FlushPendingGeometry();
            result["geometryFailureRetried"] = collisionRejected && source.Proxy?.IsValid == true &&
                source.Proxy.Colliders.Count == 1 && first.PendingGeometryUpdateCount == 0;
        }
        CaptureGeometryQueueCancellationBoundaries(first, result);
        CaptureFixedRectanglePrecision(first, result);
        for (int i = 0; i < 8; i++) { first.ScheduleGeometryUpdate(update); first.FlushPendingGeometry(); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        int countBefore = calls;
        for (int i = 0; i < 1024; i++) { first.ScheduleGeometryUpdate(update); first.FlushPendingGeometry(); }
        result["allocatedBytes"] = GC.GetAllocatedBytesForCurrentThread() - before;
        result["stable"] = calls == countBefore + 1024 && first.PendingGeometryUpdateCount == 0;
        result["iterations"] = 1024;
        first.ScheduleGeometryUpdate(update); first.Dispose(); first.FlushPendingGeometry();
        result["cleared"] = first.PendingGeometryUpdateCount == 0 && calls == countBefore + 1024;
        return result;
    }

    // 互相取消的回调不依赖集合遍历顺序，验证已经取出的批次仍尊重来源撤销。
    private static void CaptureGeometryQueueCancellationBoundaries(WorldPhysicsService2D physics, JObject result)
    {
        int cancelledCalls = 0;
        Action left = null, right = null;
        left = () => { cancelledCalls++; physics.CancelGeometryUpdate(right); };
        right = () => { cancelledCalls++; physics.CancelGeometryUpdate(left); };
        physics.ScheduleGeometryUpdate(left); physics.ScheduleGeometryUpdate(right);
        physics.FlushPendingGeometry();
        result["cancelledInFlightSkipped"] = cancelledCalls == 1 && physics.PendingGeometryUpdateCount == 0;

        int replacedCalls = 0;
        void ReplacePending(Action counterpart)
        {
            if (++replacedCalls != 1) return;
            physics.CancelGeometryUpdate(counterpart);
            physics.ScheduleGeometryUpdate(counterpart);
        }
        left = () => ReplacePending(right); right = () => ReplacePending(left);
        physics.ScheduleGeometryUpdate(left); physics.ScheduleGeometryUpdate(right);
        physics.FlushPendingGeometry();
        bool replacementDeferred = replacedCalls == 1 && physics.PendingGeometryUpdateCount == 1;
        physics.FlushPendingGeometry();
        result["cancelledAndRequeuedDeferred"] = replacementDeferred && replacedCalls == 2 && physics.PendingGeometryUpdateCount == 0;

        int failedCalls = 0;
        void CancelOtherAndFailOnce(Action counterpart)
        {
            if (++failedCalls != 1) return;
            physics.CancelGeometryUpdate(counterpart);
            throw new InvalidOperationException("probe:cancelled-tail");
        }
        left = () => CancelOtherAndFailOnce(right); right = () => CancelOtherAndFailOnce(left);
        physics.ScheduleGeometryUpdate(left); physics.ScheduleGeometryUpdate(right);
        try { physics.FlushPendingGeometry(); } catch (InvalidOperationException) { }
        bool onlyLiveRetry = physics.PendingGeometryUpdateCount == 1;
        physics.FlushPendingGeometry();
        result["failureDoesNotReviveCancelled"] = onlyLiveRetry && failedCalls == 2 && physics.PendingGeometryUpdateCount == 0;

        int selfCancelledCalls = 0;
        Action selfCancelled = null;
        selfCancelled = () =>
        {
            selfCancelledCalls++;
            physics.CancelGeometryUpdate(selfCancelled);
            throw new InvalidOperationException("probe:self-cancelled-failure");
        };
        physics.ScheduleGeometryUpdate(selfCancelled);
        try { physics.FlushPendingGeometry(); } catch (InvalidOperationException) { }
        result["failedCancelledCallbackNotRetried"] = selfCancelledCalls == 1 && physics.PendingGeometryUpdateCount == 0;
        physics.CancelGeometryUpdate(selfCancelled);

        int nestedCalls = 0;
        Action nested = null;
        nested = () =>
        {
            if (++nestedCalls != 1) return;
            physics.ScheduleGeometryUpdate(nested);
            physics.FlushPendingGeometry();
        };
        physics.ScheduleGeometryUpdate(nested); physics.FlushPendingGeometry();
        bool nestedDeferred = nestedCalls == 1 && physics.PendingGeometryUpdateCount == 1;
        physics.FlushPendingGeometry();
        result["recursiveFlushDeferred"] = nestedDeferred && nestedCalls == 2 && physics.PendingGeometryUpdateCount == 0;
    }

    // 微小变化必须提交，并与 Unity 原生 Setter 的真实精度契约一致。
    private static void CaptureFixedRectanglePrecision(WorldPhysicsService2D physics, JObject result)
    {
        using var source = physics.CreateFixedRectangleSource(new object(), Vector2.zero);
        source.SetRectangle(1, Vector2.one * .5f, Vector2.one);
        physics.FlushPendingGeometry();
        var collider = (BoxCollider2D)source.Proxy.Colliders[0];
        Vector2 center = new(.500004f, .5f), size = new(1.000004f, 1f);
        source.SetRectangle(1, center, size);
        result["subToleranceGeometrySubmitted"] = physics.PendingGeometryUpdateCount == 1;
        physics.FlushPendingGeometry();
        var referenceRoot = new GameObject("Native rectangle precision reference") { hideFlags = HideFlags.HideAndDontSave };
        referenceRoot.SetActive(false);
        try
        {
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(referenceRoot, physics.PrimarySpace.Scene);
            var native = referenceRoot.AddComponent<BoxCollider2D>();
            native.enabled = false;
            native.offset = Vector2.one * .5f; native.size = Vector2.one;
            native.offset = center; native.size = size;
            result["rectangleRequestedWidth"] = size.x;
            result["rectangleActualWidth"] = collider.size.x;
            result["rectangleNativeWidth"] = native.size.x;
            result["rectangleRequestedCenterX"] = center.x;
            result["rectangleActualCenterX"] = collider.offset.x;
            result["rectangleNativeCenterX"] = native.offset.x;
            result["nativeGeometryContractPreserved"] = collider.size.Equals(native.size) && collider.offset.Equals(native.offset);
        }
        finally { WorldPhysicsService2D.DestroyPhysicsObject(referenceRoot); }
        center = new(.75f, .5f); size = new(1.25f, 1f);
        source.SetRectangle(1, center, size); physics.FlushPendingGeometry();
        result["changedGeometryPreserved"] = collider.size.Equals(size) && collider.offset.Equals(center);
        Vector2 origin = new(.000002f, 0f);
        source.SetOrigin(origin); physics.FlushPendingGeometry();
        result["subToleranceOriginPreserved"] = source.LogicalOrigin.Equals(origin) &&
            Vector2.Distance(source.Proxy.CapturePose().Position, origin) < .0000001f;
        source.SetRectangle(1, center, size); source.SetOrigin(origin);
        result["unchangedGeometryNotScheduled"] = physics.PendingGeometryUpdateCount == 0;
    }
    #endregion
}
