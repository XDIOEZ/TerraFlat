using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
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

/// <summary>
/// 鸡 AI：支持觅食/进食/睡眠/交配/下蛋/逃跑等行为。
/// 状态优先级：逃跑 > 进食 > 睡眠 > 交配 > 下蛋 > 觅食 > 移动 > 待机
/// </summary>
public partial class AI_Chicken : AI_Base<ChickenState>
{
	private const string SpeedOneBuffId = "速度1";

	#region SaveData
	[Serializable]
	[MemoryPackable]
	public partial class AI_ChickenSaveData
	{
		public ChickenState State = ChickenState.Idle;
		public float EggTimer = 0f;
		public float Fatigue01 = 0f;
		public bool GrassSustenanceInitialized;
		public float GrassSustenanceRemaining;
	}
	#endregion

	#region ModuleData
	public AI_ChickenSaveData Data = new AI_ChickenSaveData();
	#endregion

	#region RuntimeState - Chicken 特有
	[SerializeField, ReadOnly]
	private Item _currentFoodTarget;

	[SerializeField, ReadOnly]
	private Item _currentThreat;

	[SerializeField, ReadOnly]
	private Vector2Int _currentGrassTarget;

	[SerializeField, ReadOnly]
	private bool _hasGrassTarget;

	// 新版权威草层目标；不创建或查找草 Item 实体。
	private ChunkTerrainData _currentGrassRuntimeTerrain;
	private Vector2Int _currentGrassRuntimeLocal;
	private bool _hasRuntimeGrassTarget;

	private float _mateRequestRemain;
	private float _sleepCooldownTimer;
	private float _grassSearchCooldown;
	private bool _layEggTriggered;
	private Vector3 _fleeTarget;
	#endregion

	#region CachedModules - Chicken 特有
	[SerializeField, ReadOnly] private Mod_Food _food;
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
	[FoldoutGroup("移动与觅食"), PropertyOrder(42), LabelText("启用吃草")]
	public bool enableGrassForaging = true;
	[FoldoutGroup("移动与觅食"), PropertyOrder(43), LabelText("寻草半径"), SuffixLabel("米", true), MinValue(0.5f)]
	public float grassSearchRadius = 8f;
	[FoldoutGroup("移动与觅食"), PropertyOrder(44), LabelText("吃草动作时长"), SuffixLabel("秒", true), MinValue(0f)]
	public float grassEatDuration = 1f;
	[FoldoutGroup("移动与觅食"), PropertyOrder(45), LabelText("一朵草维持天数"), SuffixLabel("天", true), MinValue(0.1f)]
	public float grassSustenanceDays = 2f;
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
	[FoldoutGroup("移动与觅食"), PropertyOrder(49), LabelText("闲逛避开高权重")]
	public bool wanderAvoidHighPenalty = true;
	[FoldoutGroup("移动与觅食"), PropertyOrder(50), LabelText("危险权重阈值"), MinValue(0)]
	public int wanderDangerPenalty = 1200;
	[FoldoutGroup("移动与觅食"), PropertyOrder(51), LabelText("闲逛采样点数"), MinValue(1)]
	public int wanderSampleCount = 8;
	[FoldoutGroup("移动与觅食"), PropertyOrder(52), LabelText("权重惩罚系数"), MinValue(0f)]
	public float wanderPenaltyWeight = 1f;

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
	[FoldoutGroup("逃跑参数"), PropertyOrder(58), LabelText("受击威胁记忆"), SuffixLabel("秒", true), MinValue(0.1f)]
	public float damageFleeDuration = 5f;
	[FoldoutGroup("逃跑参数"), PropertyOrder(58), LabelText("威胁TypeTag列表")]
	public List<string> threatTags = new List<string> { "Predator", "Wolf" };
	[FoldoutGroup("逃跑参数"), PropertyOrder(59), LabelText("检测所有玩家")]
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

