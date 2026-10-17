using System;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>翻滚只组合输入、主动移动、体力与无敌能力，不改角色根节点姿态。</summary>
public sealed partial class Mod_Roll : Module, IItemModuleDependencyBinder
{
    #region 配置与动作状态
    public const string ModuleId = "Roll";
    public const string StaminaSourceId = "flatworld.movement.roll";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public Ex_ModData modData = new() { ID = ModuleId };
    public override ModuleData _Data { get => modData; set => modData = (Ex_ModData)value; }
    [Min(.01f)] public float Duration = .4f;
    [Min(0f)] public float SpeedMultiplier = 2f;
    [Range(.01f, 1f)] public float ColliderScale = .5f;
    [Min(0f)] public float Cooldown = 0f;
    [Min(0f)] public float StaminaCost = 10f;
    public Transform visualRoot;
    public Collider2D[] bodyColliders = Array.Empty<Collider2D>();
    public bool IsRolling { get; private set; }
    public Vector2 Direction { get; private set; } = Vector2.right;
    public float Elapsed { get; private set; }
    public float CooldownRemaining { get; private set; }
    public uint Sequence { get; private set; }
    public Func<Vector2, bool> NetworkRollRequest;
    private Player player;
    private Mod_Mover mover;
    private Mod_Stamina stamina;
    private Mod_DamageReceiver health;
    private Mod_GameController controller;
    private Mod_TurnBack facing;
    private Mod_Mover.DrivenMotionLease motionLease;
    private IDisposable invulnerabilityLease;
    private bool networkAuthority;
    private bool networkPresentation;
    private bool networkOwned;
    private uint lastNetworkSequence;
    private int startedScene;
    private float activeDuration;
    private float activeCooldown;
    private bool starting;
    private bool startCancelled;
    private bool IsServerRemoteAuthority => networkAuthority && player != null && !player.IsLocalProfile;
    #endregion

