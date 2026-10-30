using System;
using System.Collections.Generic;
using System.IO;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;

/// <summary>支撑、地层、液体与污染的独立地形层差量。</summary>
public partial class SaveDataMgr
{
    #region 支撑面差量
    /// <summary>立即记录平台/地板覆盖面的增删，底层原始地形不进入覆盖快照。</summary>
    public void RecordSupportCell(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null)
            return;
        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, true);
        record.SupportCells.RemoveAll(cell => cell.LocalPosition == sample.LocalCell);
        int id = TerrainSupportLayer.GetTileId(sample.Terrain, sample.LocalCell.x, sample.LocalCell.y);
        if (id != 0)
        {
            sample.Terrain.TryGetEnvironmentValue(TerrainSupportLayer.CostLayer, sample.LocalCell.x, sample.LocalCell.y, out float cost);
            record.SupportCells.Add(new SupportCellSaveData { LocalPosition = sample.LocalCell, TileId = id, NavigationCost = (short)cost });
        }
        if (ChunkMgr.ExistingInstance?.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk) == true)
            NotifyAuthoritativeTerrainCellChanged(chunk, sample.LocalCell);
    }

    /// <summary>在区块表现绑定前恢复独立支撑层。</summary>
    private static void RestoreSupportTerrain(ChunkRuntime chunk, ChunkSaveRecord record)
    {
        foreach (SupportCellSaveData cell in record.SupportCells)
            TerrainSupportLayer.Set(chunk.Terrain, cell.LocalPosition.x, cell.LocalPosition.y, cell.TileId, cell.NavigationCost);
    }
    #endregion

    #region 地层差量
    /// <summary>单格立即记录天然采挖层数和玩家覆盖栈，同图不同区块及维度独立保存。</summary>
    public void RecordGroundLayerCell(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null) return;
        GroundLayerCellSaveData state = GroundTileLayerSystem.Capture(sample.Terrain, sample.LocalCell);
        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, true);
        record.GroundLayerCells ??= new List<GroundLayerCellSaveData>();
        record.GroundLayerCells.RemoveAll(cell => cell.LocalPosition == sample.LocalCell);
        if (state.ExcavatedLayers != 0 || state.ExcavatedSurfaceLevels != 0 || state.Covers.Count != 0)
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
                cell.LiquidLevel = terrain.GetLiquidLevel(x, y);
            }
        }
        record.LiquidCells.Sort(LiquidBatchComparison);
    }
    #endregion

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

