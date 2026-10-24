using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 传送带负载与拐角规则采样
    private static void RegisterConveyorLoadOperations(Dictionary<string, System.Func<JObject, JObject>> result)
        => result.Add("mechanical/conveyor_loads", WorldConveyorLoads);

    // 独立节点读取正式配置，调用正式构图和求解，不接管玩家的机器世界。
    private static JObject WorldConveyorLoads(JObject args)
    {
        TextAsset asset = Resources.Load<TextAsset>("Config/Mechanical/mechanical-catalog");
        var definitions = (JArray)JObject.Parse(asset.text)["Nodes"];
        MachineDefinition Definition(string id)
        {
            MachineDefinition definition = definitions.Single(value => value.Value<string>("Id") == id)
                .ToObject<MachineDefinition>();
            definition.Validate();
            return definition;
        }
        int rotation = (args.Value<int?>("rotation") ?? 0) & 3;
        bool mirror = args.Value<bool?>("mirror") == true;
        MachineEntity Node(int id, string kind, int x, int y, int facing = 0)
        {
            Vector2 cell = ConveyorPath.Rotate(new Vector2(mirror ? -x : x, y), rotation);
            return new MachineEntity
            {
                Id = id, Cell = new Vector2Int((int)cell.x, (int)cell.y), State = new MachineState(),
                Snapshot = new Data_GeneralItem(), Definition = Definition(kind),
                RotationQuarterTurns = ((mirror ? (2 - facing) & 3 : facing) + rotation) & 3
            };
        }
        MachineEntity[] belts =
        {
            Node(1, "Conveyor_Wood", 0, 0, 1), Node(2, "Conveyor_Cloth", 0, 1, 1),
            Node(3, "Conveyor_Wood", 0, 2, 1), Node(4, "Conveyor_Cloth", 1, 2),
            Node(5, "Conveyor_Wood", 2, 2)
        };
        MachineEntity lower = Node(10, "HandCrank", 2, 1), upper = Node(14, "HandCrank", 2, 4);
        MachineEntity[] nodes = belts.Concat(new[] { lower, Node(11, "Shaft_Wood", 1, 1),
            Node(12, "Gear_Wood", 1, 3, 1), Node(13, "Gear_Wood", 1, 4, 2), upper }).ToArray();
        MechanicalNetworkGraph graph = WorldMechanicalGraph(nodes);
        if (args.Value<bool?>("manual") == true)
        {
            foreach (MachineEntity belt in belts) belt.ConveyorMode = belt.ConveyorRoute.ManualMode;
            graph.Rebuild(nodes);
        }
        MechanicalNetwork network = graph.Networks[0];
        MachineEntity source = args.Value<bool?>("upper") == true ? upper : lower;
        void Solve(int sign) => MechanicalNetworkGraph.Solve(network,
            node => sign != 0 && ReferenceEquals(node, source) ? 1f : 0f,
            node => node.Definition.Rpm * sign, 20f);
        JObject Sample()
        {
            var samples = new JArray();
            foreach (MachineEntity belt in belts)
            {
                belt.GetLocalTorque(out _, out float demand);
                ConveyorPath route = belt.ConveyorRoute;
                Vector2 start = route.Curved ? ConveyorPath.Rotate(
                    new Vector2(-.5f + Mathf.Sqrt(.125f), .5f - Mathf.Sqrt(.125f)), route.Rotation) : Vector2.zero;
                float distance = .1f * MachineWorld.GetWorkEfficiency(belt) * Mathf.Sign(belt.Rpm);
                bool moved = route.TryMove(start, distance, .5f, out Vector2 end);
                samples.Add(new JObject { ["rpm"] = belt.Rpm, ["demand"] = demand,
                    ["satisfied"] = belt.LoadSatisfied, ["flow"] = belt.FlowVisited,
                    ["moved"] = moved ? (end - start).magnitude : 0f });
            }
            return new JObject { ["supply"] = network.TorqueSupply, ["demand"] = network.TorqueDemand,
                ["lowerRpm"] = lower.Rpm, ["upperRpm"] = upper.Rpm, ["belts"] = samples };
        }
        var result = new JObject { ["networks"] = graph.Networks.Count, ["curved"] = belts[2].ConveyorRoute.Curved };
        Solve(1); result["forward"] = Sample();
        Solve(-1); result["reverse"] = Sample();
        Solve(0); result["stopped"] = Sample();

        // 旧十点档只在独立样本中复现，证明连接未断却会因负载放大而停带。
        source = lower;
        foreach (MachineEntity belt in belts) belt.Definition.TorqueLoadStep = 10f;
        Solve(1); result["coarseLoad"] = Sample();

        MachineEntity[] chain = Enumerable.Range(0, 11)
            .Select(i => Node(100 + i, "Conveyor_Wood", 0, i, 1)).ToArray();
        MachineEntity crank = Node(99, "HandCrank", -1, 0);
        graph.Rebuild(chain.Concat(new[] { crank }));
        MechanicalNetwork budget = graph.Networks.Single();
        MechanicalNetworkGraph.Solve(budget, node => ReferenceEquals(node, crank) ? 1f : 0f, 20f);
        result["budgetDemand"] = budget.TorqueDemand;
        result["satisfiedCount"] = chain.Count(node => node.LoadSatisfied);
        result["lastRpm"] = chain[10].Rpm;
        var ordinary = new MachineEntity { Definition = new MachineDefinition { Id = "probe:ordinary", TorqueLoad = 2f } };
        result["ordinaryDemand"] = ordinary.RequiredTorque;
        result["fractionalDemand"] = MechanicalNetworkGraph.RoundLoadTorque(.3f, .25f);
        result["invalidSteps"] = new JArray(new[] { 0f, -1f, float.NaN, float.PositiveInfinity }
            .Select(value => WorldException(() => new MachineDefinition { Id = "probe:invalid", TorqueLoadStep = value }.Validate())));
        return result;
    }
    #endregion
}
