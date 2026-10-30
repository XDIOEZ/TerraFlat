using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Profiling;

/// <summary>主线程统一驱动轻量掉落物；没有 Entity、逐物品 Update、Rigidbody2D 或完整 Item 模块。</summary>
internal sealed partial class DroppedItemRuntime : IDisposable
{
    private const float SpatialCellSize = 2f;
    private readonly LightweightDroppedItemSimulation simulation = new();
    private readonly Dictionary<int, ItemData> payloads = new();
    private readonly Dictionary<int, DroppedItemPayloadCaptureCache> savePayloads = new();
    private readonly Dictionary<int, DroppedItemVisual> visuals = new();
    private readonly Dictionary<(string, Sprite), DroppedItemVisual> visualCache = new();
    private readonly Dictionary<Vector2Int, HashSet<int>> spatial = new();
    private readonly Dictionary<int, Vector2Int> spatialOwners = new();
    private readonly List<LightweightDroppedChange> changes = new();
    private readonly List<DroppedItemSaveRecord> unresolved = new();
    private DroppedItemPresentation presentation;
    private Scene presentationScene;
    private WorldTopologyDomain domain;
    private readonly string matterWorldKey;
    public int Count => simulation.Count;
    public bool IsCreated => simulation.IsCreated;
    public int VisibleViewCount => presentation?.VisibleViewCount ?? 0;
    public bool Contains(int id) => simulation.Contains(id);

    public DroppedItemRuntime(string worldKey, List<DroppedItemSaveRecord> records)
    {
        if (string.IsNullOrWhiteSpace(worldKey)) throw new ArgumentException("掉落物世界身份为空。", nameof(worldKey));
        matterWorldKey = worldKey;
        try
        {
            domain = WorldTopologyRuntime.GetActiveDomain();
            if (records != null)
                foreach (DroppedItemSaveRecord record in records) Restore(record);
            MachineWorld.WorldPresentationCellChanged += OnTransportCellChanged;
            MachineWorld.WorldPresentationCellsChanged += OnTransportCellsChanged;
        }
        catch
        {
            presentation?.Dispose(); simulation.Dispose();
            throw;
        }
    }

    public DroppedItemRuntime(Scene scene, List<DroppedItemSaveRecord> records)
        : this(ResolveSceneWorldKey(scene), records)
    {
        try { BindScenePresentation(scene); }
        catch { Dispose(); throw; }
    }

