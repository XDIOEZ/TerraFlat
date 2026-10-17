using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using Unity.Profiling;

/// <summary>
/// 局部冷热源的空间索引。只为冷热源覆盖的区块分配距离权重缓存，不遍历世界或持有地形租约。
/// 热源提供目标温度，距离权重由 ThermalInfluenceRuntime 共用；区块仅缓存几何权重，不缓存天气温度。
/// 最大半径 64 格；目标温度变化无需重建几何缓存，只有移动、半径变化或移除才使覆盖区块失效。
/// 这是可重建的环境影响层，设备开关、燃料和持久化由来源模块负责。
/// </summary>
internal sealed class LocalTemperatureField
{
    public const float MaximumSourceRadius = 64f;
    private static readonly ProfilerMarker RebuildMarker = new("FlatWorld.Temperature.RebuildLocalChunk");

    #region 空间数据

    internal sealed class Source
    {
        public Vector2 Center; // 已归一化的中心位置
        public float Radius; // 影响半径
        public float TargetTemperature; // 热源希望达到的环境温度
        public bool Cooling; // 制冷设备才会降低当地环境温度
        public float Strength; // 供电等来源强度改变无需重算空间权重
        public readonly List<Vector2Int> Buckets = new(); // 覆盖的空间分区
    }

    internal readonly struct Influence
    {
        public readonly Source Source;
        public readonly float[] Weights;
        public Influence(Source source, float[] weights) { Source = source; Weights = weights; }
    }

    private sealed class Bucket
    {
        public readonly List<Source> Sources = new(); // 只保存能影响当前分区的来源
        public Influence[] Influences = Array.Empty<Influence>(); // 每种来源的逐格距离权重
        public bool Dirty = true; // 来源变化后延迟重建
    }

    private readonly Dictionary<object, Source> sources = new();
    private readonly Dictionary<Vector2Int, Bucket> buckets = new();
    private readonly int width;
    private readonly int height;
    private readonly WorldTopologyBounds bounds;

    public LocalTemperatureField(int width, int height, WorldTopologyBounds bounds)
    {
        this.width = width;
        this.height = height;
        this.bounds = bounds;
    }

    #endregion

    #region 来源注册

