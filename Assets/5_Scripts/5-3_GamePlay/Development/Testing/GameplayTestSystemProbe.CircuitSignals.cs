using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 电路正式算法原始采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterCircuitSignalRules()
    {
        Register("circuit_signals", "gate_table", CaptureCircuitGateTruthTable);
        Register("circuit_signals", "topology", CaptureCircuitTopologyAndFeedback);
        Register("circuit_signals", "relay", CaptureCircuitRelayElectricalContacts);
        Register("radio", "propagation", CaptureRadioPropagationAndLifecycle);
    }
    private static MachineEntity CircuitProbeNode(int id, int x, int y, string kind = "wire")
        => new()
        {
            Id = id, Cell = new Vector2Int(x, y),
            Definition = new MachineDefinition { Id = "probe:" + id, Ports = "none",
                Signal = new CircuitSignalDefinition { Kind = kind, Output = "right" } }
        };
    private static JObject CaptureCircuitGateTruthTable(JObject args)
    {
        string kind = args.Value<string>("kind") ?? "and";
        var rows = new JArray();
        for (int bits = 0; bits < 4; bits++)
        {
            var a = CircuitProbeNode(1, -2, 0, "source");
            var b = CircuitProbeNode(2, 0, -2, "source"); b.Definition.Signal.Output = "up";
            var gate = CircuitProbeNode(3, 0, 0, kind);
            var output = CircuitProbeNode(4, 1, 0);
            var graph = new CircuitSignalNetworkGraph(default);
            graph.Rebuild(new[] { a, b, gate, output, CircuitProbeNode(5, -1, 0), CircuitProbeNode(6, 0, -1) });
            bool Sample(MachineEntity node) => node.Id == 1 ? (bits & 1) != 0 : (bits & 2) != 0;
            graph.Advance(Sample, null);
            bool first = output.SignalOutput;
            graph.Advance(Sample, null);
            rows.Add(new JObject { ["a"] = (bits & 1) != 0, ["b"] = (bits & 2) != 0,
                ["first"] = first, ["output"] = output.SignalOutput });
        }
        return new JObject { ["rows"] = rows };
    }
    private static JObject CaptureCircuitTopologyAndFeedback(JObject args)
    {
        var left = CircuitProbeNode(1, 0, 1, "source"); left.Definition.Signal.Output = "down";
        var right = CircuitProbeNode(2, 2, 1, "source"); right.Definition.Signal.Output = "down";
        var wire = CircuitProbeNode(3, 0, 0);
        var middle = CircuitProbeNode(4, 1, 0);
        var end = CircuitProbeNode(5, 2, 0);
        var graph = new CircuitSignalNetworkGraph(default);
        var nodes = new List<MachineEntity> { left, right, wire, middle, end };
        graph.Rebuild(nodes);
        var sequence = new JArray();
        foreach (int bits in new[] { 1, 3, 2, 0 })
        { graph.Advance(n => n.Id == 1 ? (bits & 1) != 0 : (bits & 2) != 0, null); sequence.Add(end.SignalOutput); }
        graph.Advance(_ => true, null);
        nodes.Remove(middle); graph.Rebuild(nodes);
        bool split = !ReferenceEquals(graph.At(wire.Cell), graph.At(end.Cell));
        nodes.Remove(right); graph.Rebuild(nodes); graph.Advance(_ => false, null);
        bool removed = end.SignalOutput;

        var tx = CircuitProbeNode(10, -1, 0, "radio"); tx.Definition.Signal.InputA = "right";
        var rx = CircuitProbeNode(11, 1, 0, "radio"); rx.Definition.Signal.Output = "left"; rx.RadioTransmitMode = false;
        graph.Rebuild(new[] { tx, rx, wire }, false);
        bool feedback = graph.HasRadioFeedback;
        rx.RadioHasSource = true;
        graph.Rebuild(new[] { tx, rx, wire });
        bool blockedNotification = graph.ChangedDevices.Contains(rx);
        rx.RadioChannel = 2; graph.Rebuild(new[] { tx, rx, wire });
        bool unblockedNotification = graph.ChangedDevices.Contains(rx);
        bool channelsSeparate = graph.HasRadioFeedback;
        var wrap = new CircuitSignalNetworkGraph(new WorldTopologyDomain(int2.zero, new int2(32, 32), true));
        wrap.Rebuild(new[] { CircuitProbeNode(20, 31, 0), CircuitProbeNode(21, 0, 0) });
        return new JObject { ["or"] = sequence, ["split"] = split, ["removed"] = removed,
            ["feedback"] = feedback, ["differentChannelsFeedback"] = channelsSeparate,
            ["blockedNotification"] = blockedNotification, ["unblockedNotification"] = unblockedNotification,
            ["wrapped"] = ReferenceEquals(wrap.At(new Vector2Int(31, 0)), wrap.At(Vector2Int.zero)) };
    }
    private static JObject CaptureCircuitRelayElectricalContacts(JObject args)
    {
        MachineEntity Node(int id, int x, string role)
        {
            var node = CircuitProbeNode(id, x, 0);
            node.Definition.Signal = null;
            node.Definition.Electrical = new ElectricalDefinition { Role = role, Connection = "cell", NominalVoltage = 120,
                PowerWatts = 60, MaxCurrentAmps = 10 };
            return node;
        }
        var wireA = Node(1, -1, "wire"); var wireB = Node(2, 2, "wire");
        var relayA = Node(3, 0, "switch"); var relayB = Node(4, 1, "switch");
        var generator = Node(5, -1, "generator"); var load = Node(6, 2, "consumer");
        var nodes = new[] { wireA, wireB, relayA, relayB, generator, load };
        var graph = new ElectricalNetworkGraph(default);
        var results = new JArray();
        foreach (int state in new[] { 0, 1, 3, 2, 0 })
        {
            relayA.ElectricalContactsClosed = (state & 1) != 0;
            relayB.ElectricalContactsClosed = (state & 2) != 0;
            graph.Rebuild(nodes);
            foreach (ElectricalNetwork network in graph.Networks)
                ElectricalNetworkGraph.Solve(network, .05f, _ => 1f, _ => 1f, null);
            results.Add(new JObject { ["supplied"] = load.ElectricalSuppliedWatts,
                ["connected"] = ReferenceEquals(wireA.ElectricalNetwork, wireB.ElectricalNetwork) });
        }
        relayA.ElectricalContactsClosed = relayB.ElectricalContactsClosed = true;
        relayB.RotationQuarterTurns = 1; graph.Rebuild(nodes);
        return new JObject { ["sequence"] = results, ["rotatedConnected"] = ReferenceEquals(wireA.ElectricalNetwork, wireB.ElectricalNetwork) };
    }
    private static JObject CaptureRadioPropagationAndLifecycle(JObject args)
    {
        var service = new RadioSignalService("test-world", default);
        RadioCircuitState received = default;
        Vector2 position = Vector2.right * 100;
        int textCount = 0;
        service.Register(1, 1, () => Vector2.zero, RadioEndpointCapabilities.TransmitCircuit | RadioEndpointCapabilities.TransmitText);
        service.Register(2, 1, () => position, RadioEndpointCapabilities.ReceiveCircuit | RadioEndpointCapabilities.ReceiveText,
            state => received = state, _ => textCount++);
        service.Register(3, 1, () => Vector2.zero, RadioEndpointCapabilities.TransmitCircuit);
        service.SendCircuitBit(1, false);
        bool validZero = received.HasSource && !received.Value;
        service.SendCircuitBit(3, true); bool merged = received.Value;
        service.Unregister(3); bool removedHigh = received.HasSource && !received.Value;
        service.SendCircuitBit(1, true); bool rising = received.Value;
        service.TryGetLastCircuitFrame(1, out RadioCircuitFrame oldFrame);
        service.SendCircuitBit(1, false);
        service.ReceiveCircuitFrame(oldFrame);
        bool oldFrameOverwrote = received.Value;
        service.ReceiveCircuitFrame(new RadioCircuitFrame("another-world", 1, oldFrame.Epoch,
            oldFrame.Sequence + 100, 1, Vector2.zero, true));
        bool wrongWorldOverwrote = received.Value;
        service.SendText(1, "sender", "hello");
        position = Vector2.right * 200;
        service.SendText(1, "sender", "out of range");
        service.Advance(3.1f); bool expired = received.HasSource;
        service.Unregister(2); received = default;
        service.Register(2, 2, () => Vector2.zero, RadioEndpointCapabilities.ReceiveCircuit, state => received = state);
        service.Advance(1.1f); bool wrongChannel = received.HasSource;
        service.Clear();
        var wrap = new RadioSignalService("wrapped", new WorldTopologyDomain(int2.zero, new int2(1024, 1024), true));
        bool wrapped = false;
        wrap.Register(1, 1, () => Vector2.one, RadioEndpointCapabilities.TransmitCircuit);
        wrap.Register(2, 1, () => new Vector2(1023, 1), RadioEndpointCapabilities.ReceiveCircuit, state => wrapped = state.Value);
        wrap.SendCircuitBit(1, true);
        return new JObject { ["validZero"] = validZero, ["merged"] = merged, ["removedHigh"] = removedHigh,
            ["rising"] = rising, ["textCount"] = textCount, ["expiredHasSource"] = expired,
            ["oldFrameOverwrote"] = oldFrameOverwrote, ["wrongWorldOverwrote"] = wrongWorldOverwrote,
            ["wrongChannelHasSource"] = wrongChannel, ["wrapped"] = wrapped };
    }
    #endregion
}
