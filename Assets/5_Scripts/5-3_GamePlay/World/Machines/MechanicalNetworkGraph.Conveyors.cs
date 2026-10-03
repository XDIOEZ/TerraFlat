using System.Collections.Generic;
using UnityEngine;

public sealed partial class MachineEntity
{
    #region 输送带派生状态
    public ConveyorPath Conveyor, PreviousConveyor;
    public float ConveyorVisualPhase, ConveyorVisualSpeed, ConveyorVisualTime;
    public ConveyorPath ConveyorRoute => Conveyor.Valid ? Conveyor : ConveyorPath.Straight(RotationQuarterTurns);
    #endregion
}

public sealed partial class MechanicalNetworkGraph
{
    #region 输送带拓扑
    public readonly List<MachineEntity> ConveyorChanges = new();
    private readonly HashSet<MachineEntity> conveyorVisited = new();
    private readonly Queue<MachineEntity> conveyorQueue = new();

    /// <summary>只有互相选中的带端才连接，平行带不会从侧面串成输送线路。</summary>
    private void RebuildConveyorPaths(List<MachineEntity> sorted)
    {
        ConveyorChanges.Clear(); conveyorVisited.Clear(); conveyorQueue.Clear();
        foreach (MachineEntity node in sorted)
        {
            if (node.Definition.Transport == null) continue;
            node.PreviousConveyor = node.Conveyor;
            node.Conveyor = SelectConveyorPath(node.Cell, node.RotationQuarterTurns, node.Definition.Transport.AutoConnect);
        }
        // 从线路端点开始统一方向，闭合线路再使用稳定节点顺序选起点。
        for (int pass = 0; pass < 2; pass++)
        foreach (MachineEntity root in sorted)
        {
            if (root.Definition.Transport == null || conveyorVisited.Contains(root) ||
                pass == 0 && CountConveyorLinks(root) == 2) continue;
            conveyorVisited.Add(root); conveyorQueue.Enqueue(root);
            while (conveyorQueue.Count > 0)
            {
                MachineEntity node = conveyorQueue.Dequeue();
                for (int direction = 0; direction < 4; direction++)
                {
                    MachineEntity next = ConveyorNeighbor(node, direction);
                    if (next == null || !conveyorVisited.Add(next)) continue;
                    int opposite = (direction + 2) & 3;
                    int other = next.Conveyor.Input == opposite ? next.Conveyor.Output : next.Conveyor.Input;
                    if (next.Definition.Transport.AutoConnect)
                        next.Conveyor = node.Conveyor.Output == direction
                            ? new ConveyorPath(opposite, other) : new ConveyorPath(other, opposite);
                    conveyorQueue.Enqueue(next);
                }
            }
        }
        foreach (MachineEntity node in sorted)
            if (node.Definition.Transport != null && !node.Conveyor.Same(node.PreviousConveyor)) ConveyorChanges.Add(node);
    }

    private int CountConveyorLinks(MachineEntity node)
    {
        int count = 0;
        for (int direction = 0; direction < 4; direction++) if (ConveyorNeighbor(node, direction) != null) count++;
        return count;
    }

    private MachineEntity ConveyorNeighbor(MachineEntity node, int direction)
    {
        if (!node.Conveyor.HasPathPort(direction)) return null;
        MachineEntity next = At(node.Cell + Directions[direction], node.Definition.Layer);
        return next?.Definition.Transport != null && next.Conveyor.HasPathPort((direction + 2) & 3) ? next : null;
    }

    /// <summary>放置预览查询数据邻格，既不加载 Chunk，也不修改现有带形。</summary>
    public ConveyorPath SelectConveyorPath(Vector2Int cell, int rotation, bool autoConnect)
    {
        if (!autoConnect) return ConveyorPath.Straight(rotation);
        int neighbors = 0;
        for (int direction = 0; direction < 4; direction++)
            if (At(cell + Directions[direction], 0)?.Definition.Transport != null) neighbors |= 1 << direction;
        return ConveyorPath.Select(neighbors, rotation);
    }
    #endregion
}
