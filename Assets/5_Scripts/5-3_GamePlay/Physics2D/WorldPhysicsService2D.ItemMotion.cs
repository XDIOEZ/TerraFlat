using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>物品的实际运动能力声明独立物理需求。</summary>
public interface IItemIndependentWorldMotionCapability2D
{
    bool RequiresIndependentWorldMotion { get; }
}

/// <summary>保存临时表现接管前的原生刚体状态。</summary>
internal readonly struct WorldPhysicsItemBodyRepresentation2D
{
    #region 原生表示快照
    internal readonly RigidbodyType2D BodyType;
    internal readonly bool Simulated;
    internal WorldPhysicsItemBodyRepresentation2D(Rigidbody2D body)
    { BodyType = body.bodyType; Simulated = body.simulated; }
    #endregion
}

/// <summary>临时玩法对象借用原生刚体，释放时只清理自己的登记。</summary>
public sealed class WorldPhysicsTransientBodyLease2D : IDisposable
{
    #region 临时刚体所有权
    internal readonly WorldPhysicsService2D Owner;
    internal readonly Component Entity;
    internal readonly Rigidbody2D Body;
    internal readonly List<Collider2D> Colliders = new();
    internal bool Released;
    internal WorldPhysicsTransientBodyLease2D(WorldPhysicsService2D owner, Component entity, Rigidbody2D body)
    { Owner = owner; Entity = entity; Body = body; }
    public bool IsValid => !Released && Owner.IsValid && Body != null;
    public void Dispose() => Owner.ReleaseTransientIndependentBody(this);
    #endregion
}

public sealed partial class WorldPhysicsService2D
{
    #region 普通物品实际运动请求
    public static bool CanItemUseIndependentMotion(Item item)
        => item != null && GetConfiguredItemPhysics(item)?.Mode != WorldPhysicsProxyMode2D.WorldFixed;

    private static bool ResolveLegacyItemIndependentWorldMotion(Item item, Rigidbody2D body)
    {
        if (body == null) return false;
        if (body.bodyType == RigidbodyType2D.Dynamic) return true;
        using var scratch = ListPool<MonoBehaviour>.Get(out var behaviours);
        item.GetComponentsInChildren(true, behaviours);
        foreach (MonoBehaviour behaviour in behaviours)
            if (behaviour is IItemIndependentWorldMotionCapability2D capability &&
                (behaviour is not Module module || module.Enabled) &&
                behaviour.GetComponentInParent<Item>(true) == item && capability.RequiresIndependentWorldMotion)
                return true;
        return false;
    }

    internal static bool UsesLegacyItemPhysicsDefaults(Item item) => GetConfiguredItemPhysics(item) == null;

    internal static void CaptureItemPhysicsMotionForPersistence(Item item)
    {
        if (item == null || item.itemData == null || !itemOwners.TryGetValue(item, out var owner) ||
            !owner.TryGetItemProxy(item, out var proxy) || !proxy.StateAuthority ||
            proxy.Mode != WorldPhysicsProxyMode2D.IndependentDynamic || proxy.Body == null ||
            !proxy.Body.simulated || proxy.Body.bodyType != RigidbodyType2D.Dynamic) return;
        item.itemData.PhysicsState ??= new ItemPhysicsRuntimeState();
        item.itemData.PhysicsState.Velocity = proxy.Body.velocity;
        item.itemData.PhysicsState.AngularVelocity = proxy.Body.angularVelocity;
    }

