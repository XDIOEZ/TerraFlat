using System;
using System.Collections.Generic;
using System.IO;

namespace FlatWorld.Persistence
{
    #region 记录格式与迁移

    public interface ISaveArchivePayloadMigration
    {
        string SchemaId { get; }
        int FromVersion { get; }
        int ToVersion { get; }
        byte[] Migrate(byte[] payload);
    }

    public sealed class SaveArchiveSchemaRegistry
    {
        private readonly Dictionary<string, int> _versions = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<(string, int), ISaveArchivePayloadMigration> _migrations =
            new Dictionary<(string, int), ISaveArchivePayloadMigration>();
        private readonly object _gate = new object();

        public void RegisterSchema(string schemaId, int currentVersion)
        {
            if (string.IsNullOrWhiteSpace(schemaId) || currentVersion < 1)
                throw new ArgumentException("存档 Schema 身份或版本无效。");
            lock (_gate)
                _versions.Add(schemaId, currentVersion);
        }

        public void RegisterMigration(ISaveArchivePayloadMigration migration)
        {
            if (migration == null || string.IsNullOrWhiteSpace(migration.SchemaId) ||
                migration.FromVersion < 1 || migration.ToVersion <= migration.FromVersion)
                throw new ArgumentException("存档迁移声明无效。", nameof(migration));
            lock (_gate)
            {
                if (!_versions.TryGetValue(migration.SchemaId, out int current) || migration.ToVersion > current)
                    throw new InvalidOperationException("存档迁移必须属于已登记的 Schema。");
                _migrations.Add((migration.SchemaId, migration.FromVersion), migration);
            }
        }

        public int GetCurrentVersion(string schemaId)
        {
            lock (_gate)
                if (schemaId != null && _versions.TryGetValue(schemaId, out int current))
                    return current;
            throw new InvalidDataException($"未登记的存档 Schema：{schemaId}");
        }

        // 升级只改变独立记录的载荷；未登记或更高版本必须明确拒绝。
        public SaveArchiveRecord MigrateRecord(SaveArchiveRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            int current = GetCurrentVersion(record.SchemaId);
            if (record.SchemaVersion < 1 || record.SchemaVersion > current)
                throw new InvalidDataException($"不支持的存档版本：{record.SchemaId}/{record.SchemaVersion}");
            if (record.SchemaVersion == current)
                return record;

            int version = record.SchemaVersion;
            byte[] payload = record.Payload == null ? null : (byte[])record.Payload.Clone();
            while (version < current)
            {
                ISaveArchivePayloadMigration migration;
                lock (_gate)
                    if (!_migrations.TryGetValue((record.SchemaId, version), out migration))
                        throw new InvalidDataException($"缺少存档迁移：{record.SchemaId}/{version}");
                payload = migration.Migrate(payload);
                if (payload == null && !record.Deleted)
                    throw new InvalidDataException($"存档迁移返回空载荷：{record.SchemaId}/{version}");
                version = migration.ToVersion;
            }
            return new SaveArchiveRecord(record.Kind, record.WorldKey, record.Key, record.ChunkX, record.ChunkY,
                record.SchemaId, version, payload, record.Deleted);
        }
    }

    #endregion
}
