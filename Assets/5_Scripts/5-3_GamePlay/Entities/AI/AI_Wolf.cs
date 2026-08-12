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
    Flee,
    Advance
}

/// <summary>
/// 狼 AI：支持群体协作（呼叫同伴、集火）、攻击伤害窗口、逃跑/避让等行为。
/// 状态优先级：逃跑 > 攻击 > 追击 > 避让 > 警觉 > 移动 > 待机
/// </summary>
public partial class AI_Wolf : AI_Base<WolfState>, IAIAdvanceCommandReceiver
{
#region SaveData
	[Serializable]
	[MemoryPackable]
	public partial class AI_WolfSaveData
	{
		public WolfState State = WolfState.Idle;
		public float Fatigue01 = 0f;
		public bool HasAdvanceCommand;
		public int AdvanceTargetItemGuid;
		public Vector3 AdvanceTargetPosition;
		public float AdvanceArrivalDistance = 1.25f;
		public bool AttackActorsOnRoute;
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

	// 追击阵型：缓存一个稳定的寻路目标，避免每帧向导航层注入转向力。
	[SerializeField, ReadOnly]
	private Vector3 _chaseFormationTarget;

	[SerializeField, ReadOnly]
	private int _chaseFormationSlot = -1;

	[SerializeField, ReadOnly]
	private int _chaseFormationMemberCount;

	private bool _hasChaseFormationTarget;
	private readonly List<AI_Wolf> _chaseFormationMembers = new List<AI_Wolf>(8);
	private static readonly Comparison<AI_Wolf> ChaseFormationMemberComparer = CompareChaseFormationMembers;

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
	public float allyCallDistance = 120f;

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/协作"), LabelText("同伴支援时长"), SuffixLabel("秒", true), MinValue(0.1f)]
	public float packAssistDuration = 5f;

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/协作"), LabelText("呼叫冷却"), SuffixLabel("秒", true), MinValue(0f)]
	public float packCallCooldown = 0.8f;

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/追击站位"), LabelText("启用追击站位")]
	public bool enableChaseFormation = true;

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/追击站位"), HorizontalGroup("配置/群体/追击站位/参数1"), LabelText("最少成员"), MinValue(2)]
	public int chaseFormationMinMembers = 2;
	[HorizontalGroup("配置/群体/追击站位/参数1"), LabelText("站位半径"), SuffixLabel("米", true), MinValue(0.05f)]
	public float chaseFormationRadius = 1.05f;

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/追击站位"), HorizontalGroup("配置/群体/追击站位/参数2"), LabelText("纵向间隔"), SuffixLabel("米", true), MinValue(0f)]
	public float chaseFormationVerticalSpacing = 0.42f;
	[HorizontalGroup("配置/群体/追击站位/参数2"), LabelText("最大纵向比例"), Range(0.1f, 0.85f)]
	public float chaseFormationMaxVerticalRatio = 0.55f;

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/追击站位"), HorizontalGroup("配置/群体/追击站位/参数3"), LabelText("攻击安全余量"), SuffixLabel("米", true), MinValue(0.01f)]
	public float chaseFormationAttackMargin = 0.18f;
	[HorizontalGroup("配置/群体/追击站位/参数3"), LabelText("攻击槽容差"), SuffixLabel("米", true), MinValue(0.01f)]
	public float chaseFormationAttackSlotTolerance = 0.14f;

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/追击站位"), HorizontalGroup("配置/群体/追击站位/参数4"), LabelText("分离距离"), SuffixLabel("米", true), MinValue(0.05f)]
	public float chaseSeparationDistance = 0.85f;
	[HorizontalGroup("配置/群体/追击站位/参数4"), LabelText("分离强度"), Range(0f, 2f)]
	public float chaseSeparationStrength = 0.7f;

