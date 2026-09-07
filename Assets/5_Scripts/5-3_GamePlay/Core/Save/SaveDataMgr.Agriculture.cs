using System;
using FlatWorld.Networking;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>农业独立差量：同一格保存耕作进度、水肥及一株作物，避免挤入建筑存档规则。</summary>
public partial class SaveDataMgr
{
    #region 农业差量

    private ChunkSaveRecord GetAgricultureRecord(RuntimeWorldAddress address, bool create)
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

    /// <summary>每次有效耕作或水肥结算立即记录，区块回收不会丢失半成品。</summary>
    public void RecordAgricultureCell(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null)
            return;
        ChunkSaveRecord record = GetAgricultureRecord(sample.Address, true);
        AgricultureCellSaveData cell = record.AgricultureCells.Find(c => c.LocalPosition == sample.LocalCell);
        if (cell == null)
        {
            cell = new AgricultureCellSaveData { LocalPosition = sample.LocalCell };
            record.AgricultureCells.Add(cell);
        }
        cell.Progress = FarmlandSystem.Read(sample.Terrain, sample.LocalCell, FarmlandSystem.ProgressLayer);
        cell.SourceTileId = (int)FarmlandSystem.Read(sample.Terrain, sample.LocalCell, FarmlandSystem.SourceLayer);
        cell.Water = FarmlandSystem.Read(sample.Terrain, sample.LocalCell, FarmlandSystem.WaterLayer);
        cell.Fertility = FarmlandSystem.Read(sample.Terrain, sample.LocalCell, FarmlandSystem.FertilityLayer);
    }

    /// <summary>作物生成、保存及收获后更新对应格的独立快照。</summary>
    public void RecordCultivatedCrop(RuntimeWorldAddress address, Vector2Int local, Item crop)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null)
            return;
        var record = GetAgricultureRecord(address, true);
        var cell = record.AgricultureCells.Find(c => c.LocalPosition == local);
        if (cell == null)
            throw new InvalidOperationException("种植格缺少农业状态，必须先完成锄地。");
        cell.Crop = crop == null ? null : CloneItemData(crop.itemData);
    }

    public IReadOnlyList<AgricultureCellSaveData> GetAgricultureCells(RuntimeWorldAddress address) =>
        GetAgricultureRecord(address, false)?.AgricultureCells ?? (IReadOnlyList<AgricultureCellSaveData>)Array.Empty<AgricultureCellSaveData>();

    /// <summary>地形差量恢复后、表现绑定前恢复农业权威层。</summary>
    private static void RestoreAgricultureTerrain(ChunkRuntime chunk, ChunkSaveRecord record)
    {
        foreach (AgricultureCellSaveData cell in record.AgricultureCells)
        {
            int x = cell.LocalPosition.x;
            int y = cell.LocalPosition.y;
            if (cell.Progress > 0f)
                chunk.Terrain.SetGrass(x, y, 0);
            chunk.Terrain.SetEnvironmentValue(FarmlandSystem.SourceLayer, x, y, cell.SourceTileId);
            chunk.Terrain.SetEnvironmentValue(FarmlandSystem.ProgressLayer, x, y, cell.Progress);
            chunk.Terrain.SetEnvironmentValue(FarmlandSystem.WaterLayer, x, y, cell.Water);
            chunk.Terrain.SetEnvironmentValue(FarmlandSystem.FertilityLayer, x, y, cell.Fertility);
        }
    }

    #endregion
}

/// <summary>一格农业的持久化快照，Crop 为空代表尚未播种或已收获。</summary>
[Serializable, MemoryPackable]
public partial class AgricultureCellSaveData
{
    public Vector2Int LocalPosition; // 区块内格坐标
    public float Progress; // 0～1 的耕作进度
    public int SourceTileId; // 开始耕作时的来源地表
    public float Water; // 当前水分
    public float Fertility; // 当前肥力
    public ItemData Crop; // 唯一作物快照
}
