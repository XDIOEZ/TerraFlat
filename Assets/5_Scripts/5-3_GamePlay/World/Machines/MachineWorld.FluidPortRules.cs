using System;

public static partial class MachineWorld
{
    #region 共用的端口声明校验，不执行气液混相结算
    private static bool CanRouteReservedFluid(FluidNetworkGraph.Edge edge, FluidInventory inventory, FluidPhase phase, string fluidId)
    {
        string phaseName = phase == FluidPhase.Gas ? "gas" : "liquid";
        string outletPhase = GetFluidState(edge.Source).OutletPhase;
        return (outletPhase == "both" || outletPhase == phaseName) &&
            PortAcceptsPhase(edge.SourcePort, phase) && (edge.TargetPort == null || PortAcceptsPhase(edge.TargetPort, phase)) &&
            !string.IsNullOrEmpty(fluidId) && inventory.Catalog.TryGet(fluidId, out FluidDefinition definition) &&
            definition.AllowIndustrialTransport && MatchesFluid(edge.SourcePort, fluidId) &&
            (edge.TargetPort == null || MatchesFluid(edge.TargetPort, fluidId)) &&
            GetFluidDeviceBehavior(edge.Source).CanRoute(edge.Source, edge.SourcePort, phase, fluidId, out _);
    }

    private static void ReleaseUnsupportedReservationPhase(FluidNetworkGraph.Edge edge, FluidInventory source, string reservationId, FluidPhase slotPhase)
    {
        FluidOutputReservation reservation = source.GetReservation(reservationId);
        if (reservation == null) return;
        bool changed = false;
        if (reservation.GasMoles > 0m && (slotPhase != FluidPhase.Gas || !CanRouteReservedFluid(edge, source, FluidPhase.Gas, reservation.FluidId)))
        { reservation.GasMoles = 0m; changed = true; }
        if (reservation.LiquidMoles > 0m && (slotPhase != FluidPhase.Liquid || !CanRouteReservedFluid(edge, source, FluidPhase.Liquid, reservation.FluidId)))
        { reservation.LiquidMoles = 0m; changed = true; }
        if (!changed) return;
        if (reservation.TotalMoles == 0m) source.ReleaseReservation(reservationId);
        StateChanged(edge.Source);
    }

    private static bool BlockFluid(FluidNetworkGraph.Edge edge, string reason)
    {
        FluidMachineState state = GetFluidState(edge.Source);
        state.BlockedPorts[edge.SourcePort.Id] = reason; state.Status = reason;
        return false;
    }
    private static bool PortAcceptsPhase(FluidMachinePortDefinition port, FluidPhase phase)
        => port.Phase == "both" || port.Phase == (phase == FluidPhase.Gas ? "gas" : "liquid");
    private static bool MatchesFluid(FluidMachinePortDefinition port, string id)
        => string.IsNullOrEmpty(port.FluidId) || string.Equals(port.FluidId, id, StringComparison.OrdinalIgnoreCase);
    #endregion
}
