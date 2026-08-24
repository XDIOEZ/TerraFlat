using System;
using System.Collections.Generic;
using MemoryPack;
using UnityEngine;

public enum SnowLeopardState
{
    Idle,
    Move,
    Alert,
    Chase,
    Attack
}

/// <summary>
/// 雪豹 AI：饥饿前后使用不同感知半径，始终会主动攻击玩家，但只有饥饿时才把兔子纳入目标。
/// 目标在进入追击时锁定；兔子追击最多持续 30 秒，成功捕食后恢复一整天饥饿计时。
/// </summary>
public sealed partial class AI_SnowLeopard : AI_Base<SnowLeopardState>
{
    #region 保存数据

    [Serializable]
    [MemoryPackable]
    public partial class AI_SnowLeopardSaveData
    {
        public SnowLeopardState State = SnowLeopardState.Idle;
        public float HungerRemaining;
        public bool HungerInitialized;
    }

    #endregion

    #region 模块数据

    public AI_SnowLeopardSaveData Data = new();

    #endregion

    #region 运行时状态

    [SerializeField] private Item currentTarget;
    [SerializeField] private Item lockedTarget;
    [SerializeField] private bool lockedTargetIsRabbit;
    private float rabbitChaseElapsed;
    private float preyReacquireTimer;
    private readonly AI_AttackController attack = new();

    #endregion

    #region 配置

    [Header("调试")]
    public bool debugLog;

    [Header("感知与目标")]
    [Min(0.05f)] public float detectorRefreshInterval = 0.6f;
    [Min(0.1f)] public float perceptionRangeSatiated = 8f;
    [Min(0.1f)] public float perceptionRangeHungry = 24f;
    [Min(0.1f)] public float chaseLossDistance = 30f;
    [Min(0f)] public float preyReacquireDelay = 10f;

    [Header("捕食")]
    [Min(0.1f)] public float rabbitChaseDuration = 30f;

    [Header("攻击")]
    [Min(0.1f)] public float attackTriggerDistance = 1.4f;
    [Min(0f)] public float attackCooldown = 1.2f;
    [Min(0.01f)] public float attackDamageWindow = 0.25f;
    [Min(0f)] public float attackDamageStartDelay = 0.35f;
    [Min(0f)] public float attackRecoveryDuration = 0.25f;

    [Header("闲逛")]
    public bool enableWander = true;
    [Min(0f)] public float idleMinDuration = 0.8f;
    [Min(0f)] public float idleMaxDuration = 2.2f;
    [Min(0.1f)] public float wanderRadius = 5f;
    [Min(0.05f)] public float wanderStopDistance = 0.4f;
    [Min(0f)] public float wanderPauseMin = 0.8f;
    [Min(0f)] public float wanderPauseMax = 2.5f;
    public bool wanderAvoidHighPenalty = true;
    [Min(0)] public int wanderDangerPenalty = 1200;
    [Min(1)] public int wanderSampleCount = 8;
    [Min(0f)] public float wanderPenaltyWeight = 1f;

    [Header("动画")]
    public string animIdle = "Idle";
    public string animMove = "Move";
    public string animAlert = "Idle";
    public string animChase = "Move";
    public string animAttack = "Attack";

    #endregion

    #region 基类配置

    protected override AI_WanderConfig WanderConfig => new()
    {
        enabled = enableWander,
        radius = wanderRadius,
        stopDistance = wanderStopDistance,
        pauseMin = wanderPauseMin,
        pauseMax = wanderPauseMax,
        avoidHighPenalty = wanderAvoidHighPenalty,
        dangerPenalty = wanderDangerPenalty,
        sampleCount = wanderSampleCount,
        penaltyWeight = wanderPenaltyWeight
    };

    protected override AI_IdleConfig IdleConfig => new()
    {
        minDuration = idleMinDuration,
        maxDuration = idleMaxDuration
    };

