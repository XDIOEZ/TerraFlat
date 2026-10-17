using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

/// <summary>单轮检测复用区块寻址；不缓存可见结果或遮挡位，所有格子读取当前权威地形。</summary>
internal sealed class GridLineOfSightQuery : IDisposable
{
    #region 单轮只读上下文

    private static readonly ProfilerMarker LineMarker = new("FlatWorld.Perception.GridLineOfSight");
    private static readonly ProfilerMarker LookupMarker = new("FlatWorld.Perception.ResolveSightChunk");
    private readonly Dictionary<Vector2Int, ChunkRuntime> chunks = new();
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
    private ChunkRuntime currentChunk;

    internal int ChunkLookupCount { get; private set; }
    internal int CachedChunkCount => chunks.Count;

    internal void Begin(WorldRuntime sourceWorld, WorldTopologyDomain sourceTopology,
        string sourceDimension, int width, int height, Vector2 observerPosition)
    {
        Dispose();
        world = sourceWorld;
        worldChunks = world?.Chunks;
        worldEpoch = world?.Epoch ?? 0;
        topology = sourceTopology;
        dimensionId = sourceDimension;
        chunkWidth = Mathf.Max(1, width);
        chunkHeight = Mathf.Max(1, height);
        observer = NormalizeCell(new Vector2Int(
            Mathf.FloorToInt(observerPosition.x), Mathf.FloorToInt(observerPosition.y)));
        ChunkLookupCount = 0;
    }

    public void Dispose()
    {
        chunks.Clear();
        world = null;
        worldChunks = null;
        dimensionId = null;
        currentChunk = null;
        hasCurrentChunk = false;
    }

    #endregion

    #region 整数格视线

    // Bresenham 保留起终点豁免与对角侧格规则，只改变数据寻址的复用范围。
    internal bool HasClearLineTo(Vector2 targetPosition)
    {
        using var scope = LineMarker.Auto();
        if (world == null || world.Epoch != worldEpoch)
            return false;
        Vector2Int target = NormalizeCell(new Vector2Int(
            Mathf.FloorToInt(targetPosition.x), Mathf.FloorToInt(targetPosition.y)));
        if (!IsCellLoaded(observer) || !IsCellLoaded(target))
            return false;
        if (observer == target)
            return true;

        int2 delta = topology.ShortestDelta(new int2(observer.x, observer.y), new int2(target.x, target.y));
        Vector2Int unwrappedTarget = observer + new Vector2Int(delta.x, delta.y);
        int x = observer.x;
        int y = observer.y;
        int dx = Mathf.Abs(delta.x);
        int dy = Mathf.Abs(delta.y);
        int stepX = delta.x >= 0 ? 1 : -1;
        int stepY = delta.y >= 0 ? 1 : -1;
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
        return true;
    }

    private Vector2Int NormalizeCell(Vector2Int cell)
    {
        if (topology.Contains(new int2(cell.x, cell.y)))
            return cell;
        int2 normalized = topology.Normalize(new int2(cell.x, cell.y));
        return new Vector2Int(normalized.x, normalized.y);
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
        if (hasCurrentChunk && TryGetCell(currentChunk, currentOrigin, cell, out terrain, out localX, out localY))
            return true;

        Vector2Int origin = new(FloorOrigin(cell.x, chunkWidth), FloorOrigin(cell.y, chunkHeight));
        origin = NormalizeCell(origin);
        if (!chunks.TryGetValue(origin, out ChunkRuntime chunk) || !IsReady(chunk))
        {
            using var scope = LookupMarker.Auto();
            ChunkLookupCount++;
            if (!worldChunks.TryGetValue(new RuntimeWorldAddress(dimensionId, new Int2(origin.x, origin.y)), out chunk))
            {
                terrain = null;
                localX = localY = 0;
                return false;
            }
            chunks[origin] = chunk;
        }
        currentOrigin = origin;
        currentChunk = chunk;
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
