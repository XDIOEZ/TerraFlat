using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public class ChunkGenerator_Land : ChunkGeneratorBase
{
    [Title("1. 生物群系")]
    [LabelText("有序群系列表")]
    [PropertyTooltip("按列表顺序解析；顺序固定代表优先级。")]
    public List<BiomeData> biomes = new();

    [Title("2. 温度映射")]
    [LabelText("摄氏温度区间")]
    public Vector2 TemperatureCelsiusRange = new Vector2(0f, 50f);

    [Title("3. 环境噪声通道")]
    [LabelText("噪声配置")]
    public List<TerrainNoiseConfig> NoiseConfigs = new();

    [ShowInInspector, Sirenix.OdinInspector.ReadOnly, MultiLineProperty(3)]
    [LabelText("通道配置摘要")]
    private string NoiseConfigurationSummary => BuildNoiseConfigurationSummary();

    [Title("4. 高度后处理")]
    [LabelText("启用高度二次强化")]
    public bool enableHeightSecondaryBoost;

    [LabelText("强化强度")]
    [Range(0f, 2f)]
    public float heightSecondaryBoostStrength = 1f;

    [Title("5. 风场与地形降雨")]
    [LabelText("区域风场")]
    public WindFieldConfig WindField = WindFieldConfig.Default;

    [LabelText("逆风采样距离"), Min(8)]
    public int OrographicSampleDistance = 64;

    [LabelText("逆风采样数"), Range(1, 8)]
    public int OrographicSampleCount = 4;

    [LabelText("迎风增雨强度"), Range(0f, 2f)]
    public float WindwardRainGain = 0.8f;

    [LabelText("背风雨影强度"), Range(0f, 2f)]
    public float LeewardRainLoss = 0.6f;

    public override GenerationStage Stage => GenerationStage.BaseTerrain;
    public Vector2 ChunkSize => ChunkMgr.GetChunkSize();
    public float NoiseScale => ResolveNoiseScale(_sourcePlanetData ?? SaveDataMgr.Instance?.GetCurrentPlanetData());

    [NonSerialized] private PlanetData _sourcePlanetData;
    [NonSerialized] private int _generationSeed = 1;
    [NonSerialized] private BiomeResolver _resolver;
    [NonSerialized] private byte[] _runtimeBiomeIndices;
    [NonSerialized] private int _runtimeBiomeWidth;
    [NonSerialized] private int _runtimeBiomeHeight;
    [NonSerialized] private Vector2Int _runtimeBiomeOrigin;
    [NonSerialized] private IWindFieldProvider _windFieldProvider;
    [NonSerialized] private IWorldGenerationDomain _worldDomain;

    [NonSerialized] private bool _jobActive;
    [NonSerialized] private JobHandle _activeHandle;
    [NonSerialized] private NativeArray<TerrainNoiseConfig> _activeNoiseConfigs;
    [NonSerialized] private NativeArray<CompiledBiomeRange> _activeBiomeRanges;
    [NonSerialized] private NativeArray<float4> _activeEnvironment;
    [NonSerialized] private NativeArray<float2> _activeWind;
    [NonSerialized] private NativeArray<byte> _activeBiomeIndices;

    public IWindFieldProvider WindFieldProvider
    {
        get => _windFieldProvider ??= RegionalRandomWindFieldProvider.Instance;
        set => _windFieldProvider = value ?? throw new ArgumentNullException(nameof(value));
    }

    public IWorldGenerationDomain WorldDomain
    {
        get => _worldDomain ??= UnboundedWorldGenerationDomain.Instance;
        set => _worldDomain = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override IEnumerator GenerateAsync(MapGenerationContext context, int workBatchSize)
    {
        if (context?.Map == null)
            throw new ArgumentNullException(nameof(context), "[ChunkGenerator_Land] 缺少地图生成上下文。");

        Map = context.Map;
        _sourcePlanetData = context.PlanetData ?? SaveDataMgr.Instance?.GetCurrentPlanetData();
        _generationSeed = context.WorldSeed == 0 ? 1 : context.WorldSeed;
        WorldDomain = context.WorldDomain;
        ValidateConfiguration();
        InitializeMapStorage(Map);

        int width = Map.Data.Width;
        int height = Map.Data.Height;
        int cellCount = checked(width * height);
        TerrainNoiseConfig[] noiseConfigs = NoiseConfigs.ToArray();
        CompiledBiomeRange[] biomeRanges = _resolver.CopyCompiledRanges();

        _activeNoiseConfigs = new NativeArray<TerrainNoiseConfig>(noiseConfigs, Allocator.Persistent);
        _activeBiomeRanges = new NativeArray<CompiledBiomeRange>(biomeRanges, Allocator.Persistent);
        _activeEnvironment = new NativeArray<float4>(cellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _activeWind = new NativeArray<float2>(cellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _activeBiomeIndices = new NativeArray<byte>(cellCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        try
        {
            WorldTopologyDomain topologyDomain = ResolveTopologyDomain(_sourcePlanetData);
            if (WindFieldProvider is RegionalRandomWindFieldProvider)
            {
                var job = new LandEnvironmentJob
                {
                    Origin = new int2(Map.Data.position.x, Map.Data.position.y),
                    Width = width,
                    NoiseScale = ResolveNoiseScale(_sourcePlanetData),
                    TopologyDomain = topologyDomain,
                    WorldSeed = _generationSeed,
                    TemperatureCelsiusRange = new float2(TemperatureCelsiusRange.x, TemperatureCelsiusRange.y),
                    EnableHeightBoost = enableHeightSecondaryBoost,
                    HeightBoostStrength = heightSecondaryBoostStrength,
                    WindField = WindField,
                    OrographicSampleDistance = OrographicSampleDistance,
                    OrographicSampleCount = OrographicSampleCount,
                    WindwardRainGain = WindwardRainGain,
                    LeewardRainLoss = LeewardRainLoss,
                    NoiseConfigs = _activeNoiseConfigs,
                    BiomeRanges = _activeBiomeRanges,
                    Environment = _activeEnvironment,
                    Wind = _activeWind,
                    BiomeIndices = _activeBiomeIndices
                };

                _activeHandle = job.Schedule(cellCount, 64);
                _jobActive = true;
                while (!_activeHandle.IsCompleted)
                {
                    if (context.IsCancellationRequested)
                        yield break;
                    yield return null;
                }

                _activeHandle.Complete();
            }
            else
            {
                var samplingBudget = new ChunkGenerationWorkBudget(Map, Mathf.Max(1, workBatchSize));
                for (int index = 0; index < cellCount; index++)
                {
                    int localX = index % width;
                    int localY = index / width;
                    Vector2Int worldPosition = Map.Data.position + new Vector2Int(localX, localY);
                    ClimateSample sample = SampleClimateAtWorld(worldPosition, _generationSeed, _sourcePlanetData);
                    EnvironmentSample environment = sample.Environment;
                    _activeEnvironment[index] = new float4(
                        environment.Temperature,
                        environment.TemperatureCelsius,
                        environment.Precipitation,
                        environment.Height);
                    _activeWind[index] = new float2(sample.Wind.Direction.x, sample.Wind.Direction.y);
                    _activeBiomeIndices[index] = _resolver.ResolveIndex(environment);

                    if (!samplingBudget.ShouldYield())
                        continue;
                    yield return null;
                    samplingBudget.BeginNextFrame();
                }
            }

            var managedBiomeIndices = new byte[cellCount];
            _activeBiomeIndices.CopyTo(managedBiomeIndices);
            SetRuntimeBiomeCache(_resolver, managedBiomeIndices, width, height, Map.Data.position);
            context.SetBiomeCache(_resolver, managedBiomeIndices, width, height);

            var budget = new ChunkGenerationWorkBudget(Map, Mathf.Max(1, workBatchSize));
            for (int index = 0; index < cellCount; index++)
            {
                int localX = index % width;
                int localY = index / width;
                Vector2Int worldPosition = Map.Data.position + new Vector2Int(localX, localY);
                byte biomeIndex = managedBiomeIndices[index];
                BiomeData biome = _resolver.GetBiome(biomeIndex);
                if (biome == null)
                {
                    float4 invalidEnvironment = _activeEnvironment[index];
                    throw new InvalidOperationException(
                        $"位置 {worldPosition} 未匹配任何 Biome：" +
                        $"T={invalidEnvironment.x}, P={invalidEnvironment.z}, H={invalidEnvironment.w}。");
                }

                float4 environment = _activeEnvironment[index];
                Map.Data.SetEnvironmentAtLocal(
                    localX,
                    localY,
                    environment.x,
                    environment.y,
                    environment.z,
                    environment.w);
                float2 wind = _activeWind[index];
                Map.Data.SetWindAtLocal(localX, localY, new Vector2(wind.x, wind.y));

                Tile_Block tileBlock = GetTerrainTileBlock(biome);
                TileData tile = tileBlock.tileDataTemplate.Clone();
                tile.Initialize_Env(Map.Data.EnvironmentLayers, localX, localY);
                tile.position = new Vector3Int(worldPosition.x, worldPosition.y, 0);
                if (!Map.Data.SetBaseTile(worldPosition, tile))
                    throw new InvalidOperationException($"无法写入基础地形：{worldPosition}");

                if (!budget.ShouldYield())
                    continue;

                yield return null;
                budget.BeginNextFrame();
            }
        }
        finally
        {
            CompleteAndDisposeActiveJob();
        }
    }

    public override void CancelPendingWork()
    {
        CompleteAndDisposeActiveJob();
    }

    [Button("生成随机地图")]
    public void GenerateRandomMap_TileData(Map map, PlanetData planetData)
    {
        if (map == null)
            throw new ArgumentNullException(nameof(map));

        DimensionManager dimensionManager = DimensionManager.Instance;
        int baseSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        var context = new MapGenerationContext(
            map,
            planetData,
            dimensionManager != null ? dimensionManager.GetActiveGenerationSeed(baseSeed) : baseSeed,
            dimensionManager != null ? dimensionManager.ActiveAddress : default,
            dimensionManager != null ? dimensionManager.ActiveDefinition : null,
            WrappedWorldGenerationDomain.Create(planetData));
        GenerateImmediate(context);
    }

    public EnvironmentSample SampleEnvironmentAtWorld(Vector2Int worldPosition, int worldSeed)
    {
        return SampleEnvironmentAtWorld(
            worldPosition,
            worldSeed,
            _sourcePlanetData ?? SaveDataMgr.Instance?.GetCurrentPlanetData());
    }

    public EnvironmentSample SampleEnvironmentAtWorld(
        Vector2Int worldPosition,
        int worldSeed,
        PlanetData sourcePlanetData)
    {
        return SampleClimateAtWorld(worldPosition, worldSeed, sourcePlanetData).Environment;
    }

    public virtual ClimateSample SampleClimateAtWorld(Vector2Int worldPosition, int worldSeed)
    {
        return SampleClimateAtWorld(
            worldPosition,
            worldSeed,
            _sourcePlanetData ?? SaveDataMgr.Instance?.GetCurrentPlanetData());
    }

    public virtual ClimateSample SampleClimateAtWorld(
        Vector2Int worldPosition,
        int worldSeed,
        PlanetData sourcePlanetData)
    {
        if (WorldDomain is WrappedWorldGenerationDomain wrappedDomain)
            worldPosition = wrappedDomain.Bounds.NormalizeCell(worldPosition);
        else if (WorldTopologyBounds.TryCreate(sourcePlanetData, out WorldTopologyBounds sourceBounds))
            worldPosition = sourceBounds.NormalizeCell(worldPosition);

        if (!WorldDomain.Contains(worldPosition))
            throw new ArgumentOutOfRangeException(nameof(worldPosition), $"世界坐标不在当前生成域内：{worldPosition}");

        float noiseScale = ResolveNoiseScale(sourcePlanetData);
        WorldTopologyDomain topologyDomain = ResolveTopologyDomain(sourcePlanetData);
        int generationSeed = worldSeed == 0 ? 1 : worldSeed;
        SampleChannels(
            new float2(worldPosition.x, worldPosition.y),
            noiseScale,
            generationSeed,
            topologyDomain,
            out float temperature,
            out float basePrecipitation,
            out float height);
        WindSample wind = WindFieldProvider is RegionalRandomWindFieldProvider
            ? new WindSample(ToVector2(WindFieldKernel.Sample(
                new float2(worldPosition.x, worldPosition.y),
                generationSeed,
                WindField,
                topologyDomain)))
            : WindFieldProvider.Sample(worldPosition, generationSeed, WindField);
        int sampleCount = Mathf.Clamp(OrographicSampleCount, 1, 8);
        float sampleDistance = Mathf.Max(8f, OrographicSampleDistance);
        float meanUpwindHeight = 0f;
        float maxUpwindHeight = 0f;
        Vector2 windDirection = wind.Direction;
        for (int sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
        {
            float distance = sampleDistance * sampleIndex / sampleCount;
            Vector2 sampleWorld = (Vector2)worldPosition - windDirection * distance;
            float upwindHeight = SampleHeightChannel(
                new float2(sampleWorld.x, sampleWorld.y),
                noiseScale,
                generationSeed,
                topologyDomain);
            meanUpwindHeight += upwindHeight;
            maxUpwindHeight = Mathf.Max(maxUpwindHeight, upwindHeight);
        }

        meanUpwindHeight /= sampleCount;
        float precipitation = ClimateFieldKernel.ApplyOrographicPrecipitation(
            basePrecipitation,
            height,
            meanUpwindHeight,
            maxUpwindHeight,
            WindwardRainGain,
            LeewardRainLoss);
        float celsius = math.lerp(TemperatureCelsiusRange.x, TemperatureCelsiusRange.y, temperature);
        return new ClimateSample(
            new EnvironmentSample(temperature, celsius, precipitation, height),
            basePrecipitation,
            wind);
    }

    public virtual float SampleHeightAtWorld(Vector2Int worldPosition, int worldSeed, PlanetData sourcePlanetData = null)
    {
        float noiseScale = ResolveNoiseScale(sourcePlanetData ?? _sourcePlanetData);
        WorldTopologyDomain topologyDomain = ResolveTopologyDomain(sourcePlanetData ?? _sourcePlanetData);
        return SampleHeightChannel(
            new float2(worldPosition.x, worldPosition.y),
            noiseScale,
            worldSeed == 0 ? 1 : worldSeed,
            topologyDomain);
    }

    public bool TryResolveBiome(EnvironmentSample sample, out BiomeData biome)
    {
        EnsureResolver();
        biome = _resolver.Resolve(sample);
        return biome != null;
    }

    public bool TryGetBiomeAtWorld(Vector2Int worldPosition, out BiomeData biome)
    {
        biome = null;
        EnsureResolver();
        if (Map?.Data == null || !Map.Data.TryGetEnvironmentLocalPos(worldPosition, out Vector2Int localPosition))
            return false;

        if (_runtimeBiomeIndices != null &&
            _runtimeBiomeOrigin == Map.Data.position &&
            _runtimeBiomeWidth == Map.Data.Width &&
            _runtimeBiomeHeight == Map.Data.Height)
        {
            int index = localPosition.y * _runtimeBiomeWidth + localPosition.x;
            biome = _resolver.GetBiome(_runtimeBiomeIndices[index]);
            return biome != null;
        }

        RebuildRuntimeBiomeCacheFromEnvironment();
        int rebuiltIndex = localPosition.y * _runtimeBiomeWidth + localPosition.x;
        biome = _resolver.GetBiome(_runtimeBiomeIndices[rebuiltIndex]);
        return biome != null;
    }

    public bool TryFindWalkableTerrainNear(
        Vector2Int anchor,
        int worldSeed,
        int maxSearchRadius,
        int maxSamples,
        out Vector2Int terrainPosition)
    {
        return TryFindWalkableTerrainNear(
            anchor,
            worldSeed,
            _sourcePlanetData ?? SaveDataMgr.Instance?.GetCurrentPlanetData(),
            null,
            maxSearchRadius,
            maxSamples,
            out terrainPosition);
    }

    public bool TryFindWalkableTerrainNear(
        Vector2Int anchor,
        int worldSeed,
        PlanetData sourcePlanetData,
        ChunkGenerator_River riverGenerator,
        int maxSearchRadius,
        int maxSamples,
        out Vector2Int terrainPosition)
    {
        bool wrapped = WorldTopologyBounds.TryCreate(
            sourcePlanetData,
            out WorldTopologyBounds topologyBounds);
        if (wrapped)
            anchor = topologyBounds.NormalizeCell(anchor);

        terrainPosition = anchor;
        int searchRadius = Mathf.Max(0, maxSearchRadius);
        int sampleBudget = Mathf.Max(1, maxSamples);
        int sampled = 0;
        var preview = new TerrainPreviewSampler(this, riverGenerator, sourcePlanetData, worldSeed);
        HashSet<Vector2Int> visited = wrapped ? new HashSet<Vector2Int>() : null;

        visited?.Add(anchor);
        if (IsWalkableTerrainAtWorld(anchor, preview))
            return true;
        sampled++;

        int localRadius = Mathf.Min(searchRadius, 8);
        for (int y = -localRadius; y <= localRadius && sampled < sampleBudget; y++)
        {
            for (int x = -localRadius; x <= localRadius && sampled < sampleBudget; x++)
            {
                if (x == 0 && y == 0)
                    continue;

                Vector2Int candidate = anchor + new Vector2Int(x, y);
                if (wrapped)
                {
                    candidate = topologyBounds.NormalizeCell(candidate);
                    if (!visited.Add(candidate))
                        continue;
                }
                sampled++;
                if (IsWalkableTerrainAtWorld(candidate, preview))
                {
                    terrainPosition = candidate;
                    return true;
                }
            }
        }

        if (searchRadius == 0 || sampled >= sampleBudget)
            return false;

        int gridSize = Mathf.Max(2, Mathf.CeilToInt(Mathf.Sqrt(sampleBudget - sampled)));
        for (int y = 0; y < gridSize && sampled < sampleBudget; y++)
        {
            for (int x = 0; x < gridSize && sampled < sampleBudget; x++)
            {
                Vector2Int candidate = new Vector2Int(
                    anchor.x + Mathf.RoundToInt(Mathf.Lerp(-searchRadius, searchRadius, (x + 0.5f) / gridSize)),
                    anchor.y + Mathf.RoundToInt(Mathf.Lerp(-searchRadius, searchRadius, (y + 0.5f) / gridSize)));
                if (wrapped)
                {
                    candidate = topologyBounds.NormalizeCell(candidate);
                    if (!visited.Add(candidate))
                        continue;
                }
                sampled++;
                if (IsWalkableTerrainAtWorld(candidate, preview))
                {
                    terrainPosition = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    public bool IsWalkableTerrainAtWorld(Vector2Int worldPosition, int worldSeed)
    {
        return IsWalkableTerrainAtWorld(
            worldPosition,
            worldSeed,
            _sourcePlanetData ?? SaveDataMgr.Instance?.GetCurrentPlanetData(),
            null);
    }

    public bool IsWalkableTerrainAtWorld(
        Vector2Int worldPosition,
        int worldSeed,
        PlanetData sourcePlanetData,
        ChunkGenerator_River riverGenerator)
    {
        var preview = new TerrainPreviewSampler(this, riverGenerator, sourcePlanetData, worldSeed);
        return IsWalkableTerrainAtWorld(worldPosition, preview);
    }

    private static bool IsWalkableTerrainAtWorld(
        Vector2Int worldPosition,
        TerrainPreviewSampler preview)
    {
        if (!preview.TrySample(worldPosition, out TerrainPreviewSample sample) || sample.HasWater)
            return false;

        TileData template = GetTerrainTileBlock(sample.Biome)?.tileDataTemplate;
        return template != null && template is not TileData_Water && template.IsWalkable;
    }

    public byte[] CopyRuntimeBiomeIndices()
    {
        return _runtimeBiomeIndices == null ? null : (byte[])_runtimeBiomeIndices.Clone();
    }

    public virtual void ValidateConfiguration()
    {
        if (NoiseConfigs == null || NoiseConfigs.Count != 3)
            throw new InvalidOperationException("地形必须恰好配置高度、降水、温度三个噪声通道。");

        var configuredChannels = new HashSet<NoiseType>();
        for (int i = 0; i < NoiseConfigs.Count; i++)
        {
            TerrainNoiseConfig config = NoiseConfigs[i];
            if (!TerrainNoiseKernel.IsValid(config, out string reason))
                throw new InvalidOperationException($"NoiseConfigs[{i}] 非法：{reason}");
            if (!configuredChannels.Add(config.noiseType))
                throw new InvalidOperationException($"噪声通道重复：{config.noiseType}");
        }

        foreach (NoiseType required in Enum.GetValues(typeof(NoiseType)))
        {
            if (!configuredChannels.Contains(required))
                throw new InvalidOperationException($"缺少必需噪声通道：{required}");
        }

        if (!IsFinite(TemperatureCelsiusRange.x) || !IsFinite(TemperatureCelsiusRange.y) ||
            TemperatureCelsiusRange.x > TemperatureCelsiusRange.y)
        {
            throw new InvalidOperationException("摄氏温度区间非法。");
        }

        if (!IsFinite(heightSecondaryBoostStrength) || heightSecondaryBoostStrength < 0f)
            throw new InvalidOperationException("高度二次强化强度必须是有限非负数。");
        if (!IsFinite(WindField.RegionSize) || WindField.RegionSize < 8f)
            throw new InvalidOperationException("风区尺寸必须是大于等于 8 的有限数。");
        if (OrographicSampleDistance < 8 || OrographicSampleCount < 1 || OrographicSampleCount > 8)
            throw new InvalidOperationException("地形降雨采样距离或数量非法。");
        if (!IsFinite(WindwardRainGain) || WindwardRainGain < 0f ||
            !IsFinite(LeewardRainLoss) || LeewardRainLoss < 0f)
        {
            throw new InvalidOperationException("地形降雨增益必须是有限非负数。");
        }

        _resolver = new BiomeResolver(biomes);
        for (int i = 0; i < biomes.Count; i++)
            GetTerrainTileBlock(biomes[i]);
    }

    public static float ResolveNoiseScale(PlanetData sourcePlanetData)
    {
        float configuredScale = sourcePlanetData != null
            ? sourcePlanetData.NoiseScale
            : PlanetData.DefaultNoiseScale;
        if (!IsFinite(configuredScale) || configuredScale <= 0f)
            return PlanetData.DefaultNoiseScale;
        return PlanetData.NormalizeNoiseScale(configuredScale);
    }

    private void InitializeMapStorage(Map map)
    {
        if (map.Data == null)
            map.BindData(new Data_TileMap());

        if (map.transform.parent != null)
        {
            map.Data.position = new Vector2Int(
                Mathf.RoundToInt(map.transform.parent.position.x),
                Mathf.RoundToInt(map.transform.parent.position.y));
        }

        Vector2 chunkSize = ChunkSize;
        int width = Mathf.Max(1, Mathf.RoundToInt(chunkSize.x));
        int height = Mathf.Max(1, Mathf.RoundToInt(chunkSize.y));
        map.Data.EnsureTileStorage(width, height);
        map.Data.ClearAllTiles();
        map.Data.EnsureEnvironmentStorage(width, height);
    }

    private void SampleChannels(
        float2 worldPosition,
        float noiseScale,
        int worldSeed,
        in WorldTopologyDomain topologyDomain,
        out float temperature,
        out float precipitation,
        out float height)
    {
        float3 sums = float3.zero;
        int3 counts = int3.zero;
        if (NoiseConfigs != null)
        {
            for (int i = 0; i < NoiseConfigs.Count; i++)
            {
                TerrainNoiseConfig config = NoiseConfigs[i];
                float value = TerrainNoiseKernel.SampleBurst(
                    config,
                    worldPosition,
                    noiseScale,
                    worldSeed,
                    topologyDomain);
                AccumulateChannel(config.noiseType, value, ref sums, ref counts);
            }
        }

        temperature = counts.x > 0 ? sums.x / counts.x : TerrainNoiseKernel.DefaultChannelValue;
        precipitation = counts.y > 0 ? sums.y / counts.y : TerrainNoiseKernel.DefaultChannelValue;
        height = counts.z > 0 ? sums.z / counts.z : TerrainNoiseKernel.DefaultChannelValue;
        height = TerrainNoiseKernel.ApplyHeightBoost(height, enableHeightSecondaryBoost, heightSecondaryBoostStrength);
    }

    private float SampleHeightChannel(
        float2 worldPosition,
        float noiseScale,
        int worldSeed,
        in WorldTopologyDomain topologyDomain)
    {
        float sum = 0f;
        int count = 0;
        if (NoiseConfigs != null)
        {
            for (int i = 0; i < NoiseConfigs.Count; i++)
            {
                TerrainNoiseConfig config = NoiseConfigs[i];
                if (config.noiseType != NoiseType.Height)
                    continue;
                sum += TerrainNoiseKernel.SampleBurst(
                    config,
                    worldPosition,
                    noiseScale,
                    worldSeed,
                    topologyDomain);
                count++;
            }
        }

        float height = count > 0 ? sum / count : TerrainNoiseKernel.DefaultChannelValue;
        return TerrainNoiseKernel.ApplyHeightBoost(
            height,
            enableHeightSecondaryBoost,
            heightSecondaryBoostStrength);
    }

    private static float SampleHeightChannel(
        NativeArray<TerrainNoiseConfig> configs,
        float2 worldPosition,
        float noiseScale,
        int worldSeed,
        in WorldTopologyDomain topologyDomain,
        bool enableHeightBoost,
        float heightBoostStrength)
    {
        float sum = 0f;
        int count = 0;
        for (int i = 0; i < configs.Length; i++)
        {
            TerrainNoiseConfig config = configs[i];
            if (config.noiseType != NoiseType.Height)
                continue;
            sum += TerrainNoiseKernel.SampleBurst(
                config,
                worldPosition,
                noiseScale,
                worldSeed,
                topologyDomain);
            count++;
        }

        float height = count > 0 ? sum / count : TerrainNoiseKernel.DefaultChannelValue;
        return TerrainNoiseKernel.ApplyHeightBoost(height, enableHeightBoost, heightBoostStrength);
    }

    private static void AccumulateChannel(
        NoiseType type,
        float value,
        ref float3 sums,
        ref int3 counts)
    {
        switch (type)
        {
            case NoiseType.Temperature:
                sums.x += value;
                counts.x++;
                break;
            case NoiseType.Precipitation:
                sums.y += value;
                counts.y++;
                break;
            case NoiseType.Height:
                sums.z += value;
                counts.z++;
                break;
        }
    }

    private void EnsureResolver()
    {
        _resolver ??= new BiomeResolver(biomes);
    }

    private void SetRuntimeBiomeCache(
        BiomeResolver resolver,
        byte[] indices,
        int width,
        int height,
        Vector2Int origin)
    {
        _resolver = resolver;
        _runtimeBiomeIndices = indices;
        _runtimeBiomeWidth = width;
        _runtimeBiomeHeight = height;
        _runtimeBiomeOrigin = origin;
    }

    private void RebuildRuntimeBiomeCacheFromEnvironment()
    {
        int width = Map.Data.Width;
        int height = Map.Data.Height;
        var indices = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var sample = new EnvironmentSample(
                    Map.Data.EnvironmentLayers.Temperature[x, y],
                    Map.Data.EnvironmentLayers.TemperatureCelsius[x, y],
                    Map.Data.EnvironmentLayers.Precipitation[x, y],
                    Map.Data.EnvironmentLayers.Height[x, y]);
                indices[y * width + x] = _resolver.ResolveIndex(sample);
            }
        }

        SetRuntimeBiomeCache(_resolver, indices, width, height, Map.Data.position);
    }

    internal static Tile_Block GetTerrainTileBlock(BiomeData biome)
    {
        if (biome?.TerrainConfig?.TileSpawns_NoSO == null || biome.TerrainConfig.TileSpawns_NoSO.Count == 0)
            throw new InvalidOperationException($"Biome {biome?.BiomeId ?? "null"} 缺少基础 Tile 配置。");

        Tile_Block block = biome.TerrainConfig.TileSpawns_NoSO[0]?.TileBlock;
        if (block?.tileDataTemplate == null)
            throw new InvalidOperationException($"Biome {biome.BiomeId} 的基础 Tile 无效。");
        return block;
    }

    private string BuildNoiseConfigurationSummary()
    {
        if (NoiseConfigs == null)
            return "未配置";

        int height = 0;
        int precipitation = 0;
        int temperature = 0;
        for (int i = 0; i < NoiseConfigs.Count; i++)
        {
            switch (NoiseConfigs[i].noiseType)
            {
                case NoiseType.Height: height++; break;
                case NoiseType.Precipitation: precipitation++; break;
                case NoiseType.Temperature: temperature++; break;
            }
        }

        return $"高度 {height} | 降水 {precipitation} | 温度 {temperature}";
    }

    private void CompleteAndDisposeActiveJob()
    {
        if (_jobActive)
            _activeHandle.Complete();
        if (_activeNoiseConfigs.IsCreated)
            _activeNoiseConfigs.Dispose();
        if (_activeBiomeRanges.IsCreated)
            _activeBiomeRanges.Dispose();
        if (_activeEnvironment.IsCreated)
            _activeEnvironment.Dispose();
        if (_activeWind.IsCreated)
            _activeWind.Dispose();
        if (_activeBiomeIndices.IsCreated)
            _activeBiomeIndices.Dispose();
        _jobActive = false;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static WorldTopologyDomain ResolveTopologyDomain(PlanetData planetData)
    {
        return WorldTopologyBounds.TryCreate(planetData, out WorldTopologyBounds bounds)
            ? bounds.ToDomain()
            : default;
    }

    private static Vector2 ToVector2(float2 value) => new(value.x, value.y);

    [BurstCompile(FloatMode = FloatMode.Strict, FloatPrecision = FloatPrecision.Standard)]
    private struct LandEnvironmentJob : IJobParallelFor
    {
        public int2 Origin;
        public int Width;
        public float NoiseScale;
        public WorldTopologyDomain TopologyDomain;
        public int WorldSeed;
        public float2 TemperatureCelsiusRange;
        public bool EnableHeightBoost;
        public float HeightBoostStrength;
        public WindFieldConfig WindField;
        public int OrographicSampleDistance;
        public int OrographicSampleCount;
        public float WindwardRainGain;
        public float LeewardRainLoss;

        [Unity.Collections.ReadOnly] public NativeArray<TerrainNoiseConfig> NoiseConfigs;
        [Unity.Collections.ReadOnly] public NativeArray<CompiledBiomeRange> BiomeRanges;
        [WriteOnly] public NativeArray<float4> Environment;
        [WriteOnly] public NativeArray<float2> Wind;
        [WriteOnly] public NativeArray<byte> BiomeIndices;

        public void Execute(int index)
        {
            int localX = index % Width;
            int localY = index / Width;
            float2 worldPosition = new float2(Origin.x + localX, Origin.y + localY);
            float3 sums = float3.zero;
            int3 counts = int3.zero;
            for (int i = 0; i < NoiseConfigs.Length; i++)
            {
                TerrainNoiseConfig config = NoiseConfigs[i];
                float value = TerrainNoiseKernel.SampleBurst(
                    config,
                    worldPosition,
                    NoiseScale,
                    WorldSeed,
                    TopologyDomain);
                AccumulateChannel(config.noiseType, value, ref sums, ref counts);
            }

            float temperature = counts.x > 0 ? sums.x / counts.x : TerrainNoiseKernel.DefaultChannelValue;
            float basePrecipitation = counts.y > 0 ? sums.y / counts.y : TerrainNoiseKernel.DefaultChannelValue;
            float height = counts.z > 0 ? sums.z / counts.z : TerrainNoiseKernel.DefaultChannelValue;
            height = TerrainNoiseKernel.ApplyHeightBoost(height, EnableHeightBoost, HeightBoostStrength);
            float2 wind = WindFieldKernel.Sample(worldPosition, WorldSeed, WindField, TopologyDomain);
            int sampleCount = math.clamp(OrographicSampleCount, 1, 8);
            float sampleDistance = math.max(8f, OrographicSampleDistance);
            float meanUpwindHeight = 0f;
            float maxUpwindHeight = 0f;
            for (int sampleIndex = 1; sampleIndex <= sampleCount; sampleIndex++)
            {
                float distance = sampleDistance * sampleIndex / sampleCount;
                float2 upwindPosition = worldPosition - wind * distance;
                float upwindHeight = SampleHeightChannel(
                    NoiseConfigs,
                    upwindPosition,
                    NoiseScale,
                    WorldSeed,
                    TopologyDomain,
                    EnableHeightBoost,
                    HeightBoostStrength);
                meanUpwindHeight += upwindHeight;
                maxUpwindHeight = math.max(maxUpwindHeight, upwindHeight);
            }

            meanUpwindHeight /= sampleCount;
            float precipitation = ClimateFieldKernel.ApplyOrographicPrecipitation(
                basePrecipitation,
                height,
                meanUpwindHeight,
                maxUpwindHeight,
                WindwardRainGain,
                LeewardRainLoss);
            float celsius = math.lerp(TemperatureCelsiusRange.x, TemperatureCelsiusRange.y, temperature);

            byte biomeIndex = BiomeResolver.UnmatchedIndex;
            for (int i = 0; i < BiomeRanges.Length; i++)
            {
                CompiledBiomeRange range = BiomeRanges[i];
                if (!range.Matches(temperature, precipitation, height))
                    continue;
                biomeIndex = range.Index;
                break;
            }

            Environment[index] = new float4(temperature, celsius, precipitation, height);
            Wind[index] = wind;
            BiomeIndices[index] = biomeIndex;
        }
    }
}

public readonly struct TerrainPreviewSample
{
    public EnvironmentSample Environment { get; }
    public BiomeData Biome { get; }
    public HydrologyWaterKind WaterKind { get; }
    public float Flow { get; }
    public bool IsRiver => WaterKind == HydrologyWaterKind.River;
    public bool IsLake => WaterKind == HydrologyWaterKind.Lake;
    public float RiverDepth { get; }
    public bool HasWater { get; }
    public float WaterSalt { get; }
    public float WaterDepth { get; }

    public TerrainPreviewSample(
        EnvironmentSample environment,
        BiomeData biome,
        HydrologyWaterKind waterKind,
        float flow,
        float riverDepth,
        bool hasWater,
        float waterSalt,
        float waterDepth)
    {
        Environment = environment;
        Biome = biome;
        WaterKind = waterKind;
        Flow = Mathf.Max(0f, flow);
        RiverDepth = riverDepth;
        HasWater = hasWater;
        WaterSalt = waterSalt;
        WaterDepth = waterDepth;
    }
}

public sealed class TerrainPreviewSampler
{
    private readonly ChunkGenerator_Land _land;
    private readonly ChunkGenerator_River _river;
    private readonly PlanetData _planetData;
    private readonly int _worldSeed;

    public TerrainPreviewSampler(
        ChunkGenerator_Land land,
        ChunkGenerator_River river,
        PlanetData planetData,
        int worldSeed)
    {
        _land = land ?? throw new ArgumentNullException(nameof(land));
        _river = river;
        _planetData = planetData;
        _worldSeed = worldSeed == 0 ? 1 : worldSeed;
        _land.WorldDomain = WrappedWorldGenerationDomain.Create(planetData);
        _land.ValidateConfiguration();
        _river?.ValidateConfiguration();
        _river?.ConfigureQueryContext(_land, _planetData, _worldSeed);
    }

    public bool TrySample(Vector2Int worldPosition, out TerrainPreviewSample preview)
    {
        if (WorldTopologyBounds.TryCreate(_planetData, out WorldTopologyBounds bounds))
            worldPosition = bounds.NormalizeCell(worldPosition);

        EnvironmentSample baseEnvironment = _land.SampleEnvironmentAtWorld(worldPosition, _worldSeed, _planetData);
        if (!_land.TryResolveBiome(baseEnvironment, out BiomeData biome))
        {
            preview = default;
            return false;
        }

        TileData baseTerrain = ChunkGenerator_Land.GetTerrainTileBlock(biome).tileDataTemplate;
        HydrologyCellSample hydrology = default;
        bool hasHydrology = _river != null &&
                            _river.TryEvaluateAppliedHydrologyCell(
                                worldPosition,
                                _worldSeed,
                                baseTerrain,
                                out hydrology);
        bool baseHasWater = baseTerrain is TileData_Water;
        TileData_Water baseWater = baseTerrain as TileData_Water;
        float baseWaterDepth = baseHasWater
            ? TileData_Water.CalculateDepthFromHeight(baseEnvironment.Height)
            : 0f;
        preview = new TerrainPreviewSample(
            baseEnvironment,
            biome,
            hasHydrology ? hydrology.WaterKind : HydrologyWaterKind.None,
            hasHydrology ? hydrology.Flow : 0f,
            hasHydrology && hydrology.WaterKind == HydrologyWaterKind.River ? hydrology.Depth : 0f,
            hasHydrology || baseHasWater,
            hasHydrology ? 0f : baseWater?.salt ?? 0f,
            hasHydrology ? hydrology.Depth : baseWaterDepth);
        return true;
    }
}