	#region Base Overrides - Config Accessors
	protected override AI_WanderConfig WanderConfig => new AI_WanderConfig
	{
		enabled = enableIdleWander,
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
	protected override float DamageThreatMemoryDuration => damageFleeDuration;
	protected override bool IsMoveState(ChickenState state) => state == ChickenState.Move;
	protected override bool IsIdleState(ChickenState state) => state == ChickenState.Idle;
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
		_mateRequestRemain = 0f;
		_sleepCooldownTimer = 0f;
		_grassSearchCooldown = 0f;
		_layEggTriggered = false;
		_currentFoodTarget = null;
		_currentThreat = null;
		ClearGrassTarget();
	}

	protected override void OnDamageThreatUpdated(DamageReceiverDamageInfo damageInfo)
	{
		if (!TryGetRecentDamageThreat(out Item threat, out Vector3 sourcePosition))
			return;

		_currentThreat = threat;
		UpdateFleeDestination(sourcePosition);

		if (_isReady &&
			_stateMachine != null &&
			_stateMachine.IsInitialized &&
			_currentState != ChickenState.Flee)
		{
			SwitchState(ChickenState.Flee);
		}

		MoveTo(_fleeTarget);
	}

	/// <summary>小鸡受到有效伤害后获得短时速度1，不要求伤害必须来自可识别攻击者。</summary>
	protected override void OnDamageReceived(DamageReceiverDamageInfo damageInfo)
	{
		if (item == null || damageInfo == null || damageInfo.DamageValue <= 0f)
			return;

		BuffManager buffManager = item.itemMods?.GetMod_ByID<BuffManager>(ModText.BuffManager);
		buffManager?.AddBuff(SpeedOneBuffId);
	}

	protected override void OnBindExtraModules()
	{
		item.itemMods.GetMod_ByID(ModText.Food, out Mod_Food food);
		_food = food;
		InitializeGrassSustenance();

		if (_detector != null)
		{
			_detector.DetectionRadius = Mathf.Max(
				_detector.DetectionRadius,
				Mathf.Max(fleeTriggerDistance, fleeSafeDistance));
		}
	}

