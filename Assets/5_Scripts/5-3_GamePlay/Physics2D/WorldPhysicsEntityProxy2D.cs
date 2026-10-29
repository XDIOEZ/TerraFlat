using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>游戏决定如何持有物理表示，Unity 刚体类型只由世界服务实现。</summary>
public enum WorldPhysicsProxyMode2D : byte { WorldFixed, IndependentDynamic }
public enum WorldPhysicsMassSource2D : byte { ItemStack }

public interface IItemWorldPhysicsPoseReceiver2D
{
    void OnWorldPhysicsPoseCommitted(Item item, WorldPhysicsEntityPose2D pose);
}

public readonly struct WorldPhysicsEntityPose2D
{
    #region 实体运动结果
    public readonly Vector2 Position;
    public readonly float RotationDegrees;
    public readonly Vector2 Velocity;
    public readonly float AngularVelocity;
    public WorldPhysicsEntityPose2D(Vector2 position, float rotationDegrees, Vector2 velocity, float angularVelocity)
    { Position = position; RotationDegrees = rotationDegrees; Velocity = velocity; AngularVelocity = angularVelocity; }
    #endregion
}

public readonly struct WorldPhysicsEntityRegion2D
{
    #region 区域用途与原始接收者
    public readonly string Id;
    public readonly WorldPhysicsPurpose2D Purpose;
    public readonly ItemColliderDefinitionDto Collider;
    public readonly string ReceiverModule;
    public readonly Vector3 LocalPosition;
    public readonly Quaternion LocalRotation;
    public readonly Vector3 LocalScale;
    public WorldPhysicsEntityRegion2D(string id, WorldPhysicsPurpose2D purpose, ItemColliderDefinitionDto collider,
        string receiverModule = null, Vector3? localPosition = null, Quaternion? localRotation = null,
        Vector3? localScale = null)
    {
        Id = id; Purpose = purpose; Collider = collider; ReceiverModule = receiverModule;
        LocalPosition = localPosition ?? Vector3.zero; LocalRotation = localRotation ?? Quaternion.identity;
        LocalScale = localScale ?? Vector3.one;
    }
    #endregion
}

/// <summary>一次实体生命周期的物理租约，释放后旧引用不能访问再次领取的物理壳。</summary>
public sealed class WorldPhysicsEntityProxy2D : IDisposable
{
    #region 代理租约
    internal readonly WorldPhysicsService2D.EntityProxyShell Shell;
    internal readonly Item Item;
    internal readonly ItemData Snapshot;
    internal readonly Action<WorldPhysicsEntityPose2D> PoseReceiver;
    internal readonly ItemPhysicsDefinitionDto Definition;
    internal readonly bool StateAuthority;
    internal bool Released;
    internal bool MassDirty = true;
    internal Vector2 LogicalOrigin;
    private bool hasPublishedPose;
    private WorldPhysicsEntityPose2D publishedPose;
    private WorldPhysicsEntityPose2D suspendedPose;
    private bool isParticipatingInWorldPhysics = true;
    public WorldPhysicsService2D Owner { get; }
    public object Entity { get; }
    public WorldPhysicsProxyMode2D Mode => Definition.Mode;
    public Rigidbody2D Body => IsValid ? Shell.Body : null;
    public bool IsValid => !Released && Owner.IsValid && Shell.Body != null && Shell.Root != null;
    public bool IsParticipatingInWorldPhysics => IsValid && isParticipatingInWorldPhysics;
    public IReadOnlyList<Collider2D> Colliders => IsValid ? Shell.ActiveColliders : Array.Empty<Collider2D>();

    internal WorldPhysicsEntityProxy2D(WorldPhysicsService2D owner, object entity, ItemData snapshot,
        ItemPhysicsDefinitionDto definition, WorldPhysicsService2D.EntityProxyShell shell,
        Action<WorldPhysicsEntityPose2D> poseReceiver, bool stateAuthority, Item item = null, Vector2? logicalPosition = null)
    {
        Owner = owner; Entity = entity; Snapshot = snapshot;
        Definition = new ItemPhysicsDefinitionDto { Mode = definition.Mode, MassSource = definition.MassSource,
            FreezeRotation = definition.FreezeRotation, LinearDamping = definition.LinearDamping,
            AngularDamping = definition.AngularDamping };
        Shell = shell;
        LogicalOrigin = logicalPosition ?? (Vector2)(snapshot?.transform?.position ?? Vector3.zero);
        PoseReceiver = poseReceiver; StateAuthority = stateAuthority; Item = item;
        if (Definition.Mode == WorldPhysicsProxyMode2D.IndependentDynamic)
            Snapshot.MassChanged += MarkEntityMassDirty;
    }

