using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.Persistence;
using MemoryPack;
using UnityEngine;

public partial class SaveDataMgr
{
    #region SQLite 会话与独立记录
    private ISaveArchiveStore archiveStore;
    private string archivePath, archiveSession;
    private CancellationTokenSource archiveReadCancellation = new();
    private long archiveRecordRevision;
    private readonly Dictionary<string, PersistedChunkRead> persistedChunkReads = new();
    private readonly Dictionary<string, Task> persistedChunkStages = new();
    private readonly Dictionary<(string world, Vector2Int region), (long epoch, long revision)> networkWeatherRevisions = new();
    private const string ArchiveWorldRemovalKind = "$remove-world";
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> retiredArchiveWorlds = new();
    private long persistedChunkAccess;
    private int ArchiveCacheCount => Application.isMobilePlatform ? 256 : 1024;
    private long ArchiveCacheBytes => (Application.isMobilePlatform ? 8L : 32L) * 1024 * 1024;
    public string LastArchiveError { get; private set; }

    private sealed class PersistedChunkRead
    {
        public Task Task;
        public bool Ready, Released, Materialized;
        public long Access, Bytes;
        public long RuntimePinGeneration;
        public CancellationTokenRegistration RuntimePinCancellation;
        public CancellationTokenSource ReadCancellation;
        public int ReadWaiters;
        public SaveArchiveRecord StagedRecord, LoadedRecord;
        public string World, Kind;
        public Vector2Int Origin;
    }

    private void CloseSqliteArchive()
    {
        archiveReadCancellation.Cancel();
        archiveReadCancellation.Dispose();
        archiveReadCancellation = new CancellationTokenSource();
        archiveStore?.Dispose();
        archiveStore = null;
        archivePath = archiveSession = null;
        foreach (var state in persistedChunkReads.Values) state.RuntimePinCancellation.Dispose();
        persistedChunkReads.Clear();
        persistedChunkStages.Clear();
        networkWeatherRevisions.Clear();
        retiredArchiveWorlds.Clear();
        archiveRecordRevision = 0;
    }

    private void OpenSqliteArchive(string path, bool create)
    {
        path = Path.GetFullPath(path);
        if (archiveStore != null && string.Equals(archivePath, path, StringComparison.OrdinalIgnoreCase)) return;
        CloseSqliteArchive();
        archiveStore = new SqliteSaveArchiveStore(path, create);
        archivePath = path;
        archiveSession = Guid.NewGuid().ToString("N");
    }

    private static SaveArchiveRecord ArchiveRecord(string kind, string world, string key, byte[] bytes,
        int x = 0, int y = 0, bool deleted = false)
    {
        string schema = "flatworld." + (kind == "weather" ? "weather" : key.Split(':')[0]);
        return new SaveArchiveRecord(kind, world ?? "", key, x, y, schema, ArchiveSchemas.GetCurrentVersion(schema), bytes, deleted);
    }

