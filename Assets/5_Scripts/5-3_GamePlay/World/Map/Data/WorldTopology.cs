using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public readonly struct WorldWrapEvent
{
    public Vector2 PreviousPosition { get; }
    public Vector2 CurrentPosition { get; }
    public Vector2 WorldShift { get; }

    public WorldWrapEvent(Vector2 previousPosition, Vector2 currentPosition)
    {
        PreviousPosition = previousPosition;
        CurrentPosition = currentPosition;
        WorldShift = currentPosition - previousPosition;
    }
}

/// <summary>
/// Immutable, chunk-aligned bounds for a wrapped world. Min is inclusive and
/// MaxExclusive is exclusive on both axes.
/// </summary>
public readonly struct WorldTopologyBounds
{
    public WorldTopologyMode Mode { get; }
    public Vector2Int ChunkSize { get; }
    public Vector2Int HalfExtent { get; }
    public Vector2Int Min { get; }
    public Vector2Int MaxExclusive { get; }
    public Vector2Int Span { get; }

    public bool IsWrapped => Mode == WorldTopologyMode.Wrapped;

    private WorldTopologyBounds(Vector2Int chunkSize, Vector2Int halfExtent)
    {
        Mode = WorldTopologyMode.Wrapped;
        ChunkSize = chunkSize;
        HalfExtent = halfExtent;
        Min = -halfExtent;
        MaxExclusive = halfExtent;
        Span = halfExtent * 2;
    }

    public static bool TryCreate(PlanetData planetData, out WorldTopologyBounds bounds)
    {
        bounds = default;
        if (planetData == null || planetData.TopologyMode != WorldTopologyMode.Wrapped)
        {
            return false;
        }

        if (planetData.Radius <= 0 || planetData.ChunkSize.x <= 0 || planetData.ChunkSize.y <= 0)
        {
            return false;
        }

        if (!TryAlignHalfExtent(planetData.Radius, planetData.ChunkSize.x, out int halfX) ||
            !TryAlignHalfExtent(planetData.Radius, planetData.ChunkSize.y, out int halfY))
        {
            return false;
        }

        bounds = new WorldTopologyBounds(planetData.ChunkSize, new Vector2Int(halfX, halfY));
        return true;
    }

    public bool Contains(Vector2 position)
    {
        return ToDomain().Contains(new float2(position.x, position.y));
    }

    public bool Contains(Vector2Int cell)
    {
        return ToDomain().Contains(new int2(cell.x, cell.y));
    }

    public Vector2 NormalizePosition(Vector2 position)
    {
        float2 normalized = ToDomain().Normalize(new float2(position.x, position.y));
        return new Vector2(normalized.x, normalized.y);
    }

    public Vector3 NormalizePosition(Vector3 position)
    {
        Vector2 normalized = NormalizePosition(new Vector2(position.x, position.y));
        return new Vector3(normalized.x, normalized.y, position.z);
    }

    public Vector2Int NormalizeCell(Vector2Int cell)
    {
        int2 normalized = ToDomain().Normalize(new int2(cell.x, cell.y));
        return new Vector2Int(normalized.x, normalized.y);
    }

    public Vector2Int NormalizeChunkOrigin(Vector2Int chunkOrigin)
    {
        return NormalizeCell(chunkOrigin);
    }

    public HashSet<Vector2Int> BuildChunkWindow(Vector2Int centerChunkOrigin, int radiusInChunks)
    {
        radiusInChunks = Mathf.Max(0, radiusInChunks);
        centerChunkOrigin = NormalizeChunkOrigin(centerChunkOrigin);
        var result = new HashSet<Vector2Int>();
        for (int x = -radiusInChunks; x <= radiusInChunks; x++)
        {
            for (int y = -radiusInChunks; y <= radiusInChunks; y++)
            {
                result.Add(NormalizeChunkOrigin(new Vector2Int(
                    centerChunkOrigin.x + x * ChunkSize.x,
                    centerChunkOrigin.y + y * ChunkSize.y)));
            }
        }

        return result;
    }

    /// <summary>Returns the shortest wrapped displacement from from to to.</summary>
    public Vector2 ShortestDelta(Vector2 from, Vector2 to)
    {
        float2 delta = ToDomain().ShortestDelta(new float2(from.x, from.y), new float2(to.x, to.y));
        return new Vector2(delta.x, delta.y);
    }

    public Vector2Int ShortestDelta(Vector2Int from, Vector2Int to)
    {
        int2 delta = ToDomain().ShortestDelta(new int2(from.x, from.y), new int2(to.x, to.y));
        return new Vector2Int(delta.x, delta.y);
    }

    public float Distance(Vector2 from, Vector2 to) =>
        ToDomain().Distance(new float2(from.x, from.y), new float2(to.x, to.y));

    public float SqrDistance(Vector2 from, Vector2 to) =>
        ToDomain().SqrDistance(new float2(from.x, from.y), new float2(to.x, to.y));

    public Vector2 NearestImagePosition(Vector2 origin, Vector2 target)
    {
        float2 position = ToDomain().NearestImagePosition(new float2(origin.x, origin.y), new float2(target.x, target.y));
        return new Vector2(position.x, position.y);
    }

    public WorldTopologyDomain ToDomain()
    {
        return new WorldTopologyDomain(
            new int2(Min.x, Min.y),
            new int2(Span.x, Span.y),
            IsWrapped);
    }

    private static bool TryAlignHalfExtent(int radius, int chunkSize, out int aligned)
    {
        long value = ((long)radius + chunkSize - 1L) / chunkSize * chunkSize;
        if (value <= 0L || value > int.MaxValue / 2L)
        {
            aligned = 0;
            return false;
        }

        aligned = (int)value;
        return true;
    }

}

