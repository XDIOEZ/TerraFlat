using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>按观察者格复用权威 LOS 结果，大批目标用阴影投射快速判定通透方向。</summary>
internal sealed class GridLineOfSightQuery : IDisposable
{
    #region 单轮只读上下文

    private static readonly ProfilerMarker LineMarker = new("FlatWorld.Perception.GridLineOfSight");
    private static readonly ProfilerMarker LookupMarker = new("FlatWorld.Perception.ResolveSightChunk");
    private static readonly ProfilerMarker ShadowcastMarker = new("FlatWorld.Perception.Shadowcast");
    private const int ShadowcastCandidateThreshold = 24;
    private const int MaximumShadowcastRadius = 16;
    private const int MaximumCachedTargets = 1024;
    // 每两个八分区组成一个象限，只在整个象限通透时直接接受以保留切角规则。
    private static readonly int[,] Octants =
    {
        { 1, 0, 0, 1 }, { 0, 1, 1, 0 }, { 0, -1, 1, 0 }, { -1, 0, 0, 1 },
        { -1, 0, 0, -1 }, { 0, -1, -1, 0 }, { 0, 1, -1, 0 }, { 1, 0, 0, -1 }
    };
    private readonly struct ChunkStamp
    {
        public readonly ChunkTerrainData Terrain;
        public readonly long BlockingRevision;
        public ChunkStamp(ChunkTerrainData terrain)
        {
            Terrain = terrain;
            BlockingRevision = terrain.BlockingRevision;
        }
    }
    private readonly Dictionary<Vector2Int, ChunkRuntime> chunks = new();
    private readonly Dictionary<Vector2Int, ChunkStamp> chunkStamps = new();
    private readonly Dictionary<Vector2Int, bool> exactResults = new();
    private readonly int[] clearOctantDepths = new int[8];
    private WorldRuntime world;
    private IReadOnlyDictionary<RuntimeWorldAddress, ChunkRuntime> worldChunks;
    private long worldEpoch;
    private WorldTopologyDomain topology;
    private string dimensionId;
    private int chunkWidth;
    private int chunkHeight;
    private Vector2Int observer;
    private bool hasCurrentChunk;
    private Vector2Int currentOrigin;
    private ChunkTerrainData currentTerrain;
    private int shadowcastRadius;
    private bool shadowcastReady;
    private bool encounteredUnavailableChunk;

    internal int ChunkLookupCount { get; private set; }
    internal int CachedChunkCount => chunks.Count;
    internal int ExactRayCount { get; private set; }
    internal int CachedRayCount { get; private set; }
    internal int ShadowcastFastPathCount { get; private set; }
    internal int ShadowcastBuildCount { get; private set; }

    internal void Begin(WorldRuntime sourceWorld, WorldTopologyDomain sourceTopology,
        string sourceDimension, int width, int height, Vector2 observerPosition)
    {
        Vector2Int nextObserver = new(Mathf.FloorToInt(observerPosition.x), Mathf.FloorToInt(observerPosition.y));
        if (sourceTopology.IsWrapped)
            nextObserver = new Vector2Int(sourceTopology.NormalizeX(nextObserver.x), sourceTopology.NormalizeY(nextObserver.y));
        if (!ReferenceEquals(world, sourceWorld) || worldEpoch != (sourceWorld?.Epoch ?? 0) ||
            topology.IsWrappedValue != sourceTopology.IsWrappedValue ||
            math.any(topology.Min != sourceTopology.Min) || math.any(topology.Span != sourceTopology.Span) ||
            !string.Equals(dimensionId, sourceDimension, StringComparison.Ordinal) ||
            chunkWidth != Mathf.Max(1, width) || chunkHeight != Mathf.Max(1, height) ||
            observer != nextObserver)
            Dispose();
        else
            InvalidateIfChunksChanged();

        world = sourceWorld;
        worldChunks = world?.Chunks;
        worldEpoch = world?.Epoch ?? 0;
        topology = sourceTopology;
        dimensionId = sourceDimension;
        chunkWidth = Mathf.Max(1, width);
        chunkHeight = Mathf.Max(1, height);
        observer = nextObserver;
        ChunkLookupCount = 0;
        ExactRayCount = 0;
        CachedRayCount = 0;
        ShadowcastFastPathCount = 0;
        ShadowcastBuildCount = 0;
    }