    private List<SaveArchiveRecord> CaptureSqliteCoreRecords(GameSaveData source, byte[] machines, byte[] drops)
    {
        var records = new List<SaveArchiveRecord>();
        var planets = source.PlanetData_Dict;
        var players = source.PlayerData_Dict;
        string space = source.SpaceStateJson;
        // 核心记录剔除空间增长的数据，玩家和世界分别成为独立记录。
        try
        {
            source.PlanetData_Dict = new(); source.PlayerData_Dict = new(); source.SpaceStateJson = null;
            records.Add(ArchiveRecord("global", "", "core", SerializeCoreDataWithoutChunks(source)));
        }
        finally { source.PlanetData_Dict = planets; source.PlayerData_Dict = players; source.SpaceStateJson = space; }
        foreach (var pair in planets)
        {
            PlanetData planet = pair.Value;
            var maps = planet.MapData_Dict; var ecology = planet.Ecology?.Chunks; var weather = planet.RegionalWeatherStates;
            try
            {
                planet.MapData_Dict = new();
                planet.RegionalWeatherStates = new();
                if (planet.Ecology != null) planet.Ecology.Chunks = null;
                records.Add(ArchiveRecord("world", pair.Key, "planet", MemoryPackSerializer.Serialize(planet)));
            }
            finally { planet.MapData_Dict = maps; planet.RegionalWeatherStates = weather; if (planet.Ecology != null) planet.Ecology.Chunks = ecology; }
            foreach (var region in weather ?? new List<RegionalWeatherRecord>())
                records.Add(ArchiveRecord("weather", pair.Key, region.RegionX + ":" + region.RegionY, MemoryPackSerializer.Serialize(region), region.RegionX, region.RegionY));
        }
        foreach (var player in players)
            records.Add(ArchiveRecord("global", "", "player:" + player.Key,
                MemoryPackSerializer.Serialize(ItemInstanceSnapshot.Capture(player.Value))));
        ModRuntimeManager mods = ModRuntimeManager.Instance;
        if (mods == null || !mods.IsReady) throw new InvalidOperationException("MOD 框架尚未就绪。");
        records.Add(ArchiveRecord("global", "", "mods", MemoryPackSerializer.Serialize(mods.CaptureSaveMetadata())));
        AppendSqliteSpaceRecords(records, space);
        var machineArchive = machines?.Length > 0 ? MemoryPackSerializer.Deserialize<FrozenMachineArchivePayload>(machines) : null;
        if (machineArchive != null)
        {
            records.Add(ArchiveRecord("scope", "", "machine-metadata", machineArchive.Metadata));
            foreach (var pair in machineArchive.Worlds)
                foreach (byte[] item in pair.Value)
                {
                    var snapshot = MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(item);
                    records.Add(ArchiveRecord("entity", pair.Key, "machine:" + snapshot.Guid, item));
                }
        }
        var dropArchive = drops?.Length > 0 ? MemoryPackSerializer.Deserialize<DroppedItemArchive>(drops) : null;
        if (dropArchive != null)
            foreach (var pair in dropArchive.Worlds)
                foreach (var drop in pair.Value)
                    records.Add(ArchiveRecord("entity", pair.Key, "drop:" + drop.Guid, MemoryPackSerializer.Serialize(drop)));
        foreach (string world in retiredArchiveWorlds.Keys)
            records.Add(new SaveArchiveRecord(ArchiveWorldRemovalKind, world, "retired", 0, 0, "", 0, null, true));
        return records;
    }

    private SqliteChunkSnapshot CaptureSqliteChunk(string world, Vector2Int origin, GameSaveData source = null)
    {
        TryGetChunkDelta(world, origin, out ChunkSaveRecord terrain);
        EcologyChunkSaveData ecology = null;
        if ((source ?? SaveData)?.PlanetData_Dict?.TryGetValue(world, out PlanetData planet) == true)
            ecology = planet.Ecology?.Chunks?.Find(value => value != null && value.ChunkX == origin.x && value.ChunkY == origin.y);
        FrozenEcologyChunkSaveRecord frozen = null;
        if (ecology != null)
        {
            var metadata = new EcologyChunkSaveData { ChunkX = origin.x, ChunkY = origin.y,
                RemovedGuids = ecology.RemovedGuids, RenewalYears = ecology.RenewalYears, ChangedItems = null };
            frozen = new FrozenEcologyChunkSaveRecord { WorldKey = world, ChunkData = MemoryPackSerializer.Serialize(metadata) };
            foreach (var item in ecology.ChangedItems ?? new List<ItemData>()) frozen.ChangedItems.Add(ItemSnapshotSerialization.SerializePayload(item == null ? null : ItemSnapshotSerialization.Capture(item)));
        }
        return new SqliteChunkSnapshot { Terrain = terrain?.HasChanges == true ? MemoryPackSerializer.Serialize(terrain) : null, Ecology = frozen };
    }

    private SaveArchiveRecord FreezeSqliteChunk(string world, Vector2Int origin, GameSaveData source = null)
    {
        SqliteChunkSnapshot snapshot = CaptureSqliteChunk(world, origin, source);
        return FreezeSqliteChunk(world, origin, snapshot);
    }

    private static SaveArchiveRecord FreezeSqliteChunk(string world, Vector2Int origin, SqliteChunkSnapshot snapshot)
    {
        bool empty = snapshot.Terrain == null && snapshot.Ecology == null;
        return ArchiveRecord("chunk", world, "chunk", empty ? null : MemoryPackSerializer.Serialize(snapshot), origin.x, origin.y, empty);
    }

