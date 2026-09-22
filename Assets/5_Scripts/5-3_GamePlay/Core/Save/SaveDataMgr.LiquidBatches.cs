using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class SaveDataMgr
{
    #region 液体批次内存差量
    private static readonly Comparison<LiquidCellSaveData> LiquidBatchComparison = CompareLiquidCell;

    /// <summary>每 Chunk 每 Tick 更新一次内存差量；复用已有记录，不写磁盘、不增加任何存档字段。</summary>
    public void RecordLiquidBatch(FlatWorld.WorldModel.WorldAddress address, ChunkTerrainData terrain, ReadOnlySpan<int> indices)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null || terrain == null || terrain.IsDisposed) return;
        ChunkSaveRecord record = GetRuntimeChunkRecord(address, true);
        record.LiquidCells ??= new List<LiquidCellSaveData>();
        for (int i = 0; i < indices.Length; i++)
        {
            int x = indices[i] % terrain.Width, y = indices[i] / terrain.Width;
            var position = new Vector2Int(x, y);
            int existing = -1;
            for (int j = 0; j < record.LiquidCells.Count; j++)
                if (record.LiquidCells[j].LocalPosition == position) { existing = j; break; }
            if (!terrain.IsLiquidChanged(x, y))
            {
                if (existing >= 0) record.LiquidCells.RemoveAt(existing);
                continue;
            }
            if (existing < 0) record.LiquidCells.Add(LiquidCellSaveData.Capture(terrain, x, y));
            else
            {
                LiquidCellSaveData cell = record.LiquidCells[existing];
                cell.LiquidId = terrain.GetLiquidId(x, y);
                cell.LiquidDepth = terrain.GetLiquidDepth(x, y);
            }
        }
        record.LiquidCells.Sort(LiquidBatchComparison);
    }
    #endregion
}
