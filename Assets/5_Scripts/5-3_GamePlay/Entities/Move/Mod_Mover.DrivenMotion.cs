using System;
using UnityEngine;

public partial class Mod_Mover
{
    #region 临时主动移动租约
    private DrivenMotionLease drivenMotionLease;
    public bool HasDrivenMotion => drivenMotionLease != null;
    public bool IsSurfaceMotionContextAvailable => !resettingMotion && Enabled && isActiveAndEnabled &&
        item != null && !item.DestructionHandled && CarrierSource == null && !spaceMotionOwned &&
        !SpaceMgr.IsSpaceScene(item.gameObject.scene.name);
    public bool IsSurfaceMotionAvailable => IsSurfaceMotionContextAvailable && !IsLock;

    /// <summary>只绑定已有刚体与正式配置，不改变远端的物理归属或开启模拟。</summary>
    public void InitializeDrivenMotion(Rigidbody2D body)
    {
        if (body == null || item == null || body.gameObject != item.gameObject)
            throw new ArgumentException("主动移动需要本物品已有的刚体。", nameof(body));
        if (!IsRuntimeLoaded) ModDataMemoryPack.ReadData(ref Data);
        rb = body;
    }

    public float WalkingSpeed => GetWalkingSpeedValue() *
        ResolveBuildingMoveSpeedMultiplier() * ResolveSurfaceGravitySpeedMultiplier();

    /// <summary>临时动作复用正式刚体与环境速度，只替换当前主动速度。</summary>
    public bool TryAcquireDrivenMotion(Vector2 velocity, out DrivenMotionLease lease)
    {
        lease = null;
        if (!IsSurfaceMotionAvailable || HasDrivenMotion || !FiniteVelocity(velocity)) return false;
        if (rb == null) rb = ItemPhysicsProjection2D.EnsureMovable(item);
        if (rb == null) return false;
        drivenMotionLease = lease = new DrivenMotionLease(this, velocity);
        ApplyDrivenMotion();
        return true;
    }

    private static bool FiniteVelocity(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y);

    private void ApplyDrivenMotion()
    {
        if (drivenMotionLease == null || rb == null) return;
        DrivenVelocity = drivenMotionLease.Velocity;
        RequestedMoveInput = Vector2.zero;
        ExternalVelocity = ResolveExternalVelocity();
        if (rb.simulated && rb.bodyType == RigidbodyType2D.Dynamic)
            rb.velocity = DrivenVelocity + ExternalVelocity;
        hungerActionInstance?.SetMovementState(false, false);
        UpdateMovementState();
    }

    private void ClearDrivenMotion()
    {
        if (drivenMotionLease == null) return;
        InvalidateDrivenMotion();
        DrivenVelocity = RequestedMoveInput = Vector2.zero;
        ExternalVelocity = rb != null ? ResolveExternalVelocity() : Vector2.zero;
        if (rb != null && rb.simulated && rb.bodyType == RigidbodyType2D.Dynamic)
            rb.velocity = ExternalVelocity;
        UpdateMovementState();
    }

    private void InvalidateDrivenMotion()
    {
        drivenMotionLease?.Invalidate();
        drivenMotionLease = null;
    }

    public sealed class DrivenMotionLease : IDisposable
    {
        private Mod_Mover owner;
        public Vector2 Velocity { get; private set; }
        public bool IsActive => owner != null && ReferenceEquals(owner.drivenMotionLease, this);
        internal DrivenMotionLease(Mod_Mover owner, Vector2 velocity) { this.owner = owner; Velocity = velocity; }
        public void SetVelocity(Vector2 velocity)
        {
            if (!IsActive || !FiniteVelocity(velocity)) return;
            Velocity = velocity;
            owner.ApplyDrivenMotion();
        }
        internal void Invalidate() => owner = null;
        public void Dispose()
        {
            if (!IsActive) { owner = null; return; }
            owner.ClearDrivenMotion();
        }
    }
    #endregion
}
