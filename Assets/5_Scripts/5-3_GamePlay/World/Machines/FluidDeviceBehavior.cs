using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>设备只声明自身能力，管道只按端口、方向和物质过滤连接。</summary>
public abstract class FluidDeviceBehavior
{
    #region 设备能力
    public virtual bool IsTransit => false;
    public virtual bool IsStorage => false;
    public virtual bool EmitsToEnvironment => false;
    public virtual bool SwitchesMechanicalPorts => false;
    public virtual bool SwitchesElectricalPorts => false;
    public virtual bool FloorsSourceTorque => false;
    public virtual bool HasMechanicalSource => false;
    public virtual bool UsesFilterMaterial => false;
    public virtual bool StoresResidue => false;
    public virtual bool ReceivesOnDemand => false;
    public virtual bool CanCacheElectricalDemandWithinStep => false;
    public virtual bool UsesObservedTickState => true;
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

public sealed class FluidReceiverBehavior : FluidDeviceBehavior
{
    #region 按需供给
    public override bool ReceivesOnDemand => true;
    #endregion
}

public class FluidTransitBehavior : FluidDeviceBehavior
{
    #region 无库存管道
    public override bool IsTransit => true;
    #endregion
}

public sealed class FluidOutletBehavior : FluidTransitBehavior
{
    #region 环境出口
    public override bool EmitsToEnvironment => true;
    public override bool ReceivesOnDemand => true;
    public override string ActionLabel(MachineEntity node) => "设置每秒排放量";
    public override bool Execute(MachineEntity node, string operation, string argument)
    {
        if (operation != "fluid.rate" || !double.TryParse(argument, NumberStyles.Float,
            CultureInfo.InvariantCulture, out double rate) || !double.IsFinite(rate) || rate < 0) return false;
        MachineWorld.GetFluidState(node).OutputLitersPerSecond = rate;
        return true;
    }
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
        var state = MachineWorld.GetFluidState(node);
        state.ValveOpen = !state.ValveOpen; MachineWorld.TopologyChanged(node); return true;
    }
    #endregion
}

public sealed class FluidSelectorBehavior : FluidTransitBehavior
{
    #region 只放行指定组分
    public override string ActionLabel(MachineEntity node) => "选择气体";
    public override bool CanRoute(MachineEntity node, FluidMachinePortDefinition port, FluidPhase phase, string fluidId, out string reason)
    {
        string selected = MachineWorld.GetFluidState(node).SelectedGasId;
        bool allowed = phase == FluidPhase.Gas && !string.IsNullOrEmpty(selected) &&
            string.Equals(selected, fluidId, StringComparison.OrdinalIgnoreCase);
        reason = allowed ? null : "过滤管只放行选定气体";
        return allowed;
    }
    public override bool Execute(MachineEntity node, string operation, string argument)
    {
        if (operation != "fluid.select" && operation != "work") return false;
        var state = MachineWorld.GetFluidState(node);
        string selected = argument;
        if (string.IsNullOrEmpty(selected))
        {
            var ids = new List<string>(FluidCatalog.Default.Definitions.Keys); ids.Sort(StringComparer.Ordinal);
            int index = ids.IndexOf(state.SelectedGasId); selected = ids.Count == 0 ? "" : ids[(index + 1) % ids.Count];
        }
        if (!FluidCatalog.Default.TryGet(selected, out FluidDefinition definition) || !definition.AllowIndustrialTransport) return false;
        state.SelectedGasId = definition.Id; MachineWorld.TopologyChanged(node); return true;
    }
    #endregion
}

public sealed class FluidStorageBehavior : FluidDeviceBehavior
{
    #region 各罐独立存物
    public override bool IsStorage => true;
    public override string ActionLabel(MachineEntity node)
        => node.Definition.Fluid.AllowManualAirFill ? "自然补充空气" : string.Empty;
    public override bool Execute(MachineEntity node, string operation, string argument)
        => (operation == "work" || operation == "fluid.fill-air") && MachineWorld.FillFluidTankAir(node);
    #endregion
}

public sealed class FluidGasPumpBehavior : FluidDeviceBehavior
{
    #region 动力气泵
    public override void Advance(MachineEntity node, float seconds) => FluidDeviceOperations.AdvanceGasPump(node, seconds);
    public override bool CanOutput(MachineEntity node, out string reason)
    { reason = FluidDeviceOperations.IsGasPumpWet(node) ? "气泵遇到液体" : null; return reason == null; }
    #endregion
}

public sealed class FluidLiquidPumpBehavior : FluidDeviceBehavior
{
    #region 液体泵
    public override void Advance(MachineEntity node, float seconds) => FluidDeviceOperations.AdvanceLiquidPump(node, seconds);
    public override float ElectricalDemand(MachineEntity node) => 1;
    #endregion
}

public sealed class FluidFillerBehavior : FluidDeviceBehavior
{
    #region 顺序容器装填
    private readonly bool liquid;
    public FluidFillerBehavior(bool liquid) => this.liquid = liquid;
    public override bool ReceivesOnDemand => true;
    public override void Advance(MachineEntity node, float seconds) => FluidDeviceOperations.FillContainers(node, seconds, liquid);
    #endregion
}

public sealed class FluidEngineBehavior : FluidDeviceBehavior
{
    #region 实际消耗后供能
    public override bool ReceivesOnDemand => true;
    public override bool FloorsSourceTorque => true;
    public override bool HasMechanicalSource => true;
    public override void BeginStep(MachineEntity node, float seconds)
        => MachineWorld.SetFluidMechanicalPower(node, FluidDeviceOperations.CalculateAvailableEnginePower(node, seconds));
    public override float MechanicalSourceFactor(MachineEntity node) => MachineWorld.GetFluidMechanicalSourceFactor(node);
    public override void Advance(MachineEntity node, float seconds) => FluidDeviceOperations.SettleFluidEngine(node, seconds);
    #endregion
}
