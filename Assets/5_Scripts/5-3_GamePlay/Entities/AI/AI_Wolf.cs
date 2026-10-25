using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using MemoryPack;
using Sirenix.OdinInspector;
using Unity.Mathematics;
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
    Advance,
    Search
}

/// <summary>
/// 狼 AI：支持群体协作（呼叫同伴、集火）、攻击伤害窗口、逃跑/避让等行为。
/// 状态优先级：逃跑 > 攻击 > 追击 > 搜索最后位置 > 避让 > 警觉 > 移动 > 待机
/// </summary>
public partial class AI_Wolf : AI_Base<WolfState>, IAIAdvanceCommandReceiver, IAIAdvanceCommandCancellationReceiver, IAnimalAttackRangeSource
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
	private bool _holdingChaseFormationPosition;
	private readonly List<AI_Wolf> _packAllies = new List<AI_Wolf>(8);
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
	[HorizontalGroup("配置/群体/追击站位/参数3"), LabelText("站位停止距离"), SuffixLabel("米", true), MinValue(0.01f)]
	public float chaseFormationArrivalDistance = 0.3f;
	[TabGroup("配置", "群体"), BoxGroup("配置/群体/追击站位"), LabelText("站位恢复移动距离"), SuffixLabel("米", true), MinValue(0.01f)]
	public float chaseFormationResumeDistance = 0.6f;

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
	public float attackTriggerDistance = 2.1f;
	[HorizontalGroup("配置/行为/战斗/Hr2"), LabelText("追击放弃"), SuffixLabel("米", true), MinValue(0.1f)]
	public float chaseLossDistance = 44f;
	/// <summary>狼接受新追击路线的总代价上限，不包含上限本身。</summary>
	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), LabelText("路径代价上限"), MinValue(1)]
	public int chasePathCostLimit = 440;
	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), LabelText("路径重试间隔"), SuffixLabel("秒", true), MinValue(0.1f)]
	public float chasePathRetryDelay = 3f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), HorizontalGroup("配置/行为/战斗/Hr3"), LabelText("攻击冷却"), SuffixLabel("秒", true), MinValue(0f)]
	public float attackCooldown = 2f;
	[HorizontalGroup("配置/行为/战斗/Hr3"), LabelText("伤害窗口"), SuffixLabel("秒", true), MinValue(0.01f)]
	public float attackDamageWindow = 0.33333334f;
	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), LabelText("攻击前摇"), SuffixLabel("秒", true), MinValue(0f)]
	public float attackDamageStartDelay = 0.25f;
	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), LabelText("攻击后摇"), SuffixLabel("秒", true), MinValue(0f)]
	public float attackRecoveryDuration = 0.35f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/战斗"), LabelText("警觉维持时长"), SuffixLabel("秒", true), MinValue(0f)]
	public float alertKeepDuration = 2f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/逃跑"), HorizontalGroup("配置/行为/逃跑/Hr1"), LabelText("触发血量(双狼)"), Range(0f, 1f)]
	public float fleeTriggerHpRate = 0.22f;
	[HorizontalGroup("配置/行为/逃跑/Hr1"), LabelText("安全血量(双狼)"), Range(0f, 1f)]
	public float fleeSafeHpRate = 0.45f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/逃跑"), LabelText("远离玩家距离"), SuffixLabel("米", true), MinValue(1f)]
	public float avoidRunDistance = 30f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/逃跑"), LabelText("避让安全距离"), SuffixLabel("米", true), MinValue(1f)]
	public float avoidSafeDistance = 30f; // 玩家远离到此距离后结束本轮避让。

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/逃跑"), LabelText("逃跑距离"), SuffixLabel("米", true), MinValue(1f)]
	public float fleeRunDistance = 30f;

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
	protected override bool IsAmbientAudioState(WolfState state) =>
		state == WolfState.Idle || state == WolfState.Move;
#endregion

