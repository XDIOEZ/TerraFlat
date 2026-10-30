using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using SQLite;

namespace FlatWorld.Persistence
{
    #region 记录查询与暂存

    public sealed partial class SqliteSaveArchiveStore
    {
        private const string CurrentChunkQuery = "SELECT kind,world_key,key_value,chunk_x,chunk_y,schema_id,schema_version,payload FROM archive_records WHERE kind='chunk' AND world_key=? AND chunk_x=? AND chunk_y=? AND until_generation IS NULL";
        private const string StagedChunkQuery = "SELECT kind,world_key,key_value,chunk_x,chunk_y,schema_id,schema_version,payload,deleted,revision FROM staged_records WHERE session_id=? AND kind='chunk' AND world_key=? AND chunk_x=? AND chunk_y=? ORDER BY revision DESC LIMIT 1";
        private const string CurrentRecordQuery = "SELECT kind,world_key,key_value,chunk_x,chunk_y,schema_id,schema_version,payload FROM archive_records WHERE kind=? AND world_key=? AND record_key=? AND until_generation IS NULL";
        private const string StagedRecordQuery = "SELECT kind,world_key,key_value,chunk_x,chunk_y,schema_id,schema_version,payload,deleted,revision FROM staged_records WHERE session_id=? AND kind=? AND world_key=? AND record_key=? ORDER BY revision DESC LIMIT 1";
        private const string CurrentRecordsQuery = "SELECT kind,world_key,key_value,chunk_x,chunk_y,schema_id,schema_version,payload FROM archive_records WHERE kind=? AND world_key=? AND until_generation IS NULL ORDER BY record_key";
        private const string CurrentRecordsAllWorldsQuery = "SELECT kind,world_key,key_value,chunk_x,chunk_y,schema_id,schema_version,payload FROM archive_records WHERE kind=? AND until_generation IS NULL ORDER BY world_key,record_key";
        private const string CurrentKeysQuery = "SELECT kind,world_key,key_value,chunk_x,chunk_y,schema_id,schema_version FROM archive_records WHERE kind=? AND world_key=? AND until_generation IS NULL ORDER BY record_key";
        private const string CurrentKeysAllWorldsQuery = "SELECT kind,world_key,key_value,chunk_x,chunk_y,schema_id,schema_version FROM archive_records WHERE kind=? AND until_generation IS NULL ORDER BY world_key,record_key";
        private const string CurrentExistsQuery = "SELECT 1 FROM archive_records WHERE kind=? AND world_key=? AND record_key=? AND until_generation IS NULL";
        private const string CurrentEqualsQuery = "SELECT 1 FROM archive_records WHERE kind=? AND world_key=? AND record_key=? AND until_generation IS NULL AND key_value=? AND chunk_x=? AND chunk_y=? AND schema_id=? AND schema_version=? AND payload=?";
        private const string LatestStagedComparisonQuery = "SELECT deleted,(key_value=? AND chunk_x=? AND chunk_y=? AND schema_id=? AND schema_version=? AND payload IS ?),revision FROM staged_records WHERE session_id=? AND kind=? AND world_key=? AND record_key=? AND revision<=? ORDER BY revision DESC LIMIT 1";
        private const string UpsertStagedQuery = "INSERT INTO staged_records (session_id,kind,world_key,record_key,key_value,chunk_x,chunk_y,schema_id,schema_version,payload,deleted,revision) VALUES (?,?,?,?,?,?,?,?,?,?,?,?) ON CONFLICT(session_id,kind,world_key,record_key,revision) DO UPDATE SET key_value=excluded.key_value,chunk_x=excluded.chunk_x,chunk_y=excluded.chunk_y,schema_id=excluded.schema_id,schema_version=excluded.schema_version,payload=excluded.payload,deleted=excluded.deleted WHERE key_value IS NOT excluded.key_value OR chunk_x IS NOT excluded.chunk_x OR chunk_y IS NOT excluded.chunk_y OR schema_id IS NOT excluded.schema_id OR schema_version IS NOT excluded.schema_version OR payload IS NOT excluded.payload OR deleted IS NOT excluded.deleted";
        private const string CollectCurrentWorldRecordsQuery = "INSERT OR IGNORE INTO removal_records SELECT kind,world_key,record_key,key_value,chunk_x,chunk_y,schema_id,schema_version FROM archive_records WHERE world_key=? AND until_generation IS NULL";
        private const string CollectStagedWorldRecordsQuery = "INSERT OR REPLACE INTO removal_records SELECT s.kind,s.world_key,s.record_key,s.key_value,s.chunk_x,s.chunk_y,s.schema_id,s.schema_version FROM staged_records s WHERE s.session_id=? AND s.world_key=? AND s.revision<=? AND NOT EXISTS (SELECT 1 FROM staged_records n WHERE n.session_id=s.session_id AND n.kind=s.kind AND n.world_key=s.world_key AND n.record_key=s.record_key AND n.revision<=? AND n.revision>s.revision)";
        private const string StageRemovedWorldRecordsQuery = "INSERT OR REPLACE INTO removal_candidates (session_id,revision,kind,world_key,record_key,key_value,chunk_x,chunk_y,schema_id,schema_version) SELECT ?,?,kind,world_key,record_key,key_value,chunk_x,chunk_y,schema_id,schema_version FROM removal_records";
        private const string CandidateChunkQuery = "SELECT revision FROM removal_candidates WHERE session_id=? AND kind='chunk' AND world_key=? AND chunk_x=? AND chunk_y=? AND revision>=? ORDER BY revision DESC LIMIT 1";
        private const string CandidateRecordQuery = "SELECT revision FROM removal_candidates WHERE session_id=? AND kind=? AND world_key=? AND record_key=? AND revision>=? ORDER BY revision DESC LIMIT 1";
        private const string EarlierCandidateQuery = "SELECT 1 FROM removal_candidates WHERE session_id=? AND kind=? AND world_key=? AND record_key=? AND revision<? AND revision>=? LIMIT 1";

