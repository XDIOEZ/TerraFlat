using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MemoryPack;

public partial class SaveDataMgr
{
    #region 自动保存独立载荷

    /// <summary>主线程按阶段冻结载荷，后台只组合字节并原子写盘。</summary>
    private IEnumerator QueueCapturedSaveWriteCoroutine(
        GameSaveData capturedSave,
        string fullPath,
        long revision,
        AutoSaveWorkBudget budget,
        Action captureCoreState,
        Action<Task<bool>> onQueued)
    {
        if (!IsCapturedSaveCurrent(capturedSave, fullPath, revision))
        {
            onQueued?.Invoke(Task.FromResult(false));
            yield break;
        }

        // 排空已有积分后再冻结天气时钟和区块字节，期间只暂缓新的权威天气步。
        using IDisposable weatherCapture = WeatherMgr.ExistingInstance?.BeginSurfaceSaveCapture();
        if (WeatherMgr.ExistingInstance != null)
        {
            IEnumerator weatherRoutine = WeatherMgr.ExistingInstance.FlushSurfaceWorkForAutoSave(budget);
            while (weatherRoutine.MoveNext())
            {
                yield return weatherRoutine.Current;
                if (!IsCapturedSaveCurrent(capturedSave, fullPath, revision))
                {
                    onQueued?.Invoke(Task.FromResult(false));
                    yield break;
                }
            }
        }

        MonsterSpawnerManager spawnerManager = FindObjectOfType<MonsterSpawnerManager>();
        spawnerManager?.CaptureSaveData(capturedSave);
        yield return budget.WaitForNextSlice();
        if (!IsCapturedSaveCurrent(capturedSave, fullPath, revision))
        {
            onQueued?.Invoke(Task.FromResult(false));
            yield break;
        }

        List<FrozenEcologyChunkSaveRecord> ecologyRecords = new();
        var ecologyItems = new Dictionary<ItemData, byte[]>(EcologyItemReferenceComparer.Instance);
        foreach (int work in FreezeEcologyChunks(capturedSave, ecologyRecords, ecologyItems))
        {
            if (!budget.ShouldYield())
                continue;
            yield return budget.WaitForNextSlice();
            if (!IsCapturedSaveCurrent(capturedSave, fullPath, revision))
            {
                onQueued?.Invoke(Task.FromResult(false));
                yield break;
            }
        }

        // 玩家、机器与掉落物之间会转移真实物品，必须同帧冻结，避免重复或丢失。
        if (captureCoreState == null) throw new ArgumentNullException(nameof(captureCoreState));
        // 采收会在让帧期间改动删除记录，最终点按现有成员重组，只重捕获新增或替换的独立快照。
        ecologyRecords.Clear();
        using (SaveEcologyFinalizePoint.Measure())
            foreach (int work in FreezeEcologyChunks(capturedSave, ecologyRecords, ecologyItems)) { }
        ecologyItems.Clear();
        using (SavePlayerCapturePoint.Measure()) captureCoreState();
        byte[] machines;
        using (SaveMachinesCapturePoint.Measure()) machines = MachineWorld.CaptureArchive(capturedSave);
        using (SaveSpaceCapturePoint.Measure()) FlatWorld.Spaceflight.SpaceSession.Capture(capturedSave);
        byte[] coreSaveData = SerializeCoreDataWithoutChunks(capturedSave);
        byte[] droppedItems;
        using (SaveDropsCapturePoint.Measure()) droppedItems = DroppedItemService.CaptureArchive(capturedSave);
        yield return budget.WaitForNextSlice();
        if (!IsCapturedSaveCurrent(capturedSave, fullPath, revision))
        {
            onQueued?.Invoke(Task.FromResult(false));
            yield break;
        }

        ModRuntimeManager modRuntime = ModRuntimeManager.Instance;
        if (modRuntime == null || !modRuntime.IsReady)
            throw new InvalidOperationException("MOD 框架尚未完成加载，无法保存世界");
        byte[] modMetadata = MemoryPackSerializer.Serialize(modRuntime.CaptureSaveMetadata());
        yield return budget.WaitForNextSlice();
        if (!IsCapturedSaveCurrent(capturedSave, fullPath, revision))
        {
            onQueued?.Invoke(Task.FromResult(false));
            yield break;
        }

        List<string> recordKeys = new(chunkDeltas.Keys);
        recordKeys.Sort(CompareChunkSaveKeys);
        List<byte[]> chunkRecords = new(recordKeys.Count);
        for (int i = 0; i < recordKeys.Count; i++)
        {
            if (chunkDeltas.TryGetValue(recordKeys[i], out ChunkSaveRecord record) &&
                record != null && record.HasChanges)
            {
                // 每条记录立即成为独立字节，后续帧的区块变化不会改写已冻结载荷。
                chunkRecords.Add(MemoryPackSerializer.Serialize(record));
            }

            if (!budget.ShouldYield())
                continue;

            yield return budget.WaitForNextSlice();
            if (!IsCapturedSaveCurrent(capturedSave, fullPath, revision))
            {
                onQueued?.Invoke(Task.FromResult(false));
                yield break;
            }
        }

        if (!IsCapturedSaveCurrent(capturedSave, fullPath, revision))
        {
            onQueued?.Invoke(Task.FromResult(false));
            yield break;
        }

        Task<bool> writeTask = Task.Run(() =>
        {
            if (!IsSaveRevisionCurrent(fullPath, revision))
                return false;
            byte[] payload = BuildCapturedSavePayload(
                coreSaveData, chunkRecords, machines, droppedItems, modMetadata, ecologyRecords);
            return WriteSaveAtomically(fullPath, payload, revision, discardIfSuperseded: true);
        });
        onQueued?.Invoke(writeTask);
    }

