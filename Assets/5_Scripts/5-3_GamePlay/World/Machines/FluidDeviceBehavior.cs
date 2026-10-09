using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using FlatWorld.Localization;

/// <summary>设备声明端口与加工规则，管网只消费能力；注册策略不持有某个节点的运行态。</summary>
public abstract class FluidDeviceBehavior
{
    #region 设备能力
    public virtual bool IsTransit => false;
    public virtual bool IsSharedStorage => false;
    public virtual bool EmitsToEnvironment => false;
    public virtual bool SwitchesMechanicalPorts => false;
    public virtual bool SwitchesElectricalPorts => false;
    public virtual bool FloorsSourceTorque => false;
    public virtual bool HasMechanicalSource => false;
    public virtual bool UsesFilterMaterial => false;
    public virtual bool StoresResidue => false;
    public virtual bool PortsEnabled(MachineEntity node) => true;
    public virtual bool CanOutput(MachineEntity node, out string reason) { reason = null; return true; }
    public virtual bool CanRoute(MachineEntity node, FluidMachinePortDefinition port, FluidPhase phase,
        string fluidId, out string reason) { reason = null; return true; }
    public virtual float ElectricalDemand(MachineEntity node) => 0;
    public virtual float MechanicalSourceFactor(MachineEntity node) => 0;
    public virtual void BeginStep(MachineEntity node, float seconds) { }
    public virtual void Advance(MachineEntity node, float seconds) { }
    public virtual string ActionLabel(MachineEntity node) => string.Empty;
    public virtual bool Execute(MachineEntity node, string operation, string argument) => false;
    public virtual void AppendDescription(MachineEntity node, StringBuilder text) { }
    #endregion
}

public class FluidTransitBehavior : FluidDeviceBehavior
{
    #region 普通管段
    public override bool IsTransit => true;
    public override string ActionLabel(MachineEntity node) => "排出气体";
    public override bool Execute(MachineEntity node, string operation, string argument)
        => (operation == "fluid.vent" || operation == "work") && MachineWorld.VentFluidPipe(node);
    #endregion
}

public sealed class FluidOutletBehavior : FluidTransitBehavior
{
    #region 环境出口
    public override bool EmitsToEnvironment => true;
    #endregion
}

public sealed class FluidValveBehavior : FluidTransitBehavior
{
    #region 阀门
    public override bool PortsEnabled(MachineEntity node) => MachineWorld.GetFluidState(node).ValveOpen;
    public override string ActionLabel(MachineEntity node) => MachineWorld.GetFluidState(node).ValveOpen ? "关闭阀门" : "打开阀门";
    public override bool Execute(MachineEntity node, string operation, string argument)
    {
        if (operation != "fluid.valve" && operation != "work") return false;
        FluidMachineState state = MachineWorld.GetFluidState(node);
        state.ValveOpen = !state.ValveOpen; MachineWorld.TopologyChanged(node); return true;
    }
    #endregion
}

public sealed class FluidSelectorBehavior : FluidTransitBehavior
{
    #region 气体选择与路由
    public override string ActionLabel(MachineEntity node) => "选择下一个气体";
    public override bool CanRoute(MachineEntity node, FluidMachinePortDefinition port, FluidPhase phase, string fluidId, out string reason)
    {
        string selected = MachineWorld.GetFluidState(node).SelectedGasId;
        reason = string.IsNullOrEmpty(selected) ? "请先选择目标气体" : null;
        return phase == FluidPhase.Gas && reason == null && fluidId != null &&
            string.Equals(selected, fluidId, StringComparison.OrdinalIgnoreCase) == (port.Id == "selected");
    }
    public override bool Execute(MachineEntity node, string operation, string argument)
    {
        if (operation != "fluid.select" && operation != "work") return false;
        FluidMachineState state = MachineWorld.GetFluidState(node);
        string selected = argument;
        if (string.IsNullOrEmpty(selected))
        {
            var ids = new List<string>(FluidCatalog.Default.Definitions.Keys); ids.Sort(StringComparer.Ordinal);
            int index = ids.IndexOf(state.SelectedGasId); selected = ids.Count == 0 ? "" : ids[(index + 1) % ids.Count];
        }
        if (!FluidCatalog.Default.TryGet(selected, out FluidDefinition definition) || !definition.AllowIndustrialTransport) return false;
        state.SelectedGasId = definition.Id; return true;
    }
    public override void AppendDescription(MachineEntity node, StringBuilder text)
    {
        string id = MachineWorld.GetFluidState(node).SelectedGasId;
        text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("输出1：{0}\n输出2：其他气体", string.IsNullOrEmpty(id)
            ? FlatWorldLocalizationService.GetUiText("请选择气体") : FlatWorldLocalizationService.GetUiText(FluidCatalog.Default.Find(id).DisplayName)));
    }
    #endregion
}