        public Task<SaveArchiveRecord> ReadChunkAsync(string worldKey, int x, int y,
            CancellationToken cancellationToken = default)
        {
            if (worldKey == null)
                throw new ArgumentNullException(nameof(worldKey));
            return EnqueueArchiveOperationAsync(token => ReadCurrentChunk(worldKey, x, y), cancellationToken);
        }

        public Task<SaveArchiveRecord> ReadLiveChunkAsync(string sessionId, string worldKey, int x, int y,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentException("会话 ID 不能为空。", nameof(sessionId));
            if (worldKey == null)
                throw new ArgumentNullException(nameof(worldKey));
            return EnqueueArchiveOperationAsync(token =>
            {
                RequireLiveSessionIfPreviouslyOpened(sessionId);
                var statement = GetStatement(StagedChunkQuery);
                SaveArchiveRecord staged = null;
                long stagedRevision = -1;
                bool found = false;
                try
                {
                    statement.Bind(1, sessionId);
                    statement.Bind(2, worldKey);
                    statement.Bind(3, x);
                    statement.Bind(4, y);
                    if (statement.Step() == SQLite3.Result.Row)
                    {
                        found = true;
                        stagedRevision = statement.Long(9);
                        if (statement.Int(8) == 0) staged = ReadRecord(statement);
                    }
                }
                finally { statement.Reset(); }
                if (_sessions.TryGetValue(sessionId, out var state) && state.HasRemovalCandidates &&
                    IsChunkRetiredByLatestCandidate(sessionId, worldKey, x, y, stagedRevision)) return null;
                if (found) return staged;
                return ReadCurrentChunk(worldKey, x, y);
            }, cancellationToken);
        }

        public Task<SaveArchiveRecord> ReadRecordAsync(string kind, string worldKey, string key,
            CancellationToken cancellationToken = default)
        {
            RequireKeyedRecordIdentity(kind, worldKey, key);
            return EnqueueArchiveOperationAsync(token => ReadCurrentRecord(kind, worldKey, key), cancellationToken);
        }

        public Task<SaveArchiveRecord> ReadLiveRecordAsync(string sessionId, string kind, string worldKey, string key,
            CancellationToken cancellationToken = default)
        {
            RequireKeyedRecordIdentity(kind, worldKey, key);
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentException("会话 ID 不能为空。", nameof(sessionId));
            return EnqueueArchiveOperationAsync(token =>
            {
                RequireLiveSessionIfPreviouslyOpened(sessionId);
                var statement = GetStatement(StagedRecordQuery);
                SaveArchiveRecord staged = null;
                long stagedRevision = -1;
                bool found = false;
                try
                {
                    statement.Bind(1, sessionId);
                    statement.Bind(2, kind);
                    statement.Bind(3, worldKey);
                    statement.Bind(4, key);
                    if (statement.Step() == SQLite3.Result.Row)
                    {
                        found = true;
                        stagedRevision = statement.Long(9);
                        if (statement.Int(8) == 0) staged = ReadRecord(statement);
                    }
                }
                finally { statement.Reset(); }
                if (_sessions.TryGetValue(sessionId, out var state) && state.HasRemovalCandidates &&
                    IsRecordRetiredByLatestCandidate(sessionId, kind, worldKey, key, stagedRevision)) return null;
                if (found) return staged;
                return ReadCurrentRecord(kind, worldKey, key);
            }, cancellationToken);
        }

