using System;
using System.Collections.Generic;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;
using UltEvents;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum WolfState
{
    Idle,
    Move,
    Alert,
    Chase,
    Attack,
    Avoid,
    Flee
}

/// <summary>
/// 狼 AI：支持群体协作（呼叫同伴、集火）、攻击伤害窗口、逃跑/避让等行为。
/// 状态优先级：逃跑 > 攻击 > 追击 > 避让 > 警觉 > 移动 > 待机
/// </summary>
public partial class AI_Wolf : AI_Base<WolfState>
{
#region SaveData
	[Serializable]
	[MemoryPackable]
	public partial class AI_WolfSaveData
	{
		public WolfState State = WolfState.Idle;
		public float Fatigue01 = 0f;
	}
#endregion

#region ModuleData
	public AI_WolfSaveData Data = new AI_WolfSaveData();
#endregion

#region RuntimeState - Wolf 特有
	[SerializeField, ReadOnly]
	private Item _currentThreat;

	[SerializeField, ReadOnly]
	private int _packCount = 1;

	[SerializeField, ReadOnly]
	private bool _isAlphaWolf = true;

	[SerializeField, ReadOnly]
	private AI_Wolf _alphaWolf;

	private float _alertTimer;
	private float _packAssistTimer;
	private float _packCallCooldownTimer;
	private Vector3 _packCenter;
	private bool _hasPackMate;
	private AI_AttackController _attack = new AI_AttackController();
#endregion

#region Config
	[TabGroup("配置", "调试"), HideLabel]
	public bool debugLog;

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/识别"), LabelText("同伴标签")]
	public List<string> wolfTags = new List<string> { "Wolf" };

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/识别"), LabelText("玩家标签")]
	public List<string> playerTags = new List<string> { "Player" };

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/识别"), LabelText("检测间隔"), SuffixLabel("秒", true), MinValue(0.05f)]
	public float detectorRefreshInterval = 0.6f;

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/协作"), LabelText("同伴响应范围"), SuffixLabel("米", true), MinValue(0.1f)]
	public float allyCallDistance = 15f;

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/协作"), LabelText("同伴支援时长"), SuffixLabel("秒", true), MinValue(0.1f)]
	public float packAssistDuration = 5f;

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/协作"), LabelText("呼叫冷却"), SuffixLabel("秒", true), MinValue(0f)]
	public float packCallCooldown = 0.8f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), HorizontalGroup("配置/行为/战斗/Hr1"), LabelText("警觉距离"), SuffixLabel("米", true), MinValue(0.1f)]
	public float alertDetectDistance = 10f;
	[HorizontalGroup("配置/行为/战斗/Hr1"), LabelText("追击触发"), SuffixLabel("米", true), MinValue(0.1f)]
	public float chaseTriggerDistance = 14f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), HorizontalGroup("配置/行为/战斗/Hr2"), LabelText("攻击距离"), SuffixLabel("米", true), MinValue(0.1f)]
	public float attackTriggerDistance = 1.4f;
	[HorizontalGroup("配置/行为/战斗/Hr2"), LabelText("追击放弃"), SuffixLabel("米", true), MinValue(0.1f)]
	public float chaseLossDistance = 22f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), HorizontalGroup("配置/行为/战斗/Hr3"), LabelText("攻击冷却"), SuffixLabel("秒", true), MinValue(0f)]
	public float attackCooldown = 2f;
	[HorizontalGroup("配置/行为/战斗/Hr3"), LabelText("伤害窗口"), SuffixLabel("秒", true), MinValue(0.01f)]
	public float attackDamageWindow = 0.2f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), LabelText("警觉维持时长"), SuffixLabel("秒", true), MinValue(0f)]
	public float alertKeepDuration = 2f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/逃跑"), HorizontalGroup("配置/行为/逃跑/Hr1"), LabelText("触发血量(双狼)"), Range(0f, 1f)]
	public float fleeTriggerHpRate = 0.22f;
	[HorizontalGroup("配置/行为/逃跑/Hr1"), LabelText("安全血量(双狼)"), Range(0f, 1f)]
	public float fleeSafeHpRate = 0.45f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/逃跑"), LabelText("远离玩家距离"), SuffixLabel("米", true), MinValue(1f)]
	public float avoidRunDistance = 9f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/逃跑"), LabelText("逃跑距离"), SuffixLabel("米", true), MinValue(1f)]
	public float fleeRunDistance = 12f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/巡逻"), LabelText("启用闲逛")]
	public bool enableWander = true;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/巡逻"), HorizontalGroup("配置/行为/巡逻/Hr1"), LabelText("待机最小"), SuffixLabel("秒", true), MinValue(0f)]
	public float idleMinDuration = 0.8f;
	[HorizontalGroup("配置/行为/巡逻/Hr1"), LabelText("待机最大"), SuffixLabel("秒", true), MinValue(0f)]
	public float idleMaxDuration = 2.2f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/巡逻"), HorizontalGroup("配置/行为/巡逻/Hr2"), LabelText("闲逛半径"), SuffixLabel("米", true), MinValue(0.1f)]
	public float wanderRadius = 5f;
	[HorizontalGroup("配置/行为/巡逻/Hr2"), LabelText("到达距离"), SuffixLabel("米", true), MinValue(0.05f)]
	public float wanderStopDistance = 0.4f;
	[TabGroup("配置", "行为"), BoxGroup("配置/行为/巡逻"), LabelText("聚拢权重"), Range(0f, 2f)]
	public float wanderCohesionWeight = 0.65f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/巡逻"), HorizontalGroup("配置/行为/巡逻/Hr3"), LabelText("停顿最小"), SuffixLabel("秒", true), MinValue(0f)]
	public float wanderPauseMin = 0.8f;
	[HorizontalGroup("配置/行为/巡逻/Hr3"), LabelText("停顿最大"), SuffixLabel("秒", true), MinValue(0f)]
	public float wanderPauseMax = 2.5f;
	[TabGroup("配置", "行为"), BoxGroup("配置/行为/巡逻"), HorizontalGroup("配置/行为/巡逻/H避险"), LabelText("避开高权重")]
	public bool wanderAvoidHighPenalty = true;
	[HorizontalGroup("配置/行为/巡逻/H避险"), LabelText("危险阈值"), MinValue(0)]
	public int wanderDangerPenalty = 1200;
	[HorizontalGroup("配置/行为/巡逻/H避险"), LabelText("采样点"), MinValue(1)]
	public int wanderSampleCount = 8;
	[TabGroup("配置", "行为"), BoxGroup("配置/行为/巡逻"), LabelText("权重惩罚系数"), MinValue(0f)]
	public float wanderPenaltyWeight = 1f;

	[TabGroup("配置", "动画"), BoxGroup("配置/动画/状态"), HorizontalGroup("配置/动画/状态/Hr1"), LabelText("待机")]
	public string animIdle = "Stand";
	[HorizontalGroup("配置/动画/状态/Hr1"), LabelText("移动")]
	public string animMove = "Move";
	[HorizontalGroup("配置/动画/状态/Hr1"), LabelText("警觉")]
	public string animAlert = "Stand";

	[TabGroup("配置", "动画"), BoxGroup("配置/动画/状态"), HorizontalGroup("配置/动画/状态/Hr2"), LabelText("追击")]
	public string animChase = "Run";
	[HorizontalGroup("配置/动画/状态/Hr2"), LabelText("攻击")]
	public string animAttack = "Attack";
	[HorizontalGroup("配置/动画/状态/Hr2"), LabelText("逃离")]
	public string animAvoid = "Run";

	[TabGroup("配置", "动画"), BoxGroup("配置/动画/状态"), LabelText("残血逃跑")]
	public string animFlee = "Run";
