using System;
using FlatWorld.Localization;
using FlatWorld.Networking;
using MemoryPack;
using UnityEngine;

/// <summary>信号线、逻辑门与继电器的面板只读已经提交的网络状态。</summary>
public sealed class MachineCircuitSignalLogic : MachineLogic
{
    #region 只读仪表
    public MachineCircuitSignalLogic(MachineEntity entity) : base(entity) { }
    public override bool CanAct => false;
    public override GameObject PanelPrefab => GameRes.Instance.GetPrefab("UI_Mechanical");
    public override bool SupportsUnchangedSaveSnapshotReuse => true;
    public override string Status
    {
        get
        {
            CircuitSignalDefinition signal = Entity.Definition.Signal;
            if (signal.IsWire) return FlatWorldLocalizationService.GetUiFormat("信号 {0}", Entity.SignalOutput ? 1 : 0);
            string inputs = signal.IsGate && signal.Kind != "not"
                ? FlatWorldLocalizationService.GetUiFormat("输入 A {0} · B {1}", Entity.SignalInputA ? 1 : 0, Entity.SignalInputB ? 1 : 0)
                : FlatWorldLocalizationService.GetUiFormat("信号 {0}", Entity.SignalInputA ? 1 : 0);
            if (signal.Kind != "relay") return inputs + "\n" + FlatWorldLocalizationService.GetUiFormat("输出 {0}", Entity.SignalOutput ? 1 : 0);
            return inputs + "\n" + FlatWorldLocalizationService.GetUiText(Entity.ElectricalContactsClosed ? "触点接通" : "触点断开") +
                "\n" + FlatWorldLocalizationService.GetUiFormat("触点 A {0} · B {1}",
                FlatWorldLocalizationService.GetUiText(MachineWorld.GetElectricalContactNetwork(Entity, false) != null ? "已接线" : "未接线"),
                FlatWorldLocalizationService.GetUiText(MachineWorld.GetElectricalContactNetwork(Entity, true) != null ? "已接线" : "未接线"));
        }
    }
    public override bool Execute(string operation, string argument, Item actor) => operation == "begin-interaction";
    public override void Capture() { }
    #endregion
}

[Serializable, MemoryPackable(GenerateType.VersionTolerant)]
public partial class MachineRadioConfiguration
{
    #region 持久设备配置
    [MemoryPackOrder(0)] public bool Enabled = true;
    [MemoryPackOrder(1)] public int Channel = 1;
    [MemoryPackOrder(2)] public bool Transmit = true;
    #endregion
}

