using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通用物品投射模块：负责把带 Mod_Damage 的物品以刚体方式发射、按蓄力缩放速度与伤害，
/// 用碰撞体扫掠补足高速 Trigger 的帧间穿透，并以轻微空气阻力和虚拟抛物线高度决定落地停止。
/// </summary>
public sealed class Mod_Projectile : Module, IItemModuleDependencyBinder, ICombatDamageContextModifier, IDeathCauseSnapshotSource, IItemIndependentWorldMotionCapability2D, IRuntimeAiPersistencePolicy
{
    public const string PersistedModuleId = "Mod_Projectile";

    #region 发射来源
    private DeathCauseRecord launchDeathCause;

    public DeathCauseRecord CaptureDeathCause() => launchDeathCause?.Copy();

    public void ModifyDamageContext(ref FlatWorld.Combat.CombatDamageContext context)
    {
        if (launchDeathCause != null)
            context.WeaponItemId = GameplayCombatBridge.NarrativeItemId(launchDeathCause.WeaponItemId);
    }
    #endregion

    #region 配置

    [Min(0f), Tooltip("最低蓄力时的飞行速度。")]
    public float MinSpeed = 25f;

    [Min(0f), Tooltip("满蓄力时的飞行速度。")]
    public float MaxSpeed = 60f;

    [Min(0f), Tooltip("飞行中的线性空气阻力；只让水平速度缓慢衰减，不负责决定落地。")]
    public float FlightLinearDrag = 0.12f;

    [Range(0f, 1f), Tooltip("投射物撞到实体墙面时由 Physics2D 计算的反弹系数。")]
    public float CollisionBounciness = 0.35f;

    [Range(0.1f, 1f), Tooltip("实体碰撞盒相对伤害盒的尺寸倍率；小于 1 可保留先判伤害的接触余量。")]
    public float SolidColliderSizeMultiplier = 1f;

    [Min(0f), Tooltip("投射物被防御弹开时的视觉旋转速度。")]
    public float BounceSpinDegreesPerSecond = 900f;

    [Min(0.01f), Tooltip("虚拟抛物线使用的重力；仅用于计算箭矢离地高度与落地时机。")]
    public float VirtualGravity = 9.8f;

    [Range(0f, 1f), Tooltip("最低蓄力时的伤害倍率。")]
    public float MinDamageMultiplier = 0.35f;

    [Min(0f), Tooltip("满蓄力时的伤害倍率。")]
    public float MaxDamageMultiplier = 1f;

    [Min(0.05f), Tooltip("满蓄力且没有命中目标时的完整虚拟抛物线飞行时长，同时作为安全上限；未满蓄力会按蓄力比例缩短。")]
    public float MaxFlightSeconds = 2.5f;

    [Range(0f, 1f), Tooltip("投射结束后保留为可拾取物品的概率；箭矢默认 50%。")]
    public float RecoveryChance = 0.5f;

    [Tooltip("命中 DamageReceiver 且本次判定为可回收时，是否嵌入目标并随目标移动。")]
    public bool EmbedOnDamageReceiverWhenRecovered;

    [Range(0f, 1f), Tooltip("投射物损坏后，从其普通合成配方中掉落一份原材料的概率；箭矢默认 30%。")]
    public float BrokenSalvageChance = 0.3f;

    [Tooltip("素材自身的朝向角度；当前箭矢素材从左下指向右上，因此为 45 度。")]
    public float SpriteForwardAngleDegrees = 45f;

    [Tooltip("抛掷类在视觉和受击盒上使用真实抛物线；关闭时保持箭矢的直线飞行。")]
    public bool UseVisibleArc;
    [Range(0f, 1f), Tooltip("抛掷物只在最后这一比例的飞行阶段允许命中地面目标。")]
    public float GroundImpactFraction = 0.15f;
    [Tooltip("抛掷类飞行时每秒自转角度；零表示保持朝向。")]
    public float SpinDegreesPerSecond;

    public Ex_ModData_MemoryPackable Data = new Ex_ModData_MemoryPackable();
    public override ModuleData _Data { get => Data; set => Data = (Ex_ModData_MemoryPackable)value; }
    public override string CanonicalModuleId => PersistedModuleId;

    #endregion

    #region 运行时状态

    private Mod_Damage _damage;
    private Rigidbody2D _body;
    private BoxCollider2D _solidCollider;
    private PhysicsMaterial2D _flightMaterial;
    private CombatDamage _baseDamage;
    private float _flightRemain;
    private float _flightElapsed;
    private float _virtualLaunchVerticalSpeed;
    private float _virtualHeight;
    private Vector2 _lastFlightPosition;
    private Vector2 _lastSensorPosition;
    private Vector2 _groundFlightPosition, _arcVelocity;
    private Transform _arcVisual, _arcHitbox;
    private Vector3 _arcVisualBase, _arcHitboxBase;
    private Quaternion _arcVisualBaseRotation;
    private bool _arcVisualBaseCaptured;
    private float _flightDuration;
    private readonly RaycastHit2D[] _sweepHits = new RaycastHit2D[16];
    private readonly List<RaycastHit2D> _physicalSweepHits = new(16);
    private readonly List<Collider2D> _ignoredFlightColliders = new();
    private Vector2 _pendingImpactPosition;
    private Vector2 _pendingImpactNormal;
    private bool _hasPendingImpact;
    private bool _processingPhysicalContact;
    private bool _physicalContactResolved;
    private bool _resolvedBounceThisSweep;
    private bool _bounceSpinActive;
    private float _bounceSpinDirection = 1f;
    private float _bounceSpinReferenceSpeed;
    private bool _isFlying;
    private bool _isPreparingFlight;
    public bool RequiresIndependentWorldMotion => _isPreparingFlight || _isFlying;
    public bool PersistRuntimeAi => !_isPreparingFlight && !_isFlying && !_endingFlight;
    private bool _endingFlight;
    private FlatWorld.Spaceflight.SpaceCombatFrame spaceFrame;
    private int _lastFlightUpdateFrame = -1;
    public Vector2 SpaceFlightVelocity => _arcVelocity;
    public static readonly HashSet<Mod_Projectile> NetworkSurfaceProjectiles = new();
    public static readonly HashSet<Mod_Projectile> LocalWorldProjectiles = new();
    public string NetworkSurfaceWorldKey { get; private set; }
    public Vector2 NetworkSurfaceCanonicalOrigin { get; private set; }
    public Vector2 NetworkSurfacePresentationOrigin { get; private set; }
    public bool IsNetworkSurfaceQueryOnly { get; private set; }
    public Vector2 NetworkSurfaceLogicalPosition => PressureExplosionQueue.NormalizeInWorld(NetworkSurfaceWorldKey,
        NetworkSurfaceCanonicalOrigin + (Vector2)item.transform.position - NetworkSurfacePresentationOrigin);
    public float NetworkSurfaceVisualHeight => UseVisibleArc ? Mathf.Max(0f, _virtualHeight) : 0f;
    public float NetworkSurfaceVisualRotation => _arcVisual != null ? _arcVisual.localEulerAngles.z : 0f;
    public Vector2 NetworkSurfaceVelocity => IsNetworkSurfaceQueryOnly ? _arcVelocity : _body != null ? _body.velocity : Vector2.zero;
    private Transform _embeddedTarget;
    private bool _wasEmbedded;
    private Vector3 _embeddedLocalPosition;
    private Quaternion _embeddedLocalRotation = Quaternion.identity;
    /// <summary>飞行或仍附着目标时属于战斗实体，不能自动转换成静态掉落物。</summary>
    public bool HasActiveWorldAttachment => _isFlying || _wasEmbedded;

    #endregion

    public override ModuleTickMode TickMode => HasActiveWorldAttachment
        ? ModuleTickMode.EveryFrame : ModuleTickMode.Disabled;

    /// <summary>确保模块数据拥有稳定 ID。</summary>
    public override void Awake()
    {
        Data ??= new Ex_ModData_MemoryPackable();
        Data.ID = PersistedModuleId;
        base.Awake();
    }

