using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

public sealed partial class WorldPhysicsService2D
{
    #region 实体代理分配与物理壳池
    internal sealed class EntityProxyShell
    {
        public Rigidbody2D Body;
        public Transform Root;
        public readonly List<Collider2D> AllColliders = new();
        public readonly List<Collider2D> ActiveColliders = new();
        public readonly List<BoxCollider2D> FixedProjectionColliders = new();
        public readonly List<Collider2D> DisabledSourceColliders = new();
        public bool OwnsRoot;
        public bool SharedFixedBody;
    }

    private readonly Dictionary<object, WorldPhysicsEntityProxy2D> entityProxies = new();
    private readonly HashSet<WorldPhysicsEntityProxy2D> movingEntityProxies = new();
    private readonly Stack<EntityProxyShell> entityProxyPool = new();
    private readonly Stack<EntityProxyShell> fixedEntityProxyPool = new();
    private readonly Dictionary<Vector2Int, Rigidbody2D> fixedChunkBodies = new();
    private readonly Dictionary<Rigidbody2D, int> fixedChunkBodyReferences = new();
    private readonly Dictionary<Rigidbody2D, Vector2Int> fixedChunkBodyAddresses = new();

    public static ItemPhysicsDefinitionDto ResolveEntityPhysicsDefinition(ItemPhysicsDefinitionDto configured,
        bool defaultIndependentMovement = false)
    {
        if (configured != null) { ValidateProxyConfiguration(configured); return configured; }
        return new ItemPhysicsDefinitionDto { Mode = defaultIndependentMovement
            ? WorldPhysicsProxyMode2D.IndependentDynamic : WorldPhysicsProxyMode2D.WorldFixed };
    }

    public static WorldPhysicsProxyMode2D ResolveRequiredWorldFixedProxyMode(ItemPhysicsDefinitionDto definition, string entityDescription)
    {
        if (definition == null || definition.Mode == WorldPhysicsProxyMode2D.WorldFixed)
            return WorldPhysicsProxyMode2D.WorldFixed;
        throw new InvalidOperationException($"{entityDescription} 的固定格或网络约束不支持独立可动物理代理。");
    }

    public static bool CanBatchWorldFixedBlockingRegions(ItemPhysicsDefinitionDto definition,
        WorldPhysicsPurpose2D purpose, bool hasContactReceiver = false)
        => (definition == null || definition.Mode == WorldPhysicsProxyMode2D.WorldFixed) &&
           purpose == WorldPhysicsPurpose2D.Blocking && !hasContactReceiver;

    public bool TryGetEntityProxy(object entity, out WorldPhysicsEntityProxy2D proxy)
    {
        proxy = null;
        return entity != null && entityProxies.TryGetValue(entity, out proxy) && proxy.IsValid;
    }

    public bool TryGetItemProxy(Item item, out WorldPhysicsEntityProxy2D proxy) => TryGetEntityProxy(item, out proxy);

    internal static bool TryGetRegisteredItemProxy(Item item, out WorldPhysicsEntityProxy2D proxy)
    {
        proxy = null;
        return item != null && itemOwners.TryGetValue(item, out var owner) && owner.TryGetItemProxy(item, out proxy);
    }

    internal static ItemPhysicsDefinitionDto GetConfiguredItemPhysics(Item item)
    {
        return item?.itemData != null && GameRes.ExistingInstance != null &&
            GameRes.ExistingInstance.TryGetItemDefinition(item.itemData.IDName, out var definition)
                ? definition.Physics : null;
    }

