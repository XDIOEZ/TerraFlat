using System;
using FlatWorld.Combat;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>鸟的地面、助跑与飞行阶段；助跑仍接触地面，离地后才屏蔽近战和地块效果。</summary>
public enum BirdFlightPhase { Ground, RunUp, TakingOff, Flying, Landing }

/// <summary>常驻飞行物种可注册独立行为模块，复用鸟的飞行、导航和表现能力。</summary>
public interface IBirdFlightPilot
{
    /// <summary>由鸟的权威 Tick 驱动具体物种行为。</summary>
    void TickFlight(float deltaTime);
}

/// <summary>
/// GameObject 鸟、海鸥与常驻飞行生物共用模块。普通鸟地面 0.5 格/秒、助跑 2.6 格/秒、空中 6.3 格/秒，飞行受独立耐力约束。
/// Item 和刚体始终保存地面映射坐标；独立 LiftRoot 在飞行时提升表现与受击盒 2 单位。
/// 普通鸟助跑 1.2 格后离地；常驻飞行生物默认直接巡航，独立 Pilot 可在特定行为期间临时落地。
/// </summary>
public sealed partial class AI_Bird : Module, IAIActor, IItemModuleDependencyBinder, IRuntimeAiPersistencePolicy,
    IIncomingDamageRule, IIncomingDamageContextRule, ICombatAirborneTarget, IVisualGroundOffset,
    IWaterCurrentExposure
{
    #region 配置与独立存档
    private static readonly int GroundAnimationHash = Animator.StringToHash("Base Layer.Ground");
    private static readonly int WalkAnimationHash = Animator.StringToHash("Base Layer.Walk");
    private static readonly int TakingOffAnimationHash = Animator.StringToHash("Base Layer.TakingOff");
    private static readonly int FlyingAnimationHash = Animator.StringToHash("Base Layer.Flying");
    private static readonly int LandingAnimationHash = Animator.StringToHash("Base Layer.Landing");
    private static readonly float[] RunUpDirectionOffsets = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
    private static readonly float[] FlightTurnOffsets = { 45f, -45f, 90f, -90f, 135f, -135f, 180f }; // 巡航直线受阻后的转向顺序。
    private const float RunUpSampleSpacing = 0.25f;
    private const float RunUpStallSeconds = 1.5f;
    private const float FlightTargetArrivalDistance = 0.2f; // 到达目标后须立即接续下一段飞行。
    private const float FatigueLandingScanInterval = 0.4f;
    private const float FatigueLandingArrivalDistance = 0.3f;

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
        /// <summary>离地时延续的水平前进方向 X。</summary>
        public float TakeoffDirectionX;
        /// <summary>离地时延续的水平前进方向 Y。</summary>
        public float TakeoffDirectionY;
        /// <summary>本次助跑已经完成的前进距离。</summary>
        public float RunUpDistance;
        public float HomeX; // 常驻飞行物种的巢位 X。
        public float HomeY; // 常驻飞行物种的巢位 Y。
        public bool HasHome; // 是否绑定了巢位。
        public int HomeHiveGuid; // 所属蜂巢的稳定 GUID；独立鸟类为零。
    }

    public Ex_ModData Data = new();
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }
    public override string CanonicalModuleId => "AI_Bird";
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public float groundSpeed = 0.5f;
    [Tooltip("离地前在地面助跑的速度。"), Min(0.1f)] public float takeoffRunSpeed = 2.6f;
    [Tooltip("实际向前跑满此距离后才允许离地。"), Min(0.1f)] public float takeoffRunDistance = 1.2f;
    public float flightSpeed = 6.3f;
    public float flightHeight = 2f;
    public float groundDuration = 8f;
    public float flightDuration = 100f;
    public float transitionDuration = 0.75f;
    public float groundWanderRadius = 2f;
    public float flightWanderRadius = 18f;
    public bool permanentFlight; // 蜜蜂等物种默认空中巡航，不进入普通鸟耐力循环；Pilot 可临时切到地面。
    [Min(0.01f)] public float flightStaminaMax = 100f;
    [Min(0f)] public float flightStaminaDrainRate = 1f;
    [Min(0f)] public float flightStaminaRecoveryRate = 10f;
    [Tooltip("飞行耐力降到这个比例后，鸟开始等待无危险的安全落点。"), Range(0f, 1f)]
    public float fatigueLandingStaminaRatio = 0.5f;
    [Tooltip("疲劳降落时，每次在鸟周围搜索可站立地块的半径。"), Min(0f)]
    public float fatigueLandingSearchRadius = 4f;
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
    private Vector2 runUpLastPosition; // 上次助跑位移采样位置。
    private float runUpStallElapsed; // 助跑连续未前进的时间。
    private Vector2 fatigueLandingTarget;
    private Vector2 fatigueThreatOrigin;
    private float fatigueLandingScanRemaining;
    private bool hasFatigueLandingTarget;
    private bool fatigueAreaSafe;
    private long fatigueSafetyVersion = -1;
    private bool restoreGroundDestination;
    private BirdFlightStaminaBar staminaDisplay;
    private IBirdFlightPilot flightPilot; // 可替换的常驻飞行行为。
    public Item ActorItem => item;
    public bool PersistRuntimeAi => state.HomeHiveGuid == 0;
    public int HomeHiveGuid => state.HomeHiveGuid;
    public bool IsAlive => health != null && health.Hp > 0f;
    public BirdFlightPhase Phase => state.Phase;
    public bool IsAirborne => state.Phase != BirdFlightPhase.Ground && state.Phase != BirdFlightPhase.RunUp;
    public bool ReceivesWaterCurrent => !IsAirborne;
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
        if (liftRoot == null || liftRoot.parent == null || birdAnimator == null)
            throw new InvalidOperationException("鸟外壳缺少 LiftRoot、Animator 或根刚体，请运行鸟资源定向构建菜单。");
        liftOrigin = liftRoot.localPosition;
    }

    public override void Load()
    {
        body = item.GetComponent<Rigidbody2D>();
        if (body == null)
            throw new InvalidOperationException("鸟外壳缺少根刚体。");
        // 依赖装配阶段尚未执行 Module.LoadMod，item 只在 Load 阶段保证已绑定。
        if (!permanentFlight)
        {
            staminaDisplay = item.GetComponent<BirdFlightStaminaBar>();
            if (staminaDisplay == null) staminaDisplay = item.gameObject.AddComponent<BirdFlightStaminaBar>();
            staminaDisplay.Bind(this, liftRoot);
        }
        state = Data.GetData<FlightState>() ?? new FlightState();
        if (!Enum.IsDefined(typeof(BirdFlightPhase), state.Phase))
            throw new InvalidOperationException("鸟存档包含无效飞行阶段。");
        if (permanentFlight)
        {
            state.Phase = BirdFlightPhase.Flying;
            state.HasTransitionStartHeight = false;
            if (!state.HasHome)
                SetFlightHome(body.position);
        }
        state.Stamina = Mathf.Clamp(state.Stamina, 0f, flightStaminaMax);
        if (state.Stamina <= 0f) state.MustRecoverStamina = true;
        threatDetector.DetectionRadius = Mathf.Max(fleeTriggerDistance, fleeSafeDistance);
        loaded = true;
        stoppedForDeath = false;
        ResetForaging();
        ResetFatigueLanding();
        health.OnDamageReceived -= HandleBirdDamage;
        health.OnDamageReceived += HandleBirdDamage;
        restoreGroundDestination = state.Phase == BirdFlightPhase.Ground && state.HasTarget;
        ApplyFlightContact();
        if (state.Phase == BirdFlightPhase.RunUp)
        {
            runUpLastPosition = body.position;
            runUpStallElapsed = 0f;
            SetRunUpDestination();
        }
        ApplyFlightPresentation();
    }

    public override void Save() => Data.WriteData(state);

    public override void Unload()
    {
        loaded = false;
        if (health != null) health.OnDamageReceived -= HandleBirdDamage;
        ResetForaging();
        ResetFatigueLanding();
        if (liftRoot != null)
            liftRoot.localPosition = liftOrigin;
        tileReceiver?.SetEffectsSuppressed(this, false);
        mover?.StopMovement();
        if (body != null)
            body.velocity = Vector2.zero;
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
            staminaDisplay?.SetVisible(false);
            return;
        }
        if (GameNetwork.HasStateAuthority)
        {
            float step = Mathf.Max(0f, deltaTime);
            state.Elapsed += step;
            bool exhausted = !permanentFlight && AdvanceFlightStamina(
                state, step, flightStaminaMax, flightStaminaDrainRate, flightStaminaRecoveryRate);
            if (flightPilot == null)
                TickVigilance(step);
            if (flightPilot != null)
            {
                flightPilot.TickFlight(step);
                ApplyFlightPresentation();
                return;
            }
            // 耗尽后仍把降落放在逃跑/觅食之前，但不允许在不可站立地块上硬降落。
            // 若脚下没有安全地块，就继续飞到检测到安全落点为止。
            if (exhausted && (state.Phase == BirdFlightPhase.Flying || state.Phase == BirdFlightPhase.TakingOff))
            {
                escapeRemaining = 0f;
                if (state.Phase == BirdFlightPhase.TakingOff)
                    TickTakeoff(step);
                else
                    TickFatigueLanding(step, forceLanding: true);
                ApplyFlightPresentation();
                return;
            }
            // 强制降落必须先于逃跑和觅食，避免零耐力后仍被逃跑分支继续当作飞机移动。
            if (state.Phase == BirdFlightPhase.Landing && state.MustRecoverStamina)
            {
                mover.StopMovement();
                if (state.Elapsed >= transitionDuration) CompleteLanding();
                ApplyFlightPresentation();
                return;
            }
            if (TickEscape(step) ||
                (!permanentFlight && (TickFatigueLanding(step, forceLanding: false) || TickForaging(step))))
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
                case BirdFlightPhase.RunUp:
                    TickRunUp(step);
                    break;
                case BirdFlightPhase.TakingOff:
                    TickTakeoff(step);
                    break;
                case BirdFlightPhase.Flying:
                    TickFlightWander(step);
                    if (!permanentFlight && state.Elapsed >= flightDuration && CanLand(body.position)) BeginLanding();
                    break;
                case BirdFlightPhase.Landing:
                    mover.StopMovement();
                    if (state.Elapsed >= transitionDuration) CompleteLanding();
                    break;
            }
        }
        ApplyFlightPresentation();
    }

    /// <summary>地面起飞先选可走助跑线；从降落中受惊则沿现有高度直接重新加速。</summary>
    public void BeginTakeoff()
    {
        if (state.Stamina <= 0f || state.MustRecoverStamina ||
            (state.Phase != BirdFlightPhase.Ground && state.Phase != BirdFlightPhase.Landing)) return;
        if (state.Phase == BirdFlightPhase.Ground)
        {
            if (!TryChooseRunUpDirection(out Vector2 direction))
            {
                state.Elapsed = 0f;
                return;
            }
            EnterPhase(BirdFlightPhase.RunUp);
            state.TakeoffDirectionX = direction.x;
            state.TakeoffDirectionY = direction.y;
            state.RunUpDistance = 0f;
            runUpStallElapsed = 0f;
            runUpLastPosition = body.position;
            SetRunUpDestination();
            return;
        }
        Vector2 airborneDirection = escapeRemaining > 0f
            ? escapeDirection
            : new Vector2(state.TakeoffDirectionX, state.TakeoffDirectionY);
        if (airborneDirection.sqrMagnitude < 0.0001f) airborneDirection = UnityEngine.Random.insideUnitCircle;
        airborneDirection = airborneDirection.sqrMagnitude > 0.0001f ? airborneDirection.normalized : Vector2.right;
        EnterPhase(BirdFlightPhase.TakingOff);
        state.TakeoffDirectionX = airborneDirection.x;
        state.TakeoffDirectionY = airborneDirection.y;
    }
    public void BeginLanding()
    {
        if (!permanentFlight) EnterPhase(BirdFlightPhase.Landing);
    }

    public void CompleteLanding()
    {
        if (!permanentFlight) EnterPhase(BirdFlightPhase.Ground);
    }

    /// <summary>巢群在创建后绑定独立巡航中心，常驻飞行目标只围绕该位置选择。</summary>
    public void SetFlightHome(Vector2 position)
    {
        Vector2 home = WorldTopologyRuntime.NormalizePosition(position);
        state.HomeX = home.x;
        state.HomeY = home.y;
        state.HasHome = true;
    }

    /// <summary>蜂巢接管该鸟的生命周期，记录自身归属并绑定巡航中心。</summary>
    public void SetColonyHome(int hiveGuid, Vector2 position)
    {
        if (hiveGuid == 0)
            throw new InvalidOperationException("巢群成员必须绑定有效的蜂巢 GUID。");
        SetFlightHome(position);
        state.HomeHiveGuid = hiveGuid;
    }

    /// <summary>蜂巢被摧毁后解除巢群归属，但保留原巢位置作为自由飞行中心。</summary>
    public void ReleaseColonyHome(Vector2 position)
    {
        SetFlightHome(position);
        state.HomeHiveGuid = 0;
    }

    /// <summary>为常驻飞行物种注册独立行为，不让普通鸟的逃跑和觅食覆盖它。</summary>
    public void RegisterFlightPilot(IBirdFlightPilot pilot)
    {
        if (!permanentFlight || pilot == null || (flightPilot != null && !ReferenceEquals(flightPilot, pilot)))
            throw new InvalidOperationException("鸟的常驻飞行行为注册无效。");
        flightPilot = pilot;
    }

    /// <summary>模块卸载时解除本轮飞行行为绑定。</summary>
    public void UnregisterFlightPilot(IBirdFlightPilot pilot)
    {
        if (ReferenceEquals(flightPilot, pilot))
            flightPilot = null;
    }

    /// <summary>向独立行为开放同一套飞行通行和转向逻辑。</summary>
    public void FlyTo(Vector2 destination, float deltaTime)
    {
        EnsurePilotAirborne();
        FlyTowards(destination, deltaTime);
    }

    /// <summary>按基础飞行速度倍率移动，并返回本帧是否成功前进。</summary>
    public bool TryFlyTo(Vector2 destination, float deltaTime, float speedMultiplier)
    {
        if (speedMultiplier <= 0f)
            return false;
        EnsurePilotAirborne();
        mover.StopMovement();
        body.velocity = Vector2.zero;
        Vector2 displacement = WorldTopologyRuntime.ShortestDelta(body.position, destination);
        return MoveCruiseStep(displacement, flightSpeed * speedMultiplier, deltaTime);
    }

    /// <summary>没有明确目标时沿用以巢位为中心的巡航。</summary>
    public void WanderAroundHome(float deltaTime)
    {
        EnsurePilotAirborne();
        TickFlightWander(deltaTime);
    }

    /// <summary>常驻飞行 Pilot 在采蜜等明确地面行为期间临时切换地面接触与表现。</summary>
    public void SetPilotGrounded(IBirdFlightPilot pilot, bool grounded)
    {
        if (!permanentFlight || pilot == null || !ReferenceEquals(flightPilot, pilot))
            throw new InvalidOperationException("只有已注册的常驻飞行 Pilot 才能切换临时落地状态。");

        BirdFlightPhase target = grounded ? BirdFlightPhase.Ground : BirdFlightPhase.Flying;
        if (state.Phase == target)
        {
            if (grounded)
                mover.StopMovement();
            return;
        }
        EnterPhase(target);
    }

    /// <summary>独立 Pilot 发出飞行移动请求时自动结束临时落地，避免地面状态仍执行空中位移。</summary>
    private void EnsurePilotAirborne()
    {
        if (permanentFlight && flightPilot != null && state.Phase != BirdFlightPhase.Flying)
            EnterPhase(BirdFlightPhase.Flying);
    }

    /// <summary>纯耐力结算：飞行每秒扣 1，地面每秒回 10；耗尽后必须回满才能解除强制休息。</summary>
    public static bool AdvanceFlightStamina(FlightState state, float deltaTime, float maximum, float drain, float recovery)
    {
        float step = Mathf.Max(0f, deltaTime);
        maximum = Mathf.Max(0.01f, maximum);
        if (state.Phase == BirdFlightPhase.Ground || state.Phase == BirdFlightPhase.RunUp)
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
        if (phase != BirdFlightPhase.Flying)
            ResetFatigueLanding();
        ApplyFlightContact();
        ApplyFlightPresentation();
    }

    private void ApplyFlightContact()
    {
        tileReceiver.SetEffectsSuppressed(this, IsAirborne);
        mover.Speed.BaseValue = state.Phase == BirdFlightPhase.RunUp ? takeoffRunSpeed : groundSpeed;
        if (IsAirborne) mover.StopMovement();
    }

    private void ApplyFlightPresentation()
    {
        liftRoot.localPosition = liftOrigin + Vector3.up * CurrentFlightHeight;
        staminaDisplay?.Refresh();
        // 对象池先 Load 后激活；激活后以 Animator 的真实状态为准，避免重绑或外部播放造成飞行时残留步行动画。
        if (!birdAnimator.isActiveAndEnabled)
            return;

        int animationHash = ResolveAnimationStateHash();
        if (!birdAnimator.IsInTransition(0) &&
            birdAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash == animationHash)
            return;
        if (!birdAnimator.HasState(0, animationHash))
            throw new InvalidOperationException($"鸟外壳的 Animator 缺少 {state.Phase} 阶段动画。");
        birdAnimator.Play(animationHash, 0, 0f);
    }

    /// <summary>仅向阴影提供随 LiftRoot 升高的视觉位移，根部地面坐标仍由 Item 持有。</summary>
    public bool TryGetVisualGroundOffset(Transform visual, out Vector3 worldOffset)
    {
        worldOffset = Vector3.zero;
        if (visual == null || liftRoot == null || !visual.IsChildOf(liftRoot))
            return false;
        worldOffset = liftRoot.position - liftRoot.parent.TransformPoint(liftOrigin);
        return true;
    }

    /// <summary>地面按实际步行状态选动画，空中直接以飞行阶段为权威。</summary>
    private int ResolveAnimationStateHash()
    {
        return state.Phase switch
        {
            BirdFlightPhase.Ground => mover.IsActuallyMoving ? WalkAnimationHash : GroundAnimationHash,
            BirdFlightPhase.RunUp => WalkAnimationHash,
            BirdFlightPhase.TakingOff => TakingOffAnimationHash,
            BirdFlightPhase.Flying => FlyingAnimationHash,
            BirdFlightPhase.Landing => LandingAnimationHash,
            _ => throw new InvalidOperationException($"未知鸟类飞行阶段：{state.Phase}")
        };
    }

    /// <summary>受伤打断起降时从当前真实高度过渡，避免落地中重新起飞先瞬移到地面。</summary>
    public float CurrentFlightHeight => state.HasTransitionStartHeight
        ? Mathf.Lerp(state.TransitionStartHeight, state.Phase == BirdFlightPhase.Landing ? 0f : flightHeight,
            Mathf.Clamp01(state.Elapsed / Mathf.Max(0.001f, transitionDuration)))
        : ResolveHeight(state.Phase, state.Elapsed, transitionDuration, flightHeight);

    /// <summary>纯函数：助跑保持地面高度，离地后升高，落地归零。</summary>
    public static float ResolveHeight(BirdFlightPhase phase, float elapsed, float duration, float height)
    {
        float progress = Mathf.Clamp01(elapsed / Mathf.Max(0.001f, duration));
        return phase == BirdFlightPhase.Ground || phase == BirdFlightPhase.RunUp ? 0f : phase == BirdFlightPhase.TakingOff
            ? height * progress : phase == BirdFlightPhase.Landing ? height * (1f - progress) : height;
    }
    #endregion

    #region 地面与飞行寻路
    /// <summary>沿现有步行方向或逃离方向选出连续可走的助跑线。</summary>
    private bool TryChooseRunUpDirection(out Vector2 direction)
    {
        Vector2 preferred = escapeRemaining > 0f ? escapeDirection : mover.NavigationAgent.Velocity;
        if (preferred.sqrMagnitude < 0.0001f && state.HasTarget)
            preferred = WorldTopologyRuntime.ShortestDelta(body.position, new Vector2(state.TargetX, state.TargetY));
        if (preferred.sqrMagnitude < 0.0001f) preferred = UnityEngine.Random.insideUnitCircle;
        preferred = preferred.sqrMagnitude > 0.0001f ? preferred.normalized : Vector2.right;

        float runwayLength = takeoffRunDistance + mover.stopDistance + 0.5f;
        for (int index = 0; index < RunUpDirectionOffsets.Length; index++)
        {
            Vector2 candidate = Quaternion.Euler(0f, 0f, RunUpDirectionOffsets[index]) * preferred;
            if (!IsRunwayClear(candidate, runwayLength)) continue;
            direction = candidate;
            return true;
        }
        direction = Vector2.zero;
        return false;
    }

    /// <summary>助跑只经过导航认可的地面格。</summary>
    private bool IsRunwayClear(Vector2 direction, float length)
    {
        int samples = Mathf.CeilToInt(length / RunUpSampleSpacing);
        for (int index = 1; index <= samples; index++)
        {
            Vector2 sample = WorldTopologyRuntime.NormalizePosition(body.position + direction * (length * index / samples));
            if (!CanLand(sample)) return false;
        }
        float takeoffTravel = 0.5f * (takeoffRunSpeed + flightSpeed) * transitionDuration;
        return flightNavigation.CanTraverse(body.position,
            WorldTopologyRuntime.NormalizePosition(body.position + direction * (takeoffRunDistance + takeoffTravel)));
    }

    /// <summary>由地面导航驱动助跑，只有真实前进到足够距离才进入离地动画。</summary>
    private void TickRunUp(float deltaTime)
    {
        Vector2 movement = WorldTopologyRuntime.ShortestDelta(runUpLastPosition, body.position);
        runUpLastPosition = body.position;
        Vector2 direction = new(state.TakeoffDirectionX, state.TakeoffDirectionY);
        float forward = Vector2.Dot(movement, direction);
        if (forward > 0.001f)
        {
            state.RunUpDistance += forward;
            runUpStallElapsed = 0f;
        }
        else runUpStallElapsed += deltaTime;

        if (state.RunUpDistance >= takeoffRunDistance)
        {
            Vector2 actualDirection = mover.NavigationAgent.Velocity;
            if (actualDirection.sqrMagnitude > 0.0001f)
            {
                actualDirection.Normalize();
                state.TakeoffDirectionX = actualDirection.x;
                state.TakeoffDirectionY = actualDirection.y;
            }
            EnterPhase(BirdFlightPhase.TakingOff);
            return;
        }
        if (mover.HasReachedTarget || runUpStallElapsed >= RunUpStallSeconds)
            EnterPhase(BirdFlightPhase.Ground);
    }

    /// <summary>离地期间延续助跑方向，水平速度逐渐接近巡航速度。</summary>
    private void TickTakeoff(float deltaTime)
    {
        Vector2 direction = new(state.TakeoffDirectionX, state.TakeoffDirectionY);
        float progress = Mathf.Clamp01(state.Elapsed / Mathf.Max(0.001f, transitionDuration));
        float speed = Mathf.Lerp(takeoffRunSpeed, flightSpeed, progress);
        if (!MoveFlightStep(direction * (speed * deltaTime), speed, deltaTime))
        {
            BeginLanding();
            return;
        }
        if (state.Elapsed >= transitionDuration) EnterPhase(BirdFlightPhase.Flying);
    }

    /// <summary>加载助跑状态时恢复导航目的地，不从保存位置额外累计位移。</summary>
    private void SetRunUpDestination()
    {
        Vector2 direction = new(state.TakeoffDirectionX, state.TakeoffDirectionY);
        float remaining = Mathf.Max(0f, takeoffRunDistance - state.RunUpDistance);
        Vector2 destination = WorldTopologyRuntime.NormalizePosition(
            body.position + direction * (remaining + mover.stopDistance + 0.5f));
        mover.SetDestination(destination);
    }

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
        if (!state.HasTarget || delta.sqrMagnitude <= FlightTargetArrivalDistance * FlightTargetArrivalDistance)
        {
            float angle = UnityEngine.Random.value * Mathf.PI * 2f;
            float distance = UnityEngine.Random.Range(1f, Mathf.Max(1f, flightWanderRadius));
            Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 center = permanentFlight && state.HasHome
                ? new Vector2(state.HomeX, state.HomeY)
                : position;
            destination = WorldTopologyRuntime.NormalizePosition(center + direction * distance);
            SetWanderTarget(destination);
            delta = WorldTopologyRuntime.ShortestDelta(position, destination);
        }
        if (!MoveCruiseStep(delta, deltaTime))
        {
            state.HasTarget = false;
        }
    }

    /// <summary>
    /// 耐力降到阈值后进入“寻找安全落点”模式。危险、感知结果尚未刷新或附近无可站立地块时继续飞行；
    /// 找到落点后先飞到该地块，只有当前位置本身可站立时才真正开始下降。
    /// </summary>
    private bool TickFatigueLanding(float deltaTime, bool forceLanding)
    {
        if (state.Phase != BirdFlightPhase.Flying)
            return false;

        float threshold = Mathf.Max(0.01f, flightStaminaMax) * Mathf.Clamp01(fatigueLandingStaminaRatio);
        if (!forceLanding && state.Stamina > threshold)
        {
            ResetFatigueLanding();
            return false;
        }

        // 感知请求未应用时保持空中，不能拿旧结果判断“安全”。
        if (threatDetector.RequestedVersion > threatDetector.AppliedVersion)
        {
            ContinueFatiguedFlight(deltaTime);
            return true;
        }

        // 只在新的感知快照应用时重算危险 LOS，避免疲劳阶段逐帧重复扫描。
        if (fatigueSafetyVersion != threatDetector.AppliedVersion)
        {
            fatigueSafetyVersion = threatDetector.AppliedVersion;
            fatigueAreaSafe = !TryGetNearbyThreat(fleeSafeDistance, out Item landingThreat);
            if (!fatigueAreaSafe && landingThreat != null)
                fatigueThreatOrigin = landingThreat.transform.position;
        }

        // 疲劳降落采用更保守的安全距离；存在威胁就继续飞离威胁，不进入觅食降落。
        if (!fatigueAreaSafe)
        {
            hasFatigueLandingTarget = false;
            fatigueLandingScanRemaining = 0f;
            FlyAwayFromFatigueThreat(deltaTime);
            return true;
        }

        if (CanLand(body.position))
        {
            BeginLanding();
            return true;
        }

        if (hasFatigueLandingTarget)
        {
            if (!CanLand(fatigueLandingTarget) || !flightNavigation.CanTraverse(body.position, fatigueLandingTarget))
            {
                hasFatigueLandingTarget = false;
                fatigueLandingScanRemaining = 0f;
            }
            else
            {
                FlyTowards(fatigueLandingTarget, deltaTime);
                float distanceSqr = WorldTopologyRuntime.SqrDistance(body.position, fatigueLandingTarget);
                if (distanceSqr <= FatigueLandingArrivalDistance * FatigueLandingArrivalDistance && CanLand(body.position))
                    BeginLanding();
                return true;
            }
        }

        fatigueLandingScanRemaining -= Mathf.Max(0f, deltaTime);
        if (fatigueLandingScanRemaining <= 0f)
        {
            fatigueLandingScanRemaining = FatigueLandingScanInterval;
            if (TryFindFatigueLandingPoint(body.position, out fatigueLandingTarget))
            {
                hasFatigueLandingTarget = true;
                FlyTowards(fatigueLandingTarget, deltaTime);
                return true;
            }
        }

        ContinueFatiguedFlight(deltaTime);
        return true;
    }

    private void ContinueFatiguedFlight(float deltaTime)
    {
        if (hasFatigueLandingTarget)
            FlyTowards(fatigueLandingTarget, deltaTime);
        else
            TickFlightWander(deltaTime);
    }

    private void FlyAwayFromFatigueThreat(float deltaTime)
    {
        Vector2 away = WorldTopologyRuntime.ShortestDelta(fatigueThreatOrigin, body.position);
        if (away.sqrMagnitude < 0.0001f)
            away = UnityEngine.Random.insideUnitCircle;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector2.right;
        FlyTowards(WorldTopologyRuntime.NormalizePosition(body.position + away * flightWanderRadius), deltaTime);
    }

    /// <summary>无分配地按由近到远的方形环搜索附近可站立格，并确认飞行路径所需地形已经加载。</summary>
    private bool TryFindFatigueLandingPoint(Vector2 origin, out Vector2 landingPoint)
    {
        landingPoint = default;
        float radius = Mathf.Max(0f, fatigueLandingSearchRadius);
        if (radius <= 0f)
            return false;

        Vector2Int originCell = WorldTopologyRuntime.NormalizeCell(new Vector2Int(
            Mathf.FloorToInt(origin.x), Mathf.FloorToInt(origin.y)));
        int maxRing = Mathf.Max(1, Mathf.CeilToInt(radius));
        float radiusSqr = radius * radius;

        for (int ring = 1; ring <= maxRing; ring++)
        {
            bool found = false;
            float bestDistanceSqr = float.PositiveInfinity;
            Vector2 best = default;
            for (int y = -ring; y <= ring; y++)
            {
                for (int x = -ring; x <= ring; x++)
                {
                    if (Mathf.Abs(x) != ring && Mathf.Abs(y) != ring)
                        continue;

                    Vector2Int cell = WorldTopologyRuntime.NormalizeCell(originCell + new Vector2Int(x, y));
                    Vector2 candidate = WorldTopologyRuntime.NormalizePosition(new Vector2(cell.x + 0.5f, cell.y + 0.5f));
                    float distanceSqr = WorldTopologyRuntime.SqrDistance(origin, candidate);
                    if (distanceSqr > radiusSqr || distanceSqr >= bestDistanceSqr || !CanLand(candidate))
                        continue;
                    if (!flightNavigation.CanTraverse(origin, candidate))
                        continue;

                    bestDistanceSqr = distanceSqr;
                    best = candidate;
                    found = true;
                }
            }

            if (!found)
                continue;
            landingPoint = best;
            return true;
        }

        return false;
    }

    private void ResetFatigueLanding()
    {
        hasFatigueLandingTarget = false;
        fatigueLandingTarget = default;
        fatigueThreatOrigin = default;
        fatigueLandingScanRemaining = 0f;
        fatigueAreaSafe = false;
        fatigueSafetyVersion = -1;
    }

    /// <summary>巡航直线受阻时沿已加载地形转向；没有空中出口但可落脚时结束飞行。</summary>
    private bool MoveCruiseStep(Vector2 desiredDisplacement, float deltaTime)
        => MoveCruiseStep(desiredDisplacement, flightSpeed, deltaTime);

    /// <summary>允许具体飞行行为覆写本次巡航速度，同时复用统一通行和转向逻辑。</summary>
    private bool MoveCruiseStep(Vector2 desiredDisplacement, float speed, float deltaTime)
    {
        if (desiredDisplacement.sqrMagnitude <= 0.0001f || speed <= 0f) return false;
        if (MoveFlightStep(desiredDisplacement, speed, deltaTime)) return true;

        Vector2 forward = desiredDisplacement.normalized;
        for (int index = 0; index < FlightTurnOffsets.Length; index++)
        {
            Vector2 direction = Quaternion.Euler(0f, 0f, FlightTurnOffsets[index]) * forward;
            if (MoveFlightStep(direction * (speed * deltaTime), speed, deltaTime)) return true;
        }

        if (!permanentFlight && CanLand(body.position)) BeginLanding();
        return false;
    }

    /// <summary>飞行与离地共用同一段通行检查和位置通知。</summary>
    private bool MoveFlightStep(Vector2 direction, float speed, float deltaTime)
    {
        Vector2 position = body.position;
        Vector2 next = WorldTopologyRuntime.NormalizePosition(
            position + Vector2.ClampMagnitude(direction, speed * deltaTime));
        if (!flightNavigation.CanTraverse(position, next)) return false;
        body.position = next;
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        return true;
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
        phase == BirdFlightPhase.Ground || phase == BirdFlightPhase.RunUp ||
        (capabilities & CombatDeliveryCapabilities.AirborneTargets) != 0;
    #endregion
}
