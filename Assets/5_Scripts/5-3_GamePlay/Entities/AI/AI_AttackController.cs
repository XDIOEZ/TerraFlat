using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AI 攻击控制器，封装攻击伤害窗口的开启/关闭、冷却计时等逻辑。
/// 可组合到任何需要攻击行为的 AI 中，消除攻击逻辑的重复代码。
///
/// 使用方式：
/// 1. 在 AI 子类中声明字段: AI_AttackController _attack = new AI_AttackController();
/// 2. OnBindExtraModules 中调用 _attack.Bind(item) 绑定伤害组件
/// 3. UpdateExtraTimers 中调用 _attack.Update(deltaTime) 更新冷却和窗口
/// 4. TickAttack 中使用 _attack.StartWindow / StopWindow 控制攻击
/// 5. OnBeforeSwitchState 中处理进入/离开攻击状态的逻辑
///
/// 注意：Bind() 会自动处理 Mod_Damage_AI 的动画事件冲突，
/// 取消其动画事件订阅，由本控制器统一管理伤害碰撞器的开关。
/// </summary>
public class AI_AttackController
{
#region Fields
	private readonly List<Mod_Damage> _damageMods = new List<Mod_Damage>();
	private float _cooldownTimer;
	private float _windowStartDelayTimer;
	private float _windowRemainTimer;
	private bool _windowTriggered;
	private bool _damageWindowActive;
	/// <summary>缓存的动画接收器引用，用于同步 IsAttacking 状态</summary>
	private Mod_AnimatorController_Receiver _animatorReceiver;
	/// <summary>是否已取消 Mod_Damage_AI 的动画事件订阅</summary>
	private bool _hasUnsubscribedDamageAI;
#endregion

#region Properties
	/// <summary>攻击冷却时间（秒）</summary>
	public float Cooldown { get; set; }

	/// <summary>伤害窗口持续时间（秒）</summary>
	public float DamageWindow { get; set; }

	/// <summary>攻击动画开始后，延迟多少秒开启伤害碰撞。</summary>
	public float DamageWindowStartDelay { get; set; }

	/// <summary>冷却是否已结束</summary>
	public bool IsCooldownDone => _cooldownTimer <= 0f;

	/// <summary>是否已触发当前伤害窗口（防止同一窗口内重复触发）</summary>
	public bool IsWindowTriggered => _windowTriggered;

	/// <summary>伤害碰撞当前是否处于有效窗口。</summary>
	public bool IsDamageWindowActive => _damageWindowActive;

	/// <summary>是否找到伤害组件</summary>
	public bool HasDamageMods => _damageMods.Count > 0;
#endregion

#region Public API
	/// <summary>
	/// 绑定 Item 上的所有 Mod_Damage 组件，并处理 Mod_Damage_AI 的动画事件冲突。
	/// 对于 Mod_Damage_AI，会取消其 OnAttackStart/OnAttackStop 动画事件订阅，
	/// 改由本控制器统一管理伤害碰撞器的开关，避免动画事件与控制器状态不同步。
	/// </summary>
	public void Bind(Item item)
	{
		_damageMods.Clear();
		Mod_Damage[] damageModules = item.GetComponentsInChildren<Mod_Damage>(true);
		for (int i = 0; i < damageModules.Length; i++)
		{
			Mod_Damage damageModule = damageModules[i];
			if (damageModule != null)
				_damageMods.Add(damageModule);
		}

		// 缓存 Mod_AnimatorController_Receiver，用于同步 IsAttacking 状态
		_animatorReceiver = item.GetComponentInChildren<Mod_AnimatorController_Receiver>(true);

		// 首次尝试取消 Mod_Damage_AI 的动画事件订阅
		TryUnsubscribeDamageAI();

		SetDamageEnabled(false);
	}

	/// <summary>重置所有攻击状态（Load 时调用）</summary>
	public void Reset()
	{
		_cooldownTimer = 0f;
		_windowStartDelayTimer = 0f;
		_windowRemainTimer = 0f;
		_windowTriggered = false;
		_damageWindowActive = false;
		SetDamageEnabled(false);
		SetAnimatorAttacking(false);
	}

	/// <summary>
	/// 每帧更新冷却和伤害窗口计时器。
	/// 当伤害窗口到期时自动关闭伤害碰撞并进入冷却。
	/// </summary>
	public void Update(float deltaTime)
	{
		// 懒取消：如果 Bind() 时 Mod_Damage_AI 还未初始化，在此处重试
		if (!_hasUnsubscribedDamageAI)
		{
			TryUnsubscribeDamageAI();
		}

		// 冷却计时器递减
		if (_cooldownTimer > 0f)
		{
			_cooldownTimer = Mathf.Max(0f, _cooldownTimer - deltaTime);
		}

		float remainingDeltaTime = Mathf.Max(0f, deltaTime);
		if (_windowStartDelayTimer > 0f)
		{
			if (remainingDeltaTime < _windowStartDelayTimer)
			{
				_windowStartDelayTimer -= remainingDeltaTime;
				return;
			}

			remainingDeltaTime -= _windowStartDelayTimer;
			_windowStartDelayTimer = 0f;
			ActivateDamageWindow();
		}

		// 伤害窗口计时器递减
		if (_windowRemainTimer <= 0f)
			return;

		_windowRemainTimer = Mathf.Max(0f, _windowRemainTimer - remainingDeltaTime);
		if (_windowRemainTimer > 0f)
			return;

		// 伤害窗口结束：关闭伤害碰撞，开始冷却
		SetDamageEnabled(false);
		_damageWindowActive = false;
		SetAnimatorAttacking(false);
		_windowTriggered = false;
		if (Cooldown > 0f)
		{
			_cooldownTimer = Mathf.Max(_cooldownTimer, Cooldown);
		}
	}

