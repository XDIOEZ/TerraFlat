using System;
using System.Collections.Generic;
using UnityEngine;

#region Actor 能力上下文

/// <summary>行为树节点读取的 Actor 能力入口；运行态由 Blackboard 保存，物种参数由 Actor JSON 提供。</summary>
public sealed partial class AnimalBehaviorTreeContext
{
    public Item Actor { get; } // 当前行为树所属实体。
    public Mod_Mover_AI Mod_Mover { get; } // 通用移动能力。
    public Mod_ItemDetector Detector { get; } // 通用感知能力。
    public Mod_DamageReceiver Health { get; } // 通用生命能力。
    public Mod_Food Food { get; } // 通用营养能力。
    public Mod_AnimatorController Animator { get; } // 结构动画能力。
    public float StateElapsed { get => _blackboard.StateElapsed; set => _blackboard.StateElapsed = value; } // 当前状态时长。
    public Vector3 Position => Actor.transform.position;
    public bool DamagedSinceStateEnter => _blackboard.DamagedSinceStateEnter; // 当前状态受击事实。
    public bool HasRecentDamageThreat => _blackboard.HasRecentDamageThreat; // 近期受击来源是否有效。
    public bool ForageSatisfied { get => _blackboard.ForageSatisfied; internal set => _blackboard.ForageSatisfied = value; }
    public bool ForageAvailable { get => _blackboard.ForageAvailable; internal set => _blackboard.ForageAvailable = value; }
    public float RecentDamageRemaining => _blackboard.RecentDamageRemaining; // 可存档的受击记忆时长。
    public Vector3 RecentDamageOrigin => _blackboard.RecentDamageOrigin; // 可存档的受击来源。
    public event Action Damaged; // 通知当前节点重规划并提前评估状态。

    private readonly AnimalBehaviorTreeBlackboard _blackboard; // 可存档事实和计时器。
    private readonly AnimalBehaviorTreePerception _perception; // 目标感知与刷新。
    private bool _usesSharedPerceptionUpdates = true;
    private readonly float _damageThreatMemoryDuration; // 受击来源记忆时长。
    private Mod_DamageReceiver _damageEventSource; // 当前受击事件来源。
    private Mod_TurnBack _turnBody; // 通用朝向模块。

    public AnimalBehaviorTreeContext(
        Item actor,
        Mod_Mover_AI mover,
        Mod_ItemDetector detector,
        Mod_DamageReceiver health,
        Mod_Food food,
        Mod_AnimatorController animator,
        float damageThreatMemoryDuration,
        float detectorRefreshInterval,
        IReadOnlyDictionary<string, float> savedTimers,
        float savedStateElapsed,
        float savedDamageRemaining,
        Vector3 savedDamageOrigin)
    {
        Actor = actor ?? throw new ArgumentNullException(nameof(actor));
        Mod_Mover = mover;
        Detector = detector;
        Health = health;
        Food = food;
        Animator = animator;
        _blackboard = new AnimalBehaviorTreeBlackboard(
            savedTimers, savedStateElapsed, savedDamageRemaining, savedDamageOrigin);
        _perception = new AnimalBehaviorTreePerception(actor, detector, detectorRefreshInterval);
        _damageThreatMemoryDuration = Mathf.Max(0f, damageThreatMemoryDuration);
        _turnBody = actor.GetComponentInChildren<Mod_TurnBack>(true);
        BindDamageEvents();
    }

    /// <summary>从当前感知快照中按配置查找最近、可见且位于目标倍率范围内的威胁。</summary>
    public Item FindClosestThreat(float distance, string[] tags, bool includePlayers)
    {
        return _perception.FindClosestThreat(distance, tags, includePlayers);
    }

    /// <summary>按标签和有效感知距离查找最近物品目标。</summary>
    public Item FindClosestTaggedItem(float distance, string[] tags)
    {
        return _perception.FindClosestTaggedFood(distance, tags);
    }

    /// <summary>在威胁筛选后按阵营和生命值选择可攻击目标，避免友方遮挡更远的敌人。</summary>
    public Item FindClosestAttackTarget(float distance, string[] tags, bool includePlayers)
    {
        return _perception.FindClosestAttackTarget(distance, tags, includePlayers);
    }