    private static string ResolveSceneWorldKey(Scene scene)
    {
        if (!GameplayWorldRegistry.TryResolveWorldKeyForScene(scene, out string worldKey))
            throw new InvalidOperationException("掉落物场景没有明确的世界归属。");
        return worldKey;
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

    public void Add(ItemData data, LightweightDroppedBody body, LightweightDroppedFlight? flight = null,
        LightweightDroppedWaterTransition? water = null)
    {
        // 先验证共享视觉，失败时还没有创建实体，调用者可以安全保留库存。
        DroppedItemVisual visual = ResolveVisual(data);
        simulation.Create(body, flight, water);
        try
        {
            payloads.Add(body.Id, data); visuals.Add(body.Id, visual);
            // 出生即建立寿命起点；过期读档载荷由下一轮统一移除，不改变生成事务。
            TimedDespawnRules.IsWorldItemExpired(data, matterWorldKey);
            lastMatterTimes.Add(body.Id, matterTime);
            UpdatePlacement(body.Id);
            if (body.WaterKind != 0 && !body.IsOnBottom) wetItems.Add(body.Id);
            if (!flight.HasValue) QueueEnvironment(body.Id);
        }
        catch { Remove(body.Id); throw; }
        DroppedItemService.NotifyWorldItemsChanged(matterWorldKey);
    }

    public void Remove(int id)
    {
        if (!simulation.Contains(id)) return;
        presentation?.Remove(id);
        if (spatialOwners.TryGetValue(id, out Vector2Int cell))
        {
            HashSet<int> bucket = spatial[cell]; bucket.Remove(id);
            if (bucket.Count == 0) spatial.Remove(cell);
            spatialOwners.Remove(id);
        }
        DetachTerrain(id);
        transportItems.Remove(id);
        wetItems.Remove(id); pendingEnvironmentSet.Remove(id);
        lastMatterTimes.Remove(id);
        visuals.Remove(id); payloads.Remove(id); savePayloads.Remove(id); simulation.Remove(id);
        DroppedItemService.NotifyWorldItemsChanged(matterWorldKey);
    }

    private static Vector2Int SpatialCell(Vector2 position) => new(
        Mathf.FloorToInt(position.x / SpatialCellSize), Mathf.FloorToInt(position.y / SpatialCellSize));

    private void UpdatePlacement(int id)
    {
        LightweightDroppedBody body = simulation.Get(id);
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
        presentation?.Changed(id);
        if (MachineWorld.GetTransportAt(body.Position) != null) transportItems.Add(id);
        else transportItems.Remove(id);
    }

    #endregion

    #region 驱动与保存

    // 集中驱动也分阶段记账，区分运动、入水、输送、物质与拾取的实际开销。
    private static readonly ProfilerMarker MotionTickMarker = new("DroppedItems.Tick.Motion");
    private static readonly ProfilerMarker WaterTickMarker = new("DroppedItems.Tick.Water");
    private static readonly ProfilerMarker WindTickMarker = new("DroppedItems.Tick.Wind");
    private static readonly ProfilerMarker TransportTickMarker = new("DroppedItems.Tick.Transport");
    private static readonly ProfilerMarker MatterTickMarker = new("DroppedItems.Tick.Matter");
    private static readonly ProfilerMarker PickupTickMarker = new("DroppedItems.Tick.Pickup");

    public void Tick(float deltaTime, IEnumerable<Mod_ItemPicker> pickers)
    {
        if (deltaTime <= 0f) return;
        environmentMotionOwners.Clear();
        domain = WorldTopologyRuntime.GetActiveDomain();
        using var terrainSampling = new TerrainSamplingScope(this);
        using (MotionTickMarker.Auto())
        {
            simulation.Step(deltaTime, domain, changes);
            foreach (LightweightDroppedChange change in changes)
            {
                if (!simulation.Contains(change.Id)) continue;
                if (change.Kind == 3) wetItems.Remove(change.Id);
                if (change.Kind == 1) CheckEnvironment(change.Id);
                if (simulation.Contains(change.Id)) UpdatePlacement(change.Id);
            }
        }
        using (WaterTickMarker.Auto()) TickWater(deltaTime);
        using (WindTickMarker.Auto()) TickWind(deltaTime);
        using (TransportTickMarker.Auto()) TickTransport(deltaTime);
        using (MatterTickMarker.Auto()) TickMatter(deltaTime);
        using (PickupTickMarker.Auto()) TickPickup(deltaTime, pickers);
    }

    // 视图只绑定有效的世界场景，重绑不改变掉落物模拟与句柄。
    public void BindScenePresentation(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) { DetachScenePresentation(); return; }
        if (presentation != null && presentationScene == scene) return;
        DetachScenePresentation();
        presentation = new DroppedItemPresentation(scene, matterWorldKey, simulation, visuals, payloads, transportItems);
        presentationScene = scene;
    }

    public void DetachScenePresentation(Scene scene)
    {
        if (presentation != null && presentationScene == scene) DetachScenePresentation();
    }

    private void DetachScenePresentation()
    {
        DroppedItemPresentation previous = presentation;
        presentation = null;
        presentationScene = default;
        previous?.Dispose();
    }

    public void Present(Camera camera, Matrix4x4? worldToDisplay = null, WorldPresentationSortingDomain sortingDomain = null)
    {
        if (presentation != null && (!presentationScene.IsValid() || !presentationScene.isLoaded))
            DetachScenePresentation();
        presentation?.SetSortingDomain(sortingDomain);
        presentation?.Present(camera, domain, spatial, SpatialCellSize, worldToDisplay ?? Matrix4x4.identity);
    }

