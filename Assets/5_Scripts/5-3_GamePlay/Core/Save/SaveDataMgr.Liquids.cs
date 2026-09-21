using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;

/// <summary>液体独立稀疏差量；生成原世界后、任何显示和导航绑定前恢复。</summary>
public partial class SaveDataMgr
{
    #region 液体差量
    public void RecordLiquidChange(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null) return;
        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, true);
        record.LiquidCells ??= new List<LiquidCellSaveData>();
        record.LiquidCells.RemoveAll(value => value.LocalPosition == sample.LocalCell);
        int x = sample.LocalCell.x, y = sample.LocalCell.y;
        if (sample.Terrain.IsLiquidChanged(x, y))
            record.LiquidCells.Add(LiquidCellSaveData.Capture(sample.Terrain, x, y));
        record.LiquidCells.Sort(CompareLiquidCell);
    }

    private static int CompareLiquidCell(LiquidCellSaveData left, LiquidCellSaveData right)
    {
        int y = left.LocalPosition.y.CompareTo(right.LocalPosition.y);
        return y != 0 ? y : left.LocalPosition.x.CompareTo(right.LocalPosition.x);
    }

    /// <summary>先验证全部记录再覆盖；未知 MOD 液体或非法深度明确拒绝，不能静默丢失存档。</summary>
    private static void RestoreLiquidTerrain(ChunkRuntime chunk, ChunkSaveRecord record)
    {
        if (record.LiquidCells == null) return;
        var positions = new HashSet<Vector2Int>();
        foreach (LiquidCellSaveData cell in record.LiquidCells)
        {
            if (cell == null || !positions.Add(cell.LocalPosition)) throw new InvalidDataException("液体差量包含空格或重复坐标。");
            cell.Validate(chunk.Terrain);
        }
        foreach (LiquidCellSaveData cell in record.LiquidCells) cell.Apply(chunk.Terrain);
    }
    #endregion
}

/// <summary>只持久化位置、稳定 LiquidId 和 LiquidDepth；禁止追加会话数字编号。</summary>
[Serializable, MemoryPackable]
public partial class LiquidCellSaveData
{
    #region 持久化字段
    public Vector2Int LocalPosition;
    public string LiquidId;
    public float LiquidDepth;
    #endregion

    #region 捕获与恢复
    public static LiquidCellSaveData Capture(ChunkTerrainData terrain, int x, int y) => new()
    {
        LocalPosition = new Vector2Int(x, y), LiquidId = terrain.GetLiquidId(x, y), LiquidDepth = terrain.GetLiquidDepth(x, y)
    };
    public void Validate(ChunkTerrainData terrain)
    {
        if ((uint)LocalPosition.x >= (uint)terrain.Width || (uint)LocalPosition.y >= (uint)terrain.Height ||
            !float.IsFinite(LiquidDepth) || LiquidDepth < 0f || LiquidDepth > 1f ||
            (LiquidDepth == 0f) != string.IsNullOrEmpty(LiquidId))
            throw new InvalidDataException("液体差量坐标、身份或深度无效。");
        terrain.LiquidTypes.GetIndex(LiquidId);
    }
    public void Apply(ChunkTerrainData terrain)
    {
        Validate(terrain);
        terrain.SetLiquid(LocalPosition.x, LocalPosition.y, terrain.LiquidTypes.GetIndex(LiquidId), LiquidDepth);
    }
    #endregion
}
