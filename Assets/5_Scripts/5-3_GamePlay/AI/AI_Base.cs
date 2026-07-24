using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using UltEvents;

/// <summary>
/// AI 闲逛配置，封装闲逛相关的所有参数。
/// 由各 AI 子类在 Inspector 中配置，通过 WanderConfig 属性提供给基类。
/// </summary>
[Serializable]
public struct AI_WanderConfig
{
	public bool enabled;
	public float radius;
	public float stopDistance;
	public float pauseMin;
	public float pauseMax;
	public bool avoidHighPenalty;
	public int dangerPenalty;
	public int sampleCount;
	public float penaltyWeight;
}

/// <summary>
/// AI 待机配置，封装待机时长参数。
/// </summary>
[Serializable]
public struct AI_IdleConfig
{
	public float minDuration;
	public float maxDuration;
}

/// <summary>
/// AI 状态机抽象基类，封装所有 AI 共享的核心逻辑：
/// - 状态机运行循环（评估 → 切换 → 帧逻辑）
/// - 通用计时器管理
/// - 模块绑定与验证
/// - 待机 / 闲逛行为
/// - 动画播放
/// - 调试显示
///
/// 子类只需实现差异化的状态评估、帧逻辑和配置即可。
/// </summary>
public abstract class AI_Base<TState> : Module where TState : struct, Enum
{
#region ModuleData
	public Ex_ModData_MemoryPackable ModData = new Ex_ModData_MemoryPackable();
	public override ModuleData _Data
	{
		get => ModData;
		set => ModData = (Ex_ModData_MemoryPackable)value;
	}
#endregion

#region RuntimeState
	/// <summary>当前状态</summary>
	[SerializeField, ReadOnly]
	protected TState _currentState;

	/// <summary>当前状态已持续时间</summary>
	[SerializeField, ReadOnly]
	protected float _stateElapsed;

	/// <summary>模块是否就绪（所有必需模块已绑定）</summary>
	[SerializeField, ReadOnly]
	protected bool _isReady;

	// --- 通用计时器 ---
	protected float _detectorRefreshTimer;
	protected float _wanderWaitTimer;
	protected float _idleRemainTimer;

	// --- 闲逛状态 ---
	protected Vector3 _wanderTarget;
	protected bool _hasWanderTarget;

	protected string _lastPlayedAnimation;
	protected static GUIStyle _debugStateStyle;
#endregion

#region CachedModules
	[SerializeField, ReadOnly] protected Mover_AI _mover;
	[SerializeField, ReadOnly] protected DamageReceiver _hp;
	[SerializeField, ReadOnly] protected Mod_ItemDetector _detector;
	[SerializeField, ReadOnly] protected Mod_AnimatorController _animator;
#endregion

#region Events
	/// <summary>状态切换事件，参数为 (旧状态, 新状态)</summary>
	public UltEvent<TState, TState> OnStateChanged = new UltEvent<TState, TState>();
#endregion

#region Abstract - 子类必须实现
	/// <summary>评估下一状态（按优先级从高到低判断）</summary>
	protected abstract TState EvaluateNextState();

	/// <summary>帧逻辑分发（switch 当前状态调用对应 Tick 方法）</summary>
	protected abstract void TickCurrentState(float deltaTime);

	/// <summary>获取状态对应的动画名</summary>
	protected abstract string GetAnimationNameForState(TState state);

	/// <summary>获取状态的中文显示名（用于调试 HUD）</summary>
	protected abstract string GetStateTextCN(TState state);
#endregion

#region Abstract - 配置访问器
	/// <summary>闲逛参数配置</summary>
	protected abstract AI_WanderConfig WanderConfig { get; }

	/// <summary>待机时长配置</summary>
	protected abstract AI_IdleConfig IdleConfig { get; }

	/// <summary>检测器刷新间隔（秒）</summary>
	protected abstract float DetectorRefreshInterval { get; }

	/// <summary>是否启用调试日志</summary>
	protected abstract bool DebugLogEnabled { get; }
#endregion

#region Virtual - 状态分类
	/// <summary>判断是否为移动状态（移动状态不清除闲逛目标）</summary>
	protected virtual bool IsMoveState(TState state) => false;