#endregion

#region Base Overrides - Config Accessors
	protected override AI_WanderConfig WanderConfig => new AI_WanderConfig
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

	protected override AI_IdleConfig IdleConfig => new AI_IdleConfig
	{
		minDuration = idleMinDuration,
		maxDuration = idleMaxDuration
	};

	protected override float DetectorRefreshInterval => detectorRefreshInterval;
	protected override bool DebugLogEnabled => debugLog;
	protected override bool IsMoveState(WolfState state) => state == WolfState.Move;
	protected override bool IsIdleState(WolfState state) => state == WolfState.Idle;
#endregion

#region Lifecycle
	public override void Load()
	{
		ModData.ReadData(ref Data);
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

#region Base Overrides - Hooks
	protected override void OnResetRuntimeState()
	{
		_alertTimer = 0f;
		_packAssistTimer = 0f;
		_packCallCooldownTimer = 0f;
		_packCount = 1;
		_hasPackMate = false;
		_isAlphaWolf = true;
		_alphaWolf = this;
		_packCenter = transform.position;
		_currentThreat = null;
		_attack.Reset();
		_attack.Cooldown = attackCooldown;
		_attack.DamageWindow = attackDamageWindow;
	}

	protected override void OnBindExtraModules()
	{
		_attack.Bind(item);
	}

	protected override void OnValidateExtraModules()
	{
		if (!_attack.HasDamageMods)
		{
			Debug.LogWarning($"[{nameof(AI_Wolf)}] 未找到 Mod_Damage 组件，攻击不会造成伤害。目标物体: {name}", this);
		}
	}

	protected override void UpdateExtraTimers(float deltaTime)
	{
		_alertTimer = DecrementTimer(_alertTimer, deltaTime);
		_packAssistTimer = DecrementTimer(_packAssistTimer, deltaTime);
		_packCallCooldownTimer = DecrementTimer(_packCallCooldownTimer, deltaTime);
		_attack.Update(deltaTime);
	}

	protected override void OnPreEvaluate()
	{
		RefreshPackStatus();
		RefreshThreatTarget();
	}

	protected override void OnBeforeSwitchState(WolfState previous, WolfState next)
	{
		// 离开战斗相关状态时，若支援计时器已过期则清除威胁目标
		if (next != WolfState.Alert && next != WolfState.Chase
		    && next != WolfState.Attack && next != WolfState.Avoid && next != WolfState.Flee)
		{
			if (_packAssistTimer <= 0f)
			{
				_currentThreat = null;
			}
		}

		// 离开攻击状态：停止伤害窗口，进入冷却
		if (previous == WolfState.Attack && next != WolfState.Attack)
		{
			_attack.OnExitAttackState();
		}

		// 进入攻击状态：重置窗口触发标记
		if (next == WolfState.Attack)
		{
			_attack.OnEnterAttackState();
		}
	}

	/// <summary>狼群聚拢修正：非头狼向头狼/群中心偏移</summary>
	protected override void ApplyWanderOffsetModifier(ref Vector2 offset)
	{
		if (!_hasPackMate || _isAlphaWolf || wanderCohesionWeight <= 0f)
		{
			return;
		}

		Vector3 cohesionAnchor = _alphaWolf != null ? _alphaWolf.transform.position : _packCenter;
		Vector2 toPack = (Vector2)(cohesionAnchor - transform.position);
		if (toPack.sqrMagnitude > 0.0001f)
		{
			offset += toPack.normalized * (wanderRadius * wanderCohesionWeight);
		}
	}

	protected override string GetDebugExtraInfo()
	{
		string roleText = _isAlphaWolf ? "头狼" : "跟随";
		return $" | 狼群数: {_packCount} | 角色: {roleText}";
	}
#endregion

#region PublicAPI
	[Button("狼群集火玩家")]
	public void TriggerPackAttack(Item threatSource)
	{
		if (threatSource == null)
		{
			throw new ArgumentNullException(nameof(threatSource));
		}

		_currentThreat = threatSource;
		_alertTimer = alertKeepDuration;
		_packAssistTimer = packAssistDuration;
		CallNearbyWolves(threatSource);

		if (debugLog)
		{
			Debug.Log($"[WolfAI] {name} 发起群体集火，目标={threatSource.name}", this);
		}
	}

	public void ReceivePackCall(Item threatSource, AI_Wolf caller)
	{
		if (threatSource == null)
		{
			throw new ArgumentNullException(nameof(threatSource));
		}

		if (caller == this)
		{
			return;
		}

		_currentThreat = threatSource;
		_alertTimer = Mathf.Max(_alertTimer, alertKeepDuration);
		_packAssistTimer = Mathf.Max(_packAssistTimer, packAssistDuration);

		if (debugLog)
		{
			Debug.Log($"[WolfAI] {name} 收到同伴呼叫，呼叫者={caller?.name}, 目标={threatSource.name}", this);
		}
	}
#endregion

#region StateMachine
	protected override WolfState EvaluateNextState()
	{
		if (ShouldFlee())     return WolfState.Flee;
		if (ShouldAttack())   return WolfState.Attack;
		if (ShouldChase())    return WolfState.Chase;
		if (ShouldAvoid())    return WolfState.Avoid;
		if (ShouldAlert())    return WolfState.Alert;
		if (ShouldMoveBase()) return WolfState.Move;
		return WolfState.Idle;
	}

	protected override void ConfigureStateNodes(AIStateMachine<WolfState> stateMachine)
	{
		RegisterLocomotionStateNodes(stateMachine, WolfState.Idle, WolfState.Move);
		stateMachine.Register(CreateStoppedStateNode(WolfState.Alert, _ => TickAlert()));
		stateMachine.Register(CreateMovingStateNode(WolfState.Chase, _ => TickChase()));
		stateMachine.Register(CreateStoppedActionStateNode(WolfState.Attack, _ => TickAttack()));
		stateMachine.Register(CreateMovingStateNode(WolfState.Avoid, _ => TickAvoid()));
		stateMachine.Register(CreateMovingStateNode(WolfState.Flee, _ => TickFlee()));
	}
#endregion

#region Tick - Wolf 特有状态
	private void TickAlert()
	{
		if (_currentThreat == null) return;
		FaceTarget(_currentThreat.transform.position);
	}

	private void TickChase()
	{
		if (_currentThreat == null) { StopMove(); return; }

		MoveTo(_currentThreat.transform.position);
		TryCallNearbyWolves();
	}

	private void TickAttack()
	{
		if (_currentThreat == null)
		{
			_attack.StopWindow();
			StopMove();
			return;
		}

		Vector3 targetPosition = _currentThreat.transform.position;
		FaceTarget(targetPosition, true);

		float distance = DistanceTo(_currentThreat.transform);
		if (distance <= attackTriggerDistance)
		{
			StopMove();
			// 冷却结束且未触发窗口 → 发起攻击
			if (!_attack.IsWindowTriggered && _attack.IsCooldownDone)
			{
				_attack.StartWindow(
					_animator,
					animAttack,
					targetPosition - transform.position);
			}
		}
		else
		{
			_attack.StopWindow();
			StopMove();
		}

		TryCallNearbyWolves();
	}

	private void TickAvoid()
	{
		if (_currentThreat == null) { StopMove(); return; }
		MoveAwayFrom(_currentThreat.transform.position, avoidRunDistance);
	}

	private void TickFlee()
	{
		if (_currentThreat == null) { StopMove(); return; }
		MoveAwayFrom(_currentThreat.transform.position, fleeRunDistance);
	}
#endregion

#region Conditions
	/// <summary>逃跑条件：双狼且血量低于阈值</summary>
	private bool ShouldFlee()
	{
		if (_currentThreat == null) return false;
		if (_packCount != 2) return false;

		float hpRate = GetHpRate();
		return _currentState == WolfState.Flee
			? hpRate < fleeSafeHpRate
			: hpRate < fleeTriggerHpRate;
	}

	/// <summary>攻击条件：有威胁目标且距离/群体数满足要求</summary>
	private bool ShouldAttack()
	{
		if (_currentThreat == null) return false;
		float distance = DistanceTo(_currentThreat.transform);

		if (_packCount < 2) return false;

		return distance <= attackTriggerDistance;
	}

	/// <summary>追击条件：双狼以上且在追击范围内</summary>
	private bool ShouldChase()
	{
		if (_currentThreat == null) return false;
		float distance = DistanceTo(_currentThreat.transform);

		if (_packCount >= 2)
		{
			return _currentState == WolfState.Chase
				? distance <= chaseLossDistance
				: distance <= chaseTriggerDistance;
		}
		return false;
	}

	/// <summary>避让条件：独狼在追击触发范围内</summary>
	private bool ShouldAvoid()
	{
		if (_currentThreat == null) return false;
		if (_packCount > 1) return false;
		return DistanceTo(_currentThreat.transform) <= chaseTriggerDistance;
	}

	/// <summary>警觉条件：威胁在警觉距离内，或警觉计时器未过期</summary>
	private bool ShouldAlert()
	{
		if (_currentThreat == null) return _alertTimer > 0f;

		if (DistanceTo(_currentThreat.transform) <= alertDetectDistance)
		{
			_alertTimer = alertKeepDuration;
			return true;
		}
		return _alertTimer > 0f;
	}
#endregion

#region Helpers - Wolf 特有
	private void RefreshPackStatus()
	{
		_packCount = 1;
		_hasPackMate = false;
		_packCenter = transform.position;
		_alphaWolf = this;
		_isAlphaWolf = true;

		if (_detector.CurrentItemsInArea == null) return;

		int allyCount = 0;
		Vector3 allyPosSum = Vector3.zero;
		int alphaPriority = GetInstanceID();

		foreach (Item it in _detector.CurrentItemsInArea)
		{
			if (!TryGetWolfAlly(it, out AI_Wolf ally)) continue;

			_packCount++;
			allyCount++;
			allyPosSum += ally.transform.position;

			int allyPriority = ally.GetInstanceID();
			if (allyPriority < alphaPriority)
			{
				alphaPriority = allyPriority;
				_alphaWolf = ally;
			}
		}

		if (allyCount > 0)
		{
			_hasPackMate = true;
			_packCenter = allyPosSum / allyCount;
			_isAlphaWolf = _alphaWolf == this;
		}
	}

	private void RefreshThreatTarget()
	{
		Item nearestPlayer = FindClosestPlayerThreat();

		if (nearestPlayer != null)
		{
			_currentThreat = nearestPlayer;
			return;
		}

		if (_currentThreat == null) return;
		if (_packAssistTimer > 0f) return;
		_currentThreat = null;
	}

	private Item FindClosestPlayerThreat()
	{
		List<Item> allItems = _detector.CurrentItemsInArea;
		if (allItems == null || allItems.Count == 0) return null;

		Item closest = null;
		float closestDistance = float.MaxValue;

		foreach (Item it in allItems)
		{
			if (it == null || !IsPlayerThreat(it)) continue;
			float distance = DistanceTo(it.transform);
			if (distance < closestDistance)
			{
				closest = it;
				closestDistance = distance;
			}
		}
		return closest;
	}

	private bool IsPlayerThreat(Item target)
	{
		if (target == null || target.itemData == null
		    || target.itemData.Tags == null || target.itemData.Tags.Count == 0)
			return false;

		if (playerTags == null || playerTags.Count == 0) return false;

		foreach (string playerTag in playerTags)
		{
			if (!string.IsNullOrEmpty(playerTag) && target.itemData.Tags.Contains(playerTag))
				return true;
		}
		return false;
	}

	/// <summary>呼叫附近狼群同伴支援</summary>
	private void TryCallNearbyWolves()
	{
		if (_packCount < 2 || _packCallCooldownTimer > 0f) return;
		CallNearbyWolves(_currentThreat);
		_packCallCooldownTimer = packCallCooldown;
	}

	private void CallNearbyWolves(Item threatSource)
	{
		if (threatSource == null) return;

		List<Item> allItems = _detector.CurrentItemsInArea;
		if (allItems == null || allItems.Count == 0) return;

		foreach (Item it in allItems)
		{
			if (!TryGetWolfAlly(it, out AI_Wolf ally)) continue;
			if (DistanceTo(ally.transform) > allyCallDistance) continue;
			ally.ReceivePackCall(threatSource, this);
		}
	}

	private bool TryGetWolfAlly(Item target, out AI_Wolf ally)
	{
		ally = null;
		if (target == null || target == item) return false;

		ally = target.GetComponentInChildren<AI_Wolf>();
		if (ally == null || ally == this) return false;

		if (wolfTags == null || wolfTags.Count == 0) return true;

		if (target.itemData == null || target.itemData.Tags == null || target.itemData.Tags.Count == 0)
			return false;

		foreach (string wolfTag in wolfTags)
		{
			if (!string.IsNullOrEmpty(wolfTag) && target.itemData.Tags.Contains(wolfTag))
				return true;
		}
		return false;
	}
#endregion

#region Animation Mapping
	protected override string GetAnimationNameForState(WolfState state)
	{
		switch (state)
		{
			case WolfState.Idle:   return animIdle;
			case WolfState.Move:   return animMove;
			case WolfState.Alert:  return animAlert;
			case WolfState.Chase:  return animChase;
			case WolfState.Attack: return animAttack;
			case WolfState.Avoid:  return animAvoid;
			case WolfState.Flee:   return animFlee;
			default: throw new ArgumentOutOfRangeException(nameof(state), state, null);
		}
	}

	protected override string GetStateTextCN(WolfState state)
	{
		switch (state)
		{
			case WolfState.Idle:   return "待机";
			case WolfState.Move:   return "闲逛";
			case WolfState.Alert:  return "警觉";
			case WolfState.Chase:  return "追击";
			case WolfState.Attack: return "攻击";
			case WolfState.Avoid:  return "避让";
			case WolfState.Flee:   return "逃跑";
			default: throw new ArgumentOutOfRangeException(nameof(state), state, null);
		}
	}
#endregion

#region Debug Gizmos
#if UNITY_EDITOR
	private void OnDrawGizmosSelected()
	{
		Vector3 center = transform.position;

		DrawRangeWithLabel(center, attackTriggerDistance, new Color(1f, 0.2f, 0.2f, 1f), "攻击距离 attackTriggerDistance", 8f);
		DrawRangeWithLabel(center, wanderRadius, new Color(0.3f, 1f, 0.5f, 1f), "闲逛半径 wanderRadius", 52f);
		DrawRangeWithLabel(center, avoidRunDistance, new Color(0.2f, 0.6f, 1f, 1f), "避让距离 avoidRunDistance", 96f);
		DrawRangeWithLabel(center, alertDetectDistance, new Color(1f, 0.6f, 0.1f, 1f), "警觉距离 alertDetectDistance", 142f);
		DrawRangeWithLabel(center, chaseTriggerDistance, new Color(1f, 0.9f, 0.2f, 1f), "追击触发 chaseTriggerDistance", 196f);
		DrawRangeWithLabel(center, fleeRunDistance, new Color(0.5f, 0.8f, 1f, 1f), "逃跑距离 fleeRunDistance", 236f);
		DrawRangeWithLabel(center, allyCallDistance, new Color(0.2f, 0.9f, 0.9f, 1f), "呼叫范围 allyCallDistance", 284f);
		DrawRangeWithLabel(center, chaseLossDistance, new Color(0.9f, 0.3f, 0.9f, 1f), "追击放弃 chaseLossDistance", 328f);

		if (_hasPackMate)
		{
			Gizmos.color = new Color(0.2f, 1f, 0.8f, 0.8f);
			Gizmos.DrawLine(center, _packCenter);
			Handles.Label(_packCenter + Vector3.up * 0.25f, "狼群中心 packCenter");

			if (_alphaWolf != null)
			{
				Vector3 alphaPos = _alphaWolf.transform.position;
				Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.85f);
				Gizmos.DrawLine(center, alphaPos);
				Handles.Label(alphaPos + Vector3.up * 0.42f, "头狼 alphaWolf");
			}
		}
	}

	private void DrawRangeWithLabel(Vector3 center, float radius, Color color, string labelText, float angleDeg)
	{
		if (radius <= 0f) return;

		Gizmos.color = color;
		Gizmos.DrawWireSphere(center, radius);

		float rad = angleDeg * Mathf.Deg2Rad;
		Vector3 dir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
		Vector3 edge = center + dir * radius;
		Vector3 labelPos = edge + dir * 0.28f;

		Handles.color = color;
		Handles.Label(labelPos, labelText);
	}
#endif
#endregion
}
