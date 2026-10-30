using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SQLite;

namespace FlatWorld.Persistence
{
    #region 单档串行存储

    public sealed partial class SqliteSaveArchiveStore : ISaveArchiveStore
    {
        private const int FormatVersion = 1;
        private const int QueueCapacity = 128;
        private readonly string _databasePath;
        private readonly bool _createIfMissing;
        private readonly object _queueLock = new object();
        private readonly SemaphoreSlim _queueSpace = new SemaphoreSlim(QueueCapacity, QueueCapacity);
        private readonly BlockingCollection<IArchiveOperation> _operations = new BlockingCollection<IArchiveOperation>();
        private readonly Dictionary<string, SaveArchiveSqlStatement> _statements = new Dictionary<string, SaveArchiveSqlStatement>();
        private readonly Dictionary<string, SessionRevisionState> _sessions = new Dictionary<string, SessionRevisionState>();
        private readonly Dictionary<string, long> _latestSaveRequests = new Dictionary<string, long>();
        private readonly HashSet<string> _closedSessions = new HashSet<string>();
        private readonly Thread _worker;
        private SQLiteConnection _database;
        private Exception _openFailure;
        private bool _disposed;

        public SqliteSaveArchiveStore(string databasePath, bool createIfMissing = false)
        {
            if (string.IsNullOrEmpty(databasePath))
                throw new ArgumentException("存档路径不能为空。", nameof(databasePath));
            _databasePath = Path.GetFullPath(databasePath);
            _createIfMissing = createIfMissing;
            _worker = new Thread(ExecuteQueuedArchiveOperations) { IsBackground = true, Name = "FlatWorld SQLite save" };
            _worker.Start();
        }

        public void Dispose()
        {
            lock (_queueLock)
            {
                if (_disposed)
                    return;
                _disposed = true;
                _operations.CompleteAdding();
            }
            if (Thread.CurrentThread != _worker)
                _worker.Join();
        }

        private async Task<T> EnqueueArchiveOperationAsync<T>(Func<CancellationToken, T> operation,
            CancellationToken cancellationToken)
        {
            await _queueSpace.WaitAsync(cancellationToken).ConfigureAwait(false);
            var work = new ArchiveOperation<T>(operation, cancellationToken);
            lock (_queueLock)
            {
                if (_disposed)
                {
                    _queueSpace.Release();
                    throw new ObjectDisposedException(nameof(SqliteSaveArchiveStore));
                }
                _operations.Add(work);
            }
            return await work.Completion.Task.ConfigureAwait(false);
        }

        private void ExecuteQueuedArchiveOperations()
        {
            try
            {
                foreach (var operation in _operations.GetConsumingEnumerable())
                {
                    _queueSpace.Release();
                    try
                    {
                        operation.CancellationToken.ThrowIfCancellationRequested();
                        OpenArchiveOnWorkerIfNeeded();
                        operation.Execute();
                    }
                    catch (Exception exception)
                    {
                        operation.Fail(exception);
                    }
                }
            }
            finally
            {
                foreach (var statement in _statements.Values)
                    statement.Dispose();
                _database?.Dispose();
            }
        }

