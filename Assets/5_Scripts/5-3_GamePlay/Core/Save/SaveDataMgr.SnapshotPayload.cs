using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MemoryPack;
using UnityEngine;

/// <summary>存档统一编解码、独立载荷冻结与后台封装。</summary>
public partial class SaveDataMgr
{
    #region 存档封装格式
    private const int CompactSaveVersion = 37; // 新增区块火场稀疏状态载荷。
    private const int ModdedSaveVersion = 15;
    // 实例状态采用当前布局，先检查封装头，再解析嵌套 MemoryPack 数据。
    private static readonly byte[] ModdedSaveMagic = { (byte)'F', (byte)'W', (byte)'D', (byte)'6' };
    #endregion

    #region 快照编解码与实例克隆
    private byte[] BuildCompactSavePayload(GameSaveData saveData)
    {
        using var encodeSample = SaveEncodePoint.Measure();
        ModRuntimeManager modRuntime = ModRuntimeManager.Instance;
        if (modRuntime == null || !modRuntime.IsReady)
            throw new InvalidOperationException("MOD 框架尚未完成加载，无法保存世界");

        CompactSaveEnvelope corePayload = BuildCoreCompactSavePayload(saveData);
        ModdedSaveEnvelope moddedEnvelope = new ModdedSaveEnvelope
        {
            Version = ModdedSaveVersion,
            CoreSavePayload = corePayload,
            ModMetadata = MemoryPackSerializer.Serialize(modRuntime.CaptureSaveMetadata())
        };

        byte[] body = MemoryPackSerializer.Serialize(moddedEnvelope);
        byte[] payload = new byte[ModdedSaveMagic.Length + body.Length];
        Buffer.BlockCopy(ModdedSaveMagic, 0, payload, 0, ModdedSaveMagic.Length);
        Buffer.BlockCopy(body, 0, payload, ModdedSaveMagic.Length, body.Length);
        SaveEncodePoint.AddWork(payload.LongLength);
        SaveEncodePoint.SetGauge(payload.LongLength);
        return payload;
    }

    private CompactSaveEnvelope BuildCoreCompactSavePayload(GameSaveData saveData)
    {
        using var machineCapture = MachineWorld.BeginSaveSnapshotCapture();
        MonsterSpawnerManager.CaptureAll(saveData);

        // 先捕获所有移动作用域，再序列化核心数据，避免船体和设备库存落在不同时间点。
        byte[] machines, coreSaveData, droppedItems;
        using (SaveCoreCapturePoint.Measure())
        {
            using (SaveMachinesCapturePoint.Measure()) machines = MachineWorld.CaptureArchive(saveData);
            using (SaveSpaceCapturePoint.Measure()) FlatWorld.Spaceflight.SpaceSession.Capture(saveData);
            coreSaveData = SerializeCoreDataWithoutChunks(saveData);
            using (SaveDropsCapturePoint.Measure()) droppedItems = DroppedItemService.CaptureArchive(saveData);
            SaveCoreCapturePoint.AddWork((machines?.LongLength ?? 0) + (coreSaveData?.LongLength ?? 0) + (droppedItems?.LongLength ?? 0));
        }
        List<ChunkSaveRecord> records = new();

        foreach (KeyValuePair<string, ChunkSaveRecord> pair in chunkDeltas)
        {
            if (pair.Value == null || !pair.Value.HasChanges)
                continue;

            records.Add(pair.Value);
        }

        records.Sort(CompareChunkSaveRecords);
        List<byte[]> chunkRecords = new(records.Count);
        using (SaveChunkEncodePoint.Measure(records.Count))
        {
            foreach (ChunkSaveRecord record in records)
                chunkRecords.Add(MemoryPackSerializer.Serialize(record));
            SaveChunkEncodePoint.SetGauge(records.Count);
        }

        List<FrozenEcologyChunkSaveRecord> ecologyRecords = new();
        foreach (int work in FreezeEcologyChunks(saveData, ecologyRecords)) { }
        return BuildCompactSaveEnvelope(coreSaveData, chunkRecords, machines, droppedItems, ecologyRecords);
    }

