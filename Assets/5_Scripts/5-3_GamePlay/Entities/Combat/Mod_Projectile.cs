using System.Collections.Generic;
using FlatWorld.NaturalEntities;
using UnityEngine;

/// <summary>
/// 通用物品投射模块：负责把带 Mod_Damage 的物品以刚体方式发射、按蓄力缩放速度与伤害，
/// 用碰撞体扫掠补足高速 Trigger 的帧间穿透，并以轻微空气阻力和虚拟抛物线高度决定落地停止。
/// </summary>
public sealed class Mod_Projectile : Module, IItemModuleDependencyBinder
{
    public const string PersistedModuleId = "Mod_Projectile";

    #region 配置

    [Min(0f), Tooltip("最低蓄力时的飞行速度。")]
    public float MinSpeed = 25f;

    [Min(0f), Tooltip("满蓄力时的飞行速度。")]
    public float MaxSpeed = 60f;

    [Min(0f), Tooltip("飞行中的线性空气阻力；只让水平速度缓慢衰减，不负责决定落地。")]
    public float FlightLinearDrag = 0.12f;

    [Range(0f, 1f), Tooltip("投射物撞到实体墙面时由 Physics2D 计算的反弹系数。")]
    public float CollisionBounciness = 0.35f;

    [Min(0f), Tooltip("投射物被防御弹开时的视觉旋转速度。")]
    public float BounceSpinDegreesPerSecond = 900f;

    [Min(0f), Tooltip("投射物被防御弹开后的视觉旋转持续时间。")]
    public float BounceSpinDuration = 0.35f;

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

    [Tooltip("命中硬目标后允许发生形态变化的物品标签；默认只让 Stone 类投射物参与。")]
    public string HardImpactTransformRequiredTag = "Stone";

    [Tooltip("命中硬目标后成功变化得到的物品 ID；默认得到打制石器。")]
    public string HardImpactTransformItemId = "ChippedTool";

    [Range(0f, 1f), Tooltip("满足硬度门槛后发生形态变化的概率。")]
    public float HardImpactTransformChance = 0.1f;

    [Min(0f), Tooltip("目标对应伤害类型防御达到该数值才视为足够坚硬。")]
    public float HardImpactMinimumDefense = 2f;

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
    private readonly List<Collider2D> _ignoredShooterColliders = new();
    private Vector2 _pendingImpactPosition;
    private Vector2 _pendingImpactNormal;
    private bool _hasPendingImpact;
    private bool _processingPhysicalContact;
    private bool _physicalContactResolved;
    private bool _resolvedBounceThisSweep;
    private float _bounceSpinRemaining;
    private float _bounceSpinDirection = 1f;
    private bool _isFlying;
    private bool _endingFlight;
    private bool _hardImpactTransformPending;
    private long _lastHardImpactTargetKey = long.MinValue;
    private double _lastHardImpactTime = double.NegativeInfinity;
    private Transform _embeddedTarget;
    private bool _wasEmbedded;
    private Vector3 _embeddedLocalPosition;
    private Quaternion _embeddedLocalRotation = Quaternion.identity;
    /// <summary>飞行或仍附着目标时属于战斗实体，不能自动转换成静态掉落物。</summary>
    public bool HasActiveWorldAttachment => _isFlying || _wasEmbedded || _hardImpactTransformPending;

    #endregion

    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;

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
    public override void Load()
    {
        if (_damage == null)
            throw new MissingComponentException($"{name} 缺少 Mod_Damage 依赖。");

        _damage.OnReceiverDamageResolved -= HandleReceiverDamageResolved;
        _damage.OnReceiverDamageResolved += HandleReceiverDamageResolved;
        _damage.OnExternalDamageResolved -= HandleExternalDamageResolved;
        _damage.OnExternalDamageResolved += HandleExternalDamageResolved;
        _baseDamage = _damage.ResolveDamageValues().Scaled(1f);
        _damage.StopAttack();
        RestoreShooterCollisions();
        ConfigureBodyForRest();
        _isFlying = false;
        _endingFlight = false;
        _hardImpactTransformPending = false;
        _lastHardImpactTargetKey = long.MinValue;
        _lastHardImpactTime = double.NegativeInfinity;
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
    public override void Save()
    {
    }

    /// <summary>飞行期间补做帧间碰撞体扫掠，并按虚拟抛物线高度决定落地。</summary>
    public override void ModUpdate(float deltaTime)
    {
        if (_hardImpactTransformPending)
        {
            CompleteHardImpactTransform();
            return;
        }

        if (!_isFlying && _wasEmbedded)
        {
            UpdateEmbeddedPose();
            return;
        }

        if (!_isFlying)
            return;

        float step = Mathf.Min(Mathf.Max(0f, deltaTime), Mathf.Max(0f, _flightRemain));
        _flightElapsed += step;
        _flightRemain -= step;

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
        SweepFlightPath();
        if (!_isFlying) return;
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);

        if ((_flightElapsed > 0f && _virtualHeight <= 0f) || _flightRemain <= 0f)
            FinishFlight();
    }

