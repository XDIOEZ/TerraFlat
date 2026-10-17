using System;
using FlatWorld.WorldModel;
using Unity.Profiling;
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
    private LiquidTemperatureField liquidTemperatureField;
    private int fieldContextFrame = -1;
    private float ambientFieldOffset;
    private float seasonTiltOffsetAtPole;
    private float orbitalTemperatureFactor;
    private int fieldChunkWidth;
    private int fieldChunkHeight;
    private int fieldContextVersion;
    private static readonly ProfilerMarker PointTemperatureMarker = new("FlatWorld.Temperature.SamplePoint");

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
            liquidTemperatureField = world != null && planet != null && dimensionId != null && width > 0 && height > 0
                ? new LiquidTemperatureField(manager, dimensionId, fieldChunkWidth, fieldChunkHeight, fieldBounds) : null;
        }

        ambientFieldOffset = planet != null
            ? planet.GlobalTemperature - PlanetData.DefaultGlobalTemperature : 0f;
        seasonTiltOffsetAtPole = 0f;
        orbitalTemperatureFactor = 0f;
        if (planet != null && dimension?.ActiveDefinition?.SuppressWeather != true &&
            DayTimeSystem.Instance != null &&
            DayTimeSystem.Instance.TryGetActiveTimeData(out TimeData time))
        {
            SeasonSnapshot season = SeasonCalendar.Sample(time);
            seasonTiltOffsetAtPole = OrbitalSeasonPhysics.ResolveTiltTemperatureOffset(
                time, season.TemperatureOffset, 1f);
            orbitalTemperatureFactor = OrbitalSeasonPhysics.ResolveEccentricTemperatureFactor(
                time, time.GetTotalGameTimeSeconds());
        }
    }

    #endregion

    #region 统一逐格采样

    internal sealed class AmbientChunkSnapshot
    {
        private readonly ChunkTerrainData terrain;
        private readonly Vector2Int origin;
        private readonly WorldTopologyBounds bounds;
        private readonly float ambientOffset, globalOffset, seasonOffset, orbitalFactor, weatherOffset;
        private readonly FrozenInfluence[] influences;

        private readonly struct FrozenInfluence
        {
            public readonly float Target, Strength;
            public readonly bool Cooling;
            public readonly float[] Weights;
            public FrozenInfluence(float target, float strength, bool cooling, float[] weights)
            { Target = target; Strength = strength; Cooling = cooling; Weights = weights; }
        }

        // 来源数值复制，几何权重只在缓存重建时替换，因此旧数组可由快照安全持有。
        internal AmbientChunkSnapshot(ChunkRuntime chunk, WorldTopologyBounds bounds, float ambientOffset,
            float globalOffset, float seasonOffset, float orbitalFactor, float weatherOffset,
            ReadOnlySpan<LocalTemperatureField.Influence> local, ReadOnlySpan<LiquidTemperatureField.Influence> liquid)
        {
            terrain = chunk.Terrain;
            origin = new Vector2Int(chunk.Address.ChunkOrigin.X, chunk.Address.ChunkOrigin.Y);
            this.bounds = bounds;
            this.ambientOffset = ambientOffset;
            this.globalOffset = globalOffset;
            this.seasonOffset = seasonOffset;
            this.orbitalFactor = orbitalFactor;
            this.weatherOffset = weatherOffset;
            influences = local.Length + liquid.Length == 0 ? Array.Empty<FrozenInfluence>() :
                new FrozenInfluence[local.Length + liquid.Length];
            for (int i = 0; i < local.Length; i++)
                influences[i] = new FrozenInfluence(local[i].Source.TargetTemperature,
                    local[i].Source.Strength, local[i].Source.Cooling, local[i].Weights);
            for (int i = 0; i < liquid.Length; i++)
                influences[local.Length + i] = new FrozenInfluence(liquid[i].TargetTemperature, 1f, false, liquid[i].Weights);
        }

        internal bool TrySample(ChunkRuntime chunk, ReadOnlySpan<int> indices, Span<float> temperatures)
        {
            if (terrain.IsDisposed || !ReferenceEquals(chunk?.Terrain, terrain))
                return false;
            if (temperatures.Length < terrain.CellCount)
                throw new ArgumentException("区块温度缓冲区小于格子数量。", nameof(temperatures));
            foreach (int index in indices)
            {
                if ((uint)index >= (uint)terrain.CellCount)
                    throw new ArgumentOutOfRangeException(nameof(indices));
                int x = index % terrain.Width, y = index / terrain.Width;
                if (!terrain.TryGetEnvironmentValue("temperature.celsius", x, y, out float baseline))
                {
                    temperatures[index] = float.NaN;
                    continue;
                }
                Vector2 position = new(origin.x + x + 0.5f, origin.y + y + 0.5f);
                if (bounds.IsWrapped)
                    position = bounds.NormalizePosition(position);
                float ambient = CalculateAmbientBaseline(bounds, position, baseline, ambientOffset, globalOffset,
                    seasonOffset, orbitalFactor, weatherOffset);
                float heating = 0f, cooling = 0f;
                foreach (FrozenInfluence influence in influences)
                    ThermalInfluenceRuntime.Accumulate(ambient, influence.Target,
                        influence.Weights[index] * influence.Strength, ref heating, ref cooling, influence.Cooling);
                temperatures[index] = ThermalInfluenceRuntime.Compose(ambient, heating, cooling);
            }
            return true;
        }
    }

    internal bool TryCaptureAmbientChunk(ChunkRuntime chunk, out AmbientChunkSnapshot snapshot)
    {
        snapshot = null;
        PrepareFieldContext();
        if (localTemperatureField == null || fieldChunkManager == null || chunk?.Terrain == null ||
            chunk.Terrain.IsDisposed || chunk.DataStatus != ChunkDataStatus.Ready ||
            chunk.Address.DimensionId != fieldDimension || chunk.Terrain.Width != fieldChunkWidth ||
            chunk.Terrain.Height != fieldChunkHeight ||
            !fieldChunkManager.TryGetChunkRuntime(chunk.Address, out ChunkRuntime current) || !ReferenceEquals(current, chunk))
            return false;
        Vector2Int origin = new(chunk.Address.ChunkOrigin.X, chunk.Address.ChunkOrigin.Y);
        Vector2 samplePosition = (Vector2)origin + Vector2.one * 0.5f;
        snapshot = new AmbientChunkSnapshot(chunk, fieldBounds, ambientFieldOffset,
            fieldPlanet.GlobalTemperature - PlanetData.DefaultGlobalTemperature, seasonTiltOffsetAtPole,
            orbitalTemperatureFactor, WeatherMgr.ExistingInstance?.GetTemperatureOffsetAt(samplePosition) ?? 0f,
            localTemperatureField.GetChunkInfluences(origin), liquidTemperatureField != null
                ? liquidTemperatureField.GetChunkInfluences(origin) : ReadOnlySpan<LiquidTemperatureField.Influence>.Empty);
        return true;
    }

    /// <summary>只读生成时确定的地理温度，不叠加星球调温、季节、天气或局部热源。</summary>
    public bool TryGetGeographicTemperature(Vector2 worldPosition, out float temperature)
        => TrySampleTemperature(worldPosition, false, out temperature, false);

    /// <summary>只读已加载区块的生成温度并叠加天气和局部冷热源；不生成区块、不复制环境数组。</summary>
    public bool TryGetAmbientTemperature(Vector2 worldPosition, out float temperature)
        => TrySampleTemperature(worldPosition, true, out temperature);

    /// <summary>读取群系与星球气候基温，供历史季节补算；不把当前天气或短时火源延伸到过去。</summary>
    public bool TryGetClimateBaseline(Vector2 worldPosition, out float temperature)
        => TrySampleTemperature(worldPosition, false, out temperature);

    /// <summary>同一次地块读取同时获取当前环境温度与不含短时来源的气候基温。</summary>
    public bool TryGetAmbientAndClimateBaseline(Vector2 worldPosition, out float ambient, out float baseline)
        => TrySampleTemperature(worldPosition, true, out ambient, out baseline);

    /// <summary>同步采样已绑定区块，复用冷热源缓存；结果只用于本次结算，不跨帧保存。</summary>
    internal bool TrySampleAmbientChunk(ChunkRuntime chunk, ReadOnlySpan<int> cellIndices,
        Span<float> temperatures)
    {
        PrepareFieldContext();
        if (localTemperatureField == null || fieldChunkManager == null || chunk?.Terrain == null ||
            chunk.Terrain.IsDisposed || chunk.DataStatus != ChunkDataStatus.Ready ||
            chunk.Address.DimensionId != fieldDimension || chunk.Terrain.Width != fieldChunkWidth ||
            chunk.Terrain.Height != fieldChunkHeight ||
            !fieldChunkManager.TryGetChunkRuntime(chunk.Address, out ChunkRuntime current) ||
            !ReferenceEquals(current, chunk))
            return false;

        ChunkTerrainData terrain = chunk.Terrain;
        if (temperatures.Length < terrain.CellCount)
            throw new ArgumentException("区块温度缓冲区小于格子数量。", nameof(temperatures));
        Vector2Int origin = new(chunk.Address.ChunkOrigin.X, chunk.Address.ChunkOrigin.Y);
        ReadOnlySpan<LocalTemperatureField.Influence> localSources = localTemperatureField.GetChunkInfluences(origin);
        ReadOnlySpan<LiquidTemperatureField.Influence> liquidSources = liquidTemperatureField != null
            ? liquidTemperatureField.GetChunkInfluences(origin) : ReadOnlySpan<LiquidTemperatureField.Influence>.Empty;
        foreach (int index in cellIndices)
        {
            if ((uint)index >= (uint)terrain.CellCount)
                throw new ArgumentOutOfRangeException(nameof(cellIndices), "区块温度采样格超出范围。");
            int x = index % terrain.Width;
            int y = index / terrain.Width;
            if (!terrain.TryGetEnvironmentValue("temperature.celsius", x, y, out float baseline))
            {
                temperatures[index] = float.NaN;
                continue;
            }
            Vector2 position = new(origin.x + x + 0.5f, origin.y + y + 0.5f);
            if (fieldBounds.IsWrapped)
                position = fieldBounds.NormalizePosition(position);
            Vector2Int cell = new(Mathf.FloorToInt(position.x), Mathf.FloorToInt(position.y));
            temperatures[index] = ComposeAmbientTemperature(position, baseline, index, localSources, liquidSources);
        }
        return true;
    }

    /// <summary>共用地块查询，按需要叠加当前季节、天气及局部热源。</summary>
    private bool TrySampleTemperature(Vector2 worldPosition, bool includeTransient, out float temperature,
        bool includePlanetOffset = true)
        => TrySampleTemperature(worldPosition, includeTransient, out temperature, out _, includePlanetOffset);

    private bool TrySampleTemperature(Vector2 worldPosition, bool includeTransient, out float temperature,
        out float climateBaseline, bool includePlanetOffset = true)
    {
        PrepareFieldContext();
        temperature = 0f;
        climateBaseline = 0f;
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
        climateBaseline = baseline + fieldPlanet.GlobalTemperature - PlanetData.DefaultGlobalTemperature;
        if (includeTransient)
        {
            using (PointTemperatureMarker.Auto())
            {
                float ambient = CalculateAmbientBaseline(position, baseline);
                float heating = 0f, cooling = 0f;
                localTemperatureField.AccumulateAtPoint(cell, ambient, ref heating, ref cooling);
                liquidTemperatureField?.AccumulateAtPoint(cell, ambient, ref heating, ref cooling);
                temperature = ThermalInfluenceRuntime.Compose(ambient, heating, cooling);
            }
        }
        else
            temperature = baseline + (includePlanetOffset ? fieldPlanet.GlobalTemperature - PlanetData.DefaultGlobalTemperature : 0f);
        return true;
    }

    // 单格与批量入口共用温度合成，避免雨雪和角色读到不同规则。
    private float ComposeAmbientTemperature(Vector2 position, float baseline, int index,
        ReadOnlySpan<LocalTemperatureField.Influence> localSources,
        ReadOnlySpan<LiquidTemperatureField.Influence> liquidSources)
    {
        float ambient = CalculateAmbientBaseline(position, baseline);
        float heating = 0f, cooling = 0f;
        LocalTemperatureField.Accumulate(localSources, index, ambient, ref heating, ref cooling);
        LiquidTemperatureField.Accumulate(liquidSources, index, ambient, ref heating, ref cooling);
        return ThermalInfluenceRuntime.Compose(ambient, heating, cooling);
    }

    private float CalculateAmbientBaseline(Vector2 position, float baseline)
    {
        return CalculateAmbientBaseline(fieldBounds, position, baseline, ambientFieldOffset,
            fieldPlanet.GlobalTemperature - PlanetData.DefaultGlobalTemperature, seasonTiltOffsetAtPole,
            orbitalTemperatureFactor, WeatherMgr.ExistingInstance?.GetTemperatureOffsetAt(position) ?? 0f);
    }

    private static float CalculateAmbientBaseline(WorldTopologyBounds bounds, Vector2 position, float baseline,
        float ambientOffset, float globalOffset, float seasonTilt, float orbitalFactor, float weatherOffset)
    {
        float poleProximity = OrbitalSeasonPhysics.ResolvePoleProximity(bounds, position);
        float physicalBaseline = baseline + globalOffset;
        float seasonOffset = seasonTilt * poleProximity + Mathf.Max(1f, physicalBaseline + 273.15f) * orbitalFactor;
        return baseline + ambientOffset + seasonOffset + weatherOffset;
    }

    #endregion

    #region 局部温度源

    /// <summary>发布目标温度热源；背景温度变化后直接使用缓存权重重新计算温差。</summary>
    public bool SetLocalTemperatureSource(object owner, Vector2 position, float radius, float targetTemperature,
        bool cooling = false, float strength = 1f)
    {
        PrepareFieldContext();
        if (localTemperatureField == null)
            return false;
        localTemperatureField.SetSource(owner, position, radius, targetTemperature, cooling, strength);
        return true;
    }

    /// <summary>设备关闭、移除或退出世界时撤销自身来源，不影响其他来源。</summary>
    public void RemoveLocalTemperatureSource(object owner)
    {
        localTemperatureField?.RemoveSource(owner);
    }

    #endregion
}