/// <summary>数据机器负责供电与登记，无线服务不识别设备类型。</summary>
public abstract class MachineRadioDeviceLogic : MachineLogic
{
    #region 供能、配置与登记
    public MachineRadioConfiguration State { get; private set; }
    private RadioSignalService registered;
    private string registeredWorld;
    private float publishedSuppliedWatts = -1f;
    internal string ConfigurationFailureReason;
    public bool IsPowered => GameNetwork.HasStateAuthority ? State.Enabled && Entity.Active &&
        Entity.ElectricalSuppliedWatts + .0001f >= Entity.Definition.Electrical.PowerWatts &&
        Entity.Definition.Electrical.AcceptsVoltage(Entity.ElectricalVoltage) : Entity.RadioPowered;
    public override float TickInterval => 0f;
    public override float ElectricalDemandFactor => State.Enabled ? 1f : 0f;
    public override GameObject PanelPrefab => GameRes.Instance.GetPrefab("UI_Mechanical");
    public override string Status => FlatWorldLocalizationService.GetUiFormat("频道 {0} · {1}\n实际供电 {2:0.#}/{3:0.#} W\n输入 {4} · 输出 {5}\n{6}",
        State.Channel, FlatWorldLocalizationService.GetUiText(IsPowered ? "已供电" : "未供电"),
        Entity.ElectricalSuppliedWatts, Entity.Definition.Electrical.PowerWatts,
        Entity.SignalInputA ? 1 : 0, Entity.SignalOutput ? 1 : 0,
        FlatWorldLocalizationService.GetUiText(ConfigurationFailureReason ?? (Entity.RadioFeedbackBlocked ? "无线反馈连接已阻断" :
            Entity.RadioHasSource ? "无线信号有效" : "无有效无线来源")));
    protected abstract RadioEndpointCapabilities Capabilities { get; }
    protected MachineRadioDeviceLogic(MachineEntity entity) : base(entity)
    {
        State = MachinePersistence.Read<MachineRadioConfiguration>(entity.Snapshot, "radio-device") ?? new MachineRadioConfiguration();
        registeredWorld = entity.ScopeKey;
        entity.RadioChannel = State.Channel; entity.RadioTransmitMode = State.Transmit;
    }
    public override void Tick(float seconds) => RefreshRegistration();
    internal void ResetTransferredReceptionBeforeRebuildingSignals()
    {
        if (registeredWorld == Entity.ScopeKey) return;
        byte old = Entity.CircuitStateBits;
        Unregister(false); Entity.SignalOutput = Entity.RadioPowered = false;
        Entity.ElectricalSuppliedWatts = Entity.ElectricalVoltage = 0f;
        registeredWorld = Entity.ScopeKey;
        if (old != Entity.CircuitStateBits) MachineWorld.NotifyRadioReceptionChanged(Entity);
    }
    public void RefreshRegistration()
    {
        if (!GameNetwork.HasStateAuthority || !MachineWorld.Contains(Entity)) return;
        bool powered = IsPowered;
        bool powerChanged = publishedSuppliedWatts != Entity.ElectricalSuppliedWatts || Entity.RadioPowered != powered;
        publishedSuppliedWatts = Entity.ElectricalSuppliedWatts; Entity.RadioPowered = powered;
        RadioSignalService service = MachineWorld.GetRadioServiceForWorld(Entity.ScopeKey);
        if (!powered || Entity.RadioFeedbackBlocked || service == null)
        { Unregister(); if (powerChanged) MachineWorld.NotifyRadioReceptionChanged(Entity); return; }
        if (powerChanged) MachineWorld.NotifyRadioReceptionChanged(Entity);
        if (ReferenceEquals(registered, service)) return;
        Unregister();
        if (service.Register(Entity.Id, State.Channel, () => Entity.Position, Capabilities,
            received =>
            {
                Entity.RadioReceiving = received.Value; Entity.RadioHasSource = received.HasSource;
                MachineWorld.NotifyRadioReceptionChanged(Entity);
            }, message => RadioTextDelivery.DeliverAt(Entity.ScopeKey, message))) registered = service;
    }
    public void Unregister(bool notify = true)
    {
        byte old = Entity.CircuitStateBits;
        RadioSignalService previous = registered; registered = null;
        previous?.Unregister(Entity.Id);
        Entity.RadioReceiving = Entity.RadioHasSource = false;
        if (notify && old != Entity.CircuitStateBits) MachineWorld.NotifyRadioReceptionChanged(Entity);
    }
    public void ConfigurationChanged() { ConfigurationFailureReason = null; Capture(); NotifyChanged(visual: true); }
    public override bool Execute(string operation, string argument, Item actor)
        => operation == "begin-interaction" || MachineWorld.TryChangeRadioConfiguration(this, operation, argument);
    public override void Capture() => MachinePersistence.Write(Entity.Snapshot, "radio-device", State);
    public override bool ApplyRemoteSnapshot(ItemData snapshot)
    {
        State = MachinePersistence.Read<MachineRadioConfiguration>(snapshot, "radio-device") ?? new MachineRadioConfiguration();
        Entity.RadioChannel = State.Channel; Entity.RadioTransmitMode = State.Transmit;
        NotifyRemoteChanged(); return true;
    }
    public override void Dispose() { Unregister(false); base.Dispose(); }
    protected bool SendText(string sender, string text, string messageId)
    { RefreshRegistration(); return registered?.SendText(Entity.Id, sender, text, messageId) == true; }
    #endregion
}

public sealed class MachineRadioSignalConverterLogic : MachineRadioDeviceLogic
{
    #region 有线无线转换
    public MachineRadioSignalConverterLogic(MachineEntity entity) : base(entity) { }
    protected override RadioEndpointCapabilities Capabilities => State.Transmit
        ? RadioEndpointCapabilities.TransmitCircuit : RadioEndpointCapabilities.ReceiveCircuit;
    public void PublishSignalBit()
    {
        RefreshRegistration();
        if (IsPowered && State.Transmit && !Entity.RadioFeedbackBlocked)
            MachineWorld.GetRadioServiceForWorld(Entity.ScopeKey)?.SendCircuitBit(Entity.Id, Entity.SignalInputA);
    }
    #endregion
}

public sealed class MachineRadioStationLogic : MachineRadioDeviceLogic
{
    #region 固定广播电台
    public MachineRadioStationLogic(MachineEntity entity) : base(entity) { }
    protected override RadioEndpointCapabilities Capabilities => RadioEndpointCapabilities.TransmitText | RadioEndpointCapabilities.ReceiveText;
    public bool TryBroadcast(string messageId)
        => IsPowered && RadioBroadcastMessages.TryGet(messageId, out string text) &&
            SendText(Entity.Definition.Content.Definition.DisplayName, text, messageId);
    private bool TrySendPlayerText(Item actor, string text)
    {
        if (!GameNetwork.HasStateAuthority || actor == null || !IsPowered || string.IsNullOrWhiteSpace(text) ||
            text.Length > RadioSignalService.MaximumTextLength) return false;
        string sender = actor.itemData.GetActorProfileMetadata()?.DisplayName;
        return SendText(string.IsNullOrWhiteSpace(sender) ? actor.itemData.GameName : sender, text, null);
    }
    public override bool Execute(string operation, string argument, Item actor)
        => operation == "radio.broadcast" ? TryBroadcast(argument) : operation == "radio.text"
            ? TrySendPlayerText(actor, argument) : base.Execute(operation, argument, actor);
    #endregion
}
