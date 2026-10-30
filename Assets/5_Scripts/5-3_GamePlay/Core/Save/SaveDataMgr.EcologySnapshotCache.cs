using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using MemoryPack;

public partial class SaveDataMgr
{
    #region 生态冻结快照复用

    // 缓存跟随存档根和区块对象释放，不持有失效世界或累计旧物品快照。
    private static readonly ConditionalWeakTable<GameSaveData, EcologyFrozenSnapshotCache> EcologyFrozenCaches = new();
    private static readonly ConditionalWeakTable<Dictionary<ItemData, byte[]>, EcologyFrozenItemsRevision> EcologyFrozenItemsRevisions = new();

    private sealed class EcologyFrozenSnapshotCache
    {
        public readonly ConditionalWeakTable<EcologyChunkSaveData, EcologyFrozenChunkCacheEntry> Chunks = new();
    }

    private sealed class EcologyFrozenChunkCacheEntry
    {
        public EcologyFrozenChunkSnapshot Snapshot;
    }

    private sealed class EcologyFrozenChunkSnapshot
    {
        public EcologyFrozenChunkMetadata Metadata;
        public ItemData[] ItemSources;
        public FrozenEcologyChunkSaveRecord Record;
        public ulong CodecRevision;
    }

    private sealed class EcologyFrozenItemsRevision
    {
        public bool Initialized;
        public ulong Revision;
        public bool PublicPlayerState;
    }

    private sealed class EcologyFrozenChunkMetadata
    {
        private readonly int chunkX, chunkY;
        private readonly int[] removedGuids;
        private readonly Dictionary<int, int> renewalYears;

        public EcologyFrozenChunkMetadata(EcologyChunkSaveData chunk)
        {
            chunkX = chunk.ChunkX;
            chunkY = chunk.ChunkY;
            removedGuids = chunk.RemovedGuids?.ToArray();
            renewalYears = chunk.RenewalYears == null ? null : new Dictionary<int, int>(chunk.RenewalYears);
        }

        public bool Matches(EcologyChunkSaveData chunk)
        {
            if (chunkX != chunk.ChunkX || chunkY != chunk.ChunkY ||
                (removedGuids == null) != (chunk.RemovedGuids == null) ||
                (renewalYears == null) != (chunk.RenewalYears == null)) return false;
            if (removedGuids != null)
            {
                if (removedGuids.Length != chunk.RemovedGuids.Count) return false;
                for (int i = 0; i < removedGuids.Length; i++)
                    if (removedGuids[i] != chunk.RemovedGuids[i]) return false;
            }
            if (renewalYears != null)
            {
                if (renewalYears.Count != chunk.RenewalYears.Count) return false;
                foreach (KeyValuePair<int, int> pair in chunk.RenewalYears)
                    if (!renewalYears.TryGetValue(pair.Key, out int year) || year != pair.Value) return false;
            }
            return true;
        }
    }

    private static IEnumerable<int> FreezeEcologyChunksWithReusableSnapshots(
        GameSaveData saveData, List<FrozenEcologyChunkSaveRecord> destination,
        Dictionary<ItemData, byte[]> frozenItems)
    {
        if (saveData.PlanetData_Dict == null) yield break;
        EcologyFrozenSnapshotCache cache = EcologyFrozenCaches.GetValue(saveData, _ => new EcologyFrozenSnapshotCache());
        List<string> worldKeys = new(saveData.PlanetData_Dict.Keys);
        worldKeys.Sort(StringComparer.Ordinal);
        foreach (string worldKey in worldKeys)
        {
            List<EcologyChunkSaveData> chunks = saveData.PlanetData_Dict[worldKey]?.Ecology?.Chunks;
            if (chunks == null) continue;
            foreach (EcologyChunkSaveData chunk in chunks.ToArray())
            {
                if (chunk == null) throw new InvalidDataException("生态存档包含空区块。");
                foreach (int work in FreezeOneEcologyChunkWithReusableSnapshots(worldKey, chunk, destination, frozenItems, cache))
                    yield return work;
            }
        }
    }

