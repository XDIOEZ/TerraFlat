using System;
using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;
using UltEvents;

public enum WildBoarState
{
	Idle,
	Move,
	Forage,
	Eat,
	Sleep,
	Alert,
	Chase,
	Attack,
	Flee
}

/// <summary>
/// 野猪 AI：支持觅食/进食/睡眠/愤怒值/攻击伤害窗口/逃跑等行为。
/// 状态优先级：逃跑 > 攻击 > 追击 > 警觉 > 睡眠 > 进食 > 觅食 > 移动 > 待机
/// </summary>
public partial class AI_WildBoar : AI_Base<WildBoarState>
{
	#region SaveData
	[Serializable]
	[MemoryPackable]
	public partial class AI_WildBoarSaveData
	{
		public WildBoarState State = WildBoarState.Idle;
		public float Fatigue01 = 0f;
		public float RageLevel = 0f;
	}
	#endregion

	#region ModuleData
	public AI_WildBoarSaveData Data = new AI_WildBoarSaveData();
	#endregion

	#region RuntimeState - WildBoar 特有
	[SerializeField, ReadOnly]
	private Item _currentFoodTarget;

	[SerializeField, ReadOnly]
	private Item _currentThreat;

	private float _sleepCooldownTimer;
	private float _alertCooldownTimer;
	private Vector3 _chaseTarget;
	private AI_AttackController _attack = new AI_AttackController();
	#endregion

	#region Config
	[TabGroup("配置", "调试"), HideLabel]
	public bool debugLog;

	[TabGroup("配置", "生存"), BoxGroup("配置/生存/饥饿"), HorizontalGroup("配置/生存/饥饿/Hr1"), LabelText("进食触发"), Range(0f, 1f)]
	public float eatEnterHungerRate = 0.4f;
	[HorizontalGroup("配置/生存/饥饿/Hr1"), LabelText("进食退出"), Range(0f, 1f)]
	public float eatExitHungerRate = 0.8f;

	[TabGroup("配置", "生存"), BoxGroup("配置/生存/睡眠"), HorizontalGroup("配置/生存/睡眠/Hr1"), LabelText("血量触发"), Range(0f, 1f)]
	public float sleepEnterHpRate = 0.45f;
	[HorizontalGroup("配置/生存/睡眠/Hr1"), LabelText("血量维持"), Range(0f, 1f)]
	public float sleepExitHpRate = 0.5f;

	[TabGroup("配置", "生存"), BoxGroup("配置/生存/睡眠"), HorizontalGroup("配置/生存/睡眠/Hr2"), LabelText("回血速度"), SuffixLabel("/秒", true)]
	public float sleepRecoverHpPerSecond = 5f;
	[HorizontalGroup("配置/生存/睡眠/Hr2"), LabelText("睡眠时长"), SuffixLabel("秒", true), MinValue(0.1f)]
	public float sleepDuration = 8f;

	[TabGroup("配置", "生存"), BoxGroup("配置/生存/睡眠"), LabelText("睡醒冷却"), SuffixLabel("秒", true), MinValue(0f)]
	public float sleepCooldown = 4f;

	[TabGroup("配置", "昼夜"), BoxGroup("配置/昼夜/时间"), HorizontalGroup("配置/昼夜/时间/Hr1"), LabelText("白天开始"), Range(0f, 1f)]
	public float dayStartRatio = 0.25f;
	[HorizontalGroup("配置/昼夜/时间/Hr1"), LabelText("白天结束"), Range(0f, 1f)]
	public float dayEndRatio = 0.75f;

	[TabGroup("配置", "昼夜"), BoxGroup("配置/昼夜/时间"), HorizontalGroup("配置/昼夜/时间/Hr2"), LabelText("黄昏开始"), Range(0f, 1f)]
	public float duskStartRatio = 0.65f;

