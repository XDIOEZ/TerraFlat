using System.Text.Json;
using UnityEngine;

internal static class ConveyorLoadTests
{
    #region 传送带小负载与拐角回归
    internal static void Run(Action<bool, string> check)
    {
        // 从正式目录读取负载与手摇供给，避免测试数值和实际游戏各用一套。
        const string catalog = "Assets/Resources/Config/Mechanical/mechanical-catalog.json";
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(catalog));
        MachineDefinition Definition(string id)
        {
            JsonElement data = document.RootElement.GetProperty("Nodes").EnumerateArray()
                .Single(value => value.GetProperty("Id").GetString() == id);
            float Number(string key, float fallback = 0f)
                => data.TryGetProperty(key, out JsonElement value) ? value.GetSingle() : fallback;
            return new MachineDefinition
            {
                Kind = data.GetProperty("Kind").GetString(), Ports = data.GetProperty("Ports").GetString(),
                PortMode = data.TryGetProperty("PortMode", out JsonElement mode) ? mode.GetString()
                    : id == "HandCrank" ? "output" : "relay",
                Torque = Number("Torque"), TorqueLoad = Number("TorqueLoad"),
                TorqueLoadStep = Number("TorqueLoadStep", 10f), Rpm = Number("Rpm", 20f),
                Transport = data.TryGetProperty("Transport", out _) ? new TransportDefinition() : null
            };
        }

        for (int rotation = 0; rotation < 4; rotation++)
        foreach (bool mirrored in new[] { false, true })
        {
            Vector2Int Cell(int x, int y)
            {
                Vector2 value = ConveyorPath.Rotate(new Vector2(mirrored ? -x : x, y), rotation);
                return new Vector2Int((int)value.x, (int)value.y);
            }
            MachineEntity Node(int id, string kind, int x, int y, int facing = 0) => new()
            {
                Id = id, Cell = Cell(x, y), State = new(), Snapshot = new(), Definition = Definition(kind),
                RotationQuarterTurns = ((mirrored ? (2 - facing) & 3 : facing) + rotation) & 3
            };
            MachineEntity[] belts =
            {
                Node(1, "Conveyor_Wood", 0, 0, 1), Node(2, "Conveyor_Cloth", 0, 1, 1),
                Node(3, "Conveyor_Wood", 0, 2, 1), Node(4, "Conveyor_Cloth", 1, 2),
                Node(5, "Conveyor_Wood", 2, 2)
            };
            MachineEntity lower = Node(10, "HandCrank", 2, 1);
            MachineEntity upper = Node(14, "HandCrank", 2, 4);
            var nodes = belts.Concat(new[] { lower, Node(11, "Shaft_Wood", 1, 1),
                Node(12, "Gear_Wood", 1, 3, 1), Node(13, "Gear_Wood", 1, 4, 2), upper }).ToArray();
            var graph = new MechanicalNetworkGraph(new(16, 16), Vector2Int.zero, default(WorldTopologyDomain));
            graph.Rebuild(nodes);
            check(graph.Networks.Count == 1 && belts[2].ConveyorRoute.Curved, "截图布局没有连成带拐角的完整机械网");
            MechanicalNetwork network = graph.Networks[0];
            void Solve(MachineEntity source, int sign = 1) => MechanicalNetworkGraph.Solve(network,
                node => ReferenceEquals(node, source) ? 1f : 0f, node => node.Definition.Rpm * sign, 20f);

            // 独立样本复现旧故障：图边连通且上方曲柄跟转，但拐角因每段抬到十点而停机。
            float[] configuredSteps = belts.Select(belt => belt.Definition.TorqueLoadStep).ToArray();
            foreach (MachineEntity belt in belts) belt.Definition.TorqueLoadStep = 10f;
            Solve(lower);
            check(network.TorqueDemand == 50f && belts[2].FlowVisited && !belts[2].LoadSatisfied &&
                belts[2].Rpm == 0f && upper.SpeedRpm == 20f, "未复现拐角停带但上方曲柄跟转的旧故障");
            for (int i = 0; i < belts.Length; i++) belts[i].Definition.TorqueLoadStep = configuredSteps[i];

            foreach (MachineEntity source in new[] { lower, upper })
            foreach (int sign in new[] { 1, -1 })
            {
                Solve(source, sign);
                check(network.TorqueSupply == 20f && network.TorqueDemand == 10f,
                    $"五段带应只消耗10点而非50点：旋转{rotation} 镜像{mirrored} 实际需求{network.TorqueDemand}");
                foreach (MachineEntity belt in belts)
                {
                    check(belt.FlowVisited && belt.LoadSatisfied && belt.SpeedRpm == 20f,
                        "机械已连通但拐角或下游带面没有取得运行转速");
                    belt.GetLocalTorque(out _, out float demand);
                    check(demand == 2f, "面板与真实负载必须一致且每段只消耗两点");
                    ConveyorPath route = belt.ConveyorRoute;
                    Vector2 start = route.Curved ? ConveyorPath.Rotate(
                        new Vector2(-.5f + MathF.Sqrt(.125f), .5f - MathF.Sqrt(.125f)), route.Rotation) : default;
                    check(route.TryMove(start, .1f * Mathf.Sign(belt.Rpm), .5f, out Vector2 moved) &&
                        (moved - start).magnitude > .02f, "取得转速后直线与拐角运输路径必须实际前进");
                }
                check(lower.SpeedRpm == 20f && upper.SpeedRpm == 20f, "上下两手摇曲柄仍应沿真实图边跟转");
            }
            // 手动固定形状也必须使用同一负载求解，而不是只修复自动拐角。
            foreach (MachineEntity belt in belts) belt.ConveyorMode = belt.ConveyorRoute.ManualMode;
            graph.Rebuild(nodes); network = graph.Networks.Single(); Solve(lower);
            check(belts.All(node => node.LoadSatisfied && node.SpeedRpm == 20f), "手动拐角仍中断");
            Solve(null);
            check(nodes.All(node => node.Rpm == 0f), "松开动力后仍强行播放运行状态");
        }

