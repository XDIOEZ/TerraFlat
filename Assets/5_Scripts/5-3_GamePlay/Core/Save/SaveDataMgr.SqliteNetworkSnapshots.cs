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
    #region 区块网络快照
    public async Task<byte[]> CaptureNetworkChunkSnapshotAsync(string worldKey, Vector2Int origin, CancellationToken cancellationToken = default)
    {
        Vector2Int region = WeatherMgr.ResolveRegionalWeatherStorageOrigin(worldKey, origin);
        using var weatherLease = WeatherMgr.AcquireRegionalPersistenceLease(worldKey, region);
        await Task.WhenAll(EnsurePersistedChunkLoadedAsync(worldKey, origin, cancellationToken),
            EnsurePersistedWeatherRegionLoadedAsync(worldKey, region, cancellationToken));
        return CaptureNetworkChunkSnapshot(worldKey, origin);
    }

    public byte[] CaptureNetworkChunkSnapshot(string worldKey, Vector2Int origin)
    {
        using var scope = GameplayWorldRegistry.UseWorld(worldKey);
        if (!CaptureRuntimeTerrainStates()) throw new InvalidOperationException("网络区块地形采集失败。");
        if (GameplayWorldRegistry.TryGet(worldKey, out var runtime) && runtime.Chunks != null)
        {
            runtime.Chunks.CaptureRuntimeNaturalItemStates();
            if (runtime.Chunks.Chunk_Dic_ByPos.TryGetValue(origin, out Chunk legacyChunk) && legacyChunk != null &&
                !TrySaveChunkDifferences(legacyChunk)) throw new InvalidOperationException("网络区块实例采集失败。");
        }
        CaptureRuntimeBuildingStates();
        if (!CaptureRuntimeAiEntityStates()) throw new InvalidOperationException("网络区块实体采集失败。");
        using var policy = ItemSnapshotSerialization.BeginPublicPlayerState(MachineInventoryCommands.PublicSpecialData);
        Vector2Int region = WeatherMgr.ResolveRegionalWeatherStorageOrigin(worldKey, origin);
        var weather = SaveData.PlanetData_Dict[worldKey].RegionalWeatherStates.Find(value => value.RegionX == region.x && value.RegionY == region.y);
        return MemoryPackSerializer.Serialize(new NetworkArchiveChunkSnapshot
        {
            Chunk = MemoryPackSerializer.Serialize(CaptureSqliteChunk(worldKey, origin)),
            Weather = weather == null ? null : MemoryPackSerializer.Serialize(weather)
        });
    }

    public void ApplyNetworkChunkSnapshot(byte[] bytes, string worldKey, Vector2Int origin, long worldEpoch = 0, long snapshotRevision = 0)
    {
        var snapshot = MemoryPackSerializer.Deserialize<NetworkArchiveChunkSnapshot>(bytes);
        var chunk = DecodeSqliteChunkSnapshot(worldKey, origin, MemoryPackSerializer.Deserialize<SqliteChunkSnapshot>(snapshot.Chunk));
        Vector2Int region = WeatherMgr.ResolveRegionalWeatherStorageOrigin(worldKey, origin);
        var weather = DecodeSqliteWeatherRecord(region, snapshot.Weather);
        ApplyDecodedSqliteChunkSnapshot(worldKey, origin, chunk);
        var regionKey = (worldKey, region);
        if (!networkWeatherRevisions.TryGetValue(regionKey, out var previousWeather) ||
            previousWeather.epoch != worldEpoch || snapshotRevision > previousWeather.revision)
        {
            var list = SaveData.PlanetData_Dict[worldKey].RegionalWeatherStates;
            list.RemoveAll(value => value.RegionX == region.x && value.RegionY == region.y);
            if (weather != null) list.Add(weather);
            networkWeatherRevisions[regionKey] = (worldEpoch, snapshotRevision);
            PublishReadyArchiveCacheRecord("weather", worldKey, region, snapshot.Weather);
        }
        PublishReadyArchiveCacheRecord("chunk", worldKey, origin, snapshot.Chunk);
        TrimReleasedSqliteChunkCache();
    }

    private void PublishReadyArchiveCacheRecord(string kind, string worldKey, Vector2Int origin, byte[] payload)
    {
        string key = ArchiveRecordCacheKey(kind, worldKey, origin);
        if (persistedChunkReads.TryGetValue(key, out var previous))
            previous.RuntimePinCancellation.Dispose();
        long bytes = payload?.LongLength ?? 0;
        persistedChunkReads[key] = new PersistedChunkRead
        {
            Kind = kind, World = worldKey, Origin = origin, Ready = true, Materialized = true,
            Task = Task.CompletedTask, Access = ++persistedChunkAccess, Bytes = bytes,
            LoadedRecord = ArchiveRecord(kind, worldKey, kind == "chunk" ? "chunk" : origin.x + ":" + origin.y, payload, origin.x, origin.y, payload == null)
        };
    }

    public byte[] CaptureNetworkBootstrapSnapshot(string profileId = null, string worldKey = null)
    {
        using var policy = ItemSnapshotSerialization.BeginPublicPlayerState(MachineInventoryCommands.PublicSpecialData);
        profileId ??= CurrentContrrolPlayerName;
        worldKey ??= GameplayWorldRegistry.ExecutionWorldKey;
        string privateSpace = SaveData.SpaceStateJson;
        var players = SaveData.PlayerData_Dict;
        var worlds = SaveData.PlanetData_Dict;
        var spawners = SaveData.MonsterSpawnerWorlds;
        try
        {
            FlatWorld.Spaceflight.SpaceSession.Capture(SaveData);
            var observerSpace = FlatWorld.Spaceflight.SpaceSession.Current?.CaptureBootstrapObserverState(profileId);
            SaveData.SpaceStateJson = observerSpace == null ? CaptureBootstrapSpaceMetadata(SaveData.SpaceStateJson, worldKey)
                : Newtonsoft.Json.JsonConvert.SerializeObject(observerSpace);
            SaveData.PlayerData_Dict = players.TryGetValue(profileId ?? "", out var player)
                ? new Dictionary<string, Data_Player> { [profileId] = player } : new();
            SaveData.PlanetData_Dict = worlds.TryGetValue(worldKey ?? "", out var world)
                ? new Dictionary<string, PlanetData> { [worldKey] = world } : new();
            SaveData.MonsterSpawnerWorlds = new();
            return SerializeCoreDataWithoutChunks(SaveData);
        }
        finally
        {
            SaveData.SpaceStateJson = privateSpace;
            SaveData.PlayerData_Dict = players; SaveData.PlanetData_Dict = worlds; SaveData.MonsterSpawnerWorlds = spawners;
        }
    }

    public void ApplyNetworkBootstrapSnapshot(byte[] bytes)
    { CloseSqliteArchive(); ResetChunkDifferenceState(); SaveData = MemoryPackSerializer.Deserialize<GameSaveData>(bytes); }
    #endregion
}
