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

#region 风箱拓扑索引
// 新建、清空和增删风箱只按拓扑重建更新存在标志。
var bellowsGraph = Graph();
Check(!bellowsGraph.HasBellows, "空图不应报告有风箱");
var ordinaryNode = Node(901, 0);
ordinaryNode.Definition.Kind = "gear";
bellowsGraph.Rebuild(new[] { ordinaryNode });
Check(!bellowsGraph.HasBellows, "只有普通机械时应走无风箱快路径");
var bellowsNode = Node(902, 1);
bellowsNode.Definition.Kind = "bellows";
bellowsGraph.Rebuild(new[] { ordinaryNode, bellowsNode });
Check(bellowsGraph.HasBellows, "新增风箱后快路径必须立即失效");
bellowsGraph.Rebuild(new[] { ordinaryNode });
Check(!bellowsGraph.HasBellows, "拆除最后一台风箱后应恢复快路径");
#endregion

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

#region 无限发电规则
// 创造电源按正式电网的实际需求供电，边界样本不进入当前游戏世界。
MachineEntity ElectricalNode(int id, string role, float watts = 0f, bool unlimited = false)
{
    var node = Node(id, 0);
    node.Definition.HasMechanicalPorts = false;
    node.Definition.Electrical = new() { Role = role, PowerWatts = watts, UnlimitedGeneration = unlimited };
    return node;
}
ElectricalNetwork ElectricalGraph(params MachineEntity[] nodes)
{
    var result = new ElectricalNetwork(); result.Nodes.AddRange(nodes); return result;
}
void SolveElectrical(ElectricalNetwork result, bool commit = true, float factor = 1f, float demand = 1f)
    => ElectricalNetworkGraph.Solve(result, .5f, _ => factor, _ => demand, null, commit);
bool FiniteElectrical(ElectricalNetwork result)
    => float.IsFinite(result.GeneratedWatts) && float.IsFinite(result.DeliveredWatts) &&
        float.IsFinite(result.ChargingWatts) && float.IsFinite(result.WastedWatts) && float.IsFinite(result.CurrentAmps) &&
        result.Nodes.All(node => float.IsFinite(node.ElectricalGeneratedWatts) && float.IsFinite(node.ElectricalStoredJoules));

var creative = ElectricalNode(300, "generator", unlimited: true);
var heavyConsumer = ElectricalNode(301, "consumer", 12000f);
var ordinaryBattery = ElectricalNode(302, "battery"); ordinaryBattery.ElectricalStoredJoules = 1000f;
var creativeNetwork = ElectricalGraph(creative, heavyConsumer, ordinaryBattery);
for (int tick = 0; tick < 64; tick++)
{
    SolveElectrical(creativeNetwork);
    Check(heavyConsumer.ElectricalSuppliedWatts == 12000f && creative.ElectricalGeneratedWatts == 12000f &&
        creativeNetwork.DischargingWatts == 0f && ordinaryBattery.ElectricalStoredJoules == 1000f &&
        creative.ElectricalStoredJoules == 0f && FiniteElectrical(creativeNetwork),
        "无限源连续32秒必须完整供12000瓦、不扣储能且读数有限");
}
SolveElectrical(creativeNetwork, demand: 0f);
Check(creative.ElectricalGeneratedWatts == 0f && creativeNetwork.GeneratedWatts == 0f &&
    creativeNetwork.CurrentAmps == 0f && creativeNetwork.WastedWatts == 0f && FiniteElectrical(creativeNetwork),
    "闲置无限源必须归零，不用无穷大表示功率");

ordinaryBattery.ElectricalStoredJoules = 250f;
int creativeNotifications = 0;
for (int preview = 0; preview < 5; preview++)
    ElectricalNetworkGraph.Solve(creativeNetwork, .5f, _ => 1f, _ => 1f, _ => creativeNotifications++, false);
Check(ordinaryBattery.ElectricalStoredJoules == 250f && creativeNotifications == 0 &&
    creative.ElectricalGeneratedWatts == 12100f && creativeNetwork.ChargingWatts == 100f,
    "无限源充电探算要有实际充电读数，但不写储能、不发通知");
ElectricalNetworkGraph.Solve(creativeNetwork, .5f, _ => 1f, _ => 1f, _ => creativeNotifications++, true);
Check(ordinaryBattery.ElectricalStoredJoules == 300f && creativeNotifications == 1 &&
    heavyConsumer.ElectricalSuppliedWatts == 12000f && creativeNetwork.WastedWatts == 0f,
    "正式半秒应只给普通电池充50焦耳，同时满足负载");
for (int tick = 0; tick < 14; tick++) SolveElectrical(creativeNetwork);
SolveElectrical(creativeNetwork);
Check(ordinaryBattery.ElectricalStoredJoules == 1000f && creativeNetwork.ChargingWatts == 0f &&
    creative.ElectricalGeneratedWatts == 12000f, "电池满电后无限源必须撤掉充电出力");

var creativeSecond = ElectricalNode(303, "generator", unlimited: true);
ordinaryBattery.ElectricalStoredJoules = 950f;
creativeNetwork.Nodes.Add(creativeSecond);
SolveElectrical(creativeNetwork);
Check(creativeNetwork.GeneratedWatts == 12100f && creative.ElectricalGeneratedWatts == 6050f &&
    creativeSecond.ElectricalGeneratedWatts == 6050f && ordinaryBattery.ElectricalStoredJoules == 1000f &&
    creativeNetwork.WastedWatts == 0f, "多个无限源只能分摊一个真实供电和充电缺口");
SolveElectrical(creativeNetwork);
Check(creativeNetwork.GeneratedWatts == 12000f && creative.ElectricalGeneratedWatts == 6000f &&
    creativeSecond.ElectricalGeneratedWatts == 6000f, "多个无限源满电后必须同步收回多余出力");

