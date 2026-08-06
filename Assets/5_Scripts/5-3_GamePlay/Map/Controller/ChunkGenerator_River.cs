using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public enum HydrologyWaterKind : byte
{
    None = 0,
    River = 1,
    Lake = 2
}

public readonly struct HydrologyCellSample
{
    public HydrologyWaterKind WaterKind { get; }
    public float Flow { get; }
    public float Depth { get; }
    public float SurfaceLevel { get; }
    public bool HasFreshWater => WaterKind != HydrologyWaterKind.None;

    public HydrologyCellSample(
        HydrologyWaterKind waterKind,
        float flow,
        float depth,
        float surfaceLevel = 0f)
    {
        WaterKind = waterKind;
        Flow = Mathf.Max(0f, flow);
        Depth = Mathf.Clamp01(depth);
        SurfaceLevel = Mathf.Clamp01(surfaceLevel);
    }
}

[Serializable]
public sealed class ChunkGenerator_River : ChunkGeneratorBase
{
    private const float SeaSalt = 80f;
    private const float DownhillEpsilon = 0.00001f;

    public override GenerationStage Stage => GenerationStage.Hydrology;

    [Header("水体 Tile")]
    public Tile_Block riverTileBlock;

    [Header("区域水文")]
    public int seed = 12345;
    [Min(64)] public int hydrologyRegionSize = 256;
    [Min(16)] public int runoffCellSize = 64;
    [Min(1)] public int runoffSampleStride = 8;
    [Min(32)] public int maxTraceSteps = 512;
    [Range(0f, 1f)] public float seaLevel = 0.5f;
    [Range(0f, 1f)] public float infiltrationFloor = 0.25f;
    [Min(0.01f)] public float riverStartFlow = 0.12f;
    [Min(0.01f)] public float fullWidthFlow = 2.5f;
    [Range(1, 5)] public int maxRiverWidth = 5;
    [Range(0f, 0.02f)] public float meanderTieTolerance = 0.002f;

    [Header("湖泊")]
    [Min(1)] public int minLakeCells = 18;
    [Min(8)] public int maxLakeCells = 220;
    [Range(0.001f, 0.25f)] public float maxLakeLevelRise = 0.045f;
    [Min(0.01f)] public float lakeMinFlow = 0.35f;

    [Header("缓存")]
    [Range(1, 32)] public int maxCachedRegions = 9;

    [Header("写入与水深")]
    public RiverWriteMode writeMode = RiverWriteMode.ReplaceTop;
    [Range(0f, 1f)] public float riverDepthMin = 0.2f;
    [Range(0f, 1f)] public float riverDepthMax = 0.9f;

    [NonSerialized] private ChunkGenerator_Land _activeLand;
    [NonSerialized] private PlanetData _activePlanetData;
    [NonSerialized] private int _activeWorldSeed = 1;
    [NonSerialized] private WorldAddress _activeWorldAddress;
    [NonSerialized] private IWorldGenerationDomain _activeWorldDomain;

    public enum RiverWriteMode
    {
        ReplaceTop = 0,
        AddLayer = 1,
        ReplaceAll = 2
    }

    public override IEnumerator GenerateAsync(MapGenerationContext context, int workBatchSize)
    {
        if (context?.Map?.Data == null)
            throw new ArgumentNullException(nameof(context), "[ChunkGenerator_River] 缺少地图生成上下文。");

        Map = context.Map;
        _activeLand = context.ClimateService ??
            throw new InvalidOperationException("[ChunkGenerator_River] 管线缺少 ChunkGenerator_Land。");
        _activePlanetData = context.PlanetData ?? SaveDataMgr.Instance?.GetCurrentPlanetData();
        _activeWorldSeed = context.WorldSeed == 0 ? 1 : context.WorldSeed;
        _activeWorldAddress = NormalizeAddress(context.WorldAddress);
        _activeWorldDomain = context.WorldDomain ?? UnboundedWorldGenerationDomain.Instance;
        _activeLand.WorldDomain = _activeWorldDomain;
        ValidateConfiguration();
        _activeLand.ValidateConfiguration();

        int width = Map.Data.Width;
        int height = Map.Data.Height;
        List<HydrologyRegionEntry> entries = CollectRequiredRegions(
            Map.Data.position,
            width,
            height,
            _activeLand,
            _activePlanetData,
            _activeWorldSeed,
            _activeWorldAddress);
        int sourcesPerFrame = Mathf.Max(1, workBatchSize / Mathf.Max(1, runoffCellSize));
        for (int i = 0; i < entries.Count; i++)
        {
            HydrologyRegionEntry entry = entries[i];
            while (!entry.IsComplete)
            {
                if (context.IsCancellationRequested)
                    yield break;
                entry.Advance(sourcesPerFrame);
                if (!entry.IsComplete)
                    yield return null;
            }
        }

        var budget = new ChunkGenerationWorkBudget(Map, Mathf.Max(1, workBatchSize));
        for (int localY = 0; localY < height; localY++)
        {
            for (int localX = 0; localX < width; localX++)
            {
                Vector2Int worldPosition = Map.Data.position + new Vector2Int(localX, localY);
                if (TrySampleHydrologyCell(
                        worldPosition,
                        _activeWorldSeed,
                        _activeLand,
                        _activePlanetData,
                        _activeWorldAddress,
                        out HydrologyCellSample sample) &&
                    sample.HasFreshWater)
                {
                    WriteFreshWaterAt(worldPosition, sample.Depth);
                }

                if (!budget.ShouldYield())
                    continue;
                yield return null;
                budget.BeginNextFrame();
            }
        }
    }

