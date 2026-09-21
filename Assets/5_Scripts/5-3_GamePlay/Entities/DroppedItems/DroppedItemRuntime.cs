using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>主线程只桥接资源、地形、库存和存档；位置、数量及运动状态保存在独立 ECS World。</summary>
internal sealed partial class DroppedItemRuntime : IDisposable
{
    private const float SpatialCellSize = 2f;
    private readonly DroppedItemSimulation simulation = new();
    private readonly Dictionary<int, ItemData> payloads = new();
    private readonly Dictionary<int, DroppedItemVisual> visuals = new();
    private readonly Dictionary<(string, Sprite), DroppedItemVisual> visualCache = new();
    private readonly Dictionary<Vector2Int, HashSet<int>> spatial = new();
    private readonly Dictionary<int, Vector2Int> spatialOwners = new();
    private readonly List<DroppedChange> changes = new();
    private readonly List<DroppedItemSaveRecord> unresolved = new();
    private readonly DroppedItemPresentation presentation;
    private WorldTopologyDomain domain;
    public int Count => simulation.Count;
    public bool IsCreated => simulation.IsCreated;
    public int VisibleBatchCount => presentation.VisibleBatchCount;
    public bool Contains(int id) => simulation.Contains(id);

    public DroppedItemRuntime(Scene scene, List<DroppedItemSaveRecord> records)
    {
        try
        {
            domain = WorldTopologyRuntime.GetActiveDomain();
            presentation = new DroppedItemPresentation(scene, simulation, visuals);
            if (records != null)
                foreach (DroppedItemSaveRecord record in records) Restore(record);
        }
        catch
        {
            presentation?.Dispose(); simulation.Dispose();
            throw;
        }
    }

    #region 冷载荷与空间登记

    private DroppedItemVisual ResolveVisual(ItemData data)
    {
        Sprite sprite = DroppedItemVisual.ResolveSprite(data);
        var key = (data.IDName, sprite);
        if (!visualCache.TryGetValue(key, out DroppedItemVisual visual))
            visualCache.Add(key, visual = DroppedItemVisual.Resolve(data, sprite));
        return visual;
    }

    public void Add(ItemData data, DroppedBody body, DroppedFlight? flight = null, DroppedWaterTransition? water = null)
    {
        // 先验证共享视觉，失败时还没有创建实体，调用者可以安全保留库存。
        DroppedItemVisual visual = ResolveVisual(data);
        simulation.Create(body, flight, water);
        try
        {
            payloads.Add(body.Id, data); visuals.Add(body.Id, visual);
            UpdatePlacement(body.Id);
            if (body.WaterKind != 0) wetItems.Add(body.Id);
            if (!flight.HasValue) QueueEnvironment(body.Id);
        }
        catch { Remove(body.Id); throw; }
    }

    public void Remove(int id)
    {
        if (!simulation.Contains(id)) return;
        presentation.Remove(id);
        if (spatialOwners.TryGetValue(id, out Vector2Int cell))
        {
            HashSet<int> bucket = spatial[cell]; bucket.Remove(id);
            if (bucket.Count == 0) spatial.Remove(cell);
            spatialOwners.Remove(id);
        }
        DetachTerrain(id);
        wetItems.Remove(id); pendingEnvironmentSet.Remove(id);
        visuals.Remove(id); payloads.Remove(id); simulation.Remove(id);
    }

    private static Vector2Int SpatialCell(Vector2 position) => new(
        Mathf.FloorToInt(position.x / SpatialCellSize), Mathf.FloorToInt(position.y / SpatialCellSize));

    private void UpdatePlacement(int id)
    {
        DroppedBody body = simulation.Get(id);
        Vector2Int cell = SpatialCell(body.Position);
        if (!spatialOwners.TryGetValue(id, out Vector2Int previous) || previous != cell)
        {
            if (spatialOwners.ContainsKey(id))
            {
                HashSet<int> old = spatial[previous]; old.Remove(id);
                if (old.Count == 0) spatial.Remove(previous);
            }
            if (!spatial.TryGetValue(cell, out HashSet<int> bucket)) spatial.Add(cell, bucket = new HashSet<int>());
            bucket.Add(id); spatialOwners[id] = cell;
        }
        presentation.Changed(id);
    }

