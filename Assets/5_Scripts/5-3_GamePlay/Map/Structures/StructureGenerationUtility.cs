using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class StructureHashUtility
{
    private const uint OffsetBasis = 2166136261u;
    private const uint Prime = 16777619u;

    public static uint Begin() => OffsetBasis;

    public static uint Add(uint hash, bool value) => Add(hash, value ? 1 : 0);

    public static uint Add(uint hash, float value)
    {
        int quantized = Mathf.RoundToInt(value * 1000000f);
        return Add(hash, quantized);
    }

    public static uint Add(uint hash, int value)
    {
        unchecked
        {
            hash = (hash ^ (byte)value) * Prime;
            hash = (hash ^ (byte)(value >> 8)) * Prime;
            hash = (hash ^ (byte)(value >> 16)) * Prime;
            hash = (hash ^ (byte)(value >> 24)) * Prime;
            return hash;
        }
    }

    public static uint Add(uint hash, uint value) => Add(hash, unchecked((int)value));

    public static uint Add(uint hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        unchecked
        {
            for (int i = 0; i < bytes.Length; i++)
                hash = (hash ^ bytes[i]) * Prime;
            return (hash ^ 0xffu) * Prime;
        }
    }

    public static uint Combine(params int[] values)
    {
        uint hash = Begin();
        if (values == null)
            return hash;
        for (int i = 0; i < values.Length; i++)
            hash = Add(hash, values[i]);
        return hash;
    }
}

public struct StructureRandom
{
    private uint state;

    public StructureRandom(uint seed)
    {
        state = seed == 0u ? 0x9E3779B9u : seed;
    }

    public uint NextUInt()
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    public float Next01()
    {
        return (NextUInt() & 0xFFFFFFu) / 16777216f;
    }

    public int Range(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
            return minInclusive;
        uint width = (uint)(maxExclusive - minInclusive);
        return minInclusive + (int)(NextUInt() % width);
    }
}

public static class StructureTransformUtility
{
    public static int NormalizeQuarterTurns(int quarterTurns)
    {
        int value = quarterTurns % 4;
        return value < 0 ? value + 4 : value;
    }

    public static Vector2Int TransformCell(Vector2Int cell, Vector2Int size, int quarterTurns, bool mirrorX)
    {
        int x = mirrorX ? size.x - 1 - cell.x : cell.x;
        int y = cell.y;
        switch (NormalizeQuarterTurns(quarterTurns))
        {
            case 1: return new Vector2Int(size.y - 1 - y, x);
            case 2: return new Vector2Int(size.x - 1 - x, size.y - 1 - y);
            case 3: return new Vector2Int(y, size.x - 1 - x);
            default: return new Vector2Int(x, y);
        }
    }

    public static Vector2 TransformPoint(Vector2 point, Vector2Int size, int quarterTurns, bool mirrorX)
    {
        float x = mirrorX ? size.x - point.x : point.x;
        float y = point.y;
        switch (NormalizeQuarterTurns(quarterTurns))
        {
            case 1: return new Vector2(size.y - y, x);
            case 2: return new Vector2(size.x - x, size.y - y);
            case 3: return new Vector2(y, size.x - x);
            default: return new Vector2(x, y);
        }
    }

    public static float TransformRotation(float rotationZ, int quarterTurns, bool mirrorX)
    {
        // Reflection must be paired with a negative X scale. Rotating by 180 degrees
        // would also turn upright sprites upside down instead of mirroring them.
        float rotation = mirrorX ? -rotationZ : rotationZ;
        return Mathf.Repeat(rotation + NormalizeQuarterTurns(quarterTurns) * 90f, 360f);
    }

    public static Vector3 TransformScale(Vector3 scale, bool mirrorX)
    {
        if (!mirrorX)
            return scale;

        scale.x = -scale.x;
        return scale;
    }
}

public sealed class StructureGenerationMask
{
    private readonly bool[,] occupied;