    /// <summary>解除伤害事件，防止对象池复用后重复订阅。</summary>
    public override void Unload()
    {
        _damage?.SetDeliveryCapabilities(FlatWorld.Combat.CombatDeliveryCapabilities.None);
        if (_damage != null)
        {
            _damage.OnReceiverDamageResolved -= HandleReceiverDamageResolved;
            _damage.OnExternalDamageResolved -= HandleExternalDamageResolved;
            _damage.SetExplicitProjectileSweep(false);
        }
        _isFlying = false;
        _endingFlight = false;
        _hardImpactTransformPending = false;
        _lastHardImpactTargetKey = long.MinValue;
        _lastHardImpactTime = double.NegativeInfinity;
        _flightRemain = 0f;
        _flightElapsed = 0f;
        _virtualHeight = 0f;
        ResetBounceSpin();
        ClearEmbeddedState();
        UpdateArcPresentation(0f);
        RestoreShooterCollisions();
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
    public void Launch(Item shooter, Vector2 direction, float charge01, float sourceDamageMultiplier, float sourceSpeedMultiplier)
    {
        if (item == null || _damage == null || direction.sqrMagnitude < 0.0001f)
            throw new System.InvalidOperationException($"{name} 无法发射：投射物尚未正确初始化或方向无效。");

        float normalizedCharge = Mathf.Clamp01(charge01);
        float speed = ResolveLaunchSpeed(normalizedCharge, sourceSpeedMultiplier);
        float damageMultiplier = Mathf.Lerp(
            Mathf.Max(0f, MinDamageMultiplier),
            Mathf.Max(0f, MaxDamageMultiplier),
            normalizedCharge);
        damageMultiplier *= Mathf.Max(0f, sourceDamageMultiplier);
        Vector2 normalizedDirection = direction.normalized;

        item.Owner = shooter;
        item.SetInHand(false);
        item.itemData.Stack.Amount = 1f;
        item.itemData.Stack.CanBePickedUp = false;
        ClearEmbeddedState();

        EnsureBody();
        EnsureSolidCollider();
        _body.bodyType = RigidbodyType2D.Dynamic;
        _body.gravityScale = 0f;
        _body.mass = Mathf.Max(0.01f, item.itemData.Stack.CurrentWeight);
        // 可见抛物线必须保持匀速地面位移，否则向下投掷时阻力会把曲线扭成非二次曲线。
        _body.drag = UseVisibleArc ? 0f : Mathf.Max(0f, FlightLinearDrag);
        _body.constraints = RigidbodyConstraints2D.FreezeRotation;
        _body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        _body.interpolation = RigidbodyInterpolation2D.Interpolate;
        _arcVelocity = normalizedDirection * speed;
        _groundFlightPosition = _body.position;
        _body.velocity = _arcVelocity;
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
        _hardImpactTransformPending = false;
        _lastHardImpactTargetKey = long.MinValue;
        _lastHardImpactTime = double.NegativeInfinity;
        ResetBounceSpin();
        _isFlying = true;

        _damage.SetExplicitProjectileSweep(true);
        SetFlightDeliveryCapabilities();

        // 先进入飞行态再开伤害窗，确保出生点附近的有效命中也能立即结束箭矢。
        _damage.StartAttack();
    }

    /// <summary>实体命中时把刚体放到 Physics2D 检出的接触点。</summary>
    private void SetProjectilePosition(Vector2 position)
    {
        _body.position = position;
        item.transform.position = new Vector3(position.x, position.y, item.transform.position.z);
    }

    /// <summary>高空段与落地段共享同一窗口，切换资格不能清掉已经命中的目标。</summary>
    private void SetFlightDeliveryCapabilities()
    {
        var capabilities = FlatWorld.Combat.CombatDeliveryCapabilities.Projectile |
            FlatWorld.Combat.CombatDeliveryCapabilities.AirborneTargets;
        if (UseVisibleArc && _flightElapsed < _flightDuration * (1f - Mathf.Clamp01(GroundImpactFraction)))
            capabilities |= FlatWorld.Combat.CombatDeliveryCapabilities.AirborneOnly;
        _damage.SetDeliveryCapabilities(capabilities);
    }

    /// <summary>用伤害盒扫过上一帧到当前帧的完整路径，补足高速 Trigger 可能漏掉的目标。</summary>
    private void SweepFlightPath()
    {
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

        int hitCount = Physics2D.BoxCastNonAlloc(
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

        if (_hasPendingImpact)
            SetProjectilePosition(_pendingImpactPosition);
        if (receiver != null && TryQueueHardImpactTransform(
                ResolveImpactDefense(receiver.Defense), BuildHardImpactTargetKey(receiver.GetInstanceID(), false)))
        {
            PrepareHardImpactTransform();
            return;
        }

        if (resolvedDamage > 0f)
        {
            FinishFlight(receiver);
            return;
        }

        if (_hasPendingImpact && !_processingPhysicalContact)
            SetProjectilePosition(_pendingImpactPosition);
        BounceFromBlockedHit(_pendingImpactNormal, _processingPhysicalContact);
    }

    /// <summary>ECS 结算回执同样结束投射物，不能因没有 DamageReceiver 组件而穿过狼继续飞。</summary>
    private void HandleExternalDamageResolved(FlatWorld.Combat.CombatDamageContext context, float resolvedDamage)
    {
        if (!_isFlying || resolvedDamage < 0f) return;
        Vector2 offset = _damage.DamageCollider is BoxCollider2D box
            ? (Vector2)box.transform.TransformPoint(box.offset) - (Vector2)item.transform.position : Vector2.zero;
        SetProjectilePosition(WorldTopologyRuntime.NormalizePosition((Vector2)context.HitPoint - offset));
        if (TryQueueNaturalHardImpactTransform(context.HitPoint))
        {
            PrepareHardImpactTransform();
            return;
        }
        if (resolvedDamage == 0f)
        {
            BounceFromBlockedHit(Vector2.zero, false);
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
            if (TryQueueNaturalHardImpactTransform(contactPoint))
            {
                PrepareHardImpactTransform();
                return;
            }
            StartBounceSpin(contactNormal);
            return;
        }

        _pendingImpactPosition = contactPoint;
        _pendingImpactNormal = contactNormal;
        _hasPendingImpact = true;
        _processingPhysicalContact = true;
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
        if (_isFlying && !_physicalContactResolved)
            StartBounceSpin(contactNormal);
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
        if (BounceSpinDuration <= 0f || BounceSpinDegreesPerSecond <= 0f)
            return;

        Vector2 velocity = _body != null ? _body.velocity : Vector2.zero;
        float cross = velocity.x * impactNormal.y - velocity.y * impactNormal.x;
        _bounceSpinDirection = Mathf.Abs(cross) > 0.001f
            ? Mathf.Sign(cross)
            : (velocity.x >= 0f ? -1f : 1f);
        _bounceSpinRemaining = BounceSpinDuration;
    }

    private void UpdateBounceSpin(float deltaTime)
    {
        if (_bounceSpinRemaining <= 0f || _arcVisual == null || deltaTime <= 0f)
            return;

        float duration = Mathf.Max(0.0001f, BounceSpinDuration);
        float strength = Mathf.Clamp01(_bounceSpinRemaining / duration);
        _arcVisual.localRotation *= Quaternion.Euler(
            0f, 0f, BounceSpinDegreesPerSecond * _bounceSpinDirection * strength * deltaTime);
        _bounceSpinRemaining = Mathf.Max(0f, _bounceSpinRemaining - deltaTime);
    }

    private void ResetBounceSpin()
    {
        _bounceSpinRemaining = 0f;
        _bounceSpinDirection = 1f;
        _pendingImpactNormal = Vector2.zero;
        _processingPhysicalContact = false;
        _physicalContactResolved = false;
        _resolvedBounceThisSweep = false;
    }

    #region 硬目标打制变化

    /// <summary>读取当前主伤害类型对应的目标防御，避免把“坚硬”写死为某个资源 ID。</summary>
    private float ResolveImpactDefense(CombatDefense defense)
    {
        if (defense == null)
            return 0f;

        return _damage.ResolveDamageValues().DominantKind switch
        {
            CombatDamageKind.Cutting => defense.Cutting,
            CombatDamageKind.Piercing => defense.Piercing,
            CombatDamageKind.Chopping => defense.Chopping,
            CombatDamageKind.Blunt => defense.Blunt,
            _ => 0f
        };
    }

    /// <summary>资源实体没有 DamageReceiver 外壳，因此直接从自然实体后端读取命中点防御。</summary>
    private bool TryQueueNaturalHardImpactTransform(Vector2 hitPoint)
    {
        CombatDamageKind kind = _damage.ResolveDamageValues().DominantKind;
        if (!NaturalEntityEcsService.TryGetCombatDefenseAtPoint(hitPoint, kind,
                out float defense, out int runtimeId))
        {
            return false;
        }

        return TryQueueHardImpactTransform(defense, BuildHardImpactTargetKey(runtimeId, true));
    }

    /// <summary>同一次硬目标接触只抽一次概率，避免 ECS 扫掠与物理碰撞重复判定。</summary>
    private bool TryQueueHardImpactTransform(float defense, long targetKey)
    {
        if (_hardImpactTransformPending || item?.itemData?.Tags == null ||
            string.IsNullOrWhiteSpace(HardImpactTransformRequiredTag) ||
            !item.itemData.Tags.Contains(HardImpactTransformRequiredTag) ||
            string.IsNullOrWhiteSpace(HardImpactTransformItemId) ||
            defense < Mathf.Max(0f, HardImpactMinimumDefense))
        {
            return false;
        }

        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition(HardImpactTransformItemId, out _))
            return false;

        double now = Time.timeAsDouble;
        if (targetKey == _lastHardImpactTargetKey && now - _lastHardImpactTime < 0.12d)
            return false;

        _lastHardImpactTargetKey = targetKey;
        _lastHardImpactTime = now;
        if (Random.value >= Mathf.Clamp01(HardImpactTransformChance))
            return false;

        _hardImpactTransformPending = true;
        return true;
    }

    /// <summary>给 GameObject 与纯数据实体分开命名空间，避免运行时整数 ID 偶然相同。</summary>
    private static long BuildHardImpactTargetKey(int id, bool external)
        => ((long)(external ? 2 : 1) << 32) | (uint)id;

    /// <summary>成功抽中后立即结束战斗飞行，但把替换延后一帧，避免在伤害回调栈内回收当前物品。</summary>
    private void PrepareHardImpactTransform()
    {
        if (!_hardImpactTransformPending || !_isFlying || _endingFlight)
            return;

        _endingFlight = true;
        _isFlying = false;
        _damage.StopAttack();
        _damage.SetExplicitProjectileSweep(false);
        _damage.SetDamageValues(_baseDamage.Scaled(1f));
        _damage.SetDeliveryCapabilities(FlatWorld.Combat.CombatDeliveryCapabilities.None);
        RestoreShooterCollisions();

        if (_body != null)
        {
            _body.velocity = Vector2.zero;
            _body.angularVelocity = 0f;
            _body.drag = 0f;
            _body.bodyType = RigidbodyType2D.Kinematic;
        }
        if (_solidCollider != null) _solidCollider.enabled = false;

        _flightRemain = 0f;
        _virtualHeight = 0f;
        ResetBounceSpin();
        UpdateArcPresentation(0f);
        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
    }

    /// <summary>把命中的石头替换成真实可拾取的打制产物；配置失效时保留原石头而不吞物品。</summary>
    private void CompleteHardImpactTransform()
    {
        _hardImpactTransformPending = false;
        GameRes resources = GameRes.ExistingInstance;
        ItemMgr itemManager = ItemMgr.Instance;
        if (item == null || resources == null || itemManager == null ||
            !resources.TryGetItemDefinition(HardImpactTransformItemId, out _))
        {
            _endingFlight = false;
            if (item != null)
            {
                item.Owner = null;
                item.itemData.Stack.CanBePickedUp = true;
                WorldItemWaterSystem.ScheduleSpawnCheck(item);
            }
            return;
        }

        ItemData replacement = resources.CreateItemData(HardImpactTransformItemId);
        replacement.Stack.Amount = 1f;
        replacement.Stack.CanBePickedUp = true;
        DroppedItemService.Spawn(replacement, item.transform.position,
            scale: Vector3.one, rotation: item.transform.eulerAngles.z);
        itemManager.DespawnItem(item, saveData: false);
    }

    #endregion

    /// <summary>停止飞行，并按 RecoveryChance 决定留下可拾取物还是销毁。</summary>
    private void FinishFlight(Mod_DamageReceiver hitReceiver = null)
    {
        if (!_isFlying || _endingFlight)
            return;

        _endingFlight = true;
        _isFlying = false;
        float remainingHeight = Mathf.Max(0f, _virtualHeight);
        _damage.StopAttack();
        _damage.SetExplicitProjectileSweep(false);
        _damage.SetDamageValues(_baseDamage.Scaled(1f));
        _damage.SetDeliveryCapabilities(FlatWorld.Combat.CombatDeliveryCapabilities.None);
        RestoreShooterCollisions();

        if (_body != null)
        {
            _body.velocity = Vector2.zero;
            _body.angularVelocity = 0f;
            _body.drag = 0f;
            _body.bodyType = RigidbodyType2D.Kinematic;
        }
        if (_solidCollider != null) _solidCollider.enabled = false;

        _flightRemain = 0f;
        _virtualHeight = 0f;
        ResetBounceSpin();
        UpdateArcPresentation(0f);

        ItemMgr.Instance?.NotifyRuntimeItemMoved(item);
        bool recover = Random.value < Mathf.Clamp01(RecoveryChance);
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
    }

    /// <summary>记录命中瞬间相对受击实体的局部姿态，使可回收投射物继续附着在移动目标上。</summary>
    private void EmbedInReceiver(Mod_DamageReceiver receiver)
    {
        Transform target = receiver.item != null ? receiver.item.transform : receiver.transform;
        if (target == null || !target.gameObject.activeInHierarchy || item == null)
            return;

        _embeddedTarget = target;
        _wasEmbedded = true;
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
        _embeddedTarget = null;
        _wasEmbedded = false;
        _embeddedLocalPosition = Vector3.zero;
        _embeddedLocalRotation = Quaternion.identity;
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
        if (_solidCollider == null)
        {
            BoxCollider2D[] colliders = item.GetComponents<BoxCollider2D>();
            for (int i = 0; i < colliders.Length; i++)
                if (!colliders[i].isTrigger) { _solidCollider = colliders[i]; break; }
            if (_solidCollider == null)
                _solidCollider = item.gameObject.AddComponent<BoxCollider2D>();
        }
        if (_damage.DamageCollider is BoxCollider2D hitbox)
            _solidCollider.size = hitbox.size;
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
    }

    /// <summary>发射点可能还在射手身体内，飞行期间只排除这一组实体碰撞。</summary>
    private void IgnoreShooterCollisions(Item shooter)
    {
        RestoreShooterCollisions();
        if (shooter == null || _solidCollider == null) return;
        Collider2D[] colliders = shooter.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || collider.isTrigger || collider == _solidCollider) continue;
            Physics2D.IgnoreCollision(_solidCollider, collider, true);
            _ignoredShooterColliders.Add(collider);
        }
    }

    /// <summary>回收或重复发射时撤销上一位射手的临时碰撞排除。</summary>
    private void RestoreShooterCollisions()
    {
        if (_solidCollider != null)
            for (int i = 0; i < _ignoredShooterColliders.Count; i++)
                if (_ignoredShooterColliders[i] != null)
                    Physics2D.IgnoreCollision(_solidCollider, _ignoredShooterColliders[i], false);
        _ignoredShooterColliders.Clear();
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
        _body.bodyType = RigidbodyType2D.Kinematic;
        _body.gravityScale = 0f;
        _body.drag = 0f;
        _body.velocity = Vector2.zero;
        _body.angularVelocity = 0f;
        _body.constraints = RigidbodyConstraints2D.FreezeRotation;
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

    private void OnCollisionEnter2D(Collision2D collision) => projectile?.HandlePhysicalContact(collision);
}
