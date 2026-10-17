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

        // 玩家、机器与掉落物之间会转移真实物品，必须同帧冻结，避免重复或丢失。
        byte[] machines = MachineWorld.CaptureArchive(capturedSave);
        FlatWorld.Spaceflight.SpaceSession.Capture(capturedSave);
        byte[] coreSaveData = SerializeCoreDataWithoutChunks(capturedSave);
        byte[] droppedItems = DroppedItemService.CaptureArchive(capturedSave);
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
                coreSaveData, chunkRecords, machines, droppedItems, modMetadata);
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
        byte[] modMetadata)
    {
        using var encodeSample = SaveBackgroundEncodePoint.Measure();
        byte[] corePayload = BuildCompactSaveEnvelopePayload(
            coreSaveData, chunkRecords, mechanicalNetworks, droppedItems);
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

    private static byte[] BuildCompactSaveEnvelopePayload(
        byte[] coreSaveData,
        List<byte[]> chunkRecords,
        byte[] mechanicalNetworks,
        byte[] droppedItems)
    {
        if (coreSaveData == null)
            throw new InvalidDataException("差异存档的核心数据为空");

        CompactSaveEnvelope envelope = new()
        {
            Version = CompactSaveVersion,
            CoreSaveData = coreSaveData,
            ChunkRecords = chunkRecords ?? new List<byte[]>(),
            DroppedItems = droppedItems,
            MechanicalNetworks = mechanicalNetworks
        };
        return AddSaveHeader(MemoryPackSerializer.Serialize(envelope), CompactSaveMagic);
    }

    private static byte[] AddSaveHeader(byte[] body, byte[] magic)
    {
        byte[] payload = new byte[magic.Length + body.Length];
        Buffer.BlockCopy(magic, 0, payload, 0, magic.Length);
        Buffer.BlockCopy(body, 0, payload, magic.Length, body.Length);
        return payload;
    }

    #endregion
}
