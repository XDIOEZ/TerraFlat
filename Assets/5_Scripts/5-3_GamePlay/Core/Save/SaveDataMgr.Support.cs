using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;

public partial class SaveDataMgr
{
    /// <summary>立即记录支撑面的增删，基础水体不进入平台快照。</summary>
    public void RecordSupportCell(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null)
            return;
        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, true);
        record.SupportCells.RemoveAll(cell => cell.LocalPosition == sample.LocalCell);
        int id = TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y);
        if (id == 0)
            return;
        sample.Terrain.TryGetEnvironmentValue(TerrainSupportLayer.CostLayer, sample.LocalCell.x, sample.LocalCell.y, out float cost);
        record.SupportCells.Add(new SupportCellSaveData { LocalPosition = sample.LocalCell, TileId = id, NavigationCost = (short)cost });
    }

    /// <summary>在区块表现绑定前恢复独立支撑层。</summary>
    private static void RestoreSupportTerrain(ChunkRuntime chunk, ChunkSaveRecord record)
    {
        foreach (SupportCellSaveData cell in record.SupportCells)
            TerrainSupportLayer.Set(chunk.Terrain, cell.LocalPosition.x, cell.LocalPosition.y, cell.TileId, cell.NavigationCost);
    }
}

/// <summary>可移除表面的独立持久化数据；不复制或替换下方地形。</summary>
[Serializable, MemoryPackable]
public partial class SupportCellSaveData
{
    public Vector2Int LocalPosition; // 区块内坐标。
    public int TileId; // 支撑材料地块。
    public short NavigationCost; // 支撑面的行走代价。
}
