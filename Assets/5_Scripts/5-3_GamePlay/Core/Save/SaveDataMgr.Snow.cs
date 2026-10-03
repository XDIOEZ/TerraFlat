using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;

public partial class SaveDataMgr
{
    #region 积雪差量
    public void RecordSnowCell(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null) return;
        var state = WorldSnowInteraction.Capture(sample);
        var record = GetRuntimeChunkRecord(sample.Address, true);
        record.SnowCells ??= new List<SnowCellSaveData>();
        record.SnowCells.RemoveAll(cell => cell.LocalPosition == sample.LocalCell);
        if (state.Edited) record.SnowCells.Add(state);
    }

    private static void RestoreSnowTerrain(ChunkRuntime chunk, ChunkSaveRecord record)
    {
        if (record.SnowCells == null) return;
        var seen = new HashSet<Vector2Int>();
        foreach (var state in record.SnowCells)
        {
            if (state == null || !seen.Add(state.LocalPosition))
                throw new InvalidOperationException("积雪存档包含空值或重复格子。");
            WorldSnowInteraction.Restore(chunk.Terrain, state);
        }
    }
    #endregion
}

/// <summary>仅保存玩家改过的雪格，空雪格也保留标记以免天然雪重新出现。</summary>
[Serializable, MemoryPackable]
public partial class SnowCellSaveData
{
    #region 雪格快照
    public Vector2Int LocalPosition;
    public bool Edited;
    public float Depth;
    public float SeasonalDepth;
    #endregion
}
