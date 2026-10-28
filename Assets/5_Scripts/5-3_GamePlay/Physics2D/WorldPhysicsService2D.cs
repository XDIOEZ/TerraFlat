using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.SceneManagement;

[Flags]
public enum WorldPhysicsPurpose2D : byte
{
    Blocking = 1,
    Damage = 2,
    Interaction = 4,
    Trigger = 8
}

/// <summary>每个区域保留准确来源，阻挡合并不会把任意成员冒充整个形状的受击目标。</summary>
public readonly struct WorldPhysicsColliderBinding2D
{
    #region 区域来源
    public readonly WorldPhysicsService2D World;
    public readonly Item Item;
    public readonly Component Receiver;
    public readonly string RegionId;
    public readonly WorldPhysicsPurpose2D Purpose;
    public readonly object Entity;
    public readonly string ReceiverModule;
    internal readonly IGameplayPhysicsDamageTarget DamageTarget;

    internal WorldPhysicsColliderBinding2D(WorldPhysicsService2D world, Item item, Component receiver,
        string regionId, WorldPhysicsPurpose2D purpose, object entity, IGameplayPhysicsDamageTarget damageTarget,
        string receiverModule = null)
    {
        World = world; Item = item; Receiver = receiver; RegionId = regionId;
        Purpose = purpose; Entity = entity; DamageTarget = damageTarget;
        ReceiverModule = receiverModule;
    }
    #endregion
}

/// <summary>世界拥有碰撞登记、代理池和原生空间，业务模块继续拥有伤害及触发规则。</summary>
public sealed partial class WorldPhysicsService2D : IDisposable
{
    #region 世界与碰撞所有权
    private static readonly Dictionary<Collider2D, WorldPhysicsService2D> colliderOwners = new();
    private static readonly Dictionary<Item, WorldPhysicsService2D> itemOwners = new();
    private static readonly Dictionary<PhysicsScene2D, WorldPhysicsService2D> simulationOwners = new();
    private readonly Dictionary<Collider2D, WorldPhysicsColliderBinding2D> colliders = new();
    private readonly Dictionary<Item, List<Collider2D>> itemColliders = new();
    private readonly Stack<BoxCollider2D> obstaclePool = new();
    private readonly List<BoxCollider2D> ownedObstacleBoxes = new();
    private Transform poolRoot;
    private bool disposed;
    internal GameplayWorldRuntime ExecutionContext;
    public string WorldKey { get; }
    public WorldPhysicsScene2D PrimarySpace { get; }
    public int ColliderCount => colliders.Count;
    public bool IsValid => !disposed && PrimarySpace.IsValid;
    public bool IsSimulating { get; private set; }

    public WorldPhysicsService2D(string worldKey, WorldPhysicsScene2D primarySpace)
    {
        if (string.IsNullOrWhiteSpace(worldKey)) throw new ArgumentException("必须指定世界身份。", nameof(worldKey));
        PrimarySpace = primarySpace ?? throw new ArgumentNullException(nameof(primarySpace));
        if (!primarySpace.IsValid) throw new ArgumentException("世界必须绑定有效物理空间。", nameof(primarySpace));
        // 不同世界在创建时就隔离原生模拟，不能等接触事件发生后再过滤。
        if (simulationOwners.TryGetValue(primarySpace.PhysicsScene, out var owner) && owner.WorldKey != worldKey)
            throw new InvalidOperationException($"世界 {worldKey} 与 {owner.WorldKey} 需要独立的 PhysicsScene2D。");
        WorldKey = worldKey;
        simulationOwners[primarySpace.PhysicsScene] = this;
        WorldTopologyRuntime.LocalPlayerWrapped += ReprojectEntityPhysicsToLocalAnchor;
    }