    internal static bool RequestItemMovementTowardsLogicalPosition(Item item, Rigidbody2D body, Vector2 logicalPosition)
    {
        if (item == null || body == null || !float.IsFinite(logicalPosition.x) || !float.IsFinite(logicalPosition.y)) return false;
        ItemPhysicsDefinitionDto definition = GetConfiguredItemPhysics(item);
        if (definition?.Mode == WorldPhysicsProxyMode2D.WorldFixed) return false;
        Vector2 target = WorldLocalPresentation.ProjectPosition(WorldTopologyRuntime.NormalizePosition(logicalPosition));
        if (definition?.Mode == WorldPhysicsProxyMode2D.IndependentDynamic)
        {
            if (!GameNetwork.HasStateAuthority || !body.simulated || body.bodyType != RigidbodyType2D.Dynamic) return false;
            // 动态实体只提交运动意图，碰撞和结果回写交给世界物理步。
            body.MovePosition(target);
            return true;
        }
        body.velocity = Vector2.zero;
        body.position = target;
        item.transform.position = new Vector3(target.x, target.y, item.transform.position.z);
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        return true;
    }

    internal static bool StartConfiguredItemDropMotion(Item item, Vector2 start, Vector2 end, float duration)
    {
        ItemPhysicsDefinitionDto definition = GetConfiguredItemPhysics(item);
        if (definition == null) return false;
        if (!float.IsFinite(start.x) || !float.IsFinite(start.y) || !float.IsFinite(end.x) ||
            !float.IsFinite(end.y) || !float.IsFinite(duration) || duration < 0f)
            throw new ArgumentException("物理掉落的位置与时长必须有效。");
        if (!TryGetRegisteredItemProxy(item, out var proxy))
            throw new InvalidOperationException("配置物理的掉落物尚未登记世界代理。");
        Vector2 logicalStart = WorldTopologyRuntime.NormalizePosition(start);
        Vector2 logicalEnd = WorldTopologyRuntime.NormalizePosition(end);
        Vector2 logicalPosition = definition.Mode == WorldPhysicsProxyMode2D.WorldFixed ? logicalEnd : logicalStart;
        Vector2 presentation = WorldLocalPresentation.ProjectPosition(logicalPosition);
        if (definition.Mode == WorldPhysicsProxyMode2D.WorldFixed)
        {
            item.transform.position = new Vector3(presentation.x, presentation.y, item.transform.position.z);
            Rigidbody2D fixedBody = item.GetComponent<Rigidbody2D>();
            if (fixedBody != null) fixedBody.position = presentation;
            proxy.Owner.RegisterItem(item);
        }
        else
        {
            Vector2 velocity = duration > 0f
                ? WorldTopologyRuntime.ShortestDelta(logicalStart, logicalEnd) / duration : Vector2.zero;
            WorldPhysicsEntityPose2D old = proxy.CapturePose();
            proxy.SetPose(new WorldPhysicsEntityPose2D(presentation, old.RotationDegrees, velocity, 0f));
        }
        if (item.itemData?.transform != null)
            item.itemData.transform.position = new Vector3(logicalPosition.x, logicalPosition.y, item.transform.position.z);
        if (item.itemData?.Stack != null) item.itemData.Stack.CanBePickedUp = true;
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        return true;
    }

    internal static bool ConfigureItemBodyForIndependentMotion(Item item, Rigidbody2D body, bool refreshRegistration = true)
    {
        if (item == null || body == null || body.gameObject != item.gameObject || !CanItemUseIndependentMotion(item))
            return false;
        ItemPhysicsDefinitionDto definition = GetConfiguredItemPhysics(item);
        body.bodyType = GameNetwork.HasStateAuthority ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
        body.simulated = true;
        body.gravityScale = 0f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.useAutoMass = false;
        body.mass = ResolveEntityMass(item.itemData);
        if (definition == null) body.constraints |= RigidbodyConstraints2D.FreezeRotation;
        else
        {
            body.constraints = definition.FreezeRotation ? RigidbodyConstraints2D.FreezeRotation : RigidbodyConstraints2D.None;
            body.drag = definition.LinearDamping;
            body.angularDrag = definition.AngularDamping;
        }
        if (!GameNetwork.HasStateAuthority) { body.velocity = Vector2.zero; body.angularVelocity = 0f; }
        if (refreshRegistration && itemOwners.TryGetValue(item, out var owner) &&
            owner.TryGetItemProxy(item, out var proxy) &&
            (proxy.Mode != WorldPhysicsProxyMode2D.IndependentDynamic || proxy.PoseReceiver == null))
            owner.RegisterItem(item);
        else if (refreshRegistration) ItemPhysicsProjection2D.Ensure(item);
        return true;
    }