    private List<SaveArchiveRecord> CaptureSqliteSnapshot(GameSaveData source)
    {
        using var machineCapture = MachineWorld.BeginSaveSnapshotCapture();
        MonsterSpawnerManager.CaptureAll(source);
        byte[] machines = MachineWorld.CaptureArchive(source);
        FlatWorld.Spaceflight.SpaceSession.Capture(source);
        byte[] drops = DroppedItemService.CaptureArchive(source);
        return AppendResidentSqliteChunks(CaptureSqliteCoreRecords(source, machines, drops), source);
    }

    private List<SaveArchiveRecord> AppendResidentSqliteChunks(List<SaveArchiveRecord> records, GameSaveData source)
    {
        var chunks = new HashSet<(string world, Vector2Int origin)>();
        foreach (ChunkSaveRecord record in chunkDeltas.Values)
            if (record != null) chunks.Add((record.PlanetName, record.ChunkPosition));
        foreach (var world in source.PlanetData_Dict)
            if (world.Value?.Ecology?.Chunks != null)
                foreach (EcologyChunkSaveData chunk in world.Value.Ecology.Chunks)
                    chunks.Add((world.Key, new Vector2Int(chunk.ChunkX, chunk.ChunkY)));
        foreach (var state in persistedChunkReads.Values)
            if (state.Kind == "chunk" && state.Ready && !state.Released) chunks.Add((state.World, state.Origin));
        foreach (var key in chunks) records.Add(FreezeSqliteChunk(key.world, key.origin, source));
        return records;
    }

    private long ReserveArchiveSnapshotRevision(string fullPath)
    {
        OpenSqliteArchive(fullPath, true);
        long revision = ++archiveRecordRevision;
        archiveStore.ReserveSaveRevision(archiveSession, revision);
        return revision;
    }

    private async Task CommitSqliteRecordsAsync(string fullPath, GameSaveData source, List<SaveArchiveRecord> records, long revision = 0)
    {
        if (revision == 0) revision = ReserveArchiveSnapshotRevision(fullPath);
        string[] retired = records.Where(record => record.Kind == ArchiveWorldRemovalKind).Select(record => record.WorldKey).ToArray();
        await CommitSqliteRecordsToStoreAsync(archiveStore, archiveSession, revision, source.saveName, records).ConfigureAwait(false);
        foreach (string world in retired) retiredArchiveWorlds.TryRemove(world, out _);
    }

    private static async Task CommitSqliteRecordsToStoreAsync(ISaveArchiveStore store, string session, long revision, string displayName, List<SaveArchiveRecord> records)
    {
        var retired = new HashSet<string>(records.Where(record => record.Kind == ArchiveWorldRemovalKind).Select(record => record.WorldKey));
        records.RemoveAll(record => record.Kind == ArchiveWorldRemovalKind || retired.Contains(record.WorldKey));
        foreach (string world in retired)
            await store.StageWorldRemovalAsync(session, revision, world).ConfigureAwait(false);
        var identities = new HashSet<string>(records.Select(RecordIdentity));
        // 全量域显式删除旧身份，区块域只增量提交，不能删除未读的远区数据。
        foreach (string kind in new[] { "global", "world", "scope", "entity" })
            foreach (var previous in await store.ReadRecordKeysAsync(kind, null).ConfigureAwait(false))
                if (IsOwnedCoreRecord(previous) && !identities.Contains(RecordIdentity(previous)))
                    records.Add(new SaveArchiveRecord(previous.Kind, previous.WorldKey, previous.Key,
                        previous.ChunkX, previous.ChunkY, previous.SchemaId, previous.SchemaVersion, null, true));
        await store.CommitSnapshotAsync(session, revision, displayName, records).ConfigureAwait(false);
    }

