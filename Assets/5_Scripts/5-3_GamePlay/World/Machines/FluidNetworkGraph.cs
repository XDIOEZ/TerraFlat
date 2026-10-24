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
            // 气网在相邻端口已经给出唯一总库存；不要再沿普通管段走到另一台设备去越级取料。
            if (phase == FluidPhase.Gas && index > 0 && MachineWorld.IsSharedGasNetworkMember(downstream.Node)) continue;
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

    private static bool CanSupply(MachineEntity node, FluidPhase phase)
    {
        FluidMachineState state = MachineWorld.GetFluidState(node);
        return state != null && !state.Ruptured && string.IsNullOrEmpty(state.RuptureBudgetId) &&
            MachineWorld.GetFluidDeviceBehavior(node).PortsEnabled(node) &&
            (state.OutletPhase == "both" || state.OutletPhase == (phase == FluidPhase.Gas ? "gas" : "liquid"));
    }
    private static bool Accepts(FluidMachinePortDefinition port, FluidPhase phase, string fluidId)
        => (port.Phase == "both" || port.Phase == (phase == FluidPhase.Gas ? "gas" : "liquid")) &&
            (string.IsNullOrEmpty(port.FluidId) || string.Equals(port.FluidId, fluidId, StringComparison.OrdinalIgnoreCase));

    public void Rebuild(IEnumerable<MachineEntity> source)
    {
        pipes.Clear(); devices.Clear(); incoming.Clear(); outletDistance.Clear(); connectionMasks.Clear(); Edges.Clear(); Components.Clear(); NodeComponents.Clear();
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

/// <summary>气相只在拓扑改变时归并普通管段，液相依旧保留每段独立库存。</summary>
internal sealed class IndustrialGasNetworkGraph
{
    #region 气网分组与边界
    internal sealed class Group
    {
        public readonly List<MachineEntity> Members = new();
        public readonly FluidInventory Inventory = new();
        public readonly List<FluidLiquidPhaseContact> PhaseContacts = new();
        public double MaximumPhaseMolesPerSecond;
        private readonly Dictionary<int, double> liquidLiters = new();
        public double TotalVolumeLiters;
        public double MinimumGasSpaceLiters;
        public double ThroughputLitersPerSecond;
        public long Epoch;
        private long snapshotSequence;
        public double OccupiedLiquidLiters { get; private set; }
        public double GasSpaceLiters => Math.Max(MinimumGasSpaceLiters, TotalVolumeLiters - OccupiedLiquidLiters);
        public int OwnerId => Members[0].Id;

        public double GetMemberGasSpaceLiters(MachineEntity node)
            => Math.Max(node.Definition.Fluid.MinimumGasSpaceLiters,
                node.Definition.Fluid.VolumeLiters - liquidLiters.GetValueOrDefault(node.Id));

        public FluidGasNetworkSnapshot CaptureSnapshot()
        {
            var snapshot = new FluidGasNetworkSnapshot
            {
                Epoch = Epoch, Sequence = checked(++snapshotSequence), OwnerId = OwnerId,
                VolumeLiters = TotalVolumeLiters, MinimumGasSpaceLiters = MinimumGasSpaceLiters,
                OccupiedLiquidLiters = OccupiedLiquidLiters, Inventory = Inventory.State.Clone()
            };
            foreach (MachineEntity member in Members) snapshot.MemberIds.Add(member.Id);
            snapshot.Validate(Inventory.Catalog);
            return snapshot;
        }

        public void UpdateLiquid(MachineEntity node, FluidInventory local)
        {
            double next = local.GetLiquidLiters();
            liquidLiters.TryGetValue(node.Id, out double previous);
            liquidLiters[node.Id] = next;
            OccupiedLiquidLiters = Math.Max(0d, OccupiedLiquidLiters + next - previous);
        }
    }

    private readonly Dictionary<int, Group> nodeGroups = new();
    public readonly List<Group> Groups = new();
    public readonly List<FluidNetworkGraph.Edge> Edges = new();

    public bool TryGetGroup(MachineEntity node, out Group group)
    { group = null; return node != null && nodeGroups.TryGetValue(node.Id, out group); }

    private static bool AcceptsGas(FluidMachinePortDefinition port)
        => port != null && (port.Phase == "both" || port.Phase == "gas");

    private static bool IsPassiveGasPipe(MachineEntity node)
    {
        FluidMachineDefinition definition = node?.Definition.Fluid;
        if (definition == null || !MachineWorld.GetFluidDeviceBehavior(node).IsTransit ||
            MachineWorld.GetFluidDeviceBehavior(node).EmitsToEnvironment ||
            definition.Kind == "selector" || definition.Ports.Length == 0 ||
            MachineWorld.GetFluidState(node).OutletPhase == "liquid") return false;
        foreach (FluidMachinePortDefinition port in definition.Ports)
            if (port.Chamber != "main" || port.Mode != "both" || port.Outlet ||
                !AcceptsGas(port) || !string.IsNullOrEmpty(port.FluidId)) return false;
        return true;
    }

    public IndustrialGasNetworkGraph(FluidNetworkGraph topology, IEnumerable<MachineEntity> nodes,
        Func<MachineEntity, FluidInventory> localInventory)
    {
        var originals = new Dictionary<FluidInventory, FluidInventoryState>();
        FluidInventory CaptureLocal(MachineEntity node)
        {
            FluidInventory inventory = localInventory(node);
            if (!originals.ContainsKey(inventory)) originals.Add(inventory, inventory.State.Clone());
            return inventory;
        }
        try { Build(topology, nodes, CaptureLocal); }
        catch
        {
            // 后面的成员不能接收时，前面已经归并的成员也必须归还，不能随失败的图一起消失。
            foreach (var original in originals) original.Key.Restore(original.Value);
            throw;
        }
    }

    private void Build(FluidNetworkGraph topology, IEnumerable<MachineEntity> nodes,
        Func<MachineEntity, FluidInventory> localInventory)
    {
        var passive = new Dictionary<int, MachineEntity>();
        var neighbors = new Dictionary<int, List<int>>();
        foreach (MachineEntity node in nodes)
            if (IsPassiveGasPipe(node)) passive.Add(node.Id, node);
        foreach (FluidNetworkGraph.Edge edge in topology.Edges)
        {
            if (edge.Target == null || !AcceptsGas(edge.SourcePort) || !AcceptsGas(edge.TargetPort) ||
                !passive.ContainsKey(edge.Source.Id) || !passive.ContainsKey(edge.Target.Id)) continue;
            if (!neighbors.TryGetValue(edge.Source.Id, out var list)) neighbors.Add(edge.Source.Id, list = new List<int>());
            list.Add(edge.Target.Id);
            if (!neighbors.TryGetValue(edge.Target.Id, out var reverse)) neighbors.Add(edge.Target.Id, reverse = new List<int>());
            reverse.Add(edge.Source.Id);
        }
        var queue = new Queue<int>();
        foreach (MachineEntity root in passive.Values)
        {
            if (nodeGroups.ContainsKey(root.Id)) continue;
            var group = new Group(); Groups.Add(group);
            nodeGroups.Add(root.Id, group); queue.Enqueue(root.Id);
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                MachineEntity node = passive[id];
                group.Members.Add(node);
                group.TotalVolumeLiters += node.Definition.Fluid.VolumeLiters;
                group.MinimumGasSpaceLiters += node.Definition.Fluid.MinimumGasSpaceLiters;
                if (!neighbors.TryGetValue(id, out var adjacent)) continue;
                foreach (int next in adjacent)
                    if (nodeGroups.TryAdd(next, group)) queue.Enqueue(next);
            }
            group.Members.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (MachineEntity member in group.Members)
            {
                FluidInventory local = localInventory(member);
                group.UpdateLiquid(member, local);
                AbsorbGas(group, local);
                group.PhaseContacts.Add(new FluidLiquidPhaseContact(local, member.Definition.Fluid.VolumeLiters,
                    member.Definition.Fluid.MinimumGasSpaceLiters, member.Definition.Fluid.SingleLiquid));
                group.MaximumPhaseMolesPerSecond += member.Definition.Fluid.MaximumPhaseMolesPerSecond;
            }
        }
        foreach (FluidNetworkGraph.Edge edge in topology.Edges)
        {
            if (!AcceptsGas(edge.SourcePort) || edge.TargetPort != null && !AcceptsGas(edge.TargetPort)) continue;
            if (edge.Target != null && TryGetGroup(edge.Source, out Group source) &&
                TryGetGroup(edge.Target, out Group target) && ReferenceEquals(source, target)) continue;
            Edges.Add(edge);
        }
        var boundaryPorts = new HashSet<(int, string)>();
        foreach (FluidNetworkGraph.Edge edge in Edges)
        {
            if (TryGetGroup(edge.Source, out Group sourceGroup) && boundaryPorts.Add((edge.Source.Id, edge.SourcePort.Id)))
                sourceGroup.ThroughputLitersPerSecond += edge.Source.Definition.Fluid.ThroughputLitersPerSecond;
            if (edge.Target != null && TryGetGroup(edge.Target, out Group targetGroup) && boundaryPorts.Add((edge.Target.Id, edge.TargetPort.Id)))
                targetGroup.ThroughputLitersPerSecond += edge.Target.Definition.Fluid.ThroughputLitersPerSecond;
        }
    }

    public static void AbsorbGas(Group group, FluidInventory local)
    {
        if (local.GasMoles <= 0m) return;
        FluidInventoryState backup = local.State.Clone();
        FluidInventoryState beforeGroup = group.Inventory.State.Clone();
        bool firstBatch = group.Inventory.GasMoles == 0m;
        var gasPorts = new List<FluidOutputReservation>();
        foreach (FluidOutputReservation reservation in local.State.Ports)
            if (reservation.GasMoles > 0m)
                gasPorts.Add(new FluidOutputReservation
                { PortId = reservation.PortId, FluidId = reservation.FluidId, GasMoles = reservation.GasMoles });
        local.ClearGas(out List<FluidBatch> batches);
        try
        {
            if (firstBatch) group.Inventory.State.RandomState = backup.RandomState;
            foreach (FluidBatch batch in batches)
                if (!group.Inventory.TryAdd(batch, group.GasSpaceLiters, group.MinimumGasSpaceLiters))
                    throw new InvalidOperationException("气网合并未能守恒接收原气体库存。");
            foreach (FluidOutputReservation port in gasPorts)
            {
                if (group.Inventory.GetReservation(port.PortId) != null)
                    throw new InvalidOperationException("气网合并出现重复气体端口预留：" + port.PortId);
                group.Inventory.State.Ports.Add(port);
            }
            if (gasPorts.Count > 0) group.Inventory.State.SynchronizeReservations();
        }
        catch
        {
            local.Restore(backup);
            group.Inventory.Restore(beforeGroup);
            throw;
        }
    }

    // 分网前按真实气相空间分配；先验证全部目标，再提交，不能把某段的液体空间当成气体空间。
    public void FlushToLocal(Func<MachineEntity, bool> stillExists, Func<MachineEntity, FluidInventory> localInventory)
    {
        // 拆卸/销毁必须先取出份额再删除成员。拓扑事务不做外部排放，避免回调中断后二次排气。
        foreach (Group group in Groups)
            foreach (MachineEntity member in group.Members)
                if (!stillExists(member)) throw new InvalidOperationException("气网成员删除前未交接库存，原气网已保留：" + member.Id);
        var snapshots = new Dictionary<FluidInventory, FluidInventoryState>();
        var originals = new Dictionary<FluidInventory, FluidInventoryState>();
        try
        {
            foreach (Group group in Groups)
            {
                if (group.Inventory.GasMoles == 0m) continue;
                originals.Add(group.Inventory, group.Inventory.State.Clone());
                var spaces = new List<decimal>(group.Members.Count);
                var memberIds = new List<int>(group.Members.Count);
                foreach (MachineEntity member in group.Members)
                {
                    group.UpdateLiquid(member, localInventory(member));
                    spaces.Add((decimal)group.GetMemberGasSpaceLiters(member));
                    memberIds.Add(member.Id);
                }
                FluidInventoryState[] shares = FluidGasPartition.Prepare(group.Inventory, spaces, memberIds);
                for (int index = 0; index < group.Members.Count; index++)
                {
                    MachineEntity member = group.Members[index];
                    var share = new FluidInventory(shares[index], group.Inventory.Catalog);
                    if (share.GasMoles == 0m) continue;
                    double temperature = share.GetTemperatureKelvin();
                    FluidInventory local = localInventory(member);
                    double startingEnergy = local.State.TotalInternalEnergyJoules;
                    if (!snapshots.ContainsKey(local)) snapshots.Add(local, local.State.Clone());
                    foreach (FluidComponentState component in share.State.Components)
                    {
                        FluidBatch batch = FluidInventory.CreateBatch(group.Inventory.Catalog.Find(component.FluidId),
                            component.GasMoles, 0m, temperature);
                        if (!local.TryAdd(batch, member.Definition.Fluid.VolumeLiters,
                            member.Definition.Fluid.MinimumGasSpaceLiters))
                            throw new InvalidOperationException("气网拆分未能守恒写回成员库存：" + member.Id);
                    }
                    local.State.TotalInternalEnergyJoules = startingEnergy + share.State.TotalInternalEnergyJoules;
                    foreach (FluidOutputReservation reservation in share.State.Ports)
                    {
                        if (local.GetReservation(reservation.PortId) != null)
                            throw new InvalidOperationException("气网拆分出现重复端口预留：" + reservation.PortId);
                        local.State.Ports.Add(reservation.Clone());
                    }
                    local.State.SynchronizeReservations();
                    FluidInventory.ValidateState(local.State, local.Catalog);
                }
            }
            foreach (var original in originals) original.Key.ClearGas(out _);
        }
        catch
        {
            foreach (var local in snapshots) local.Key.Restore(local.Value);
            foreach (var original in originals) original.Key.Restore(original.Value);
            throw;
        }
    }
    #endregion
}

