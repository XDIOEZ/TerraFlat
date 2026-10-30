using System;
using System.Collections.Generic;
using MemoryPack;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 生态冻结缓存采样

    private static void RegisterEcologyFrozenSnapshotCacheOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("save/ecology_snapshot_cache", ServiceEcologyFrozenSnapshotCache);
    }

    // 使用正式冻结入口和入世复制入口采样，不在探针里重算生态规则。
    private static JObject ServiceEcologyFrozenSnapshotCache(JObject args)
    {
        var source = new GameSaveData();
        var world = new PlanetData();
        source.PlanetData_Dict.Add("probe:world", world);
        EcologyChunkSaveData chunk = world.Ecology.GetOrCreateChunk(16, -32);
        chunk.RemovedGuids.AddRange(new[] { 90, 91 });
        chunk.RenewalYears.Add(90, 7);
        chunk.RenewalYears.Add(91, 11);
        Data_GeneralItem first = ServiceItem(args);
        Data_GeneralItem second = ServiceItem(args);
        Data_GeneralItem third = ServiceItem(args);
        Data_GeneralItem nested = ServiceItem(args);
        first.Guid = 101; second.Guid = 102; third.Guid = 103; nested.Guid = 201;
        nested.Stack.Amount = 5;
        first.ModuleDataDic.Add("cargo", new Inventory_ModuleData
        {
            StableName = "cargo", ModuleId = "inventory",
            Data = new Dictionary<string, Inventory_Data>
            {
                ["cargo"] = new(new List<ItemSlot> { new(0) { itemData = nested } }, "cargo")
            }
        });
        chunk.ChangedItems.AddRange(new ItemData[] { first, second, third });
        world.Ecology.GetOrCreateChunk(48, 0).RemovedGuids.Add(200);

        List<FrozenEcologyChunkSaveRecord> initial = FreezeEcologyForCacheProbe(source, out int initialWork);
        List<FrozenEcologyChunkSaveRecord> repeated = FreezeEcologyForCacheProbe(source, out int repeatedWork);
        bool recordsReused = ReferenceEquals(initial[0], repeated[0]) && ReferenceEquals(initial[1], repeated[1]);
        bool frozenBytesReused = ReferenceEquals(initial[0].ChunkData, repeated[0].ChunkData) &&
            ReferenceEquals(initial[0].ChangedItems[0], repeated[0].ChangedItems[0]);

        ItemData runtime = ChunkNaturalItemRenderer.CreateRuntimeItemFromEcologySnapshot(first);
        ItemData runtimeNested = ((Inventory_ModuleData)runtime.ModuleDataDic["cargo"]).Data["cargo"].itemSlots[0].itemData;
        runtime.Stack.Amount = 42;
        runtimeNested.Stack.Amount = 55;
        List<FrozenEcologyChunkSaveRecord> afterRuntimeEdit = FreezeEcologyForCacheProbe(source, out _);
        bool runtimeIsolated = !ReferenceEquals(runtime, first) && !ReferenceEquals(runtimeNested, nested) &&
            first.Stack.Amount == 3 && nested.Stack.Amount == 5 && ReferenceEquals(initial[0], afterRuntimeEdit[0]);

        chunk.ChunkX = 17;
        chunk.RemovedGuids[0] = 97;
        chunk.RenewalYears[90] = 8;
        List<FrozenEcologyChunkSaveRecord> metadataOnly = FreezeEcologyForCacheProbe(source, out _);
        EcologyChunkSaveData changedMetadata = MemoryPackSerializer.Deserialize<EcologyChunkSaveData>(metadataOnly[0].ChunkData);
        bool metadataChanged = !ReferenceEquals(initial[0].ChunkData, metadataOnly[0].ChunkData) &&
            changedMetadata.ChunkX == 17 && changedMetadata.ChunkY == -32 &&
            changedMetadata.RemovedGuids[0] == 97 && changedMetadata.RenewalYears[90] == 8;
        bool metadataOnlyReusedItems = ReferenceEquals(initial[0].ChangedItems, metadataOnly[0].ChangedItems) &&
            ReferenceEquals(initial[1], metadataOnly[1]);

        Data_GeneralItem replacement = ServiceItem(args);
        replacement.Guid = 102;
        replacement.Stack.Amount = 9;
        Data_GeneralItem added = ServiceItem(args);
        added.Guid = 104;
        added.Stack.Amount = 12;
        chunk.CaptureChangedItem(replacement);
        chunk.CaptureChangedItem(added);
        List<FrozenEcologyChunkSaveRecord> membersChanged = FreezeEcologyForCacheProbe(source, out _);
        bool membershipSelective = ReferenceEquals(metadataOnly[0].ChunkData, membersChanged[0].ChunkData) &&
            ReferenceEquals(metadataOnly[0].ChangedItems[0], membersChanged[0].ChangedItems[0]) &&
            !ReferenceEquals(metadataOnly[0].ChangedItems[1], membersChanged[0].ChangedItems[1]) &&
            ReferenceEquals(metadataOnly[0].ChangedItems[2], membersChanged[0].ChangedItems[2]) &&
            MemoryPackSerializer.Deserialize<ItemData>(membersChanged[0].ChangedItems[1]).Stack.Amount == 9 &&
            MemoryPackSerializer.Deserialize<ItemData>(membersChanged[0].ChangedItems[3]).Guid == 104;

        chunk.MarkRemoved(101);
        chunk.RenewalYears[101] = 14;
        world.Ecology.GetOrCreateChunk(64, 0).RemovedGuids.Add(300);
        List<FrozenEcologyChunkSaveRecord> final = FreezeEcologyForCacheProbe(source, out _);
        EcologyChunkSaveData finalMetadata = MemoryPackSerializer.Deserialize<EcologyChunkSaveData>(final[0].ChunkData);
        bool harvestAndLateChunk = final.Count == 3 && finalMetadata.IsRemoved(101) &&
            finalMetadata.RenewalYears[101] == 14 && final[0].ChangedItems.Count == 3 &&
            MemoryPackSerializer.Deserialize<ItemData>(final[0].ChangedItems[0]).Guid == 102 &&
            ReferenceEquals(membersChanged[0].ChangedItems[2], final[0].ChangedItems[1]) &&
            MemoryPackSerializer.Deserialize<EcologyChunkSaveData>(final[2].ChunkData).IsRemoved(300);

        var otherRoot = new GameSaveData();
        otherRoot.PlanetData_Dict.Add("probe:world", new PlanetData { Ecology = world.Ecology });
        List<FrozenEcologyChunkSaveRecord> otherRootRecords = FreezeEcologyForCacheProbe(otherRoot, out _);
        bool rootSeparated = !ReferenceEquals(final[0], otherRootRecords[0]) &&
            !ReferenceEquals(final[0].ChangedItems[0], otherRootRecords[0].ChangedItems[0]);
        var newChunk = new EcologyChunkSaveData { ChunkX = 17, ChunkY = -32 };
        newChunk.RemovedGuids.Add(777);
        world.Ecology.Chunks[0] = newChunk;
        List<FrozenEcologyChunkSaveRecord> replacedChunk = FreezeEcologyForCacheProbe(source, out _);
        bool chunkSeparated = !ReferenceEquals(final[0], replacedChunk[0]) &&
            MemoryPackSerializer.Deserialize<EcologyChunkSaveData>(replacedChunk[0].ChunkData).IsRemoved(777);

        replacement.Stack.Amount = 99;
        nested.Stack.Amount = 77;
        chunk.RemovedGuids.Clear();
        chunk.RenewalYears.Clear();
        ItemData oldFirst = MemoryPackSerializer.Deserialize<ItemData>(initial[0].ChangedItems[0]);
        ItemData oldNested = ((Inventory_ModuleData)oldFirst.ModuleDataDic["cargo"]).Data["cargo"].itemSlots[0].itemData;
        bool frozenIsolation = oldFirst.Stack.Amount == 3 && oldNested.Stack.Amount == 5 &&
            MemoryPackSerializer.Deserialize<ItemData>(final[0].ChangedItems[0]).Stack.Amount == 9 &&
            MemoryPackSerializer.Deserialize<EcologyChunkSaveData>(final[0].ChunkData).IsRemoved(101);

        return new JObject
        {
            ["initialWork"] = initialWork, ["repeatedWork"] = repeatedWork,
            ["recordsReused"] = recordsReused, ["frozenBytesReused"] = frozenBytesReused,
            ["runtimeIsolated"] = runtimeIsolated, ["metadataChanged"] = metadataChanged,
            ["metadataOnlyReusedItems"] = metadataOnlyReusedItems, ["membershipSelective"] = membershipSelective,
            ["harvestAndLateChunk"] = harvestAndLateChunk, ["rootSeparated"] = rootSeparated,
            ["chunkSeparated"] = chunkSeparated, ["frozenIsolation"] = frozenIsolation,
            ["overlappingCapture"] = ProbeOverlappingEcologyFrozenCaptures(args),
            ["playerCapturePolicy"] = ProbeEcologyPlayerCapturePolicy(),
            ["nestedPlayerCapturePolicy"] = ProbeEcologyNestedPlayerCapturePolicy(args),
            ["codecRevision"] = ProbeEcologyFrozenCodecRevision(args)
        };
    }

    private static List<FrozenEcologyChunkSaveRecord> FreezeEcologyForCacheProbe(GameSaveData source, out int workUnits,
        Dictionary<ItemData, byte[]> frozenItems = null)
    {
        var records = new List<FrozenEcologyChunkSaveRecord>();
        workUnits = 0;
        foreach (int work in SaveDataMgr.FreezeEcologyChunks(source, records, frozenItems)) workUnits += work;
        return records;
    }

    private static bool ProbeOverlappingEcologyFrozenCaptures(JObject args)
    {
        var source = new GameSaveData();
        var world = new PlanetData();
        source.PlanetData_Dict.Add("probe:overlap", world);
        EcologyChunkSaveData chunk = world.Ecology.GetOrCreateChunk(0, 0);
        Data_GeneralItem first = ServiceItem(args), oldSecond = ServiceItem(args), newSecond = ServiceItem(args);
        first.Guid = 1; oldSecond.Guid = 2; newSecond.Guid = 2;
        oldSecond.Stack.Amount = 4; newSecond.Stack.Amount = 8;
        chunk.ChangedItems.Add(first);
        chunk.ChangedItems.Add(oldSecond);
        var oldRecords = new List<FrozenEcologyChunkSaveRecord>();
        using (var pending = SaveDataMgr.FreezeEcologyChunks(source, oldRecords).GetEnumerator())
        {
            if (!pending.MoveNext() || !pending.MoveNext()) return false;
            chunk.CaptureChangedItem(newSecond);
            List<FrozenEcologyChunkSaveRecord> immediate = FreezeEcologyForCacheProbe(source, out _);
            while (pending.MoveNext()) { }
            List<FrozenEcologyChunkSaveRecord> final = FreezeEcologyForCacheProbe(source, out _);
            return MemoryPackSerializer.Deserialize<ItemData>(oldRecords[0].ChangedItems[1]).Stack.Amount == 4 &&
                MemoryPackSerializer.Deserialize<ItemData>(immediate[0].ChangedItems[1]).Stack.Amount == 8 &&
                MemoryPackSerializer.Deserialize<ItemData>(final[0].ChangedItems[1]).Stack.Amount == 8;
        }
    }

    private static bool ProbeEcologyPlayerCapturePolicy()
    {
        var source = new GameSaveData();
        var world = new PlanetData();
        source.PlanetData_Dict.Add("probe:player-world", world);
        world.Ecology.GetOrCreateChunk(0, 0).ChangedItems.Add(new Data_Player
        {
            IDName = "probe:player", Guid = 42, Stack = new ItemStack { Amount = 1 }, ItemSpecialData = "private"
        });
        List<FrozenEcologyChunkSaveRecord> local = FreezeEcologyForCacheProbe(source, out _);
        List<FrozenEcologyChunkSaveRecord> network;
        using (ItemSnapshotSerialization.BeginPublicPlayerState(_ => "public"))
            network = FreezeEcologyForCacheProbe(source, out _);
        List<FrozenEcologyChunkSaveRecord> localAgain = FreezeEcologyForCacheProbe(source, out _);
        return MemoryPackSerializer.Deserialize<ItemData>(local[0].ChangedItems[0]).ItemSpecialData == "private" &&
            MemoryPackSerializer.Deserialize<ItemData>(network[0].ChangedItems[0]).ItemSpecialData == "public" &&
            MemoryPackSerializer.Deserialize<ItemData>(localAgain[0].ChangedItems[0]).ItemSpecialData == "private" &&
            !ReferenceEquals(local[0], network[0]) && !ReferenceEquals(network[0], localAgain[0]);
    }

    private static bool ProbeEcologyNestedPlayerCapturePolicy(JObject args)
    {
        var source = new GameSaveData();
        var world = new PlanetData();
        source.PlanetData_Dict.Add("probe:nested-player", world);
        var player = new Data_Player
        {
            IDName = "probe:nested-player", Guid = 51, Stack = new ItemStack { Amount = 1 }, ItemSpecialData = "private"
        };
        Data_GeneralItem secret = ServiceItem(args);
        secret.Guid = 52;
        player._inventoryData.Add("private", new Inventory_Data(new List<ItemSlot> { new(0) { itemData = secret } }, "private"));
        Data_GeneralItem carrier = ServiceItem(args);
        carrier.ModuleDataDic.Add("cargo", new Inventory_ModuleData
        {
            StableName = "cargo", ModuleId = "inventory",
            Data = new Dictionary<string, Inventory_Data>
            {
                ["cargo"] = new(new List<ItemSlot> { new(0) { itemData = player } }, "cargo")
            }
        });
        world.Ecology.GetOrCreateChunk(0, 0).ChangedItems.Add(carrier);
        var sessionItems = new Dictionary<ItemData, byte[]>();
        List<FrozenEcologyChunkSaveRecord> local = FreezeEcologyForCacheProbe(source, out _, sessionItems);
        bool localDictionaryUsed = sessionItems.Count == 1;
        List<FrozenEcologyChunkSaveRecord> firstPublic, secondPublic;
        bool publicDictionaryEmpty;
        using (ItemSnapshotSerialization.BeginPublicPlayerState(_ => "public-one"))
        {
            firstPublic = FreezeEcologyForCacheProbe(source, out _, sessionItems);
            using (ItemSnapshotSerialization.BeginPublicPlayerState(_ => "public-two"))
                secondPublic = FreezeEcologyForCacheProbe(source, out _, sessionItems);
            publicDictionaryEmpty = sessionItems.Count == 0;
        }
        List<FrozenEcologyChunkSaveRecord> localAgain = FreezeEcologyForCacheProbe(source, out _, sessionItems);
        Data_Player savedLocal = ReadNestedEcologyPlayer(local[0]);
        Data_Player savedPublicOne = ReadNestedEcologyPlayer(firstPublic[0]);
        Data_Player savedPublicTwo = ReadNestedEcologyPlayer(secondPublic[0]);
        Data_Player savedLocalAgain = ReadNestedEcologyPlayer(localAgain[0]);
        return localDictionaryUsed && publicDictionaryEmpty &&
            savedLocal.ItemSpecialData == "private" && savedLocal._inventoryData.Count == 1 &&
            savedPublicOne.ItemSpecialData == "public-one" && savedPublicOne._inventoryData.Count == 0 &&
            savedPublicTwo.ItemSpecialData == "public-two" && savedPublicTwo._inventoryData.Count == 0 &&
            savedLocalAgain.ItemSpecialData == "private" && savedLocalAgain._inventoryData.Count == 1 &&
            !ReferenceEquals(firstPublic[0], secondPublic[0]) && ReferenceEquals(local[0], localAgain[0]);
    }

    private static Data_Player ReadNestedEcologyPlayer(FrozenEcologyChunkSaveRecord record)
    {
        ItemData carrier = MemoryPackSerializer.Deserialize<ItemData>(record.ChangedItems[0]);
        return (Data_Player)((Inventory_ModuleData)carrier.ModuleDataDic["cargo"]).Data["cargo"].itemSlots[0].itemData;
    }

    private static bool ProbeEcologyFrozenCodecRevision(JObject args)
    {
        var source = new GameSaveData();
        var world = new PlanetData();
        source.PlanetData_Dict.Add("probe:codec", world);
        Data_GeneralItem item = ServiceItem(args);
        item.ModuleDataDic.Add("codec", new EcologySnapshotCacheProbeModule
        { StableName = "codec", ModuleId = "probe:ecology", Value = 10 });
        world.Ecology.GetOrCreateChunk(0, 0).ChangedItems.Add(item);
        var sessionItems = new Dictionary<ItemData, byte[]>();
        var firstCodec = new EcologySnapshotCacheProbeCodec(1);
        List<FrozenEcologyChunkSaveRecord> first;
        using (ModuleInstanceStateCodecs.Register(firstCodec))
        {
            first = FreezeEcologyForCacheProbe(source, out _, sessionItems);
            List<FrozenEcologyChunkSaveRecord> repeated = FreezeEcologyForCacheProbe(source, out _, sessionItems);
            if (!ReferenceEquals(first[0], repeated[0]) || firstCodec.Captures != 1) return false;
        }
        var nextCodec = new EcologySnapshotCacheProbeCodec(2);
        using (ModuleInstanceStateCodecs.Register(nextCodec))
        {
            List<FrozenEcologyChunkSaveRecord> updated = FreezeEcologyForCacheProbe(source, out _, sessionItems);
            ItemData restored = MemoryPackSerializer.Deserialize<ItemData>(updated[0].ChangedItems[0]);
            List<FrozenEcologyChunkSaveRecord> repeated = FreezeEcologyForCacheProbe(source, out _, sessionItems);
            return ReferenceEquals(first[0].ChunkData, updated[0].ChunkData) &&
                !ReferenceEquals(first[0].ChangedItems[0], updated[0].ChangedItems[0]) &&
                ((EcologySnapshotCacheProbeModule)restored.ModuleDataDic["codec"]).Value == 12 &&
                ReferenceEquals(updated[0], repeated[0]) && nextCodec.Captures == 1;
        }
    }

    #endregion
}

#region 生态缓存编解码器测试替身

internal sealed class EcologySnapshotCacheProbeModule : ModuleData { public byte Value; }

internal sealed class EcologySnapshotCacheProbeCodec : IModuleInstanceStateCodec
{
    private readonly byte increment;
    public int Captures;
    public EcologySnapshotCacheProbeCodec(byte increment) => this.increment = increment;
    public string Id => "probe.ecology-cache.revision";
    public Type DataType => typeof(EcologySnapshotCacheProbeModule);
    public ModuleData CreateData() => new EcologySnapshotCacheProbeModule();
    public byte[] Capture(ModuleData data, bool publicState)
    {
        Captures++;
        return new[] { (byte)(((EcologySnapshotCacheProbeModule)data).Value + increment) };
    }
    public void Restore(ModuleData data, byte[] payload) => ((EcologySnapshotCacheProbeModule)data).Value = payload[0];
}

#endregion
