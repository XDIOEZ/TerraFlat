using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.WorldModel;
using MemoryPack;
using UnityEngine;

public partial class SaveDataMgr
{
    #region 天气地表批量差量

    public static event Action<WeatherSurfaceChunkSnapshot> AuthoritativeWeatherSurfaceChanged;
    private readonly Dictionary<int, AgricultureCellSaveData> weatherSoilIndex = new();
    private readonly Dictionary<int, SnowCellSaveData> weatherSnowIndex = new();
    private readonly Dictionary<int, WeatherSurfaceCellDelta> weatherSurfaceIndex = new();

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
        weatherSoilIndex.Clear();
        weatherSnowIndex.Clear();
        weatherSurfaceIndex.Clear();
        bool publish = AuthoritativeWeatherSurfaceChanged != null;
        try
        {
            if ((soilChanged?.Count ?? 0) > 0)
            {
                foreach (AgricultureCellSaveData cell in record.AgricultureCells)
                    weatherSoilIndex[cell.LocalPosition.y * terrain.Width + cell.LocalPosition.x] = cell;
                for (int i = 0; i < soilChanged.Count; i++)
                {
                    int index = RequireWeatherSurfaceIndex(terrain, soilChanged[i]);
                    var local = new Vector2Int(index % terrain.Width, index / terrain.Width);
                    if (!weatherSoilIndex.TryGetValue(index, out AgricultureCellSaveData cell))
                    {
                        cell = new AgricultureCellSaveData { LocalPosition = local };
                        weatherSoilIndex.Add(index, cell);
                        record.AgricultureCells.Add(cell);
                    }
                    cell.SourceTileId = (int)FarmlandSystem.Read(terrain, local, FarmlandSystem.SourceLayer);
                    cell.Water = FarmlandSystem.Read(terrain, local, FarmlandSystem.WaterLayer);
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
                    if (cell.Edited || cell.WeatherDepth > 0f)
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
                AuthoritativeWeatherSurfaceChanged?.Invoke(new WeatherSurfaceChunkSnapshot(
                    ResolveRuntimePlanetName(), chunk.Address, terrain.Revision, payload, false));
            }
        }
        finally
        {
            weatherSoilIndex.Clear();
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
            return new WeatherSurfaceChunkSnapshot(ResolveRuntimePlanetName(), chunk.Address,
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
            if (cell.SoilChanged)
            {
                terrain.SetEnvironmentValue(FarmlandSystem.SourceLayer, x, y, cell.SoilSourceTileId);
                terrain.SetEnvironmentValue(FarmlandSystem.WaterLayer, x, y, cell.Water);
                terrain.SetEnvironmentValue(FarmlandSystem.FertilityLayer, x, y, cell.Fertility);
                FarmlandSystem.SyncSoilEnvironment(terrain, x, y, cell.Water, cell.Fertility);
            }
            if (cell.SnowChanged)
            {
                terrain.SetEnvironmentValue(WorldSnowSystem.WeatherDepthLayer, x, y, cell.WeatherDepth);
                terrain.SetEnvironmentValue(WorldSnowInteraction.DepthLayer, x, y, cell.Depth);
                terrain.SetEnvironmentValue(WorldSnowInteraction.SeasonLayer, x, y, cell.SeasonalDepth);
                terrain.SetEnvironmentValue(WorldSnowInteraction.EditedLayer, x, y, cell.Edited ? 1f : 0f);
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
        result.Water = FarmlandSystem.Read(terrain, local, FarmlandSystem.WaterLayer);
        result.Fertility = FarmlandSystem.Read(terrain, local, FarmlandSystem.FertilityLayer);
    }

    private static void CaptureWeatherSnow(SnowCellSaveData result, ChunkTerrainData terrain, Vector2Int local)
    {
        terrain.TryGetEnvironmentValue(WorldSnowInteraction.EditedLayer, local.x, local.y, out float edited);
        result.Edited = edited > 0f;
        terrain.TryGetEnvironmentValue(WorldSnowInteraction.DepthLayer, local.x, local.y, out result.Depth);
        terrain.TryGetEnvironmentValue(WorldSnowInteraction.SeasonLayer, local.x, local.y, out result.SeasonalDepth);
        result.WeatherDepth = WorldSnowSystem.GetWeatherDepth(terrain, local.x, local.y);
    }

    private static void CaptureWeatherSnow(WeatherSurfaceCellDelta result, SnowCellSaveData snow)
    {
        result.SnowChanged = true;
        result.WeatherDepth = snow.WeatherDepth;
        result.Edited = snow.Edited;
        result.Depth = snow.Depth;
        result.SeasonalDepth = snow.SeasonalDepth;
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
                !float.IsFinite(cell.Water) || cell.Water < 0f || cell.SoilSourceTileId < 0 ||
                !float.IsFinite(cell.Fertility) || cell.Fertility < 0f ||
                !float.IsFinite(cell.WeatherDepth) || cell.WeatherDepth < 0f || cell.WeatherDepth > 1f ||
                !float.IsFinite(cell.Depth) || cell.Depth < 0f ||
                !float.IsFinite(cell.SeasonalDepth) || cell.SeasonalDepth < 0f || cell.SeasonalDepth > 1f)
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
    public float Water;
    public float Fertility;
    public bool SnowChanged;
    public float WeatherDepth;
    public bool Edited;
    public float Depth;
    public float SeasonalDepth;
    #endregion
}
