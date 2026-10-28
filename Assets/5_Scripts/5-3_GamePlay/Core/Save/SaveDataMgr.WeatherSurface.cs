using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;

/// <summary>天气影响的地表批量差量、积雪恢复与联机快照。</summary>
public partial class SaveDataMgr
{
    #region 积雪差量
    public void RecordSnowCell(RuntimeTerrainTileSample sample)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null) return;
        var manager = ChunkMgr.ExistingInstance;
        if (manager != null && manager.TryGetChunkRuntime(sample.Address, out ChunkRuntime chunk))
            RecordWeatherSurfaceChunk(chunk, Array.Empty<int>(),
                new[] { sample.LocalCell.y * sample.Terrain.Width + sample.LocalCell.x });
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

    #region 天气地表批量差量

    public static event Action<WeatherSurfaceChunkSnapshot> AuthoritativeWeatherSurfaceChanged;
    // 非网络订阅者始终正常收事件；联机层另行登记发布器与远端需求查询。
    public static Action<WeatherSurfaceChunkSnapshot> ReplicateWeatherSurfaceToRemote;
    public static Func<string, bool> HasWeatherSurfaceReplicationAudience;
    private readonly Dictionary<int, SnowCellSaveData> weatherSnowIndex = new();
    private readonly Dictionary<int, WeatherSurfaceCellDelta> weatherSurfaceIndex = new();

    private static bool ShouldReplicateWeatherSurface(string worldKey)
    {
        if (ReplicateWeatherSurfaceToRemote == null) return false;
        if (HasWeatherSurfaceReplicationAudience == null) return true;
        try { return HasWeatherSurfaceReplicationAudience(worldKey); }
        catch (Exception exception)
        {
            // 观察者查询故障时保守发布，不能阻断本次权威地表落档。
            Debug.LogWarning($"[天气地表] 远端观察者查询失败，回退完整发布：{exception.Message}");
            return true;
        }
    }

    internal static bool ShouldBuildWeatherSurfaceDelta(string worldKey, out bool replicate)
    {
        replicate = ShouldReplicateWeatherSurface(worldKey);
        return replicate || AuthoritativeWeatherSurfaceChanged != null;
    }

    // 每个区块只建一次格索引，雨雪补水不得覆盖已有作物快照。
    public void RecordWeatherSurfaceChunk(ChunkRuntime chunk, IReadOnlyList<int> soilChanged,
        IReadOnlyList<int> snowChanged)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null || chunk?.Terrain == null ||
            chunk.Terrain.IsDisposed || chunk.DataStatus != ChunkDataStatus.Ready ||
            ((soilChanged?.Count ?? 0) == 0 && (snowChanged?.Count ?? 0) == 0))
            return;

        ChunkTerrainData terrain = chunk.Terrain;
        ChunkSaveRecord record = GetRuntimeChunkRecord(chunk.Address, true);
        record.AgricultureCells ??= new List<AgricultureCellSaveData>();
        record.SnowCells ??= new List<SnowCellSaveData>();
        weatherSnowIndex.Clear();
        weatherSurfaceIndex.Clear();
        string worldKey = GameplayWorldRegistry.ExecutionWorldKey;
        bool publish = ShouldBuildWeatherSurfaceDelta(worldKey, out bool replicate);
        try
        {
            if ((soilChanged?.Count ?? 0) > 0)
            {
                for (int i = 0; i < soilChanged.Count; i++)
                {
                    int index = RequireWeatherSurfaceIndex(terrain, soilChanged[i]);
                    var local = new Vector2Int(index % terrain.Width, index / terrain.Width);
                    if (!FarmlandSystem.IsFarmland(terrain.GetCell(local.x, local.y))) continue;
                    AgricultureCellSaveData cell = record.GetOrAddAgricultureCell(local);
                    cell.SourceTileId = (int)FarmlandSystem.Read(terrain, local, FarmlandSystem.SourceLayer);
                    cell.Water = FarmlandSystem.ReadWaterLevel(terrain, local);
                    cell.WaterSupplyRemainder = FarmlandSystem.Read(terrain, local, FarmlandSystem.WaterSupplyRemainderLayer);
                    cell.WaterConsumptionRemainder = FarmlandSystem.Read(terrain, local,
                        FarmlandSystem.WaterConsumptionRemainderLayer);
                    cell.Fertility = FarmlandSystem.Read(terrain, local, FarmlandSystem.FertilityLayer);
                    if (publish)
                        CaptureWeatherSoil(GetWeatherSurfaceDelta(index, local), terrain, local);
                }
            }
            if ((snowChanged?.Count ?? 0) > 0)
            {
                foreach (SnowCellSaveData cell in record.SnowCells)
                    weatherSnowIndex[cell.LocalPosition.y * terrain.Width + cell.LocalPosition.x] = cell;
                for (int i = 0; i < snowChanged.Count; i++)
                {
                    int index = RequireWeatherSurfaceIndex(terrain, snowChanged[i]);
                    var local = new Vector2Int(index % terrain.Width, index / terrain.Width);
                    if (!weatherSnowIndex.TryGetValue(index, out SnowCellSaveData cell))
                        cell = new SnowCellSaveData { LocalPosition = local };
                    CaptureWeatherSnow(cell, terrain, local);
                    if (cell.Edited || cell.WeatherDepth > 0f || cell.IceMeltProgress > 0f)
                        weatherSnowIndex[index] = cell;
                    else
                        weatherSnowIndex.Remove(index);
                    if (publish)
                        CaptureWeatherSnow(GetWeatherSurfaceDelta(index, local), cell);
                }
                record.SnowCells.Clear();
                foreach (SnowCellSaveData cell in weatherSnowIndex.Values)
                    record.SnowCells.Add(cell);
                record.SnowCells.Sort(CompareWeatherSnowPosition);
            }
            if (publish)
            {
                WeatherSurfaceDelta payload = BuildWeatherSurfaceDelta();
                var snapshot = new WeatherSurfaceChunkSnapshot(worldKey, chunk.Address, terrain.Revision, payload, false);
                AuthoritativeWeatherSurfaceChanged?.Invoke(snapshot);
                if (replicate) ReplicateWeatherSurfaceToRemote?.Invoke(snapshot);
            }
        }
        finally
        {
            weatherSnowIndex.Clear();
            weatherSurfaceIndex.Clear();
        }
    }

    #endregion

    #region 联机完整快照与只读应用

    public WeatherSurfaceChunkSnapshot CaptureWeatherSurfaceChunk(ChunkRuntime chunk)
    {
        if (!GameNetwork.HasStateAuthority || SaveData == null || chunk?.Terrain == null || chunk.Terrain.IsDisposed)
            throw new InvalidOperationException("天气地表快照需要就绪的权威区块。");

        ChunkSaveRecord record = GetRuntimeChunkRecord(chunk.Address, false);
        ChunkTerrainData terrain = chunk.Terrain;
        weatherSurfaceIndex.Clear();
        try
        {
            if (record?.AgricultureCells != null)
                foreach (AgricultureCellSaveData cell in record.AgricultureCells)
                {
                    if (cell.SourceTileId <= 0 ||
                        !FarmlandSystem.IsFarmland(terrain.GetCell(cell.LocalPosition.x, cell.LocalPosition.y)))
                        continue;
                    int index = RequireWeatherSurfaceIndex(terrain,
                        cell.LocalPosition.y * terrain.Width + cell.LocalPosition.x);
                    CaptureWeatherSoil(GetWeatherSurfaceDelta(index, cell.LocalPosition), terrain, cell.LocalPosition);
                }
            if (record?.SnowCells != null)
                foreach (SnowCellSaveData cell in record.SnowCells)
                {
                    int index = RequireWeatherSurfaceIndex(terrain,
                        cell.LocalPosition.y * terrain.Width + cell.LocalPosition.x);
                    CaptureWeatherSnow(GetWeatherSurfaceDelta(index, cell.LocalPosition),
                        WorldSnowInteraction.Capture(terrain, cell.LocalPosition));
                }
            return new WeatherSurfaceChunkSnapshot(GameplayWorldRegistry.ExecutionWorldKey, chunk.Address,
                terrain.Revision, BuildWeatherSurfaceDelta(), true);
        }
        finally
        {
            weatherSurfaceIndex.Clear();
        }
    }

    // 客户端只覆盖层数值，不执行降水、融雪或土壤补水结算。
    public static void ApplyWeatherSurfaceDelta(ChunkRuntime chunk, WeatherSurfaceDelta payload, bool fullState)
    {
        if (GameNetwork.HasStateAuthority || chunk?.Terrain == null || chunk.Terrain.IsDisposed)
            return;
        ChunkTerrainData terrain = chunk.Terrain;
        payload.Validate(terrain.Width, terrain.Height);
        if (fullState)
        {
            for (int y = 0; y < terrain.Height; y++)
            for (int x = 0; x < terrain.Width; x++)
            {
                if (terrain.TryGetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, x, y, out float progress) && progress > 0f)
                    terrain.SetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, x, y, 0f);
                if (WorldSnowSystem.GetWeatherDepth(terrain, x, y) > 0f)
                    terrain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, x, y, 0f);
                if (terrain.TryGetEnvironmentValue(WorldSnowInteraction.EditedLayer, x, y, out float edited) && edited > 0f)
                {
                    terrain.SetEnvironmentValue(WorldSnowInteraction.EditedLayer, x, y, 0f);
                    terrain.SetEnvironmentValue(WorldSnowInteraction.DepthLayer, x, y, 0f);
                    terrain.SetEnvironmentValue(WorldSnowInteraction.SeasonLayer, x, y, 0f);
                }
            }
        }
        foreach (WeatherSurfaceCellDelta cell in payload.Cells)
        {
            int x = cell.LocalPosition.x, y = cell.LocalPosition.y;
            if (cell.SoilChanged && FarmlandSystem.IsFarmland(terrain.GetCell(x, y)))
            {
                terrain.SetEnvironmentValue(FarmlandSystem.SourceLayer, x, y, cell.SoilSourceTileId);
                terrain.SetEnvironmentValue(FarmlandSystem.WaterLayer, x, y, cell.Water);
                terrain.SetEnvironmentValue(FarmlandSystem.WaterSupplyRemainderLayer, x, y, cell.WaterSupplyRemainder);
                terrain.SetEnvironmentValue(FarmlandSystem.WaterConsumptionRemainderLayer, x, y,
                    cell.WaterConsumptionRemainder);
                terrain.SetEnvironmentValue(FarmlandSystem.FertilityLayer, x, y, cell.Fertility);
            }
            if (cell.SnowChanged)
            {
                terrain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, x, y, cell.WeatherDepth);
                terrain.SetEnvironmentValue(WorldSnowInteraction.DepthLayer, x, y, cell.Depth);
                terrain.SetEnvironmentValue(WorldSnowInteraction.SeasonLayer, x, y, cell.SeasonalDepth);
                terrain.SetEnvironmentValue(WorldSnowInteraction.EditedLayer, x, y, cell.Edited ? 1f : 0f);
                terrain.SetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, x, y, cell.IceMeltProgress);
            }
        }
    }

    #endregion

    #region 批次捕获

    private static int RequireWeatherSurfaceIndex(ChunkTerrainData terrain, int index)
    {
        if ((uint)index >= (uint)(terrain.Width * terrain.Height))
            throw new ArgumentOutOfRangeException(nameof(index));
        return index;
    }

    private WeatherSurfaceCellDelta GetWeatherSurfaceDelta(int index, Vector2Int local)
    {
        if (!weatherSurfaceIndex.TryGetValue(index, out WeatherSurfaceCellDelta result))
        {
            result = new WeatherSurfaceCellDelta { LocalPosition = local };
            weatherSurfaceIndex.Add(index, result);
        }
        return result;
    }

    private WeatherSurfaceDelta BuildWeatherSurfaceDelta()
    {
        var result = new WeatherSurfaceDelta();
        foreach (WeatherSurfaceCellDelta cell in weatherSurfaceIndex.Values)
            result.Cells.Add(cell);
        result.Cells.Sort(CompareWeatherSurfacePosition);
        return result;
    }

    private static void CaptureWeatherSoil(WeatherSurfaceCellDelta result, ChunkTerrainData terrain, Vector2Int local)
    {
        result.SoilChanged = true;
        result.SoilSourceTileId = (int)FarmlandSystem.Read(terrain, local, FarmlandSystem.SourceLayer);
        result.Water = FarmlandSystem.ReadWaterLevel(terrain, local);
        result.WaterSupplyRemainder = FarmlandSystem.Read(terrain, local, FarmlandSystem.WaterSupplyRemainderLayer);
        result.WaterConsumptionRemainder = FarmlandSystem.Read(terrain, local,
            FarmlandSystem.WaterConsumptionRemainderLayer);
        result.Fertility = FarmlandSystem.Read(terrain, local, FarmlandSystem.FertilityLayer);
    }

    private static void CaptureWeatherSnow(SnowCellSaveData result, ChunkTerrainData terrain, Vector2Int local)
    {
        terrain.TryGetEnvironmentValue(WorldSnowInteraction.EditedLayer, local.x, local.y, out float edited);
        result.Edited = edited > 0f;
        terrain.TryGetEnvironmentValue(WorldSnowInteraction.DepthLayer, local.x, local.y, out result.Depth);
        terrain.TryGetEnvironmentValue(WorldSnowInteraction.SeasonLayer, local.x, local.y, out result.SeasonalDepth);
        result.WeatherDepth = WorldSnowSystem.GetWeatherDepth(terrain, local.x, local.y);
        terrain.TryGetEnvironmentValue(FrozenSurfaceWaterLayer.MeltProgressLayer, local.x, local.y, out result.IceMeltProgress);
    }

    private static void CaptureWeatherSnow(WeatherSurfaceCellDelta result, SnowCellSaveData snow)
    {
        result.SnowChanged = true;
        result.WeatherDepth = snow.WeatherDepth;
        result.Edited = snow.Edited;
        result.Depth = snow.Depth;
        result.SeasonalDepth = snow.SeasonalDepth;
        result.IceMeltProgress = snow.IceMeltProgress;
    }

    private static int CompareWeatherSnowPosition(SnowCellSaveData a, SnowCellSaveData b)
    {
        int y = a.LocalPosition.y.CompareTo(b.LocalPosition.y);
        return y != 0 ? y : a.LocalPosition.x.CompareTo(b.LocalPosition.x);
    }

    private static int CompareWeatherSurfacePosition(WeatherSurfaceCellDelta a, WeatherSurfaceCellDelta b)
    {
        int y = a.LocalPosition.y.CompareTo(b.LocalPosition.y);
        return y != 0 ? y : a.LocalPosition.x.CompareTo(b.LocalPosition.x);
    }

    #endregion
}