    internal static byte[] SerializeCoreDataWithoutChunks(GameSaveData saveData)
    {
        using var coreSample = SaveCoreEncodePoint.Measure();
        List<(PlanetData planet, Dictionary<string, MapSave> maps, List<EcologyChunkSaveData> ecology, List<RegionalWeatherRecord> weather)> backups = new();
        if (saveData.PlanetData_Dict != null)
        {
            foreach (PlanetData planet in saveData.PlanetData_Dict.Values)
            {
                if (planet == null)
                    continue;

                backups.Add((planet, planet.MapData_Dict, planet.Ecology?.Chunks, planet.RegionalWeatherStates));
                planet.MapData_Dict = new Dictionary<string, MapSave>();
                planet.RegionalWeatherStates = new List<RegionalWeatherRecord>();
                if (planet.Ecology != null)
                    planet.Ecology.Chunks = null;
            }
        }

        try
        {
            byte[] payload = MemoryPackSerializer.Serialize(saveData);
            SaveCoreEncodePoint.AddWork(payload.LongLength);
            SaveCoreEncodePoint.SetGauge(payload.LongLength);
            return payload;
        }
        finally
        {
            for (int i = 0; i < backups.Count; i++)
            {
                backups[i].planet.MapData_Dict = backups[i].maps;
                backups[i].planet.RegionalWeatherStates = backups[i].weather;
                if (backups[i].planet.Ecology != null)
                    backups[i].planet.Ecology.Chunks = backups[i].ecology;
            }
        }
    }

    private GameSaveData DeserializeSavePayload(byte[] payload)
    {
        ModdedSaveEnvelope envelope = ReadSaveEnvelope(payload);
        ModSaveMetadata metadata = envelope.ModMetadata?.Length > 0
            ? MemoryPackSerializer.Deserialize<ModSaveMetadata>(envelope.ModMetadata) : null;
        var restoredChunkDeltas = new Dictionary<string, ChunkSaveRecord>();
        GameSaveData saveData = DeserializeCoreSavePayload(envelope.CoreSavePayload, restoredChunkDeltas);
        ValidateAndRestoreModMetadata(metadata);
        // 整份文件验证完才替换差量，失败候选不能清空或留下半份区块记录。
        ResetChunkDifferenceState();
        foreach (var entry in restoredChunkDeltas)
            chunkDeltas.Add(entry.Key, entry.Value);
        return saveData;
    }

    private static ModdedSaveEnvelope ReadSaveEnvelope(byte[] payload)
    {
        if (!HasSaveHeader(payload, ModdedSaveMagic))
        {
            throw new SaveVersionIncompatibleException(
                "存档封装与当前 Item 实例状态格式不兼容，请创建新世界。不会迁移、覆盖或删除该存档。");
        }
        ModdedSaveEnvelope envelope = MemoryPackSerializer.Deserialize<ModdedSaveEnvelope>(
            payload.AsSpan(ModdedSaveMagic.Length));
        if (envelope?.CoreSavePayload == null)
            throw new InvalidDataException("MOD 存档封装已损坏");
        EnsureCurrentSaveVersion("MOD 存档", envelope.Version, ModdedSaveVersion);
        return envelope;
    }

    private GameSaveData DeserializeCoreSavePayload(CompactSaveEnvelope envelope,
        Dictionary<string, ChunkSaveRecord> restoredChunkDeltas)
    {
        if (envelope == null || envelope.CoreSaveData == null)
            throw new InvalidDataException("差异存档封装已损坏");
        EnsureCurrentSaveVersion("差异存档", envelope.Version, CompactSaveVersion);

        GameSaveData saveData = MemoryPackSerializer.Deserialize<GameSaveData>(envelope.CoreSaveData);
        if (saveData == null)
            throw new InvalidDataException("差异存档的核心数据为空");

        if (!string.IsNullOrWhiteSpace(saveData.SpaceStateJson))
            _ = DeserializeSpaceState(saveData.SpaceStateJson);

        RestoreEcologyChunks(saveData, envelope.EcologyRecords);
        DroppedItemService.RestoreArchive(saveData, envelope.DroppedItems);
        MachineWorld.RestoreArchive(saveData, envelope.MechanicalNetworks);

        if (envelope.ChunkRecords == null)
            return saveData;

        for (int i = 0; i < envelope.ChunkRecords.Count; i++)
        {
            byte[] chunkPayload = envelope.ChunkRecords[i];
            if (chunkPayload == null || chunkPayload.Length == 0)
                throw new InvalidDataException("差异存档的区块快照为空");
            ChunkSaveRecord record = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(chunkPayload);
            if (record == null || string.IsNullOrEmpty(record.PlanetName) || string.IsNullOrEmpty(record.ChunkName))
                continue;

            restoredChunkDeltas[BuildChunkKey(record.PlanetName, record.ChunkName)] = record;
        }

        return saveData;
    }