    private bool IsCapturedSaveCurrent(GameSaveData capturedSave, string fullPath, long revision) =>
        ReferenceEquals(SaveData, capturedSave) && IsSaveRevisionCurrent(fullPath, revision);

    private int CompareChunkSaveKeys(string left, string right)
    {
        chunkDeltas.TryGetValue(left, out ChunkSaveRecord leftRecord);
        chunkDeltas.TryGetValue(right, out ChunkSaveRecord rightRecord);
        int recordCompare = CompareChunkSaveRecords(leftRecord, rightRecord);
        return recordCompare != 0 ? recordCompare : string.CompareOrdinal(left, right);
    }

    private static int CompareChunkSaveRecords(ChunkSaveRecord left, ChunkSaveRecord right)
    {
        int planetCompare = string.CompareOrdinal(left?.PlanetName, right?.PlanetName);
        return planetCompare != 0
            ? planetCompare
            : string.CompareOrdinal(left?.ChunkName, right?.ChunkName);
    }

    /// <summary>组合已冻结的独立字节载荷；调用方移交后不再修改这些数组和区块列表。</summary>
    public static byte[] BuildCapturedSavePayload(
        byte[] coreSaveData,
        List<byte[]> chunkRecords,
        byte[] mechanicalNetworks,
        byte[] droppedItems,
        byte[] modMetadata,
        List<FrozenEcologyChunkSaveRecord> ecologyRecords)
    {
        using var encodeSample = SaveBackgroundEncodePoint.Measure();
        CompactSaveEnvelope corePayload = BuildCompactSaveEnvelope(
            coreSaveData, chunkRecords, mechanicalNetworks, droppedItems, ecologyRecords);
        ModdedSaveEnvelope envelope = new()
        {
            Version = ModdedSaveVersion,
            CoreSavePayload = corePayload,
            ModMetadata = modMetadata
        };
        byte[] payload = AddSaveHeader(MemoryPackSerializer.Serialize(envelope), ModdedSaveMagic);
        encodeSample.AddWork(payload.LongLength);
        encodeSample.SetGauge(payload.LongLength);
        return payload;
    }

    private static CompactSaveEnvelope BuildCompactSaveEnvelope(
        byte[] coreSaveData,
        List<byte[]> chunkRecords,
        byte[] mechanicalNetworks,
        byte[] droppedItems,
        List<FrozenEcologyChunkSaveRecord> ecologyRecords)
    {
        if (coreSaveData == null)
            throw new InvalidDataException("差异存档的核心数据为空");

        CompactSaveEnvelope envelope = new()
        {
            Version = CompactSaveVersion,
            CoreSaveData = coreSaveData,
            ChunkRecords = chunkRecords ?? new List<byte[]>(),
            DroppedItems = droppedItems,
            MechanicalNetworks = mechanicalNetworks,
            EcologyRecords = ecologyRecords ?? new List<FrozenEcologyChunkSaveRecord>()
        };
        return envelope;
    }