        private bool IsChunkRetiredByLatestCandidate(string sessionId, string worldKey, int x, int y, long stagedRevision)
        {
            var statement = GetStatement(CandidateChunkQuery);
            try
            {
                statement.Bind(1, sessionId); statement.Bind(2, worldKey); statement.Bind(3, x); statement.Bind(4, y);
                statement.Bind(5, Math.Max(stagedRevision, LatestSaveRequestRevision(sessionId)));
                return statement.Step() == SQLite3.Result.Row;
            }
            finally { statement.Reset(); }
        }

        private bool IsRecordRetiredByLatestCandidate(string sessionId, string kind, string worldKey, string key, long stagedRevision)
        {
            var statement = GetStatement(CandidateRecordQuery);
            try
            {
                statement.Bind(1, sessionId); statement.Bind(2, kind); statement.Bind(3, worldKey); statement.Bind(4, key);
                statement.Bind(5, Math.Max(stagedRevision, LatestSaveRequestRevision(sessionId)));
                return statement.Step() == SQLite3.Result.Row;
            }
            finally { statement.Reset(); }
        }

        private static void RequireKeyedRecordIdentity(string kind, string worldKey, string key)
        {
            if (string.IsNullOrEmpty(kind) || worldKey == null || key == null)
                throw new ArgumentException("存档记录身份不能为空。");
            if (kind == SaveArchiveRecordKinds.Chunk)
                throw new ArgumentException("区块记录必须使用坐标查询。", nameof(kind));
        }

        private void RequireLiveSessionIfPreviouslyOpened(string sessionId)
        {
            if (_closedSessions.Contains(sessionId))
                throw new InvalidOperationException("存档会话已经关闭。");
            if (_sessions.TryGetValue(sessionId, out var state))
            {
                var metadata = ReadMetadataOnWorker();
                if (state.BaseGeneration != metadata.CurrentGeneration || state.BaseMetadataRevision != metadata.MetadataRevision)
                    throw new InvalidOperationException("该存档会话已过期。");
            }
        }

        public Task<IReadOnlyList<SaveArchiveRecord>> ReadRecordsAsync(string kind, string worldKey,
            CancellationToken cancellationToken = default)
        {
            if (kind == null)
                throw new ArgumentNullException(nameof(kind));
            return EnqueueArchiveOperationAsync<IReadOnlyList<SaveArchiveRecord>>(token =>
            {
                var records = new List<SaveArchiveRecord>();
                var statement = GetStatement(worldKey == null ? CurrentRecordsAllWorldsQuery : CurrentRecordsQuery);
                try
                {
                    statement.Bind(1, kind);
                    if (worldKey != null)
                        statement.Bind(2, worldKey);
                    while (statement.Step() == SQLite3.Result.Row)
                    {
                        token.ThrowIfCancellationRequested();
                        records.Add(ReadRecord(statement));
                    }
                    return records;
                }
                finally { statement.Reset(); }
            }, cancellationToken);
        }

        public Task<IReadOnlyList<SaveArchiveRecordKey>> ReadRecordKeysAsync(string kind, string worldKey = null,
            CancellationToken cancellationToken = default)
        {
            if (kind == null)
                throw new ArgumentNullException(nameof(kind));
            return EnqueueArchiveOperationAsync<IReadOnlyList<SaveArchiveRecordKey>>(token =>
            {
                var records = new List<SaveArchiveRecordKey>();
                var statement = GetStatement(worldKey == null ? CurrentKeysAllWorldsQuery : CurrentKeysQuery);
                try
                {
                    statement.Bind(1, kind);
                    if (worldKey != null)
                        statement.Bind(2, worldKey);
                    while (statement.Step() == SQLite3.Result.Row)
                    {
                        token.ThrowIfCancellationRequested();
                        records.Add(new SaveArchiveRecordKey
                        {
                            Kind = statement.Text(0), WorldKey = statement.Text(1), Key = statement.Text(2),
                            ChunkX = statement.Int(3), ChunkY = statement.Int(4),
                            SchemaId = statement.Text(5), SchemaVersion = statement.Int(6)
                        });
                    }
                    return records;
                }
                finally { statement.Reset(); }
            }, cancellationToken);
        }

