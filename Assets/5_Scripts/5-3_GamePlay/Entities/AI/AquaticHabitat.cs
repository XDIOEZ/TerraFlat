using UnityEngine;

/// <summary>水生动物读取真实液体层，不把船、平台或陆地寻路网格当成水底。</summary>
public static class AquaticHabitat
{
    public const float MinimumDepth = 0.05f;

    /// <summary>未知区块返回 false；已加载的干地返回 true、depth 为零。</summary>
    public static bool TryGetDepth(Vector2 position, out float depth)
    {
        depth = 0f;
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (chunks == null || !chunks.TryGetRuntimeTerrainTile(position, out var sample)) return false;
        if (WorldLiquidSystem.TryGetDefinition(sample, out _))
            depth = sample.Terrain.GetLiquidDepth(sample.LocalCell.x, sample.LocalCell.y);
        return true;
    }

    public static bool CanSwimAt(Vector2 position) => TryGetDepth(position, out float depth) && depth >= MinimumDepth;

    /// <summary>沿最短环绕路径检查每小段水域，禁止高速穿岸或跨越未加载区块。</summary>
    public static bool CanTraverse(Vector2 origin, Vector2 target)
    {
        if (!CanSwimAt(origin)) return false;
        Vector2 delta = WorldTopologyRuntime.ShortestDelta(origin, target);
        int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.2f));
        for (int index = 1; index <= steps; index++)
            if (!CanSwimAt(WorldTopologyRuntime.NormalizePosition(origin + delta * ((float)index / steps))))
                return false;
        return true;
    }
}