    private static string RecordIdentity(SaveArchiveRecord record) => record.Kind + "\u001f" + record.WorldKey + "\u001f" + record.Key;
    private static string RecordIdentity(SaveArchiveRecordKey record) => record.Kind + "\u001f" + record.WorldKey + "\u001f" + record.Key;
    private static bool IsOwnedCoreRecord(SaveArchiveRecordKey record)
    {
        if (!record.SchemaId.StartsWith("flatworld.", StringComparison.Ordinal)) return false;
        return record.Kind switch
        {
            "world" => record.Key == "planet",
            "global" => record.Key == "core" || record.Key == "mods" || record.Key.StartsWith("player:", StringComparison.Ordinal),
            "scope" => record.WorldKey.Length == 0 && (record.Key == "space" || record.Key == "machine-metadata"),
            "entity" => record.Key.StartsWith("machine:", StringComparison.Ordinal) || record.Key.StartsWith("drop:", StringComparison.Ordinal) || record.Key.StartsWith("space:", StringComparison.Ordinal),
            _ => false
        };
    }

    private async Task<bool> CommitSqliteRecordsAndReportAsync(string path, GameSaveData source, List<SaveArchiveRecord> records, long revision = 0, long fileRevision = 0)
    {
        try { await CommitSqliteRecordsAsync(path, source, records, revision).ConfigureAwait(false); }
        catch (InvalidOperationException) when (fileRevision > 0 && !IsSaveRevisionCurrent(path, fileRevision)) { return false; }
        catch (OperationCanceledException) when (fileRevision > 0 && !IsSaveRevisionCurrent(path, fileRevision)) { return false; }
        return true;
    }

    private bool SaveSqliteArchive(GameSaveData source, string path)
    {
        var records = CaptureSqliteSnapshot(source);
        if (ReferenceEquals(source, SaveData)) CommitSqliteRecordsAsync(path, source, records).GetAwaiter().GetResult();
        else
        {
            using var store = new SqliteSaveArchiveStore(path, true);
            string session = Guid.NewGuid().ToString("N");
            store.ReserveSaveRevision(session, 1);
            CommitSqliteRecordsToStoreAsync(store, session, 1, source.saveName, records).GetAwaiter().GetResult();
        }
        return true;
    }

    private bool LoadSqliteArchive(string path)
    {
        CloseSqliteArchive();
        OpenSqliteArchive(path, false);
        GameSaveData candidate = ReadSqliteArchiveCandidate(archiveStore, out ModSaveMetadata modMetadata);
        SaveData = candidate;
        ValidateAndRestoreModMetadata(modMetadata);
        ResetChunkDifferenceState();
        return true;
    }

    private static GameSaveData ReadSqliteArchiveCandidate(ISaveArchiveStore store, out ModSaveMetadata modMetadata)
    {
        var metadata = store.ReadMetadataAsync().GetAwaiter().GetResult();
        if (!metadata.HasSnapshot) throw new InvalidDataException("数据库尚无已提交的存档。");
        var globals = store.ReadRecordsAsync("global", null).GetAwaiter().GetResult().Select(UpgradeSqliteRecord).ToArray();
        SaveArchiveRecord core = globals.FirstOrDefault(r => r.Key == "core" && r.SchemaId == "flatworld.core");
        if (core == null) throw new InvalidDataException("存档缺少核心记录。");
        GameSaveData candidate = MemoryPackSerializer.Deserialize<GameSaveData>(core.Payload);
        candidate.ArchiveId = metadata.ArchiveId; candidate.saveName = metadata.DisplayName;
        foreach (var record in store.ReadRecordsAsync("world", null).GetAwaiter().GetResult().Select(UpgradeSqliteRecord))
        {
            if (record.SchemaId.StartsWith("optional.", StringComparison.Ordinal)) continue;
            if (record.Key != "planet") continue;
            PlanetData planet = MemoryPackSerializer.Deserialize<PlanetData>(record.Payload);
            planet.Ecology?.EnsureCompatibleVersion();
            candidate.PlanetData_Dict[record.WorldKey] = planet;
        }
        modMetadata = null;
        foreach (SaveArchiveRecord record in globals)
        {
            if (record.SchemaId.StartsWith("optional.", StringComparison.Ordinal)) continue;
            if (record.Key == "mods") modMetadata = MemoryPackSerializer.Deserialize<ModSaveMetadata>(record.Payload);
            if (record.Key.StartsWith("player:", StringComparison.Ordinal))
                candidate.PlayerData_Dict[record.Key.Substring(7)] = (Data_Player)MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(record.Payload).CreateColdData();
        }
        var machines = new FrozenMachineArchivePayload { Worlds = new() };
        var drops = new DroppedItemArchive();
        foreach (var record in store.ReadRecordsAsync("scope", null).GetAwaiter().GetResult().Select(UpgradeSqliteRecord))
        {
            if (record.SchemaId.StartsWith("optional.", StringComparison.Ordinal)) continue;
            switch (record.Key)
            {
                case "space": candidate.SpaceStateJson = System.Text.Encoding.UTF8.GetString(record.Payload); break;
                case "machine-metadata": machines.Metadata = record.Payload; break;
            }
        }
        var entities = store.ReadRecordsAsync("entity", null).GetAwaiter().GetResult().Select(UpgradeSqliteRecord).ToArray();
        foreach (var record in entities)
        {
            if (record.SchemaId.StartsWith("optional.", StringComparison.Ordinal)) continue;
            if (record.Key.StartsWith("machine:", StringComparison.Ordinal))
            {
                if (!machines.Worlds.TryGetValue(record.WorldKey, out var items)) machines.Worlds.Add(record.WorldKey, items = new());
                items.Add(record.Payload);
            }
            else if (record.Key.StartsWith("drop:", StringComparison.Ordinal))
            {
                if (!drops.Worlds.TryGetValue(record.WorldKey, out var items)) drops.Worlds.Add(record.WorldKey, items = new());
                items.Add(MemoryPackSerializer.Deserialize<DroppedItemSaveRecord>(record.Payload));
            }
        }
        candidate.SpaceStateJson = RestoreSqliteSpaceRecords(candidate.SpaceStateJson, entities);
        if (!string.IsNullOrWhiteSpace(candidate.SpaceStateJson)) _ = DeserializeSpaceState(candidate.SpaceStateJson);
        if (machines.Metadata != null) MachineWorld.RestoreArchive(candidate, MemoryPackSerializer.Serialize(machines));
        DroppedItemService.RestoreArchive(candidate, MemoryPackSerializer.Serialize(drops));
        ModRuntimeManager mods = ModRuntimeManager.Instance;
        if (mods == null || !mods.IsReady || !mods.ValidateSaveMetadata(modMetadata, out string error))
            throw new InvalidDataException("存档 MOD 环境不兼容。");
        return candidate;
    }