        public Task StageRecordsAsync(string sessionId, long revision, IReadOnlyList<SaveArchiveRecord> records,
            CancellationToken cancellationToken = default)
        {
            if (records == null)
                throw new ArgumentNullException(nameof(records));
            return EnqueueArchiveOperationAsync(token =>
            {
                _database.Execute("BEGIN IMMEDIATE");
                try
                {
                    var state = GetSession(sessionId, ReadMetadataOnWorker());
                    RequireStageRevision(state, revision);
                    StageFrozenRecords(sessionId, revision, records, token);
                    token.ThrowIfCancellationRequested();
                    _database.Execute("COMMIT");
                    state.HighestRevision = revision;
                    return true;
                }
                catch
                {
                    RollbackArchiveTransaction();
                    throw;
                }
            }, cancellationToken);
        }

        public Task StageWorldRemovalAsync(string sessionId, long revision, string worldKey,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(worldKey))
                throw new ArgumentException("世界 ID 不能为空。", nameof(worldKey));
            return EnqueueArchiveOperationAsync(token =>
            {
                _database.Execute("BEGIN IMMEDIATE");
                try
                {
                    var state = GetSession(sessionId, ReadMetadataOnWorker());
                    RequireCommitRevision(state, revision);
                    RequireLatestSaveRequest(sessionId, revision);
                    _database.Execute("DELETE FROM removal_records");
                    _database.Execute(CollectCurrentWorldRecordsQuery, worldKey);
                    token.ThrowIfCancellationRequested();
                    _database.Execute(CollectStagedWorldRecordsQuery, sessionId, worldKey, revision, revision);
                    token.ThrowIfCancellationRequested();
                    _database.Execute("DELETE FROM removal_candidates WHERE session_id=? AND revision=? AND world_key=?", sessionId, revision, worldKey);
                    _database.Execute(StageRemovedWorldRecordsQuery, sessionId, revision);
                    _database.Execute("DELETE FROM removal_records");
                    token.ThrowIfCancellationRequested();
                    RequireLatestSaveRequest(sessionId, revision);
                    _database.Execute("COMMIT");
                    state.HighestRevision = Math.Max(state.HighestRevision, revision);
                    state.HasRemovalCandidates = true;
                    return true;
                }
                catch
                {
                    RollbackArchiveTransaction();
                    throw;
                }
            }, cancellationToken);
        }

        private SaveArchiveRecord ReadCurrentChunk(string worldKey, int x, int y)
        {
            var statement = GetStatement(CurrentChunkQuery);
            try
            {
                statement.Bind(1, worldKey);
                statement.Bind(2, x);
                statement.Bind(3, y);
                return statement.Step() == SQLite3.Result.Row ? ReadRecord(statement) : null;
            }
            finally { statement.Reset(); }
        }

        private SaveArchiveRecord ReadCurrentRecord(string kind, string worldKey, string key)
        {
            var statement = GetStatement(CurrentRecordQuery);
            try
            {
                statement.Bind(1, kind);
                statement.Bind(2, worldKey);
                statement.Bind(3, key);
                return statement.Step() == SQLite3.Result.Row ? ReadRecord(statement) : null;
            }
            finally { statement.Reset(); }
        }

        private static SaveArchiveRecord ReadRecord(SaveArchiveSqlStatement statement)
        {
            return new SaveArchiveRecord(statement.Text(0), statement.Text(1), statement.Text(2),
                statement.Int(3), statement.Int(4), statement.Text(5), statement.Int(6), statement.Bytes(7));
        }

