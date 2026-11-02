using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

/// <summary>天然水域先在内存中完成本次邻格转移，再按区块统一提交水量、存档和表现通知。</summary>
internal sealed class WorldLiquidDiffusionBatch
{
    #region 单轮水量覆盖

    private sealed class ChunkChanges
    {
        public ChunkTerrainData Terrain;
        public FlatWorld.WorldModel.WorldAddress Address;
        public readonly Dictionary<int, LiquidCellValue> Values = new();
        public LiquidCellUpdate[] Updates = Array.Empty<LiquidCellUpdate>();
        public int[] Indices = Array.Empty<int>();
        public int Count;
    }

    private readonly Dictionary<ChunkTerrainData, ChunkChanges> byTerrain = new();
    private readonly List<ChunkChanges> changedChunks = new();
    private readonly Stack<ChunkChanges> recycled = new();

    public int GetCurrentLevel(RuntimeTerrainTileSample sample) => ReadCurrent(sample).LiquidLevel;

    private LiquidCellValue ReadCurrent(RuntimeTerrainTileSample sample)
    {
        int index = sample.LocalCell.y * sample.Terrain.Width + sample.LocalCell.x;
        return byTerrain.TryGetValue(sample.Terrain, out ChunkChanges chunk) &&
               chunk.Values.TryGetValue(index, out LiquidCellValue value)
            ? value
            : new LiquidCellValue(sample.Terrain.GetLiquidTypeIndex(sample.LocalCell.x, sample.LocalCell.y),
                sample.Terrain.GetLiquidLevel(sample.LocalCell.x, sample.LocalCell.y));
    }

    private void Stage(RuntimeTerrainTileSample sample, LiquidCellValue value)
    {
        if (!byTerrain.TryGetValue(sample.Terrain, out ChunkChanges chunk))
        {
            chunk = recycled.Count > 0 ? recycled.Pop() : new ChunkChanges();
            chunk.Terrain = sample.Terrain;
            chunk.Address = sample.Address;
            byTerrain.Add(chunk.Terrain, chunk);
            changedChunks.Add(chunk);
        }
        chunk.Values[sample.LocalCell.y * sample.Terrain.Width + sample.LocalCell.x] = value;
    }

    /// <summary>连续转移读取本轮已计算出的水量，避免每一步提交时触发整套表现与存档更新。</summary>
    public bool TryBalance(RuntimeTerrainTileSample source, RuntimeTerrainTileSample target)
    {
        if (ReferenceEquals(source.Terrain, target.Terrain) && source.LocalCell == target.LocalCell)
            return false;
        LiquidCellValue a = ReadCurrent(source), b = ReadCurrent(target);
        if (a.LiquidLevel < b.LiquidLevel)
        {
            (source, target) = (target, source);
            (a, b) = (b, a);
        }
        int amount = WorldLiquidReplenishment.GetConnectedTransferLevel(a.LiquidLevel, b.LiquidLevel);
        if (amount <= 0 || a.LiquidTypeIndex == 0) return false;
        string liquidId = source.Terrain.LiquidTypes.GetId(a.LiquidTypeIndex);
        if (string.IsNullOrEmpty(liquidId)) return false;
        if (b.LiquidLevel > 0)
        {
            if (!string.Equals(liquidId, target.Terrain.LiquidTypes.GetId(b.LiquidTypeIndex),
                    StringComparison.OrdinalIgnoreCase)) return false;
        }
        else
        {
            LiquidCellValue baseline = target.Terrain.GetLiquidBaseline(target.LocalCell.x, target.LocalCell.y);
            if (baseline.LiquidLevel > 0 &&
                !string.Equals(liquidId, target.Terrain.LiquidTypes.GetId(baseline.LiquidTypeIndex),
                    StringComparison.OrdinalIgnoreCase)) return false;
        }

        int targetType = b.LiquidLevel > 0 ? b.LiquidTypeIndex : target.Terrain.LiquidTypes.GetIndex(liquidId);
        Stage(source, new LiquidCellValue(a.LiquidTypeIndex, a.LiquidLevel - amount));
        Stage(target, new LiquidCellValue(targetType, b.LiquidLevel + amount));
        return true;
    }

    #endregion

    #region 合并写回与通知

    private sealed class CellUpdateComparer : IComparer<LiquidCellUpdate>
    {
        public static readonly CellUpdateComparer Instance = new();
        public int Compare(LiquidCellUpdate a, LiquidCellUpdate b) => a.CellIndex.CompareTo(b.CellIndex);
    }

    public void Commit(bool recordSave = true)
    {
        if (changedChunks.Count == 0) return;
        Exception failure = null;
        try
        {
            // 先让全部地形格看到最终结果，跨区块监听者不会读到另一端的中间水位。
            foreach (ChunkChanges chunk in changedChunks)
            {
                if (chunk.Updates.Length < chunk.Values.Count)
                {
                    chunk.Updates = new LiquidCellUpdate[chunk.Values.Count];
                    chunk.Indices = new int[chunk.Values.Count];
                }
                chunk.Count = 0;
                foreach (KeyValuePair<int, LiquidCellValue> pair in chunk.Values)
                {
                    int x = pair.Key % chunk.Terrain.Width, y = pair.Key / chunk.Terrain.Width;
                    LiquidCellValue value = pair.Value;
                    if (chunk.Terrain.GetLiquidLevel(x, y) == value.LiquidLevel &&
                        chunk.Terrain.GetLiquidTypeIndex(x, y) == value.LiquidTypeIndex) continue;
                    chunk.Updates[chunk.Count++] = new LiquidCellUpdate(pair.Key,
                        value.LiquidTypeIndex, value.LiquidLevel);
                }
                Array.Sort(chunk.Updates, 0, chunk.Count, CellUpdateComparer.Instance);
                for (int i = 0; i < chunk.Count; i++) chunk.Indices[i] = chunk.Updates[i].CellIndex;
                if (chunk.Count > 0)
                    chunk.Terrain.SetLiquidBatch(chunk.Updates.AsSpan(0, chunk.Count), deferNotification: true);
            }

            if (recordSave)
                foreach (ChunkChanges chunk in changedChunks)
                    if (chunk.Count > 0)
                        SaveDataMgr.Instance?.RecordLiquidBatch(chunk.Address, chunk.Terrain,
                            chunk.Indices.AsSpan(0, chunk.Count));
        }
        catch (Exception exception) { failure = exception; }
        finally
        {
            foreach (ChunkChanges chunk in changedChunks)
            {
                try { chunk.Terrain.PublishLiquidBatch(); }
                catch (Exception exception) { failure ??= exception; }
            }
            Reset();
        }
        if (failure != null) throw new InvalidOperationException("天然水域批量写回失败。", failure);
    }

    public void Reset()
    {
        byTerrain.Clear();
        foreach (ChunkChanges chunk in changedChunks)
        {
            chunk.Values.Clear();
            chunk.Terrain = null;
            chunk.Count = 0;
            recycled.Push(chunk);
        }
        changedChunks.Clear();
    }

    #endregion
}
