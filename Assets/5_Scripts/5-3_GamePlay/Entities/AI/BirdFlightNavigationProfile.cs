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
        if (chunks == null)
            return false;
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(origin, target);
        int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / Mathf.Max(0.05f, sampleSpacing)));
        for (int index = 1; index <= steps; index++)
        {
            Vector2 sample = WorldTopologyRuntime.NormalizePosition(origin + delta * ((float)index / steps));
            if (!chunks.TryGetRuntimeTerrainTile(sample, out _) || (!crossGroundObstacles && !AI_Bird.CanLand(sample)))
                return false;
        }
        return true;
    }
    #endregion
}