	/// <summary>判断是否为待机状态（进入时重置待机计时器）</summary>
	protected virtual bool IsIdleState(TState state) => false;
#endregion

#region Virtual - 扩展钩子
	/// <summary>状态切换前的自定义逻辑（如清理旧状态资源、重置标志位）</summary>
	protected virtual void OnBeforeSwitchState(TState previous, TState next) { }

	/// <summary>绑定额外模块（子类特有模块，如 Food、Damage 等）</summary>
	protected virtual void OnBindExtraModules() { }

	/// <summary>验证额外模块（子类特有模块的验证和警告）</summary>
	protected virtual void OnValidateExtraModules() { }

	/// <summary>更新额外计时器（子类特有计时器的递减逻辑，在状态评估前执行）</summary>
	protected virtual void UpdateExtraTimers(float deltaTime) { }

	/// <summary>评估前的数据刷新（如刷新威胁目标、群体状态等）</summary>
	protected virtual void OnPreEvaluate() { }

	/// <summary>重置运行时状态（Load 时调用，子类初始化自己的运行时字段）</summary>
	protected virtual void OnResetRuntimeState() { }

	/// <summary>修改闲逛偏移量（如狼群聚拢修正），在随机偏移生成后、安全性评估前调用</summary>
	protected virtual void ApplyWanderOffsetModifier(ref Vector2 offset) { }

	/// <summary>调试 HUD 显示的额外信息（如 " | 狼群数: 2"）</summary>
	protected virtual string GetDebugExtraInfo() => string.Empty;
#endregion

#region Lifecycle
	public override void Awake()
	{
		if (string.IsNullOrEmpty(_Data.ID))
		{
			_Data.ID = ModText.AI;
		}
	}

	/// <summary>通用初始化流程，子类 Load() 中读取存档数据后调用</summary>
	protected void InitializeAI()
	{
		_stateElapsed = 0f;
		_detectorRefreshTimer = GetDetectorPhaseOffset();
		_wanderWaitTimer = 0f;
		_hasWanderTarget = false;
		_lastPlayedAnimation = null;

		BindCommonModules();
		OnResetRuntimeState();
		TryRefreshDetector();
		OnPreEvaluate();
		PlayStateAnimation(_currentState, true);
	}

	public override void ModUpdate(float deltaTime)
	{
		if (!_isReady)
		{
			return;
		}

		// 更新状态计时器
		_stateElapsed += deltaTime;
		_detectorRefreshTimer += deltaTime;

		// 子类特有计时器递减
		UpdateExtraTimers(deltaTime);

		// 通用计时器递减（不限状态的计时器）
		_wanderWaitTimer = DecrementTimer(_wanderWaitTimer, deltaTime);

		// 刷新检测器与前置数据
		TryRefreshDetector();
		OnPreEvaluate();

		// ---- 状态机核心循环：评估 → 切换 → 帧逻辑 ----
		TState nextState = EvaluateNextState();
		if (!EqualityComparer<TState>.Default.Equals(nextState, _currentState))
		{
			SwitchState(nextState);
		}

		TickCurrentState(deltaTime);
	}
#endregion

#region StateMachine
	/// <summary>
	/// 状态切换通用逻辑：
	/// 1. 更新当前状态与计时器
	/// 2. 清理闲逛目标（非移动状态）
	/// 3. 重置待机计时器（进入待机状态）
	/// 4. 调用子类自定义切换逻辑
	/// 5. 播放状态动画、触发事件
	/// </summary>
	protected void SwitchState(TState next)
	{
		TState previous = _currentState;
		_currentState = next;
		_stateElapsed = 0f;

		// 离开移动状态时清除闲逛目标
		if (!IsMoveState(next))
		{
			_hasWanderTarget = false;
		}

		// 进入待机状态时重置待机计时器
		if (IsIdleState(next))
		{
			_idleRemainTimer = GetIdleDuration();
		}

		// 子类自定义切换逻辑
		OnBeforeSwitchState(previous, next);

		if (DebugLogEnabled)
		{
			Debug.Log($"[{GetType().Name}] {name} 状态切换: {previous} -> {next}", this);
		}

		PlayStateAnimation(next);
		OnStateChanged?.Invoke(previous, next);
	}
#endregion

#region Common Tick
	/// <summary>待机帧逻辑：停止移动，递减待机计时器</summary>
	protected void TickIdle(float deltaTime)
	{
		_mover.CanMove = false;
		_mover.HasReachedTarget = true;
		_idleRemainTimer = DecrementTimer(_idleRemainTimer, deltaTime);
	}

