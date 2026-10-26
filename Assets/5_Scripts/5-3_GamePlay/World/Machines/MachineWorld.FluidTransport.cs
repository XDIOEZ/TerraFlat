using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class MachineWorld
{
    #region 无库存出口按需排放
    private static void AdvanceIndustrialOutlets(float seconds)
    {
        if (!GameNetwork.HasStateAuthority) return;
        foreach (FluidNetworkGraph.Edge edge in fluidGraph.Edges)
        {
            if (!edge.Environment || !IsFluidActive(edge.Source) || !GetFluidDeviceBehavior(edge.Source).EmitsToEnvironment) continue;
            if (edge.SourcePort.Phase != "liquid") EmitIndustrialOutlet(edge, FluidPhase.Gas, seconds);
            if (edge.SourcePort.Phase != "gas") EmitIndustrialOutlet(edge, FluidPhase.Liquid, seconds);
        }
    }

    private static double EmitIndustrialOutlet(FluidNetworkGraph.Edge edge, FluidPhase phase, float seconds, string exactGas = null)
    {
        MachineEntity outlet = edge.Source; FluidMachineState state = GetFluidState(outlet);
        double rate = state.OutputLitersPerSecond < 0d ? outlet.Definition.Fluid.ThroughputLitersPerSecond : state.OutputLitersPerSecond;
        if (rate <= 0d || !GetFluidDeviceBehavior(outlet).PortsEnabled(outlet))
        { state.WorldLiquidOutputLiters.Clear(); return 0d; }
        var workspace = new FluidNetworkGraph.SupplyWorkspace(); var sources = new HashSet<FluidInventory>();
        double remaining = rate * seconds, total = 0d;
        foreach (FluidDefinition fluid in FluidCatalog.Default.Definitions.Values)
        {
            if (remaining <= 0d) break;
            if (!fluid.AllowIndustrialTransport || exactGas != null && !FluidInventory.Same(fluid.Id, exactGas)) continue;
            CollectFluidSupplyRoutes(outlet, edge.SourcePort.Chamber, phase, new[] { fluid.Id }, workspace);
            for (int index = 1; index < workspace.Nodes.Count && remaining > 0d; index++)
            {
                FluidNetworkGraph.SupplyNode route = workspace.Nodes[index];
                if (GetFluidDeviceBehavior(route.Node).IsTransit) continue;
                FluidInventory source = GetFluidInventory(route.Node, route.Chamber);
                if (source.GetAvailableMoles(fluid.Id, phase) <= 0m || !sources.Add(source)) continue;
                double budget = Math.Min(remaining, GetFluidSupplyStandardLiters(workspace, index, seconds, true));
                if (budget <= 0d) continue;
                string selected = exactGas ?? GetFluidRouteFilter(workspace, index);
                double moved;
                if (phase == FluidPhase.Gas)
                {
                    var emitted = new FluidInventory();
                    var before = source.State.Clone();
                    moved = IndustrialFluidTransfer.Transfer(source, emitted, phase, budget, budget, .001d, selected);
                    if (moved <= 0d) continue;
                    double charged = exactGas == null ? GetFluidRouteCharge(workspace, index, before, moved) : moved;
                    // 候选先准备，排放成功的批次才扣来源，异常不会吞掉尚未排出的组分。
                    source.Restore(before);
                    foreach (FluidBatch batch in emitted.Drain())
                    {
                        if (!source.TryTakeExact(batch.FluidId, FluidPhase.Gas, batch.GasMoles, out FluidBatch actual))
                            throw new InvalidOperationException("出口来源在原子排放期间发生变化。");
                        try
                        {
                            if (exactGas == null) RecordAtmosphereEmission(outlet, actual,
                                (Vector2)(outlet.Cell + MechanicalNetworkGraph.Directions[edge.Direction]) + Vector2.one * .5f);
                        }
                        catch
                        {
                            if (!source.TryAdd(actual, GetFluidVolumeLiters(route.Node) + .001d, .001d))
                                throw new InvalidOperationException("出口失败回滚无法恢复来源。");
                            throw;
                        }
                        ConsumeFluidStepStock(source, actual.FluidId, actual.GasMoles, 0m);
                    }
                    ConsumeFluidRouteBudget(workspace, index, charged); remaining -= charged;
                }
                else
                {
                    if (!CanAcceptIndustrialLiquid(outlet, fluid.Id)) { state.Status = "液网存在异种液体，拒绝排放"; continue; }
                    moved = EmitIndustrialLiquid(edge, source, fluid, budget, out double charged);
                    if (charged > 0d) { ConsumeFluidRouteBudget(workspace, index, charged); remaining -= charged; }
                    if (moved <= 0d) continue;
                    ConsumeFluidStepStock(source, fluid.Id, 0m, FluidUnits.LiquidLitersToMol(fluid, (decimal)moved));
                }
                total += moved; state.Status = "排放中"; state.LastFlowPhase = phase == FluidPhase.Gas ? "gas" : "liquid";
                state.LastFlowDirection = edge.Direction; StateChanged(route.Node); StateChanged(outlet);
            }
        }
        return total;
    }

    private static double EmitIndustrialLiquid(FluidNetworkGraph.Edge edge, FluidInventory source, FluidDefinition fluid,
        double budget, out double charged)
    {
        charged = 0d; FluidMachineState state = GetFluidState(edge.Source);
        if (string.IsNullOrEmpty(fluid.LiquidId) || GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetLiquidDefinition(fluid.LiquidId, out LiquidDefinition liquid) || liquid.WorldWater == null) return 0d;
        decimal serving = FluidUnits.LiquidLitersToMol(fluid, FlatWorld.WorldModel.LiquidCellValue.LitersPerUnit);
        double litersPerServing = (double)FluidUnits.MolToLiquidLiters(fluid, serving);
        string creditKey = edge.SourcePort.Id + ":" + fluid.Id;
        double carried = state.WorldLiquidOutputLiters.GetValueOrDefault(creditKey);
        decimal available = source.GetAvailableMoles(fluid.Id, FluidPhase.Liquid);
        int count = (int)Math.Min(int.MaxValue, Math.Min((double)decimal.Floor(available / serving),
            Math.Floor((carried + budget + 1e-10d) / litersPerServing)));
        if (count <= 0)
        {
            if (available < serving) { state.WorldLiquidOutputLiters.Remove(creditKey); return 0d; }
            charged = Math.Min(budget, Math.Max(0d, litersPerServing - carried));
            state.WorldLiquidOutputLiters[creditKey] = carried + charged; state.Status = "累计排液量"; return 0d;
        }
        Vector2 destination = (Vector2)(edge.Source.Cell + MechanicalNetworkGraph.Directions[edge.Direction]) + Vector2.one * .5f;
        string targetWorld = worldKey; Vector2 targetPosition = destination;
        if (!TryResolveFluidSurfacePoint(destination, out targetWorld, out targetPosition) &&
            (IsShipScope || worldKey != SceneManager.GetActiveScene().name)) return 0d;
        int accepted = 0;
        for (; accepted < count; accepted++)
        {
            FluidInventoryState before = source.State.Clone();
            if (!source.TryTakeExact(fluid.Id, FluidPhase.Liquid, serving, out _)) break;
            int added;
            // 逐个十升提交，目标只剩一份空间时也先接满，拒收仅回滚当前一批。
            try { added = ReleaseFluidEnvironmentLiquid(targetWorld ?? worldKey, targetPosition, fluid.LiquidId, 1); }
            catch { source.Restore(before); throw; }
            if (added == 1) continue;
            source.Restore(before);
            state.Status = "地表已满或液体不同，来源保留";
            break;
        }
        if (accepted <= 0) { state.WorldLiquidOutputLiters.Remove(creditKey); return 0d; }
        double liters = accepted * litersPerServing;
        double credit = accepted == count ? Math.Min(litersPerServing, Math.Max(0d, carried + budget - liters)) : Math.Max(0d, carried - liters);
        charged = Math.Max(0d, liters + credit - carried);
        state.WorldLiquidOutputLiters[creditKey] = credit;
        return liters;
    }

    internal static bool TrySupplyOutletOxygen(MachineEntity node, CombustionSupportRequest request)
    {
        if (request.IsSupplied || !Contains(node) || !IsFluidActive(node) || fluidGraph == null) return false;
        foreach (FluidNetworkGraph.Edge edge in fluidGraph.GasEdges)
            if (edge.Source == node && edge.Environment && GetFluidDeviceBehavior(node).EmitsToEnvironment &&
                NormalizeMachineCell(node.Cell + MechanicalNetworkGraph.Directions[edge.Direction]) == request.Receiver.Entity.Cell &&
                EmitIndustrialOutlet(edge, FluidPhase.Gas, request.Seconds, FluidIds.Oxygen) > 0d)
                return request.TrySupply(request.FuelUnits, MachineCatalog.Settings.OxygenOutletHeatBonus);
        return false;
    }
    #endregion

    #region 正式环境与容器适配
    internal static bool TryFindAdjacentLiquidPort(MachineEntity node, int direction, ContainerPortDirection required,
        out ILiquidTransferPort port, out bool industrialNeighbor)
    {
        port = null;
        industrialNeighbor = false;
        if (!GameNetwork.HasStateAuthority || node == null || !Contains(node) || direction < 0 || direction > 3) return false;
        Vector2Int cell = graph.NormalizeCell(node.Cell + MechanicalNetworkGraph.Directions[direction]);
        MachineEntity adjacent = GetAtCurrentWorld(cell, 0);
        // 工业腔体和管段共用管网预算，不能经普通容器事务重复输出刚收到的液体。
        if (adjacent?.Definition.Fluid != null || fluidGraph?.PipeAt(cell) != null)
        { industrialNeighbor = true; return false; }
        if (adjacent == null || !Contains(adjacent)) return false;
        WakeForInteraction(adjacent);
        if (adjacent.Logic is not IContainerPortProvider provider) return false;
        var declared = new List<IContainerPort>(); provider.CollectContainerPorts(declared);
        foreach (IContainerPort candidate in declared)
            if (candidate is ILiquidTransferPort liquid && liquid.IsValid && (liquid.Configuration.Direction & required) != 0)
            { port = liquid; return true; }
        return false;
    }
    private static void RecordAtmosphereEmission(MachineEntity node, FluidBatch batch, Vector2? environmentPosition = null)
    {
        Vector2 emissionPoint = environmentPosition ?? node.Position;
        bool local = IsShipScope || SpaceSession.Current != null &&
            SpaceSession.Current.TryGetGroundEnvironment(worldKey, emissionPoint, out _);
        AtmosphereEmissionResult result = local
            ? RequireShipEnvironment().EmitMachineGas(shipScopeId ?? worldKey, emissionPoint, batch)
            : AtmosphereService.Emit(GetCurrentAtmosphere(node), batch);
        FluidMachineState state = GetFluidState(node);
        state.LastEmittedStandardLiters += FluidUnits.MolToStandardLiters(batch.GasMoles);
        state.LastRetainedStandardLiters += result.RetainedStandardLiters;
        state.LastEscapedStandardLiters += result.EscapedStandardLiters;
    }

    internal static AtmosphereState GetCurrentAtmosphere(MachineEntity node = null)
        => TryGetLocalAtmosphereKey(node, out string atmosphereKey) ? RequireShipEnvironment().GetMachineAirSnapshot(atmosphereKey, node?.Position ?? Vector2.zero)
            : AtmosphereService.TryGetForWorld(worldKey, out AtmosphereState atmosphere) ? atmosphere : null;

    #endregion
}