    private static void ValidateAndRestoreModMetadata(ModSaveMetadata metadata)
    {
        ModRuntimeManager modRuntime = ModRuntimeManager.Instance;
        if (modRuntime == null || !modRuntime.IsReady)
            throw new InvalidOperationException("MOD 框架尚未完成加载，不能读取存档");

        if (!modRuntime.ValidateSaveMetadata(metadata, out string error))
            throw new InvalidDataException($"存档 MOD 环境不兼容：{error}");

        modRuntime.RestoreSaveMetadata(metadata);
    }

    private static bool HasSaveHeader(byte[] payload, byte[] magic)
    {
        if (payload == null || magic == null || payload.Length <= magic.Length)
            return false;

        for (int i = 0; i < magic.Length; i++)
        {
            if (payload[i] != magic[i])
                return false;
        }

        return true;
    }

    #region 星区快照的严格格式边界
    // 先验证真实 JSON 版本，字段默认值不能把缺少版本的档案伪装成新格式。
    public static FlatWorld.Spaceflight.SpaceSessionState DeserializeSpaceState(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("太空存档内容为空。");
        Newtonsoft.Json.Linq.JObject document = Newtonsoft.Json.Linq.JObject.Parse(json);
        Newtonsoft.Json.Linq.JToken version = document[nameof(FlatWorld.Spaceflight.SpaceSessionState.Version)];
        if (version?.Type != Newtonsoft.Json.Linq.JTokenType.Integer)
            throw new InvalidDataException("太空存档缺少有效的格式版本。");
        EnsureCurrentSaveVersion("太空星区存档", version.ToObject<int>(), FlatWorld.Spaceflight.SpaceSessionState.CurrentVersion);
        var state = document.ToObject<FlatWorld.Spaceflight.SpaceSessionState>();
        if (state == null || state.Sectors == null || state.Sectors.Count == 0 || state.Ships == null ||
            state.Flights == null || state.Devices == null || state.Passengers == null || state.Navigation == null ||
            state.Docking == null || state.LooseItems == null || state.GroundAtmospheres == null ||
            !FlatWorld.Spaceflight.SpaceVector2.Finite(state.SimulationSeconds) || state.SimulationSeconds < 0d)
            throw new InvalidDataException("太空星区存档的权威状态不完整。");
        var sectors = new HashSet<FlatWorld.Spaceflight.SpaceSectorId>();
        foreach (FlatWorld.Spaceflight.UniverseState sector in state.Sectors)
            if (sector == null || !sectors.Add(sector.Sector) || sector.Bodies == null)
                throw new InvalidDataException("太空存档存在无效或重复的星区身份。");
        return state;
    }
    #endregion

    /// <summary>只读取当前存档格式；开发阶段不保留旧版本迁移或自动升级路径。</summary>
    private static void EnsureCurrentSaveVersion(string label, int savedVersion, int currentVersion)
    {
        if (savedVersion <= 0)
            throw new InvalidDataException($"{label}版本无效：{savedVersion}");

        if (savedVersion != currentVersion)
            throw new SaveVersionIncompatibleException(
                $"{label}版本不兼容：存档={savedVersion}，当前={currentVersion}。请创建新世界。");
    }