    public List<DroppedItemSaveRecord> Capture(bool includeUnresolved = true)
    {
        List<DroppedItemSaveRecord> records = new(simulation.Count + unresolved.Count);
        foreach (int id in simulation.Ids)
        {
            if (!savePayloads.TryGetValue(id, out DroppedItemPayloadCaptureCache cache))
                savePayloads.Add(id, cache = new DroppedItemPayloadCaptureCache());
            records.Add(DroppedItemSaveRecord.Capture(payloads[id], simulation, id, cache));
        }
        if (includeUnresolved) records.AddRange(unresolved);
        records.Sort((left, right) => (left?.Guid ?? 0).CompareTo(right?.Guid ?? 0));
        return records;
    }

    private void Restore(DroppedItemSaveRecord record)
    {
        if (DeferRestoringPendingSurfaceMotion(record)) return;
        try
        {
            if (record?.Data?.Stack == null || record.Data.Stack.Amount <= 0f)
                throw new InvalidOperationException("掉落物快照缺少有效载荷。");
            ItemData data = ItemDefinitionRuntime.RebasePersistedData(GameRes.ExistingInstance,
                FastCloner.FastCloner.DeepClone(record.Data));
            DroppedItemService.RemoveLegacyDropData(data);
            data.inHand = false; data.Stack.CanBePickedUp = true;
            LightweightDroppedBody body = record.RestoreBody(data, domain);
            LightweightDroppedFlight? flight = record.HasFlight ? new LightweightDroppedFlight
            {
                Start = record.FlightStart, End = record.FlightEnd, Control = record.FlightControl,
                Duration = record.FlightDuration, Elapsed = record.FlightElapsed,
                ArcHeight = record.ArcHeight, RotationSpeed = record.RotationSpeed
            } : null;
            LightweightDroppedWaterTransition? water = record.HasWaterTransition ? new LightweightDroppedWaterTransition
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
            Debug.LogWarning($"[DroppedItems] 无法恢复掉落物，原快照已保留：{record?.Guid}，{exception.Message}");
        }
    }

    public void Dispose()
    {
        MachineWorld.WorldPresentationCellChanged -= OnTransportCellChanged;
        MachineWorld.WorldPresentationCellsChanged -= OnTransportCellsChanged;
        transportItems.Clear(); transportScratch.Clear();
        environmentMotionOwners.Clear();
        ClearTerrainSubscriptions();
        DetachScenePresentation(); simulation.Dispose();
        payloads.Clear(); savePayloads.Clear(); visuals.Clear(); visualCache.Clear(); spatial.Clear(); spatialOwners.Clear();
        pickupAttempts.Clear();
        pickupAttemptCapacityRevisions.Clear();
        matterScratch.Clear(); lastMatterTimes.Clear();
    }

    #endregion

    #region 输送带掉落物
    private readonly HashSet<int> transportItems = new();
    private readonly List<int> transportScratch = new();
    private readonly HashSet<int> environmentMotionOwners = new();

    // 环境来源只交付目标，位置、空间索引、水态和表现统一在这里提交。
    private bool CommitEnvironmentMotion(int id, Vector2 position, float interpolationSeconds = 0f)
    {
        if (!simulation.Contains(id)) return false;
        LightweightDroppedBody body = simulation.Get(id);
        Vector2 movement = domain.ShortestDelta(body.Position, position);
        if (movement.sqrMagnitude <= .00000001f) return false;
        if (interpolationSeconds > 0f) presentation?.RecordWaterDrift(id, movement, interpolationSeconds);
        body.Position = position;
        simulation.Set(body);
        UpdatePlacement(id);
        CheckEnvironment(id);
        return true;
    }

    /// <summary>放置、拆除或转速改变时只检查这个格附近的空间桶，静止地面物不逐帧查带。</summary>
    private void OnTransportCellChanged(string worldKey, Vector2Int cell)
    {
        if (worldKey != matterWorldKey) return;
        using var scope = GameplayWorldRegistry.UseExistingWorldOrMachineScope(worldKey);
        RefreshTransportCell(cell);
    }

