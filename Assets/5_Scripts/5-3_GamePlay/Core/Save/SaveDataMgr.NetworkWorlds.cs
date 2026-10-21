using System;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
using UnityEngine;

[MemoryPackable]
public partial class NetworkWorldSaveSnapshot
{
    public string WorldKey;
    public byte[] Core;
    public List<byte[]> Chunks = new();
    public List<FrozenEcologyChunkSaveRecord> Ecology = new();
    public MachineArchive Machines = new();
    public List<DroppedItemSaveRecord> Drops = new();
}

public partial class SaveDataMgr
{
    #region 按世界冻结与合并联机资料
    // 切维度只传目标世界，不替换正在使用的存档根、玩家数据或其它世界运行时。
    public byte[] CaptureNetworkWorldSnapshot(string worldKey)
    {
        if (SaveData == null || !SaveData.PlanetData_Dict.TryGetValue(worldKey, out PlanetData planet))
            throw new InvalidDataException("目标世界没有正式星球资料：" + worldKey);
        using var scope = GameplayWorldRegistry.UseWorld(worldKey);
        PrepareLoadedChunksForSave();
        _ = MachineWorld.CaptureArchive(SaveData);
        _ = DroppedItemService.CaptureArchive(SaveData);
        var subset = new GameSaveData { Seed = SaveData.Seed, Time = SaveData.Time, TimeSpeed = SaveData.TimeSpeed,
            WorldGenerationConfigMode = SaveData.WorldGenerationConfigMode, DayTimeData = SaveData.DayTimeData,
            DimensionStateJson = SaveData.DimensionStateJson,
            MonsterSpawnerWorlds = new Dictionary<string, MonsterSpawnerSaveData> { [worldKey] = SaveData.GetMonsterSpawnerData(worldKey) },
            PlanetData_Dict = new Dictionary<string, PlanetData> { [worldKey] = planet } };
        var snapshot = new NetworkWorldSaveSnapshot { WorldKey = worldKey };
        foreach (int _ in FreezeEcologyChunks(subset, snapshot.Ecology)) { }
        using (ItemSnapshotSerialization.BeginPublicPlayerState(MachineInventoryCommands.PublicSpecialData))
        {
            snapshot.Core = SerializeCoreDataWithoutChunks(subset);
            foreach (ChunkSaveRecord record in chunkDeltas.Values)
                if (record?.PlanetName == worldKey && record.HasChanges) snapshot.Chunks.Add(MemoryPackSerializer.Serialize(record));
            if (SaveData.Mechanical?.Worlds.TryGetValue(worldKey, out List<ItemData> nodes) == true) snapshot.Machines.Worlds[worldKey] = nodes;
            if (SaveData.Mechanical?.RemovedGenerated.TryGetValue(worldKey, out HashSet<int> removed) == true) snapshot.Machines.RemovedGenerated[worldKey] = removed;
            if (SaveData.DroppedItems?.Worlds.TryGetValue(worldKey, out List<DroppedItemSaveRecord> drops) == true) snapshot.Drops = drops;
            return MemoryPackSerializer.Serialize(snapshot);
        }
    }

    public string ApplyNetworkWorldSnapshot(byte[] bytes, string expectedWorldKey)
    {
        NetworkWorldSaveSnapshot snapshot = MemoryPackSerializer.Deserialize<NetworkWorldSaveSnapshot>(bytes);
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.WorldKey) || snapshot.WorldKey != expectedWorldKey || snapshot.Core == null ||
            snapshot.Chunks == null || snapshot.Ecology == null || snapshot.Machines?.Worlds == null || snapshot.Drops == null)
            throw new InvalidDataException("目标世界快照结构不完整。");
        GameSaveData subset = MemoryPackSerializer.Deserialize<GameSaveData>(snapshot.Core);
        if (subset?.PlanetData_Dict?.Count != 1 || !subset.PlanetData_Dict.TryGetValue(snapshot.WorldKey, out PlanetData planet) || planet == null || subset.Seed != SaveData?.Seed)
            throw new InvalidDataException("目标世界快照的身份或世界种子不匹配。");
        var records = new List<ChunkSaveRecord>(snapshot.Chunks.Count);
        foreach (byte[] payload in snapshot.Chunks)
        {
            ChunkSaveRecord record = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(payload);
            if (record?.PlanetName != snapshot.WorldKey || string.IsNullOrWhiteSpace(record.ChunkName)) throw new InvalidDataException("区块资料越过目标世界边界。");
            records.Add(record);
        }
        foreach (FrozenEcologyChunkSaveRecord record in snapshot.Ecology)
            if (record?.WorldKey != snapshot.WorldKey) throw new InvalidDataException("生态资料越过目标世界边界。");
        foreach (string key in snapshot.Machines.Worlds.Keys)
            if (key != snapshot.WorldKey) throw new InvalidDataException("机器资料越过目标世界边界。");
        RestoreEcologyChunks(subset, snapshot.Ecology);
        if (GameplayWorldRegistry.TryGet(snapshot.WorldKey, out GameplayWorldRuntime runtime))
        {
            using (GameplayWorldRegistry.UseWorld(snapshot.WorldKey)) runtime.Chunks.OnSceneChange();
            MachineWorld.ReleaseNetworkScope(snapshot.WorldKey);
            worldRestoreStates.Remove(snapshot.WorldKey); parkedWorldStates.Remove(snapshot.WorldKey);
        }
        SaveData.PlanetData_Dict[snapshot.WorldKey] = planet;
        SaveData.DimensionStateJson = subset.DimensionStateJson;
        if (subset.MonsterSpawnerWorlds?.TryGetValue(snapshot.WorldKey, out MonsterSpawnerSaveData spawner) == true)
            SaveData.MonsterSpawnerWorlds[snapshot.WorldKey] = spawner;
        SaveData.DayTimeData ??= new DayTimeSaveData();
        if (subset.DayTimeData?.WorldTimeDict != null)
            foreach (var pair in subset.DayTimeData.WorldTimeDict) SaveData.DayTimeData.WorldTimeDict[pair.Key] = pair.Value;
        if (subset.DayTimeData?.SceneLightingRateDict != null)
            foreach (var pair in subset.DayTimeData.SceneLightingRateDict) SaveData.DayTimeData.SceneLightingRateDict[pair.Key] = pair.Value;
        SaveData.Mechanical ??= new MachineArchive();
        SaveData.Mechanical.Worlds[snapshot.WorldKey] = snapshot.Machines.Worlds.TryGetValue(snapshot.WorldKey, out var nodes) ? nodes : new();
        SaveData.Mechanical.RemovedGenerated[snapshot.WorldKey] = snapshot.Machines.RemovedGenerated.TryGetValue(snapshot.WorldKey, out var removed) ? removed : new();
        SaveData.DroppedItems ??= new DroppedItemArchive();
        SaveData.DroppedItems.Worlds[snapshot.WorldKey] = snapshot.Drops;
        var removeKeys = new List<string>();
        foreach (var pair in chunkDeltas) if (pair.Value?.PlanetName == snapshot.WorldKey) removeKeys.Add(pair.Key);
        foreach (string key in removeKeys) chunkDeltas.Remove(key);
        foreach (ChunkSaveRecord record in records) chunkDeltas[BuildChunkKey(snapshot.WorldKey, record.ChunkName)] = record;
        return snapshot.WorldKey;
    }
    #endregion
}
