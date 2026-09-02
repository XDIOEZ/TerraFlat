using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct WorldNavigationCell
{
    public readonly uint Penalty;
    public readonly bool Walkable;

    public WorldNavigationCell(uint penalty, bool walkable)
    {
        Penalty = penalty;
        Walkable = walkable;
    }
}

/// <summary>
/// Sparse navigation storage keyed by canonical world cells. Infinite worlds keep absolute
/// coordinates; wrapped worlds connect opposite boundary cells as direct neighbours.
/// </summary>
public sealed class WorldNavigationGrid
{
    private const int MaxPreciseChangedCells = 2048;

    private static readonly Vector2Int[] NeighbourOffsets =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
    };

    private readonly Dictionary<Vector2Int, WorldNavigationCell> cells = new(8192);
    private readonly Dictionary<Vector2Int, int> blockerCounts = new(512);
    private readonly Dictionary<int, HashSet<Vector2Int>> blockerCells = new(128);
    private readonly HashSet<Vector2Int> changedCells = new();
    private bool fullResetPending;
    private int batchUpdateDepth;
    private bool batchHasChanges;
    private bool batchInvalidatesExistingPaths;
    private bool batchChangesPathCost;

    public int Revision { get; private set; }
    public int PathInvalidationRevision { get; private set; }
    public int PathCostRevision { get; private set; }
    public int CellCount => cells.Count;

    public static Vector2Int WorldToCell(Vector2 worldPosition)
        => NormalizeCell(new Vector2Int(Mathf.FloorToInt(worldPosition.x), Mathf.FloorToInt(worldPosition.y)));

    public static Vector2 CellCenter(Vector2Int cell)
    {
        cell = NormalizeCell(cell);
        return new Vector2(cell.x + 0.5f, cell.y + 0.5f);
    }

    public static Vector2Int NormalizeCell(Vector2Int cell)
        => WorldTopologyRuntime.NormalizeCell(cell);

    public void BeginBatchUpdate()
    {
        batchUpdateDepth++;
    }

    public void EndBatchUpdate()
    {
        if (batchUpdateDepth <= 0)
            throw new InvalidOperationException("No navigation grid batch update is active.");

        batchUpdateDepth--;
        if (batchUpdateDepth == 0 && batchHasChanges)
        {
            batchHasChanges = false;
            Revision++;
        }

        if (batchUpdateDepth == 0 && batchInvalidatesExistingPaths)
        {
            batchInvalidatesExistingPaths = false;
            PathInvalidationRevision++;
        }

        if (batchUpdateDepth == 0 && batchChangesPathCost)
        {
            batchChangesPathCost = false;
            PathCostRevision++;
        }
    }

    public void Clear()
    {
        if (cells.Count == 0 && blockerCounts.Count == 0 && blockerCells.Count == 0)
            return;

        cells.Clear();
        blockerCounts.Clear();
        blockerCells.Clear();
        changedCells.Clear();
        fullResetPending = true;
        MarkRevisionChanged(invalidatesExistingPaths: true);
    }

    public void SetCell(Vector2Int position, uint penalty, bool walkable)
    {
        position = NormalizeCell(position);
        WorldNavigationCell next = new(penalty, walkable && penalty > 0u);
        bool hasCurrent = cells.TryGetValue(position, out WorldNavigationCell current);
        if (hasCurrent &&
            current.Penalty == next.Penalty &&
            current.Walkable == next.Walkable)
        {
            return;
        }

        bool blocked = blockerCounts.TryGetValue(position, out int blockerCount) && blockerCount > 0;
        bool wasEffectivelyWalkable = hasCurrent && current.Walkable && !blocked;
        bool willBeEffectivelyWalkable = next.Walkable && !blocked;
        cells[position] = next;
        RecordChangedCell(position);
        // 代价变化会改变最优路径，单独记录代价版本供运行中的 Agent 重新寻路。
        bool changesPathCost = wasEffectivelyWalkable && current.Penalty != next.Penalty;
        bool invalidatesExistingPaths =
            wasEffectivelyWalkable &&
            !willBeEffectivelyWalkable;
        MarkRevisionChanged(invalidatesExistingPaths, changesPathCost);
    }

    public bool RemoveCell(Vector2Int position)
    {
        position = NormalizeCell(position);
        if (!cells.TryGetValue(position, out WorldNavigationCell current))
            return false;

        bool blocked = blockerCounts.TryGetValue(position, out int blockerCount) && blockerCount > 0;
        bool invalidatesExistingPaths = current.Walkable && !blocked;
        cells.Remove(position);
        RecordChangedCell(position);
        MarkRevisionChanged(invalidatesExistingPaths);
        return true;
    }

    public int RemoveRegion(RectInt region)
    {
        int removed = 0;
        bool invalidatesExistingPaths = false;
        var visited = new HashSet<Vector2Int>();
        for (int y = region.yMin; y < region.yMax; y++)
        {
            for (int x = region.xMin; x < region.xMax; x++)
            {
                Vector2Int position = NormalizeCell(new Vector2Int(x, y));
                if (!visited.Add(position))
                    continue;
                if (cells.TryGetValue(position, out WorldNavigationCell current))
                {
                    bool blocked = blockerCounts.TryGetValue(position, out int blockerCount) && blockerCount > 0;
                    invalidatesExistingPaths |= current.Walkable && !blocked;
                    cells.Remove(position);
                    RecordChangedCell(position);
                    removed++;
                }
            }
        }

        if (removed > 0)
            MarkRevisionChanged(invalidatesExistingPaths);
        return removed;
    }

    public bool TryGetCell(Vector2Int position, out WorldNavigationCell cell)
    {
        position = NormalizeCell(position);
        if (!cells.TryGetValue(position, out WorldNavigationCell terrain))
        {
            cell = default;
            return false;
        }

        bool blocked = blockerCounts.TryGetValue(position, out int count) && count > 0;
        cell = new WorldNavigationCell(terrain.Penalty, terrain.Walkable && !blocked);
        return true;
    }

    public bool IsWalkable(Vector2Int position)
        => TryGetCell(position, out WorldNavigationCell cell) && cell.Walkable;

    public void RegisterBlocker(int blockerId, IEnumerable<Vector2Int> occupiedCells)
    {
        if (occupiedCells == null)
        {
            UnregisterBlocker(blockerId);
            return;
        }

        var next = new HashSet<Vector2Int>();
        foreach (Vector2Int occupiedCell in occupiedCells)
            next.Add(NormalizeCell(occupiedCell));
        if (blockerCells.TryGetValue(blockerId, out HashSet<Vector2Int> current) && current.SetEquals(next))
            return;

        BeginBatchUpdate();
        try
        {
            UnregisterBlocker(blockerId);
            if (next.Count == 0)
                return;

            bool invalidatesExistingPaths = false;
            blockerCells[blockerId] = next;
            foreach (Vector2Int cell in next)
            {
                blockerCounts.TryGetValue(cell, out int count);
                if (count <= 0 && cells.TryGetValue(cell, out WorldNavigationCell terrain) && terrain.Walkable)
                    invalidatesExistingPaths = true;

                blockerCounts[cell] = count + 1;
                RecordChangedCell(cell);
            }

            MarkRevisionChanged(invalidatesExistingPaths);
        }
        finally
        {
            EndBatchUpdate();
        }
    }

    public void UnregisterBlocker(int blockerId)
    {
        if (!blockerCells.TryGetValue(blockerId, out HashSet<Vector2Int> occupied))
            return;

        blockerCells.Remove(blockerId);
        foreach (Vector2Int cell in occupied)
        {
            if (!blockerCounts.TryGetValue(cell, out int count))
                continue;

            if (count <= 1)
                blockerCounts.Remove(cell);
            else
                blockerCounts[cell] = count - 1;

            RecordChangedCell(cell);
        }

        // Removing an obstacle only opens routes; it cannot invalidate a route already in use.
        MarkRevisionChanged(invalidatesExistingPaths: false);
    }

    public bool TryFindNearestWalkable(Vector2Int origin, int maxRadius, out Vector2Int result)
    {
        origin = NormalizeCell(origin);
        if (IsWalkable(origin))
        {
            result = origin;
            return true;
        }

        int radiusLimit = Mathf.Max(0, maxRadius);
        for (int radius = 1; radius <= radiusLimit; radius++)
        {
            int min = -radius;
            int max = radius;
            for (int x = min; x <= max; x++)
            {
                if (TryWalkableOffset(origin, x, min, out result) ||
                    TryWalkableOffset(origin, x, max, out result))
                {
                    return true;
                }
            }

            for (int y = min + 1; y < max; y++)
            {
                if (TryWalkableOffset(origin, min, y, out result) ||
                    TryWalkableOffset(origin, max, y, out result))
                {
                    return true;
                }
            }
        }

        result = default;
        return false;
    }

    public bool CanTraverse(Vector2Int from, Vector2Int to, out int traversalCost)
    {
        traversalCost = 0;
        from = NormalizeCell(from);
        to = NormalizeCell(to);
        Vector2Int delta = WorldTopologyRuntime.ShortestDelta(from, to);
        int dx = delta.x;
        int dy = delta.y;
        int absX = Mathf.Abs(dx);
        int absY = Mathf.Abs(dy);
        if (absX > 1 || absY > 1 || (absX == 0 && absY == 0))
            return false;

        if (!IsWalkable(from) ||
            !TryGetCell(to, out WorldNavigationCell destination) ||
            !destination.Walkable)
            return false;

        bool diagonal = absX == 1 && absY == 1;
        if (diagonal &&
            (!IsWalkable(NormalizeCell(new Vector2Int(from.x + dx, from.y))) ||
             !IsWalkable(NormalizeCell(new Vector2Int(from.x, from.y + dy)))))
        {
            return false;
        }

        int terrainCost = destination.Penalty >= 100000u
            ? 1000
            : (int)(destination.Penalty / 100u);
        traversalCost = (diagonal ? 14 : 10) + terrainCost;
        return true;
    }

    /// <summary>按导航网格的实际相邻移动规则计算一条直线路径的总代价。</summary>
    public bool TryCalculateLineTraversalCost(
        Vector2Int from,
        Vector2Int to,
        out int totalCost)
    {
        totalCost = 0;
        from = NormalizeCell(from);
        to = NormalizeCell(to);
        if (!IsWalkable(from) || !IsWalkable(to))
            return false;

        Vector2Int shortest = WorldTopologyRuntime.ShortestDelta(from, to);
        Vector2Int unwrappedTo = from + shortest;
        int x = from.x;
        int y = from.y;
        int dx = Mathf.Abs(shortest.x);
        int dy = Mathf.Abs(shortest.y);
        int stepX = shortest.x >= 0 ? 1 : -1;
        int stepY = shortest.y >= 0 ? 1 : -1;
        int error = dx - dy;

        while (x != unwrappedTo.x || y != unwrappedTo.y)
        {
            Vector2Int previous = NormalizeCell(new Vector2Int(x, y));
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

            Vector2Int next = NormalizeCell(new Vector2Int(x, y));
            if (!CanTraverse(previous, next, out int traversalCost))
                return false;

            totalCost += traversalCost;
        }

        return true;
    }

    /// <summary>
    /// Returns mutations accumulated since the navigation manager last synchronized. The
    /// sparse grid has one consumer, so draining avoids keeping an unbounded revision journal.
    /// </summary>
    public void ConsumeChanges(List<Vector2Int> result, out bool fullReset)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        result.Clear();
        fullReset = fullResetPending;
        if (!fullReset)
            result.AddRange(changedCells);

        changedCells.Clear();
        fullResetPending = false;
    }

    private void RecordChangedCell(Vector2Int position)
    {
        if (fullResetPending)
            return;

        changedCells.Add(position);
        if (changedCells.Count <= MaxPreciseChangedCells)
            return;

        // A very large streamed region is cheaper to treat as one conservative cache reset
        // than to compare every changed tile against every cached goal field.
        changedCells.Clear();
        fullResetPending = true;
    }

    private void MarkRevisionChanged(
        bool invalidatesExistingPaths = false,
        bool changesPathCost = false)
    {
        if (batchUpdateDepth > 0)
        {
            batchHasChanges = true;
            batchInvalidatesExistingPaths |= invalidatesExistingPaths;
            batchChangesPathCost |= changesPathCost;
        }
        else
        {
            Revision++;
            if (invalidatesExistingPaths)
                PathInvalidationRevision++;
            if (changesPathCost)
                PathCostRevision++;
        }
    }

    public bool HasLineOfSight(Vector2Int from, Vector2Int to)
        => HasLineOfSight(from, to, uint.MaxValue, enforcePenaltyLimit: false);

    /// <summary>检查仅允许指定最大代价地形的直线路径。</summary>
    public bool HasLineOfSight(Vector2Int from, Vector2Int to, uint maxPenalty)
        => HasLineOfSight(from, to, maxPenalty, enforcePenaltyLimit: true);

    public bool CanSmoothPathSegment(IReadOnlyList<Vector2Int> rawPath, int startIndex, int endIndex)
    {
        if (rawPath == null ||
            startIndex < 0 ||
            endIndex < startIndex ||
            endIndex >= rawPath.Count)
        {
            return false;
        }

        uint maxPathPenalty = 0u;
        for (int i = startIndex; i <= endIndex; i++)
        {
            if (!TryGetCell(rawPath[i], out WorldNavigationCell cell) || !cell.Walkable)
                return false;
            if (cell.Penalty > maxPathPenalty)
                maxPathPenalty = cell.Penalty;
        }

        return HasLineOfSight(
            rawPath[startIndex],
            rawPath[endIndex],
            maxPathPenalty,
            enforcePenaltyLimit: true);
    }

    private bool HasLineOfSight(
        Vector2Int from,
        Vector2Int to,
        uint maxPenalty,
        bool enforcePenaltyLimit)
    {
        if (!IsAllowedLineCell(from, maxPenalty, enforcePenaltyLimit) ||
            !IsAllowedLineCell(to, maxPenalty, enforcePenaltyLimit))
            return false;

        from = NormalizeCell(from);
        to = NormalizeCell(to);
        Vector2Int shortest = WorldTopologyRuntime.ShortestDelta(from, to);
        Vector2Int unwrappedTo = from + shortest;
        int x = from.x;
        int y = from.y;
        int dx = Mathf.Abs(shortest.x);
        int dy = Mathf.Abs(shortest.y);
        int stepX = shortest.x >= 0 ? 1 : -1;
        int stepY = shortest.y >= 0 ? 1 : -1;
        int error = dx - dy;

        while (x != unwrappedTo.x || y != unwrappedTo.y)
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

            if (x != previousX && y != previousY &&
                (!IsWalkable(NormalizeCell(new Vector2Int(x, previousY))) ||
                 !IsWalkable(NormalizeCell(new Vector2Int(previousX, y)))))
            {
                return false;
            }

            if (!IsAllowedLineCell(NormalizeCell(new Vector2Int(x, y)), maxPenalty, enforcePenaltyLimit))
                return false;
        }

        return true;
    }

    private bool IsAllowedLineCell(Vector2Int position, uint maxPenalty, bool enforcePenaltyLimit)
        => TryGetCell(position, out WorldNavigationCell cell) &&
           cell.Walkable &&
           (!enforcePenaltyLimit || cell.Penalty <= maxPenalty);

    /// <summary>Deterministic synchronous path query used by tests and tooling.</summary>
    public bool TryFindPath(
        Vector2Int start,
        Vector2Int goal,
        List<Vector2Int> result,
        int maxExpandedCells = 65536)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        result.Clear();
        start = NormalizeCell(start);
        goal = NormalizeCell(goal);
        if (!IsWalkable(start) || !IsWalkable(goal))
            return false;
        if (start == goal)
        {
            result.Add(start);
            return true;
        }

        MinHeap open = new();
        Dictionary<Vector2Int, int> costs = new(512) { [start] = 0 };
        Dictionary<Vector2Int, Vector2Int> previous = new(512);
        open.Push(start, Heuristic(start, goal), 0);

        int expanded = 0;
        while (open.TryPop(out HeapEntry entry) && expanded < Mathf.Max(1, maxExpandedCells))
        {
            if (!costs.TryGetValue(entry.Cell, out int knownCost) || knownCost != entry.Cost)
                continue;

            expanded++;
            if (entry.Cell == goal)
            {
                BuildAndSmoothPath(start, goal, previous, result);
                return result.Count > 0;
            }

            for (int i = 0; i < NeighbourOffsets.Length; i++)
            {
                Vector2Int neighbour = NormalizeCell(entry.Cell + NeighbourOffsets[i]);
                if (!CanTraverse(entry.Cell, neighbour, out int stepCost))
                    continue;

                int nextCost = knownCost + stepCost;
                if (costs.TryGetValue(neighbour, out int oldCost) && oldCost <= nextCost)
                    continue;

                costs[neighbour] = nextCost;
                previous[neighbour] = entry.Cell;
                int priority = nextCost + Heuristic(neighbour, goal);
                open.Push(neighbour, priority, nextCost);
            }
        }

        return false;
    }

    internal static IReadOnlyList<Vector2Int> GetNeighbourOffsets() => NeighbourOffsets;

    private bool TryWalkableOffset(Vector2Int origin, int x, int y, out Vector2Int result)
    {
        result = NormalizeCell(new Vector2Int(origin.x + x, origin.y + y));
        return IsWalkable(result);
    }

    private void BuildAndSmoothPath(
        Vector2Int start,
        Vector2Int goal,
        Dictionary<Vector2Int, Vector2Int> previous,
        List<Vector2Int> result)
    {
        List<Vector2Int> reversed = new() { goal };
        Vector2Int current = goal;
        int guard = previous.Count + 1;
        while (current != start && guard-- > 0)
        {
            if (!previous.TryGetValue(current, out current))
            {
                result.Clear();
                return;
            }
            reversed.Add(current);
        }

        reversed.Reverse();
        result.Add(reversed[0]);
        int anchor = 0;
        while (anchor < reversed.Count - 1)
        {
            int furthest = Mathf.Min(reversed.Count - 1, anchor + 16);
            while (furthest > anchor + 1 && !CanSmoothPathSegment(reversed, anchor, furthest))
                furthest--;

            result.Add(reversed[furthest]);
            anchor = furthest;
        }
    }

    private static int Heuristic(Vector2Int from, Vector2Int to)
    {
        Vector2Int delta = WorldTopologyRuntime.ShortestDelta(from, to);
        int dx = Mathf.Abs(delta.x);
        int dy = Mathf.Abs(delta.y);
        int diagonal = Mathf.Min(dx, dy);
        int straight = Mathf.Max(dx, dy) - diagonal;
        return diagonal * 14 + straight * 10;
    }

    internal readonly struct HeapEntry
    {
        public readonly Vector2Int Cell;
        public readonly int Priority;
        public readonly int Cost;

        public HeapEntry(Vector2Int cell, int priority, int cost)
        {
            Cell = cell;
            Priority = priority;
            Cost = cost;
        }
    }

    internal sealed class MinHeap
    {
        private readonly List<HeapEntry> entries = new(512);
        public int Count => entries.Count;

        public void Push(Vector2Int cell, int priority, int cost)
        {
            HeapEntry entry = new(cell, priority, cost);
            entries.Add(entry);
            int index = entries.Count - 1;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (ComesBefore(entries[parent], entry))
                    break;
                entries[index] = entries[parent];
                index = parent;
            }
            entries[index] = entry;
        }

        public bool TryPop(out HeapEntry result)
        {
            if (entries.Count == 0)
            {
                result = default;
                return false;
            }

            result = entries[0];
            int lastIndex = entries.Count - 1;
            HeapEntry tail = entries[lastIndex];
            entries.RemoveAt(lastIndex);
            if (entries.Count == 0)
                return true;

            int index = 0;
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= entries.Count)
                    break;

                int right = left + 1;
                int child = right < entries.Count && ComesBefore(entries[right], entries[left]) ? right : left;
                if (ComesBefore(tail, entries[child]))
                    break;

                entries[index] = entries[child];
                index = child;
            }
            entries[index] = tail;
            return true;
        }

        private static bool ComesBefore(HeapEntry a, HeapEntry b)
            => a.Priority < b.Priority ||
               (a.Priority == b.Priority && a.Cost < b.Cost) ||
                (a.Priority == b.Priority && a.Cost == b.Cost &&
                (a.Cell.x < b.Cell.x || (a.Cell.x == b.Cell.x && a.Cell.y < b.Cell.y)));
    }
}