    #endregion

    #region 驱动与保存

    public void Tick(float deltaTime, IEnumerable<ItemPicker> pickers)
    {
        if (deltaTime <= 0f) return;
        domain = WorldTopologyRuntime.GetActiveDomain();
        simulation.Step(deltaTime, domain, changes);
        foreach (DroppedChange change in changes)
        {
            if (!simulation.Contains(change.Id)) continue;
            if (change.Kind == 3) { Remove(change.Id); continue; }
            if (change.Kind == 1) CheckEnvironment(change.Id);
            if (simulation.Contains(change.Id)) UpdatePlacement(change.Id);
        }
        TickWater(deltaTime);
        TickPickup(deltaTime, pickers);
    }

    public void Present(Camera camera) => presentation.Present(camera, domain);

    public List<DroppedItemSaveRecord> Capture()
    {
        List<DroppedItemSaveRecord> records = new(simulation.Count + unresolved.Count);
        foreach (int id in simulation.Ids) records.Add(DroppedItemSaveRecord.Capture(payloads[id], simulation, id));
        records.AddRange(unresolved);
        records.Sort((left, right) => (left?.Data?.Guid ?? 0).CompareTo(right?.Data?.Guid ?? 0));
        return records;
    }

    private void Restore(DroppedItemSaveRecord record)
    {
        try
        {
            if (record?.Data?.Stack == null || record.Data.Stack.Amount <= 0f)
                throw new InvalidOperationException("掉落物快照缺少有效载荷。");
            ItemData data = ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance,
                FastCloner.FastCloner.DeepClone(record.Data));
            DroppedItemService.RemoveLegacyDropData(data);
            data.inHand = false; data.Stack.CanBePickedUp = true;
            DroppedBody body = new()
            {
                Id = data.Guid, Amount = data.Stack.Amount, Position = domain.Normalize(record.Position),
                Scale = record.Scale, Rotation = record.Rotation, VisualHeight = record.VisualHeight,
                LiquidDepth = record.LiquidDepth, WaterKind = record.WaterKind, Pickable = 0,
                SubmergedProgress = record.WaterKind == 2 && record.HasWaterTransition
                    ? Mathf.Clamp01((record.WaterElapsed - record.WaterDuration) / WorldItemWaterRules.SubmergedRecedeDuration) : 0f
            };
            DroppedFlight? flight = record.HasFlight ? new DroppedFlight
            {
                Start = record.FlightStart, End = record.FlightEnd, Control = record.FlightControl,
                Duration = record.FlightDuration, Elapsed = record.FlightElapsed,
                ArcHeight = record.ArcHeight, RotationSpeed = record.RotationSpeed
            } : null;
            DroppedWaterTransition? water = record.HasWaterTransition ? new DroppedWaterTransition
            {
                StartDepth = record.WaterStart, TargetDepth = record.WaterTarget,
                Duration = record.WaterDuration, Elapsed = record.WaterElapsed,
                RecedeDuration = record.WaterKind == 2 ? WorldItemWaterRules.SubmergedRecedeDuration : 0f
            } : null;
            Add(data, body, flight, water);
        }
        catch (Exception exception)
        {
            // MOD 资源暂缺时保留原记录，下次保存不会把无法显示的库存载荷悄悄删除。
            unresolved.Add(record);
            Debug.LogWarning($"[DroppedItems] 无法恢复掉落物，原快照已保留：{record?.Data?.IDName}，{exception.Message}");
        }
    }

    public void Dispose()
    {
        ClearTerrainSubscriptions();
        presentation.Dispose(); simulation.Dispose();
        payloads.Clear(); visuals.Clear(); visualCache.Clear(); spatial.Clear(); spatialOwners.Clear();
        pickupAttempts.Clear();
    }

    #endregion
}