	protected override void OnValidateExtraModules()
	{
		if (_food == null)
		{
			Debug.LogError($"[{nameof(AI_Chicken)}] 缺少食物模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}
	}

	protected override void UpdateExtraTimers(float deltaTime)
	{
		_mateRequestRemain = DecrementTimer(_mateRequestRemain, deltaTime);
		_sleepCooldownTimer = DecrementTimer(_sleepCooldownTimer, deltaTime);
		_grassSearchCooldown = DecrementTimer(_grassSearchCooldown, deltaTime);
		if (enableGrassForaging && Data.GrassSustenanceInitialized)
		{
			Data.GrassSustenanceRemaining = DecrementTimer(
				Data.GrassSustenanceRemaining,
				deltaTime);
		}
		ApplyGrassSustenanceState();
		Data.EggTimer += deltaTime;
	}

	protected override void OnBeforeSwitchState(ChickenState previous, ChickenState next)
	{
		// 离开觅食/进食状态时清除食物目标
		if (next != ChickenState.Forage && next != ChickenState.Eat)
		{
			_currentFoodTarget = null;
			ClearGrassTarget();
		}

		// 离开逃跑状态时清除威胁目标
		if (next != ChickenState.Flee)
		{
			_currentThreat = null;
		}

		// 重置下蛋触发标记（每次状态切换都重置，防止重复下蛋）
		_layEggTriggered = false;

		// 离开睡眠状态：进入睡醒冷却
		if (previous == ChickenState.Sleep && next != ChickenState.Sleep)
		{
			_sleepCooldownTimer = sleepCooldown;
		}
	}

	protected override string GetDebugExtraInfo()
	{
		float hungerRate = _food != null
			? Mathf.Clamp01(_food.Data.nutrition.GetFoodRate())
			: 0f;
		float grassRemaining = Data != null && Data.GrassSustenanceInitialized
			? Mathf.Max(0f, Data.GrassSustenanceRemaining)
			: 0f;
		string grassText = !enableGrassForaging
			? "关闭"
			: Data != null && Data.GrassSustenanceInitialized
				? $"{grassRemaining:F0}s"
				: "未初始化";
		return $" | 饱食度: {hungerRate:P0} | 草食剩余: {grassText}";
	}
	#endregion

	#region PublicAPI
	[Button("请求交配状态")]
	public void RequestMateState(float duration = -1f)
	{
		_mateRequestRemain = duration > 0f ? duration : defaultMateDuration;
	}
	#endregion

	#region StateMachine
	protected override ChickenState EvaluateNextState()
	{
		if (ShouldFlee())    return ChickenState.Flee;
		if (ShouldEat())     return ChickenState.Eat;
		if (ShouldSleep())   return ChickenState.Sleep;
		if (ShouldMate())    return ChickenState.Mate;
		if (ShouldLayEgg())  return ChickenState.LayEgg;
		if (ShouldForage())  return ChickenState.Forage;
		if (ShouldMoveBase())return ChickenState.Move;
		return ChickenState.Idle;
	}

	protected override void ConfigureStateNodes(AIStateMachine<ChickenState> stateMachine)
	{
		RegisterLocomotionStateNodes(stateMachine, ChickenState.Idle, ChickenState.Move);
		stateMachine.Register(CreateMovingStateNode(ChickenState.Forage, _ => TickForage()));
		stateMachine.Register(CreateStoppedStateNode(ChickenState.Eat, TickEat));
		stateMachine.Register(CreateStoppedStateNode(ChickenState.Sleep, TickSleep));
		stateMachine.Register(CreateStoppedStateNode(ChickenState.Mate, _ => TickMate()));
		stateMachine.Register(CreateStoppedStateNode(ChickenState.LayEgg, _ => TickLayEgg()));
		stateMachine.Register(CreateMovingStateNode(ChickenState.Flee, _ => TickFlee()));
	}
	#endregion

	#region Tick - Chicken 特有状态
	private void TickForage()
	{
		if (IsGrassMealDue() && TryAcquireGrassTarget())
		{
			MoveTo(GetGrassTargetWorldPosition());
			return;
		}

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
		if (IsGrassMealDue() && TryAcquireGrassTarget())
		{
			if (GetGrassTargetDistance() > eatDistance || _stateElapsed < grassEatDuration)
				return;

			Vector2Int grassPosition = _currentGrassTarget;
			ClearGrassTarget();

			ChunkMgr chunkManager = ChunkMgr.Instance;
			bool consumed = chunkManager != null &&
				chunkManager.TryConsumeRuntimeGrass(grassPosition);

			if (consumed)
				ConsumeGrass(grassPosition);
			return;
		}

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
	}

	private void TickMate()
	{
	}

	private void TickLayEgg()
	{
		if (_stateElapsed < layEggDuration || _layEggTriggered) return;

		SpawnEgg();
		Data.EggTimer = 0f;
		_layEggTriggered = true;
	}

	private void TickFlee()
	{
		if (TryGetRecentDamageThreat(out Item damageThreat, out Vector3 damageSource))
		{
			_currentThreat = damageThreat;
			UpdateFleeDestination(damageSource);
			MoveTo(_fleeTarget);
			return;
		}

		if (_currentThreat == null) { StopMove(); return; }
		UpdateFleeDestination(_currentThreat.transform.position);
		MoveTo(_fleeTarget);
	}
	#endregion

	#region Conditions
	private bool ShouldFlee()
	{
		if (TryGetRecentDamageThreat(out Item damageThreat, out _))
		{
			_currentThreat = damageThreat;
			return true;
		}

		TryRefreshDetector();
		Item threat = FindClosestThreat();

		if (_currentState == ChickenState.Flee)
		{
			if (threat == null) return false;
			_currentThreat = threat;
			return IsWithinEffectivePerceptionRange(threat, fleeSafeDistance);
		}

		if (threat == null) return false;
		if (!IsWithinEffectivePerceptionRange(threat, fleeTriggerDistance)) return false;

		_currentThreat = threat;
		return true;
	}

	private bool ShouldEat()
	{
		if (IsGrassMealDue() && TryAcquireGrassTarget())
			return GetGrassTargetDistance() <= eatDistance;

		float hungerRate = _food.Data.nutrition.GetFoodRate();
		if (_currentState == ChickenState.Eat)
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

	private bool ShouldSleep()
	{
		float hpRate = GetHpRate();

		if (_currentState == ChickenState.Sleep)
		{
			if (_stateElapsed < sleepDuration) return true;
			return IsNightTime() || hpRate < sleepExitHpRate;
		}

		if (_sleepCooldownTimer > 0f) return false;
		return IsNightTime() || hpRate < sleepEnterHpRate;
	}

	private bool ShouldMate()
	{
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

	private bool ShouldForage()
	{
		if (IsGrassMealDue() && TryAcquireGrassTarget())
			return GetGrassTargetDistance() > eatDistance;

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

	#region Helpers - Chicken 特有
	private void UpdateFleeDestination(Vector3 threatPosition)
	{
		Vector2 awayDirection = GetDirectionAwayFrom(threatPosition);
		_fleeTarget = (Vector2)transform.position + awayDirection * fleeRunDistance;
	}

	private Item FindClosestThreat()
	{
		Item closestThreat = _detector.FindClosestItemByTags(threatTags, transform.position);
		float closestDistanceSqr = closestThreat != null
			? WorldTopologyRuntime.SqrDistance(transform.position, closestThreat.transform.position)
			: float.MaxValue;

		if (!fleeFromPlayer)
			return closestThreat;

		List<Item> detectedItems = _detector.CurrentItemsInArea;
		for (int i = 0; i < detectedItems.Count; i++)
		{
			Item detectedItem = detectedItems[i];
			if (!(detectedItem is Player))
				continue;

			float distanceSqr = WorldTopologyRuntime.SqrDistance(transform.position, detectedItem.transform.position);
			if (distanceSqr >= closestDistanceSqr)
				continue;

			closestThreat = detectedItem;
			closestDistanceSqr = distanceSqr;
		}

		return closestThreat;
	}

	private Item FindClosestEdibleItem()
	{
		return _detector.FindClosestItemByTags(edibleTags, transform.position);
	}

	private void InitializeGrassSustenance()
	{
		if (_food == null)
			return;

		if (enableGrassForaging && !Data.GrassSustenanceInitialized)
		{
			Data.GrassSustenanceInitialized = true;
			Data.GrassSustenanceRemaining = GetGrassSustenanceDuration();
		}

		ApplyGrassSustenanceState();
	}

	private void ApplyGrassSustenanceState()
	{
		if (_food == null)
			return;

		_food.RuntimeNutritionConsumeMultiplier =
			enableGrassForaging && Data.GrassSustenanceRemaining > 0f ? 0f : 1f;
	}

	private bool IsGrassMealDue()
	{
		return enableGrassForaging &&
		       Data.GrassSustenanceInitialized &&
		       Data.GrassSustenanceRemaining <= 0f;
	}

	private bool TryAcquireGrassTarget()
	{
		if (!IsGrassMealDue())
			return false;

		if (HasValidGrassTarget())
			return true;

		ClearGrassTarget();
		if (_grassSearchCooldown > 0f)
			return false;

		_grassSearchCooldown = Mathf.Max(0.05f, detectorRefreshInterval);

		ChunkMgr chunkManager = ChunkMgr.Instance;
		Vector2 origin = transform.position;
		float radius = Mathf.Max(eatDistance, grassSearchRadius);
		if (chunkManager != null &&
			chunkManager.TryFindRuntimeGrassNear(origin, radius,
				out RuntimeTerrainTileSample runtimeGrass))
		{
			_currentGrassRuntimeTerrain = runtimeGrass.Terrain;
			_currentGrassRuntimeLocal = runtimeGrass.LocalCell;
			_currentGrassTarget = runtimeGrass.WorldCell;
			_hasGrassTarget = true;
			_hasRuntimeGrassTarget = true;
			_currentFoodTarget = null;
			return true;
		}

		return false;
	}

	private bool HasValidGrassTarget()
	{
		if (!_hasGrassTarget)
			return false;

		if (_hasRuntimeGrassTarget)
		{
			return _currentGrassRuntimeTerrain != null &&
			       !_currentGrassRuntimeTerrain.IsDisposed &&
			       _currentGrassRuntimeTerrain.GetGrass(
				       _currentGrassRuntimeLocal.x, _currentGrassRuntimeLocal.y) ==
				       ChunkTerrainData.GrassPresent;
		}

		return false;
	}

	private void ClearGrassTarget()
	{
		_currentGrassTarget = default;
		_hasGrassTarget = false;
		_currentGrassRuntimeTerrain = null;
		_currentGrassRuntimeLocal = default;
		_hasRuntimeGrassTarget = false;
	}

	private float GetGrassTargetDistance()
	{
		return WorldTopologyRuntime.Distance(transform.position, GetGrassTargetWorldPosition());
	}

	private Vector2 GetGrassTargetWorldPosition()
	{
		return GetGrassWorldPosition(_currentGrassTarget);
	}

	private static Vector2 GetGrassWorldPosition(Vector2Int grassPosition)
	{
		return new Vector2(grassPosition.x + 0.5f, grassPosition.y + 0.5f);
	}

	private void ConsumeGrass(Vector2Int grassPosition)
	{
		_food.RestoreNutritionToMaximum();
		Data.GrassSustenanceRemaining = GetGrassSustenanceDuration();
		ApplyGrassSustenanceState();

		if (debugLog)
			Debug.Log($"[ChickenAI] {name} 吃掉草 {grassPosition}，可维持 {grassSustenanceDays:0.##} 天。", this);
	}

	private float GetGrassSustenanceDuration()
	{
		const float fallbackDayLength = 1440f;
		float dayLength = fallbackDayLength;
		if (DayTimeSystem.Instance != null &&
		    DayTimeSystem.Instance.WorldTimeDict.TryGetValue(gameObject.scene.name, out TimeData timeData) &&
		    timeData != null)
		{
			dayLength = Mathf.Max(1f, timeData.DayLength);
		}

		return dayLength * Mathf.Max(0.1f, grassSustenanceDays);
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
		if (DayTimeSystem.Instance == null) return true;

		string sceneName = gameObject.scene.name;
		if (!DayTimeSystem.Instance.WorldTimeDict.TryGetValue(sceneName, out TimeData timeData)) return true;

		float dayLength = Mathf.Max(1f, timeData.DayLength);
		float normalized = Mathf.Repeat(DayTimeSystem.Instance.GetCurrentTime(sceneName), dayLength) / dayLength;
		return normalized >= dayStartRatio && normalized <= dayEndRatio;
	}

	private bool IsNightTime() => !IsDayTime();
	#endregion

	#region Animation Mapping
	protected override string GetAnimationNameForState(ChickenState state)
	{
		switch (state)
		{
			case ChickenState.Idle:   return animIdle;
			case ChickenState.Move:   return animMove;
			case ChickenState.Forage: return animForage;
			case ChickenState.Eat:    return animEat;
			case ChickenState.Sleep:  return animSleep;
			case ChickenState.Mate:   return animMate;
			case ChickenState.LayEgg: return animLayEgg;
			case ChickenState.Flee:   return animFlee;
			default: throw new ArgumentOutOfRangeException(nameof(state), state, null);
		}
	}

	protected override string GetStateTextCN(ChickenState state)
	{
		switch (state)
		{
			case ChickenState.Idle:   return "待机";
			case ChickenState.Move:   return "闲逛";
			case ChickenState.Forage: return "觅食";
			case ChickenState.Eat:    return "吃饭";
			case ChickenState.Sleep:  return "睡觉";
			case ChickenState.Mate:   return "交配";
			case ChickenState.LayEgg: return "下蛋";
			case ChickenState.Flee:   return "逃跑";
			default: throw new ArgumentOutOfRangeException(nameof(state), state, null);
		}
	}
	#endregion
}