    private static ItemData CloneItemData(ItemData itemData)
    {
        if (itemData == null)
            return null;

        byte[] bytes = MemoryPackSerializer.Serialize<ItemData>(itemData);
        return MemoryPackSerializer.Deserialize<ItemData>(bytes);
    }

    /// <summary>恢复运行时实体前先复制存档状态，再用当前物品定义刷新静态配置和模块组合。</summary>
    private static ItemData CloneAndRebaseItemData(ItemData itemData)
    {
        ItemData cloned = CloneItemData(itemData);
        GameRes gameRes = GameRes.Instance;
        return gameRes == null
            ? cloned
            : ItemDefinitionRuntime.RebasePersistedData(gameRes, cloned);
    }
    #endregion

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
        using IDisposable weatherCapture = WeatherMgr.BeginAllSurfaceSaveCapture();
        {
            IEnumerator weatherRoutine = WeatherMgr.FlushAllSurfaceWorkForAutoSave(budget);
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

        MonsterSpawnerManager.CaptureAll(capturedSave);
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

        IEnumerator machinePreparation = MachineWorld.PrepareSaveSnapshotCache(capturedSave, budget);
        while (machinePreparation.MoveNext())
        {
            yield return machinePreparation.Current;
            if (!IsCapturedSaveCurrent(capturedSave, fullPath, revision))
            {
                onQueued?.Invoke(Task.FromResult(false));
                yield break;
            }
        }

        // 机器准备与分帧期间火场仍可能变化；在冻结区块字节前再提交一次稀疏火场差量。
        if (!CaptureRuntimeFireStatesForSave())
            throw new InvalidOperationException("自动保存的火场状态采集失败。");

        // 地形先逐条冻结，联合点之后只组合这些字节，不再触碰运行态。
        var frozenChunks = new Dictionary<(string world, Vector2Int origin), SqliteChunkSnapshot>();
        var frozenTerrainRevisions = new Dictionary<string, long>();
        var frozenTerrainAddresses = new Dictionary<string, (string world, Vector2Int origin)>();
        foreach (string key in new List<string>(chunkDeltas.Keys))
        {
            if (chunkDeltas.TryGetValue(key, out ChunkSaveRecord record) && record != null)
            {
                frozenChunks[(record.PlanetName, record.ChunkPosition)] = new SqliteChunkSnapshot
                    { Terrain = record.HasChanges ? MemoryPackSerializer.Serialize(record) : null };
                frozenTerrainAddresses[key] = (record.PlanetName, record.ChunkPosition);
                frozenTerrainRevisions[key] = chunkDifferenceRevisions.TryGetValue(key, out long version) ? version : 0;
            }
            if (!budget.ShouldYield()) continue;
            yield return budget.WaitForNextSlice();
            if (!IsCapturedSaveCurrent(capturedSave, fullPath, revision))
            { onQueued?.Invoke(Task.FromResult(false)); yield break; }
        }

        // 玩家、机器与掉落物之间会转移真实物品，必须同帧冻结，避免重复或丢失。
        if (captureCoreState == null) throw new ArgumentNullException(nameof(captureCoreState));
        if (!CaptureRuntimeTerrainStates()) throw new InvalidOperationException("最终保存点的地形采集失败。");
        CaptureRuntimeBuildingStates();
        if (!CaptureRuntimeAiEntityStates()) throw new InvalidOperationException("最终保存点的实体采集失败。");
        // 采收会在让帧期间改动删除记录，最终点按现有成员重组，只重捕获新增或替换的独立快照。
        ecologyRecords.Clear();
        using (SaveEcologyFinalizePoint.Measure())
            foreach (int work in FreezeEcologyChunks(capturedSave, ecologyRecords, ecologyItems)) { }
        ecologyItems.Clear();
        long archiveRevision = ReserveArchiveSnapshotRevision(fullPath);
        byte[] machines, droppedItems;
        List<FlatWorld.Persistence.SaveArchiveRecord> sqliteRecords;
        using (MachineWorld.BeginSaveSnapshotCapture())
        {
            using (SavePlayerCapturePoint.Measure()) captureCoreState();
            using (SaveMachinesCapturePoint.Measure()) machines = MachineWorld.CaptureArchive(capturedSave);
            using (SaveSpaceCapturePoint.Measure()) FlatWorld.Spaceflight.SpaceSession.Capture(capturedSave);
            using (SaveDropsCapturePoint.Measure()) droppedItems = DroppedItemService.CaptureArchive(capturedSave);
            sqliteRecords = CaptureSqliteCoreRecords(capturedSave, machines, droppedItems);
        }
        // 让帧期间发生的采矿/建造必须在联合点补冻，保持地形与最终库存、掉落一致。
        foreach (var changed in new List<KeyValuePair<string, long>>(chunkDifferenceRevisions))
        {
            if (frozenTerrainRevisions.TryGetValue(changed.Key, out long version) && version == changed.Value) continue;
            if (chunkDeltas.TryGetValue(changed.Key, out var current) && current != null)
            {
                var address = (current.PlanetName, current.ChunkPosition);
                if (!frozenChunks.TryGetValue(address, out var chunk)) frozenChunks.Add(address, chunk = new SqliteChunkSnapshot());
                chunk.Terrain = current.HasChanges ? MemoryPackSerializer.Serialize(current) : null;
            }
            else if (frozenTerrainAddresses.TryGetValue(changed.Key, out var address)) frozenChunks[address].Terrain = null;
        }
        foreach (FrozenEcologyChunkSaveRecord ecology in ecologyRecords)
        {
            var metadata = MemoryPackSerializer.Deserialize<EcologyChunkSaveData>(ecology.ChunkData);
            var key = (ecology.WorldKey, new Vector2Int(metadata.ChunkX, metadata.ChunkY));
            if (!frozenChunks.TryGetValue(key, out SqliteChunkSnapshot chunk))
                frozenChunks.Add(key, chunk = new SqliteChunkSnapshot());
            chunk.Ecology = ecology;
        }
        foreach (var state in persistedChunkReads.Values)
            if (state.Kind == "chunk" && state.Ready && !state.Released && !frozenChunks.ContainsKey((state.World, state.Origin)))
                frozenChunks.Add((state.World, state.Origin), new SqliteChunkSnapshot());
        foreach (var pair in frozenChunks)
        {
            sqliteRecords.Add(FreezeSqliteChunk(pair.Key.world, pair.Key.origin, pair.Value));
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

        Task<bool> writeTask = CommitSqliteRecordsAndReportAsync(fullPath, capturedSave, sqliteRecords, archiveRevision, revision);
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
        foreach (int work in FreezeEcologyChunksWithReusableSnapshots(saveData, destination, frozenItems))
            yield return work;
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
                chunk.ChangedItems.Add(MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(itemBytes)?.CreateColdData());
            }
            planet.Ecology.Chunks ??= new List<EcologyChunkSaveData>();
            planet.Ecology.Chunks.Add(chunk);
        }
    }

    #endregion
}

