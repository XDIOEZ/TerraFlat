using System;
using System.Collections.Generic;
using FlatWorld.DroppedItems;
using UnityEngine;

internal sealed partial class DroppedItemRuntime
{
    #region 局部觅食空间查询

    private readonly HashSet<int> observationQueryDedupe = new();

    /// <summary>复用掉落物空间桶采集附近 ECS 实体的只读观察快照，飞行中的掉落也会被返回。</summary>
    public void QueryNearbyObservations(
        Vector2 origin,
        float radius,
        List<DroppedItemObservation> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        results.Clear();
        observationQueryDedupe.Clear();
        float radiusSquared = radius * radius;
        Vector2Int min = SpatialCell(origin - Vector2.one * radius);
        Vector2Int max = SpatialCell(origin + Vector2.one * radius);

        try
        {
            for (int y = min.y; y <= max.y; y++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    Vector2 point = domain.Normalize(new Unity.Mathematics.float2(
                        (x + 0.5f) * SpatialCellSize,
                        (y + 0.5f) * SpatialCellSize));
                    if (!spatial.TryGetValue(SpatialCell(point), out HashSet<int> bucket))
                        continue;

                    foreach (int id in bucket)
                    {
                        if (!observationQueryDedupe.Add(id) ||
                            !simulation.Contains(id) ||
                            !payloads.TryGetValue(id, out ItemData payload))
                        {
                            continue;
                        }

                        DroppedBody body = simulation.Get(id);
                        Vector2 nearestPosition = domain.NearestImagePosition(origin, body.Position);
                        if ((nearestPosition - origin).sqrMagnitude > radiusSquared)
                            continue;

                        string[] tags = payload.Tags == null || payload.Tags.Count == 0
                            ? Array.Empty<string>()
                            : payload.Tags.GetRange(0, Mathf.Min(8, payload.Tags.Count)).ToArray();
                        results.Add(new DroppedItemObservation(
                            id,
                            payload.IDName,
                            payload.GameName,
                            body.Position,
                            body.Amount,
                            body.Pickable != 0 && body.Amount >= 1f,
                            tags));
                    }
                }
            }
        }
        finally
        {
            observationQueryDedupe.Clear();
        }
    }

    public bool TryFindNearestTagged(Vector2 origin, float radius, string tag, out int nearestId)
    {
        nearestId = 0;
        float nearestDistance = radius * radius;
        Vector2Int min = SpatialCell(origin - Vector2.one * radius), max = SpatialCell(origin + Vector2.one * radius);
        for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
            {
                Vector2 point = domain.Normalize(new Unity.Mathematics.float2((x + 0.5f) * SpatialCellSize, (y + 0.5f) * SpatialCellSize));
                if (!spatial.TryGetValue(SpatialCell(point), out var bucket)) continue;
                foreach (int id in bucket)
                {
                    DroppedBody body = simulation.Get(id);
                    if (body.Pickable == 0 || body.Amount < 1f || payloads[id].Tags?.Contains(tag) != true) continue;
                    Vector2 nearestPosition = domain.NearestImagePosition(origin, body.Position);
                    float distance = (nearestPosition - origin).sqrMagnitude;
                    if (distance > nearestDistance || (distance == nearestDistance && nearestId != 0 && id > nearestId)) continue;
                    nearestDistance = distance; nearestId = id;
                }
            }
        return nearestId != 0;
    }

    public bool TryGetPickablePosition(int id, out Vector2 position)
    {
        position = default;
        if (!simulation.Contains(id)) return false;
        DroppedBody body = simulation.Get(id);
        if (body.Pickable == 0 || body.Amount < 1f) return false;
        position = body.Position;
        return true;
    }

    public ItemData Snapshot(int id)
    {
        ItemData result = FastCloner.FastCloner.DeepClone(payloads[id]);
        result.Stack.Amount = simulation.Get(id).Amount;
        return result;
    }

    public bool TryConsumeTagged(int id, string tag, int amount)
    {
        if (!simulation.Contains(id) || !pickupReservations.Add(id)) return false;
        try
        {
            DroppedBody body = simulation.Get(id);
            if (body.Pickable == 0 || body.Amount < amount || payloads[id].Tags?.Contains(tag) != true) return false;
            body.Amount -= amount;
            if (body.Amount <= 0f) Remove(id); else simulation.Set(body);
            return true;
        }
        finally { pickupReservations.Remove(id); }
    }

    #endregion
}
