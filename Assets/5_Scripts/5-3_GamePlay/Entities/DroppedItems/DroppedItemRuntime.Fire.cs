using System.Collections.Generic;
using UnityEngine;

internal sealed partial class DroppedItemRuntime
{
    #region 掉落物可燃性来源
    private sealed class DroppedCombustibilitySource
    {
        internal Vector2Int Cell;
        internal RuntimeItemDefinition Definition;
        internal float Amount;
    }
    private readonly Dictionary<int, DroppedCombustibilitySource> combustibilitySources = new();

    private void PublishDroppedCombustibility(int id)
    {
        if (!payloads.TryGetValue(id, out var data) || simulation.TryGetFlight(id, out _) ||
            !GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out var definition) || !definition.FireFuel.IsCombustible)
        { RemoveDroppedCombustibility(id); return; }
        var body = simulation.Get(id);
        Vector2Int root = Vector2Int.FloorToInt(body.Position);
        if (!combustibilitySources.TryGetValue(id, out var source))
            combustibilitySources.Add(id, source = new DroppedCombustibilitySource());
        if (source.Cell == root && ReferenceEquals(source.Definition, definition) && source.Amount == body.Amount) return;
        var cells = new HashSet<Vector2Int>();
        foreach (var offset in definition.WorldGridOccupancy) cells.Add(root + new Vector2Int(offset.X, offset.Y));
        if (cells.Count == 0) cells.Add(root);
        source.Cell = root; source.Definition = definition; source.Amount = body.Amount;
        WorldCombustibilitySystem.ReplaceCombustibilitySource(matterWorldKey, source, cells,
            definition.FireFuel.Flammability, definition.FireFuel.BurnDurationSeconds * body.Amount);
    }

    private void RemoveDroppedCombustibility(int id)
    {
        if (combustibilitySources.Remove(id, out var source))
            WorldCombustibilitySystem.RemoveCombustibilitySource(matterWorldKey, source);
    }

    private void ClearDroppedCombustibility()
    {
        foreach (var source in combustibilitySources.Values)
            WorldCombustibilitySystem.RemoveCombustibilitySource(matterWorldKey, source, preserveFuelConsumption: true);
        combustibilitySources.Clear();
    }
    #endregion
}