/// <summary>保存区域天气雪与玩家编辑，铲空仍保留标记以免天然雪重新出现。</summary>
[Serializable, MemoryPackable]
public partial class SnowCellSaveData
{
    #region 雪格快照
    public Vector2Int LocalPosition;
    public bool Edited;
    public float Depth;
    public float SeasonalDepth;
    public float WeatherDepth;
    public float IceMeltProgress; // 天然冰融化进度与雪覆盖共用稀疏地表快照。
    #endregion
}

public readonly struct WeatherSurfaceChunkSnapshot
{
    #region 完整批次标识
    public readonly string WorldKey;
    public readonly FlatWorld.WorldModel.WorldAddress Address;
    public readonly long Revision;
    public readonly WeatherSurfaceDelta Payload;
    public readonly bool FullState;

    public WeatherSurfaceChunkSnapshot(string worldKey, FlatWorld.WorldModel.WorldAddress address,
        long revision, WeatherSurfaceDelta payload, bool fullState)
    {
        WorldKey = worldKey;
        Address = address;
        Revision = revision;
        Payload = payload;
        FullState = fullState;
    }
    #endregion
}

[MemoryPackable, Serializable]
public partial class WeatherSurfaceDelta
{
    #region 独立数值载荷
    public List<WeatherSurfaceCellDelta> Cells = new();