    protected override float DetectorRefreshInterval => detectorRefreshInterval;
    protected override bool DebugLogEnabled => debugLog;
    protected override bool IsMoveState(SnowLeopardState state) =>
        state == SnowLeopardState.Move || state == SnowLeopardState.Chase;
    protected override bool IsIdleState(SnowLeopardState state) => state == SnowLeopardState.Idle;

    #endregion

    #region 生命周期

    public override void Load()
    {
        ModData.ReadData(ref Data);
        Data ??= new AI_SnowLeopardSaveData();
        _currentState = Data.State;
        _idleRemainTimer = GetIdleDuration();
        InitializeAI();
    }

    public override void Save()
    {
        Data.State = _currentState;
        ModData.WriteData(Data);
    }

    #endregion

    #region 基类钩子

    protected override void OnResetRuntimeState()
    {
        currentTarget = null;
        lockedTarget = null;
        lockedTargetIsRabbit = false;
        rabbitChaseElapsed = 0f;
        preyReacquireTimer = 0f;

        if (!Data.HungerInitialized)
        {
            Data.HungerRemaining = GetDayLength();
            Data.HungerInitialized = true;
        }

        attack.Reset();
        attack.Cooldown = attackCooldown;
        attack.DamageWindow = attackDamageWindow;
        attack.WindupDuration = attackDamageStartDelay;
        attack.RecoveryDuration = attackRecoveryDuration;
    }

    protected override void OnBindExtraModules()
    {
        if (_detector != null)
        {
            _detector.DetectionRadius = Mathf.Max(
                _detector.DetectionRadius,
                Mathf.Max(perceptionRangeHungry, chaseLossDistance));
        }

        attack.Bind(item);
        Mod_Damage[] damageModules = item.GetComponentsInChildren<Mod_Damage>(true);
        CombatDamage damage = new CombatDamage(10f, 10f, 1f, 1f);
        for (int i = 0; i < damageModules.Length; i++)
            damageModules[i]?.SetDamageValues(damage);
    }

    protected override void OnValidateExtraModules()
    {
        if (!attack.HasDamageMods)
        {
            Debug.LogWarning(
                $"[{nameof(AI_SnowLeopard)}] 未找到 Mod_Damage 组件，雪豹不会造成伤害。目标物体: {name}",
                this);
        }
    }

    protected override void UpdateExtraTimers(float deltaTime)
    {
        Data.HungerRemaining = Mathf.Max(0f, Data.HungerRemaining - Mathf.Max(0f, deltaTime));
        preyReacquireTimer = DecrementTimer(preyReacquireTimer, deltaTime);
        attack.Tick(deltaTime);
    }

    protected override void OnPreEvaluate()
    {
        RefreshLockedTarget();
        if (lockedTarget == null && preyReacquireTimer <= 0f)
            AcquireNearestTarget();
    }

    protected override void OnBeforeSwitchState(
        SnowLeopardState previous,
        SnowLeopardState next)
    {
        if (previous == SnowLeopardState.Attack && next != SnowLeopardState.Attack)
            attack.OnExitAttackState();

        if (next == SnowLeopardState.Attack)
            attack.OnEnterAttackState();
    }

    protected override string GetDebugExtraInfo()
    {
        string hungerText = IsHungry ? "饥饿" : $"饱食 {Data.HungerRemaining:F0}s";
        string targetText = currentTarget?.itemData?.IDName ?? "无目标";
        return $" | {hungerText} | 目标: {targetText}";
    }

    #endregion

    #region 状态机

    protected override SnowLeopardState EvaluateNextState()
    {
        if (ShouldAttack())
            return SnowLeopardState.Attack;
        if (ShouldChase())
            return SnowLeopardState.Chase;
        if (ShouldAlert())
            return SnowLeopardState.Alert;
        if (ShouldMoveBase())
            return SnowLeopardState.Move;
        return SnowLeopardState.Idle;
    }

