using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>设备业务提供真实工作结果，信号图不识别具体设备。</summary>
public interface IMachineWorkingStateSource
{
    bool IsActuallyWorking { get; }
}

[Serializable, MemoryPack.MemoryPackable]
public sealed partial class CircuitSignalDefinition
{
    #region 信号端口配置
    public string Kind = "source";
    public string InputA = "left";
    public string InputB = "down";
    public string Output = "up";
    public string SourceProvider = "logic";
    [MemoryPack.MemoryPackIgnore] public bool IsWire => Kind == "wire";
    [MemoryPack.MemoryPackIgnore] public bool IsGate => Kind == "and" || Kind == "or" || Kind == "not" || Kind == "xor";
    [MemoryPack.MemoryPackIgnore] public bool HasOutput => Kind == "source" || IsGate || Kind == "radio";
    [MemoryPack.MemoryPackIgnore] public bool HasInput => IsGate || Kind == "relay" || Kind == "radio";
    public static Vector2Int Offset(string port, int rotation)
    {
        int direction = port switch { "right" => 0, "up" => 1, "left" => 2, "down" => 3, _ => -1 };
        return direction < 0 ? Vector2Int.zero : ((direction + rotation) & 3) switch
        { 0 => Vector2Int.right, 1 => Vector2Int.up, 2 => Vector2Int.left, _ => Vector2Int.down };
    }
    public static bool EvaluateGate(string kind, bool a, bool b)
        => kind switch { "and" => a && b, "or" => a || b, "not" => !a, "xor" => a != b, _ => false };
    public void Validate(string id)
    {
        if (!IsWire && !IsGate && Kind != "source" && Kind != "relay" && Kind != "radio")
            throw new ArgumentException("信号设备类型无效：" + id);
        if (HasInput && string.IsNullOrEmpty(InputA) || HasOutput && string.IsNullOrEmpty(Output) ||
            IsGate && Kind != "not" && string.IsNullOrEmpty(InputB))
            throw new ArgumentException("启用的信号端口缺少方向：" + id);
        foreach (string port in new[] { InputA, InputB, Output })
            if (!string.IsNullOrEmpty(port) && port != "cell" && port != "left" && port != "right" && port != "up" && port != "down")
                throw new ArgumentException("信号端口方向无效：" + id);
    }
    #endregion
}

public sealed partial class MachineEntity
{
    #region 非持久信号状态
    public bool SignalOutput, SignalInputA, SignalInputB, ElectricalContactsClosed;
    public bool RadioReceiving, RadioHasSource, RadioFeedbackBlocked, RadioPowered;
    public int RadioChannel = 1;
    public bool RadioTransmitMode = true;
    public ulong SignalRevision;
    public byte CircuitStateBits => (byte)((SignalOutput ? 1 : 0) | (SignalInputA ? 2 : 0) | (SignalInputB ? 4 : 0) |
        (ElectricalContactsClosed ? 8 : 0) | (RadioHasSource ? 16 : 0) | (RadioFeedbackBlocked ? 32 : 0) | (RadioPowered ? 64 : 0));
    #endregion
}