    public void Validate(int width, int height)
    {
        if (Cells == null || Cells.Count > width * height)
            throw new InvalidOperationException("天气地表批次格数无效。");
        var seen = new HashSet<Vector2Int>();
        foreach (WeatherSurfaceCellDelta cell in Cells)
        {
            if (cell == null || (uint)cell.LocalPosition.x >= (uint)width ||
                (uint)cell.LocalPosition.y >= (uint)height || !seen.Add(cell.LocalPosition) ||
                cell.Water < 0 || cell.Water > TileData_Farmland.MaxWaterLevel || cell.SoilSourceTileId < 0 ||
                !float.IsFinite(cell.WaterSupplyRemainder) || cell.WaterSupplyRemainder < 0f || cell.WaterSupplyRemainder >= 1f ||
                !float.IsFinite(cell.WaterConsumptionRemainder) || cell.WaterConsumptionRemainder < 0f ||
                cell.WaterConsumptionRemainder >= 1f ||
                !float.IsFinite(cell.Fertility) || cell.Fertility < 0f ||
                !float.IsFinite(cell.WeatherDepth) || cell.WeatherDepth < 0f || cell.WeatherDepth > 1f ||
                !float.IsFinite(cell.Depth) || cell.Depth < 0f ||
                !float.IsFinite(cell.SeasonalDepth) || cell.SeasonalDepth < 0f || cell.SeasonalDepth > 1f ||
                !float.IsFinite(cell.IceMeltProgress) || cell.IceMeltProgress < 0f || cell.IceMeltProgress > 1f)
                throw new InvalidOperationException("天气地表批次包含无效或重复格子。");
        }
    }
    #endregion
}

[MemoryPackable, Serializable]
public partial class WeatherSurfaceCellDelta
{
    #region 单格层值
    public Vector2Int LocalPosition;
    public bool SoilChanged;
    public int SoilSourceTileId;
    public int Water;
    public float WaterSupplyRemainder;
    public float WaterConsumptionRemainder;
    public float Fertility;
    public bool SnowChanged;
    public float WeatherDepth;
    public bool Edited;
    public float Depth;
    public float SeasonalDepth;
    public float IceMeltProgress;
    #endregion
}
