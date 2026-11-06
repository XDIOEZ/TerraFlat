using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

public static partial class MachineWorld
{
    #region 独立信号与无线作用域
    private static CircuitSignalNetworkGraph signalGraph => scope.Signals;
    public static event Action<MachineEntity> SignalStateChanged;
    public static RadioSignalService GetRadioServiceForWorld(string key)
        => key != null && scopes.TryGetValue(key, out ScopeState state) ? state.Radio : null;
    public static int GetSignalWireConnectionMask(Vector2Int cell)
    { RebuildGraphsIfDirty(); return signalGraph?.ConnectionMask(cell) ?? 0; }
    public static bool ReadSignalWire(MachineEntity node) => node?.SignalOutput == true;

    private static void InitializeSignalScope(WorldTopologyDomain topology)
    {
        scope.Signals = new CircuitSignalNetworkGraph(topology);
        scope.Radio = new RadioSignalService(worldKey, topology);
    }
    private static void InitializeSignalDeviceState(MachineEntity node)
    {
        if (node.Definition.LogicId != "radio-converter" && node.Definition.LogicId != "radio-station") return;
        MachineRadioConfiguration radio = MachinePersistence.Read<MachineRadioConfiguration>(node.Snapshot, "radio-device") ?? new MachineRadioConfiguration();
        node.RadioChannel = radio.Channel; node.RadioTransmitMode = radio.Transmit;
    }
    private static void RebuildCircuitSignalsAndSimulationDependencies(bool rebuildSignals)
    {
        if (signalGraph == null) return;
        if (rebuildSignals) foreach (MachineEntity node in nodes.Values)
            if (node.Logic is MachineRadioDeviceLogic radio) radio.ResetTransferredReceptionBeforeRebuildingSignals();
        if (rebuildSignals) signalGraph.Rebuild(nodes.Values, GameNetwork.HasStateAuthority);
        scope.AutomationNodes.Clear(); scope.AutomationQueue.Clear();
        foreach (MachineEntity node in signalGraph.Devices)
            if (node.Definition.Signal.Kind != "source" || signalGraph.Port(node, node.Definition.Signal.Output) != null) Add(node);
        foreach (MachineEntity node in nodes.Values)
            if (node.Definition.LogicId == "radio-station") Add(node);
        // 拓扑变化时预计算依赖闭包，不为可见区块以外的自动化反复扫整世界。
        while (scope.AutomationQueue.Count > 0)
        {
            MachineEntity node = scope.AutomationQueue.Dequeue();
            if (node.Network != null)
                foreach (MachineEntity member in node.Network.Nodes) Add(member);
            if (node.ElectricalNetwork != null)
                foreach (MachineEntity member in node.ElectricalNetwork.Nodes) Add(member);
            if (node.Definition.Electrical?.Role == "switch")
                foreach (string contact in new[] { node.Definition.Electrical.ContactA, node.Definition.Electrical.ContactB })
                {
                    MachineEntity wire = electricalGraph.GetWire(node.Cell + CircuitSignalDefinition.Offset(contact, node.RotationQuarterTurns));
                    if (wire?.ElectricalNetwork != null)
                        foreach (MachineEntity member in wire.ElectricalNetwork.Nodes) Add(member);
                }
            if (fluidGraph != null && fluidGraph.NodeComponents.TryGetValue(node.Id, out var fluid))
                foreach (MachineEntity member in fluid.Nodes) Add(member);
        }
        if (rebuildSignals) foreach (MachineEntity wire in signalGraph.ChangedWires) NotifySignalStateChanged(wire);
        if (rebuildSignals) foreach (MachineEntity node in signalGraph.ChangedDevices) NotifySignalStateChanged(node);
        foreach (MachineEntity node in signalGraph.Devices)
            if (node.Logic is MachineRadioDeviceLogic radio) radio.RefreshRegistration();
        void Add(MachineEntity node)
        { if (scope.AutomationNodes.Add(node.Id)) scope.AutomationQueue.Enqueue(node); }
    }
    private static bool RequiresSignalSimulation(MechanicalNetwork network)
    {
        foreach (MachineEntity node in network.Nodes)
            if (scope.AutomationNodes.Contains(node.Id)) return true;
        return false;
    }
    internal static bool RequiresAutomationSimulation(MachineEntity node) => scope.AutomationNodes.Contains(node.Id);
    private static bool SampleActualMachineWorkingState(MachineEntity node)
    {
        if (node.Definition.Signal.SourceProvider == "electrical") return ReadActualElectricalWorkingState(node);
        return node.Logic is IMachineWorkingStateSource source && source.IsActuallyWorking;
    }
    // 电气业务只报告本轮真实能量交换，储量和开关不构成工作状态。
    private static bool ReadActualElectricalWorkingState(MachineEntity node)
    {
        if (node.Definition.IsConverter)
            return node.ConversionMode == ElectricalConversionMode.Motor
                ? node.ElectricalSuppliedWatts > 0f && node.SpeedRpm > 0f : node.ElectricalGeneratedWatts > 0f;
        return node.ElectricalGeneratedWatts > 0f || node.ElectricalSuppliedWatts > 0f;
    }
    private static void AdvanceCircuitSignals(float seconds)
    {
        if (signalGraph == null) return;
        signalGraph.Advance(SampleActualMachineWorkingState, NotifySignalStateChanged);
        foreach (MachineEntity node in signalGraph.Devices)
        {
            if (node.Definition.Signal.Kind == "relay" && node.ElectricalContactsClosed != node.SignalInputA)
            { node.ElectricalContactsClosed = node.SignalInputA; scope.ElectricalTopologyDirty = true; NotifySignalStateChanged(node); }
            if (node.Logic is MachineRadioSignalConverterLogic converter) converter.PublishSignalBit();
        }
    }
    private static void NotifySignalStateChanged(MachineEntity node)
    {
        if (GameNetwork.HasStateAuthority) node.SignalRevision++;
        NotifySurfaceCellChanged(node.Cell);
        if (interactions.TryGetValue(node.Id, out var target)) target.RefreshPanel();
        SignalStateChanged?.Invoke(node);
    }
    internal static void NotifyRadioReceptionChanged(MachineEntity node)
    {
        using var nodeScope = UseNodeScope(node);
        if (Contains(node)) NotifySignalStateChanged(node);
    }
    public static byte CaptureCircuitSignalBits(MachineEntity node)
        => node.CircuitStateBits;
    public static void ApplyRemoteCircuitState(int id, byte bits, ulong revision, float suppliedWatts)
    {
        if (!nodes.TryGetValue(id, out MachineEntity node)) return;
        if (revision < node.SignalRevision) return;
        node.SignalRevision = revision;
        node.SignalOutput = (bits & 1) != 0; node.SignalInputA = (bits & 2) != 0; node.SignalInputB = (bits & 4) != 0;
        node.ElectricalContactsClosed = (bits & 8) != 0; node.RadioHasSource = (bits & 16) != 0; node.RadioFeedbackBlocked = (bits & 32) != 0;
        node.RadioPowered = (bits & 64) != 0; node.ElectricalSuppliedWatts = suppliedWatts;
        NotifySignalStateChanged(node);
    }
    #endregion

