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

    public static void CollectFluidDeliveryRoutes(MachineEntity source, string chamber, FluidPhase phase,
        IReadOnlyList<string> fluidIds, FluidNetworkGraph.SupplyWorkspace workspace)
    {
        using var nodeScope = UseNodeScope(source); RebuildGraphsIfDirty();
        if (fluidGraph == null) { workspace.Nodes.Clear(); return; }
        fluidGraph.CollectDeliveryRoutes(source, chamber, phase, fluidIds, workspace);
    }

    public static void ConsumeFluidRouteBudget(FluidNetworkGraph.SupplyWorkspace workspace, int sourceIndex, double liters)
    {
        workspace.Inventories.Clear();
        for (int index = sourceIndex; index >= 0; index = workspace.Nodes[index].DownstreamIndex)
        {
            FluidNetworkGraph.SupplyNode route = workspace.Nodes[index];
            FluidInventory inventory = GetFluidInventory(route.Node, route.Chamber);
            var nodeBudgets = workspace.Phase == FluidPhase.Gas ? scope.GasNodeBudget : scope.LiquidNodeBudget;
            if (workspace.Inventories.Add(inventory) && nodeBudgets.TryGetValue(route.Node.Id, out double budget))
            {
                nodeBudgets[route.Node.Id] = Math.Max(0d, budget - liters);
                if (workspace.Phase == FluidPhase.Gas) SetGasTransferBudget(inventory, Math.Max(0d, budget - liters));
                else SetFluidTransferBudget(inventory, Math.Max(0d, budget - liters));
            }
            if (workspace.Phase == FluidPhase.Gas && route.Boundary != null)
            {
                ConsumeGasPort(route.Boundary.Source, route.Boundary.SourcePort, liters);
                ConsumeGasPort(route.Boundary.Target, route.Boundary.TargetPort, liters);
            }
        }
    }

    public static string GetFluidRouteFilter(FluidNetworkGraph.SupplyWorkspace workspace, int index)
    {
        string selected = null;
        for (; index >= 0; index = workspace.Nodes[index].DownstreamIndex)
        {
            FluidNetworkGraph.SupplyNode route = workspace.Nodes[index];
            if (GetFluidDeviceBehavior(route.Node) is FluidSelectorBehavior) selected = GetFluidState(route.Node).SelectedGasId;
            if (!string.IsNullOrEmpty(route.Boundary?.SourcePort.FluidId)) selected = route.Boundary.SourcePort.FluidId;
            if (!string.IsNullOrEmpty(route.Boundary?.TargetPort.FluidId)) selected = route.Boundary.TargetPort.FluidId;
        }
        return selected;
    }

    public static double GetFluidRouteCharge(FluidNetworkGraph.SupplyWorkspace workspace, int index,
        FluidInventoryState sourceBefore, double transferredLiters)
    {
        string filter = workspace.Phase == FluidPhase.Gas ? GetFluidRouteFilter(workspace, index) : null;
        if (filter == null) return transferredLiters;
        decimal total = 0m, selected = 0m;
        foreach (FluidComponentState component in sourceBefore.Components)
        {
            total += component.GasMoles;
            if (FluidInventory.Same(component.FluidId, filter)) selected += component.GasMoles;
        }
        return selected <= 0m ? 0d : transferredLiters * (double)(total / selected);
    }

    private static void SupplyIndustrialConsumer(MachineEntity receiver, float seconds)
    {
        var routes = new FluidNetworkGraph.SupplyWorkspace();
        foreach (FluidPhase phase in new[] { FluidPhase.Gas, FluidPhase.Liquid })
        {
            var visited = new HashSet<FluidInventory>();
            foreach (string id in FluidCatalog.Default.Definitions.Keys)
            {
                CollectFluidSupplyRoutes(receiver, "main", phase, new[] { id }, routes);
                for (int i = 1; i < routes.Nodes.Count; i++)
                {
                    var route = routes.Nodes[i];
                    if (GetFluidDeviceBehavior(route.Node).IsTransit) continue;
                    FluidInventory source = GetFluidInventory(route.Node, route.Chamber);
                    if (source.GetAvailableMoles(id, phase) <= 0m || !visited.Add(source)) continue;
                    double free = GetIndustrialFreeCapacity(receiver, phase, id);
                    double budget = Math.Min(free, GetFluidSupplyStandardLiters(routes, i, seconds, true));
                    if (budget <= 0d) continue;
                    FluidInventoryState before = source.State.Clone();
                    double moved = IndustrialFluidTransfer.Transfer(source, GetFluidInventory(receiver), phase, budget,
                        GetFluidGasCapacityStandardLiters(receiver), GetFluidVolumeLiters(receiver), GetFluidRouteFilter(routes, i));
                    if (moved <= 0d) continue;
                    var consumed = new List<FluidRequirement>();
                    foreach (FluidComponentState component in before.Components)
                    {
                        decimal previous = phase == FluidPhase.Gas ? component.GasMoles : component.LiquidMoles;
                        decimal amount = previous - (phase == FluidPhase.Gas ? source.GetGasMoles(component.FluidId) : source.GetLiquidMoles(component.FluidId));
                        if (amount > 0m) consumed.Add(new FluidRequirement(component.FluidId, phase, amount));
                    }
                    ConsumeFluidSupplyBudget(routes, i, consumed, GetFluidRouteCharge(routes, i, before, moved)); StateChanged(receiver);
                }
            }
        }
    }

    // 液网按物质身份整体互斥，拒绝通过方向或旁路把异种液体混入同网。
    public static bool CanAcceptIndustrialLiquid(MachineEntity receiver, string fluidId, string chamber = "main")
    {
        if (receiver == null || string.IsNullOrEmpty(fluidId)) return false;
        bool Matches(MachineEntity node, string id)
        {
            foreach (FluidComponentState component in GetFluidInventory(node, id).State.Components)
                if (component.LiquidMoles > 0m && !FluidInventory.Same(component.FluidId, fluidId)) return false;
            return true;
        }
        var members = fluidGraph?.GetLiquidComponent(receiver, chamber);
        if (members == null) return Matches(receiver, chamber);
        foreach (var member in members) if (!Matches(member.Node, member.Chamber)) return false;
        return true;
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
            double throughput = GetIndustrialThroughput(route.Node);
            double budget = throughput * seconds;
            if (workspace.Phase == FluidPhase.Gas && route.Boundary != null)
            {
                FluidNetworkGraph.Edge edge = route.Boundary;
                double sourcePort = useStepBudget ? GasPortBudget(edge.Source, edge.SourcePort, (float)seconds)
                    : GetIndustrialThroughput(edge.Source) * seconds;
                double targetPort = useStepBudget ? GasPortBudget(edge.Target, edge.TargetPort, (float)seconds)
                    : GetIndustrialThroughput(edge.Target) * seconds;
                budget = Math.Min(budget, Math.Min(sourcePort, targetPort));
            }
            if (useStepBudget)
            {
                if (!(workspace.Phase == FluidPhase.Gas ? scope.GasNodeBudget : scope.LiquidNodeBudget).TryGetValue(route.Node.Id, out double remaining)) return 0d;
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
        IReadOnlyList<FluidRequirement> consumed, double routeLiters = -1d)
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
        if (routeLiters >= 0d) liters = routeLiters;
        workspace.Inventories.Clear();
        for (int index = sourceIndex; index >= 0; index = workspace.Nodes[index].DownstreamIndex)
        {
            FluidNetworkGraph.SupplyNode route = workspace.Nodes[index];
            FluidInventory inventory = GetPhaseFluidInventory(route.Node, route.Chamber, workspace.Phase);
            var nodeBudgets = workspace.Phase == FluidPhase.Gas ? scope.GasNodeBudget : scope.LiquidNodeBudget;
            if (workspace.Inventories.Add(inventory) && nodeBudgets.TryGetValue(route.Node.Id, out double budget))
            {
                nodeBudgets[route.Node.Id] = Math.Max(0d, budget - liters);
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
