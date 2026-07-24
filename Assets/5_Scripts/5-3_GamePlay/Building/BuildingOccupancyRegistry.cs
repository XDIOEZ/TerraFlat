// 建筑动态占地覆盖层：不修改地形 TileData，只提交导航脏格。
using System.Collections.Generic;
using UnityEngine;

public static class BuildingOccupancyRegistry
{
    private static readonly Dictionary<Vector2Int, HashSet<Mod_Building>> OccupantsByCell = new();
    private static readonly Dictionary<Mod_Building, HashSet<Vector2Int>> CellsByBuilding = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        OccupantsByCell.Clear();
        CellsByBuilding.Clear();
    }

    public static bool IsOccupied(Vector2Int cell, Mod_Building except = null)
    {
        if (!OccupantsByCell.TryGetValue(cell, out HashSet<Mod_Building> occupants))
            return false;

        occupants.RemoveWhere(building => building == null || !building.isActiveAndEnabled || !building.IsInstalled());
        foreach (Mod_Building building in occupants)
        {
            if (building != except)
                return true;
        }

        if (occupants.Count == 0)
            OccupantsByCell.Remove(cell);
        return false;
    }

    public static bool GetEffectiveWalkable(Vector2Int cell, bool terrainWalkable)
        => terrainWalkable && !IsOccupied(cell);

    public static void Register(Mod_Building building, IEnumerable<Vector2Int> cells)
    {
        if (building == null || cells == null)
            return;

        HashSet<Vector2Int> nextCells = new(cells);
        if (CellsByBuilding.TryGetValue(building, out HashSet<Vector2Int> currentCells) &&
            currentCells.SetEquals(nextCells))
        {
            foreach (Vector2Int cell in nextCells)
                RefreshCell(cell);
            return;
        }

        Unregister(building);
        if (nextCells.Count == 0)
            return;

        CellsByBuilding[building] = nextCells;
        foreach (Vector2Int cell in nextCells)
        {
            if (!OccupantsByCell.TryGetValue(cell, out HashSet<Mod_Building> occupants))
            {
                occupants = new HashSet<Mod_Building>();
                OccupantsByCell[cell] = occupants;
            }

            occupants.Add(building);
            RefreshCell(cell);
        }
    }

    public static void Unregister(Mod_Building building)
    {
        if (building == null || !CellsByBuilding.TryGetValue(building, out HashSet<Vector2Int> cells))
            return;

        CellsByBuilding.Remove(building);
        foreach (Vector2Int cell in cells)
        {
            if (OccupantsByCell.TryGetValue(cell, out HashSet<Mod_Building> occupants))
            {
                occupants.Remove(building);
                if (occupants.Count == 0)
                    OccupantsByCell.Remove(cell);
            }

            RefreshCell(cell);
        }
    }

    private static void RefreshCell(Vector2Int cell)
    {
        if (ChunkMgr.Instance == null)
            return;

        Vector2 center = new(cell.x + 0.5f, cell.y + 0.5f);
        ChunkMgr.Instance.GetChunkBy_ItemPosition(center, out Chunk chunk);
        List<TileData> tiles = chunk?.Map?.Data?.GetTileListAt(cell);
        if (tiles == null || tiles.Count == 0)
            return;

        chunk.Map.MarkPenaltyDirty(cell);
        chunk.Map.BackTilePenalty_Async();
    }
}
