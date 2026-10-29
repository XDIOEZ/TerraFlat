#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using FlatWorld.Combat;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public static partial class GameplayTestWorldPhysicsProxyRuntimeProbe
{
    private sealed class PhysicsTestTransferPresentation
    {
        internal GameplayWorldRuntime World;
        internal ChunkMgr Manager;
        internal ChunkRuntime Chunk;
        internal ChunkView View;
        internal ChunkTilemapRenderer Tilemap;
        internal ChunkCollisionRenderer Collision;
    }

    private sealed class TransferBusyReentrancyProbe
    {
        internal bool Armed, Initialized, SourceStillValidAfterRemove, SourceTakeRejected, SourceRefreshRejected;
        internal bool SourceDamageRejected, PublicSourceRemoveRejected, SourceCaptureAllowed;
        internal bool SourceTransferRejected, TargetStillValidAfterRemove, TargetTakeRejected, TargetRefreshRejected;
        internal bool TargetCaptureAllowed, TargetTransferRejected;
        internal NaturalEntityHandle SourceHandle;
        internal string SourceWorldKey, TargetWorldKey;
        internal ChunkMgr SourceManager;
        internal RuntimeWorldAddress SourceAddress;
        internal Vector2 TargetPosition;
        internal int Guid;

        internal void Initialize(ResourceEntityCapabilityContext context)
        {
            if (!Armed || Initialized) return;
            Initialized = true;
            NaturalEntityEcsService.Remove(SourceHandle);
            SourceStillValidAfterRemove = NaturalEntityEcsService.Contains(SourceHandle);
            SourceTakeRejected = !NaturalEntityEcsService.TryTakeSnapshotAndRemove(SourceHandle, out _);
            SourceRefreshRejected = !NaturalEntityEcsService.TryRefreshDefinition(SourceHandle, context.Definition);
            var damage = new CombatDamageContext
            {
                Attack = new CombatAttackKey { Source = new CombatIdentity { Backend = CombatBackend.External, Value = 1UL } },
                IsTrueDamage = 1,
                Damage = new Unity.Mathematics.float4(1f),
                Clock = new CombatClock { Time = Time.timeAsDouble, DeltaTime = Time.fixedDeltaTime }
            };
            SourceDamageRejected = NaturalEntityEcsService.ApplyDamage(SourceHandle, damage) < 0f;
            NaturalEntityEcsService.TryTransferNaturalEntityToWorld(SourceHandle, TargetWorldKey, TargetPosition,
                out _, out _);
            SourceTransferRejected = NaturalEntityEcsService.Contains(SourceHandle);
            using (GameplayWorldRegistry.UseWorld(SourceWorldKey))
            {
                PublicSourceRemoveRejected = !SourceManager.TryRemoveNaturalItem(SourceAddress, Guid);
                SourceCaptureAllowed = NaturalEntityEcsService.TryCapture(SourceHandle, out _);
            }

            NaturalEntityEcsService.Remove(context.Handle);
            TargetStillValidAfterRemove = NaturalEntityEcsService.Contains(context.Handle);
            TargetTakeRejected = !NaturalEntityEcsService.TryTakeSnapshotAndRemove(context.Handle, out _);
            TargetRefreshRejected = !NaturalEntityEcsService.TryRefreshDefinition(context.Handle, context.Definition);
            TargetCaptureAllowed = NaturalEntityEcsService.TryCapture(context.Handle, out ItemData captured) &&
                captured?.Guid == Guid;
            NaturalEntityEcsService.TryTransferNaturalEntityToWorld(context.Handle, SourceWorldKey,
                context.Snapshot.transform.position, out _, out _);
            TargetTransferRejected = NaturalEntityEcsService.Contains(context.Handle);
        }
    }

    private static PhysicsTestTransferPresentation BindPhysicsTestTransferPresentation(string worldKey, Vector2 point)
    {
        GameplayWorldRuntime world = GameplayWorldRegistry.GetOrCreate(worldKey);
        ChunkMgr manager = world.Chunks;
        RuntimeWorldAddress address;
        ChunkRuntime chunk;
        using (GameplayWorldRegistry.UseWorld(worldKey))
        {
            address = manager.ResolveWorldAddress(point);
            if (!manager.TryGetChunkRuntime(address, out chunk) || chunk?.Terrain == null ||
                chunk.DataStatus != ChunkDataStatus.Ready)
                throw new InvalidOperationException("跨世界迁移目标ChunkRuntime尚未准备好。");
        }
        if (!DimensionManager.Instance.TryGetDefinitionForWorldKey(worldKey, out DimensionDefinition dimension) ||
            dimension.ChunkViewPrefab == null)
            throw new InvalidOperationException("跨世界迁移目标维度没有真实ChunkView Prefab。");
        ChunkView view;
        using (GameplayWorldRegistry.UseWorld(worldKey))
        {
            // 复用正式管理器唯一绑定，避免与目标世界自己的流送视图重复注册自然资源身份。
            view = manager.EnsureRuntimeChunkViewBoundForStateTransfer(address);
        }
        var presentation = new PhysicsTestTransferPresentation
        {
            World = world,
            Manager = manager,
            Chunk = chunk,
            View = view,
            Tilemap = view.GetComponentInChildren<ChunkTilemapRenderer>(true),
            Collision = view.GetComponentInChildren<ChunkCollisionRenderer>(true)
        };
        if (presentation.Tilemap == null || presentation.Collision == null ||
            presentation.Collision.BoundChunk != chunk)
            throw new InvalidOperationException("迁移目标表现没有绑定真实地形与ChunkCollisionRenderer。");
        return presentation;
    }

    #region 正式区块资源领取与迁移
    private static IEnumerator CaptureProxyChunkStreaming(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var own = new Fixture();
        GameplayTestRuntimeProbe.RegisterCleanup(own);
        string world = GameplayWorldRegistry.GetItemWorldKey(player);
        ChunkMgr manager;
        RuntimeWorldAddress sourceAddress;
        Vector2 boundaryStart, targetPoint;
        using (GameplayWorldRegistry.UseWorld(world))
        {
            manager = ChunkMgr.Instance;
            Vector2 playerPoint = WorldLocalPresentation.ToLogical(player.transform.position);
            sourceAddress = manager.ResolveWorldAddress(playerPoint);
            boundaryStart = new Vector2(sourceAddress.ChunkOrigin.X + manager.ActiveGenerationProfile.Width - .2f,
                Mathf.Floor(playerPoint.y) + 3.5f);
            boundaryStart.y = Mathf.Clamp(boundaryStart.y, sourceAddress.ChunkOrigin.Y + 1.5f,
                sourceAddress.ChunkOrigin.Y + manager.ActiveGenerationProfile.Height - 1.5f);
            targetPoint = boundaryStart + Vector2.right * 2f;
        }
        ChunkView source = null, destination = null;
        float deadline = Time.realtimeSinceStartup + 20f;
        while (source == null || destination == null)
        {
            using (GameplayWorldRegistry.UseWorld(world))
            {
                manager.TryGetRuntimeChunkView(boundaryStart, out source);
                manager.TryGetRuntimeChunkView(targetPoint, out destination);
            }
            if (source != null && destination != null) break;
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("跨块代理回归需要两个正式绑定的相邻区块。");
            yield return null;
        }
        var result = new JObject();
        using (GameplayWorldRegistry.UseWorld(world))
        {
            JObject dynamicResourcePhysics = PhysicsJson("IndependentDynamic");
            dynamicResourcePhysics["freezeRotation"] = false;
            RuntimeItemDefinition definition = own.InstallPhysicsDefinition("Tree_Pine", dynamicResourcePhysics);
            ChunkTilemapRenderer sourceOwner = source.GetComponentInChildren<ChunkTilemapRenderer>(true);
            ChunkTilemapRenderer destinationOwner = destination.GetComponentInChildren<ChunkTilemapRenderer>(true);
            ChunkNaturalItemRenderer sourceRenderer = source.GetComponentInChildren<ChunkNaturalItemRenderer>(true);
            IndependentNaturalEntityRegistry registry = manager.IndependentNaturalEntities;
            registry.BindChunk(source.Model, sourceOwner);
            registry.BindChunk(destination.Model, destinationOwner);
            int guid = ItemMgr.Instance.GenerateGuid();
            var placement = new NaturalItemPlacement(guid, definition.Id,
                manager.ActiveGenerationProfile.Width - 1,
                Mathf.FloorToInt(boundaryStart.y) - sourceAddress.ChunkOrigin.Y,
                .3f, 0f, "test:configured-physics-proxy");
            own.CleanupActions.Add(() =>
            {
                using var cleanupWorld = GameplayWorldRegistry.UseWorld(world);
                manager.MarkNaturalItemRemoved(sourceAddress, guid);
                registry.BindChunk(source.Model, sourceOwner);
                registry.BindChunk(destination.Model, destinationOwner);
            });
            result["formalChunkSpawn"] = sourceRenderer.BindGeneratedPhysicsTestResource(placement);
            if (!registry.TryGetGeneratedEntityHandle(sourceAddress, guid, out NaturalEntityHandle handle))
                throw new InvalidOperationException("区块正式生成没有登记可动自然资源身份。");
            WorldPhysicsEntityProxy2D proxy = RequireProxy(handle);
            int bodyId = proxy.Body.GetInstanceID();
            Vector2 start = proxy.Body.position;
            proxy.Body.velocity = Vector2.right * 2f;
            proxy.Body.angularVelocity = 18f;
            WorldPhysicsService2D service = proxy.Owner;
            for (int step = 0; step < 50; step++) service.Simulate(.02f);
            registry.CaptureAll();
            CaptureIndependentNaturalEntityEcologyMotionRoundtrip(world, sourceAddress, guid, proxy.CapturePose(), result);
            NaturalEntityEcsService.TryGetPresentation(handle, out NaturalEntityPresentationState moved);
            Vector2 movedPoint = new(moved.Position.x, moved.Position.y);
            result["crossedChunk"] = manager.ResolveWorldAddress(movedPoint) != sourceAddress;
            result["nativeMovement"] = Vector2.Distance(start, proxy.Body.position);
            result["sameBodyAfterCrossing"] = proxy.IsValid && proxy.Body.GetInstanceID() == bodyId;
            registry.UnbindChunk(source.Model, sourceOwner);
            result["birthChunkUnloadedAlive"] = NaturalEntityEcsService.Contains(handle) && proxy.IsValid;
            result["sourceRebindRecognized"] = sourceRenderer.BindGeneratedPhysicsTestResource(placement);
            result["sourceDidNotClone"] = registry.TryGetGeneratedEntityHandle(sourceAddress, guid, out NaturalEntityHandle same) && same == handle &&
                !sourceRenderer.ContainsGeneratedPhysicsTestStaticResource(guid);
            WorldPhysicsEntityPose2D beforeSleep = proxy.CapturePose();
            registry.UnbindChunk(destination.Model, destinationOwner);
            result["currentChunkUnloadedReleased"] = !NaturalEntityEcsService.Contains(handle) && !proxy.IsValid;
            registry.BindChunk(destination.Model, destinationOwner);
            if (!registry.TryGetGeneratedEntityHandle(sourceAddress, guid, out NaturalEntityHandle restored))
                throw new InvalidOperationException("当前位置区块重载没有恢复原自然资源。");
            WorldPhysicsEntityProxy2D restoredProxy = RequireProxy(restored);
            result["restoredUnique"] = restored != handle && restoredProxy.Body.GetInstanceID() == bodyId;
            result["restoredPositionError"] = Vector2.Distance(restoredProxy.Body.position, beforeSleep.Position);
            result["restoredVelocityError"] = Vector2.Distance(restoredProxy.Body.velocity, beforeSleep.Velocity);
            registry.UnbindChunk(destination.Model, destinationOwner);
            own.InstallPhysicsDefinition("Tree_Pine", PhysicsJson("WorldFixed"));
            result["modeChangedSourceRecognized"] = sourceRenderer.BindGeneratedPhysicsTestResource(placement);
            result["modeChangedSourceNotCloned"] = !sourceRenderer.ContainsGeneratedPhysicsTestStaticResource(guid);
            registry.BindChunk(destination.Model, destinationOwner);
            if (!registry.TryGetGeneratedEntityHandle(sourceAddress, guid, out NaturalEntityHandle fixedHandle))
                throw new InvalidOperationException("模式改为固定后没有从原保存身份恢复。");
            WorldPhysicsEntityProxy2D fixedProxy = RequireProxy(fixedHandle);
            result["restoredChangedMode"] = fixedProxy.Mode.ToString();
            result["restoredChangedType"] = fixedProxy.Body.bodyType.ToString();
            result["changedModePositionError"] = Vector2.Distance(fixedProxy.CapturePose().Position, beforeSleep.Position);
            registry.BindChunk(destination.Model, destinationOwner);
            result["repeatBindOneEntity"] = registry.TryGetGeneratedEntityHandle(sourceAddress, guid, out NaturalEntityHandle repeated) && repeated == fixedHandle;
        }
        global::WorldAddress transferWorldAddress = global::WorldAddress.FromWorldKey(world);
        string transferTargetWorld = transferWorldAddress.WithDimension(transferWorldAddress.IsSurface
            ? global::WorldAddress.CaveDimensionId : global::WorldAddress.SurfaceDimensionId).WorldKey;
        Vector2 transferSourcePoint = new(sourceAddress.ChunkOrigin.X + 4.5f, sourceAddress.ChunkOrigin.Y + 4.5f);
        yield return CaptureGeneratedResourceWorldTransfers(own, world, transferTargetWorld, manager,
            sourceAddress, source, transferSourcePoint, result);
        yield return null;
        completed(result);
    }

    private static IEnumerator CaptureGeneratedResourceWorldTransfers(Fixture own, string sourceWorldKey,
        string targetWorldKey, ChunkMgr sourceManager, RuntimeWorldAddress sourceAddress, ChunkView sourceView,
        Vector2 sourcePosition, JObject result)
    {
        Vector2 targetPosition = sourcePosition;
        yield return GameplayWorldRegistry.PrepareWorld(targetWorldKey, targetPosition);
        PhysicsTestTransferPresentation destination = BindPhysicsTestTransferPresentation(targetWorldKey, targetPosition);
        if (destination == null || destination.Collision?.BoundChunk != destination.Chunk)
            throw new InvalidOperationException("迁移目标没有真实ChunkView和ChunkCollisionRenderer表现租约。");
        ChunkNaturalItemRenderer sourceRenderer = sourceView.GetComponentInChildren<ChunkNaturalItemRenderer>(true);
        if (sourceRenderer == null) throw new InvalidOperationException("迁移源ChunkView缺少自然资源生成器。");
        var busyProbe = new TransferBusyReentrancyProbe();
        const string busyModuleSlot = "probe:world-transfer-busy";
        const string busyModulePrefab = "probe:world-transfer-busy";
        IDisposable busyRegistration = ResourceEntityCapabilityRegistry.Register(busyModulePrefab,
            (RuntimeItemModuleDefinition module, out ResourceEntityCapability capability, out string reason) =>
            {
                reason = null;
                capability = new ResourceEntityCapability(busyProbe.Initialize);
                return true;
            });
        own.CleanupActions.Add(busyRegistration.Dispose);
        Vector2Int sourceLocalCell = Vector2Int.FloorToInt(sourcePosition) -
            new Vector2Int(sourceAddress.ChunkOrigin.X, sourceAddress.ChunkOrigin.Y);
        CaptureGeneratedResourceTransfer(own, sourceWorldKey, targetWorldKey, sourceManager, sourceRenderer,
            sourceAddress, destination, sourceLocalCell, sourcePosition, sourcePosition, busyProbe,
            busyModuleSlot, busyModulePrefab,
            "WorldFixed", "fixedTransfer", result);
        yield return null;
        CaptureGeneratedResourceTransfer(own, sourceWorldKey, targetWorldKey, sourceManager, sourceRenderer,
            sourceAddress, destination, sourceLocalCell + Vector2Int.right, sourcePosition + Vector2.right,
            sourcePosition + Vector2.right * 3f, busyProbe, busyModuleSlot, busyModulePrefab,
            "IndependentDynamic", "dynamicTransfer", result);
    }

    private static void CaptureGeneratedResourceTransfer(Fixture own, string sourceWorldKey, string targetWorldKey,
        ChunkMgr sourceManager, ChunkNaturalItemRenderer sourceRenderer, RuntimeWorldAddress sourceAddress,
        PhysicsTestTransferPresentation destination, Vector2Int sourceLocalCell, Vector2 sourcePosition,
        Vector2 targetPosition, TransferBusyReentrancyProbe busyProbe, string busyModuleSlot,
        string busyModulePrefab, string mode, string prefix, JObject result)
    {
        using var sourceScope = GameplayWorldRegistry.UseWorld(sourceWorldKey);
        JObject physics = PhysicsJson(mode);
        if (mode == "IndependentDynamic") physics["freezeRotation"] = false;
        RuntimeItemDefinition definition = own.InstallPhysicsDefinition("Tree_Pine", physics, source =>
            source.Modules[busyModuleSlot] = new ItemModuleDefinitionDto
            { Prefab = busyModulePrefab, Id = "probe:world-transfer-busy", Enabled = true,
                Data = new JObject(), Parameters = new JObject() });
        int guid = ItemMgr.Instance.GenerateGuid();
        int localX = Mathf.Clamp(sourceLocalCell.x, 1, sourceManager.ActiveGenerationProfile.Width - 2);
        int localY = Mathf.Clamp(sourceLocalCell.y, 1, sourceManager.ActiveGenerationProfile.Height - 2);
        var sourcePlacement = new NaturalItemPlacement(guid, definition.Id, localX, localY,
            sourcePosition.x - Mathf.Floor(sourcePosition.x), sourcePosition.y - Mathf.Floor(sourcePosition.y),
            "test:world-physics-transfer-" + prefix);
        NaturalEntityHandle migratedHandle = default;
        own.CleanupActions.Add(() => CleanupGeneratedResourceTransfer(sourceWorldKey, targetWorldKey,
            sourceManager, sourceAddress, guid, sourcePosition, migratedHandle));
        if (!sourceRenderer.BindGeneratedPhysicsTestResource(sourcePlacement))
            throw new InvalidOperationException("源生成点没有通过正式区块流程登记自然资源。");
        NaturalEntityHandle sourceHandle;
        if (mode == "WorldFixed")
        {
            if (!sourceRenderer.TryGetGeneratedPhysicsTestStaticHandle(guid, out sourceHandle))
                throw new InvalidOperationException("固定自然资源没有保留正式生成句柄。");
        }
        else if (!sourceManager.IndependentNaturalEntities.TryGetGeneratedEntityHandle(sourceAddress, guid, out sourceHandle))
            throw new InvalidOperationException("独立动态自然资源没有保留正式来源句柄。");
        busyProbe.SourceHandle = sourceHandle;
        busyProbe.SourceWorldKey = sourceWorldKey;
        busyProbe.TargetWorldKey = targetWorldKey;
        busyProbe.SourceManager = sourceManager;
        busyProbe.SourceAddress = sourceAddress;
        busyProbe.TargetPosition = targetPosition;
        busyProbe.Guid = guid;
        WorldPhysicsEntityProxy2D sourceProxy = RequireProxy(sourceHandle);
        object sourceEntity = sourceProxy.Entity;
        WorldPhysicsService2D sourcePhysics = sourceProxy.Owner;
        Vector2 sourcePose = sourceProxy.CapturePose().Position;
        if (mode == "IndependentDynamic")
            sourceProxy.SetPose(new WorldPhysicsEntityPose2D(sourcePose, sourceProxy.CapturePose().RotationDegrees,
                new Vector2(0.35f, -0.2f), 14f));
        WorldPhysicsEntityPose2D expectedTransferPose = sourceProxy.CapturePose();
        Vector2 missingPresentationPoint = sourcePosition + new Vector2(0f,
            Mathf.Max(3, destination.Manager.ActiveGenerationProfile.Height) * 8f);
        bool missingPresentationFailed = !NaturalEntityEcsService.TryTransferNaturalEntityToWorld(
            sourceHandle, targetWorldKey, missingPresentationPoint, out _, out string failure);
        result[prefix + "FailureKeepsSource"] = missingPresentationFailed && NaturalEntityEcsService.Contains(sourceHandle) &&
            sourceProxy.IsValid && sourceProxy.IsParticipatingInWorldPhysics && ReferenceEquals(RequireProxy(sourceHandle), sourceProxy);
        result[prefix + "FailureHasReason"] = !string.IsNullOrWhiteSpace(failure);

        NaturalEntityHandle unrelatedSource = own.SpawnResource(definition, targetPosition);
        WorldPhysicsEntityProxy2D unrelatedSourceProxy = RequireProxy(unrelatedSource);
        Collider2D[] sourceRegions = unrelatedSourceProxy.Colliders.ToArray();
        busyProbe.Armed = true;
        bool moved = NaturalEntityEcsService.TryTransferNaturalEntityToWorld(sourceHandle, targetWorldKey,
            targetPosition, out migratedHandle, out string transferFailure);
        busyProbe.Armed = false;
        result[prefix + "Success"] = moved && string.IsNullOrWhiteSpace(transferFailure);
        if (!moved) throw new InvalidOperationException("正式跨世界资源迁移失败：" + transferFailure);
        result[prefix + "BusyTargetInitialized"] = busyProbe.Initialized;
        result[prefix + "BusySourceGuarded"] = busyProbe.SourceStillValidAfterRemove && busyProbe.SourceTakeRejected &&
            busyProbe.SourceRefreshRejected && busyProbe.SourceDamageRejected && busyProbe.PublicSourceRemoveRejected &&
            busyProbe.SourceCaptureAllowed && busyProbe.SourceTransferRejected;
        result[prefix + "BusyTargetGuarded"] = busyProbe.TargetStillValidAfterRemove && busyProbe.TargetTakeRejected &&
            busyProbe.TargetRefreshRejected && busyProbe.TargetCaptureAllowed && busyProbe.TargetTransferRejected;
        result[prefix + "SourceInvalidated"] = !NaturalEntityEcsService.Contains(sourceHandle) && !sourceProxy.IsValid &&
            sourceManager.IsNaturalItemRemoved(sourceAddress, guid);

        using (GameplayWorldRegistry.UseWorld(targetWorldKey))
        {
            WorldPhysicsEntityProxy2D targetProxy = RequireProxy(migratedHandle);
            ItemData targetSnapshot = null;
            NaturalEntityEcsService.TryCapture(migratedHandle, out targetSnapshot);
            RuntimeWorldAddress targetAddress = destination.Manager.ResolveWorldAddress(targetPosition);
            var targetLocalPlacement = new NaturalItemPlacement(guid, definition.Id,
                Mathf.Clamp(Mathf.FloorToInt(targetPosition.x) - targetAddress.ChunkOrigin.X, 0,
                    destination.Chunk.Terrain.Width - 1),
                Mathf.Clamp(Mathf.FloorToInt(targetPosition.y) - targetAddress.ChunkOrigin.Y, 0,
                    destination.Chunk.Terrain.Height - 1), 0f, 0f, "test:target-local-natural-source");
            result[prefix + "TargetMode"] = targetProxy.Mode.ToString();
            result[prefix + "TargetNativeScene"] = targetProxy.Owner.PrimarySpace.PhysicsScene != sourcePhysics.PrimarySpace.PhysicsScene;
            result[prefix + "TargetCollisionRendererBound"] = destination.Collision.BoundChunk == destination.Chunk &&
                ReferenceEquals(destination.World.Physics, targetProxy.Owner);
            result[prefix + "TargetGuidPreserved"] = targetSnapshot?.Guid == guid && targetSnapshot.IDName == definition.Id;
            result[prefix + "TargetSourceWorldPreserved"] = destination.Manager.IndependentNaturalEntities
                .ContainsGeneratedSourceIdentity(targetAddress, targetLocalPlacement) == false &&
                NaturalEntityEcsService.HasGeneratedNaturalEntitySourceInCurrentWorld(sourceWorldKey,
                    sourceAddress.ChunkOrigin.X, sourceAddress.ChunkOrigin.Y, guid);
            result[prefix + "TargetPositionError"] = targetSnapshot?.transform != null
                ? Vector2.Distance(targetSnapshot.transform.position, targetPosition) : float.MaxValue;
            WorldPhysicsEntityPose2D targetPose = targetProxy.CapturePose();
            result[prefix + "TargetVelocityError"] = Vector2.Distance(targetPose.Velocity, expectedTransferPose.Velocity);
            result[prefix + "TargetAngularVelocityError"] = Mathf.Abs(targetPose.AngularVelocity - expectedTransferPose.AngularVelocity);
            result[prefix + "TargetRotationError"] = Mathf.Abs(Mathf.DeltaAngle(targetPose.RotationDegrees, expectedTransferPose.RotationDegrees));
            result[prefix + "TargetProxyUnique"] = targetProxy.IsValid && targetProxy.Owner == destination.World.Physics &&
                targetProxy.Entity != sourceEntity && targetProxy.Body != null;

            using (GameplayWorldRegistry.UseWorld(sourceWorldKey)) sourcePhysics.Simulate(.02f);
            targetProxy.Owner.Simulate(.02f);
            Collider2D[] targetHits = new Collider2D[128];
            int targetHitCount = targetProxy.Owner.PrimarySpace.PhysicsScene.OverlapCircle(targetPosition, .8f,
                new ContactFilter2D { useTriggers = true }, targetHits);
            bool targetEntityHit = false, unrelatedSourceHit = false;
            for (int i = 0; i < targetHitCount; i++)
                if (WorldPhysicsService2D.TryResolveCollider(targetHits[i], out WorldPhysicsColliderBinding2D binding))
                {
                    targetEntityHit |= ReferenceEquals(binding.Entity, targetProxy.Entity);
                    unrelatedSourceHit |= sourceRegions.Contains(targetHits[i]);
                }
            result[prefix + "NativeTargetVisible"] = targetEntityHit;
            result[prefix + "OtherWorldNativeShapeAbsent"] = !unrelatedSourceHit &&
                !SceneContainsPhysicsEntity(targetProxy.Owner, targetPosition, unrelatedSourceProxy.Entity);
            result[prefix + "SourceOtherWorldShapeVisible"] = SceneContainsPhysicsEntity(sourcePhysics,
                targetPosition, unrelatedSourceProxy.Entity);
            result[prefix + "SourceMigratedEntityShapeRemoved"] = !SceneContainsPhysicsEntity(sourcePhysics,
                sourcePosition, sourceEntity);
        }
    }

    private static bool SceneContainsPhysicsEntity(WorldPhysicsService2D service, Vector2 point, object entity)
    {
        Collider2D[] hits = new Collider2D[128];
        int count = service.PrimarySpace.PhysicsScene.OverlapCircle(point, .8f,
            new ContactFilter2D { useTriggers = true }, hits);
        for (int i = 0; i < count; i++)
            if (WorldPhysicsService2D.TryResolveCollider(hits[i], out WorldPhysicsColliderBinding2D binding) &&
                ReferenceEquals(binding.Entity, entity)) return true;
        return false;
    }

    private static void CleanupGeneratedResourceTransfer(string sourceWorldKey, string targetWorldKey,
        ChunkMgr sourceManager, RuntimeWorldAddress sourceAddress, int guid, Vector2 sourcePosition,
        NaturalEntityHandle migratedHandle)
    {
        if (migratedHandle.IsValid && GameplayWorldRegistry.TryGet(targetWorldKey, out _))
        {
            using var targetScope = GameplayWorldRegistry.UseWorld(targetWorldKey);
            if (NaturalEntityEcsService.Contains(migratedHandle))
                NaturalEntityEcsService.TryTransferNaturalEntityToWorld(migratedHandle, sourceWorldKey,
                    sourcePosition, out _, out _);
        }
        using (GameplayWorldRegistry.UseWorld(sourceWorldKey))
        {
            sourceManager.MarkNaturalItemRemoved(sourceAddress, guid);
            if (SaveDataMgr.Instance?.SaveData?.PlanetData_Dict != null &&
                SaveDataMgr.Instance.SaveData.PlanetData_Dict.TryGetValue(sourceWorldKey, out PlanetData planet) &&
                planet?.Ecology?.TryGetChunk(sourceAddress.ChunkOrigin.X, sourceAddress.ChunkOrigin.Y,
                    out EcologyChunkSaveData chunk) == true)
            {
                chunk.RemovedGuids?.Remove(guid);
                chunk.RenewalYears?.Remove(guid);
                chunk.ChangedItems?.RemoveAll(value => value == null || value.Guid == guid);
            }
        }
    }

    private static void CaptureIndependentNaturalEntityEcologyMotionRoundtrip(string sourceWorldKey,
        RuntimeWorldAddress sourceAddress, int guid, WorldPhysicsEntityPose2D expected, JObject result)
    {
        if (SaveDataMgr.Instance?.SaveData?.PlanetData_Dict == null ||
            !SaveDataMgr.Instance.SaveData.PlanetData_Dict.TryGetValue(sourceWorldKey, out PlanetData planet) ||
            planet?.Ecology?.IndependentEntities == null)
            throw new InvalidOperationException("独立自然资源缺少正式生态存档所有权。");
        IndependentNaturalEntitySaveData saved = planet.Ecology.IndependentEntities.FirstOrDefault(value =>
            value != null && value.Guid == guid && value.SourceWorldKey == sourceWorldKey &&
            value.SourceChunkX == sourceAddress.ChunkOrigin.X && value.SourceChunkY == sourceAddress.ChunkOrigin.Y);
        if (saved?.Snapshot?.PhysicsState == null)
            throw new InvalidOperationException("独立自然资源正式存档没有完整物理快照。");
        saved.Snapshot.PhysicsState.LastContactPoint = new Vector2(91f, -37f);
        saved.Snapshot.PhysicsState.LastContactNormal = Vector2.up;
        saved.Snapshot.PhysicsState.LastContactItemGuid = int.MaxValue;
        saved.Snapshot.PhysicsState.ContactVersion = uint.MaxValue;
        var archive = new EcologyWorldSaveData();
        archive.IndependentEntities.Add(saved);
        archive.CaptureChangedItem(sourceAddress.ChunkOrigin.X, sourceAddress.ChunkOrigin.Y, saved.Snapshot);
        byte[] payload = MemoryPack.MemoryPackSerializer.Serialize(archive);
        EcologyWorldSaveData restored = MemoryPack.MemoryPackSerializer.Deserialize<EcologyWorldSaveData>(payload);
        IndependentNaturalEntitySaveData independent = restored.IndependentEntities.FirstOrDefault(value => value?.Guid == guid);
        bool hasChangedItem = restored.TryGetChangedItem(sourceAddress.ChunkOrigin.X, sourceAddress.ChunkOrigin.Y,
            guid, out ItemData changedItem);
        result["independentSaveIdentityPreserved"] = independent != null &&
            independent.SourceWorldKey == sourceWorldKey && independent.SourceChunkX == sourceAddress.ChunkOrigin.X &&
            independent.SourceChunkY == sourceAddress.ChunkOrigin.Y && independent.Guid == guid;
        result["independentSavePositionError"] = independent?.Snapshot?.transform != null
            ? Vector2.Distance(independent.Snapshot.transform.position, expected.Position) : float.MaxValue;
        result["independentSaveVelocityError"] = independent?.Snapshot?.PhysicsState != null
            ? Vector2.Distance(independent.Snapshot.PhysicsState.Velocity, expected.Velocity) : float.MaxValue;
        result["independentSaveAngularVelocityError"] = independent?.Snapshot?.PhysicsState != null
            ? Mathf.Abs(independent.Snapshot.PhysicsState.AngularVelocity - expected.AngularVelocity) : float.MaxValue;
        result["independentSaveContactsCleared"] = independent?.Snapshot?.PhysicsState != null &&
            independent.Snapshot.PhysicsState.LastContactPoint == Vector2.zero &&
            independent.Snapshot.PhysicsState.LastContactNormal == Vector2.zero &&
            independent.Snapshot.PhysicsState.LastContactItemGuid == 0 && independent.Snapshot.PhysicsState.ContactVersion == 0;
        result["changedItemSaveFound"] = hasChangedItem;
        result["changedItemSavePositionError"] = changedItem?.transform != null
            ? Vector2.Distance(changedItem.transform.position, expected.Position) : float.MaxValue;
        result["changedItemSaveVelocityError"] = changedItem?.PhysicsState != null
            ? Vector2.Distance(changedItem.PhysicsState.Velocity, expected.Velocity) : float.MaxValue;
        result["changedItemSaveAngularVelocityError"] = changedItem?.PhysicsState != null
            ? Mathf.Abs(changedItem.PhysicsState.AngularVelocity - expected.AngularVelocity) : float.MaxValue;
        result["changedItemSaveContactsCleared"] = changedItem?.PhysicsState != null &&
            changedItem.PhysicsState.LastContactPoint == Vector2.zero &&
            changedItem.PhysicsState.LastContactNormal == Vector2.zero &&
            changedItem.PhysicsState.LastContactItemGuid == 0 && changedItem.PhysicsState.ContactVersion == 0;
    }
    #endregion

    #region 同坐标真实资源的原生世界隔离
    private static IEnumerator CaptureResourceProxyWorldIsolation(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var own = new Fixture();
        GameplayTestRuntimeProbe.RegisterCleanup(own);
        string firstWorld = GameplayWorldRegistry.GetItemWorldKey(player);
        global::WorldAddress address = global::WorldAddress.FromWorldKey(firstWorld);
        string secondWorld = address.WithDimension(address.IsSurface
            ? global::WorldAddress.CaveDimensionId : global::WorldAddress.SurfaceDimensionId).WorldKey;
        Vector2 point = WorldLocalPresentation.ToLogical((Vector2)player.transform.position) + new Vector2(3f, 6f);
        yield return GameplayWorldRegistry.PrepareWorld(secondWorld, point);
        WorldPhysicsEntityProxy2D first, second;
        using (GameplayWorldRegistry.UseWorld(firstWorld))
        {
            RuntimeItemDefinition definition = own.InstallPhysicsDefinition("Tree_Pine", PhysicsJson("IndependentDynamic"));
            first = RequireProxy(own.SpawnResource(definition, point));
        }
        using (GameplayWorldRegistry.UseWorld(secondWorld))
        {
            RuntimeItemDefinition definition = GameRes.ExistingInstance.ItemDefinitions["Tree_Pine"];
            second = RequireProxy(own.SpawnResource(definition, point));
        }
        first.Body.velocity = Vector2.right * 2f;
        var result = new JObject
        {
            ["differentNativeScenes"] = first.Owner.PrimarySpace.PhysicsScene != second.Owner.PrimarySpace.PhysicsScene,
            ["sameInitialCoordinates"] = Vector2.Distance(first.Body.position, second.Body.position) < .001f
        };
        using (GameplayWorldRegistry.UseWorld(firstWorld))
            for (int step = 0; step < 25; step++) first.Owner.Simulate(.02f);
        result["firstMoved"] = Vector2.Distance(first.Body.position, point);
        result["secondStayed"] = Vector2.Distance(second.Body.position, point);
        result["noCrossWorldNativeContact"] = !first.Body.IsTouching(second.Colliders.First(value => !value.isTrigger));
        foreach (WorldPhysicsEntityProxy2D proxy in new[] { first, second })
        {
            Collider2D[] hits = new Collider2D[64];
            int count = proxy.Owner.PrimarySpace.PhysicsScene.OverlapCircle(point, 3f,
                new ContactFilter2D { useTriggers = true }, hits);
            bool ownHit = false, otherHit = false;
            for (int i = 0; i < count; i++)
            {
                ownHit |= proxy.Colliders.Contains(hits[i]);
                otherHit |= (ReferenceEquals(proxy, first) ? second : first).Colliders.Contains(hits[i]);
            }
            result[ReferenceEquals(proxy, first) ? "firstOnlyOwnRegions" : "secondOnlyOwnRegions"] = ownHit && !otherHit;
        }
        yield return null;
        completed(result);
    }
    #endregion
}

public sealed partial class ChunkNaturalItemRenderer
{
    #region 正式自然生成链路测试入口
    // 仅把自有生成点交给正式区块装配流程，测试不重算实体或代理规则。
    internal bool BindGeneratedPhysicsTestResource(NaturalItemPlacement placement) => TryBindStaticEntity(placement);
    internal bool ContainsGeneratedPhysicsTestStaticResource(int guid) => staticEntities.ContainsKey(guid);
    internal bool TryGetGeneratedPhysicsTestStaticHandle(int guid, out NaturalEntityHandle handle)
    {
        handle = default;
        if (!staticEntities.TryGetValue(guid, out StaticNaturalRuntime runtime)) return false;
        handle = runtime.Handle;
        return NaturalEntityEcsService.Contains(handle);
    }
    #endregion
}
#endif
