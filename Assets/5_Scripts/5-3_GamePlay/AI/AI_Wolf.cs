using System;
using System.Collections.Generic;
using System.Linq;
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

public partial class AI_Wolf : Module
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
	public Ex_ModData_MemoryPackable ModData = new Ex_ModData_MemoryPackable();
	public override ModuleData _Data
	{
		get => ModData;
		set => ModData = (Ex_ModData_MemoryPackable)value;
	}

	public AI_WolfSaveData Data = new AI_WolfSaveData();
#endregion

#region RuntimeState
	[SerializeField, ReadOnly]
	private WolfState _currentState = WolfState.Idle;

	[SerializeField, ReadOnly]
	private float _stateElapsed;

	[SerializeField, ReadOnly]
	private bool _isReady;

	[SerializeField, ReadOnly]
	private Item _currentThreat;

	[SerializeField, ReadOnly]
	private int _packCount = 1;

	[SerializeField, ReadOnly]
	private bool _isAlphaWolf = true;

	[SerializeField, ReadOnly]
	private AI_Wolf _alphaWolf;

	private float _detectorRefreshTimer;
	private float _alertTimer;
	private float _packAssistTimer;
	private float _packCallCooldownTimer;
	private float _idleRemainTimer;
	private float _wanderWaitTimer;
	private Vector3 _wanderTarget;
	private Vector3 _packCenter;
	private bool _hasWanderTarget;
	private bool _hasPackMate;
	private string _lastPlayedAnimation;
	private static GUIStyle _debugStateStyle;
#endregion

#region CachedModules
	[SerializeField, ReadOnly] private Mover_AI _mover;
	[SerializeField, ReadOnly] private DamageReceiver _hp;
	[SerializeField, ReadOnly] private Mod_ItemDetector _detector;
	[SerializeField, ReadOnly] private Mod_AnimatorController _animator;
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
	public float attackTriggerDistance = 2f;
	[HorizontalGroup("配置/行为/战斗/Hr2"), LabelText("追击放弃"), SuffixLabel("米", true), MinValue(0.1f)]
	public float chaseLossDistance = 22f;

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

#region Events
	public UltEvent<WolfState, WolfState> OnStateChanged = new UltEvent<WolfState, WolfState>();
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
		_alertTimer = 0f;
		_packAssistTimer = 0f;
		_packCallCooldownTimer = 0f;
		_idleRemainTimer = GetIdleDuration();
		_wanderWaitTimer = 0f;
		_hasWanderTarget = false;
		_hasPackMate = false;
		_isAlphaWolf = true;
		_alphaWolf = this;
		_packCenter = transform.position;
		_currentThreat = null;
		_lastPlayedAnimation = null;

		BindModules();
		TryRefreshDetector();
		RefreshPackStatus();
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

		if (_alertTimer > 0f)
		{
			_alertTimer = Mathf.Max(0f, _alertTimer - deltaTime);
		}

		if (_packAssistTimer > 0f)
		{
			_packAssistTimer = Mathf.Max(0f, _packAssistTimer - deltaTime);
		}

		if (_packCallCooldownTimer > 0f)
		{
			_packCallCooldownTimer = Mathf.Max(0f, _packCallCooldownTimer - deltaTime);
		}

		if (_wanderWaitTimer > 0f)
		{
			_wanderWaitTimer = Mathf.Max(0f, _wanderWaitTimer - deltaTime);
		}

		TryRefreshDetector();
		RefreshPackStatus();
		RefreshThreatTarget();

		WolfState next = EvaluateNextState();
		if (next != _currentState)
		{
			SwitchState(next);
		}

		TickCurrentState(deltaTime);
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

		string roleText = _isAlphaWolf ? "头狼" : "跟随";
		string text = $"状态: {GetStateTextCN(_currentState)} | 狼群数: {_packCount} | 角色: {roleText}";
		Vector2 size = _debugStateStyle.CalcSize(new GUIContent(text));
		float width = Mathf.Max(220f, size.x + 14f);
		float height = 28f;
		Rect rect = new Rect(
			screenPos.x - width * 0.5f,
			Screen.height - screenPos.y - height * 0.5f,
			width,
			height);

		GUI.Box(rect, text, _debugStateStyle);
	}
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
		if (radius <= 0f)
		{
			return;
		}

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