    public WorldPhysicsEntityPose2D CapturePose()
    {
        if (!IsValid) throw new ObjectDisposedException(nameof(WorldPhysicsEntityProxy2D));
        if (!isParticipatingInWorldPhysics) return suspendedPose;
        if (Shell.SharedFixedBody)
            return new WorldPhysicsEntityPose2D(Shell.Root.position,
                Shell.Root.eulerAngles.z, Vector2.zero, 0f);
        return new WorldPhysicsEntityPose2D(Shell.Body.position, Shell.Body.rotation,
            Shell.Body.velocity, Shell.Body.angularVelocity);
    }

    public void SetPose(WorldPhysicsEntityPose2D pose)
    {
        if (!IsValid) throw new ObjectDisposedException(nameof(WorldPhysicsEntityProxy2D));
        if (!isParticipatingInWorldPhysics) { suspendedPose = pose; return; }
        if (Shell.SharedFixedBody)
        {
            if (!Shell.OwnsRoot) throw new InvalidOperationException("固定合批代理通过实体恢复重新申请，不能独立移动共享主体。");
            Shell.Root.SetPositionAndRotation(new Vector3(pose.Position.x, pose.Position.y, Shell.Root.position.z),
                Quaternion.Euler(0f, 0f, pose.RotationDegrees));
            LogicalOrigin = WorldLocalPresentation.ToLogical(pose.Position);
            Owner.RefreshFixedProxyBoundaryImages(this);
            return;
        }
        Shell.Body.position = pose.Position;
        Shell.Body.rotation = pose.RotationDegrees;
        Shell.Body.velocity = StateAuthority && Mode == WorldPhysicsProxyMode2D.IndependentDynamic ? pose.Velocity : Vector2.zero;
        Shell.Body.angularVelocity = StateAuthority && Mode == WorldPhysicsProxyMode2D.IndependentDynamic && !Definition.FreezeRotation
            ? pose.AngularVelocity : 0f;
    }

    /// <summary>暂挂自身物理壳以便跨世界事务失败后恢复同一租约。</summary>
    public void SetParticipationInWorldPhysics(bool active)
    {
        if (!IsValid) throw new ObjectDisposedException(nameof(WorldPhysicsEntityProxy2D));
        if (!Shell.OwnsRoot) throw new InvalidOperationException("普通物品不能通过代理停用整个实体。");
        if (isParticipatingInWorldPhysics == active) return;
        if (!active)
        {
            PublishSimulationResult();
            if (!IsValid) throw new ObjectDisposedException(nameof(WorldPhysicsEntityProxy2D));
            suspendedPose = CapturePose();
            isParticipatingInWorldPhysics = false;
            Shell.Root.gameObject.SetActive(false);
            return;
        }
        Shell.Root.gameObject.SetActive(true);
        isParticipatingInWorldPhysics = true;
        SetPose(suspendedPose);
    }

    public void SetEntityScale(Vector3 scale)
    {
        if (!IsValid) throw new ObjectDisposedException(nameof(WorldPhysicsEntityProxy2D));
        Shell.Root.localScale = scale;
        Owner.RefreshFixedProxyBoundaryImages(this);
    }

    internal void PublishSimulationResult()
    {
        if (!IsParticipatingInWorldPhysics || !StateAuthority || Mode != WorldPhysicsProxyMode2D.IndependentDynamic) return;
        WorldPhysicsEntityPose2D pose = CapturePose();
        if (hasPublishedPose && publishedPose.Position == pose.Position &&
            publishedPose.RotationDegrees == pose.RotationDegrees && publishedPose.Velocity == pose.Velocity &&
            publishedPose.AngularVelocity == pose.AngularVelocity) return;
        publishedPose = pose; hasPublishedPose = true;
        Snapshot.PhysicsState ??= new ItemPhysicsRuntimeState();
        Snapshot.PhysicsState.Velocity = pose.Velocity;
        Snapshot.PhysicsState.AngularVelocity = pose.AngularVelocity;
        PoseReceiver?.Invoke(pose);
    }

    private void MarkEntityMassDirty(ItemData _) => MassDirty = true;
    internal void DetachSnapshotObservers()
    {
        if (Definition.Mode == WorldPhysicsProxyMode2D.IndependentDynamic)
            Snapshot.MassChanged -= MarkEntityMassDirty;
    }

    public void Dispose() => Owner.ReleaseEntityProxy(this);
    #endregion
}