    #region 依赖与生命周期
    public void BindModuleDependencies(ItemMods modules)
    {
        UnbindCallbacks();
        player = item as Player;
        mover = modules.GetMod_ByID<Mod_Mover>(ModText.Mod_Mover);
        stamina = modules.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
        health = modules.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        controller = modules.GetMod_ByID<Mod_GameController>(ModText.Controller);
        facing = modules.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody);
    }

    protected override void OnLoad()
    {
        BindModuleDependencies(item.itemMods);
        BindCallbacks();
        CooldownRemaining = 0f;
    }
    protected override void OnSave() { }
    protected override void OnUnload()
    {
        CancelRoll();
        UnbindCallbacks();
        networkAuthority = false;
        networkOwned = false;
        CooldownRemaining = 0f;
        Sequence = lastNetworkSequence = 0u;
    }
    private void OnDisable() { CancelRoll(); }
    private void OnDestroy() { CancelRoll(); UnbindCallbacks(); NetworkRollRequest = null; }
    private void BindCallbacks()
    {
        if (controller != null && player != null && player.IsLocalProfile) controller.RollRequested += HandleRollRequested;
        if (health != null) health.DeathStarted += HandleDeath;
    }
    private void UnbindCallbacks()
    {
        if (controller != null) controller.RollRequested -= HandleRollRequested;
        if (health != null) health.DeathStarted -= HandleDeath;
    }
    private void HandleRollRequested() => RequestRoll();
    private void HandleDeath(Mod_DamageReceiver _) => CancelRoll();

    /// <summary>服务端远端玩家不加载本地输入/UI，网络桥只建立翻滚所需能力。</summary>
    public void EnableNetworkAuthority(bool active)
    {
        if (networkAuthority == active) return;
        if (!active) { CancelRoll(); UnbindCallbacks(); networkAuthority = false; return; }
        BindModuleDependencies(item.itemMods);
        networkAuthority = true;
        BindCallbacks();
    }
    public override void ModUpdate(float deltaTime) => AdvanceRoll(deltaTime);
    #endregion

    #region 正式发动与推进
    public bool RequestRoll()
    {
        if (player == null || !player.IsLocalProfile || !CanStartRoll()) return false;
        Vector2 input = controller != null ? controller.ReadMoveInput(controller._inputActions.Win10.Move_Player) : Vector2.zero;
        if (controller?._mainCamera != null) input = controller._mainCamera.transform.TransformDirection(input);
        input = BodyTraumaBuffEffects.TransformMoveInput(item, input, Time.time);
        // 方向输入优先，静止时朝鼠标、手柄或手机共用的世界准星翻滚。
        Vector2 cursorDirection = Vector2.zero;
        if (controller != null && controller.TryGetMouseWorldPosition(out Vector3 cursorPosition))
            cursorDirection = WorldTopologyRuntime.ShortestDelta(item.transform.position, cursorPosition);
        Vector2 direction = ResolveDirection(input, cursorDirection, facing != null ? facing.currentDirection : Vector2.right);
        return NetworkRollRequest != null ? NetworkRollRequest(direction) : TryStartRoll(direction);
    }
    public bool TryStartRoll() => RequestRoll();
    public bool TryStartRoll(Vector2 worldDirection)
    {
        if (!GameNetwork.HasStateAuthority || !CanStartRoll()) return false;
        Vector2 direction = ResolveDirection(worldDirection, facing != null ? facing.currentDirection : Vector2.right);
        starting = true;
        startCancelled = false;
        uint generation = RuntimeGeneration;
        try
        {
            if (!IsServerRemoteAuthority && !mover.TryAcquireDrivenMotion(
                    direction * mover.WalkingSpeed * Mathf.Max(0f, SpeedMultiplier), out motionLease)) return false;
            if (!stamina.TryConsumeStamina(StaminaSourceId, Mathf.Max(0f, StaminaCost)) ||
                startCancelled || generation != RuntimeGeneration || !(IsRuntimeLoaded || networkAuthority) ||
                !CanContinueRoll() || (!IsServerRemoteAuthority && motionLease?.IsActive != true))
            { ReleaseRollEffects(); return false; }
            networkOwned = false;
            BeginRoll(direction, 0f, unchecked(Sequence + 1u));
            return true;
        }
        catch { CancelRoll(); throw; }
        finally { starting = false; }
    }
    private bool CanStartRoll() => !starting && !IsRolling && CooldownRemaining <= 0f && CanContinueRoll() &&
        (IsRuntimeLoaded || networkAuthority) && stamina != null && stamina.Data != null &&
        (IsServerRemoteAuthority || !mover.HasDrivenMotion) && CanUsePresentation();
    private bool CanContinueRoll() => isActiveAndEnabled && Enabled && player != null &&
        !player.DestructionHandled && health != null && !health.IsRuntimeTransitioning && health.Hp > 0f && mover != null &&
        (IsServerRemoteAuthority ? mover.IsSurfaceMotionContextAvailable : mover.IsSurfaceMotionAvailable) && (player.IsLocalProfile
            ? controller?.IsGameplayInputLocked != true && !EventSystemGuard.IsTextInputFocused
            : networkAuthority);

    private void BeginRoll(Vector2 direction, float elapsed, uint sequence)
    {
        Direction = direction;
        Elapsed = Mathf.Clamp(elapsed, 0f, Mathf.Max(.01f, Duration));
        Sequence = sequence;
        activeDuration = Mathf.Max(.01f, Duration);
        activeCooldown = Mathf.Max(0f, Cooldown);
        startedScene = item.gameObject.scene.handle;
        IsRolling = true;
        invulnerabilityLease = health.AcquireTemporaryInvulnerability();
        BeginPresentation();
        UpdatePresentation(Elapsed / activeDuration);
    }

    public void AdvanceRoll(float deltaTime)
    {
        float seconds = !float.IsNaN(deltaTime) && !float.IsInfinity(deltaTime) ? Mathf.Max(0f, deltaTime) : 0f;
        if (!IsRolling) { CooldownRemaining = Mathf.Max(0f, CooldownRemaining - seconds); return; }
        if (networkPresentation) return;
        if (!CanContinueRoll() || item.gameObject.scene.handle != startedScene ||
            (!IsServerRemoteAuthority && motionLease?.IsActive != true))
        { CancelRoll(); return; }
        Elapsed += seconds;
        if (Elapsed >= activeDuration)
        {
            float overrun = Elapsed - activeDuration;
            EndRoll();
            CooldownRemaining = Mathf.Max(0f, CooldownRemaining - overrun);
            return;
        }
        UpdatePresentation(Elapsed / activeDuration);
    }

    public void CancelRoll()
    {
        if (starting) startCancelled = true;
        if (IsRolling) EndRoll();
        else ReleaseRollEffects();
    }
    private void EndRoll()
    {
        IsRolling = false;
        networkPresentation = false;
        Elapsed = activeDuration;
        CooldownRemaining = activeCooldown;
        ReleaseRollEffects();
    }
    private void ReleaseRollEffects()
    {
        invulnerabilityLease?.Dispose(); invulnerabilityLease = null;
        motionLease?.Dispose(); motionLease = null;
        RestorePresentation();
    }
    public static Vector2 ResolveDirection(Vector2 moveInput, Vector2 cursorDirection, Vector2 facingDirection)
        => ResolveDirection(moveInput, ResolveDirection(cursorDirection, facingDirection));

    public static Vector2 ResolveDirection(Vector2 moveInput, Vector2 facingDirection)
    {
        if (FiniteDirection(moveInput) && moveInput.sqrMagnitude > .0001f) return moveInput.normalized;
        return FiniteDirection(facingDirection) && facingDirection.sqrMagnitude > .0001f ? facingDirection.normalized : Vector2.right;
    }
    private static bool FiniteDirection(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    #endregion

    #region 网络许可与远端表现
    public void ApplyNetworkOwnedRoll(Vector2 direction, float elapsed, uint sequence)
    {
        if (item is not Player local || !local.IsLocalProfile || !isActiveAndEnabled || !Enabled) return;
        if (sequence == lastNetworkSequence)
        {
            if (IsRolling && networkOwned)
            {
                Elapsed = Mathf.Max(Elapsed, elapsed);
                if (Elapsed >= activeDuration) EndRoll();
                else UpdatePresentation(Elapsed / activeDuration);
            }
            return;
        }
        if (lastNetworkSequence != 0 && unchecked((int)(sequence - lastNetworkSequence)) <= 0) return;
        lastNetworkSequence = sequence;
        CancelRoll();
        if (player == null) { BindModuleDependencies(item.itemMods); BindCallbacks(); }
        if (!CanContinueRoll() || !CanUsePresentation() || elapsed >= Mathf.Max(.01f, Duration)) return;
        Vector2 resolved = ResolveDirection(direction, Vector2.right);
        if (!mover.TryAcquireDrivenMotion(resolved * mover.WalkingSpeed * Mathf.Max(0f, SpeedMultiplier), out motionLease)) return;
        networkOwned = true;
        try { BeginRoll(resolved, elapsed, sequence); }
        catch { CancelRoll(); throw; }
    }

    /// <summary>远端状态按服务器绝对进度显示，重复快照不会重开动作或叠加缩放。</summary>
    public void ApplyNetworkPresentation(bool active, Vector2 direction, float elapsed, uint sequence)
    {
        if (lastNetworkSequence != 0 && unchecked((int)(sequence - lastNetworkSequence)) < 0) return;
        if (!active || !isActiveAndEnabled || !Enabled || elapsed >= Mathf.Max(.01f, Duration))
        { lastNetworkSequence = sequence; CancelRoll(); return; }
        if (lastNetworkSequence == sequence && !IsRolling) return;
        if (!networkPresentation || Sequence != sequence)
        {
            CancelRoll();
            if (!CanUsePresentation()) return;
            networkPresentation = true;
            Direction = ResolveDirection(direction, Vector2.right);
            Sequence = lastNetworkSequence = sequence;
            activeDuration = Mathf.Max(.01f, Duration);
            activeCooldown = Mathf.Max(0f, Cooldown);
            IsRolling = true;
            Elapsed = 0f;
            BeginPresentation();
        }
        Elapsed = Mathf.Clamp(Mathf.Max(Elapsed, elapsed), 0f, activeDuration);
        UpdatePresentation(Elapsed / activeDuration);
    }
    #endregion
}
