using System;
using System.Collections.Generic;
using FlatWorld.Combat;
using MemoryPack;
using UnityEngine;

public enum BearTerritorialState { Idle, Move, Chase, Attack }

/// <summary>熊平静时闲逛，只对持续靠近或真实命中的对象按独立仇恨反击。</summary>
public partial class AI_BearTerritorialRetaliation : AI_Base<BearTerritorialState>
{
    #region 保存与运行状态
    [Serializable, MemoryPackable]
    public partial class BearTerritorialSaveData
    {
        public BearTerritorialState State;
    }

    public BearTerritorialSaveData Data = new();
    private readonly AIProximityHatredLedger<CombatIdentity> hatredLedger = new();
    private readonly Dictionary<CombatIdentity, Item> trackedTargets = new();
    private readonly List<CombatIdentity> invalidTargets = new();
    private readonly AI_AttackController attack = new();
    private Predicate<CombatIdentity> eligibleTargetPredicate;
    private Mod_DamageReceiver confirmedHitSource;
    private Item currentThreat;

    internal Item CurrentThreat => currentThreat;
    internal float CurrentHatred => currentThreat != null ? hatredLedger.GetHatred(GameplayCombatBridge.Identity(currentThreat)) : 0f;
    internal BearTerritorialState CurrentBehaviorState => _currentState;
    internal AIProximityHatredLedger<CombatIdentity> HatredLedger => hatredLedger;
    #endregion

    #region 熊的行为配置
    public bool debugLog;
    [Min(0.05f)] public float detectorRefreshInterval = 0.5f;
    [Min(0.1f)] public float territorialPresenceRadius = 8f;
    [Min(0f)] public float territorialPresenceThresholdSeconds = 60f;
    [Min(0f)] public float territorialInitialHatred = 120f;
    [Min(0f)] public float hatredDecayPerSecond = 1f;
    [Min(0f)] public float hatredPerConfirmedHit = 30f;
    [Min(0.1f)] public float chasePerceptionDistance = 30f;
    [Min(1)] public int chasePathCostLimit = 300;
    [Min(0.1f)] public float chasePathRetryDelay = 3f;
    [Min(0.1f)] public float attackTriggerDistance = 1.8f;
    [Min(0f)] public float attackCooldown = 1.8f;
    [Min(0.01f)] public float attackDamageWindow = 0.2f;
    [Min(0f)] public float attackWindupDuration = 0.4f;
    [Min(0f)] public float attackRecoveryDuration = 0.3f;
    public bool enableWander = true;
    [Min(0.1f)] public float wanderRadius = 5f;
    [Min(0.05f)] public float wanderStopDistance = 0.4f;
    [Min(0f)] public float idleMinDuration = 1f;
    [Min(0f)] public float idleMaxDuration = 3f;
    [Min(0f)] public float wanderPauseMin = 1f;
    [Min(0f)] public float wanderPauseMax = 3f;
    public string animIdle = "Idle";
    public string animMove = "Move";
    public string animAttack = "Attack";

    protected override AI_WanderConfig WanderConfig => new()
    {
        enabled = enableWander, radius = wanderRadius, stopDistance = wanderStopDistance,
        pauseMin = wanderPauseMin, pauseMax = wanderPauseMax,
        avoidHighPenalty = true, dangerPenalty = 1200, sampleCount = 8, penaltyWeight = 1f
    };
    protected override AI_IdleConfig IdleConfig => new() { minDuration = idleMinDuration, maxDuration = idleMaxDuration };
    protected override float DetectorRefreshInterval => detectorRefreshInterval;
    protected override bool DebugLogEnabled => debugLog;
    protected override bool IsMoveState(BearTerritorialState state) => state == BearTerritorialState.Move || state == BearTerritorialState.Chase;
    protected override bool IsIdleState(BearTerritorialState state) => state == BearTerritorialState.Idle;
    protected override bool IsAmbientAudioState(BearTerritorialState state) => state == BearTerritorialState.Idle || state == BearTerritorialState.Move;
    #endregion

    #region 装卸与受击订阅
    protected override void OnLoad()
    {
        ModData.ReadData(ref Data);
        Data ??= new BearTerritorialSaveData();
        _currentState = BearTerritorialState.Idle;
        InitializeAI();
    }

    protected override void OnSave()
    {
        Data.State = _currentState;
        ModData.WriteData(Data);
    }

    protected override void OnBindExtraModules()
    {
        if (_detector != null)
            _detector.DetectionRadius = Mathf.Max(territorialPresenceRadius, chasePerceptionDistance);
        attack.Bind(item);
        if (confirmedHitSource != null) confirmedHitSource.OnHitReceived -= HandleConfirmedHit;
        confirmedHitSource = _hp;
        if (confirmedHitSource != null) confirmedHitSource.OnHitReceived += HandleConfirmedHit;
    }

