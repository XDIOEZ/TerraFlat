using UnityEngine;

public static class AI_WanderUtility
{
#region API
    public static Vector2 PickSaferOffset(
        Vector3 origin,
        Vector2 baseOffset,
        float radius,
        bool enableAvoidDanger = true,
        int sampleCount = 6,
        uint dangerPenalty = 1200u,
        float penaltyWeight = 1f,
        float minimumDistance = 0f)
    {
        float safeRadius = Mathf.Max(0.01f, radius);
        float safeMinimumDistance = Mathf.Clamp(minimumDistance, 0f, safeRadius);
        Vector2 preferred = ClampOffset(baseOffset, safeMinimumDistance, safeRadius);
        if (preferred.sqrMagnitude <= 0.0001f)
        {
            preferred = RandomOffset(safeMinimumDistance, safeRadius);
        }

        if (!enableAvoidDanger)
        {
            return preferred;
        }

        if (WorldNavigationManager.Instance == null)
        {
            return preferred;
        }

        int totalSamples = Mathf.Max(1, sampleCount);
        Vector2 best = preferred;
        float bestScore = EvaluateCandidate(origin, best, preferred, safeRadius, dangerPenalty, penaltyWeight);

        for (int i = 0; i < totalSamples; i++)
        {
            Vector2 candidate = RandomOffset(safeMinimumDistance, safeRadius);
            float score = EvaluateCandidate(origin, candidate, preferred, safeRadius, dangerPenalty, penaltyWeight);
            if (score < bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }
#endregion

#region Helpers
    private static float EvaluateCandidate(
        Vector3 origin,
        Vector2 candidate,
        Vector2 preferred,
        float radius,
        uint dangerPenalty,
        float penaltyWeight)
    {
        Vector2 worldPos = new Vector2(origin.x + candidate.x, origin.y + candidate.y);
        bool ok = WorldNavigationManager.Instance.TryGetCell(worldPos, out uint penalty, out bool isWalkable);

        float score = 0f;

        float preferDistance = Vector2.Distance(candidate, preferred);
        score += Mathf.Clamp01(preferDistance / Mathf.Max(0.01f, radius)) * 0.35f;

        if (!ok || !isWalkable)
        {
            return score + 100f;
        }

        if (penalty >= dangerPenalty)
        {
            score += 10f;
        }

        score += (penalty / 1000f) * Mathf.Max(0f, penaltyWeight);
        return score;
    }

    private static Vector2 ClampOffset(Vector2 offset, float minimumDistance, float radius)
    {
        float magnitude = offset.magnitude;
        if (magnitude <= 0.0001f)
            return RandomOffset(minimumDistance, radius);

        if (magnitude < minimumDistance)
            return offset / magnitude * minimumDistance;

        if (magnitude <= radius)
            return offset;

        return offset.normalized * radius;
    }

    private static Vector2 RandomOffset(float minimumDistance, float radius)
    {
        float angle = Random.value * Mathf.PI * 2f;
        float distance = Mathf.Sqrt(Random.Range(
            minimumDistance * minimumDistance,
            radius * radius));
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
    }
#endregion
}
