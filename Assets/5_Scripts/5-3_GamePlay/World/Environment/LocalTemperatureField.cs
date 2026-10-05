using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using Unity.Profiling;

/// <summary>
/// 局部冷热源的空间索引。只为冷热源覆盖的区块分配温度偏移缓存，不遍历世界或持有地形租约。
/// 源中心使用摄氏度增量，正数升温、负数降温；影响按 (1 - 距离平方 / 半径平方)² 衰减并叠加。
/// 最大半径 64 格；只有注册、移动、改值或移除才使覆盖区块失效，查询重建一次后为数组读取。
/// 这是可重建的环境影响层，设备开关、燃料和持久化由来源模块负责。
/// </summary>
internal sealed class LocalTemperatureField
{
    public const float MaximumSourceRadius = 64f;
    private static readonly ProfilerMarker RebuildMarker = new("FlatWorld.Temperature.RebuildLocalChunk");

    #region 空间数据

    private sealed class Source
    {
        public Vector2 Center; // 已归一化的中心位置
        public float Radius; // 影响半径
        public float Offset; // 中心温度增量
        public readonly List<Vector2Int> Buckets = new(); // 覆盖的空间分区
    }

    private sealed class Bucket
    {
        public readonly List<Source> Sources = new(); // 只保存能影响当前分区的来源
        public float[] Offsets; // 行优先的逐格温度偏移
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
    public void SetSource(object owner, Vector2 center, float radius, float offset)
    {
        if (owner == null)
            throw new ArgumentNullException(nameof(owner));
        if (!IsFinite(center.x) || !IsFinite(center.y) || !IsFinite(offset) ||
            !IsFinite(radius) || radius <= 0f || radius > MaximumSourceRadius)
            throw new ArgumentOutOfRangeException(nameof(radius), "冷热源参数必须有限，半径范围为 (0, 64] 格。");

        center = Normalize(center);
        if (offset == 0f)
        {
            RemoveSource(owner);
            return;
        }

        if (sources.TryGetValue(owner, out Source source))
        {
            if (source.Center == center && source.Radius == radius && source.Offset == offset)
                return;
            RemoveFromBuckets(source);
        }
        else
        {
            source = new Source();
            sources.Add(owner, source);
        }

        source.Center = center;
        source.Radius = radius;
        source.Offset = offset;
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

    /// <summary>从已归一化的世界格坐标读取温度偏移；没有冷热源的区域直接返回零。</summary>
    public float Sample(Vector2Int cell)
    {
        Vector2Int origin = new(
            Mathf.FloorToInt((float)cell.x / width) * width,
            Mathf.FloorToInt((float)cell.y / height) * height);
        if (!buckets.TryGetValue(origin, out Bucket bucket))
            return 0f;
        if (bucket.Dirty)
            Rebuild(origin, bucket);
        return bucket.Offsets[(cell.y - origin.y) * width + cell.x - origin.x];
    }

    private void Rebuild(Vector2Int origin, Bucket bucket)
    {
        using (RebuildMarker.Auto())
        {
            bucket.Offsets ??= new float[width * height];
            Array.Clear(bucket.Offsets, 0, bucket.Offsets.Length);
            foreach (Source source in bucket.Sources)
            {
                float inverseRadiusSquared = 1f / (source.Radius * source.Radius);
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    Vector2 center = new(origin.x + x + 0.5f, origin.y + y + 0.5f);
                    Vector2 delta = bounds.IsWrapped
                        ? bounds.ShortestDelta(source.Center, center)
                        : center - source.Center;
                    float weight = Mathf.Max(0f, 1f - delta.sqrMagnitude * inverseRadiusSquared);
                    bucket.Offsets[y * width + x] += source.Offset * weight * weight;
                }
            }
            bucket.Dirty = false;
        }
    }

    private Vector2 Normalize(Vector2 position) => bounds.IsWrapped ? bounds.NormalizePosition(position) : position;
    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    #endregion
}

/// <summary>把已加载世界液体的辐射热缓存成逐格最大温升，避免大面积岩浆按格叠加到失控。</summary>
internal sealed class RadiantLiquidTemperatureField
{
    #region 分区缓存

