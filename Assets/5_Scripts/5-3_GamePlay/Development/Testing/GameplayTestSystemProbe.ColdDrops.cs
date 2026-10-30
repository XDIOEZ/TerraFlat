using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 后台炉体的正式掉落物查询与转移采样
    private static void RegisterColdDropProbes(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("processing/cold_fuel_candidates", ColdFuelCandidates);
        operations.Add("processing/cold_fuel_transfer", ColdFuelTransfer);
        operations.Add("processing/cold_motion_workspace", ColdMotionWorkspace);
        operations.Add("item_runtime/drop_motion_storage", DroppedMotionStorage);
        operations.Add("item_runtime/drop_visibility_broadphase", DropVisibilityBroadphase);
        operations.Add("surface_fluids/bottom_recovery", BottomRecovery);
    }

    #region 轻量掉落物视野筛选性能与接缝回归

    private static JObject DropVisibilityBroadphase(JObject args)
    {
        const float cellSize = 2f;
        var buckets = new Dictionary<Vector2Int, HashSet<int>>();
        var candidates = new HashSet<int>();
        void Add(int id, float x, float y)
        {
            var cell = new Vector2Int(Mathf.FloorToInt(x / cellSize), Mathf.FloorToInt(y / cellSize));
            if (!buckets.TryGetValue(cell, out HashSet<int> ids))
                buckets.Add(cell, ids = new HashSet<int>());
            ids.Add(id);
        }

        // 稀疏时按现存桶筛选，保留镜头边界；远处掉落物不应进入精确表现阶段。
        Add(1, .5f, .5f);
        Add(2, 4.5f, .5f);
        Add(3, 26f, 26f);
        DroppedItemPresentation.CollectCandidates(Vector2.zero, 5f, 5f, default, buckets, cellSize, candidates);
        bool sparseCorrect = candidates.Contains(1) && candidates.Contains(2) && !candidates.Contains(3);
        for (int i = 0; i < 32; i++)
            DroppedItemPresentation.CollectCandidates(Vector2.zero, 5f, 5f, default, buckets, cellSize, candidates);
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++)
            DroppedItemPresentation.CollectCandidates(Vector2.zero, 5f, 5f, default, buckets, cellSize, candidates);
        long sparseBytes = GC.GetAllocatedBytesForCurrentThread() - start;

        // 密集时按视野格查询，不扫描世界上所有的远处掉落物。
        buckets.Clear();
        Add(11, .5f, .5f);
        for (int i = 0; i < 64; i++) Add(100 + i, 50f + i * 2f, 50f);
        DroppedItemPresentation.CollectCandidates(Vector2.zero, 5f, 5f, default, buckets, cellSize, candidates);
        bool denseCorrect = candidates.Count == 1 && candidates.Contains(11);
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++)
            DroppedItemPresentation.CollectCandidates(Vector2.zero, 5f, 5f, default, buckets, cellSize, candidates);
        long denseBytes = GC.GetAllocatedBytesForCurrentThread() - start;

        // 世界从最右侧看向最左侧时仍能找到对岸掉落物；超大视野也不能丢失镜像。
        var wrapped = new WorldTopologyDomain(new int2(-8, -8), new int2(16, 16), true);
        buckets.Clear();
        Add(21, -7.5f, .5f);
        Add(22, 0f, 0f);
        DroppedItemPresentation.CollectCandidates(new Vector2(7f, 0f), 3f, 3f,
            wrapped, buckets, cellSize, candidates);
        bool seamCorrect = candidates.Contains(21) && !candidates.Contains(22);
        DroppedItemPresentation.CollectCandidates(new Vector2(7f, 0f), 20f, 20f,
            wrapped, buckets, cellSize, candidates);
        bool wideCorrect = candidates.Contains(21) && candidates.Contains(22);

        return new JObject
        {
            ["sparseCorrect"] = sparseCorrect, ["denseCorrect"] = denseCorrect,
            ["seamCorrect"] = seamCorrect, ["wideCorrect"] = wideCorrect,
            ["sparseAllocatedBytes"] = sparseBytes, ["denseAllocatedBytes"] = denseBytes
        };
    }

    #endregion

    private static DroppedItemSaveRecord ColdFuelRecord(int id, float x, float y, float amount = 1f)
    {
        Data_GeneralItem data = EntityProbeData("test:cold-fuel", amount);
        data.Guid = id;
        return new DroppedItemSaveRecord { Data = data, Position = new Vector2(x, y) };
    }

    // 夹具只传独立记录列表，正式查询负责筛格、去重、飞行排除和距离排序。
    private static JObject ColdFuelCandidates(JObject args)
    {
        const string world = "test:cold-fuel-world";
        var records = new List<DroppedItemSaveRecord>();
        for (int index = 0; index < 826; index++) records.Add(ColdFuelRecord(1000 + index, 10f + index, 10f));
        DroppedItemSaveRecord farther = ColdFuelRecord(1, .9f, .9f);
        DroppedItemSaveRecord nearest = ColdFuelRecord(2, .52f, .52f);
        records.Add(farther); records.Add(nearest);
        records.Add(ColdFuelRecord(3, 1.01f, .5f));
        DroppedItemSaveRecord flying = ColdFuelRecord(4, .5f, .5f); flying.HasFlight = true; records.Add(flying);
        DroppedItemSaveRecord unpickable = ColdFuelRecord(5, .5f, .5f); unpickable.Data.Stack.CanBePickedUp = false; records.Add(unpickable);
        records.Add(ColdFuelRecord(6, .5f, .5f, 0f));
        records.Add(ColdFuelRecord(7, .5f, .5f));
        records.Add(ColdFuelRecord(1, .51f, .51f));
        int sourceCount = records.Count;
        var identities = new HashSet<int> { 7 };
        var candidates = new List<DroppedItemService.ColdFuelCandidate>();
        DroppedItemService.CollectColdFuelCandidates(records, world, Vector2Int.zero, default, identities, candidates);
        DroppedItemService.SortColdFuelCandidates(candidates);
        var ids = new JArray();
        foreach (var candidate in candidates) ids.Add(candidate.Drop.Data.Guid);
        var nearestDrop = candidates[0].Drop;
        bool originalPresent = nearestDrop.IsCurrentRecord(records);
        bool replacedArchivePresent = nearestDrop.IsCurrentRecord(new List<DroppedItemSaveRecord>(records));
        records.RemoveAt(0);
        bool shiftedPresent = nearestDrop.IsCurrentRecord(records);
        records.Remove(nearest);
        bool removedPresent = nearestDrop.IsCurrentRecord(records);
        var wrapped = new WorldTopologyDomain(new int2(-8, -8), new int2(16, 16), true);
        var wrappedRecords = new List<DroppedItemSaveRecord> { ColdFuelRecord(90, 8.52f, .52f), ColdFuelRecord(91, -7.99f, 1.01f) };
        candidates.Clear(); identities.Clear();
        DroppedItemService.CollectColdFuelCandidates(wrappedRecords, world, new Vector2Int(-8, 0), wrapped, identities, candidates);
        int wrappedCount = candidates.Count;
        int wrappedGuid = wrappedCount > 0 ? candidates[0].Drop.Data.Guid : 0;
        candidates.Clear(); identities.Clear();
        DroppedItemService.CollectColdFuelCandidates(wrappedRecords, "test:other-world", new Vector2Int(-8, 0), default, identities, candidates);
        return new JObject
        {
            ["sourceCount"] = sourceCount, ["candidateIds"] = ids,
            ["originalPresent"] = originalPresent, ["replacedArchivePresent"] = replacedArchivePresent,
            ["shiftedPresent"] = shiftedPresent, ["removedPresent"] = removedPresent,
            ["wrappedCount"] = wrappedCount, ["wrappedGuid"] = wrappedGuid,
            ["unwrappedCount"] = candidates.Count,
            ["farRecordStillCold"] = records[0].Data.SharedConfiguration == null
        };
    }

    private static JObject ColdFuelTransfer(JObject args)
    {
        Data_GeneralItem source = EntityProbeData("test:cold-fuel", 5f);
        source.Guid = 100;
        var inventory = new Inventory_Data(new List<ItemSlot> { new(0) { SlotMaxVolume = 2f } }, "独立后台炉体燃料槽")
            { IsDepositBlocked = true };
        float blocked = DroppedItemService.TransferColdFuel(source, inventory);
        float afterBlocked = source.Stack.Amount;
        inventory.IsDepositBlocked = false;
        float partial = DroppedItemService.TransferColdFuel(source, inventory);
        float sourceAfterPartial = source.Stack.Amount;
        float targetAfterPartial = inventory.itemSlots[0].itemData?.Stack.Amount ?? 0f;
        bool splitIdentity = inventory.itemSlots[0].itemData?.Guid != source.Guid;
        float full = DroppedItemService.TransferColdFuel(source, inventory);
        float sourceAfterFull = source.Stack.Amount;
        inventory.itemSlots[0].itemData = null;
        float resumed = DroppedItemService.TransferColdFuel(source, inventory);
        return new JObject
        {
            ["blocked"] = blocked, ["afterBlocked"] = afterBlocked,
            ["partial"] = partial, ["sourceAfterPartial"] = sourceAfterPartial, ["targetAfterPartial"] = targetAfterPartial,
            ["splitIdentity"] = splitIdentity, ["full"] = full, ["sourceAfterFull"] = sourceAfterFull,
            ["resumed"] = resumed, ["finalSource"] = source.Stack.Amount,
            ["finalTarget"] = inventory.itemSlots[0].itemData?.Stack.Amount ?? 0f
        };
    }
    #endregion

    #region 后台掉落物复用工作区采样
    private static DroppedItemSaveRecord ColdMotionRecord(int id, float start, float end, float duration)
    {
        DroppedItemSaveRecord record = ColdFuelRecord(id, start, 0f);
        record.Scale = Vector2.one;
        record.HasFlight = true;
        record.FlightStart = record.Position;
        record.FlightEnd = new Vector2(end, 0f);
        record.FlightControl = (record.FlightStart + record.FlightEnd) * .5f;
        record.FlightDuration = duration;
        return record;
    }

    // 只调用正式冷物推进，跨世界、异常回收和真实状态判断交给 Lua。
    private static JObject ColdMotionWorkspace(JObject args)
    {
        var first = new List<DroppedItemSaveRecord> { ColdMotionRecord(1, 0f, 2f, 1f) };
        var second = new List<DroppedItemSaveRecord> { ColdMotionRecord(1, 8f, 12f, 2f) };
        DroppedItemService.StepColdDropMotion(first, .25f, default);
        float firstPosition = first[0].Position.x, firstElapsed = first[0].FlightElapsed;
        bool flyingPickable = first[0].Data.Stack.CanBePickedUp;
        DroppedItemService.StepColdDropMotion(second, .5f, default);
        float secondPosition = second[0].Position.x;
        float firstAfterSecond = first[0].Position.x;
        DroppedItemService.StepColdDropMotion(first, .75f, default);
        bool landed = !first[0].HasFlight && first[0].Data.Stack.CanBePickedUp;
        var floating = new List<DroppedItemSaveRecord> { ColdFuelRecord(2, 0f, 0f) };
        floating[0].WaterKind = 1; floating[0].HasWaterTransition = true;
        floating[0].WaterStart = 0f; floating[0].WaterTarget = .5f; floating[0].WaterDuration = 1f;
        DroppedItemService.StepColdDropMotion(floating, .5f, default);
        float middleDepth = floating[0].LiquidDepth;
        DroppedItemService.StepColdDropMotion(floating, .5f, default);
        float finalDepth = floating[0].LiquidDepth;
        bool floatingTransition = floating[0].HasWaterTransition;
        var sinking = new List<DroppedItemSaveRecord> { ColdFuelRecord(3, 0f, 0f) };
        sinking[0].WaterKind = 2; sinking[0].HasWaterTransition = true;
        sinking[0].WaterDuration = .1f; sinking[0].WaterTarget = 1f;
        sinking[0].WaterElapsed = .1f + WorldItemWaterRules.SubmergedRecedeDuration - .01f;
        DroppedItemService.StepColdDropMotion(sinking, .02f, default);
        bool sinkingRetained = sinking.Count == 1 && !sinking[0].HasWaterTransition &&
            sinking[0].WaterKind == LightweightDroppedBody.BottomWaterKind && sinking[0].Data.Stack.CanBePickedUp;
        DroppedItemService.StepColdDropMotion(sinking, 60f, default);
        string duplicateError = "";
        try
        {
            DroppedItemService.StepColdDropMotion(new List<DroppedItemSaveRecord>
                { ColdMotionRecord(4, 0f, 2f, 1f), ColdMotionRecord(4, 0f, 2f, 1f) }, .25f, default);
        }
        catch (ArgumentException error) { duplicateError = error.GetType().Name; }
        var recovered = new List<DroppedItemSaveRecord> { ColdMotionRecord(4, 0f, 2f, 1f) };
        DroppedItemService.StepColdDropMotion(recovered, .25f, default);
        float fixedStep = float.IsFinite(Time.fixedDeltaTime) && Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : .02f;
        var delayed = new List<DroppedItemSaveRecord> { ColdMotionRecord(5, 0f, 100f, 100f) };
        delayed[0].PendingSurfaceMotionSeconds = fixedStep * 2f;
        DroppedItemService.StepColdDropMotion(delayed, fixedStep, default);
        bool pendingSettled = delayed[0].PendingSurfaceMotionSeconds <= .000001f &&
            Mathf.Abs(delayed[0].FlightElapsed - fixedStep * 3f) < .0001f;
        var bounded = new List<DroppedItemSaveRecord> { ColdMotionRecord(6, 0f, 100f, 100f) };
        bounded[0].PendingSurfaceMotionSeconds = fixedStep * 70f;
        DroppedItemService.StepColdDropMotion(bounded, fixedStep, default);
        bool pendingBounded = bounded[0].PendingSurfaceMotionSeconds > 0f &&
            Mathf.Abs(bounded[0].FlightElapsed - fixedStep * 64f) < .001f;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        {
            recovered[0].FlightElapsed = 0f;
            DroppedItemService.StepColdDropMotion(recovered, .25f, default);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return new JObject
        {
            ["firstPosition"] = firstPosition, ["firstElapsed"] = firstElapsed, ["flyingPickable"] = flyingPickable,
            ["secondPosition"] = secondPosition, ["firstAfterSecond"] = firstAfterSecond,
            ["landed"] = landed, ["landedPosition"] = first[0].Position.x,
            ["middleDepth"] = middleDepth, ["finalDepth"] = finalDepth, ["floatingTransition"] = floatingTransition,
            ["sinkingCount"] = sinking.Count, ["sinkingRetained"] = sinkingRetained,
            ["sinkingDepth"] = sinking.Count > 0 ? sinking[0].LiquidDepth : -1f,
            ["sinkingAmount"] = sinking.Count > 0 ? sinking[0].Data.Stack.Amount : 0f,
            ["duplicateError"] = duplicateError,
            ["pendingSettled"] = pendingSettled, ["pendingBounded"] = pendingBounded,
            ["recoveredPosition"] = recovered[0].Position.x, ["repeatedMotionBytes"] = allocated
        };
    }
    #endregion

    #region 掉落物运动表增删与复用采样

    private static JObject CaptureDroppedMotionStorage(LightweightDroppedItemSimulation simulation,
        List<LightweightDroppedChange> changes)
    {
        var items = new JObject();
        foreach (int id in simulation.Ids)
        {
            LightweightDroppedBody body = simulation.Get(id);
            bool flying = simulation.TryGetFlight(id, out LightweightDroppedFlight flight);
            bool enteringWater = simulation.TryGetWater(id, out LightweightDroppedWaterTransition water);
            items[id.ToString()] = new JObject
            {
                ["x"] = body.Position.x, ["amount"] = body.Amount, ["rotation"] = body.Rotation,
                ["height"] = body.VisualHeight, ["waterKind"] = body.WaterKind, ["depth"] = body.LiquidDepth,
                ["flying"] = flying, ["flightElapsed"] = flight.Elapsed,
                ["enteringWater"] = enteringWater, ["waterElapsed"] = water.Elapsed
            };
        }
        var events = new JArray();
        foreach (LightweightDroppedChange change in changes)
            events.Add(new JObject { ["id"] = change.Id, ["kind"] = change.Kind });
        return new JObject { ["count"] = simulation.Count, ["items"] = items, ["changes"] = events };
    }

    // 只通过正式接口交错增删，Lua 核对补位后的身份、轨迹、水态和稳态分配。
    private static JObject DroppedMotionStorage(JObject args)
    {
        using var simulation = new LightweightDroppedItemSimulation();
        var changes = new List<LightweightDroppedChange>(128);
        for (int id = 1; id <= 9; id++)
        {
            bool flying = id % 2 == 0 || id == 9;
            bool enteringWater = id % 3 == 0;
            float2 start = new(id * 10f, 0f), end = start + new float2(4f, 0f);
            simulation.Create(new LightweightDroppedBody
            {
                Id = id, Position = start, Scale = new float2(.5f), Amount = id,
                WaterKind = (byte)(enteringWater ? 1 : 0)
            }, flying ? new LightweightDroppedFlight
            {
                Start = start, End = end, Control = (start + end) * .5f,
                Duration = id == 2 ? .25f : 2f, ArcHeight = 2f, RotationSpeed = 10f
            } : null, enteringWater ? new LightweightDroppedWaterTransition
            {
                TargetDepth = .8f, Duration = id == 3 ? .25f : 2f
            } : null);
        }
        simulation.Remove(1);
        simulation.Remove(4);
        simulation.Remove(6);
        simulation.Remove(999);
        var result = new JObject { ["removed"] = CaptureDroppedMotionStorage(simulation, changes) };
        simulation.Step(.25f, default, changes);
        result["stepped"] = CaptureDroppedMotionStorage(simulation, changes);

        LightweightDroppedBody edited = simulation.Get(9);
        edited.Amount = 37f;
        simulation.Set(edited);
        edited = simulation.Get(8);
        edited.WaterKind = 2;
        simulation.Set(edited);
        simulation.SetWater(9, new LightweightDroppedWaterTransition
            { StartDepth = .1f, TargetDepth = .6f, Duration = 1f });
        simulation.SetWater(8, new LightweightDroppedWaterTransition
            { TargetDepth = 1f, Duration = .25f, RecedeDuration = .25f });
        simulation.SetWater(999, null);
        simulation.Remove(2);
        simulation.Remove(3);
        simulation.Step(.25f, default, changes);
        result["updated"] = CaptureDroppedMotionStorage(simulation, changes);
        simulation.Remove(9);
        simulation.SetWater(8, null);
        changes.Clear();
        result["cancelled"] = CaptureDroppedMotionStorage(simulation, changes);
        simulation.SetWater(8, new LightweightDroppedWaterTransition
            { StartDepth = 1f, TargetDepth = 1f, Duration = .25f, RecedeDuration = .25f });
        simulation.Step(.5f, default, changes);
        result["sunk"] = CaptureDroppedMotionStorage(simulation, changes);
        simulation.Create(new LightweightDroppedBody { Id = 1, Amount = 99f, Position = new float2(-.5f) });
        simulation.Step(2f, default, changes);
        result["landed"] = CaptureDroppedMotionStorage(simulation, changes);

        simulation.Clear();
        result["clearedCount"] = simulation.Count;
        result["clearedContains"] = simulation.Contains(8);
        simulation.Create(new LightweightDroppedBody { Id = 8, Amount = 3f, Position = new float2(-3f, -2f) });
        simulation.Step(1f, default, changes);
        result["reused"] = CaptureDroppedMotionStorage(simulation, changes);

        simulation.Clear();
        for (int id = 1; id <= 64; id++)
            simulation.Create(new LightweightDroppedBody { Id = id, Amount = 1f, WaterKind = 1 },
                new LightweightDroppedFlight { End = new float2(1f), Duration = 10000f },
                new LightweightDroppedWaterTransition { TargetDepth = .5f, Duration = 10000f });
        for (int i = 0; i < 32; i++) simulation.Step(.001f, default, changes);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 128; i++) simulation.Step(.001f, default, changes);
        result["motionAllocatedBytes"] = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 32; i++)
        {
            simulation.Clear();
            simulation.Create(new LightweightDroppedBody { Id = 9, Amount = 1f },
                new LightweightDroppedFlight { Duration = 1f },
                new LightweightDroppedWaterTransition { Duration = 1f });
            simulation.Step(.25f, default, changes);
            simulation.Remove(9);
        }
        result["reuseAllocatedBytes"] = GC.GetAllocatedBytesForCurrentThread() - before;
        result["finalCount"] = simulation.Count;
        return result;
    }

    #endregion

    #region 河底物品状态与拾取采样

    private static JObject BottomRecovery(JObject args)
    {
        using var simulation = new LightweightDroppedItemSimulation();
        var changes = new List<LightweightDroppedChange>();
        simulation.Create(new LightweightDroppedBody
        {
            Id = 101, Position = new float2(-.9f, -.9f), Scale = new float2(.5f, .5f),
            Amount = 7f, WaterKind = 2, Pickable = 1
        }, water: new LightweightDroppedWaterTransition
        { StartDepth = 0f, TargetDepth = 1f, Duration = 1f, RecedeDuration = .5f });
        simulation.Step(1f, default, changes);
        bool stillSinking = !simulation.Get(101).IsOnBottom && simulation.TryGetWater(101, out _);
        simulation.Step(.5f, default, changes);
        LightweightDroppedBody bottom = simulation.Get(101);
        bool completed = changes.Count == 1 && changes[0].Kind == 3 && !simulation.TryGetWater(101, out _);
        Data_GeneralItem payload = EntityProbeData("test:bottom-water", 7f);
        payload.Guid = 101;
        DroppedItemSaveRecord captured = DroppedItemSaveRecord.Capture(payload, simulation, 101);
        byte[] bytes = MemoryPack.MemoryPackSerializer.Serialize(captured);
        DroppedItemSaveRecord restored = MemoryPack.MemoryPackSerializer.Deserialize<DroppedItemSaveRecord>(bytes);
        using var restoredSimulation = new LightweightDroppedItemSimulation();
        restoredSimulation.Create(restored.RestoreBody(restored.Data, default));
        restoredSimulation.Step(120f, default, changes);
        LightweightDroppedBody retained = restoredSimulation.Get(101);

        // 完整 Item 走同一存档扩展，不允许把河底标记带进背包或覆盖别的模块。
        payload.ItemSpecialData = "{\"other\":{\"keep\":42}}";
        WorldItemWaterSystem.SetSavedBottomState(payload, true);
        ItemData itemRestored = ItemInstanceSnapshot.Capture(payload).CreateColdData();
        bool itemStateSaved = WorldItemWaterSystem.GetSavedBottomState(itemRestored);
        WorldItemWaterSystem.SetSavedBottomState(itemRestored, false);
        bool itemStateCleared = !WorldItemWaterSystem.GetSavedBottomState(itemRestored);
        int otherState = JObject.Parse(itemRestored.ItemSpecialData)["other"]["keep"].Value<int>();
        payload.ItemSpecialData = "ordinary-special-data";
        WorldItemWaterSystem.SetSavedBottomState(payload, true);
        WorldItemWaterSystem.SetSavedBottomState(payload, false);

        return new JObject
        {
            ["stillSinking"] = stillSinking, ["completed"] = completed,
            ["retainedCount"] = restoredSimulation.Count, ["onBottom"] = retained.IsOnBottom,
            ["amount"] = retained.Amount, ["scale"] = retained.Scale.x,
            ["x"] = retained.Position.x, ["y"] = retained.Position.y,
            ["visualScale"] = WorldItemWaterRules.ResolveSubmergedScale(retained.SubmergedProgress),
            ["sameCell"] = WorldItemWaterRules.CanPickUp(true, new Vector2(-.01f, -.01f), bottom.Position),
            ["shoreCell"] = WorldItemWaterRules.CanPickUp(true, new Vector2(.01f, -.9f), bottom.Position),
            ["adjacentWaterCell"] = WorldItemWaterRules.CanPickUp(true, new Vector2(-.9f, -1.01f), bottom.Position),
            ["floatingReach"] = WorldItemWaterRules.CanPickUp(false, new Vector2(.01f, -.9f), bottom.Position),
            ["itemStateSaved"] = itemStateSaved, ["itemStateCleared"] = itemStateCleared,
            ["otherState"] = otherState, ["rawStateRestored"] = payload.ItemSpecialData == "ordinary-special-data"
        };
    }

    #endregion
}