    public int Width { get; }
    public int Height { get; }

    public StructureGenerationMask(int width, int height)
    {
        Width = Mathf.Max(0, width);
        Height = Mathf.Max(0, height);
        occupied = new bool[Width, Height];
    }

    public bool Contains(int x, int y)
    {
        return (uint)x < (uint)Width && (uint)y < (uint)Height;
    }

    public bool IsOccupied(int x, int y)
    {
        return Contains(x, y) && occupied[x, y];
    }

    public void SetOccupied(int x, int y, bool value = true)
    {
        if (Contains(x, y))
            occupied[x, y] = value;
    }

    public bool Overlaps(RectInt localBounds)
    {
        for (int x = localBounds.xMin; x < localBounds.xMax; x++)
        {
            for (int y = localBounds.yMin; y < localBounds.yMax; y++)
            {
                if (!Contains(x, y) || occupied[x, y])
                    return true;
            }
        }
        return false;
    }

    public void Fill(RectInt localBounds)
    {
        for (int x = localBounds.xMin; x < localBounds.xMax; x++)
        {
            for (int y = localBounds.yMin; y < localBounds.yMax; y++)
                SetOccupied(x, y);
        }
    }
}

public sealed class StructureRuntimeLocation
{
    public int WorldSeed { get; }
    public string StructureId { get; }
    public string DisplayName { get; }
    public uint InstanceSeed { get; }
    public Vector2 EntrancePosition { get; }

    public StructureRuntimeLocation(
        int worldSeed,
        string structureId,
        string displayName,
        uint instanceSeed,
        Vector2 entrancePosition)
    {
        WorldSeed = worldSeed;
        StructureId = structureId;
        DisplayName = displayName;
        InstanceSeed = instanceSeed;
        EntrancePosition = entrancePosition;
    }
}

/// <summary>
/// 记录本次运行期间已经实际生成的遗迹，供 GM 调试工具定位。
/// 坐标由世界种子隔离；Chunk 卸载后仍保留，因为遗迹会按同一种子确定性重建。
/// </summary>
public static class StructureRuntimeRegistry
{
    private static readonly List<StructureRuntimeLocation> Locations = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Locations.Clear();
    }

    public static void Register(
        int worldSeed,
        string structureId,
        string displayName,
        uint instanceSeed,
        Vector2 entrancePosition)
    {
        if (string.IsNullOrWhiteSpace(structureId))
            return;

        for (int i = 0; i < Locations.Count; i++)
        {
            StructureRuntimeLocation existing = Locations[i];
            if (existing.WorldSeed == worldSeed &&
                existing.StructureId == structureId &&
                existing.InstanceSeed == instanceSeed)
            {
                Locations[i] = new StructureRuntimeLocation(
                    worldSeed,
                    structureId,
                    displayName,
                    instanceSeed,
                    entrancePosition);
                return;
            }
        }

        Locations.Add(new StructureRuntimeLocation(
            worldSeed,
            structureId,
            displayName,
            instanceSeed,
            entrancePosition));
    }

    public static int Count(int worldSeed, string structureId)
    {
        int count = 0;
        for (int i = 0; i < Locations.Count; i++)
        {
            StructureRuntimeLocation location = Locations[i];
            if (location.WorldSeed == worldSeed && location.StructureId == structureId)
                count++;
        }

        return count;
    }

    public static bool TryFindNearest(
        int worldSeed,
        string structureId,
        Vector2 origin,
        out StructureRuntimeLocation nearest)
    {
        nearest = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < Locations.Count; i++)
        {
            StructureRuntimeLocation location = Locations[i];
            if (location.WorldSeed != worldSeed || location.StructureId != structureId)
                continue;

            float distance = WorldTopologyRuntime.SqrDistance(origin, location.EntrancePosition);
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearest = location;
        }

        return nearest != null;
    }
}