    protected override void OnResetRuntimeState()
    {
        hatredLedger.Clear();
        hatredLedger.Configure(territorialPresenceThresholdSeconds, territorialInitialHatred, hatredDecayPerSecond, hatredPerConfirmedHit);
        trackedTargets.Clear();
        invalidTargets.Clear();
        currentThreat = null;
        eligibleTargetPredicate ??= IsEligibleVisibleHatredTarget;
        attack.Reset();
        attack.Cooldown = attackCooldown;
        attack.DamageWindow = attackDamageWindow;
        attack.WindupDuration = attackWindupDuration;
        attack.RecoveryDuration = attackRecoveryDuration;
    }

    protected override void OnUnload()
    {
        if (confirmedHitSource != null) confirmedHitSource.OnHitReceived -= HandleConfirmedHit;
        confirmedHitSource = null;
        attack.Reset();
        hatredLedger.Clear();
        trackedTargets.Clear();
        currentThreat = null;
        base.OnUnload();
    }

    internal void HandleConfirmedHit(DamageReceiverDamageInfo info)
    {
        if (info == null || info.DamageValue < 0f || !IsAlive) return;
        Item attacker = ResolveRetaliationCreditActor(info);
        if (!IsValidLivingTarget(attacker)) return;
        CombatIdentity identity = GameplayCombatBridge.Identity(attacker);
        if (!identity.IsValid) return;
        trackedTargets[identity] = attacker;
        hatredLedger.RecordHit(identity);
        OnPreEvaluate();
    }

    // 优先核对命中时冻结的归属身份，避免弹体回收或换持有者后向另一实体记仇。
    internal static Item ResolveRetaliationCreditActor(DamageReceiverDamageInfo info)
    {
        if (info == null || info.Context.Attack.Source.Backend == CombatBackend.Environment) return null;
        Item attacker = info.Attacker;
        if (info.Context.Credit.IsValid)
        {
            for (int depth = 0; attacker != null && depth < 8; depth++)
            {
                if (GameplayCombatBridge.Identity(attacker) == info.Context.Credit) return attacker;
                if (attacker.Owner == attacker) break;
                attacker = attacker.Owner;
            }
            Item creditedActor = ItemMgr.GetInstance()?.GetItemByGuid(unchecked((int)info.Context.Credit.Value));
            return creditedActor != null && GameplayCombatBridge.Identity(creditedActor) == info.Context.Credit ? creditedActor : null;
        }
        for (int depth = 0; attacker?.Owner != null && attacker.Owner != attacker && depth < 8; depth++)
            attacker = attacker.Owner;
        return attacker;
    }
    #endregion

    #region 独立驻留计时和仇恨选择
    protected override void UpdateExtraTimers(float deltaTime)
    {
        hatredLedger.BeginUpdate(Mathf.Max(0f, deltaTime));
        RemoveInvalidTrackedTargets();
        ObserveNearbyTerritorialPlayers();
        hatredLedger.EndUpdate();
        RemoveExpiredTrackedTargets();
        // 在前摇可能开伤害窗前复核仇恨与视野，当帧脱战不能再补一次命中。
        if (currentThreat != null && !IsEligibleVisibleHatredTarget(GameplayCombatBridge.Identity(currentThreat)))
        {
            attack.StopWindow();
            currentThreat = null;
        }
        attack.Tick(deltaTime);
    }

    protected virtual void ObserveNearbyTerritorialPlayers()
    {
        ItemMgr manager = ItemMgr.GetInstance();
        if (manager == null) return;
        // 驻留读取已注册的真实玩家，遮挡只影响出手，不会抹掉周围玩家的连续停留。
        foreach (Player player in manager.Player_DIC.Values)
            ObserveNearbyTerritorialPlayer(player);
    }

    internal bool ObserveNearbyTerritorialPlayer(Item target)
    {
        if (!IsValidLivingTarget(target) ||
            !(target is Player || target.itemData.Tags?.ContainsTag(Tag.Player) == true)) return false;
        float radius = Mathf.Min(Mathf.Max(0f, territorialPresenceRadius), item.MaximumPerceptionRadius);
        if (WorldTopologyRuntime.SqrDistance(transform.position, target.transform.position) > radius * radius) return false;
        CombatIdentity identity = GameplayCombatBridge.Identity(target);
        if (!identity.IsValid) return false;
        trackedTargets[identity] = target;
        hatredLedger.ObserveNearbyPlayer(identity);
        return true;
    }

    protected override void OnPreEvaluate()
    {
        // 完整攻击动作沿用起手目标，动作完成后再按当前最高仇恨换敌。
        if (attack.IsAttackLocked && currentThreat != null && IsEligibleVisibleHatredTarget(GameplayCombatBridge.Identity(currentThreat))) return;
        currentThreat = hatredLedger.TryGetHighestHatredTarget(eligibleTargetPredicate, out CombatIdentity identity)
            ? trackedTargets[identity] : null;
    }