    /// <summary>读取当前营养比例。</summary>
    public float GetNutritionRate()
    {
        return Food?.Data?.nutrition == null
            ? 0f
            : Mathf.Clamp01(Food.Data.nutrition.GetFoodRate());
    }

    /// <summary>进入新状态时清除仅属于上一状态的瞬时事实。</summary>
    public void BeginState()
    {
        _blackboard.BeginState();
    }

    /// <summary>推进行为树共享计时器和受击记忆。</summary>
    public void Tick(float deltaTime)
    {
        StateElapsed += Mathf.Max(0f, deltaTime);
        float step = Mathf.Max(0f, deltaTime);
        _blackboard.Tick(step, null);
        if (_usesSharedPerceptionUpdates) _perception.Tick(step);


        for (int i = 0; i < _capabilityList.Count; i++) _capabilityList[i].Tick(step);
    }

    public void DisableSharedPerceptionUpdates() => _usesSharedPerceptionUpdates = false;

    /// <summary>写入或刷新一个可存档计时器。</summary>
    public void SetTimer(string key, float seconds)
    {
        _blackboard.SetTimer(key, seconds);
    }

    public bool IsTimerElapsed(string key)
    {
        return _blackboard.IsTimerElapsed(key);
    }

    public float GetTimerRemaining(string key)
    {
        return _blackboard.GetTimerRemaining(key);
    }

    public Dictionary<string, float> ExportTimers()
    {
        return _blackboard.ExportTimers();
    }

    /// <summary>读取当前世界昼夜比例。</summary>
    public bool IsNightTime(float dayStartRatio, float dayEndRatio)
    {
        DayTimeSystem timeSystem = DayTimeSystem.Instance;
        if (timeSystem == null ||
            !timeSystem.WorldTimeDict.TryGetValue(Actor.gameObject.scene.name, out TimeData timeData) ||
            timeData == null)
        {
            return false;
        }

        float dayLength = Mathf.Max(1f, timeData.DayLength);
        float normalized = Mathf.Repeat(timeSystem.GetCurrentTime(Actor.gameObject.scene.name), dayLength) / dayLength;
        return normalized < dayStartRatio || normalized > dayEndRatio;
    }

    /// <summary>优先返回近期受击来源位置，供逃离节点在攻击者脱离 Detector 后继续撤离。</summary>
    public bool TryGetRecentDamageOrigin(out Vector3 origin)
    {
        return _blackboard.TryGetRecentDamageOrigin(out origin);
    }

    /// <summary>立即面向目标；攻击节点不修改移动目标。</summary>
    public void FaceTarget(Vector3 targetPosition, bool immediate)
    {
        if (_turnBody == null)
            return;

        Vector2 direction = WorldTopologyRuntime.ShortestDelta(Position, targetPosition);
        if (direction.sqrMagnitude < 0.0001f)
            return;
        direction.Normalize();

        if (immediate)
            _turnBody.ResetTurnState();
        _turnBody.TurnBodyToDirection(direction);
        if (immediate)
        {
            _turnBody.UpdateTurn(float.MaxValue);
            _turnBody.UpdateAllTransformDirections();
        }
    }

    public bool IsLivingAttackTarget(Item target)
    {
        return _perception.IsLivingAttackTarget(target);
    }

    /// <summary>释放本只生物的能力与受击订阅。</summary>
    public void Dispose()
    {
        for (int i = _capabilityList.Count - 1; i >= 0; i--) _capabilityList[i].Dispose();
        _capabilityList.Clear();
        _capabilities.Clear();
        if (_damageEventSource != null)
            _damageEventSource.OnDamageReceived -= HandleDamageReceived;
        _damageEventSource = null;
    }

    private void BindDamageEvents()
    {
        if (Health == null)
            return;
        _damageEventSource = Health;
        _damageEventSource.OnDamageReceived += HandleDamageReceived;
    }

    private void HandleDamageReceived(DamageReceiverDamageInfo info)
    {
        if (info == null || info.DamageValue <= 0f || info.Attacker == Actor)
            return;

        bool hasOrigin = DamageThreatOrigin.TryResolve(info, out Vector2 origin);
        _blackboard.RecordDamage(_damageThreatMemoryDuration, hasOrigin, origin);
        Damaged?.Invoke();
    }

}

#endregion
