using Unity.Profiling;
using UnityEngine;

#region 纯生产算法回归入口
int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}
MachineEntity Node(int id, int x, float torque = 0f, float load = 0f, string mode = "relay")
    => new() { Id = id, Cell = new(x, 0), State = new(), Snapshot = new(),
        Definition = new() { Torque = torque, TorqueLoad = load, PortMode = mode } };
MechanicalNetworkGraph Graph(params MachineEntity[] nodes)
{
    var graph = new MechanicalNetworkGraph(new(16, 16), Vector2Int.zero, default(WorldTopologyDomain));
    graph.Rebuild(nodes);
    return graph;
}
void Solve(MechanicalNetwork network) => MechanicalNetworkGraph.Solve(network,
    node => node.TestSourceFactor, node => node.Definition.Rpm * node.Definition.SourceRotationDirection, 60);

// 近处负载先取得完整扭矩；无功率变化时路径不重建。
var source = Node(1, 0, torque: 100, mode: "output");
var near = Node(2, 1, load: 60);
var far = Node(3, 2, load: 60, mode: "input");
var network = Graph(source, near, far).Networks.Single();
Solve(network);
Check(near.LoadSatisfied && !far.LoadSatisfied, "较近负载应优先取力且不足不能部分启动");
int builds = ProfilerMarker.Counts.GetValueOrDefault("MachineWorld.Mechanical.Routes.Rebuild");
for (int i = 0; i < 20; i++) Solve(network);
Check(ProfilerMarker.Counts["MachineWorld.Mechanical.Routes.Rebuild"] == builds, "稳定拓扑和有效源应复用路径");
source.TestSourceFactor = .8f;
Solve(network);
Check(ProfilerMarker.Counts["MachineWorld.Mechanical.Routes.Rebuild"] == builds, "仅功率变化不得重建路径");

// 手推输入终点变成有效源后才允许穿过，停止后路径必须撤回。
var external = Node(10, 0, torque: 100, mode: "output");
var manual = Node(11, 1, load: 10, mode: "input");
manual.Definition.ManualDriveTorque = 100;
manual.TestSourceFactor = 0;
var downstream = Node(12, 2, load: 10);
network = Graph(external, manual, downstream).Networks.Single();
Solve(network);
Check(!downstream.FlowVisited, "停用手推输入终点不能传递上游动力");
manual.TestSourceFactor = 1;
Solve(network);
Check(downstream.FlowVisited && downstream.LoadSatisfied, "手推激活后应重新开放可达路径");
manual.TestSourceFactor = 0;
Solve(network);
Check(!downstream.FlowVisited && !downstream.LoadSatisfied, "手推关闭后缓存不得继续借上游动力");

// 齿轮啮合必须反转，多个异速源必须停网。
var gearSource = Node(20, 0, torque: 100, mode: "output");
var gearLoad = Node(21, 1, load: 20, mode: "input");
gearSource.Definition.Kind = gearLoad.Definition.Kind = "gear";
network = Graph(gearSource, gearLoad).Networks.Single(); Solve(network);
Check(gearLoad.Rpm == -60, "两枚齿轮啮合应反转");
var another = Node(22, 2, torque: 100, mode: "output"); another.Definition.Rpm = 90;
network = Graph(source, Node(23, 1), another).Networks.Single(); Solve(network);
Check(network.OutputConflict, "异速源应卡死网络");

// 同样的可达节点和有效源集合里直接撤一条边，缓存仍须重建实际可达来源。
var topologySourceA = Node(24, 0, torque: 100, mode: "output");
var topologyConsumer = Node(25, 1, load: 150);
var topologySourceB = Node(26, 2, torque: 100, mode: "output");
network = Graph(topologySourceA, topologyConsumer, topologySourceB).Networks.Single(); Solve(network);
Check(topologyConsumer.LoadSatisfied, "两路100扭矩应共同满足150负载");
topologySourceA.Links.Clear();
topologyConsumer.Links.RemoveAll(link => ReferenceEquals(link.Target, topologySourceA));
Solve(network);
Check(!topologyConsumer.LoadSatisfied, "撤边后不得复用旧路径借用断开源的扭矩");

// 电池反复探算不能修改储能，正式提交恰好扣除本步需要的能量。
var battery = Node(30, 0); battery.Definition.HasMechanicalPorts = false;
battery.Definition.Electrical = new() { Role = "battery" }; battery.ElectricalStoredJoules = 100;
var consumer = Node(31, 0); consumer.Definition.HasMechanicalPorts = false;
consumer.Definition.Electrical = new() { Role = "consumer", PowerWatts = 50 };
var electrical = new ElectricalNetwork(); electrical.Nodes.AddRange(new[] { battery, consumer });
int changes = 0;
for (int i = 0; i < 10; i++) ElectricalNetworkGraph.Solve(electrical, .1f, _ => 1, _ => 1, _ => changes++, false);
Check(battery.ElectricalStoredJoules == 100 && changes == 0, "探算不能扣电池或通知储能变化");
ElectricalNetworkGraph.Solve(electrical, .1f, _ => 1, _ => 1, _ => changes++, true);
Check(battery.ElectricalStoredJoules == 95 && changes == 1, "正式结算应只扣5J并通知一次");
var generator = Node(32, 0); generator.Definition.Electrical = new() { Role = "generator", PowerWatts = 50 };
electrical.Nodes.Add(generator); int samples = 0;
ElectricalNetworkGraph.Solve(electrical, .1f, _ => { samples++; return 1; }, _ => 1, null, false);
Check(samples == 1, "同次解算电压与功率应只采样一次发电provider");

