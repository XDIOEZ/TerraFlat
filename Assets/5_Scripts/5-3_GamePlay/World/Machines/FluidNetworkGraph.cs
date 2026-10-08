using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>管道独立覆盖层只负责端口连通及由来源到出气口的路由，压力不参与流向。</summary>
public sealed class FluidNetworkGraph
{
    #region 端口拓扑
    public sealed class Edge
    {
        public MachineEntity Source;
        public MachineEntity Target;
        public FluidMachinePortDefinition SourcePort;
        public FluidMachinePortDefinition TargetPort;
        public int Direction;
        public bool Environment;
        public string Id => Source.Id + ":" + SourcePort.Id;
    }
    public sealed class Component
    {
        public readonly List<MachineEntity> Nodes = new();
        public bool Active;
        public float AwaySeconds;
    }
    private readonly Dictionary<Vector2Int, MachineEntity> pipes = new();
    private readonly Dictionary<Vector2Int, List<MachineEntity>> devices = new();
    private readonly Dictionary<int, List<Edge>> incoming = new();
    private readonly Dictionary<int, int> outletDistance = new();
    public readonly List<Edge> Edges = new();
    public readonly List<Component> Components = new();
    public readonly Dictionary<int, Component> NodeComponents = new();
    private readonly Func<Vector2Int, Vector2Int> normalize;
    public FluidNetworkGraph(Func<Vector2Int, Vector2Int> normalize) => this.normalize = normalize;

    public MachineEntity PipeAt(Vector2Int cell) => pipes.TryGetValue(normalize(cell), out var node) ? node : null;

    public void Rebuild(IEnumerable<MachineEntity> source)
    {
        pipes.Clear(); devices.Clear(); incoming.Clear(); outletDistance.Clear(); Edges.Clear(); Components.Clear(); NodeComponents.Clear();
        var sorted = new List<MachineEntity>();
        foreach (MachineEntity node in source)
        {
            if (node.Definition.Fluid == null) continue;
            sorted.Add(node);
            if (node.Definition.Layer == 2 && node.Definition.Fluid.Ports.Length > 0) pipes.Add(normalize(node.Cell), node);
            else
            {
                Vector2Int cell = normalize(node.Cell);
                if (!devices.TryGetValue(cell, out var list)) devices[cell] = list = new List<MachineEntity>();
                list.Add(node);
            }
        }
        sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
        foreach (MachineEntity node in sorted)
        {
            FluidMachineState state = MachineWorld.GetFluidState(node);
            if (state.Ruptured || !MachineWorld.GetFluidDeviceBehavior(node).PortsEnabled(node)) continue;
            foreach (FluidMachinePortDefinition port in node.Definition.Fluid.Ports)
            {
                if (port.Mode == "input") continue;
                int direction = (port.Direction + node.RotationQuarterTurns) & 3;
                Vector2Int adjacent = normalize(node.Cell + MechanicalNetworkGraph.Directions[direction]);
                bool hasTarget = false;
                bool hasPhysicalTarget = pipes.ContainsKey(adjacent) || devices.TryGetValue(adjacent, out var physicalTargets) &&
                    physicalTargets.Exists(candidate => candidate.Definition.Fluid.Ports.Length > 0);
                if (pipes.TryGetValue(adjacent, out MachineEntity pipe)) hasTarget |= Connect(node, port, pipe, direction);
                if (devices.TryGetValue(adjacent, out var targets))
                    foreach (MachineEntity target in targets) hasTarget |= Connect(node, port, target, direction);
                if (!hasTarget && !hasPhysicalTarget && port.Outlet && MachineWorld.GetFluidDeviceBehavior(node).EmitsToEnvironment)
                    Edges.Add(new Edge { Source = node, SourcePort = port, Direction = direction, Environment = true });
            }
        }
        foreach (Edge edge in Edges)
        {
            if (edge.Target == null) continue;
            if (!incoming.TryGetValue(edge.Target.Id, out var list)) incoming[edge.Target.Id] = list = new List<Edge>();
            list.Add(edge);
        }
        var queue = new Queue<int>();
        foreach (Edge edge in Edges)
        {
            // 每个设备的正式输出端也是出气口；容器只作为接收终点，不给普通裸管赋予排放资格。
            if (!edge.Environment && !(edge.SourcePort.Outlet && edge.Target != null && !IsTransit(edge.Target))) continue;
            if (!outletDistance.ContainsKey(edge.Source.Id)) { outletDistance.Add(edge.Source.Id, 0); queue.Enqueue(edge.Source.Id); }
        }
        while (queue.Count > 0)
        {
            int target = queue.Dequeue();
            if (!incoming.TryGetValue(target, out var inputs)) continue;
            foreach (Edge edge in inputs)
            {
                if (outletDistance.ContainsKey(edge.Source.Id)) continue;
                outletDistance.Add(edge.Source.Id, outletDistance[target] + 1);
                queue.Enqueue(edge.Source.Id);
            }
        }
        BuildComponents(sorted);
    }

