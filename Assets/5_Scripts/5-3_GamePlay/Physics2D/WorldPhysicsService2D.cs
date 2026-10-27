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
    internal readonly IGameplayPhysicsDamageTarget DamageTarget;

    internal WorldPhysicsColliderBinding2D(WorldPhysicsService2D world, Item item, Component receiver,
        string regionId, WorldPhysicsPurpose2D purpose, object entity, IGameplayPhysicsDamageTarget damageTarget)
    {
        World = world; Item = item; Receiver = receiver; RegionId = regionId;
        Purpose = purpose; Entity = entity; DamageTarget = damageTarget;
    }
    #endregion
}

/// <summary>世界拥有碰撞登记、代理池和原生空间，业务模块继续拥有伤害及触发规则。</summary>
public sealed class WorldPhysicsService2D : IDisposable
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
    }

    internal bool RegisterCollider(Collider2D collider, Item item = null, Component receiver = null,
        string regionId = null, WorldPhysicsPurpose2D purpose = WorldPhysicsPurpose2D.Blocking,
        object entity = null, IGameplayPhysicsDamageTarget damageTarget = null)
    {
        if (collider == null || disposed) return false;
        if (collider.gameObject.scene.GetPhysicsScene2D() != PrimarySpace.PhysicsScene)
            throw new InvalidOperationException($"碰撞体 {collider.name} 尚未进入世界 {WorldKey} 的物理空间。");
        if (colliderOwners.TryGetValue(collider, out var previous) && previous != this)
            previous.UnregisterCollider(collider);
        var binding = new WorldPhysicsColliderBinding2D(this, item, receiver, regionId, purpose, entity, damageTarget);
        if (colliders.TryGetValue(collider, out var current) && current.Item == item && current.Receiver == receiver &&
            current.RegionId == regionId && current.Purpose == purpose && ReferenceEquals(current.Entity, entity) &&
            ReferenceEquals(current.DamageTarget, damageTarget)) return false;
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
    }

    internal Rigidbody2D EnsureMovableItemBody(Item target)
    {
        Rigidbody2D body = target.GetComponent<Rigidbody2D>();
        if (body == null) body = target.gameObject.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Dynamic;
        body.gravityScale = 0f;
        body.constraints |= RigidbodyConstraints2D.FreezeRotation;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        if (!ItemPhysicsDefinitionRuntime.HasConfiguredPhysicsRegions(target))
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
        ReleaseItem(item);
        // 归属交接只在登记或迁移执行，模拟帧不扫描场景或逐物品检测世界。
        AssignPhysicsRootToPrimarySpace(item.gameObject);
        var registered = ListPool<Collider2D>.Get();
        itemColliders[item] = registered;
        itemOwners[item] = this;
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
    }

    internal static void ReleaseItem(Item item)
    {
        if (ReferenceEquals(item, null) || !itemOwners.Remove(item, out var owner)) return;
        if (!owner.itemColliders.Remove(item, out var shapes)) return;
        for (int i = 0; i < shapes.Count; i++) owner.UnregisterCollider(shapes[i]);
        ListPool<Collider2D>.Release(shapes);
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
            collider.usedByComposite = false;
        }
        collider.transform.SetParent(parent, false);
        collider.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        collider.transform.localScale = Vector3.one;
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

    internal static void DestroyPhysicsObject(UnityEngine.Object target)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(target);
        else UnityEngine.Object.DestroyImmediate(target);
    }
    #endregion

    #region 世界关闭
    internal void Simulate(float seconds)
    {
        // 脏碰撞在本世界原生模拟前统一提交，物品和区块不再各自选择模拟空间。
        ChunkCollisionRenderer.FlushDirtyDataObstacles(WorldKey);
        PrimarySpace.PhysicsScene.Simulate(seconds);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
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
