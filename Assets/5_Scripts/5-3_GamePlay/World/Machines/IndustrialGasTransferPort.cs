using System;
using FlatWorld.Networking;

/// <summary>工业气体接收接口；液体通过 ILiquidTransferPort 交换，不共用混相搬运事务。</summary>
public interface IIndustrialGasReceiver
{
    object StorageIdentity { get; }
    double PressureKPa { get; }
    bool TryGetPressureAfterAdding(FluidBatch batch, out double pressureKPa);
    decimal GetReceivableGasMoles(string fluidId, decimal requested);
    bool InsertGas(FluidBatch batch);
    void CaptureState(FluidInventoryState destination);
    void RestoreState(FluidInventoryState snapshot);
}

internal sealed class IndustrialGasTransferPort : IIndustrialGasReceiver
{
    #region 设备气口与独立气网适配
    private readonly MachineEntity node;
    private readonly FluidMachinePortDefinition port;
    private FluidInventory Inventory => MachineWorld.GetPhaseFluidInventory(node, port.Chamber, FluidPhase.Gas);
    public object StorageIdentity => Inventory.State;
    public double PressureKPa => Inventory.GetPressureKPa(MachineWorld.GetPhaseFluidVolumeLiters(node, FluidPhase.Gas),
        MachineWorld.GetPhaseFluidMinimumGasSpaceLiters(node, FluidPhase.Gas));
    public bool TryGetPressureAfterAdding(FluidBatch batch, out double pressureKPa)
        => Inventory.TryGetPressureAfterAdding(batch, MachineWorld.GetPhaseFluidVolumeLiters(node, FluidPhase.Gas),
            MachineWorld.GetPhaseFluidMinimumGasSpaceLiters(node, FluidPhase.Gas), out pressureKPa);
    public IndustrialGasTransferPort(MachineEntity node, FluidMachinePortDefinition port)
    { this.node = node; this.port = port; }

    public decimal GetReceivableGasMoles(string fluidId, decimal requested)
    {
        if (!GameNetwork.HasStateAuthority || !MachineWorld.Contains(node) || requested <= 0m ||
            port.Mode == "output" || port.Phase == "liquid" ||
            !string.IsNullOrEmpty(port.FluidId) && !FluidInventory.Same(port.FluidId, fluidId)) return 0m;
        FluidMachineState state = MachineWorld.GetFluidState(node);
        if (state.Ruptured || !string.IsNullOrEmpty(state.RuptureBudgetId) ||
            !MachineWorld.GetFluidDeviceBehavior(node).PortsEnabled(node)) return 0m;
        FluidInventory inventory = Inventory;
        bool single = node.Definition.Fluid.SingleGas && !MachineWorld.IsSharedGasNetworkMember(node);
        if (single)
            foreach (FluidComponentState component in inventory.State.Components)
                if (component.GasMoles > 0m && !FluidInventory.Same(component.FluidId, fluidId)) return 0m;
        decimal capacity = FluidInventory.MaximumMoles - inventory.GasMoles - inventory.LiquidMoles;
        if (single) capacity = Math.Min(capacity,
            FluidUnits.StandardLitersToMol((decimal)node.Definition.Fluid.GasBufferStandardLiters) - inventory.GasMoles);
        return Math.Min(requested, Math.Max(0m, capacity));
    }

    public bool InsertGas(FluidBatch batch)
    {
        if (!batch.IsGas || GetReceivableGasMoles(batch.FluidId, batch.GasMoles) < batch.GasMoles) return false;
        return Inventory.TryAdd(batch, MachineWorld.GetPhaseFluidVolumeLiters(node, FluidPhase.Gas),
            MachineWorld.GetPhaseFluidMinimumGasSpaceLiters(node, FluidPhase.Gas),
            node.Definition.Fluid.SingleGas && !MachineWorld.IsSharedGasNetworkMember(node), false);
    }
    public void CaptureState(FluidInventoryState destination) => destination.CopyFrom(Inventory.State);
    public void RestoreState(FluidInventoryState snapshot) => Inventory.Restore(snapshot);
    #endregion
}
