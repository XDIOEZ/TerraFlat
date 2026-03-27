using System;
using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;
using UltEvents;

public enum ChickenState
{
	Idle,
	Move,
	Forage,
	Eat,
	Sleep,
	Mate,
	LayEgg,
	Flee
}

public partial class AI_Chicken : Module
{
	#region SaveData
	[Serializable]
	[MemoryPackable]
	public partial class AI_ChickenSaveData
	{
		public ChickenState State = ChickenState.Idle;
		public float EggTimer = 0f;
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

	public AI_ChickenSaveData Data = new AI_ChickenSaveData();
	#endregion

	#region RuntimeState
	[SerializeField, ReadOnly]
	private ChickenState _currentState = ChickenState.Idle;

	[SerializeField, ReadOnly]
	private float _stateElapsed;

	[SerializeField, ReadOnly]
	private bool _isReady;

	[SerializeField, ReadOnly]
	private Item _currentFoodTarget;

	private float _detectorRefreshTimer;
	private float _mateRequestRemain;
	private float _sleepCooldownTimer;
	private float _idleRemainTimer;
	private float _wanderWaitTimer;
	private Vector3 _wanderTarget;
	private bool _hasWanderTarget;
	private bool _layEggTriggered;
	private string _lastPlayedAnimation;
	private static GUIStyle _debugStateStyle;
	private Item _currentThreat;
	private Vector3 _fleeTarget;
	#endregion

	#region CachedModules
	[SerializeField, ReadOnly] private Mover_AI _mover;
	[SerializeField, ReadOnly] private Mod_Food _food;
	[SerializeField, ReadOnly] private DamageReceiver _hp;
	[SerializeField, ReadOnly] private Mod_ItemDetector _detector;
	[SerializeField, ReadOnly] private Mod_AnimatorController _animator;
	#endregion

	#region Config
	[FoldoutGroup("调试"), PropertyOrder(0), LabelText("启用调试日志")]
	public bool debugLog;

	[FoldoutGroup("通用参数"), PropertyOrder(10), LabelText("进食触发饥饿阈值"), Range(0f, 1f)]
	public float eatEnterHungerRate = 0.35f;
	[FoldoutGroup("通用参数"), PropertyOrder(11), LabelText("进食退出饥饿阈值"), Range(0f, 1f)]
	public float eatExitHungerRate = 0.85f;

	[FoldoutGroup("睡眠参数"), PropertyOrder(20), LabelText("睡眠触发血量阈值"), Range(0f, 1f)]
	public float sleepEnterHpRate = 0.5f;
	[FoldoutGroup("睡眠参数"), PropertyOrder(21), LabelText("睡眠维持血量阈值"), Range(0f, 1f)]
	public float sleepExitHpRate = 0.55f;
	[FoldoutGroup("睡眠参数"), PropertyOrder(22), LabelText("睡眠回血速度"), SuffixLabel("/秒", true)]
	public float sleepRecoverHpPerSecond = 6f;
	[FoldoutGroup("睡眠参数"), PropertyOrder(23), LabelText("单次睡眠时长"), SuffixLabel("秒", true), MinValue(0.1f)]
	public float sleepDuration = 6f;
	[FoldoutGroup("睡眠参数"), PropertyOrder(24), LabelText("睡醒冷却时间"), SuffixLabel("秒", true), MinValue(0f)]
	public float sleepCooldown = 3f;

	[FoldoutGroup("昼夜参数"), PropertyOrder(30), LabelText("白天开始比例"), Range(0f, 1f)]
	public float dayStartRatio = 0.25f;
	[FoldoutGroup("昼夜参数"), PropertyOrder(31), LabelText("白天结束比例"), Range(0f, 1f)]
	public float dayEndRatio = 0.75f;