    private bool Connect(MachineEntity source, FluidMachinePortDefinition output, MachineEntity target, int direction)
    {
        FluidMachineState state = MachineWorld.GetFluidState(target);
        if (state.Ruptured || !MachineWorld.GetFluidDeviceBehavior(target).PortsEnabled(target)) return false;
        bool connected = false;
        foreach (FluidMachinePortDefinition input in target.Definition.Fluid.Ports)
        {
            if (input.Mode == "output" || ((input.Direction + target.RotationQuarterTurns) & 3) != ((direction + 2) & 3)) continue;
            if (output.Phase != "both" && input.Phase != "both" && output.Phase != input.Phase) continue;
            Edges.Add(new Edge { Source = source, Target = target, SourcePort = output, TargetPort = input, Direction = direction });
            connected = true;
        }
        return connected;
    }

    public bool CanRoute(Edge edge)
    {
        if (edge.Environment || edge.SourcePort.Outlet) return true;
        if (!outletDistance.TryGetValue(edge.Source.Id, out int sourceDistance)) return false;
        if (edge.Target == null || !outletDistance.TryGetValue(edge.Target.Id, out int targetDistance))
            return edge.Target != null && !IsTransit(edge.Target) && sourceDistance == 0 && edge.SourcePort.Outlet;
        return targetDistance < sourceDistance;
    }

    private static bool IsTransit(MachineEntity node)
        => MachineWorld.GetFluidDeviceBehavior(node).IsTransit;

    private void BuildComponents(List<MachineEntity> sorted)
    {
        var neighbors = new Dictionary<int, List<MachineEntity>>();
        foreach (Edge edge in Edges)
        {
            if (edge.Target == null) continue;
            if (!neighbors.TryGetValue(edge.Source.Id, out var a)) neighbors[edge.Source.Id] = a = new();
            if (!neighbors.TryGetValue(edge.Target.Id, out var b)) neighbors[edge.Target.Id] = b = new();
            a.Add(edge.Target); b.Add(edge.Source);
        }
        foreach (MachineEntity node in sorted)
        {
            FluidDeviceBehavior behavior = MachineWorld.GetFluidDeviceBehavior(node);
            if (behavior.SwitchesMechanicalPorts || behavior.SwitchesElectricalPorts)
            {
                MachineEntity pipe = PipeAt(node.Cell);
                if (pipe != null) LinkComponent(neighbors, node, pipe);
            }
            if (!node.Definition.Fluid.CombineAdjacent || !behavior.IsSharedStorage) continue;
            foreach (Vector2Int direction in MechanicalNetworkGraph.Directions)
                if (devices.TryGetValue(normalize(node.Cell + direction), out var adjacent))
                    foreach (MachineEntity neighbor in adjacent)
                        if (neighbor.Definition.Fluid.CombineAdjacent && MachineWorld.GetFluidDeviceBehavior(neighbor).IsSharedStorage && neighbor.Definition.Fluid.MaterialId == node.Definition.Fluid.MaterialId)
                            LinkComponent(neighbors, node, neighbor);
        }
        var queue = new Queue<MachineEntity>();
        foreach (MachineEntity root in sorted)
        {
            if (NodeComponents.ContainsKey(root.Id)) continue;
            var component = new Component(); Components.Add(component); NodeComponents.Add(root.Id, component); queue.Enqueue(root);
            while (queue.Count > 0)
            {
                MachineEntity node = queue.Dequeue(); component.Nodes.Add(node); component.Active |= node.Active;
                if (!neighbors.TryGetValue(node.Id, out var list)) continue;
                foreach (MachineEntity neighbor in list)
                {
                    if (NodeComponents.ContainsKey(neighbor.Id)) continue;
                    NodeComponents.Add(neighbor.Id, component); queue.Enqueue(neighbor);
                }
            }
        }
    }
    private static void LinkComponent(Dictionary<int, List<MachineEntity>> neighbors, MachineEntity a, MachineEntity b)
    {
        if (!neighbors.TryGetValue(a.Id, out var first)) neighbors[a.Id] = first = new();
        if (!neighbors.TryGetValue(b.Id, out var second)) neighbors[b.Id] = second = new();
        first.Add(b); second.Add(a);
    }
    #endregion
}
