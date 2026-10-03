using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class TemperatureMgr
{
    #region 温度场上下文

    private ChunkMgr fieldChunkManager;
    private WorldRuntime fieldWorld;
    private long fieldWorldEpoch;
    private PlanetData fieldPlanet;
    private string fieldDimension;
    private WorldTopologyBounds fieldBounds;
    private LocalTemperatureField localTemperatureField;
    private RadiantLiquidTemperatureField radiantLiquidTemperatureField;
    private int fieldContextFrame = -1;
    private float ambientFieldOffset;
    private int fieldChunkWidth;
    private int fieldChunkHeight;
    private int fieldContextVersion;
    private readonly Dictionary<object, CellTemperatureSource> cellTemperatureSources = new();
    private readonly Dictionary<Vector2Int, List<CellTemperatureSource>> cellTemperatureIndex = new();

    private sealed class CellTemperatureSource
    {
        public Vector2Int Center;
        public float CenterTemperature;
        public float NeighborOffset;
        public readonly List<Vector2Int> Cells = new(9);
    }

    /// <summary>世界或维度切换时递增，显示任务据此丢弃旧世界尚未完成的采样。</summary>
    public int FieldContextVersion
    {
        get { PrepareFieldContext(); return fieldContextVersion; }
    }

    /// <summary>地块生成温度之外的星球基准与天气修正，同一帧只读取一次。</summary>
    public float AmbientFieldOffset
    {
        get { PrepareFieldContext(); return ambientFieldOffset; }
    }

    private void PrepareFieldContext()
    {
        if (fieldContextFrame == Time.frameCount)
            return;
        fieldContextFrame = Time.frameCount;

        ChunkMgr manager = ChunkMgr.ExistingInstance;
        WorldRuntime world = manager != null ? manager.WorldRuntime : null;
        PlanetData planet = null;
        if (SaveDataMgr.Instance != null)
            SaveDataMgr.Instance.TryGetActivePlanetData(out planet);
        DimensionManager dimension = DimensionManager.ExistingInstance;
        string dimensionId = dimension != null && dimension.ActiveAddress.IsValid
            ? dimension.ActiveAddress.DimensionId : null;
        int width = manager?.ActiveGenerationProfile?.Width ?? 0;
        int height = manager?.ActiveGenerationProfile?.Height ?? 0;
        long epoch = world?.Epoch ?? 0;

        // 世界实例、星球或维度更换时丢弃可重建的来源缓存，避免上一世界的热源残留。
        if (!ReferenceEquals(world, fieldWorld) || !ReferenceEquals(planet, fieldPlanet) ||
            epoch != fieldWorldEpoch || dimensionId != fieldDimension ||
            width != fieldChunkWidth || height != fieldChunkHeight)
        {
            fieldContextVersion++;
            fieldChunkManager = manager;
            fieldWorld = world;
            fieldWorldEpoch = epoch;
            fieldPlanet = planet;
            fieldDimension = dimensionId;
            fieldBounds = default;
            if (planet != null)
                WorldTopologyBounds.TryCreate(planet, out fieldBounds);
            fieldChunkWidth = width;
            fieldChunkHeight = height;
            localTemperatureField = world != null && planet != null && dimensionId != null && width > 0 && height > 0
                ? new LocalTemperatureField(fieldChunkWidth, fieldChunkHeight, fieldBounds) : null;
            radiantLiquidTemperatureField = world != null && planet != null && dimensionId != null && width > 0 && height > 0
                ? new RadiantLiquidTemperatureField(manager, dimensionId, fieldChunkWidth, fieldChunkHeight, fieldBounds) : null;
            cellTemperatureSources.Clear();
            cellTemperatureIndex.Clear();
        }

        float weatherOffset = dimension?.ActiveDefinition?.SuppressWeather == true
            ? 0f : WeatherMgr.CalculateWeatherTemperatureOffset(planet);
        ambientFieldOffset = planet != null
            ? planet.GlobalTemperature - PlanetData.DefaultGlobalTemperature + weatherOffset + DayTimeSystem.GetSeasonTemperatureOffset() : 0f;
    }

    #endregion

    #region 统一逐格采样

    /// <summary>只读已加载区块的生成温度并叠加天气和局部冷热源；不生成区块、不复制环境数组。</summary>
    public bool TryGetAmbientTemperature(Vector2 worldPosition, out float temperature)
        => TrySampleTemperature(worldPosition, true, out temperature);

    /// <summary>读取群系与星球气候基温，供历史季节补算；不把当前天气或短时火源延伸到过去。</summary>
    public bool TryGetClimateBaseline(Vector2 worldPosition, out float temperature)
        => TrySampleTemperature(worldPosition, false, out temperature);

    /// <summary>共用地块查询，按需要叠加当前季节、天气及局部热源。</summary>
    private bool TrySampleTemperature(Vector2 worldPosition, bool includeTransient, out float temperature)
    {
        PrepareFieldContext();
        temperature = 0f;
        if (localTemperatureField == null || fieldChunkManager == null)
            return false;

        Vector2 position = fieldBounds.IsWrapped ? fieldBounds.NormalizePosition(worldPosition) : worldPosition;
        Vector2Int cell = new(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y));
        Int2 origin = new(
            Mathf.FloorToInt((float)cell.x / fieldChunkWidth) * fieldChunkWidth,
            Mathf.FloorToInt((float)cell.y / fieldChunkHeight) * fieldChunkHeight);
        RuntimeWorldAddress address = new(fieldDimension, origin);
        if (!fieldChunkManager.TryGetChunkRuntime(address, out ChunkRuntime chunk) ||
            chunk.DataStatus != ChunkDataStatus.Ready || chunk.Terrain == null || chunk.Terrain.IsDisposed)
            return false;

        int x = cell.x - origin.X;
        int y = cell.y - origin.Y;
        ChunkTerrainData terrain = chunk.Terrain;
        if ((uint)x >= (uint)terrain.Width || (uint)y >= (uint)terrain.Height ||
            !terrain.TryGetEnvironmentValue("temperature.celsius", x, y, out float baseline))
            return false;

        temperature = includeTransient
            ? baseline + ambientFieldOffset + localTemperatureField.Sample(cell) +
              (radiantLiquidTemperatureField?.Sample(cell) ?? 0f)
            : baseline + (fieldPlanet.GlobalTemperature - PlanetData.DefaultGlobalTemperature);
        if (includeTransient)
            temperature = ApplyCellTemperatureSources(cell, temperature);
        // 热液体的当前温度随 Liquid 身份派生，抽干即撤销，不污染地形基温或历史季节。
        if (includeTransient && WorldLiquidSystem.GetSurfaceDepth(terrain, x, y) > 0f &&
            GameRes.ExistingInstance != null &&
            GameRes.ExistingInstance.TryGetLiquidDefinition(terrain.GetLiquidId(x, y), out var liquid) &&
            liquid.WorldWater?.ContactHeatingPerSecond > 0f)
            temperature = Mathf.Max(temperature, liquid.WorldWater.Temperature);
        return true;
    }

    #endregion

    #region 局部温度源

    /// <summary>发布当前世界中的局部温度源；owner 是稳定的运行时来源，正偏移升温、负偏移降温。</summary>
    public bool SetLocalTemperatureSource(object owner, Vector2 position, float radius, float celsiusOffset)
    {
        PrepareFieldContext();
        if (localTemperatureField == null)
            return false;
        localTemperatureField.SetSource(owner, position, radius, celsiusOffset);
        return true;
    }

    /// <summary>设备关闭、移除或退出世界时撤销自身来源，不影响其他来源。</summary>
    public void RemoveLocalTemperatureSource(object owner)
    {
        localTemperatureField?.RemoveSource(owner);
    }

    /// <summary>发布九宫格热源：中心格至少达到设备温度，周围八格只叠加固定温差。</summary>
    public bool SetCellTemperatureSource(object owner, Vector2 position, float centerTemperature, float neighborOffset)
    {
        PrepareFieldContext();
        if (localTemperatureField == null)
            return false;
        if (owner == null)
            throw new System.ArgumentNullException(nameof(owner));
        if (!IsFinite(centerTemperature) || !IsFinite(neighborOffset))
            throw new System.ArgumentOutOfRangeException(nameof(centerTemperature), "格温热源参数必须是有限数。");

        Vector2Int center = new(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y));
        if (fieldBounds.IsWrapped)
            center = fieldBounds.NormalizeCell(center);

        if (cellTemperatureSources.TryGetValue(owner, out CellTemperatureSource current) &&
            current.Center == center &&
            Mathf.Approximately(current.CenterTemperature, centerTemperature) &&
            Mathf.Approximately(current.NeighborOffset, neighborOffset))
        {
            return true;
        }

        RemoveCellTemperatureSource(owner);
        var source = new CellTemperatureSource
        {
            Center = center,
            CenterTemperature = centerTemperature,
            NeighborOffset = neighborOffset
        };
        cellTemperatureSources.Add(owner, source);

        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            Vector2Int cell = center + new Vector2Int(x, y);
            if (fieldBounds.IsWrapped)
                cell = fieldBounds.NormalizeCell(cell);
            if (source.Cells.Contains(cell))
                continue;
            source.Cells.Add(cell);
            if (!cellTemperatureIndex.TryGetValue(cell, out List<CellTemperatureSource> sources))
            {
                sources = new List<CellTemperatureSource>();
                cellTemperatureIndex.Add(cell, sources);
            }
            sources.Add(source);
        }
        return true;
    }

    /// <summary>移除九宫格温度源，不影响普通半径冷热源。</summary>
    public void RemoveCellTemperatureSource(object owner)
    {
        if (owner == null || !cellTemperatureSources.TryGetValue(owner, out CellTemperatureSource source))
            return;

        foreach (Vector2Int cell in source.Cells)
        {
            if (!cellTemperatureIndex.TryGetValue(cell, out List<CellTemperatureSource> sources))
                continue;
            sources.Remove(source);
            if (sources.Count == 0)
                cellTemperatureIndex.Remove(cell);
        }
        cellTemperatureSources.Remove(owner);
    }

    private float ApplyCellTemperatureSources(Vector2Int cell, float temperature)
    {
        if (!cellTemperatureIndex.TryGetValue(cell, out List<CellTemperatureSource> sources))
            return temperature;

        float neighborOffset = 0f;
        float centerTemperature = float.NegativeInfinity;
        foreach (CellTemperatureSource source in sources)
        {
            if (source.Center == cell)
                centerTemperature = Mathf.Max(centerTemperature, source.CenterTemperature);
            else
                neighborOffset += source.NeighborOffset;
        }

        temperature += neighborOffset;
        return float.IsNegativeInfinity(centerTemperature)
            ? temperature
            : Mathf.Max(temperature, centerTemperature);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    #endregion
}