    internal WorldPhysicsEntityProxy2D AllocateEntityProxy(object entity, ItemData snapshot,
        ItemPhysicsDefinitionDto definition, IReadOnlyList<WorldPhysicsEntityRegion2D> regions,
        Action<WorldPhysicsEntityPose2D> applyPose, IGameplayPhysicsDamageTarget damageTarget = null,
        bool stateAuthority = true)
    {
        if (disposed) throw new ObjectDisposedException(nameof(WorldPhysicsService2D));
        if (entity == null || snapshot == null) throw new ArgumentNullException(nameof(entity));
        definition ??= new ItemPhysicsDefinitionDto();
        ValidateProxyConfiguration(definition);
        ValidateEntityPhysicsRegions(regions);
        if (entityProxies.TryGetValue(entity, out var existing))
            throw new InvalidOperationException($"实体已在世界 {WorldKey} 领取物理代理。");
        Vector3 presentationPosition = WorldLocalPresentation.ProjectPosition(snapshot.transform?.position ?? Vector3.zero);
        EntityProxyShell shell = RentEntityProxyShell(definition.Mode, presentationPosition);
        var proxy = new WorldPhysicsEntityProxy2D(this, entity, snapshot, definition, shell, applyPose, stateAuthority);
        try
        {
            if (!shell.SharedFixedBody) ConfigureProxyBody(shell.Body, snapshot, definition, stateAuthority);
            shell.Root.localScale = snapshot.transform?.scale ?? Vector3.one;
            proxy.SetPose(new WorldPhysicsEntityPose2D(presentationPosition,
                snapshot.transform?.rotation.eulerAngles.z ?? 0f, snapshot.PhysicsState?.Velocity ?? Vector2.zero,
                snapshot.PhysicsState?.AngularVelocity ?? 0f));
            if (regions != null)
                for (int i = 0; i < regions.Count; i++)
                {
                    WorldPhysicsEntityRegion2D region = regions[i];
                    // 固定阻挡继续由区块系统裁剪与合批，受击和事件区域保留逐实体身份。
                    if (definition.Mode == WorldPhysicsProxyMode2D.WorldFixed &&
                        region.ChunkBlockingProjection && (region.Purpose & WorldPhysicsPurpose2D.Blocking) != 0) continue;
                    if (region.Collider?.Enabled == false) continue;
                    Collider2D shape = RentEntityRegionCollider(shell, region);
                    RegisterCollider(shape, regionId: region.Id, purpose: region.Purpose, entity: entity,
                        damageTarget: (region.Purpose & WorldPhysicsPurpose2D.Damage) != 0 ? damageTarget : null,
                        receiverModule: region.ReceiverModule);
                    WorldPhysicsRegionTriggerRelay2D relay = shape.GetComponent<WorldPhysicsRegionTriggerRelay2D>();
                    if ((region.Purpose & WorldPhysicsPurpose2D.Trigger) != 0 && stateAuthority &&
                        entity is IWorldPhysicsEntityRegionTriggerReceiver2D receiver)
                    {
                        if (relay == null) relay = shape.gameObject.AddComponent<WorldPhysicsRegionTriggerRelay2D>();
                        relay.Bind(receiver, region.Id, region.ReceiverModule, region.Purpose);
                    }
                    else relay?.ClearBinding();
                }
            entityProxies.Add(entity, proxy);
            if (definition.Mode == WorldPhysicsProxyMode2D.IndependentDynamic) movingEntityProxies.Add(proxy);
            shell.Root.gameObject.SetActive(true);
            return proxy;
        }
        catch { ReleaseEntityProxy(proxy); throw; }
    }

    private static void ValidateProxyConfiguration(ItemPhysicsDefinitionDto definition)
    {
        if (!Enum.IsDefined(typeof(WorldPhysicsProxyMode2D), definition.Mode) ||
            definition.MassSource != WorldPhysicsMassSource2D.ItemStack ||
            !float.IsFinite(definition.LinearDamping) || definition.LinearDamping < 0f ||
            !float.IsFinite(definition.AngularDamping) || definition.AngularDamping < 0f)
            throw new InvalidDataException("物理代理配置包含未知模式、质量来源或无效阻尼。");
    }

    private static void ValidateEntityPhysicsRegions(IReadOnlyList<WorldPhysicsEntityRegion2D> regions)
    {
        if (regions == null) return;
        for (int i = 0; i < regions.Count; i++)
        {
            WorldPhysicsEntityRegion2D region = regions[i];
            if (region.Collider == null || string.IsNullOrWhiteSpace(region.Id) ||
                region.Purpose == 0 || (region.Purpose & ~(WorldPhysicsPurpose2D.Blocking |
                    WorldPhysicsPurpose2D.Damage | WorldPhysicsPurpose2D.Interaction | WorldPhysicsPurpose2D.Trigger)) != 0 ||
                (region.Purpose & WorldPhysicsPurpose2D.Blocking) != 0 &&
                (region.Purpose & (WorldPhysicsPurpose2D.Damage | WorldPhysicsPurpose2D.Trigger)) != 0)
                throw new InvalidDataException("物理区域身份或用途无效，阻挡与受击/触发必须分开。");
            for (int previous = 0; previous < i; previous++)
                if (string.Equals(regions[previous].Id, region.Id, StringComparison.Ordinal))
                    throw new InvalidDataException($"物理区域身份重复：{region.Id}");
            ItemPhysicsDefinitionRuntime.ResolveColliderType(region.Collider.Type);
        }
    }