    /// <summary>添加或更新一个来源；相同快照不重复使缓存失效。</summary>
    public void SetSource(object owner, Vector2 center, float radius, float targetTemperature,
        bool cooling = false, float strength = 1f)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));
        if (!IsFinite(center.x) || !IsFinite(center.y) || !IsFinite(targetTemperature) ||
            !IsFinite(strength) || strength < 0f || strength > 1f ||
            !IsFinite(radius) || radius <= 0f || radius > MaximumSourceRadius)
            throw new ArgumentOutOfRangeException(nameof(radius), "冷热源参数必须有限，半径范围为 (0, 64] 格。");

        center = Normalize(center);
        if (sources.TryGetValue(owner, out Source source))
        {
            // 热源变温不影响位置权重，炉温实时变化也不用重新遍历区块。
            if (source.Center == center && source.Radius == radius)
            {
                source.TargetTemperature = targetTemperature;
                source.Cooling = cooling;
                source.Strength = strength;
                return;
            }
            RemoveFromBuckets(source);
        }
        else
        {
            source = new Source();
            sources.Add(owner, source);
        }

        source.Center = center;
        source.Radius = radius;
        source.TargetTemperature = targetTemperature;
        source.Cooling = cooling;
        source.Strength = strength;
        int minX = Mathf.FloorToInt((center.x - radius) / width) * width;
        int minY = Mathf.FloorToInt((center.y - radius) / height) * height;
        int maxX = Mathf.FloorToInt((center.x + radius) / width) * width;
        int maxY = Mathf.FloorToInt((center.y + radius) / height) * height;
        for (int y = minY; y <= maxY; y += height)
        for (int x = minX; x <= maxX; x += width)
        {
            Vector2Int key = new(x, y);
            if (bounds.IsWrapped)
                key = bounds.NormalizeChunkOrigin(key);
            // 小型环形世界可能多次命中同一区块，一个来源只能贡献一次。
            if (source.Buckets.Contains(key))
                continue;
            source.Buckets.Add(key);
            if (!buckets.TryGetValue(key, out Bucket bucket))
            {
                bucket = new Bucket();
                buckets.Add(key, bucket);
            }
            bucket.Sources.Add(source);
            bucket.Dirty = true;
        }
    }

    /// <summary>来源卸载时移除索引，最后一个来源离开后释放分区缓存。</summary>
    public void RemoveSource(object owner)
    {
        if (owner == null || !sources.TryGetValue(owner, out Source source))
            return;
        RemoveFromBuckets(source);
        sources.Remove(owner);
    }

    private void RemoveFromBuckets(Source source)
    {
        foreach (Vector2Int key in source.Buckets)
        {
            Bucket bucket = buckets[key];
            bucket.Sources.Remove(source);
            bucket.Dirty = true;
            if (bucket.Sources.Count == 0)
                buckets.Remove(key);
        }
        source.Buckets.Clear();
    }

    #endregion

    #region 查询

    /// <summary>读取缓存好的来源权重，逐格采样时用实时基础温度合成。</summary>
    public ReadOnlySpan<Influence> Sample(Vector2Int cell)
    {
        Vector2Int origin = new(
            Mathf.FloorToInt((float)cell.x / width) * width,
            Mathf.FloorToInt((float)cell.y / height) * height);
        return GetChunkInfluences(origin);
    }

    public ReadOnlySpan<Influence> GetChunkInfluences(Vector2Int origin)
    {
        if (!buckets.TryGetValue(origin, out Bucket bucket))
            return ReadOnlySpan<Influence>.Empty;
        if (bucket.Dirty)
            Rebuild(origin, bucket);
        return bucket.Influences;
    }

    // 单格只计算来源到本格的距离，不为一个掉落物重建整块权重。
    public void AccumulateAtPoint(Vector2Int cell, float ambient,
        ref float strongestHeating, ref float strongestCooling)
    {
        Vector2Int origin = new(
            Mathf.FloorToInt((float)cell.x / width) * width,
            Mathf.FloorToInt((float)cell.y / height) * height);
        if (bounds.IsWrapped)
            origin = bounds.NormalizeChunkOrigin(origin);
        if (!buckets.TryGetValue(origin, out Bucket bucket))
            return;
        if (!bucket.Dirty)
        {
            int index = (cell.y - origin.y) * width + cell.x - origin.x;
            Accumulate(bucket.Influences, index, ambient, ref strongestHeating, ref strongestCooling);
            return;
        }
        Vector2 center = new(cell.x + 0.5f, cell.y + 0.5f);
        foreach (Source source in bucket.Sources)
        {
            Vector2 delta = bounds.IsWrapped
                ? bounds.ShortestDelta(source.Center, center) : center - source.Center;
            float weight = ThermalInfluenceRuntime.DistanceWeight(delta.sqrMagnitude,
                source.Radius * source.Radius) * source.Strength;
            ThermalInfluenceRuntime.Accumulate(ambient, source.TargetTemperature, weight,
                ref strongestHeating, ref strongestCooling, source.Cooling);
        }
    }

    public static void Accumulate(ReadOnlySpan<Influence> influences, int index, float ambient,
        ref float strongestHeating, ref float strongestCooling)
    {
        foreach (Influence influence in influences)
            ThermalInfluenceRuntime.Accumulate(ambient, influence.Source.TargetTemperature,
                influence.Weights[index] * influence.Source.Strength,
                ref strongestHeating, ref strongestCooling, influence.Source.Cooling);
    }

    private void Rebuild(Vector2Int origin, Bucket bucket)
    {
        using (RebuildMarker.Auto())
        {
            var influences = new Influence[bucket.Sources.Count];
            for (int sourceIndex = 0; sourceIndex < bucket.Sources.Count; sourceIndex++)
            {
                Source source = bucket.Sources[sourceIndex];
                float[] weights = new float[width * height];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Vector2 center = new(origin.x + x + 0.5f, origin.y + y + 0.5f);
                    Vector2 delta = bounds.IsWrapped
                        ? bounds.ShortestDelta(source.Center, center)
                        : center - source.Center;
                    weights[y * width + x] = ThermalInfluenceRuntime.DistanceWeight(
                        delta.sqrMagnitude, source.Radius * source.Radius);
                }
                influences[sourceIndex] = new Influence(source, weights);
            }
            bucket.Influences = influences;
            bucket.Dirty = false;
        }
    }

    private Vector2 Normalize(Vector2 position) => bounds.IsWrapped ? bounds.NormalizePosition(position) : position;
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    #endregion
}