	[TabGroup("配置", "昼夜"), BoxGroup("配置/昼夜/时间"), LabelText("黄昏时更具攻击性")]
	public bool aggressiveDuringDusk = true;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/移动觅食"), HorizontalGroup("配置/行为/移动觅食/Hr1"), LabelText("检测间隔"), SuffixLabel("秒", true), MinValue(0.05f)]
	public float detectorRefreshInterval = 1.0f;
	[HorizontalGroup("配置/行为/移动觅食/Hr1"), LabelText("进食距离"), SuffixLabel("米", true), MinValue(0.1f)]
	public float eatDistance = 1.5f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/移动觅食"), LabelText("启用闲逛")]
	public bool enableWander = true;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/移动觅食"), HorizontalGroup("配置/行为/移动觅食/H待机"), LabelText("待机最小"), SuffixLabel("秒", true), MinValue(0f)]
	public float idleMinDuration = 1f;
	[HorizontalGroup("配置/行为/移动觅食/H待机"), LabelText("待机最大"), SuffixLabel("秒", true), MinValue(0f)]
	public float idleMaxDuration = 3f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/移动觅食"), HorizontalGroup("配置/行为/移动觅食/H闲逛"), LabelText("闲逛半径"), SuffixLabel("米", true), MinValue(0.1f)]
	public float wanderRadius = 6f;
	[HorizontalGroup("配置/行为/移动觅食/H闲逛"), LabelText("到达距离"), SuffixLabel("米", true), MinValue(0.05f)]
	public float wanderStopDistance = 0.5f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/移动觅食"), HorizontalGroup("配置/行为/移动觅食/H停顿"), LabelText("停顿最小"), SuffixLabel("秒", true), MinValue(0f)]
	public float wanderPauseMin = 1f;
	[HorizontalGroup("配置/行为/移动觅食/H停顿"), LabelText("停顿最大"), SuffixLabel("秒", true), MinValue(0f)]
	public float wanderPauseMax = 3f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/移动觅食"), LabelText("可食物标签")]
	public List<string> edibleTags = new List<string> { "Food", "Corpse" };
	[TabGroup("配置", "行为"), BoxGroup("配置/行为/移动觅食"), HorizontalGroup("配置/行为/移动觅食/H避险"), LabelText("避开高权重")]
	public bool wanderAvoidHighPenalty = true;
	[HorizontalGroup("配置/行为/移动觅食/H避险"), LabelText("危险阈值"), MinValue(0)]
	public int wanderDangerPenalty = 1200;
	[HorizontalGroup("配置/行为/移动觅食/H避险"), LabelText("采样点"), MinValue(1)]
	public int wanderSampleCount = 8;
	[TabGroup("配置", "行为"), BoxGroup("配置/行为/移动觅食"), LabelText("权重惩罚系数"), MinValue(0f)]
	public float wanderPenaltyWeight = 1f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/警觉"), HorizontalGroup("配置/行为/警觉/Hr1"), LabelText("视范距离"), SuffixLabel("米", true), MinValue(0.1f)]
	public float alertDetectDistance = 8f;
	[HorizontalGroup("配置/行为/警觉/Hr1"), LabelText("持续时间"), SuffixLabel("秒", true), MinValue(0.1f)]
	public float alertDuration = 3f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/警觉"), LabelText("警觉延迟"), SuffixLabel("秒", true), MinValue(0f)]
	public float alertToChaseDuration = 1f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/追击"), HorizontalGroup("配置/行为/追击/Hr1"), LabelText("触发距离"), SuffixLabel("米", true), MinValue(0.1f)]
	public float chaseTriggerDistance = 12f;
	[HorizontalGroup("配置/行为/追击/Hr1"), LabelText("放弃距离"), SuffixLabel("米", true), MinValue(0.1f)]
	public float chaseLossDistance = 20f;

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/追击"), LabelText("威胁标签")]
	public List<string> chaseThreatTags = new List<string> { "Player" };

	[TabGroup("配置", "行为"), BoxGroup("配置/行为/追击"), LabelText("追击玩家")]
	public bool chasePlayer = true;

