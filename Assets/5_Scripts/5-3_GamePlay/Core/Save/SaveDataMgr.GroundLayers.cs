using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;

public partial class SaveDataMgr
{
    #region 地层差量
    /// <summary>单格立即记录天然采挖层数和玩家覆盖栈，同图不同区块及维度独立保存。</summary>
    public void RecordGroundLayerCell(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null) return;
        GroundLayerCellSaveData state = GroundTileLayerSystem.Capture(sample.Terrain, sample.LocalCell);
        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, true);
        record.GroundLayerCells ??= new List<GroundLayerCellSaveData>();
        record.GroundLayerCells.RemoveAll(cell => cell.LocalPosition == sample.LocalCell);
        if (state.ExcavatedLayers != 0 || state.Covers.Count != 0)
            record.GroundLayerCells.Add(state);
    }

    /// <summary>完整地形与草层恢复后，再恢复隐藏地块和已采层数。</summary>
    private static void RestoreGroundLayerTerrain(ChunkRuntime chunk, ChunkSaveRecord record)
    {
        if (record.GroundLayerCells == null) return;
        var seen = new HashSet<Vector2Int>();
        foreach (GroundLayerCellSaveData cell in record.GroundLayerCells)
        {
            if (cell == null || !seen.Add(cell.LocalPosition))
                throw new InvalidOperationException("地层存档包含空值或重复格子。");
            GroundTileLayerSystem.Restore(chunk.Terrain, cell);
        }
    }
    #endregion
}

/// <summary>只保存被挖深或被玩家覆盖的格子，未修改的天然地形不生成记录。</summary>
[Serializable, MemoryPackable]
public partial class GroundLayerCellSaveData
{
    #region 地层快照
    public Vector2Int LocalPosition;
    public int ExcavatedLayers;
    public byte SurfaceGrass;
    public List<GroundCoverLayerSaveData> Covers = new();
    #endregion
}

/// <summary>覆盖前的完整地块、未完成工作量和独立草层，按后放先挖的顺序恢复。</summary>
[Serializable, MemoryPackable]
public partial class GroundCoverLayerSaveData
{
    #region 下层快照
    public RuntimeTileCellSaveDelta UnderlyingCell;
    public byte UnderlyingGrass;
    #endregion
}