    internal bool RegisterCollider(Collider2D collider, Item item = null, Component receiver = null,
        string regionId = null, WorldPhysicsPurpose2D purpose = WorldPhysicsPurpose2D.Blocking,
        object entity = null, IGameplayPhysicsDamageTarget damageTarget = null, string receiverModule = null)
    {
        if (collider == null || disposed) return false;
        if (collider.gameObject.scene.GetPhysicsScene2D() != PrimarySpace.PhysicsScene)
            throw new InvalidOperationException($"碰撞体 {collider.name} 尚未进入世界 {WorldKey} 的物理空间。");
        if (colliderOwners.TryGetValue(collider, out var previous) && previous != this)
            previous.UnregisterCollider(collider);
        var binding = new WorldPhysicsColliderBinding2D(this, item, receiver, regionId, purpose, entity, damageTarget, receiverModule);
        if (colliders.TryGetValue(collider, out var current) && current.Item == item && current.Receiver == receiver &&
            current.RegionId == regionId && current.Purpose == purpose && ReferenceEquals(current.Entity, entity) &&
            ReferenceEquals(current.DamageTarget, damageTarget) && current.ReceiverModule == receiverModule) return false;
        colliders[collider] = binding;
        colliderOwners[collider] = this;
        return true;
    }

    public bool UnregisterCollider(Collider2D collider)
    {
        if (ReferenceEquals(collider, null) || !colliders.Remove(collider)) return false;
        if (colliderOwners.TryGetValue(collider, out var owner) && owner == this) colliderOwners.Remove(collider);
        return true;
    }

