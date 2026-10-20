using System;
using System.Collections.Generic;
using System.Text;
using FlatWorld.WorldModel;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 自动保存预算与冻结载荷采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterAutoSaveRules()
    {
        Register("save", "autosave_budget", ServiceAutoSaveBudget);
        Register("save", "autosave_chunk_payload", ServiceAutoSaveChunkPayload);
        Register("save", "autosave_ecology_payload", ServiceAutoSaveEcologyPayload);
        Register("save", "agriculture_cell_index", ServiceAgricultureCellIndex);
        Register("save", "load_failure_state", ServiceLoadFailureState);
        Register("save", "rename_feedback", ServiceRenameFeedback);
        Register("save", "resource_reload_feedback", ServiceResourceReloadFeedback);
    }

    private static JObject ServiceRenameFeedback(JObject args)
    {
        var host = new GameObject("probe:inactive-rename-feedback");
        host.SetActive(false);
        try { return host.AddComponent<SaveDataMgr>().CaptureTestRenameFeedback(); }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    private static JObject ServiceResourceReloadFeedback(JObject args)
    {
        const string failure = "probe:actual-publish-failure";
        Exception nestedFailure = null;
        var steps = GameManager.RunWorldTransitionSteps(OuterSteps(), exception => nestedFailure = exception);
        while (steps.MoveNext()) { }
        return new JObject
        {
            ["beforePublish"] = GameSaveStatusHUD.GetResourceReloadResultText(false, false, failure),
            ["afterPublish"] = GameSaveStatusHUD.GetResourceReloadResultText(false, true, failure),
            ["success"] = GameSaveStatusHUD.GetResourceReloadResultText(true, true, failure),
            ["missingReason"] = GameSaveStatusHUD.GetResourceReloadResultText(false, false, null),
            ["dimensionReason"] = GameManager.GetNetworkDimensionFailureText(failure),
            ["nestedReloadReason"] = nestedFailure?.Message
        };

        // 使用正式迭代器驱动器验证跨帧嵌套异常不会绕过热更新失败反馈。
        System.Collections.IEnumerator OuterSteps() { yield return FailingSteps(); }
        System.Collections.IEnumerator FailingSteps()
        {
            yield return null;
            throw new InvalidOperationException(failure);
        }
    }

    private static JObject ServiceLoadFailureState(JObject args)
    {
        var host = new GameObject("probe:inactive-save-failure");
        host.SetActive(false);
        try { return host.AddComponent<SaveDataMgr>().CaptureTestLoadFailureState(); }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    // 使用 Lua 提供的确定性时间采样正式预算，规则不驱动正在游玩的帧循环。
    private static JObject ServiceAutoSaveBudget(JObject args)
    {
        var budget = new AutoSaveWorkBudget(Number(args, "startTime", 0d));
        var decisions = new JArray();
        if (args["steps"] is not JArray steps)
            throw new ArgumentException("自动保存预算采样必须提供 steps。");
        foreach (JToken token in steps)
        {
            if (token is not JObject step)
                throw new ArgumentException("自动保存预算 steps 必须是对象。");
            double now = Number(step, "now", 0d);
            if (Flag(step, "restart", false))
                budget.Restart(now);
            else
                decisions.Add(budget.ShouldYield(now));
        }
        return new JObject
        {
            ["sliceSeconds"] = AutoSaveWorkBudget.SliceSeconds,
            ["maxWorkUnits"] = AutoSaveWorkBudget.MaxWorkUnits,
            ["yieldFrames"] = AutoSaveWorkBudget.YieldFrames,
            ["decisions"] = decisions
        };
    }

    // 真实实例与地形先各自冻结为区块字节，再调用后台实际使用的纯字节封装入口。
    private static JObject ServiceAutoSaveChunkPayload(JObject args)
    {
        Data_GeneralItem item = ServiceItem(args);
        Data_GeneralItem child = ServiceItem(args);
        child.Guid += 10;
        var inventory = new Inventory_Data(new List<ItemSlot> { new(0) { itemData = child }, new(1) }, "cargo")
        { Index = 1 };
        item.ModuleDataDic.Add("cargo", new Inventory_ModuleData
        {
            StableName = "cargo", ModuleId = "inventory",
            Data = new Dictionary<string, Inventory_Data> { ["cargo"] = inventory }
        });
        var terrain = new TerrainCell(101, 102, 103, 104, 105, TerrainCellFlags.Blocking);
        var source = new ChunkSaveRecord
        {
            PlanetName = "probe:autosave", ChunkName = "1_0", ChunkPosition = new Vector2Int(1, 0),
            ChangedItems = new List<ItemData> { item }, RemovedItemGuids = new List<int> { 41 },
            RuntimeTileDeltas = new List<RuntimeTileCellSaveDelta>
            {
                RuntimeTileCellSaveDelta.Capture(new Vector2Int(2, 3), terrain, 12.5f)
            }
        };
        var second = new ChunkSaveRecord
        {
            PlanetName = "probe:autosave", ChunkName = "0_0", ChunkPosition = Vector2Int.zero,
            RemovedItemGuids = new List<int> { 42 }
        };
        byte[] firstBytes = MemoryPackSerializer.Serialize(source);
        byte[] secondBytes = MemoryPackSerializer.Serialize(second);
        var core = new GameSaveData { saveName = "probe:autosave_core", Seed = 2468 };
        byte[] coreBytes = MemoryPackSerializer.Serialize(core);
        byte[] machines = { 1, 2, 3 };
        byte[] drops = { 4, 5 };
        byte[] mods = { 6, 7, 8, 9 };

        item.Guid += 1000;
        item.Stack.Amount += 7;
        child.Stack.Amount += 11;
        source.RuntimeTileDeltas[0].GroundTileId = 901;
        source.RemovedItemGuids.Add(99);
        second.ChunkName = "changed_after_capture";
        core.Seed = -1;

        byte[] payload = SaveDataMgr.BuildCapturedSavePayload(coreBytes,
            new List<byte[]> { firstBytes, secondBytes }, machines, drops, mods, new List<FrozenEcologyChunkSaveRecord>());
        ModdedSaveEnvelope outer = MemoryPackSerializer.Deserialize<ModdedSaveEnvelope>(ServiceAutoSaveBody(payload));
        CompactSaveEnvelope compact = outer.CoreSavePayload;
        GameSaveData restoredCore = MemoryPackSerializer.Deserialize<GameSaveData>(compact.CoreSaveData);
        ChunkSaveRecord restored = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(compact.ChunkRecords[0]);
        ChunkSaveRecord restoredSecond = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(compact.ChunkRecords[1]);
        ItemData restoredItem = restored.ChangedItems[0];
        Inventory_Data restoredInventory = ((Inventory_ModuleData)restoredItem.ModuleDataDic["cargo"]).Data["cargo"];
        ItemData restoredChild = restoredInventory.itemSlots[0].itemData;
        RuntimeTileCellSaveDelta delta = restored.RuntimeTileDeltas[0];
        return new JObject
        {
            ["bytes"] = payload.Length,
            ["outerMagic"] = Encoding.ASCII.GetString(payload, 0, 4), ["outerVersion"] = outer.Version,
            ["compactVersion"] = compact.Version,
            ["coreName"] = restoredCore.saveName, ["coreSeed"] = restoredCore.Seed,
            ["recordCount"] = compact.ChunkRecords.Count,
            ["firstChunk"] = restored.ChunkName, ["secondChunk"] = restoredSecond.ChunkName,
            ["planet"] = restored.PlanetName, ["chunkX"] = restored.ChunkPosition.x,
            ["itemCount"] = restored.ChangedItems.Count, ["guid"] = restoredItem.Guid,
            ["amount"] = restoredItem.Stack.Amount, ["definitionId"] = restoredItem.IDName,
            ["sourceGuid"] = item.Guid, ["sourceAmount"] = item.Stack.Amount,
            ["childGuid"] = restoredChild.Guid, ["childAmount"] = restoredChild.Stack.Amount,
            ["sourceChildAmount"] = child.Stack.Amount,
            ["slotCount"] = restoredInventory.itemSlots.Count, ["selected"] = restoredInventory.Index,
            ["secondEmpty"] = restoredInventory.itemSlots[1].itemData == null,
            ["removedCount"] = restored.RemovedItemGuids.Count, ["removedGuid"] = restored.RemovedItemGuids[0],
            ["terrainCount"] = restored.RuntimeTileDeltas.Count,
            ["localX"] = delta.LocalPosition.x, ["localY"] = delta.LocalPosition.y,
            ["ground"] = delta.GroundTileId, ["back"] = delta.BackTileId, ["blocking"] = delta.BlockingTileId,
            ["biome"] = delta.BiomeId, ["navigationCost"] = delta.NavigationCost,
            ["flags"] = (int)delta.Flags, ["damage"] = delta.AccumulatedDamage,
            ["firstBytesPreserved"] = Convert.ToBase64String(compact.ChunkRecords[0]) == Convert.ToBase64String(firstBytes),
            ["machinesPreserved"] = Convert.ToBase64String(compact.MechanicalNetworks) == Convert.ToBase64String(machines),
            ["dropsPreserved"] = Convert.ToBase64String(compact.DroppedItems) == Convert.ToBase64String(drops),
            ["modsPreserved"] = Convert.ToBase64String(outer.ModMetadata) == Convert.ToBase64String(mods)
        };
    }

    // 逐物品冻结、让帧期间的采收/替换与最终联合点重组都走正式入口。
    private static JObject ServiceAutoSaveEcologyPayload(JObject args)
    {
        var source = new GameSaveData { saveName = "probe:ecology", Seed = 9123 };
        var surface = new PlanetData();
        var cave = new PlanetData();
        source.PlanetData_Dict.Add("surface", surface);
        source.PlanetData_Dict.Add("surface__dimension__cave", cave);
        source.PlanetData_Dict.Add("empty", new PlanetData());
        EcologyChunkSaveData chunk = cave.Ecology.GetOrCreateChunk(16, -32);
        chunk.RemovedGuids.Add(90);
        chunk.RenewalYears.Add(90, 7);
        for (int i = 0; i < 40; i++)
        {
            Data_GeneralItem item = ServiceItem(args);
            item.Guid = 1000 + i;
            chunk.ChangedItems.Add(item);
        }
        surface.Ecology.GetOrCreateChunk(0, 0).RemovedGuids.Add(80);
        var frozen = new List<FrozenEcologyChunkSaveRecord>();
        var cache = new Dictionary<ItemData, byte[]>();
        int workUnits = 0;
        foreach (int work in SaveDataMgr.FreezeEcologyChunks(source, frozen, cache)) workUnits += work;
        List<EcologyChunkSaveData> sourceChunks = cave.Ecology.Chunks;
        Dictionary<string, MapSave> sourceMaps = cave.MapData_Dict;
        byte[] core = SaveDataMgr.SerializeCoreDataWithoutChunks(source);
        bool sourceListsRestored = ReferenceEquals(sourceChunks, cave.Ecology.Chunks) &&
            ReferenceEquals(sourceMaps, cave.MapData_Dict) && chunk.ChangedItems.Count == 40;
        GameSaveData coreOnly = MemoryPackSerializer.Deserialize<GameSaveData>(core);
        var initial = MemoryPackSerializer.Deserialize<EcologyChunkSaveData>(frozen[1].ChunkData);
        ItemData first = MemoryPackSerializer.Deserialize<ItemData>(frozen[1].ChangedItems[0]);

        chunk.MarkRemoved(1000);
        Data_GeneralItem replacement = ServiceItem(args);
        replacement.Guid = 1001;
        replacement.Stack.Amount = 9;
        Data_GeneralItem nested = ServiceItem(args);
        nested.Guid = 2001;
        nested.Stack.Amount = 5;
        replacement.ModuleDataDic.Add("cargo", new Inventory_ModuleData
        {
            StableName = "cargo", ModuleId = "inventory",
            Data = new Dictionary<string, Inventory_Data>
            {
                ["cargo"] = new(new List<ItemSlot> { new(0) { itemData = nested } }, "cargo")
            }
        });
        chunk.CaptureChangedItem(replacement);
        chunk.RenewalYears[90] = 8;
        cave.Ecology.GetOrCreateChunk(32, -32).RemovedGuids.Add(91);
        frozen = new List<FrozenEcologyChunkSaveRecord>();
        foreach (int work in SaveDataMgr.FreezeEcologyChunks(source, frozen, cache)) { }
        byte[] payload = SaveDataMgr.BuildCapturedSavePayload(core, new List<byte[]>(), null, null, null, frozen);
        replacement.Stack.Amount = 99;
        nested.Stack.Amount = 77;
        chunk.RemovedGuids.Clear();
        chunk.RenewalYears.Clear();

        var host = new GameObject("probe:inactive-save");
        host.SetActive(false);
        try
        {
            SaveDataMgr reader = host.AddComponent<SaveDataMgr>();
            GameSaveData restored = reader.ReadTestArchiveWithoutPublishing(payload);
            EcologyChunkSaveData result = restored.PlanetData_Dict["surface__dimension__cave"].Ecology.Chunks[0];
            EcologyWorldSaveData emptyEcology = restored.PlanetData_Dict["empty"].Ecology;
            bool emptyRoundTrip = emptyEcology.Chunks == null && emptyEcology.GetOrCreateChunk(64, 96).ChunkX == 64 &&
                emptyEcology.Chunks.Count == 1;
            ItemData restoredNested = ((Inventory_ModuleData)result.ChangedItems[0].ModuleDataDic["cargo"])
                .Data["cargo"].itemSlots[0].itemData;
            byte[] oldHeader = (byte[])payload.Clone();
            oldHeader[3] = (byte)'5';
            string oldFormat = null;
            try { reader.ReadTestArchiveWithoutPublishing(oldHeader); }
            catch (Exception exception) { oldFormat = exception.GetType().Name; }
            return new JObject
            {
                ["workUnits"] = workUnits, ["cacheCount"] = cache.Count,
                ["coreExcludesChunks"] = coreOnly.PlanetData_Dict["surface__dimension__cave"].Ecology.Chunks == null,
                ["sourceChunksRestored"] = sourceListsRestored,
                ["emptyRoundTrip"] = emptyRoundTrip, ["nestedAmount"] = restoredNested.Stack.Amount,
                ["initialCount"] = initial.ChangedItems?.Count ?? 0, ["initialGuid"] = first.Guid,
                ["initialAmount"] = first.Stack.Amount,
                ["worlds"] = restored.PlanetData_Dict.Count,
                ["surfaceRemoved"] = restored.PlanetData_Dict["surface"].Ecology.Chunks[0].RemovedGuids[0],
                ["caveChunks"] = restored.PlanetData_Dict["surface__dimension__cave"].Ecology.Chunks.Count,
                ["itemCount"] = result.ChangedItems.Count, ["firstGuid"] = result.ChangedItems[0].Guid,
                ["firstAmount"] = result.ChangedItems[0].Stack.Amount,
                ["harvestRemoved"] = result.IsRemoved(1000), ["renewalYear"] = result.RenewalYears[90],
                ["chunkX"] = result.ChunkX, ["chunkY"] = result.ChunkY,
                ["lateChunkRemoved"] = restored.PlanetData_Dict["surface__dimension__cave"].Ecology.Chunks[1].RemovedGuids[0],
                ["oldFormat"] = oldFormat
            };
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    // 独立记录验证密集耕地索引、外部列表变化和真实存档序列化均保持原样。
    private static JObject ServiceAgricultureCellIndex(JObject args)
    {
        var record = new ChunkSaveRecord { PlanetName = "probe", ChunkName = "0_0" };
        bool initiallyMissing = record.FindAgricultureCell(Vector2Int.zero) == null;
        for (int i = 0; i < 256; i++)
        {
            AgricultureCellSaveData cell = record.GetOrAddAgricultureCell(new Vector2Int(i % 16, i / 16));
            cell.Water = i % 11;
        }
        AgricultureCellSaveData first = record.FindAgricultureCell(new Vector2Int(3, 0));
        first.Crop = ServiceItem(args);
        bool identityPreserved = ReferenceEquals(first, record.GetOrAddAgricultureCell(first.LocalPosition)) &&
            ReferenceEquals(first.Crop, record.FindAgricultureCell(first.LocalPosition).Crop) &&
            record.AgricultureCells.Count == 256;
        byte[] beforeIndexRead = MemoryPackSerializer.Serialize(record);
        const int iterations = 8192;
        int baselineSum = 0, indexedSum = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            Vector2Int position = new((i * 73 & 255) % 16, (i * 73 & 255) / 16);
            AgricultureCellSaveData found = record.AgricultureCells.Find(c => c.LocalPosition == position);
            baselineSum += found.Water;
        }
        timer.Stop();
        double linearMilliseconds = timer.Elapsed.TotalMilliseconds;
        long before = GC.GetAllocatedBytesForCurrentThread();
        timer.Restart();
        for (int i = 0; i < iterations; i++)
        {
            Vector2Int position = new((i * 73 & 255) % 16, (i * 73 & 255) / 16);
            indexedSum += record.FindAgricultureCell(position).Water;
        }
        timer.Stop();
        long indexedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        double indexedMilliseconds = timer.Elapsed.TotalMilliseconds;
        bool persistedBytesUnchanged = beforeIndexRead.AsSpan().SequenceEqual(MemoryPackSerializer.Serialize(record));

        // 列表追加、同数量替换、移除以及更换列表实例都不能命中旧位置。
        var extra = new AgricultureCellSaveData { LocalPosition = new Vector2Int(16, 0) };
        record.AgricultureCells.Add(extra);
        bool appended = ReferenceEquals(record.FindAgricultureCell(extra.LocalPosition), extra);
        var replacement = new AgricultureCellSaveData { LocalPosition = new Vector2Int(17, 0) };
        record.AgricultureCells[1] = replacement;
        bool replaced = record.FindAgricultureCell(new Vector2Int(1, 0)) == null &&
            ReferenceEquals(record.FindAgricultureCell(replacement.LocalPosition), replacement);
        record.AgricultureCells.Remove(extra);
        bool removed = record.FindAgricultureCell(extra.LocalPosition) == null;
        var duplicate = new AgricultureCellSaveData { LocalPosition = first.LocalPosition };
        record.AgricultureCells.Add(duplicate);
        bool originalFirst = ReferenceEquals(first, record.FindAgricultureCell(first.LocalPosition));
        record.AgricultureCells.Remove(first);
        bool duplicateNext = ReferenceEquals(duplicate, record.FindAgricultureCell(duplicate.LocalPosition));
        record.AgricultureCells = new List<AgricultureCellSaveData> { replacement };
        bool listReplaced = record.FindAgricultureCell(duplicate.LocalPosition) == null &&
            ReferenceEquals(record.FindAgricultureCell(replacement.LocalPosition), replacement);

        ChunkSaveRecord restored = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(beforeIndexRead);
        bool roundTrip = restored.AgricultureCells.Count == 256 &&
            restored.FindAgricultureCell(new Vector2Int(3, 0)).Crop?.Guid == first.Crop.Guid &&
            restored.FindAgricultureCell(new Vector2Int(15, 15)).Water == 255 % 11;
        return new JObject
        {
            ["initiallyMissing"] = initiallyMissing, ["identityPreserved"] = identityPreserved,
            ["baselineSum"] = baselineSum, ["indexedSum"] = indexedSum,
            ["linearMilliseconds"] = linearMilliseconds, ["indexedMilliseconds"] = indexedMilliseconds,
            ["indexedBytes"] = indexedBytes, ["persistedBytesUnchanged"] = persistedBytesUnchanged,
            ["appended"] = appended, ["replaced"] = replaced, ["removed"] = removed,
            ["originalFirst"] = originalFirst, ["duplicateNext"] = duplicateNext,
            ["listReplaced"] = listReplaced, ["roundTrip"] = roundTrip
        };
    }

    private static byte[] ServiceAutoSaveBody(byte[] payload)
    {
        var body = new byte[payload.Length - 4];
        Buffer.BlockCopy(payload, 4, body, 0, body.Length);
        return body;
    }

    #endregion
}
