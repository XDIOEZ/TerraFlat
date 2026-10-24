using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using UnityEngine;

public static partial class MachineWorld
{
    #region 气相独立库存预算
    private static Dictionary<FluidInventory, FluidInventoryState> gasStepStock => scope.GasStock;
    private static Dictionary<FluidInventory, FluidInventoryState> gasStockCache => scope.GasStockCache;
    private static Dictionary<FluidInventory, double> gasThroughputRemaining => scope.GasThroughput;

    internal static bool TryGetGasTransferBudget(FluidInventory inventory, out FluidInventoryState stock, out double liters)
    {
        bool exists = gasStepStock.TryGetValue(inventory, out stock);
        return gasThroughputRemaining.TryGetValue(inventory, out liters) && exists;
    }
    internal static void SetGasTransferBudget(FluidInventory inventory, double remaining)
        => gasThroughputRemaining[inventory] = Math.Max(0d, remaining);
    private static void ConsumeGasStepStock(FluidInventory inventory, string fluidId, decimal moles)
    {
        if (!gasStepStock.TryGetValue(inventory, out var stock)) return;
        FluidComponentState component = stock.Components.Find(value => FluidInventory.Same(value.FluidId, fluidId));
        if (component != null) component.GasMoles = Math.Max(0m, component.GasMoles - moles);
    }
    private static void ResetGasBudgets(bool clearCache = false)
    {
        gasStepStock.Clear(); gasThroughputRemaining.Clear(); scope.GasPortThroughput.Clear();
        if (clearCache) gasStockCache.Clear();
    }
    private static void RememberGasInventory(FluidInventory inventory, double litersPerSecond, float seconds)
    {
        if (gasStepStock.ContainsKey(inventory)) return;
        if (!gasStockCache.TryGetValue(inventory, out FluidInventoryState stock))
            gasStockCache.Add(inventory, stock = new FluidInventoryState());
        stock.CopyFrom(inventory.State, false);
        gasStepStock.Add(inventory, stock);
        gasThroughputRemaining.Add(inventory, Math.Max(0d, litersPerSecond * seconds));
    }
    private static double GasPortBudget(MachineEntity node, FluidMachinePortDefinition port, float seconds)
    {
        var key = (node.Id, port.Id);
        if (!scope.GasPortThroughput.TryGetValue(key, out double remaining))
            scope.GasPortThroughput.Add(key, remaining = node.Definition.Fluid.ThroughputLitersPerSecond * seconds);
        return remaining;
    }
    private static void ConsumeGasPort(MachineEntity node, FluidMachinePortDefinition port, double liters)
    {
        var key = (node.Id, port.Id);
        if (scope.GasPortThroughput.TryGetValue(key, out double remaining))
            scope.GasPortThroughput[key] = Math.Max(0d, remaining - liters);
    }
    #endregion

    #region 仅在气网边界结算气体
    private sealed class GasBoundaryWorkspace
    {
        public readonly string ReservationId;
        public readonly Func<string, bool> Candidates, OxygenCandidates;
        public readonly FluidInventoryState SourceBefore = new(), TargetBefore = new();
        public readonly IIndustrialGasReceiver Receiver;
        public GasBoundaryWorkspace(FluidNetworkGraph.Edge edge)
        {
            ReservationId = edge.Id + ":gas";
            Candidates = fluidId => CanReserveGasBoundary(edge, fluidId);
            OxygenCandidates = fluidId => FluidInventory.Same(fluidId, FluidIds.Oxygen) && Candidates(fluidId);
            if (edge.Target != null) Receiver = new IndustrialGasTransferPort(edge.Target, edge.TargetPort);
        }
    }
    private static readonly ConditionalWeakTable<FluidNetworkGraph.Edge, GasBoundaryWorkspace> gasBoundaries = new();

    private static bool CanReserveGasBoundary(FluidNetworkGraph.Edge edge, string fluidId)
    {
        FluidInventory inventory = GetPhaseFluidInventory(edge.Source, edge.SourcePort.Chamber, FluidPhase.Gas);
        if (!gasStepStock.TryGetValue(inventory, out var stock)) return false;
        FluidComponentState available = stock.Components.Find(value => FluidInventory.Same(value.FluidId, fluidId));
        return available?.GasMoles > 0m && CanRouteReservedFluid(edge, inventory, FluidPhase.Gas, fluidId);
    }

