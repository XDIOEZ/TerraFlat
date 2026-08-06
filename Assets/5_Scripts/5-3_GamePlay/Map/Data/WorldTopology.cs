using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Burst-safe description of the active generation domain. A default value is
/// intentionally unbounded so legacy/infinite callers keep their old behaviour.
/// </summary>
public readonly struct WorldTopologyDomain
{
    public readonly int2 Min;
    public readonly int2 Span;
    public readonly int IsWrappedValue;

    public bool IsWrapped => IsWrappedValue != 0;

    public WorldTopologyDomain(int2 min, int2 span, bool isWrapped)
    {
        Min = min;
        Span = span;
        IsWrappedValue = isWrapped ? 1 : 0;
    }

    public float2 Normalize(float2 position)
    {
        if (!IsWrapped)
            return position;

        return new float2(
            Wrap(position.x, Min.x, Span.x),
            Wrap(position.y, Min.y, Span.y));
    }

    public int2 Normalize(int2 position)
    {
        if (!IsWrapped)
            return position;

        return new int2(
            Wrap(position.x, Min.x, Span.x),
            Wrap(position.y, Min.y, Span.y));
    }

    public float2 ShortestDelta(float2 from, float2 to)
    {
        float2 delta = to - from;
        if (!IsWrapped)
            return delta;

        return new float2(WrapDelta(delta.x, Span.x), WrapDelta(delta.y, Span.y));
    }

    private static float Wrap(float value, int min, int span)
    {
        float offset = value - min;
        return min + offset - math.floor(offset / span) * span;
    }

    private static int Wrap(int value, int min, int span)
    {
        long offset = (long)value - min;
        long wrapped = offset % span;
        if (wrapped < 0L)
            wrapped += span;
        return (int)(min + wrapped);
    }

    private static float WrapDelta(float delta, int span)
    {
        return delta - math.floor((delta + span * 0.5f) / span) * span;
    }
}

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
        return position.x >= Min.x && position.x < MaxExclusive.x &&
               position.y >= Min.y && position.y < MaxExclusive.y;
    }

    public bool Contains(Vector2Int cell)
    {
        return cell.x >= Min.x && cell.x < MaxExclusive.x &&
               cell.y >= Min.y && cell.y < MaxExclusive.y;
    }

    public Vector2 NormalizePosition(Vector2 position)
    {
        return new Vector2(
            Wrap(position.x, Min.x, Span.x),
            Wrap(position.y, Min.y, Span.y));
    }

    public Vector3 NormalizePosition(Vector3 position)
    {
        Vector2 normalized = NormalizePosition(new Vector2(position.x, position.y));
        return new Vector3(normalized.x, normalized.y, position.z);
    }

    public Vector2Int NormalizeCell(Vector2Int cell)
    {
        return new Vector2Int(
            Wrap(cell.x, Min.x, Span.x),
            Wrap(cell.y, Min.y, Span.y));
    }

    public Vector2Int NormalizeChunkOrigin(Vector2Int chunkOrigin)
    {
        return new Vector2Int(
            Wrap(chunkOrigin.x, Min.x, Span.x),
            Wrap(chunkOrigin.y, Min.y, Span.y));
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
        Vector2 delta = to - from;
        return new Vector2(WrapDelta(delta.x, Span.x), WrapDelta(delta.y, Span.y));
    }

    public Vector2Int ShortestDelta(Vector2Int from, Vector2Int to)
    {
        Vector2Int delta = to - from;
        return new Vector2Int(WrapDelta(delta.x, Span.x), WrapDelta(delta.y, Span.y));
    }

    public float Distance(Vector2 from, Vector2 to) => ShortestDelta(from, to).magnitude;

    public float SqrDistance(Vector2 from, Vector2 to) => ShortestDelta(from, to).sqrMagnitude;

    public Vector2 NearestImagePosition(Vector2 origin, Vector2 target)
    {
        return origin + ShortestDelta(origin, target);
    }

    public WorldTopologyDomain ToDomain()
    {
        return new WorldTopologyDomain(
            new int2(Min.x, Min.y),
            new int2(Span.x, Span.y),
            true);
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

    private static float Wrap(float value, int min, int span)
    {
        double offset = value - min;
        double wrapped = offset - Math.Floor(offset / span) * span;
        return (float)(min + wrapped);
    }

    private static int Wrap(int value, int min, int span)
    {
        long offset = (long)value - min;
        long wrapped = offset % span;
        if (wrapped < 0L)
        {
            wrapped += span;
        }

        return (int)(min + wrapped);
    }

    private static float WrapDelta(float delta, int span)
    {
        double wrapped = delta - Math.Floor((delta + span * 0.5d) / span) * span;
        return (float)wrapped;
    }

    private static int WrapDelta(int delta, int span)
    {
        long half = span / 2L;
        long wrapped = ((long)delta + half) % span;
        if (wrapped < 0L)
        {
            wrapped += span;
        }

        return (int)(wrapped - half);
    }
}

/// <summary>Central access point for the topology of the active planet.</summary>
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
        return ShortestDelta(from, to).magnitude;
    }

    public static float SqrDistance(Vector2 from, Vector2 to)
    {
        return ShortestDelta(from, to).sqrMagnitude;
    }

    public static Vector2 NearestImagePosition(Vector2 origin, Vector2 target)
    {
        return origin + ShortestDelta(origin, target);
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
