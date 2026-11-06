using System.Text.Json;
using UnityEngine;

public static class CircuitSignalTests
{
    #region 生产图行为回归
    public static void Run(Action<bool, string> check)
    {
        // 使用正式目录验证禁用端口，防止配置在启动阶段被拒绝。
        using (JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(
            "Assets/Resources/Config/Mechanical/mechanical-catalog.json")))
            foreach (JsonElement node in catalog.RootElement.GetProperty("Nodes").EnumerateArray())
                if (node.TryGetProperty("Signal", out JsonElement signal))
                {
                    var definition = JsonSerializer.Deserialize<CircuitSignalDefinition>(signal.GetRawText(),
                        new JsonSerializerOptions { IncludeFields = true });
                    definition.Validate(node.GetProperty("Id").GetString());
                    check(true, "正式信号目录通过业务校验");
                }
        foreach (var definition in new[] { new CircuitSignalDefinition { Output = "" },
            new CircuitSignalDefinition { Kind = "and", InputB = "" },
            new CircuitSignalDefinition { Kind = "radio", InputA = "" } })
        {
            bool rejected = false;
            try { definition.Validate("invalid-active-port"); }
            catch (ArgumentException) { rejected = true; }
            check(rejected, "活动端口缺失时错误接入设备中心");
        }
        MachineEntity Node(int id, int x, int y, string kind) => new()
        { Id = id, Cell = new(x, y), Definition = new() { Ports = "none", Signal = new() { Kind = kind, Output = "right" } } };
        foreach (var (kind, truth) in new[] { ("and", new[] { false, false, false, true }),
            ("or", new[] { false, true, true, true }), ("not", new[] { true, false, true, false }),
            ("xor", new[] { false, true, true, false }) })
        for (int bits = 0; bits < 4; bits++)
        {
            var a = Node(1, -2, 0, "source"); var b = Node(2, 0, -2, "source"); b.Definition.Signal.Output = "up";
            var gate = Node(3, 0, 0, kind); var output = Node(4, 1, 0, "wire");
            var graph = new CircuitSignalNetworkGraph(default);
            graph.Rebuild(new[] { a, b, gate, output, Node(5, -1, 0, "wire"), Node(6, 0, -1, "wire") });
            bool Sample(MachineEntity node) => node.Id == 1 ? (bits & 1) != 0 : (bits & 2) != 0;
            graph.Advance(Sample, null);
            check(output.SignalOutput == (kind == "not"), "门没有读取上一固定步快照");
            graph.Advance(Sample, null);
            check(output.SignalOutput == truth[bits], kind + "真值表错误");
        }
        var inverter = Node(1, 0, 0, "not");
        var feedbackGraph = new CircuitSignalNetworkGraph(default);
        feedbackGraph.Rebuild(new[] { inverter, Node(2,-1,0,"wire"), Node(3,-1,1,"wire"),
            Node(4,0,1,"wire"), Node(5,1,1,"wire"), Node(6,1,0,"wire") });
        for (int tick = 0; tick < 20; tick++)
        {
            feedbackGraph.Advance(_ => false, null);
            check(inverter.SignalOutput == ((tick & 1) == 0), "反相环没有以固定步确定性振荡");
        }
        var source = Node(1, -1, 0, "source"); source.SignalOutput = true;
        var wire = Node(2, 0, 0, "wire"); wire.SignalOutput = false;
        var candidate = new CircuitSignalNetworkGraph(default);
        candidate.Rebuild(new[] { source, wire }, false);
        check(!wire.SignalOutput, "候选接线改写正式线路运行态");
        var tx = Node(10,-1,0,"radio"); tx.Definition.Signal.InputA = "right";
        var rx = Node(11,1,0,"radio"); rx.RadioTransmitMode = false; rx.Definition.Signal.Output = "left";
        rx.RadioHasSource = true;
        candidate.Rebuild(new[] { tx, rx, wire });
        check(rx.RadioFeedbackBlocked && !rx.RadioHasSource && candidate.ChangedDevices.Contains(rx), "零电平来源被反馈阻断时没有设备通知");
        candidate.Rebuild(new[] { tx, rx });
        check(!rx.RadioFeedbackBlocked && candidate.ChangedDevices.Contains(rx), "解除反馈阻断没有设备通知");

        MachineEntity PowerNode(int id, int x, string role)
        {
            var node = Node(id, x, 0, "wire"); node.Definition.Signal = null;
            node.Definition.Electrical = new() { Role = role, PowerWatts = 60 };
            return node;
        }
        var left = PowerNode(1,-1,"wire"); var right = PowerNode(2,2,"wire");
        var relayA = PowerNode(3,0,"switch"); var relayB = PowerNode(4,1,"switch");
        var generator = PowerNode(5,-1,"generator"); var load = PowerNode(6,2,"consumer");
        var electrical = new ElectricalNetworkGraph(default);
        foreach (int bits in new[] { 0, 1, 3, 2, 0 })
        {
            relayA.ElectricalContactsClosed = (bits & 1) != 0;
            relayB.ElectricalContactsClosed = (bits & 2) != 0;
            electrical.Rebuild(new[] { left, right, relayA, relayB, generator, load });
            foreach (var network in electrical.Networks) ElectricalNetworkGraph.Solve(network, .05f, _ => 1, _ => 1, null);
            check(load.ElectricalSuppliedWatts == (bits == 3 ? 60 : 0), "连续继电器未按独立触点真实供电");
        }
    }
    #endregion
}
