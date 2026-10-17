using System;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>成功取水后只唤醒四邻水格，各自给相邻的空水域转移整数水量。</summary>
public static class WorldLiquidReplenishment
{
    #region 补水规则
    private static readonly Vector2Int[] Directions =
        { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };

    /// <summary>只向原本同种液体的空格补水，每次取当前剩余量的四分之一，最低一级。</summary>
    public static int GetTransferLevel(RuntimeTerrainTileSample source, RuntimeTerrainTileSample target)
    {
        if (!IsOpen(source) || !IsOpen(target) ||
            ReferenceEquals(source.Terrain, target.Terrain) && source.LocalCell == target.LocalCell)
            return 0;
        int sourceLevel = source.Terrain.GetLiquidLevel(source.LocalCell.x, source.LocalCell.y);
        if (sourceLevel <= 0 || target.Terrain.GetLiquidLevel(target.LocalCell.x, target.LocalCell.y) != 0)
            return 0;
        LiquidCellValue baseline = target.Terrain.GetLiquidBaseline(target.LocalCell.x, target.LocalCell.y);
        if (baseline.LiquidLevel <= 0 || !string.Equals(
                source.Terrain.GetLiquidId(source.LocalCell.x, source.LocalCell.y),
                target.Terrain.LiquidTypes.GetId(baseline.LiquidTypeIndex), StringComparison.OrdinalIgnoreCase))
            return 0;
        return Math.Max(1, sourceLevel / 4);
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
        foreach (Vector2Int direction in Directions)
        {
            if (!TryGetNeighbour(chunks, origin, direction, out var source) || !IsOpen(source) ||
                WorldLiquidFlowObstacles.IsBlocked(source.WorldCell) ||
                !WorldLiquidSystem.TryGetDefinition(source, out var liquid) || !liquid.WorldWater.WaterContact)
                continue;
            // 每个供水格只检查一遍，不让补水产生新一轮邻居通知。
            foreach (Vector2Int targetDirection in Directions)
                if (TryGetNeighbour(chunks, source, targetDirection, out var target) &&
                    !WorldLiquidFlowObstacles.IsBlocked(target.WorldCell))
                    TryTransfer(source, target);
        }
    }

    private static bool TryGetNeighbour(ChunkMgr chunks, RuntimeTerrainTileSample sample, Vector2Int direction,
        out RuntimeTerrainTileSample neighbour) =>
        chunks.TryGetRuntimeTerrainTile((Vector2)(sample.WorldCell + direction) + Vector2.one * .5f, out neighbour);

    private static void TryTransfer(RuntimeTerrainTileSample source, RuntimeTerrainTileSample target)
    {
        int amount = GetTransferLevel(source, target);
        if (amount <= 0) return;
        int before = source.Terrain.GetLiquidLevel(source.LocalCell.x, source.LocalCell.y);
        string id = source.Terrain.GetLiquidId(source.LocalCell.x, source.LocalCell.y);
        try
        {
            if (!WorldLiquidSystem.TrySet(source, id, before - amount)) return;
            if (WorldLiquidSystem.TrySet(target, id, amount)) return;
        }
        catch
        {
            Restore();
            throw;
        }
        Restore();

        // 任一端拒绝或通知报错时归还两端水量，不能凭空消耗供水格。
        void Restore()
        {
            try
            {
                if (target.Terrain.GetLiquidLevel(target.LocalCell.x, target.LocalCell.y) != 0 &&
                    !WorldLiquidSystem.TrySet(target, null, 0))
                    throw new InvalidOperationException("邻格补水失败后无法恢复目标空水格。");
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