    public static SaveArchiveSchemaRegistry ArchiveSchemas { get; } = CreateArchiveSchemaRegistry();

    private static SaveArchiveSchemaRegistry CreateArchiveSchemaRegistry()
    {
        var registry = new SaveArchiveSchemaRegistry();
        foreach (string id in new[] { "core", "planet", "player", "mods", "space", "chunk", "weather", "machine-metadata", "machine", "drop" })
            registry.RegisterSchema("flatworld." + id, 1);
        return registry;
    }

    private static SaveArchiveRecord UpgradeSqliteRecord(SaveArchiveRecord record) =>
        record.SchemaId.StartsWith("optional.", StringComparison.Ordinal) ? record : ArchiveSchemas.MigrateRecord(record);

    private void RecordPermanentlyRetiredArchiveWorld(string worldKey)
    {
        retiredArchiveWorlds.TryAdd(worldKey, 0);
        foreach (var pair in persistedChunkReads.Where(pair => pair.Value.World == worldKey).ToArray())
        {
            var state = pair.Value;
            if (!state.Ready && state.Task?.IsCompleted != true) state.ReadCancellation?.Cancel();
            state.RuntimePinCancellation.Dispose();
            persistedChunkReads.Remove(pair.Key); persistedChunkStages.Remove(pair.Key);
        }
        foreach (var key in networkWeatherRevisions.Keys.Where(key => key.world == worldKey).ToArray())
            networkWeatherRevisions.Remove(key);
    }
    #endregion


}

[MemoryPackable]
public partial class NetworkArchiveChunkSnapshot
{
    #region 观察者区块载荷
    public byte[] Chunk;
    public byte[] Weather;
    #endregion
}

[MemoryPackable(GenerateType.VersionTolerant)]
public partial class SqliteChunkSnapshot
{
    #region 发布区块契约
    [MemoryPackOrder(0)] public byte[] Terrain;
    [MemoryPackOrder(1)] public FrozenEcologyChunkSaveRecord Ecology;
    #endregion
}

public partial class GameSaveData
{
    #region 存档身份
    [MemoryPackIgnore] public string ArchiveId;
    #endregion
}
