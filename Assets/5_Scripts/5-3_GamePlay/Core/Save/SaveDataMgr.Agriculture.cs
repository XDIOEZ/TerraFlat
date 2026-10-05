using System;
using FlatWorld.Networking;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>农业独立差量：同一格只保存水肥及作物；未完成耕作属于临时视觉，不进入存档。</summary>
public partial class SaveDataMgr
{
    #region 农业差量

    private ChunkSaveRecord GetRuntimeChunkRecord(RuntimeWorldAddress address, bool create)
    {
        string planet = ResolveRuntimePlanetName();
        string key = BuildChunkKey(planet, ToChunkName(address));
        if (chunkDeltas.TryGetValue(key, out var record))
            return record;
        if (!create)
            return null;
        record = CreateRuntimeChunkDelta(planet, address);
        chunkDeltas.Add(key, record);
        return record;
    }

    /// <summary>只记录需要持久化的水肥状态；未完成耕作不会调用这里。</summary>
    public void RecordAgricultureCell(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null)
            return;
        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, true);
        AgricultureCellSaveData cell = record.AgricultureCells.Find(c => c.LocalPosition == sample.LocalCell);
        if (cell == null)
        {
            cell = new AgricultureCellSaveData { LocalPosition = sample.LocalCell };
            record.AgricultureCells.Add(cell);
        }
        cell.SourceTileId = (int)FarmlandSystem.Read(sample.Terrain, sample.LocalCell, FarmlandSystem.SourceLayer);
        cell.Water = FarmlandSystem.Read(sample.Terrain, sample.LocalCell, FarmlandSystem.WaterLayer);
        cell.Fertility = FarmlandSystem.Read(sample.Terrain, sample.LocalCell, FarmlandSystem.FertilityLayer);
    }

    /// <summary>复用已有草层稀疏差量格式，保存 WorldModel 的清除状态，不改变存档对象布局。</summary>
    public void RecordRuntimeGrassRemoved(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null) return;
        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, true);
        GrassCellSaveDelta delta = record.GrassDeltas.Find(c => c.LocalPosition == sample.LocalCell);
        if (delta == null)
        {
            delta = new GrassCellSaveDelta { LocalPosition = sample.LocalCell };
            record.GrassDeltas.Add(delta);
        }
        delta.State = GrassCellState.Removed;
    }

    /// <summary>作物生成、保存及收获后更新对应格的独立快照。</summary>
    public void RecordCultivatedCrop(RuntimeWorldAddress address, Vector2Int local, Item crop)
        => RecordCultivatedCropData(address, local, crop == null ? null : crop.itemData);

    /// <summary>Entity 作物直接提交纯数据快照，不为保存临时实例化 Item。</summary>
    public void RecordCultivatedCropData(RuntimeWorldAddress address, Vector2Int local, ItemData crop)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null)
            return;
        var record = GetRuntimeChunkRecord(address, true);
        var cell = record.AgricultureCells.Find(c => c.LocalPosition == local);
        if (cell == null)
            throw new InvalidOperationException("种植格缺少水肥状态，必须先准备种植基质。");
        cell.Crop = crop == null ? null : CloneItemData(crop);
    }

    public IReadOnlyList<AgricultureCellSaveData> GetAgricultureCells(RuntimeWorldAddress address) =>
        GetRuntimeChunkRecord(address, false)?.AgricultureCells ?? (IReadOnlyList<AgricultureCellSaveData>)Array.Empty<AgricultureCellSaveData>();

    /// <summary>地形差量恢复后、表现绑定前恢复农业权威层。</summary>
    private static void RestoreAgricultureTerrain(ChunkRuntime chunk, ChunkSaveRecord record)
    {
        foreach (AgricultureCellSaveData cell in record.AgricultureCells)
        {
            int x = cell.LocalPosition.x;
            int y = cell.LocalPosition.y;
            chunk.Terrain.SetEnvironmentValue(FarmlandSystem.SourceLayer, x, y, cell.SourceTileId);
            chunk.Terrain.SetEnvironmentValue(FarmlandSystem.WaterLayer, x, y, cell.Water);
            chunk.Terrain.SetEnvironmentValue(FarmlandSystem.FertilityLayer, x, y, cell.Fertility);
            if (cell.SourceTileId != 0)
                FarmlandSystem.SyncSoilEnvironment(chunk.Terrain, x, y, cell.Water, cell.Fertility);
        }
    }

    #endregion
}

/// <summary>一格农业的持久化快照，Crop 为空代表尚未播种或已收获。</summary>
[Serializable, MemoryPackable]
public partial class AgricultureCellSaveData
{
    public Vector2Int LocalPosition; // 区块内格坐标
    public int SourceTileId; // 水肥状态关联的当前地表
    public float Water; // 当前水分
    public float Fertility; // 当前肥力
    public ItemData Crop; // 唯一作物快照
}
