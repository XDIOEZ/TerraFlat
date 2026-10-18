using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>成功取水后在本格与四邻涉及的相邻边上均衡水量，不扩大到整个水域。</summary>
public static class WorldLiquidReplenishment
{
    #region 补水规则
    private static readonly Vector2Int[] Directions =
        { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };

    /// <summary>原有同种水域按水量差的一半补水，差一级停稳，供水格不会被搬空。</summary>
    public static int GetTransferLevel(RuntimeTerrainTileSample source, RuntimeTerrainTileSample target)
    {
        if (!IsOpen(source) || !IsOpen(target) ||
            ReferenceEquals(source.Terrain, target.Terrain) && source.LocalCell == target.LocalCell)
            return 0;
        int sourceLevel = source.Terrain.GetLiquidLevel(source.LocalCell.x, source.LocalCell.y);
        int targetLevel = target.Terrain.GetLiquidLevel(target.LocalCell.x, target.LocalCell.y);
        if (sourceLevel - targetLevel < 2)
            return 0;
        string id = source.Terrain.GetLiquidId(source.LocalCell.x, source.LocalCell.y);
        LiquidCellValue baseline = target.Terrain.GetLiquidBaseline(target.LocalCell.x, target.LocalCell.y);
        if (baseline.LiquidLevel <= 0 || !string.Equals(
                id, target.Terrain.LiquidTypes.GetId(baseline.LiquidTypeIndex), StringComparison.OrdinalIgnoreCase) ||
            targetLevel > 0 && !string.Equals(id,
                target.Terrain.GetLiquidId(target.LocalCell.x, target.LocalCell.y), StringComparison.OrdinalIgnoreCase))
            return 0;
        return (sourceLevel - targetLevel) / 2;
    }

    private static bool IsOpen(RuntimeTerrainTileSample sample)
    {
        if (sample.Terrain == null || sample.Terrain.IsDisposed) return false;
        TerrainCell cell = sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y);
        return cell.BlockingTileId == 0 && (cell.Flags & TerrainCellFlags.Blocking) == 0;
    }
    #endregion

    #region 成功取水通知
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void Subscribe()
    {
        WorldLiquidSystem.SurfaceLiquidPumped -= OnSurfaceLiquidPumped;
        WorldLiquidSystem.SurfaceLiquidPumped += OnSurfaceLiquidPumped;
    }

    private static void OnSurfaceLiquidPumped(RuntimeTerrainTileSample origin)
    {
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        if (!GameNetwork.HasStateAuthority || chunks == null || origin.Terrain == null || origin.Terrain.IsDisposed ||
            !chunks.TryGetRuntimeTerrainTile((Vector2)origin.WorldCell + Vector2.one * .5f, out var current) ||
            !ReferenceEquals(current.Terrain, origin.Terrain))
            return;
        var edges = new List<(RuntimeTerrainTileSample A, RuntimeTerrainTileSample B)>(16);
        foreach (Vector2Int direction in Directions)
        {
            if (!TryGetNeighbour(chunks, origin, direction, out var source))
                continue;
            edges.Add((origin, source));
            foreach (Vector2Int targetDirection in Directions)
                if (targetDirection != -direction && TryGetNeighbour(chunks, source, targetDirection, out var target))
                    edges.Add((source, target));
        }
        // 只复用固定邻边，每次转移都降低水量平方和，因此有限步后自然停稳。
        bool changed;
        do
        {
            changed = false;
            foreach (var edge in edges)
                changed |= TryBalance(edge.A, edge.B);
        } while (changed);
    }

    private static bool TryGetNeighbour(ChunkMgr chunks, RuntimeTerrainTileSample sample, Vector2Int direction,
        out RuntimeTerrainTileSample neighbour) =>
        chunks.TryGetRuntimeTerrainTile((Vector2)(sample.WorldCell + direction) + Vector2.one * .5f, out neighbour);

    private static bool TryBalance(RuntimeTerrainTileSample source, RuntimeTerrainTileSample target)
    {
        if (!IsOpen(source) || !IsOpen(target) || WorldLiquidFlowObstacles.IsBlocked(source.WorldCell) ||
            WorldLiquidFlowObstacles.IsBlocked(target.WorldCell)) return false;
        if (source.Terrain.GetLiquidLevel(source.LocalCell.x, source.LocalCell.y) <
            target.Terrain.GetLiquidLevel(target.LocalCell.x, target.LocalCell.y))
            (source, target) = (target, source);
        return WorldLiquidSystem.TryGetDefinition(source, out var liquid) && liquid.WorldWater.WaterContact &&
            TryTransfer(source, target);
    }

    private static bool TryTransfer(RuntimeTerrainTileSample source, RuntimeTerrainTileSample target)
    {
        int amount = GetTransferLevel(source, target);
        if (amount <= 0) return false;
        int before = source.Terrain.GetLiquidLevel(source.LocalCell.x, source.LocalCell.y);
        int targetBefore = target.Terrain.GetLiquidLevel(target.LocalCell.x, target.LocalCell.y);
        string id = source.Terrain.GetLiquidId(source.LocalCell.x, source.LocalCell.y);
        string targetId = target.Terrain.GetLiquidId(target.LocalCell.x, target.LocalCell.y);
        try
        {
            if (!WorldLiquidSystem.TrySet(source, id, before - amount)) return false;
            if (WorldLiquidSystem.TrySet(target, id, targetBefore + amount)) return true;
        }
        catch
        {
            Restore();
            throw;
        }
        Restore();
        return false;

        // 任一端拒绝或通知报错时归还两端水量，不能凭空消耗供水格。
        void Restore()
        {
            try
            {
                if (target.Terrain.GetLiquidLevel(target.LocalCell.x, target.LocalCell.y) != targetBefore &&
                    !WorldLiquidSystem.TrySet(target, targetId, targetBefore))
                    throw new InvalidOperationException("邻格补水失败后无法恢复目标水量。");
            }
            finally
            {
                if (source.Terrain.GetLiquidLevel(source.LocalCell.x, source.LocalCell.y) != before &&
                    !WorldLiquidSystem.TrySet(source, id, before))
                    throw new InvalidOperationException("邻格补水失败后无法归还来源水量。");
            }
        }
    }
    #endregion
}
