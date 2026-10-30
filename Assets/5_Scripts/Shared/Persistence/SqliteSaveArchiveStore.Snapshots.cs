using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SQLite;

namespace FlatWorld.Persistence
{
    #region 快照事务与备份

    public sealed partial class SqliteSaveArchiveStore
    {
        private const string MetadataQuery = "SELECT m.archive_id,m.current_generation,m.previous_generation,COALESCE(s.display_name,''),COALESCE(s.updated_utc_ticks,0),m.metadata_revision FROM archive_meta m LEFT JOIN archive_snapshots s ON s.generation=m.current_generation WHERE m.singleton=1";
        private const string StagedExistsQuery = "SELECT 1 FROM publish_records LIMIT 1";
        private const string StagedIdentityQuery = "SELECT kind,world_key,record_key FROM publish_records";
        private const string CloseCurrentRecordQuery = "UPDATE archive_records SET until_generation=? WHERE kind=? AND world_key=? AND record_key=? AND until_generation IS NULL";
        private const string PublishStagedRecordsQuery = "INSERT INTO archive_records (kind,world_key,record_key,key_value,chunk_x,chunk_y,schema_id,schema_version,payload,from_generation,until_generation) SELECT s.kind,s.world_key,s.record_key,s.key_value,s.chunk_x,s.chunk_y,s.schema_id,s.schema_version,s.payload,?,NULL FROM publish_records p JOIN staged_records s ON s.session_id=? AND s.kind=p.kind AND s.world_key=p.world_key AND s.record_key=p.record_key AND s.revision=p.revision WHERE s.deleted=0";
        private const string SelectPublishRecordsQuery = "INSERT INTO publish_records SELECT s.kind,s.world_key,s.record_key,s.revision FROM staged_records s WHERE s.session_id=? AND s.revision<=? AND NOT EXISTS (SELECT 1 FROM staged_records n WHERE n.session_id=s.session_id AND n.kind=s.kind AND n.world_key=s.world_key AND n.record_key=s.record_key AND n.revision<=? AND n.revision>s.revision) AND ((s.deleted=1 AND EXISTS (SELECT 1 FROM archive_records r WHERE r.kind=s.kind AND r.world_key=s.world_key AND r.record_key=s.record_key AND r.until_generation IS NULL)) OR (s.deleted=0 AND NOT EXISTS (SELECT 1 FROM archive_records r WHERE r.kind=s.kind AND r.world_key=s.world_key AND r.record_key=s.record_key AND r.until_generation IS NULL AND r.key_value=s.key_value AND r.chunk_x=s.chunk_x AND r.chunk_y=s.chunk_y AND r.schema_id=s.schema_id AND r.schema_version=s.schema_version AND r.payload IS s.payload)))";
        private const string DeleteCommittedStagesQuery = "DELETE FROM staged_records WHERE session_id=? AND revision<=?";
        private const string DeleteSessionQuery = "DELETE FROM staged_records WHERE session_id=?";
        private const string PublishRemovalCandidatesQuery = "INSERT OR REPLACE INTO staged_records (session_id,kind,world_key,record_key,key_value,chunk_x,chunk_y,schema_id,schema_version,payload,deleted,revision) SELECT session_id,kind,world_key,record_key,key_value,chunk_x,chunk_y,schema_id,schema_version,NULL,1,revision FROM removal_candidates WHERE session_id=? AND revision=?";

        public Task<SaveArchiveMetadata> ReadMetadataAsync(CancellationToken cancellationToken = default)
        {
            return EnqueueArchiveOperationAsync(token => ReadMetadataOnWorker(), cancellationToken);
        }

        public Task RenameDisplayNameAsync(string displayName, CancellationToken cancellationToken = default)
        {
            if (displayName == null)
                throw new ArgumentNullException(nameof(displayName));
            return EnqueueArchiveOperationAsync(token =>
            {
                ChangeArchiveIdentityAndName(null, displayName, token);
                return true;
            }, cancellationToken);
        }

        public Task ReidentifyArchiveAsync(string newArchiveId, string displayName,
            CancellationToken cancellationToken = default)
        {
            if (!Guid.TryParseExact(newArchiveId, "N", out var parsedId))
                throw new ArgumentException("存档 ID 必须是 32 位 GUID。", nameof(newArchiveId));
            if (displayName == null)
                throw new ArgumentNullException(nameof(displayName));
            string archiveId = parsedId.ToString("N");
            return EnqueueArchiveOperationAsync(token =>
            {
                ChangeArchiveIdentityAndName(archiveId, displayName, token);
                return true;
            }, cancellationToken);
        }