#region Lifecycle
	protected override void OnLoad()
	{
		ModData.ReadData(ref Data);
		Data ??= new AI_WolfSaveData();
		NormalizeAdvanceData();
		_currentState = Data.State;
		if (_currentState == WolfState.Search ||
			_currentState == WolfState.Advance && !Data.HasAdvanceCommand)
			_currentState = WolfState.Idle;
		_idleRemainTimer = GetIdleDuration();
		InitializeAI();
	}

	protected override void OnSave()
	{
		Data.State = _currentState;
		ModData.WriteData(Data);
	}

	protected override void OnUnload()
	{
		ForgetThreat();
		_packAllies.Clear();
		_chaseFormationMembers.Clear();
		base.OnUnload();
	}
#endregion

#region Base Overrides - Hooks
	protected override void OnResetRuntimeState()
	{
		_packAllies.Clear();
		_alertTimer = 0f;
		_packAssistTimer = 0f;
		_packCallCooldownTimer = 0f;
		_packCount = 1;
		_hasPackMate = false;
		_isAlphaWolf = true;
		_alphaWolf = this;
		_packCenter = transform.position;
		ForgetThreat();
		_attack.Reset();
		_attack.Cooldown = attackCooldown;
		_attack.DamageWindow = attackDamageWindow;
		_attack.WindupDuration = attackDamageStartDelay;
		_attack.RecoveryDuration = attackRecoveryDuration;
	}

	protected override void OnBindExtraModules()
	{
		if (_detector != null)
		{
			// 感知半径覆盖追击触发和独狼避让距离；较远的当前威胁由目标记忆维持。
			_detector.DetectionRadius = Mathf.Max(
				_detector.DetectionRadius,
				Mathf.Max(chaseTriggerDistance, avoidSafeDistance));
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
		TickThreatMemory(deltaTime);
		_attack.Tick(deltaTime);
	}

	protected override void OnPreEvaluate()
	{
		RefreshPackStatus();
		RefreshThreatTarget();
		UpdateSearchArrival();
		RefreshChaseFormationTarget();
	}

	protected override void OnBeforeSwitchState(WolfState previous, WolfState next)
	{
		// 离开战斗相关状态时，若支援计时器已过期则清除威胁目标
		if (next != WolfState.Alert && next != WolfState.Chase
		    && next != WolfState.Attack && next != WolfState.Avoid && next != WolfState.Flee
		    && next != WolfState.Search)
		{
			if (_packAssistTimer <= 0f)
			{
				ForgetThreat();
			}
		}

		// 只有打断尚未结束的攻击才重新计冷却，追击切换保留已流逝的冷却。
		if (previous == WolfState.Attack && next != WolfState.Attack)
		{
			if (_attack.IsAttackLocked)
				_attack.OnExitAttackState();
			else
				_attack.StopWindow();
		}

		if (next != WolfState.Chase && next != WolfState.Attack)
		{
			ClearChaseFormation();
		}

		// 进入攻击状态：重置窗口触发标记
		if (next == WolfState.Attack)
		{
			_canSearchForThreat = true;
			_attack.OnEnterAttackState();
		}

		if (next == WolfState.Chase)
		{
			_canSearchForThreat = true;
			RefreshChaseFormationTarget();
		}
		if (next == WolfState.Avoid || next == WolfState.Flee)
			_canSearchForThreat = false;
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

	private AI_DebugOverlay.TextCache<(int Count, bool Leader, bool Formation, int Slot, int Members, bool Advance)> debugExtraCache;

	protected override string GetDebugExtraInfo()
	{
		var key = (_packCount, _isAlphaWolf, _hasChaseFormationTarget,
			_chaseFormationSlot, _chaseFormationMemberCount, HasAdvanceCommand);
		uint generation = item != null ? item.RuntimeGeneration : 0;
		if (debugExtraCache.TryGet(key, AI_DebugOverlay.TextLocale, generation, out string cached))
			return cached;
		string roleText = FlatWorldLocalizationService.GetUiText(_isAlphaWolf ? "头狼" : "跟随");
		string advanceText = HasAdvanceCommand ? FlatWorldLocalizationService.GetUiText(" | 推进: 是") : string.Empty;
		string formationText = _hasChaseFormationTarget
			? FlatWorldLocalizationService.GetUiFormat(
				" | 追击位: {0}/{1}", GetChaseFormationSlotLabel(_chaseFormationSlot), _chaseFormationMemberCount)
			: string.Empty;
		return debugExtraCache.Store(key, AI_DebugOverlay.TextLocale, generation,
			FlatWorldLocalizationService.GetUiFormat(
				" | 狼群数: {0} | 角色: {1}{2}{3}", _packCount, roleText, formationText, advanceText));
	}
#endregion

#region PublicAPI
	public int PackMemberCount => _packCount;
	internal int ValidatedPackAllyCount => _packAllies.Count;
	public AI_Wolf PackLeader => _alphaWolf;
	public Item CurrentThreat => _currentThreat;
	public float AttackRange => attackTriggerDistance;
	internal float AttackCooldownRemaining => _attack.CooldownRemaining;
	internal bool IsAttackLocked => _attack.IsAttackLocked;
	internal Vector3? ChaseFormationTarget => _hasChaseFormationTarget ? _chaseFormationTarget : (Vector3?)null;
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
		ForgetThreat();
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

	/// <summary>取消外部推进并停下旧寻路，后续由正常状态机重新选择行为。</summary>
	public void CancelAdvance()
	{
		CompleteAdvance();
		ClearChaseFormation();
		item?.GetComponentInChildren<Mod_Mover_AI>(true)?.StopMovement();
	}

	[Button("狼群集火玩家")]
	public void TriggerPackAttack(Item threatSource)
	{
		if (threatSource == null)
		{
			throw new ArgumentNullException(nameof(threatSource));
		}

		if (!IsUsableThreat(threatSource) ||
			!IsWithinEffectivePerceptionRange(threatSource, chaseLossDistance))
			return;

		// 集火确认立即允许搜索，避免目标在首次状态评估前躲回遮挡后被遗忘。
		RememberThreat(threatSource, threatSource.transform.position, true, pursue: true);
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

		if ((caller != null &&
			FactionRelationService.GetRelation(caller.item, item) != FactionRelation.Friendly) ||
			!IsUsableThreat(threatSource))
		{
			return;
		}

		if (IsWithinEffectivePerceptionRange(threatSource, chaseLossDistance))
			RememberThreat(threatSource, threatSource.transform.position, true, pursue: true);
		else if (caller != null && caller._threatVisible && caller.MatchesRememberedThreat(threatSource))
			RememberThreat(threatSource, caller._lastKnownThreatPosition, false, pursue: true);
		else
			return;
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
		if (_animalSkills.IsAnyActive) return WolfState.Attack;
		if (ShouldFlee())     return WolfState.Flee;
		if (ShouldAttack())   return WolfState.Attack;
		if (ShouldChase())    return WolfState.Chase;
		if (ShouldSearch())   return WolfState.Search;
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
		stateMachine.Register(CreateMovingStateNode(WolfState.Search, _ => TickSearch()));
		stateMachine.Register(CreateStateNode(WolfState.Attack, _ => TickAttack()));
		stateMachine.Register(CreateFleeStateNode(
			WolfState.Avoid,
			ResolveFleeSourcePosition,
			() => avoidRunDistance));
		stateMachine.Register(CreateFleeStateNode(
			WolfState.Flee,
			ResolveFleeSourcePosition,
			() => fleeRunDistance));
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
		FaceTarget(_lastKnownThreatPosition);
	}

	private void TickChase()
	{
		if (_currentThreat == null || !_threatVisible) { StopMove(); return; }

		// 编队追击时以实际编队目标为准，保证狼的头朝向真实行进方向。
		Vector3 chaseTarget = _hasChaseFormationTarget
			? _chaseFormationTarget
			: _lastKnownThreatPosition;
		// 编队点本身已占用一部分攻击距离，导航内圈只能使用剩余余量。
		float approachRange = Mathf.Max(0.01f, attackTriggerDistance -
			WorldTopologyRuntime.Distance(chaseTarget, _lastKnownThreatPosition));
		if (ShouldHoldChaseFormationPosition(approachRange))
		{
			StopMove();
			FaceTarget(_lastKnownThreatPosition);
			TryCallNearbyWolves();
			return;
		}
		WorldNavigationDestinationResult moveResult =
			MoveToChaseTarget(chaseTarget, chasePathCostLimit, approachRange);
		if (TryHandleRejectedChasePath(moveResult, _currentThreat, chasePathRetryDelay))
			return;

		FaceTarget(chaseTarget);
		TryCallNearbyWolves();
	}

	private void TickAttack()
	{
		// 持续技能独占攻击动作，普通攻击窗口不能在技能期间重新打开。
		if (_animalSkills.IsAnyActive) { _attack.StopWindow(); StopMove(); return; }
		if (!_threatVisible) { _attack.StopWindow(); StopMove(); return; }
		if (_currentThreat == null)
		{
			_attack.StopWindow();
			StopMove();
			return;
		}

		if (!IsUsableThreat(_currentThreat) || !MatchesRememberedThreat(_currentThreat))
		{
			ForgetThreat();
			_attack.StopWindow();
			StopMove();
			return;
		}

		if (!_attack.IsAttackLocked && _animalSkills.TryStart(_currentThreat))
		{ _attack.StopWindow(); StopMove(); return; }
		Vector3 targetPosition = _lastKnownThreatPosition;
		FaceTarget(targetPosition, true);

		float distance = WorldTopologyRuntime.Distance(transform.position, targetPosition);
		if (_attack.IsAttackLocked)
		{
			StopMove();
		}
		else if (distance <= attackTriggerDistance && _attack.IsCooldownDone)
		{
			StopMove();
			_attack.StartWindow(
				_animator,
				animAttack,
				WorldTopologyRuntime.ShortestDelta(transform.position, targetPosition));
		}
		else
		{
			// 动作尚未起手或已结束时继续贴近，不能等下一次状态评估才恢复追击。
			TickChase();
			return;
		}

		TryCallNearbyWolves();
	}

	private Vector3? ResolveFleeSourcePosition()
	{
		return _hasThreatMemory ? _lastKnownThreatPosition : (Vector3?)null;
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
		ForgetThreat();
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
		if (_currentThreat == null || !_threatVisible) return false;
		float distance = DistanceTo(_currentThreat.transform);
		if (_currentState == WolfState.Attack && _attack.IsAttackLocked)
			return true;

		if (!IsAggressiveAdvanceActive() && _packCount < 2) return false;
		if (distance > attackTriggerDistance) return false;

		// 编队只引导接近方向，不能让已经进入攻击范围的狼继续找点而错过出手。
		return true;
	}

	/// <summary>追击条件：双狼以上且在追击范围内</summary>
	private bool ShouldChase()
	{
		if (_currentThreat == null || !_threatVisible) return false;
		if (IsChaseTargetBlockedByPathCost(_currentThreat)) return false;
		bool alreadyEngaged = _currentState == WolfState.Chase || _currentState == WolfState.Attack ||
			_currentState == WolfState.Search;
		float chaseDistance = alreadyEngaged ? chaseLossDistance : chaseTriggerDistance;

		if (IsAggressiveAdvanceActive())
			return IsWithinEffectivePerceptionRange(_currentThreat, chaseDistance);

		if (_packCount >= 2)
			return IsWithinEffectivePerceptionRange(_currentThreat, chaseDistance);
		return false;
	}

	/// <summary>避让条件：独狼在追击触发范围内</summary>
	private bool ShouldAvoid()
	{
		if (_currentThreat == null) return false;
		if (IsAggressiveAdvanceActive()) return false;
		if (_packCount > 1) return false;

		return IsWithinFleeDistance(
			_currentThreat,
			_currentState == WolfState.Avoid,
			chaseTriggerDistance,
			avoidSafeDistance);
	}

	/// <summary>警觉条件：威胁在警觉距离内，或警觉计时器未过期</summary>
	private bool ShouldAlert()
	{
		if (_currentThreat == null) return _alertTimer > 0f;
		if (IsChaseTargetBlockedByPathCost(_currentThreat)) return false;

		if (IsWithinEffectivePerceptionRange(_currentThreat, alertDetectDistance))
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
		// 有效同伴共用一份快照，避免追击编队再次遍历感知候选并查询阵营。
		_packAllies.Clear();
		_packCount = 1;
		_hasPackMate = false;
		_packCenter = transform.position;
		_alphaWolf = this;
		_isAlphaWolf = true;

		if (_detector.CurrentItemsInArea == null) return;

		int allyCount = 0;
		Vector3 allyPosSum = Vector3.zero;
		foreach (Item it in _detector.CurrentItemsInArea)
		{
			if (!TryGetWolfAlly(it, out AI_Wolf ally)) continue;

			_packAllies.Add(ally);
			_packCount++;
			allyCount++;
			allyPosSum += ally.transform.position;

			if (CompareChaseFormationMembers(ally, _alphaWolf) < 0)
			{
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
		if (!IsUsableThreat(_currentThreat) || !MatchesRememberedThreat(_currentThreat))
			ForgetThreat();
		_threatVisible = false;
		Item detected = IsAggressiveAdvanceActive()
			? FindClosestAdvanceAggressionTarget() : FindClosestPlayerThreat();
		if (detected != null)
		{
			RememberThreat(detected, detected.transform.position, true);
			return;
		}

		float threatRetentionDistance = _currentState == WolfState.Avoid
			? Mathf.Max(chaseLossDistance, avoidSafeDistance)
			: chaseLossDistance;
		if (_currentThreat != null && IsWithinEffectivePerceptionRange(_currentThreat, threatRetentionDistance))
		{
			RememberThreat(_currentThreat, _currentThreat.transform.position, true);
			return;
		}
		if (!_canSearchForThreat || !_hasThreatMemory)
			ForgetThreat();
	}

	private Item FindClosestAdvanceAggressionTarget()
	{
		List<Item> allItems = _detector.CurrentItemsInArea;
		if (allItems == null || allItems.Count == 0) return null;

		Item closest = null;
		float closestDistanceSqr = float.MaxValue;
		// 同一次威胁筛选只解析一次世界边界，候选距离仍逐个实时读取。
		WorldTopologyDomain topology = default;
		bool topologyResolved = false;
		Vector3 position = transform.position;
		float2 origin = new(position.x, position.y);
		foreach (Item candidate in allItems)
		{
			if (!IsUsableThreat(candidate) || !IsLivingActorTarget(candidate)) continue;

			if (!topologyResolved)
			{
				topology = WorldTopologyRuntime.GetActiveDomain();
				topologyResolved = true;
			}
			Vector3 target = candidate.transform.position;
			float distanceSqr = topology.SqrDistance(origin, new float2(target.x, target.y));
			if (distanceSqr < closestDistanceSqr &&
				IsWithinEffectivePerceptionRange(candidate, _detector.DetectionRadius))
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

		if (!FactionRelationService.CanAttack(item, target))
			return false;

		if (TryGetWolfAlly(target, out _))
			return false;

		Mod_DamageReceiver receiver = target.itemMods?.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
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
		List<Item> detectedItems = _detector?.CurrentItemsInArea;
		if (detectedItems == null || detectedItems.Count == 0)
			return null;

		Item closest = null;
		float closestDistanceSqr = float.MaxValue;
		WorldTopologyDomain topology = default;
		bool topologyResolved = false;
		Vector3 position = transform.position;
		float2 origin = new(position.x, position.y);
		for (int i = 0; i < detectedItems.Count; i++)
		{
			Item candidate = detectedItems[i];
			if (!IsUsableThreat(candidate) || !IsPlayerChaseTarget(candidate))
			{
				continue;
			}

			if (!topologyResolved)
			{
				topology = WorldTopologyRuntime.GetActiveDomain();
				topologyResolved = true;
			}
			Vector3 target = candidate.transform.position;
			float distanceSqr = topology.SqrDistance(origin, new float2(target.x, target.y));
			if (distanceSqr >= closestDistanceSqr ||
				!IsWithinEffectivePerceptionRange(candidate, _detector.DetectionRadius))
				continue;

			closest = candidate;
			closestDistanceSqr = distanceSqr;
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

	/// <summary>刷新追击槽位：同一目标的狼按稳定 ID 排序，并在左右浅扇形中分配不同目的地。</summary>
	private void RefreshChaseFormationTarget()
	{
		_chaseFormationMembers.Clear();

		if (!enableChaseFormation || !_threatVisible || _currentThreat == null || !IsPlayerChaseTarget(_currentThreat))
		{
			ClearChaseFormation();
			return;
		}

		_chaseFormationMembers.Add(this);
		for (int i = 0; i < _packAllies.Count; i++)
		{
			AI_Wolf ally = _packAllies[i];
			if (!IsEligibleChaseFormationMember(ally, _currentThreat) ||
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
		// 同一轮阵型只读取一次世界拓扑，成员位置及地形资格仍实时核验。
		WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
		int slotIndex = ResolveChaseFormationSlotIndex(_currentThreat.transform.position, topology);
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
			slotOffset + CalculateChaseSeparationOffset(topology),
			maxFormationRadius);

		Vector3 targetPosition = _currentThreat.transform.position;
		Vector3 formationTarget = NormalizeChasePosition(targetPosition + (Vector3)desiredOffset, topology);
		if (!IsWalkableFormationDestination(formationTarget))
		{
			// 分离偏移踩到障碍时，优先保留原槽位；槽位同样不可走时回退旧追击逻辑。
			formationTarget = NormalizeChasePosition(targetPosition + (Vector3)slotOffset, topology);
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
	private Vector2 CalculateChaseSeparationOffset(in WorldTopologyDomain topology)
	{
		float separationDistance = Mathf.Max(0.05f, chaseSeparationDistance);
		float maxOffset = Mathf.Max(0f, chaseSeparationMaxOffset);
		if (maxOffset <= 0f || chaseSeparationStrength <= 0f || _chaseFormationMembers.Count < 2)
			return Vector2.zero;

		float separationDistanceSqr = separationDistance * separationDistance;
		Vector2 separation = Vector2.zero;
		Vector3 wolfPosition = transform.position;
		for (int i = 0; i < _chaseFormationMembers.Count; i++)
		{
			AI_Wolf ally = _chaseFormationMembers[i];
			if (ally == null || ally == this)
				continue;

			Vector2 away = GetChaseDelta(ally.transform.position, wolfPosition, topology);
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
			wolf._currentThreat == threat && wolf._threatVisible &&
			wolf._currentState != WolfState.Avoid && wolf._currentState != WolfState.Flee;
	}

	/// <summary>优先占据自己所在的左右翼，避免为了固定编号从玩家身体中央穿过。</summary>
	private int ResolveChaseFormationSlotIndex(Vector3 targetPosition, in WorldTopologyDomain topology)
	{
		int leftLaneCount = 0;
		int rightLaneCount = 0;
		for (int memberIndex = 0; memberIndex < _chaseFormationMembers.Count; memberIndex++)
		{
			AI_Wolf member = _chaseFormationMembers[memberIndex];
			bool useRightLane = ShouldUseRightChaseLane(member, targetPosition, memberIndex, topology);
			int slotIndex = useRightLane
				? rightLaneCount++ * 2 + 1
				: leftLaneCount++ * 2;
			if (member == this)
				return slotIndex;
		}

		return -1;
	}

	/// <summary>横向位置明确时保留原侧；与玩家 X 轴重合时按稳定排序交替分翼。</summary>
	private static bool ShouldUseRightChaseLane(AI_Wolf wolf, Vector3 targetPosition, int stableMemberIndex,
		in WorldTopologyDomain topology)
	{
		if (wolf == null)
			return (stableMemberIndex & 1) != 0;

		return ResolveRightChaseLane(targetPosition, wolf.transform.position, stableMemberIndex, topology);
	}

	internal static bool ResolveRightChaseLane(Vector2 targetPosition, Vector2 wolfPosition, int stableMemberIndex,
		in WorldTopologyDomain topology)
	{
		float horizontalOffset = GetChaseDelta(targetPosition, wolfPosition, topology).x;
		if (Mathf.Abs(horizontalOffset) > 0.05f)
			return horizontalOffset > 0f;

		return (stableMemberIndex & 1) != 0;
	}

	internal static Vector2 GetChaseDelta(Vector2 from, Vector2 to, in WorldTopologyDomain topology)
	{
		float2 delta = topology.ShortestDelta(new float2(from.x, from.y), new float2(to.x, to.y));
		return new Vector2(delta.x, delta.y);
	}

	internal static Vector3 NormalizeChasePosition(Vector3 position, in WorldTopologyDomain topology)
	{
		float2 normalized = topology.Normalize(new float2(position.x, position.y));
		return new Vector3(normalized.x, normalized.y, position.z);
	}

	/// <summary>玩家身份只读取项目 ItemData Tag，不再依赖 Unity Tag。</summary>
	private bool IsPlayerChaseTarget(Item target)
	{
		return IsPlayerThreat(target);
	}

	/// <summary>站位到达后保留更宽的恢复边界，吸收刹车惯性和目标小幅移动。</summary>
	private bool ShouldHoldChaseFormationPosition(float approachRange)
	{
		if (!_hasChaseFormationTarget ||
			WorldTopologyRuntime.SqrDistance(transform.position, _lastKnownThreatPosition) >
			attackTriggerDistance * attackTriggerDistance)
		{
			_holdingChaseFormationPosition = false;
			return false;
		}

		float arrivalDistance = Mathf.Max(Mathf.Max(0.01f, chaseFormationArrivalDistance),
			_mover != null ? _mover.GetInteractionStopDistance(approachRange) : 0f);
		float tolerance = _holdingChaseFormationPosition
			? Mathf.Max(arrivalDistance, chaseFormationResumeDistance)
			: arrivalDistance;
		_holdingChaseFormationPosition =
			WorldTopologyRuntime.SqrDistance(transform.position, _chaseFormationTarget) <= tolerance * tolerance;
		return _holdingChaseFormationPosition;
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
		_holdingChaseFormationPosition = false;
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

		string lane = FlatWorldLocalizationService.GetUiText((slotIndex & 1) == 0 ? "左翼" : "右翼");
		return $"{lane}{slotIndex / 2 + 1}";
	}

	/// <summary>呼叫附近狼群同伴支援</summary>
	private void TryCallNearbyWolves()
	{
		if (!_threatVisible || _packCount < 2 || _packCallCooldownTimer > 0f) return;
		CallNearbyWolves(_currentThreat);
		_packCallCooldownTimer = packCallCooldown;
	}

	private void CallNearbyWolves(Item threatSource)
	{
		if (!_threatVisible || !MatchesRememberedThreat(threatSource)) return;

		List<Item> allItems = _detector.CurrentItemsInArea;
		if (allItems == null || allItems.Count == 0) return;

		foreach (Item it in allItems)
		{
			if (!TryGetWolfAlly(it, out AI_Wolf ally)) continue;
			if (!IsWithinEffectivePerceptionRange(it, allyCallDistance)) continue;
			ally.ReceivePackCall(threatSource, this);
		}
	}

	private bool TryGetWolfAlly(Item target, out AI_Wolf ally)
	{
		ally = null;
		if (target == null || target == item) return false;

		ally = target.itemMods?.GetMod_ByID<AI_Wolf>(ModText.AI);
		// 只有已就绪、启用且存活的同伴才能贡献群体人数和响应支援。
		if (ally == null || ally == this || !ally._isReady || !ally.Enabled || !ally.IsAlive)
			return false;

		if (FactionRelationService.GetRelation(item, target) != FactionRelation.Friendly)
			return false;

		// 新定义以 FactionId 为准；只有旧存档/旧 Prefab 才继续使用 Wolf 标签回退。
		if (!string.IsNullOrWhiteSpace(target.itemData?.FactionId))
			return true;

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
			case WolfState.Search: return animChase;
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
			case WolfState.Search:return "搜索最后位置";
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
		DrawRangeWithLabel(center, avoidSafeDistance, new Color(0.2f, 0.8f, 1f, 1f), "避让安全距离 avoidSafeDistance", 120f);
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