    private EntityProxyShell RentEntityProxyShell(WorldPhysicsProxyMode2D mode, Vector2 presentationPosition)
    {
        bool fixedMode = mode == WorldPhysicsProxyMode2D.WorldFixed;
        Stack<EntityProxyShell> pool = fixedMode ? fixedEntityProxyPool : entityProxyPool;
        while (pool.Count > 0)
        {
            EntityProxyShell pooled = pool.Pop();
            if (pooled.Root == null) continue;
            if (fixedMode)
            {
                Rigidbody2D body = null;
                try
                {
                    body = GetWorldFixedChunkBody(presentationPosition);
                    pooled.Root.SetParent(body.transform, false);
                    pooled.Body = body;
                }
                catch
                {
                    if (body != null) ReleaseWorldFixedChunkBody(body);
                    pool.Push(pooled);
                    throw;
                }
            }
            return pooled;
        }
        var root = new GameObject("World Entity Physics Proxy") { hideFlags = HideFlags.DontSave };
        root.SetActive(false);
        Rigidbody2D newBody = null;
        try
        {
            SceneManager.MoveGameObjectToScene(root, PrimarySpace.Scene);
            newBody = fixedMode ? GetWorldFixedChunkBody(presentationPosition) : root.AddComponent<Rigidbody2D>();
            if (fixedMode) root.transform.SetParent(newBody.transform, false);
            return new EntityProxyShell { Body = newBody, Root = root.transform, OwnsRoot = true, SharedFixedBody = fixedMode };
        }
        catch
        {
            if (fixedMode && newBody != null) ReleaseWorldFixedChunkBody(newBody);
            DestroyPhysicsObject(root);
            throw;
        }
    }

    private Collider2D RentEntityRegionCollider(EntityProxyShell shell, WorldPhysicsEntityRegion2D region)
    {
        if (region.Collider == null || string.IsNullOrWhiteSpace(region.Id) || region.Purpose == 0)
            throw new InvalidDataException("物理区域必须声明稳定身份、用途与形状。");
        Type type = ItemPhysicsDefinitionRuntime.ResolveColliderType(region.Collider.Type);
        Collider2D collider = null;
        foreach (Collider2D available in shell.AllColliders)
            if (available != null && available.GetType() == type && !shell.ActiveColliders.Contains(available))
            { collider = available; break; }
        if (collider == null)
        {
            var child = new GameObject("Physics Region");
            child.transform.SetParent(shell.Root, false);
            child.SetActive(false);
            try { collider = (Collider2D)child.AddComponent(type); }
            catch { DestroyPhysicsObject(child); throw; }
            shell.AllColliders.Add(collider);
        }
        // 先登记到租约，形状配置异常时回收也能关闭这只碰撞体。
        shell.ActiveColliders.Add(collider);
        collider.gameObject.name = "PhysicsRegion_" + region.Id;
        collider.transform.SetLocalPositionAndRotation(region.LocalPosition, region.LocalRotation);
        collider.transform.localScale = region.LocalScale;
        collider.gameObject.layer = (region.Purpose & WorldPhysicsPurpose2D.Damage) != 0
            ? CombatPhysicsChannels.DamageReceiverLayer : 0;
        ResetColliderPhysicsSettings(collider);
        collider.offset = Vector2.zero;
        collider.enabled = true;
        collider.isTrigger = (region.Purpose & WorldPhysicsPurpose2D.Blocking) == 0;
        ResetColliderShapeDefaults(collider);
        ItemPhysicsDefinitionRuntime.ApplyColliderParameters(collider, region.Collider);
        collider.isTrigger = (region.Purpose & WorldPhysicsPurpose2D.Blocking) == 0;
        collider.gameObject.SetActive(true);
        return collider;
    }

    private static void ResetColliderShapeDefaults(Collider2D collider)
    {
        switch (collider)
        {
            case BoxCollider2D box: box.size = Vector2.one; box.edgeRadius = 0f; break;
            case CircleCollider2D circle: circle.radius = 0.5f; break;
            case CapsuleCollider2D capsule: capsule.size = Vector2.one; capsule.direction = CapsuleDirection2D.Vertical; break;
            case PolygonCollider2D polygon:
                polygon.pathCount = 1;
                polygon.SetPath(0, new[] { new Vector2(-0.5f, -0.5f), new Vector2(-0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, -0.5f) });
                break;
        }
    }