    /// <summary>在 Item 完成模块注册后显式解析伤害模块依赖。</summary>
    public void BindModuleDependencies(ItemMods modules)
    {
        _damage = modules.RequireSingleModById<Mod_Damage>("Mod_Damage");
    }

    /// <summary>初始化待发射状态，并关闭伤害窗口。</summary>
    protected override void OnLoad()
    {
        if (_damage == null)
            throw new MissingComponentException($"{name} 缺少 Mod_Damage 依赖。");

        _damage.OnReceiverDamageResolved -= HandleReceiverDamageResolved;
        _damage.OnReceiverDamageResolved += HandleReceiverDamageResolved;
        _damage.OnReceiverLogicalMissed -= HandleReceiverLogicalMissed;
        _damage.OnReceiverLogicalMissed += HandleReceiverLogicalMissed;
        _damage.OnExternalDamageResolved -= HandleExternalDamageResolved;
        _damage.OnExternalDamageResolved += HandleExternalDamageResolved;
        _baseDamage = _damage.ResolveDamageValues().Scaled(1f);
        _damage.StopAttack();
        _damage.SetIdleTickSuppressed(true, this);
        RestoreFlightCollisions();
        ConfigureBodyForRest();
        _isFlying = false;
        _isPreparingFlight = false;
        _endingFlight = false;
        _lastFlightUpdateFrame = -1;
        _flightRemain = 0f;
        _flightElapsed = 0f;
        _virtualLaunchVerticalSpeed = 0f;
        _virtualHeight = 0f;
        ResetBounceSpin();
        ClearEmbeddedState();
        BindArcPresentation();
        UpdateArcPresentation(0f);
        _lastFlightPosition = item != null ? (Vector2)item.transform.position : Vector2.zero;
        _lastSensorPosition = ResolveSensorPosition();
    }

    /// <summary>投射物没有额外持久化运行态。</summary>
    protected override void OnSave()
    {
    }

    /// <summary>飞行期间补做帧间碰撞体扫掠，并按虚拟抛物线高度决定落地。</summary>
    public override void ModUpdate(float deltaTime)
    {
        if (!_isFlying && _wasEmbedded)
        {
            UpdateEmbeddedPose();
            return;
        }

        if (!_isFlying)
            return;

        // 物品本帧迁入另一个调度组时不能再推进一次。
        if (_lastFlightUpdateFrame == Time.frameCount) return;
        _lastFlightUpdateFrame = Time.frameCount;

        float step = Mathf.Min(Mathf.Max(0f, deltaTime), Mathf.Max(0f, _flightRemain));
        _flightElapsed += step;
        _flightRemain -= step;

        if (spaceFrame?.Active == true)
        {
            if (!spaceFrame.SectorLocal) RefreshSpaceProjectilePresentation();
            // 太空投射物使用权威直线位移和规范坐标扫掠，不接触本机地表物理世界。
            Vector2 next = (Vector2)item.transform.position + _arcVelocity * step;
            SetProjectilePosition(next);
            _virtualHeight = 0f;
            UpdateArcPresentation(0f);
            SweepFlightPath();
            if (_isFlying && spaceFrame.SectorLocal)
            {
                spaceFrame.DepartingShipId = null;
                if (spaceFrame.RebaseSectorIfNeeded(out Vector2 shift))
                { _lastFlightPosition += shift; _lastSensorPosition += shift; _groundFlightPosition += shift; }
            }
            if (_isFlying && _flightRemain <= 0f) FinishFlight();
            return;
        }
        if (IsNetworkSurfaceQueryOnly)
        {
            // 远端世界的箭保留地表弧线，只查询该世界船体，不借当前场景的地面物理。
            if (!UseVisibleArc) _arcVelocity *= Mathf.Exp(-Mathf.Max(0f, FlightLinearDrag) * step);
            SetProjectilePosition((Vector2)item.transform.position + _arcVelocity * step);
            _groundFlightPosition = item.transform.position;
        }

        float gravity = Mathf.Max(0.01f, VirtualGravity);
        _virtualHeight = _virtualLaunchVerticalSpeed * _flightElapsed -
                         0.5f * gravity * _flightElapsed * _flightElapsed;

        if (UseVisibleArc)
        {
            // 地面位移由刚体负责，虚拟高度只移动视觉和攻击传感器。
            _groundFlightPosition = _body.position;
            UpdateArcPresentation(Mathf.Max(0f, _virtualHeight));
            if (_arcVisual != null)
                _arcVisual.localRotation *= Quaternion.Euler(0f, 0f, SpinDegreesPerSecond * step);
            SetFlightDeliveryCapabilities();
        }

        UpdateBounceSpin(step);
        var session = FlatWorld.Spaceflight.SpaceSession.Current;
        Vector2 end = _body != null ? _body.position : (Vector2)item.transform.position;
        Vector2 start = _lastFlightPosition;
        FlatWorld.Spaceflight.ShipState exitShip = null;
        float exitFraction = 0f;
        bool exitsShip = FlatWorld.Networking.GameNetwork.HasStateAuthority && session != null &&
            session.TryFindProjectileShipExit(item, start, end, out exitShip, out exitFraction);
        if (exitsShip) SetProjectilePosition(Vector2.Lerp(start, end, exitFraction));
        SweepFlightPath();
        if (!_isFlying) return;
        if (_resolvedBounceThisSweep) exitsShip = false;
        if (exitsShip)
        {
            Vector2 localBoundary = item.transform.position;
            if (TransferProjectileOutOfShipInterior(session, exitShip, localBoundary, out string targetWorld))
            {
                FlatWorld.Spaceflight.ShipGeometry.Rotate(end.x - localBoundary.x, end.y - localBoundary.y,
                    exitShip.AngleRadians, out double dx, out double dy);
                _lastFlightPosition = item.transform.position;
                _lastSensorPosition = ResolveSensorPosition();
                SetProjectilePosition((Vector2)item.transform.position + new Vector2((float)dx, (float)dy));
                if (spaceFrame?.SectorLocal == true)
                {
                    using (GameplayWorldRegistry.UseSpaceScene(targetWorld)) SweepFlightPath();
                    if (_isFlying)
                    {
                        if (dx != 0d || dy != 0d) spaceFrame.DepartingShipId = null;
                        if (spaceFrame.RebaseSectorIfNeeded(out Vector2 shift))
                        { _lastFlightPosition += shift; _lastSensorPosition += shift; _groundFlightPosition += shift; }
                    }
                }
                else
                {
                    using (GameplayWorldRegistry.UseWorld(targetWorld)) SweepFlightPath();
                }
            }
            if (_isFlying && (_flightRemain <= 0f || spaceFrame?.Active != true && _flightElapsed > 0f && _virtualHeight <= 0f))
                FinishFlight();
            return;
        }
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);