    protected override void ConfigureStateNodes(AIStateMachine<SnowLeopardState> stateMachine)
    {
        RegisterLocomotionStateNodes(stateMachine, SnowLeopardState.Idle, SnowLeopardState.Move);
        stateMachine.Register(CreateStoppedStateNode(
            SnowLeopardState.Alert,
            _ => TickAlert()));
        stateMachine.Register(CreateMovingStateNode(
            SnowLeopardState.Chase,
            TickChase));
        stateMachine.Register(CreateStoppedActionStateNode(
            SnowLeopardState.Attack,
            TickAttack));
    }

    #endregion

    #region 状态条件与 Tick

    private bool ShouldAttack()
    {
        if (!IsLivingTarget(currentTarget) || !FactionRelationService.CanAttack(item, currentTarget))
            return false;

        if (_currentState == SnowLeopardState.Attack && attack.IsAttackLocked)
            return true;

        return DistanceTo(currentTarget.transform) <= attackTriggerDistance;
    }

    private bool ShouldChase()
    {
        if (!IsLivingTarget(currentTarget))
            return false;

        float chaseDistance = _currentState == SnowLeopardState.Chase ||
                              _currentState == SnowLeopardState.Attack
            ? chaseLossDistance
            : GetPerceptionRange();
        return IsWithinEffectivePerceptionRange(currentTarget, chaseDistance);
    }

    private bool ShouldAlert()
    {
        return IsLivingTarget(currentTarget) &&
               DistanceTo(currentTarget.transform) <= GetPerceptionRange();
    }

    private void TickAlert()
    {
        if (currentTarget != null)
            FaceTarget(currentTarget.transform.position);
    }

    private void TickChase(float deltaTime)
    {
        if (!IsLivingTarget(currentTarget))
        {
            StopMove();
            return;
        }

        if (lockedTargetIsRabbit)
        {
            rabbitChaseElapsed += Mathf.Max(0f, deltaTime);
            if (rabbitChaseElapsed >= Mathf.Max(0.1f, rabbitChaseDuration))
            {
                GiveUpCurrentTarget();
                StopMove();
                return;
            }
        }

        MoveTo(currentTarget.transform.position);
        FaceTarget(currentTarget.transform.position);
    }

    private void TickAttack(float _)
    {
        if (!IsLivingTarget(currentTarget) || !FactionRelationService.CanAttack(item, currentTarget))
        {
            attack.StopWindow();
            StopMove();
            return;
        }

        Vector3 targetPosition = currentTarget.transform.position;
        FaceTarget(targetPosition, true);
        float distance = DistanceTo(currentTarget.transform);
        if (distance <= attackTriggerDistance || attack.IsAttackLocked)
        {
            StopMove();
            if (!attack.IsAttackLocked && attack.IsCooldownDone)
            {
                attack.StartWindow(
                    _animator,
                    animAttack,
                    WorldTopologyRuntime.ShortestDelta(transform.position, targetPosition));
            }
        }
        else
        {
            attack.StopWindow();
            StopMove();
        }
    }

    #endregion

    #region 目标与饥饿

    private bool IsHungry => Data.HungerRemaining <= 0f;

    private float GetPerceptionRange()
    {
        return IsHungry
            ? Mathf.Max(0.1f, perceptionRangeHungry)
            : Mathf.Max(0.1f, perceptionRangeSatiated);
    }

    private void RefreshLockedTarget()
    {
        if (lockedTarget == null)
        {
            if (lockedTargetIsRabbit)
                RestoreHungerAfterRabbit();

            lockedTargetIsRabbit = false;
            currentTarget = null;
            return;
        }

        if (!IsLivingTarget(lockedTarget))
        {
            bool rabbitWasCaught = lockedTargetIsRabbit;
            ClearTargetState();
            if (rabbitWasCaught)
                RestoreHungerAfterRabbit();
            return;
        }

        if (!FactionRelationService.CanAttack(item, lockedTarget) ||
            DistanceTo(lockedTarget.transform) > Mathf.Max(0.1f, chaseLossDistance))
        {
            ClearTargetState();
            return;
        }

        currentTarget = lockedTarget;
    }