public sealed class FluidStorageBehavior : FluidDeviceBehavior
{
    #region 共享储存
    public override bool IsSharedStorage => true;
    public override void AppendDescription(MachineEntity node, StringBuilder text)
    {
        FluidMachineState state = MachineWorld.GetFluidState(node);
        if (node.Definition.Fluid.TrackAmbientTemperature && state.BodyTemperatureInitialized)
            text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("罐体温度 {0:0.##}℃", state.BodyTemperatureCelsius));
    }
    #endregion
}

public sealed class FluidGasPumpBehavior : FluidDeviceBehavior
{
    #region 大气抽取
    public override bool CanOutput(MachineEntity node, out string reason)
    { reason = FluidDeviceOperations.IsGasPumpWet(node) ? "气泵遇到液体" : null; return reason == null; }
    public override void Advance(MachineEntity node, float seconds) => FluidDeviceOperations.AdvanceGasPump(node, seconds);
    public override void AppendDescription(MachineEntity node, StringBuilder text)
    {
        text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("转速 {0:0.#} RPM · 动力{1} · 抽取 {2:0.#}%", node.SpeedRpm,
            FlatWorldLocalizationService.GetUiText(node.LoadSatisfied ? "足够" : "不足"), Math.Clamp(MachineWorld.GetFluidState(node).PumpProgress, 0, 1) * 100));
        text.Append(MachineWorld.DescribeAtmosphere());
    }
    #endregion
}

public sealed class FluidLiquidPumpBehavior : FluidDeviceBehavior
{
    #region 液体抽取
    public override void Advance(MachineEntity node, float seconds) => FluidDeviceOperations.AdvanceLiquidPump(node, seconds);
    public override float ElectricalDemand(MachineEntity node)
        => MachineWorld.GetFluidInventory(node).GetLiquidLiters() < node.Definition.Fluid.VolumeLiters - node.Definition.Fluid.MinimumGasSpaceLiters ? 1 : 0;
    #endregion
}

public sealed class FluidCompressorBehavior : FluidDeviceBehavior
{
    #region 压缩与充装
    public override void Advance(MachineEntity node, float seconds) => FluidDeviceOperations.AdvanceCompressor(node, seconds);
    public override float ElectricalDemand(MachineEntity node) => MachineWorld.GetFluidInventory(node).GasMoles > 0 ? 1 : 0;
    public override string ActionLabel(MachineEntity node) => "调整充装压力";
    public override bool Execute(MachineEntity node, string operation, string argument)
    {
        if (operation != "fluid.pressure" && operation != "work") return false;
        FluidMachineState state = MachineWorld.GetFluidState(node);
        if (string.IsNullOrEmpty(argument)) state.CompressionTargetKPa = Math.Min(node.Definition.Fluid.MaximumPumpPressureKPa, state.CompressionTargetKPa + 100);
        else if (double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out double pressure) &&
            double.IsFinite(pressure) && pressure > 0 && pressure <= node.Definition.Fluid.MaximumPumpPressureKPa) state.CompressionTargetKPa = pressure;
        else return false;
        return true;
    }
    #endregion
}

