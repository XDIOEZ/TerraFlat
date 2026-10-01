using System;
using UnityEngine;

/// <summary>
/// 实例独立的飞行通行配置，默认穿越已加载地形及建筑、不读取地面代价。
/// 仅查询正式运行时地形是否存在；绝不向共享 WorldNavigationGrid 写入飞行权重。
/// </summary>
[Serializable]
public sealed class BirdFlightNavigationProfile
{
    #region 通行配置
    public bool crossGroundObstacles = true;
    public float sampleSpacing = 0.25f;

    /// <summary>沿地面映射线段检查已加载格，避免跨越未提交区块；不使用 Physics2D。</summary>
    public bool CanTraverse(Vector2 origin, Vector2 target)
    {
        ChunkMgr chunks = ChunkMgr.Instance;
        if (chunks == null || !chunks.TryCreateTerrainPresenceQuery(out ChunkMgr.RuntimeTerrainPresenceQuery query))
            return false;

        return CanTraverse(origin, target, ref query);
    }

    internal bool CanTraverse(Vector2 origin, Vector2 target, ref ChunkMgr.RuntimeTerrainPresenceQuery query)
    {
        Vector2 delta = query.ShortestDelta(origin, target);
        int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / Mathf.Max(0.05f, sampleSpacing)));
        int previousX = 0;
        int previousY = 0;
        bool hasPreviousCell = false;
        for (int index = 1; index <= steps; index++)
        {
            Vector2 sample = query.NormalizePosition(origin + delta * ((float)index / steps));
            int cellX = Mathf.FloorToInt(sample.x);
            int cellY = Mathf.FloorToInt(sample.y);
            // 同一格的地形就绪和地面可走状态只检查一次。
            if (hasPreviousCell && cellX == previousX && cellY == previousY)
                continue;
            hasPreviousCell = true;
            previousX = cellX;
            previousY = cellY;
            if (!query.IsLoadedNormalized(sample) || (!crossGroundObstacles && !Mod_AI_Bird.CanLand(sample)))
                return false;
        }
        return true;
    }
    #endregion
}