	[TabGroup("配置", "战斗"), BoxGroup("配置/战斗/攻击"), HorizontalGroup("配置/战斗/攻击/Hr1"), LabelText("触发距离"), SuffixLabel("米", true), MinValue(0.1f)]
	public float attackTriggerDistance = 2f;
	[HorizontalGroup("配置/战斗/攻击/Hr1"), LabelText("持续时间"), SuffixLabel("秒", true), MinValue(0.1f)]
	public float attackDuration = 1.5f;

	[TabGroup("配置", "战斗"), BoxGroup("配置/战斗/攻击"), HorizontalGroup("配置/战斗/攻击/Hr2"), LabelText("愤怒增长"), MinValue(0f)]
	public float rageBuildupRate = 0.5f;
	[HorizontalGroup("配置/战斗/攻击/Hr2"), LabelText("愤怒衰减"), MinValue(0f)]
	public float rageDecayRate = 0.2f;

	[TabGroup("配置", "战斗"), BoxGroup("配置/战斗/攻击"), LabelText("攻击冷却"), SuffixLabel("秒", true), MinValue(0f)]
	public float attackCooldown = 2f;
	[TabGroup("配置", "战斗"), BoxGroup("配置/战斗/攻击"), LabelText("攻击窗口"), SuffixLabel("秒", true), MinValue(0.01f)]
	public float attackDamageWindow = 0.2f;

	[TabGroup("配置", "战斗"), BoxGroup("配置/战斗/逃跑"), HorizontalGroup("配置/战斗/逃跑/Hr1"), LabelText("触发血量"), Range(0f, 1f)]
	public float fleeTriggerHpRate = 0.2f;
	[HorizontalGroup("配置/战斗/逃跑/Hr1"), LabelText("安全血量"), Range(0f, 1f)]
	public float fleeSafeHpRate = 0.4f;

	[TabGroup("配置", "战斗"), BoxGroup("配置/战斗/逃跑"), LabelText("逃离距离"), SuffixLabel("米", true), MinValue(1f)]
	public float fleeRunDistance = 10f;

	[TabGroup("配置", "动画"), BoxGroup("配置/动画/生存"), HorizontalGroup("配置/动画/生存/Hr1"), LabelText("待机")]
	public string animIdle = "Stand";
	[HorizontalGroup("配置/动画/生存/Hr1"), LabelText("移动")]
	public string animMove = "Move";
	[HorizontalGroup("配置/动画/生存/Hr1"), LabelText("睡眠")]
	public string animSleep = "Sleep";

	[TabGroup("配置", "动画"), BoxGroup("配置/动画/生存"), HorizontalGroup("配置/动画/生存/Hr2"), LabelText("觅食")]
	public string animForage = "Move";
	[HorizontalGroup("配置/动画/生存/Hr2"), LabelText("进食")]
	public string animEat = "Sit";

	[TabGroup("配置", "动画"), BoxGroup("配置/动画/战斗"), HorizontalGroup("配置/动画/战斗/Hr1"), LabelText("警觉")]
	public string animAlert = "Stand";
	[HorizontalGroup("配置/动画/战斗/Hr1"), LabelText("追击")]
	public string animChase = "Move";

