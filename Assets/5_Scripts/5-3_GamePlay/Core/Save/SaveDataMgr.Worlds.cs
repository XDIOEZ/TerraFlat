using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;
using UnityEngine.Pool;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>世界资料、局部世界与完整联机快照的捕获和替换。</summary>
public partial class SaveDataMgr
{
    #region 世界恢复进度
    private sealed class WorldRestoreState
    {
        public readonly HashSet<string> AiChunks = new(StringComparer.Ordinal), BuildingChunks = new(StringComparer.Ordinal);
        public WorldRuntime AiWorld, BuildingWorld;
        public long AiEpoch = long.MinValue, BuildingEpoch = long.MinValue;
    }
    private readonly Dictionary<string, WorldRestoreState> worldRestoreStates = new(StringComparer.Ordinal);
    private WorldRestoreState RestoreState
    {
        get
        {
            string key = GameplayWorldRegistry.ExecutionWorldKey;
            if (!worldRestoreStates.TryGetValue(key, out WorldRestoreState value)) worldRestoreStates.Add(key, value = new WorldRestoreState());
            return value;
        }
    }
    private HashSet<string> restoredRuntimeAiChunks => RestoreState.AiChunks;
    private HashSet<string> restoredRuntimeBuildingChunks => RestoreState.BuildingChunks;
    private WorldRuntime restoredRuntimeAiWorld { get => RestoreState.AiWorld; set => RestoreState.AiWorld = value; }
    private WorldRuntime restoredRuntimeBuildingWorld { get => RestoreState.BuildingWorld; set => RestoreState.BuildingWorld = value; }
    private long restoredRuntimeAiEpoch { get => RestoreState.AiEpoch; set => RestoreState.AiEpoch = value; }
    private long restoredRuntimeBuildingEpoch { get => RestoreState.BuildingEpoch; set => RestoreState.BuildingEpoch = value; }
    #endregion

