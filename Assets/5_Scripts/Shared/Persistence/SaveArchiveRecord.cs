using System;

namespace FlatWorld.Persistence
{
    #region 存档记录

    public static class SaveArchiveRecordKinds
    {
        public const string Global = "global";
        public const string World = "world";
        public const string Chunk = "chunk";
        public const string Entity = "entity";
        public const string Scope = "scope";
    }

    // 载荷必须由调用方冻结，异步操作结束前不得修改。
    public sealed class SaveArchiveRecord
    {
        public string Kind { get; }
        public string WorldKey { get; }
        public string Key { get; }
        public int ChunkX { get; }
        public int ChunkY { get; }
        public string SchemaId { get; }
        public int SchemaVersion { get; }
        public byte[] Payload { get; }
        public bool Deleted { get; }

        public SaveArchiveRecord(string kind, string worldKey, string key, int chunkX, int chunkY,
            string schemaId, int schemaVersion, byte[] payload, bool deleted = false)
        {
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
            WorldKey = worldKey ?? throw new ArgumentNullException(nameof(worldKey));
            Key = key ?? throw new ArgumentNullException(nameof(key));
            ChunkX = chunkX;
            ChunkY = chunkY;
            SchemaId = schemaId ?? throw new ArgumentNullException(nameof(schemaId));
            SchemaVersion = schemaVersion;
            Payload = payload;
            Deleted = deleted;
            if (!deleted && payload == null)
                throw new ArgumentNullException(nameof(payload));
        }
    }

    public sealed class SaveArchiveMetadata
    {
        public string ArchiveId { get; internal set; }
        public string DisplayName { get; internal set; }
        public long UpdatedUtcTicks { get; internal set; }
        public long CurrentGeneration { get; internal set; }
        public long PreviousGeneration { get; internal set; }
        internal long MetadataRevision { get; set; }
        public bool HasSnapshot => CurrentGeneration > 0;
        public bool HasPreviousSnapshot => PreviousGeneration > 0;
    }

    public sealed class SaveArchiveRecordKey
    {
        public string Kind { get; internal set; }
        public string WorldKey { get; internal set; }
        public string Key { get; internal set; }
        public int ChunkX { get; internal set; }
        public int ChunkY { get; internal set; }
        public string SchemaId { get; internal set; }
        public int SchemaVersion { get; internal set; }
    }

    #endregion
}
