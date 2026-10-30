using System.Collections;
using System.Diagnostics;
using FlatWorld.Persistence;
using SQLite;

internal static class Program
{
    private static readonly SaveArchiveRecord[] Empty = Array.Empty<SaveArchiveRecord>();

    private static int Main(string[] args)
    {
        string root = Path.Combine(args[0], "run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            TestSchemaMigration();
            TestTransactionsAndRecovery(root);
            TestWorldRemovalAndRecovery(root);
            TestIndexedScaling(root);
            Console.WriteLine("PASS: all real SQLite persistence tests");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static void TestWorldRemovalAndRecovery(string root)
    {
        string path = Path.Combine(root, "retired-world", "save.sqlite");
        using var store = new SqliteSaveArchiveStore(path, true);
        var keep = new SaveArchiveRecord("scope", "keep", "day", 0, 0, "schema", 1, new byte[] { 9 });
        var retiredScope = new SaveArchiveRecord("scope", "retire", "day", 0, 0, "schema", 1, new byte[] { 1 });
        var retiredChunk = new SaveArchiveRecord("chunk", "retire", "terrain", 2, 3, "schema", 1, new byte[] { 2 });
        var stagedChunk = new SaveArchiveRecord("chunk", "retire", "terrain", 4, 5, "schema", 1, new byte[] { 3 });
        var futureChunk = new SaveArchiveRecord("chunk", "retire", "terrain", 2, 3, "schema", 1, new byte[] { 4 });
        Wait(store.CommitSnapshotAsync("worlds", 1, "worlds", new[] { keep, retiredScope, retiredChunk }));
        using (var database = new SQLiteConnection(path))
        {
            IntPtr plan = SQLite3.Prepare2(database.Handle, "EXPLAIN QUERY PLAN SELECT kind FROM archive_records WHERE world_key=? AND until_generation IS NULL");
            try
            {
                Require(SQLite3.Step(plan) == SQLite3.Result.Row && SQLite3.ColumnString(plan, 3).Contains("archive_current_world"),
                    "retired world uses its index");
            }
            finally { SQLite3.Finalize(plan); }
        }
        Wait(store.StageRecordsAsync("worlds", 2, new[] { stagedChunk }));
        store.ReserveSaveRevision("worlds", 3);
        Wait(store.StageRecordsAsync("worlds", 4, new[] { futureChunk }));
        Wait(store.StageWorldRemovalAsync("worlds", 3, "retire"));
        Require(Value(Wait(store.ReadChunkAsync("retire", 2, 3))) == 2, "retirement remains invisible before commit");
        Require(Wait(store.ReadLiveChunkAsync("worlds", "retire", 4, 5)) == null, "retirement removes staged-only identity");
        Require(Value(Wait(store.ReadLiveChunkAsync("worlds", "retire", 2, 3))) == 4, "future stage survives retirement");
        Wait(store.CommitSnapshotAsync("worlds", 3, "worlds", Empty));
        Require(Wait(store.ReadChunkAsync("retire", 2, 3)) == null &&
            Wait(store.ReadRecordAsync("scope", "retire", "day")) == null, "retirement publishes all world kinds");
        Require(Value(Wait(store.ReadRecordAsync("scope", "keep", "day"))) == 9, "retirement leaves other world intact");
        Require(Value(Wait(store.ReadLiveChunkAsync("worlds", "retire", 2, 3))) == 4, "future stage survives cutoff commit");
        ExpectFailure(() => Wait(store.StageWorldRemovalAsync("worlds", 3, "retire")), "committed retirement revision rejected");
        Require(Wait(store.RestorePreviousSnapshotAsync()), "previous world snapshot exists");
        Require(Value(Wait(store.ReadChunkAsync("retire", 2, 3))) == 2 &&
            Value(Wait(store.ReadRecordAsync("scope", "retire", "day"))) == 1, "previous snapshot restores retired world");
        Require(Wait(store.ReadChunkAsync("retire", 4, 5)) == null &&
            Value(Wait(store.ReadRecordAsync("scope", "keep", "day"))) == 9, "recovery excludes uncommitted stage and preserves other world");

        using var superseded = new SqliteSaveArchiveStore(Path.Combine(root, "superseded-retirement", "save.sqlite"), true);
        Wait(superseded.CommitSnapshotAsync("pending", 1, "save", new[] { retiredScope, retiredChunk }));
        superseded.ReserveSaveRevision("pending", 2);
        Wait(superseded.StageWorldRemovalAsync("pending", 2, "retire"));
        Require(Wait(superseded.ReadLiveChunkAsync("pending", "retire", 2, 3)) == null, "pending candidate visible in live view");
        superseded.ReserveSaveRevision("pending", 3);
        Require(Value(Wait(superseded.ReadLiveChunkAsync("pending", "retire", 2, 3))) == 2, "superseded candidate disappears from live view");
        Wait(superseded.CommitSnapshotAsync("pending", 3, "save", Empty));
        Require(Value(Wait(superseded.ReadChunkAsync("retire", 2, 3))) == 2 &&
            Value(Wait(superseded.ReadRecordAsync("scope", "retire", "day"))) == 1,
            "superseded retirement never leaks into newer commit");
        superseded.ReserveSaveRevision("pending", 4);
        Wait(superseded.StageWorldRemovalAsync("pending", 4, "retire"));
        var replacedDuringCommit = new CallbackRecords(new[] { keep }, () => superseded.ReserveSaveRevision("pending", 5));
        ExpectFailure(() => Wait(superseded.CommitSnapshotAsync("pending", 4, "save", replacedDuringCommit)), "retirement commit superseded");
        Wait(superseded.CommitSnapshotAsync("pending", 5, "save", Empty));
        Require(Value(Wait(superseded.ReadChunkAsync("retire", 2, 3))) == 2, "rolled back retirement does not leak");
        Console.WriteLine("PASS: retirement cutoff, staged-only records, future stages and previous-world recovery");
    }

    private static void TestSchemaMigration()
    {
        var registry = new SaveArchiveSchemaRegistry();
        registry.RegisterSchema("test.codec", 3);
        Require(registry.GetCurrentVersion("test.codec") == 3, "registered current writer version");
        ExpectFailure(() => registry.GetCurrentVersion("unknown"), "unregistered writer schema rejected");
        registry.RegisterMigration(new IncrementMigration(1, 2));
        registry.RegisterMigration(new IncrementMigration(2, 3));
        var old = new SaveArchiveRecord("global", "", "entry", 0, 0, "test.codec", 1, new byte[] { 5 });
        var migrated = registry.MigrateRecord(old);
        Require(migrated.SchemaVersion == 3 && migrated.Payload[0] == 7 && old.Payload[0] == 5,
            "independent migration chain");
        Require(ReferenceEquals(migrated, registry.MigrateRecord(migrated)), "current schema does not rewrite");
        ExpectFailure(() => registry.MigrateRecord(new SaveArchiveRecord("global", "", "x", 0, 0,
            "unknown", 1, new byte[] { 1 })), "unknown schema rejected");
        ExpectFailure(() => registry.MigrateRecord(new SaveArchiveRecord("global", "", "x", 0, 0,
            "test.codec", 4, new byte[] { 1 })), "future schema rejected");
        var incomplete = new SaveArchiveSchemaRegistry();
        incomplete.RegisterSchema("test.codec", 2);
        ExpectFailure(() => incomplete.MigrateRecord(old), "missing migration rejected");
        Console.WriteLine("PASS: schema registration, migration chain, unknown and future version rejection");
    }

    private static void TestTransactionsAndRecovery(string root)
    {
        string archiveId = Guid.NewGuid().ToString("N");
        string path = Path.Combine(root, archiveId, "save.sqlite");
        using var store = new SqliteSaveArchiveStore(path, true);
        Require(Wait(store.ReadMetadataAsync()).ArchiveId == archiveId, "directory archive ID");
        var first = Chunk(0, 0, 10);
        var remote = Chunk(10000, -4, 20);
        var emptyBlob = new SaveArchiveRecord(SaveArchiveRecordKinds.Global, "", "empty", 0, 0, "schema", 1, Array.Empty<byte>());
        var scope1 = new SaveArchiveRecord(SaveArchiveRecordKinds.Scope, "世界", "one", 0, 0, "schema", 1, new byte[] { 1 });
        var scope2 = new SaveArchiveRecord(SaveArchiveRecordKinds.Scope, "第二", "two", 0, 0, "schema", 1, new byte[] { 2 });
        var weather = new SaveArchiveRecord("weather", "世界", "-1:2", 0, 0, "flatworld.weather", 1, new byte[] { 10 });
        Wait(store.CommitSnapshotAsync("s", 1, "初始档", new[] { first, remote, emptyBlob, scope1, scope2, weather }));
        Require(Wait(store.ReadRecordsAsync(SaveArchiveRecordKinds.Global, ""))[0].Payload.Length == 0, "empty blob roundtrip");
        Require(Wait(store.ReadRecordsAsync(SaveArchiveRecordKinds.Scope, null)).Count == 2, "null world selects all worlds");
        Require(Wait(store.ReadRecordsAsync(SaveArchiveRecordKinds.Scope, "")).Count == 0, "empty world selects only empty");
        var scopeKeys = Wait(store.ReadRecordKeysAsync(SaveArchiveRecordKinds.Scope));
        Require(scopeKeys.Count == 2 && scopeKeys.Any(key => key.Key == "two" && key.WorldKey == "第二"), "key-only query across worlds");

        var nextWeather = new SaveArchiveRecord("weather", "世界", "-1:2", 0, 0, "flatworld.weather", 1, new byte[] { 11 });
        Wait(store.StageRecordsAsync("s", 2, new[] { Chunk(0, 0, 11), nextWeather }));
        Require(Value(Wait(store.ReadRecordAsync("weather", "世界", "-1:2"))) == 10, "keyed formal read excludes stage");
        Require(Value(Wait(store.ReadLiveRecordAsync("s", "weather", "世界", "-1:2"))) == 11, "keyed live read sees stage");
        Require(Value(Wait(store.ReadChunkAsync("世界", 0, 0))) == 10, "stage not committed");
        Require(Value(Wait(store.ReadLiveChunkAsync("s", "世界", 0, 0))) == 11, "live sees stage");
        store.ReserveSaveRevision("s", 2);
        Wait(store.StageRecordsAsync("s", 3, new[] { Chunk(0, 0, 12) }));
        Wait(store.CommitSnapshotAsync("s", 2, "初始档", Empty));
        Require(Value(Wait(store.ReadChunkAsync("世界", 0, 0))) == 11, "commit cutoff");
        Require(Value(Wait(store.ReadRecordAsync("weather", "世界", "-1:2"))) == 11, "keyed staged record committed");
        Require(Value(Wait(store.ReadLiveChunkAsync("s", "世界", 0, 0))) == 12, "future stage retained");
        Require(Value(Wait(store.ReadChunkAsync("世界", 10000, -4))) == 20, "unloaded chunk retained");
        Wait(store.CommitSnapshotAsync("s", 3, "初始档", Empty));
        Require(Value(Wait(store.ReadChunkAsync("世界", 0, 0))) == 12, "future stage committed later");

        // 未来阶段即使等于旧正式值，也不能被更早的冻结提交覆盖。
        store.ReserveSaveRevision("s", 4);
        Wait(store.StageRecordsAsync("s", 5, new[] { Chunk(0, 0, 12) }));
        Wait(store.CommitSnapshotAsync("s", 4, "初始档", new[] { Chunk(0, 0, 13) }));
        Require(Value(Wait(store.ReadChunkAsync("世界", 0, 0))) == 13, "old frozen snapshot published");
        Require(Value(Wait(store.ReadLiveChunkAsync("s", "世界", 0, 0))) == 12, "future identical-to-old stage survives");
        Wait(store.CommitSnapshotAsync("s", 5, "初始档", Empty));
        Require(Value(Wait(store.ReadChunkAsync("世界", 0, 0))) == 12, "future reversion published");

        store.ReserveSaveRevision("s", 6);
        Wait(store.StageRecordsAsync("s", 6, new[] { Chunk(0, 0, 14) }));
        Wait(store.StageRecordsAsync("s", 7, new[] { Chunk(0, 0, 14) }));
        Wait(store.CommitSnapshotAsync("s", 6, "初始档", new[] { Chunk(0, 0, 15) }));
        Require(Value(Wait(store.ReadLiveChunkAsync("s", "世界", 0, 0))) == 14, "future duplicate survives overwritten cutoff");
        Wait(store.CommitSnapshotAsync("s", 7, "初始档", Empty));
        Require(Value(Wait(store.ReadChunkAsync("世界", 0, 0))) == 14, "future duplicate published");

        Wait(store.CommitSnapshotAsync("s", 8, "删除后", new[] { Tombstone(0, 0) }));
        Require(Wait(store.ReadChunkAsync("世界", 0, 0)) == null, "explicit deletion");
        Require(Wait(store.RestorePreviousSnapshotAsync()), "restore previous exists");
        Require(Value(Wait(store.ReadChunkAsync("世界", 0, 0))) == 14, "restore previous data");
        Require(Wait(store.ReadMetadataAsync()).DisplayName == "初始档", "restore previous display name");
        Require(Wait(store.RestorePreviousSnapshotAsync()), "restore after restore");
        Require(Wait(store.ReadChunkAsync("世界", 0, 0)) == null, "restore retains displaced generation");
        ExpectFailure(() => Wait(store.StageRecordsAsync("s", 9, new[] { first })), "restored session rejected");

        Wait(store.StageRecordsAsync("s2", 1, new[] { Chunk(0, 0, 42) }));
        using (var external = new SQLiteConnection(path))
            external.Execute("CREATE TRIGGER fail_publish BEFORE INSERT ON archive_records BEGIN SELECT RAISE(ABORT,'injected transaction failure'); END");
        ExpectFailure(() => Wait(store.CommitSnapshotAsync("s2", 1, "应回滚", Empty)), "injected failure");
        Require(Wait(store.ReadChunkAsync("世界", 0, 0)) == null, "failed publish rolls back data");
        Require(Wait(store.ReadMetadataAsync()).DisplayName == "删除后", "failed publish rolls back metadata");
        Require(Value(Wait(store.ReadLiveChunkAsync("s2", "世界", 0, 0))) == 42, "failed publish keeps stage");
        using (var external = new SQLiteConnection(path))
            external.Execute("DROP TRIGGER fail_publish");
        Wait(store.CommitSnapshotAsync("s2", 1, "成功", Empty));
        ExpectFailure(() => Wait(store.StageRecordsAsync("s2", 1, new[] { first })), "committed revision rejected");

        long changes = Wait(store.ReadChangedRowCountForTestsAsync());
        var before = Wait(store.ReadMetadataAsync());
        Wait(store.CommitSnapshotAsync("s2", 2, "成功", new[] { Chunk(0, 0, 42) }));
        var after = Wait(store.ReadMetadataAsync());
        Require(before.CurrentGeneration == after.CurrentGeneration && before.UpdatedUtcTicks < after.UpdatedUtcTicks,
            "unchanged commit does not create generation");
        Require(Wait(store.ReadChangedRowCountForTestsAsync()) == changes + 1, "unchanged commit only updates snapshot timestamp");

        var superseded = new CallbackRecords(new[] { Chunk(0, 0, 43) }, () => store.ReserveSaveRevision("s2", 4));
        ExpectFailure(() => Wait(store.CommitSnapshotAsync("s2", 3, "被取代", superseded)), "in-flight save superseded");
        Require(Value(Wait(store.ReadChunkAsync("世界", 0, 0))) == 42, "superseded transaction rolls back");
        Require(Value(Wait(store.ReadLiveChunkAsync("s2", "世界", 0, 0))) == 42, "superseded frozen candidate removed");

        ExpectFailure(() => Wait(store.CommitSnapshotAsync("s2", 3, "过期", Empty)), "new save rejects older request");
        using (var cancellation = new CancellationTokenSource())
        {
            var rows = new CancelingRecords(new[] { Chunk(3, 0, 1), Chunk(4, 0, 2) }, cancellation);
            ExpectFailure(() => Wait(store.StageRecordsAsync("s2", 5, rows, cancellation.Token)), "cancel stage");
            Require(Wait(store.ReadLiveChunkAsync("s2", "世界", 3, 0)) == null, "canceled stage rolls back all rows");
        }

        Wait(store.StageRecordsAsync("s2", 6, new[] { Chunk(0, 0, 99) }));
        string backup = Path.Combine(root, "backup.sqlite");
        Wait(store.BackupAsync(backup));
        using (var backupStore = new SqliteSaveArchiveStore(backup))
        {
            Require(Wait(backupStore.ReadMetadataAsync()).ArchiveId == archiveId, "backup stable archive ID");
            Require(Value(Wait(backupStore.ReadChunkAsync("世界", 0, 0))) == 42, "backup excludes uncommitted stage");
        }
        string forkId = Guid.NewGuid().ToString("N");
        string forkPath = Path.Combine(root, forkId, "save.sqlite");
        Wait(store.ForkAsync(forkPath, forkId, "复制档"));
        using (var forkStore = new SqliteSaveArchiveStore(forkPath))
        {
            var forkMetadata = Wait(forkStore.ReadMetadataAsync());
            Require(forkMetadata.ArchiveId == forkId && forkMetadata.DisplayName == "复制档", "fork has new identity and name");
            Require(Value(Wait(forkStore.ReadChunkAsync("世界", 0, 0))) == 42, "fork preserves committed payload");
            Wait(forkStore.StageRecordsAsync("old-name-session", 1, new[] { Chunk(0, 0, 77) }));
            Wait(forkStore.RenameDisplayNameAsync("改名档"));
            Require(Wait(forkStore.ReadMetadataAsync()).DisplayName == "改名档", "rename persists");
            ExpectFailure(() => Wait(forkStore.CommitSnapshotAsync("old-name-session", 1, "旧名", Empty)), "renamed session rejected");
            Require(Wait(forkStore.RestorePreviousSnapshotAsync()), "fork retains previous generation");
            Require(Wait(forkStore.ReadMetadataAsync()).DisplayName == "改名档", "renamed identity survives recovery");
        }
        Require(Wait(store.ReadMetadataAsync()).ArchiveId == archiveId && Wait(store.ReadMetadataAsync()).DisplayName == "成功", "fork leaves source identity intact");
        ExpectFailure(() => Wait(store.ForkAsync(forkPath, forkId, "覆盖")), "fork refuses existing target");
        Wait(store.DiscardSessionAsync("s2"));
        ExpectFailure(() => Wait(store.StageRecordsAsync("s2", 7, new[] { first })), "discarded session rejected");

        using (var staleA = new SqliteSaveArchiveStore(path))
        using (var concurrentB = new SqliteSaveArchiveStore(path))
        {
            Wait(staleA.StageRecordsAsync("a", 1, new[] { Chunk(0, 0, 55) }));
            Wait(concurrentB.CommitSnapshotAsync("b", 1, "并发", new[] { Chunk(0, 0, 56) }));
            ExpectFailure(() => Wait(staleA.CommitSnapshotAsync("a", 1, "旧档", Empty)), "stale session rejected");
            ExpectFailure(() => Wait(staleA.ReadLiveChunkAsync("a", "世界", 0, 0)), "stale live query rejected");
        }
        using (var missing = new SqliteSaveArchiveStore(Path.Combine(root, "missing.sqlite")))
            ExpectFailure(() => Wait(missing.ReadChunkAsync("世界", 0, 0)), "read error is not absent chunk");
        Console.WriteLine("PASS: transactions, cutoff, cancellation, rollback, recovery, stale saves, backup");
    }

    private static void TestIndexedScaling(string root)
    {
        string path = Path.Combine(root, "scale", "save.sqlite");
        using var store = new SqliteSaveArchiveStore(path, true);
        int previous = 0;
        int revision = 0;
        foreach (int count in new[] { 1000, 10000, 100000 })
        {
            var records = new SaveArchiveRecord[count - previous];
            for (int i = previous; i < count; i++)
                records[i - previous] = Chunk(i, i * 3, i % 256);
            var watch = Stopwatch.StartNew();
            Wait(store.CommitSnapshotAsync("scale", ++revision, "压力档", records));
            watch.Stop();
            long writeMs = watch.ElapsedMilliseconds;
            long beforeBytes = GC.GetTotalAllocatedBytes(true);
            watch.Restart();
            const int samples = 256;
            for (int i = 0; i < samples; i++)
            {
                int index = (int)((long)i * (count - 1) / (samples - 1));
                Require(Value(Wait(store.ReadChunkAsync("世界", index, index * 3))) == index % 256, "indexed chunk value");
            }
            watch.Stop();
            long allocated = GC.GetTotalAllocatedBytes(true) - beforeBytes;
            Require(Wait(store.ReadChunkAsync("世界", -10, -20)) == null, "confirmed absent chunk");
            string queryPlan = ReadChunkQueryPlan(path);
            Require(queryPlan.Contains("archive_current_chunk", StringComparison.Ordinal), "coordinate index selected: " + queryPlan);
            Console.WriteLine($"records={count:N0} write={writeMs}ms reads={samples} elapsed={watch.Elapsed.TotalMilliseconds:F1}ms alloc={allocated:N0}B plan={queryPlan}");
            previous = count;
        }
    }

    private static string ReadChunkQueryPlan(string path)
    {
        using var database = new SQLiteConnection(path);
        IntPtr statement = SQLite3.Prepare2(database.Handle, "EXPLAIN QUERY PLAN SELECT payload FROM archive_records WHERE kind='chunk' AND world_key=? AND chunk_x=? AND chunk_y=? AND until_generation IS NULL");
        try
        {
            Require(SQLite3.Step(statement) == SQLite3.Result.Row, "query plan result");
            return SQLite3.ColumnString(statement, 3);
        }
        finally { SQLite3.Finalize(statement); }
    }

    private static SaveArchiveRecord Chunk(int x, int y, int value) =>
        new SaveArchiveRecord(SaveArchiveRecordKinds.Chunk, "世界", "terrain", x, y, "chunk-v1", 1, new[] { (byte)value });

    private static SaveArchiveRecord Tombstone(int x, int y) =>
        new SaveArchiveRecord(SaveArchiveRecordKinds.Chunk, "世界", "terrain", x, y, "chunk-v1", 1, null, true);

    private static int Value(SaveArchiveRecord record) => record?.Payload[0] ?? -1;
    private static void Require(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
    }
    private static void ExpectFailure(Action action, string description)
    {
        try { action(); }
        catch (Exception) { return; }
        throw new Exception("FAIL: expected error: " + description);
    }
    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();
    private static void Wait(Task task) => task.GetAwaiter().GetResult();

    private sealed class CancelingRecords : IReadOnlyList<SaveArchiveRecord>
    {
        private readonly SaveArchiveRecord[] _records;
        private readonly CancellationTokenSource _source;
        public CancelingRecords(SaveArchiveRecord[] records, CancellationTokenSource source) { _records = records; _source = source; }
        public int Count => _records.Length;
        public SaveArchiveRecord this[int index] => _records[index];
        public IEnumerator<SaveArchiveRecord> GetEnumerator()
        {
            yield return _records[0];
            _source.Cancel();
            yield return _records[1];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CallbackRecords : IReadOnlyList<SaveArchiveRecord>
    {
        private readonly SaveArchiveRecord[] _records;
        private readonly Action _callback;
        public CallbackRecords(SaveArchiveRecord[] records, Action callback) { _records = records; _callback = callback; }
        public int Count => _records.Length;
        public SaveArchiveRecord this[int index] => _records[index];
        public IEnumerator<SaveArchiveRecord> GetEnumerator()
        {
            yield return _records[0];
            _callback();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class IncrementMigration : ISaveArchivePayloadMigration
    {
        public string SchemaId => "test.codec";
        public int FromVersion { get; }
        public int ToVersion { get; }
        public IncrementMigration(int from, int to) { FromVersion = from; ToVersion = to; }
        public byte[] Migrate(byte[] payload) => new byte[] { (byte)(payload[0] + 1) };
    }
}