    private static void ResetColliderPhysicsSettings(Collider2D collider)
    {
        // 每次领取都清除上一租户的层过滤与接触策略。
        collider.sharedMaterial = null;
        collider.usedByComposite = false;
        collider.usedByEffector = false;
        collider.layerOverridePriority = 0;
        collider.includeLayers = 0;
        collider.excludeLayers = 0;
        collider.forceSendLayers = ~0;
        collider.forceReceiveLayers = ~0;
        collider.contactCaptureLayers = ~0;
        collider.callbackLayers = ~0;
    }

    private static void CopyColliderPhysicsSettings(Collider2D source, Collider2D target)
    {
        target.sharedMaterial = source.sharedMaterial;
        target.usedByEffector = source.usedByEffector;
        target.layerOverridePriority = source.layerOverridePriority;
        target.includeLayers = source.includeLayers;
        target.excludeLayers = source.excludeLayers;
        target.forceSendLayers = source.forceSendLayers;
        target.forceReceiveLayers = source.forceReceiveLayers;
        target.contactCaptureLayers = source.contactCaptureLayers;
        target.callbackLayers = source.callbackLayers;
    }

    private static void ConfigureProxyBody(Rigidbody2D body, ItemData snapshot,
        ItemPhysicsDefinitionDto definition, bool stateAuthority)
    {
        bool dynamic = definition.Mode == WorldPhysicsProxyMode2D.IndependentDynamic;
        body.simulated = false;
        body.bodyType = dynamic ? stateAuthority ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic : RigidbodyType2D.Static;
        body.gravityScale = 0f;
        body.constraints = definition.FreezeRotation ? RigidbodyConstraints2D.FreezeRotation : RigidbodyConstraints2D.None;
        body.drag = definition.LinearDamping;
        body.angularDrag = definition.AngularDamping;
        body.useAutoMass = false;
        body.mass = ResolveEntityMass(snapshot);
        body.interpolation = RigidbodyInterpolation2D.None;
        body.collisionDetectionMode = dynamic ? CollisionDetectionMode2D.Continuous : CollisionDetectionMode2D.Discrete;
        body.velocity = Vector2.zero;
        body.angularVelocity = 0f;
        body.simulated = true;
    }

    internal static float ResolveEntityMass(ItemData snapshot)
    {
        float mass = snapshot?.Stack?.CurrentWeight ?? 1f;
        return float.IsFinite(mass) && mass > 0f ? Mathf.Max(0.01f, mass) : 0.01f;
    }

    internal void ReleaseEntityProxy(WorldPhysicsEntityProxy2D proxy)
    {
        if (proxy == null || proxy.Released || proxy.Owner != this) return;
        proxy.Released = true;
        proxy.DetachSnapshotObservers();
        movingEntityProxies.Remove(proxy);
        if (entityProxies.TryGetValue(proxy.Entity, out var current) && ReferenceEquals(current, proxy))
            entityProxies.Remove(proxy.Entity);
        EntityProxyShell shell = proxy.Shell;
        foreach (Collider2D collider in shell.ActiveColliders)
        {
            UnregisterCollider(collider);
            if (collider != null) collider.GetComponent<WorldPhysicsRegionTriggerRelay2D>()?.ClearBinding();
            if (shell.OwnsRoot && collider != null)
            { collider.enabled = false; collider.gameObject.SetActive(false); }
        }
        shell.ActiveColliders.Clear();
        foreach (BoxCollider2D collider in shell.FixedProjectionColliders) ReturnObstacleBox(collider);
        shell.FixedProjectionColliders.Clear();
        foreach (Collider2D source in shell.DisabledSourceColliders)
            if (source != null) source.enabled = true;
        shell.DisabledSourceColliders.Clear();
        if (shell.Body == null)
        {
            if (shell.OwnsRoot && shell.Root != null) DestroyPhysicsObject(shell.Root.gameObject);
            return;
        }
        if (shell.Root == null)
        {
            if (shell.SharedFixedBody) ReleaseWorldFixedChunkBody(shell.Body);
            return;
        }
        if (!shell.SharedFixedBody)
        {
            shell.Body.velocity = Vector2.zero;
            shell.Body.angularVelocity = 0f;
        }
        if (!shell.OwnsRoot)
        {
            if (shell.SharedFixedBody) ReleaseWorldFixedChunkBody(shell.Body);
            return;
        }
        if (!shell.SharedFixedBody) shell.Body.simulated = false;
        shell.Root.gameObject.SetActive(false);
        if (shell.SharedFixedBody)
        {
            shell.Root.SetParent(GetPhysicsPoolRoot(), false);
            ReleaseWorldFixedChunkBody(shell.Body);
        }
        shell.Root.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        shell.Root.localScale = Vector3.one;
        if (disposed) DestroyPhysicsObject(shell.Root.gameObject);
        else (shell.SharedFixedBody ? fixedEntityProxyPool : entityProxyPool).Push(shell);
    }