        private void OpenArchiveOnWorkerIfNeeded()
        {
            if (_database != null)
                return;
            if (_openFailure != null)
                throw new InvalidOperationException("无法打开存档。", _openFailure);

            try
            {
                bool exists = File.Exists(_databasePath);
                if (!exists && !_createIfMissing)
                    throw new FileNotFoundException("存档文件不存在。", _databasePath);
                if (!exists)
                    Directory.CreateDirectory(Path.GetDirectoryName(_databasePath));
                var flags = SQLiteOpenFlags.ReadWrite | (exists ? 0 : SQLiteOpenFlags.Create);
                _database = new SQLiteConnection(_databasePath, flags);

                if (!exists)
                    CreateNewArchiveSchema();
                else if (_database.ExecuteScalar<int>("PRAGMA user_version") != FormatVersion)
                    throw new InvalidDataException("存档数据库版本不受支持。");
                _database.Execute("CREATE INDEX IF NOT EXISTS archive_current_world ON archive_records(world_key,kind,record_key) WHERE until_generation IS NULL");

                string journalMode = _database.ExecuteScalar<string>("PRAGMA journal_mode=WAL");
                if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("存档数据库未能启用 WAL。");
                _database.Execute("PRAGMA synchronous=FULL");
                _database.Execute("PRAGMA cache_size=-2048");
                _database.Execute("PRAGMA mmap_size=0");
                _database.Execute("PRAGMA temp_store=FILE");
                _database.Execute("PRAGMA temp.cache_size=-2048");
                _database.Execute("CREATE TEMP TABLE staged_records (session_id TEXT NOT NULL, kind TEXT NOT NULL, world_key TEXT NOT NULL, record_key TEXT NOT NULL, key_value TEXT NOT NULL, chunk_x INTEGER NOT NULL, chunk_y INTEGER NOT NULL, schema_id TEXT NOT NULL, schema_version INTEGER NOT NULL, payload BLOB, deleted INTEGER NOT NULL, revision INTEGER NOT NULL, PRIMARY KEY(session_id,kind,world_key,record_key,revision)) WITHOUT ROWID");
                _database.Execute("CREATE INDEX staged_chunks ON staged_records(session_id,world_key,chunk_x,chunk_y,revision DESC) WHERE kind='chunk'");
                _database.Execute("CREATE INDEX staged_world ON staged_records(session_id,world_key,kind,record_key,revision DESC)");
                _database.Execute("CREATE TEMP TABLE removal_records (kind TEXT NOT NULL, world_key TEXT NOT NULL, record_key TEXT NOT NULL, key_value TEXT NOT NULL, chunk_x INTEGER NOT NULL, chunk_y INTEGER NOT NULL, schema_id TEXT NOT NULL, schema_version INTEGER NOT NULL, PRIMARY KEY(kind,world_key,record_key)) WITHOUT ROWID");
                _database.Execute("CREATE TEMP TABLE removal_candidates (session_id TEXT NOT NULL, revision INTEGER NOT NULL, kind TEXT NOT NULL, world_key TEXT NOT NULL, record_key TEXT NOT NULL, key_value TEXT NOT NULL, chunk_x INTEGER NOT NULL, chunk_y INTEGER NOT NULL, schema_id TEXT NOT NULL, schema_version INTEGER NOT NULL, PRIMARY KEY(session_id,revision,kind,world_key,record_key)) WITHOUT ROWID");
                _database.Execute("CREATE INDEX removal_candidate_chunk ON removal_candidates(session_id,world_key,chunk_x,chunk_y,revision DESC) WHERE kind='chunk'");
                _database.Execute("CREATE INDEX removal_candidate_key ON removal_candidates(session_id,kind,world_key,record_key,revision DESC)");
                _database.Execute("CREATE TEMP TABLE publish_records (kind TEXT NOT NULL, world_key TEXT NOT NULL, record_key TEXT NOT NULL, revision INTEGER NOT NULL, PRIMARY KEY(kind,world_key,record_key)) WITHOUT ROWID");
            }
            catch (Exception exception)
            {
                _database?.Dispose();
                _database = null;
                _openFailure = exception;
                throw;
            }
        }

        private void CreateNewArchiveSchema()
        {
            _database.Execute("BEGIN IMMEDIATE");
            try
            {
                _database.Execute("CREATE TABLE archive_meta (singleton INTEGER PRIMARY KEY CHECK(singleton=1), archive_id TEXT NOT NULL, current_generation INTEGER NOT NULL, previous_generation INTEGER NOT NULL, metadata_revision INTEGER NOT NULL)");
                _database.Execute("CREATE TABLE archive_snapshots (generation INTEGER PRIMARY KEY, display_name TEXT NOT NULL, updated_utc_ticks INTEGER NOT NULL)");
                _database.Execute("CREATE TABLE archive_records (kind TEXT NOT NULL, world_key TEXT NOT NULL, record_key TEXT NOT NULL, key_value TEXT NOT NULL, chunk_x INTEGER NOT NULL, chunk_y INTEGER NOT NULL, schema_id TEXT NOT NULL, schema_version INTEGER NOT NULL, payload BLOB NOT NULL, from_generation INTEGER NOT NULL, until_generation INTEGER, PRIMARY KEY(kind,world_key,record_key,from_generation)) WITHOUT ROWID");
                _database.Execute("CREATE UNIQUE INDEX archive_current_key ON archive_records(kind,world_key,record_key) WHERE until_generation IS NULL");
                _database.Execute("CREATE UNIQUE INDEX archive_current_chunk ON archive_records(world_key,chunk_x,chunk_y) WHERE kind='chunk' AND until_generation IS NULL");
                _database.Execute("CREATE INDEX archive_current_generation ON archive_records(from_generation) WHERE until_generation IS NULL");
                _database.Execute("CREATE INDEX archive_ended_generation ON archive_records(until_generation) WHERE until_generation IS NOT NULL");
                string directoryName = Path.GetFileName(Path.GetDirectoryName(_databasePath));
                string archiveId = Guid.TryParseExact(directoryName, "N", out var parsedId)
                    ? parsedId.ToString("N") : Guid.NewGuid().ToString("N");
                _database.Execute("INSERT INTO archive_meta VALUES (1,?,0,0,0)", archiveId);
                _database.Execute("PRAGMA user_version=1");
                _database.Execute("COMMIT");
            }
            catch
            {
                RollbackArchiveTransaction();
                throw;
            }
        }

