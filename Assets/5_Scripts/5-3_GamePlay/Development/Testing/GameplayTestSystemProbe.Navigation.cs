using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 独立导航网格
    private static void RegisterNavigationOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("navigation/weighted_path", NavigationPath);
        operations.Add("navigation/blockers", NavigationBlockers);
        operations.Add("navigation/grid_changes", NavigationChanges);
        operations.Add("navigation/corner", NavigationCorner);
    }

    private static WorldNavigationGrid NavigationFixture()
    {
        var grid = new WorldNavigationGrid();
        // 只构造本用例的稀疏网格，路径搜索仍调用正式导航算法。
        for (int y = 10; y <= 12; y++)
        for (int x = 10; x <= 12; x++) grid.SetCell(new Vector2Int(x, y), 1, true);
        return grid;
    }

    private static JObject NavigationPath(JObject args)
    {
        var grid = NavigationFixture();
        uint penalty = checked((uint)Number(args, "penalty", 1000d));
        var center = new Vector2Int(11, 11);
        grid.SetCell(center, penalty, !Flag(args, "blocked", false));
        var start = new Vector2Int(10, 11);
        var goal = new Vector2Int(12, 11);
        var path = new List<Vector2Int>();
        bool found = grid.TryFindPath(start, goal, path, checked((int)Number(args, "budget", 64d)));
        bool valid = found;
        int totalCost = 0;
        bool includesCenter = path.Contains(center);
        for (int i = 1; i < path.Count; i++)
        {
            // 正式路径会平滑成多格线段，按正式线段遍历接口读取实际代价。
            bool traversable = grid.TryCalculateLineTraversalCost(path[i - 1], path[i], out int cost);
            valid &= traversable; totalCost += cost;
            Vector2Int delta = path[i] - path[i - 1], offset = center - path[i - 1];
            int projection = offset.x * delta.x + offset.y * delta.y;
            includesCenter |= offset.x * delta.y == offset.y * delta.x &&
                projection >= 0 && projection <= delta.sqrMagnitude;
        }
        return new JObject { ["found"] = found, ["length"] = path.Count, ["valid"] = valid,
            ["includesCostlyCell"] = includesCenter, ["totalCost"] = totalCost,
            ["centerWalkable"] = grid.IsWalkable(center),
            ["endsAtTarget"] = found && path[path.Count - 1] == WorldNavigationGrid.NormalizeCell(goal),
            ["lineOfSight"] = grid.HasLineOfSight(start, goal) };
    }

    private static JObject NavigationBlockers(JObject args)
    {
        var grid = NavigationFixture();
        var center = new Vector2Int(11, 11);
        bool initially = grid.IsWalkable(center);
        grid.RegisterBlocker(1, new[] { center });
        grid.RegisterBlocker(2, new[] { center });
        bool both = grid.IsWalkable(center);
        grid.UnregisterBlocker(1);
        bool one = grid.IsWalkable(center);
        grid.UnregisterBlocker(2);
        bool released = grid.IsWalkable(center);
        grid.RegisterBlocker(1, new[] { center });
        grid.RegisterBlocker(1, new[] { new Vector2Int(12, 11) });
        return new JObject { ["initiallyWalkable"] = initially, ["bothWalkable"] = both,
            ["oneWalkable"] = one, ["releasedWalkable"] = released,
            ["oldFootprintReleased"] = grid.IsWalkable(center), ["newFootprintBlocked"] = !grid.IsWalkable(new Vector2Int(12, 11)) };
    }

    private static JObject NavigationChanges(JObject args)
    {
        var grid = new WorldNavigationGrid();
        int initial = grid.Revision;
        grid.BeginBatchUpdate();
        grid.SetCell(new Vector2Int(10, 10), 1, true);
        grid.SetCell(new Vector2Int(11, 10), 1, true);
        int during = grid.Revision;
        grid.EndBatchUpdate();
        int after = grid.Revision;
        int beforeRepeat = grid.Revision;
        grid.SetCell(new Vector2Int(10, 10), 1, true);
        var changed = new List<Vector2Int>();
        grid.ConsumeChanges(changed, out bool reset);
        int repeated = grid.Revision;
        int removed = grid.RemoveRegion(new RectInt(10, 10, 2, 1));
        grid.SetCell(new Vector2Int(10, 10), 1, true);
        grid.Clear();
        grid.ConsumeChanges(changed, out bool cleared);
        return new JObject { ["initialRevision"] = initial, ["duringRevision"] = during, ["afterRevision"] = after,
            ["beforeRepeat"] = beforeRepeat, ["afterRepeat"] = repeated,
            ["removed"] = removed, ["remaining"] = grid.CellCount, ["clearInvalidatesAll"] = cleared };
    }

    private static JObject NavigationCorner(JObject args)
    {
        var grid = NavigationFixture();
        var from = new Vector2Int(10, 10);
        var to = new Vector2Int(11, 11);
        bool open = grid.CanTraverse(from, to, out int openCost);
        grid.SetCell(new Vector2Int(11, 10), 0, false);
        bool closed = grid.CanTraverse(from, to, out _);
        grid.SetCell(new Vector2Int(11, 10), 1, true);
        grid.SetCell(new Vector2Int(10, 11), 0, false);
        bool otherClosed = grid.CanTraverse(from, to, out _);
        return new JObject { ["open"] = open, ["openCost"] = openCost, ["closed"] = closed, ["otherClosed"] = otherClosed,
            ["farTraversal"] = grid.CanTraverse(from, new Vector2Int(12, 12), out _),
            ["sameTraversal"] = grid.CanTraverse(from, from, out _) };
    }
    #endregion
}