        var sourceNode = new MachineEntity { Id = 100, Cell = new(-1, 0), Definition = Definition("HandCrank") };
        var chain = Enumerable.Range(0, 11).Select(i => new MachineEntity
        {
            Id = i + 101, Cell = new(i, 0), Definition = Definition("Conveyor_Wood"), RotationQuarterTurns = 1
        }).ToArray();
        // 侧向入轴、带路向上：十段满载，第十一段必须因真实扭矩不足停转。
        for (int i = 0; i < chain.Length; i++) { chain[i].Cell = new(0, i); chain[i].ConveyorMode = 2; }
        var budgetGraph = new MechanicalNetworkGraph(new(16, 16), Vector2Int.zero, default(WorldTopologyDomain));
        budgetGraph.Rebuild(chain.Prepend(sourceNode));
        MechanicalNetwork budget = budgetGraph.Networks.Single();
        MechanicalNetworkGraph.Solve(budget, node => ReferenceEquals(node, sourceNode) ? 1f : 0f, 20f);
        check(budget.TorqueSupply == 20f && budget.TorqueDemand == 22f, "小负载累计预算错误");
        check(chain.Take(10).All(node => node.LoadSatisfied) && !chain[10].LoadSatisfied && chain[10].Rpm == 0f,
            "修复小负载后不能绕过真实过载与近处优先规则");
        check(MechanicalNetworkGraph.RoundLoadTorqueToTens(2f) == 10f &&
            MechanicalNetworkGraph.RoundLoadTorqueToTens(15f) == 20f, "普通设备十点档规则被改变");
        check(MechanicalNetworkGraph.RoundLoadTorque(.3f, .25f) == .25f &&
            MechanicalNetworkGraph.RoundLoadTorque(.01f, .25f) == .25f &&
            MechanicalNetworkGraph.RoundLoadTorque(0f, .25f) == 0f, "自定义档位与最小非零负载规则错误");
        var generator = new MachineEntity { Definition = new() { TorqueLoad = 2f, TorqueLoadStep = 1f,
            Electrical = new() { Role = "converter" } } };
        generator.ConversionMode = ElectricalConversionMode.Motor;
        check(generator.RequiredTorque == 0f, "电驱模式不能消耗发电负载");
        generator.ConversionMode = ElectricalConversionMode.Generator;
        check(generator.RequiredTorque == 2f, "转换器发电负载没有复用同一档位规则");
    }
    #endregion
}