	[TabGroup("配置", "动画"), BoxGroup("配置/动画/战斗"), HorizontalGroup("配置/动画/战斗/Hr2"), LabelText("攻击")]
	public string animAttack = "Attack";
	[HorizontalGroup("配置/动画/战斗/Hr2"), LabelText("逃跑")]
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
	protected override bool IsMoveState(WildBoarState state) => state == WildBoarState.Move;
	protected override bool IsIdleState(WildBoarState state) => state == WildBoarState.Idle;
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
		_sleepCooldownTimer = 0f;
		_alertCooldownTimer = 0f;
		_currentFoodTarget = null;
		_currentThreat = null;
		_attack.Reset();
		_attack.Cooldown = attackCooldown;
		_attack.DamageWindow = attackDamageWindow;
	}

	protected override void OnBindExtraModules()
	{
		item.itemMods.GetMod_ByID(ModText.Food, out Mod_Food food);
		_food = food;
		_attack.Bind(item);
	}

	protected override void OnValidateExtraModules()
	{
		if (_food == null)
		{
			Debug.LogError($"[{nameof(AI_WildBoar)}] 缺少食物模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (!_attack.HasDamageMods)
		{
			Debug.LogWarning($"[{nameof(AI_WildBoar)}] 未找到 Mod_Damage 组件，攻击不会造成伤害。目标物体: {name}", this);
		}
	}

	protected override void UpdateExtraTimers(float deltaTime)
	{
		_sleepCooldownTimer = DecrementTimer(_sleepCooldownTimer, deltaTime);
		_alertCooldownTimer = DecrementTimer(_alertCooldownTimer, deltaTime);
		_attack.Update(deltaTime);
		UpdateRageLevel(deltaTime);
	}

	protected override void OnPreEvaluate()
	{
		RefreshThreatTarget();
	}

	protected override void OnBeforeSwitchState(WildBoarState previous, WildBoarState next)
	{
		// 离开觅食/进食状态时清除食物目标
		if (next != WildBoarState.Forage && next != WildBoarState.Eat)
		{
			_currentFoodTarget = null;
		}

		// 离开所有战斗相关状态时清除威胁目标
		if (next != WildBoarState.Alert && next != WildBoarState.Chase
		    && next != WildBoarState.Attack && next != WildBoarState.Flee)
		{
			_currentThreat = null;
		}

		// 离开攻击状态：停止伤害窗口，进入冷却
		if (previous == WildBoarState.Attack && next != WildBoarState.Attack)
		{
			_attack.OnExitAttackState();
		}

		// 进入攻击状态：重置窗口触发标记
		if (next == WildBoarState.Attack)
		{
			_attack.OnEnterAttackState();
		}

		// 离开睡眠状态：进入睡醒冷却
		if (previous == WildBoarState.Sleep && next != WildBoarState.Sleep)
		{
			_sleepCooldownTimer = sleepCooldown;
		}
	}

	protected override string GetDebugExtraInfo()
	{
		return $" | 愤怒: {Data.RageLevel:F2}";
	}
	#endregion

	#region CachedModules - WildBoar 特有
	[SerializeField, ReadOnly] private Mod_Food _food;
	#endregion

	#region PublicAPI
	[Button("激怒野猪")]
	public void TriggerAlert(Item threatSource)
	{
		_currentThreat = threatSource;
		_alertCooldownTimer = alertDuration;
		if (debugLog)
		{
			Debug.Log($"[WildBoar] {name} 被激怒，威胁来源: {threatSource?.name}", this);
		}
	}
	#endregion

	#region StateMachine
	protected override WildBoarState EvaluateNextState()
	{
		if (ShouldFlee())    return WildBoarState.Flee;
		if (ShouldAttack())  return WildBoarState.Attack;
		if (ShouldChase())   return WildBoarState.Chase;
		if (ShouldAlert())   return WildBoarState.Alert;
		if (ShouldSleep())   return WildBoarState.Sleep;
		if (ShouldEat())     return WildBoarState.Eat;
		if (ShouldForage())  return WildBoarState.Forage;
		if (ShouldMoveBase())return WildBoarState.Move;
		return WildBoarState.Idle;
	}

	protected override void TickCurrentState(float deltaTime)
	{
		switch (_currentState)
		{
			case WildBoarState.Idle:   TickIdle(deltaTime); break;
			case WildBoarState.Move:   TickMove(deltaTime); break;
			case WildBoarState.Forage: TickForage();        break;
			case WildBoarState.Eat:    TickEat(deltaTime);  break;
			case WildBoarState.Sleep:  TickSleep(deltaTime);break;
			case WildBoarState.Alert:  TickAlert(deltaTime);break;
			case WildBoarState.Chase:  TickChase();         break;
			case WildBoarState.Attack: TickAttack();        break;
			case WildBoarState.Flee:   TickFlee();          break;
			default: throw new ArgumentOutOfRangeException();
		}
	}
	#endregion

	#region Tick - WildBoar 特有状态
	private void TickForage()
	{
		TryRefreshDetector();
		if (_currentFoodTarget == null)
		{
			_currentFoodTarget = FindClosestEdibleItem();
			if (_currentFoodTarget == null) { StopMove(); return; }
		}
		MoveTo(_currentFoodTarget.transform.position);
	}

	private void TickEat(float deltaTime)
	{
		StopMove();
		if (_currentFoodTarget == null)
		{
			_currentFoodTarget = FindClosestEdibleItem();
			if (_currentFoodTarget == null) return;
		}

		if (DistanceTo(_currentFoodTarget.transform) > eatDistance) return;

		Mod_Food targetFood = _currentFoodTarget.GetComponentInChildren<Mod_Food>();
		if (targetFood == null) { _currentFoodTarget = null; return; }

		_food.Eat(targetFood);
		if (_currentFoodTarget == null || _currentFoodTarget.itemData == null || _currentFoodTarget.itemData.Stack.Amount <= 0)
		{
			_currentFoodTarget = null;
		}
	}

	private void TickSleep(float deltaTime)
	{
		StopMove();
		_hp.Heal(deltaTime * sleepRecoverHpPerSecond, item);
	}

	private void TickAlert(float deltaTime)
	{
		StopMove();
		if (_currentThreat != null && DistanceTo(_currentThreat.transform) <= alertDetectDistance)
		{
			FaceTarget(_currentThreat.transform.position);
		}
	}

	private void TickChase()
	{
		if (_currentThreat == null) { StopMove(); return; }
		_chaseTarget = _currentThreat.transform.position;
		MoveTo(_chaseTarget);
	}

	private void TickAttack()
	{
		if (_currentThreat == null)
		{
			_attack.StopWindow();
			StopMove();
			return;
		}

		float distance = DistanceTo(_currentThreat.transform);
		if (distance <= attackTriggerDistance)
		{
			StopMove();
			if (!_attack.IsWindowTriggered && _attack.IsCooldownDone)
			{
				_attack.StartWindow(_animator, animAttack);
			}
		}
		else
		{
			_attack.StopWindow();
			MoveTo(_currentThreat.transform.position);
		}
	}

	private void TickFlee()
	{
		if (_currentThreat == null) { StopMove(); return; }
		MoveAwayFrom(_currentThreat.transform.position, fleeRunDistance);
	}
	#endregion

	#region Conditions
	/// <summary>逃跑条件：血量低于阈值（逃跑中需恢复到安全血量才停止）</summary>
	private bool ShouldFlee()
	{
		float hpRate = GetHpRate();
		return _currentState == WildBoarState.Flee
			? hpRate < fleeSafeHpRate
			: hpRate < fleeTriggerHpRate;
	}

	/// <summary>攻击条件：有威胁且距离/愤怒值满足要求</summary>
	private bool ShouldAttack()
	{
		if (_currentThreat == null) return false;
		float distance = DistanceTo(_currentThreat.transform);
		float rageLevel = Data.RageLevel;

		if (_currentState == WildBoarState.Attack)
		{
			return distance < chaseLossDistance && rageLevel > 0.1f;
		}

		// 从警觉或追击状态升级：愤怒值足够高时直接发起攻击
		if (_currentState == WildBoarState.Alert || _currentState == WildBoarState.Chase)
		{
			if (rageLevel >= 0.7f && distance < chaseLossDistance) return true;
		}

		return distance <= attackTriggerDistance && rageLevel > 0.3f;
	}

	private bool ShouldChase()
	{
		TryRefreshDetector();
		Item threat = FindClosestThreat();

		if (_currentState == WildBoarState.Chase)
		{
			if (threat == null) return false;
			float distance = DistanceTo(threat.transform);
			_currentThreat = threat;
			return distance < chaseLossDistance;
		}

		if (threat == null) return false;
		float threatDistance = DistanceTo(threat.transform);
		if (threatDistance > chaseTriggerDistance) return false;

		_currentThreat = threat;
		return true;
	}

	private bool ShouldAlert()
	{
		TryRefreshDetector();
		Item threat = FindThreatInAlertRange();

		if (_currentState == WildBoarState.Alert)
		{
			if (threat != null)
			{
				_currentThreat = threat;
				_alertCooldownTimer = alertDuration;
				return true;
			}
			if (_alertCooldownTimer > 0f) return true;
			_currentThreat = null;
			return false;
		}

		if (threat != null)
		{
			_currentThreat = threat;
			_alertCooldownTimer = alertDuration;
			return true;
		}
		return false;
	}

	private bool ShouldSleep()
	{
		float hpRate = GetHpRate();

		if (_currentState == WildBoarState.Sleep)
		{
			if (_stateElapsed < sleepDuration) return true;
			return IsNightTime() || hpRate < sleepExitHpRate;
		}

		if (_sleepCooldownTimer > 0f) return false;
		return IsNightTime() || hpRate < sleepEnterHpRate;
	}

	private bool ShouldEat()
	{
		float hungerRate = _food.Data.nutrition.GetFoodRate();
		if (_currentState == WildBoarState.Eat)
		{
			if (hungerRate >= eatExitHungerRate) return false;
			if (_currentFoodTarget == null)
			{
				_currentFoodTarget = FindClosestEdibleItem();
				if (_currentFoodTarget == null) return false;
			}
			return DistanceTo(_currentFoodTarget.transform) <= eatDistance;
		}

		if (hungerRate > eatEnterHungerRate) return false;
		if (_currentFoodTarget == null)
		{
			TryRefreshDetector();
			_currentFoodTarget = FindClosestEdibleItem();
			if (_currentFoodTarget == null) return false;
		}
		return DistanceTo(_currentFoodTarget.transform) <= eatDistance;
	}

	private bool ShouldForage()
	{
		float hungerRate = _food.Data.nutrition.GetFoodRate();
		if (hungerRate > eatEnterHungerRate) return false;

		if (_currentFoodTarget == null)
		{
			TryRefreshDetector();
			_currentFoodTarget = FindClosestEdibleItem();
			if (_currentFoodTarget == null) return false;
		}
		return DistanceTo(_currentFoodTarget.transform) > eatDistance;
	}
	#endregion

	#region Helpers - WildBoar 特有
	private void RefreshThreatTarget()
	{
		TryRefreshDetector();
		Item nearestThreat = FindClosestThreat();

		if (nearestThreat != null)
		{
			_currentThreat = nearestThreat;
			return;
		}

		// 没有检测到新威胁，清除现有威胁
		_currentThreat = null;
	}

	private void UpdateRageLevel(float deltaTime)
	{
		if (_currentThreat != null && (_currentState == WildBoarState.Alert || _currentState == WildBoarState.Chase || _currentState == WildBoarState.Attack))
		{
			Data.RageLevel = Mathf.Min(1f, Data.RageLevel + rageBuildupRate * deltaTime);
			if (aggressiveDuringDusk && IsDuskTime())
			{
				Data.RageLevel = Mathf.Min(1f, Data.RageLevel + rageBuildupRate * 0.5f * deltaTime);
			}
		}
		else
		{
			Data.RageLevel = Mathf.Max(0f, Data.RageLevel - rageDecayRate * deltaTime);
		}
	}

	private Item FindClosestThreat()
	{
		List<Item> threats = new List<Item>();

		List<Item> tagThreats = _detector.GetItemsByTags(chaseThreatTags);
		if (tagThreats != null) threats.AddRange(tagThreats);

		if (chasePlayer)
		{
			foreach (Item it in _detector.CurrentItemsInArea)
			{
				if (it != null && it.CompareTag("Player") && !threats.Contains(it))
					threats.Add(it);
			}
		}

		if (threats.Count == 0) return null;

		return threats
			.Where(x => x != null)
			.OrderBy(x => (x.transform.position - transform.position).sqrMagnitude)
			.FirstOrDefault();
	}

	private Item FindThreatInAlertRange()
	{
		List<Item> allItems = _detector.CurrentItemsInArea;
		if (allItems == null || allItems.Count == 0) return null;

		Item closestThreat = null;
		float closestDistance = float.MaxValue;

		foreach (Item item in allItems)
		{
			if (item == null) continue;

			bool isThreat = false;
			if (chasePlayer && item.CompareTag("Player")) isThreat = true;
			if (!isThreat && chaseThreatTags != null && item.itemData != null && item.itemData.Tags != null)
			{
				foreach (string tag in chaseThreatTags)
				{
					if (item.itemData.Tags.Contains(tag)) { isThreat = true; break; }
				}
			}
			if (item.name != null && chaseThreatTags != null && chaseThreatTags.Contains(item.name)) isThreat = true;

			if (isThreat)
			{
				float dist = DistanceTo(item.transform);
				if (dist < closestDistance && dist <= alertDetectDistance)
				{
					closestThreat = item;
					closestDistance = dist;
				}
			}
		}
		return closestThreat;
	}

	private Item FindClosestEdibleItem()
	{
		List<Item> items = _detector.GetItemsByTags(edibleTags);
		if (items.Count == 0) return null;

		return items
			.Where(x => x != null)
			.OrderBy(x => (x.transform.position - transform.position).sqrMagnitude)
			.FirstOrDefault();
	}

	private bool IsDayTime()
	{
		if (DayTimeSystem.Instance == null) return true;

		string sceneName = gameObject.scene.name;
		if (!DayTimeSystem.Instance.WorldTimeDict.TryGetValue(sceneName, out TimeData timeData)) return true;

		float dayLength = Mathf.Max(1f, timeData.DayLength);
		float normalized = Mathf.Repeat(DayTimeSystem.Instance.GetCurrentTime(sceneName), dayLength) / dayLength;
		return normalized >= dayStartRatio && normalized <= dayEndRatio;
	}

	private bool IsNightTime() => !IsDayTime();

	private bool IsDuskTime()
	{
		if (DayTimeSystem.Instance == null) return false;

		string sceneName = gameObject.scene.name;
		if (!DayTimeSystem.Instance.WorldTimeDict.TryGetValue(sceneName, out TimeData timeData)) return false;

		float dayLength = Mathf.Max(1f, timeData.DayLength);
		float normalized = Mathf.Repeat(DayTimeSystem.Instance.GetCurrentTime(sceneName), dayLength) / dayLength;
		return normalized >= duskStartRatio && normalized <= dayEndRatio;
	}
	#endregion

	#region Animation Mapping
	protected override string GetAnimationNameForState(WildBoarState state)
	{
		switch (state)
		{
			case WildBoarState.Idle:   return animIdle;
			case WildBoarState.Move:   return animMove;
			case WildBoarState.Forage: return animForage;
			case WildBoarState.Eat:    return animEat;
			case WildBoarState.Sleep:  return animSleep;
			case WildBoarState.Alert:  return animAlert;
			case WildBoarState.Chase:  return animChase;
			case WildBoarState.Attack: return animAttack;
			case WildBoarState.Flee:   return animFlee;
			default: throw new ArgumentOutOfRangeException(nameof(state), state, null);
		}
	}

	protected override string GetStateTextCN(WildBoarState state)
	{
		switch (state)
		{
			case WildBoarState.Idle:   return "待机";
			case WildBoarState.Move:   return "闲逛";
			case WildBoarState.Forage: return "觅食";
			case WildBoarState.Eat:    return "吃饭";
			case WildBoarState.Sleep:  return "睡觉";
			case WildBoarState.Alert:  return "警觉";
			case WildBoarState.Chase:  return "追击";
			case WildBoarState.Attack: return "攻击";
			case WildBoarState.Flee:   return "逃跑";
			default: throw new ArgumentOutOfRangeException(nameof(state), state, null);
		}
	}
	#endregion
}