        private void StageFrozenRecords(string sessionId, long revision, IReadOnlyList<SaveArchiveRecord> records,
            CancellationToken cancellationToken)
        {
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (record == null || string.IsNullOrEmpty(record.Kind))
                    throw new ArgumentException("存档记录必须包含类型。", nameof(records));
                string identity = RecordIdentity(record);
                bool? sameAsStaged = RecordMatchesLatestStagedVersion(sessionId, revision, record, identity, out long stagedRevision);
                bool sameAsVisibleVersion = sameAsStaged ?? (record.Deleted
                    ? !RecordExistsInCurrentSnapshot(record, identity)
                    : RecordMatchesCurrentSnapshot(record, identity));
                bool preserveFutureVersion = sameAsStaged.HasValue && stagedRevision < revision;
                if (!sameAsStaged.HasValue && sameAsVisibleVersion)
                {
                    lock (_queueLock)
                        preserveFutureVersion = _latestSaveRequests.TryGetValue(sessionId, out var reserved) &&
                            reserved > _sessions[sessionId].LastCommittedRevision && reserved < revision;
                    if (!preserveFutureVersion && _sessions[sessionId].HasRemovalCandidates)
                        preserveFutureVersion = HasEarlierWorldRemovalCandidate(sessionId, record, identity, revision);
                }
                if (!sameAsVisibleVersion || preserveFutureVersion)
                    UpsertStagedRecord(sessionId, revision, record, identity);
            }
        }

        private bool HasEarlierWorldRemovalCandidate(string sessionId, SaveArchiveRecord record, string identity, long revision)
        {
            var statement = GetStatement(EarlierCandidateQuery);
            try
            {
                statement.Bind(1, sessionId); statement.Bind(2, record.Kind); statement.Bind(3, record.WorldKey);
                statement.Bind(4, identity); statement.Bind(5, revision); statement.Bind(6, LatestSaveRequestRevision(sessionId));
                return statement.Step() == SQLite3.Result.Row;
            }
            finally { statement.Reset(); }
        }

        private static string RecordIdentity(SaveArchiveRecord record)
        {
            return record.Kind == SaveArchiveRecordKinds.Chunk
                ? record.ChunkX.ToString(CultureInfo.InvariantCulture) + ":" + record.ChunkY.ToString(CultureInfo.InvariantCulture)
                : record.Key;
        }

        private bool RecordExistsInCurrentSnapshot(SaveArchiveRecord record, string identity)
        {
            var statement = GetStatement(CurrentExistsQuery);
            try
            {
                BindIdentity(statement, record, identity);
                return statement.Step() == SQLite3.Result.Row;
            }
            finally { statement.Reset(); }
        }

        private bool RecordMatchesCurrentSnapshot(SaveArchiveRecord record, string identity)
        {
            var statement = GetStatement(CurrentEqualsQuery);
            try
            {
                BindIdentity(statement, record, identity);
                statement.Bind(4, record.Key);
                statement.Bind(5, record.ChunkX);
                statement.Bind(6, record.ChunkY);
                statement.Bind(7, record.SchemaId);
                statement.Bind(8, record.SchemaVersion);
                statement.Bind(9, record.Payload);
                return statement.Step() == SQLite3.Result.Row;
            }
            finally { statement.Reset(); }
        }

        private static void BindIdentity(SaveArchiveSqlStatement statement, SaveArchiveRecord record, string identity)
        {
            statement.Bind(1, record.Kind);
            statement.Bind(2, record.WorldKey);
            statement.Bind(3, identity);
        }

        private bool? RecordMatchesLatestStagedVersion(string sessionId, long revision, SaveArchiveRecord record, string identity,
            out long stagedRevision)
        {
            stagedRevision = -1;
            var statement = GetStatement(LatestStagedComparisonQuery);
            try
            {
                statement.Bind(1, record.Key);
                statement.Bind(2, record.ChunkX);
                statement.Bind(3, record.ChunkY);
                statement.Bind(4, record.SchemaId);
                statement.Bind(5, record.SchemaVersion);
                statement.Bind(6, record.Deleted ? null : record.Payload);
                statement.Bind(7, sessionId);
                statement.Bind(8, record.Kind);
                statement.Bind(9, record.WorldKey);
                statement.Bind(10, identity);
                statement.Bind(11, revision);
                if (statement.Step() != SQLite3.Result.Row)
                    return null;
                stagedRevision = statement.Long(2);
                bool deleted = statement.Int(0) != 0;
                return deleted == record.Deleted && (deleted || statement.Int(1) != 0);
            }
            finally { statement.Reset(); }
        }

        private void UpsertStagedRecord(string sessionId, long revision, SaveArchiveRecord record, string identity)
        {
            var statement = GetStatement(UpsertStagedQuery);
            try
            {
                statement.Bind(1, sessionId);
                statement.Bind(2, record.Kind);
                statement.Bind(3, record.WorldKey);
                statement.Bind(4, identity);
                statement.Bind(5, record.Key);
                statement.Bind(6, record.ChunkX);
                statement.Bind(7, record.ChunkY);
                statement.Bind(8, record.SchemaId);
                statement.Bind(9, record.SchemaVersion);
                statement.Bind(10, record.Deleted ? null : record.Payload);
                statement.Bind(11, record.Deleted ? 1 : 0);
                statement.Bind(12, revision);
                statement.Step();
            }
            finally { statement.Reset(); }
        }
    }

    #endregion
}