    #region 完整联机快照
    /// <summary>
    /// 为联机客户端创建完整世界快照。快照包含存档种子、星球参数和地图区块数据，
    /// 并使用 GZip 压缩以降低首次加入时的传输体积。
    /// </summary>
    public byte[] CreateCompressedNetworkSnapshot()
    {
        if (SaveData == null)
            throw new InvalidOperationException("SaveData为null，无法创建联机世界快照");

        PrepareLoadedChunksForSave();
        using IDisposable publicPlayerCapture = ItemSnapshotSerialization.BeginPublicPlayerState(
            MachineInventoryCommands.PublicSpecialData);
        byte[] rawData = BuildCompactSavePayload(SaveData);
        using MemoryStream output = new MemoryStream();
        using var compressionSample = SaveSnapshotCompressPoint.Measure(rawData.LongLength);
        using (GZipStream gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
            gzip.Write(rawData, 0, rawData.Length);
        return output.ToArray();
    }

    /// <summary>
    /// 应用主机发送的完整世界快照。客户端只替换内存数据，不写入本地磁盘。
    /// </summary>
    public void ApplyCompressedNetworkSnapshot(byte[] compressedData)
    {
        if (compressedData == null || compressedData.Length == 0)
            throw new ArgumentException("联机世界快照为空", nameof(compressedData));

        if (compressedData.Length < 2 || compressedData[0] != 0x1F || compressedData[1] != 0x8B)
            throw new InvalidDataException("联机世界快照缺少 GZip 文件头，请确认两端脚本版本一致");

        using MemoryStream input = new MemoryStream(compressedData, false);
        using GZipStream gzip = new GZipStream(input, CompressionMode.Decompress);
        using MemoryStream output = new MemoryStream();
        gzip.CopyTo(output);

        GameSaveData snapshot = DeserializeSavePayload(output.ToArray());
        if (snapshot == null)
            throw new InvalidDataException("联机世界快照反序列化失败");

        SaveData = snapshot;
    }
    #endregion

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
        GameplayWorldRegistry.TryGetOwnedWorldModel(snapshot.WorldKey, out var previousWorld);
        if (GameplayWorldRegistry.TryGet(snapshot.WorldKey, out GameplayWorldRuntime runtime))
        {
            if (!ChunkManagerBelongsToSnapshotWorld(runtime.Chunks, snapshot.WorldKey, allowUnowned: true))
                throw new InvalidOperationException("旧场景管理器已属于其他世界，不能替换目标快照。");
            using (GameplayWorldRegistry.UseWorld(snapshot.WorldKey)) runtime.Chunks?.OnSceneChange();
        }
        else if (GameplayWorldRegistry.Worlds.TryGetValue(snapshot.WorldKey, out GameplayWorldRuntime staleRuntime) &&
                 ChunkManagerBelongsToSnapshotWorld(staleRuntime.Chunks, snapshot.WorldKey))
            staleRuntime.Chunks?.DetachAfterPresentationSceneUnloaded();

        // 网络快照明确替换旧模型；场景解绑不能把之前的区块数据带入新快照。
        if (previousWorld != null && !GameplayWorldRegistry.ReleaseOwnedWorldModel(snapshot.WorldKey, previousWorld))
            throw new InvalidOperationException("目标世界仍被旧区块管理器占用，不能应用网络快照。");
        MachineWorld.ReleaseNetworkScope(snapshot.WorldKey);
        worldRestoreStates.Remove(snapshot.WorldKey);
        parkedWorldStates.Remove(snapshot.WorldKey);
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

    // 旧注册记录不能停止已经转交给另一个世界的区块管理器。
    private static bool ChunkManagerBelongsToSnapshotWorld(ChunkMgr manager, string worldKey, bool allowUnowned = false)
    {
        if (manager == null) return true;
        string owner = !string.IsNullOrEmpty(manager.OwnedWorldKey) ? manager.OwnedWorldKey : manager.WorldRuntime?.WorldId;
        return string.IsNullOrEmpty(owner) ? allowUnowned : string.Equals(owner, worldKey, StringComparison.Ordinal);
    }
    #endregion

    #region 局部世界普通存档采集
    internal void CaptureRuntimeLocalWorldState(string worldKey)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null) return;
        using var scope = GameplayWorldRegistry.UseWorld(worldKey);
        if (!CaptureRuntimeTerrainStates())
            throw new InvalidOperationException("局部世界地形尚未完整捕获：" + worldKey);
        ChunkMgr.ExistingInstance?.CaptureRuntimeNaturalItemStates();
        CaptureRuntimeBuildingStates();
        if (!CaptureRuntimeAiEntityStates())
            throw new InvalidOperationException("局部世界居民尚未完整捕获：" + worldKey);
    }

    internal void CopyRuntimeLocalWorldSavedChunkOrigins(string worldKey, ICollection<Vector2Int> destination)
    {
        if (destination == null) throw new ArgumentNullException(nameof(destination));
        destination.Clear();
        foreach (ChunkSaveRecord record in chunkDeltas.Values)
            if (record?.PlanetName == worldKey && record.HasChanges) destination.Add(record.ChunkPosition);
    }
    #endregion

    #region 退役世界差量清理
    // 只有实体已移交且世界永久退役时才丢弃差量，普通场景解绑继续保留存档。
    internal void DiscardRuntimeWorldDifferences(string worldKey)
    {
        if (string.IsNullOrWhiteSpace(worldKey)) throw new ArgumentException("退役世界身份为空。", nameof(worldKey));
        if (GameplayWorldRegistry.GetWorldItemCount(worldKey) != 0)
            throw new InvalidOperationException("退役世界仍持有物品，不能删除存档差量：" + worldKey);
        string prefix = worldKey + "\u001f";
        RemoveRuntimeWorldDictionaryEntries(chunkBaselines, prefix);
        RemoveRuntimeWorldDictionaryEntries(chunkDeltas, prefix);
        RemoveRuntimeWorldDictionaryEntries(runtimeChunkBaselines, prefix);
        RemoveRuntimeWorldDictionaryEntries(runtimeBuildingDirtyAddresses, prefix);
        RemoveRuntimeWorldDictionaryEntries(unloadedRuntimeBuildingGuids, prefix);
        runtimeTerrainDirtyChunks.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
        runtimeBuildingDirtyChunks.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
        buildingUnloadFailures.RemoveWhere(key => key.StartsWith(prefix, StringComparison.Ordinal));
        worldRestoreStates.Remove(worldKey);
        parkedWorldStates.Remove(worldKey);
        using var scratch = ListPool<(string Planet, Vector2Int Origin)>.Get(out var keys);
        foreach (var key in chunkDeltaLookupKeys.Keys) if (key.Planet == worldKey) keys.Add(key);
        foreach (var key in keys) chunkDeltaLookupKeys.Remove(key);
    }

    private static void RemoveRuntimeWorldDictionaryEntries<T>(IDictionary<string, T> entries, string prefix)
    {
        using var scratch = ListPool<string>.Get(out var keys);
        foreach (string key in entries.Keys)
            if (key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(key);
        foreach (string key in keys) entries.Remove(key);
    }
    #endregion

    #region 局部世界联机快照
    internal byte[] CaptureNetworkLocalWorldSnapshot(string worldKey)
    {
        if (!GameNetwork.HasStateAuthority || !GameplayWorldRegistry.UsesLocalCoordinates(worldKey))
            throw new InvalidOperationException("局部世界没有权威或尚未就绪：" + worldKey);
        CaptureRuntimeLocalWorldState(worldKey);
        var snapshot = new NetworkWorldSaveSnapshot { WorldKey = worldKey };
        var records = new List<ChunkSaveRecord>();
        foreach (ChunkSaveRecord record in chunkDeltas.Values)
            if (record?.PlanetName == worldKey && record.HasChanges) records.Add(record);
        records.Sort((a, b) => a.ChunkPosition.x != b.ChunkPosition.x
            ? a.ChunkPosition.x.CompareTo(b.ChunkPosition.x) : a.ChunkPosition.y.CompareTo(b.ChunkPosition.y));
        using (ItemSnapshotSerialization.BeginPublicPlayerState(MachineInventoryCommands.PublicSpecialData))
        {
            foreach (ChunkSaveRecord record in records) snapshot.Chunks.Add(MemoryPackSerializer.Serialize(record));
            snapshot.Drops = DroppedItemService.CaptureWorldItems(worldKey);
            return MemoryPackSerializer.Serialize(snapshot);
        }
    }

    internal static List<ChunkSaveRecord> DecodeNetworkLocalWorldChunks(byte[] payload, string worldKey,
        out NetworkWorldSaveSnapshot snapshot)
    {
        snapshot = MemoryPackSerializer.Deserialize<NetworkWorldSaveSnapshot>(payload);
        if (snapshot == null || snapshot.WorldKey != worldKey || snapshot.Chunks == null || snapshot.Drops == null)
            throw new InvalidDataException("局部世界联机快照无效。");
        var records = new List<ChunkSaveRecord>(snapshot.Chunks.Count);
        var origins = new HashSet<Vector2Int>();
        foreach (byte[] bytes in snapshot.Chunks)
        {
            ChunkSaveRecord record = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(bytes);
            if (record == null || record.PlanetName != worldKey || !origins.Add(record.ChunkPosition) ||
                record.ChunkName != record.ChunkPosition.ToString() || record.RuntimeTileDeltas == null ||
                record.SupportCells == null || record.AgricultureCells == null || record.ContaminationCells == null ||
                record.LiquidCells == null || record.GroundLayerCells == null || record.SnowCells == null ||
                record.GrassDeltas == null || record.ChangedItems == null)
                throw new InvalidDataException("局部世界联机区块身份或内容无效。");
            records.Add(record);
        }
        return records;
    }

    internal void ApplyNetworkLocalWorldChunks(string worldKey, IReadOnlyList<ChunkSaveRecord> records)
    {
        if (GameNetwork.HasStateAuthority || !GameplayWorldRegistry.TryGet(worldKey, out var world) || world.Chunks == null)
            throw new InvalidOperationException("局部世界尚未准备好接收联机状态：" + worldKey);
        using var scope = GameplayWorldRegistry.UseWorld(worldKey);
        var previous = new Dictionary<Vector2Int, ChunkSaveRecord>();
        foreach (ChunkSaveRecord record in chunkDeltas.Values)
            if (record?.PlanetName == worldKey) previous.Add(record.ChunkPosition, record);
        var next = new Dictionary<Vector2Int, ChunkSaveRecord>();
        foreach (ChunkSaveRecord record in records) next.Add(record.ChunkPosition, record);
        var changed = new HashSet<Vector2Int>(previous.Keys);
        changed.UnionWith(next.Keys);
        foreach (Vector2Int origin in changed)
        {
            previous.TryGetValue(origin, out ChunkSaveRecord oldRecord);
            next.TryGetValue(origin, out ChunkSaveRecord newRecord);
            if (oldRecord != null && newRecord != null &&
                MemoryPackSerializer.Serialize(oldRecord).AsSpan().SequenceEqual(MemoryPackSerializer.Serialize(newRecord))) continue;
            var address = new RuntimeWorldAddress(worldKey, new Int2(origin.x, origin.y));
            string key = BuildChunkKey(worldKey, origin.ToString());
            bool terrainChanged = !SerializeNetworkLocalTerrain(oldRecord).AsSpan().SequenceEqual(SerializeNetworkLocalTerrain(newRecord));
            if (terrainChanged && oldRecord != null && world.Chunks.TryGetChunkRuntime(address, out ChunkRuntime existing))
                ClearNetworkLocalWorldTerrain(existing, oldRecord);
            if (newRecord == null) chunkDeltas.Remove(key);
            else chunkDeltas[key] = newRecord;
            if (!terrainChanged) continue;
            if (!world.Chunks.TryGetChunkRuntime(address, out ChunkRuntime chunk)) continue;
            if (TryEnsureRuntimeChunkBaseline(key, chunk, out RuntimeChunkBaseline baseline))
                baseline.PersistenceRestored = false;
            RestoreRuntimeTerrainForChunk(address, chunk);
        }
    }

    internal void PrepareDetachedNetworkLocalWorldChunks(string worldKey, IReadOnlyList<ChunkSaveRecord> records)
    {
        if (GameNetwork.HasStateAuthority || GameplayWorldRegistry.TryGet(worldKey, out _))
            throw new InvalidOperationException("不能替换仍在运行的局部世界：" + worldKey);
        var keys = new List<string>();
        foreach (var pair in chunkDeltas)
            if (pair.Value?.PlanetName == worldKey) keys.Add(pair.Key);
        foreach (string key in keys) chunkDeltas.Remove(key);
        foreach (ChunkSaveRecord record in records)
            chunkDeltas.Add(BuildChunkKey(worldKey, record.ChunkName), record);
    }

    private static byte[] SerializeNetworkLocalTerrain(ChunkSaveRecord record)
    {
        var terrain = new ChunkSaveRecord();
        if (record != null)
        {
            terrain.RuntimeTileDeltas = record.RuntimeTileDeltas;
            terrain.SupportCells = record.SupportCells;
            terrain.ContaminationCells = record.ContaminationCells;
            terrain.LiquidCells = record.LiquidCells;
            terrain.GroundLayerCells = record.GroundLayerCells;
            terrain.SnowCells = record.SnowCells;
            terrain.GrassDeltas = record.GrassDeltas;
            foreach (AgricultureCellSaveData cell in record.AgricultureCells)
                terrain.AgricultureCells.Add(new AgricultureCellSaveData
                {
                    LocalPosition = cell.LocalPosition, SourceTileId = cell.SourceTileId, Water = cell.Water,
                    WaterSupplyRemainder = cell.WaterSupplyRemainder, WaterConsumptionRemainder = cell.WaterConsumptionRemainder,
                    Fertility = cell.Fertility
                });
        }
        return MemoryPackSerializer.Serialize(terrain);
    }

    // 先还原旧差量触及的格子，再按普通存档恢复新差量，保留原区块实例与监听。
    private static void ClearNetworkLocalWorldTerrain(ChunkRuntime chunk, ChunkSaveRecord record)
    {
        ChunkTerrainData terrain = chunk.Terrain;
        foreach (RuntimeTileCellSaveDelta cell in record.RuntimeTileDeltas)
        {
            int x = cell.LocalPosition.x, y = cell.LocalPosition.y;
            terrain.SetCell(x, y, default);
            WriteRuntimeBuildingDamage(terrain, x, y, 0f);
        }
        foreach (SupportCellSaveData cell in record.SupportCells)
            TerrainSupportLayer.Set(terrain, cell.LocalPosition.x, cell.LocalPosition.y, 0, 0);
        foreach (AgricultureCellSaveData cell in record.AgricultureCells)
        {
            int x = cell.LocalPosition.x, y = cell.LocalPosition.y;
            terrain.SetEnvironmentValue(FarmlandSystem.SourceLayer, x, y, 0);
            terrain.SetEnvironmentValue(FarmlandSystem.WaterLayer, x, y, 0);
            terrain.SetEnvironmentValue(FarmlandSystem.WaterSupplyRemainderLayer, x, y, 0);
            terrain.SetEnvironmentValue(FarmlandSystem.WaterConsumptionRemainderLayer, x, y, 0);
            terrain.SetEnvironmentValue(FarmlandSystem.FertilityLayer, x, y, 0);
        }
        foreach (ContaminationCellSaveData cell in record.ContaminationCells)
            foreach (ContaminationValueSaveData value in cell.Values)
            {
                if (!ContaminationSystem.TryGetDefinition(value.ContaminationId, out ContaminationDefinition definition))
                    throw new InvalidDataException("联机区块引用了未知污染类型。");
                ContaminationSystem.RestoreValue(terrain, cell.LocalPosition, value.ContaminationId, definition.DefaultValue);
            }
        foreach (LiquidCellSaveData cell in record.LiquidCells)
            terrain.SetLiquid(cell.LocalPosition.x, cell.LocalPosition.y, 0, 0);
        foreach (GroundLayerCellSaveData cell in record.GroundLayerCells)
            GroundTileLayerSystem.Restore(terrain, new GroundLayerCellSaveData { LocalPosition = cell.LocalPosition });
        foreach (SnowCellSaveData cell in record.SnowCells)
            WorldSnowInteraction.Restore(terrain, new SnowCellSaveData { LocalPosition = cell.LocalPosition });
        foreach (GrassCellSaveDelta cell in record.GrassDeltas)
            terrain.SetGrass(cell.LocalPosition.x, cell.LocalPosition.y, 0);
    }
    #endregion

    #region 世界生成配置策略
    private readonly struct EcologyConfigurationBackup
    {
        public EcologyConfigurationBackup(EcologyWorldSaveData ecology)
        {
            Ecology = ecology;
            DataVersion = ecology.DataVersion;
            ProfileId = ecology.ProfileId;
            ConfigurationFingerprint = ecology.ConfigurationFingerprint;
            GlobalMultiplier = ecology.GlobalMultiplier;
            Rules = ecology.Rules;
            Generation = ecology.Generation;
        }

        public EcologyWorldSaveData Ecology { get; }
        public int DataVersion { get; }
        public string ProfileId { get; }
        public ulong ConfigurationFingerprint { get; }
        public double GlobalMultiplier { get; }
        public List<EcologyRuleSaveData> Rules { get; }
        public WorldGenerationProfileSaveData Generation { get; }
    }

    /// <summary>修改当前已加载存档的生成配置冻结模式，并立即原子写回磁盘。</summary>
    public bool TrySetWorldGenerationConfigurationFrozen(bool frozen)
    {
        if (SaveData == null || string.IsNullOrWhiteSpace(SaveData.saveName))
        {
            UnityEngine.Debug.LogWarning("[SaveDataMgr] 当前没有可修改生成配置模式的存档。");
            return false;
        }

        WorldGenerationConfigurationMode targetMode = frozen
            ? WorldGenerationConfigurationMode.Frozen
            : WorldGenerationConfigurationMode.FollowCurrent;
        if (SaveData.WorldGenerationConfigMode == targetMode)
            return true;

        WorldGenerationConfigurationMode previousMode = SaveData.WorldGenerationConfigMode;
        var backups = new List<EcologyConfigurationBackup>();

        try
        {
            SaveData.WorldGenerationConfigMode = targetMode;
            if (!frozen)
                ClearFrozenWorldGenerationConfiguration(backups);

            if (SaveToDisk(SaveData, UserSavePath, SaveData.saveName))
                return true;

            RestoreFrozenWorldGenerationConfiguration(previousMode, backups);
            return false;
        }
        catch (Exception exception)
        {
            RestoreFrozenWorldGenerationConfiguration(previousMode, backups);
            UnityEngine.Debug.LogError($"[SaveDataMgr] 修改世界生成配置模式失败：{exception.Message}");
            UnityEngine.Debug.LogException(exception);
            return false;
        }
    }

    /// <summary>清除所有维度的冻结快照，但不触碰生态区块差量。</summary>
    private void ClearFrozenWorldGenerationConfiguration(
        List<EcologyConfigurationBackup> backups)
    {
        if (SaveData?.PlanetData_Dict == null)
            return;

        foreach (PlanetData planet in SaveData.PlanetData_Dict.Values)
        {
            EcologyWorldSaveData ecology = planet?.Ecology;
            if (ecology == null)
                continue;

            backups.Add(new EcologyConfigurationBackup(ecology));
            ecology.ClearFrozenConfiguration();
        }
    }

    /// <summary>写盘失败时恢复本次修改前的冻结配置引用与模式。</summary>
    private void RestoreFrozenWorldGenerationConfiguration(
        WorldGenerationConfigurationMode previousMode,
        List<EcologyConfigurationBackup> backups)
    {
        if (SaveData == null)
            return;

        SaveData.WorldGenerationConfigMode = previousMode;
        for (int i = 0; i < backups.Count; i++)
        {
            EcologyConfigurationBackup backup = backups[i];
            EcologyWorldSaveData ecology = backup.Ecology;
            if (ecology == null)
                continue;

            ecology.DataVersion = backup.DataVersion;
            ecology.ProfileId = backup.ProfileId;
            ecology.ConfigurationFingerprint = backup.ConfigurationFingerprint;
            ecology.GlobalMultiplier = backup.GlobalMultiplier;
            ecology.Rules = backup.Rules;
            ecology.Generation = backup.Generation;
        }
    }
    #endregion
}

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
