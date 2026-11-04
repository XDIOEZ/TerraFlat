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
    /// <summary>立即捕获真实三维列，旧计数仅在兼容导入时读取。</summary>
    public void RecordGroundLayerCell(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null) return;
        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, true);
        RecordGroundColumnSnapshot(record, sample.Terrain, sample.LocalCell);
    }

    private static void RecordGroundColumnSnapshot(ChunkSaveRecord record, ChunkTerrainData terrain, Vector2Int local)
    {
        record.GroundColumns ??= new();
        record.GroundColumns.RemoveAll(cell => cell.LocalPosition == local);
        record.GroundColumns.Add(GroundTileLayerSystem.Capture(terrain, local));
        record.GroundLayerCells?.RemoveAll(cell => cell.LocalPosition == local);
        record.GrassDeltas?.RemoveAll(cell => cell.LocalPosition == local);
    }

    /// <summary>先恢复完整列，再让表面、农业与支撑面恢复各自的独立状态。</summary>
    private static void RestoreGroundLayerTerrain(ChunkRuntime chunk, ChunkSaveRecord record)
    {
        record.GroundColumns ??= new();
        var seen = new HashSet<Vector2Int>();
        foreach (GroundLayerCellSaveData cell in record.GroundColumns)
        {
            if (cell == null || !seen.Add(cell.LocalPosition) || !cell.BottomZ.HasValue)
                throw new InvalidOperationException("三维地层存档包含无效或重复列。");
            GroundTileLayerSystem.Restore(chunk.Terrain, cell);
        }
        foreach (GroundLayerCellSaveData cell in record.GroundLayerCells ?? new())
        {
            if (cell == null) throw new InvalidOperationException("旧地层存档包含空值。");
            if (!seen.Add(cell.LocalPosition)) continue;
            GrassCellSaveDelta grass = record.GrassDeltas?.Find(value => value.LocalPosition == cell.LocalPosition);
            if (grass != null && (grass.State == GrassCellState.Removed || grass.State == GrassCellState.Empty))
                cell.SurfaceGrass = ChunkTerrainData.GrassEmpty;
            GroundTileLayerSystem.Restore(chunk.Terrain, cell);
            record.GroundColumns.Add(GroundTileLayerSystem.Capture(chunk.Terrain, cell.LocalPosition));
        }
        record.GroundLayerCells?.Clear();
        foreach (RuntimeTileCellSaveDelta cell in record.RuntimeTileDeltas ?? new())
        {
            if (!seen.Add(cell.LocalPosition) || cell.BlockingTileId != 0 ||
                (uint)cell.LocalPosition.x >= (uint)chunk.Terrain.Width || (uint)cell.LocalPosition.y >= (uint)chunk.Terrain.Height) continue;
            GroundTileLayerSystem.Restore(chunk.Terrain, new GroundLayerCellSaveData { LocalPosition = cell.LocalPosition,
                SurfaceGrass = chunk.Terrain.GetGrass(cell.LocalPosition.x, cell.LocalPosition.y) });
            record.GroundColumns.Add(GroundTileLayerSystem.Capture(chunk.Terrain, cell.LocalPosition));
        }
        record.GrassDeltas?.RemoveAll(grass => record.GroundColumns.Exists(column => column.LocalPosition == grass.LocalPosition));
    }
    #endregion

    #region 液体差量
    public delegate void AuthoritativeLiquidCellsChangedHandler(string worldKey, ChunkRuntime chunk, ReadOnlySpan<int> indices);
    public static event AuthoritativeLiquidCellsChangedHandler AuthoritativeLiquidCellsChanged;

    // 只发布所属世界的已提交液体格，接收者在同步回调中复制索引。
    private static void NotifyAuthoritativeLiquidCellsChanged(FlatWorld.WorldModel.WorldAddress address,
        ChunkTerrainData terrain, ReadOnlySpan<int> indices)
    {
        var handler = AuthoritativeLiquidCellsChanged;
        if (handler == null || indices.IsEmpty) return;
        ChunkMgr manager = GameplayWorldRegistry.ExecutionChunks ?? ChunkMgr.ExistingInstance;
        if (manager?.WorldRuntime == null || !manager.TryGetChunkRuntime(address, out ChunkRuntime chunk) ||
            !ReferenceEquals(chunk.Terrain, terrain)) return;
        handler(manager.WorldRuntime.WorldId, chunk, indices);
    }

    public void RecordLiquidChange(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null) return;
        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, true);
        record.LiquidCells ??= new List<LiquidCellSaveData>();
        int x = sample.LocalCell.x, y = sample.LocalCell.y;
        UpsertLiquidCellDifference(record.LiquidCells, sample.Terrain, y * sample.Terrain.Width + x);
        Span<int> changed = stackalloc int[1] { y * sample.Terrain.Width + x };
        NotifyAuthoritativeLiquidCellsChanged(sample.Address, sample.Terrain, changed);
    }

    private static int CompareLiquidCell(LiquidCellSaveData left, LiquidCellSaveData right)
    {
        int y = left.LocalPosition.y.CompareTo(right.LocalPosition.y);
        return y != 0 ? y : left.LocalPosition.x.CompareTo(right.LocalPosition.x);
    }

    // 差量始终按格索引排序，二分定位可避免每次取水都全表扫描、删除再排序。
    private static void UpsertLiquidCellDifference(List<LiquidCellSaveData> cells, ChunkTerrainData terrain, int index)
    {
        int left = 0, right = cells.Count;
        while (left < right)
        {
            int middle = left + ((right - left) >> 1);
            Vector2Int position = cells[middle].LocalPosition;
            int existingIndex = position.y * terrain.Width + position.x;
            if (existingIndex < index) left = middle + 1;
            else right = middle;
        }
        bool exists = left < cells.Count &&
                      cells[left].LocalPosition.y * terrain.Width + cells[left].LocalPosition.x == index;
        int x = index % terrain.Width, y = index / terrain.Width;
        if (!terrain.IsLiquidChanged(x, y))
        {
            if (exists) cells.RemoveAt(left);
            return;
        }
        if (!exists)
        {
            cells.Insert(left, LiquidCellSaveData.Capture(terrain, x, y));
            return;
        }
        LiquidCellSaveData current = cells[left];
        current.LiquidId = terrain.GetLiquidId(x, y);
        current.LiquidLevel = terrain.GetLiquidLevel(x, y);
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
    /// <summary>每 Chunk 每 Tick 更新一次内存差量；复用已有记录，不写磁盘、不增加任何存档字段。</summary>
    public void RecordLiquidBatch(FlatWorld.WorldModel.WorldAddress address, ChunkTerrainData terrain, ReadOnlySpan<int> indices)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null || terrain == null || terrain.IsDisposed) return;
        ChunkSaveRecord record = GetRuntimeChunkRecord(address, true);
        record.LiquidCells ??= new List<LiquidCellSaveData>();
        for (int i = 0; i < indices.Length; i++)
            UpsertLiquidCellDifference(record.LiquidCells, terrain, indices[i]);
        NotifyAuthoritativeLiquidCellsChanged(address, terrain, indices);
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
    [MemoryPack.MemoryPackOrder(5)] public int? BottomZ;
    [MemoryPack.MemoryPackOrder(6)] public List<GroundStratumSaveData> Strata;

    public static GroundLayerCellSaveData CaptureColumn(ChunkTerrainData terrain, Vector2Int local)
    {
        GroundColumnSnapshot column = terrain.CaptureGroundColumn(local.x, local.y);
        return new GroundLayerCellSaveData { LocalPosition = local, BottomZ = column.BottomZ,
            Strata = new List<GroundStratum>(column.Layers).ConvertAll(GroundStratumSaveData.Capture) };
    }
    public GroundColumnSnapshot ToGroundColumn() => new() { BottomZ = BottomZ.Value,
        Layers = Strata?.ConvertAll(layer => layer.ToRuntime()).ToArray() };
    [MemoryPack.MemoryPackIgnore] public int PlayerCoverCount
    {
        get
        {
            if (Strata == null) return Covers?.Count ?? 0;
            int count = 0;
            for (int i = Strata.Count - 1; i >= 0 && Strata[i].PlayerPlaced; i--) count++;
            return count;
        }
    }
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

