using System;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

/// <summary>水生动物读取真实液体层，不把船、平台或陆地寻路网格当成水底。</summary>
public static class AquaticHabitat
{
    #region 水域采样

    public const float MinimumDepth = 0.05f;
    private static readonly Func<Vector2, bool> SwimAt = CanSwimAt;
    private static readonly Func<Vector2, bool> WalkOrSwimAt = CanWalkOrSwimAt;
    private static readonly Func<Vector2, bool> NavigateWaterAt = CanNavigateWaterAt;
    private static readonly ProfilerMarker TraverseMarker = new("FlatWorld.AquaticHabitat.Traverse");

    /// <summary>未知区块返回 false；已加载的干地返回 true、depth 为零。</summary>
    public static bool TryGetDepth(Vector2 position, out float depth)
    {
        return TryGetHabitat(position, out depth, out _);
    }

    private static bool TryGetHabitat(Vector2 position, out float depth, out bool nonWaterLiquid)
    {
        depth = 0f;
        nonWaterLiquid = false;
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (chunks == null || !chunks.TryGetRuntimeTerrainTile(position, out var sample)) return false;
        float liquidDepth = sample.Terrain.GetLiquidDepth(sample.LocalCell.x, sample.LocalCell.y);
        if (liquidDepth > 0f)
        {
            if (WorldLiquidSystem.TryGetDefinition(sample, out var liquid) && liquid.WorldWater?.WaterContact == true)
                depth = liquidDepth;
            else nonWaterLiquid = true;
        }
        return true;
    }

    public static bool CanSwimAt(Vector2 position) => TryGetDepth(position, out float depth) && depth >= MinimumDepth;

    /// <summary>沿最短环绕路径检查每小段水域，禁止高速穿岸或跨越未加载区块。</summary>
    public static bool CanTraverse(Vector2 origin, Vector2 target)
    {
        using var marker = TraverseMarker.Auto();
        return CanTraverse(origin, target, WorldTopologyRuntime.GetActiveDomain(), SwimAt);
    }

    // 两栖和受阻水生移动同时验证真实地形、导航障碍以及相邻斜角。
    public static bool CanTraverse(Vector2 origin, Vector2 target, bool allowLand, bool respectNavigationBlockers)
    {
        if (!allowLand && !respectNavigationBlockers) return CanTraverse(origin, target);
        return CanTraverse(origin, target, WorldTopologyRuntime.GetActiveDomain(),
            allowLand ? WalkOrSwimAt : NavigateWaterAt, true);
    }

    private static bool CanWalkOrSwimAt(Vector2 position) => CanNavigateHabitatAt(position, false);
    private static bool CanNavigateWaterAt(Vector2 position) => CanNavigateHabitatAt(position, true);

    private static bool CanNavigateHabitatAt(Vector2 position, bool requireWater)
    {
        WorldNavigationManager navigation = WorldNavigationManager.ExistingInstance;
        return TryGetHabitat(position, out float depth, out bool nonWaterLiquid) && !nonWaterLiquid &&
            (!requireWater || depth >= MinimumDepth) && navigation != null && navigation.IsWalkable(position);
    }

    // 同一调用内液体格不会变化，保留原线路采样点但不重复查询连续相同格。
    internal static bool CanTraverse(Vector2 origin, Vector2 target, WorldTopologyDomain topology,
        Func<Vector2, bool> canSwimAt, bool preventDiagonalCornerCutting = false)
    {
        float2 normalizedOrigin = topology.Normalize(new float2(origin.x, origin.y));
        Vector2 sample = new(normalizedOrigin.x, normalizedOrigin.y);
        if (!canSwimAt(sample)) return false;
        Vector2Int previousCell = Vector2Int.FloorToInt(sample);
        float2 shortest = topology.ShortestDelta(new float2(origin.x, origin.y), new float2(target.x, target.y));
        Vector2 delta = new(shortest.x, shortest.y);
        int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.2f));
        for (int index = 1; index <= steps; index++)
        {
            Vector2 point = origin + delta * ((float)index / steps);
            float2 normalized = topology.Normalize(new float2(point.x, point.y));
            sample = new Vector2(normalized.x, normalized.y);
            Vector2Int cell = Vector2Int.FloorToInt(sample);
            if (cell == previousCell) continue;
            if (preventDiagonalCornerCutting)
            {
                int2 cellDelta = topology.ShortestDelta(new int2(previousCell.x, previousCell.y), new int2(cell.x, cell.y));
                if (cellDelta.x != 0 && cellDelta.y != 0)
                {
                    int2 horizontal = topology.Normalize(new int2(previousCell.x + cellDelta.x, previousCell.y));
                    int2 vertical = topology.Normalize(new int2(previousCell.x, previousCell.y + cellDelta.y));
                    if (!canSwimAt(new Vector2(horizontal.x + 0.5f, horizontal.y + 0.5f)) ||
                        !canSwimAt(new Vector2(vertical.x + 0.5f, vertical.y + 0.5f))) return false;
                }
            }
            if (!canSwimAt(sample)) return false;
            previousCell = cell;
        }
        return true;
    }

    #endregion
}
