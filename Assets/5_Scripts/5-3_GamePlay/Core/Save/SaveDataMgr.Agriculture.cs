using System;
using FlatWorld.Networking;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;
using Unity.Profiling;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>农业独立差量：同一格只保存水肥及作物；未完成耕作属于临时视觉，不进入存档。</summary>
public partial class SaveDataMgr
{
    #region 农业差量

    private static readonly ProfilerMarker AgricultureRecordMarker = new("NaturalEntityRuntime.Soil.Commit.Persist");

    private ChunkSaveRecord GetRuntimeChunkRecord(RuntimeWorldAddress address, bool create)
    {
        // 世界上下文已缓存名称；热查询复用坐标键，只在首次创建差量时格式化存档键。
        string planet = GameplayWorldRegistry.ExecutionWorldKey;
        if (string.IsNullOrWhiteSpace(planet)) planet = ResolveRuntimePlanetName();
        return GetRuntimeChunkRecord(planet, address, create);
    }

    private ChunkSaveRecord GetRuntimeChunkRecord(string planet, RuntimeWorldAddress address, bool create)
    {
        var origin = new Vector2Int(address.ChunkOrigin.X, address.ChunkOrigin.Y);
        if (TryGetChunkDelta(planet, origin, out var record))
            return record;
        if (!create)
            return null;
        record = CreateRuntimeChunkDelta(planet, address);
        chunkDeltas.Add(BuildChunkKey(planet, ToChunkName(address)), record);
        return record;
    }

    /// <summary>只记录需要持久化的水肥状态；未完成耕作不会调用这里。</summary>
    public void RecordAgricultureCell(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null)
            return;
        using var sampleMarker = AgricultureRecordMarker.Auto();
        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, true);
        AgricultureCellSaveData cell = record.GetOrAddAgricultureCell(sample.LocalCell);
        cell.SourceTileId = (int)FarmlandSystem.Read(sample.Terrain, sample.LocalCell, FarmlandSystem.SourceLayer);
        cell.Water = FarmlandSystem.ReadWaterLevel(sample.Terrain, sample.LocalCell);
        cell.WaterSupplyRemainder = FarmlandSystem.Read(sample.Terrain, sample.LocalCell,
            FarmlandSystem.WaterSupplyRemainderLayer);
        cell.WaterConsumptionRemainder = FarmlandSystem.Read(sample.Terrain, sample.LocalCell,
            FarmlandSystem.WaterConsumptionRemainderLayer);
        cell.Fertility = FarmlandSystem.Read(sample.Terrain, sample.LocalCell, FarmlandSystem.FertilityLayer);
    }

    /// <summary>复用已有草层稀疏差量格式，保存 WorldModel 的清除状态，不改变存档对象布局。</summary>
    public void RecordRuntimeGrassRemoved(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null) return;
        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, true);
        GrassCellSaveDelta delta = record.GrassDeltas.Find(c => c.LocalPosition == sample.LocalCell);
        if (delta == null)
        {
            delta = new GrassCellSaveDelta { LocalPosition = sample.LocalCell };
            record.GrassDeltas.Add(delta);
        }
        delta.State = GrassCellState.Removed;
    }

    /// <summary>作物生成、保存及收获后更新对应格的独立快照。</summary>
    public void RecordCultivatedCrop(RuntimeWorldAddress address, Vector2Int local, Item crop)
        => RecordCultivatedCropData(address, local, crop == null ? null : crop.itemData);

    /// <summary>Entity 作物直接提交纯数据快照，不为保存临时实例化 Item。</summary>
    public void RecordCultivatedCropData(RuntimeWorldAddress address, Vector2Int local, ItemData crop)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null)
            return;
        var record = GetRuntimeChunkRecord(address, true);
        var cell = record.FindAgricultureCell(local);
        if (cell == null && crop == null) return;
        cell ??= record.GetOrAddAgricultureCell(local);
        cell.Crop = crop == null ? null : CloneItemData(crop);
        if (cell.Crop == null && cell.SourceTileId == 0)
            record.AgricultureCells.Remove(cell);
    }

    /// <summary>自然基质上的玩家作物只登记植株，不创建地块水肥。</summary>
    internal bool HasNaturalSubstrateCrop(RuntimeTerrainTileSample sample)
    {
        AgricultureCellSaveData cell = GetRuntimeChunkRecord(sample.Address, false)?.FindAgricultureCell(sample.LocalCell);
        return cell is { SourceTileId: 0, Crop: not null };
    }

    public IReadOnlyList<AgricultureCellSaveData> GetAgricultureCells(RuntimeWorldAddress address) =>
        GetRuntimeChunkRecord(address, false)?.AgricultureCells ?? (IReadOnlyList<AgricultureCellSaveData>)Array.Empty<AgricultureCellSaveData>();

    /// <summary>土壤状态已清空且没有作物时移除本格农业记录，避免残留无来源的零差量。</summary>
    public bool TryRemoveUninitializedAgricultureCell(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null || sample.Terrain == null || sample.Terrain.IsDisposed ||
            FarmlandSystem.Read(sample.Terrain, sample.LocalCell, FarmlandSystem.SourceLayer) > 0f)
            return false;
        ChunkSaveRecord record = GetRuntimeChunkRecord(sample.Address, false);
        if (record == null) return true;
        AgricultureCellSaveData cell = record.FindAgricultureCell(sample.LocalCell);
        if (cell == null) return true;
        if (cell.Crop != null) return false;
        record.AgricultureCells.Remove(cell);
        return true;
    }

    /// <summary>地形差量恢复后、表现绑定前恢复农业权威层。</summary>
    private static void RestoreAgricultureTerrain(ChunkRuntime chunk, ChunkSaveRecord record)
    {
        foreach (AgricultureCellSaveData cell in record.AgricultureCells)
        {
            int x = cell.LocalPosition.x;
            int y = cell.LocalPosition.y;
            if (cell.SourceTileId <= 0 || !FarmlandSystem.IsFarmland(chunk.Terrain.GetCell(x, y)))
                continue;
            chunk.Terrain.SetEnvironmentValue(FarmlandSystem.SourceLayer, x, y, cell.SourceTileId);
            chunk.Terrain.SetEnvironmentValue(FarmlandSystem.WaterLayer, x, y, cell.Water);
            chunk.Terrain.SetEnvironmentValue(FarmlandSystem.WaterSupplyRemainderLayer, x, y, cell.WaterSupplyRemainder);
            chunk.Terrain.SetEnvironmentValue(FarmlandSystem.WaterConsumptionRemainderLayer, x, y,
                cell.WaterConsumptionRemainder);
            chunk.Terrain.SetEnvironmentValue(FarmlandSystem.FertilityLayer, x, y, cell.Fertility);
        }
    }

    #endregion
}

