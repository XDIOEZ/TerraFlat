// 动态建筑放置占用层：以离散世界格记录建筑位置，不依赖任何 Physics2D Collider。
// 该层同时服务放置冲突判断与导航脏格更新；建筑实体的 Collider 只负责交互/受击等运行时物理。
using System.Collections.Generic;
using UnityEngine;

public static class BuildingOccupancyRegistry
{
    public static uint Revision { get; private set; } // LOS 快照可独立于导航值变化失效。
    public static event System.Action<Vector2Int> CellChanged; // 少量地图快照订阅者，禁止逐 AI 订阅。
    private static readonly Dictionary<Vector2Int, HashSet<Mod_Building>> OccupantsByCell = new();
    private static readonly Dictionary<Mod_Building, HashSet<Vector2Int>> CellsByBuilding = new();
    // 开门只撤销导航阻挡，不能撤销建筑放置占用，否则其它建筑可以重叠放进门里。
    private static readonly HashSet<Mod_Building> PassableBuildings = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        OccupantsByCell.Clear();
        CellsByBuilding.Clear();
        PassableBuildings.Clear();
        Revision++;
        CellChanged = null;
    }

    public static bool IsOccupied(Vector2Int cell, Mod_Building except = null, int layer = -1)
    {
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        if (!OccupantsByCell.TryGetValue(cell, out HashSet<Mod_Building> occupants))
            return false;

        occupants.RemoveWhere(building => building == null || !building.isActiveAndEnabled || !building.IsInstalled());
        foreach (Mod_Building building in occupants)
        {
            if (building != except && (layer < 0 || GetPlacementLayer(building) == layer))
                return true;
        }

        if (occupants.Count == 0)
            OccupantsByCell.Remove(cell);
        return false;
    }

    /// <summary>检查指定放置格是否空闲；这是动态建筑放置冲突的权威入口。</summary>
    public static bool CanPlace(Vector2Int cell, Mod_Building except, out string reason)
    {
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        int layer = except == null ? 0 : GetPlacementLayer(except);
        if (!IsOccupied(cell, except, layer) && !MechanicalWorld.IsOccupied(cell, layer, except?.item?.itemData?.Guid ?? 0))
        {
            reason = null;
            return true;
        }

        reason = $"地块 ({cell.x},{cell.y}) 已被建筑占用";
        return false;
    }

    public static bool GetEffectiveWalkable(Vector2Int cell, bool terrainWalkable)
    {
        if (!terrainWalkable) return false;
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        if (!OccupantsByCell.TryGetValue(cell, out HashSet<Mod_Building> occupants)) return true;
        foreach (Mod_Building building in occupants)
            if (building != null && building.isActiveAndEnabled && building.IsInstalled() && GetPlacementLayer(building) == 0 && !PassableBuildings.Contains(building))
                return false;
        return true;
    }

    /// <summary>门状态的正式导航入口；同一建筑的放置占格保持不变。</summary>
    public static void SetPassable(Mod_Building building, bool passable)
    {
        if (building == null) return;
        bool changed = passable ? PassableBuildings.Add(building) : PassableBuildings.Remove(building);
        if (changed && CellsByBuilding.TryGetValue(building, out HashSet<Vector2Int> cells))
            foreach (Vector2Int cell in cells) RefreshCell(cell);
    }

    /// <summary>普通建筑仍占 Layer0，上层桥式传动只在自己的层内互斥。</summary>
    public static int GetPlacementLayer(Mod_Building building)
        => BuildingPlacementLifecycle.GetExtension(building?.item)?.OccupancyLayer ?? 0;

    public static void Register(Mod_Building building, IEnumerable<Vector2Int> cells)
    {
        if (building == null || cells == null)
            return;

        var nextCells = new HashSet<Vector2Int>();
        foreach (Vector2Int cell in cells)
            nextCells.Add(WorldTopologyRuntime.NormalizeCell(cell));
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

    /// <summary>注册单格动态建筑；当前可交互建筑与墙体一样以格位置作为放置槽。</summary>
    public static void Register(Mod_Building building, Vector2Int cell)
    {
        if (building == null)
            return;

        cell = WorldTopologyRuntime.NormalizeCell(cell);
        if (CellsByBuilding.TryGetValue(building, out HashSet<Vector2Int> currentCells) &&
            currentCells.Count == 1 && currentCells.Contains(cell))
        {
            RefreshCell(cell);
            return;
        }

        Unregister(building);
        var cells = new HashSet<Vector2Int> { cell };
        CellsByBuilding[building] = cells;
        if (!OccupantsByCell.TryGetValue(cell, out HashSet<Mod_Building> occupants))
        {
            occupants = new HashSet<Mod_Building>();
            OccupantsByCell[cell] = occupants;
        }

        occupants.Add(building);
        RefreshCell(cell);
    }

    public static void Unregister(Mod_Building building)
    {
        if (building != null) PassableBuildings.Remove(building);
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

    /// <summary>占地发生变化时通知导航并推进只读快照版本。</summary>
    private static void RefreshCell(Vector2Int cell)
    {
        Revision++;
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        CellChanged?.Invoke(cell);
        if (ChunkMgr.Instance == null)
            return;

        Vector2 center = new(cell.x + 0.5f, cell.y + 0.5f);
        var address = ChunkMgr.Instance.ResolveWorldAddress(center);
        if (!ChunkMgr.Instance.TryGetChunkRuntime(address, out var chunk) || chunk.Terrain == null)
            return;
        WorldNavigationManager.Instance?.QueueNavigationCell(cell);
    }
}
