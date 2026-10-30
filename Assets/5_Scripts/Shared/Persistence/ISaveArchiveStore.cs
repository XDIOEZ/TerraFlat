using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FlatWorld.Persistence
{
    #region 存档读写接口

    public interface ISaveArchiveStore : IDisposable
    {
        Task<SaveArchiveRecord> ReadChunkAsync(string worldKey, int x, int y,
            CancellationToken cancellationToken = default);

        Task<SaveArchiveRecord> ReadLiveChunkAsync(string sessionId, string worldKey, int x, int y,
            CancellationToken cancellationToken = default);

        Task<SaveArchiveRecord> ReadRecordAsync(string kind, string worldKey, string key,
            CancellationToken cancellationToken = default);

        Task<SaveArchiveRecord> ReadLiveRecordAsync(string sessionId, string kind, string worldKey, string key,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SaveArchiveRecord>> ReadRecordsAsync(string kind, string worldKey,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SaveArchiveRecordKey>> ReadRecordKeysAsync(string kind, string worldKey = null,
            CancellationToken cancellationToken = default);

        Task StageRecordsAsync(string sessionId, long revision, IReadOnlyList<SaveArchiveRecord> records,
            CancellationToken cancellationToken = default);

        Task StageWorldRemovalAsync(string sessionId, long revision, string worldKey,
            CancellationToken cancellationToken = default);

        void ReserveSaveRevision(string sessionId, long revision);

        Task CommitSnapshotAsync(string sessionId, long revision, string displayName,
            IReadOnlyList<SaveArchiveRecord> records, CancellationToken cancellationToken = default);

        Task<SaveArchiveMetadata> ReadMetadataAsync(CancellationToken cancellationToken = default);

        Task RenameDisplayNameAsync(string displayName, CancellationToken cancellationToken = default);

        Task ReidentifyArchiveAsync(string newArchiveId, string displayName, CancellationToken cancellationToken = default);

        Task<bool> RestorePreviousSnapshotAsync(CancellationToken cancellationToken = default);

        Task BackupAsync(string destinationPath, CancellationToken cancellationToken = default);

        Task ForkAsync(string destinationPath, string newArchiveId, string displayName,
            CancellationToken cancellationToken = default);

        Task DiscardSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    }

    #endregion
}
