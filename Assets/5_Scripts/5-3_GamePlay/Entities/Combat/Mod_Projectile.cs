using System.Collections.Generic;
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
    private readonly List<Collider2D> _ignoredShooterColliders = new();
    private Vector2 _pendingImpactPosition;
    private bool _hasPendingImpact;
    private bool _isFlying;
    private bool _endingFlight;
    private Transform _embeddedTarget;
    private bool _wasEmbedded;
    private Vector3 _embeddedLocalPosition;
    private Quaternion _embeddedLocalRotation = Quaternion.identity;
    /// <summary>飞行或仍附着目标时属于战斗实体，不能自动转换成静态掉落物。</summary>
    public bool HasActiveWorldAttachment => _isFlying || _wasEmbedded;

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
        _flightRemain = 0f;
        _flightElapsed = 0f;
        _virtualLaunchVerticalSpeed = 0f;
        _virtualHeight = 0f;
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
        _flightRemain = 0f;
        _flightElapsed = 0f;
        _virtualHeight = 0f;
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

    /// <summary>按当前投射物配置计算本次发射速度，预览与真实发射共用同一公式。</summary>
    public float ResolveLaunchSpeed(float charge01, float sourceSpeedMultiplier = 1f)
    {
        float normalizedCharge = Mathf.Clamp01(charge01);
        float speed = Mathf.Lerp(Mathf.Max(0f, MinSpeed), Mathf.Max(MinSpeed, MaxSpeed), normalizedCharge);
        return speed * Mathf.Max(0f, sourceSpeedMultiplier);
    }

    /// <summary>按当前蓄力计算完整飞行时长，保持轻点也有最短有效飞行段。</summary>
    public float ResolveFlightDuration(float charge01)
    {
        return Mathf.Max(0.05f, Mathf.Max(0f, MaxFlightSeconds) * Mathf.Clamp01(charge01));
    }

    /// <summary>计算无碰撞情况下的可见轨迹点；可见抛物线始终使用向下开的二次曲线。</summary>
    public Vector2 EvaluateVisibleTrajectoryPoint(
        Vector2 launchPosition,
        Vector2 direction,
        float charge01,
        float normalizedTime,
        float sourceSpeedMultiplier = 1f)
    {
        Vector2 normalizedDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        float duration = ResolveFlightDuration(charge01);
        float elapsed = duration * Mathf.Clamp01(normalizedTime);
        Vector2 position = launchPosition + normalizedDirection * ResolveLaunchSpeed(charge01, sourceSpeedMultiplier) * elapsed;

        if (!UseVisibleArc)
            return position;

        float gravity = Mathf.Max(0.01f, VirtualGravity);
        float launchVerticalSpeed = 0.5f * gravity * duration;
        float height = launchVerticalSpeed * elapsed - 0.5f * gravity * elapsed * elapsed;
        position.y += Mathf.Max(0f, height);
        return position;
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

        for (int i = 0; i < hitCount && _isFlying; i++)
        {
            Collider2D hitCollider = _sweepHits[i].collider;
            if (hitCollider == null)
                continue;

            float impactFraction = Mathf.Clamp01(_sweepHits[i].distance / distance);
            Vector2 impactPosition = _lastFlightPosition + rootDisplacement * impactFraction;
            _pendingImpactPosition = impactPosition;
            _hasPendingImpact = true;
            try { _damage.ProcessExplicitColliderHit(hitCollider, castOrigin, _sweepHits[i].point); }
            finally { _hasPendingImpact = false; }
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

    /// <summary>首次有效实体命中后立即结束飞行；无效结算不会吞掉箭矢。</summary>
    private void HandleReceiverDamageResolved(DamageReceiver receiver, float resolvedDamage)
    {
        if (_isFlying && resolvedDamage >= 0f)
        {
            if (_hasPendingImpact) SetProjectilePosition(_pendingImpactPosition);
            FinishFlight(receiver);
        }
    }

    /// <summary>ECS 结算回执同样结束投射物，不能因没有 DamageReceiver 组件而穿过狼继续飞。</summary>
    private void HandleExternalDamageResolved(FlatWorld.Combat.CombatDamageContext context, float resolvedDamage)
    {
        if (!_isFlying || resolvedDamage < 0f) return;
        Vector2 offset = _damage.DamageCollider is BoxCollider2D box
            ? (Vector2)box.transform.TransformPoint(box.offset) - (Vector2)item.transform.position : Vector2.zero;
        SetProjectilePosition(WorldTopologyRuntime.NormalizePosition((Vector2)context.HitPoint - offset));
        FinishFlight();
    }

    /// <summary>停止飞行，并按 RecoveryChance 决定留下可拾取物还是销毁。</summary>
    private void FinishFlight(DamageReceiver hitReceiver = null)
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
    private void EmbedInReceiver(DamageReceiver receiver)
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
        if (_arcVisual != null) _arcVisual.localPosition = _arcVisualBase + Vector3.up * height;
        if (_arcHitbox != null) _arcHitbox.localPosition = _arcHitboxBase + Vector3.up * height;
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