    private static byte[] AddSaveHeader(byte[] body, byte[] magic)
    {
        byte[] payload = new byte[magic.Length + body.Length];
        Buffer.BlockCopy(magic, 0, payload, 0, magic.Length);
        Buffer.BlockCopy(body, 0, payload, magic.Length, body.Length);
        return payload;
    }

    #endregion

    #region 生态区块独立冻结

    // 每个物品单独冻结，预算耗尽后可以让帧，后台只收到不可变字节。
    internal static IEnumerable<int> FreezeEcologyChunks(
        GameSaveData saveData, List<FrozenEcologyChunkSaveRecord> destination,
        Dictionary<ItemData, byte[]> frozenItems = null)
    {
        if (saveData.PlanetData_Dict == null)
            yield break;
        List<string> worldKeys = new(saveData.PlanetData_Dict.Keys);
        worldKeys.Sort(StringComparer.Ordinal);
        foreach (string worldKey in worldKeys)
        {
            List<EcologyChunkSaveData> chunks = saveData.PlanetData_Dict[worldKey]?.Ecology?.Chunks;
            if (chunks == null)
                continue;
            foreach (EcologyChunkSaveData chunk in chunks.ToArray())
            {
                if (chunk == null)
                    throw new InvalidDataException("生态存档包含空区块。");
                List<ItemData> items = chunk.ChangedItems;
                ItemData[] itemSources = items?.ToArray() ?? Array.Empty<ItemData>();
                var record = new FrozenEcologyChunkSaveRecord { WorldKey = worldKey };
                try
                {
                    chunk.ChangedItems = null;
                    using (SaveEcologyMetadataPoint.Measure())
                        record.ChunkData = MemoryPackSerializer.Serialize(chunk);
                }
                finally { chunk.ChangedItems = items; }
                destination.Add(record);
                yield return 1;
                foreach (ItemData item in itemSources)
                {
                    byte[] bytes;
                    if (item == null || frozenItems == null || !frozenItems.TryGetValue(item, out bytes))
                    {
                        using (SaveEcologyItemPoint.Measure())
                            bytes = MemoryPackSerializer.Serialize<ItemData>(item);
                        if (item != null && frozenItems != null)
                            frozenItems[item] = bytes;
                    }
                    record.ChangedItems.Add(bytes);
                    yield return 1;
                }
            }
        }
    }

    private sealed class EcologyItemReferenceComparer : IEqualityComparer<ItemData>
    {
        public static readonly EcologyItemReferenceComparer Instance = new();
        public bool Equals(ItemData left, ItemData right) => ReferenceEquals(left, right);
        public int GetHashCode(ItemData value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }

    private static void RestoreEcologyChunks(GameSaveData saveData, List<FrozenEcologyChunkSaveRecord> records)
    {
        if (records == null)
            throw new InvalidDataException("存档缺少独立生态区块载荷。");
        foreach (FrozenEcologyChunkSaveRecord record in records)
        {
            if (record == null || string.IsNullOrEmpty(record.WorldKey) || record.ChunkData == null ||
                record.ChangedItems == null || saveData.PlanetData_Dict == null ||
                !saveData.PlanetData_Dict.TryGetValue(record.WorldKey, out PlanetData planet) || planet?.Ecology == null)
                throw new InvalidDataException("生态区块身份或独立载荷无效。");
            EcologyChunkSaveData chunk = MemoryPackSerializer.Deserialize<EcologyChunkSaveData>(record.ChunkData);
            if (chunk == null)
                throw new InvalidDataException("生态区块快照为空。");
            chunk.ChangedItems = new List<ItemData>(record.ChangedItems.Count);
            foreach (byte[] itemBytes in record.ChangedItems)
            {
                if (itemBytes == null || itemBytes.Length == 0)
                    throw new InvalidDataException("生态物品快照为空。");
                chunk.ChangedItems.Add(MemoryPackSerializer.Deserialize<ItemData>(itemBytes));
            }
            planet.Ecology.Chunks ??= new List<EcologyChunkSaveData>();
            planet.Ecology.Chunks.Add(chunk);
        }
    }

    #endregion
}

[MemoryPackable]
[Serializable]
public partial class FrozenEcologyChunkSaveRecord
{
    #region 独立冻结字节
    public string WorldKey;
    public byte[] ChunkData;
    public List<byte[]> ChangedItems = new();
    #endregion
}
