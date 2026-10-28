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
        internal readonly WorldPresentationSortingDomain SortingDomain;
        internal readonly Action DomainDisposing;
        internal Entry(Matrix4x4 matrix, bool visible, WorldPresentationSortingDomain sortingDomain, Action domainDisposing)
        { Matrix = matrix; Visible = visible; SortingDomain = sortingDomain; DomainDisposing = domainDisposing; }
    }
    private static readonly Dictionary<string, Entry> worlds = new(StringComparer.Ordinal);

    public static void Set(string world, Matrix4x4 matrix, bool visible, WorldPresentationSortingDomain sortingDomain = null)
    {
        if (string.IsNullOrEmpty(world)) throw new ArgumentException("显示世界身份为空。", nameof(world));
        if (sortingDomain != null && (sortingDomain.IsDisposed || sortingDomain.WorldKey != world))
            throw new ArgumentException("排序域与显示世界不匹配。", nameof(sortingDomain));
        worlds.TryGetValue(world, out Entry previous);
        Action handler = previous.DomainDisposing;
        if (!ReferenceEquals(previous.SortingDomain, sortingDomain))
        {
            if (previous.SortingDomain != null) previous.SortingDomain.Disposing -= handler;
            handler = null;
            if (sortingDomain != null)
            {
                handler = () => RemoveIfSortingDomainMatches(world, sortingDomain);
                sortingDomain.Disposing += handler;
            }
        }
        worlds[world] = new Entry(matrix, visible, sortingDomain, handler);
    }
    public static bool TryGet(string world, out Matrix4x4 matrix, out bool visible)
    {
        if (world != null && worlds.TryGetValue(world, out Entry entry))
        { matrix = entry.Matrix; visible = entry.Visible; return true; }
        matrix = Matrix4x4.identity; visible = false; return false;
    }
    public static bool TryGetSortingDomain(string world, out WorldPresentationSortingDomain sortingDomain)
    {
        sortingDomain = world != null && worlds.TryGetValue(world, out Entry entry) ? entry.SortingDomain : null;
        if (sortingDomain != null && !sortingDomain.IsDisposed) return true;
        sortingDomain = null;
        return false;
    }
    public static void Remove(string world)
    {
        if (world == null || !worlds.Remove(world, out Entry previous)) return;
        if (previous.SortingDomain != null) previous.SortingDomain.Disposing -= previous.DomainDisposing;
    }

    private static void RemoveIfSortingDomainMatches(string world, WorldPresentationSortingDomain domain)
    {
        if (worlds.TryGetValue(world, out Entry entry) && ReferenceEquals(entry.SortingDomain, domain))
            Remove(world);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        foreach (Entry entry in worlds.Values)
            if (entry.SortingDomain != null) entry.SortingDomain.Disposing -= entry.DomainDisposing;
        worlds.Clear();
    }
    #endregion
}