    internal static bool ConfigureItemBodyForExternallyPositionedMotion(Item item, Rigidbody2D body)
    {
        if (item == null || body == null || !CanItemUseIndependentMotion(item)) return false;
        ItemPhysicsDefinitionDto definition = GetConfiguredItemPhysics(item);
        body.bodyType = definition != null && GameNetwork.HasStateAuthority
            ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
        body.velocity = Vector2.zero;
        body.angularVelocity = 0f;
        return true;
    }

    internal static void ConfigureItemBodyForRestingPose(Item item, Rigidbody2D body)
    {
        if (body == null) return;
        ItemPhysicsDefinitionDto definition = GetConfiguredItemPhysics(item);
        body.bodyType = definition == null || definition.Mode == WorldPhysicsProxyMode2D.WorldFixed ? RigidbodyType2D.Static :
            GameNetwork.HasStateAuthority ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        // 静态刚体切型后由原生清速，其余可动刚体显式清零。
        if (body.bodyType != RigidbodyType2D.Static)
        {
            body.velocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
        if (definition == null) { body.drag = 0f; body.constraints = RigidbodyConstraints2D.FreezeRotation; }
    }

    internal static void ApplyLegacyItemLinearDamping(Item item, Rigidbody2D body, float damping)
    {
        if (body != null && GetConfiguredItemPhysics(item) == null) body.drag = Mathf.Max(0f, damping);
    }

    internal static void ApplyLegacyItemAngularDamping(Item item, Rigidbody2D body, float damping)
    {
        if (body != null && GetConfiguredItemPhysics(item) == null) body.angularDrag = Mathf.Max(0f, damping);
    }

    internal static Rigidbody2D ConfigureItemAttachedDamageSensorBody(Item item, Collider2D sensor, Component receiver)
    {
        if (item == null || sensor == null || sensor.GetComponentInParent<Item>(true) != item ||
            !itemOwners.TryGetValue(item, out var owner))
            throw new InvalidOperationException("物品受击传感器必须属于已登记的世界物品。");
        bool wasEnabled = sensor.enabled;
        sensor.enabled = false;
        try
        {
            Rigidbody2D body = sensor.GetComponent<Rigidbody2D>();
            if (body == null) body = sensor.gameObject.AddComponent<Rigidbody2D>();
            body.simulated = false;
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            sensor.isTrigger = true;
            owner.RegisterCollider(sensor, item, receiver, purpose: WorldPhysicsPurpose2D.Trigger | WorldPhysicsPurpose2D.Damage);
            if (owner.itemColliders.TryGetValue(item, out var shapes) && !shapes.Contains(sensor)) shapes.Add(sensor);
            body.simulated = true;
            return body;
        }
        finally { if (sensor != null) sensor.enabled = wasEnabled; }
    }
    #endregion

    #region 临时玩法刚体登记与回收
    private readonly Dictionary<Component, WorldPhysicsTransientBodyLease2D> transientBodies = new();

    public WorldPhysicsTransientBodyLease2D RegisterTransientIndependentBody(Component entity, Rigidbody2D body)
    {
        if (disposed) throw new ObjectDisposedException(nameof(WorldPhysicsService2D));
        if (entity == null || body == null || entity.gameObject != body.gameObject)
            throw new ArgumentException("临时物理实体必须拥有自己的根刚体。");
        if (transientBodies.ContainsKey(entity)) throw new InvalidOperationException("临时物理实体已登记。");
        AssignPhysicsRootToPrimarySpace(entity.gameObject);
        var lease = new WorldPhysicsTransientBodyLease2D(this, entity, body);
        transientBodies.Add(entity, lease);
        try
        {
            body.bodyType = GameNetwork.HasStateAuthority ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
            body.simulated = true;
            body.gravityScale = 0f;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            if (!GameNetwork.HasStateAuthority) { body.velocity = Vector2.zero; body.angularVelocity = 0f; }
            using var scratch = ListPool<Collider2D>.Get(out var shapes);
            entity.GetComponentsInChildren(true, shapes);
            foreach (Collider2D shape in shapes)
            {
                if (shape == null || shape.GetComponentInParent<Rigidbody2D>(true) != body) continue;
                Module receiver = shape.GetComponentInParent<Module>(true);
                RegisterCollider(shape, receiver: receiver, purpose: shape.isTrigger
                    ? WorldPhysicsPurpose2D.Trigger : WorldPhysicsPurpose2D.Blocking, entity: entity);
                lease.Colliders.Add(shape);
            }
            return lease;
        }
        catch { ReleaseTransientIndependentBody(lease); throw; }
    }

    internal void ReleaseTransientIndependentBody(WorldPhysicsTransientBodyLease2D lease)
    {
        if (lease == null || lease.Released || lease.Owner != this) return;
        lease.Released = true;
        if (!ReferenceEquals(lease.Entity, null) && transientBodies.TryGetValue(lease.Entity, out var current) &&
            ReferenceEquals(current, lease)) transientBodies.Remove(lease.Entity);
        foreach (Collider2D shape in lease.Colliders) UnregisterCollider(shape);
        lease.Colliders.Clear();
        if (lease.Body != null)
        {
            lease.Body.velocity = Vector2.zero;
            lease.Body.angularVelocity = 0f;
            lease.Body.simulated = false;
        }
    }

    private void DisposeTransientBodies()
    {
        using var scratch = ListPool<WorldPhysicsTransientBodyLease2D>.Get(out var leases);
        leases.AddRange(transientBodies.Values);
        foreach (WorldPhysicsTransientBodyLease2D lease in leases) ReleaseTransientIndependentBody(lease);
        transientBodies.Clear();
    }
    #endregion

    #region 太空承载及网络表现接管
    internal static WorldPhysicsItemBodyRepresentation2D CaptureItemBodyRepresentation(Rigidbody2D body)
        => new(body);

    internal static void SetItemBodySimulationEnabled(Rigidbody2D body, bool enabled)
    {
        if (body != null) body.simulated = enabled;
    }

    internal static void ConfigureItemBodyForNetworkRepresentation(Item item, Rigidbody2D body, bool simulated)
    {
        if (body == null) return;
        body.bodyType = GetConfiguredItemPhysics(item)?.Mode == WorldPhysicsProxyMode2D.WorldFixed
            ? RigidbodyType2D.Static : RigidbodyType2D.Kinematic;
        body.simulated = simulated;
        body.velocity = Vector2.zero;
        body.angularVelocity = 0f;
    }

    internal static void RestoreItemBodyRepresentation(Item item, Rigidbody2D body,
        WorldPhysicsItemBodyRepresentation2D state, bool clearVelocity = false)
    {
        if (body == null) return;
        RestoreItemBodyRepresentationType(item, body, state);
        if (clearVelocity) { body.velocity = Vector2.zero; body.angularVelocity = 0f; }
        body.simulated = state.Simulated;
    }

    internal static void RestoreItemBodyRepresentationType(Item item, Rigidbody2D body,
        WorldPhysicsItemBodyRepresentation2D state)
    {
        if (body == null) return;
        ItemPhysicsDefinitionDto definition = GetConfiguredItemPhysics(item);
        body.bodyType = definition?.Mode == WorldPhysicsProxyMode2D.WorldFixed ? RigidbodyType2D.Static :
            definition != null && !GameNetwork.HasStateAuthority ? RigidbodyType2D.Kinematic : state.BodyType;
    }
    #endregion
}