    private bool IsValidLivingTarget(Item target)
    {
        if (item == null || target == null || target == item || target.itemData == null ||
            target.DestructionHandled || target.IsUnloadingModules || !target.gameObject.activeInHierarchy ||
            target.gameObject.scene != item.gameObject.scene || !FactionRelationService.CanAttack(item, target)) return false;
        Mod_DamageReceiver health = target.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
        return health != null && health.Hp > 0f;
    }

    private bool IsEligibleVisibleHatredTarget(CombatIdentity identity)
    {
        return trackedTargets.TryGetValue(identity, out Item target) && IsValidLivingTarget(target) &&
               GameplayCombatBridge.Identity(target) == identity && hatredLedger.GetHatred(identity) > 0f &&
               !IsChaseTargetBlockedByPathCost(target) && IsWithinEffectivePerceptionRange(target, chasePerceptionDistance);
    }

    private void RemoveInvalidTrackedTargets()
    {
        invalidTargets.Clear();
        foreach (KeyValuePair<CombatIdentity, Item> pair in trackedTargets)
            if (!IsValidLivingTarget(pair.Value) || GameplayCombatBridge.Identity(pair.Value) != pair.Key)
                invalidTargets.Add(pair.Key);
        for (int index = 0; index < invalidTargets.Count; index++)
        {
            hatredLedger.Remove(invalidTargets[index]);
            trackedTargets.Remove(invalidTargets[index]);
        }
    }

    private void RemoveExpiredTrackedTargets()
    {
        invalidTargets.Clear();
        foreach (CombatIdentity identity in trackedTargets.Keys)
            if (!hatredLedger.Contains(identity)) invalidTargets.Add(identity);
        for (int index = 0; index < invalidTargets.Count; index++) trackedTargets.Remove(invalidTargets[index]);
    }
    #endregion

    #region 通用移动与攻击状态接入
    protected override BearTerritorialState EvaluateNextState()
    {
        if (currentThreat != null)
        {
            if (attack.IsAttackLocked || attack.IsCooldownDone && DistanceTo(currentThreat.transform) <= attackTriggerDistance)
                return BearTerritorialState.Attack;
            return BearTerritorialState.Chase;
        }
        return ShouldMoveBase() ? BearTerritorialState.Move : BearTerritorialState.Idle;
    }

    protected override void ConfigureStateNodes(AIStateMachine<BearTerritorialState> stateMachine)
    {
        RegisterLocomotionStateNodes(stateMachine, BearTerritorialState.Idle, BearTerritorialState.Move);
        stateMachine.Register(CreateMovingStateNode(BearTerritorialState.Chase, TickHatredChase));
        stateMachine.Register(CreateStateNode(BearTerritorialState.Attack, TickRetaliationAttack));
    }

    protected override void OnBeforeSwitchState(BearTerritorialState previous, BearTerritorialState next)
    {
        if (previous == BearTerritorialState.Attack && next != BearTerritorialState.Attack) attack.OnExitAttackState();
        if (next == BearTerritorialState.Attack) attack.OnEnterAttackState();
    }

    private void TickHatredChase(float deltaTime)
    {
        if (currentThreat == null || !IsEligibleVisibleHatredTarget(GameplayCombatBridge.Identity(currentThreat)))
        {
            StopMove();
            return;
        }
        WorldNavigationDestinationResult result = MoveToChaseTarget(currentThreat.transform.position, chasePathCostLimit, attackTriggerDistance);
        if (TryHandleRejectedChasePath(result, currentThreat, chasePathRetryDelay)) return;
        FaceTarget(currentThreat.transform.position);
    }

    private void TickRetaliationAttack(float deltaTime)
    {
        if (currentThreat == null || !IsEligibleVisibleHatredTarget(GameplayCombatBridge.Identity(currentThreat)))
        {
            attack.StopWindow();
            StopMove();
            return;
        }
        FaceTarget(currentThreat.transform.position, true);
        if (attack.IsAttackLocked) StopMove();
        else if (attack.IsCooldownDone && DistanceTo(currentThreat.transform) <= attackTriggerDistance)
        {
            StopMove();
            attack.StartWindow(_animator, animAttack, WorldTopologyRuntime.ShortestDelta(transform.position, currentThreat.transform.position));
        }
        else TickHatredChase(deltaTime);
    }

    protected override string GetAnimationNameForState(BearTerritorialState state) => state switch
    {
        BearTerritorialState.Attack => animAttack,
        BearTerritorialState.Move or BearTerritorialState.Chase => animMove,
        _ => animIdle
    };
    protected override string GetStateTextCN(BearTerritorialState state) => state switch
    {
        BearTerritorialState.Move => "闲逛", BearTerritorialState.Chase => "追击",
        BearTerritorialState.Attack => "攻击", _ => "待机"
    };
    #endregion
}
