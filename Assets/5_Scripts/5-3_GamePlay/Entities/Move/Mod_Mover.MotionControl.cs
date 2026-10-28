using System;
using FlatWorld.Mobile;
using UnityEngine;

/// <summary>主动移动、手机输入与网络消耗共用同一角色的运动状态。</summary>
public partial class Mod_Mover
{
    #region 临时主动移动租约
    private DrivenMotionLease drivenMotionLease;
    public bool HasDrivenMotion => drivenMotionLease != null;
    public bool IsSurfaceMotionContextAvailable => physicsAllowsIndependentMotion && !resettingMotion && Enabled && isActiveAndEnabled &&
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
        physicsAllowsIndependentMotion = WorldPhysicsService2D.CanItemUseIndependentMotion(item);
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

    #region 手机摇杆移动
    private bool mobileMovementInputActive;
    private bool manualRunBeforeMobile;
    private bool mobileRunExhausted;

    public bool RunPreference => mobileMovementInputActive ? manualRunBeforeMobile : IsRunning;

    // 自动奔跑只接管实际手机移动，离开手机输入后恢复键鼠的手动偏好。
    internal Vector2 ResolveMobileMoveInput(Vector2 input, bool isMobileMoveInput)
    {
        if (!isMobileMoveInput || !FiniteVelocity(input) || IsSpaceRunningDisabled || CarrierSource != null)
        {
            ReleaseMobileMovementInput();
            return FiniteVelocity(input) ? input : Vector2.zero;
        }

        if (!mobileMovementInputActive)
        {
            manualRunBeforeMobile = IsRunning;
            mobileMovementInputActive = true;
            mobileRunExhausted = false;
        }

        float deflection = Mathf.Clamp01(input.magnitude);
        bool runRequested = deflection > MobileMovementRules.RunStart && RunSpeedRate > 1f;
        if (!runRequested) mobileRunExhausted = false;
        else if (stamina != null && stamina.CurrentValue < RunStaminaThreshold)
            mobileRunExhausted = true;

        float walkingSpeed = GetWalkingSpeedValue();
        ApplyRunState(runRequested && !mobileRunExhausted);
        float modeSpeed = Mathf.Max(0f, Speed.Value);
        if (deflection <= 0f || walkingSpeed <= 0f || modeSpeed <= 0f) return Vector2.zero;

        float deadZone = inputController != null
            ? inputController.AimDeadZone : PlayerAimCursorSystem.DefaultDeadZone;
        float speedFactor = MobileMovementRules.EvaluateSpeedFactor(deflection, deadZone,
            IsRunning ? modeSpeed / walkingSpeed : 1f);
        // 以实际走速和跑速换算输入，模式切换与装备最终加算都不产生速度台阶。
        return input.normalized * Mathf.Clamp01(speedFactor * walkingSpeed / modeSpeed);
    }

    private void ReleaseMobileMovementInput()
    {
        if (!mobileMovementInputActive) return;
        bool restoreRun = manualRunBeforeMobile;
        mobileMovementInputActive = mobileRunExhausted = manualRunBeforeMobile = false;
        ApplyRunState(restoreRun);
    }

    private float GetWalkingSpeedValue()
    {
        float speed = Speed.Value;
        if (IsRunning)
            speed = (speed - Speed.FinalAdditive) / Mathf.Max(0.0001f, RunSpeedRate) + Speed.FinalAdditive;
        return Mathf.Max(0f, speed);
    }

    #endregion

    #region 网络移动与生存消耗分离
    private bool networkAuthorityMovement, networkMoving, networkRunning;
    public void EnableNetworkAuthorityMovement() => networkAuthorityMovement = true;
    public void ApplyNetworkMovementActivity(bool moving, bool running)
    { networkMoving = moving; networkRunning = running; }
    private void TickNetworkMovementCosts(float seconds)
    {
        SetRunState(networkRunning);
        if (networkMoving && stamina != null)
            stamina.ConsumeStaminaPerSecond(IsRunning ? StaminaConsumptionSources.MovementRun : StaminaConsumptionSources.MovementWalk,
                IsRunning ? RunStaminaConsume : MoveStaminaConsume, seconds);
        hungerActionInstance?.SetMovementState(networkMoving, IsRunning);
    }
    #endregion
}