	[FoldoutGroup("移动与觅食"), PropertyOrder(40), LabelText("检测刷新间隔"), SuffixLabel("秒", true), MinValue(0.05f)]
	public float detectorRefreshInterval = 0.8f;
	[FoldoutGroup("移动与觅食"), PropertyOrder(41), LabelText("进食距离"), SuffixLabel("米", true), MinValue(0.1f)]
	public float eatDistance = 1.2f;
	[FoldoutGroup("移动与觅食"), PropertyOrder(42), LabelText("启用空闲闲逛")]
	public bool enableIdleWander = true;
	[FoldoutGroup("移动与觅食"), PropertyOrder(43), LabelText("待机最小时长"), SuffixLabel("秒", true), MinValue(0f)]
	public float idleMinDuration = 0.6f;
	[FoldoutGroup("移动与觅食"), PropertyOrder(44), LabelText("待机最大时长"), SuffixLabel("秒", true), MinValue(0f)]
	public float idleMaxDuration = 1.8f;
	[FoldoutGroup("移动与觅食"), PropertyOrder(43), LabelText("闲逛半径"), SuffixLabel("米", true), MinValue(0.1f)]
	public float wanderRadius = 4f;
	[FoldoutGroup("移动与觅食"), PropertyOrder(45), LabelText("闲逛到达距离"), SuffixLabel("米", true), MinValue(0.05f)]
	public float wanderStopDistance = 0.35f;
	[FoldoutGroup("移动与觅食"), PropertyOrder(46), LabelText("闲逛停顿最小时间"), SuffixLabel("秒", true), MinValue(0f)]
	public float wanderPauseMin = 0.8f;
	[FoldoutGroup("移动与觅食"), PropertyOrder(47), LabelText("闲逛停顿最大时间"), SuffixLabel("秒", true), MinValue(0f)]
	public float wanderPauseMax = 2.2f;
	[FoldoutGroup("移动与觅食"), PropertyOrder(48), LabelText("可食物Tag列表")]
	[FormerlySerializedAs("edibleItemIds")]
	[SerializeField]
	public List<string> edibleTags = new List<string> { "Food" };

	[FoldoutGroup("下蛋参数"), PropertyOrder(50), LabelText("鸡蛋物品ID")]
	public string eggItemId = "Egg";
	[FoldoutGroup("下蛋参数"), PropertyOrder(51), LabelText("下蛋周期"), SuffixLabel("秒", true), MinValue(1f)]
	public float layEggInterval = 2880f;
	[FoldoutGroup("下蛋参数"), PropertyOrder(52), LabelText("下蛋动作时长"), SuffixLabel("秒", true), MinValue(0.1f)]
	public float layEggDuration = 2f;

	[FoldoutGroup("逃跑参数"), PropertyOrder(55), LabelText("逃跑触发距离"), SuffixLabel("米", true), MinValue(0.1f)]
	public float fleeTriggerDistance = 6f;
	[FoldoutGroup("逃跑参数"), PropertyOrder(56), LabelText("逃跑安全距离"), SuffixLabel("米", true), MinValue(0.1f)]
	public float fleeSafeDistance = 10f;
	[FoldoutGroup("逃跑参数"), PropertyOrder(57), LabelText("逃跑偏移距离"), SuffixLabel("米", true), MinValue(1f)]
	public float fleeRunDistance = 8f;
	[FoldoutGroup("逃跑参数"), PropertyOrder(58), LabelText("威胁TypeTag列表")]
	public List<string> threatTags = new List<string> { "Predator" };
	[FoldoutGroup("逃跑参数"), PropertyOrder(59), LabelText("检测玩家(Player标签)")]
	public bool fleeFromPlayer = true;

	[FoldoutGroup("交配参数"), PropertyOrder(60), LabelText("交配占位时长"), SuffixLabel("秒", true), MinValue(0.1f)]
	public float defaultMateDuration = 8f;

	[FoldoutGroup("动画状态名"), PropertyOrder(70), LabelText("待机动画")]
	public string animIdle = "Stand";
	[FoldoutGroup("动画状态名"), PropertyOrder(71), LabelText("移动动画")]
	public string animMove = "Move";
	[FoldoutGroup("动画状态名"), PropertyOrder(72), LabelText("觅食动画")]
	public string animForage = "Move";
	[FoldoutGroup("动画状态名"), PropertyOrder(73), LabelText("吃饭动画")]
	public string animEat = "Sit";
	[FoldoutGroup("动画状态名"), PropertyOrder(74), LabelText("睡觉动画")]
	public string animSleep = "Sleep";
	[FoldoutGroup("动画状态名"), PropertyOrder(75), LabelText("交配动画")]
	public string animMate = "Sit";
	[FoldoutGroup("动画状态名"), PropertyOrder(76), LabelText("下蛋动画")]
	public string animLayEgg = "Sit";
	[FoldoutGroup("动画状态名"), PropertyOrder(77), LabelText("逃跑动画")]
	public string animFlee = "Move";
	#endregion

	#region Events
	public UltEvent<ChickenState, ChickenState> OnStateChanged = new UltEvent<ChickenState, ChickenState>();
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
		_mateRequestRemain = 0f;
		_sleepCooldownTimer = 0f;
		_wanderWaitTimer = 0f;
		_idleRemainTimer = GetIdleDuration();
		_hasWanderTarget = false;
		_layEggTriggered = false;
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
		Data.EggTimer += deltaTime;

