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
    #region 区块异步准备与受限缓存
    public Task EnsurePersistedChunkLoadedAsync(string worldKey, Vector2Int origin, CancellationToken cancellationToken = default, bool retainForRuntime = false)
        => EnsurePersistedRecordLoadedAsync("chunk", worldKey, origin, cancellationToken, retainForRuntime);

    public Task EnsurePersistedWeatherRegionLoadedAsync(string worldKey, Vector2Int regionOrigin, CancellationToken cancellationToken = default)
        => EnsurePersistedRecordLoadedAsync("weather", worldKey, regionOrigin, cancellationToken, true);

    private Task EnsurePersistedRecordLoadedAsync(string kind, string worldKey, Vector2Int origin, CancellationToken cancellationToken, bool retainForRuntime)
    {
        if (archiveStore == null && ReferenceEquals(instance, this) && FlatWorld.Networking.GameNetwork.HasStateAuthority && !string.IsNullOrEmpty(UserSavePath))
        {
            SaveData.ArchiveId ??= Guid.NewGuid().ToString("N");
            OpenSqliteArchive(Path.Combine(UserSavePath, SaveData.ArchiveId, "save.sqlite"), true);
        }
        string key = ArchiveRecordCacheKey(kind, worldKey, origin);
        if (persistedChunkReads.TryGetValue(key, out var canceledRead) && !canceledRead.Ready && canceledRead.ReadCancellation?.IsCancellationRequested == true)
        { canceledRead.RuntimePinCancellation.Dispose(); persistedChunkReads.Remove(key); }
        if (persistedChunkReads.TryGetValue(key, out PersistedChunkRead state))
        {
            state.Access = ++persistedChunkAccess;
            if (state.Ready) MaterializeCachedArchiveRecord(state);
        }
        else
        {
            state = new PersistedChunkRead { Kind = kind, World = worldKey, Origin = origin, Access = ++persistedChunkAccess, Released = !retainForRuntime };
            persistedChunkReads.Add(key, state);
            state.ReadCancellation = CancellationTokenSource.CreateLinkedTokenSource(archiveReadCancellation.Token);
            state.Task = ReadPersistedChunkAsync(key, state, archiveStore, archiveSession, SaveData, state.ReadCancellation.Token);
        }
        if (retainForRuntime)
        {
            state.Released = false;
            if (kind != "chunk") return WaitForArchiveReadCallerAsync(state, cancellationToken);
            state.RuntimePinCancellation.Dispose();
            long generation = ++state.RuntimePinGeneration;
            var retained = state;
            state.RuntimePinCancellation = cancellationToken.Register(() =>
            {
                if (persistedChunkReads.TryGetValue(key, out var current) && ReferenceEquals(current, retained) &&
                    current.RuntimePinGeneration == generation) current.Released = true;
            });
        }
        return WaitForArchiveReadCallerAsync(state, cancellationToken);
    }

    private static async Task WaitForArchiveReadCallerAsync(PersistedChunkRead state, CancellationToken cancellation)
    {
        state.ReadWaiters++;
        try { await WaitForSharedChunkReadAsync(state.Task, cancellation); }
        finally
        {
            state.ReadWaiters--;
            if (state.ReadWaiters == 0 && !state.Ready && !state.Task.IsCompleted) state.ReadCancellation?.Cancel();
        }
    }

    // 单个观察者离开只取消等候，共享读取只随存档会话结束取消。
    private static async Task WaitForSharedChunkReadAsync(Task shared, CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled) { await shared; return; }
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => canceled.TrySetCanceled(cancellationToken)))
        {
            Task completed = await Task.WhenAny(shared, canceled.Task);
            await completed;
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private async Task ReadPersistedChunkAsync(string key, PersistedChunkRead state, ISaveArchiveStore store,
        string session, GameSaveData source, CancellationToken cancellationToken)
    {
        try
        {
            if (persistedChunkStages.TryGetValue(key, out Task staged)) await staged;
            SaveArchiveRecord record = store == null ? null : state.Kind == "chunk"
                ? await store.ReadLiveChunkAsync(session, state.World, state.Origin.x, state.Origin.y, cancellationToken)
                : await store.ReadLiveRecordAsync(session, state.Kind, state.World, state.Origin.x + ":" + state.Origin.y, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(source, SaveData) || !ReferenceEquals(store, archiveStore) ||
                !persistedChunkReads.TryGetValue(key, out var current) || !ReferenceEquals(current, state))
                throw new OperationCanceledException("存档会话已切换。");
            if (record != null)
            {
                record = UpgradeSqliteRecord(record);
                state.LoadedRecord = record;
                state.Bytes = record.Payload.LongLength;
            }
            MaterializeCachedArchiveRecord(state);
            state.Ready = true;
            TrimReleasedSqliteChunkCache(key);
        }
        catch (OperationCanceledException) { state.RuntimePinCancellation.Dispose(); if (persistedChunkReads.TryGetValue(key, out var current) && ReferenceEquals(current, state)) persistedChunkReads.Remove(key); throw; }
        catch (Exception exception)
        {
            state.RuntimePinCancellation.Dispose();
            if (ReferenceEquals(store, archiveStore) && ReferenceEquals(source, SaveData))
            {
                if (persistedChunkReads.TryGetValue(key, out var current) && ReferenceEquals(current, state)) persistedChunkReads.Remove(key);
                LastArchiveError = exception.Message;
            }
            throw;
        }
        finally { state.ReadCancellation?.Dispose(); }
    }

    public bool IsPersistedChunkReady(string worldKey, Vector2Int origin) => IsPersistedRecordReady("chunk", worldKey, origin);

    public bool IsPersistedWeatherRegionReady(string worldKey, Vector2Int origin) => IsPersistedRecordReady("weather", worldKey, origin);

    private bool IsPersistedRecordReady(string kind, string worldKey, Vector2Int origin)
    {
        if (!persistedChunkReads.TryGetValue(ArchiveRecordCacheKey(kind, worldKey, origin), out var state) || !state.Ready) return false;
        MaterializeCachedArchiveRecord(state);
        state.Access = ++persistedChunkAccess;
        return true;
    }

    private void MaterializeCachedArchiveRecord(PersistedChunkRead state)
    {
        if (state.Materialized) return;
        SaveArchiveRecord record = state.LoadedRecord;
        if (state.Kind == "chunk")
        {
            if (record?.Deleted != true && record?.Payload != null)
                ApplySqliteChunkSnapshot(state.World, state.Origin, MemoryPackSerializer.Deserialize<SqliteChunkSnapshot>(record.Payload));
        }
        else
        {
            var weather = DecodeSqliteWeatherRecord(state.Origin, record?.Deleted != true ? record?.Payload : null);
            var planet = SaveData.PlanetData_Dict[state.World];
            planet.RegionalWeatherStates.RemoveAll(value => value.RegionX == state.Origin.x && value.RegionY == state.Origin.y);
            if (weather != null) planet.RegionalWeatherStates.Add(weather);
        }
        state.Materialized = true;
    }

    public void ReleasePersistedChunkIfUnleased(string worldKey, Vector2Int origin)
        => ReleasePersistedRecordIfUnleased("chunk", worldKey, origin);

    public void ReleasePersistedWeatherRegionIfUnleased(string worldKey, Vector2Int origin)
        => ReleasePersistedRecordIfUnleased("weather", worldKey, origin);

    private void ReleasePersistedRecordIfUnleased(string kind, string worldKey, Vector2Int origin)
    {
        string key = ArchiveRecordCacheKey(kind, worldKey, origin);
        if (!persistedChunkReads.TryGetValue(key, out PersistedChunkRead state) || state.Released) return;
        state.Released = true;
        state.RuntimePinCancellation.Dispose();
        if (!state.Ready) return;
        StageReleasedSqliteChunk(key, state);
        TrimReleasedSqliteChunkCache();
    }

    private bool StageReleasedSqliteChunk(string key, PersistedChunkRead state)
    {
        if (!state.Materialized) return true;
        if (archiveStore == null) return true;
        if (persistedChunkStages.TryGetValue(key, out Task pending) && !pending.IsCompleted) return false;
        SaveArchiveRecord record;
        if (state.Kind == "weather")
        {
            var weather = SaveData.PlanetData_Dict[state.World].RegionalWeatherStates.Find(value => value.RegionX == state.Origin.x && value.RegionY == state.Origin.y);
            record = ArchiveRecord("weather", state.World, state.Origin.x + ":" + state.Origin.y,
                weather == null ? null : MemoryPackSerializer.Serialize(weather), state.Origin.x, state.Origin.y, weather == null);
        }
        else record = FreezeSqliteChunk(state.World, state.Origin);
        if (pending?.Status == TaskStatus.RanToCompletion && state.StagedRecord != null &&
            record.Deleted == state.StagedRecord.Deleted &&
            (record.Payload == null || state.StagedRecord.Payload != null && record.Payload.SequenceEqual(state.StagedRecord.Payload))) return true;
        state.StagedRecord = record;
        state.LoadedRecord = record;
        state.Bytes = record.Payload?.LongLength ?? 0;
        Task task = archiveStore.StageRecordsAsync(archiveSession, ++archiveRecordRevision, new[] { record });
        persistedChunkStages[key] = task;
        _ = CompleteReleasedChunkStageAsync(key, state, task);
        return false;
    }

    private async Task CompleteReleasedChunkStageAsync(string key, PersistedChunkRead state, Task task)
    {
        try { await task; }
        catch (Exception exception)
        {
            if (persistedChunkReads.TryGetValue(key, out var current) && ReferenceEquals(current, state))
                LastArchiveError = exception.Message;
            return;
        }
        if (persistedChunkReads.TryGetValue(key, out var latest) && ReferenceEquals(latest, state))
        {
            if (state.Released && !IsEcologySnapshotInProgress && StageReleasedSqliteChunk(key, state)) DetachReleasedArchiveRecord(state);
            TrimReleasedSqliteChunkCache();
        }
    }

    private void TrimReleasedSqliteChunkCache(string completingReadKey = null)
    {
        if (IsEcologySnapshotInProgress) return;
        var released = persistedChunkReads.Where(pair => pair.Value.Ready && pair.Value.Released).OrderBy(pair => pair.Value.Access).ToArray();
        int count = released.Length;
        long bytes = released.Sum(pair => pair.Value.Bytes);
        foreach (var candidate in released)
        {
            if (persistedChunkStages.TryGetValue(candidate.Key, out Task stage) && !stage.IsCompleted) continue;
            if (candidate.Key == completingReadKey) continue;
            PersistedChunkRead state = candidate.Value;
            if (!StageReleasedSqliteChunk(candidate.Key, state)) return;
            DetachReleasedArchiveRecord(state);
            if (count <= ArchiveCacheCount && bytes <= ArchiveCacheBytes) continue;
            persistedChunkReads.Remove(candidate.Key); persistedChunkStages.Remove(candidate.Key);
            // 排序基线与有界区域缓存同寿命；在途请求持有区域租约，不能在此逐出。
            if (state.Kind == "weather") networkWeatherRevisions.Remove((state.World, state.Origin));
            state.RuntimePinCancellation.Dispose();
            count--; bytes -= state.Bytes;
        }
    }

    // 可逐出的缓存只保留编码字节，活动区块/后台模拟仍由各自系统持有。
    private void DetachReleasedArchiveRecord(PersistedChunkRead state)
    {
        if (!state.Materialized) return;
        if (SaveData?.PlanetData_Dict?.TryGetValue(state.World, out PlanetData planet) == true)
        {
            if (state.Kind == "weather") planet.RegionalWeatherStates.RemoveAll(value => value.RegionX == state.Origin.x && value.RegionY == state.Origin.y);
            else
            {
                string chunkKey = BuildChunkKey(state.World, state.Origin.ToString());
                chunkDeltas.Remove(chunkKey); runtimeChunkBaselines.Remove(chunkKey);
                chunkDifferenceRevisions.Remove(chunkKey);
                planet.Ecology?.Chunks?.RemoveAll(chunk => chunk.ChunkX == state.Origin.x && chunk.ChunkY == state.Origin.y);
            }
        }
        state.Materialized = false;
    }

    private static string ArchiveChunkCacheKey(string world, Vector2Int origin) => world + "\u001f" + origin.x + ":" + origin.y;
    private static string ArchiveRecordCacheKey(string kind, string world, Vector2Int origin) =>
        kind == "chunk" ? ArchiveChunkCacheKey(world, origin) : kind + "\u001f" + ArchiveChunkCacheKey(world, origin);

    private void ApplySqliteChunkSnapshot(string world, Vector2Int origin, SqliteChunkSnapshot snapshot)
        => ApplyDecodedSqliteChunkSnapshot(world, origin, DecodeSqliteChunkSnapshot(world, origin, snapshot));

    // 完整解码后再发布，失败读取不会留下半份地形或生态状态。
    private static (ChunkSaveRecord Terrain, EcologyChunkSaveData Ecology) DecodeSqliteChunkSnapshot(string world, Vector2Int origin, SqliteChunkSnapshot snapshot)
    {
        if (snapshot == null) throw new InvalidDataException("区块记录为空。");
        var terrain = snapshot.Terrain == null ? null : MemoryPackSerializer.Deserialize<ChunkSaveRecord>(snapshot.Terrain);
        if (terrain != null && (terrain.PlanetName != world || terrain.ChunkPosition != origin))
            throw new InvalidDataException("地形资料越过请求区块。");
        EcologyChunkSaveData ecology = null;
        if (snapshot.Ecology != null)
        {
            ecology = MemoryPackSerializer.Deserialize<EcologyChunkSaveData>(snapshot.Ecology.ChunkData);
            if (snapshot.Ecology.WorldKey != world || ecology == null || ecology.ChunkX != origin.x || ecology.ChunkY != origin.y)
                throw new InvalidDataException("生态资料越过请求区块。");
            ecology.ChangedItems = new List<ItemData>();
            foreach (byte[] item in snapshot.Ecology.ChangedItems ?? new List<byte[]>())
                ecology.ChangedItems.Add(item == null ? null : MemoryPackSerializer.Deserialize<ItemInstanceSnapshot>(item)?.CreateColdData());
        }
        return (terrain, ecology);
    }

    private void ApplyDecodedSqliteChunkSnapshot(string world, Vector2Int origin, (ChunkSaveRecord Terrain, EcologyChunkSaveData Ecology) decoded)
    {
        var planet = SaveData.PlanetData_Dict[world];
        string key = BuildChunkKey(world, origin.ToString());
        if (decoded.Terrain != null) chunkDeltas[key] = decoded.Terrain;
        else chunkDeltas.Remove(key);
        planet.Ecology ??= new EcologyWorldSaveData();
        planet.Ecology.Chunks ??= new();
        planet.Ecology.Chunks.RemoveAll(chunk => chunk.ChunkX == origin.x && chunk.ChunkY == origin.y);
        if (decoded.Ecology != null) planet.Ecology.Chunks.Add(decoded.Ecology);
    }

    private static RegionalWeatherRecord DecodeSqliteWeatherRecord(Vector2Int region, byte[] payload)
    {
        if (payload == null) return null;
        var weather = MemoryPackSerializer.Deserialize<RegionalWeatherRecord>(payload);
        if (weather == null || weather.RegionX != region.x || weather.RegionY != region.y)
            throw new InvalidDataException("天气资料越过请求区域。");
        return weather;
    }
    #endregion
}