	[TabGroup("配置", "群体"), BoxGroup("配置/群体/追击站位"), LabelText("最大分离偏移"), SuffixLabel("米", true), MinValue(0f)]
	public float chaseSeparationMaxOffset = 0.2f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), HorizontalGroup("配置/行为/战斗/Hr1"), LabelText("警觉距离"), SuffixLabel("米", true), MinValue(0.1f)]
	public float alertDetectDistance = 20f;
	[HorizontalGroup("配置/行为/战斗/Hr1"), LabelText("追击触发"), SuffixLabel("米", true), MinValue(0.1f)]
	public float chaseTriggerDistance = 28f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), HorizontalGroup("配置/行为/战斗/Hr2"), LabelText("攻击距离"), SuffixLabel("米", true), MinValue(0.1f)]
	public float attackTriggerDistance = 1.4f;
	[HorizontalGroup("配置/行为/战斗/Hr2"), LabelText("追击放弃"), SuffixLabel("米", true), MinValue(0.1f)]
	public float chaseLossDistance = 44f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), HorizontalGroup("配置/行为/战斗/Hr3"), LabelText("攻击冷却"), SuffixLabel("秒", true), MinValue(0f)]
	public float attackCooldown = 2f;
	[HorizontalGroup("配置/行为/战斗/Hr3"), LabelText("伤害窗口"), SuffixLabel("秒", true), MinValue(0.01f)]
	public float attackDamageWindow = 0.33333334f;
	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), LabelText("攻击窗口延迟"), SuffixLabel("秒", true), MinValue(0f)]
	public float attackDamageStartDelay = 0.35f;

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
	public string animIdle = "Idle";
	[HorizontalGroup("配置/动画/状态/Hr1"), LabelText("移动")]
	public string animMove = "Move";
	[HorizontalGroup("配置/动画/状态/Hr1"), LabelText("警觉")]
	public string animAlert = "Idle";

	[TabGroup("配置", "动画"), BoxGroup("配置/动画/状态"), HorizontalGroup("配置/动画/状态/Hr2"), LabelText("追击")]
	public string animChase = "Move";
	[HorizontalGroup("配置/动画/状态/Hr2"), LabelText("攻击")]
	public string animAttack = "Attack";
	[HorizontalGroup("配置/动画/状态/Hr2"), LabelText("逃离")]
	public string animAvoid = "Move";

	[TabGroup("配置", "动画"), BoxGroup("配置/动画/状态"), LabelText("残血逃跑")]
	public string animFlee = "Move";
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
	protected override bool IsMoveState(WolfState state) =>
		state == WolfState.Move || state == WolfState.Advance;
	protected override bool IsIdleState(WolfState state) => state == WolfState.Idle;
#endregion

