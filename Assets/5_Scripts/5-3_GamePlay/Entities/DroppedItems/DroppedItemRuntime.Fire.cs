using FlatWorld.WorldModel;
using System.Collections.Generic;
using UnityEngine;

internal sealed partial class DroppedItemRuntime
{
    #region 地面燃料索引
    private readonly HashSet<int> fireFuelVisited = new();
    internal void CollectWorldFireFuel(WorldFireSimulationContext context)
    {
        fireFuelVisited.Clear();
        foreach (var cell in context.Indices.Keys)
        {
            Vector2Int bucketCell = new(Mathf.FloorToInt(cell.x / SpatialCellSize), Mathf.FloorToInt(cell.y / SpatialCellSize));
            if (!spatial.TryGetValue(bucketCell, out var bucket)) continue;
            foreach (int id in bucket)
            {
                if (!fireFuelVisited.Add(id) || !simulation.Contains(id) || simulation.TryGetFlight(id, out _) ||
                    !payloads.TryGetValue(id, out ItemData data) ||
                    !GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition) ||
                    !definition.FireFuel.IsCombustible) continue;
                var body = simulation.Get(id);
                if (body.Amount > 0f) context.AddFuel(body.Position, definition.WorldGridOccupancy, definition.FireFuel.Flammability);
            }
        }
    }
    #endregion
}

public static partial class DroppedItemService
{
    #region 世界火场燃料桥
    internal static void CollectWorldFireFuel(WorldFireSimulationContext context)
    {
        if (worlds.TryGetValue(context.WorldKey, out var world)) world.Runtime?.CollectWorldFireFuel(context);
    }
    #endregion
}
