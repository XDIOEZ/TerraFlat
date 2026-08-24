using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 动物通用蓄力冲撞模块：蓄力期间锁定原地，冲刺开始时锁定方向和终点，
/// 按配置距离直线冲刺后停止，并用独立触发器按普通 Mod_Damage 的伤害模板结算一次碰撞伤害。
/// </summary>
public sealed class Mod_ChargeAttack : Module, IAnimalCombatSkill, ITrunDirection
{
    public const string DefaultSkillId = "animal.charge_attack";
    public const string PersistedModuleId = "AnimalSkill_ChargeAttack";

    private enum ChargeAttackPhase
    {
        Idle,
        Charging,
        Rushing
    }

    #region 序列化字段
    [Tooltip("动物技能 JSON 模板 ID")]
    public string SkillId = DefaultSkillId;

    public Ex_ModData_MemoryPackable ModData = new Ex_ModData_MemoryPackable();

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }
    #endregion

    #region 运行时字段
    private AnimalSkillDefinition _definition;
    private Mover_AI _mover;
    private Mod_Damage _normalDamage;
    private Mod_AnimatorController _animator;
    private Mod_TurnBack _turnBody;
    private Collider2D _hitbox;
    private Item _target;
    private readonly HashSet<DamageReceiver> _hitReceivers = new HashSet<DamageReceiver>();
    private readonly List<Collider2D> _overlapColliders = new List<Collider2D>();
    private readonly ChargeDamageSender _damageSender = new ChargeDamageSender();

    private ChargeAttackPhase _phase;
    private float _phaseRemain;
    private float _cooldownRemain;
    private float _appliedSpeedMultiplier = 1f;
    private bool _speedMultiplierApplied;
    private bool _phaseAnimationPlayed;
    private Vector3 _baseLocalPosition;
    private Vector2 _rushDirection;
    private Vector2 _rushEndPosition;
    private Transform _previousMoverTarget;
    private bool _moverTargetLocked;
    private bool _definitionWarningLogged;
    #endregion

    #region 技能契约
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;

    public string SkillIdValue => string.IsNullOrWhiteSpace(SkillId) ? DefaultSkillId : SkillId.Trim();
    string IAnimalCombatSkill.SkillId => SkillIdValue;
    public float TriggerDistance => _definition != null ? _definition.TriggerDistance : 0f;
    public bool IsReady => _definition != null && _hitbox != null;
    public bool IsActive => _phase != ChargeAttackPhase.Idle;
    public bool IsOnCooldown => _cooldownRemain > 0f;

    public override void Awake()
    {
        if (ModData == null)
            ModData = new Ex_ModData_MemoryPackable();
        if (string.IsNullOrWhiteSpace(ModData.ID))
            ModData.ID = PersistedModuleId;
    }

    public override void Load()
    {
        _phase = ChargeAttackPhase.Idle;
        _phaseRemain = 0f;
        _cooldownRemain = 0f;
        _hitReceivers.Clear();
        _baseLocalPosition = transform.localPosition;
        _hitbox = GetComponent<Collider2D>();
        if (_hitbox != null)
            CombatPhysicsChannels.AssignDamageSender(_hitbox);
        ResolveDependencies();
        // GameRes 可能仍在加载技能目录，先静默尝试，真正调用技能时再严格报错。
        TryResolveDefinition(false);
        ConfigureHitbox();

        if (_hitbox != null)
            _hitbox.enabled = false;

        if (_turnBody != null)
        {
            _turnBody.OnTrun -= SetFacingDirection;
            _turnBody.OnTrun += SetFacingDirection;
            SetFacingDirection(_turnBody.currentDirection);
        }
    }

    public override void Save()
    {
    }

    public override void ModUpdate(float deltaTime)
    {
        if (_cooldownRemain > 0f)
            _cooldownRemain = Mathf.Max(0f, _cooldownRemain - Mathf.Max(0f, deltaTime));

        if (_phase == ChargeAttackPhase.Idle)
            return;

        if (!TryResolveDefinition())
        {
            ResetRuntime();
            return;
        }

        if (_phase == ChargeAttackPhase.Charging)
        {
            UpdateCharging(deltaTime);
            return;
        }

        UpdateRushing(deltaTime);
    }

    public bool CanStart(Item target)
    {
        ResolveDependencies();
        if (!TryResolveDefinition() || _phase != ChargeAttackPhase.Idle || IsOnCooldown ||
            _mover == null || _normalDamage == null || target == null || item == null)
        {
            return false;
        }

        if (!FactionRelationService.CanAttack(item, target))
            return false;

        return WorldTopologyRuntime.Distance(transform.position, target.transform.position) <=
               Mathf.Max(0.05f, _definition.TriggerDistance);
    }

    public bool TryStart(Item target)
    {
        if (!CanStart(target))
            return false;

        _target = target;
        _phase = ChargeAttackPhase.Charging;
        _phaseRemain = _definition.ChargeDurationSeconds;
        _phaseAnimationPlayed = false;
        _hitReceivers.Clear();
        _mover.StopMovement();
        FaceTarget(target.transform.position, true);
        return true;
    }

    public void Cancel()
    {
        if (_phase == ChargeAttackPhase.Idle)
            return;

        CleanupActiveState(true);
    }

    public void ResetRuntime()
    {
        CleanupActiveState(false);
        _cooldownRemain = 0f;
    }
    #endregion

    #region 蓄力与冲刺
    private void UpdateCharging(float deltaTime)
    {
        if (_target == null || _mover == null)
        {
            Cancel();
            return;
        }

        _mover.StopMovement();
        FaceTarget(_target.transform.position);
        PlayPhaseAnimation(_definition.ChargeAnimation);
        _phaseRemain -= Mathf.Max(0f, deltaTime);
        if (_phaseRemain <= 0f)
            BeginRush();
    }

    private void BeginRush()
    {
        if (_target == null || _mover == null || _normalDamage == null)
        {
            Cancel();
            return;
        }

        Vector2 rushDelta = WorldTopologyRuntime.ShortestDelta(
            transform.position,
            _target.transform.position);
        if (rushDelta.sqrMagnitude < 0.0001f && _turnBody != null)
            rushDelta = _turnBody.currentDirection;
        if (rushDelta.sqrMagnitude < 0.0001f)
        {
            Cancel();
            return;
        }

        _rushDirection = rushDelta.normalized;
        _rushEndPosition = WorldTopologyRuntime.NormalizePosition(
            (Vector2)transform.position + _rushDirection * _definition.RushDistance);

        _damageSender.attacker = item;
        _damageSender.DamageValues = _normalDamage.ResolveDamageValues()
            .Scaled(_definition.DamageMultiplier);
        _phase = ChargeAttackPhase.Rushing;
        _phaseRemain = _definition.RushDurationSeconds;
        _phaseAnimationPlayed = false;
        ApplySpeedMultiplier();
        FaceDirection(_rushDirection, true);
        _turnBody?.SetDirectionLock(true);
        LockMoverTarget();
        _mover.SetDestination(_rushEndPosition, true);
        SetHitboxEnabled(true);
        PlayPhaseAnimation(_definition.RushAnimation);
        ScanCurrentOverlapsAndApplyDamage();
    }

    private void UpdateRushing(float deltaTime)
    {
        if (_mover == null)
        {
            FinishRush();
            return;
        }

        Vector2 remaining = WorldTopologyRuntime.ShortestDelta(
            transform.position,
            _rushEndPosition);
        float stopDistance = Mathf.Max(0.05f, _definition.ArrivalDistance);
        if (remaining.sqrMagnitude <= stopDistance * stopDistance ||
            Vector2.Dot(remaining, _rushDirection) <= 0f)
        {
            FinishRush();
            return;
        }

        // 冲刺期间只重复提交固定终点，不读取玩家当前位置，避免追踪转向。
        _mover.SetDestination(_rushEndPosition);
        PlayPhaseAnimation(_definition.RushAnimation);
        _phaseRemain -= Mathf.Max(0f, deltaTime);
        if (_phaseRemain <= 0f)
            FinishRush();
    }

    private void FinishRush()
    {
        CleanupActiveState(true);
    }

    private void CleanupActiveState(bool startCooldown)
    {
        SetHitboxEnabled(false);
        _mover?.StopMovement();
        RestoreMoverTarget();
        _turnBody?.SetDirectionLock(false);
        RestoreSpeedMultiplier();
        _phase = ChargeAttackPhase.Idle;
        _phaseRemain = 0f;
        _target = null;
        _rushDirection = Vector2.zero;
        _rushEndPosition = default;
        _hitReceivers.Clear();
        _phaseAnimationPlayed = false;
        if (startCooldown && _definition != null)
            _cooldownRemain = Mathf.Max(_cooldownRemain, _definition.CooldownSeconds);
    }
    #endregion

    #region 碰撞伤害
    private void ScanCurrentOverlapsAndApplyDamage()
    {
        if (_phase != ChargeAttackPhase.Rushing || _hitbox == null || !_hitbox.enabled)
            return;

        Physics2D.SyncTransforms();
        _overlapColliders.Clear();
        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = true,
            useLayerMask = true,
            layerMask = CombatPhysicsChannels.DamageReceiverMask,
            useDepth = false,
            useNormalAngle = false
        };
        _hitbox.OverlapCollider(filter, _overlapColliders);

        for (int i = 0; i < _overlapColliders.Count; i++)
        {
            DamageReceiver receiver = WorldTopologyColliderProxy.ResolveComponent<DamageReceiver>(
                _overlapColliders[i]);
            ApplyDamage(receiver);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_phase != ChargeAttackPhase.Rushing ||
            !CombatPhysicsChannels.IsDamageReceiverCollider(other))
        {
            return;
        }

        ApplyDamage(WorldTopologyColliderProxy.ResolveComponent<DamageReceiver>(other));
    }

    private void ApplyDamage(DamageReceiver receiver)
    {
        if (receiver == null || receiver.item == null || receiver.item == item ||
            !_hitReceivers.Add(receiver))
        {
            return;
        }

        if (!FactionRelationService.CanAttack(item, receiver.item))
        {
            _hitReceivers.Remove(receiver);
            return;
        }

        receiver.Hurt(_damageSender);
    }
    #endregion

    #region 依赖与表现
    private void ResolveDependencies()
    {
        if (item == null)
            return;

        if (_mover == null)
        {
            _mover = item.itemMods.GetMod_ByID<Mover_AI>(ModText.Mover) ??
                     item.itemMods.GetMod_ByID<Mover_AI>(ModText.Mover_AI);
        }

        if (_normalDamage == null)
        {
            Mod_Damage[] damageModules = item.GetComponentsInChildren<Mod_Damage>(true);
            for (int i = 0; i < damageModules.Length; i++)
            {
                if (damageModules[i] != null)
                {
                    _normalDamage = damageModules[i];
                    break;
                }
            }
        }

        _animator ??= item.GetComponentInChildren<Mod_AnimatorController>(true);
        _turnBody ??= item.itemMods.GetMod_ByID<Mod_TurnBack>(ModText.TrunBody);
    }

    private bool TryResolveDefinition(bool reportError = true)
    {
        if (_definition != null &&
            string.Equals(_definition.Id, SkillIdValue, System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (AnimalSkillCatalogService.TryGet(SkillIdValue, out AnimalSkillDefinition definition) &&
            string.Equals(definition.Type, AnimalSkillDefinition.ChargeAttackType,
                System.StringComparison.OrdinalIgnoreCase))
        {
            _definition = definition;
            ConfigureHitbox();
            return true;
        }

        if (reportError && !_definitionWarningLogged)
        {
            _definitionWarningLogged = true;
            Debug.LogError(
                $"[{nameof(Mod_ChargeAttack)}] 找不到技能模板：{SkillIdValue}，目标物体：{name}",
                this);
        }

        return false;
    }

    private void ConfigureHitbox()
    {
        if (_hitbox == null || _definition == null)
            return;

        _hitbox.isTrigger = true;
        _hitbox.offset = _definition.HitboxOffset;
        if (_hitbox is BoxCollider2D box)
            box.size = _definition.HitboxSize;
    }

    private void PlayPhaseAnimation(string animationName)
    {
        if (_phaseAnimationPlayed || _animator == null || string.IsNullOrWhiteSpace(animationName))
            return;

        _animator.ForcePlayAnimation(animationName);
        _phaseAnimationPlayed = true;
    }

    private void FaceTarget(Vector3 targetPosition, bool immediate = false)
    {
        Vector2 direction = WorldTopologyRuntime.ShortestDelta(transform.position, targetPosition);
        if (direction.sqrMagnitude < 0.0001f)
            return;

        FaceDirection(direction, immediate);
    }

    /// <summary>按给定方向转身，冲刺开始后不再重新读取目标。</summary>
    private void FaceDirection(Vector2 direction, bool immediate = false)
    {
        if (direction.sqrMagnitude < 0.0001f)
            return;

        if (_turnBody == null)
        {
            SetFacingDirection(direction);
            return;
        }

        if (immediate)
            _turnBody.ResetTurnState();
        _turnBody.TurnBodyToDirection(direction);
        if (immediate)
        {
            _turnBody.UpdateTurn(float.MaxValue);
            _turnBody.UpdateAllTransformDirections();
        }
    }

    /// <summary>冲刺期间暂时屏蔽 Mover_AI 对动态目标的追踪。</summary>
    private void LockMoverTarget()
    {
        if (_mover == null || _moverTargetLocked)
            return;

        _previousMoverTarget = _mover.target;
        _mover.target = null;
        _moverTargetLocked = true;
    }

    /// <summary>技能结束后恢复 Mover_AI 原本的目标。</summary>
    private void RestoreMoverTarget()
    {
        if (!_moverTargetLocked || _mover == null)
            return;

        _mover.target = _previousMoverTarget;
        _previousMoverTarget = null;
        _moverTargetLocked = false;
    }

    /// <summary>同步冲撞触发器到动物当前左右朝向。</summary>
    private void SetFacingDirection(Vector2 direction)
    {
        if (Mathf.Abs(direction.x) < 0.001f)
            return;

        Vector3 localPosition = transform.localPosition;
        float offset = _definition != null ? Mathf.Abs(_definition.HitboxForwardOffset) : 0f;
        localPosition.x = _baseLocalPosition.x + offset * Mathf.Sign(direction.x);
        transform.localPosition = localPosition;
    }

    private void ApplySpeedMultiplier()
    {
        if (_speedMultiplierApplied || _mover == null || _definition == null)
            return;

        if (_mover.Speed == null)
            _mover.Data.Speed = new GameValue_float(1f);

        _appliedSpeedMultiplier = Mathf.Max(0.01f, _definition.RushSpeedMultiplier);
        _mover.Speed.MultiplicativeModifier *= _appliedSpeedMultiplier;
        _speedMultiplierApplied = true;
    }

    private void RestoreSpeedMultiplier()
    {
        if (!_speedMultiplierApplied || _mover == null)
            return;

        _mover.Speed.MultiplicativeModifier /= Mathf.Max(0.01f, _appliedSpeedMultiplier);
        _speedMultiplierApplied = false;
        _appliedSpeedMultiplier = 1f;
    }

    private void SetHitboxEnabled(bool enabled)
    {
        if (_hitbox != null)
            _hitbox.enabled = enabled;
    }

    private void OnDisable()
    {
        CleanupActiveState(false);
    }

    private void OnDestroy()
    {
        if (_turnBody != null)
            _turnBody.OnTrun -= SetFacingDirection;
        CleanupActiveState(false);
    }
    #endregion

    private sealed class ChargeDamageSender : IDamageSender
    {
        public CombatDamage DamageValues { get; set; }
        public Item attacker { get; set; }
    }
}