    private void PrepareEntityProxySimulation()
    {
        foreach (WorldPhysicsEntityProxy2D proxy in movingEntityProxies)
            if (proxy.IsParticipatingInWorldPhysics && proxy.MassDirty)
            { proxy.Body.mass = ResolveEntityMass(proxy.Snapshot); proxy.MassDirty = false; }
    }

    private void PublishEntityProxySimulation()
    {
        using var scratch = ListPool<WorldPhysicsEntityProxy2D>.Get(out var proxies);
        proxies.AddRange(movingEntityProxies);
        for (int i = 0; i < proxies.Count; i++) proxies[i].PublishSimulationResult();
    }

    private void DisposeEntityProxies()
    {
        using var scratch = ListPool<WorldPhysicsEntityProxy2D>.Get(out var proxies);
        proxies.AddRange(entityProxies.Values);
        foreach (WorldPhysicsEntityProxy2D proxy in proxies) ReleaseEntityProxy(proxy);
        while (entityProxyPool.Count > 0)
        {
            EntityProxyShell shell = entityProxyPool.Pop();
            if (shell.Root != null) DestroyPhysicsObject(shell.Root.gameObject);
        }
        while (fixedEntityProxyPool.Count > 0)
        {
            EntityProxyShell shell = fixedEntityProxyPool.Pop();
            if (shell.Root != null) DestroyPhysicsObject(shell.Root.gameObject);
        }
        foreach (Rigidbody2D body in fixedChunkBodies.Values)
            if (body != null) DestroyPhysicsObject(body.gameObject);
        fixedChunkBodies.Clear();
        fixedChunkBodyReferences.Clear(); fixedChunkBodyAddresses.Clear();
    }

