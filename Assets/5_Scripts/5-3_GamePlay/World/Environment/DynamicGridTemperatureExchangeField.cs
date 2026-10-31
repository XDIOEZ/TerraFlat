using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>来源替换稀疏交换偏移，地形实例作为生命周期身份，重载不继承旧影响。</summary>
internal sealed class DynamicGridTemperatureExchangeField
{
    #region 动态格交换来源
    internal readonly struct CellOffset
    {
        internal readonly object Terrain;
        internal readonly float Celsius;
        internal CellOffset(object terrain, float celsius) { Terrain = terrain; Celsius = celsius; }
    }
    private readonly Dictionary<object, Dictionary<Vector2Int, CellOffset>> sources = new();
    private readonly Dictionary<(Vector2Int Cell, object Terrain), (double Sum, int Count)> totals = new();
    private readonly Dictionary<object, float> minimumOffsets = new();
    public long Revision { get; private set; }

    public bool ReplaceSourceOffsets(object owner, IReadOnlyDictionary<Vector2Int, CellOffset> values)
    {
        if (!sources.TryGetValue(owner, out var previous))
        {
            if (values.Count == 0) return false;
            sources.Add(owner, previous = new());
        }
        bool changed = previous.Count != values.Count;
        if (!changed)
            foreach (var pair in values)
                if (!previous.TryGetValue(pair.Key, out CellOffset old) ||
                    !ReferenceEquals(old.Terrain, pair.Value.Terrain) || old.Celsius != pair.Value.Celsius)
                { changed = true; break; }
        if (!changed) return false;
        foreach (var pair in previous) Accumulate(pair.Key, pair.Value, -1);
        previous.Clear();
        foreach (var pair in values)
        {
            previous.Add(pair.Key, pair.Value);
            Accumulate(pair.Key, pair.Value, 1);
        }
        if (previous.Count == 0) sources.Remove(owner);
        RefreshMinimumOffsets();
        Revision++;
        return true;
    }

    public bool RemoveSource(object owner)
    {
        if (!sources.Remove(owner, out var previous)) return false;
        foreach (var pair in previous) Accumulate(pair.Key, pair.Value, -1);
        RefreshMinimumOffsets(); Revision++; return true;
    }

    private void Accumulate(Vector2Int cell, CellOffset offset, int sign)
    {
        var key = (cell, offset.Terrain);
        totals.TryGetValue(key, out var current);
        int count = current.Count + sign;
        if (count == 0) totals.Remove(key);
        else totals[key] = (current.Sum + offset.Celsius * sign, count);
    }

    private void RefreshMinimumOffsets()
    {
        minimumOffsets.Clear();
        foreach (var pair in totals)
        {
            float value = (float)(pair.Value.Sum / pair.Value.Count);
            minimumOffsets.TryGetValue(pair.Key.Terrain, out float minimum);
            if (value < minimum) minimumOffsets[pair.Key.Terrain] = value;
        }
    }

    public float GetOffset(Vector2Int cell, object terrain)
        => totals.TryGetValue((cell, terrain), out var value) ? (float)(value.Sum / value.Count) : 0f;

    public float GetMinimumOffset(object terrain)
        => minimumOffsets.TryGetValue(terrain, out float minimum) ? minimum : 0f;

    public float[] CaptureChunkOffsets(Vector2Int origin, int width, int height, object terrain)
    {
        float[] values = null;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float offset = GetOffset(new Vector2Int(origin.x + x, origin.y + y), terrain);
            if (offset == 0f) continue;
            values ??= new float[width * height];
            values[y * width + x] = offset;
        }
        return values;
    }
    #endregion
}
