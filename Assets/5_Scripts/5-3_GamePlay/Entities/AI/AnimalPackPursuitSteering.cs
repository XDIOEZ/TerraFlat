using UnityEngine;

/// <summary>群体按稳定序号分散追击，远处走两翼或截路，近处收拢到真实攻击站位。</summary>
public static class AnimalPackPursuitSteering
{
    #region 预测与分散接近
    public static Vector2 CalculateDispersedPursuitOffset(int memberIndex, int memberCount,
        Vector2 forward, Vector2 combatOffset, float targetDistance, float attackRange,
        float spreadRadius, Vector2 targetVelocity, float predictionSeconds, float maximumPredictionDistance)
    {
        if (memberCount < 2 || !float.IsFinite(targetDistance) || !float.IsFinite(attackRange) || attackRange <= 0f)
            return combatOffset;
        if (!float.IsFinite(forward.x) || !float.IsFinite(forward.y) || forward.sqrMagnitude < .0001f)
            forward = Vector2.right;
        else forward.Normalize();
        if (!float.IsFinite(targetVelocity.x) || !float.IsFinite(targetVelocity.y)) targetVelocity = Vector2.zero;
        spreadRadius = float.IsFinite(spreadRadius) ? Mathf.Max(attackRange + .25f, spreadRadius) : attackRange + .25f;
        predictionSeconds = float.IsFinite(predictionSeconds) ? Mathf.Clamp(predictionSeconds, 0f, 2f) : 0f;
        maximumPredictionDistance = float.IsFinite(maximumPredictionDistance)
            ? Mathf.Max(0f, maximumPredictionDistance) : 0f;
        int rank = Mathf.Clamp(memberIndex, 0, memberCount - 1);
        int angularSlot = rank == 0 ? 0 : (rank & 1) != 0 ? (rank + 1) / 2 : memberCount - rank / 2;
        float angle = memberCount == 2 ? (rank == 0 ? 90f : -90f)
            : 180f - 360f * angularSlot / memberCount;
        float radians = angle * Mathf.Deg2Rad;
        Vector2 side = new(-forward.y, forward.x);
        float radius = memberCount > 2 && rank == 0 ? attackRange * .75f : spreadRadius;
        Vector2 dispersed = (forward * Mathf.Cos(radians) + side * Mathf.Sin(radians)) * radius;
        Vector2 prediction = Vector2.ClampMagnitude(targetVelocity * predictionSeconds, maximumPredictionDistance);
        // 越接近目标越收拢，避免在攻击范围外完成包围后原地等待。
        float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
            attackRange * 1.25f, attackRange + spreadRadius * 1.5f, targetDistance));
        return Vector2.Lerp(combatOffset, dispersed + prediction, blend);
    }
    #endregion
}