	/// <summary>移动帧逻辑：执行闲逛行为</summary>
	protected void TickMove(float deltaTime)
	{
		TickIdleWander(deltaTime);
	}

	/// <summary>
	/// 闲逛行为帧逻辑：
	/// 1. 若已到达闲逛目标 → 停顿等待
	/// 2. 若正在停顿 → 等待计时器到期
	/// 3. 否则 → 生成新的随机闲逛目标（含安全性评估）
	/// </summary>
	protected void TickIdleWander(float deltaTime)
	{
		var cfg = WanderConfig;
		if (!cfg.enabled)
		{
			StopMove();
			return;
		}

		// 已有闲逛目标，检查是否到达
		if (_hasWanderTarget)
		{
			float distance = Vector2.Distance(transform.position, _wanderTarget);
			if (distance <= cfg.stopDistance || _mover.HasReachedTarget)
			{
				_hasWanderTarget = false;
				_wanderWaitTimer = GetWanderPauseDuration();
				StopMove();
				return;
			}

			MoveTo(_wanderTarget);
			return;
		}

		// 停顿等待中
		if (_wanderWaitTimer > 0f)
		{
			StopMove();
			return;
		}

		// 生成新的闲逛目标
		Vector2 offset = UnityEngine.Random.insideUnitCircle * cfg.radius;

		// 子类可修改偏移（如狼群聚拢）
		ApplyWanderOffsetModifier(ref offset);

		// 限制偏移在半径内
		if (offset.sqrMagnitude > cfg.radius * cfg.radius)
		{
			offset = offset.normalized * cfg.radius;
		}

		// 安全性评估：选择更安全的位置
		offset = AI_WanderUtility.PickSaferOffset(
			transform.position,
			offset,
			cfg.radius,
			cfg.avoidHighPenalty,
			cfg.sampleCount,
			(uint)Mathf.Max(0, cfg.dangerPenalty),
			cfg.penaltyWeight);

		_wanderTarget = new Vector3(
			transform.position.x + offset.x,
			transform.position.y + offset.y,
			transform.position.z);
		_hasWanderTarget = true;
		MoveTo(_wanderTarget);
	}