		if (_mateRequestRemain > 0f)
		{
			_mateRequestRemain = Mathf.Max(0f, _mateRequestRemain - deltaTime);
		}

		if (_sleepCooldownTimer > 0f)
		{
			_sleepCooldownTimer = Mathf.Max(0f, _sleepCooldownTimer - deltaTime);
		}

		if (_wanderWaitTimer > 0f)
		{
			_wanderWaitTimer = Mathf.Max(0f, _wanderWaitTimer - deltaTime);
		}

		ChickenState next = EvaluateNextState();
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

		string text = $"状态: {GetStateTextCN(_currentState)}";
		Vector2 size = _debugStateStyle.CalcSize(new GUIContent(text));
		float width = Mathf.Max(120f, size.x + 14f);
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
	[Button("请求交配状态")]
	public void RequestMateState(float duration = -1f)
	{
		_mateRequestRemain = duration > 0f ? duration : defaultMateDuration;
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
		item.itemMods.GetMod_ByID(ModText.AnimatorReceiver, out _animator);

		if (_mover == null)
		{
			Debug.LogError($"[{nameof(AI_Chicken)}] 缺少移动模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_food == null)
		{
			Debug.LogError($"[{nameof(AI_Chicken)}] 缺少食物模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_detector == null)
		{
			Debug.LogError($"[{nameof(AI_Chicken)}] 缺少检测模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_hp == null)
		{
			Debug.LogError($"[{nameof(AI_Chicken)}] 缺少生命值模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_animator == null)
		{
			Debug.LogWarning($"[{nameof(AI_Chicken)}] 未找到动画模块，将跳过状态动画同步。目标物体: {name}", this);
		}
	}
	#endregion

	#region StateMachine
	private ChickenState EvaluateNextState()
	{
		if (ShouldFlee())
		{
			return ChickenState.Flee;
		}

		if (ShouldEat())
		{
			return ChickenState.Eat;
		}

		if (ShouldSleep())
		{
			return ChickenState.Sleep;
		}

		if (ShouldMate())
		{
			return ChickenState.Mate;
		}

		if (ShouldLayEgg())
		{
			return ChickenState.LayEgg;
		}

		if (ShouldForage())
		{
			return ChickenState.Forage;
		}

		if (ShouldMove())
		{
			return ChickenState.Move;
		}

		return ChickenState.Idle;
	}

	private void SwitchState(ChickenState next)
	{
		ChickenState previous = _currentState;
		_currentState = next;
		_stateElapsed = 0f;
		_layEggTriggered = false;

		if (next != ChickenState.Move)
		{
			_hasWanderTarget = false;
		}

		if (next != ChickenState.Forage && next != ChickenState.Eat)
		{
			_currentFoodTarget = null;
		}

		if (next != ChickenState.Flee)
		{
			_currentThreat = null;
		}

		if (next == ChickenState.Idle)
		{
			_idleRemainTimer = GetIdleDuration();
		}

		if (debugLog)
		{
			Debug.Log($"[ChickenAI] {name} 状态切换: {previous} -> {next}", this);
		}

		if (previous == ChickenState.Sleep && next != ChickenState.Sleep)
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
			case ChickenState.Idle:
					TickIdle(deltaTime);
				break;
			case ChickenState.Move:
					TickMove(deltaTime);
				break;
				case ChickenState.Forage:
					TickForage();
					break;
			case ChickenState.Eat:
				TickEat(deltaTime);
				break;
			case ChickenState.Sleep:
				TickSleep(deltaTime);
				break;
			case ChickenState.Mate:
				TickMate();
				break;
			case ChickenState.LayEgg:
				TickLayEgg();
				break;
			case ChickenState.Flee:
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

	private void TickMate()
	{
		_mover.CanMove = false;
		_mover.HasReachedTarget = true;
	}

	private void TickLayEgg()
	{
		_mover.CanMove = false;
		_mover.HasReachedTarget = true;

		if (_stateElapsed < layEggDuration)
		{
			return;
		}

		if (_layEggTriggered)
		{
			return;
		}

		SpawnEgg();
		Data.EggTimer = 0f;
		_layEggTriggered = true;
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
		_fleeTarget = transform.position + awayDir * fleeRunDistance;

		_mover.CanMove = true;
		_mover.HasReachedTarget = false;
		_mover.TargetPosition = _fleeTarget;
	}
	#endregion

	#region Conditions
	private bool ShouldFlee()
	{
		TryRefreshDetector();
		Item threat = FindClosestThreat();

		if (_currentState == ChickenState.Flee)
		{
			if (threat == null)
				return false;

			float dist = Vector2.Distance(transform.position, threat.transform.position);
			_currentThreat = threat;
			return dist < fleeSafeDistance;
		}

		if (threat == null)
			return false;

		float distance = Vector2.Distance(transform.position, threat.transform.position);
		if (distance > fleeTriggerDistance)
			return false;

		_currentThreat = threat;
		return true;
	}

	private bool ShouldEat()
	{
		float hungerRate = _food.Data.nutrition.GetFoodRate();
		if (_currentState == ChickenState.Eat)
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

	private bool ShouldSleep()
	{
		float hpRate = GetHpRate();

		if (_currentState == ChickenState.Sleep)
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

	private bool ShouldMate()
	{
		if (_currentState == ChickenState.Mate)
		{
			return _mateRequestRemain > 0f;
		}

		return _mateRequestRemain > 0f;
	}

	private bool ShouldLayEgg()
	{
		if (_currentState == ChickenState.LayEgg)
		{
			return !_layEggTriggered;
		}

		return Data.EggTimer >= layEggInterval;
	}

	private bool ShouldMove()
	{
		if (_currentState == ChickenState.Move)
		{
			return _hasWanderTarget;
		}

		if (!enableIdleWander)
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

	private void TickIdleWander(float deltaTime)
	{
		if (!enableIdleWander)
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
		List<Item> threats = _detector.GetItemsByTags(threatTags);

		if (fleeFromPlayer)
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

	private void RecoverNutrition(float deltaTime)
	{
		Nutrition nutrition = _food.Data.nutrition;

		nutrition.Carbohydrates = Mathf.Min(nutrition.Max_Carbohydrates.Value, nutrition.Carbohydrates + deltaTime * 10f);
		nutrition.Fat = Mathf.Min(nutrition.Max_Fat.Value, nutrition.Fat + deltaTime * 6f);
		nutrition.Protein = Mathf.Min(nutrition.Max_Protein.Value, nutrition.Protein + deltaTime * 8f);
		nutrition.Water = Mathf.Min(nutrition.Max_Water.Value, nutrition.Water + deltaTime * 10f);
		nutrition.Vitamins = Mathf.Min(nutrition.Max_Vitamins.Value, nutrition.Vitamins + deltaTime * 2f);
	}

	private void SpawnEgg()
	{
		Item egg = ItemMgr.Instance.InstantiateItem(eggItemId, transform.position);
		egg.Load();
		egg.DropInRange();

		if (debugLog)
		{
			Debug.Log($"[ChickenAI] {name} 产出鸡蛋: {eggItemId}", this);
		}
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

	private float GetHpRate()
	{
		if (_hp.MaxHp <= 0f)
		{
			Debug.LogError($"[{nameof(AI_Chicken)}] MaxHp 小于等于 0，无法计算血量百分比。目标物体: {name}", this);
			return 0f;
		}

		return _hp.Hp / _hp.MaxHp;
	}

	private string GetAnimationNameForState(ChickenState state)
	{
		switch (state)
		{
			case ChickenState.Idle:
				return animIdle;
			case ChickenState.Move:
				return animMove;
			case ChickenState.Forage:
				return animForage;
			case ChickenState.Eat:
				return animEat;
			case ChickenState.Sleep:
				return animSleep;
			case ChickenState.Mate:
				return animMate;
			case ChickenState.LayEgg:
				return animLayEgg;
			case ChickenState.Flee:
				return animFlee;
			default:
				throw new ArgumentOutOfRangeException(nameof(state), state, null);
		}
	}

	private string GetStateTextCN(ChickenState state)
	{
		switch (state)
		{
			case ChickenState.Idle:
				return "待机";
			case ChickenState.Move:
				return "闲逛";
			case ChickenState.Forage:
				return "觅食";
			case ChickenState.Eat:
				return "吃饭";
			case ChickenState.Sleep:
				return "睡觉";
			case ChickenState.Mate:
				return "交配";
			case ChickenState.LayEgg:
				return "下蛋";
			case ChickenState.Flee:
				return "逃跑";
			default:
				throw new ArgumentOutOfRangeException(nameof(state), state, null);
		}
	}

	private void PlayStateAnimation(ChickenState state, bool force = false)
	{
		if (_animator == null)
		{
			return;
		}

		string animationName = GetAnimationNameForState(state);
		if (string.IsNullOrEmpty(animationName))
		{
			Debug.LogError($"[{nameof(AI_Chicken)}] 状态 {state} 未配置动画名。目标物体: {name}", this);
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