        private SaveArchiveSqlStatement GetStatement(string sql)
        {
            if (!_statements.TryGetValue(sql, out var statement))
            {
                statement = new SaveArchiveSqlStatement(_database, sql);
                _statements.Add(sql, statement);
            }
            return statement;
        }

        private void RollbackArchiveTransaction()
        {
            try { _database.Execute("ROLLBACK"); }
            catch { }
        }

        private SessionRevisionState GetSession(string sessionId, SaveArchiveMetadata metadata)
        {
            if (string.IsNullOrEmpty(sessionId))
                throw new ArgumentException("会话 ID 不能为空。", nameof(sessionId));
            if (_closedSessions.Contains(sessionId))
                throw new InvalidOperationException("存档会话已经关闭。");
            if (!_sessions.TryGetValue(sessionId, out var state))
            {
                state = new SessionRevisionState { BaseGeneration = metadata.CurrentGeneration, BaseMetadataRevision = metadata.MetadataRevision };
                _sessions.Add(sessionId, state);
            }
            if (state.BaseGeneration != metadata.CurrentGeneration || state.BaseMetadataRevision != metadata.MetadataRevision)
                throw new InvalidOperationException("该存档会话已过期。");
            return state;
        }

        private static void RequireStageRevision(SessionRevisionState state, long revision)
        {
            if (revision < 0 || revision < state.HighestRevision || revision <= state.LastCommittedRevision)
                throw new InvalidOperationException("存档修订已过期。");
        }

        private static void RequireCommitRevision(SessionRevisionState state, long revision)
        {
            if (revision < 0 || revision <= state.LastCommittedRevision)
                throw new InvalidOperationException("存档修订已过期。");
        }

        public void ReserveSaveRevision(string sessionId, long revision)
        {
            if (string.IsNullOrEmpty(sessionId) || revision < 0)
                throw new ArgumentException("存档会话或修订无效。");
            lock (_queueLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(SqliteSaveArchiveStore));
                if (_latestSaveRequests.TryGetValue(sessionId, out var latest) && revision < latest)
                    throw new InvalidOperationException("存档修订已过期。");
                _latestSaveRequests[sessionId] = revision;
            }
        }

        private void RequireLatestSaveRequest(string sessionId, long revision)
        {
            lock (_queueLock)
            {
                if (_latestSaveRequests.TryGetValue(sessionId, out var latest) && latest > revision)
                    throw new InvalidOperationException("存档请求已被更新的保存取代。");
            }
        }

        private long LatestSaveRequestRevision(string sessionId)
        {
            lock (_queueLock)
                return _latestSaveRequests.TryGetValue(sessionId, out var latest) ? latest : -1;
        }

#if SQLITE_PERSISTENCE_TESTS
        internal Task<long> ReadChangedRowCountForTestsAsync()
        {
            return EnqueueArchiveOperationAsync(token => _database.ExecuteScalar<long>("SELECT total_changes()"), default);
        }
#endif

        private sealed class SessionRevisionState
        {
            public long BaseGeneration;
            public long BaseMetadataRevision;
            public long HighestRevision = -1;
            public long LastCommittedRevision = -1;
            public bool HasRemovalCandidates;
        }

        private interface IArchiveOperation
        {
            CancellationToken CancellationToken { get; }
            void Execute();
            void Fail(Exception exception);
        }

        private sealed class ArchiveOperation<T> : IArchiveOperation
        {
            private readonly Func<CancellationToken, T> _action;
            public CancellationToken CancellationToken { get; }
            public TaskCompletionSource<T> Completion { get; } =
                new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            public ArchiveOperation(Func<CancellationToken, T> action, CancellationToken cancellationToken)
            {
                _action = action;
                CancellationToken = cancellationToken;
            }

            public void Execute() => Completion.TrySetResult(_action(CancellationToken));
            public void Fail(Exception exception)
            {
                if (exception is OperationCanceledException canceled && canceled.CancellationToken == CancellationToken)
                    Completion.TrySetCanceled(CancellationToken);
                else
                    Completion.TrySetException(exception);
            }
        }
    }

    #endregion
}