// 无电力依赖网络一轮只求解一次，马达在电力结算后仍能带动负载。
var independent = Node(40, 0, torque: 100, mode: "output");
var independentLoad = Node(41, 1, load: 10, mode: "input");
var motor = Node(42, 10, torque: 100, mode: "output"); motor.Definition.Source = "electric";
motor.Definition.Electrical = new() { Role = "consumer", PowerWatts = 100, DemandProvider = "motor" };
var motorLoad = Node(43, 11, load: 10, mode: "input");
var power = Node(44, 10); power.Definition.HasMechanicalPorts = false;
power.Definition.Layer = 4;
power.Definition.Electrical = new() { Role = "generator", PowerWatts = 100 };
var wire = Node(45, 10); wire.Definition.HasMechanicalPorts = false; wire.Definition.Layer = 3;
wire.Definition.Electrical = new() { Role = "wire" };
var all = new[] { independent, independentLoad, motor, motorLoad, power, wire };
var mechanicalGraph = Graph(all); var electricalGraph = new ElectricalNetworkGraph(default);
electricalGraph.Rebuild(all); MachineWorld.SetWorld(mechanicalGraph, electricalGraph, all);
MachineWorld.SettleCoupled(.1f);
Check(independent.TestSourceCalls == 1, "独立机械源本步不应重复求解");
Check(motorLoad.LoadSatisfied && motorLoad.Rpm == 60, "电力结算后马达应重新取得真实出力");

// 多级转换、共享电池及回流环路与优化前的规则逐节点比较。
(MechanicalNetworkGraph Mechanical, ElectricalNetworkGraph Electrical, MachineEntity[] Nodes) CoupledChain(float factor, bool loop)
{
    MachineEntity Converter(int id, int x, float torque, float load)
    {
        var converter = Node(id, x, torque, load);
        converter.Definition.Electrical = new() { Role = "converter", PowerWatts = torque * 60 };
        return converter;
    }
    var s = Node(200, 0, 1000, mode: "output"); s.TestSourceFactor = factor;
    var g1 = Converter(201, 1, 400, 400);
    var m2 = Converter(202, 10, 400, 100);
    var l2 = Node(203, 11, load: 40);
    var g2 = Converter(204, 12, 100, 100);
    var m3 = Converter(205, 20, 100, 100);
    var l3 = Node(206, 21, load: 50, mode: "input");
    var b = Node(207, 30); b.Definition.HasMechanicalPorts = false;
    b.Definition.Electrical = new() { Role = "battery", MaxDischargeWatts = 1000 }; b.ElectricalStoredJoules = 1000;
    var entities = new[] { s, g1, m2, l2, g2, m3, l3, b };
    var mg = Graph(entities);
    var eg = new ElectricalNetworkGraph(default);
    void AddElectrical(params MachineEntity[] members)
    {
        var n = new ElectricalNetwork(); n.Nodes.AddRange(members); eg.Networks.Add(n);
        foreach (MachineEntity member in members) member.ElectricalNetwork = n;
    }
    if (loop) AddElectrical(g1, m2, g2, m3, b);
    else { AddElectrical(g1, m2, b); AddElectrical(g2, m3); }
    return (mg, eg, entities);
}
foreach (bool loop in new[] { false, true })
foreach (float factor in new[] { 0f, .05f, .2f, .5f, 1f })
{
    var current = CoupledChain(factor, loop); var reference = CoupledChain(factor, loop);
    for (int tick = 0; tick < 8; tick++)
    {
        MachineWorld.SetWorld(current.Mechanical, current.Electrical, current.Nodes); MachineWorld.SettleCoupled(.1f);
        MachineWorld.SetWorld(reference.Mechanical, reference.Electrical, reference.Nodes); MachineWorld.SettleCoupledReference(.1f);
        for (int index = 0; index < current.Nodes.Length; index++)
        {
            MachineEntity a = current.Nodes[index], b = reference.Nodes[index];
            Check(a.Rpm == b.Rpm && a.LoadSatisfied == b.LoadSatisfied && a.ConversionMode == b.ConversionMode &&
                a.ElectricalSuppliedWatts == b.ElectricalSuppliedWatts && a.ElectricalGeneratedWatts == b.ElectricalGeneratedWatts &&
                a.ElectricalStoredJoules == b.ElectricalStoredJoules,
                $"耦合规则差分失败 loop={loop}, factor={factor}, tick={tick}, node={a.Id}");
        }
    }
}

// 预热后连续求解不再按负载数量分配临时集合。
var chain = Enumerable.Range(0, 129).Select(index => Node(100 + index, index,
    torque: index == 0 ? 2000 : 0, load: index == 0 ? 0 : 10, mode: index == 0 ? "output" : "relay")).ToArray();
network = Graph(chain).Networks.Single();
for (int i = 0; i < 10; i++) Solve(network);
long before = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 100; i++) Solve(network);
long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
Check(allocated < 1024, "稳定129节点/128负载连续100轮应无按负载数量增长的分配，实际 " + allocated);
Console.WriteLine($"PASS {passed} assertions; stable 129-node/128-consumer x100 allocated {allocated} B; routes reused.");
#endregion
