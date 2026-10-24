using System;
using System.Collections.Generic;

public static partial class MachineWorld
{
    #region 正式管路按需供给
    public static void CollectFluidSupplyRoutes(MachineEntity receiver, string chamber, FluidPhase phase,
        IReadOnlyList<string> requiredFluidIds, FluidNetworkGraph.SupplyWorkspace workspace)
    {
        using var nodeScope = UseNodeScope(receiver);
        RebuildGraphsIfDirty();
        if (fluidGraph == null) { workspace.Nodes.Clear(); return; }
        fluidGraph.CollectSupplyRoutes(receiver, chamber, phase, requiredFluidIds, workspace);
    }

    public static double GetFluidSupplyStandardLiters(FluidNetworkGraph.SupplyWorkspace workspace, int sourceIndex,
        double seconds, bool useStepBudget, IDictionary<FluidInventory, double> projectedBudget = null)
    {
        double available = double.PositiveInfinity;
        workspace.Inventories.Clear();
        for (int index = sourceIndex; index >= 0; index = workspace.Nodes[index].DownstreamIndex)
        {
            FluidNetworkGraph.SupplyNode route = workspace.Nodes[index];
            FluidInventory inventory = GetPhaseFluidInventory(route.Node, route.Chamber, workspace.Phase);
            if (!workspace.Inventories.Add(inventory)) continue;
            double throughput = route.Node.Definition.Fluid.ThroughputLitersPerSecond;
            if (workspace.Phase == FluidPhase.Gas && gasGraph != null && gasGraph.TryGetGroup(route.Node, out var gasGroup))
                throughput = gasGroup.ThroughputLitersPerSecond;
            if (fluidTankOwners.TryGetValue(route.Node.Id, out var group)) throughput *= group.Members.Count;
            double budget = throughput * seconds;
            if (workspace.Phase == FluidPhase.Gas && route.Boundary != null)
            {
                FluidNetworkGraph.Edge edge = route.Boundary;
                double sourcePort = useStepBudget ? GasPortBudget(edge.Source, edge.SourcePort, (float)seconds)
                    : edge.Source.Definition.Fluid.ThroughputLitersPerSecond * seconds;
                double targetPort = useStepBudget ? GasPortBudget(edge.Target, edge.TargetPort, (float)seconds)
                    : edge.Target.Definition.Fluid.ThroughputLitersPerSecond * seconds;
                budget = Math.Min(budget, Math.Min(sourcePort, targetPort));
            }
            if (useStepBudget)
            {
                if (!TryGetPhaseSupplyBudget(inventory, workspace.Phase, out _, out double remaining)) return 0d;
                budget = Math.Min(budget, remaining);
            }
            if (projectedBudget != null)
            {
                if (!projectedBudget.TryGetValue(inventory, out double projected)) projectedBudget[inventory] = projected = budget;
                budget = Math.Min(budget, projected);
            }
            available = Math.Min(available, budget);
        }
        return double.IsFinite(available) ? Math.Max(0d, available) : 0d;
    }

    public static void ReserveFluidSupplyProjection(FluidNetworkGraph.SupplyWorkspace workspace, int sourceIndex,
        double liters, IDictionary<FluidInventory, double> projectedBudget)
    {
        workspace.Inventories.Clear();
        for (int index = sourceIndex; index >= 0; index = workspace.Nodes[index].DownstreamIndex)
        {
            FluidNetworkGraph.SupplyNode route = workspace.Nodes[index];
            FluidInventory inventory = GetPhaseFluidInventory(route.Node, route.Chamber, workspace.Phase);
            if (workspace.Inventories.Add(inventory) && projectedBudget.TryGetValue(inventory, out double available))
                projectedBudget[inventory] = Math.Max(0d, available - liters);
        }
    }

    public static decimal GetFluidSupplyStock(FluidNetworkGraph.SupplyWorkspace workspace, int sourceIndex,
        string fluidId, FluidPhase phase)
    {
        FluidNetworkGraph.SupplyNode source = workspace.Nodes[sourceIndex];
        FluidInventory inventory = GetPhaseFluidInventory(source.Node, source.Chamber, phase);
        if (!TryGetPhaseSupplyBudget(inventory, phase, out FluidInventoryState stock, out _)) return 0m;
        FluidComponentState component = stock.Components.Find(value => value.FluidId == fluidId);
        decimal amount = component == null ? 0m : phase == FluidPhase.Gas ? component.GasMoles : component.LiquidMoles;
        return Math.Min(amount, inventory.GetAvailableMoles(fluidId, phase));
    }

    public static void ConsumeFluidSupplyBudget(FluidNetworkGraph.SupplyWorkspace workspace, int sourceIndex,
        IReadOnlyList<FluidRequirement> consumed)
    {
        double liters = 0d;
        FluidNetworkGraph.SupplyNode source = workspace.Nodes[sourceIndex];
        FluidInventory sourceInventory = GetPhaseFluidInventory(source.Node, source.Chamber, workspace.Phase);
        foreach (FluidRequirement requirement in consumed)
        {
            bool gas = requirement.Phase == FluidPhase.Gas;
            ConsumeFluidStepStock(sourceInventory, requirement.FluidId, gas ? requirement.Moles : 0m, gas ? 0m : requirement.Moles);
            liters += (double)(gas ? FluidUnits.MolToStandardLiters(requirement.Moles) :
                FluidUnits.MolToLiquidLiters(sourceInventory.Catalog.Find(requirement.FluidId), requirement.Moles));
        }
        workspace.Inventories.Clear();
        for (int index = sourceIndex; index >= 0; index = workspace.Nodes[index].DownstreamIndex)
        {
            FluidNetworkGraph.SupplyNode route = workspace.Nodes[index];
            FluidInventory inventory = GetPhaseFluidInventory(route.Node, route.Chamber, workspace.Phase);
            if (workspace.Inventories.Add(inventory) && TryGetPhaseSupplyBudget(inventory, workspace.Phase, out _, out double budget))
            {
                if (workspace.Phase == FluidPhase.Gas) SetGasTransferBudget(inventory, Math.Max(0d, budget - liters));
                else SetFluidTransferBudget(inventory, Math.Max(0d, budget - liters));
            }
            if (workspace.Phase == FluidPhase.Gas && route.Boundary != null)
            {
                ConsumeGasPort(route.Boundary.Source, route.Boundary.SourcePort, liters);
                ConsumeGasPort(route.Boundary.Target, route.Boundary.TargetPort, liters);
            }
        }
        StateChanged(source.Node);
    }
    private static bool TryGetPhaseSupplyBudget(FluidInventory inventory, FluidPhase phase,
        out FluidInventoryState stock, out double liters)
        => phase == FluidPhase.Gas ? TryGetGasTransferBudget(inventory, out stock, out liters)
            : TryGetFluidTransferBudget(inventory, out stock, out liters);
    #endregion
}
