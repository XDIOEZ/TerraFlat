using FlatWorld.DroppedItems;
using UnityEngine;

internal sealed partial class DroppedItemRuntime
{
    #region 局部觅食空间查询

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