    public void Dispose()
    {
        chunks.Clear();
        chunkStamps.Clear();
        exactResults.Clear();
        shadowcastReady = false;
        encounteredUnavailableChunk = false;
        world = null;
        worldChunks = null;
        dimensionId = null;
        currentTerrain = null;
        hasCurrentChunk = false;
    }

    private void InvalidateIfChunksChanged()
    {
        if (exactResults.Count == 0 && !shadowcastReady)
            return;
        bool structureChanged = false;
        bool blockingChanged = false;
        foreach (KeyValuePair<Vector2Int, ChunkStamp> stamp in chunkStamps)
        {
            Vector2Int origin = stamp.Key;
            if (worldChunks == null ||
                !worldChunks.TryGetValue(new RuntimeWorldAddress(dimensionId, new Int2(origin.x, origin.y)), out ChunkRuntime chunk) ||
                !IsReady(chunk) || !chunks.TryGetValue(origin, out ChunkRuntime cachedChunk) ||
                !ReferenceEquals(chunk, cachedChunk) || !ReferenceEquals(chunk.Terrain, stamp.Value.Terrain))
            {
                structureChanged = true;
                break;
            }
            if (chunk.Terrain.BlockingRevision != stamp.Value.BlockingRevision)
                blockingChanged = true;
        }
        if (!structureChanged && !blockingChanged)
            return;
        // 地形、占地、区块状态变化后，丢弃一切从旧遮挡数据得到的可见性结果。
        exactResults.Clear();
        shadowcastReady = false;
        currentTerrain = null;
        hasCurrentChunk = false;
        if (structureChanged)
        {
            chunks.Clear();
            chunkStamps.Clear();
        }
        else
        {
            // 仅遮挡位变化无需重查区块地址，保留已解析的 ChunkRuntime。
            foreach (KeyValuePair<Vector2Int, ChunkRuntime> entry in chunks)
                chunkStamps[entry.Key] = new ChunkStamp(entry.Value.Terrain);
        }
    }

    #endregion

    #region 整数格视线

    // Bresenham 保留起终点豁免与对角侧格规则，阴影边缘不改变旧判定。
    internal void PrepareCandidateBatch(int candidateCount, float maximumRadius)
    {
        if (candidateCount < ShadowcastCandidateThreshold || world == null ||
            world.Epoch != worldEpoch || shadowcastReady ||
            !float.IsFinite(maximumRadius) || maximumRadius <= 0f)
            return;
        InvalidateIfChunksChanged();
        encounteredUnavailableChunk = false;
        if (!IsCellLoaded(observer))
            return;

        int radius = Mathf.Clamp(Mathf.CeilToInt(maximumRadius), 1, MaximumShadowcastRadius);
        using (ShadowcastMarker.Auto())
        {
            for (int octant = 0; octant < clearOctantDepths.Length; octant++)
                clearOctantDepths[octant] = radius;
            for (int octant = 0; octant < 8; octant++)
                CastShadow(octant, 1, 1f, 0f, radius);
        }
        shadowcastRadius = radius;
        shadowcastReady = true;
        ShadowcastBuildCount++;
    }

    private void CastShadow(int octant, int firstRow, float startSlope, float endSlope, int radius)
    {
        if (startSlope < endSlope)
            return;
        for (int row = firstRow; row <= radius; row++)
        {
            bool previousBlocked = false;
            float nextStart = startSlope;
            for (int col = -row; col <= 0; col++)
            {
                float leftSlope = (col - .5f) / (-row + .5f);
                float rightSlope = (col + .5f) / (-row - .5f);
                if (startSlope < rightSlope) continue;
                if (endSlope > leftSlope) break;

                Vector2Int cell = new(
                    observer.x + col * Octants[octant, 0] - row * Octants[octant, 1],
                    observer.y + col * Octants[octant, 2] - row * Octants[octant, 3]);
                bool blocked = IsSightBlockingCell(cell);
                if (blocked)
                    clearOctantDepths[octant] = Mathf.Min(clearOctantDepths[octant], row - 1);

                if (previousBlocked)
                {
                    if (blocked) { nextStart = rightSlope; continue; }
                    previousBlocked = false;
                    startSlope = nextStart;
                }
                else if (blocked && row < radius)
                {
                    previousBlocked = true;
                    CastShadow(octant, row + 1, startSlope, leftSlope, radius);
                    nextStart = rightSlope;
                }
            }
            if (previousBlocked) break;
        }
    }