/// <summary>
/// 主线程的活动世界配置与通知入口，仍读取 SaveDataMgr，不能在 Jobs 中调用。
/// 后台任务只接收主线程预先取出的 WorldTopologyDomain 值副本。
/// </summary>
public static class WorldTopologyRuntime
{
    public static event Action LocalPlayerWrapped;
    public static event Action<WorldWrapEvent> LocalPlayerPositionWrapped;
    public static event Action<WorldWrapEvent> PositionWrapped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeEvents()
    {
        LocalPlayerWrapped = null;
        LocalPlayerPositionWrapped = null;
        PositionWrapped = null;
    }

    public static bool TryGetActiveBounds(out WorldTopologyBounds bounds)
    {
        SaveDataMgr saveDataManager = SaveDataMgr.Instance;
        PlanetData activePlanet = saveDataManager != null &&
                                  saveDataManager.TryGetActivePlanetData(out PlanetData planet)
            ? planet
            : null;
        return WorldTopologyBounds.TryCreate(activePlanet, out bounds);
    }

    public static Vector2 NormalizePosition(Vector2 position)
    {
        return TryGetActiveBounds(out WorldTopologyBounds bounds)
            ? bounds.NormalizePosition(position)
            : position;
    }

    public static Vector3 NormalizePosition(Vector3 position)
    {
        return TryGetActiveBounds(out WorldTopologyBounds bounds)
            ? bounds.NormalizePosition(position)
            : position;
    }

    public static Vector2Int NormalizeCell(Vector2Int cell)
    {
        return TryGetActiveBounds(out WorldTopologyBounds bounds)
            ? bounds.NormalizeCell(cell)
            : cell;
    }

    public static Vector2Int NormalizeChunkOrigin(Vector2Int chunkOrigin)
    {
        return TryGetActiveBounds(out WorldTopologyBounds bounds)
            ? bounds.NormalizeChunkOrigin(chunkOrigin)
            : chunkOrigin;
    }

    public static Vector2 ShortestDelta(Vector2 from, Vector2 to)
    {
        return TryGetActiveBounds(out WorldTopologyBounds bounds)
            ? bounds.ShortestDelta(from, to)
            : to - from;
    }

    public static Vector2Int ShortestDelta(Vector2Int from, Vector2Int to)
    {
        return TryGetActiveBounds(out WorldTopologyBounds bounds)
            ? bounds.ShortestDelta(from, to)
            : to - from;
    }

    public static float Distance(Vector2 from, Vector2 to)
    {
        return GetActiveDomain().Distance(new float2(from.x, from.y), new float2(to.x, to.y));
    }

    public static float SqrDistance(Vector2 from, Vector2 to)
    {
        return GetActiveDomain().SqrDistance(new float2(from.x, from.y), new float2(to.x, to.y));
    }

    public static Vector2 NearestImagePosition(Vector2 origin, Vector2 target)
    {
        float2 position = GetActiveDomain().NearestImagePosition(new float2(origin.x, origin.y), new float2(target.x, target.y));
        return new Vector2(position.x, position.y);
    }

    public static WorldTopologyDomain GetActiveDomain()
    {
        return TryGetActiveBounds(out WorldTopologyBounds bounds)
            ? bounds.ToDomain()
            : default;
    }

    public static void NotifyLocalPlayerWrapped()
    {
        LocalPlayerWrapped?.Invoke();
    }

    public static void NotifyLocalPlayerWrapped(Vector2 previousPosition, Vector2 currentPosition)
    {
        var wrapEvent = new WorldWrapEvent(previousPosition, currentPosition);
        PositionWrapped?.Invoke(wrapEvent);
        LocalPlayerPositionWrapped?.Invoke(wrapEvent);
        LocalPlayerWrapped?.Invoke();
    }

    public static void NotifyPositionWrapped(Vector2 previousPosition, Vector2 currentPosition)
    {
        PositionWrapped?.Invoke(new WorldWrapEvent(previousPosition, currentPosition));
    }
}


/// <summary>Finite generation domain whose exits re-enter from the opposite edge.</summary>
public sealed class WrappedWorldGenerationDomain : IWorldGenerationDomain
{
    private readonly WorldTopologyBounds bounds;

    public WorldTopologyBounds Bounds => bounds;
    public uint GenerationSignature { get; }

    public WrappedWorldGenerationDomain(WorldTopologyBounds bounds)
    {
        if (!bounds.IsWrapped)
            throw new ArgumentException("Wrapped generation domain requires wrapped bounds.", nameof(bounds));

        this.bounds = bounds;
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)bounds.Min.x) * 16777619u;
            hash = (hash ^ (uint)bounds.Min.y) * 16777619u;
            hash = (hash ^ (uint)bounds.Span.x) * 16777619u;
            hash = (hash ^ (uint)bounds.Span.y) * 16777619u;
            GenerationSignature = hash;
        }
    }

    public bool Contains(Vector2Int worldPosition) => bounds.Contains(worldPosition);

    public bool TryResolveOutflow(
        Vector2Int fromWorldPosition,
        Vector2Int outsideCandidate,
        out Vector2Int outflowPosition)
    {
        _ = fromWorldPosition;
        outflowPosition = bounds.NormalizeCell(outsideCandidate);
        return true;
    }

    public static IWorldGenerationDomain Create(PlanetData planetData)
    {
        return WorldTopologyBounds.TryCreate(planetData, out WorldTopologyBounds wrappedBounds)
            ? new WrappedWorldGenerationDomain(wrappedBounds)
            : UnboundedWorldGenerationDomain.Instance;
    }
}
