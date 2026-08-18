using FlatWorld.WorldModel;
using UnityEngine;

public static class AI_WanderUtility
{
#region API
    /// <summary>
    /// 根据逃跑方向选择一个不主动进入水面的偏移。
    /// 当正后方是河流时，候选方向会优先选择两侧陆地，因此动物会沿河岸逃跑。
    /// </summary>
    public static Vector2 PickWaterAwareEscapeOffset(
        Vector2 origin,
        Vector2 preferredDirection,
        float distance)
    {
        float safeDistance = Mathf.Max(0.1f, distance);
        Vector2 preferred = preferredDirection.sqrMagnitude > 0.0001f
            ? preferredDirection.normalized
            : Vector2.right;

        if (ChunkMgr.Instance == null)
            return preferred * safeDistance;

        // 先检查正后方，再检查两侧和反方向；候选顺序保持确定，避免动物在河边左右抖动。
        float[] candidateAngles = { 0f, 45f, -45f, 90f, -90f, 135f, -135f, 180f };
        Vector2 bestOffset = preferred * safeDistance;
        float bestScore = float.MaxValue;
        for (int i = 0; i < candidateAngles.Length; i++)
        {
            Vector2 direction = Rotate(preferred, candidateAngles[i]);
            Vector2 candidateOffset = direction * safeDistance;
            float score = EvaluateEscapeCandidate(origin, direction, safeDistance, preferred);
            if (score < bestScore)
            {
                bestOffset = candidateOffset;
                bestScore = score;
            }
        }

        return bestOffset;
    }

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
    private static float EvaluateEscapeCandidate(
        Vector2 origin,
        Vector2 direction,
        float distance,
        Vector2 preferredDirection)
    {
        float score = (1f - Vector2.Dot(direction, preferredDirection)) * 0.5f;
        int sampleCount = Mathf.Max(2, Mathf.CeilToInt(distance));
        bool hasTerrainSample = false;
        int waterSamples = 0;
        int blockedSamples = 0;

        for (int i = 1; i <= sampleCount; i++)
        {
            float sampleDistance = distance * i / sampleCount;
            Vector2 samplePosition = origin + direction * sampleDistance;
            if (!ChunkMgr.Instance.TryGetRuntimeTerrainTile(
                    samplePosition, out RuntimeTerrainTileSample sample))
            {
                continue;
            }

            hasTerrainSample = true;
            if ((sample.Cell.Flags & TerrainCellFlags.Water) != 0)
                waterSamples++;
            if (!sample.Terrain.IsWalkable(sample.LocalCell.x, sample.LocalCell.y))
                blockedSamples++;
        }

        // 未加载区块不强行改变原有行为；已加载地形则明确惩罚水面和不可走格。
        if (!hasTerrainSample)
            return score;

        score += waterSamples * 100f;
        score += blockedSamples * 40f;
        return score;
    }

    private static Vector2 Rotate(Vector2 direction, float angleDegrees)
    {
        float radians = angleDegrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        return new Vector2(
            direction.x * cos - direction.y * sin,
            direction.x * sin + direction.y * cos);
    }

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
