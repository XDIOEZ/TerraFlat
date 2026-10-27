using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class SaveDataMgr
{
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
}