    // 普通管段之间没有这里的调用；只处理机器、筛选器、定向口和环境之间的气相事务。
    private static bool TryMoveGasBoundary(FluidNetworkGraph.Edge edge, float seconds, CombustionSupportRequest combustion = null)
    {
        if (!GameNetwork.HasStateAuthority || edge.Target != null && GetFluidDeviceBehavior(edge.Target).ReceivesOnDemand) return false;
        FluidMachineState state = GetFluidState(edge.Source);
        if (state.Ruptured || !string.IsNullOrEmpty(state.RuptureBudgetId) || state.OutletPhase == "liquid" ||
            !GetFluidDeviceBehavior(edge.Source).PortsEnabled(edge.Source) ||
            !GetFluidDeviceBehavior(edge.Source).CanOutput(edge.Source, out _)) return false;
        if (!PortAcceptsPhase(edge.SourcePort, FluidPhase.Gas) || edge.TargetPort != null && !PortAcceptsPhase(edge.TargetPort, FluidPhase.Gas)) return false;
        FluidInventory source = GetPhaseFluidInventory(edge.Source, edge.SourcePort.Chamber, FluidPhase.Gas);
        if (!TryGetGasTransferBudget(source, out FluidInventoryState stock, out double budget) || budget <= 0d || source.GasMoles <= 0m) return false;
        GasBoundaryWorkspace workspace = gasBoundaries.GetValue(edge, value => new GasBoundaryWorkspace(value));
        if (workspace.Receiver != null && ReferenceEquals(source.State, workspace.Receiver.StorageIdentity)) return false;
        budget = Math.Min(budget, GasPortBudget(edge.Source, edge.SourcePort, seconds));
        if (edge.Target != null) budget = Math.Min(budget, GasPortBudget(edge.Target, edge.TargetPort, seconds));
        if (budget <= 0d) return false;
        ReleaseUnsupportedReservationPhase(edge, source, workspace.ReservationId, FluidPhase.Gas);
        if (!source.TryReserve(workspace.ReservationId, FluidPhase.Gas,
            FluidUnits.StandardLitersToMol((decimal)edge.Source.Definition.Fluid.BatchStandardLiters),
            combustion == null ? workspace.Candidates : workspace.OxygenCandidates, out FluidBatch selected)) return false;
        if (!selected.IsGas || combustion != null && !FluidInventory.Same(selected.FluidId, FluidIds.Oxygen)) return false;
        FluidComponentState available = stock.Components.Find(value => FluidInventory.Same(value.FluidId, selected.FluidId));
        if (available == null || available.GasMoles <= 0m) return false;
        decimal maximum = Math.Min(selected.GasMoles, Math.Min(available.GasMoles, FluidUnits.StandardLitersToMol((decimal)budget)));
        if (workspace.Receiver != null) maximum = workspace.Receiver.GetReceivableGasMoles(selected.FluidId, maximum);
        if (workspace.Receiver != null && maximum > 0m)
            maximum = LimitGasByPressure(edge, source, workspace.Receiver, selected, maximum);
        if (maximum <= 0m) return BlockFluid(edge, "气体接收口已满或不接受当前组分");
        workspace.SourceBefore.CopyFrom(source.State);
        workspace.Receiver?.CaptureState(workspace.TargetBefore);
        if (!source.TryTakeReserved(workspace.ReservationId, maximum, out FluidBatch moved)) return false;
        if (workspace.Receiver != null)
        {
            try
            {
                if (!workspace.Receiver.InsertGas(moved))
                {
                    source.Restore(workspace.SourceBefore);
                    workspace.Receiver.RestoreState(workspace.TargetBefore);
                    return BlockFluid(edge, "气体接收事务失败，来源已保留");
                }
            }
            catch
            {
                source.Restore(workspace.SourceBefore);
                workspace.Receiver.RestoreState(workspace.TargetBefore);
                throw;
            }
            StateChanged(edge.Target);
        }
        else if (combustion == null)
            RecordAtmosphereEmission(edge.Source, moved,
                (Vector2)(edge.Source.Cell + MechanicalNetworkGraph.Directions[edge.Direction]) + Vector2.one * .5f);
        ConsumeGasStepStock(source, moved.FluidId, moved.GasMoles);
        double liters = (double)FluidUnits.MolToStandardLiters(moved.GasMoles);
        gasThroughputRemaining[source] = Math.Max(0d, gasThroughputRemaining[source] - liters);
        ConsumeGasPort(edge.Source, edge.SourcePort, liters);
        if (edge.Target != null) ConsumeGasPort(edge.Target, edge.TargetPort, liters);
        state.BlockedPorts.Remove(edge.SourcePort.Id); state.Status = "输送中";
        state.LastFlowDirection = edge.Direction; state.LastFlowPhase = "gas";
        if (combustion != null) combustion.TrySupply(combustion.FuelUnits, MachineCatalog.Settings.OxygenOutletHeatBonus);
        StateChanged(edge.Source);
        return true;
    }

    private static decimal LimitGasByPressure(FluidNetworkGraph.Edge edge, FluidInventory source,
        IIndustrialGasReceiver receiver, FluidBatch batch, decimal maximum)
    {
        double sourcePressure = source.GetPressureKPa(GetPhaseFluidVolumeLiters(edge.Source, FluidPhase.Gas),
            GetPhaseFluidMinimumGasSpaceLiters(edge.Source, FluidPhase.Gas));
        if (sourcePressure <= receiver.PressureKPa || source.GasMoles <= 0m) return 0m;
        bool Fits(decimal amount)
        {
            // 从等温均匀库存取走一份气体后，剩余温度不变；接收端按真实能量重新求压。
            double remainingPressure = sourcePressure * (double)(1m - amount / source.GasMoles);
            return receiver.TryGetPressureAfterAdding(batch.Scale(amount / batch.GasMoles), out double pressure) && pressure <= remainingPressure;
        }
        if (Fits(maximum)) return maximum;
        decimal low = 0m, high = maximum;
        for (int i = 0; i < 32; i++)
        {
            decimal middle = (low + high) / 2m;
            if (Fits(middle)) low = middle; else high = middle;
        }
        return low;
    }
    #endregion
}
