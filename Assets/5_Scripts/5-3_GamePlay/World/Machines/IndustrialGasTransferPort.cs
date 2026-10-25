using System;
using FlatWorld.Networking;

/// <summary>工业气体接收接口；液体通过 ILiquidTransferPort 交换，不共用混相搬运事务。</summary>
public interface IIndustrialGasReceiver
{
    object StorageIdentity { get; }
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
    public IndustrialGasTransferPort(MachineEntity node, FluidMachinePortDefinition port)
    { this.node = node; this.port = port; }

    public decimal GetReceivableGasMoles(string fluidId, decimal requested)
    {
        if (!GameNetwork.HasStateAuthority || !MachineWorld.Contains(node) || requested <= 0m ||
            port.Mode == "output" || port.Phase == "liquid" ||
            !string.IsNullOrEmpty(port.FluidId) && !FluidInventory.Same(port.FluidId, fluidId)) return 0m;
        FluidMachineState state = MachineWorld.GetFluidState(node);
        if (state.Ruptured || !MachineWorld.GetFluidDeviceBehavior(node).PortsEnabled(node)) return 0m;
        decimal capacity = FluidUnits.StandardLitersToMol((decimal)MachineWorld.GetIndustrialFreeCapacity(node, FluidPhase.Gas, fluidId, port.Chamber));
        return Math.Min(requested, Math.Max(0m, capacity));
    }

    public bool InsertGas(FluidBatch batch)
    {
        if (!batch.IsGas || GetReceivableGasMoles(batch.FluidId, batch.GasMoles) < batch.GasMoles) return false;
        return Inventory.TryAdd(batch, MachineWorld.GetPhaseFluidVolumeLiters(node, FluidPhase.Gas),
            MachineWorld.GetPhaseFluidMinimumGasSpaceLiters(node, FluidPhase.Gas),
            false, true);
    }
    public void CaptureState(FluidInventoryState destination) => destination.CopyFrom(Inventory.State);
    public void RestoreState(FluidInventoryState snapshot) => Inventory.Restore(snapshot);
    #endregion
}
