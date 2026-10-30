using System;
using System.IO;
using System.Linq;
using System.Threading;
using FlatWorld.Persistence;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region SQLite 发布与暂存规则
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterSqliteSaveOperations() => Register("save", "sqlite_records", CaptureSqliteRecordRules);

    private static JObject CaptureSqliteRecordRules(JObject args)
    {
        string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "FlatWorldGameplayTests",
            "SqliteRules", Guid.NewGuid().ToString("N"), "save.sqlite");
        var terrain = new ChunkSaveRecord { PlanetName = "probe:sqlite", ChunkName = "(16, -32)" };
        terrain.RemovedItemGuids.Add(71);
        byte[] frozen = MemoryPackSerializer.Serialize(terrain);
        terrain.RemovedItemGuids.Add(72);
        SaveArchiveRecord Chunk(byte[] payload) => new("chunk", "probe:sqlite", "chunk", 16, -32, "flatworld.chunk", 1, payload);
        using var store = new SqliteSaveArchiveStore(path, true);
        const string session = "lua-sqlite-records";
        store.CommitSnapshotAsync(session, 1, "规则存档", new[] { Chunk(frozen) }).GetAwaiter().GetResult();
        store.StageRecordsAsync(session, 2, new[] { Chunk(new byte[] { 2 }) }).GetAwaiter().GetResult();
        bool formalIsolated = store.ReadChunkAsync("probe:sqlite", 16, -32).GetAwaiter().GetResult().Payload.SequenceEqual(frozen);
        bool stageVisible = store.ReadLiveChunkAsync(session, "probe:sqlite", 16, -32).GetAwaiter().GetResult().Payload[0] == 2;
        store.CommitSnapshotAsync(session, 3, "规则存档", Array.Empty<SaveArchiveRecord>()).GetAwaiter().GetResult();
        long generation = store.ReadMetadataAsync().GetAwaiter().GetResult().CurrentGeneration;
        store.StageRecordsAsync(session, 5, new[] { Chunk(new byte[] { 5 }) }).GetAwaiter().GetResult();
        store.CommitSnapshotAsync(session, 4, "规则存档", Array.Empty<SaveArchiveRecord>()).GetAwaiter().GetResult();
        bool futureStageIsolated = store.ReadChunkAsync("probe:sqlite", 16, -32).GetAwaiter().GetResult().Payload[0] == 2;
        bool futureStageRetained = store.ReadLiveChunkAsync(session, "probe:sqlite", 16, -32).GetAwaiter().GetResult().Payload[0] == 5;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool canceled = false;
        try { store.CommitSnapshotAsync(session, 6, "失败保存", new[] { Chunk(new byte[] { 6 }) }, cancellation.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { canceled = true; }
        bool failurePreserved = store.ReadMetadataAsync().GetAwaiter().GetResult().CurrentGeneration == generation &&
            store.ReadChunkAsync("probe:sqlite", 16, -32).GetAwaiter().GetResult().Payload[0] == 2;
        bool previous = store.RestorePreviousSnapshotAsync().GetAwaiter().GetResult();
        var restored = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(store.ReadChunkAsync("probe:sqlite", 16, -32).GetAwaiter().GetResult().Payload);
        bool unknownRejected = false;
        try { SaveDataMgr.ArchiveSchemas.MigrateRecord(new SaveArchiveRecord("chunk", "probe:sqlite", "chunk", 0, 0, "flatworld.chunk", 2, frozen)); }
        catch (InvalidDataException) { unknownRejected = true; }
        var host = new GameObject("probe:inactive-sqlite-read");
        host.SetActive(false);
        JObject readFailures;
        try { readFailures = host.AddComponent<SaveDataMgr>().CaptureTestSqliteReadFailureState(); }
        finally { UnityEngine.Object.DestroyImmediate(host); }
        return new JObject
        {
            ["formalIsolated"] = formalIsolated, ["stageVisible"] = stageVisible,
            ["futureStageIsolated"] = futureStageIsolated, ["futureStageRetained"] = futureStageRetained,
            ["canceled"] = canceled, ["failurePreserved"] = failurePreserved, ["previousRestored"] = previous,
            ["frozenItemIds"] = new JArray(restored.RemovedItemGuids), ["futureSchemaRejected"] = unknownRejected,
            ["chunkFailurePreserved"] = readFailures["chunkFailurePreserved"],
            ["weatherFailurePreserved"] = readFailures["weatherFailurePreserved"],
            ["path"] = path
        };
    }
    #endregion
}
