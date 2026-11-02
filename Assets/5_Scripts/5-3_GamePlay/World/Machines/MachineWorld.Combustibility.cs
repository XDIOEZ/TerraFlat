using System.Collections.Generic;
using UnityEngine;

public static partial class MachineWorld
{
    #region 数据机器可燃性来源
    private static void PublishMachineCombustibility(MachineEntity node)
    {
        if (node.Snapshot == null || GameRes.ExistingInstance?.TryGetItemDefinition(node.Snapshot.IDName, out var definition) != true ||
            !definition.FireFuel.IsCombustible)
        { RemoveMachineCombustibility(node); return; }
        var cells = new HashSet<Vector2Int>();
        foreach (var offset in definition.WorldGridOccupancy)
        {
            Vector2Int rotated = new(offset.X, offset.Y);
            for (int i = 0; i < (node.RotationQuarterTurns & 3); i++) rotated = new(-rotated.y, rotated.x);
            cells.Add(node.Cell + rotated);
        }
        if (cells.Count == 0) cells.Add(node.Cell);
        WorldCombustibilitySystem.ReplaceCombustibilitySource(node.ScopeKey ?? worldKey, node, cells,
            definition.FireFuel.Flammability, definition.FireFuel.BurnDurationSeconds);
    }

    private static void RemoveMachineCombustibility(MachineEntity node, bool preserveFuelConsumption = false)
        => WorldCombustibilitySystem.RemoveCombustibilitySource(node.ScopeKey ?? worldKey, node, preserveFuelConsumption);
    #endregion
}
