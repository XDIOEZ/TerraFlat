using System;
using System.Collections.Generic;
using FlatWorld.Navigation;
using Unity.Mathematics;
using UnityEngine;

/// <summary>长距离旧请求借用共享出口图，按需把局部流场方向还原为原有 Waypoints 契约。</summary>
public sealed partial class WorldNavigationManager
{
    #region Chunk Portal 路径适配
    private const int PortalPathDistance = 48;
    private const int MaximumCachedPortalPathGoals = 12;

    private FlowGoalHandle GetPortalPathGoal(FlowNavigationCache cache,
        Vector2Int goalCell, Vector2 position)
    {
        float2 target = new(position.x, position.y);
        if (portalPathGoals.TryGetValue(goalCell, out FlowGoalHandle cached))
        {
            if (cache.IsValid(cached))
            {
                cache.UpdateGoal(cached, target);
                return cached;
            }
            portalPathGoals.Remove(goalCell);
        }
        while (portalPathGoals.Count >= MaximumCachedPortalPathGoals && portalPathGoalOrder.Count > 0)
        {
            Vector2Int oldest = portalPathGoalOrder.Dequeue();
            if (!portalPathGoals.Remove(oldest, out FlowGoalHandle oldHandle)) continue;
            cache.RemoveGoal(oldHandle);
        }
        FlowGoalHandle handle = cache.CreateGoal(target);
        portalPathGoals[goalCell] = handle;
        portalPathGoalOrder.Enqueue(goalCell);
        return handle;
    }

    private bool TryBuildPortalPath(PathRequest request, Vector2Int requestedGoal,
        out Vector2[] waypoints, out Vector2 resolvedDestination,
        out bool reachesDestination, out int totalCost)
    {
        waypoints = null;
        resolvedDestination = request.GoalCell == requestedGoal
            ? WorldTopologyRuntime.NormalizePosition(request.Destination)
            : WorldNavigationGrid.CellCenter(request.GoalCell);
        reachesDestination = false;
        totalCost = 0;
        Vector2Int delta = WorldTopologyRuntime.ShortestDelta(request.StartCell, request.GoalCell);
        if (Mathf.Max(Mathf.Abs(delta.x), Mathf.Abs(delta.y)) < PortalPathDistance)
            return false;

        FlowNavigationCache cache = GetSharedNavigation();
        FlowGoalHandle handle;
        try
        {
            handle = GetPortalPathGoal(cache, request.GoalCell, resolvedDestination);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        {
            FlowNavigationSnapshot snapshot = cache.Read();
            int limit = Mathf.Max(2, maxPathCellsPerResult);
            var raw = new List<Vector2Int>(Mathf.Min(limit, 256)) { request.StartCell };
            var seen = new HashSet<Vector2Int> { request.StartCell };
            Vector2Int current = request.StartCell;
            Vector2 position = WorldNavigationGrid.CellCenter(current);
            while (raw.Count < limit)
            {
                FlowSample sample = snapshot.Sample(new float2(position.x, position.y), handle, 0.01f);
                if (sample.Status == FlowSampleStatus.Arrived)
                {
                    reachesDestination = true;
                    break;
                }
                if (sample.Status != FlowSampleStatus.Moving ||
                    !math.all(math.isfinite(sample.Delta)))
                    return false;

                Vector2 next = WorldTopologyRuntime.NormalizePosition(new Vector2(
                    position.x + sample.Delta.x, position.y + sample.Delta.y));
                Vector2Int nextCell = WorldNavigationGrid.WorldToCell(next);
                if (nextCell == current)
                {
                    if (current != request.GoalCell) return false;
                    reachesDestination = true;
                    break;
                }
                if (!seen.Add(nextCell) || !grid.CanTraverse(current, nextCell, out int stepCost))
                    return false;
                totalCost = totalCost > int.MaxValue - stepCost
                    ? int.MaxValue : totalCost + stepCost;
                raw.Add(nextCell);
                current = nextCell;
                position = WorldNavigationGrid.CellCenter(current);
                if (current == request.GoalCell)
                {
                    reachesDestination = true;
                    break;
                }
            }

            if (raw.Count < 2 && !reachesDestination) return false;
            var simplified = new List<Vector2>(raw.Count);
            if (request.StartCell != WorldNavigationGrid.WorldToCell(request.Start))
                simplified.Add(WorldNavigationGrid.CellCenter(request.StartCell));
            int anchor = 0;
            while (anchor < raw.Count - 1)
            {
                int furthest = Mathf.Min(raw.Count - 1, anchor + 16);
                while (furthest > anchor + 1 && !grid.CanSmoothPathSegment(raw, anchor, furthest))
                    furthest--;
                simplified.Add(WorldNavigationGrid.CellCenter(raw[furthest]));
                anchor = furthest;
            }
            Vector2 end = reachesDestination ? resolvedDestination : WorldNavigationGrid.CellCenter(raw[^1]);
            if (simplified.Count == 0 || WorldTopologyRuntime.SqrDistance(simplified[^1], end) > 0.0001f)
                simplified.Add(end);
            else
                simplified[^1] = end;
            waypoints = simplified.ToArray();
            return true;
        }
    }
    #endregion
}