public class FluidReactionBehavior : FluidDeviceBehavior
{
    #region 守恒反应
    public override void Advance(MachineEntity node, float seconds) => FluidDeviceOperations.AdvanceFluidReaction(node, seconds);
    public override float ElectricalDemand(MachineEntity node) => FluidDeviceOperations.GetReactionDemand(node);
    #endregion
}

public sealed class FluidFilterBehavior : FluidReactionBehavior
{
    #region 过滤耗材
    public override bool UsesFilterMaterial => true;
    public override bool StoresResidue => true;
    #endregion
}

/// <summary>电解液体留在副产物腔，残渣槽只接收反应分离出的固体杂质。</summary>
public sealed class FluidElectrolyzerBehavior : FluidReactionBehavior
{
    #region 电解副产物
    public override bool StoresResidue => true;
    #endregion
}

public sealed class FluidEngineBehavior : FluidDeviceBehavior
{
    #region 燃料动力结算
    public override bool FloorsSourceTorque => true;
    public override bool HasMechanicalSource => true;
    public override void BeginStep(MachineEntity node, float seconds)
        => MachineWorld.SetFluidMechanicalPower(node, FluidDeviceOperations.CalculateAvailableEnginePower(node, seconds));
    public override float MechanicalSourceFactor(MachineEntity node) => MachineWorld.GetFluidMechanicalSourceFactor(node);
    public override void Advance(MachineEntity node, float seconds) => FluidDeviceOperations.SettleFluidEngine(node, seconds);
    #endregion
}

public sealed class FluidPressureProbeBehavior : FluidDeviceBehavior
{
    #region 压力开关
    private readonly bool electrical;
    public FluidPressureProbeBehavior(bool electrical) => this.electrical = electrical;
    public override bool SwitchesMechanicalPorts => !electrical;
    public override bool SwitchesElectricalPorts => electrical;
    public override void BeginStep(MachineEntity node, float seconds)
    {
        FluidMachineState state = MachineWorld.GetFluidState(node);
        double pressure = MachineWorld.GetPressureProbeReading(node);
        bool connected = state.ProbeConnected;
        if (connected && pressure > state.DisconnectPressureKPa) connected = false;
        else if (!connected && pressure <= state.ReconnectPressureKPa) connected = true;
        node.FluidProbeConnected = connected;
        if (connected == state.ProbeConnected) return;
        state.ProbeConnected = connected; state.Status = connected ? "探针已接通" : "管压超限，探针已断开";
        MachineWorld.TopologyChanged(node);
    }
    public override bool Execute(MachineEntity node, string operation, string argument)
    {
        if (operation != "fluid.probe") return false;
        string[] values = (argument ?? "").Split('|');
        if (values.Length != 2 || !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double disconnect) ||
            !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double reconnect) ||
            !double.IsFinite(disconnect) || !double.IsFinite(reconnect) || reconnect < 0 || disconnect <= reconnect) return false;
        FluidMachineState state = MachineWorld.GetFluidState(node);
        state.DisconnectPressureKPa = disconnect; state.ReconnectPressureKPa = reconnect; return true;
    }
    public override void AppendDescription(MachineEntity node, StringBuilder text)
    {
        FluidMachineState state = MachineWorld.GetFluidState(node);
        text.Append("\n").Append(FlatWorldLocalizationService.GetUiFormat("测点管压 {0:0.##} kPa\n断开 {1:0.##} / 恢复 {2:0.##} kPa\n当前{3}",
            MachineWorld.GetPressureProbeReading(node), state.DisconnectPressureKPa, state.ReconnectPressureKPa,
            FlatWorldLocalizationService.GetUiText(state.ProbeConnected ? "接通" : "断开")));
    }
    #endregion
}