/// <summary>液体只遍历可接受液相的正式端口边，不使用气相共享库存。</summary>
internal sealed class IndustrialLiquidNetworkGraph
{
    #region 液相有向连通
    public readonly List<FluidNetworkGraph.Edge> Edges = new();
    private readonly Dictionary<int, int> outletDistance = new();
    public IndustrialLiquidNetworkGraph(FluidNetworkGraph topology)
    {
        var incoming = new Dictionary<int, List<FluidNetworkGraph.Edge>>();
        var queue = new Queue<int>();
        foreach (FluidNetworkGraph.Edge edge in topology.Edges)
            if (edge.SourcePort.Phase != "gas" && (edge.TargetPort == null || edge.TargetPort.Phase != "gas"))
            {
                Edges.Add(edge);
                if (edge.Target != null)
                {
                    if (!incoming.TryGetValue(edge.Target.Id, out var inputs)) incoming.Add(edge.Target.Id, inputs = new());
                    inputs.Add(edge);
                }
                if ((edge.Environment || edge.SourcePort.Outlet && edge.Target != null &&
                    !MachineWorld.GetFluidDeviceBehavior(edge.Target).IsTransit) && outletDistance.TryAdd(edge.Source.Id, 0))
                    queue.Enqueue(edge.Source.Id);
            }
        while (queue.Count > 0)
        {
            int target = queue.Dequeue();
            if (!incoming.TryGetValue(target, out var inputs)) continue;
            foreach (FluidNetworkGraph.Edge edge in inputs)
                if (outletDistance.TryAdd(edge.Source.Id, outletDistance[target] + 1)) queue.Enqueue(edge.Source.Id);
        }
    }
    public bool CanRoute(FluidNetworkGraph.Edge edge)
    {
        if (edge.Environment || edge.SourcePort.Outlet) return true;
        return edge.Target != null && outletDistance.TryGetValue(edge.Source.Id, out int source) &&
            outletDistance.TryGetValue(edge.Target.Id, out int target) && target < source;
    }
    #endregion
}
