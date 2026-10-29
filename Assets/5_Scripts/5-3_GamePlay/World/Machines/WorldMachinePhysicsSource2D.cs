using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.Pool;

// 机器只提交当前阻挡数据，合并结果和原生组件统一交给世界物理服务。
internal sealed class WorldMachinePhysicsSource2D : IDisposable
{
    #region 机器数据接入
    private sealed class Entry
    {
        internal object GroupKey;
        internal WorldPhysicsFixedRectangleSource2D Group;
    }
    private readonly WorldPhysicsService2D physics;
    private readonly Dictionary<int, Entry> entries = new();
    private readonly Dictionary<int, Vector2Int> anchors = new();
    private readonly Dictionary<object, WorldPhysicsFixedRectangleSource2D> groups = new();
    private readonly HashSet<Vector2Int> dirtyCells = new();
    private readonly HashSet<int> liveIds = new();
    private readonly List<int> staleIds = new();
    private readonly List<MachineEntity> nodeScratch = new();
    private readonly List<MachineWorld.MachineRenderCell> cellScratch = new();
    private readonly HashSet<WorldPhysicsFixedRectangleSource2D> changedGroups = new();
    private readonly Action synchronize;
    private IDisposable simulationChanged, terrainCommitted;
    private WorldRuntime model;
    private bool fullRefresh = true, disposed;

    internal WorldMachinePhysicsSource2D(WorldPhysicsService2D owner)
    {
        physics = owner;
        synchronize = Synchronize;
        physics.Disposing += Dispose;
        MachineWorld.WorldPresentationCellChanged += OnCell;
        MachineWorld.WorldPresentationCellsChanged += OnCells;
        MachineWorld.PhysicsSourcesReset += OnReset;
        physics.ScheduleGeometryUpdate(synchronize);
    }

    internal void Bind(WorldRuntime world)
    {
        if (ReferenceEquals(model, world)) return;
        simulationChanged?.Dispose(); terrainCommitted?.Dispose();
        model = world;
        simulationChanged = world?.Events.Subscribe<ChunkSimulationStatusChanged>(_ => OnReset(physics.WorldKey));
        terrainCommitted = world?.Events.Subscribe<ChunkCommitted>(_ => OnReset(physics.WorldKey));
        OnReset(physics.WorldKey);
    }

    internal void Unbind(WorldRuntime expected)
    {
        if (!ReferenceEquals(model, expected)) return;
        Bind(null);
        foreach (var group in groups.Values) group.Dispose();
        groups.Clear(); entries.Clear(); anchors.Clear(); changedGroups.Clear(); dirtyCells.Clear();
    }

    private void OnReset(string key)
    {
        if (disposed || key != physics.WorldKey) return;
        fullRefresh = true;
        physics.ScheduleGeometryUpdate(synchronize);
    }

    private void OnCell(string key, Vector2Int cell)
    {
        if (disposed || key != physics.WorldKey) return;
        dirtyCells.Add(cell);
        physics.ScheduleGeometryUpdate(synchronize);
    }
    private void OnCells(string key, IReadOnlyList<Vector2Int> cells)
    { if (cells != null) foreach (Vector2Int cell in cells) OnCell(key, cell); }

    private bool IsSimulated(MachineEntity node, GameplayWorldRuntime world)
    {
        if (world.Chunks == null || model == null || !ReferenceEquals(world.Chunks.WorldRuntime, model)) return false;
        var address = world.Chunks.ResolveWorldAddress(node.Cell);
        return model.TryGetChunk(address, out var chunk) && chunk.DataStatus == ChunkDataStatus.Ready &&
            chunk.SimulationStatus == ChunkSimulationStatus.Active;
    }

    private void Synchronize()
    {
        if (disposed || !physics.IsValid) return;
        if (!GameplayWorldRegistry.TryGet(physics.WorldKey, out var world)) return;
        using var scope = GameplayWorldRegistry.UseWorld(physics.WorldKey);
        try { Synchronize(world); }
        catch { fullRefresh = true; throw; }
    }

