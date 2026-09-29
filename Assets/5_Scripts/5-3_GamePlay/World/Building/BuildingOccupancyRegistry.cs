// 动态建筑放置占用层：以离散世界格记录建筑位置，不依赖任何 Physics2D Collider。
// 该层同时服务放置冲突判断与导航脏格更新；建筑实体的 Collider 只负责交互/受击等运行时物理。
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Unity.Mathematics;
using UnityEngine;

public static class BuildingOccupancyRegistry
{
    public static uint Revision { get; private set; } // LOS 快照可独立于导航值变化失效。
    public static uint SightBlockingRebuildRevision { get; private set; }
    public static event System.Action<Vector2Int> CellChanged; // 少量地图快照订阅者，禁止逐 AI 订阅。
    private static readonly Dictionary<Vector2Int, HashSet<Mod_Building>> OccupantsByCell = new();
    private static readonly Dictionary<Mod_Building, HashSet<Vector2Int>> CellsByBuilding = new();
    private static readonly HashSet<Vector2Int> SightBlockedCells = new();
    // 开门只撤销导航阻挡，不能撤销建筑放置占用，否则其它建筑可以重叠放进门里。
    private static readonly HashSet<Mod_Building> PassableBuildings = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        OccupantsByCell.Clear();
        CellsByBuilding.Clear();
        SightBlockedCells.Clear();
        PassableBuildings.Clear();
        Revision++;
        SightBlockingRebuildRevision++;
        CellChanged = null;
    }

    #region 占地查询

    public static bool IsOccupied(Vector2Int cell, Mod_Building except = null, int layer = -1)
        => IsOccupiedNormalized(WorldTopologyRuntime.NormalizeCell(cell), except, layer);

    /// <summary>批量读取已按当前世界归一化的格子，避免每格重新查询存档与场景。</summary>
    public static bool IsOccupiedNormalized(Vector2Int cell, Mod_Building except = null, int layer = -1)
    {
        bool mechanicalOccupied = layer < 0
            ? MachineWorld.IsOccupiedOnPlacementLayersNormalized(cell)
            : MachineWorld.IsOccupiedNormalized(cell, layer);
        if (!OccupantsByCell.TryGetValue(cell, out HashSet<Mod_Building> occupants))
            return mechanicalOccupied;

        occupants.RemoveWhere(building => building == null || !building.isActiveAndEnabled || !building.IsInstalled());
        foreach (Mod_Building building in occupants)
        {
            if (building != except && (layer < 0 || GetPlacementLayer(building) == layer))
                return true;
        }

        if (occupants.Count == 0)
            OccupantsByCell.Remove(cell);
        return mechanicalOccupied;
    }

    /// <summary>检查指定放置格是否空闲；这是动态建筑放置冲突的权威入口。</summary>
    public static bool CanPlace(Vector2Int cell, Mod_Building except, out string reason)
    {
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        int layer = except == null ? 0 : GetPlacementLayer(except);
        if (!IsOccupiedNormalized(cell, except, layer))
        {
            reason = null;
            return true;
        }

        reason = $"地块 ({cell.x},{cell.y}) 已被建筑占用";
        return false;
    }

    #endregion

    #region 视线遮挡层

    /// <summary>新区块就绪时从占地索引一次性填入动态遮挡位。</summary>
    public static void InitializeSightBlockingForChunk(ChunkRuntime chunk)
    {
        ChunkTerrainData terrain = chunk?.Terrain;
        if (chunk == null || chunk.DataStatus != ChunkDataStatus.Ready ||
            terrain == null || terrain.IsDisposed)
            return;

        WorldTopologyDomain topology = WorldTopologyRuntime.GetActiveDomain();
        int originX = chunk.Address.ChunkOrigin.X;
        int originY = chunk.Address.ChunkOrigin.Y;
        for (int y = 0; y < terrain.Height; y++)
        for (int x = 0; x < terrain.Width; x++)
        {
            int2 worldCell = new int2(originX + x, originY + y);
            if (!topology.Contains(worldCell))
                worldCell = topology.Normalize(worldCell);
            terrain.SetDynamicSightBlockingCell(x, y,
                SightBlockedCells.Contains(new Vector2Int(worldCell.x, worldCell.y)));
        }
    }

    /// <summary>机械世界切换后从现有建筑和机械节点重建一次动态遮挡索引。</summary>
    public static void RebuildSightBlockingIndex(IEnumerable<MachineEntity> mechanicalNodes,
        WorldTopologyDomain mechanicalTopology)
    {
        SightBlockedCells.Clear();
        foreach (KeyValuePair<Vector2Int, HashSet<Mod_Building>> entry in OccupantsByCell)
        {
            foreach (Mod_Building building in entry.Value)
            {
                if (building == null || !building.isActiveAndEnabled || !building.IsInstalled())
                    continue;
                SightBlockedCells.Add(entry.Key);
                break;
            }
        }

        if (mechanicalNodes != null)
        {
            foreach (MachineEntity node in mechanicalNodes)
            {
                if (node?.Definition == null ||
                    (node.Definition.Layer != 0 && node.Definition.Layer != 1))
                    continue;
                int2 cell = mechanicalTopology.Normalize(new int2(node.Cell.x, node.Cell.y));
                SightBlockedCells.Add(new Vector2Int(cell.x, cell.y));
            }
        }

        Revision++;
        SightBlockingRebuildRevision++;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager == null)
            return;
        foreach (KeyValuePair<FlatWorld.WorldModel.WorldAddress, ChunkRuntime> pair in manager.Chunks)
            InitializeSightBlockingForChunk(pair.Value);
    }

    #endregion

    public static bool GetEffectiveWalkable(Vector2Int cell, bool terrainWalkable)
    {
        if (!terrainWalkable) return false;
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        MachineEntity mechanical = MachineWorld.GetAtCurrentWorld(cell, 0);
        if (mechanical?.Definition.BlocksMovement == true) return false;
        if (OccupantsByCell.TryGetValue(cell, out HashSet<Mod_Building> occupants))
            foreach (Mod_Building building in occupants)
                if (building != null && building.isActiveAndEnabled && building.IsInstalled() &&
                    GetPlacementLayer(building) == 0 && !PassableBuildings.Contains(building) &&
                    IsMovementBlocking(building))
                    return false;
        return true;
    }

    /// <summary>读取玩家当前格的建筑移速倍率；同格多来源取最强惩罚，避免多层机械重复叠乘。</summary>
    public static float GetPlayerMoveSpeedMultiplier(Vector2 position)
    {
        Vector2Int cell = WorldTopologyRuntime.NormalizeCell(
            new Vector2Int(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y)));
        float multiplier = MachineWorld.GetAtCurrentWorld(cell, 0)?.Definition.PlayerMoveSpeedMultiplier ?? 1f;
        if (!OccupantsByCell.TryGetValue(cell, out HashSet<Mod_Building> occupants))
            return multiplier;
        occupants.RemoveWhere(building => building == null || !building.isActiveAndEnabled || !building.IsInstalled());
        foreach (Mod_Building building in occupants)
        {
            IBuildingTraversalPolicy policy = BuildingPlacementLifecycle.GetTraversalPolicy(building.item);
            if (policy == null)
                continue;

            multiplier = Mathf.Min(multiplier, Mathf.Clamp(policy.PlayerMoveSpeedMultiplier, 0.01f, 1f));
        }

        if (occupants.Count == 0)
            OccupantsByCell.Remove(cell);
        return multiplier;
    }

    /// <summary>纯数据机械占地变化沿用建筑格的导航与视线失效通知。</summary>
    public static void NotifyMechanicalChanged(Vector2Int cell) => RefreshCell(WorldTopologyRuntime.NormalizeCell(cell));

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

    /// <summary>未声明通行策略的建筑继续按实体障碍处理。</summary>
    private static bool IsMovementBlocking(Mod_Building building)
        => BuildingPlacementLifecycle.GetTraversalPolicy(building?.item)?.BlocksMovement ?? true;

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
        cell = WorldTopologyRuntime.NormalizeCell(cell);
        bool sightBlocked = IsOccupiedNormalized(cell);
        if (sightBlocked)
            SightBlockedCells.Add(cell);
        else
            SightBlockedCells.Remove(cell);

        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (manager != null && manager.WorldRuntime != null && !manager.IsWorldRuntimeShuttingDown)
        {
            Vector2 center = new(cell.x + 0.5f, cell.y + 0.5f);
            var address = manager.ResolveWorldAddress(center);
            if (manager.TryGetChunkRuntime(address, out ChunkRuntime chunk) &&
                chunk.DataStatus == ChunkDataStatus.Ready && chunk.Terrain != null &&
                !chunk.Terrain.IsDisposed)
            {
                int x = cell.x - address.ChunkOrigin.X;
                int y = cell.y - address.ChunkOrigin.Y;
                if ((uint)x < (uint)chunk.Terrain.Width && (uint)y < (uint)chunk.Terrain.Height)
                {
                    chunk.Terrain.SetDynamicSightBlockingCell(x, y, sightBlocked);
                    WorldNavigationManager.Instance?.QueueNavigationCell(cell);
                }
            }
        }

        Revision++;
        CellChanged?.Invoke(cell);
    }
}
