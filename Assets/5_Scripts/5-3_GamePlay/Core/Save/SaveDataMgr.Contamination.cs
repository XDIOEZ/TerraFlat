using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;

/// <summary>
/// 污染层独立差量：只保存偏离定义默认值的地格与指标，避免把整张环境数组写进区块存档。
/// </summary>
public partial class SaveDataMgr
{
    #region 污染差量

    /// <summary>污染值发生有效变化后立即更新内存差量，区块卸载不会丢失 MOD 或本体污染状态。</summary>
    public void RecordContaminationValue(
        RuntimeTerrainTileSample sample,
        string contaminationId,
        float value)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null)
            return;
        if (!ContaminationSystem.TryGetDefinition(contaminationId, out ContaminationDefinition definition))
            throw new InvalidOperationException($"记录污染差量时找不到定义：{contaminationId}");

        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, true);
        record.ContaminationCells ??= new List<ContaminationCellSaveData>();
        ContaminationCellSaveData cell = record.ContaminationCells.Find(
            candidate => candidate.LocalPosition == sample.LocalCell);

        float clamped = definition.Clamp(value);
        if (definition.IsDefault(clamped))
        {
            if (cell != null)
            {
                cell.Values ??= new List<ContaminationValueSaveData>();
                cell.Values.RemoveAll(candidate =>
                    string.Equals(candidate.ContaminationId, definition.Id, StringComparison.OrdinalIgnoreCase));
                if (cell.Values.Count == 0)
                    record.ContaminationCells.Remove(cell);
            }
        }
        else
        {
            if (cell == null)
            {
                cell = new ContaminationCellSaveData { LocalPosition = sample.LocalCell };
                record.ContaminationCells.Add(cell);
            }

            cell.Values ??= new List<ContaminationValueSaveData>();
            ContaminationValueSaveData stored = cell.Values.Find(candidate =>
                string.Equals(candidate.ContaminationId, definition.Id, StringComparison.OrdinalIgnoreCase));
            if (stored == null)
            {
                stored = new ContaminationValueSaveData { ContaminationId = definition.Id };
                cell.Values.Add(stored);
            }
            stored.Value = clamped;
            cell.Values.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(
                left?.ContaminationId,
                right?.ContaminationId));
        }

        record.ContaminationCells.Sort(CompareContaminationCell);
        if (!record.HasChanges)
        {
            string planet = ResolveRuntimePlanetName();
            chunkDeltas.Remove(BuildChunkKey(planet, ToChunkName(sample.Address)));
        }
    }

    /// <summary>地形和农业差量恢复后，把持久化的污染负荷写回权威环境层。</summary>
    private static void RestoreContaminationTerrain(ChunkRuntime chunk, ChunkSaveRecord record)
    {
        if (chunk?.Terrain == null || chunk.Terrain.IsDisposed || record?.ContaminationCells == null)
            return;

        foreach (ContaminationCellSaveData cell in record.ContaminationCells)
        {
            if (cell == null || cell.Values == null)
                continue;
            int x = cell.LocalPosition.x;
            int y = cell.LocalPosition.y;
            if ((uint)x >= (uint)chunk.Terrain.Width || (uint)y >= (uint)chunk.Terrain.Height)
                continue;

            foreach (ContaminationValueSaveData value in cell.Values)
            {
                if (value == null || string.IsNullOrWhiteSpace(value.ContaminationId))
                    throw new InvalidDataException("污染存档包含空污染 ID");
                ContaminationSystem.RestoreValue(
                    chunk.Terrain,
                    cell.LocalPosition,
                    value.ContaminationId,
                    value.Value);
            }
        }
    }

    /// <summary>按格坐标稳定排序污染差量，保证同一世界状态产生稳定存档顺序。</summary>
    private static int CompareContaminationCell(ContaminationCellSaveData left, ContaminationCellSaveData right)
    {
        int x = (left?.LocalPosition.x ?? int.MinValue).CompareTo(right?.LocalPosition.x ?? int.MinValue);
        return x != 0
            ? x
            : (left?.LocalPosition.y ?? int.MinValue).CompareTo(right?.LocalPosition.y ?? int.MinValue);
    }

    #endregion
}

/// <summary>一格污染差量；Values 只保存偏离各自默认值的指标。</summary>
[Serializable, MemoryPackable]
public partial class ContaminationCellSaveData
{
    public Vector2Int LocalPosition;
    public List<ContaminationValueSaveData> Values = new();
}

/// <summary>一个污染指标在单格上的持久化值。</summary>
[Serializable, MemoryPackable]
public partial class ContaminationValueSaveData
{
    public string ContaminationId;
    public float Value;
}