    private void Synchronize(GameplayWorldRuntime world)
    {
        if (fullRefresh)
        {
            fullRefresh = false;
            dirtyCells.Clear();
            MachineWorld.CollectExistingPhysicsNodes(physics.WorldKey, nodeScratch);
            liveIds.Clear();
            foreach (MachineEntity node in nodeScratch)
            { liveIds.Add(node.Id); SynchronizeNode(node, world); }
            staleIds.Clear();
            foreach (int id in entries.Keys) if (!liveIds.Contains(id)) staleIds.Add(id);
            foreach (int id in staleIds) Remove(id);
            nodeScratch.Clear();
        }
        else
        {
            using var scratch = ListPool<Vector2Int>.Get(out var cells);
            cells.AddRange(dirtyCells); dirtyCells.Clear();
            foreach (Vector2Int cell in cells)
            {
                liveIds.Clear();
                MachineWorld.CollectInBounds(new BoundsInt(cell.x, cell.y, 0, 1, 1, 1), cellScratch);
                foreach (var candidate in cellScratch)
                    if (liveIds.Add(candidate.Node.Id)) SynchronizeNode(candidate.Node, world);
                CollectStaleMachineIds(anchors, cell, liveIds, staleIds);
                foreach (int id in staleIds) Remove(id);
            }
            cellScratch.Clear();
        }
        foreach (var group in changedGroups) if (group.IsValid) group.Synchronize();
        changedGroups.Clear();
    }

    private void SynchronizeNode(MachineEntity node, GameplayWorldRuntime world)
    {
        if (node.Definition?.BlocksTraversal != true || !IsSimulated(node, world) || HasPhysicalItemCollider(node))
        { Remove(node.Id); return; }
        MachineWorld.ResolveRequiredWorldFixedMachineProxyMode(node);
        MachineCollisionBounds.ResolveWorldBox(node, out Vector2 center, out Vector2 extents);
        if (!(extents.x > 0f && extents.y > 0f) || !float.IsFinite(center.x) || !float.IsFinite(center.y) ||
            !float.IsFinite(extents.x) || !float.IsFinite(extents.y)) { Remove(node.Id); return; }
        Vector2Int batch = physics.ResolveFixedBatchAddress(center);
        bool merge = CanMergeAdjacentStorageCollision(node);
        object key = merge ? (node.Definition.Id, batch) : node;
        Vector2 origin = merge ? (Vector2)(batch * physics.FixedBatchCellSize) : center;
        if (entries.TryGetValue(node.Id, out var old) && !Equals(old.GroupKey, key)) { Remove(node.Id); old = null; }
        if (!groups.TryGetValue(key, out var group))
        {
            group = physics.CreateFixedRectangleSource(key, origin);
            groups.Add(key, group);
        }
        group.SetOrigin(origin);
        group.SetRectangle(node.Id, center - origin, extents * 2f);
        if (old == null) entries[node.Id] = new Entry { GroupKey = key, Group = group };
        anchors[node.Id] = node.Cell;
        changedGroups.Add(group);
    }

    private bool HasPhysicalItemCollider(MachineEntity node)
    {
        Item item = node.View != null ? node.View.item : null;
        if (item == null) return false;
        if (physics.TryGetItemProxy(item, out var proxy))
            foreach (Collider2D collider in proxy.Colliders)
                if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy && !collider.isTrigger) return true;
        return false;
    }

    private void Remove(int id)
    {
        if (!entries.Remove(id, out var entry)) return;
        anchors.Remove(id);
        entry.Group.RemoveRectangle(id);
        if (entry.Group.RectangleSourceCount == 0)
        {
            groups.Remove(entry.GroupKey);
            changedGroups.Remove(entry.Group);
            entry.Group.Dispose();
        }
        else changedGroups.Add(entry.Group);
    }

    internal static bool CanMergeAdjacentStorageCollision(MachineEntity node)
        => node?.Definition?.BlocksTraversal == true && node.Definition.Fluid?.CombineAdjacent == true &&
           MachineWorld.IsFluidTankBlock(node) && MachineWorld.CanBatchWorldFixedMachineBlockingRegions(node);

    internal static void CollectStaleMachineIds(Dictionary<int, Vector2Int> anchors, Vector2Int cell,
        HashSet<int> retained, List<int> result)
    {
        result.Clear();
        foreach (var entry in anchors)
            if (entry.Value == cell && !retained.Contains(entry.Key)) result.Add(entry.Key);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        physics.Disposing -= Dispose;
        physics.CancelGeometryUpdate(synchronize);
        MachineWorld.WorldPresentationCellChanged -= OnCell;
        MachineWorld.WorldPresentationCellsChanged -= OnCells;
        MachineWorld.PhysicsSourcesReset -= OnReset;
        simulationChanged?.Dispose(); terrainCommitted?.Dispose();
        foreach (var group in groups.Values) group.Dispose();
        groups.Clear(); entries.Clear(); anchors.Clear(); changedGroups.Clear(); dirtyCells.Clear();
        model = null;
    }
    #endregion
}