var ordinaryGenerator = ElectricalNode(304, "generator", 5000f);
creativeNetwork = ElectricalGraph(ordinaryGenerator, creative, heavyConsumer);
SolveElectrical(creativeNetwork);
Check(ordinaryGenerator.ElectricalGeneratedWatts == 5000f && creative.ElectricalGeneratedWatts == 7000f &&
    creativeNetwork.GeneratedWatts == 12000f, "普通发电应优先，无限源仅补缺口");
ordinaryGenerator.Definition.Electrical.PowerWatts = 15000f;
SolveElectrical(creativeNetwork);
Check(ordinaryGenerator.ElectricalGeneratedWatts == 15000f && creative.ElectricalGeneratedWatts == 0f &&
    creativeNetwork.WastedWatts == 3000f, "普通发电已有余量时无限源必须待机，保留普通发电规则");
ordinaryGenerator.Definition.Electrical.PowerWatts = 5000f;
ordinaryBattery.ElectricalStoredJoules = 250f;
creativeNetwork.Nodes.Add(ordinaryBattery);
SolveElectrical(creativeNetwork);
Check(creative.ElectricalGeneratedWatts == 7100f && creativeNetwork.GeneratedWatts == 12100f &&
    creativeNetwork.ChargingWatts == 100f && ordinaryBattery.ElectricalStoredJoules == 300f,
    "普通发电与无限源合供时应同时补负载和普通电池充电缺口");

creativeNetwork = ElectricalGraph(creative, heavyConsumer);
SolveElectrical(creativeNetwork, factor: 0f);
Check(creativeNetwork.GeneratedWatts == 0f && heavyConsumer.ElectricalSuppliedWatts == 0f &&
    creative.ElectricalGeneratedWatts == 0f, "无限发电provider停用仍必须立即断供并清除旧读数");
SolveElectrical(creativeNetwork, factor: .2f);
Check(heavyConsumer.ElectricalSuppliedWatts == 12000f, "有效无限源不能被provider比例变成有限额定功率");
creativeSecond.Definition.Electrical.NominalVoltage = 24f;
creativeNetwork.Nodes.Add(creativeSecond);
SolveElectrical(creativeNetwork);
Check(creativeNetwork.VoltageConflict && creativeNetwork.DeliveredWatts == 0f &&
    creative.ElectricalGeneratedWatts == 0f && heavyConsumer.ElectricalSuppliedWatts == 0f,
    "无限源仍必须遵守多个电压源冲突停网");
creativeNetwork.Nodes.Remove(creativeSecond);
heavyConsumer.Definition.Electrical.NominalVoltage = 24f;
SolveElectrical(creativeNetwork);
Check(creativeNetwork.VoltageIncompatible && heavyConsumer.ElectricalSuppliedWatts == 0f &&
    creative.ElectricalGeneratedWatts == 0f, "无限源不能向电压不兼容负载供电");
heavyConsumer.Definition.Electrical.NominalVoltage = 120f;
var limitedWire = ElectricalNode(305, "wire"); limitedWire.Definition.Electrical.MaximumVoltage = 24f;
creativeNetwork.Nodes.Add(limitedWire);
SolveElectrical(creativeNetwork);
Check(creativeNetwork.VoltageIncompatible && creativeNetwork.DeliveredWatts == 0f &&
    creative.ElectricalGeneratedWatts == 0f, "无限源不能越过电线耐压限制");
limitedWire.Definition.Electrical.MaximumVoltage = 0f;
limitedWire.Definition.Electrical.MaxCurrentAmps = 1f;
SolveElectrical(creativeNetwork);
Check(creativeNetwork.Overloaded && creativeNetwork.CurrentAmps == 100f &&
    heavyConsumer.ElectricalSuppliedWatts == 12000f, "无限源仍应根据实际电流标记电线过载");

// 动态 provider 返回非有限数值时，不能让无实际供电的电网保留虚假电压。
creativeNetwork = ElectricalGraph(creative, heavyConsumer);
foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
{
    SolveElectrical(creativeNetwork, factor: invalid);
    Check(creativeNetwork.Voltage == 0f && creativeNetwork.GeneratedWatts == 0f &&
        creativeNetwork.DeliveredWatts == 0f && heavyConsumer.ElectricalSuppliedWatts == 0f &&
        FiniteElectrical(creativeNetwork), "非法发电系数未安全关停无限电源");
    SolveElectrical(creativeNetwork, demand: invalid);
    Check(creativeNetwork.DemandWatts == 0f && creativeNetwork.GeneratedWatts == 0f &&
        heavyConsumer.ElectricalRequestedWatts == 0f && FiniteElectrical(creativeNetwork),
        "非法负载系数污染电网功率读数");
}

var disconnectedWireA = ElectricalNode(306, "wire");
var disconnectedWireB = ElectricalNode(307, "wire"); disconnectedWireB.Cell = new(2, 0);
heavyConsumer.Cell = new(2, 0);
var disconnectedGraph = new ElectricalNetworkGraph(default);
disconnectedGraph.Rebuild(new[] { creative, heavyConsumer, disconnectedWireA, disconnectedWireB });
foreach (ElectricalNetwork component in disconnectedGraph.Networks) SolveElectrical(component);
Check(disconnectedGraph.Networks.Count == 2 && !ReferenceEquals(creative.ElectricalNetwork, heavyConsumer.ElectricalNetwork) &&
    creative.ElectricalGeneratedWatts == 0f && heavyConsumer.ElectricalSuppliedWatts == 0f,
    "无限源断线后不得跨独立电网供电");
#endregion

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