    #region 接线与配置事务
    private static bool ValidateSignalPlacement(MachineDefinition definition, Vector2Int cell, out string reason)
    {
        reason = null;
        if (definition.Electrical?.IsWire == true && graph.At(cell, 0)?.Definition.Electrical?.Role == "switch" ||
            definition.Electrical?.Role == "switch" && graph.At(cell, 3)?.Definition.Electrical?.IsWire == true)
        { reason = "继电器主体格不能穿过电线"; return false; }
        return true;
    }
    private static bool HasCandidateRadioFeedback(MachineEntity candidate)
    {
        if (candidate.Definition.Signal?.IsWire != true && candidate.Definition.Signal?.Kind != "radio") return false;
        var candidates = new List<MachineEntity>(nodes.Count + 1);
        foreach (MachineEntity node in nodes.Values)
            if (!ReferenceEquals(node, candidate)) candidates.Add(node);
        candidates.Add(candidate);
        return HasCandidateRadioFeedback(candidates, new[] { candidate });
    }
    private static bool HasCandidateRadioFeedback(List<MachineEntity> candidates, IEnumerable<MachineEntity> changedNodes)
    {
        // 候选图只验证连接，不修改正式节点的反馈资格。
        var originalBlocks = new List<(MachineEntity Node, bool Blocked)>();
        foreach (MachineEntity node in candidates) originalBlocks.Add((node, node.RadioFeedbackBlocked));
        var topology = graph.Topology;
        var candidateGraph = new CircuitSignalNetworkGraph(topology);
        try
        {
            candidateGraph.Rebuild(candidates, false);
            if (!candidateGraph.HasRadioFeedback) return false;
            foreach (MachineEntity node in changedNodes)
                if (candidateGraph.IsNodeInBlockedRadioFeedbackNetwork(node)) return true;
            return false;
        }
        finally { foreach (var old in originalBlocks) old.Node.RadioFeedbackBlocked = old.Blocked; }
    }
    private static void ValidateTransferredCircuitTopology(List<ScopeNodeTransfer> transfers, HashSet<int> identities, bool sameScope)
    {
        bool relevant = false;
        foreach (ScopeNodeTransfer transfer in transfers)
            relevant |= transfer.Node.Definition.Signal != null || transfer.Node.Definition.Electrical?.IsWire == true ||
                transfer.Node.Definition.Electrical?.Role == "switch";
        if (!relevant) return;
        var finalNodes = new List<MachineEntity>(nodes.Count + transfers.Count);
        var changedNodes = new List<MachineEntity>(transfers.Count);
        foreach (MachineEntity node in nodes.Values) if (!sameScope || !identities.Contains(node.Id)) finalNodes.Add(node);
        foreach (ScopeNodeTransfer transfer in transfers)
        {
            var node = new MachineEntity { Id = transfer.Node.Id, Definition = transfer.Node.Definition,
                Cell = transfer.Placement.Cell, RotationQuarterTurns = transfer.Placement.QuarterTurns,
                RadioChannel = transfer.Node.RadioChannel, RadioTransmitMode = transfer.Node.RadioTransmitMode };
            finalNodes.Add(node); changedNodes.Add(node);
        }
        var wires = new HashSet<Vector2Int>();
        foreach (MachineEntity node in finalNodes) if (node.Definition.Electrical?.IsWire == true) wires.Add(node.Cell);
        foreach (MachineEntity node in finalNodes)
            if (node.Definition.Electrical?.Role == "switch" && wires.Contains(node.Cell))
                throw new InvalidOperationException("继电器主体格不能穿过电线");
        if (HasCandidateRadioFeedback(finalNodes, changedNodes))
            throw new InvalidOperationException("接线会形成同网同频道无线收发回环");
    }
    internal static bool TryChangeRadioConfiguration(MachineRadioDeviceLogic radio, string operation, string argument)
    {
        MachineRadioConfiguration state = radio.State;
        int channel = state.Channel; bool transmit = state.Transmit; bool enabled = state.Enabled;
        if (operation == "radio.channel")
        {
            if (!int.TryParse(argument, out channel) || channel < RadioSignalService.MinimumChannel || channel > RadioSignalService.MaximumChannel) return false;
        }
        else if (operation == "radio.mode")
        { if (argument != "tx" && argument != "rx") return false; transmit = argument == "tx"; }
        else if (operation == "radio.power")
        { if (!bool.TryParse(argument, out enabled)) return false; }
        else return false;
        MachineEntity node = radio.Entity;
        int oldChannel = node.RadioChannel; bool oldTransmit = node.RadioTransmitMode;
        node.RadioChannel = channel; node.RadioTransmitMode = transmit;
        bool invalid;
        try { invalid = node.Definition.Signal?.Kind == "radio" && HasCandidateRadioFeedback(node); }
        finally { node.RadioChannel = oldChannel; node.RadioTransmitMode = oldTransmit; }
        if (invalid) { radio.ConfigurationFailureReason = "接线会形成同网同频道无线收发回环"; return false; }
        radio.Unregister();
        state.Channel = channel; state.Transmit = transmit; state.Enabled = enabled;
        node.RadioChannel = channel; node.RadioTransmitMode = transmit;
        node.RadioReceiving = node.RadioHasSource = node.SignalOutput = false;
        dirty = true;
        radio.ConfigurationChanged();
        return true;
    }
    public static ElectricalNetwork GetElectricalContactNetwork(MachineEntity node, bool contactB)
    {
        if (node?.ScopeKey == null) return null;
        using var nodeScope = UseNodeScope(node);
        ElectricalDefinition electrical = node.Definition.Electrical;
        Vector2Int cell = node.Cell + CircuitSignalDefinition.Offset(contactB ? electrical.ContactB : electrical.ContactA, node.RotationQuarterTurns);
        return electricalGraph?.GetWire(cell)?.ElectricalNetwork;
    }
    #endregion
}