    public bool TrySampleHydrologyCell(Vector2Int worldPosition, out HydrologyCellSample sample)
    {
        return TrySampleHydrologyCell(worldPosition, _activeWorldSeed, out sample);
    }

    public bool TrySampleHydrologyCell(
        Vector2Int worldPosition,
        int worldSeed,
        out HydrologyCellSample sample)
    {
        ChunkGenerator_Land land = _activeLand ?? Map?.LandGenerator;
        if (land == null)
        {
            sample = default;
            return false;
        }

        return TrySampleHydrologyCell(
            worldPosition,
            worldSeed,
            land,
            _activePlanetData ?? SaveDataMgr.Instance?.GetCurrentPlanetData(),
            NormalizeAddress(_activeWorldAddress),
            out sample);
    }

    public bool TryEvaluateRiverCell(Vector2Int worldPosition, out float depth)
    {
        return TryEvaluateRiverCell(worldPosition, _activeWorldSeed, out depth);
    }

    public bool TryEvaluateRiverCell(Vector2Int worldPosition, int worldSeed, out float depth)
    {
        if (TrySampleHydrologyCell(worldPosition, worldSeed, out HydrologyCellSample sample) &&
            sample.WaterKind == HydrologyWaterKind.River)
        {
            depth = sample.Depth;
            return true;
        }

        depth = 0f;
        return false;
    }

    internal void ConfigureQueryContext(
        ChunkGenerator_Land land,
        PlanetData planetData,
        int worldSeed,
        WorldAddress worldAddress = default)
    {
        _activeLand = land ?? throw new ArgumentNullException(nameof(land));
        _activePlanetData = planetData;
        _activeWorldSeed = worldSeed == 0 ? 1 : worldSeed;
        _activeWorldAddress = NormalizeAddress(worldAddress);
        _activeWorldDomain = land.WorldDomain;
    }

    internal bool TryEvaluateAppliedHydrologyCell(
        Vector2Int worldPosition,
        int worldSeed,
        TileData baseTerrain,
        out HydrologyCellSample sample)
    {
        if (IsSeaWater(baseTerrain) ||
            !TrySampleHydrologyCell(
                worldPosition,
                worldSeed,
                _activeLand,
                _activePlanetData,
                NormalizeAddress(_activeWorldAddress),
                out sample))
        {
            sample = default;
            return false;
        }

        return sample.HasFreshWater;
    }

    internal bool TryEvaluateAppliedRiverCell(
        Vector2Int worldPosition,
        int worldSeed,
        TileData baseTerrain,
        out float depth)
    {
        if (TryEvaluateAppliedHydrologyCell(
                worldPosition,
                worldSeed,
                baseTerrain,
                out HydrologyCellSample sample) &&
            sample.WaterKind == HydrologyWaterKind.River)
        {
            depth = sample.Depth;
            return true;
        }

        depth = 0f;
        return false;
    }

    public void ValidateConfiguration()
    {
        if (riverTileBlock?.tileDataTemplate is not TileData_Water)
            throw new InvalidOperationException("riverTileBlock 必须提供 TileData_Water 模板。");
        if (hydrologyRegionSize < 64 || runoffCellSize < 16 || runoffSampleStride < 1 ||
            maxTraceSteps < 32 || maxCachedRegions < 1)
        {
            throw new InvalidOperationException("水文区域、径流单元、追踪或缓存参数非法。");
        }
        if (runoffCellSize % runoffSampleStride != 0)
            throw new InvalidOperationException("径流采样步长必须整除径流单元尺寸。");
        if (!IsFinite01(seaLevel) || !IsFinite01(infiltrationFloor) ||
            !IsFinitePositive(riverStartFlow) || !IsFinitePositive(fullWidthFlow) ||
            fullWidthFlow < riverStartFlow || maxRiverWidth < 1 || maxRiverWidth > 5)
        {
            throw new InvalidOperationException("河流径流、海平面或宽度参数非法。");
        }
        if (minLakeCells < 1 || maxLakeCells < minLakeCells ||
            !IsFinitePositive(maxLakeLevelRise) || !IsFinitePositive(lakeMinFlow))
        {
            throw new InvalidOperationException("湖泊参数非法。");
        }
        if (!IsFinite01(riverDepthMin) || !IsFinite01(riverDepthMax) || riverDepthMin > riverDepthMax)
            throw new InvalidOperationException("河流深度范围非法。");
    }

    public static void ClearHydrologyCache()
    {
        HydrologyRegionCache.Clear();
    }

    public static int CachedRegionCount => HydrologyRegionCache.Count;
    public static int CompletedCachedRegionCount => HydrologyRegionCache.CompletedCount;

    internal static bool IsSeaWater(TileData tile)
    {
        return tile is TileData_Water water && math.abs(water.salt - SeaSalt) <= 0.01f;
    }

