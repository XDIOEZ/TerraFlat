using UnityEngine;

public partial class Mod_Weapon_AnimationAction
{
    #region 命中卡肉
    [Header("命中卡肉")]
    [SerializeField, Range(0f, 0.25f), Tooltip("造成正伤害后只暂停本武器动画；0关闭，默认0.12秒，每段挥击最多一次。")]
    private float hitStopDuration = CombatHitFeedback.DefaultHitStopDuration;
    private WeaponHitStopClock hitStopClock;
    private Mod_Damage hitFeedbackSource;

    public float HitStopDuration => CombatHitFeedback.HitStopDuration(hitStopDuration);
    public bool IsHitStopped => hitStopClock.IsPaused;
    private float AttackTime => (float)hitStopClock.AnimationTime(Time.timeAsDouble);

    private void BindHitFeedback(Mod_Damage source)
    {
        UnbindHitFeedback();
        hitFeedbackSource = source;
        if (hitFeedbackSource != null) hitFeedbackSource.OnDamageApplied += HandleImpactDamage;
    }

    private void UnbindHitFeedback()
    {
        if (hitFeedbackSource != null) hitFeedbackSource.OnDamageApplied -= HandleImpactDamage;
        hitFeedbackSource = null;
        ResetHitStop();
    }

    private void HandleImpactDamage(float damage)
    {
        if (!isAttacking || !isActiveAndEnabled || animator == null) return;
        if (hitStopClock.TryPause(damage, HitStopDuration, Time.timeAsDouble, Time.unscaledTimeAsDouble))
            animator.speed = 0f;
    }

    private bool UpdateHitStop()
    {
        if (hitStopClock.ResumeIfReady(Time.timeAsDouble, Time.unscaledTimeAsDouble))
            ApplyAttackSpeedToAnimator();
        return hitStopClock.IsPaused;
    }

    private void ResetHitStop()
    {
        bool wasPaused = hitStopClock.IsPaused;
        hitStopClock.Reset();
        if (wasPaused) ApplyAttackSpeedToAnimator();
    }

    /// <summary>换手、隐藏和回池都撤销停顿，避免再次装备时继承零动画速度。</summary>
    private void OnDisable()
    {
        isHoldingInput = false;
        ResetToIdle(playAnimation: false);
    }
    #endregion
}
