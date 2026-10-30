using System;
using UnityEngine;

/// <summary>
/// 实例独立的飞行通行配置，默认穿越已加载地形及建筑、不读取地面代价。
/// 仅查询正式运行时地形是否存在；绝不向共享 WorldNavigationGrid 写入飞行权重。
/// </summary>
[Serializable]
public sealed class AnimalFlightNavigationProfile
{
    #region 通行配置
    public bool crossGroundObstacles = true;
    public float sampleSpacing = 0.25f;

    // 游标只在一次不中断的前视弧线内复用，实际移动与下一次预览重新读取地形。
    internal struct TraversalCursor
    {
        internal int CellX;
        internal int CellY;
        internal bool HasValidatedCell;
        internal int TerrainQueryCount;
    }

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
        var cursor = new TraversalCursor();
        return CanTraverse(origin, target, ref query, ref cursor);
    }

    internal bool CanTraverse(Vector2 origin, Vector2 target, ref ChunkMgr.RuntimeTerrainPresenceQuery query,
        ref TraversalCursor cursor)
    {
        // 需要额外地面可走判断时保留逐段复核，不把复合规则结果带到下一小段。
        if (!crossGroundObstacles) cursor.HasValidatedCell = false;
        Vector2 delta = query.ShortestDelta(origin, target);
        int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / Mathf.Max(0.05f, sampleSpacing)));
        for (int index = 1; index <= steps; index++)
        {
            Vector2 sample = query.NormalizePosition(origin + delta * ((float)index / steps));
            int cellX = Mathf.FloorToInt(sample.x);
            int cellY = Mathf.FloorToInt(sample.y);
            // 同一条同步弧线的连续同格只检查一次，包括前后相接的预览小段。
            if (cursor.HasValidatedCell && cellX == cursor.CellX && cellY == cursor.CellY)
                continue;
            cursor.TerrainQueryCount++;
            if (!query.IsLoadedNormalized(sample) || (!crossGroundObstacles && !Mod_AnimalFlightMovement.CanLand(sample)))
                return false;
            cursor.HasValidatedCell = true;
            cursor.CellX = cellX;
            cursor.CellY = cellY;
        }
        return true;
    }
    #endregion
}