    private List<HydrologyRegionEntry> CollectRequiredRegions(
        Vector2Int origin,
        int width,
        int height,
        ChunkGenerator_Land land,
        PlanetData planetData,
        int worldSeed,
        WorldAddress address)
    {
        var output = new List<HydrologyRegionEntry>(4);
        Vector2Int minRegion = GetRegionCoordinate(origin, _activeWorldDomain);
        Vector2Int maxRegion = GetRegionCoordinate(
            origin + new Vector2Int(width - 1, height - 1),
            _activeWorldDomain);
        int minRegionX = minRegion.x;
        int maxRegionX = maxRegion.x;
        int minRegionY = minRegion.y;
        int maxRegionY = maxRegion.y;
        for (int regionY = minRegionY; regionY <= maxRegionY; regionY++)
        {
            for (int regionX = minRegionX; regionX <= maxRegionX; regionX++)
            {
                output.Add(HydrologyRegionCache.GetOrCreate(
                    this,
                    land,
                    planetData,
                    worldSeed,
                    address,
                    new Vector2Int(regionX, regionY)));
            }
        }

        return output;
    }

    private bool TrySampleHydrologyCell(
        Vector2Int worldPosition,
        int worldSeed,
        ChunkGenerator_Land land,
        PlanetData planetData,
        WorldAddress address,
        out HydrologyCellSample sample)
    {
        sample = default;
        if (land == null)
            return false;

        if (!land.WorldDomain.Contains(worldPosition))
        {
            if (!land.WorldDomain.TryResolveOutflow(worldPosition, worldPosition, out worldPosition))
                return false;
        }

        Vector2Int regionCoordinate = GetRegionCoordinate(worldPosition, land.WorldDomain);
        HydrologyRegionEntry entry = HydrologyRegionCache.GetOrCreate(
            this,
            land,
            planetData,
            worldSeed == 0 ? 1 : worldSeed,
            NormalizeAddress(address),
            regionCoordinate);
        entry.CompleteSynchronously();
        sample = entry.Result.Get(worldPosition);
        return sample.HasFreshWater;
    }

    private void WriteFreshWaterAt(Vector2Int worldPosition, float depth)
    {
        int layerCount = Map.Data.GetLayerCount(worldPosition);
        TileData top = Map.Data.GetTopTile(worldPosition);
        if (IsSeaWater(top))
            return;

        TileData riverTile = riverTileBlock.tileDataTemplate.Clone();
        if (riverTile is not TileData_Water waterTile)
            throw new InvalidOperationException("河流 Tile 模板克隆后不是 TileData_Water。");

        riverTile.position = new Vector3Int(worldPosition.x, worldPosition.y, 0);
        Vector2Int localPosition = worldPosition - Map.Data.position;
        riverTile.Initialize_Env(Map.Data.EnvironmentLayers, localPosition.x, localPosition.y);
        waterTile.salt = 0f;
        waterTile.deepValue = math.saturate(depth);

        if (layerCount == 0)
        {
            Map.Data.SetBaseTile(worldPosition, riverTile);
            return;
        }

        switch (writeMode)
        {
            case RiverWriteMode.AddLayer when top is not TileData_Water:
                Map.Data.PushTile(worldPosition, riverTile);
                break;
            case RiverWriteMode.ReplaceAll:
                Map.Data.ReplaceStack(worldPosition, riverTile);
                break;
            default:
                Map.Data.ReplaceTop(worldPosition, riverTile);
                break;
        }
    }

    private static WorldAddress NormalizeAddress(WorldAddress address)
    {
        return address.IsValid
            ? address
            : new WorldAddress("hydrology_preview", WorldAddress.SurfaceDimensionId);
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder != 0 && ((remainder < 0) != (divisor < 0))
            ? quotient - 1
            : quotient;
    }

    private Vector2Int GetRegionCoordinate(
        Vector2Int worldPosition,
        IWorldGenerationDomain domain)
    {
        if (domain is WrappedWorldGenerationDomain wrapped)
        {
            Vector2Int canonical = wrapped.Bounds.NormalizeCell(worldPosition);
            return new Vector2Int(
                FloorDiv(canonical.x - wrapped.Bounds.Min.x, hydrologyRegionSize),
                FloorDiv(canonical.y - wrapped.Bounds.Min.y, hydrologyRegionSize));
        }

        return new Vector2Int(
            FloorDiv(worldPosition.x, hydrologyRegionSize),
            FloorDiv(worldPosition.y, hydrologyRegionSize));
    }

    private static bool IsFinite01(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
    }

    private static bool IsFinitePositive(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    }

    private sealed class HydrologyRegionResult
    {
        private readonly HydrologyCellSample[] _cells;

        public Vector2Int Origin { get; }
        public int Size { get; }

        public HydrologyRegionResult(Vector2Int origin, int size, HydrologyCellSample[] cells)
        {
            Origin = origin;
            Size = size;
            _cells = cells;
        }

        public HydrologyCellSample Get(Vector2Int worldPosition)
        {
            int localX = worldPosition.x - Origin.x;
            int localY = worldPosition.y - Origin.y;
            return (uint)localX < (uint)Size && (uint)localY < (uint)Size
                ? _cells[localY * Size + localX]
                : default;
        }
    }

