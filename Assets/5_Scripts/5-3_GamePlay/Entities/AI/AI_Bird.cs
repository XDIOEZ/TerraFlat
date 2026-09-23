using System;
using FlatWorld.Combat;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>鸟的四段状态；下降期间仍为空中目标，接触地面后才恢复近战和地块效果。</summary>
public enum BirdFlightPhase { Ground, TakingOff, Flying, Landing }

/// <summary>
/// GameObject 鸟与海鸥共用模块。地面 0.5 格/秒、空中 6.3 格/秒，飞行受独立耐力约束。
/// Item 和刚体始终保存地面映射坐标；独立 LiftRoot 仅提升表现与受击盒 1.5 单位。
/// 状态、阶段计时与目的地随模块存档，回收时释放地块抑制并归零表现高度。
/// </summary>
public sealed partial class AI_Bird : Module, IAIActor, IItemModuleDependencyBinder,
    IIncomingDamageRule, IIncomingDamageContextRule, ICombatAirborneTarget
{
    #region 配置与独立存档
    [Serializable]
    public sealed class FlightState
    {
        public BirdFlightPhase Phase;
        public float Elapsed;
        public float TargetX;
        public float TargetY;
        public bool HasTarget;
        public float PauseRemaining;
        public float TransitionStartHeight;
        public bool HasTransitionStartHeight;
        public float Stamina = 100f;
        public bool MustRecoverStamina;
    }

    public Ex_ModData Data = new();
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }
    public override string CanonicalModuleId => "AI_Bird";
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public float groundSpeed = 0.5f;
    public float flightSpeed = 6.3f;
    public float flightHeight = 1.5f;
    public float groundDuration = 8f;
    public float flightDuration = 100f;
    public float transitionDuration = 0.75f;
    public float groundWanderRadius = 2f;
    public float flightWanderRadius = 18f;
    [Min(0.01f)] public float flightStaminaMax = 100f;
    [Min(0f)] public float flightStaminaDrainRate = 1f;
    [Min(0f)] public float flightStaminaRecoveryRate = 10f;
    public BirdFlightNavigationProfile flightNavigation = new();
    public Transform liftRoot;
    public Animator birdAnimator;

    private FlightState state = new();
    private Mover_AI mover;
    private TileEffectReceiver tileReceiver;
    private DamageReceiver health;
    private Rigidbody2D body;
    private bool loaded;
    private bool stoppedForDeath;
    private Vector3 liftOrigin;
    private string currentAnimation;
    private bool restoreGroundDestination;
    private BirdFlightStaminaBar staminaDisplay;
    public Item ActorItem => item;
    public bool IsAlive => health != null && health.Hp > 0f;
    public BirdFlightPhase Phase => state.Phase;
    public bool IsAirborne => state.Phase != BirdFlightPhase.Ground;
    public float FlightStamina => state.Stamina;
    public bool IsRecoveringFlightStamina => state.MustRecoverStamina;
    #endregion

    #region 装配与回收
    /// <summary>从模块注册表获取依赖，表现引用由显式外壳构建器绑定。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        mover = modules.RequireSingleModById<Mover_AI>(ModText.Mover);
        tileReceiver = modules.RequireSingleModById<TileEffectReceiver>(ModText.TileEffectReceiver);
        health = modules.RequireSingleModById<DamageReceiver>(ModText.Hp);
        food = modules.RequireSingleModById<Mod_Food>(ModText.Food);
        threatDetector = modules.RequireSingleModById<Mod_ItemDetector>(ModText.Detector);
        if (liftRoot == null || birdAnimator == null)
            throw new InvalidOperationException("鸟外壳缺少 LiftRoot、Animator 或根刚体，请运行鸟资源定向构建菜单。");
        liftOrigin = liftRoot.localPosition;
    }

    public override void Load()
    {
        body = item.GetComponent<Rigidbody2D>();
        if (body == null)
            throw new InvalidOperationException("鸟外壳缺少根刚体。");
        // 依赖装配阶段尚未执行 Module.LoadMod，item 只在 Load 阶段保证已绑定。
        staminaDisplay = item.GetComponent<BirdFlightStaminaBar>();
        if (staminaDisplay == null) staminaDisplay = item.gameObject.AddComponent<BirdFlightStaminaBar>();
        staminaDisplay.Bind(this, liftRoot);
        state = Data.GetData<FlightState>() ?? new FlightState();
        if (!Enum.IsDefined(typeof(BirdFlightPhase), state.Phase))
            throw new InvalidOperationException("鸟存档包含无效飞行阶段。");
        state.Stamina = Mathf.Clamp(state.Stamina, 0f, flightStaminaMax);
        if (state.Stamina <= 0f) state.MustRecoverStamina = true;
        threatDetector.DetectionRadius = Mathf.Max(fleeTriggerDistance, fleeSafeDistance);
        loaded = true;
        stoppedForDeath = false;
        currentAnimation = null;
        ResetForaging();
        health.OnDamageReceived -= HandleBirdDamage;
        health.OnDamageReceived += HandleBirdDamage;
        restoreGroundDestination = state.HasTarget && !IsAirborne;
        ApplyFlightContact();
        ApplyFlightPresentation();
    }

    public override void Save() => Data.WriteData(state);

    public override void Unload()
    {
        loaded = false;
        if (health != null) health.OnDamageReceived -= HandleBirdDamage;
        ResetForaging();
        if (liftRoot != null)
            liftRoot.localPosition = liftOrigin;
        tileReceiver?.SetEffectsSuppressed(this, false);
        mover?.StopMovement();
        if (body != null)
            body.velocity = Vector2.zero;
        currentAnimation = null;
        staminaDisplay?.SetVisible(false);
    }
    #endregion

    #region 阶段推进
    public override void ModUpdate(float deltaTime)
    {
        if (!loaded)
            return;
        if (!IsAlive)
        {
            if (!stoppedForDeath)
            {
                stoppedForDeath = true;
                mover.StopMovement();
                body.velocity = Vector2.zero;
                tileReceiver.SetEffectsSuppressed(this, false);
                liftRoot.localPosition = liftOrigin;
            }
            return;
        }
        if (GameNetwork.HasStateAuthority)
        {
            float step = Mathf.Max(0f, deltaTime);
            state.Elapsed += step;
            bool exhausted = AdvanceFlightStamina(state, step, flightStaminaMax, flightStaminaDrainRate, flightStaminaRecoveryRate);
            if (exhausted && (state.Phase == BirdFlightPhase.Flying || state.Phase == BirdFlightPhase.TakingOff))
            {
                escapeRemaining = 0f;
                BeginLanding();
            }
            // 强制降落必须先于逃跑和觅食，避免零耐力后仍被逃跑分支继续当作飞机移动。
            if (state.Phase == BirdFlightPhase.Landing && state.MustRecoverStamina)
            {
                mover.StopMovement();
                if (state.Elapsed >= transitionDuration) CompleteLanding();
                ApplyFlightPresentation();
                return;
            }
            TickVigilance(step);
            if (TickEscape(step) || TickForaging(step))
            {
                ApplyFlightPresentation();
                return;
            }
            switch (state.Phase)
            {
                case BirdFlightPhase.Ground:
                    TickGroundWander(step);
                    if (state.Elapsed >= groundDuration) BeginTakeoff();
                    break;
                case BirdFlightPhase.TakingOff:
                    mover.StopMovement();
                    if (state.Elapsed >= transitionDuration) EnterPhase(BirdFlightPhase.Flying);
                    break;
                case BirdFlightPhase.Flying:
                    TickFlightWander(step);
                    if (state.Elapsed >= flightDuration && CanLand(body.position)) BeginLanding();
                    break;
                case BirdFlightPhase.Landing:
                    mover.StopMovement();
                    if (state.Elapsed >= transitionDuration) CompleteLanding();
                    break;
            }
        }
        ApplyFlightPresentation();
    }

    /// <summary>起飞入口先撤销地块效果，再移动受击盒；同一 Tick 即拒绝近战。</summary>
    public void BeginTakeoff()
    {
        if (state.Stamina <= 0f || state.MustRecoverStamina || state.Phase == BirdFlightPhase.TakingOff) return;
        EnterPhase(BirdFlightPhase.TakingOff);
    }
    public void BeginLanding() => EnterPhase(BirdFlightPhase.Landing);
    public void CompleteLanding() => EnterPhase(BirdFlightPhase.Ground);

    /// <summary>纯耐力结算：飞行每秒扣 1，地面每秒回 10；耗尽后必须回满才能解除强制休息。</summary>
    public static bool AdvanceFlightStamina(FlightState state, float deltaTime, float maximum, float drain, float recovery)
    {
        float step = Mathf.Max(0f, deltaTime);
        maximum = Mathf.Max(0.01f, maximum);
        if (state.Phase == BirdFlightPhase.Ground)
        {
            state.Stamina = Mathf.Min(maximum, state.Stamina + Mathf.Max(0f, recovery) * step);
            if (state.Stamina >= maximum) state.MustRecoverStamina = false;
        }
        else
        {
            state.Stamina = Mathf.Max(0f, state.Stamina - Mathf.Max(0f, drain) * step);
            if (state.Stamina <= 0f) state.MustRecoverStamina = true;
        }
        return state.MustRecoverStamina && state.Stamina <= 0f;
    }

    private void EnterPhase(BirdFlightPhase phase)
    {
        float previousHeight = CurrentFlightHeight;
        mover.StopMovement();
        body.velocity = Vector2.zero;
        state.Phase = phase;
        state.TransitionStartHeight = previousHeight;
        state.HasTransitionStartHeight = phase == BirdFlightPhase.TakingOff || phase == BirdFlightPhase.Landing;
        state.Elapsed = 0f;
        state.HasTarget = false;
        state.PauseRemaining = 0f;
        ApplyFlightContact();
        ApplyFlightPresentation();
    }

    private void ApplyFlightContact()
    {
        tileReceiver.SetEffectsSuppressed(this, IsAirborne);
        mover.Speed.BaseValue = groundSpeed;
        if (IsAirborne) mover.StopMovement();
    }

    private void ApplyFlightPresentation()
    {
        liftRoot.localPosition = liftOrigin + Vector3.up * CurrentFlightHeight;
        // 对象池先 Load 后激活；保留待播状态，激活后的首个 Tick 才交给 Animator。
        if (!birdAnimator.isActiveAndEnabled)
        {
            currentAnimation = null;
            return;
        }
        string animation = IsAirborne ? state.Phase.ToString() : mover.IsActuallyMoving ? "Walk" : "Ground";
        if (currentAnimation == animation)
            return;
        currentAnimation = animation;
        birdAnimator.Play(animation);
    }

    /// <summary>受伤打断起降时从当前真实高度过渡，避免落地中重新起飞先瞬移到地面。</summary>
    public float CurrentFlightHeight => state.HasTransitionStartHeight
        ? Mathf.Lerp(state.TransitionStartHeight, state.Phase == BirdFlightPhase.Landing ? 0f : flightHeight,
            Mathf.Clamp01(state.Elapsed / Mathf.Max(0.001f, transitionDuration)))
        : ResolveHeight(state.Phase, state.Elapsed, transitionDuration, flightHeight);

    /// <summary>纯函数：起飞升高、巡航恒高、落地归零，永不改写 Item 坐标。</summary>
    public static float ResolveHeight(BirdFlightPhase phase, float elapsed, float duration, float height)
    {
        float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, duration));
        return phase == BirdFlightPhase.Ground ? 0f : phase == BirdFlightPhase.TakingOff
            ? height * progress : phase == BirdFlightPhase.Landing ? height * (1f - progress) : height;
    }
    #endregion

    #region 地面与飞行寻路
    private void TickGroundWander(float deltaTime)
    {
        mover.Speed.BaseValue = groundSpeed;
        if (restoreGroundDestination)
        {
            mover.SetDestination(new Vector2(state.TargetX, state.TargetY));
            restoreGroundDestination = false;
        }
        if (state.HasTarget && !mover.HasReachedTarget)
            return;
        if (state.HasTarget)
        {
            state.HasTarget = false;
            state.PauseRemaining = 1.5f;
            mover.StopMovement();
        }
        state.PauseRemaining -= deltaTime;
        if (state.PauseRemaining > 0f)
            return;
        Vector2 destination = WorldTopologyRuntime.NormalizePosition(body.position + UnityEngine.Random.insideUnitCircle * groundWanderRadius);
        if (!CanLand(destination))
            return;
        SetWanderTarget(destination);
        mover.SetDestination(destination);
    }

    private void TickFlightWander(float deltaTime)
    {
        mover.StopMovement();
        body.velocity = Vector2.zero;
        Vector2 position = body.position;
        Vector2 destination = new(state.TargetX, state.TargetY);
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(position, destination);
        if (!state.HasTarget || delta.sqrMagnitude < 0.04f)
        {
            destination = WorldTopologyRuntime.NormalizePosition(position + UnityEngine.Random.insideUnitCircle * flightWanderRadius);
            SetWanderTarget(destination);
            delta = WorldTopologyRuntime.ShortestDelta(position, destination);
        }
        Vector2 next = WorldTopologyRuntime.NormalizePosition(position + Vector2.ClampMagnitude(delta, flightSpeed * deltaTime));
        if (!flightNavigation.CanTraverse(position, next))
        {
            state.HasTarget = false;
            return;
        }
        body.position = next;
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
    }

    private void SetWanderTarget(Vector2 position)
    {
        mover.TargetPosition = position;
        state.TargetX = position.x;
        state.TargetY = position.y;
        state.HasTarget = true;
    }

    public static bool CanLand(Vector2 position)
    {
        WorldNavigationManager navigation = WorldNavigationManager.ExistingInstance;
        return navigation != null && navigation.TryGetCell(position, out _, out bool walkable) && walkable;
    }
    #endregion

    #region 局部受击能力
    public float GetDamageMultiplier(IDamageSender sender) => AllowsDamage(state.Phase,
        sender is IDamageDeliverySource delivery ? delivery.DeliveryCapabilities : CombatDeliveryCapabilities.None) ? 1f : 0f;
    public float GetDamageMultiplier(in CombatDamageContext context) =>
        AllowsDamage(state.Phase, context.DeliveryCapabilities) ? 1f : 0f;

    /// <summary>近战穿刺不等于投射物；可扩展能力允许 MOD 远程技能显式命中空中目标。</summary>
    public static bool AllowsDamage(BirdFlightPhase phase, CombatDeliveryCapabilities capabilities) =>
        phase == BirdFlightPhase.Ground || (capabilities & CombatDeliveryCapabilities.AirborneTargets) != 0;
    #endregion
}
