using System;
using UnityEngine;

public static partial class MachineWorld
{
    #region 工作台共享输入
    public static bool CanReachSharedCraftingInput(MachineEntity source, Func<MachineEntity, bool> canUseWorkbench)
    {
        using var world = UseNodeScope(source);
        if (source?.Definition.ProvidesWorkbenchInput != true || !Contains(source)) return false;
        RebuildGraphsIfDirty();
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0) continue;
                for (int layer = 0; layer <= graph.HighestOccupiedLayer; layer++)
                {
                    MachineEntity candidate = graph.At(source.Cell + new Vector2Int(x, y), layer);
                    if (candidate?.Logic is WorkbenchLogic workbench &&
                        ReferenceEquals(workbench.SharedInput?.MachineOwner, source) && canUseWorkbench(candidate)) return true;
                }
            }
        return false;
    }

    public static bool IsAdjacentCraftingCell(MachineEntity workbench, Vector2Int changedCell)
    {
        using var world = UseNodeScope(workbench);
        if (graph == null) return false;
        Vector2Int normalized = graph.NormalizeCell(changedCell);
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                if ((x != 0 || y != 0) && graph.NormalizeCell(workbench.Cell + new Vector2Int(x, y)) == normalized)
                    return true;
        return normalized == graph.NormalizeCell(workbench.Cell);
    }

    public static MachineEntity FindAdjacentCraftingInput(MachineEntity workbench, int preferredId = 0)
    {
        using var world = UseNodeScope(workbench);
        EnsureScope();
        if (!Contains(workbench)) return null;
        RebuildGraphsIfDirty();
        return FindAdjacentCraftingInput(graph, workbench, preferredId);
    }

    internal static MachineEntity FindAdjacentCraftingInput(MechanicalNetworkGraph graph, MachineEntity workbench, int preferredId = 0)
    {
        MachineEntity selected = null;
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0) continue;
                Vector2Int cell = workbench.Cell + new Vector2Int(x, y);
                for (int layer = 0; layer <= graph.HighestOccupiedLayer; layer++)
                {
                    MachineEntity candidate = graph.At(cell, layer);
                    if (candidate == null || ReferenceEquals(candidate, workbench) || !candidate.Definition.ProvidesWorkbenchInput) continue;
                    if (candidate.Id == preferredId) return candidate;
                    if (selected == null || candidate.Id < selected.Id) selected = candidate;
                }
            }
        return selected;
    }
    #endregion
}
