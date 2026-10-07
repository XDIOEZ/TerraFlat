using System;
using System.Collections.Generic;
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
public sealed partial class Mod_AI_Bird : Module, IAIActor, IItemModuleDependencyBinder, IRuntimeAiPersistencePolicy,
    IIncomingDamageRule, IIncomingDamageContextRule, ICombatAirborneTarget, IVisualGroundOffset,
    IWaterCurrentExposure, ITemperatureSafetyMovement, IDamageSender
{
    #region 配置与独立存档
    private static readonly int GroundAnimationHash = Animator.StringToHash("Base Layer.Ground");
    private static readonly int WalkAnimationHash = Animator.StringToHash("Base Layer.Walk");
    private static readonly int TakingOffAnimationHash = Animator.StringToHash("Base Layer.TakingOff");
    private static readonly int FlyingAnimationHash = Animator.StringToHash("Base Layer.Flying");
    private static readonly int LandingAnimationHash = Animator.StringToHash("Base Layer.Landing");
    private static readonly float[] RunUpDirectionOffsets = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
    private const float FlightSteeringStepSeconds = 0.05f;
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
        public float FlightDirectionX; // 巡航方向随个体保存，换目标不重置朝向。
        public float FlightDirectionY;
        /// <summary>本次助跑已经完成的前进距离。</summary>
        public float RunUpDistance;
        public float HomeX; // 常驻飞行物种的巢位 X。
        public float HomeY; // 常驻飞行物种的巢位 Y。
        public bool HasHome; // 是否绑定了巢位。
        public int HomeHiveGuid; // 所属蜂巢的稳定 GUID；独立鸟类为零。
        public AnimalEggLayingSchedule EggLaying = new(); // 产蛋截止日随同个体保存，读档不重新随机。
    }

    public Ex_ModData Data = new();
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData)value; }
    public override string CanonicalModuleId => "AI_Bird";
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;
    public float groundSpeed = 0.5f;
    [Tooltip("离地前在地面助跑的速度。"), Min(0.1f)] public float takeoffRunSpeed = 2.6f;
    [Tooltip("实际向前跑满此距离后才允许离地。"), Min(0.1f)] public float takeoffRunDistance = 1.2f;
    public float flightSpeed = 6.3f;
    [Tooltip("巡航速度下的最小转弯半径，低速接近落点时允许收小。"), Min(0.1f)]
    public float flightTurnRadius = 2.5f;
    [Tooltip("飞行方向每秒最多转过的角度。"), Min(1f)] public float flightMaxTurnSpeed = 150f;
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
    private Mod_Mover_AI mover;
    private Mod_TileEffectReceiver tileReceiver;
    private Mod_DamageReceiver health;
    private Rigidbody2D body;
    private Collider2D[] flightSolidColliders = Array.Empty<Collider2D>();
    private bool[] flightSolidColliderOriginalStates = Array.Empty<bool>();
    private bool flightSolidCollidersBound;
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
    private bool temperatureSafetyRetreat;
    private bool temperatureSafetyReached;
    private Vector2 temperatureSafetyDestination;
    public Item ActorItem => item;
    public bool PersistRuntimeAi => state.HomeHiveGuid == 0;
    public int HomeHiveGuid => state.HomeHiveGuid;
    public bool IsAlive => health != null && health.Hp > 0f;
    public BirdFlightPhase Phase => state.Phase;
    public bool IsAirborne => state.Phase != BirdFlightPhase.Ground && state.Phase != BirdFlightPhase.RunUp;
    public bool ReceivesWaterCurrent => !IsAirborne;
    public float FlightStamina => state.Stamina;
    public bool IsRecoveringFlightStamina => state.MustRecoverStamina;
    public int TemperatureSafetyMovementPriority => 100;
    public bool ShouldAdvanceTemperatureSafetyDestination => temperatureSafetyRetreat && temperatureSafetyReached;
    #endregion

    #region 装配与回收
    /// <summary>从模块注册表获取依赖，表现引用由显式外壳构建器绑定。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        mover = modules.RequireSingleModById<Mod_Mover_AI>(ModText.Mod_Mover);
        tileReceiver = modules.RequireSingleModById<Mod_TileEffectReceiver>(ModText.Mod_TileEffectReceiver);
        health = modules.RequireSingleModById<Mod_DamageReceiver>(ModText.Hp);
        food = modules.RequireSingleModById<Mod_Food>(ModText.Food);
        threatDetector = modules.RequireSingleModById<Mod_ItemDetector>(ModText.Detector);
        if (liftRoot == null || liftRoot.parent == null || birdAnimator == null)
            throw new InvalidOperationException("鸟外壳缺少 LiftRoot、Animator 或根刚体，请运行鸟资源定向构建菜单。");
        liftOrigin = liftRoot.localPosition;
    }

    protected override void OnLoad()
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
        InitializeEggLaying();
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
        temperatureSafetyRetreat = false;
        temperatureSafetyReached = false;
        ResetForaging();
        ResetFishHunting(releaseCaptured: false);
        ResetFatigueLanding();
        ClearTemperatureSafetyDestination();
        health.OnDamageReceived -= HandleBirdDamage;
        health.OnDamageReceived += HandleBirdDamage;
        health.DeathStarted -= HandleBirdDeath;
        health.DeathStarted += HandleBirdDeath;
        BindFlightHitbox();
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

    protected override void OnSave() => Data.WriteData(state);

    protected override void OnUnload()
    {
        loaded = false;
        if (health != null)
        {
            health.OnDamageReceived -= HandleBirdDamage;
            health.DeathStarted -= HandleBirdDeath;
        }
        ReleaseFlightHitbox();
        RestoreFlightSolidColliders();
        ClearTemperatureSafetyDestination();
        ResetFishHunting(releaseCaptured: true);
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
        EnsureFlightSolidColliders();
        if (!IsAlive)
        {
            if (!stoppedForDeath)
            {
                stoppedForDeath = true;
                ResetFishHunting(releaseCaptured: true);
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
            TickEggLaying();
            if (temperatureSafetyRetreat)
            {
                TickTemperatureSafetyRetreat(step);
                ApplyFlightPresentation();
                return;
            }
            if (flightPilot != null)
            {
                flightPilot.TickFlight(step);
                ApplyFlightPresentation();
                return;
            }
            float fatigueThreshold = Mathf.Max(0.01f, flightStaminaMax) * Mathf.Clamp01(fatigueLandingStaminaRatio);
            if (IsFishHunting && (state.MustRecoverStamina || state.Stamina <= fatigueThreshold))
                ResetFishHunting(releaseCaptured: true);
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
                (!permanentFlight && (TickFishHunting(step) || TickFatigueLanding(step, forceLanding: false) || TickForaging(step))))
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
        Vector2 airborneDirection = new(state.FlightDirectionX, state.FlightDirectionY);
        if (airborneDirection.sqrMagnitude < 0.0001f)
            airborneDirection = new Vector2(state.TakeoffDirectionX, state.TakeoffDirectionY);
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

    #region 温度避险

    public void SetTemperatureSafetyDestination(Vector2 destination)
    {
        temperatureSafetyDestination = WorldTopologyRuntime.NormalizePosition(destination);
        temperatureSafetyRetreat = true;
        temperatureSafetyReached = false;
        state.HasTarget = false;
        ResetFishHunting(releaseCaptured: true);
        ResetForaging();
        ResetFatigueLanding();
    }

    public void ClearTemperatureSafetyDestination()
    {
        temperatureSafetyRetreat = false;
        temperatureSafetyReached = false;
        if (mover != null)
            mover.ClearTemperatureSafetyDestination();
    }

    private void TickTemperatureSafetyRetreat(float deltaTime)
    {
        Vector2 current = body.position;
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(current, temperatureSafetyDestination);
        if (delta.sqrMagnitude <= FlightTargetArrivalDistance * FlightTargetArrivalDistance)
        {
            mover.StopMovement();
            body.velocity = Vector2.zero;
            temperatureSafetyReached = true;
            return;
        }

        temperatureSafetyReached = false;
        if (IsAirborne)
        {
            mover.ClearTemperatureSafetyDestination();
            if (!MoveCruiseStep(delta, deltaTime))
                temperatureSafetyReached = true;
            return;
        }

        mover.SetTemperatureSafetyDestination(temperatureSafetyDestination);
    }

    #endregion

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
        if (phase == BirdFlightPhase.Ground || phase == BirdFlightPhase.RunUp)
        {
            state.FlightDirectionX = 0f;
            state.FlightDirectionY = 0f;
        }
        if (phase != BirdFlightPhase.Flying)
            ResetFatigueLanding();
        ApplyFlightContact();
        ApplyFlightPresentation();
    }

    private void ApplyFlightContact()
    {
        EnsureFlightSolidColliders();
        ApplyFlightSolidCollisionState();
        tileReceiver.SetEffectsSuppressed(this, IsAirborne);
        mover.Speed.BaseValue = state.Phase == BirdFlightPhase.RunUp ? takeoffRunSpeed : groundSpeed;
        if (IsAirborne) mover.StopMovement();
    }

    /// <summary>离地后关闭实体阻挡碰撞，避免位置驱动的轻型飞行生物反向推动重型角色。</summary>
    private void EnsureFlightSolidColliders()
    {
        if (flightSolidCollidersBound || item == null)
            return;

        Collider2D[] rootColliders = item.GetComponents<Collider2D>();
        int solidCount = 0;
        for (int i = 0; i < rootColliders.Length; i++)
            if (rootColliders[i] != null && !rootColliders[i].isTrigger)
                solidCount++;
        if (solidCount == 0)
            return;

        flightSolidColliders = new Collider2D[solidCount];
        flightSolidColliderOriginalStates = new bool[solidCount];
        int writeIndex = 0;
        for (int i = 0; i < rootColliders.Length; i++)
        {
            Collider2D collider = rootColliders[i];
            if (collider == null || collider.isTrigger)
                continue;
            flightSolidColliders[writeIndex] = collider;
            flightSolidColliderOriginalStates[writeIndex] = collider.enabled;
            writeIndex++;
        }
        flightSolidCollidersBound = true;
        ApplyFlightSolidCollisionState();
    }

    /// <summary>飞行仅保留 Trigger 感知与受击盒，落地后恢复原本的实体阻挡。</summary>
    private void ApplyFlightSolidCollisionState()
    {
        if (!flightSolidCollidersBound)
            return;
        for (int i = 0; i < flightSolidColliders.Length; i++)
        {
            Collider2D collider = flightSolidColliders[i];
            if (collider != null)
                collider.enabled = !IsAirborne && flightSolidColliderOriginalStates[i];
        }
    }

    private void RestoreFlightSolidColliders()
    {
        if (flightSolidCollidersBound)
        {
            for (int i = 0; i < flightSolidColliders.Length; i++)
            {
                Collider2D collider = flightSolidColliders[i];
                if (collider != null)
                    collider.enabled = flightSolidColliderOriginalStates[i];
            }
        }
        flightSolidColliders = Array.Empty<Collider2D>();
        flightSolidColliderOriginalStates = Array.Empty<bool>();
        flightSolidCollidersBound = false;
    }

    private void ApplyFlightPresentation()
    {
        float visualHeight = HasFishHuntVisualHeightOverride ? FishHuntVisualHeightOverride : CurrentFlightHeight;
        liftRoot.localPosition = liftOrigin + Vector3.up * visualHeight;
        RefreshFlightHitbox();
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
        // 导航代理尚未完成装配时退回移动模块的真实驱动速度，避免起飞首帧空引用。
        Vector2 preferred = escapeRemaining > 0f
            ? escapeDirection
            : mover.NavigationAgent != null
                ? mover.NavigationAgent.Velocity
                : mover.DrivenVelocity;
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
        float arrival = permanentFlight ? FlightTargetArrivalDistance : Mathf.Max(FlightTargetArrivalDistance, flightTurnRadius * 0.65f);
        if (!state.HasTarget || delta.sqrMagnitude <= arrival * arrival)
        {
            Vector2 heading = new(state.FlightDirectionX, state.FlightDirectionY);
            float angle = !permanentFlight && heading.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(heading.y, heading.x) + UnityEngine.Random.Range(-65f, 65f) * Mathf.Deg2Rad
                : UnityEngine.Random.value * Mathf.PI * 2f;
            float minimumDistance = permanentFlight ? 1f : Mathf.Max(1f, flightTurnRadius * 2f);
            float distance = UnityEngine.Random.Range(minimumDistance, Mathf.Max(minimumDistance, flightWanderRadius));
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
        => TryFindLandingPoint(origin, fatigueLandingSearchRadius, out landingPoint);

    /// <summary>按由近到远的方形环寻找已加载且飞行可达的落脚格，疲劳降落与捕食搬运共用。</summary>
    private bool TryFindLandingPoint(Vector2 origin, float searchRadius, out Vector2 landingPoint)
    {
        landingPoint = default;
        float radius = Mathf.Max(0f, searchRadius);
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

    /// <summary>目标只决定期望方向，实际飞行沿有半径的弧线逐步转向。</summary>
    private bool MoveCruiseStep(Vector2 desiredDisplacement, float deltaTime)
        => MoveCruiseStep(desiredDisplacement, flightSpeed, deltaTime);

    /// <summary>允许具体飞行行为覆写本次巡航速度，同时复用统一通行和转向逻辑。</summary>
    private bool MoveCruiseStep(Vector2 desiredDisplacement, float speed, float deltaTime)
    {
        if (desiredDisplacement.sqrMagnitude <= 0.0001f || speed <= 0f || deltaTime <= 0f) return false;
        ChunkMgr chunks = ChunkMgr.Instance;
        if (chunks == null || !chunks.TryCreateTerrainPresenceQuery(out ChunkMgr.RuntimeTerrainPresenceQuery query))
        {
            if (!permanentFlight && CanLand(body.position)) BeginLanding();
            return false;
        }
        Vector2 destination = query.NormalizePosition(body.position + desiredDisplacement);
        float remainingTime = deltaTime;
        bool moved = false;
        while (remainingTime > 0.0001f)
        {
            Vector2 delta = query.ShortestDelta(body.position, destination);
            if (delta.sqrMagnitude <= 0.0001f) break;
            float step = Mathf.Min(remainingTime, FlightSteeringStepSeconds);
            // 靠近落点先减速，再以更小半径接近，避免围着近目标不停盘旋。
            float approachSpeed = Mathf.Min(speed, Mathf.Max(speed * 0.15f,
                delta.magnitude * speed / Mathf.Max(0.1f, flightTurnRadius)));
            Vector2 forward = new(state.FlightDirectionX, state.FlightDirectionY);
            if (forward.sqrMagnitude <= 0.0001f)
                forward = new Vector2(state.TakeoffDirectionX, state.TakeoffDirectionY);
            if (forward.sqrMagnitude <= 0.0001f) forward = delta.normalized;
            float currentAngle = Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg;
            float desiredAngle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            float speedRatio = approachSpeed / Mathf.Max(0.01f, flightSpeed);
            float radius = Mathf.Max(0.1f, flightTurnRadius * speedRatio * speedRatio);
            float turnSpeed = Mathf.Min(flightMaxTurnSpeed, approachSpeed / radius * Mathf.Rad2Deg);
            if (!TryMoveFlightArc(currentAngle, desiredAngle, approachSpeed, turnSpeed, step, delta.magnitude, ref query) &&
                !TryMoveFlightArc(currentAngle, currentAngle, approachSpeed, turnSpeed, step, delta.magnitude, ref query) &&
                !TryMoveFlightArc(currentAngle, currentAngle + 90f, approachSpeed, turnSpeed, step, delta.magnitude, ref query) &&
                !TryMoveFlightArc(currentAngle, currentAngle - 90f, approachSpeed, turnSpeed, step, delta.magnitude, ref query))
            {
                if (!permanentFlight && CanLand(body.position)) BeginLanding();
                return moved;
            }
            moved = true;
            remainingTime -= step;
        }
        return moved;
    }

    /// <summary>提前检查将要经过的弧线，在已加载地形边界前开始拐弯。</summary>
    private bool TryMoveFlightArc(float angle, float targetAngle, float speed, float turnSpeed,
        float deltaTime, float targetDistance, ref ChunkMgr.RuntimeTerrainPresenceQuery query)
    {
        float lookAhead = Mathf.Min(targetDistance, Mathf.Max(flightTurnRadius * 1.5f, speed * 0.35f));
        float previewRemaining = Mathf.Min(0.75f, lookAhead / Mathf.Max(0.01f, speed));
        Vector2 preview = body.position;
        float previewAngle = angle;
        while (previewRemaining > 0.0001f)
        {
            float step = Mathf.Min(previewRemaining, 0.1f);
            Vector2 offset = ResolveFlightArc(previewAngle, targetAngle, speed, turnSpeed, step, out previewAngle);
            Vector2 next = query.NormalizePosition(preview + offset);
            if (!flightNavigation.CanTraverse(preview, next, ref query)) return false;
            preview = next;
            previewRemaining -= step;
        }
        Vector2 displacement = ResolveFlightArc(angle, targetAngle, speed, turnSpeed, deltaTime, out float nextAngle);
        if (Mathf.Abs(Mathf.DeltaAngle(angle, targetAngle)) < 45f)
            displacement = Vector2.ClampMagnitude(displacement, targetDistance);
        if (!MoveFlightStep(displacement, speed, deltaTime, ref query)) return false;
        state.FlightDirectionX = Mathf.Cos(nextAngle * Mathf.Deg2Rad);
        state.FlightDirectionY = Mathf.Sin(nextAngle * Mathf.Deg2Rad);
        return true;
    }

    /// <summary>积分限速转向的圆弧，达到目标朝向后再沿直线走完剩余时间。</summary>
    public static Vector2 ResolveFlightArc(float angle, float targetAngle, float speed, float turnSpeed,
        float deltaTime, out float nextAngle)
    {
        float step = Mathf.Max(0f, deltaTime);
        float turn = Mathf.Clamp(Mathf.DeltaAngle(angle, targetAngle), -turnSpeed * step, turnSpeed * step);
        nextAngle = angle + turn;
        float turnTime = turnSpeed > 0f ? Mathf.Min(step, Mathf.Abs(turn) / turnSpeed) : 0f;
        float halfTurn = turn * Mathf.Deg2Rad * 0.5f;
        float arcScale = Mathf.Abs(halfTurn) > 0.0001f ? Mathf.Sin(halfTurn) / halfTurn : 1f;
        float middle = (angle + turn * 0.5f) * Mathf.Deg2Rad;
        float end = nextAngle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(middle), Mathf.Sin(middle)) * (speed * turnTime * arcScale) +
               new Vector2(Mathf.Cos(end), Mathf.Sin(end)) * (speed * (step - turnTime));
    }

    /// <summary>飞行与离地共用同一段通行检查和位置通知。</summary>
    private bool MoveFlightStep(Vector2 direction, float speed, float deltaTime)
    {
        ChunkMgr chunks = ChunkMgr.Instance;
        if (chunks == null || !chunks.TryCreateTerrainPresenceQuery(out ChunkMgr.RuntimeTerrainPresenceQuery query))
            return false;
        return MoveFlightStep(direction, speed, deltaTime, ref query);
    }

    private bool MoveFlightStep(Vector2 direction, float speed, float deltaTime,
        ref ChunkMgr.RuntimeTerrainPresenceQuery query)
    {
        Vector2 position = body.position;
        Vector2 next = query.NormalizePosition(
            position + Vector2.ClampMagnitude(direction, speed * deltaTime));
        if (!flightNavigation.CanTraverse(position, next, ref query)) return false;
        body.position = next;
        if (direction.sqrMagnitude > 0.0001f)
        {
            Vector2 forward = direction.normalized;
            state.FlightDirectionX = forward.x;
            state.FlightDirectionY = forward.y;
        }
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

    #region 跟随贴图的独立受击盒
    private BoxCollider2D flightHitbox;
    private Rigidbody2D flightHitboxBody;
    private Vector2 flightHitboxOriginalOffset;
    private Vector2 flightHitboxOriginalSize;
    private readonly Dictionary<Sprite, Bounds> flightSpriteBounds = new();
    private readonly List<Vector2> flightShapePoints = new();

    /// <summary>受击 Trigger 使用独立运动学刚体，根部阻挡关闭或远距休眠时仍能被投射物查询。</summary>
    private void BindFlightHitbox()
    {
        if (!health.transform.IsChildOf(liftRoot))
            throw new InvalidOperationException("鸟的生命模块必须位于 BirdLift 下，随贴图一起升高。");
        flightHitbox = health.GetComponent<BoxCollider2D>();
        if (flightHitbox == null)
            throw new InvalidOperationException("鸟的生命模块缺少专用 BoxCollider2D 受击盒。");
        flightHitboxOriginalOffset = flightHitbox.offset;
        flightHitboxOriginalSize = flightHitbox.size;
        flightHitboxBody = health.GetComponent<Rigidbody2D>();
        if (flightHitboxBody == null)
            flightHitboxBody = health.gameObject.AddComponent<Rigidbody2D>();
        flightHitboxBody.bodyType = RigidbodyType2D.Kinematic;
        flightHitboxBody.gravityScale = 0f;
        flightHitboxBody.constraints = RigidbodyConstraints2D.FreezeRotation;
        flightHitboxBody.simulated = true;
        CombatPhysicsChannels.AssignDamageReceiver(health);
    }

    /// <summary>动画更新后再对齐一次，让翅膀轮廓、镜像和升降高度都进入受击几何。</summary>
    private void LateUpdate()
    {
        if (loaded) RefreshFlightHitbox();
    }

    private void RefreshFlightHitbox()
    {
        SpriteRenderer renderer = item != null ? item.Sprite : null;
        if (flightHitbox == null || flightHitboxBody == null || renderer == null || renderer.sprite == null)
            return;
        Sprite sprite = renderer.sprite;
        if (!flightSpriteBounds.TryGetValue(sprite, out Bounds bounds))
        {
            bool hasPoint = false;
            bounds = sprite.bounds;
            for (int shape = 0; shape < sprite.GetPhysicsShapeCount(); shape++)
            {
                flightShapePoints.Clear();
                sprite.GetPhysicsShape(shape, flightShapePoints);
                foreach (Vector2 point in flightShapePoints)
                {
                    if (!hasPoint) bounds = new Bounds(point, Vector3.zero);
                    else bounds.Encapsulate(point);
                    hasPoint = true;
                }
            }
            flightSpriteBounds.Add(sprite, bounds);
        }
        Bounds localBounds = default;
        Matrix4x4 spriteToHitbox = flightHitbox.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
        for (int corner = 0; corner < 4; corner++)
        {
            Vector3 point = new((corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                (corner & 2) == 0 ? bounds.min.y : bounds.max.y, 0f);
            if (renderer.flipX) point.x = -point.x;
            if (renderer.flipY) point.y = -point.y;
            point = spriteToHitbox.MultiplyPoint3x4(point);
            if (corner == 0) localBounds = new Bounds(point, Vector3.zero);
            else localBounds.Encapsulate(point);
        }
        Vector2 offset = localBounds.center;
        Vector2 size = new(Mathf.Max(0.01f, localBounds.size.x), Mathf.Max(0.01f, localBounds.size.y));
        if (flightHitbox.offset != offset) flightHitbox.offset = offset;
        if (flightHitbox.size != size) flightHitbox.size = size;
        // 直接提交受击刚体姿态，避免空中贴图已移动而物理查询仍使用旧位置。
        Vector2 position = flightHitbox.transform.position;
        float rotation = flightHitbox.transform.eulerAngles.z;
        if (flightHitboxBody.position != position) flightHitboxBody.position = position;
        if (!Mathf.Approximately(flightHitboxBody.rotation, rotation)) flightHitboxBody.rotation = rotation;
    }

    private void ReleaseFlightHitbox()
    {
        if (flightHitboxBody != null) flightHitboxBody.simulated = false;
        if (flightHitbox != null)
        {
            flightHitbox.offset = flightHitboxOriginalOffset;
            flightHitbox.size = flightHitboxOriginalSize;
        }
        flightHitbox = null;
        flightHitboxBody = null;
        flightSpriteBounds.Clear();
        flightShapePoints.Clear();
    }

    /// <summary>空中死亡先落回地面映射位置，再交给生命模块执行唯一死亡与掉落结算。</summary>
    private void HandleBirdDeath(Mod_DamageReceiver receiver)
    {
        stoppedForDeath = true;
        ResetFishHunting(releaseCaptured: true);
        mover.StopMovement();
        body.velocity = Vector2.zero;
        state.Phase = BirdFlightPhase.Ground;
        state.HasTarget = false;
        state.HasTransitionStartHeight = false;
        liftRoot.localPosition = liftOrigin;
        RefreshFlightHitbox();
        staminaDisplay?.SetVisible(false);
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
