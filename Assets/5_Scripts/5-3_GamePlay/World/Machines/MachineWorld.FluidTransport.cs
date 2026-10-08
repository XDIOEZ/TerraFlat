using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class MachineWorld
{
    #region 共享吞吐与原子搬运
    private static void TryMoveFluidEdge(FluidNetworkGraph.Edge edge, float seconds)
    {
        FluidMachineState sourceState = GetFluidState(edge.Source);
        if (sourceState.Ruptured || !string.IsNullOrEmpty(sourceState.RuptureBudgetId)) return;
        if (edge.Target != null && (GetFluidState(edge.Target).Ruptured || !string.IsNullOrEmpty(GetFluidState(edge.Target).RuptureBudgetId))) return;
        if (edge.Source.Definition.Fluid.Kind == "gas-pump" && IsGasPumpWet(edge.Source))
        { sourceState.Status = "气泵遇到液体"; return; }
        FluidInventory source = GetFluidInventory(edge.Source, edge.SourcePort.Chamber);
        if (source.IsEmpty || !fluidThroughputRemaining.TryGetValue(source, out double budget) || budget <= 0) return;
        bool vertical = (edge.Direction & 1) == 1;
        FluidPhase preferred = vertical ? FluidPhase.Liquid : FluidPhase.Gas;
        if (edge.SourcePort.Phase == "gas") preferred = FluidPhase.Gas;
        if (edge.SourcePort.Phase == "liquid") preferred = FluidPhase.Liquid;
        if (TryReserveAndMove(edge, source, preferred, budget, seconds)) return;
        // 每口的气液子槽分别保留已抽出的物质，堵住首选相时另一相仍按同一吞吐预算回退。
        TryReserveAndMove(edge, source, preferred == FluidPhase.Gas ? FluidPhase.Liquid : FluidPhase.Gas, budget, seconds);
    }

    private static bool TryReserveAndMove(FluidNetworkGraph.Edge edge, FluidInventory source, FluidPhase phase, double budget, float seconds)
    {
        if (!PortAcceptsPhase(edge.SourcePort, phase) || edge.TargetPort != null && !PortAcceptsPhase(edge.TargetPort, phase)) return false;
        FluidMachineState state = GetFluidState(edge.Source);
        if (state.OutletPhase != "both" && state.OutletPhase != (phase == FluidPhase.Gas ? "gas" : "liquid")) return false;
        if (phase == FluidPhase.Gas && source.GasMoles <= 0 || phase == FluidPhase.Liquid && source.LiquidMoles <= 0) return false;
        string reservationId = FluidEdgeReservationId(edge, phase);
        if (edge.Source.Definition.Fluid.Kind == "selector")
        {
            if (phase != FluidPhase.Gas || string.IsNullOrEmpty(state.SelectedGasId)) return false;
            string fluidId = source.GetReservation(reservationId)?.FluidId;
            if (fluidId == null)
                foreach (FluidComponentState component in source.State.Components)
                    if (component.GasMoles > 0) { fluidId = component.FluidId; break; }
            if (fluidId == null || string.Equals(state.SelectedGasId, fluidId, StringComparison.OrdinalIgnoreCase) != (edge.SourcePort.Id == "selected")) return false;
        }
        decimal maximum = phase == FluidPhase.Gas
            ? FluidUnits.StandardLitersToMol((decimal)edge.Source.Definition.Fluid.BatchStandardLiters) : FluidInventory.MaximumMoles;
        bool alreadyReserved = source.GetReservation(reservationId) != null;
        if (!source.TryReserve(reservationId, phase, maximum, out FluidBatch batch)) return false;
        if (phase == FluidPhase.Liquid && !alreadyReserved)
        {
            FluidOutputReservation reservation = source.GetReservation(reservationId);
            reservation.LiquidMoles = Math.Min(reservation.LiquidMoles, FluidUnits.ServingsToMol(source.Catalog.Find(batch.FluidId),
                (decimal)edge.Source.Definition.Fluid.BatchLiquidServings));
        }
        return MoveReservedFluid(edge, source, reservationId, budget, seconds);
    }

    private static string FluidEdgeReservationId(FluidNetworkGraph.Edge edge, FluidPhase phase)
        => edge.Id + (phase == FluidPhase.Gas ? ":gas" : ":liquid");

    private static bool MoveReservedFluid(FluidNetworkGraph.Edge edge, FluidInventory source, string reservationId, double budget, float seconds)
    {
        FluidOutputReservation reservation = source.GetReservation(reservationId);
        if (reservation == null || reservation.TotalMoles <= 0) return false;
        FluidMachineState state = GetFluidState(edge.Source);
        FluidDefinition substance = source.Catalog.Find(reservation.FluidId);
        if (!substance.AllowIndustrialTransport) return BlockFluid(edge, "这种物质不能经过基础管道");
        if (reservation.GasMoles > 0 && (!PortAcceptsPhase(edge.SourcePort, FluidPhase.Gas) ||
            edge.TargetPort != null && !PortAcceptsPhase(edge.TargetPort, FluidPhase.Gas)) ||
            reservation.LiquidMoles > 0 && (!PortAcceptsPhase(edge.SourcePort, FluidPhase.Liquid) ||
            edge.TargetPort != null && !PortAcceptsPhase(edge.TargetPort, FluidPhase.Liquid)))
            return BlockFluid(edge, "当前批次的相态不能从这个口输出");
        if (!MatchesFluid(edge.SourcePort, substance.Id) || edge.TargetPort != null && !MatchesFluid(edge.TargetPort, substance.Id))
            return BlockFluid(edge, "这个口不接收当前物质");
        if (edge.Source.Definition.Fluid.Kind == "selector")
        {
            if (string.IsNullOrEmpty(state.SelectedGasId)) return BlockFluid(edge, "请先选择目标气体");
            bool selected = string.Equals(state.SelectedGasId, substance.Id, StringComparison.OrdinalIgnoreCase);
            if (selected != (edge.SourcePort.Id == "selected")) return false;
        }
        if (!fluidStepStock.TryGetValue(source, out FluidInventoryState beginning)) return false;
        FluidComponentState stock = null;
        foreach (FluidComponentState component in beginning.Components)
            if (component.FluidId == substance.Id) { stock = component; break; }
        if (stock == null) return false;
        decimal fraction = 1;
        if (reservation.GasMoles > 0) fraction = Math.Min(fraction, stock.GasMoles / reservation.GasMoles);
        if (reservation.LiquidMoles > 0) fraction = Math.Min(fraction, stock.LiquidMoles / reservation.LiquidMoles);
        double fullLiters = (double)FluidUnits.MolToStandardLiters(reservation.GasMoles) +
            (double)FluidUnits.MolToLiquidLiters(substance, reservation.LiquidMoles);
        if (fullLiters <= 0) return false;
        fraction = Math.Min(fraction, (decimal)Math.Min(1, budget / fullLiters));
        decimal maximum = reservation.TotalMoles * Math.Max(0, fraction);
        if (maximum <= 0) return false;
        FluidBatch moved;
        if (edge.Environment)
        {
            if (reservation.LiquidMoles > 0 && TryFindAdjacentVesselPort(edge.Source, edge.Direction, ContainerPortDirection.Input, out ILiquidTransferPort vessel))
            {
                if (reservation.GasMoles > 0 || edge.Source.Logic is not FluidMachineLogic logic) return BlockFluid(edge, "目标容器只接收液体");
                FluidMachinePortDefinition output = edge.SourcePort;
                var industrial = new IndustrialLiquidTransferPort(logic, output, reservationId);
                float requested = (float)FluidUnits.MolToServings(substance, maximum);
                var result = ContainerTransferService.TransferLiquid(industrial, vessel, new ContainerTransferContext(edge.Source.Position,
                    SceneManager.GetSceneByName(worldKey).handle, ContainerAccessKind.Machine, "工业出气口输出"), requested);
                if (!result.Success) return BlockFluid(edge, "目标容器容量不足或拒收当前液体");
                moved = FluidInventory.CreateBatch(substance, 0, FluidUnits.ServingsToMol(substance, (decimal)result.LiquidServings),
                    source.GetTemperatureKelvin());
            }
            else if (!EmitReservedFluid(edge, source, reservationId, maximum, out moved)) return BlockFluid(edge, "地表不能接收这份液体");
        }
        else
        {
            FluidInventory target = GetFluidInventory(edge.Target, edge.TargetPort.Chamber);
            if (ReferenceEquals(source, target)) return false;
            FluidMachineDefinition targetDefinition = edge.Target.Definition.Fluid;
            if (targetDefinition.SingleGas && reservation.GasMoles > 0)
            {
                decimal remainingGas = FluidUnits.StandardLitersToMol((decimal)targetDefinition.GasBufferStandardLiters) - target.GasMoles;
                maximum = Math.Min(maximum, Math.Max(0, remainingGas) * reservation.TotalMoles / reservation.GasMoles);
            }
            if (reservation.LiquidMoles > 0)
            {
                decimal freeMoles = FluidUnits.LiquidLitersToMol(substance, (decimal)Math.Max(0,
                    GetFluidVolumeLiters(edge.Target) - GetFluidMinimumGasSpaceLiters(edge.Target) - target.GetLiquidLiters()));
                maximum = Math.Min(maximum, freeMoles * reservation.TotalMoles / reservation.LiquidMoles);
            }
            if (maximum <= 0 || !source.TryTransfer(reservationId, target, maximum, GetFluidVolumeLiters(edge.Target),
                GetFluidMinimumGasSpaceLiters(edge.Target), out moved, targetDefinition.SingleGas, targetDefinition.SingleLiquid))
                return BlockFluid(edge, "出口容量不足或相槽里还有其他物质");
            StateChanged(edge.Target);
        }
        stock.GasMoles = Math.Max(0, stock.GasMoles - moved.GasMoles);
        stock.LiquidMoles = Math.Max(0, stock.LiquidMoles - moved.LiquidMoles);
        double consumed = (double)FluidUnits.MolToStandardLiters(moved.GasMoles) + (double)FluidUnits.MolToLiquidLiters(substance, moved.LiquidMoles);
        fluidThroughputRemaining[source] = Math.Max(0, budget - consumed);
        state.BlockedPorts.Remove(edge.SourcePort.Id); state.Status = "输送中";
        state.LastFlowDirection = edge.Direction;
        state.LastFlowPhase = moved.GasMoles > 0 && moved.LiquidMoles > 0 ? "both" : moved.GasMoles > 0 ? "gas" : "liquid";
        StateChanged(edge.Source);
        return true;
    }

    private static bool EmitReservedFluid(FluidNetworkGraph.Edge edge, FluidInventory source, string reservationId, decimal maximum, out FluidBatch moved)
    {
        moved = default;
        FluidOutputReservation reservation = source.GetReservation(reservationId);
        if (reservation == null) return false;
        FluidDefinition substance = source.Catalog.Find(reservation.FluidId);
        FluidInventoryState before = source.State.Clone();
        if (!source.TryTakeReserved(reservationId, maximum, out moved)) return false;
        if (moved.LiquidMoles > 0)
        {
            Vector2 destination = (Vector2)(edge.Source.Cell + MechanicalNetworkGraph.Directions[edge.Direction]) + Vector2.one * .5f;
            if (string.IsNullOrEmpty(substance.LiquidId) || GameRes.ExistingInstance == null ||
                !GameRes.ExistingInstance.TryGetLiquidDefinition(substance.LiquidId, out LiquidDefinition liquid) || liquid.WorldWater == null ||
                !WorldLiquidSystem.TryPour(destination, substance.LiquidId,
                    (float)(FluidUnits.MolToServings(substance, moved.LiquidMoles) * (decimal)liquid.WorldWater.DepthPerServing), out _))
            { source.Restore(before); moved = default; return false; }
        }
        if (moved.GasMoles > 0) RecordAtmosphereEmission(edge.Source, FluidInventory.CreateBatch(substance, moved.GasMoles, 0, source.IsEmpty
            ? BatchTemperature(substance, moved) : source.GetTemperatureKelvin()));
        return true;
    }

    private static bool BlockFluid(FluidNetworkGraph.Edge edge, string reason)
    {
        FluidMachineState state = GetFluidState(edge.Source);
        state.BlockedPorts[edge.SourcePort.Id] = reason; state.Status = reason;
        return false;
    }
    private static bool TryFindAdjacentVesselPort(MachineEntity node, int direction, ContainerPortDirection required,
        out ILiquidTransferPort port)
    {
        port = null;
        MachineEntity adjacent = GetAtCurrentWorld(graph.NormalizeCell(node.Cell + MechanicalNetworkGraph.Directions[direction]), 0);
        if (adjacent?.Logic is not VesselLogic vessel) return false;
        var declared = new List<IContainerPort>(); vessel.CollectContainerPorts(declared);
        foreach (IContainerPort candidate in declared)
            if (candidate is ILiquidTransferPort liquid && liquid.IsValid && (liquid.Configuration.Direction & required) != 0)
            { port = liquid; return true; }
        return false;
    }
    private static bool PortAcceptsPhase(FluidMachinePortDefinition port, FluidPhase phase)
        => port.Phase == "both" || port.Phase == (phase == FluidPhase.Gas ? "gas" : "liquid");
    private static bool MatchesFluid(FluidMachinePortDefinition port, string id)
        => string.IsNullOrEmpty(port.FluidId) || string.Equals(port.FluidId, id, StringComparison.OrdinalIgnoreCase);
    private static double BatchTemperature(FluidDefinition definition, FluidBatch batch)
        => (batch.InternalEnergyJoules - (double)batch.GasMoles * definition.GasEnergyOffsetJPerMol) /
           ((double)batch.GasMoles * definition.GasHeatCapacityJPerMolKelvin + (double)batch.LiquidMoles * definition.LiquidHeatCapacityJPerMolKelvin);

    private static IEnumerable<FluidBatch> FluidInventoryBatches(FluidInventory inventory)
    {
        double temperature = inventory.GetTemperatureKelvin();
        foreach (FluidComponentState component in inventory.State.Components)
            if (component.GasMoles + component.LiquidMoles > 0)
                yield return FluidInventory.CreateBatch(inventory.Catalog.Find(component.FluidId), component.GasMoles, component.LiquidMoles, temperature);
    }
    #endregion

    #region 显式环境排气与泵遇液停机
    public static bool VentFluidPipe(MachineEntity node)
    {
        if (!GameNetwork.HasStateAuthority || node?.Definition.Fluid == null || node.Definition.Layer != 2 || !Contains(node)) return false;
        FluidInventory inventory = GetFluidInventory(node);
        inventory.ClearGas(out var batches);
        FluidMachineState state = GetFluidState(node);
        state.LastEmittedStandardLiters = 0; state.LastRetainedStandardLiters = 0; state.LastEscapedStandardLiters = 0;
        foreach (FluidBatch batch in batches) RecordAtmosphereEmission(node, batch);
        state.Status = "已排出选中管段的气体";
        StateChanged(node);
        return true;
    }

    private static void RecordAtmosphereEmission(MachineEntity node, FluidBatch batch)
    {
        AtmosphereEmissionResult result = AtmosphereService.Emit(GetCurrentAtmosphere(), batch);
        FluidMachineState state = GetFluidState(node);
        state.LastEmittedStandardLiters += FluidUnits.MolToStandardLiters(batch.GasMoles);
        state.LastRetainedStandardLiters += result.RetainedStandardLiters;
        state.LastEscapedStandardLiters += result.EscapedStandardLiters;
    }

    internal static AtmosphereState GetCurrentAtmosphere()
        => AtmosphereService.TryGetForWorld(worldKey, out AtmosphereState atmosphere) ? atmosphere : null;

    public static bool IsGasPumpWet(MachineEntity pump)
    {
        if (GetFluidInventory(pump).LiquidMoles > 0) return true;
        if (WorldLiquidSourceResolver.TryResolve((Vector2)pump.Cell + Vector2.one * .5f, out _)) return true;
        if (fluidGraph != null)
            foreach (FluidNetworkGraph.Edge edge in fluidGraph.Edges)
                if (ReferenceEquals(edge.Source, pump) && edge.Target != null && GetFluidInventory(edge.Target, edge.TargetPort.Chamber).LiquidMoles > 0)
                    return true;
        return false;
    }
    #endregion
}

internal static class FluidIndustrialStateBridge
{
    #region 组合库存预留承接
    public static void MergeReservations(FluidInventoryState target, FluidInventoryState source)
    {
        foreach (FluidOutputReservation reservation in source.Ports)
        {
            foreach (FluidOutputReservation existing in target.Ports)
                if (existing.PortId == reservation.PortId) throw new InvalidOperationException("组合罐端口预留重复：" + reservation.PortId);
            target.Ports.Add(reservation.Clone());
        }
    }
    #endregion
}