    internal bool HasClearLineTo(Vector2 targetPosition)
    {
        using var scope = LineMarker.Auto();
        if (world == null || world.Epoch != worldEpoch)
            return false;
        InvalidateIfChunksChanged();
        hasCurrentChunk = false;
        Vector2Int target = NormalizeCell(new Vector2Int(
            Mathf.FloorToInt(targetPosition.x), Mathf.FloorToInt(targetPosition.y)));
        if (exactResults.TryGetValue(target, out bool cached))
        {
            CachedRayCount++;
            return cached;
        }

        // Shadowcasting 仅直接接受无阻挡象限，复杂阴影边界继续使用原有精确切角判定。
        int2 delta = topology.ShortestDelta(new int2(observer.x, observer.y), new int2(target.x, target.y));
        int distance = Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y));
        if (shadowcastReady && distance > 0 && distance <= shadowcastRadius)
        {
            int quadrant = delta.x >= 0 ? (delta.y >= 0 ? 2 : 1) : (delta.y >= 0 ? 3 : 0);
            int primaryOctant = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                ? (quadrant == 0 ? 1 : quadrant == 1 ? 2 : quadrant == 2 ? 5 : 6)
                : (quadrant == 0 ? 0 : quadrant == 1 ? 3 : quadrant == 2 ? 4 : 7);
            int clearDepth = clearOctantDepths[primaryOctant];
            // 靠近对角线会触及另一八分区的侧格，必须同时保证相邻八分区通透。
            if (Mathf.Min(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) * 2 > distance)
                clearDepth = Mathf.Min(clearDepth, clearOctantDepths[(quadrant << 1) + ((primaryOctant & 1) == 0 ? 1 : 0)]);
            if (distance <= clearDepth)
            {
                ShadowcastFastPathCount++;
                CacheResult(target, true);
                return true;
            }
        }

        encounteredUnavailableChunk = false;
        ExactRayCount++;
        bool visible = TraceLineTo(target, delta);
        if (!encounteredUnavailableChunk)
            CacheResult(target, visible);
        return visible;
    }

    private void CacheResult(Vector2Int target, bool visible)
    {
        if (exactResults.Count < MaximumCachedTargets)
            exactResults[target] = visible;
    }

    private bool TraceLineTo(Vector2Int target, int2 delta)
    {
        if (!IsCellLoaded(observer))
            return false;
        if (observer == target)
            return true;

        Vector2Int unwrappedTarget = observer + new Vector2Int(delta.x, delta.y);
        int x = observer.x;
        int y = observer.y;
        int dx = Mathf.Abs(delta.x);
        int dy = Mathf.Abs(delta.y);
        int stepX = delta.x >= 0 ? 1 : -1;
        int stepY = delta.y >= 0 ? 1 : -1;

        // 水平或垂直射线没有切角，直接检查中途格，免去每格 Bresenham 分支。
        if (dy == 0)
        {
            for (int nextX = x + stepX; nextX != unwrappedTarget.x; nextX += stepX)
                if (IsSightBlockingCell(new Vector2Int(nextX, y)))
                    return false;
            return IsCellLoaded(target);
        }
        if (dx == 0)
        {
            for (int nextY = y + stepY; nextY != unwrappedTarget.y; nextY += stepY)
                if (IsSightBlockingCell(new Vector2Int(x, nextY)))
                    return false;
            return IsCellLoaded(target);
        }

        int error = dx - dy;
        while (x != unwrappedTarget.x || y != unwrappedTarget.y)
        {
            int previousX = x;
            int previousY = y;
            int doubledError = error * 2;
            if (doubledError > -dy)
            {
                error -= dy;
                x += stepX;
            }
            if (doubledError < dx)
            {
                error += dx;
                y += stepY;
            }
            bool movedDiagonally = x != previousX && y != previousY;
            if (movedDiagonally &&
                (IsSightBlockingCell(new Vector2Int(x, previousY)) ||
                 IsSightBlockingCell(new Vector2Int(previousX, y))))
                return false;
            if ((x != unwrappedTarget.x || y != unwrappedTarget.y) &&
                IsSightBlockingCell(new Vector2Int(x, y)))
                return false;
        }
        // 射线已被遮挡时跳过远端加载查询，无遮挡才验证终点区块。
        return IsCellLoaded(target);
    }

    private Vector2Int NormalizeCell(Vector2Int cell)
    {
        if (!topology.IsWrapped ||
            (uint)(cell.x - topology.Min.x) < (uint)topology.Span.x &&
            (uint)(cell.y - topology.Min.y) < (uint)topology.Span.y)
            return cell;
        return new Vector2Int(topology.NormalizeX(cell.x), topology.NormalizeY(cell.y));
    }

    private bool IsCellLoaded(Vector2Int cell) => TryResolveTerrain(cell, out _, out _, out _);

    private bool IsSightBlockingCell(Vector2Int cell)
    {
        cell = NormalizeCell(cell);
        return !TryResolveTerrain(cell, out ChunkTerrainData terrain, out int localX, out int localY) ||
               terrain.IsSightBlockingCell(localX, localY);
    }

    #endregion

    #region 规范区块寻址缓存

    private bool TryResolveTerrain(Vector2Int cell, out ChunkTerrainData terrain, out int localX, out int localY)
    {
        if (hasCurrentChunk)
        {
            terrain = currentTerrain;
            localX = cell.x - currentOrigin.x;
            localY = cell.y - currentOrigin.y;
            if ((uint)localX < (uint)terrain.Width && (uint)localY < (uint)terrain.Height)
                return true;
        }

        Vector2Int origin = new(FloorOrigin(cell.x, chunkWidth), FloorOrigin(cell.y, chunkHeight));
        origin = NormalizeCell(origin);
        if (!chunks.TryGetValue(origin, out ChunkRuntime chunk) || !IsReady(chunk))
        {
            using var scope = LookupMarker.Auto();
            ChunkLookupCount++;
            if (!worldChunks.TryGetValue(new RuntimeWorldAddress(dimensionId, new Int2(origin.x, origin.y)), out chunk))
            {
                encounteredUnavailableChunk = true;
                terrain = null;
                localX = localY = 0;
                return false;
            }
            chunks[origin] = chunk;
        }
        if (!IsReady(chunk))
        {
            encounteredUnavailableChunk = true;
            terrain = null;
            localX = localY = 0;
            return false;
        }
        if (!chunkStamps.TryGetValue(origin, out ChunkStamp stamp) ||
            !ReferenceEquals(stamp.Terrain, chunk.Terrain) ||
            stamp.BlockingRevision != chunk.Terrain.BlockingRevision)
            chunkStamps[origin] = new ChunkStamp(chunk.Terrain);
        currentOrigin = origin;
        currentTerrain = chunk.Terrain;
        hasCurrentChunk = true;
        return TryGetCell(chunk, origin, cell, out terrain, out localX, out localY);
    }

    private static bool TryGetCell(ChunkRuntime chunk, Vector2Int origin, Vector2Int cell,
        out ChunkTerrainData terrain, out int localX, out int localY)
    {
        terrain = chunk?.Terrain;
        localX = cell.x - origin.x;
        localY = cell.y - origin.y;
        return IsReady(chunk) && (uint)localX < (uint)terrain.Width && (uint)localY < (uint)terrain.Height;
    }

    private static bool IsReady(ChunkRuntime chunk)
        => chunk != null && chunk.DataStatus == ChunkDataStatus.Ready &&
           chunk.Terrain != null && !chunk.Terrain.IsDisposed;

    private static int FloorOrigin(int cell, int size)
        => (cell / size - (cell % size < 0 ? 1 : 0)) * size;

    #endregion
}
