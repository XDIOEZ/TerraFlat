using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>工业摩尔库存与普通容器份数通过同一原子端口换算，不复制任何组分。</summary>
public sealed class IndustrialLiquidTransferPort : ContainerPortBase, ILiquidTransferPort
{
    #region 工业液相适配
    private readonly FluidMachineLogic logic;
    private readonly string chamber;
    private readonly string reservationId;
    private FluidInventory Inventory => MachineWorld.GetFluidInventory(logic.Entity, chamber);
    public IndustrialLiquidTransferPort(FluidMachineLogic logic, FluidMachinePortDefinition port, string reservationId = null,
        ContainerAccessKind access = ContainerAccessKind.Manual | ContainerAccessKind.Machine | ContainerAccessKind.Mod)
        : base("machine:" + logic.Entity.Id, port.Id, new ContainerPortConfiguration
        {
            Id = port.Id, Type = "core:industrial_liquid", Reach = 2, Access = access,
            Direction = port.Mode == "input" ? ContainerPortDirection.Input : port.Mode == "output" ? ContainerPortDirection.Output : ContainerPortDirection.Both,
            LiquidIds = string.IsNullOrEmpty(port.FluidId) ? Array.Empty<string>() : new[] { FluidCatalog.Default.Find(port.FluidId).LiquidId }
        }, () => MachineWorld.Contains(logic.Entity) && ReferenceEquals(logic.Entity.Logic, logic),
            position: logic.Entity.Position, sceneHandle: SceneManager.GetSceneByName(MachineWorld.WorldKey).handle)
    { this.logic = logic; chamber = port.Chamber; this.reservationId = reservationId ?? logic.Entity.Id + ":container:" + port.Id; }

    public override object StorageIdentity => Inventory.State;
    public override long StateVersion
    {
        get
        {
            long value = Inventory.State.TotalInternalEnergyJoules.GetHashCode();
            foreach (FluidComponentState component in Inventory.State.Components)
                value = unchecked(value * 397 ^ component.GasMoles.GetHashCode() ^ component.LiquidMoles.GetHashCode() ^ component.FluidId.GetHashCode());
            return value;
        }
    }
    public bool PeekLiquid(out LiquidTransferBatch batch)
    {
        FluidOutputReservation reserved = Inventory.GetReservation(reservationId);
        if (reserved != null) return Describe(reserved.FluidId, reserved.LiquidMoles, out batch);
        foreach (FluidComponentState component in Inventory.State.Components)
            if (Inventory.GetAvailableMoles(component.FluidId, FluidPhase.Liquid) > 0 &&
                Describe(component.FluidId, Inventory.GetAvailableMoles(component.FluidId, FluidPhase.Liquid), out batch)) return true;
        batch = default; return false;
    }
    public bool ReserveLiquid(out LiquidTransferBatch batch)
    {
        bool existing = Inventory.GetReservation(reservationId) != null;
        if (!Inventory.TryReserve(reservationId, FluidPhase.Liquid, FluidInventory.MaximumMoles, out FluidBatch selected))
        { batch = default; return false; }
        FluidDefinition fluid = Inventory.Catalog.Find(selected.FluidId);
        if (!existing)
        {
            FluidOutputReservation reserved = Inventory.GetReservation(reservationId);
            reserved.LiquidMoles = Math.Min(reserved.LiquidMoles, FluidUnits.ServingsToMol(fluid, (decimal)logic.Definition.BatchLiquidServings));
            PublishState();
        }
        return Describe(selected.FluidId, Inventory.GetReservation(reservationId).LiquidMoles, out batch);
    }
    private bool Describe(string id, decimal moles, out LiquidTransferBatch batch)
    {
        FluidDefinition fluid = Inventory.Catalog.Find(id);
        batch = new LiquidTransferBatch(fluid.LiquidId, (float)FluidUnits.MolToServings(fluid, moles), (float)(Inventory.GetTemperatureKelvin() - 273.15));
        return moles > 0 && fluid.AllowIndustrialTransport && !string.IsNullOrEmpty(fluid.LiquidId);
    }
    public float GetReceivableServings(string liquidId, float requested)
    {
        if (!float.IsFinite(requested) || requested <= 0 || !Configuration.AllowsLiquid(liquidId) ||
            !FluidCatalog.Default.TryFromLiquidId(liquidId, out FluidDefinition fluid) || !fluid.AllowIndustrialTransport ||
            logic.State.Ruptured || !string.IsNullOrEmpty(logic.State.RuptureBudgetId)) return 0;
        if (logic.Definition.SingleLiquid)
            foreach (FluidComponentState component in Inventory.State.Components)
                if (component.LiquidMoles > 0 && component.FluidId != fluid.Id) return 0;
        double space = Math.Max(0, MachineWorld.GetFluidVolumeLiters(logic.Entity) -
            MachineWorld.GetFluidMinimumGasSpaceLiters(logic.Entity) - Inventory.GetLiquidLiters());
        decimal maximum = FluidUnits.MolToServings(fluid, FluidUnits.LiquidLitersToMol(fluid, (decimal)space));
        return Math.Min(requested, (float)maximum);
    }
    public bool ExtractLiquid(LiquidTransferBatch batch, float servings)
    {
        FluidOutputReservation reserved = Inventory.GetReservation(reservationId);
        if (reserved == null || reserved.GasMoles > 0 || !FluidCatalog.Default.TryFromLiquidId(batch.LiquidId, out FluidDefinition fluid) || reserved.FluidId != fluid.Id) return false;
        decimal moles = FluidUnits.ServingsToMol(fluid, (decimal)servings);
        return moles > 0 && reserved.LiquidMoles >= moles && Inventory.TryTakeReserved(reservationId, moles, out _);
    }
    public bool InsertLiquid(LiquidTransferBatch batch, float servings)
    {
        if (GetReceivableServings(batch.LiquidId, servings) + .000001f < servings ||
            !FluidCatalog.Default.TryFromLiquidId(batch.LiquidId, out FluidDefinition fluid)) return false;
        FluidBatch actual = FluidInventory.CreateBatch(fluid, 0, FluidUnits.ServingsToMol(fluid, (decimal)servings), Math.Max(.001, batch.TemperatureCelsius + 273.15));
        return Inventory.TryAdd(actual, MachineWorld.GetFluidVolumeLiters(logic.Entity), MachineWorld.GetFluidMinimumGasSpaceLiters(logic.Entity),
            logic.Definition.SingleGas, logic.Definition.SingleLiquid);
    }
    public override object CaptureState() => Inventory.State.Clone();
    public override void RestoreState(object snapshot) => Inventory.Restore((FluidInventoryState)snapshot);
    public override void PublishState() => MachineWorld.StateChanged(logic.Entity);
    #endregion
}
