using System;
using System.Collections.Generic;
using System.Text;
using FlatWorld.Localization;
using UnityEngine;

/// <summary>所有工业设备沿同一机器世界调度，领域逻辑只消费真实输入、动力与输出余量。</summary>
public sealed class FluidMachineLogic : MachineLogic, IContainerPortProvider
{
    #region 设备状态与生命周期
    public FluidMachineState State { get; private set; }
    public FluidMachineDefinition Definition => Entity.Definition.Fluid;
    public FluidDeviceBehavior Behavior => MachineLogicRegistry.GetFluidDeviceBehavior(Definition.Kind);
    private Inventory portableTank;
    private Inventory filterMaterial;
    private Inventory residue;
    private readonly IDisposable combustionSourceLease;
    public Inventory PortableTank { get { EnsureDeviceInventories(); return portableTank; } }
    public Inventory FilterMaterial { get { EnsureDeviceInventories(); return Behavior.UsesFilterMaterial ? filterMaterial : null; } }
    public Inventory Residue { get { EnsureDeviceInventories(); return Behavior.StoresResidue ? residue : null; } }
    private long deviceRegistryGeneration = -1;
    private readonly List<IContainerPort> containerPorts = new();
    private long portRegistryGeneration = -1;
    public override float TickInterval => .1f;
    public override float Progress01 => (float)Math.Clamp(State.PumpProgress, 0, 1);
    public override string Status => Describe();
    public override string ActionLabel => Behavior.ActionLabel(Entity);
    public override GameObject PanelPrefab => GameRes.Instance.GetPrefab(Definition.PanelId);

