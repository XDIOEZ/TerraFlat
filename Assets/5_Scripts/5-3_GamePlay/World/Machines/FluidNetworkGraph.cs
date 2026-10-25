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
    public readonly struct SupplyNode
    {
        public readonly MachineEntity Node;
        public readonly string Chamber;
        public readonly int DownstreamIndex;
        public readonly Edge Boundary;
        public SupplyNode(MachineEntity node, string chamber, int downstreamIndex, Edge boundary = null)
        { Node = node; Chamber = chamber; DownstreamIndex = downstreamIndex; Boundary = boundary; }
    }
    public sealed class SupplyWorkspace
    {
        public readonly List<SupplyNode> Nodes = new();
        public FluidPhase Phase { get; internal set; }
        internal readonly HashSet<(int, string)> Visited = new();
        internal readonly HashSet<FluidInventory> Inventories = new();
    }
    private readonly Dictionary<Vector2Int, MachineEntity> pipes = new();
    private readonly Dictionary<Vector2Int, List<MachineEntity>> devices = new();
    private readonly Dictionary<int, List<Edge>> incoming = new();
    private readonly Dictionary<int, int> outletDistance = new();
    private readonly Dictionary<int, List<Edge>> outgoing = new();
    public readonly List<Edge> GasEdges = new();
    public readonly List<Edge> LiquidEdges = new();
    private readonly Dictionary<(int, string), List<SupplyNode>> liquidComponents = new();
    private readonly Dictionary<int, int> connectionMasks = new(); // 连接外形只读取正式端口图，不依赖本轮流量。
    private static readonly int[] directionBits = { 2, 1, 8, 4 };
    public readonly List<Edge> Edges = new();
    public readonly List<Component> Components = new();
    public readonly Dictionary<int, Component> NodeComponents = new();
    private readonly Func<Vector2Int, Vector2Int> normalize;
    public FluidNetworkGraph(Func<Vector2Int, Vector2Int> normalize) => this.normalize = normalize;

    public MachineEntity PipeAt(Vector2Int cell) => pipes.TryGetValue(normalize(cell), out var node) ? node : null;
    public int GetConnectionMask(MachineEntity node)
        => node != null && connectionMasks.TryGetValue(node.Id, out int mask) ? mask : 0;

    // 按真实输入方向回溯供给，路径上每一个口都必须允许全部所需物质通过。
    public void CollectSupplyRoutes(MachineEntity receiver, string chamber, FluidPhase phase,
        IReadOnlyList<string> requiredFluidIds, SupplyWorkspace workspace)
    {
        workspace.Phase = phase;
        workspace.Nodes.Clear(); workspace.Visited.Clear();
        if (receiver?.Definition.Fluid == null || requiredFluidIds == null || requiredFluidIds.Count == 0 ||
            !CanSupply(receiver, phase)) return;
        workspace.Nodes.Add(new SupplyNode(receiver, chamber, -1));
        workspace.Visited.Add((receiver.Id, chamber));
        for (int index = 0; index < workspace.Nodes.Count; index++)
        {
            SupplyNode downstream = workspace.Nodes[index];
            // 储罐是唯一物料来源，搜索只穿过无库存的连接节点。
            if (index > 0 && !IsTransit(downstream.Node)) continue;
            if (!incoming.TryGetValue(downstream.Node.Id, out var inputs)) continue;
            foreach (Edge edge in inputs)
            {
                if (edge.TargetPort.Chamber != downstream.Chamber || !CanSupply(edge.Source, phase)) continue;
                FluidDeviceBehavior behavior = MachineWorld.GetFluidDeviceBehavior(edge.Source);
                if (!behavior.CanOutput(edge.Source, out _)) continue;
                bool allowed = true;
                foreach (string id in requiredFluidIds)
                    if (!Accepts(edge.SourcePort, phase, id) || !Accepts(edge.TargetPort, phase, id) ||
                        !behavior.CanRoute(edge.Source, edge.SourcePort, phase, id, out _))
                    { allowed = false; break; }
                if (!allowed || !workspace.Visited.Add((edge.Source.Id, edge.SourcePort.Chamber))) continue;
                workspace.Nodes.Add(new SupplyNode(edge.Source, edge.SourcePort.Chamber, index, edge));
            }
        }
    }

    // 正向仅寻找真实接收设备，经过的方向、相别和筛选规则全部生效。
    public void CollectDeliveryRoutes(MachineEntity source, string chamber, FluidPhase phase,
        IReadOnlyList<string> requiredFluidIds, SupplyWorkspace workspace)
    {
        workspace.Phase = phase; workspace.Nodes.Clear(); workspace.Visited.Clear();
        if (source?.Definition.Fluid == null || !CanSupply(source, phase)) return;
        workspace.Nodes.Add(new SupplyNode(source, chamber, -1)); workspace.Visited.Add((source.Id, chamber));
        for (int index = 0; index < workspace.Nodes.Count; index++)
        {
            SupplyNode upstream = workspace.Nodes[index];
            if (index > 0 && !IsTransit(upstream.Node)) continue;
            if (!outgoing.TryGetValue(upstream.Node.Id, out var edges)) continue;
            FluidDeviceBehavior behavior = MachineWorld.GetFluidDeviceBehavior(upstream.Node);
            if (!behavior.CanOutput(upstream.Node, out _)) continue;
            foreach (Edge edge in edges)
            {
                if (edge.Target == null || edge.SourcePort.Chamber != upstream.Chamber || !CanSupply(edge.Target, phase)) continue;
                bool allowed = true;
                foreach (string id in requiredFluidIds)
                    if (!Accepts(edge.SourcePort, phase, id) || !Accepts(edge.TargetPort, phase, id) ||
                        !behavior.CanRoute(upstream.Node, edge.SourcePort, phase, id, out _)) { allowed = false; break; }
                if (!allowed || !workspace.Visited.Add((edge.Target.Id, edge.TargetPort.Chamber))) continue;
                workspace.Nodes.Add(new SupplyNode(edge.Target, edge.TargetPort.Chamber, index, edge));
            }
        }
    }

    private static bool CanSupply(MachineEntity node, FluidPhase phase)
    {
        FluidMachineState state = MachineWorld.GetFluidState(node);
        return state != null && !state.Ruptured &&
            MachineWorld.GetFluidDeviceBehavior(node).PortsEnabled(node) &&
            (state.OutletPhase == "both" || state.OutletPhase == (phase == FluidPhase.Gas ? "gas" : "liquid"));
    }
    private static bool Accepts(FluidMachinePortDefinition port, FluidPhase phase, string fluidId)
        => (port.Phase == "both" || port.Phase == (phase == FluidPhase.Gas ? "gas" : "liquid")) &&
            (string.IsNullOrEmpty(port.FluidId) || string.Equals(port.FluidId, fluidId, StringComparison.OrdinalIgnoreCase));

    public void Rebuild(IEnumerable<MachineEntity> source)
    {
        pipes.Clear(); devices.Clear(); incoming.Clear(); outgoing.Clear(); GasEdges.Clear(); LiquidEdges.Clear(); outletDistance.Clear(); connectionMasks.Clear(); Edges.Clear(); Components.Clear(); NodeComponents.Clear();
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
            if (edge.SourcePort.Phase != "liquid" && (edge.TargetPort == null || edge.TargetPort.Phase != "liquid")) GasEdges.Add(edge);
            if (edge.SourcePort.Phase != "gas" && (edge.TargetPort == null || edge.TargetPort.Phase != "gas")) LiquidEdges.Add(edge);
            if (!outgoing.TryGetValue(edge.Source.Id, out var outputs)) outgoing[edge.Source.Id] = outputs = new List<Edge>();
            outputs.Add(edge);
            if (edge.Target == null) continue;
            connectionMasks[edge.Source.Id] = GetConnectionMask(edge.Source) | directionBits[edge.Direction];
            connectionMasks[edge.Target.Id] = GetConnectionMask(edge.Target) | directionBits[(edge.Direction + 2) & 3];
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
        BuildLiquidComponents();
    }

    public IReadOnlyList<SupplyNode> GetLiquidComponent(MachineEntity node, string chamber)
        => liquidComponents.TryGetValue((node.Id, chamber), out var members) ? members : null;

    // 连通成员只在拓扑变动时计算，按腔体分组，查询不反复遍历整张图。
    private void BuildLiquidComponents()
    {
        liquidComponents.Clear();
        var adjacent = new Dictionary<(int, string), List<SupplyNode>>();
        foreach (Edge edge in LiquidEdges)
        {
            if (edge.Target == null) continue;
            var source = (edge.Source.Id, edge.SourcePort.Chamber); var target = (edge.Target.Id, edge.TargetPort.Chamber);
            if (!adjacent.TryGetValue(source, out var a)) adjacent.Add(source, a = new());
            if (!adjacent.TryGetValue(target, out var b)) adjacent.Add(target, b = new());
            a.Add(new SupplyNode(edge.Target, edge.TargetPort.Chamber, -1));
            b.Add(new SupplyNode(edge.Source, edge.SourcePort.Chamber, -1));
        }
        var queue = new Queue<SupplyNode>();
        foreach (var pair in adjacent)
        {
            if (liquidComponents.ContainsKey(pair.Key)) continue;
            var members = new List<SupplyNode>();
            SupplyNode root = pair.Value[0];
            // 任一邻居会反向带回根节点，避免另建节点查找表。
            queue.Enqueue(root); liquidComponents[(root.Node.Id, root.Chamber)] = members;
            while (queue.Count > 0)
            {
                SupplyNode node = queue.Dequeue(); members.Add(node);
                if (!adjacent.TryGetValue((node.Node.Id, node.Chamber), out var neighbors)) continue;
                foreach (SupplyNode next in neighbors)
                    if (liquidComponents.TryAdd((next.Node.Id, next.Chamber), members)) queue.Enqueue(next);
            }
        }
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