    public static bool TryResolveCollider(Collider2D collider, out WorldPhysicsColliderBinding2D binding)
    {
        binding = default;
        Collider2D source = ColliderSource2D.Resolve(collider);
        return source != null && colliderOwners.TryGetValue(source, out var owner) && owner.IsValid &&
            collider.gameObject.scene.GetPhysicsScene2D() == owner.PrimarySpace.PhysicsScene &&
            source.gameObject.scene.GetPhysicsScene2D() == owner.PrimarySpace.PhysicsScene &&
            owner.colliders.TryGetValue(source, out binding);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetIndices()
    {
        colliderOwners.Clear(); itemOwners.Clear(); simulationOwners.Clear();
    }
    #endregion

    #region 普通物品登记及迁移
    internal void AssignPhysicsRootToPrimarySpace(GameObject root)
    {
        if (root.scene.GetPhysicsScene2D() == PrimarySpace.PhysicsScene) return;
        root.transform.SetParent(null, true);
        SceneManager.MoveGameObjectToScene(root, PrimarySpace.Scene);
        ItemWorldPlacement.AttachToSceneItemCategory(root.GetComponent<Item>(), root);
    }

    internal Rigidbody2D EnsureMovableItemBody(Item target)
    {
        ItemPhysicsDefinitionDto configured = GetConfiguredItemPhysics(target);
        Rigidbody2D body = target.GetComponent<Rigidbody2D>();
        if (body == null) body = target.gameObject.AddComponent<Rigidbody2D>();
        if (!ConfigureItemBodyForIndependentMotion(target, body, refreshRegistration: false))
        {
            RegisterItem(target);
            return body;
        }
        if (configured == null && !ItemPhysicsDefinitionRuntime.HasConfiguredPhysicsRegions(target))
        {
            bool hasSolid = false;
            BoxCollider2D triggerShape = null;
            using var scratch = ListPool<Collider2D>.Get(out var shapes);
            target.GetComponentsInChildren(true, shapes);
            for (int i = 0; i < shapes.Count; i++)
            {
                Collider2D collider = shapes[i];
                if (collider.GetComponentInParent<Item>(true) != target) continue;
                if (collider.enabled && !collider.isTrigger && collider.attachedRigidbody == body &&
                    IsPhysicsShapeActiveWithinItem(collider.transform, target.transform)) hasSolid = true;
                if (triggerShape == null && collider.transform == target.transform && collider is BoxCollider2D box && box.enabled && box.isTrigger)
                    triggerShape = box;
            }
            if (!hasSolid && triggerShape != null)
            {
                BoxCollider2D physical = target.gameObject.AddComponent<BoxCollider2D>();
                physical.offset = triggerShape.offset; physical.size = triggerShape.size;
                physical.isTrigger = false;
            }
        }
        ItemPhysicsProjection2D.Ensure(target);
        RegisterItem(target);
        return body;
    }

    private static bool IsPhysicsShapeActiveWithinItem(Transform shape, Transform item)
    {
        for (Transform node = shape; node != item; node = node.parent)
            if (!node.gameObject.activeSelf) return false;
        return true;
    }

    internal void RegisterItem(Item item)
    {
        if (item == null || disposed) return;
        ItemData originalData = item.itemData;
        ItemMgr manager = ItemMgr.GetExistingInstance();
        bool wasRuntimeRegistered = manager != null && manager.IsRuntimeItemRegistered(item);
        Rigidbody2D previousBody = item.GetComponent<Rigidbody2D>();
        WorldPhysicsEntityProxy2D previousProxy = null;
        bool hadPreviousProxy = itemOwners.TryGetValue(item, out var previousOwner) &&
            previousOwner.entityProxies.TryGetValue(item, out previousProxy);
        WorldPhysicsProxyMode2D previousMode = hadPreviousProxy ? previousProxy.Mode : WorldPhysicsProxyMode2D.WorldFixed;
        RigidbodyType2D previousBodyType = previousBody != null ? previousBody.bodyType : RigidbodyType2D.Static;
        bool previousSimulated = previousBody != null && previousBody.simulated;
        Vector2 previousVelocity = previousBody != null ? previousBody.velocity : Vector2.zero;
        float previousAngularVelocity = previousBody != null ? previousBody.angularVelocity : 0f;
        ReleaseItem(item, preserveNativeMotion: true);
        if (item == null || item.DestructionHandled || item.IsInPool || !ReferenceEquals(item.itemData, originalData) ||
            wasRuntimeRegistered && !manager.IsRuntimeItemRegistered(item)) return;
        // 归属交接只在登记或迁移执行，模拟帧不扫描场景或逐物品检测世界。
        AssignPhysicsRootToPrimarySpace(item.gameObject);
        ItemPhysicsDefinitionDto definition = GetConfiguredItemPhysics(item);
        bool defaultIndependent = definition == null && (ResolveLegacyItemIndependentWorldMotion(item, previousBody) ||
            previousBody != null && previousBody.bodyType == RigidbodyType2D.Kinematic &&
            (hadPreviousProxy && previousMode == WorldPhysicsProxyMode2D.IndependentDynamic ||
             item.itemData?.PhysicsState?.PersistentWorldResident == true));
        if (definition != null)
        {
            ValidateProxyConfiguration(definition);
            if (definition.Mode == WorldPhysicsProxyMode2D.IndependentDynamic)
                ItemWorldPlacement.AttachToSceneItemCategory(item, item.gameObject);
            Rigidbody2D body = item.GetComponent<Rigidbody2D>();
            if (body == null && definition.Mode == WorldPhysicsProxyMode2D.IndependentDynamic) body = item.gameObject.AddComponent<Rigidbody2D>();
            if (body != null) ConfigureProxyBody(body, item.itemData, definition, FlatWorld.Networking.GameNetwork.HasStateAuthority);
            if (body != null)
            {
                bool authority = FlatWorld.Networking.GameNetwork.HasStateAuthority;
                bool preserveRepresentation = hadPreviousProxy && previousBody == body && previousMode == definition.Mode;
                if (preserveRepresentation)
                {
                    body.bodyType = definition.Mode == WorldPhysicsProxyMode2D.WorldFixed ? RigidbodyType2D.Static :
                        !authority ? RigidbodyType2D.Kinematic : previousBodyType;
                    body.simulated = previousSimulated;
                }
                bool dynamicAuthority = authority && definition.Mode == WorldPhysicsProxyMode2D.IndependentDynamic;
                if (body.bodyType != RigidbodyType2D.Static)
                {
                    body.velocity = dynamicAuthority
                        ? preserveRepresentation ? previousVelocity : item.itemData.PhysicsState?.Velocity ?? Vector2.zero
                        : Vector2.zero;
                    body.angularVelocity = dynamicAuthority && !definition.FreezeRotation
                        ? preserveRepresentation ? previousAngularVelocity : item.itemData.PhysicsState?.AngularVelocity ?? 0f
                        : 0f;
                }
            }
        }
        else if (previousBody != null)
        {
            if (!defaultIndependent || !FlatWorld.Networking.GameNetwork.HasStateAuthority)
            {
                previousBody.bodyType = defaultIndependent ? RigidbodyType2D.Kinematic : RigidbodyType2D.Static;
                // 静态切型已由原生清速，只有运动学表示需要显式清零。
                if (previousBody.bodyType != RigidbodyType2D.Static)
                {
                    previousBody.velocity = Vector2.zero;
                    previousBody.angularVelocity = 0f;
                }
            }
        }
        var registered = ListPool<Collider2D>.Get();
        itemColliders[item] = registered;
        itemOwners[item] = this;
        try
        {
            using var scratch = ListPool<Collider2D>.Get(out var shapes);
            item.GetComponentsInChildren(true, shapes);
            for (int i = 0; i < shapes.Count; i++)
            {
                Collider2D collider = shapes[i];
                if (collider.GetComponentInParent<Item>(true) != item) continue;
                ItemPhysicsRegion2D region = collider.GetComponent<ItemPhysicsRegion2D>();
                Module module = collider.GetComponentInParent<Module>(true);
                WorldPhysicsPurpose2D purpose = region != null ? region.Purpose : collider.isTrigger
                    ? WorldPhysicsPurpose2D.Trigger : WorldPhysicsPurpose2D.Blocking;
                if (region == null && CombatPhysicsChannels.IsDamageReceiverCollider(collider))
                    purpose |= WorldPhysicsPurpose2D.Damage;
                RegisterCollider(collider, item, region != null ? region.Receiver : module,
                    region != null ? region.RegionId : null, purpose);
                registered.Add(collider);
            }
            ItemPhysicsDefinitionDto effectiveDefinition = ResolveEntityPhysicsDefinition(definition, defaultIndependent);
            RegisterConfiguredItemProxy(item, effectiveDefinition, registered);
            ItemPhysicsProjection2D.Ensure(item);
            ItemMgr.GetExistingInstance()?.RefreshPhysicsWorldResidentOwnership(item);
        }
        catch { ReleaseItem(item, preserveNativeMotion: true); throw; }
    }

    internal static void ReleaseItem(Item item, bool preserveNativeMotion = false)
    {
        if (ReferenceEquals(item, null) || !itemOwners.Remove(item, out var owner)) return;
        Rigidbody2D body = preserveNativeMotion && item != null ? item.GetComponent<Rigidbody2D>() : null;
        Vector2 velocity = body != null ? body.velocity : Vector2.zero;
        float angularVelocity = body != null ? body.angularVelocity : 0f;
        try
        {
            if (owner.entityProxies.TryGetValue(item, out var proxy))
            {
                try { proxy.PublishSimulationResult(); }
                finally { owner.ReleaseEntityProxy(proxy); }
            }
        }
        finally
        {
            if (body != null && body.bodyType != RigidbodyType2D.Static)
            {
                body.velocity = velocity;
                body.angularVelocity = angularVelocity;
            }
            if (owner.itemColliders.Remove(item, out var shapes))
            {
                for (int i = 0; i < shapes.Count; i++) owner.UnregisterCollider(shapes[i]);
                ListPool<Collider2D>.Release(shapes);
            }
        }
    }
    #endregion

    #region 世界障碍代理池
    internal BoxCollider2D RentObstacleBox(Transform parent)
    {
        BoxCollider2D collider = null;
        while (obstaclePool.Count > 0 && collider == null) collider = obstaclePool.Pop();
        if (collider == null)
        {
            var root = new GameObject("World Physics Obstacle") { hideFlags = HideFlags.DontSave };
            root.SetActive(false);
            SceneManager.MoveGameObjectToScene(root, PrimarySpace.Scene);
            collider = root.AddComponent<BoxCollider2D>();
            ownedObstacleBoxes.Add(collider);
            collider.enabled = false;
        }
        collider.transform.SetParent(parent, false);
        collider.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        collider.transform.localScale = Vector3.one;
        ResetColliderPhysicsSettings(collider);
        collider.gameObject.SetActive(true);
        return collider;
    }

    internal void ReturnObstacleBox(BoxCollider2D collider)
    {
        UnregisterCollider(collider);
        if (collider == null) return;
        collider.enabled = false;
        collider.gameObject.SetActive(false);
        if (disposed || !PrimarySpace.IsValid)
        {
            DestroyPhysicsObject(collider.gameObject);
            return;
        }
        if (poolRoot == null)
        {
            var root = new GameObject("World Physics Pool") { hideFlags = HideFlags.DontSave };
            SceneManager.MoveGameObjectToScene(root, PrimarySpace.Scene);
            poolRoot = root.transform;
        }
        collider.transform.SetParent(poolRoot, false);
        if (collider.TryGetComponent<ColliderSource2D>(out var source)) source.ClearBinding();
        obstaclePool.Push(collider);
    }

    internal static void DestroyPhysicsObject(GameObject target)
    {
        // 自有物理对象先同步释放原生刚体，再排队销毁，关闭世界前的释放也遵循此顺序。
        target.SetActive(false);
        if (Application.isPlaying) UnityEngine.Object.Destroy(target);
        else UnityEngine.Object.DestroyImmediate(target);
    }
    #endregion

    #region 世界关闭
    internal void Simulate(float seconds)
    {
        // 脏碰撞在本世界原生模拟前统一提交，物品和区块不再各自选择模拟空间。
        ChunkCollisionRenderer.FlushDirtyDataObstacles(WorldKey);
        PrepareEntityProxySimulation();
        IsSimulating = true;
        try { PrimarySpace.Simulate(seconds); }
        finally { IsSimulating = false; }
        PublishEntityProxySimulation();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        WorldTopologyRuntime.LocalPlayerWrapped -= ReprojectEntityPhysicsToLocalAnchor;
        DisposeTransientBodies();
        DisposeEntityProxies();
        foreach (var pair in itemColliders)
        {
            if (itemOwners.TryGetValue(pair.Key, out var owner) && owner == this) itemOwners.Remove(pair.Key);
            ListPool<Collider2D>.Release(pair.Value);
        }
        itemColliders.Clear();
        foreach (var pair in colliders)
        {
            if (colliderOwners.TryGetValue(pair.Key, out var owner) && owner == this) colliderOwners.Remove(pair.Key);
        }
        colliders.Clear();
        obstaclePool.Clear();
        foreach (BoxCollider2D collider in ownedObstacleBoxes)
            if (collider != null && (poolRoot == null || !collider.transform.IsChildOf(poolRoot)))
            { collider.enabled = false; DestroyPhysicsObject(collider.gameObject); }
        ownedObstacleBoxes.Clear();
        if (poolRoot != null) DestroyPhysicsObject(poolRoot.gameObject);
        poolRoot = null;
        if (simulationOwners.TryGetValue(PrimarySpace.PhysicsScene, out var current) && current == this)
            simulationOwners.Remove(PrimarySpace.PhysicsScene);
        PrimarySpace.Dispose();
        ExecutionContext = null;
    }
    #endregion
}