        if ((_flightElapsed > 0f && _virtualHeight <= 0f) || _flightRemain <= 0f)
            FinishFlight();
    }

    /// <summary>解除伤害事件，防止对象池复用后重复订阅。</summary>
    protected override void OnUnload()
    {
        spaceFrame?.Finish();
        NetworkSurfaceProjectiles.Remove(this); LocalWorldProjectiles.Remove(this);
        NetworkSurfaceWorldKey = null; IsNetworkSurfaceQueryOnly = false;
        launchDeathCause = null;
        _damage?.SetDeliveryCapabilities(FlatWorld.Combat.CombatDeliveryCapabilities.None);
        if (_damage != null)
        {
            _damage.OnReceiverDamageResolved -= HandleReceiverDamageResolved;
            _damage.OnReceiverLogicalMissed -= HandleReceiverLogicalMissed;
            _damage.OnExternalDamageResolved -= HandleExternalDamageResolved;
            _damage.SetExplicitProjectileSweep(false);
            _damage.SetIdleTickSuppressed(false, this);
        }
        _isFlying = false;
        _isPreparingFlight = false;
        _endingFlight = false;
        _flightRemain = 0f;
        _flightElapsed = 0f;
        _virtualHeight = 0f;
        ResetBounceSpin();
        ClearEmbeddedState();
        UpdateArcPresentation(0f);
        RestoreFlightCollisions();
        if (_flightMaterial != null)
        {
            Object.Destroy(_flightMaterial);
            _flightMaterial = null;
        }
    }

    #region 发射与停止

    /// <summary>只保存轨迹参数，让弹药预览与真实投射共用计算而无需生成临时物品。</summary>
    public readonly struct TrajectorySettings
    {
        public readonly float MinSpeed, MaxSpeed, MaxFlightSeconds, VirtualGravity;
        public readonly bool UseVisibleArc;

        public TrajectorySettings(float minSpeed, float maxSpeed, float maxFlightSeconds,
            float virtualGravity, bool useVisibleArc)
        {
            MinSpeed = minSpeed;
            MaxSpeed = maxSpeed;
            MaxFlightSeconds = maxFlightSeconds;
            VirtualGravity = virtualGravity;
            UseVisibleArc = useVisibleArc;
        }

        public float ResolveLaunchSpeed(float charge01, float sourceSpeedMultiplier = 1f)
        {
            float speed = Mathf.Lerp(Mathf.Max(0f, MinSpeed), Mathf.Max(MinSpeed, MaxSpeed), Mathf.Clamp01(charge01));
            return speed * Mathf.Max(0f, sourceSpeedMultiplier);
        }

        public float ResolveFlightDuration(float charge01)
        {
            return Mathf.Max(0.05f, Mathf.Max(0f, MaxFlightSeconds) * Mathf.Clamp01(charge01));
        }

        public Vector2 EvaluateVisibleTrajectoryPoint(Vector2 launchPosition, Vector2 direction,
            float charge01, float normalizedTime, float sourceSpeedMultiplier = 1f)
        {
            Vector2 normalizedDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
            float duration = ResolveFlightDuration(charge01);
            float elapsed = duration * Mathf.Clamp01(normalizedTime);
            Vector2 position = launchPosition + normalizedDirection * ResolveLaunchSpeed(charge01, sourceSpeedMultiplier) * elapsed;
            if (UseVisibleArc)
            {
                float gravity = Mathf.Max(0.01f, VirtualGravity);
                float height = 0.5f * gravity * duration * elapsed - 0.5f * gravity * elapsed * elapsed;
                position.y += Mathf.Max(0f, height);
            }
            return position;
        }
    }

    public TrajectorySettings FlightTrajectory =>
        new TrajectorySettings(MinSpeed, MaxSpeed, MaxFlightSeconds, VirtualGravity, UseVisibleArc);

    /// <summary>按当前投射物配置计算本次发射速度，预览与真实发射共用同一公式。</summary>
    public float ResolveLaunchSpeed(float charge01, float sourceSpeedMultiplier = 1f)
    {
        return FlightTrajectory.ResolveLaunchSpeed(charge01, sourceSpeedMultiplier);
    }

    /// <summary>按当前蓄力计算完整飞行时长，保持轻点也有最短有效飞行段。</summary>
    public float ResolveFlightDuration(float charge01)
    {
        return FlightTrajectory.ResolveFlightDuration(charge01);
    }

    /// <summary>计算无碰撞情况下的可见轨迹点；可见抛物线始终使用向下开的二次曲线。</summary>
    public Vector2 EvaluateVisibleTrajectoryPoint(
        Vector2 launchPosition,
        Vector2 direction,
        float charge01,
        float normalizedTime,
        float sourceSpeedMultiplier = 1f)
    {
        return FlightTrajectory.EvaluateVisibleTrajectoryPoint(
            launchPosition, direction, charge01, normalizedTime, sourceSpeedMultiplier);
    }

    /// <summary>按原有伤害倍率发射；无额外速度修饰时保持现有调用入口。</summary>
    public void Launch(Item shooter, Vector2 direction, float charge01, float sourceDamageMultiplier = 1f)
    {
        Launch(shooter, direction, charge01, sourceDamageMultiplier, 1f);
    }

    /// <summary>按射手、蓄力和武器模块产出的速度与伤害倍率开始一次飞行。</summary>
    public void Launch(Item shooter, Vector2 direction, float charge01, float sourceDamageMultiplier, float sourceSpeedMultiplier, Item sourceWeapon = null)
    {
        if (item == null || _damage == null || direction.sqrMagnitude < 0.0001f)
            throw new System.InvalidOperationException($"{name} 无法发射：投射物尚未正确初始化或方向无效。");
        if (!WorldPhysicsService2D.CanItemUseIndependentMotion(item)) return;

        _isPreparingFlight = true;
        try
        {
            item.NotifyRuntimeStructureChanged();
            float normalizedCharge = Mathf.Clamp01(charge01);
            float speed = ResolveLaunchSpeed(normalizedCharge, sourceSpeedMultiplier);
            float damageMultiplier = Mathf.Lerp(
                Mathf.Max(0f, MinDamageMultiplier),
                Mathf.Max(0f, MaxDamageMultiplier),
                normalizedCharge);
            damageMultiplier *= Mathf.Max(0f, sourceDamageMultiplier);
            Vector2 normalizedDirection = direction.normalized;

            item.Owner = shooter;
            _damage.LaunchedWeaponDurabilityOwner = sourceWeapon;
            _damage.LaunchedWeaponDurabilityData = sourceWeapon?.itemData;
            _damage.LaunchedWeaponInventoryOwner = shooter;
            // 发射时记住真实射手和武器，之后切换手持物也不会改变这支箭的来源。
            launchDeathCause = DeathCauseRecord.CaptureAttack(sourceWeapon != null ? sourceWeapon : item, shooter);
            item.SetInHand(false);
            item.itemData.Stack.Amount = 1f;
            item.itemData.Stack.CanBePickedUp = false;
            ClearEmbeddedState();

            EnsureBody();
            EnsureSolidCollider();
            if (!WorldPhysicsService2D.ConfigureItemBodyForIndependentMotion(item, _body)) return;
            // 可见抛物线必须保持匀速地面位移，否则向下投掷时阻力会把曲线扭成非二次曲线。
            WorldPhysicsService2D.ApplyLegacyItemLinearDamping(item, _body, UseVisibleArc ? 0f : FlightLinearDrag);
            _body.interpolation = RigidbodyInterpolation2D.Interpolate;
            _arcVelocity = normalizedDirection * speed;
            _groundFlightPosition = _body.position;
            _body.velocity = FlatWorld.Networking.GameNetwork.HasStateAuthority ? _arcVelocity : Vector2.zero;
            _solidCollider.enabled = true;
            IgnoreShooterCollisions(shooter);
            UpdateArcPresentation(0f);
            if (_arcVisual != null) _arcVisual.localRotation = _arcVisualBaseRotation;

            float angle = Mathf.Atan2(normalizedDirection.y, normalizedDirection.x) * Mathf.Rad2Deg;
            item.transform.rotation = Quaternion.Euler(0f, 0f, angle - SpriteForwardAngleDegrees);

            _damage.SetDamageValues(_baseDamage.Scaled(damageMultiplier));
            _damage.MaxAttackTargets = 1;

            // 飞行时长与蓄力保持同一比例：轻点只飞很短一段，满蓄力才使用完整持续时间。
            float flightSeconds = ResolveFlightDuration(normalizedCharge);
            _flightDuration = flightSeconds;
            float virtualGravity = Mathf.Max(0.01f, VirtualGravity);
            _flightRemain = flightSeconds;
            _flightElapsed = 0f;
            _virtualLaunchVerticalSpeed = 0.5f * virtualGravity * flightSeconds;
            _virtualHeight = 0f;
            _lastFlightPosition = _body.position;
            _lastSensorPosition = ResolveSensorPosition();
            _endingFlight = false;
            ResetBounceSpin();
            _isFlying = true;
            InvalidateTickSchedule();

            _damage.SetExplicitProjectileSweep(true);
            bool localWorld = GameplayWorldRegistry.UsesLocalCoordinates(GameplayWorldRegistry.GetItemWorldKey(item));
            if (localWorld) LocalWorldProjectiles.Add(this);
            if (!localWorld && FlatWorld.Spaceflight.SpaceSession.TryGetCombatFrame(shooter, out FlatWorld.Spaceflight.SpaceSectorPosition origin, out Vector2 presentation))
            {
                spaceFrame = item.GetComponent<FlatWorld.Spaceflight.SpaceCombatFrame>() ?? item.gameObject.AddComponent<FlatWorld.Spaceflight.SpaceCombatFrame>();
                spaceFrame.Initialize(item, origin, presentation);
                WorldPhysicsService2D.SetItemBodySimulationEnabled(_body, false);
                if (_solidCollider != null) _solidCollider.enabled = false;
            }
            else if (!localWorld && FlatWorld.Networking.GameNetwork.IsOnline && FlatWorld.Networking.GameNetwork.HasStateAuthority &&
                FlatWorld.Spaceflight.SpaceSession.TryGetCombatWorldKey(shooter, out string surfaceWorld))
            {
                NetworkSurfaceWorldKey = surfaceWorld;
                NetworkSurfacePresentationOrigin = shooter.transform.position;
                Player sourcePlayer = FlatWorld.Spaceflight.SpaceSession.CombatPlayer(shooter);
                NetworkSurfaceCanonicalOrigin = sourcePlayer != null && FlatWorld.Spaceflight.SpaceSession.Current != null && FlatWorld.Spaceflight.SpaceSession.Current.TryGetNetworkObserver(sourcePlayer.ProfileName, out var observer)
                    ? observer.Position.LocalMeters.ToVector2() : WorldLocalPresentation.ToLogical(shooter.transform.position);
                IsNetworkSurfaceQueryOnly = surfaceWorld != UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (IsNetworkSurfaceQueryOnly)
                {
                    WorldPhysicsService2D.SetItemBodySimulationEnabled(_body, false); _solidCollider.enabled = false;
                    foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                }
                NetworkSurfaceProjectiles.Add(this);
            }
            SetFlightDeliveryCapabilities();

            // 先进入飞行态再开伤害窗，确保出生点附近的有效命中也能立即结束箭矢。
            _damage.StartAttack();
        }
        finally
        {
            _isPreparingFlight = false;
            if (!_isFlying && !_endingFlight && item != null && !item.DestructionHandled)
            {
                if (_body != null) WorldPhysicsService2D.ConfigureItemBodyForRestingPose(item, _body);
                item.NotifyRuntimeStructureChanged();
            }
        }
    }

    /// <summary>实体命中时把刚体放到 Physics2D 检出的接触点。</summary>
    private void SetProjectilePosition(Vector2 position)
    {
        _body.position = position;
        item.transform.position = new Vector3(position.x, position.y, item.transform.position.z);
    }

    // 船内刚体完成扫掠后移交真实外部世界，不在同一帧重复推进。
    private bool TransferProjectileOutOfShipInterior(FlatWorld.Spaceflight.SpaceSession session,
        FlatWorld.Spaceflight.ShipState ship, Vector2 local, out string targetWorld)
    {
        targetWorld = null;
        if (ship == null || _body == null) return false;
        FlatWorld.Spaceflight.ShipGeometry.LocalToWorld(ship, local.x, local.y, out double x, out double y);
        FlatWorld.Spaceflight.ShipGeometry.PointVelocity(ship, x, y, out double vx, out double vy);
        Vector2 currentVelocity = _body.velocity;
        FlatWorld.Spaceflight.ShipGeometry.Rotate(currentVelocity.x, currentVelocity.y, ship.AngleRadians, out double wx, out double wy);
        Vector2 velocity = new((float)(vx + wx), (float)(vy + wy));
        float rotation = item.transform.eulerAngles.z + (float)(ship.AngleRadians * Mathf.Rad2Deg);
        var flight = session.GetFlight(ship.ShipId);
        if (flight?.Phase == FlatWorld.Spaceflight.ShipFlightPhase.Cruising)
        {
            var position = FlatWorld.Spaceflight.SpaceSectorCoordinates.Normalize(ship.Sector,
                new FlatWorld.Spaceflight.SpaceVector2(x, y));
            spaceFrame = item.GetComponent<FlatWorld.Spaceflight.SpaceCombatFrame>() ?? item.gameObject.AddComponent<FlatWorld.Spaceflight.SpaceCombatFrame>();
            if (!spaceFrame.TryEnterSectorLocal(item, position, ship.ShipId)) return false;
            targetWorld = FlatWorld.Spaceflight.SpaceSectorCoordinates.SceneKey(position.Sector);
            SetProjectilePosition(position.LocalMeters.ToVector2());
            item.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
            _body.rotation = rotation;
            _body.velocity = velocity;
            _arcVelocity = velocity;
            WorldPhysicsService2D.SetItemBodySimulationEnabled(_body, false);
            if (_solidCollider != null) _solidCollider.enabled = false;
        }
        else
        {
            targetWorld = flight?.SurfaceWorldKey ?? ship.WorldAddress;
            if (string.IsNullOrWhiteSpace(targetWorld) || SpaceMgr.IsSpaceScene(targetWorld)) return false;
            using var scope = GameplayWorldRegistry.UseWorld(targetWorld);
            GameplayWorldRegistry.AssignItemToWorld(item, targetWorld);
            if (GameplayWorldRegistry.GetItemWorldKey(item) != targetWorld) return false;
            Vector2 point = WorldLocalPresentation.ProjectPosition(new Vector2((float)x, (float)y));
            point.y += (float)FlatWorld.Spaceflight.ShipSurfaceState.VisualRise(flight);
            SetProjectilePosition(point);
            item.transform.rotation = Quaternion.Euler(0f, 0f, rotation);
            _body.rotation = rotation;
            _body.velocity = velocity;
            _arcVelocity = velocity;
            if (FlatWorld.Networking.GameNetwork.IsOnline)
            {
                NetworkSurfaceWorldKey = targetWorld;
                NetworkSurfacePresentationOrigin = point;
                NetworkSurfaceCanonicalOrigin = WorldLocalPresentation.ToLogical(point);
                NetworkSurfaceProjectiles.Add(this);
            }
        }
        LocalWorldProjectiles.Remove(this);
        _groundFlightPosition = item.transform.position;
        SetFlightDeliveryCapabilities();
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        return true;
    }

    private void RefreshSpaceProjectilePresentation()
    {
        var session = FlatWorld.Spaceflight.SpaceSession.Current;
        Player local = ItemMgr.Instance?.User_Player;
        bool visible = session != null && local != null && session.TryGetNetworkObserver(local.ProfileName, out var observer) && observer.InSpace &&
            FlatWorld.Spaceflight.SpaceSession.TryGetCombatPosition(item, item.transform.position, out var point) &&
            FlatWorld.Spaceflight.SpaceSectorCoordinates.Difference(point, observer.Position).Magnitude <= 8192d;
        foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true)) renderer.enabled = visible;
        if (!visible || !FlatWorld.Spaceflight.SpaceSession.TryGetCombatPosition(item, item.transform.position, out var canonical)) return;
        Vector2 shift = session.ProjectPosition(canonical) - (Vector2)item.transform.position;
        spaceFrame.PresentationOrigin += shift;
        _lastFlightPosition += shift; _lastSensorPosition += shift;
        SetProjectilePosition((Vector2)item.transform.position + shift);
    }

    /// <summary>高空段与落地段共享同一窗口，切换资格不能清掉已经命中的目标。</summary>
    private void SetFlightDeliveryCapabilities()
    {
        var capabilities = FlatWorld.Combat.CombatDeliveryCapabilities.Projectile |
            FlatWorld.Combat.CombatDeliveryCapabilities.AirborneTargets;
        bool airborneOnly = spaceFrame?.Active != true && UseVisibleArc &&
            _flightElapsed < _flightDuration * (1f - Mathf.Clamp01(GroundImpactFraction));
        if (airborneOnly)
            capabilities |= FlatWorld.Combat.CombatDeliveryCapabilities.AirborneOnly;
        _damage.SetDeliveryCapabilities(capabilities);

        // 高空段的地面投影不能推动地面实体；落地段重新启用时恢复本次飞行的碰撞排除。
        if (spaceFrame?.Active == true || IsNetworkSurfaceQueryOnly) { if (_solidCollider != null) _solidCollider.enabled = false; return; }
        if (_solidCollider != null && _solidCollider.enabled == airborneOnly)
        {
            _solidCollider.enabled = !airborneOnly;
            if (_solidCollider.enabled)
                for (int i = 0; i < _ignoredFlightColliders.Count; i++)
                    if (_ignoredFlightColliders[i] != null)
                        Physics2D.IgnoreCollision(_solidCollider, _ignoredFlightColliders[i], true);
        }
    }

    /// <summary>在刚体解算前检测即将发生的实体接触，防止目标先被推离伤害盒。</summary>
    public void PreparePhysicsStep(float deltaTime)
    {
        if (!_isFlying || _endingFlight || !Enabled ||
            !FlatWorld.Networking.GameNetwork.HasStateAuthority ||
            _body == null || !_body.simulated || _solidCollider == null || !_solidCollider.enabled ||
            _damage == null || _damage.DamageCollider == null || !_damage.DamageCollider.enabled)
            return;

        Vector2 displacement = _body.velocity * Mathf.Min(Mathf.Max(0f, deltaTime), Mathf.Max(0f, _flightRemain));
        float distance = displacement.magnitude;
        if (distance <= 0.0001f)
            return;

        Vector2 origin = (Vector2)_solidCollider.transform.TransformPoint(_solidCollider.offset) +
            _body.position - (Vector2)item.transform.position;
        Vector3 scale = _solidCollider.transform.lossyScale;
        Vector2 size = new Vector2(_solidCollider.size.x * Mathf.Abs(scale.x),
            _solidCollider.size.y * Mathf.Abs(scale.y));
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(Physics2D.GetLayerCollisionMask(_solidCollider.gameObject.layer));
        _physicalSweepHits.Clear();
        GameplayPhysics2D.BoxCast(origin, size, _solidCollider.transform.eulerAngles.z,
            displacement / distance, filter, _physicalSweepHits, distance);
        _physicalSweepHits.Sort((left, right) => left.distance.CompareTo(right.distance));

        for (int i = 0; i < _physicalSweepHits.Count && _isFlying; i++)
        {
            RaycastHit2D hit = _physicalSweepHits[i];
            Collider2D other = hit.collider;
            if (other == null || other.attachedRigidbody == _body ||
                _ignoredFlightColliders.Contains(other) || Physics2D.GetIgnoreCollision(_solidCollider, other))
                continue;

            Mod_DamageReceiver receiver = GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(other);
            Collider2D receiverCollider = ResolveDamageReceiverCollider(receiver);
            Vector2 impactPosition = _body.position + displacement * Mathf.Clamp01(hit.distance / distance);
            if (receiverCollider != null)
            {
                ResolvePhysicalReceiverHit(receiverCollider, impactPosition, hit.point, hit.normal, false);
            }
            else
            {
                // 已绑定目标直接转交接触点；其它障碍的后备扫掠止于最近阻挡的接触余量。
                float contactDistance = Mathf.Min(distance, hit.distance + Physics2D.defaultContactOffset);
                ResolvePhysicalDataHit(other, displacement / distance * contactDistance,
                    impactPosition, hit.point, hit.normal, false);
            }

            // 最近的实体阻挡之后不再预判，墙体等无接收器目标仍交给原有物理碰撞处理。
            return;
        }
    }

    /// <summary>用伤害盒扫过上一帧到当前帧的完整路径，补足高速 Trigger 可能漏掉的目标。</summary>
    private void SweepFlightPath()
    {
        if (spaceFrame?.Active == true || IsNetworkSurfaceQueryOnly)
        {
            Vector2 center = ResolveSensorPosition();
            _damage.QueryProjectileSweep(center - _lastSensorPosition);
            _lastSensorPosition = ResolveSensorPosition();
            _lastFlightPosition = item.transform.position;
            return;
        }
        _resolvedBounceThisSweep = false;
        Vector2 currentPosition = _body != null ? _body.position : (Vector2)item.transform.position;
        if (!(_damage.DamageCollider is BoxCollider2D damageBox) || !damageBox.enabled)
        {
            _lastFlightPosition = currentPosition;
            _lastSensorPosition = ResolveSensorPosition();
            return;
        }

        Vector2 rootDisplacement = WorldTopologyRuntime.ShortestDelta(_lastFlightPosition, currentPosition);
        Vector2 currentColliderCenter = ResolveSensorPosition();
        Vector2 displacement = WorldTopologyRuntime.ShortestDelta(_lastSensorPosition, currentColliderCenter);
        float distance = displacement.magnitude;
        if (distance <= 0.0001f)
        {
            _damage.QueryProjectileSweep(Vector2.zero);
            _lastFlightPosition = currentPosition;
            _lastSensorPosition = currentColliderCenter;
            return;
        }

        Vector2 direction = displacement / distance;
        Vector2 castOrigin = currentColliderCenter - displacement;
        Vector3 lossyScale = damageBox.transform.lossyScale;
        Vector2 castSize = new Vector2(
            damageBox.size.x * Mathf.Abs(lossyScale.x),
            damageBox.size.y * Mathf.Abs(lossyScale.y));
        float castAngle = damageBox.transform.eulerAngles.z;

        int hitCount = GameplayPhysics2D.BoxCastNonAlloc(
            castOrigin,
            castSize,
            castAngle,
            direction,
            _sweepHits,
            distance,
            CombatPhysicsChannels.DamageReceiverMask);
        SortSweepHitsByDistance(hitCount);

        // 外部后端只扫到最近的有效 GO 目标为止；预约成功就占用同一个 MaxAttackTargets 名额。
        float maximumFraction = 1f;
        for (int i = 0; i < hitCount; i++)
            if (_damage.CanHitColliderTarget(_sweepHits[i].collider))
            {
                maximumFraction = Mathf.Clamp01(_sweepHits[i].distance / distance);
                break;
            }
        _damage.QueryProjectileSweep(displacement, maximumFraction);
        if (_resolvedBounceThisSweep)
        {
            _lastFlightPosition = _body.position;
            _lastSensorPosition = ResolveSensorPosition();
            ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
            return;
        }

        for (int i = 0; i < hitCount && _isFlying; i++)
        {
            Collider2D hitCollider = _sweepHits[i].collider;
            if (hitCollider == null)
                continue;

            float impactFraction = Mathf.Clamp01(_sweepHits[i].distance / distance);
            Vector2 impactPosition = _lastFlightPosition + rootDisplacement * impactFraction;
            _pendingImpactPosition = impactPosition;
            _pendingImpactNormal = _sweepHits[i].normal;
            _hasPendingImpact = true;
            try { _damage.ProcessExplicitColliderHit(hitCollider, castOrigin, _sweepHits[i].point); }
            finally { _hasPendingImpact = false; }
            if (_resolvedBounceThisSweep)
            {
                _lastFlightPosition = _body.position;
                _lastSensorPosition = ResolveSensorPosition();
                ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
                return;
            }
            if (!_isFlying)
            {
                _lastFlightPosition = impactPosition;
                _lastSensorPosition = castOrigin + displacement * impactFraction;
                ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
                return;
            }
        }

        _lastFlightPosition = currentPosition;
        _lastSensorPosition = currentColliderCenter;
    }

    /// <summary>伤害传感器的位置包含虚拟抛物线高度，扫掠时不能只用刚体地面位移。</summary>
    private Vector2 ResolveSensorPosition()
    {
        return _damage?.DamageCollider is BoxCollider2D box
            ? box.transform.TransformPoint(box.offset)
            : _body != null ? _body.position : (Vector2)item.transform.position;
    }

    /// <summary>NonAlloc Cast 不保证顺序；按距离排序，确保先处理路径上最近的目标。</summary>
    private void SortSweepHitsByDistance(int count)
    {
        for (int i = 1; i < count; i++)
        {
            RaycastHit2D value = _sweepHits[i];
            int j = i - 1;
            while (j >= 0 && _sweepHits[j].distance > value.distance)
            {
                _sweepHits[j + 1] = _sweepHits[j];
                j--;
            }

            _sweepHits[j + 1] = value;
        }
    }

    /// <summary>有实际伤害时走正常命中流程；完全被防御的 0 伤害命中才弹开。</summary>
    private void HandleReceiverDamageResolved(Mod_DamageReceiver receiver, float resolvedDamage)
    {
        if (!_isFlying || resolvedDamage < 0f)
            return;

        if (_processingPhysicalContact)
            _physicalContactResolved = true;

        if (resolvedDamage > 0f)
        {
            if (_hasPendingImpact)
                SetProjectilePosition(_pendingImpactPosition);
            FinishFlight(receiver);
            return;
        }

        if (_hasPendingImpact && !_processingPhysicalContact)
            SetProjectilePosition(_pendingImpactPosition);
        BounceFromBlockedHit(_pendingImpactNormal, _processingPhysicalContact);
    }

    /// <summary>逻辑闪避仍然不扣血，同时排除该目标的实体接触，不能把未命中表现成推走目标。</summary>
    private void HandleReceiverLogicalMissed(Mod_DamageReceiver receiver, float chance)
    {
        if (_isFlying && receiver != null)
            IgnoreSolidCollisions(receiver.item);
    }

    /// <summary>ECS 结算回执同样结束投射物，不能因没有 DamageReceiver 组件而穿过狼继续飞。</summary>
    private void HandleExternalDamageResolved(FlatWorld.Combat.CombatDamageContext context, float resolvedDamage)
    {
        if (!_isFlying || resolvedDamage < 0f) return;
        _physicalContactResolved = true;
        if (_hasPendingImpact)
        {
            SetProjectilePosition(_pendingImpactPosition);
        }
        else
        {
            Vector2 offset = _damage.DamageCollider is BoxCollider2D box
                ? (Vector2)box.transform.TransformPoint(box.offset) - (Vector2)item.transform.position : Vector2.zero;
            Vector2 point = (Vector2)context.HitPoint - offset;
            SetProjectilePosition(spaceFrame?.Active == true || IsNetworkSurfaceQueryOnly ? point : WorldTopologyRuntime.NormalizePosition(point));
        }
        if (resolvedDamage == 0f)
        {
            BounceFromBlockedHit(_hasPendingImpact ? _pendingImpactNormal : Vector2.zero, _processingPhysicalContact);
            return;
        }
        FinishFlight();
    }

    /// <summary>实体碰撞先交给伤害系统裁决，避免能造成伤害的箭先被普通刚体碰撞弹走。</summary>
    public void HandlePhysicalContact(Collision2D collision)
    {
        if (!_isFlying || collision == null || _solidCollider == null)
            return;

        Collider2D otherCollider = collision.collider == _solidCollider
            ? collision.otherCollider
            : collision.collider;
        Vector2 contactPoint = _body != null ? _body.position : (Vector2)item.transform.position;
        Vector2 contactNormal = Vector2.zero;
        if (collision.contactCount > 0)
        {
            ContactPoint2D contact = collision.GetContact(0);
            contactPoint = contact.point;
            contactNormal = contact.normal;
            if (Vector2.Dot(_body != null ? _body.velocity : Vector2.zero, contactNormal) > 0f)
                contactNormal = -contactNormal;
        }

        Mod_DamageReceiver receiver = GameplayPhysics2D.ResolveComponent<Mod_DamageReceiver>(otherCollider);
        Collider2D receiverCollider = ResolveDamageReceiverCollider(receiver);
        if (receiverCollider == null)
        {
            // ECS 障碍没有接收器组件，由物理目标映射转接伤害，不能直接当墙反弹。
            if (!ResolvePhysicalDataHit(otherCollider, -contactNormal * Physics2D.defaultContactOffset,
                    _body != null ? _body.position : (Vector2)item.transform.position,
                    contactPoint, contactNormal, true))
                StartBounceSpin(contactNormal);
            return;
        }

        ResolvePhysicalReceiverHit(receiverCollider,
            _body != null ? _body.position : (Vector2)item.transform.position,
            contactPoint, contactNormal, true);
    }

    /// <summary>实体预判与实际接触都转交目标的专用伤害接收器，不直接扣血或重掷命中概率。</summary>
    private void ResolvePhysicalReceiverHit(Collider2D receiverCollider, Vector2 impactPosition,
        Vector2 contactPoint, Vector2 contactNormal, bool physicsAlreadyResolved)
    {
        _pendingImpactPosition = impactPosition;
        _pendingImpactNormal = contactNormal;
        _hasPendingImpact = true;
        _processingPhysicalContact = physicsAlreadyResolved;
        _physicalContactResolved = false;
        try
        {
            _damage.ProcessExplicitColliderHit(
                receiverCollider,
                _body != null ? _body.position : (Vector2)item.transform.position,
                contactPoint);
        }
        finally
        {
            _processingPhysicalContact = false;
            _hasPendingImpact = false;
        }

        // 无法进入伤害结算的普通物理反弹也给出旋转反馈；有效伤害已在回调里结束飞行。
        if (physicsAlreadyResolved && _isFlying && !_physicalContactResolved)
            StartBounceSpin(contactNormal);
    }

    /// <summary>无接收器组件的物理接触交回数据后端，保留统一身份、伤害规则和窗口去重。</summary>
    private bool ResolvePhysicalDataHit(Collider2D collider, Vector2 displacement, Vector2 impactPosition,
        Vector2 contactPoint, Vector2 contactNormal, bool physicsAlreadyResolved)
    {
        if (_damage == null || _body == null)
            return false;

        _pendingImpactPosition = impactPosition;
        _pendingImpactNormal = contactNormal;
        _hasPendingImpact = true;
        _processingPhysicalContact = physicsAlreadyResolved;
        _physicalContactResolved = false;
        try
        {
            if (!_damage.ProcessPhysicsDataColliderHit(collider, _body.position, contactPoint))
            {
                // 尚未登记直接身份的其它数据障碍保留现行真实伤害盒查询。
                Vector2 positionOffset = _body.position - (Vector2)item.transform.position;
                _damage.QueryProjectilePhysicsSweep(displacement, positionOffset);
            }
            return _physicalContactResolved || !_isFlying;
        }
        finally
        {
            _processingPhysicalContact = false;
            _hasPendingImpact = false;
        }
    }

    private static Collider2D ResolveDamageReceiverCollider(Mod_DamageReceiver receiver)
    {
        if (receiver == null)
            return null;

        Collider2D[] colliders = receiver.GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
            if (CombatPhysicsChannels.IsDamageReceiverCollider(colliders[i]))
                return colliders[i];
        return null;
    }

    /// <summary>0 伤害代表攻击被防御完全抵消，箭矢沿接触法线弹回并播放短暂翻滚。</summary>
    private void BounceFromBlockedHit(Vector2 impactNormal, bool physicsAlreadyResolved)
    {
        if (_body == null)
            return;

        if (physicsAlreadyResolved)
        {
            _arcVelocity = _body.velocity;
            StartBounceSpin(impactNormal);
            return;
        }

        Vector2 velocity = _body.velocity;
        if (velocity.sqrMagnitude <= 0.0001f)
            velocity = _arcVelocity;
        if (velocity.sqrMagnitude <= 0.0001f)
            return;

        Vector2 normal = impactNormal.sqrMagnitude > 0.0001f
            ? impactNormal.normalized
            : -velocity.normalized;
        if (Vector2.Dot(velocity, normal) < 0f)
            velocity = Vector2.Reflect(velocity, normal);

        velocity *= Mathf.Clamp01(CollisionBounciness);

        _body.velocity = velocity;
        _arcVelocity = velocity;
        _resolvedBounceThisSweep = true;
        StartBounceSpin(normal);
    }

    private void StartBounceSpin(Vector2 impactNormal)
    {
        if (BounceSpinDegreesPerSecond <= 0f)
            return;

        Vector2 velocity = _body != null ? _body.velocity : Vector2.zero;
        float speed = velocity.magnitude;
        if (speed <= 0.01f)
            return;

        float cross = velocity.x * impactNormal.y - velocity.y * impactNormal.x;
        _bounceSpinDirection = Mathf.Abs(cross) > 0.001f
            ? Mathf.Sign(cross)
            : (velocity.x >= 0f ? -1f : 1f);
        _bounceSpinReferenceSpeed = Mathf.Max(_bounceSpinReferenceSpeed, speed);
        _bounceSpinActive = true;
    }

    private void UpdateBounceSpin(float deltaTime)
    {
        if (!_bounceSpinActive || _arcVisual == null || deltaTime <= 0f)
            return;

        float speed = _body != null ? _body.velocity.magnitude : 0f;
        if (speed <= 0.01f || _bounceSpinReferenceSpeed <= 0.01f)
        {
            _bounceSpinActive = false;
            return;
        }

        // 弹飞后的旋转速度跟随线速度衰减，飞行停下时同步停转。
        float speedRatio = Mathf.Clamp01(speed / _bounceSpinReferenceSpeed);
        _arcVisual.localRotation *= Quaternion.Euler(
            0f, 0f, BounceSpinDegreesPerSecond * speedRatio * _bounceSpinDirection * deltaTime);
    }

    private void ResetBounceSpin()
    {
        _bounceSpinActive = false;
        _bounceSpinDirection = 1f;
        _bounceSpinReferenceSpeed = 0f;
        _pendingImpactNormal = Vector2.zero;
        _processingPhysicalContact = false;
        _physicalContactResolved = false;
        _resolvedBounceThisSweep = false;
    }

    /// <summary>停止飞行，并按 RecoveryChance 决定留下可拾取物还是销毁。</summary>
    private void FinishFlight(Mod_DamageReceiver hitReceiver = null)
    {
        if (!_isFlying || _endingFlight)
            return;

        _endingFlight = true;
        _isFlying = false;
        InvalidateTickSchedule();
        NetworkSurfaceProjectiles.Remove(this);
        LocalWorldProjectiles.Remove(this);
        float remainingHeight = Mathf.Max(0f, _virtualHeight);
        _damage.StopAttack();
        _damage.SetExplicitProjectileSweep(false);
        _damage.SetDamageValues(_baseDamage.Scaled(1f));
        _damage.SetDeliveryCapabilities(FlatWorld.Combat.CombatDeliveryCapabilities.None);
        RestoreFlightCollisions();

        if (_body != null)
        {
            WorldPhysicsService2D.ConfigureItemBodyForRestingPose(item, _body);
        }
        if (_solidCollider != null) _solidCollider.enabled = false;

        _flightRemain = 0f;
        _virtualHeight = 0f;
        ResetBounceSpin();
        UpdateArcPresentation(0f);

        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        bool recover = Random.value < Mathf.Clamp01(RecoveryChance);
        if (IsNetworkSurfaceQueryOnly)
        {
            if (recover) FlatWorld.Spaceflight.SpaceSession.Current?.RecoverNetworkSurfaceProjectile(item, NetworkSurfaceWorldKey, NetworkSurfaceLogicalPosition);
            ItemMgr.Instance?.DespawnItem(item, saveData: false);
            return;
        }
        if (spaceFrame?.Active == true)
        {
            if (recover && FlatWorld.Spaceflight.SpaceSession.TryGetCombatPosition(item, item.transform.position, out FlatWorld.Spaceflight.SpaceSectorPosition point))
                FlatWorld.Spaceflight.SpaceSession.Current?.RecoverNetworkProjectile(item, point, _arcVelocity);
            spaceFrame.Finish();
            ItemMgr.Instance?.DespawnItem(item, saveData: false);
            return;
        }
        if (!recover)
        {
            TryDropBrokenSalvage();
            ItemMgr.Instance?.DespawnItem(item, saveData: false);
            return;
        }

        item.Owner = null;
        item.itemData.Stack.Amount = 1f;
        item.itemData.Stack.CanBePickedUp = true;

        if (UseVisibleArc && remainingHeight > 0.05f)
        {
            DroppedItemService.Spawn(item.itemData, item.transform.position + Vector3.up * remainingHeight, _groundFlightPosition,
                0.25f, rotation: item.transform.eulerAngles.z, bezierOffset: 0f, arcHeight: 0f, rotationSpeed: SpinDegreesPerSecond);
            ItemMgr.Instance.DespawnItem(item, saveData: false);
            return;
        }

        if (EmbedOnDamageReceiverWhenRecovered && hitReceiver != null)
            EmbedInReceiver(hitReceiver);
        else
            WorldItemWaterSystem.ScheduleSpawnCheck(item);

        _endingFlight = false;
        item.NotifyRuntimeStructureChanged();
    }

    /// <summary>记录命中瞬间相对受击实体的局部姿态，使可回收投射物继续附着在移动目标上。</summary>
    private void EmbedInReceiver(Mod_DamageReceiver receiver)
    {
        Transform target = receiver.item != null ? receiver.item.transform : receiver.transform;
        if (target == null || !target.gameObject.activeInHierarchy || item == null)
            return;

        _embeddedTarget = target;
        _wasEmbedded = true;
        InvalidateTickSchedule();
        _embeddedLocalPosition = target.InverseTransformPoint(item.transform.position);
        _embeddedLocalRotation = Quaternion.Inverse(target.rotation) * item.transform.rotation;
        UpdateEmbeddedPose();
    }

    /// <summary>让嵌入的投射物跟随受击实体，并同步 ItemMgr 空间索引以保持近距离拾取有效。</summary>
    private void UpdateEmbeddedPose()
    {
        if (_embeddedTarget == null || !_embeddedTarget.gameObject.activeInHierarchy || item == null)
        {
            ClearEmbeddedState();
            if (item != null && !_isFlying) WorldItemWaterSystem.ScheduleSpawnCheck(item);
            return;
        }

        Vector3 worldPosition = _embeddedTarget.TransformPoint(_embeddedLocalPosition);
        Quaternion worldRotation = _embeddedTarget.rotation * _embeddedLocalRotation;
        bool poseChanged = (item.transform.position - worldPosition).sqrMagnitude > 0.00000001f ||
                           Quaternion.Angle(item.transform.rotation, worldRotation) > 0.001f;
        if (!poseChanged)
            return;

        item.transform.SetPositionAndRotation(worldPosition, worldRotation);
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
    }

    /// <summary>清除附着关系；箭矢保持当前世界姿态，目标销毁后会自然留在最后位置。</summary>
    private void ClearEmbeddedState()
    {
        bool wasEmbedded = _wasEmbedded;
        _embeddedTarget = null;
        _wasEmbedded = false;
        _embeddedLocalPosition = Vector3.zero;
        _embeddedLocalRotation = Quaternion.identity;
        if (wasEmbedded) InvalidateTickSchedule();
    }

    /// <summary>投射物损坏时按配置概率掉落一份真实合成配方中的原材料。</summary>
    private void TryDropBrokenSalvage()
    {
        if (Random.value >= Mathf.Clamp01(BrokenSalvageChance) ||
            !TryResolveSalvageIngredient(out string ingredientItemId))
        {
            return;
        }

        GameRes gameRes = GameRes.ExistingInstance;
        ItemMgr itemManager = ItemMgr.Instance;
        if (gameRes == null || itemManager == null)
            return;

        ItemData salvageData = gameRes.CreateItemData(ingredientItemId);
        salvageData.Stack.Amount = 1f;
        salvageData.Stack.CanBePickedUp = true;
        DroppedItemService.Spawn(salvageData, item.transform.position);
    }

    /// <summary>从产出当前投射物的普通合成配方中，按材料用量随机选择一种可确定身份的原材料。</summary>
    private bool TryResolveSalvageIngredient(out string ingredientItemId)
    {
        ingredientItemId = null;
        GameRes gameRes = GameRes.ExistingInstance;
        string projectileItemId = item?.itemData?.IDName;
        if (gameRes == null || string.IsNullOrWhiteSpace(projectileItemId))
            return false;

        var recipes = gameRes.GetRecipes(RecipeType.Crafting);
        for (int recipeIndex = 0; recipeIndex < recipes.Count; recipeIndex++)
        {
            RuntimeRecipe recipe = recipes[recipeIndex];
            if (!RecipeProducesItem(recipe, projectileItemId))
                continue;

            var ingredients = recipe.inputs?.RowItems_List;
            if (ingredients == null)
                return false;

            int totalWeight = 0;
            for (int ingredientIndex = 0; ingredientIndex < ingredients.Count; ingredientIndex++)
            {
                RuntimeRecipeIngredient ingredient = ingredients[ingredientIndex];
                if (ingredient != null && ingredient.matchMode == MatchMode.ExactItem &&
                    ingredient.amount > 0 && !string.IsNullOrWhiteSpace(ingredient.ItemName))
                {
                    totalWeight += ingredient.amount;
                }
            }

            if (totalWeight <= 0)
                return false;

            int roll = Random.Range(0, totalWeight);
            for (int ingredientIndex = 0; ingredientIndex < ingredients.Count; ingredientIndex++)
            {
                RuntimeRecipeIngredient ingredient = ingredients[ingredientIndex];
                if (ingredient == null || ingredient.matchMode != MatchMode.ExactItem ||
                    ingredient.amount <= 0 || string.IsNullOrWhiteSpace(ingredient.ItemName))
                {
                    continue;
                }

                if (roll < ingredient.amount)
                {
                    ingredientItemId = ingredient.ItemName;
                    return true;
                }

                roll -= ingredient.amount;
            }

            return false;
        }

        return false;
    }

    /// <summary>判断配方是否会产出指定物品。</summary>
    private static bool RecipeProducesItem(RuntimeRecipe recipe, string itemId)
    {
        var results = recipe?.outputs?.results;
        if (results == null)
            return false;

        for (int resultIndex = 0; resultIndex < results.Count; resultIndex++)
        {
            RuntimeRecipeResult result = results[resultIndex];
            if (result != null && result.amount > 0 &&
                string.Equals(result.ItemName, itemId, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>为投射物根节点创建或复用刚体。</summary>
    private void EnsureBody()
    {
        if (_body != null)
            return;

        _body = item.GetComponent<Rigidbody2D>();
        if (_body == null)
            _body = item.gameObject.AddComponent<Rigidbody2D>();
    }

    /// <summary>攻击 Trigger 只负责发现候选，另用实体碰撞盒交给 Physics2D 计算墙面反弹。</summary>
    private void EnsureSolidCollider()
    {
        bool addedCollider = false;
        if (_solidCollider == null)
        {
            BoxCollider2D[] colliders = item.GetComponents<BoxCollider2D>();
            for (int i = 0; i < colliders.Length; i++)
                if (!colliders[i].isTrigger) { _solidCollider = colliders[i]; break; }
            if (_solidCollider == null)
            {
                _solidCollider = item.gameObject.AddComponent<BoxCollider2D>();
                addedCollider = true;
            }
        }
        if (_damage.DamageCollider is BoxCollider2D hitbox)
            _solidCollider.size = hitbox.size * Mathf.Clamp(SolidColliderSizeMultiplier, 0.1f, 1f);
        if (_flightMaterial == null)
        {
            _flightMaterial = new PhysicsMaterial2D("Projectile Collision");
            _flightMaterial.hideFlags = HideFlags.DontSave;
        }
        _flightMaterial.friction = 0f;
        _flightMaterial.bounciness = Mathf.Clamp01(CollisionBounciness);
        _solidCollider.isTrigger = false;
        _solidCollider.sharedMaterial = _flightMaterial;

        ProjectilePhysicsContact2D contactRelay = item.GetComponent<ProjectilePhysicsContact2D>();
        if (contactRelay == null)
            contactRelay = item.gameObject.AddComponent<ProjectilePhysicsContact2D>();
        contactRelay.Bind(this);
        if (addedCollider) item.NotifyRuntimeStructureChanged();
    }

    /// <summary>发射点可能还在射手身体内，飞行期间只排除这一组实体碰撞。</summary>
    private void IgnoreShooterCollisions(Item shooter)
    {
        RestoreFlightCollisions();
        IgnoreSolidCollisions(shooter);
    }

    /// <summary>射手和本次逻辑闪避的目标共享飞行期碰撞排除，回收时统一撤销。</summary>
    private void IgnoreSolidCollisions(Item target)
    {
        if (target == null || _solidCollider == null) return;
        Collider2D[] colliders = target.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || collider.isTrigger || collider == _solidCollider ||
                _ignoredFlightColliders.Contains(collider)) continue;
            if (_solidCollider.enabled && collider.enabled)
                Physics2D.IgnoreCollision(_solidCollider, collider, true);
            _ignoredFlightColliders.Add(collider);
        }
    }

    /// <summary>回收或重复发射时撤销上一轮飞行的临时碰撞排除。</summary>
    private void RestoreFlightCollisions()
    {
        if (_solidCollider != null)
            for (int i = 0; i < _ignoredFlightColliders.Count; i++)
                if (_ignoredFlightColliders[i] != null)
                    Physics2D.IgnoreCollision(_solidCollider, _ignoredFlightColliders[i], false);
        _ignoredFlightColliders.Clear();
    }

    private void BindArcPresentation()
    {
        Transform visual = item?.Sprite != null ? item.Sprite.transform : item?.GetComponentInChildren<SpriteRenderer>(true)?.transform;
        if (_arcVisual != visual) _arcVisualBaseCaptured = false;
        _arcVisual = visual;
        _arcHitbox = _damage?.DamageCollider != null ? _damage.DamageCollider.transform : null;
        if (_arcVisual != null && !_arcVisualBaseCaptured)
        {
            _arcVisualBase = _arcVisual.localPosition;
            _arcVisualBaseRotation = _arcVisual.localRotation;
            _arcVisualBaseCaptured = true;
        }
        if (_arcHitbox != null) _arcHitboxBase = _arcHitbox.localPosition;
    }

    private void UpdateArcPresentation(float height)
    {
        // 抛物线高度固定沿世界 Y 轴抬升，不能跟随投射物朝向旋转，否则朝左时会把弧线翻成反向开口。
        ApplyWorldArcHeight(_arcVisual, _arcVisualBase, height);
        ApplyWorldArcHeight(_arcHitbox, _arcHitboxBase, height);
    }

    private static void ApplyWorldArcHeight(Transform target, Vector3 baseLocalPosition, float height)
    {
        if (target == null)
            return;

        Transform parent = target.parent;
        Vector3 groundPosition = parent != null
            ? parent.TransformPoint(baseLocalPosition)
            : baseLocalPosition;
        target.position = groundPosition + Vector3.up * Mathf.Max(0f, height);
    }

    /// <summary>普通世界箭矢保持静止但继续参与拾取触发器。</summary>
    private void ConfigureBodyForRest()
    {
        EnsureBody();
        WorldPhysicsService2D.ConfigureItemBodyForRestingPose(item, _body);
        _body.interpolation = RigidbodyInterpolation2D.None;
        if (_solidCollider != null) _solidCollider.enabled = false;
    }

    #endregion
}

/// <summary>把投射物根节点的刚体碰撞回传给模块，让伤害结算先于反弹表现作最终裁决。</summary>
[DisallowMultipleComponent]
public sealed class ProjectilePhysicsContact2D : MonoBehaviour
{
    private Mod_Projectile projectile;

    public void Bind(Mod_Projectile target) => projectile = target;

    private void FixedUpdate() => projectile?.PreparePhysicsStep(Time.fixedDeltaTime);

    private void OnCollisionEnter2D(Collision2D collision) => projectile?.HandlePhysicalContact(collision);
}
