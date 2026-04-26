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

public partial class AI_WildBoar : Module
{
	#region SaveData
	[Serializable]
	[MemoryPackable]
	public partial class AI_WildBoarSaveData
	{
		public WildBoarState State = WildBoarState.Idle;
		public float Fatigue01 = 0f;
		public float RageLevel = 0f; // 0-1，用于控制攻击性
	}
	#endregion

	#region ModuleData
	public Ex_ModData_MemoryPackable ModData = new Ex_ModData_MemoryPackable();
	public override ModuleData _Data
	{
		get => ModData;
		set => ModData = (Ex_ModData_MemoryPackable)value;
	}

	public AI_WildBoarSaveData Data = new AI_WildBoarSaveData();
	#endregion

	#region RuntimeState
	[SerializeField, ReadOnly]
	private WildBoarState _currentState = WildBoarState.Idle;

	[SerializeField, ReadOnly]
	private float _stateElapsed;

	[SerializeField, ReadOnly]
	private bool _isReady;

	[SerializeField, ReadOnly]
	private Item _currentFoodTarget;

	[SerializeField, ReadOnly]
	private Item _currentThreat;

	private float _detectorRefreshTimer;
	private float _sleepCooldownTimer;
	private float _idleRemainTimer;
	private float _wanderWaitTimer;
	private Vector3 _wanderTarget;
	private bool _hasWanderTarget;
	private Vector3 _chaseTarget;
	private float _alertCooldownTimer;
	private float _rageBuildupTimer;
	private float _attackCooldownTimer;
	private float _attackWindowRemainTimer;
	private bool _attackWindowTriggered;
	private string _lastPlayedAnimation;
	private static GUIStyle _debugStateStyle;
	#endregion

	#region CachedModules
	[SerializeField, ReadOnly] private Mover_AI _mover;
	[SerializeField, ReadOnly] private Mod_Food _food;
	[SerializeField, ReadOnly] private DamageReceiver _hp;
	[SerializeField, ReadOnly] private Mod_ItemDetector _detector;
	[SerializeField, ReadOnly] private Mod_AnimatorController _animator;
	[SerializeField, ReadOnly] private List<Mod_Damage> _attackDamageMods = new List<Mod_Damage>();
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

	#region Events
	public UltEvent<WildBoarState, WildBoarState> OnStateChanged = new UltEvent<WildBoarState, WildBoarState>();
	#endregion

	#region Lifecycle
	public override void Awake()
	{
		if (string.IsNullOrEmpty(_Data.ID))
		{
			_Data.ID = ModText.AI;
		}
	}

	public override void Load()
	{
		ModData.ReadData(ref Data);

		_currentState = Data.State;
		_stateElapsed = 0f;
		_detectorRefreshTimer = 0f;
		_sleepCooldownTimer = 0f;
		_wanderWaitTimer = 0f;
		_idleRemainTimer = GetIdleDuration();
		_hasWanderTarget = false;
		_alertCooldownTimer = 0f;
		_rageBuildupTimer = 0f;
		_attackCooldownTimer = 0f;
		_attackWindowRemainTimer = 0f;
		_attackWindowTriggered = false;
		_lastPlayedAnimation = null;

		BindModules();
		PlayStateAnimation(_currentState, true);
	}

	public override void Save()
	{
		Data.State = _currentState;
		ModData.WriteData(Data);
	}

	public override void ModUpdate(float deltaTime)
	{
		if (!_isReady)
		{
			return;
		}

		_stateElapsed += deltaTime;
		_detectorRefreshTimer += deltaTime;

		if (_sleepCooldownTimer > 0f)
		{
			_sleepCooldownTimer = Mathf.Max(0f, _sleepCooldownTimer - deltaTime);
		}

		if (_wanderWaitTimer > 0f)
		{
			_wanderWaitTimer = Mathf.Max(0f, _wanderWaitTimer - deltaTime);
		}

		if (_alertCooldownTimer > 0f)
		{
			_alertCooldownTimer = Mathf.Max(0f, _alertCooldownTimer - deltaTime);
		}

		if (_attackCooldownTimer > 0f)
		{
			_attackCooldownTimer = Mathf.Max(0f, _attackCooldownTimer - deltaTime);
		}

		UpdateAttackDamageWindow(deltaTime);

		// 更新愤怒值
		UpdateRageLevel(deltaTime);

		AI_StateMachineRunner.EvaluateAndTick(
			_currentState,
			EvaluateNextState,
			SwitchState,
			TickCurrentState,
			deltaTime);
	}

