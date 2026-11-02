using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class FluidNetworkGraph
{
    #region 复用正式端口的液温分组
    public sealed class LiquidTemperatureComponent
    {
        public readonly List<SupplyNode> Members = new();
        public readonly HashSet<Vector2Int> Cells = new();
        public readonly IndustrialLiquidNetworkTemperature Temperature = new();
    }

    private readonly Dictionary<(int Node, string Chamber, string Fluid), LiquidTemperatureComponent> liquidTemperatureComponents = new();
    private readonly Dictionary<FluidInventory, List<SupplyNode>> liquidInventoryMembers = new();
    private IReadOnlyList<MachineEntity> liquidTemperatureNodes;
    private bool liquidInventoryMembersReady;

    private void ResetLiquidTemperatureComponents(IReadOnlyList<MachineEntity> nodes)
    {
        liquidTemperatureComponents.Clear(); liquidInventoryMembers.Clear();
        liquidTemperatureNodes = nodes; liquidInventoryMembersReady = false;
    }

    // 温度视图沿用同一张有向端口图，只把允许该液体的边用于双向均温。
    public LiquidTemperatureComponent GetLiquidTemperatureComponent(MachineEntity node, string chamber, string fluidId)
    {
        var key = (node.Id, chamber, fluidId);
        if (liquidTemperatureComponents.TryGetValue(key, out var existing)) return existing;
        PrepareLiquidTemperatureInventories();
        var component = new LiquidTemperatureComponent();
        AddLiquidTemperatureMember(component, node, chamber, fluidId);
        for (int i = 0; i < component.Members.Count; i++)
        {
            SupplyNode member = component.Members[i];
            if (!IsTransit(member.Node))
            {
                FluidInventory inventory = MachineWorld.GetFluidInventory(member.Node, member.Chamber);
                component.Temperature.AddInventory(inventory);
                if (liquidInventoryMembers.TryGetValue(inventory, out var shared))
                    foreach (SupplyNode other in shared)
                        AddLiquidTemperatureMember(component, other.Node, other.Chamber, fluidId);
            }
            if (outgoing.TryGetValue(member.Node.Id, out var outputs))
                foreach (Edge edge in outputs)
                    if (edge.SourcePort.Chamber == member.Chamber && AllowsLiquidTemperatureConnection(edge, fluidId))
                        AddLiquidTemperatureMember(component, edge.Target, edge.TargetPort.Chamber, fluidId);
            if (incoming.TryGetValue(member.Node.Id, out var inputs))
                foreach (Edge edge in inputs)
                    if (edge.TargetPort.Chamber == member.Chamber && AllowsLiquidTemperatureConnection(edge, fluidId))
                        AddLiquidTemperatureMember(component, edge.Source, edge.SourcePort.Chamber, fluidId);
        }
        return component;
    }

    private void AddLiquidTemperatureMember(LiquidTemperatureComponent component, MachineEntity node, string chamber, string fluidId)
    {
        if (!liquidTemperatureComponents.TryAdd((node.Id, chamber, fluidId), component)) return;
        component.Members.Add(new SupplyNode(node, chamber, -1));
        component.Cells.Add(normalize(node.Cell));
    }

    private static bool AllowsLiquidTemperatureConnection(Edge edge, string fluidId)
        => edge.Target != null && CanSupply(edge.Source, FluidPhase.Liquid) && CanSupply(edge.Target, FluidPhase.Liquid) &&
            Accepts(edge.SourcePort, FluidPhase.Liquid, fluidId) && Accepts(edge.TargetPort, FluidPhase.Liquid, fluidId) &&
            MachineWorld.GetFluidDeviceBehavior(edge.Source).CanRoute(edge.Source, edge.SourcePort, FluidPhase.Liquid, fluidId, out _) &&
            MachineWorld.GetFluidDeviceBehavior(edge.Target).CanRoute(edge.Target, edge.TargetPort, FluidPhase.Liquid, fluidId, out _);

    internal void InvalidateLiquidTemperatureInventories()
    {
        liquidTemperatureComponents.Clear(); liquidInventoryMembers.Clear(); liquidInventoryMembersReady = false;
    }

    internal void PrepareLiquidTemperatureInventories()
    {
        if (liquidInventoryMembersReady) return;
        liquidInventoryMembersReady = true;
        foreach (MachineEntity node in liquidTemperatureNodes)
        {
            if (IsTransit(node)) continue;
            foreach (FluidMachinePortDefinition port in node.Definition.Fluid.Ports)
            {
                if (port.Phase == "gas") continue;
                FluidInventory inventory = MachineWorld.GetFluidInventory(node, port.Chamber);
                if (!liquidInventoryMembers.TryGetValue(inventory, out var members))
                    liquidInventoryMembers.Add(inventory, members = new());
                bool found = false;
                foreach (SupplyNode member in members)
                    if (member.Node == node && member.Chamber == port.Chamber) { found = true; break; }
                if (!found) members.Add(new SupplyNode(node, port.Chamber, -1));
            }
        }
    }
    #endregion
}