[Serializable, MemoryPack.MemoryPackable(MemoryPack.GenerateType.VersionTolerant)]
public partial class GroundStratumSaveData
{
    #region 地层固定字段
    [MemoryPack.MemoryPackOrder(0)] public int TileId;
    [MemoryPack.MemoryPackOrder(1)] public float HarvestProgress;
    [MemoryPack.MemoryPackOrder(2)] public byte Grass;
    [MemoryPack.MemoryPackOrder(3)] public bool PlayerPlaced;
    [MemoryPack.MemoryPackOrder(4)] public short NavigationCost;
    [MemoryPack.MemoryPackOrder(5)] public bool Walkable;
    public static GroundStratumSaveData Capture(GroundStratum layer) => new() { TileId = layer.TileId,
        HarvestProgress = layer.HarvestProgress, Grass = layer.Grass, PlayerPlaced = layer.PlayerPlaced,
        NavigationCost = layer.NavigationCost, Walkable = layer.Walkable };
    public GroundStratum ToRuntime() => new() { TileId = TileId, HarvestProgress = HarvestProgress,
        Grass = Grass, PlayerPlaced = PlayerPlaced, NavigationCost = NavigationCost, Walkable = Walkable };
    #endregion
}

/// <summary>旧地下记录保留到基线就绪后物化，迁移器不读取运行世界。</summary>
internal sealed class WorldGroundStrataSaveMigration : FlatWorld.Persistence.ISaveArchivePayloadMigration
{
    #region 三维地层记录迁移
    public string SchemaId => "flatworld.chunk";
    public int FromVersion => 2;
    public int ToVersion => 3;
    public byte[] Migrate(byte[] payload)
    {
        if (payload == null) return null;
        var snapshot = MemoryPackSerializer.Deserialize<SqliteChunkSnapshot>(payload);
        if (snapshot?.Terrain == null) return payload;
        var terrain = MemoryPackSerializer.Deserialize<ChunkSaveRecord>(snapshot.Terrain);
        terrain.GroundColumns ??= new();
        snapshot.Terrain = MemoryPackSerializer.Serialize(terrain);
        return MemoryPackSerializer.Serialize(snapshot);
    }
    #endregion
}