public partial class ChunkSaveRecord
{
    #region 农业差量快速定位

    // 只索引当前内存记录，不进入存档；列表外部改动通过枚举器结构版本检测后重建。
    [NonSerialized, MemoryPackIgnore, FastCloner.Code.FastClonerIgnore]
    private Dictionary<Vector2Int, AgricultureCellSaveData> agricultureIndex;
    [NonSerialized, MemoryPackIgnore, FastCloner.Code.FastClonerIgnore]
    private List<AgricultureCellSaveData> indexedAgricultureCells;
    [NonSerialized, MemoryPackIgnore, FastCloner.Code.FastClonerIgnore]
    private List<AgricultureCellSaveData>.Enumerator indexedAgricultureVersion;
    [NonSerialized, MemoryPackIgnore, FastCloner.Code.FastClonerIgnore]
    private int indexedAgricultureCount;

    internal AgricultureCellSaveData FindAgricultureCell(Vector2Int position)
    {
        List<AgricultureCellSaveData> cells = AgricultureCells;
        if (cells == null || cells.Count < 16)
        {
            if (indexedAgricultureCells != null)
            {
                agricultureIndex.Clear();
                indexedAgricultureCells = null;
            }
            if (cells == null) return null;
        }
        // 小列表线性读取更轻，稠密耕地才启用坐标索引。
        if (cells.Count < 16)
        {
            for (int i = 0; i < cells.Count; i++)
                if (cells[i] != null && cells[i].LocalPosition == position) return cells[i];
            return null;
        }

        bool valid = ReferenceEquals(indexedAgricultureCells, cells) && indexedAgricultureCount == cells.Count;
        if (valid)
        {
            try { var version = indexedAgricultureVersion; version.MoveNext(); }
            catch (InvalidOperationException) { valid = false; }
        }
        if (!valid)
        {
            agricultureIndex ??= new Dictionary<Vector2Int, AgricultureCellSaveData>();
            agricultureIndex.Clear();
            // 保留旧 List.Find 的首个同坐标记录优先语义。
            for (int i = 0; i < cells.Count; i++)
            {
                AgricultureCellSaveData cell = cells[i];
                if (cell != null) agricultureIndex.TryAdd(cell.LocalPosition, cell);
            }
            indexedAgricultureCells = cells;
            indexedAgricultureCount = cells.Count;
            indexedAgricultureVersion = cells.GetEnumerator();
        }
        return agricultureIndex.TryGetValue(position, out AgricultureCellSaveData found) ? found : null;
    }

    internal AgricultureCellSaveData GetOrAddAgricultureCell(Vector2Int position)
    {
        AgricultureCellSaveData cell = FindAgricultureCell(position);
        if (cell != null) return cell;
        AgricultureCells ??= new List<AgricultureCellSaveData>();
        cell = new AgricultureCellSaveData { LocalPosition = position };
        AgricultureCells.Add(cell);
        if (ReferenceEquals(indexedAgricultureCells, AgricultureCells) &&
            indexedAgricultureCount == AgricultureCells.Count - 1)
        {
            agricultureIndex.TryAdd(position, cell);
            indexedAgricultureCount = AgricultureCells.Count;
            indexedAgricultureVersion = AgricultureCells.GetEnumerator();
        }
        return cell;
    }

    #endregion
}

/// <summary>一格农业的持久化快照，Crop 为空代表尚未播种或已收获。</summary>
[Serializable, MemoryPackable]
public partial class AgricultureCellSaveData
{
    public Vector2Int LocalPosition; // 区块内格坐标
    public int SourceTileId; // 水肥状态关联的当前地表
    public int Water; // 当前水分只保存 0～10 整级。
    public float WaterSupplyRemainder; // 连续降水尚未满一份的进度。
    public float WaterConsumptionRemainder; // 连续耗水尚未满一份的进度。
    public float Fertility; // 当前肥力
    public ItemData Crop; // 唯一作物快照
}