/// <summary>可移除表面的独立持久化数据；不复制或替换下方地形。</summary>
[Serializable, MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
public partial class SupportCellSaveData
{
    [MemoryPack.MemoryPackIgnore] public Vector2Int LocalPosition;
    [MemoryPack.MemoryPackInclude, MemoryPack.MemoryPackOrder(0)]
    private ModuleGridPositionSaveSnapshot SavedLocalPosition
    {
        get => new() { X = LocalPosition.x, Y = LocalPosition.y };
        set { if (value != null) LocalPosition = new(value.X, value.Y); }
    } // 区块内坐标。
    [MemoryPack.MemoryPackOrder(2)] public int TileId; // 支撑材料地块。
    [MemoryPack.MemoryPackOrder(1)] public short NavigationCost; // 支撑面的行走代价。
}

/// <summary>只保存被挖深或被玩家覆盖的格子，未修改的天然地形不生成记录。</summary>
[Serializable, MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
public partial class GroundLayerCellSaveData
{
    #region 地层快照
    [MemoryPack.MemoryPackIgnore] public Vector2Int LocalPosition;
    [MemoryPack.MemoryPackInclude, MemoryPack.MemoryPackOrder(3)]
    private ModuleGridPositionSaveSnapshot SavedLocalPosition
    {
        get => new() { X = LocalPosition.x, Y = LocalPosition.y };
        set { if (value != null) LocalPosition = new(value.X, value.Y); }
    }
    [MemoryPack.MemoryPackOrder(1)] public int ExcavatedLayers;
    [MemoryPack.MemoryPackOrder(2)] public int ExcavatedSurfaceLevels; // 地表实际降低的层数，包含土层且不影响五层石头的产量。
    [MemoryPack.MemoryPackOrder(4)] public byte SurfaceGrass;
    [MemoryPack.MemoryPackOrder(0)] public List<GroundCoverLayerSaveData> Covers = new();
    #endregion
}

/// <summary>覆盖前的完整地块、未完成工作量和独立草层，按后放先挖的顺序恢复。</summary>
[Serializable, MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
public partial class GroundCoverLayerSaveData
{
    #region 下层快照
    [MemoryPack.MemoryPackOrder(0)] public RuntimeTileCellSaveDelta UnderlyingCell;
    [MemoryPack.MemoryPackOrder(1)] public byte UnderlyingGrass;
    #endregion
}

/// <summary>只持久化位置、稳定 LiquidId 和整数 LiquidLevel；禁止追加会话数字编号。</summary>
[Serializable, MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
public partial class LiquidCellSaveData
{
    #region 持久化字段
    [MemoryPack.MemoryPackIgnore] public Vector2Int LocalPosition;
    [MemoryPack.MemoryPackInclude, MemoryPack.MemoryPackOrder(2)]
    private ModuleGridPositionSaveSnapshot SavedLocalPosition
    {
        get => new() { X = LocalPosition.x, Y = LocalPosition.y };
        set { if (value != null) LocalPosition = new(value.X, value.Y); }
    }
    [MemoryPack.MemoryPackOrder(0)] public string LiquidId;
    [MemoryPack.MemoryPackOrder(1)] public int LiquidLevel;
    #endregion

    #region 捕获与恢复
    public static LiquidCellSaveData Capture(ChunkTerrainData terrain, int x, int y) => new()
    {
        LocalPosition = new Vector2Int(x, y), LiquidId = terrain.GetLiquidId(x, y), LiquidLevel = terrain.GetLiquidLevel(x, y)
    };
    public void Validate(ChunkTerrainData terrain)
    {
        if ((uint)LocalPosition.x >= (uint)terrain.Width || (uint)LocalPosition.y >= (uint)terrain.Height ||
            LiquidLevel < 0 || LiquidLevel > LiquidCellValue.MaximumLevel ||
            (LiquidLevel == 0) != string.IsNullOrEmpty(LiquidId))
            throw new InvalidDataException("液体差量坐标、身份或深度无效。");
        terrain.LiquidTypes.GetIndex(LiquidId);
    }
    public void Apply(ChunkTerrainData terrain)
    {
        Validate(terrain);
        terrain.SetLiquid(LocalPosition.x, LocalPosition.y, terrain.LiquidTypes.GetIndex(LiquidId), LiquidLevel);
    }
    #endregion
}

/// <summary>一格污染差量；Values 只保存偏离各自默认值的指标。</summary>
[Serializable, MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
public partial class ContaminationCellSaveData
{
    [MemoryPack.MemoryPackIgnore] public Vector2Int LocalPosition;
    [MemoryPack.MemoryPackInclude, MemoryPack.MemoryPackOrder(0)]
    private ModuleGridPositionSaveSnapshot SavedLocalPosition
    {
        get => new() { X = LocalPosition.x, Y = LocalPosition.y };
        set { if (value != null) LocalPosition = new(value.X, value.Y); }
    }
    [MemoryPack.MemoryPackOrder(1)] public List<ContaminationValueSaveData> Values = new();
}

/// <summary>一个污染指标在单格上的持久化值。</summary>
[Serializable, MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
public partial class ContaminationValueSaveData
{
    [MemoryPack.MemoryPackOrder(0)] public string ContaminationId;
    [MemoryPack.MemoryPackOrder(1)] public float Value;
}