    private void RegisterConfiguredItemProxy(Item item, ItemPhysicsDefinitionDto definition, List<Collider2D> colliders)
    {
        bool fixedMode = definition.Mode == WorldPhysicsProxyMode2D.WorldFixed;
        var shell = new EntityProxyShell { Body = fixedMode ? GetWorldFixedChunkBody(item.transform.position) : item.GetComponent<Rigidbody2D>(), Root = item.transform,
            OwnsRoot = false, SharedFixedBody = fixedMode };
        shell.ActiveColliders.AddRange(colliders);
        WorldPhysicsEntityProxy2D proxy = null;
        try
        {
            if (fixedMode)
                foreach (Collider2D source in colliders)
                {
                    if (source is not BoxCollider2D box || !source.enabled || source.isTrigger ||
                        !IsPhysicsShapeActiveWithinItem(source.transform, item.transform) || source.usedByEffector || source.usedByComposite ||
                        source.GetComponent<ItemPhysicsRegion2D>()?.ImmutableWorldFixedBlocking != true ||
                        !this.colliders.TryGetValue(source, out var binding) ||
                        !CanBatchWorldFixedBlockingRegions(definition, binding.Purpose, binding.Receiver != null && binding.Receiver is not Item)) continue;
                    Matrix4x4 matrix = source.transform.localToWorldMatrix;
                    Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1);
                    if (Mathf.Abs(Vector3.Dot(x.normalized, y.normalized)) > 0.00001f) continue;
                    BoxCollider2D projected = RentObstacleBox(shell.Body.transform);
                    shell.FixedProjectionColliders.Add(projected);
                    shell.ActiveColliders.Add(projected);
                    projected.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                    projected.transform.localScale = source.transform.lossyScale;
                    projected.gameObject.layer = source.gameObject.layer;
                    CopyColliderPhysicsSettings(source, projected);
                    projected.offset = source.offset; projected.size = box.size; projected.edgeRadius = box.edgeRadius;
                    projected.isTrigger = false; projected.enabled = true;
                    RegisterCollider(projected, item, binding.Receiver, binding.RegionId, binding.Purpose,
                        binding.Entity, binding.DamageTarget, binding.ReceiverModule);
                    source.enabled = false;
                    shell.DisabledSourceColliders.Add(source);
                }
            ItemPhysicsProjection2D.Ensure(item);
            ItemPhysicsProjection2D projection = item.GetComponent<ItemPhysicsProjection2D>();
            proxy = new WorldPhysicsEntityProxy2D(this, item, item.itemData, definition, shell,
                projection != null ? projection.ApplyWorldPhysicsPose : null, GameNetwork.HasStateAuthority, item);
            entityProxies.Add(item, proxy);
            if (definition.Mode == WorldPhysicsProxyMode2D.IndependentDynamic) movingEntityProxies.Add(proxy);
        }
        catch
        {
            if (proxy != null) ReleaseEntityProxy(proxy);
            else
            {
                foreach (BoxCollider2D projected in shell.FixedProjectionColliders) ReturnObstacleBox(projected);
                foreach (Collider2D source in shell.DisabledSourceColliders)
                    if (source != null) source.enabled = true;
                if (shell.SharedFixedBody) ReleaseWorldFixedChunkBody(shell.Body);
            }
            throw;
        }
    }

    private Rigidbody2D GetWorldFixedChunkBody(Vector2 presentationPosition)
    {
        Vector2 chunkSize = default;
        if (GameplayWorldRegistry.TryGet(WorldKey, out var world) && world.Chunks?.ActiveGenerationProfile is { } profile)
            chunkSize = new Vector2(profile.Width, profile.Height);
        Vector2Int chunk = Chunk.GetChunkPosition(WorldLocalPresentation.ToLogical(presentationPosition), chunkSize);
        if (fixedChunkBodies.TryGetValue(chunk, out var body) && body != null)
        {
            fixedChunkBodyReferences[body]++;
            return body;
        }
        var root = new GameObject($"World Fixed Physics Chunk {chunk.x},{chunk.y}") { hideFlags = HideFlags.DontSave };
        try
        {
            SceneManager.MoveGameObjectToScene(root, PrimarySpace.Scene);
            body = root.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Static;
            fixedChunkBodies[chunk] = body;
            fixedChunkBodyReferences[body] = 1; fixedChunkBodyAddresses[body] = chunk;
            return body;
        }
        catch
        {
            if (body != null) { fixedChunkBodyReferences.Remove(body); fixedChunkBodyAddresses.Remove(body); }
            fixedChunkBodies.Remove(chunk);
            DestroyPhysicsObject(root);
            throw;
        }
    }

    private void ReleaseWorldFixedChunkBody(Rigidbody2D body)
    {
        if (body == null || !fixedChunkBodyReferences.TryGetValue(body, out int count)) return;
        if (count > 1) { fixedChunkBodyReferences[body] = count - 1; return; }
        fixedChunkBodyReferences.Remove(body);
        if (fixedChunkBodyAddresses.Remove(body, out Vector2Int chunk)) fixedChunkBodies.Remove(chunk);
        DestroyPhysicsObject(body.gameObject);
    }

    private Transform GetPhysicsPoolRoot()
    {
        if (poolRoot != null) return poolRoot;
        var root = new GameObject("World Physics Pool") { hideFlags = HideFlags.DontSave };
        SceneManager.MoveGameObjectToScene(root, PrimarySpace.Scene);
        return poolRoot = root.transform;
    }

    private void ReprojectEntityPhysicsToLocalAnchor()
    {
        if (disposed || WorldKey != GameplayWorldRegistry.ExecutionWorldKey) return;
        foreach (WorldPhysicsEntityProxy2D proxy in entityProxies.Values)
        {
            if (!proxy.IsValid || proxy.Snapshot.transform == null) continue;
            if (proxy.Item != null)
            {
                for (int i = 0; i < proxy.Shell.FixedProjectionColliders.Count; i++)
                {
                    Collider2D source = proxy.Shell.DisabledSourceColliders[i];
                    BoxCollider2D projectedCollider = proxy.Shell.FixedProjectionColliders[i];
                    if (source != null && projectedCollider != null)
                        projectedCollider.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                }
                continue;
            }
            Vector2 projected = WorldLocalPresentation.ProjectPosition(proxy.Snapshot.transform.position);
            proxy.SetPose(new WorldPhysicsEntityPose2D(projected, proxy.Snapshot.transform.rotation.eulerAngles.z,
                proxy.Snapshot.PhysicsState?.Velocity ?? Vector2.zero, proxy.Snapshot.PhysicsState?.AngularVelocity ?? 0f));
        }
    }
    #endregion
}