	/// <summary>
	/// 开启攻击伤害窗口：
	/// - 标记窗口已触发
	/// - 起手阶段保持动画接收器的 IsAttacking 关闭
	/// - 到达动画有效帧后同步 IsAttacking 并启用伤害碰撞
	/// - 通过动画控制器播放攻击动画
	/// </summary>
	public void StartWindow(
		Mod_AnimatorController animator,
		string attackAnimName,
		Vector2 attackDirection)
	{
		AlignDamageDirection(attackDirection);
		_windowTriggered = true;
		_windowStartDelayTimer = Mathf.Max(0f, DamageWindowStartDelay);
		_windowRemainTimer = 0f;
		_damageWindowActive = false;

		// Attack.anim 的首帧为 IsAttacking=false。起手也保持 false，避免首击时
		// 控制器先写 true、动画首帧又回写 false，从而产生额外的启动/停止事件。
		SetAnimatorAttacking(false);

		// 动画有效帧到达前保持伤害关闭，避免攻击起手帧提前命中。
		SetDamageEnabled(false);

		// 播放攻击动画
		if (animator != null && !string.IsNullOrEmpty(attackAnimName))
		{
			animator.ForcePlayAnimation(attackAnimName);
		}

		if (_windowStartDelayTimer <= 0f)
			ActivateDamageWindow();
	}

	/// <summary>停止攻击伤害窗口（关闭伤害碰撞）</summary>
	public void StopWindow()
	{
		_windowStartDelayTimer = 0f;
		_windowRemainTimer = 0f;
		_damageWindowActive = false;
		SetDamageEnabled(false);
		SetAnimatorAttacking(false);
	}

	/// <summary>进入攻击状态时调用（重置窗口触发标记）</summary>
	public void OnEnterAttackState()
	{
		_windowTriggered = false;
	}

	/// <summary>
	/// 离开攻击状态时调用：
	/// - 停止伤害窗口
	/// - 进入冷却
	/// </summary>
	public void OnExitAttackState()
	{
		StopWindow();
		if (Cooldown > 0f)
		{
			_cooldownTimer = Cooldown;
		}
	}
#endregion

#region Private
	/// <summary>到达动画有效帧后开启伤害碰撞窗口。</summary>
	private void ActivateDamageWindow()
	{
		if (_damageWindowActive)
			return;

		_damageWindowActive = true;
		_windowRemainTimer = Mathf.Max(0.01f, DamageWindow);
		// 视觉/事件状态与实际伤害窗口同时开始，首击和后续攻击使用同一时序。
		SetAnimatorAttacking(true);
		SetDamageEnabled(true);
	}

	/// <summary>
	/// 取消所有 Mod_Damage_AI 的动画事件订阅。
	/// Mod_Damage_AI 在 Load() 中订阅了 OnAttackStart/OnAttackStop，
	/// 但由 AI_AttackController 统一管理碰撞器开关，
	/// 避免动画事件在不恰当的时机关闭碰撞器。
	/// </summary>
	private void TryUnsubscribeDamageAI()
	{
		bool allDone = true;
		for (int i = 0; i < _damageMods.Count; i++)
		{
			if (!(_damageMods[i] is Mod_Damage_AI damageAI))
			{
				continue;
			}

			// Mod_Damage_AI.Load() 可能还未执行，animator 为 null
			if (damageAI.animator == null)
			{
				allDone = false;
				continue;
			}

			damageAI.animator.OnAttackStart -= damageAI.StartAttack;
			damageAI.animator.OnAttackStop -= damageAI.StopAttack;
		}

		_hasUnsubscribedDamageAI = allDone;
	}

	/// <summary>
	/// 同步 Mod_AnimatorController_Receiver 的 IsAttacking 状态。
	/// 这确保动画接收器与攻击控制器的状态一致，
	/// 防止 IsAttacking 状态不同步导致 OnAttackStop 事件意外触发。
	/// </summary>
	private void SetAnimatorAttacking(bool attacking)
	{
		if (_animatorReceiver != null)
		{
			_animatorReceiver.IsAttacking = attacking;
		}
	}

	private void AlignDamageDirection(Vector2 attackDirection)
	{
		if (Mathf.Abs(attackDirection.x) < 0.001f)
		{
			return;
		}

		for (int i = 0; i < _damageMods.Count; i++)
		{
			if (_damageMods[i] is Mod_Damage_AI damageAI)
			{
				damageAI.SnapToDirection(attackDirection);
			}
		}
	}

	private void SetDamageEnabled(bool enabled)
	{
		for (int i = 0; i < _damageMods.Count; i++)
		{
			Mod_Damage damageMod = _damageMods[i];
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