[MemoryPackable]
[Serializable]
public partial class ModdedSaveEnvelope
{
    public int Version;
    public CompactSaveEnvelope CoreSavePayload; // 单次遍历冻结字节，避免多层大数组封装复制。
    public byte[] ModMetadata;
}

public sealed class SaveVersionIncompatibleException : IOException
{
    public SaveVersionIncompatibleException(string message) : base(message)
    {
    }
}

[MemoryPackable]
[Serializable]
public partial class CompactSaveEnvelope
{
    public int Version;
    public byte[] CoreSaveData;
    public List<byte[]> ChunkRecords = new(); // 区块在主线程独立冻结，后台封装不再回调物品模块。
    public byte[] DroppedItems;
    public byte[] MechanicalNetworks; // 追加独立版本载荷，旧版本缺失时为空。
    public List<FrozenEcologyChunkSaveRecord> EcologyRecords = new();
}

[MemoryPackable(GenerateType.VersionTolerant)]
[Serializable]
public partial class FrozenEcologyChunkSaveRecord
{
    #region 独立冻结字节
    [MemoryPackOrder(0)] public string WorldKey;
    [MemoryPackOrder(1)] public byte[] ChunkData;
    [MemoryPackOrder(2)] public List<byte[]> ChangedItems = new();
    #endregion
}
