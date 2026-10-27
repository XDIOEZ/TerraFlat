using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>为具有独立物理坐标的世界提供只读显示变换。</summary>
public static class WorldPresentationTransformRegistry
{
    #region 世界显示变换
    private readonly struct Entry
    {
        internal readonly Matrix4x4 Matrix;
        internal readonly bool Visible;
        internal Entry(Matrix4x4 matrix, bool visible) { Matrix = matrix; Visible = visible; }
    }
    private static readonly Dictionary<string, Entry> worlds = new(StringComparer.Ordinal);

    public static void Set(string world, Matrix4x4 matrix, bool visible)
    {
        if (string.IsNullOrEmpty(world)) throw new ArgumentException("显示世界身份为空。", nameof(world));
        worlds[world] = new Entry(matrix, visible);
    }
    public static bool TryGet(string world, out Matrix4x4 matrix, out bool visible)
    {
        if (world != null && worlds.TryGetValue(world, out Entry entry))
        { matrix = entry.Matrix; visible = entry.Visible; return true; }
        matrix = Matrix4x4.identity; visible = false; return false;
    }
    public static void Remove(string world) { if (world != null) worlds.Remove(world); }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => worlds.Clear();
    #endregion
}