    private static IEnumerable<int> FreezeOneEcologyChunkWithReusableSnapshots(
        string worldKey, EcologyChunkSaveData chunk, List<FrozenEcologyChunkSaveRecord> destination,
        Dictionary<ItemData, byte[]> frozenItems, EcologyFrozenSnapshotCache cache)
    {
        EcologyFrozenChunkCacheEntry entry = cache.Chunks.GetValue(chunk, _ => new EcologyFrozenChunkCacheEntry());
        List<ItemData> items = chunk.ChangedItems;
        EcologyFrozenChunkSnapshot previous = entry.Snapshot;
        ulong codecRevision = ModuleInstanceStateCodecs.Revision;
        bool publicPlayerState = ItemSnapshotSerialization.IsPublicPlayerStateCapture;
        UpdateEcologyFrozenItemsCapturePolicy(frozenItems, codecRevision, publicPlayerState);
        bool sameItems = EcologySnapshotItemsMatch(items, previous?.ItemSources, out bool cacheable);
        if (publicPlayerState || !cacheable)
        {
            // 公开快照可能嵌套角色，不能复用或覆盖本地私有字节。
            if (!publicPlayerState) entry.Snapshot = null;
            previous = null;
            sameItems = false;
            cacheable = false;
        }
        bool sameMetadata = previous != null && previous.Metadata.Matches(chunk);
        bool sameCodecs = previous != null && previous.CodecRevision == codecRevision;
        if (sameItems && sameMetadata && sameCodecs && string.Equals(previous.Record.WorldKey, worldKey, StringComparison.Ordinal))
        {
            destination.Add(previous.Record);
            yield return 1;
            yield break;
        }

        EcologyFrozenChunkMetadata metadata = sameMetadata ? previous.Metadata : new EcologyFrozenChunkMetadata(chunk);
        byte[] chunkBytes;
        if (sameMetadata) chunkBytes = previous.Record.ChunkData;
        else
        {
            try
            {
                chunk.ChangedItems = null;
                using (SaveEcologyMetadataPoint.Measure()) chunkBytes = MemoryPackSerializer.Serialize(chunk);
            }
            finally { chunk.ChangedItems = items; }
        }

        if (sameItems && sameCodecs)
        {
            var updated = new FrozenEcologyChunkSaveRecord
            {
                WorldKey = worldKey, ChunkData = chunkBytes, ChangedItems = previous.Record.ChangedItems
            };
            entry.Snapshot = new EcologyFrozenChunkSnapshot
            {
                Metadata = metadata, ItemSources = previous.ItemSources, Record = updated, CodecRevision = codecRevision
            };
            destination.Add(updated);
            yield return 1;
            yield break;
        }

        ItemData[] itemSources = items?.ToArray() ?? Array.Empty<ItemData>();
        Dictionary<ItemData, byte[]> oldItems = null;
        if (sameCodecs && previous.ItemSources.Length > 0)
        {
            oldItems = new Dictionary<ItemData, byte[]>(previous.ItemSources.Length, EcologyItemReferenceComparer.Instance);
            for (int i = 0; i < previous.ItemSources.Length; i++)
                if (previous.ItemSources[i] != null) oldItems[previous.ItemSources[i]] = previous.Record.ChangedItems[i];
        }
        var record = new FrozenEcologyChunkSaveRecord
        {
            WorldKey = worldKey, ChunkData = chunkBytes, ChangedItems = new List<byte[]>(itemSources.Length)
        };
        destination.Add(record);
        yield return 1;
        bool stablePolicy = true;
        foreach (ItemData item in itemSources)
        {
            ulong currentRevision = ModuleInstanceStateCodecs.Revision;
            bool currentPublicPlayerState = ItemSnapshotSerialization.IsPublicPlayerStateCapture;
            if (currentPublicPlayerState != publicPlayerState) stablePolicy = false;
            UpdateEcologyFrozenItemsCapturePolicy(frozenItems, currentRevision, currentPublicPlayerState);
            byte[] bytes;
            if (currentPublicPlayerState || item == null || item is Data_Player ||
                !(currentRevision == codecRevision && oldItems != null && oldItems.TryGetValue(item, out bytes) ||
                  frozenItems != null && frozenItems.TryGetValue(item, out bytes)))
            {
                using (SaveEcologyItemPoint.Measure()) bytes = ItemSnapshotSerialization.SerializePayload(item == null ? null : ItemSnapshotSerialization.Capture(item));
            }
            if (!currentPublicPlayerState && item != null && item is not Data_Player && frozenItems != null) frozenItems[item] = bytes;
            record.ChangedItems.Add(bytes);
            yield return 1;
        }
        if (cacheable && stablePolicy && !ItemSnapshotSerialization.IsPublicPlayerStateCapture &&
            ModuleInstanceStateCodecs.Revision == codecRevision)
            entry.Snapshot = new EcologyFrozenChunkSnapshot
            {
                Metadata = metadata, ItemSources = itemSources, Record = record, CodecRevision = codecRevision
            };
    }

    // 编解码契约或公开策略变化后，不能复用此前冻结的同一次采集字节。
    private static void UpdateEcologyFrozenItemsCapturePolicy(Dictionary<ItemData, byte[]> frozenItems, ulong revision, bool publicPlayerState)
    {
        if (frozenItems == null) return;
        EcologyFrozenItemsRevision state = EcologyFrozenItemsRevisions.GetValue(frozenItems, _ => new EcologyFrozenItemsRevision());
        if (state.Initialized && (state.Revision != revision || state.PublicPlayerState != publicPlayerState)) frozenItems.Clear();
        state.Initialized = true;
        state.Revision = revision;
        state.PublicPlayerState = publicPlayerState;
    }

    private static bool EcologySnapshotItemsMatch(List<ItemData> items, ItemData[] previous, out bool cacheable)
    {
        int count = items?.Count ?? 0;
        bool same = previous != null && previous.Length == count;
        cacheable = true;
        for (int i = 0; i < count; i++)
        {
            ItemData item = items[i];
            if (item is Data_Player) cacheable = false;
            if (same && !ReferenceEquals(item, previous[i])) same = false;
        }
        return same;
    }

    #endregion
}