    public FluidMachineLogic(MachineEntity entity) : base(entity)
    {
        State = MachineWorld.GetFluidState(entity);
        EnsureDeviceInventories();
        combustionSourceLease = MachineWorld.RegisterCombustionSupportSource(entity, () => Behavior as ICombustionSupportSource);
    }
    private void EnsureDeviceInventories()
    {
        if (deviceRegistryGeneration == MachineLogicRegistry.FluidDeviceGeneration) return;
        deviceRegistryGeneration = MachineLogicRegistry.FluidDeviceGeneration;
        if (Definition.AllowPortableTank && portableTank == null)
        {
            portableTank = Track(MachineInventory.Create(null, State.PortableTank, "气罐", 1));
            portableTank.Data.itemSlots[0].CanAcceptTags = new() { "Container.FluidTank" };
            State.PortableTank = portableTank.Data;
        }
        if (Behavior.UsesFilterMaterial && filterMaterial == null)
        { filterMaterial = Track(MachineInventory.Create(null, State.FilterMaterial, "滤材", 1)); State.FilterMaterial = filterMaterial.Data; }
        if (Behavior.StoresResidue && residue == null)
        { residue = Track(MachineInventory.Create(null, State.Residue, "残渣", 1)); State.Residue = residue.Data; }
    }
    public override void Dispose()
    { combustionSourceLease?.Dispose(); base.Dispose(); }
    public override void Capture() => MachineWorld.CaptureFluidState(Entity);
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        FluidMachineState incoming = MachinePersistence.Read<FluidMachineState>(snapshot, "fluid");
        if (incoming == null) return false;
        if (PortableTank != null) { MachineInventory.ApplySnapshot(PortableTank, incoming.PortableTank); incoming.PortableTank = PortableTank.Data; }
        if (FilterMaterial != null) { MachineInventory.ApplySnapshot(FilterMaterial, incoming.FilterMaterial); incoming.FilterMaterial = FilterMaterial.Data; }
        if (Residue != null) { MachineInventory.ApplySnapshot(Residue, incoming.Residue); incoming.Residue = Residue.Data; }
        State = incoming; NotifyRemoteChanged(); return true;
    }

    public override void Tick(float seconds)
    {
        EnsureDeviceInventories();
        if (MachineWorld.IsFluidActive(Entity) && !State.Ruptured && string.IsNullOrEmpty(State.RuptureBudgetId))
            Behavior.Advance(Entity, seconds);
        NotifyChanged();
    }
    public void CollectContainerPorts(List<IContainerPort> ports)
    {
        EnsureDeviceInventories();
        if (portRegistryGeneration != ContainerPortFactoryRegistry.Generation)
        {
            containerPorts.Clear(); portRegistryGeneration = ContainerPortFactoryRegistry.Generation;
            foreach (FluidMachinePortDefinition port in Definition.Ports)
                if (port.Phase != "gas") containerPorts.Add(new IndustrialLiquidTransferPort(this, port));
        }
        ports.AddRange(containerPorts);
    }
    #endregion

    #region 权威配置操作
    public override bool Execute(string operation, string argument, Player actor)
    {
        if (operation == "begin-interaction") return true;
        if (operation == "fluid.vent") return MachineWorld.VentFluidPipe(Entity);
        if (operation == "fluid.phase")
        {
            if (argument != "gas" && argument != "liquid" && argument != "both") return false;
            State.OutletPhase = argument;
        }
        else if (operation == "fluid.rotate")
        {
            if (State.Ruptured || !string.IsNullOrEmpty(State.RuptureBudgetId) || Entity.State == null) return false;
            Entity.State.RotationQuarterTurns = (Entity.State.RotationQuarterTurns + 1) & 3;
            MachineWorld.TopologyChanged(Entity); return true;
        }
        else if (!Behavior.Execute(Entity, operation, argument)) return false;
        NotifyChanged(); return true;
    }
    #endregion

    #region 面板实时读数
    private string Describe()
    {
        var text = new StringBuilder();
        text.Append(FlatWorldLocalizationService.GetUiText(State.Status));
        text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("血量 {0:0.#}/{1:0.#}", Entity.State?.Hp ?? MachineWorld.ReadMachineState(Entity.Snapshot).Hp, MachineWorld.ResolveMaximumHp(Entity.Snapshot)));
        foreach (string chamber in State.Chambers.Keys)
        {
            if (chamber == "main" && State.GroupOwnerId != 0) { DescribeInventory(text, chamber); continue; }
            DescribeInventory(text, chamber);
        }
        if (State.Chambers.Count == 0) DescribeInventory(text, "main");
        Behavior.AppendDescription(Entity, text);
        if (State.LastEmittedStandardLiters > 0) text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("排出 {0:0.###} 标准 L · 留存 {1:0.###} · 逸散 {2:0.###}", State.LastEmittedStandardLiters, State.LastRetainedStandardLiters, State.LastEscapedStandardLiters));
        foreach (var blocked in State.BlockedPorts) text.Append("\n").Append(blocked.Key).Append("：").Append(FlatWorldLocalizationService.GetUiText(blocked.Value));
        foreach (var warning in State.PhaseWarnings)
        {
            const string prefix = "超出首轮相变曲线，保留真实相态：";
            string message = warning.Value.StartsWith(prefix, StringComparison.Ordinal)
                ? FlatWorldLocalizationService.GetUiFormat("超出首轮相变曲线，保留真实相态：{0}", warning.Value.Substring(prefix.Length))
                : FlatWorldLocalizationService.GetUiText(warning.Value);
            text.Append("\n").Append(warning.Key).Append("：").Append(message);
        }
        foreach (var residue in State.ResidueProgress) if (residue.Value > 0) text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("已保留待成份残渣 {0}：{1:0.######} 份", residue.Key, residue.Value));
        return text.ToString();
    }
    private void DescribeInventory(StringBuilder text, string chamber)
    {
        FluidInventory inventory = MachineWorld.GetFluidInventory(Entity, chamber);
        double volume = MachineWorld.GetFluidVolumeLiters(Entity), minimum = MachineWorld.GetFluidMinimumGasSpaceLiters(Entity);
        text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("{0}：{1:0.##}℃ · {2:0.##}/{3:0.##} kPa\n气体 {4:0.###} 标准 L · 液体 {5:0.###} L · 气相空间 {6:0.###} L",
            chamber, inventory.GetTemperatureKelvin() - 273.15, inventory.GetPressureKPa(volume, minimum), Definition.MaxSafePressureKPa,
            FluidUnits.MolToStandardLiters(inventory.GasMoles), inventory.GetLiquidLiters(), Math.Max(minimum, volume - inventory.GetLiquidLiters())));
        foreach (FluidComponentState component in inventory.State.Components)
        {
            FluidDefinition substance = inventory.Catalog.Find(component.FluidId);
            decimal gasRatio = inventory.GasMoles > 0 ? component.GasMoles / inventory.GasMoles * 100 : 0;
            decimal totalServings = 0;
            foreach (FluidComponentState current in inventory.State.Components) totalServings += FluidUnits.MolToServings(inventory.Catalog.Find(current.FluidId), current.LiquidMoles);
            decimal liquidRatio = totalServings > 0 ? FluidUnits.MolToServings(substance, component.LiquidMoles) / totalServings * 100 : 0;
            text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("{0}：气 {1:0.###} 标准 L ({2:0.#}%) · 液 {3:0.###} L ({4:0.#}%)", FlatWorldLocalizationService.GetUiText(substance.DisplayName),
                FluidUnits.MolToStandardLiters(component.GasMoles), gasRatio, FluidUnits.MolToLiquidLiters(substance, component.LiquidMoles), liquidRatio));
        }
        foreach (FluidOutputReservation reservation in inventory.State.Ports)
            text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("待输出 {0}：{1} {2:0.######} mol", reservation.PortId, FlatWorldLocalizationService.GetUiText(inventory.Catalog.Find(reservation.FluidId).DisplayName), reservation.TotalMoles));
    }
    #endregion
}
