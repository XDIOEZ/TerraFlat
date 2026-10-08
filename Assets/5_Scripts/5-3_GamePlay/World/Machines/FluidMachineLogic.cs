using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FlatWorld.Localization;
using UnityEngine;

/// <summary>所有工业设备沿同一机器世界调度，领域逻辑只消费真实输入、动力与输出余量。</summary>
public sealed class FluidMachineLogic : MachineLogic, IContainerPortProvider
{
    #region 设备状态与生命周期
    public FluidMachineState State { get; private set; }
    public FluidMachineDefinition Definition => Entity.Definition.Fluid;
    public Inventory PortableTank { get; }
    public Inventory FilterMaterial { get; }
    public Inventory Residue { get; }
    private readonly List<IContainerPort> containerPorts = new();
    private long portRegistryGeneration = -1;
    public override float TickInterval => .1f;
    public override float Progress01 => (float)Math.Clamp(State.PumpProgress, 0, 1);
    public override string Status => Describe();
    public override string ActionLabel => Definition.Kind switch
    {
        "selector" => "选择下一个气体",
        "valve" => State.ValveOpen ? "关闭阀门" : "打开阀门",
        "pipe" or "outlet" => "排出气体",
        "compressor" => "调整充装压力",
        _ => ""
    };
    public override GameObject PanelPrefab => GameRes.Instance.GetPrefab(Definition.PanelId);

    public FluidMachineLogic(MachineEntity entity) : base(entity)
    {
        State = MachineWorld.GetFluidState(entity);
        if (Definition.AllowPortableTank)
        {
            PortableTank = Track(MachineInventory.Create(null, State.PortableTank, "气罐", 1));
            PortableTank.Data.itemSlots[0].CanAcceptTags = new() { "Container.FluidTank" };
        }
        if (Definition.Kind == "filter") FilterMaterial = Track(MachineInventory.Create(null, State.FilterMaterial, "滤材", 1));
        if (Definition.Kind == "filter" || Definition.Kind == "distiller")
            Residue = Track(MachineInventory.Create(null, State.Residue, "残渣", 1));
    }
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
        MachineWorld.AdvanceFluidDevice(Entity, seconds);
        NotifyChanged();
    }
    public void CollectContainerPorts(List<IContainerPort> ports)
    {
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
        if (operation == "fluid.vent" || operation == "work" && (Definition.Kind == "pipe" || Definition.Kind == "outlet"))
            return MachineWorld.VentFluidPipe(Entity);
        if (operation == "fluid.valve" || operation == "work" && Definition.Kind == "valve")
        { State.ValveOpen = !State.ValveOpen; MachineWorld.TopologyChanged(Entity); return true; }
        if (operation == "fluid.select" || operation == "work" && Definition.Kind == "selector")
        {
            string selected = argument;
            if (string.IsNullOrEmpty(selected))
            {
                var ids = new List<string>(FluidCatalog.Default.Definitions.Keys); ids.Sort(StringComparer.Ordinal);
                int index = ids.IndexOf(State.SelectedGasId); selected = ids.Count == 0 ? "" : ids[(index + 1) % ids.Count];
            }
            if (!FluidCatalog.Default.TryGet(selected, out var definition) || !definition.AllowIndustrialTransport) return false;
            State.SelectedGasId = definition.Id;
        }
        else if (operation == "fluid.probe")
        {
            string[] values = (argument ?? "").Split('|');
            if (values.Length != 2 || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double disconnect) ||
                !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double reconnect) ||
                !double.IsFinite(disconnect) || !double.IsFinite(reconnect) || reconnect < 0 || disconnect <= reconnect) return false;
            State.DisconnectPressureKPa = disconnect; State.ReconnectPressureKPa = reconnect;
        }
        else if (operation == "fluid.pressure" || operation == "work" && Definition.Kind == "compressor")
        {
            if (string.IsNullOrEmpty(argument)) State.CompressionTargetKPa = Math.Min(Definition.MaximumPumpPressureKPa, State.CompressionTargetKPa + 100);
            else if (double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out double pressure) &&
                double.IsFinite(pressure) && pressure > 0 && pressure <= Definition.MaximumPumpPressureKPa) State.CompressionTargetKPa = pressure;
            else return false;
        }
        else if (operation == "fluid.phase")
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
        else return false;
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
        if (Definition.Kind == "gas-pump")
        {
            text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("转速 {0:0.#} RPM · 动力{1} · 抽取 {2:0.#}%", Entity.SpeedRpm,
                FlatWorldLocalizationService.GetUiText(Entity.LoadSatisfied ? "足够" : "不足"), Progress01 * 100));
            text.Append(MachineWorld.DescribeAtmosphere());
        }
        if (Definition.Kind == "selector") text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("输出1：{0}\n输出2：其他气体", string.IsNullOrEmpty(State.SelectedGasId) ? FlatWorldLocalizationService.GetUiText("请选择气体") : FlatWorldLocalizationService.GetUiText(FluidCatalog.Default.Find(State.SelectedGasId).DisplayName)));
        if (Definition.Kind == "mechanical-probe" || Definition.Kind == "electronic-probe")
            text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("测点管压 {0:0.##} kPa\n断开 {1:0.##} / 恢复 {2:0.##} kPa\n当前{3}",
                MachineWorld.GetPressureProbeReading(Entity), State.DisconnectPressureKPa, State.ReconnectPressureKPa, FlatWorldLocalizationService.GetUiText(State.ProbeConnected ? "接通" : "断开")));
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
