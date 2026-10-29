#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Gameplay.Building;
using FlatWorld.NaturalEntities;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>代理测试只从正式内容和生成入口领取物理主体，Lua 判断实际模拟与回写结果。</summary>
public static partial class GameplayTestWorldPhysicsProxyRuntimeProbe
{
    #region 代理测试登记与自有现场
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register()
    {
        GameplayTestRuntimeProbe.RegisterAsync("world_streaming", "configured_entity_proxies", CaptureConfiguredEntityProxies);
        GameplayTestRuntimeProbe.Register("world_streaming", "resource_proxy_regions", CaptureResourceProxyRegions);
        GameplayTestRuntimeProbe.RegisterAsync("world_streaming", "proxy_chunk_streaming", CaptureProxyChunkStreaming);
        GameplayTestRuntimeProbe.RegisterAsync("world_streaming", "resource_proxy_world_isolation", CaptureResourceProxyWorldIsolation);
        GameplayTestRuntimeProbe.Register("world_streaming", "resource_proxy_native_triggers", CaptureResourceProxyNativeTriggers);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly List<NaturalEntityHandle> Resources = new();
        internal readonly List<Item> Items = new();
        internal readonly Dictionary<string, RuntimeItemDefinition> Definitions = new(StringComparer.Ordinal);
        internal readonly List<Action> CleanupActions = new();
        private bool disposed;

        internal RuntimeItemDefinition InstallPhysicsDefinition(string id, JObject physics, Action<ItemDefinitionDto> configure = null)
        {
            GameRes resources = GameRes.ExistingInstance;
            if (!Definitions.ContainsKey(id)) Definitions.Add(id, resources.ItemDefinitions[id]);
            ItemDefinitionDto source = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().First(value => value.Id == id);
            source = FastCloner.FastCloner.DeepClone(source);
            source.Physics = physics?.ToObject<ItemPhysicsDefinitionDto>();
            configure?.Invoke(source);
            RuntimeItemDefinition definition = ItemDefinitionCatalogLoader.BuildRuntimeDefinition(resources, source);
            resources.ItemDefinitions[id] = definition;
            return definition;
        }

        internal NaturalEntityHandle SpawnResource(RuntimeItemDefinition definition, Vector2 position, ItemData saved = null)
        {
            ItemData data = saved ?? definition.CreateItemData();
            if (saved == null) data.Guid = ItemMgr.Instance.GenerateGuid();
            data.transform ??= new ItemTransform();
            if (saved == null) data.transform.position = position;
            string dimension = ChunkMgr.Instance.ResolveWorldAddress(data.transform.position).DimensionId;
            if (!NaturalEntityEcsService.TryRegister(definition, data.Guid, data.transform.position, 26f, 0f,
                dimension, data, out NaturalEntityHandle handle))
                throw new InvalidOperationException("正式资源生成没有创建配置的自然实体。");
            Resources.Add(handle);
            return handle;
        }

        internal Item SpawnItem(string id, Vector2 position)
        {
            ItemData data = GameRes.ExistingInstance.CreateItemData(id);
            return SpawnItemSnapshot(data, position);
        }

        internal Item SpawnItemSnapshot(ItemData data, Vector2 position)
        {
            data.transform ??= new ItemTransform();
            data.transform.position = position;
            using ItemMgr.PreparedItemHandle prepared = ItemMgr.Instance.PrepareItem(data);
            Item item = ItemMgr.Instance.CommitPreparedItem(prepared, data,
                WorldLocalPresentation.ProjectPosition(position));
            Items.Add(item);
            return item;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = CleanupActions.Count - 1; i >= 0; i--) CleanupActions[i]();
            foreach (NaturalEntityHandle handle in Resources) NaturalEntityEcsService.Remove(handle);
            foreach (Item item in Items)
                if (item != null) ItemMgr.GetExistingInstance()?.DespawnItem(item, saveData: false);
            if (GameRes.ExistingInstance != null)
                foreach (var pair in Definitions) GameRes.ExistingInstance.ItemDefinitions[pair.Key] = pair.Value;
        }
    }

    private static JObject PhysicsJson(string mode) => JObject.Parse("{\"mode\":\"" + mode +
        "\",\"massSource\":\"ItemStack\",\"freezeRotation\":true,\"linearDamping\":0,\"angularDamping\":0}");

    private static WorldPhysicsEntityProxy2D RequireProxy(NaturalEntityHandle handle)
        => NaturalEntityEcsService.TryGetPhysicsProxy(handle, out WorldPhysicsEntityProxy2D proxy) && proxy.IsValid
            ? proxy : throw new InvalidOperationException("正式自然实体没有领取有效物理代理。");

    private static JObject PoseSample(NaturalEntityHandle handle, WorldPhysicsEntityProxy2D proxy)
    {
        if (!NaturalEntityEcsService.TryGetPresentation(handle, out NaturalEntityPresentationState visual) ||
            !NaturalEntityEcsService.TryCapture(handle, out ItemData saved) ||
            !NaturalEntityEcsService.TryGetTargetObservation(handle, out NaturalEntityTargetObservation target))
            throw new InvalidOperationException("运动后的原实体状态不可读取。");
        WorldPhysicsEntityPose2D pose = proxy.CapturePose();
        Vector2 visualPoint = new(visual.Position.x, visual.Position.y);
        return new JObject
        {
            ["x"] = pose.Position.x, ["y"] = pose.Position.y,
            ["visualError"] = Vector2.Distance(visualPoint, pose.Position),
            ["snapshotError"] = Vector2.Distance(saved.transform.position, pose.Position),
            ["targetError"] = Vector2.Distance(WorldLocalPresentation.ToLogical(target.Target.WorldPosition), pose.Position),
            ["rotationError"] = Mathf.Abs(Mathf.DeltaAngle(visual.Rotation, pose.RotationDegrees)),
            ["velocityError"] = Vector2.Distance(saved.PhysicsState.Velocity, pose.Velocity),
            ["guid"] = saved.Guid, ["id"] = saved.IDName,
            ["hasHealth"] = target.HasHealth, ["mode"] = proxy.Mode.ToString(),
            ["bodyType"] = proxy.Body.bodyType.ToString()
        };
    }
    #endregion

    #region 正式生成与原生模拟回写
    private static IEnumerator CaptureConfiguredEntityProxies(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var own = new Fixture();
        GameplayTestRuntimeProbe.RegisterCleanup(own);
        string world = GameplayWorldRegistry.GetItemWorldKey(player);
        Vector2 near = WorldLocalPresentation.ToLogical((Vector2)player.transform.position) + new Vector2(3f, 0f);
        var result = new JObject();
        NaturalEntityHandle fixedTree, movingTree;
        WorldPhysicsEntityProxy2D fixedProxy, movingProxy;
        Vector2 fixedStart, movingStart;
        ChunkTilemapRenderer owner = null;
        using (GameplayWorldRegistry.UseWorld(world))
        {
            RuntimeItemDefinition fixedDefinition = own.InstallPhysicsDefinition("Tree_Pine", PhysicsJson("WorldFixed"));
            fixedTree = own.SpawnResource(fixedDefinition, near + Vector2.down * 2f);
            fixedProxy = RequireProxy(fixedTree);
            RuntimeItemDefinition movingDefinition = own.InstallPhysicsDefinition("Tree_Pine", PhysicsJson("IndependentDynamic"));
            movingTree = own.SpawnResource(movingDefinition, near);
            movingProxy = RequireProxy(movingTree);
            result["sameDefinitionId"] = fixedDefinition.Id == movingDefinition.Id;
            result["sameResourceBackend"] = fixedDefinition.UsesResourceEntities && movingDefinition.UsesResourceEntities;
            result["sameModuleSlots"] = fixedDefinition.ModuleDefinitions.Select(value => value.StableName)
                .SequenceEqual(movingDefinition.ModuleDefinitions.Select(value => value.StableName));
            result["fixedMode"] = fixedProxy.Mode.ToString();
            result["movingMode"] = movingProxy.Mode.ToString();
            result["fixedType"] = fixedProxy.Body.bodyType.ToString();
            result["movingType"] = movingProxy.Body.bodyType.ToString();
            Transform physicsRoot = fixedProxy.Body.transform.parent;
            result["proxiesShareScenePhysicsRoot"] = physicsRoot != null &&
                physicsRoot.name == "Physics" && movingProxy.Body.transform.parent == physicsRoot &&
                physicsRoot.parent?.GetComponent<RuntimeSceneObjectHierarchy>() != null &&
                physicsRoot.gameObject.scene == fixedProxy.Owner.PrimarySpace.Scene;
            result["physicsRootPreservesWorldCoordinates"] = physicsRoot != null &&
                physicsRoot.localToWorldMatrix == Matrix4x4.identity;
            result["mass"] = movingProxy.Body.mass;
            result["expectedMass"] = movingDefinition.CreateItemData().Stack.CurrentWeight;
            result["noFullTreeItem"] = ItemMgr.Instance.GetItemByGuid(PoseSample(movingTree, movingProxy).Value<int>("guid")) == null;
            fixedStart = fixedProxy.CapturePose().Position;
            movingStart = movingProxy.Body.position;
            if (ChunkMgr.Instance.TryGetRuntimeChunkView(near, out ChunkView view))
            {
                owner = view.GetComponentInChildren<ChunkTilemapRenderer>(true);
                NaturalEntityEcsService.BindPresentation(movingTree, owner);
                NaturalEntityEcsService.Present();
                result["renderBefore"] = owner.DepthMesh.CaptureNaturalEntityPhysicsTestVisual(movingTree.Id);
            }
            fixedProxy.Body.AddForce(Vector2.right * fixedProxy.Body.mass * 3f, ForceMode2D.Impulse);
            movingProxy.Body.AddForce(Vector2.right * movingProxy.Body.mass * 3f, ForceMode2D.Impulse);
        }
        float startTime = Time.fixedTime;
        float deadline = Time.realtimeSinceStartup + 12f;
        while (Time.fixedTime - startTime < .6f)
        {
            if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("原生代理固定步推进超时。");
            yield return new WaitForFixedUpdate();
        }
        using (GameplayWorldRegistry.UseWorld(world))
        {
            result["fixedDisplacement"] = Vector2.Distance(fixedStart, fixedProxy.CapturePose().Position);
            result["movingDisplacement"] = Vector2.Distance(movingStart, movingProxy.Body.position);
            result["movingState"] = PoseSample(movingTree, movingProxy);
            result["fixedState"] = PoseSample(fixedTree, fixedProxy);
            result["allRegionsFollowBody"] = movingProxy.Colliders.Count >= 2 && movingProxy.Colliders.All(value =>
                value != null && value.attachedRigidbody == movingProxy.Body &&
                WorldPhysicsService2D.TryResolveCollider(value, out var binding) &&
                ReferenceEquals(binding.Entity, movingProxy.Entity) && binding.World == movingProxy.Owner);
            NaturalEntityEcsService.Present();
            if (owner != null) result["renderAfter"] = owner.DepthMesh.CaptureNaturalEntityPhysicsTestVisual(movingTree.Id);
            WorldPhysicsEntityPose2D beforeSave = movingProxy.CapturePose();
            int oldBody = movingProxy.Body.GetInstanceID();
            Transform oldBodyTransform = movingProxy.Body.transform;
            Transform physicsRoot = oldBodyTransform.parent;
            Collider2D[] oldRegions = movingProxy.Colliders.ToArray();
            bool captured = NaturalEntityEcsService.TryTakeSnapshotAndRemove(movingTree, out ItemData snapshot);
            result["snapshotTaken"] = captured;
            result["oldHandleRemoved"] = !NaturalEntityEcsService.Contains(movingTree);
            result["oldLeaseReleased"] = !movingProxy.IsValid;
            result["oldRegionsUnregistered"] = oldRegions.All(value => !WorldPhysicsService2D.TryResolveCollider(value, out _));
            result["releasedProxyGroupedInPool"] = oldBodyTransform.parent != null &&
                oldBodyTransform.parent.name == "World Physics Pool" && oldBodyTransform.parent.parent == physicsRoot &&
                !oldBodyTransform.gameObject.activeInHierarchy;
            RuntimeItemDefinition current = GameRes.ExistingInstance.ItemDefinitions["Tree_Pine"];
            NaturalEntityHandle restored = own.SpawnResource(current, beforeSave.Position, snapshot);
            WorldPhysicsEntityProxy2D restoredProxy = RequireProxy(restored);
            result["restored"] = PoseSample(restored, restoredProxy);
            result["restoredPoseError"] = Vector2.Distance(restoredProxy.Body.position, beforeSave.Position);
            result["restoredVelocityError"] = Vector2.Distance(restoredProxy.Body.velocity, beforeSave.Velocity);
            result["pooledBodyReused"] = restoredProxy.Body.GetInstanceID() == oldBody;
            result["restoredProxyGroupedInPhysicsRoot"] = restoredProxy.Body.transform.parent == physicsRoot;
            result["newHandle"] = restored != movingTree;
            result["newEntityBinding"] = restoredProxy.Colliders.All(value =>
                WorldPhysicsService2D.TryResolveCollider(value, out var binding) && ReferenceEquals(binding.Entity, restoredProxy.Entity));
            if (owner != null) NaturalEntityEcsService.BindPresentation(restored, owner);

            JObject ordinaryPhysics = PhysicsJson("IndependentDynamic");
            ordinaryPhysics["freezeRotation"] = false;
            RuntimeItemDefinition ordinaryDefinition = own.InstallPhysicsDefinition("Crucible_Building", ordinaryPhysics);
            result["ordinaryUsesCompleteItemBackend"] = !MachineLogicRegistry.UsesRegisteredDataBackend(ordinaryDefinition);
            Item ordinary = own.SpawnItem("Crucible_Building", near + new Vector2(-1f, 2f));
            WorldPhysicsService2D service = GameplayWorldRegistry.CurrentPhysics;
            if (!service.TryGetItemProxy(ordinary, out WorldPhysicsEntityProxy2D ordinaryProxy))
                throw new InvalidOperationException("正式普通 Item 没有按配置领取代理。");
            Vector2 ordinaryStart = ordinaryProxy.Body.position;
            ordinaryProxy.Body.AddForce(Vector2.right * ordinaryProxy.Body.mass * 2f, ForceMode2D.Impulse);
            for (int step = 0; step < 12; step++) service.Simulate(.02f);
            result["ordinaryMode"] = ordinaryProxy.Mode.ToString();
            result["ordinaryDisplacement"] = Vector2.Distance(ordinaryStart, ordinaryProxy.Body.position);
            result["ordinaryTransformError"] = Vector2.Distance(WorldLocalPresentation.ToLogical(ordinary.transform.position), ordinaryProxy.Body.position);
            result["ordinarySnapshotError"] = Vector2.Distance(ordinary.itemData.transform.position, ordinaryProxy.Body.position);

            ordinaryProxy.Body.velocity = new Vector2(1.4f, .6f);
            ordinaryProxy.Body.angularVelocity = 37f;
            int ordinaryBodyId = ordinaryProxy.Body.GetInstanceID();
            Vector2 beforeLegacyRefreshVelocity = ordinaryProxy.Body.velocity;
            float beforeLegacyRefreshAngularVelocity = ordinaryProxy.Body.angularVelocity;
            own.InstallPhysicsDefinition("Crucible_Building", null);
            ordinary.NotifyRuntimeStructureChanged();
            if (!service.TryGetItemProxy(ordinary, out WorldPhysicsEntityProxy2D refreshedLegacyProxy))
                throw new InvalidOperationException("无 JSON 物理配置的普通 Item 结构刷新后没有重新登记动态刚体。");
            result["legacyRefreshMode"] = refreshedLegacyProxy.Mode.ToString();
            result["legacyRefreshBodyPreserved"] = refreshedLegacyProxy.Body.GetInstanceID() == ordinaryBodyId;
            result["legacyRefreshVelocityError"] = Vector2.Distance(refreshedLegacyProxy.Body.velocity, beforeLegacyRefreshVelocity);
            result["legacyRefreshAngularVelocityError"] = Mathf.Abs(refreshedLegacyProxy.Body.angularVelocity - beforeLegacyRefreshAngularVelocity);
            Vector2 beforeLegacyRefreshPosition = refreshedLegacyProxy.Body.position;
            for (int step = 0; step < 8; step++) service.Simulate(.02f);
            result["legacyRefreshContinuedSimulation"] = Vector2.Distance(beforeLegacyRefreshPosition, refreshedLegacyProxy.Body.position);
            ordinary.ModuleSave();
            result["ordinaryVelocityError"] = Vector2.Distance(ordinary.itemData.PhysicsState.Velocity, refreshedLegacyProxy.Body.velocity);

            int ordinaryGuid = ordinary.itemData.Guid;
            ItemData ordinarySaved = FastCloner.FastCloner.DeepClone(ordinary.itemData);
            Vector2 ordinarySavedPosition = ordinarySaved.transform.position;
            own.Items.Remove(ordinary);
            ItemMgr.GetExistingInstance()?.DespawnItem(ordinary, saveData: false);
            own.InstallPhysicsDefinition("Crucible_Building", PhysicsJson("WorldFixed"));
            Item restoredOrdinary = own.SpawnItemSnapshot(ordinarySaved, ordinarySavedPosition);
            if (!service.TryGetItemProxy(restoredOrdinary, out WorldPhysicsEntityProxy2D restoredOrdinaryProxy))
                throw new InvalidOperationException("普通 Item 从动态快照恢复后没有领取固定代理。");
            result["ordinaryFixedRestoredMode"] = restoredOrdinaryProxy.Mode.ToString();
            result["ordinaryFixedRestoredType"] = restoredOrdinaryProxy.Body.bodyType.ToString();
            result["ordinaryFixedRestoredPositionError"] = Vector2.Distance(restoredOrdinaryProxy.CapturePose().Position,
                WorldLocalPresentation.ProjectPosition(ordinarySavedPosition));
            result["ordinaryFixedIdentityUnique"] = restoredOrdinary.itemData.Guid == ordinaryGuid &&
                ReferenceEquals(ItemMgr.Instance.GetItemByGuid(ordinaryGuid), restoredOrdinary);

            CaptureClientNonPredictingKinematicProxy(service, near + new Vector2(4f, 6f), result);
            CaptureNativeEntityContactPushes(own, service, fixedProxy, restoredProxy, result);
            CaptureRotatingEntityRestore(own, service, near + new Vector2(-3f, 4f), result);
            CaptureFixedMachinePhysicsConstraint(own, near + new Vector2(5f, 4f), result);
            CaptureMovingItemBuildingOccupancy(own, service, player, near + new Vector2(-3f, -2f), result);
        }
        yield return null;
        completed(result);
    }

    private static void CaptureClientNonPredictingKinematicProxy(WorldPhysicsService2D service, Vector2 point, JObject result)
    {
        var snapshot = new Data_GeneralItem
        {
            IDName = "probe:client-kinematic-physics",
            Guid = ItemMgr.Instance.GenerateGuid(),
            Stack = new ItemStack { Weight = 1f, Volume = 1f, Stackable = false, CanBePickedUp = false },
            transform = new ItemTransform { position = point, rotation = Quaternion.Euler(0f, 0f, 21f) },
            PhysicsState = new ItemPhysicsRuntimeState { Velocity = new Vector2(3f, -2f), AngularVelocity = 54f }
        };
        var definition = new ItemPhysicsDefinitionDto
        { Mode = WorldPhysicsProxyMode2D.IndependentDynamic, MassSource = WorldPhysicsMassSource2D.ItemStack, FreezeRotation = false };
        var region = new WorldPhysicsEntityRegion2D("probe:client-blocking", WorldPhysicsPurpose2D.Blocking,
            new ItemColliderDefinitionDto { Type = nameof(BoxCollider2D), Enabled = true, Size = Vector2.one },
            chunkBlockingProjection: false);
        var entity = new object();
        WorldPhysicsEntityProxy2D proxy = null;
        try
        {
            proxy = service.AllocateEntityProxy(entity, snapshot, definition, new[] { region }, null, stateAuthority: false);
            Vector2 setPosition = point + new Vector2(.25f, .4f);
            proxy.SetPose(new WorldPhysicsEntityPose2D(setPosition, 38f, new Vector2(-6f, 5f), -73f));
            service.Simulate(.2f);
            WorldPhysicsEntityPose2D pose = proxy.CapturePose();
            result["clientProxyBodyType"] = proxy.Body.bodyType.ToString();
            result["clientProxyParticipation"] = proxy.IsParticipatingInWorldPhysics;
            result["clientProxyNativePositionError"] = Vector2.Distance(pose.Position, setPosition);
            result["clientProxyNativeRotationError"] = Mathf.Abs(Mathf.DeltaAngle(pose.RotationDegrees, 38f));
            result["clientProxyNativeVelocity"] = proxy.Body.velocity.magnitude;
            result["clientProxyNativeAngularVelocity"] = Mathf.Abs(proxy.Body.angularVelocity);
            result["clientProxyAuthorityVelocityPreserved"] = Vector2.Distance(snapshot.PhysicsState.Velocity, new Vector2(3f, -2f));
            result["clientProxyAuthorityAngularVelocityPreserved"] = Mathf.Abs(snapshot.PhysicsState.AngularVelocity - 54f);
        }
        finally { proxy?.Dispose(); }
    }

    private static void CaptureNativeEntityContactPushes(Fixture own, WorldPhysicsService2D service,
        WorldPhysicsEntityProxy2D fixedProxy, WorldPhysicsEntityProxy2D movingProxy, JObject result)
    {
        RuntimeItemDefinition definition = own.InstallPhysicsDefinition("Tree_Pine", PhysicsJson("IndependentDynamic"));
        Vector2 dynamicStart = movingProxy.Body.position;
        Vector2 staticStart = fixedProxy.CapturePose().Position;
        movingProxy.Body.velocity = Vector2.zero;
        NaturalEntityHandle dynamicPusher = own.SpawnResource(definition, dynamicStart + Vector2.left * .8f);
        NaturalEntityHandle staticPusher = own.SpawnResource(definition, staticStart + Vector2.left * .8f);
        WorldPhysicsEntityProxy2D dynamicPusherProxy = RequireProxy(dynamicPusher);
        WorldPhysicsEntityProxy2D staticPusherProxy = RequireProxy(staticPusher);
        dynamicPusherProxy.Body.velocity = Vector2.right * 3f;
        staticPusherProxy.Body.velocity = Vector2.right * 3f;
        bool dynamicContact = false, staticContact = false;
        for (int step = 0; step < 40; step++)
        {
            service.Simulate(.02f);
            dynamicContact |= dynamicPusherProxy.Body.IsTouching(movingProxy.Body.GetComponentsInChildren<Collider2D>()
                .First(value => !value.isTrigger));
            staticContact |= staticPusherProxy.Body.velocity.x < .05f;
        }
        result["nativeCollisionMovedTree"] = Vector2.Distance(dynamicStart, movingProxy.Body.position);
        result["nativeDynamicContact"] = dynamicContact;
        result["nativeStaticContactStoppedPusher"] = staticContact;
        result["nativeCollisionFixedTree"] = Vector2.Distance(staticStart, fixedProxy.CapturePose().Position);
    }

    private static void CaptureRotatingEntityRestore(Fixture own, WorldPhysicsService2D service, Vector2 point, JObject result)
    {
        JObject json = PhysicsJson("IndependentDynamic");
        json["freezeRotation"] = false;
        RuntimeItemDefinition definition = own.InstallPhysicsDefinition("Tree_Pine", json);
        NaturalEntityHandle handle = own.SpawnResource(definition, point);
        WorldPhysicsEntityProxy2D proxy = RequireProxy(handle);
        float initialRotation = proxy.Body.rotation;
        proxy.Body.AddTorque(Mathf.Max(.001f, proxy.Body.inertia) * 2f, ForceMode2D.Impulse);
        for (int step = 0; step < 12; step++) service.Simulate(.02f);
        WorldPhysicsEntityPose2D moved = proxy.CapturePose();
        result["nativeRotationDelta"] = Mathf.Abs(Mathf.DeltaAngle(initialRotation, moved.RotationDegrees));
        result["nativeAngularVelocity"] = moved.AngularVelocity;
        result["rotatingState"] = PoseSample(handle, proxy);
        NaturalEntityEcsService.TryTakeSnapshotAndRemove(handle, out ItemData saved);
        result["savedAngularVelocityError"] = Mathf.Abs(saved.PhysicsState.AngularVelocity - moved.AngularVelocity);
        NaturalEntityHandle restored = own.SpawnResource(definition, point, saved);
        WorldPhysicsEntityProxy2D restoredProxy = RequireProxy(restored);
        result["restoredRotationError"] = Mathf.Abs(Mathf.DeltaAngle(restoredProxy.Body.rotation, moved.RotationDegrees));
        result["restoredAngularVelocityError"] = Mathf.Abs(restoredProxy.Body.angularVelocity - moved.AngularVelocity);
    }

    private static void CaptureFixedMachinePhysicsConstraint(Fixture own, Vector2 point, JObject result)
    {
        const string id = "LiquidTankBlock_Iron";
        RuntimeItemDefinition definition = own.InstallPhysicsDefinition(id, PhysicsJson("IndependentDynamic"));
        ItemData saved = definition.CreateItemData();
        saved.Guid = ItemMgr.Instance.GenerateGuid();
        saved.transform ??= new ItemTransform();
        saved.transform.position = point;
        Mod_Building.SetInstalledDataState(saved);
        bool rejected = false;
        string reason = string.Empty;
        try { MachineWorld.RestoreMachine(saved); }
        catch (InvalidOperationException error) { rejected = true; reason = error.Message; }
        result["dynamicMachineRejected"] = rejected;
        result["dynamicMachineReason"] = reason;
        result["rejectedMachineAbsent"] = MachineWorld.GetById(saved.Guid) == null;
        if (!rejected) MachineWorld.DiscardPlacementCandidate(saved.Guid);
        own.InstallPhysicsDefinition(id, PhysicsJson("WorldFixed"));
        result["fixedMachineMode"] = MachineWorld.ResolveRequiredWorldFixedMachineProxyMode(id).ToString();
    }

    private static void CaptureMovingItemBuildingOccupancy(Fixture own, WorldPhysicsService2D service,
        Player player, Vector2 point, JObject result)
    {
        JObject physics = PhysicsJson("IndependentDynamic");
        physics["freezeRotation"] = false;
        own.InstallPhysicsDefinition("Crucible_Building", physics, source =>
        {
            ItemModuleDefinitionDto module = source.Modules["建筑模块"];
            JObject state = JObject.Parse(module.Data.Value<string>("BitData") ?? "{}");
            state["FootprintWidth"] = 2;
            state["FootprintHeight"] = 1;
            module.Data["BitData"] = state.ToString(Newtonsoft.Json.Formatting.None);
        });

        ItemData source = GameRes.ExistingInstance.CreateItemData("Crucible");
        source.Guid = ItemMgr.Instance.GenerateGuid();
        var slot = new ItemSlot(0) { itemData = source };
        var inventory = new Inventory_Data(new List<ItemSlot> { slot }, "物理代理建筑测试材料");
        Vector2 authorityPosition = WorldLocalPresentation.ToLogical((Vector2)player.transform.position);
        var request = new BuildingPlacementRequest
        {
            SourceData = source,
            SourceInventory = inventory,
            SourceSlot = slot,
            Actor = player,
            Position = new Vector3(point.x, point.y, player.transform.position.z),
            AuthorityPosition = authorityPosition,
            MaximumDistance = Mod_Building.ResolveMaxPlacementDistance(player, Mod_InteractSender.DefaultMaxInteractDistance)
        };
        if (!BuildingPlacementService.TryPlace(request, out BuildingPlacementResult placement, out string reason) ||
            placement?.Item == null)
            throw new InvalidOperationException("完整 Item 建筑正式放置失败：" + reason);

        Item buildingItem = placement.Item;
        own.Items.Add(buildingItem);
        Mod_Building building = buildingItem.GetComponentInChildren<Mod_Building>(true);
        if (building == null || !building.IsInstalled() || !service.TryGetItemProxy(buildingItem, out WorldPhysicsEntityProxy2D proxy))
            throw new InvalidOperationException("完整Item建筑没有经正式链路安装并领取代理。");
        Vector2Int oldCell = Vector2Int.FloorToInt(point);
        result["buildingInitiallyOccupied"] = BuildingOccupancyRegistry.IsOccupied(oldCell) &&
            BuildingOccupancyRegistry.IsOccupied(oldCell + Vector2Int.right);
        proxy.Body.velocity = Vector2.right * 5f;
        proxy.Body.angularVelocity = 180f;
        for (int step = 0; step < 35; step++) service.Simulate(.02f);
        Vector2Int currentCell = Vector2Int.FloorToInt(buildingItem.itemData.transform.position);
        Vector2Int oldSecondCell = oldCell + Vector2Int.right;
        result["buildingMovedCell"] = currentCell != oldCell;
        int quarterTurns = Mathf.RoundToInt(buildingItem.itemData.transform.rotation.eulerAngles.z / 90f) & 3;
        Vector2Int currentSecondCell = currentCell + ((quarterTurns & 1) != 0
            ? Vector2Int.up : Vector2Int.right);
        bool oldAnchorShared = oldCell == currentCell || oldCell == currentSecondCell;
        bool oldSecondShared = oldSecondCell == currentCell || oldSecondCell == currentSecondCell;
        int footprintOverlapCount = (oldAnchorShared ? 1 : 0) + (oldSecondShared ? 1 : 0);
        var footprintOverlap = new JArray();
        if (oldAnchorShared) footprintOverlap.Add($"{oldCell.x},{oldCell.y}");
        if (oldSecondShared) footprintOverlap.Add($"{oldSecondCell.x},{oldSecondCell.y}");
        result["buildingOldFootprint"] = new JArray($"{oldCell.x},{oldCell.y}", $"{oldSecondCell.x},{oldSecondCell.y}");
        result["buildingCurrentFootprint"] = new JArray($"{currentCell.x},{currentCell.y}", $"{currentSecondCell.x},{currentSecondCell.y}");
        result["buildingFootprintOverlap"] = footprintOverlap;
        result["buildingFootprintOverlapCount"] = footprintOverlapCount;
        result["buildingOldExclusiveCellsFreed"] =
            (oldAnchorShared || !BuildingOccupancyRegistry.IsOccupied(oldCell)) &&
            (oldSecondShared || !BuildingOccupancyRegistry.IsOccupied(oldSecondCell));
        result["buildingOverlapCellsRemainOccupied"] =
            (!oldAnchorShared || BuildingOccupancyRegistry.IsOccupied(oldCell)) &&
            (!oldSecondShared || BuildingOccupancyRegistry.IsOccupied(oldSecondCell));
        result["buildingCurrentCellOccupied"] = BuildingOccupancyRegistry.IsOccupied(currentCell);
        result["buildingQuarterTurns"] = quarterTurns;
        result["buildingRotatedFootprintOccupied"] = (quarterTurns & 1) != 0 &&
            BuildingOccupancyRegistry.IsOccupied(currentCell + Vector2Int.up) &&
            !BuildingOccupancyRegistry.IsOccupied(currentCell + Vector2Int.right);

        bool rollbackSucceeded = placement.Rollback();
        Vector2Int rotatedOtherCell = currentCell + ((quarterTurns & 1) != 0 ? Vector2Int.up : Vector2Int.right);
        bool rollbackItemRemoved = buildingItem == null || buildingItem.DestructionHandled;
        bool rollbackProxyReleased = !service.TryGetItemProxy(buildingItem, out _);
        bool rollbackOccupancyReleased = !BuildingOccupancyRegistry.IsOccupied(oldCell) &&
            !BuildingOccupancyRegistry.IsOccupied(oldCell + Vector2Int.right) &&
            !BuildingOccupancyRegistry.IsOccupied(currentCell) &&
            !BuildingOccupancyRegistry.IsOccupied(rotatedOtherCell);
        result["buildingRollbackSucceeded"] = rollbackSucceeded;
        result["buildingRollbackItemRemoved"] = rollbackItemRemoved;
        result["buildingRollbackProxyReleased"] = rollbackProxyReleased;
        result["buildingRollbackOccupancyReleased"] = rollbackOccupancyReleased;
        if (rollbackSucceeded && rollbackItemRemoved && rollbackProxyReleased && rollbackOccupancyReleased)
            own.Items.Remove(buildingItem);
        else
            throw new InvalidOperationException("完整 Item 建筑正式回滚未释放实体、物理代理或占地。");
    }
    #endregion

    #region 资源多区域与用途精筛
    private static JObject CaptureResourceProxyRegions(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var own = new Fixture();
        GameplayTestRuntimeProbe.RegisterCleanup(own);
        string world = GameplayWorldRegistry.GetItemWorldKey(player);
        using var scope = GameplayWorldRegistry.UseWorld(world);
        Vector2 point = WorldLocalPresentation.ToLogical((Vector2)player.transform.position) + new Vector2(4f, 4f);
        JObject json = PhysicsJson("IndependentDynamic");
        json["regions"] = JArray.Parse(@"[
            { 'id':'body:left','purposes':'Blocking','collider':{'type':'BoxCollider2D','size':{'x':0.4,'y':0.4},'offset':{'x':-1,'y':0}} },
            { 'id':'body:right','purposes':'Blocking','collider':{'type':'BoxCollider2D','size':{'x':0.4,'y':0.4},'offset':{'x':1,'y':0}} },
            { 'id':'damage:left','purposes':'Damage','module':'生命值系统模块','collider':{'type':'BoxCollider2D','size':{'x':0.4,'y':0.4},'offset':{'x':-1,'y':0}} },
            { 'id':'damage:right','purposes':'Damage','module':'生命值系统模块','collider':{'type':'BoxCollider2D','size':{'x':0.4,'y':0.4},'offset':{'x':1,'y':0}} },
            { 'id':'interact:left','purposes':'Interaction','module':'生长模块','collider':{'type':'BoxCollider2D','size':{'x':0.4,'y':0.4},'offset':{'x':-1,'y':0}} },
            { 'id':'interact:right','purposes':'Interaction','module':'生长模块','collider':{'type':'BoxCollider2D','size':{'x':0.4,'y':0.4},'offset':{'x':1,'y':0}} }
        ]");
        RuntimeItemDefinition definition = own.InstallPhysicsDefinition("Tree_Pine", json);
        NaturalEntityHandle handle = own.SpawnResource(definition, point);
        WorldPhysicsEntityProxy2D proxy = RequireProxy(handle);
        NaturalEntityEcsService.TryGetTargetObservation(handle, out NaturalEntityTargetObservation observation);
        var interaction = observation.Target as ISpatialInteractionShape;
        Physics2D.SyncTransforms();
        Vector2 gap = proxy.Body.position;
        Collider2D left = proxy.Colliders.First(value => WorldPhysicsService2D.TryResolveCollider(value, out var binding) && binding.RegionId == "body:left");
        Vector2 leftPoint = left.bounds.center;
        Collider2D[] overlaps = new Collider2D[32];
        int gapCount = proxy.Owner.PrimarySpace.PhysicsScene.OverlapPoint(gap, new ContactFilter2D { useTriggers = true }, overlaps);
        int gapOwn = 0;
        for (int i = 0; i < gapCount; i++)
            if (WorldPhysicsService2D.TryResolveCollider(overlaps[i], out var binding) && ReferenceEquals(binding.Entity, proxy.Entity)) gapOwn++;
        int leftCount = proxy.Owner.PrimarySpace.PhysicsScene.OverlapPoint(leftPoint, new ContactFilter2D { useTriggers = true }, overlaps);
        var purposes = new HashSet<string>();
        bool allExact = true;
        for (int i = 0; i < leftCount; i++)
            if (WorldPhysicsService2D.TryResolveCollider(overlaps[i], out var binding) && ReferenceEquals(binding.Entity, proxy.Entity))
            { purposes.Add(binding.RegionId); allExact &= binding.World == proxy.Owner && binding.Item == null; }
        return new JObject
        {
            ["regionCount"] = proxy.Colliders.Count, ["gapOwnRegions"] = gapOwn,
            ["leftBlocking"] = purposes.Contains("body:left"), ["leftDamage"] = purposes.Contains("damage:left"),
            ["leftInteraction"] = purposes.Contains("interact:left"), ["exactResourceIdentity"] = allExact,
            ["gapCanInteract"] = interaction?.ContainsInteractionPoint(WorldLocalPresentation.ProjectPosition(gap)) ?? true,
            ["leftCanInteract"] = interaction?.ContainsInteractionPoint(WorldLocalPresentation.ProjectPosition(leftPoint)) ?? false
        };
    }
    #endregion
}

internal sealed partial class ChunkDepthMeshRenderer
{
    #region 自然资源物理测试只读表现
    // 读取已提交行网格的实际部件矩阵，避免只凭实体位置声称图像已经跟随。
    internal JObject CaptureNaturalEntityPhysicsTestVisual(int entityId)
    {
        if (!entries.TryGetValue(new PartKey(NaturalDomain, entityId, 0), out Entry entry))
            return new JObject { ["exists"] = false };
        Matrix4x4 world = entry.Group.Row.Root.localToWorldMatrix * entry.Visual.Matrix;
        Vector3 origin = world.MultiplyPoint3x4(Vector3.zero);
        return new JObject
        {
            ["exists"] = true, ["x"] = origin.x, ["y"] = origin.y,
            ["sprite"] = entry.Visual.Sprite != null ? entry.Visual.Sprite.name : string.Empty,
            ["rendererEnabled"] = entry.Group.Row.Root.gameObject.activeInHierarchy
        };
    }
    #endregion
}
#endif
