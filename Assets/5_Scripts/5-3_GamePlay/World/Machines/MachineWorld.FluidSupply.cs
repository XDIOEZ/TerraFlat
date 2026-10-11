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
            FluidInventory inventory = GetFluidInventory(route.Node, route.Chamber);
            if (!workspace.Inventories.Add(inventory)) continue;
            double throughput = route.Node.Definition.Fluid.ThroughputLitersPerSecond;
            if (fluidTankOwners.TryGetValue(route.Node.Id, out var group)) throughput *= group.Members.Count;
            double budget = throughput * seconds;
            if (useStepBudget)
            {
                if (!TryGetFluidTransferBudget(inventory, out _, out double remaining)) return 0d;
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
            FluidInventory inventory = GetFluidInventory(route.Node, route.Chamber);
            if (workspace.Inventories.Add(inventory) && projectedBudget.TryGetValue(inventory, out double available))
                projectedBudget[inventory] = Math.Max(0d, available - liters);
        }
    }

    public static decimal GetFluidSupplyStock(FluidNetworkGraph.SupplyWorkspace workspace, int sourceIndex,
        string fluidId, FluidPhase phase)
    {
        FluidNetworkGraph.SupplyNode source = workspace.Nodes[sourceIndex];
        FluidInventory inventory = GetFluidInventory(source.Node, source.Chamber);
        if (!TryGetFluidTransferBudget(inventory, out FluidInventoryState stock, out _)) return 0m;
        FluidComponentState component = stock.Components.Find(value => value.FluidId == fluidId);
        decimal amount = component == null ? 0m : phase == FluidPhase.Gas ? component.GasMoles : component.LiquidMoles;
        return Math.Min(amount, inventory.GetAvailableMoles(fluidId, phase));
    }

    public static void ConsumeFluidSupplyBudget(FluidNetworkGraph.SupplyWorkspace workspace, int sourceIndex,
        IReadOnlyList<FluidRequirement> consumed)
    {
        double liters = 0d;
        FluidNetworkGraph.SupplyNode source = workspace.Nodes[sourceIndex];
        FluidInventory sourceInventory = GetFluidInventory(source.Node, source.Chamber);
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
            FluidInventory inventory = GetFluidInventory(route.Node, route.Chamber);
            if (workspace.Inventories.Add(inventory) && TryGetFluidTransferBudget(inventory, out _, out double budget))
                SetFluidTransferBudget(inventory, Math.Max(0d, budget - liters));
        }
        StateChanged(source.Node);
    }
    #endregion
}
