using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;

internal sealed partial class LiquidTemperatureField
{
    #region 同步单点批次

    internal readonly struct PointSource
    {
        public readonly Vector2Int Origin;
        public readonly SourceChunk Source;
        public PointSource(Vector2Int origin, SourceChunk source) { Origin = origin; Source = source; }
    }

    internal sealed class PointBucket
    {
        public Influence[] CachedInfluences;
        public readonly List<PointSource> Sources = new();

        public void Clear()
        {
            CachedInfluences = null;
            Sources.Clear();
        }
    }

    internal sealed class PointBatchState
    {
        public readonly Dictionary<Vector2Int, PointBucket> Buckets = new();
        public readonly List<PointBucket> Pool = new();
        public bool Active;
        public int Generation;
        public int Frame;
        public int UsedBuckets;
        public int DependencyLookups;

        public void Release(int generation)
        {
            if (!Active || Generation != generation)
                return;
            Active = false;
            for (int i = 0; i < UsedBuckets; i++) Pool[i].Clear();
            Buckets.Clear();
            UsedBuckets = 0;
        }
    }

    // 批次只借用当前同步只读阶段的邻区，调用方不得在作用域内修改地形、热源或目录。
    internal readonly struct PointSamplingBatch : IDisposable
    {
        private readonly LiquidTemperatureField owner;
        private readonly PointBatchState state;
        private readonly int generation;
        private readonly int resources;

        internal PointSamplingBatch(LiquidTemperatureField owner, PointBatchState state)
        {
            this.owner = owner;
            this.state = state;
            generation = state.Generation;
            resources = owner.resourceGeneration;
        }

        public bool IsCreated => owner != null;
        public int PreparedChunkCount => state?.Buckets.Count ?? 0;
        public int DependencyLookups => state?.DependencyLookups ?? 0;

        public void AccumulateAtPoint(Vector2Int cell, float ambient,
            ref float strongestHeating, ref float strongestCooling)
        {
            if (owner == null) return;
            if (!state.Active || state.Generation != generation || state.Frame != Time.frameCount ||
                resources != owner.resourceGeneration)
                throw new InvalidOperationException("单点温度批次只能在来源未变的同步作用域内使用。");
            if (owner.world.Epoch != owner.worldEpoch || owner.world.Chunks.Count == 0)
                return;
            owner.AccumulatePointBatch(state, cell, ambient, ref strongestHeating, ref strongestCooling);
        }

        public void Dispose() => state?.Release(generation);
    }

    private readonly List<PointBatchState> pointBatchStates = new();

    public PointSamplingBatch BeginPointSamplingBatch()
        => PrepareResources(out _) ? CreatePointSamplingBatch() : default;

    // 显式目录入口也用于独立世界，来源版本变更与正式资源重载共用同一套失效规则。
    internal PointSamplingBatch BeginPointSamplingBatch(IReadOnlyDictionary<string, LiquidDefinition> definitions,
        int reloadVersion)
        => PrepareDefinitions(definitions, definitions, reloadVersion) ? CreatePointSamplingBatch() : default;

    private PointSamplingBatch CreatePointSamplingBatch()
    {
        PointBatchState state = null;
        foreach (PointBatchState candidate in pointBatchStates)
            if (!candidate.Active) { state = candidate; break; }
        if (state == null)
        {
            state = new PointBatchState();
            pointBatchStates.Add(state);
        }
        state.Active = true;
        state.Generation++;
        state.Frame = Time.frameCount;
        state.DependencyLookups = 0;
        return new PointSamplingBatch(this, state);
    }

    private void AccumulatePointBatch(PointBatchState state, Vector2Int cell, float ambient,
        ref float strongestHeating, ref float strongestCooling)
    {
        using (PointMarker.Auto())
        {
            if (bounds.IsWrapped) cell = bounds.NormalizeCell(cell);
            Vector2Int origin = ResolveChunkOrigin(cell);
            if (!state.Buckets.TryGetValue(origin, out PointBucket bucket))
            {
                if (state.UsedBuckets == state.Pool.Count) state.Pool.Add(new PointBucket());
                bucket = state.Pool[state.UsedBuckets++];
                state.Buckets.Add(origin, bucket);
                PreparePointBucket(state, origin, bucket);
            }
            if (bucket.CachedInfluences != null)
            {
                int index = (cell.y - origin.y) * width + cell.x - origin.x;
                Accumulate(bucket.CachedInfluences, index, ambient, ref strongestHeating, ref strongestCooling);
                return;
            }
            Vector2 center = new(cell.x + 0.5f, cell.y + 0.5f);
            foreach (PointSource source in bucket.Sources)
                source.Source.AccumulateAtPoint(source.Origin, center, bounds, ambient,
                    ref strongestHeating, ref strongestCooling);
        }
    }

    private void PreparePointBucket(PointBatchState state, Vector2Int targetOrigin, PointBucket target)
    {
        if (buckets.TryGetValue(targetOrigin, out Bucket cached))
        {
            state.DependencyLookups += cached.Dependencies.Count;
            if (DependenciesAreCurrent(cached))
            {
                target.CachedInfluences = cached.Influences;
                return;
            }
            buckets.Remove(targetOrigin);
        }

        int radius = Mathf.CeilToInt(WorldLiquidSettings.MaximumRadiantHeatRadius);
        int minX = Mathf.FloorToInt((float)(targetOrigin.x - radius) / width) * width;
        int minY = Mathf.FloorToInt((float)(targetOrigin.y - radius) / height) * height;
        int maxX = Mathf.FloorToInt((float)(targetOrigin.x + width - 1 + radius) / width) * width;
        int maxY = Mathf.FloorToInt((float)(targetOrigin.y + height - 1 + radius) / height) * height;
        pointOrigins.Clear();
        for (int y = minY; y <= maxY; y += height)
        for (int x = minX; x <= maxX; x += width)
        {
            Vector2Int origin = new(x, y);
            if (bounds.IsWrapped) origin = bounds.NormalizeChunkOrigin(origin);
            if (!pointOrigins.Add(origin)) continue;
            state.DependencyLookups++;
            ChunkTerrainData terrain = ResolveTerrain(origin);
            if (terrain == null) continue;
            SourceChunk source = GetSources(origin, terrain);
            if (source.HasSources) target.Sources.Add(new PointSource(origin, source));
        }
    }

    #endregion
}
