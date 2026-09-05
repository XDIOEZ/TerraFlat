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
    private int fieldContextFrame = -1;
    private float ambientFieldOffset;
    private int fieldChunkWidth;
    private int fieldChunkHeight;
    private int fieldContextVersion;

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
        }

        float weatherOffset = dimension?.ActiveDefinition?.SuppressWeather == true
            ? 0f : WeatherMgr.CalculateWeatherTemperatureOffset(planet);
        ambientFieldOffset = planet != null
            ? planet.GlobalTemperature - PlanetData.DefaultGlobalTemperature + weatherOffset : 0f;
    }

    #endregion

    #region 统一逐格采样

    /// <summary>只读已加载区块的生成温度并叠加天气和局部冷热源；不生成区块、不复制环境数组。</summary>
    public bool TryGetAmbientTemperature(Vector2 worldPosition, out float temperature)
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

        temperature = baseline + ambientFieldOffset + localTemperatureField.Sample(cell);
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

    #endregion
}