#region Init
	private void BindModules()
	{
		_isReady = true;

		item.itemMods.GetMod_ByID(ModText.Mover, out _mover);
		if (_mover == null)
		{
			item.itemMods.GetMod_ByID(ModText.Mover_AI, out _mover);
		}

		item.itemMods.GetMod_ByID(ModText.Detector, out _detector);
		item.itemMods.GetMod_ByID(ModText.Hp, out _hp);
		item.GetMod(out _animator);

		if (_mover == null)
		{
			Debug.LogError($"[{nameof(AI_Wolf)}] 缺少移动模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_detector == null)
		{
			Debug.LogError($"[{nameof(AI_Wolf)}] 缺少检测模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_hp == null)
		{
			Debug.LogError($"[{nameof(AI_Wolf)}] 缺少生命值模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_animator == null)
		{
			Debug.LogWarning($"[{nameof(AI_Wolf)}] 未找到动画模块，将跳过状态动画同步。目标物体: {name}", this);
		}
	}
#endregion

#region StateMachine
	private WolfState EvaluateNextState()
	{
		if (ShouldFlee())
		{
			return WolfState.Flee;
		}

		if (ShouldAttack())
		{
			return WolfState.Attack;
		}

		if (ShouldChase())
		{
			return WolfState.Chase;
		}

		if (ShouldAvoid())
		{
			return WolfState.Avoid;
		}

		if (ShouldAlert())
		{
			return WolfState.Alert;
		}

		if (ShouldMove())
		{
			return WolfState.Move;
		}

		return WolfState.Idle;
	}

	private void SwitchState(WolfState next)
	{
		WolfState previous = _currentState;
		_currentState = next;
		_stateElapsed = 0f;

		if (next != WolfState.Move)
		{
			_hasWanderTarget = false;
		}

		if (next == WolfState.Idle)
		{
			_idleRemainTimer = GetIdleDuration();
		}

		if (next != WolfState.Alert && next != WolfState.Chase && next != WolfState.Attack && next != WolfState.Avoid && next != WolfState.Flee)
		{
			if (_packAssistTimer <= 0f)
			{
				_currentThreat = null;
			}
		}

		if (debugLog)
		{
			Debug.Log($"[WolfAI] {name} 状态切换: {previous} -> {next} | 狼群数={_packCount}", this);
		}

		PlayStateAnimation(next);
		OnStateChanged?.Invoke(previous, next);
	}

	private void TickCurrentState(float deltaTime)
	{
		switch (_currentState)
		{
			case WolfState.Idle:
				TickIdle(deltaTime);
				break;
			case WolfState.Move:
				TickMove(deltaTime);
				break;
			case WolfState.Alert:
				TickAlert();
				break;
			case WolfState.Chase:
				TickChase();
				break;
			case WolfState.Attack:
				TickAttack();
				break;
			case WolfState.Avoid:
				TickAvoid();
				break;
			case WolfState.Flee:
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

	private void TickAlert()
	{
		_mover.CanMove = false;
		_mover.HasReachedTarget = true;

		if (_currentThreat == null)
		{
			return;
		}

		Vector3 dir = (_currentThreat.transform.position - transform.position).normalized;
		_mover.TargetPosition = transform.position + dir;
	}

	private void TickChase()
	{
		if (_currentThreat == null)
		{
			_mover.CanMove = false;
			_mover.HasReachedTarget = true;
			return;
		}

		_mover.CanMove = true;
		_mover.HasReachedTarget = false;
		_mover.TargetPosition = _currentThreat.transform.position;

		if (_packCount >= 2 && _packCallCooldownTimer <= 0f)
		{
			CallNearbyWolves(_currentThreat);
			_packCallCooldownTimer = packCallCooldown;
		}
	}

	private void TickAttack()
	{
		if (_currentThreat == null)
		{
			_mover.CanMove = false;
			_mover.HasReachedTarget = true;
			return;
		}

		float distance = Vector2.Distance(transform.position, _currentThreat.transform.position);
		if (distance <= attackTriggerDistance)
		{
			_mover.CanMove = false;
			_mover.HasReachedTarget = true;
		}
		else
		{
			_mover.CanMove = true;
			_mover.HasReachedTarget = false;
			_mover.TargetPosition = _currentThreat.transform.position;
		}

		if (_packCount >= 2 && _packCallCooldownTimer <= 0f)
		{
			CallNearbyWolves(_currentThreat);
			_packCallCooldownTimer = packCallCooldown;
		}
	}

	private void TickAvoid()
	{
		if (_currentThreat == null)
		{
			_mover.CanMove = false;
			_mover.HasReachedTarget = true;
			return;
		}

		Vector3 awayDir = (transform.position - _currentThreat.transform.position).normalized;
		Vector3 target = transform.position + awayDir * avoidRunDistance;

		_mover.CanMove = true;
		_mover.HasReachedTarget = false;
		_mover.TargetPosition = target;
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
		if (_currentThreat == null)
		{
			return false;
		}

		if (_packCount != 2)
		{
			return false;
		}

		float hpRate = GetHpRate();
		if (_currentState == WolfState.Flee)
		{
			return hpRate < fleeSafeHpRate;
		}

		return hpRate < fleeTriggerHpRate;
	}

	private bool ShouldAttack()
	{
		if (_currentThreat == null)
		{
			return false;
		}

		float distance = Vector2.Distance(transform.position, _currentThreat.transform.position);

		if (_packCount >= 3)
		{
			return distance <= chaseLossDistance;
		}

		if (_packCount == 2)
		{
			return distance <= attackTriggerDistance;
		}

		return false;
	}

	private bool ShouldChase()
	{
		if (_currentThreat == null)
		{
			return false;
		}

		float distance = Vector2.Distance(transform.position, _currentThreat.transform.position);

		if (_packCount >= 2)
		{
			if (_currentState == WolfState.Chase)
			{
				return distance <= chaseLossDistance;
			}

			return distance <= chaseTriggerDistance;
		}

		return false;
	}

	private bool ShouldAvoid()
	{
		if (_currentThreat == null)
		{
			return false;
		}

		if (_packCount > 1)
		{
			return false;
		}

		float distance = Vector2.Distance(transform.position, _currentThreat.transform.position);
		return distance <= chaseTriggerDistance;
	}

	private bool ShouldAlert()
	{
		if (_currentThreat == null)
		{
			return _alertTimer > 0f;
		}

		float distance = Vector2.Distance(transform.position, _currentThreat.transform.position);
		if (distance <= alertDetectDistance)
		{
			_alertTimer = alertKeepDuration;
			return true;
		}

		return _alertTimer > 0f;
	}

	private bool ShouldMove()
	{
		if (_currentState == WolfState.Move)
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
	private void TryRefreshDetector()
	{
		if (_detectorRefreshTimer < detectorRefreshInterval)
		{
			return;
		}

		_detectorRefreshTimer = 0f;
		_detector.Update_Detector();
	}

	private void RefreshPackStatus()
	{
		_packCount = 1;
		_hasPackMate = false;
		_packCenter = transform.position;
		_alphaWolf = this;
		_isAlphaWolf = true;

		if (_detector.CurrentItemsInArea == null)
		{
			return;
		}

		int allyCount = 0;
		Vector3 allyPosSum = Vector3.zero;
		int alphaPriority = GetAlphaPriority(this);

		foreach (Item it in _detector.CurrentItemsInArea)
		{
			if (!TryGetWolfAlly(it, out AI_Wolf ally))
			{
				continue;
			}

			_packCount++;
			allyCount++;
			allyPosSum += ally.transform.position;

			int allyPriority = GetAlphaPriority(ally);
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

	private int GetAlphaPriority(AI_Wolf wolf)
	{
		if (wolf == null)
		{
			return int.MaxValue;
		}

		return wolf.GetInstanceID();
	}

	private void RefreshThreatTarget()
	{
		Item nearestPlayer = FindClosestPlayerThreat();

		if (nearestPlayer != null)
		{
			_currentThreat = nearestPlayer;
			return;
		}

		if (_currentThreat == null)
		{
			return;
		}

		if (_packAssistTimer > 0f)
		{
			return;
		}

		_currentThreat = null;
	}

	private Item FindClosestPlayerThreat()
	{
		List<Item> allItems = _detector.CurrentItemsInArea;
		if (allItems == null || allItems.Count == 0)
		{
			return null;
		}

		Item closest = null;
		float closestDistance = float.MaxValue;

		foreach (Item it in allItems)
		{
			if (it == null)
			{
				continue;
			}

			if (!IsPlayerThreat(it))
			{
				continue;
			}

			float distance = Vector2.Distance(transform.position, it.transform.position);
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
		if (target == null)
		{
			return false;
		}

		if (target.itemData == null || target.itemData.Tags == null || target.itemData.Tags.Count == 0)
		{
			return false;
		}

		if (playerTags == null || playerTags.Count == 0)
		{
			return false;
		}

		foreach (string playerTag in playerTags)
		{
			if (string.IsNullOrEmpty(playerTag))
			{
				continue;
			}

			if (target.itemData.Tags.Contains(playerTag))
			{
				return true;
			}
		}

		return false;
	}

	private void CallNearbyWolves(Item threatSource)
	{
		if (threatSource == null)
		{
			return;
		}

		List<Item> allItems = _detector.CurrentItemsInArea;
		if (allItems == null || allItems.Count == 0)
		{
			return;
		}

		foreach (Item it in allItems)
		{
			if (!TryGetWolfAlly(it, out AI_Wolf ally))
			{
				continue;
			}

			float distance = Vector2.Distance(transform.position, ally.transform.position);
			if (distance > allyCallDistance)
			{
				continue;
			}

			ally.ReceivePackCall(threatSource, this);
		}
	}

	private bool TryGetWolfAlly(Item target, out AI_Wolf ally)
	{
		ally = null;
		if (target == null)
		{
			return false;
		}

		if (target == item)
		{
			return false;
		}

		ally = target.GetComponentInChildren<AI_Wolf>();
		if (ally == null || ally == this)
		{
			return false;
		}

		if (wolfTags == null || wolfTags.Count == 0)
		{
			return true;
		}

		if (target.itemData == null || target.itemData.Tags == null || target.itemData.Tags.Count == 0)
		{
			return false;
		}

		foreach (string wolfTag in wolfTags)
		{
			if (string.IsNullOrEmpty(wolfTag))
			{
				continue;
			}

			if (target.itemData.Tags.Contains(wolfTag))
			{
				return true;
			}
		}

		return false;
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
		if (_hasPackMate && !_isAlphaWolf && wanderCohesionWeight > 0f)
		{
			Vector3 cohesionAnchor = _packCenter;
			if (_alphaWolf != null)
			{
				cohesionAnchor = _alphaWolf.transform.position;
			}

			Vector2 toPack = (Vector2)(cohesionAnchor - transform.position);
			if (toPack.sqrMagnitude > 0.0001f)
			{
				offset += toPack.normalized * (wanderRadius * wanderCohesionWeight);
				if (offset.sqrMagnitude > wanderRadius * wanderRadius)
				{
					offset = offset.normalized * wanderRadius;
				}
			}
		}
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

	private float GetHpRate()
	{
		if (_hp.MaxHp.Value <= 0f)
		{
			Debug.LogError($"[{nameof(AI_Wolf)}] MaxHp 小于等于 0，无法计算血量百分比。目标物体: {name}", this);
			return 0f;
		}

		return _hp.Hp / _hp.MaxHp.Value;
	}

	private string GetAnimationNameForState(WolfState state)
	{
		switch (state)
		{
			case WolfState.Idle:
				return animIdle;
			case WolfState.Move:
				return animMove;
			case WolfState.Alert:
				return animAlert;
			case WolfState.Chase:
				return animChase;
			case WolfState.Attack:
				return animAttack;
			case WolfState.Avoid:
				return animAvoid;
			case WolfState.Flee:
				return animFlee;
			default:
				throw new ArgumentOutOfRangeException(nameof(state), state, null);
		}
	}

	private string GetStateTextCN(WolfState state)
	{
		switch (state)
		{
			case WolfState.Idle:
				return "待机";
			case WolfState.Move:
				return "闲逛";
			case WolfState.Alert:
				return "警觉";
			case WolfState.Chase:
				return "追击";
			case WolfState.Attack:
				return "攻击";
			case WolfState.Avoid:
				return "避让";
			case WolfState.Flee:
				return "逃跑";
			default:
				throw new ArgumentOutOfRangeException(nameof(state), state, null);
		}
	}

	private void PlayStateAnimation(WolfState state, bool force = false)
	{
		if (_animator == null)
		{
			return;
		}

		string animationName = GetAnimationNameForState(state);
		if (string.IsNullOrEmpty(animationName))
		{
			Debug.LogError($"[{nameof(AI_Wolf)}] 状态 {state} 未配置动画名。目标物体: {name}", this);
			return;
		}

		if (!force && _lastPlayedAnimation == animationName)
		{
			return;
		}

		_animator.PlayAnimation(animationName);
		_lastPlayedAnimation = animationName;
	}
#endregion

}