    private void AcquireNearestTarget()
    {
        List<Item> detectedItems = _detector?.CurrentItemsInArea;
        if (detectedItems == null || detectedItems.Count == 0)
        {
            currentTarget = null;
            return;
        }

        Item nearest = null;
        float nearestDistanceSqr = float.MaxValue;
        float perceptionRange = GetPerceptionRange();
        for (int i = 0; i < detectedItems.Count; i++)
        {
            Item candidate = detectedItems[i];
            if (!IsLivingTarget(candidate) || !FactionRelationService.CanAttack(item, candidate))
                continue;

            bool player = IsPlayerTarget(candidate);
            bool rabbit = IsRabbitTarget(candidate);
            if (!player && (!rabbit || !IsHungry))
                continue;

            if (!IsWithinEffectivePerceptionRange(candidate, perceptionRange))
                continue;

            float distanceSqr = WorldTopologyRuntime.SqrDistance(
                transform.position,
                candidate.transform.position);
            if (distanceSqr >= nearestDistanceSqr)
                continue;

            nearest = candidate;
            nearestDistanceSqr = distanceSqr;
        }

        if (nearest == null)
        {
            currentTarget = null;
            return;
        }

        currentTarget = nearest;
        lockedTarget = nearest;
        lockedTargetIsRabbit = IsRabbitTarget(nearest);
        rabbitChaseElapsed = 0f;
    }

    private void GiveUpCurrentTarget()
    {
        ClearTargetState();
        preyReacquireTimer = Mathf.Max(preyReacquireTimer, preyReacquireDelay);
    }

    private void ClearTargetState()
    {
        currentTarget = null;
        lockedTarget = null;
        lockedTargetIsRabbit = false;
        rabbitChaseElapsed = 0f;
    }

    private void RestoreHungerAfterRabbit()
    {
        Data.HungerRemaining = GetDayLength();
        if (debugLog)
            Debug.Log($"[SnowLeopardAI] {name} 捕食兔子，恢复一天饥饿计时。", this);
    }

    private float GetDayLength()
    {
        const float fallbackDayLength = 1440f;
        if (DayTimeSystem.Instance != null &&
            DayTimeSystem.Instance.WorldTimeDict.TryGetValue(
                gameObject.scene.name,
                out TimeData timeData) &&
            timeData != null)
        {
            return Mathf.Max(1f, timeData.DayLength);
        }

        return fallbackDayLength;
    }

    private bool IsLivingTarget(Item target)
    {
        if (target == null || target == item)
            return false;

        DamageReceiver receiver = target.itemMods?.GetMod_ByID<DamageReceiver>(ModText.Hp);
        return receiver != null && receiver.Hp > 0f;
    }

    private static bool IsPlayerTarget(Item target)
    {
        if (target is Player || target.CompareTag("Player"))
            return true;

        List<string> tags = target.itemData?.Tags;
        if (tags == null)
            return false;

        for (int i = 0; i < tags.Count; i++)
        {
            if (string.Equals(tags[i], "Player", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsRabbitTarget(Item target)
    {
        return string.Equals(
            target?.itemData?.IDName,
            "Rabbit",
            StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 动画与调试

    protected override string GetAnimationNameForState(SnowLeopardState state)
    {
        return state switch
        {
            SnowLeopardState.Idle => animIdle,
            SnowLeopardState.Move => animMove,
            SnowLeopardState.Alert => animAlert,
            SnowLeopardState.Chase => animChase,
            SnowLeopardState.Attack => animAttack,
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    protected override string GetStateTextCN(SnowLeopardState state)
    {
        return state switch
        {
            SnowLeopardState.Idle => "待机",
            SnowLeopardState.Move => "闲逛",
            SnowLeopardState.Alert => "警觉",
            SnowLeopardState.Chase => "追击",
            SnowLeopardState.Attack => "攻击",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    #endregion
}
