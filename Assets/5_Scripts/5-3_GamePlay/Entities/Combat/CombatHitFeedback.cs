using System;
using UnityEngine;

/// <summary>命中手感只消费实际伤害回执，不参与伤害、位移或随机数结算。</summary>
public static class CombatHitFeedback
{
    #region 命中反馈参数与衰减
    public const float DefaultHitStopDuration = 0.12f;
    public const float DefaultShakeDuration = 0.16f;
    public const float DefaultShakeDistance = 0.035f;

    public static bool HasImpact(float damage) => float.IsFinite(damage) && damage > 0f;
    public static float HitStopDuration(float seconds) => float.IsFinite(seconds) ? Mathf.Clamp(seconds, 0f, 0.25f) : 0f;
    public static float ShakeDuration(float seconds) => float.IsFinite(seconds) ? Mathf.Clamp(seconds, 0f, 1f) : 0f;
    public static float ShakeDistance(float distance) => float.IsFinite(distance) ? Mathf.Clamp(distance, 0f, 0.25f) : 0f;

    public static Vector2 ShakeDirection(Vector2 direction)
    {
        if (!float.IsFinite(direction.x) || !float.IsFinite(direction.y) || direction.sqrMagnitude < 0.000001f)
            return Vector2.right;
        return direction.normalized;
    }

    /// <summary>沿受力方向轻颤并归零，GO 与原生资源共用同一条无随机数曲线。</summary>
    public static Vector2 EvaluateShake(float elapsed, float duration, float distance, Vector2 direction)
    {
        duration = ShakeDuration(duration);
        distance = ShakeDistance(distance);
        if (!float.IsFinite(elapsed) || elapsed < 0f || duration <= 0f || elapsed >= duration || distance <= 0f)
            return Vector2.zero;
        float remaining = 1f - elapsed / duration;
        return ShakeDirection(direction) * (distance * remaining * remaining * Mathf.Cos(elapsed * 25f * Mathf.PI * 2f));
    }
    #endregion
}

/// <summary>每段挥击独立的动画时钟，真实秒控制恢复，游戏秒扣除停顿以保护连击窗口。</summary>
internal struct WeaponHitStopClock
{
    #region 局部动画时钟
    private bool usedThisAttack;
    private double pausedAtGameTime;
    private double resumeAtRealTime;
    private double pausedGameSeconds;
    public bool IsPaused { get; private set; }

    public double AnimationTime(double gameTime) => (IsPaused ? pausedAtGameTime : gameTime) - pausedGameSeconds;

    public bool TryPause(float damage, float duration, double gameTime, double realTime)
    {
        duration = CombatHitFeedback.HitStopDuration(duration);
        if (usedThisAttack || !CombatHitFeedback.HasImpact(damage) || duration <= 0f ||
            !double.IsFinite(gameTime) || !double.IsFinite(realTime)) return false;
        usedThisAttack = true;
        IsPaused = true;
        pausedAtGameTime = gameTime;
        resumeAtRealTime = realTime + duration;
        return true;
    }

    public bool ResumeIfReady(double gameTime, double realTime)
    {
        if (!IsPaused || !double.IsFinite(gameTime) || !double.IsFinite(realTime) || realTime < resumeAtRealTime)
            return false;
        pausedGameSeconds += Math.Max(0d, gameTime - pausedAtGameTime);
        IsPaused = false;
        return true;
    }

    public void Reset() => this = default;
    #endregion
}