/// <summary>把液面按液体目标温度分组，缓存各组最大距离权重，避免岩浆湖逐格重复叠热。</summary>
internal sealed class LiquidTemperatureField
{
    #region 分区缓存

    private static readonly ProfilerMarker RebuildMarker = new("FlatWorld.Temperature.RebuildRadiantLiquidChunk");
    private static readonly ProfilerMarker PointMarker = new("FlatWorld.Temperature.SampleRadiantLiquidPoint");

    private readonly struct Dependency
    {
        public readonly Vector2Int Origin;
        public readonly ChunkTerrainData Terrain;
        public readonly long SurfaceLiquidRevision;

        public Dependency(Vector2Int origin, ChunkTerrainData terrain)
        {
            Origin = origin;
            Terrain = terrain;
            SurfaceLiquidRevision = terrain?.SurfaceLiquidRevision ?? -1;
        }
    }

    private sealed class Bucket
    {
        public Influence[] Influences = Array.Empty<Influence>();
        public readonly List<Dependency> Dependencies = new(9);
    }

    internal readonly struct Influence
    {
        public readonly float TargetTemperature;
        public readonly float[] Weights;
        public Influence(float temperature, float[] weights) { TargetTemperature = temperature; Weights = weights; }
    }

    private readonly Dictionary<Vector2Int, Bucket> buckets = new();
    private readonly Dictionary<Vector2Int, SourceChunk> sourceChunks = new();
    private readonly HashSet<Vector2Int> pointOrigins = new();
    private readonly ChunkMgr chunkManager;
    private readonly string dimensionId;
    private readonly int width;
    private readonly int height;
    private readonly WorldTopologyBounds bounds;
    private GameRes cachedResources;
    private int cachedResourceReloadVersion = -1;

    public LiquidTemperatureField(ChunkMgr chunkManager, string dimensionId,
        int width, int height, WorldTopologyBounds bounds)
    {
        this.chunkManager = chunkManager;
        this.dimensionId = dimensionId;
        this.width = width;
        this.height = height;
        this.bounds = bounds;
    }

    #endregion

    #region 稀疏液体热源

    internal sealed class SourceChunk
    {
        private readonly struct Source
        {
            public readonly Vector2 LocalCenter;
            public readonly float RadiusSquared;
            public readonly float Temperature;
            public Source(Vector2 center, float radius, float temperature)
            {
                LocalCenter = center;
                RadiusSquared = radius * radius;
                Temperature = temperature;
            }
        }

        private readonly List<Source> sources = new();
        private ChunkTerrainData terrain;
        private long revision = -1;

        public void Refresh(ChunkTerrainData current, IReadOnlyDictionary<string, LiquidDefinition> definitions)
        {
            if (current?.IsDisposed == true)
                current = null;
            long currentRevision = current?.SurfaceLiquidRevision ?? -1;
            if (ReferenceEquals(current, terrain) && currentRevision == revision)
                return;
            terrain = current;
            revision = currentRevision;
            sources.Clear();
            if (current == null || current.IsDisposed)
                return;
            for (int y = 0; y < current.Height; y++)
            for (int x = 0; x < current.Width; x++)
            {
                if (WorldLiquidSystem.GetSurfaceDepth(current, x, y) <= 0f)
                    continue;
                string id = current.GetLiquidId(x, y);
                if (string.IsNullOrWhiteSpace(id) || !definitions.TryGetValue(id.Trim(), out LiquidDefinition liquid))
                    continue;
                WorldLiquidSettings settings = liquid.WorldWater;
                if (settings?.RadiantHeatRadius > 0f)
                    sources.Add(new Source(new Vector2(x + 0.5f, y + 0.5f),
                        settings.RadiantHeatRadius, settings.RadiantTargetTemperature));
            }
        }

        public void AccumulateAtPoint(Vector2Int origin, Vector2 center, WorldTopologyBounds bounds,
            float ambient, ref float strongestHeating, ref float strongestCooling)
        {
            foreach (Source source in sources)
            {
                Vector2 sourceCenter = (Vector2)origin + source.LocalCenter;
                Vector2 delta = bounds.IsWrapped ? bounds.ShortestDelta(sourceCenter, center) : center - sourceCenter;
                float weight = ThermalInfluenceRuntime.DistanceWeight(delta.sqrMagnitude, source.RadiusSquared);
                ThermalInfluenceRuntime.Accumulate(ambient, source.Temperature, weight,
                    ref strongestHeating, ref strongestCooling);
            }
        }

