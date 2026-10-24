using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class MachineWorld
{
    #region 液体独立吞吐与原子搬运
    private static readonly ConditionalWeakTable<FluidInventory, FluidInventoryState> emissionSnapshots = new();
    private sealed class LiquidEdgeReservation
    {
        public readonly string Id;
        public readonly Func<string, bool> Candidates;
        public LiquidEdgeReservation(FluidNetworkGraph.Edge edge)
        {
            Id = edge.Id + ":liquid";
            Candidates = fluid => CanReserveLiquid(edge, fluid);
        }
    }
    private static readonly ConditionalWeakTable<FluidNetworkGraph.Edge, LiquidEdgeReservation> liquidReservations = new();
    private static void TryMoveLiquidEdge(FluidNetworkGraph.Edge edge, float seconds)
    {
        if (edge.Target != null && GetFluidDeviceBehavior(edge.Target).ReceivesOnDemand) return;
        FluidMachineState sourceState = GetFluidState(edge.Source);
        if (sourceState.Ruptured || !string.IsNullOrEmpty(sourceState.RuptureBudgetId)) return;
        if (edge.Target != null && (GetFluidState(edge.Target).Ruptured || !string.IsNullOrEmpty(GetFluidState(edge.Target).RuptureBudgetId))) return;
        if (!GetFluidDeviceBehavior(edge.Source).CanOutput(edge.Source, out string outputReason))
        { sourceState.Status = outputReason ?? sourceState.Status; return; }
        FluidInventory source = GetFluidInventory(edge.Source, edge.SourcePort.Chamber);
        if (source.LiquidMoles == 0m ||
            !fluidThroughputRemaining.TryGetValue(source, out double budget) || budget <= 0) return;
        if (edge.Target != null && ReferenceEquals(source, GetFluidInventory(edge.Target, edge.TargetPort.Chamber))) return;
        bool wholeWorldOutput = IsWorldLiquidOutput(edge);
        while (budget > 0d)
        {
            bool moved = TryReserveAndMoveLiquid(edge, source, budget);
            if (!moved || !wholeWorldOutput || sourceState.LastFlowPhase != "liquid" ||
                !fluidThroughputRemaining.TryGetValue(source, out budget)) return;
            // 同一口继续消耗剩余吞吐；不足下一份时由出口保存余额，不把速率硬限为每帧一份。
        }
    }

    private static bool TryReserveAndMoveLiquid(FluidNetworkGraph.Edge edge, FluidInventory source, double budget)
    {
        LiquidEdgeReservation filters = liquidReservations.GetValue(edge, value => new LiquidEdgeReservation(value));
        ReleaseUnsupportedReservationPhase(edge, source, filters.Id, FluidPhase.Liquid);
        if (!PortAcceptsPhase(edge.SourcePort, FluidPhase.Liquid) || edge.TargetPort != null && !PortAcceptsPhase(edge.TargetPort, FluidPhase.Liquid)) return false;
        FluidMachineState state = GetFluidState(edge.Source);
        if (state.OutletPhase == "gas" || source.LiquidMoles <= 0m) return false;
        string reservationId = filters.Id;
        bool wholeWorldOutput = IsWorldLiquidOutput(edge);
        var existing = source.GetReservation(reservationId);
        if (wholeWorldOutput && existing != null && existing.LiquidMoles < FluidUnits.ServingsToMol(source.Catalog.Find(existing.FluidId), 1m))
        { source.ReleaseReservation(reservationId); StateChanged(edge.Source); }
        bool alreadyReserved = source.GetReservation(reservationId) != null;
        if (!source.TryReserve(reservationId, FluidPhase.Liquid, FluidInventory.MaximumMoles, filters.Candidates, out FluidBatch batch))
        {
            if (!GetFluidDeviceBehavior(edge.Source).CanRoute(edge.Source, edge.SourcePort, FluidPhase.Liquid, null, out string routeReason) &&
                !string.IsNullOrEmpty(routeReason)) return BlockFluid(edge, routeReason);
            return false;
        }
        if (!batch.IsLiquid) return false;
        if (!alreadyReserved)
        {
            FluidOutputReservation reservation = source.GetReservation(reservationId);
            reservation.LiquidMoles = Math.Min(reservation.LiquidMoles, FluidUnits.ServingsToMol(source.Catalog.Find(batch.FluidId),
                (decimal)(wholeWorldOutput ? Math.Max(1d, edge.Source.Definition.Fluid.BatchLiquidServings) : edge.Source.Definition.Fluid.BatchLiquidServings)));
        }
        return MoveReservedLiquid(edge, source, reservationId, budget);
    }

    // 每条边复用筛选器，抽签前同时检查物性、两端端口与设备路由，避免留下永远送不出的错误批次。
    private static bool CanReserveLiquid(FluidNetworkGraph.Edge edge, string fluidId)
    {
        FluidInventory inventory = GetFluidInventory(edge.Source, edge.SourcePort.Chamber);
        if (!fluidStepStock.TryGetValue(inventory, out FluidInventoryState beginning)) return false;
        bool availableAtStart = false;
        decimal minimum = IsWorldLiquidOutput(edge)
            ? FluidUnits.ServingsToMol(inventory.Catalog.Find(fluidId), 1m) : 0m;
        foreach (FluidComponentState component in beginning.Components)
            if (FluidInventory.Same(component.FluidId, fluidId) &&
                component.LiquidMoles > 0m && component.LiquidMoles >= minimum)
            { availableAtStart = true; break; }
        if (!availableAtStart || inventory.GetAvailableMoles(fluidId, FluidPhase.Liquid) < minimum) return false;
        return CanRouteReservedFluid(edge, inventory, FluidPhase.Liquid, fluidId);
    }

    private static bool IsWorldLiquidOutput(FluidNetworkGraph.Edge edge)
        => edge.Environment && !TryFindAdjacentLiquidPort(edge.Source, edge.Direction,
            ContainerPortDirection.Input, out _, out bool industrialTarget) && !industrialTarget;

    private static bool MoveReservedLiquid(FluidNetworkGraph.Edge edge, FluidInventory source, string reservationId, double budget)
    {
        FluidOutputReservation reservation = source.GetReservation(reservationId);
        if (reservation == null || reservation.LiquidMoles <= 0 || reservation.GasMoles != 0m) return false;
        FluidMachineState state = GetFluidState(edge.Source);
        FluidDefinition substance = source.Catalog.Find(reservation.FluidId);
        if (!substance.AllowIndustrialTransport) return BlockFluid(edge, "这种物质不能经过基础管道");
        if (!PortAcceptsPhase(edge.SourcePort, FluidPhase.Liquid) ||
            edge.TargetPort != null && !PortAcceptsPhase(edge.TargetPort, FluidPhase.Liquid))
            return BlockFluid(edge, "当前批次的相态不能从这个口输出");
        if (!MatchesFluid(edge.SourcePort, substance.Id) || edge.TargetPort != null && !MatchesFluid(edge.TargetPort, substance.Id))
            return BlockFluid(edge, "这个口不接收当前物质");
        FluidDeviceBehavior behavior = GetFluidDeviceBehavior(edge.Source);
        if (reservation.LiquidMoles > 0 && !behavior.CanRoute(edge.Source, edge.SourcePort, FluidPhase.Liquid, substance.Id, out string liquidReason))
            return string.IsNullOrEmpty(liquidReason) ? false : BlockFluid(edge, liquidReason);
        if (!fluidStepStock.TryGetValue(source, out FluidInventoryState beginning)) return false;
        FluidComponentState stock = null;
        foreach (FluidComponentState component in beginning.Components)
            if (component.FluidId == substance.Id) { stock = component; break; }
        if (stock == null) return false;
        decimal fraction = 1;
        if (reservation.LiquidMoles > 0) fraction = Math.Min(fraction, stock.LiquidMoles / reservation.LiquidMoles);
        double fullLiters = (double)FluidUnits.MolToLiquidLiters(substance, reservation.LiquidMoles);
        if (fullLiters <= 0) return false;
        decimal stockMaximum = reservation.TotalMoles * Math.Max(0, fraction);
        fraction = Math.Min(fraction, (decimal)Math.Min(1, budget / fullLiters));
        decimal maximum = reservation.TotalMoles * Math.Max(0, fraction);
        if (maximum <= 0) return false;
        double carriedOutputLiters = 0d;
        FluidBatch moved;
        if (edge.Environment)
        {
            bool industrialTarget = false;
            if (reservation.LiquidMoles > 0 && TryFindAdjacentLiquidPort(edge.Source, edge.Direction,
                ContainerPortDirection.Input, out ILiquidTransferPort vessel, out industrialTarget))
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
            else
            {
                if (industrialTarget) return BlockFluid(edge, "工业管网接口未连通");
                if (reservation.LiquidMoles > 0)
                {
                    decimal wholeMoles = FluidUnits.ServingsToMol(substance, 1m);
                    if (reservation.GasMoles > 0 || stockMaximum < wholeMoles) return false;
                    double wholeLiters = (double)FluidUnits.MolToLiquidLiters(substance, wholeMoles);
                    double carried = state.WorldLiquidOutputLiters.GetValueOrDefault(edge.SourcePort.Id);
                    if (carried + budget + 1e-10 < wholeLiters)
                    {
                        double increment = Math.Min(budget, Math.Max(0d, wholeLiters - carried));
                        state.WorldLiquidOutputLiters[edge.SourcePort.Id] = carried + increment;
                        fluidThroughputRemaining[source] = Math.Max(0d, budget - increment);
                        state.Status = "正在累计整份排液"; StateChanged(edge.Source); return false;
                    }
                    maximum = wholeMoles;
                    carriedOutputLiters = Math.Min(carried, wholeLiters);
                }
                if (!EmitReservedLiquid(edge, source, reservationId, maximum, out moved)) return BlockFluid(edge, "地表不能接收这份液体");
            }
        }
        else
        {
            FluidInventory target = GetFluidInventory(edge.Target, edge.TargetPort.Chamber);
            if (ReferenceEquals(source, target)) return false;
            FluidMachineDefinition targetDefinition = edge.Target.Definition.Fluid;
            if (reservation.LiquidMoles > 0)
            {
                decimal freeMoles = FluidUnits.LiquidLitersToMol(substance, (decimal)Math.Max(0,
                    GetFluidVolumeLiters(edge.Target) - GetFluidMinimumGasSpaceLiters(edge.Target) - target.GetLiquidLiters()));
                maximum = Math.Min(maximum, freeMoles * reservation.TotalMoles / reservation.LiquidMoles);
            }
            if (maximum <= 0 || !source.TryTransfer(reservationId, target, maximum, GetFluidVolumeLiters(edge.Target),
                GetFluidMinimumGasSpaceLiters(edge.Target), out moved, false, targetDefinition.SingleLiquid))
                return BlockFluid(edge, "出口容量不足或相槽里还有其他物质");
            StateChanged(edge.Target);
        }
        stock.LiquidMoles = Math.Max(0, stock.LiquidMoles - moved.LiquidMoles);
        double consumed = (double)FluidUnits.MolToLiquidLiters(substance, moved.LiquidMoles);
        fluidThroughputRemaining[source] = Math.Max(0, budget - Math.Max(0d, consumed - carriedOutputLiters));
        if (carriedOutputLiters > 0)
            state.WorldLiquidOutputLiters[edge.SourcePort.Id] = Math.Max(0d, state.WorldLiquidOutputLiters.GetValueOrDefault(edge.SourcePort.Id) - carriedOutputLiters);
        state.BlockedPorts.Remove(edge.SourcePort.Id); state.Status = "输送中";
        state.LastFlowDirection = edge.Direction;
        state.LastFlowPhase = "liquid";
        StateChanged(edge.Source);
        return true;
    }

    private static bool EmitReservedLiquid(FluidNetworkGraph.Edge edge, FluidInventory source, string reservationId, decimal maximum, out FluidBatch moved)
    {
        moved = default;
        FluidOutputReservation reservation = source.GetReservation(reservationId);
        if (reservation == null || reservation.GasMoles != 0m || reservation.LiquidMoles <= 0m) return false;
        FluidDefinition substance = source.Catalog.Find(reservation.FluidId);
        FluidInventoryState before = null;
        if (reservation.LiquidMoles > 0m)
        {
            before = emissionSnapshots.GetValue(source, _ => new FluidInventoryState());
            before.CopyFrom(source.State);
        }
        if (!source.TryTakeReserved(reservationId, maximum, out moved)) return false;
        if (moved.LiquidMoles > 0)
        {
            Vector2 destination = (Vector2)(edge.Source.Cell + MechanicalNetworkGraph.Directions[edge.Direction]) + Vector2.one * .5f;
            string destinationWorld = null; Vector2 destinationPosition = destination;
            LiquidDefinition liquid = null;
            if (string.IsNullOrEmpty(substance.LiquidId) || GameRes.ExistingInstance == null ||
                !GameRes.ExistingInstance.TryGetLiquidDefinition(substance.LiquidId, out liquid) || liquid.WorldWater == null ||
                !TryResolveFluidSurfacePoint(destination, out destinationWorld, out destinationPosition) &&
                (IsShipScope || worldKey != SceneManager.GetActiveScene().name))
            { source.Restore(before); moved = default; return false; }
            decimal oneServing = FluidUnits.ServingsToMol(substance, 1m);
            if (Math.Abs(moved.LiquidMoles - oneServing) > .000000000000000000000001m)
            { source.Restore(before); moved = default; return false; }
            int accepted = ReleaseFluidEnvironmentLiquid(destinationWorld ?? worldKey, destinationPosition, substance.LiquidId, 1);
            if (accepted != 1) { source.Restore(before); moved = default; return false; }
        }
        return true;
    }

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

    #region 出气口氧气助燃
    internal static bool TrySupplyOutletOxygen(MachineEntity node, CombustionSupportRequest request)
    {
        if (request.IsSupplied || !Contains(node) || !IsFluidActive(node) || fluidGraph == null) return false;
        FluidMachineState state = GetFluidState(node);
        FluidDeviceBehavior behavior = GetFluidDeviceBehavior(node);
        if (state.Ruptured || !string.IsNullOrEmpty(state.RuptureBudgetId) || !behavior.EmitsToEnvironment ||
            !behavior.PortsEnabled(node) || !behavior.CanOutput(node, out _)) return false;
        foreach (FluidNetworkGraph.Edge edge in fluidGraph.Edges)
        {
            if (edge.Source != node || !edge.Environment || !edge.SourcePort.Outlet ||
                !PortAcceptsPhase(edge.SourcePort, FluidPhase.Gas) || !fluidGraph.CanRoute(edge) ||
                NormalizeMachineCell(node.Cell + MechanicalNetworkGraph.Directions[edge.Direction]) != request.Receiver.Entity.Cell) continue;
            FluidInventory inventory = GetPhaseFluidInventory(node, edge.SourcePort.Chamber, FluidPhase.Gas);
            if (!TryGetGasTransferBudget(inventory, out _, out double budget) || budget <= 0) continue;
            // 助燃复用实际气包和本轮吞吐结算，不从混合库存中特意抽氧，也不重复排入大气。
            if (TryMoveGasBoundary(edge, request.Seconds, request) && request.IsSupplied) return true;
        }
        return false;
    }
    #endregion

    #region 显式环境排放
    public static bool VentFluidPipe(MachineEntity node)
    {
        if (!GameNetwork.HasStateAuthority || node?.Definition.Fluid == null || node.Definition.Layer != 2 || !Contains(node)) return false;
        FluidInventory inventory = GetPhaseFluidInventory(node, "main", FluidPhase.Gas);
        inventory.ClearGas(out var batches);
        FluidMachineState state = GetFluidState(node);
        state.LastEmittedStandardLiters = 0; state.LastRetainedStandardLiters = 0; state.LastEscapedStandardLiters = 0;
        foreach (FluidBatch batch in batches) RecordAtmosphereEmission(node, batch);
        state.Status = "已排出连通气网的气体";
        StateChanged(node);
        return true;
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