    private sealed class Dependency
    {
        public Vector2Int Origin;
        public ChunkTerrainData Terrain;
        public long Revision;
    }

    private sealed class Bucket
    {
        public float[] Offsets;
        public readonly List<Dependency> Dependencies = new(9);
    }

    private readonly Dictionary<Vector2Int, Bucket> buckets = new();
    private readonly ChunkMgr chunkManager;
    private readonly string dimensionId;
    private readonly int width;
    private readonly int height;
    private readonly WorldTopologyBounds bounds;

    public RadiantLiquidTemperatureField(ChunkMgr chunkManager, string dimensionId,
        int width, int height, WorldTopologyBounds bounds)
    {
        this.chunkManager = chunkManager;
        this.dimensionId = dimensionId;
        this.width = width;
        this.height = height;
        this.bounds = bounds;
    }

    #endregion

    #region 查询与重建

    public float Sample(Vector2Int cell)
    {
        if (chunkManager == null || GameRes.ExistingInstance == null)
            return 0f;

        Vector2Int origin = ResolveChunkOrigin(cell);
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

        int x = cell.x - origin.x;
        int y = cell.y - origin.y;
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
            return 0f;
        return bucket.Offsets[y * width + x];
    }

    private bool DependenciesAreCurrent(Bucket bucket)
    {
        foreach (Dependency dependency in bucket.Dependencies)
        {
            ChunkTerrainData current = ResolveTerrain(dependency.Origin);
            if (!ReferenceEquals(current, dependency.Terrain) ||
                current != null && current.Revision != dependency.Revision)
                return false;
        }
        return true;
    }

    private void Rebuild(Vector2Int targetOrigin, Bucket bucket)
    {
        bucket.Offsets ??= new float[width * height];
        Array.Clear(bucket.Offsets, 0, bucket.Offsets.Length);
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
            bucket.Dependencies.Add(new Dependency
            {
                Origin = sourceOrigin,
                Terrain = terrain,
                Revision = terrain?.Revision ?? -1
            });
            if (terrain != null)
                AccumulateTerrain(targetOrigin, sourceOrigin, terrain, bucket.Offsets);
        }
    }

    private void AccumulateTerrain(Vector2Int targetOrigin, Vector2Int sourceOrigin,
        ChunkTerrainData terrain, float[] offsets)
    {
        GameRes resources = GameRes.ExistingInstance;
        for (int sourceY = 0; sourceY < terrain.Height; sourceY++)
        for (int sourceX = 0; sourceX < terrain.Width; sourceX++)
        {
            if (WorldLiquidSystem.GetSurfaceDepth(terrain, sourceX, sourceY) <= 0f ||
                !resources.TryGetLiquidDefinition(terrain.GetLiquidId(sourceX, sourceY), out LiquidDefinition liquid))
                continue;

            WorldLiquidSettings settings = liquid.WorldWater;
            if (settings == null || settings.RadiantHeatRadius <= 0f || settings.RadiantHeatOffset <= 0f)
                continue;

            Vector2 sourceCenter = new(sourceOrigin.x + sourceX + 0.5f, sourceOrigin.y + sourceY + 0.5f);
            float radiusSquared = settings.RadiantHeatRadius * settings.RadiantHeatRadius;
            for (int targetY = 0; targetY < height; targetY++)
            for (int targetX = 0; targetX < width; targetX++)
            {
                Vector2 targetCenter = new(targetOrigin.x + targetX + 0.5f, targetOrigin.y + targetY + 0.5f);
                Vector2 delta = bounds.IsWrapped
                    ? bounds.ShortestDelta(sourceCenter, targetCenter)
                    : targetCenter - sourceCenter;
                if (delta.sqrMagnitude >= radiusSquared)
                    continue;

                float weight = Mathf.Max(0f, 1f - delta.sqrMagnitude / radiusSquared);
                float offset = settings.RadiantHeatOffset * weight * weight;
                int index = targetY * width + targetX;
                if (offset > offsets[index])
                    offsets[index] = offset;
            }
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