	/// <summary>通用的"是否应进入移动状态"判断</summary>
	protected bool ShouldMoveBase()
	{
		if (IsMoveState(_currentState))
		{
			return _hasWanderTarget;
		}

		if (!WanderConfig.enabled)
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
	/// <summary>递减计时器，确保不低于 0</summary>
	protected static float DecrementTimer(float timer, float deltaTime)
	{
		return timer > 0f ? Mathf.Max(0f, timer - deltaTime) : 0f;
	}

	/// <summary>按间隔刷新检测器</summary>
	protected void TryRefreshDetector()
	{
		float refreshInterval = Mathf.Max(0.01f, DetectorRefreshInterval);
		if (_detectorRefreshTimer < refreshInterval)
		{
			return;
		}

		_detectorRefreshTimer -= refreshInterval;
		_detector.Update_Detector();
	}

	private float GetDetectorPhaseOffset()
	{
		float refreshInterval = Mathf.Max(0.01f, DetectorRefreshInterval);
		int seed = item?.itemData != null && item.itemData.Guid != 0
			? item.itemData.Guid
			: GetInstanceID();
		uint hash = unchecked((uint)seed * 2654435761u);
		float normalizedPhase = (hash & 0xFFFFu) / 65536f;
		return normalizedPhase * refreshInterval;
	}

	/// <summary>获取随机的闲逛停顿时长</summary>
	protected float GetWanderPauseDuration()
	{
		var cfg = WanderConfig;
		if (cfg.pauseMax <= cfg.pauseMin)
		{
			return Mathf.Max(0f, cfg.pauseMin);
		}

		return UnityEngine.Random.Range(Mathf.Max(0f, cfg.pauseMin), Mathf.Max(0f, cfg.pauseMax));
	}

	/// <summary>获取随机的待机时长</summary>
	protected float GetIdleDuration()
	{
		var cfg = IdleConfig;
		if (cfg.maxDuration <= cfg.minDuration)
		{
			return Mathf.Max(0f, cfg.minDuration);
		}

		return UnityEngine.Random.Range(Mathf.Max(0f, cfg.minDuration), Mathf.Max(0f, cfg.maxDuration));
	}

	/// <summary>获取当前血量百分比 (0~1)</summary>
	protected float GetHpRate()
	{
		if (_hp == null || _hp.MaxHp <= 0f)
		{
			Debug.LogError($"[{GetType().Name}] MaxHp 无效，无法计算血量百分比。目标物体: {name}", this);
			return 0f;
		}

		return _hp.Hp / _hp.MaxHp;
	}

	/// <summary>播放状态动画（默认不强制重播同名动画）</summary>
	protected void PlayStateAnimation(TState state, bool force = false)
	{
		if (_animator == null)
		{
			return;
		}

		string animationName = GetAnimationNameForState(state);
		if (string.IsNullOrEmpty(animationName))
		{
			Debug.LogError($"[{GetType().Name}] 状态 {state} 未配置动画名。目标物体: {name}", this);
			return;
		}

		if (!force && _lastPlayedAnimation == animationName)
		{
			return;
		}

		_animator.PlayAnimation(animationName);
		_lastPlayedAnimation = animationName;
	}

	// --- 移动控制工具方法 ---

	/// <summary>停止移动</summary>
	protected void StopMove()
	{
		_mover.CanMove = false;
		_mover.HasReachedTarget = true;
	}

	/// <summary>移动到指定位置</summary>
	protected void MoveTo(Vector3 target)
	{
		_mover.CanMove = true;
		_mover.HasReachedTarget = false;
		_mover.TargetPosition = target;
	}

	/// <summary>远离指定位置方向移动</summary>
	protected void MoveAwayFrom(Vector3 sourcePosition, float distance)
	{
		Vector3 awayDir = (transform.position - sourcePosition).normalized;
		MoveTo(transform.position + awayDir * distance);
	}

	/// <summary>面向目标方向（不移动，仅设置朝向）</summary>
	protected void FaceTarget(Vector3 targetPosition)
	{
		Vector3 dir = (targetPosition - transform.position).normalized;
		_mover.TargetPosition = transform.position + dir;
	}

	/// <summary>计算到目标的 2D 距离</summary>
	protected float DistanceTo(Transform target)
	{
		return Vector2.Distance(transform.position, target.position);
	}
#endregion

#region ModuleBinding
	/// <summary>绑定通用模块（Mover、Detector、Hp、Animator），并调用子类的额外绑定</summary>
	protected void BindCommonModules()
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

		// 子类绑定额外模块
		OnBindExtraModules();

		// 验证通用模块
		if (_mover == null)
		{
			Debug.LogError($"[{GetType().Name}] 缺少移动模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_detector == null)
		{
			Debug.LogError($"[{GetType().Name}] 缺少检测模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_hp == null)
		{
			Debug.LogError($"[{GetType().Name}] 缺少生命值模块，AI 已禁用。目标物体: {name}", this);
			_isReady = false;
		}

		if (_animator == null)
		{
			Debug.LogWarning($"[{GetType().Name}] 未找到动画模块，将跳过状态动画同步。目标物体: {name}", this);
		}

		// 子类验证额外模块
		OnValidateExtraModules();
	}
#endregion

#region Debug
	private void OnGUI()
	{
		if (!DebugLogEnabled || !Application.isPlaying || Camera.main == null)
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

		string extra = GetDebugExtraInfo();
		string text = $"状态: {GetStateTextCN(_currentState)}{extra}";
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
}