        public void AccumulateChunk(Vector2Int sourceOrigin, Vector2Int targetOrigin, int width, int height,
            WorldTopologyBounds bounds, Dictionary<float, float[]> weightsByTemperature)
        {
            foreach (Source source in sources)
            {
                if (!weightsByTemperature.TryGetValue(source.Temperature, out float[] weights))
                {
                    weights = new float[width * height];
                    weightsByTemperature.Add(source.Temperature, weights);
                }
                Vector2 sourceCenter = (Vector2)sourceOrigin + source.LocalCenter;
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Vector2 center = new(targetOrigin.x + x + 0.5f, targetOrigin.y + y + 0.5f);
                    Vector2 delta = bounds.IsWrapped ? bounds.ShortestDelta(sourceCenter, center) : center - sourceCenter;
                    float weight = ThermalInfluenceRuntime.DistanceWeight(delta.sqrMagnitude, source.RadiusSquared);
                    int index = y * width + x;
                    if (weight > weights[index])
                        weights[index] = weight;
                }
            }
        }
    }

    #endregion

    #region 查询与重建

    public ReadOnlySpan<Influence> Sample(Vector2Int cell)
    {
        Vector2Int origin = ResolveChunkOrigin(cell);
        return GetChunkInfluences(origin);
    }

    // 一次同步批次只验证一次邻区液体版本，单格查询继续实时验证。
    public ReadOnlySpan<Influence> GetChunkInfluences(Vector2Int origin)
    {
        if (!PrepareResources(out _))
            return ReadOnlySpan<Influence>.Empty;

        if (bounds.IsWrapped)
            origin = bounds.NormalizeChunkOrigin(origin);
        if (!buckets.TryGetValue(origin, out Bucket bucket))
        {
            bucket = new Bucket();
            buckets.Add(origin, bucket);
            Rebuild(origin, bucket);
        }
        else if (!DependenciesAreCurrent(bucket))
        {
            Rebuild(origin, bucket);
        }

        return bucket.Influences;
    }

    public void AccumulateAtPoint(Vector2Int cell, float ambient,
        ref float strongestHeating, ref float strongestCooling)
    {
        using (PointMarker.Auto())
        {
            if (!PrepareResources(out GameRes resources))
                return;
            Vector2Int targetOrigin = ResolveChunkOrigin(cell);
            if (buckets.TryGetValue(targetOrigin, out Bucket bucket))
            {
                if (DependenciesAreCurrent(bucket))
                {
                    // 已有完整有效权重时直接复用，冷查询才读取稀疏源。
                    int index = (cell.y - targetOrigin.y) * width + cell.x - targetOrigin.x;
                    Accumulate(bucket.Influences, index, ambient, ref strongestHeating, ref strongestCooling);
                    return;
                }
                buckets.Remove(targetOrigin);
            }
            int radius = Mathf.CeilToInt(WorldLiquidSettings.MaximumRadiantHeatRadius);
            int minX = Mathf.FloorToInt((float)(cell.x - radius) / width) * width;
            int minY = Mathf.FloorToInt((float)(cell.y - radius) / height) * height;
            int maxX = Mathf.FloorToInt((float)(cell.x + radius) / width) * width;
            int maxY = Mathf.FloorToInt((float)(cell.y + radius) / height) * height;
            Vector2 center = new(cell.x + 0.5f, cell.y + 0.5f);
            pointOrigins.Clear();
            for (int y = minY; y <= maxY; y += height)
            for (int x = minX; x <= maxX; x += width)
            {
                Vector2Int origin = new(x, y);
                if (bounds.IsWrapped)
                    origin = bounds.NormalizeChunkOrigin(origin);
                if (!pointOrigins.Add(origin))
                    continue;
                GetSources(origin, ResolveTerrain(origin), resources).AccumulateAtPoint(origin, center, bounds,
                    ambient, ref strongestHeating, ref strongestCooling);
            }
        }
    }

    private bool PrepareResources(out GameRes resources)
    {
        resources = GameRes.ExistingInstance;
        if (chunkManager == null || resources == null)
            return false;
        // 资源重载同时废弃源列表和目标块权重，下一次读取直接使用新定义。
        if (!ReferenceEquals(cachedResources, resources) ||
            cachedResourceReloadVersion != resources.ResourceReloadVersion)
        {
            buckets.Clear();
            sourceChunks.Clear();
            cachedResources = resources;
            cachedResourceReloadVersion = resources.ResourceReloadVersion;
        }
        return true;
    }

    private SourceChunk GetSources(Vector2Int origin, ChunkTerrainData terrain, GameRes resources)
    {
        if (!sourceChunks.TryGetValue(origin, out SourceChunk source))
        {
            source = new SourceChunk();
            sourceChunks.Add(origin, source);
        }
        source.Refresh(terrain, resources.LiquidDefinitions);
        return source;
    }

    public static void Accumulate(ReadOnlySpan<Influence> influences, int index, float ambient,
        ref float strongestHeating, ref float strongestCooling)
    {
        foreach (Influence influence in influences)
            ThermalInfluenceRuntime.Accumulate(ambient, influence.TargetTemperature,
                influence.Weights[index], ref strongestHeating, ref strongestCooling);
    }

    private bool DependenciesAreCurrent(Bucket bucket)
    {
        foreach (Dependency dependency in bucket.Dependencies)
        {
            ChunkTerrainData current = ResolveTerrain(dependency.Origin);
            if (!ReferenceEquals(current, dependency.Terrain) ||
                current != null && current.SurfaceLiquidRevision != dependency.SurfaceLiquidRevision)
                return false;
        }
        return true;
    }

    private void Rebuild(Vector2Int targetOrigin, Bucket bucket)
    {
        using (RebuildMarker.Auto())
        {
            var weightsByTemperature = new Dictionary<float, float[]>();
            bucket.Dependencies.Clear();

            int radius = Mathf.CeilToInt(WorldLiquidSettings.MaximumRadiantHeatRadius);
            int minOriginX = Mathf.FloorToInt((float)(targetOrigin.x - radius) / width) * width;
            int minOriginY = Mathf.FloorToInt((float)(targetOrigin.y - radius) / height) * height;
            int maxOriginX = Mathf.FloorToInt((float)(targetOrigin.x + width - 1 + radius) / width) * width;
            int maxOriginY = Mathf.FloorToInt((float)(targetOrigin.y + height - 1 + radius) / height) * height;

            for (int y = minOriginY; y <= maxOriginY; y += height)
            for (int x = minOriginX; x <= maxOriginX; x += width)
            {
                Vector2Int sourceOrigin = new(x, y);
                if (bounds.IsWrapped)
                    sourceOrigin = bounds.NormalizeChunkOrigin(sourceOrigin);
                if (ContainsDependency(bucket, sourceOrigin))
                    continue;

                ChunkTerrainData terrain = ResolveTerrain(sourceOrigin);
                bucket.Dependencies.Add(new Dependency(sourceOrigin, terrain));
                if (terrain != null)
                    GetSources(sourceOrigin, terrain, cachedResources).AccumulateChunk(sourceOrigin, targetOrigin,
                        width, height, bounds, weightsByTemperature);
            }
            bucket.Influences = new Influence[weightsByTemperature.Count];
            int influenceIndex = 0;
            foreach (KeyValuePair<float, float[]> pair in weightsByTemperature)
                bucket.Influences[influenceIndex++] = new Influence(pair.Key, pair.Value);
        }
    }

    private ChunkTerrainData ResolveTerrain(Vector2Int origin)
    {
        var address = new FlatWorld.WorldModel.WorldAddress(dimensionId,
            new Int2(origin.x, origin.y));
        return chunkManager.TryGetChunkRuntime(address, out ChunkRuntime chunk) &&
               chunk.DataStatus == ChunkDataStatus.Ready && chunk.Terrain != null && !chunk.Terrain.IsDisposed
            ? chunk.Terrain
            : null;
    }

    private Vector2Int ResolveChunkOrigin(Vector2Int cell)
    {
        Vector2Int origin = new(
            Mathf.FloorToInt((float)cell.x / width) * width,
            Mathf.FloorToInt((float)cell.y / height) * height);
        return bounds.IsWrapped ? bounds.NormalizeChunkOrigin(origin) : origin;
    }

    private static bool ContainsDependency(Bucket bucket, Vector2Int origin)
    {
        foreach (Dependency dependency in bucket.Dependencies)
            if (dependency.Origin == origin)
                return true;
        return false;
    }

    #endregion
}