        public Task CommitSnapshotAsync(string sessionId, long revision, string displayName,
            IReadOnlyList<SaveArchiveRecord> records, CancellationToken cancellationToken = default)
        {
            if (displayName == null || records == null)
                throw new ArgumentNullException(displayName == null ? nameof(displayName) : nameof(records));
            ReserveSaveRevision(sessionId, revision);
            return EnqueueArchiveOperationAsync(token =>
            {
                RequireLatestSaveRequest(sessionId, revision);
                _database.Execute("BEGIN IMMEDIATE");
                try
                {
                    var metadata = ReadMetadataOnWorker();
                    var state = GetSession(sessionId, metadata);
                    RequireCommitRevision(state, revision);
                    StageFrozenRecords(sessionId, revision, records, token);
                    _database.Execute(PublishRemovalCandidatesQuery, sessionId, revision);
                    SelectChangedRecordsForCommit(sessionId, revision);
                    bool changed = HasStagedRecords();
                    long generation = metadata.CurrentGeneration;
                    long updatedUtcTicks = Math.Max(DateTime.UtcNow.Ticks, checked(metadata.UpdatedUtcTicks + 1));
                    if (changed || generation == 0 || metadata.DisplayName != displayName)
                    {
                        generation = checked(generation + 1);
                        CloseCurrentVersionsForStagedRecords(generation, token);
                        ExecuteTwoParameterStatement(PublishStagedRecordsQuery, generation, sessionId);
                        WriteSnapshotMetadata(generation, metadata.CurrentGeneration, displayName, updatedUtcTicks);
                        CollectObsoleteVersions(metadata.CurrentGeneration);
                    }
                    else
                        _database.Execute("UPDATE archive_snapshots SET updated_utc_ticks=? WHERE generation=?", updatedUtcTicks, generation);
                    DeleteCommittedStages(sessionId, revision);
                    _database.Execute("DELETE FROM removal_candidates WHERE session_id=? AND revision<=?", sessionId, revision);
                    bool hasRemovalCandidates = state.HasRemovalCandidates &&
                        _database.ExecuteScalar<long>("SELECT EXISTS(SELECT 1 FROM removal_candidates WHERE session_id=?)", sessionId) != 0;
                    _database.Execute("DELETE FROM publish_records");
                    token.ThrowIfCancellationRequested();
                    RequireLatestSaveRequest(sessionId, revision);
                    _database.Execute("COMMIT");
                    state.HighestRevision = Math.Max(state.HighestRevision, revision);
                    state.LastCommittedRevision = revision;
                    state.BaseGeneration = generation;
                    state.HasRemovalCandidates = hasRemovalCandidates;
                    return true;
                }
                catch
                {
                    RollbackArchiveTransaction();
                    throw;
                }
            }, cancellationToken);
        }

        public Task<bool> RestorePreviousSnapshotAsync(CancellationToken cancellationToken = default)
        {
            return EnqueueArchiveOperationAsync(token =>
            {
                _database.Execute("BEGIN IMMEDIATE");
                try
                {
                    var metadata = ReadMetadataOnWorker();
                    if (!metadata.HasPreviousSnapshot)
                    {
                        _database.Execute("COMMIT");
                        return false;
                    }

                    long nextGeneration = checked(metadata.CurrentGeneration + 1);
                    _database.Execute("UPDATE archive_records SET until_generation=? WHERE until_generation IS NULL AND from_generation>?", nextGeneration, metadata.PreviousGeneration);
                    _database.Execute("INSERT INTO archive_records (kind,world_key,record_key,key_value,chunk_x,chunk_y,schema_id,schema_version,payload,from_generation,until_generation) SELECT kind,world_key,record_key,key_value,chunk_x,chunk_y,schema_id,schema_version,payload,?,NULL FROM archive_records WHERE until_generation=?", nextGeneration, metadata.CurrentGeneration);
                    _database.Execute("INSERT INTO archive_snapshots (generation,display_name,updated_utc_ticks) SELECT ?,display_name,? FROM archive_snapshots WHERE generation=?", nextGeneration, DateTime.UtcNow.Ticks, metadata.PreviousGeneration);
                    _database.Execute("UPDATE archive_meta SET current_generation=?,previous_generation=? WHERE singleton=1", nextGeneration, metadata.CurrentGeneration);
                    _database.Execute("DELETE FROM staged_records");
                    _database.Execute("DELETE FROM removal_candidates");
                    CollectObsoleteVersions(metadata.CurrentGeneration);
                    token.ThrowIfCancellationRequested();
                    _database.Execute("COMMIT");
                    foreach (var sessionId in _sessions.Keys)
                        _closedSessions.Add(sessionId);
                    _sessions.Clear();
                    return true;
                }
                catch
                {
                    RollbackArchiveTransaction();
                    throw;
                }
            }, cancellationToken);
        }