    private void OnTransportCellsChanged(string worldKey, IReadOnlyList<Vector2Int> cells)
    {
        if (worldKey != matterWorldKey) return;
        using var scope = GameplayWorldRegistry.UseExistingWorldOrMachineScope(worldKey);
        foreach (Vector2Int cell in cells) RefreshTransportCell(cell);
    }

    private void RefreshTransportCell(Vector2Int cell)
    {
        Vector2 center = (Vector2)cell + Vector2.one * .5f;
        if (!spatial.TryGetValue(SpatialCell(center), out HashSet<int> bucket)) return;
        foreach (int id in bucket)
        {
            LightweightDroppedBody body = simulation.Get(id);
            Vector2Int bodyCell = new(Mathf.FloorToInt(body.Position.x), Mathf.FloorToInt(body.Position.y));
            if (bodyCell != cell) continue;
            if (MachineWorld.GetTransportAt(body.Position) != null) transportItems.Add(id);
            else transportItems.Remove(id);
        }
    }

    private void TickTransport(float seconds)
    {
        transportScratch.Clear(); transportScratch.AddRange(transportItems);
        foreach (int id in transportScratch)
        {
            if (!simulation.Contains(id)) { transportItems.Remove(id); continue; }
            LightweightDroppedBody body = simulation.Get(id);
            if (body.Pickable == 0 || body.WaterKind != 0 || simulation.TryGetFlight(id, out _) ||
                !environmentMotionOwners.Add(id)) continue;
            MachineWorld.TryReceiveConveyorDrop(GetConveyorOutput(id), body.Position, seconds, out bool terminal);
            if (!simulation.Contains(id)) continue;
            Vector2 position = terminal
                ? LooseItemMotion.AdvanceExternal(matterWorldKey, payloads[id], body.Position, seconds)
                : MachineWorld.TransportGroundItem(body.Position, seconds, payloads[id]);
            CommitEnvironmentMotion(id, position);
        }
    }
    #endregion

    #region 风推动掉落物

    private readonly List<int> windScratch = new();
    private static readonly ProfilerMarker WindPrepareMarker = new("DroppedItems.Wind.Prepare");
    private static readonly ProfilerMarker WindSampleMarker = new("DroppedItems.Wind.Sample");
    private static readonly ProfilerMarker WindResolveMarker = new("DroppedItems.Wind.ResolvePosition");
    private static readonly ProfilerMarker WindCommitMarker = new("DroppedItems.Wind.Commit");

    private void TickWind(float seconds)
    {
        WorldMotionSystem.WindSamplingContext context = default;
        bool hasWind;
        using (WindPrepareMarker.Auto())
        {
            hasWind = WorldMotionSystem.HasWindPush && WorldMotionSystem.TryCreateWindSamplingContext(out context);
            if (!hasWind && !LooseItemMotion.HasVelocitySources) return;
            windScratch.Clear();
            windScratch.AddRange(simulation.Ids);
        }
        foreach (int id in windScratch)
        {
            if (!simulation.Contains(id) || simulation.TryGetFlight(id, out _)) continue;
            LightweightDroppedBody body = simulation.Get(id);
            // 水流和带面在各自节奏内已经合成外力，同一物品不能再额外走一遍风推动。
            if (body.Pickable == 0 || body.LiquidDepth >= 0.995f || body.WaterKind != 0 ||
                transportItems.Contains(id) || !environmentMotionOwners.Add(id)) continue;
            Vector2 velocity;
            using (WindSampleMarker.Auto())
                velocity = hasWind ? context.Sample(body.Position, payloads[id].Stack.CurrentWeight) : Vector2.zero;
            if (velocity.sqrMagnitude <= 0f && !LooseItemMotion.HasVelocitySources) continue;
            Vector2 destination;
            using (WindResolveMarker.Auto())
                destination = LooseItemMotion.AdvanceExternal(matterWorldKey, payloads[id], body.Position, seconds, velocity);
            using (WindCommitMarker.Auto())
                CommitEnvironmentMotion(id, destination);
        }
        windScratch.Clear();
    }

    #endregion
}
