using System;
using System.Collections.Generic;
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
    }

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
        string duplicateError = "";
        try
        {
            DroppedItemService.StepColdDropMotion(new List<DroppedItemSaveRecord>
                { ColdMotionRecord(4, 0f, 2f, 1f), ColdMotionRecord(4, 0f, 2f, 1f) }, .25f, default);
        }
        catch (ArgumentException error) { duplicateError = error.GetType().Name; }
        var recovered = new List<DroppedItemSaveRecord> { ColdMotionRecord(4, 0f, 2f, 1f) };
        DroppedItemService.StepColdDropMotion(recovered, .25f, default);
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
            ["sinkingCount"] = sinking.Count, ["duplicateError"] = duplicateError,
            ["recoveredPosition"] = recovered[0].Position.x, ["repeatedMotionBytes"] = allocated
        };
    }
    #endregion
}
