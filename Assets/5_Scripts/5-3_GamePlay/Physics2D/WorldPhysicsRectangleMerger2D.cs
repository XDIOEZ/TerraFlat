using System.Collections.Generic;
using UnityEngine;

/// <summary>仅在源矩形变化时精确拼接同物理规则的障碍，不填充凹口、空洞或真实间距。</summary>
internal sealed class WorldPhysicsRectangleMerger2D
{
    #region 源矩形与增量失效

    internal readonly struct MergedObstacleRectangle
    {
        internal readonly int Id;
        internal readonly Rect Bounds;
        internal Vector2 Center => Bounds.center;
        internal Vector2 Size => Bounds.size;

        internal MergedObstacleRectangle(int id, Rect bounds)
        { Id = id; Bounds = bounds; }
    }

    private readonly Dictionary<int, MergedObstacleRectangle> sources = new();
    private readonly List<MergedObstacleRectangle> rectangles = new();
    private readonly List<int> staleSources = new();
    private bool dirty;

    internal int SourceCount => sources.Count;
    internal IReadOnlyList<MergedObstacleRectangle> Rectangles => rectangles;
    internal bool ContainsSource(int id) => sources.ContainsKey(id);

    internal bool SetSourceRectangle(int id, Vector2 center, Vector2 size)
    {
        if (!(size.x > 0f && size.y > 0f) || !float.IsFinite(size.x) || !float.IsFinite(size.y) ||
            !float.IsFinite(center.x) || !float.IsFinite(center.y))
            return RemoveSourceRectangle(id);
        Rect bounds = new(center - size * .5f, size);
        if (sources.TryGetValue(id, out MergedObstacleRectangle previous) && previous.Bounds.Equals(bounds))
            return false;
        sources[id] = new MergedObstacleRectangle(id, bounds);
        dirty = true;
        return true;
    }

    internal bool RemoveSourceRectangle(int id)
    {
        if (!sources.Remove(id)) return false;
        dirty = true;
        return true;
    }

    internal int RemoveSourcesExcept(HashSet<int> retained)
    {
        staleSources.Clear();
        foreach (int id in sources.Keys)
            if (!retained.Contains(id)) staleSources.Add(id);
        int removed = staleSources.Count;
        for (int i = 0; i < staleSources.Count; i++) RemoveSourceRectangle(staleSources[i]);
        staleSources.Clear();
        return removed;
    }

    internal void Clear()
    {
        dirty |= sources.Count > 0 || rectangles.Count > 0;
        sources.Clear();
        rectangles.Clear();
        staleSources.Clear();
    }

    #endregion

    #region 两轴精确矩形拼接

    private static readonly IComparer<MergedObstacleRectangle> horizontalOrder =
        Comparer<MergedObstacleRectangle>.Create(CompareHorizontal);
    private static readonly IComparer<MergedObstacleRectangle> verticalOrder =
        Comparer<MergedObstacleRectangle>.Create(CompareVertical);

    /// <summary>横纵各排序一次，复杂度固定为 O(N log N)，规则矩形阵列可直接压成一个盒。</summary>
    internal bool RebuildMergedRectangles()
    {
        if (!dirty) return false;
        rectangles.Clear();
        foreach (MergedObstacleRectangle rectangle in sources.Values) rectangles.Add(rectangle);
        MergeAlignedRectangles(false);
        MergeAlignedRectangles(true);
        dirty = false;
        return true;
    }

    private void MergeAlignedRectangles(bool vertical)
    {
        if (rectangles.Count < 2) return;
        rectangles.Sort(vertical ? verticalOrder : horizontalOrder);
        int write = 0;
        MergedObstacleRectangle current = rectangles[0];
        for (int read = 1; read < rectangles.Count; read++)
        {
            MergedObstacleRectangle next = rectangles[read];
            Rect a = current.Bounds, b = next.Bounds;
            bool contiguous = vertical
                ? a.xMin == b.xMin && a.xMax == b.xMax && b.yMin <= a.yMax
                : a.yMin == b.yMin && a.yMax == b.yMax && b.xMin <= a.xMax;
            if (contiguous)
            {
                Rect combined = Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                    Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
                current = new MergedObstacleRectangle(System.Math.Min(current.Id, next.Id), combined);
            }
            else
            {
                rectangles[write++] = current;
                current = next;
            }
        }
        rectangles[write++] = current;
        if (write < rectangles.Count) rectangles.RemoveRange(write, rectangles.Count - write);
    }

    private static int CompareHorizontal(MergedObstacleRectangle a, MergedObstacleRectangle b)
    {
        int order = a.Bounds.yMin.CompareTo(b.Bounds.yMin);
        if (order == 0) order = a.Bounds.yMax.CompareTo(b.Bounds.yMax);
        if (order == 0) order = a.Bounds.xMin.CompareTo(b.Bounds.xMin);
        if (order == 0) order = a.Bounds.xMax.CompareTo(b.Bounds.xMax);
        return order != 0 ? order : a.Id.CompareTo(b.Id);
    }

    private static int CompareVertical(MergedObstacleRectangle a, MergedObstacleRectangle b)
    {
        int order = a.Bounds.xMin.CompareTo(b.Bounds.xMin);
        if (order == 0) order = a.Bounds.xMax.CompareTo(b.Bounds.xMax);
        if (order == 0) order = a.Bounds.yMin.CompareTo(b.Bounds.yMin);
        if (order == 0) order = a.Bounds.yMax.CompareTo(b.Bounds.yMax);
        return order != 0 ? order : a.Id.CompareTo(b.Id);
    }

    #endregion
}