    private sealed class HydrologyRegionEntry
    {
        private readonly HydrologyRegionBuilder _builder;

        public HydrologyRegionResult Result { get; private set; }
        public bool IsComplete => Result != null;
        public long LastAccess { get; set; }

        public HydrologyRegionEntry(HydrologyRegionBuilder builder)
        {
            _builder = builder;
        }

        public void Advance(int sourceBudget)
        {
            LastAccess = HydrologyRegionCache.NextAccess();
            if (IsComplete)
                return;
            if (_builder.Advance(Mathf.Max(1, sourceBudget)))
                Result = _builder.BuildResult();
        }

        public void CompleteSynchronously()
        {
            LastAccess = HydrologyRegionCache.NextAccess();
            while (!IsComplete)
                Advance(int.MaxValue);
        }
    }

    private readonly struct HydrologyRegionKey : IEquatable<HydrologyRegionKey>
    {
        private readonly WorldAddress _address;
        private readonly int _worldSeed;
        private readonly uint _signature;
        private readonly int _planetNoiseScaleHash;
        private readonly Vector2Int _coordinate;

        public HydrologyRegionKey(
            WorldAddress address,
            int worldSeed,
            uint signature,
            int planetNoiseScaleHash,
            Vector2Int coordinate)
        {
            _address = address;
            _worldSeed = worldSeed;
            _signature = signature;
            _planetNoiseScaleHash = planetNoiseScaleHash;
            _coordinate = coordinate;
        }

        public bool Equals(HydrologyRegionKey other)
        {
            return _address.Equals(other._address) &&
                   _worldSeed == other._worldSeed &&
                   _signature == other._signature &&
                   _planetNoiseScaleHash == other._planetNoiseScaleHash &&
                   _coordinate == other._coordinate;
        }

        public override bool Equals(object obj)
        {
            return obj is HydrologyRegionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = _address.GetHashCode();
                hash = hash * 397 ^ _worldSeed;
                hash = hash * 397 ^ (int)_signature;
                hash = hash * 397 ^ _planetNoiseScaleHash;
                hash = hash * 397 ^ _coordinate.GetHashCode();
                return hash;
            }
        }
    }

    private static class HydrologyRegionCache
    {
        private static readonly Dictionary<HydrologyRegionKey, HydrologyRegionEntry> Entries = new();
        private static long _accessSequence;

        public static int Count => Entries.Count;

        public static int CompletedCount
        {
            get
            {
                int count = 0;
                foreach (HydrologyRegionEntry entry in Entries.Values)
                {
                    if (entry.IsComplete)
                        count++;
                }

                return count;
            }
        }

        public static long NextAccess()
        {
            return ++_accessSequence;
        }

        public static HydrologyRegionEntry GetOrCreate(
            ChunkGenerator_River river,
            ChunkGenerator_Land land,
            PlanetData planetData,
            int worldSeed,
            WorldAddress address,
            Vector2Int coordinate)
        {
            uint signature = CalculateGenerationSignature(river, land);
            float noiseScale = ChunkGenerator_Land.ResolveNoiseScale(planetData);
            var key = new HydrologyRegionKey(
                address,
                worldSeed,
                signature,
                noiseScale.GetHashCode(),
                coordinate);
            if (!Entries.TryGetValue(key, out HydrologyRegionEntry entry))
            {
                entry = new HydrologyRegionEntry(new HydrologyRegionBuilder(
                    river,
                    land,
                    planetData,
                    worldSeed,
                    coordinate));
                Entries.Add(key, entry);
            }

            entry.LastAccess = NextAccess();
            Trim(Mathf.Max(1, river.maxCachedRegions));
            return entry;
        }

        public static void Clear()
        {
            Entries.Clear();
            _accessSequence = 0;
        }

        private static void Trim(int maximumCount)
        {
            while (Entries.Count > maximumCount)
            {
                HydrologyRegionKey oldestKey = default;
                long oldestAccess = long.MaxValue;
                bool found = false;
                foreach (KeyValuePair<HydrologyRegionKey, HydrologyRegionEntry> pair in Entries)
                {
                    if (!pair.Value.IsComplete || pair.Value.LastAccess >= oldestAccess)
                        continue;
                    oldestKey = pair.Key;
                    oldestAccess = pair.Value.LastAccess;
                    found = true;
                }

                if (!found)
                    break;
                Entries.Remove(oldestKey);
            }
        }

        private static uint CalculateGenerationSignature(
            ChunkGenerator_River river,
            ChunkGenerator_Land land)
        {
            uint hash = StructureHashUtility.Begin();
            hash = StructureHashUtility.Add(hash, TerrainGenerationSignature.CurrentVersion);
            hash = StructureHashUtility.Add(hash, river.seed);
            hash = StructureHashUtility.Add(hash, river.hydrologyRegionSize);
            hash = StructureHashUtility.Add(hash, river.runoffCellSize);
            hash = StructureHashUtility.Add(hash, river.runoffSampleStride);
            hash = StructureHashUtility.Add(hash, river.maxTraceSteps);
            hash = StructureHashUtility.Add(hash, river.seaLevel);
            hash = StructureHashUtility.Add(hash, river.infiltrationFloor);
            hash = StructureHashUtility.Add(hash, river.riverStartFlow);
            hash = StructureHashUtility.Add(hash, river.fullWidthFlow);
            hash = StructureHashUtility.Add(hash, river.maxRiverWidth);
            hash = StructureHashUtility.Add(hash, river.meanderTieTolerance);
            hash = StructureHashUtility.Add(hash, river.minLakeCells);
            hash = StructureHashUtility.Add(hash, river.maxLakeCells);
            hash = StructureHashUtility.Add(hash, river.maxLakeLevelRise);
            hash = StructureHashUtility.Add(hash, river.lakeMinFlow);
            hash = StructureHashUtility.Add(hash, river.riverDepthMin);
            hash = StructureHashUtility.Add(hash, river.riverDepthMax);
            hash = StructureHashUtility.Add(hash, land.enableHeightSecondaryBoost);
            hash = StructureHashUtility.Add(hash, land.heightSecondaryBoostStrength);
            hash = StructureHashUtility.Add(hash, land.WindFieldProvider.GenerationSignature);
            hash = StructureHashUtility.Add(hash, land.WorldDomain.GenerationSignature);
            hash = StructureHashUtility.Add(hash, land.WindField.RegionSize);
            hash = StructureHashUtility.Add(hash, land.WindField.SeedSalt);
            hash = StructureHashUtility.Add(hash, land.OrographicSampleDistance);
            hash = StructureHashUtility.Add(hash, land.OrographicSampleCount);
            hash = StructureHashUtility.Add(hash, land.WindwardRainGain);
            hash = StructureHashUtility.Add(hash, land.LeewardRainLoss);
            IReadOnlyList<TerrainNoiseConfig> noise = land.NoiseConfigs;
            for (int i = 0; i < (noise?.Count ?? 0); i++)
            {
                TerrainNoiseConfig config = noise[i];
                hash = StructureHashUtility.Add(hash, (int)config.noiseType);
                hash = StructureHashUtility.Add(hash, config.coordScale);
                hash = StructureHashUtility.Add(hash, config.frequency);
                hash = StructureHashUtility.Add(hash, config.octaves);
                hash = StructureHashUtility.Add(hash, config.lacunarity);
                hash = StructureHashUtility.Add(hash, config.persistence);
                hash = StructureHashUtility.Add(hash, config.coordOffset.x);
                hash = StructureHashUtility.Add(hash, config.coordOffset.y);
            }

            return hash;
        }
    }

    private sealed class HydrologyRegionBuilder
    {
        private static readonly Vector2Int[] Neighbors =
        {
            new(-1, -1), new(0, -1), new(1, -1),
            new(-1, 0),               new(1, 0),
            new(-1, 1),  new(0, 1),  new(1, 1)
        };

        private readonly ChunkGenerator_River _river;
        private readonly ChunkGenerator_Land _land;
        private readonly PlanetData _planetData;
        private readonly int _worldSeed;
        private readonly Vector2Int _regionOrigin;
        private readonly int _minSourceCellX;
        private readonly int _maxSourceCellX;
        private readonly int _minSourceCellY;
        private readonly int _maxSourceCellY;
        private readonly Dictionary<Vector2Int, float> _flow = new();
        private readonly Dictionary<Vector2Int, BasinResult> _basins = new();
        private readonly Dictionary<Vector2Int, float> _basinFlow = new();
        private readonly HashSet<Vector2Int> _processedSourceOrigins = new();

        private int _nextSourceCellX;
        private int _nextSourceCellY;
        private bool _sourceTraversalComplete;

        public HydrologyRegionBuilder(
            ChunkGenerator_River river,
            ChunkGenerator_Land land,
            PlanetData planetData,
            int worldSeed,
            Vector2Int regionCoordinate)
        {
            _river = river;
            _land = land;
            _planetData = planetData;
            _worldSeed = worldSeed == 0 ? 1 : worldSeed;
            _regionOrigin = land.WorldDomain is WrappedWorldGenerationDomain wrapped
                ? wrapped.Bounds.Min + regionCoordinate * river.hydrologyRegionSize
                : regionCoordinate * river.hydrologyRegionSize;
            int padding = river.maxTraceSteps + Mathf.Max(2, river.maxRiverWidth / 2);
            Vector2Int sourceAnchor = land.WorldDomain is WrappedWorldGenerationDomain wrappedSource
                ? wrappedSource.Bounds.Min
                : Vector2Int.zero;
            _minSourceCellX = FloorDiv(_regionOrigin.x - padding - sourceAnchor.x, river.runoffCellSize);
            _maxSourceCellX = FloorDiv(
                _regionOrigin.x + river.hydrologyRegionSize - 1 + padding - sourceAnchor.x,
                river.runoffCellSize);
            _minSourceCellY = FloorDiv(_regionOrigin.y - padding - sourceAnchor.y, river.runoffCellSize);
            _maxSourceCellY = FloorDiv(
                _regionOrigin.y + river.hydrologyRegionSize - 1 + padding - sourceAnchor.y,
                river.runoffCellSize);
            _nextSourceCellX = _minSourceCellX;
            _nextSourceCellY = _minSourceCellY;
        }

        public bool Advance(int sourceBudget)
        {
            if (_sourceTraversalComplete)
                return true;

            int processed = 0;
            while (processed < sourceBudget)
            {
                ProcessRunoffCell(_nextSourceCellX, _nextSourceCellY);
                processed++;

                _nextSourceCellX++;
                if (_nextSourceCellX <= _maxSourceCellX)
                    continue;
                _nextSourceCellX = _minSourceCellX;
                _nextSourceCellY++;
                if (_nextSourceCellY <= _maxSourceCellY)
                    continue;
                _sourceTraversalComplete = true;
                break;
            }

            return _sourceTraversalComplete;
        }

        public HydrologyRegionResult BuildResult()
        {
            if (!_sourceTraversalComplete)
                throw new InvalidOperationException("水文区域尚未完成，不能提交结果。");

            int size = _river.hydrologyRegionSize;
            var cells = new HydrologyCellSample[size * size];
            int maximumRadius = Mathf.Max(0, (_river.maxRiverWidth - 1) / 2);
            foreach (KeyValuePair<Vector2Int, float> pair in _flow)
            {
                if (pair.Value < _river.riverStartFlow ||
                    !ContainsExpanded(pair.Key, maximumRadius))
                {
                    continue;
                }

                float widthT = Mathf.InverseLerp(
                    _river.riverStartFlow,
                    Mathf.Max(_river.riverStartFlow + 0.001f, _river.fullWidthFlow),
                    pair.Value);
                int width = 1 + Mathf.RoundToInt(widthT * (_river.maxRiverWidth - 1));
                int radius = Mathf.Clamp((width - 1 + 1) / 2, 0, maximumRadius);
                float centerDepth = Mathf.Lerp(
                    _river.riverDepthMin,
                    _river.riverDepthMax,
                    Mathf.Sqrt(widthT));
                for (int offsetY = -radius; offsetY <= radius; offsetY++)
                {
                    for (int offsetX = -radius; offsetX <= radius; offsetX++)
                    {
                        if (offsetX * offsetX + offsetY * offsetY > radius * radius + 1)
                            continue;
                        Vector2Int waterPosition = pair.Key + new Vector2Int(offsetX, offsetY);
                        if (!ContainsCore(waterPosition) ||
                            _land.SampleHeightAtWorld(waterPosition, _worldSeed, _planetData) <= _river.seaLevel)
                        {
                            continue;
                        }

                        float edgeT = radius == 0
                            ? 1f
                            : 1f - Mathf.Clamp01(new Vector2(offsetX, offsetY).magnitude / (radius + 0.5f));
                        float depth = Mathf.Lerp(_river.riverDepthMin, centerDepth, edgeT);
                        SetCell(
                            cells,
                            waterPosition,
                            new HydrologyCellSample(HydrologyWaterKind.River, pair.Value, depth));
                    }
                }
            }

            foreach (KeyValuePair<Vector2Int, float> pair in _basinFlow)
            {
                if (pair.Value < _river.lakeMinFlow ||
                    !_basins.TryGetValue(pair.Key, out BasinResult basin) ||
                    basin.Cells.Count < _river.minLakeCells ||
                    basin.Cells.Count > _river.maxLakeCells)
                {
                    continue;
                }

                for (int i = 0; i < basin.Cells.Count; i++)
                {
                    Vector2Int lakePosition = basin.Cells[i];
                    if (!ContainsCore(lakePosition))
                        continue;
                    float height = _land.SampleHeightAtWorld(lakePosition, _worldSeed, _planetData);
                    if (height <= _river.seaLevel)
                        continue;
                    float depthT = Mathf.Clamp01(
                        (basin.WaterLevel - height) / Mathf.Max(0.0001f, _river.maxLakeLevelRise));
                    float depth = Mathf.Lerp(
                        _river.riverDepthMin,
                        _river.riverDepthMax,
                        Mathf.Max(0.15f, depthT));
                    SetCell(
                        cells,
                        lakePosition,
                        new HydrologyCellSample(
                            HydrologyWaterKind.Lake,
                            pair.Value,
                            depth,
                            basin.WaterLevel));
                }
            }

            return new HydrologyRegionResult(_regionOrigin, size, cells);
        }

        private void ProcessRunoffCell(int sourceCellX, int sourceCellY)
        {
            int cellSize = _river.runoffCellSize;
            int stride = _river.runoffSampleStride;
            Vector2Int sourceAnchor = _land.WorldDomain is WrappedWorldGenerationDomain wrapped
                ? wrapped.Bounds.Min
                : Vector2Int.zero;
            Vector2Int cellOrigin = sourceAnchor + new Vector2Int(sourceCellX * cellSize, sourceCellY * cellSize);
            if (!TryResolvePosition(cellOrigin, out Vector2Int canonicalCellOrigin) ||
                !_processedSourceOrigins.Add(canonicalCellOrigin))
            {
                return;
            }

            float runoffSum = 0f;
            int sampleCount = 0;
            Vector2Int source = default;
            float sourceScore = float.MinValue;
            for (int localY = stride / 2; localY < cellSize; localY += stride)
            {
                for (int localX = stride / 2; localX < cellSize; localX += stride)
                {
                    Vector2Int worldPosition = cellOrigin + new Vector2Int(localX, localY);
                    if (!TryResolvePosition(worldPosition, out worldPosition))
                        continue;
                    ClimateSample climate = _land.SampleClimateAtWorld(worldPosition, _worldSeed, _planetData);
                    EnvironmentSample environment = climate.Environment;
                    sampleCount++;
                    if (environment.Height <= _river.seaLevel)
                        continue;

                    float runoff = Mathf.Clamp01(
                        (environment.Precipitation - _river.infiltrationFloor) /
                        Mathf.Max(0.0001f, 1f - _river.infiltrationFloor));
                    runoffSum += runoff;
                    float score = environment.Height + runoff * 0.05f +
                                  Hash01(worldPosition, _river.seed ^ _worldSeed) * 0.001f;
                    if (score <= sourceScore)
                        continue;
                    sourceScore = score;
                    source = worldPosition;
                }
            }

            if (sampleCount == 0 || sourceScore == float.MinValue)
                return;
            float contribution = runoffSum / sampleCount;
            if (contribution <= 0.0001f)
                return;
            TraceRunoff(source, contribution);
        }

        private void TraceRunoff(Vector2Int source, float contribution)
        {
            Vector2Int current = source;
            var visited = new HashSet<Vector2Int>();
            for (int step = 0; step < _river.maxTraceSteps; step++)
            {
                if (!_land.WorldDomain.Contains(current) || !visited.Add(current))
                    break;
                float currentHeight = _land.SampleHeightAtWorld(current, _worldSeed, _planetData);
                if (currentHeight <= _river.seaLevel)
                    break;
                AddFlow(current, contribution);

                if (TryChooseDownhill(current, currentHeight, out Vector2Int next))
                {
                    AddDiagonalBridge(current, next, contribution);
                    current = next;
                    continue;
                }

                BasinResult basin = ResolveBasin(current, currentHeight);
                _basinFlow.TryGetValue(current, out float existingBasinFlow);
                _basinFlow[current] = existingBasinFlow + contribution;
                if (!basin.HasOutlet)
                    break;
                current = basin.Outlet;
            }
        }

        private bool TryChooseDownhill(
            Vector2Int current,
            float currentHeight,
            out Vector2Int next)
        {
            next = default;
            float bestScore = float.MaxValue;
            bool found = false;
            for (int i = 0; i < Neighbors.Length; i++)
            {
                if (!TryResolvePosition(current + Neighbors[i], out Vector2Int candidate))
                    continue;

                float height = _land.SampleHeightAtWorld(candidate, _worldSeed, _planetData);
                if (height >= currentHeight - DownhillEpsilon)
                    continue;
                float score = height + Hash01(
                    candidate,
                    _river.seed ^ _worldSeed ^ 0x51ED270B) * _river.meanderTieTolerance;
                if (score >= bestScore)
                    continue;
                bestScore = score;
                next = candidate;
                found = true;
            }

            return found;
        }

        private BasinResult ResolveBasin(Vector2Int sink, float sinkHeight)
        {
            if (_basins.TryGetValue(sink, out BasinResult cached))
                return cached;

            var basin = new HashSet<Vector2Int> { sink };
            var queued = new HashSet<Vector2Int>();
            var frontier = new List<FrontierCell>();
            AddFrontier(sink, basin, queued, frontier);
            float waterLevel = sinkHeight;
            Vector2Int outlet = default;
            bool hasOutlet = false;

            while (frontier.Count > 0 && basin.Count < _river.maxLakeCells)
            {
                int minimumIndex = 0;
                for (int i = 1; i < frontier.Count; i++)
                {
                    if (frontier[i].CompareTo(frontier[minimumIndex]) < 0)
                        minimumIndex = i;
                }

                FrontierCell boundary = frontier[minimumIndex];
                frontier.RemoveAt(minimumIndex);
                if (boundary.Height - sinkHeight > _river.maxLakeLevelRise)
                    break;
                waterLevel = Mathf.Max(waterLevel, boundary.Height);
                basin.Add(boundary.Position);

                for (int i = 0; i < Neighbors.Length; i++)
                {
                    if (!TryResolvePosition(boundary.Position + Neighbors[i], out Vector2Int candidate))
                        continue;
                    if (basin.Contains(candidate))
                        continue;
                    float candidateHeight = _land.SampleHeightAtWorld(candidate, _worldSeed, _planetData);
                    if (candidateHeight < waterLevel - DownhillEpsilon)
                    {
                        outlet = candidate;
                        hasOutlet = true;
                        break;
                    }
                }

                if (hasOutlet)
                    break;
                AddFrontier(boundary.Position, basin, queued, frontier);
            }

            var result = new BasinResult(new List<Vector2Int>(basin), waterLevel, hasOutlet, outlet);
            _basins[sink] = result;
            return result;
        }

        private void AddFrontier(
            Vector2Int center,
            HashSet<Vector2Int> basin,
            HashSet<Vector2Int> queued,
            List<FrontierCell> frontier)
        {
            for (int i = 0; i < Neighbors.Length; i++)
            {
                if (!TryResolvePosition(center + Neighbors[i], out Vector2Int candidate) ||
                    basin.Contains(candidate) || !queued.Add(candidate))
                    continue;
                frontier.Add(new FrontierCell(
                    candidate,
                    _land.SampleHeightAtWorld(candidate, _worldSeed, _planetData)));
            }
        }

        private void AddDiagonalBridge(Vector2Int current, Vector2Int next, float contribution)
        {
            Vector2Int delta = _land.WorldDomain is WrappedWorldGenerationDomain wrapped
                ? wrapped.Bounds.ShortestDelta(current, next)
                : next - current;
            int deltaX = delta.x;
            int deltaY = delta.y;
            if (deltaX == 0 || deltaY == 0)
                return;
            Vector2Int horizontal = current + new Vector2Int(deltaX, 0);
            Vector2Int vertical = current + new Vector2Int(0, deltaY);
            TryResolvePosition(horizontal, out horizontal);
            TryResolvePosition(vertical, out vertical);
            float horizontalHeight = _land.SampleHeightAtWorld(horizontal, _worldSeed, _planetData);
            float verticalHeight = _land.SampleHeightAtWorld(vertical, _worldSeed, _planetData);
            AddFlow(horizontalHeight <= verticalHeight ? horizontal : vertical, contribution);
        }

        private void AddFlow(Vector2Int position, float contribution)
        {
            if (!TryResolvePosition(position, out position))
                return;
            _flow.TryGetValue(position, out float existing);
            _flow[position] = existing + contribution;
        }

        private void SetCell(
            HydrologyCellSample[] cells,
            Vector2Int worldPosition,
            HydrologyCellSample sample)
        {
            worldPosition = ToNearestRegionImage(worldPosition);
            int localX = worldPosition.x - _regionOrigin.x;
            int localY = worldPosition.y - _regionOrigin.y;
            if ((uint)localX >= (uint)_river.hydrologyRegionSize ||
                (uint)localY >= (uint)_river.hydrologyRegionSize)
            {
                throw new InvalidOperationException(
                    $"Hydrology cell {worldPosition} is outside region {_regionOrigin} " +
                    $"size {_river.hydrologyRegionSize} after topology projection.");
            }
            int index = localY * _river.hydrologyRegionSize + localX;
            HydrologyCellSample existing = cells[index];
            if (existing.WaterKind == HydrologyWaterKind.Lake &&
                sample.WaterKind != HydrologyWaterKind.Lake)
            {
                return;
            }
            if (existing.WaterKind == sample.WaterKind && existing.Depth > sample.Depth)
                return;
            cells[index] = sample;
        }

        private bool ContainsCore(Vector2Int position)
        {
            position = ToNearestRegionImage(position);
            return (uint)(position.x - _regionOrigin.x) < (uint)_river.hydrologyRegionSize &&
                   (uint)(position.y - _regionOrigin.y) < (uint)_river.hydrologyRegionSize &&
                   TryResolvePosition(position, out _);
        }

        private bool ContainsExpanded(Vector2Int position, int margin)
        {
            position = ToNearestRegionImage(position);
            return position.x >= _regionOrigin.x - margin &&
                   position.y >= _regionOrigin.y - margin &&
                   position.x < _regionOrigin.x + _river.hydrologyRegionSize + margin &&
                   position.y < _regionOrigin.y + _river.hydrologyRegionSize + margin;
        }

        private bool TryResolvePosition(Vector2Int position, out Vector2Int resolved)
        {
            if (_land.WorldDomain.Contains(position))
            {
                resolved = position;
                return true;
            }

            return _land.WorldDomain.TryResolveOutflow(position, position, out resolved);
        }

        private Vector2Int ToNearestRegionImage(Vector2Int position)
        {
            if (_land.WorldDomain is not WrappedWorldGenerationDomain wrapped)
                return position;

            Vector2Int center = _regionOrigin +
                                new Vector2Int(_river.hydrologyRegionSize / 2, _river.hydrologyRegionSize / 2);
            return center + wrapped.Bounds.ShortestDelta(center, position);
        }

        private static float Hash01(Vector2Int position, int salt)
        {
            unchecked
            {
                uint value = (uint)salt;
                value ^= (uint)position.x * 0x9E3779B9u;
                value ^= (uint)position.y * 0x85EBCA6Bu;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 16777216f;
            }
        }

        private readonly struct BasinResult
        {
            public List<Vector2Int> Cells { get; }
            public float WaterLevel { get; }
            public bool HasOutlet { get; }
            public Vector2Int Outlet { get; }

            public BasinResult(
                List<Vector2Int> cells,
                float waterLevel,
                bool hasOutlet,
                Vector2Int outlet)
            {
                Cells = cells;
                WaterLevel = waterLevel;
                HasOutlet = hasOutlet;
                Outlet = outlet;
            }
        }

        private readonly struct FrontierCell : IComparable<FrontierCell>
        {
            public Vector2Int Position { get; }
            public float Height { get; }

            public FrontierCell(Vector2Int position, float height)
            {
                Position = position;
                Height = height;
            }

            public int CompareTo(FrontierCell other)
            {
                int heightCompare = Height.CompareTo(other.Height);
                if (heightCompare != 0)
                    return heightCompare;
                int xCompare = Position.x.CompareTo(other.Position.x);
                return xCompare != 0 ? xCompare : Position.y.CompareTo(other.Position.y);
            }
        }
    }
}