        public Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(destinationPath))
                throw new ArgumentException("备份路径不能为空。", nameof(destinationPath));
            string fullPath = Path.GetFullPath(destinationPath);
            if (string.Equals(fullPath, _databasePath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("备份路径不能与存档相同。", nameof(destinationPath));
            return EnqueueArchiveOperationAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                CopyArchiveDatabaseToPath(fullPath);
                return true;
            }, cancellationToken);
        }

        public Task ForkAsync(string destinationPath, string newArchiveId, string displayName,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(destinationPath))
                throw new ArgumentException("复制路径不能为空。", nameof(destinationPath));
            if (!Guid.TryParseExact(newArchiveId, "N", out var parsedId))
                throw new ArgumentException("存档 ID 必须是 32 位 GUID。", nameof(newArchiveId));
            if (displayName == null)
                throw new ArgumentNullException(nameof(displayName));
            string fullPath = Path.GetFullPath(destinationPath);
            if (string.Equals(fullPath, _databasePath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("复制路径不能与存档相同。", nameof(destinationPath));
            string newId = parsedId.ToString("N");
            return EnqueueArchiveOperationAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                if (File.Exists(fullPath))
                    throw new IOException("复制目标已经存在。");
                string candidate = fullPath + ".fork-" + Guid.NewGuid().ToString("N");
                try
                {
                    CopyArchiveDatabaseToPath(candidate);
                    token.ThrowIfCancellationRequested();
                    using (var fork = new SqliteSaveArchiveStore(candidate))
                        fork.ReidentifyArchiveAsync(newId, displayName, token).GetAwaiter().GetResult();
                    token.ThrowIfCancellationRequested();
                    File.Move(candidate, fullPath);
                }
                finally
                {
                    if (File.Exists(candidate)) File.Delete(candidate);
                    if (File.Exists(candidate + "-wal")) File.Delete(candidate + "-wal");
                    if (File.Exists(candidate + "-shm")) File.Delete(candidate + "-shm");
                }
                return true;
            }, cancellationToken);
        }

        public Task DiscardSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentException("会话 ID 不能为空。", nameof(sessionId));
            return EnqueueArchiveOperationAsync(token =>
            {
                token.ThrowIfCancellationRequested();
                DeleteStagedSession(sessionId);
                _database.Execute("DELETE FROM removal_candidates WHERE session_id=?", sessionId);
                _sessions.Remove(sessionId);
                _closedSessions.Add(sessionId);
                return true;
            }, cancellationToken);
        }

        private SaveArchiveMetadata ReadMetadataOnWorker()
        {
            var statement = GetStatement(MetadataQuery);
            try
            {
                if (statement.Step() != SQLite3.Result.Row)
                    throw new InvalidDataException("存档元数据不存在。");
                return new SaveArchiveMetadata
                {
                    ArchiveId = statement.Text(0),
                    CurrentGeneration = statement.Long(1),
                    PreviousGeneration = statement.Long(2),
                    DisplayName = statement.Text(3),
                    UpdatedUtcTicks = statement.Long(4),
                    MetadataRevision = statement.Long(5)
                };
            }
            finally { statement.Reset(); }
        }

        private void ChangeArchiveIdentityAndName(string archiveId, string displayName, CancellationToken cancellationToken)
        {
            _database.Execute("BEGIN IMMEDIATE");
            try
            {
                var metadata = ReadMetadataOnWorker();
                if (!metadata.HasSnapshot)
                    throw new InvalidOperationException("尚未保存的档案无法改名或复制。");
                string newId = archiveId ?? metadata.ArchiveId;
                if (metadata.ArchiveId != newId || metadata.DisplayName != displayName)
                {
                    _database.Execute("UPDATE archive_meta SET archive_id=?,metadata_revision=metadata_revision+1 WHERE singleton=1", newId);
                    _database.Execute("UPDATE archive_snapshots SET display_name=?,updated_utc_ticks=CASE WHEN generation=? THEN ? ELSE updated_utc_ticks END", displayName, metadata.CurrentGeneration, DateTime.UtcNow.Ticks);
                }
                cancellationToken.ThrowIfCancellationRequested();
                _database.Execute("COMMIT");
            }
            catch
            {
                RollbackArchiveTransaction();
                throw;
            }
        }

        private void CopyArchiveDatabaseToPath(string destinationPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            using (var destination = new SQLiteConnection(destinationPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create))
            {
                IntPtr backup = SQLite3.BackupInit(destination.Handle, "main", _database.Handle, "main");
                if (backup == IntPtr.Zero)
                    throw SQLiteException.New(SQLite3.GetResult(destination.Handle), SQLite3.GetErrmsg(destination.Handle));
                SQLite3.Result step;
                SQLite3.Result finish;
                try { step = SQLite3.BackupStep(backup, -1); }
                finally { finish = SQLite3.BackupFinish(backup); }
                if (step != SQLite3.Result.Done || finish != SQLite3.Result.OK)
                    throw SQLiteException.New(step != SQLite3.Result.Done ? step : finish, SQLite3.GetErrmsg(destination.Handle));
            }
        }

        private bool HasStagedRecords()
        {
            var statement = GetStatement(StagedExistsQuery);
            try
            {
                return statement.Step() == SQLite3.Result.Row;
            }
            finally { statement.Reset(); }
        }

        private void SelectChangedRecordsForCommit(string sessionId, long revision)
        {
            _database.Execute("DELETE FROM publish_records");
            var statement = GetStatement(SelectPublishRecordsQuery);
            try
            {
                statement.Bind(1, sessionId);
                statement.Bind(2, revision);
                statement.Bind(3, revision);
                statement.Step();
            }
            finally { statement.Reset(); }
        }

        private void CloseCurrentVersionsForStagedRecords(long generation, CancellationToken cancellationToken)
        {
            var identities = GetStatement(StagedIdentityQuery);
            var close = GetStatement(CloseCurrentRecordQuery);
            try
            {
                while (identities.Step() == SQLite3.Result.Row)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        close.Bind(1, generation);
                        close.Bind(2, identities.Text(0));
                        close.Bind(3, identities.Text(1));
                        close.Bind(4, identities.Text(2));
                        close.Step();
                    }
                    finally { close.Reset(); }
                }
            }
            finally { identities.Reset(); }
        }

        private void ExecuteTwoParameterStatement(string sql, long number, string value)
        {
            var statement = GetStatement(sql);
            try
            {
                statement.Bind(1, number);
                statement.Bind(2, value);
                statement.Step();
            }
            finally { statement.Reset(); }
        }

        private void DeleteStagedSession(string sessionId)
        {
            var statement = GetStatement(DeleteSessionQuery);
            try
            {
                statement.Bind(1, sessionId);
                statement.Step();
            }
            finally { statement.Reset(); }
        }

        private void DeleteCommittedStages(string sessionId, long revision)
        {
            var statement = GetStatement(DeleteCommittedStagesQuery);
            try
            {
                statement.Bind(1, sessionId);
                statement.Bind(2, revision);
                statement.Step();
            }
            finally { statement.Reset(); }
        }

        private void WriteSnapshotMetadata(long generation, long previousGeneration, string displayName, long utcTicks)
        {
            _database.Execute("INSERT INTO archive_snapshots VALUES (?,?,?)", generation, displayName, utcTicks);
            _database.Execute("UPDATE archive_meta SET current_generation=?,previous_generation=? WHERE singleton=1", generation, previousGeneration);
        }

        private void CollectObsoleteVersions(long previousGeneration)
        {
            _database.Execute("DELETE FROM archive_records WHERE until_generation IS NOT NULL AND until_generation<=?", previousGeneration);
            _database.Execute("DELETE FROM archive_snapshots WHERE generation NOT IN (SELECT current_generation FROM archive_meta UNION ALL SELECT previous_generation FROM archive_meta)");
        }
    }

    #endregion
}