/// <summary>四邻信号覆盖层；所有门先读旧值，再统一提交多源 OR。</summary>
public sealed class CircuitSignalNetworkGraph
{
    #region 世界拓扑与连接
    public sealed class Network
    {
        public readonly List<MachineEntity> Wires = new();
        public bool Value;
        internal int HighSources;
    }
    private static readonly Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    private readonly WorldTopologyDomain topology;
    private readonly Dictionary<Vector2Int, MachineEntity> wires = new();
    private readonly Dictionary<Vector2Int, Network> connections = new();
    private readonly Dictionary<Vector2Int, int> portMasks = new();
    private readonly List<MachineEntity> devices = new();
    private readonly Dictionary<int, Network> outputs = new();
    private readonly HashSet<Network> dirty = new();
    private readonly HashSet<MachineEntity> changed = new();
    private readonly List<(MachineEntity Node, bool Value)> pending = new();
    public IReadOnlyList<MachineEntity> Devices => devices;
    public readonly List<MachineEntity> ChangedWires = new();
    public readonly List<MachineEntity> ChangedDevices = new();
    private readonly Dictionary<int, byte> previousDeviceBits = new();
    public CircuitSignalNetworkGraph(WorldTopologyDomain topology) => this.topology = topology;
    private Vector2Int Normalize(Vector2Int cell) => new(topology.NormalizeX(cell.x), topology.NormalizeY(cell.y));
    public Network At(Vector2Int cell) => connections.TryGetValue(Normalize(cell), out var network) ? network : null;
    public Network Port(MachineEntity node, string direction) => At(node.Cell + CircuitSignalDefinition.Offset(direction, node.RotationQuarterTurns));
    public bool Read(MachineEntity node, string direction) => Port(node, direction)?.Value == true;
    public bool ReadWire(MachineEntity node) => At(node.Cell)?.Value == true;
    public int ConnectionMask(Vector2Int cell)
    {
        int mask = 0;
        for (int i = 0; i < directions.Length; i++)
            if (wires.ContainsKey(Normalize(cell + directions[i]))) mask |= 1 << i;
        return mask | (portMasks.TryGetValue(Normalize(cell), out int ports) ? ports : 0);
    }
    public void Rebuild(IEnumerable<MachineEntity> source, bool updateNodeState = true)
    {
        wires.Clear(); connections.Clear(); devices.Clear(); outputs.Clear(); portMasks.Clear(); ChangedWires.Clear();
        ChangedDevices.Clear(); previousDeviceBits.Clear();
        var sorted = new List<MachineEntity>();
        foreach (MachineEntity node in source)
        {
            if (node.Definition.Signal == null) continue;
            if (node.Definition.Signal.IsWire) wires.Add(Normalize(node.Cell), node);
            else { devices.Add(node); if (updateNodeState) previousDeviceBits[node.Id] = node.CircuitStateBits; }
            sorted.Add(node);
        }
        sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
        devices.Sort((a, b) => a.Id.CompareTo(b.Id));
        var queue = new Queue<Vector2Int>();
        foreach (MachineEntity wire in sorted)
        {
            if (!wire.Definition.Signal.IsWire || connections.ContainsKey(Normalize(wire.Cell))) continue;
            var network = new Network();
            queue.Enqueue(Normalize(wire.Cell)); connections.Add(Normalize(wire.Cell), network);
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                network.Wires.Add(wires[cell]);
                foreach (Vector2Int direction in directions)
                {
                    Vector2Int next = Normalize(cell + direction);
                    if (!wires.ContainsKey(next) || connections.ContainsKey(next)) continue;
                    connections.Add(next, network); queue.Enqueue(next);
                }
            }
        }
        foreach (MachineEntity node in devices)
        {
            var signal = node.Definition.Signal;
            AddMask(signal.HasInput && (signal.Kind != "radio" || node.RadioTransmitMode), signal.InputA);
            AddMask(signal.IsGate && signal.Kind != "not", signal.InputB);
            AddMask(signal.HasOutput && (signal.Kind != "radio" || !node.RadioTransmitMode), signal.Output);
            void AddMask(bool enabled, string port)
            {
                if (!enabled) return;
                Vector2Int cell = Normalize(node.Cell + CircuitSignalDefinition.Offset(port, node.RotationQuarterTurns));
                portMasks.TryGetValue(cell, out int mask);
                for (int i = 0; i < directions.Length; i++)
                    if (Normalize(cell + directions[i]) == node.Cell) mask |= 1 << i;
                portMasks[cell] = mask;
            }
            if (!signal.HasOutput || signal.Kind == "radio" && node.RadioTransmitMode) continue;
            Network network = Port(node, node.Definition.Signal.Output);
            if (network == null) continue;
            outputs.Add(node.Id, network);
            if (node.SignalOutput) network.HighSources++;
            network.Value = network.HighSources > 0;
        }
        if (updateNodeState) RefreshRadioFeedbackBlocks(true);
        if (updateNodeState) foreach (var entry in wires)
        {
            bool value = At(entry.Key)?.Value == true;
            if (entry.Value.SignalOutput != value) ChangedWires.Add(entry.Value);
            entry.Value.SignalOutput = value;
        }
        if (updateNodeState) foreach (MachineEntity node in devices)
            if (previousDeviceBits[node.Id] != node.CircuitStateBits) ChangedDevices.Add(node);
    }
    #endregion

    #region 固定步提交与反馈资格
    public void Advance(Func<MachineEntity, bool> sampleWorkingState, Action<MachineEntity> notify)
    {
        pending.Clear(); dirty.Clear(); changed.Clear();
        foreach (MachineEntity node in devices)
        {
            var signal = node.Definition.Signal;
            bool a = signal.HasInput && Read(node, signal.InputA);
            bool b = signal.IsGate && signal.Kind != "not" && Read(node, signal.InputB);
            if (node.SignalInputA != a || node.SignalInputB != b) changed.Add(node);
            node.SignalInputA = a; node.SignalInputB = b;
            bool value = signal.IsGate ? CircuitSignalDefinition.EvaluateGate(signal.Kind, a, b) :
                signal.Kind == "source" ? sampleWorkingState(node) :
                signal.Kind == "radio" && !node.RadioTransmitMode && !node.RadioFeedbackBlocked && node.RadioReceiving;
            if (signal.HasOutput && (signal.Kind != "radio" || !node.RadioTransmitMode)) pending.Add((node, value));
        }
        foreach (var entry in pending)
        {
            if (entry.Node.SignalOutput == entry.Value) continue;
            entry.Node.SignalOutput = entry.Value; changed.Add(entry.Node);
            if (!outputs.TryGetValue(entry.Node.Id, out Network network)) continue;
            network.HighSources += entry.Value ? 1 : -1; dirty.Add(network);
        }
        foreach (Network network in dirty)
        {
            bool value = network.HighSources > 0;
            if (network.Value == value) continue;
            network.Value = value;
            foreach (MachineEntity wire in network.Wires) { wire.SignalOutput = value; changed.Add(wire); }
        }
        // 消费端读取统一提交后的值，触点留给下一轮电网求解。
        foreach (MachineEntity node in devices)
            if (node.Definition.Signal.Kind == "relay" || node.Definition.Signal.Kind == "radio")
            {
                bool value = Read(node, node.Definition.Signal.InputA);
                if (value != node.SignalInputA) changed.Add(node);
                node.SignalInputA = value;
            }
        foreach (MachineEntity node in changed) notify?.Invoke(node);
    }
    public bool HasRadioFeedback => RefreshRadioFeedbackBlocks(false);
    public bool IsNodeInBlockedRadioFeedbackNetwork(MachineEntity node)
    {
        if (node.Definition.Signal?.Kind == "radio") return node.RadioFeedbackBlocked;
        if (node.Definition.Signal?.IsWire != true) return false;
        Network network = At(node.Cell);
        foreach (MachineEntity transmitter in devices)
            if (transmitter.RadioFeedbackBlocked && transmitter.RadioTransmitMode &&
                ReferenceEquals(network, Port(transmitter, transmitter.Definition.Signal.InputA))) return true;
        return false;
    }
    private bool RefreshRadioFeedbackBlocks(bool clearBlockedOutputs)
    {
        bool blocked = false;
        foreach (MachineEntity node in devices) node.RadioFeedbackBlocked = false;
        foreach (MachineEntity transmitter in devices)
        {
            if (transmitter.Definition.Signal.Kind != "radio" || !transmitter.RadioTransmitMode) continue;
            Network input = Port(transmitter, transmitter.Definition.Signal.InputA);
            if (input == null) continue;
            foreach (MachineEntity receiver in devices)
                if (receiver.Definition.Signal.Kind == "radio" && !receiver.RadioTransmitMode &&
                    receiver.RadioChannel == transmitter.RadioChannel && ReferenceEquals(input, Port(receiver, receiver.Definition.Signal.Output)))
                { transmitter.RadioFeedbackBlocked = receiver.RadioFeedbackBlocked = true; blocked = true; }
        }
        if (clearBlockedOutputs) foreach (MachineEntity node in devices)
            if (node.RadioFeedbackBlocked)
            {
                node.RadioReceiving = node.RadioHasSource = false;
                if (!node.SignalOutput) continue;
                node.SignalOutput = false;
                if (outputs.TryGetValue(node.Id, out Network network))
                { network.HighSources--; network.Value = network.HighSources > 0; }
            }
        return blocked;
    }
    #endregion
}