#region Lifecycle
	public override void Load()
	{
		ModData.ReadData(ref Data);
		Data ??= new AI_WolfSaveData();
		NormalizeAdvanceData();
		_currentState = Data.State;
		if (_currentState == WolfState.Advance && !Data.HasAdvanceCommand)
			_currentState = WolfState.Idle;
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
		ClearChaseFormation();
		_attack.Reset();
		_attack.Cooldown = attackCooldown;
		_attack.DamageWindow = attackDamageWindow;
		_attack.DamageWindowStartDelay = attackDamageStartDelay;
	}

	protected override void OnBindExtraModules()
	{
		if (_detector != null)
		{
			// 感知半径至少覆盖追击触发距离；丢失距离由当前目标记忆维持。
			_detector.DetectionRadius = Mathf.Max(_detector.DetectionRadius, chaseTriggerDistance);
		}
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
		RefreshChaseFormationTarget();
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

		if (previous == WolfState.Chase && next != WolfState.Chase)
		{
			ClearChaseFormation();
		}

		// 进入攻击状态：重置窗口触发标记
		if (next == WolfState.Attack)
		{
			_attack.OnEnterAttackState();
		}

		if (next == WolfState.Chase)
		{
			RefreshChaseFormationTarget();
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
		Vector2 toPack = WorldTopologyRuntime.ShortestDelta(transform.position, cohesionAnchor);
		if (toPack.sqrMagnitude > 0.0001f)
		{
			offset += toPack.normalized * (wanderRadius * wanderCohesionWeight);
		}
	}

	protected override string GetDebugExtraInfo()
	{
		string roleText = _isAlphaWolf ? "头狼" : "跟随";
		string advanceText = HasAdvanceCommand ? " | 推进: 是" : string.Empty;
		string formationText = _hasChaseFormationTarget
			? $" | 追击位: {GetChaseFormationSlotLabel(_chaseFormationSlot)}/{_chaseFormationMemberCount}"
			: string.Empty;
		return $" | 狼群数: {_packCount} | 角色: {roleText}{formationText}{advanceText}";
	}
#endregion

#region PublicAPI
	public bool HasAdvanceCommand => Data?.HasAdvanceCommand == true;
	public Vector3 AdvanceTargetPosition => Data?.AdvanceTargetPosition ?? transform.position;

	/// <summary>
	/// 接收外部推进命令。目标物品消失后仍会走向最后记录的位置；
	/// 若开启沿途攻击，狼会在战斗结束后继续推进。
	/// </summary>
	public void BeginAdvance(AIAdvanceCommand command)
	{
		if (!IsFinite(command.TargetPosition))
			throw new ArgumentException("推进目标必须是有限坐标。", nameof(command));

		Data ??= new AI_WolfSaveData();
		Data.HasAdvanceCommand = true;
		Data.AdvanceTargetItemGuid = command.TargetItemGuid;
		Data.AdvanceTargetPosition = command.TargetPosition;
		Data.AdvanceArrivalDistance = Mathf.Max(0.05f, command.ArrivalDistance);
		Data.AttackActorsOnRoute = command.AttackActorsOnRoute;
		_currentThreat = null;
		ClearChaseFormation();
		_alertTimer = 0f;
		_packAssistTimer = 0f;

		if (debugLog)
		{
			Debug.Log(
				$"[WolfAI] {name} 接收推进命令，目标={command.TargetPosition}, " +
				$"沿途攻击={command.AttackActorsOnRoute}",
				this);
		}
	}

	[Button("狼群集火玩家")]
	public void TriggerPackAttack(Item threatSource)
	{
		if (threatSource == null)
		{
			throw new ArgumentNullException(nameof(threatSource));
		}

		_currentThreat = threatSource;
		ClearChaseFormation();
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
		ClearChaseFormation();
		_alertTimer = Mathf.Max(_alertTimer, alertKeepDuration);
		_packAssistTimer = Mathf.Max(_packAssistTimer, packAssistDuration);

		if (debugLog)
		{
			Debug.Log($"[WolfAI] {name} 收到同伴呼叫，呼叫者={caller?.name}, 目标={threatSource.name}", this);
		}
	}
#endregion

#region PublicUtility - 追击站位
	/// <summary>
	/// 计算左右浅扇形中的稳定槽位偏移。纵向偏移始终受半径比例限制，
	/// 保留较大的 X 分量，以兼容当前仅左右朝向的攻击碰撞体。
	/// </summary>
	public static Vector2 CalculateChaseFormationSlotOffset(
		int slotIndex,
		float formationRadius,
		float verticalSpacing,
		float maxVerticalRatio)
	{
		float radius = Mathf.Max(0.05f, formationRadius);
		int safeSlotIndex = Mathf.Max(0, slotIndex);
		int pairIndex = safeSlotIndex / 2;
		float laneDirection = (safeSlotIndex & 1) == 0 ? -1f : 1f;

		float requestedVertical = GetFormationVerticalOffset(pairIndex, Mathf.Max(0f, verticalSpacing));
		float maxVertical = radius * Mathf.Clamp(maxVerticalRatio, 0.05f, 0.85f);
		float vertical = Mathf.Clamp(requestedVertical, -maxVertical, maxVertical);
		float horizontal = Mathf.Sqrt(Mathf.Max(0f, radius * radius - vertical * vertical));
		return new Vector2(horizontal * laneDirection, vertical);
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
		if (ShouldAdvance())  return WolfState.Advance;
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
		stateMachine.Register(CreateAdvanceStateNode(
			WolfState.Advance,
			ResolveAdvanceTarget,
			() => Data?.AdvanceArrivalDistance ?? 1.25f,
			CompleteAdvance));
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

		// 追击期间持续面向玩家，避免寻路移动时保留旧朝向而倒着奔跑。
		FaceTarget(_currentThreat.transform.position);
		MoveTo(_hasChaseFormationTarget
			? _chaseFormationTarget
			: _currentThreat.transform.position);
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
					WorldTopologyRuntime.ShortestDelta(transform.position, targetPosition));
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

	private AIAdvanceTarget ResolveAdvanceTarget()
	{
		if (Data?.HasAdvanceCommand != true)
			return AIAdvanceTarget.None;

		if (Data.AdvanceTargetItemGuid != 0 && ItemMgr.Instance != null)
		{
			Item targetItem = ItemMgr.Instance.GetItemByGuid(Data.AdvanceTargetItemGuid);
			if (targetItem != null)
				Data.AdvanceTargetPosition = targetItem.transform.position;
		}

		return IsFinite(Data.AdvanceTargetPosition)
			? new AIAdvanceTarget(true, Data.AdvanceTargetPosition)
			: AIAdvanceTarget.None;
	}

	private void CompleteAdvance()
	{
		if (Data == null)
			return;

		Data.HasAdvanceCommand = false;
		Data.AdvanceTargetItemGuid = 0;
		Data.AttackActorsOnRoute = false;
		_currentThreat = null;
		_alertTimer = 0f;
		_packAssistTimer = 0f;

		if (debugLog)
			Debug.Log($"[WolfAI] {name} 已到达推进目标。", this);
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

		if (!IsAggressiveAdvanceActive() && _packCount < 2) return false;
		if (distance > attackTriggerDistance) return false;

		// 已分配追击槽位时先走到自己的攻击站位，再切攻击，避免刚进范围就把狼群锁成一团。
		return HasReachedChaseFormationAttackSlot();
	}

	/// <summary>追击条件：双狼以上且在追击范围内</summary>
	private bool ShouldChase()
	{
		if (_currentThreat == null) return false;
		float distance = DistanceTo(_currentThreat.transform);
		bool alreadyEngaged = _currentState == WolfState.Chase || _currentState == WolfState.Attack;

		if (IsAggressiveAdvanceActive())
			return distance <= (alreadyEngaged ? chaseLossDistance : chaseTriggerDistance);

		if (_packCount >= 2)
		{
			return alreadyEngaged
				? distance <= chaseLossDistance
				: distance <= chaseTriggerDistance;
		}
		return false;
	}

	/// <summary>避让条件：独狼在追击触发范围内</summary>
	private bool ShouldAvoid()
	{
		if (_currentThreat == null) return false;
		if (IsAggressiveAdvanceActive()) return false;
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

	private bool ShouldAdvance()
	{
		return Data?.HasAdvanceCommand == true;
	}
#endregion

#region Helpers - Wolf 特有
	private void NormalizeAdvanceData()
	{
		Data.AdvanceArrivalDistance = Data.AdvanceArrivalDistance > 0f
			? Mathf.Max(0.05f, Data.AdvanceArrivalDistance)
			: 1.25f;

		if (!Data.HasAdvanceCommand || !IsFinite(Data.AdvanceTargetPosition))
		{
			Data.HasAdvanceCommand = false;
			Data.AdvanceTargetItemGuid = 0;
			Data.AttackActorsOnRoute = false;
		}
	}

	private static bool IsFinite(Vector3 value)
	{
		return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
		       !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
		       !float.IsNaN(value.z) && !float.IsInfinity(value.z);
	}

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
		if (IsAggressiveAdvanceActive())
		{
			Item nearestActor = FindClosestAdvanceAggressionTarget();
			if (nearestActor != null)
			{
				_currentThreat = nearestActor;
				return;
			}

			if (IsLivingActorTarget(_currentThreat) &&
			    DistanceTo(_currentThreat.transform) <= chaseLossDistance)
			{
				return;
			}

			_currentThreat = null;
			return;
		}

		Item nearestPlayer = FindClosestPlayerThreat();

		if (nearestPlayer != null)
		{
			_currentThreat = nearestPlayer;
			return;
		}

		if (_currentThreat == null) return;
		if (DistanceTo(_currentThreat.transform) > chaseLossDistance)
			_currentThreat = null;
	}

	private Item FindClosestAdvanceAggressionTarget()
	{
		List<Item> allItems = _detector.CurrentItemsInArea;
		if (allItems == null || allItems.Count == 0) return null;

		Item closest = null;
		float closestDistanceSqr = float.MaxValue;
		foreach (Item candidate in allItems)
		{
			if (!IsLivingActorTarget(candidate)) continue;

			float distanceSqr = WorldTopologyRuntime.SqrDistance(transform.position, candidate.transform.position);
			if (distanceSqr < closestDistanceSqr)
			{
				closest = candidate;
				closestDistanceSqr = distanceSqr;
			}
		}

		return closest;
	}

	private bool IsLivingActorTarget(Item target)
	{
		if (target == null || target == item)
			return false;

		if (TryGetWolfAlly(target, out _))
			return false;

		DamageReceiver receiver = target.itemMods?.GetMod_ByID<DamageReceiver>(ModText.Hp);
		if (receiver == null || receiver.Hp <= 0f)
			return false;

		if (IsPlayerThreat(target))
			return true;

		Module aiModule = target.itemMods?.GetMod_ByID(ModText.AI);
		if (aiModule == null)
			return false;

		// 新状态机动物可提供精确存活语义；旧 AI（例如幽灵）仍由生命模块兜底纳入。
		return aiModule is not IAIActor actor || actor.IsAlive;
	}

	private bool IsAggressiveAdvanceActive()
	{
		return Data?.HasAdvanceCommand == true && Data.AttackActorsOnRoute;
	}

	private Item FindClosestPlayerThreat()
	{
		return _detector.FindClosestItemByTags(
			playerTags,
			transform.position,
			includeUnityPlayerTag: true);
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

	/// <summary>刷新追击槽位：同一目标的狼按稳定 ID 排序，并在左右浅扇形中分配不同目的地。</summary>
	private void RefreshChaseFormationTarget()
	{
		ClearChaseFormation();

		if (!enableChaseFormation || _currentThreat == null || !IsPlayerChaseTarget(_currentThreat))
			return;

		_chaseFormationMembers.Add(this);
		List<Item> detectedItems = _detector?.CurrentItemsInArea;
		if (detectedItems == null)
			return;

		for (int i = 0; i < detectedItems.Count; i++)
		{
			if (!TryGetWolfAlly(detectedItems[i], out AI_Wolf ally) ||
				!IsEligibleChaseFormationMember(ally, _currentThreat) ||
				_chaseFormationMembers.Contains(ally))
			{
				continue;
			}

			_chaseFormationMembers.Add(ally);
		}

		if (_chaseFormationMembers.Count < Mathf.Max(2, chaseFormationMinMembers))
		{
			ClearChaseFormation();
			return;
		}

		_chaseFormationMembers.Sort(ChaseFormationMemberComparer);
		int slotIndex = ResolveChaseFormationSlotIndex(_currentThreat.transform.position);
		if (slotIndex < 0)
		{
			ClearChaseFormation();
			return;
		}

		float maxFormationRadius = Mathf.Max(
			0.05f,
			attackTriggerDistance - Mathf.Max(0.05f, chaseFormationAttackMargin));
		float formationRadius = Mathf.Clamp(chaseFormationRadius, 0.05f, maxFormationRadius);
		Vector2 slotOffset = CalculateChaseFormationSlotOffset(
			slotIndex,
			formationRadius,
			chaseFormationVerticalSpacing,
			chaseFormationMaxVerticalRatio);
		Vector2 desiredOffset = Vector2.ClampMagnitude(
			slotOffset + CalculateChaseSeparationOffset(),
			maxFormationRadius);

		Vector3 targetPosition = _currentThreat.transform.position;
		Vector3 formationTarget = WorldTopologyRuntime.NormalizePosition(targetPosition + (Vector3)desiredOffset);
		if (!IsWalkableFormationDestination(formationTarget))
		{
			// 分离偏移踩到障碍时，优先保留原槽位；槽位同样不可走时回退旧追击逻辑。
			formationTarget = WorldTopologyRuntime.NormalizePosition(targetPosition + (Vector3)slotOffset);
			if (!IsWalkableFormationDestination(formationTarget))
			{
				ClearChaseFormation();
				return;
			}
		}

		_chaseFormationTarget = formationTarget;
		_chaseFormationSlot = slotIndex;
		_chaseFormationMemberCount = _chaseFormationMembers.Count;
		_hasChaseFormationTarget = true;
	}

	/// <summary>计算过近同伴带来的有限分离偏移，结果只影响下次寻路目标，不改写导航速度。</summary>
	private Vector2 CalculateChaseSeparationOffset()
	{
		float separationDistance = Mathf.Max(0.05f, chaseSeparationDistance);
		float maxOffset = Mathf.Max(0f, chaseSeparationMaxOffset);
		if (maxOffset <= 0f || chaseSeparationStrength <= 0f || _chaseFormationMembers.Count < 2)
			return Vector2.zero;

		float separationDistanceSqr = separationDistance * separationDistance;
		Vector2 separation = Vector2.zero;
		for (int i = 0; i < _chaseFormationMembers.Count; i++)
		{
			AI_Wolf ally = _chaseFormationMembers[i];
			if (ally == null || ally == this)
				continue;

			Vector2 away = WorldTopologyRuntime.ShortestDelta(ally.transform.position, transform.position);
			float distanceSqr = away.sqrMagnitude;
			if (distanceSqr >= separationDistanceSqr)
				continue;

			if (distanceSqr <= 0.0001f)
			{
				away = CompareChaseFormationMembers(this, ally) <= 0 ? Vector2.down : Vector2.up;
				separation += away;
				continue;
			}

			float distance = Mathf.Sqrt(distanceSqr);
			separation += away / distance * (1f - distance / separationDistance);
		}

		return separation.sqrMagnitude <= 0.0001f
			? Vector2.zero
			: Vector2.ClampMagnitude(separation * (separationDistance * chaseSeparationStrength), maxOffset);
	}

	/// <summary>只让正在围攻同一玩家、且没有进入撤退状态的同伴占用追击槽位。</summary>
	private static bool IsEligibleChaseFormationMember(AI_Wolf wolf, Item threat)
	{
		return wolf != null && wolf.isActiveAndEnabled && wolf._isReady &&
			wolf._currentThreat == threat &&
			wolf._currentState != WolfState.Avoid && wolf._currentState != WolfState.Flee;
	}

	/// <summary>优先占据自己所在的左右翼，避免为了固定编号从玩家身体中央穿过。</summary>
	private int ResolveChaseFormationSlotIndex(Vector3 targetPosition)
	{
		int leftLaneCount = 0;
		int rightLaneCount = 0;
		for (int memberIndex = 0; memberIndex < _chaseFormationMembers.Count; memberIndex++)
		{
			AI_Wolf member = _chaseFormationMembers[memberIndex];
			bool useRightLane = ShouldUseRightChaseLane(member, targetPosition, memberIndex);
			int slotIndex = useRightLane
				? rightLaneCount++ * 2 + 1
				: leftLaneCount++ * 2;
			if (member == this)
				return slotIndex;
		}

		return -1;
	}

	/// <summary>横向位置明确时保留原侧；与玩家 X 轴重合时按稳定排序交替分翼。</summary>
	private static bool ShouldUseRightChaseLane(AI_Wolf wolf, Vector3 targetPosition, int stableMemberIndex)
	{
		if (wolf == null)
			return (stableMemberIndex & 1) != 0;

		float horizontalOffset = WorldTopologyRuntime.ShortestDelta(targetPosition, wolf.transform.position).x;
		if (Mathf.Abs(horizontalOffset) > 0.05f)
			return horizontalOffset > 0f;

		return (stableMemberIndex & 1) != 0;
	}

	/// <summary>兼容 Item 标签缺失但 Unity Tag 标记为 Player 的本地玩家。</summary>
	private bool IsPlayerChaseTarget(Item target)
	{
		return IsPlayerThreat(target) || (target != null && target.CompareTag("Player"));
	}

	/// <summary>阵型追击期间，只有足够靠近自身槽位才允许进入攻击状态。</summary>
	private bool HasReachedChaseFormationAttackSlot()
	{
		if (!_hasChaseFormationTarget)
			return true;

		float tolerance = Mathf.Min(
			Mathf.Max(0.01f, chaseFormationAttackSlotTolerance),
			Mathf.Max(0.05f, chaseFormationAttackMargin));
		return WorldTopologyRuntime.SqrDistance(transform.position, _chaseFormationTarget) <= tolerance * tolerance;
	}

	/// <summary>导航未就绪时交给既有导航重试；就绪后只接受可走阵型目标。</summary>
	private static bool IsWalkableFormationDestination(Vector3 destination)
	{
		WorldNavigationManager navigation = WorldNavigationManager.ExistingInstance;
		return navigation == null || !navigation.IsNavigationReady || navigation.IsWalkable((Vector2)destination);
	}

	/// <summary>清除瞬态站位缓存，避免目标切换或脱战后沿用旧阵型。</summary>
	private void ClearChaseFormation()
	{
		_chaseFormationMembers.Clear();
		_chaseFormationTarget = transform.position;
		_chaseFormationSlot = -1;
		_chaseFormationMemberCount = 0;
		_hasChaseFormationTarget = false;
	}

	/// <summary>优先使用持久化 Item Guid 排序，对象池复用时再回退到实例 ID。</summary>
	private static int CompareChaseFormationMembers(AI_Wolf left, AI_Wolf right)
	{
		if (ReferenceEquals(left, right))
			return 0;
		if (left == null)
			return -1;
		if (right == null)
			return 1;

		int leftGuid = left.item?.itemData?.Guid ?? 0;
		int rightGuid = right.item?.itemData?.Guid ?? 0;
		int guidComparison = leftGuid.CompareTo(rightGuid);
		return guidComparison != 0
			? guidComparison
			: left.GetInstanceID().CompareTo(right.GetInstanceID());
	}

	/// <summary>第一个左右槽位在正侧面，后续槽位按上下交错排列。</summary>
	private static float GetFormationVerticalOffset(int pairIndex, float spacing)
	{
		if (pairIndex <= 0 || spacing <= 0f)
			return 0f;

		int layer = (pairIndex + 1) / 2;
		return (pairIndex & 1) == 1 ? layer * spacing : -layer * spacing;
	}

	/// <summary>把内部槽位索引转换为便于调试的左右翼编号。</summary>
	private static string GetChaseFormationSlotLabel(int slotIndex)
	{
		if (slotIndex < 0)
			return "-";

		string lane = (slotIndex & 1) == 0 ? "左翼" : "右翼";
		return $"{lane}{slotIndex / 2 + 1}";
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
			case WolfState.Advance:return animMove;
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
			case WolfState.Advance:return "推进";
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

		if (_hasChaseFormationTarget)
		{
			Gizmos.color = new Color(0.65f, 0.25f, 1f, 0.9f);
			Gizmos.DrawLine(center, _chaseFormationTarget);
			Gizmos.DrawWireSphere(_chaseFormationTarget, 0.1f);
			Handles.Label(
				_chaseFormationTarget + Vector3.up * 0.2f,
				$"追击位 {GetChaseFormationSlotLabel(_chaseFormationSlot)}/{_chaseFormationMemberCount}");
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