	private void OnGUI()
	{
		if (!debugLog)
		{
			return;
		}

		if (!Application.isPlaying)
		{
			return;
		}

		if (Camera.main == null)
		{
			return;
		}

		Vector3 worldPos = transform.position + new Vector3(0f, 1.4f, 0f);
		Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
		if (screenPos.z <= 0f)
		{
			return;
		}

		if (_debugStateStyle == null)
		{
			_debugStateStyle = new GUIStyle(GUI.skin.box)
			{
				alignment = TextAnchor.MiddleCenter,
				fontSize = 16,
				normal = { textColor = Color.white }
			};
		}

		string text = $"状态: {GetStateTextCN(_currentState)} | 愤怒: {Data.RageLevel:F2}";
		Vector2 size = _debugStateStyle.CalcSize(new GUIContent(text));
		float width = Mathf.Max(150f, size.x + 14f);
		float height = 28f;
		Rect rect = new Rect(
			screenPos.x - width * 0.5f,
			Screen.height - screenPos.y - height * 0.5f,
			width,
			height);

		GUI.Box(rect, text, _debugStateStyle);
	}
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

	#region Init
	private void BindModules()
	{
		_isReady = true;

		item.itemMods.GetMod_ByID(ModText.Mover, out _mover);
		if (_mover == null)
		{
			item.itemMods.GetMod_ByID(ModText.Mover_AI, out _mover);
		}

		item.itemMods.GetMod_ByID(ModText.Food, out _food);
		item.itemMods.GetMod_ByID(ModText.Detector, out _detector);
		item.itemMods.GetMod_ByID(ModText.Hp, out _hp);
		item.GetMod(out _animator);
		_attackDamageMods = item.GetComponentsInChildren<Mod_Damage>(true).Where(x => x != null).ToList();
		SetAttackDamageEnabled(false);

		if (_mover == null)
		{
			Debug.LogError($"[{nameof(AI_WildBoar)}] 缺少移动模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_food == null)
		{
			Debug.LogError($"[{nameof(AI_WildBoar)}] 缺少食物模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_detector == null)
		{
			Debug.LogError($"[{nameof(AI_WildBoar)}] 缺少检测模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_hp == null)
		{
			Debug.LogError($"[{nameof(AI_WildBoar)}] 缺少生命值模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_animator == null)
		{
			Debug.LogWarning($"[{nameof(AI_WildBoar)}] 未找到动画模块，将跳过状态动画同步。目标物体: {name}", this);
		}

		if (_attackDamageMods.Count == 0)
		{
			Debug.LogWarning($"[{nameof(AI_WildBoar)}] 未找到 Mod_Damage 组件，攻击不会造成伤害。目标物体: {name}", this);
		}
	}
	#endregion

	#region StateMachine
	private WildBoarState EvaluateNextState()
	{
		// 优先级：逃跑 > 攻击 > 追击 > 警觉 > 睡眠 > 进食 > 觅食 > 闲逛 > 待机

		if (ShouldFlee())
		{
			return WildBoarState.Flee;
		}

		if (ShouldAttack())
		{
			return WildBoarState.Attack;
		}

		if (ShouldChase())
		{
			return WildBoarState.Chase;
		}

		if (ShouldAlert())
		{
			return WildBoarState.Alert;
		}

		if (ShouldSleep())
		{
			return WildBoarState.Sleep;
		}

		if (ShouldEat())
		{
			return WildBoarState.Eat;
		}

		if (ShouldForage())
		{
			return WildBoarState.Forage;
		}

		if (ShouldMove())
		{
			return WildBoarState.Move;
		}

		return WildBoarState.Idle;
	}

	private void SwitchState(WildBoarState next)
	{
		WildBoarState previous = _currentState;
		_currentState = next;
		_stateElapsed = 0f;

		if (next != WildBoarState.Move)
		{
			_hasWanderTarget = false;
		}

		if (next != WildBoarState.Forage && next != WildBoarState.Eat)
		{
			_currentFoodTarget = null;
		}

		if (next == WildBoarState.Idle)
		{
			_idleRemainTimer = GetIdleDuration();
		}

		if (previous == WildBoarState.Attack && next != WildBoarState.Attack)
		{
			StopAttackDamageWindow();
			_attackCooldownTimer = attackCooldown;
		}

		if (next == WildBoarState.Attack)
		{
			_attackWindowTriggered = false;
		}

		if (debugLog)
		{
			Debug.Log($"[WildBoarAI] {name} 状态切换: {previous} -> {next}", this);
		}

		if (previous == WildBoarState.Sleep && next != WildBoarState.Sleep)
		{
			_sleepCooldownTimer = sleepCooldown;
		}

		PlayStateAnimation(next);
		OnStateChanged?.Invoke(previous, next);
	}

	private void TickCurrentState(float deltaTime)
	{
		switch (_currentState)
		{
			case WildBoarState.Idle:
				TickIdle(deltaTime);
				break;
			case WildBoarState.Move:
				TickMove(deltaTime);
				break;
			case WildBoarState.Forage:
				TickForage();
				break;
			case WildBoarState.Eat:
				TickEat(deltaTime);
				break;
			case WildBoarState.Sleep:
				TickSleep(deltaTime);
				break;
			case WildBoarState.Alert:
				TickAlert(deltaTime);
				break;
			case WildBoarState.Chase:
				TickChase();
				break;
			case WildBoarState.Attack:
				TickAttack(deltaTime);
				break;
			case WildBoarState.Flee:
				TickFlee();
				break;
			default:
				throw new ArgumentOutOfRangeException();
		}
	}
	#endregion

	#region Tick
	private void TickIdle(float deltaTime)
	{
		_mover.CanMove = false;
		_mover.HasReachedTarget = true;
		if (_idleRemainTimer > 0f)
		{
			_idleRemainTimer = Mathf.Max(0f, _idleRemainTimer - deltaTime);
		}
	}

	private void TickMove(float deltaTime)
	{
		TickIdleWander(deltaTime);
	}

	private void TickForage()
	{
		TryRefreshDetector();
		if (_currentFoodTarget == null)
		{
			_currentFoodTarget = FindClosestEdibleItem();
			if (_currentFoodTarget == null)
			{
				_mover.CanMove = false;
				_mover.HasReachedTarget = true;
				return;
			}
		}

		_mover.CanMove = true;
		_mover.HasReachedTarget = false;
		_mover.TargetPosition = _currentFoodTarget.transform.position;
	}

	private void TickEat(float deltaTime)
	{
		_mover.CanMove = false;
		_mover.HasReachedTarget = true;

		if (_currentFoodTarget == null)
		{
			_currentFoodTarget = FindClosestEdibleItem();
			if (_currentFoodTarget == null)
			{
				return;
			}
		}

		float distance = Vector2.Distance(transform.position, _currentFoodTarget.transform.position);
		if (distance > eatDistance)
		{
			return;
		}

		Mod_Food targetFood = _currentFoodTarget.GetComponentInChildren<Mod_Food>();
		if (targetFood == null)
		{
			_currentFoodTarget = null;
			return;
		}

		_food.Eat(targetFood);

		if (_currentFoodTarget == null || _currentFoodTarget.itemData == null || _currentFoodTarget.itemData.Stack.Amount <= 0)
		{
			_currentFoodTarget = null;
		}
	}

	private void TickSleep(float deltaTime)
	{
		_mover.CanMove = false;
		_mover.HasReachedTarget = true;
		_hp.Heal(deltaTime * sleepRecoverHpPerSecond, item);
	}

	private void TickAlert(float deltaTime)
	{
		_mover.CanMove = false;
		_mover.HasReachedTarget = true;

		// 在警觉状态下看着威胁源（如果仍在范围内）
		if (_currentThreat != null)
		{
			float distance = Vector2.Distance(transform.position, _currentThreat.transform.position);
			if (distance <= alertDetectDistance)
			{
				Vector3 dirToThreat = (_currentThreat.transform.position - transform.position).normalized;
				_mover.TargetPosition = transform.position + dirToThreat;
			}
		}
	}

	private void TickChase()
	{
		if (_currentThreat == null)
		{
			_mover.CanMove = false;
			_mover.HasReachedTarget = true;
			return;
		}

		_chaseTarget = _currentThreat.transform.position;
		_mover.CanMove = true;
		_mover.HasReachedTarget = false;
		_mover.TargetPosition = _chaseTarget;
	}

	private void TickAttack(float deltaTime)
	{
		if (_currentThreat == null)
		{
			StopAttackDamageWindow();
			_attackWindowTriggered = false;
			_mover.CanMove = false;
			_mover.HasReachedTarget = true;
			return;
		}

		float distance = Vector2.Distance(transform.position, _currentThreat.transform.position);

		// 攻击范围内停止移动
		if (distance <= attackTriggerDistance)
		{
			_mover.CanMove = false;
			_mover.HasReachedTarget = true;

			if (!_attackWindowTriggered && _attackCooldownTimer <= 0f)
			{
				StartAttackDamageWindow();
			}
		}
		else
		{
			StopAttackDamageWindow();
			_attackWindowTriggered = false;

			// 超出攻击范围继续靠近
			_mover.CanMove = true;
			_mover.HasReachedTarget = false;
			_mover.TargetPosition = _currentThreat.transform.position;
		}
	}

	private void TickFlee()
	{
		if (_currentThreat == null)
		{
			_mover.CanMove = false;
			_mover.HasReachedTarget = true;
			return;
		}

		Vector3 awayDir = (transform.position - _currentThreat.transform.position).normalized;
		Vector3 fleeTarget = transform.position + awayDir * fleeRunDistance;

		_mover.CanMove = true;
		_mover.HasReachedTarget = false;
		_mover.TargetPosition = fleeTarget;
	}
	#endregion

	#region Conditions
	private bool ShouldFlee()
	{
		float hpRate = GetHpRate();

		if (_currentState == WildBoarState.Flee)
		{
			// 血量恢复到安全值才停止逃跑
			return hpRate < fleeSafeHpRate;
		}

		// 血量低到一定程度立即逃跑
		return hpRate < fleeTriggerHpRate;
	}

	private bool ShouldAttack()
	{
		if (_currentThreat == null)
		{
			return false;
		}

		float distance = Vector2.Distance(transform.position, _currentThreat.transform.position);
		float rageLevel = Data.RageLevel;

		if (_currentState == WildBoarState.Attack)
		{
			// 目标距离过远或愤怒值消退时停止攻击
			return distance < chaseLossDistance && rageLevel > 0.1f;
		}

		// 从警觉或追击状态升级：愤怒值足够高时直接发起攻击
		if (_currentState == WildBoarState.Alert || _currentState == WildBoarState.Chase)
		{
			if (rageLevel >= 0.7f && distance < chaseLossDistance)
			{
				return true;
			}
		}

		// 距离足够近且愤怒值足够高时开始攻击
		return distance <= attackTriggerDistance && rageLevel > 0.3f;
	}

	private bool ShouldChase()
	{
		TryRefreshDetector();
		Item threat = FindClosestThreat();

		if (_currentState == WildBoarState.Chase)
		{
			if (threat == null)
			{
				return false;
			}

			float distance = Vector2.Distance(transform.position, threat.transform.position);
			_currentThreat = threat;
			return distance < chaseLossDistance;
		}

		if (threat == null)
		{
			return false;
		}

		float threatDistance = Vector2.Distance(transform.position, threat.transform.position);
		if (threatDistance > chaseTriggerDistance)
		{
			return false;
		}

		_currentThreat = threat;
		return true;
	}

	private bool ShouldAlert()
	{
		TryRefreshDetector();
		Item threat = FindThreatInAlertRange();

		// 如果在警觉状态且仍然有威胁
		if (_currentState == WildBoarState.Alert)
		{
			if (threat != null)
			{
				_currentThreat = threat;
				_alertCooldownTimer = alertDuration;
				return true;
			}

			// 警觉状态下威胁消失，检查冷却计时是否还有剩余
			if (_alertCooldownTimer > 0f)
			{
				// 冷却期间维持警觉状态
				return true;
			}

			// 冷却结束，退出警觉
			_currentThreat = null;
			return false;
		}

		// 非警觉状态下发现新威胁
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
			if (_stateElapsed < sleepDuration)
			{
				return true;
			}

			return IsNightTime() || hpRate < sleepExitHpRate;
		}

		if (_sleepCooldownTimer > 0f)
		{
			return false;
		}

		return IsNightTime() || hpRate < sleepEnterHpRate;
	}

	private bool ShouldEat()
	{
		float hungerRate = _food.Data.nutrition.GetFoodRate();
		if (_currentState == WildBoarState.Eat)
		{
			if (hungerRate >= eatExitHungerRate)
			{
				return false;
			}

			if (_currentFoodTarget == null)
			{
				_currentFoodTarget = FindClosestEdibleItem();
				if (_currentFoodTarget == null)
				{
					return false;
				}
			}

			return Vector2.Distance(transform.position, _currentFoodTarget.transform.position) <= eatDistance;
		}

		if (hungerRate > eatEnterHungerRate)
		{
			return false;
		}

		if (_currentFoodTarget == null)
		{
			TryRefreshDetector();
			_currentFoodTarget = FindClosestEdibleItem();
			if (_currentFoodTarget == null)
			{
				return false;
			}
		}

		return Vector2.Distance(transform.position, _currentFoodTarget.transform.position) <= eatDistance;
	}

	private bool ShouldForage()
	{
		float hungerRate = _food.Data.nutrition.GetFoodRate();
		if (hungerRate > eatEnterHungerRate)
		{
			return false;
		}

		if (_currentFoodTarget == null)
		{
			TryRefreshDetector();
			_currentFoodTarget = FindClosestEdibleItem();
			if (_currentFoodTarget == null)
			{
				return false;
			}
		}

		return Vector2.Distance(transform.position, _currentFoodTarget.transform.position) > eatDistance;
	}

	private bool ShouldMove()
	{
		if (_currentState == WildBoarState.Move)
		{
			return _hasWanderTarget;
		}

		if (!enableWander)
		{
			return false;
		}

		if (_idleRemainTimer > 0f)
		{
			return false;
		}

		if (_wanderWaitTimer > 0f)
		{
			return false;
		}

		return true;
	}
	#endregion

	#region Helpers
	private void UpdateRageLevel(float deltaTime)
	{
		if (_currentThreat != null && (_currentState == WildBoarState.Alert || _currentState == WildBoarState.Chase || _currentState == WildBoarState.Attack))
		{
			// 在看到威胁时积累愤怒值
			Data.RageLevel = Mathf.Min(1f, Data.RageLevel + rageBuildupRate * deltaTime);

			// 黄昏时期增加愤怒值积累速度
			if (aggressiveDuringDusk && IsDuskTime())
			{
				Data.RageLevel = Mathf.Min(1f, Data.RageLevel + rageBuildupRate * 0.5f * deltaTime);
			}
		}
		else
		{
			// 愤怒值自然消退
			Data.RageLevel = Mathf.Max(0f, Data.RageLevel - rageDecayRate * deltaTime);
		}
	}

	private void TryRefreshDetector()
	{
		if (_detectorRefreshTimer < detectorRefreshInterval)
		{
			return;
		}

		_detectorRefreshTimer = 0f;
		_detector.Update_Detector();
	}

	private void TickIdleWander(float deltaTime)
	{
		if (!enableWander)
		{
			_mover.CanMove = false;
			_mover.HasReachedTarget = true;
			return;
		}

		if (_hasWanderTarget)
		{
			float distance = Vector2.Distance(transform.position, _wanderTarget);
			if (distance <= wanderStopDistance || _mover.HasReachedTarget)
			{
				_hasWanderTarget = false;
				_wanderWaitTimer = GetWanderPauseDuration();
				_mover.CanMove = false;
				_mover.HasReachedTarget = true;
				return;
			}

			_mover.CanMove = true;
			_mover.HasReachedTarget = false;
			_mover.TargetPosition = _wanderTarget;
			return;
		}

		if (_wanderWaitTimer > 0f)
		{
			_mover.CanMove = false;
			_mover.HasReachedTarget = true;
			return;
		}

		Vector2 offset = UnityEngine.Random.insideUnitCircle * wanderRadius;
		offset = AI_WanderUtility.PickSaferOffset(
			transform.position,
			offset,
			wanderRadius,
			wanderAvoidHighPenalty,
			wanderSampleCount,
			(uint)Mathf.Max(0, wanderDangerPenalty),
			wanderPenaltyWeight);
		_wanderTarget = new Vector3(transform.position.x + offset.x, transform.position.y + offset.y, transform.position.z);
		_hasWanderTarget = true;
		_mover.CanMove = true;
		_mover.HasReachedTarget = false;
		_mover.TargetPosition = _wanderTarget;
	}

	private float GetWanderPauseDuration()
	{
		if (wanderPauseMax <= wanderPauseMin)
		{
			return Mathf.Max(0f, wanderPauseMin);
		}

		return UnityEngine.Random.Range(Mathf.Max(0f, wanderPauseMin), Mathf.Max(0f, wanderPauseMax));
	}

	private float GetIdleDuration()
	{
		if (idleMaxDuration <= idleMinDuration)
		{
			return Mathf.Max(0f, idleMinDuration);
		}

		return UnityEngine.Random.Range(Mathf.Max(0f, idleMinDuration), Mathf.Max(0f, idleMaxDuration));
	}

	private Item FindClosestThreat()
	{
		List<Item> threats = new List<Item>();

		// 检索追击目标标签
		List<Item> tagThreats = _detector.GetItemsByTags(chaseThreatTags);
		if (tagThreats != null)
		{
			threats.AddRange(tagThreats);
		}

		// 检查玩家
		if (chasePlayer)
		{
			foreach (Item it in _detector.CurrentItemsInArea)
			{
				if (it != null && it.CompareTag("Player") && !threats.Contains(it))
					threats.Add(it);
			}
		}

		if (threats.Count == 0)
			return null;

		return threats
			.Where(x => x != null)
			.OrderBy(x => (x.transform.position - transform.position).sqrMagnitude)
			.FirstOrDefault();
	}

	private Item FindThreatInAlertRange()
	{
		List<Item> allItems = _detector.CurrentItemsInArea;
		if (allItems == null || allItems.Count == 0)
		{
			return null;
		}

		Item closestThreat = null;
		float closestDistance = float.MaxValue;

		foreach (Item item in allItems)
		{
			if (item == null)
				continue;

			if (chasePlayer && item.CompareTag("Player"))
			{
				float dist = Vector2.Distance(transform.position, item.transform.position);
				if (dist < closestDistance && dist <= alertDetectDistance)
				{
					closestThreat = item;
					closestDistance = dist;
				}
			}

			if (chaseThreatTags.Contains(item.name) || (item.itemData != null && item.itemData.Tags != null))
			{
				float dist = Vector2.Distance(transform.position, item.transform.position);
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
		if (items.Count == 0)
		{
			return null;
		}

		return items
			.Where(x => x != null)
			.OrderBy(x => (x.transform.position - transform.position).sqrMagnitude)
			.FirstOrDefault();
	}

	private bool IsDayTime()
	{
		if (DayTimeSystem.Instance == null)
		{
			return true;
		}

		string sceneName = gameObject.scene.name;
		if (!DayTimeSystem.Instance.WorldTimeDict.TryGetValue(sceneName, out TimeData timeData))
		{
			return true;
		}

		float dayLength = Mathf.Max(1f, timeData.DayLength);
		float normalized = Mathf.Repeat(DayTimeSystem.Instance.GetCurrentTime(sceneName), dayLength) / dayLength;
		return normalized >= dayStartRatio && normalized <= dayEndRatio;
	}

	private bool IsNightTime()
	{
		return !IsDayTime();
	}

	private bool IsDuskTime()
	{
		if (DayTimeSystem.Instance == null)
		{
			return false;
		}

		string sceneName = gameObject.scene.name;
		if (!DayTimeSystem.Instance.WorldTimeDict.TryGetValue(sceneName, out TimeData timeData))
		{
			return false;
		}

		float dayLength = Mathf.Max(1f, timeData.DayLength);
		float normalized = Mathf.Repeat(DayTimeSystem.Instance.GetCurrentTime(sceneName), dayLength) / dayLength;
		return normalized >= duskStartRatio && normalized <= dayEndRatio;
	}

	private float GetHpRate()
	{
		if (_hp.MaxHp <= 0f)
		{
			Debug.LogError($"[{nameof(AI_WildBoar)}] MaxHp 小于等于 0，无法计算血量百分比。目标物体: {name}", this);
			return 0f;
		}

		return _hp.Hp / _hp.MaxHp;
	}

	private string GetAnimationNameForState(WildBoarState state)
	{
		switch (state)
		{
			case WildBoarState.Idle:
				return animIdle;
			case WildBoarState.Move:
				return animMove;
			case WildBoarState.Forage:
				return animForage;
			case WildBoarState.Eat:
				return animEat;
			case WildBoarState.Sleep:
				return animSleep;
			case WildBoarState.Alert:
				return animAlert;
			case WildBoarState.Chase:
				return animChase;
			case WildBoarState.Attack:
				return animAttack;
			case WildBoarState.Flee:
				return animFlee;
			default:
				throw new ArgumentOutOfRangeException(nameof(state), state, null);
		}
	}

	private string GetStateTextCN(WildBoarState state)
	{
		switch (state)
		{
			case WildBoarState.Idle:
				return "待机";
			case WildBoarState.Move:
				return "闲逛";
			case WildBoarState.Forage:
				return "觅食";
			case WildBoarState.Eat:
				return "吃饭";
			case WildBoarState.Sleep:
				return "睡觉";
			case WildBoarState.Alert:
				return "警觉";
			case WildBoarState.Chase:
				return "追击";
			case WildBoarState.Attack:
				return "攻击";
			case WildBoarState.Flee:
				return "逃跑";
			default:
				throw new ArgumentOutOfRangeException(nameof(state), state, null);
		}
	}

	private void PlayStateAnimation(WildBoarState state, bool force = false)
	{
		if (_animator == null)
		{
			return;
		}

		string animationName = GetAnimationNameForState(state);
		if (string.IsNullOrEmpty(animationName))
		{
			Debug.LogError($"[{nameof(AI_WildBoar)}] 状态 {state} 未配置动画名。目标物体: {name}", this);
			return;
		}

		if (!force && _lastPlayedAnimation == animationName)
		{
			return;
		}

		_animator.PlayAnimation(animationName);
		_lastPlayedAnimation = animationName;
	}

	private void StartAttackDamageWindow()
	{
		_attackWindowTriggered = true;
		_attackWindowRemainTimer = Mathf.Max(0.01f, attackDamageWindow);
		SetAttackDamageEnabled(true);
	}

	private void StopAttackDamageWindow()
	{
		_attackWindowRemainTimer = 0f;
		SetAttackDamageEnabled(false);
	}

	private void UpdateAttackDamageWindow(float deltaTime)
	{
		if (_attackWindowRemainTimer <= 0f)
		{
			return;
		}

		_attackWindowRemainTimer = Mathf.Max(0f, _attackWindowRemainTimer - deltaTime);
		if (_attackWindowRemainTimer > 0f)
		{
			return;
		}

		SetAttackDamageEnabled(false);
		_attackWindowTriggered = false;
		if (attackCooldown > 0f)
		{
			_attackCooldownTimer = Mathf.Max(_attackCooldownTimer, attackCooldown);
		}
	}

	private void SetAttackDamageEnabled(bool enabled)
	{
		for (int i = 0; i < _attackDamageMods.Count; i++)
		{
			Mod_Damage damageMod = _attackDamageMods[i];
			if (damageMod == null)
			{
				continue;
			}

			if (enabled)
			{
				damageMod.StartAttack();
			}
			else
			{
				damageMod.StopAttack();
			}
		}
	}
	#endregion
}
