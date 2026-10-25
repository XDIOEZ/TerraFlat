using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static MachineWorld;

/// <summary>在独立机器世界运行正式拓扑、冻结输送、共享储罐和保存迁移事务。</summary>
internal static class GameplayTestFluidNetworks
{
    #region 案例入口与真实载荷
    internal static readonly Dictionary<string, Action<Action<string, bool, string>>> Cases = new(StringComparer.Ordinal)
    {
        ["network_catalog"] = Catalog,
        ["network_masks"] = Masks,
        ["network_rotation"] = Rotation,
        ["network_frozen_chain"] = FrozenChain,
        ["network_pressure_direction"] = PressureDirection,
        ["network_shared_throughput"] = Throughput,
        ["network_blocked_retry"] = BlockedRetry,
        ["network_valve"] = Valve,
        ["network_selector"] = Selector,
        ["network_phase_preference"] = PhasePreference,
        ["network_port_rejection"] = PortRejection,
        ["network_receiver_routes"] = ReceiverRoutes,
        ["network_probe_hysteresis"] = Probe,
        ["tank_merge_split"] = MergeSplit,
        ["tank_snapshot_restore"] = SnapshotRestore,
        ["tank_scope_transfer"] = ScopeTransfer,
        ["tank_dismantle"] = Dismantle,
        ["tank_overpressure"] = Overpressure,
        ["tank_rupture_once"] = Rupture,
        ["environment_atmosphere"] = Atmosphere,
        ["environment_outlet_vent"] = OutletVent,
        ["container_liquid_transfer"] = LiquidTransfer,
        ["network_tick_scheduler"] = TickScheduler,
        ["gas_network_lifecycle"] = GameplayGasNetworkLifecycle,
        ["gas_network_restore_order"] = GameplayGasNetworkRestore
    };

    private static void Check(Action<string, bool, string> report, string label, bool passed, string message = null)
        => report(label, passed, message ?? (passed ? "通过" : "实际结果不符合预期"));
    private static void Require(bool passed, string label)
    { if (!passed) throw new InvalidOperationException("流体测试前提失败：" + label); }
    private static bool Near(double a, double b, double epsilon = 1e-8)
        => double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) <= epsilon * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    private static bool Near(decimal a, decimal b) => Math.Abs(a - b) <= .00000000000000000001m;
    private static string Payload(FluidInventory inventory) => JsonConvert.SerializeObject(inventory.State);
    private static FluidInventory Fill(MachineEntity node, string id, decimal gas, decimal liquid = 0m, string chamber = "main")
    {
        FluidDefinition substance = FluidCatalog.Default.Find(id);
        FluidInventory inventory = GetFluidInventory(node, chamber);
        if (gas > 0m)
        {
            FluidInventory gasInventory = GetPhaseFluidInventory(node, chamber, FluidPhase.Gas);
            Require(gasInventory.TryAdd(FluidInventory.CreateBatch(substance, gas, 0m, 293.15),
                GetPhaseFluidVolumeLiters(node, FluidPhase.Gas), GetPhaseFluidMinimumGasSpaceLiters(node, FluidPhase.Gas)),
                node.Definition.Id + " 气相装料");
            if (liquid == 0m) return gasInventory;
        }
        if (liquid > 0m)
            Require(inventory.TryAdd(FluidInventory.CreateBatch(substance, 0m, liquid, 293.15),
                GetFluidVolumeLiters(node), GetFluidMinimumGasSpaceLiters(node)), node.Definition.Id + " 液相装料");
        return inventory;
    }
    private static void FillLiters(MachineEntity node, string id, decimal liters, string chamber = "main")
        => Fill(node, id, 0, FluidUnits.LiquidLitersToMol(FluidCatalog.Default.Find(id), liters), chamber);
    private static (decimal Gas, decimal Liquid, double Energy) Total(params MachineEntity[] nodes)
    {
        decimal gas = 0, liquid = 0; double energy = 0;
        var seen = new HashSet<FluidInventory>();
        foreach (MachineEntity node in nodes)
        {
            using var lease = UseNodeScope(node);
            FluidInventory inventory = GetFluidInventory(node);
            if (seen.Add(inventory))
            { gas += inventory.GasMoles; liquid += inventory.LiquidMoles; energy += inventory.State.TotalInternalEnergyJoules; }
            FluidInventory sharedGas = GetPhaseFluidInventory(node, "main", FluidPhase.Gas);
            if (seen.Add(sharedGas))
            { gas += sharedGas.GasMoles; liquid += sharedGas.LiquidMoles; energy += sharedGas.State.TotalInternalEnergyJoules; }
        }
        return (gas, liquid, energy);
    }
    private static bool HasEdge(FluidNetworkGraph graph, MachineEntity a, MachineEntity b)
        => graph.Edges.Exists(edge => edge.Source == a && edge.Target == b);
    #endregion

    #region 正式目录与端口拓扑
    private static void Catalog(Action<string, bool, string> report)
    {
        foreach (string id in new[] { "FluidPipe", "FluidValve", "FluidOutlet", "GasSelector", "GasTankBlock_Iron",
            "GasTankBlock_Steel", "LiquidStorageTank", "GasPump_Mechanical", "LiquidPump_Electric",
            "PressureProbe_Mechanical", "PressureProbe_Electronic", "GasCompressor_Mechanical",
            "GasCompressor_Electric", "FluidFilter", "WaterElectrolyzer", "GasEngine", "ShipChemicalEngine",
            "ShipChemicalEngine_GM", "ShipAirSupply", "SpaceCultivator" })
        {
            MachineDefinition definition = MachineCatalog.Get(id);
            Require(definition?.Fluid != null, id + " 定义存在");
            definition.Validate();
            bool valid = MachineLogicRegistry.IsFluidDeviceRegistered(definition.Fluid.Kind) &&
                         GameRes.ExistingInstance.TryGetItemDefinition(id, out _);
            foreach (FluidMachinePortDefinition port in definition.Fluid.Ports)
                valid &= string.IsNullOrEmpty(port.FluidId) || FluidCatalog.Default.TryGet(port.FluidId, out _);
            foreach (FluidMachineReactionDefinition reaction in definition.Fluid.Reactions)
            foreach (FluidReactionTerm term in reaction.Inputs.Concat(reaction.Outputs))
                valid &= FluidCatalog.Default.TryGet(term.FluidId, out _);
            Check(report, id + " 目录、策略及物质引用", valid);
        }
        Check(report, "基础管道和铁钢气罐吞吐为 1000 L/s",
            new[] { "FluidPipe", "GasTankBlock_Iron", "GasTankBlock_Steel" }.All(id => MachineCatalog.Get(id).Fluid.ThroughputLitersPerSecond == 1000));
    }

    private static void Masks(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        MachineEntity center = f.Add("FluidPipe");
        var neighbors = new List<MachineEntity>();
        var offsets = new[] { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };
        for (int mask = 0; mask < 16; mask++)
        {
            foreach (MachineEntity node in neighbors) DiscardPlacementCandidate(node.Id);
            neighbors.Clear();
            for (int bit = 0; bit < 4; bit++)
                if ((mask & (1 << bit)) != 0) neighbors.Add(f.Add("FluidPipe", offsets[bit].x, offsets[bit].y));
            int actual = GetFluidPipeConnectionMask(center);
            Check(report, "管道连接掩码 " + mask, actual == mask, $"实际 {actual}，预期 {mask}；北1东2南4西8。");
        }
    }

    private static void Rotation(Action<string, bool, string> report)
    {
        for (int rotation = 0; rotation < 4; rotation++)
        {
            using var f = new GameplayFluidScope();
            MachineEntity outlet = f.Add("FluidOutlet", rotation: rotation);
            Vector2Int direction = MechanicalNetworkGraph.Directions[rotation];
            MachineEntity upstream = f.Add("FluidPipe", -direction.x, -direction.y);
            MachineEntity downstream = f.Add("LiquidStorageTank", direction.x, direction.y);
            FluidNetworkGraph graph = f.Graph;
            Check(report, "朝向 " + rotation + " 的入口和出口", HasEdge(graph, upstream, outlet) && HasEdge(graph, outlet, downstream));
            Check(report, "输入口不能反向输出 " + rotation, !HasEdge(graph, outlet, upstream) && !HasEdge(graph, downstream, outlet));
            foreach (FluidMachinePortDefinition port in outlet.Definition.Fluid.Ports) port.Phase = "gas";
            foreach (FluidMachinePortDefinition port in downstream.Definition.Fluid.Ports) port.Phase = "liquid";
            TopologyChanged(outlet); TopologyChanged(downstream);
            Check(report, "气液端口相态不匹配拒绝建边 " + rotation, !HasEdge(f.Graph, outlet, downstream));
            Check(report, "有物理目标时不绕过不兼容端口排环境 " + rotation,
                !f.Graph.Edges.Exists(edge => edge.Source == outlet && edge.Environment));
        }
    }
    #endregion

    #region 固定步长输送与共享预算
    private static void FrozenChain(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        MachineEntity a = f.Add("FluidPipe"), b = f.Add("FluidPipe", 1), outlet = f.Add("FluidOutlet", 2), tank = f.Add("LiquidStorageTank", 3);
        f.Rebuild(); Fill(a, FluidIds.Oxygen, .1m);
        var initial = Total(a, b, outlet, tank);
        Check(report, "气体整网共享单份库存、液体仍独立管段",
            ReferenceEquals(GetPhaseFluidInventory(a, "main", FluidPhase.Gas), GetPhaseFluidInventory(b, "main", FluidPhase.Gas)) &&
            !ReferenceEquals(GetFluidInventory(a), GetFluidInventory(b)) && GetFluidInventory(a).GasMoles == 0m);
        f.Step();
        Check(report, "首轮直接从整网供给出口、不逐格搬运", GetFluidInventory(outlet).GasMoles > 0 && GetFluidInventory(tank).IsEmpty);
        f.Step();
        Check(report, "下一轮出口真实送入储罐", GetFluidInventory(tank).GasMoles > 0);
        var end = Total(a, b, outlet, tank);
        Check(report, "整链物质量和内能守恒", Near(end.Gas, initial.Gas) && end.Liquid == initial.Liquid && Near(end.Energy, initial.Energy));
    }

    private static void PressureDirection(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        MachineEntity a = f.Add("FluidPipe"), b = f.Add("FluidPipe", 1);
        f.Rebuild(); Fill(a, FluidIds.Oxygen, .2m);
        for (int i = 0; i < 8; i++) f.Step();
        Check(report, "封闭裸管共享气网且不排放", GetPhaseFluidInventory(a, "main", FluidPhase.Gas).GasMoles == .2m &&
            ReferenceEquals(GetPhaseFluidInventory(a, "main", FluidPhase.Gas), GetPhaseFluidInventory(b, "main", FluidPhase.Gas)));
        Check(report, "裸管不擅自排向环境", !f.Graph.Edges.Exists(edge => edge.Environment));
        MachineEntity outlet = f.Add("FluidOutlet", 2), tank = f.Add("LiquidStorageTank", 3);
        f.Rebuild(); Fill(tank, FluidIds.Oxygen, 60m);
        for (int i = 0; i < 3; i++) f.Step();
        Check(report, "被动有向端口不能无功向更高压力储罐泵气", GetFluidInventory(tank).GasMoles == 60m &&
            Near(Total(a, b, outlet, tank).Gas, 60.2m));
    }

    private static void Throughput(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        MachineEntity a = f.Add("FluidPipe"), east = f.Add("FluidOutlet", 1), north = f.Add("FluidOutlet", 0, 1, 1);
        f.Add("LiquidStorageTank", 2); f.Add("LiquidStorageTank", 0, 2);
        a.Definition.Fluid.ThroughputLitersPerSecond = 1;
        TopologyChanged(a); f.Rebuild(); FluidInventory source = Fill(a, FluidIds.Oxygen, 1m);
        f.Step();
        decimal moved = GetFluidInventory(east).GasMoles + GetFluidInventory(north).GasMoles;
        double totalBoundaryBudget = 2d * f.Seconds;
        Check(report, "每个真实边界口独立限流、共用唯一库存", moved > 0m && (double)FluidUnits.MolToStandardLiters(moved) <= totalBoundaryBudget + 1e-8 &&
            (double)FluidUnits.MolToStandardLiters(GetFluidInventory(east).GasMoles) <= f.Seconds + 1e-8 &&
            (double)FluidUnits.MolToStandardLiters(GetFluidInventory(north).GasMoles) <= f.Seconds + 1e-8);
        Require(f.GasBudget(source, out FluidInventoryState stock, out double remaining), "独立气相源预算存在");
        Check(report, "预算和冻结库存按实际搬运扣一次", Near(remaining, totalBoundaryBudget - (double)FluidUnits.MolToStandardLiters(moved)) && Near(stock.Components[0].GasMoles, 1m - moved));
    }

    private static void BlockedRetry(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        MachineEntity a = f.Add("FluidOutlet"), tank = f.Add("LiquidStorageTank", 1);
        tank.Definition.Fluid.SingleGas = true; TopologyChanged(tank); f.Rebuild();
        FluidInventory source = Fill(a, FluidIds.Oxygen, .1m), target = Fill(tank, FluidIds.Nitrogen, .1m);
        double energy = source.State.TotalInternalEnergyJoules;
        f.Step();
        Require(source.State.Ports.Count > 0, "堵塞后保留预留");
        FluidOutputReservation reservation = source.State.Ports[0]; ulong random = source.State.RandomState;
        for (int i = 0; i < 3; i++) f.Step();
        Check(report, "堵塞重试不丢料、不重新抽签", source.GasMoles == .1m && target.GasMoles == .1m &&
            source.State.RandomState == random && ReferenceEquals(reservation, source.State.Ports[0]) && Near(source.State.TotalInternalEnergyJoules, energy));
        Check(report, "堵塞留下可诊断的端口原因", GetFluidState(a).BlockedPorts.Count > 0);
        target.Drain(); f.Step();
        Check(report, "清堵后继续搬原批次", target.GetGasMoles(FluidIds.Oxygen) > 0 && source.GasMoles < .1m);
        Check(report, "恢复输送后撤销堵塞提示", GetFluidState(a).BlockedPorts.Count == 0);
    }

    private static void Valve(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        MachineEntity source = f.Add("FluidPipe"), valve = f.Add("FluidValve", 1), outlet = f.Add("FluidOutlet", 2);
        f.Add("LiquidStorageTank", 3); f.Rebuild(); Fill(source, FluidIds.Oxygen, .1m);
        Require(GetFluidDeviceBehavior(valve).Execute(valve, "fluid.valve", null), "关闭阀门");
        Check(report, "关闭阀门切断正式边", !HasEdge(f.Graph, source, valve) && !HasEdge(f.Graph, valve, outlet));
        decimal expectedSourceShare = .1m * (decimal)(source.Definition.Fluid.VolumeLiters /
            (source.Definition.Fluid.VolumeLiters + valve.Definition.Fluid.VolumeLiters));
        for (int i = 0; i < 3; i++) f.Step();
        Check(report, "关阀期间各段保留真实份额，总量守恒", Near(GetPhaseFluidInventory(source, "main", FluidPhase.Gas).GasMoles, expectedSourceShare) &&
            Near(Total(source, valve).Gas, .1m) &&
            !ReferenceEquals(GetPhaseFluidInventory(source, "main", FluidPhase.Gas), GetPhaseFluidInventory(valve, "main", FluidPhase.Gas)));
        Require(GetFluidDeviceBehavior(valve).Execute(valve, "fluid.valve", null), "打开阀门"); f.Step();
        Check(report, "开阀合网并向出口供气", HasEdge(f.Graph, source, valve) &&
            ReferenceEquals(GetPhaseFluidInventory(source, "main", FluidPhase.Gas), GetPhaseFluidInventory(valve, "main", FluidPhase.Gas)) &&
            GetFluidInventory(outlet).GasMoles > 0);
    }

    private static void Selector(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        MachineEntity selector = f.Add("GasSelector"), north = f.Add("FluidOutlet", 0, 1, 1), south = f.Add("FluidOutlet", 0, -1, 3);
        MachineEntity selectedTank = f.Add("LiquidStorageTank", 0, 2), otherTank = f.Add("LiquidStorageTank", 0, -2);
        selector.Definition.Fluid.SingleGas = false; TopologyChanged(selector); f.Rebuild();
        Fill(selector, FluidIds.Oxygen, .1m); Fill(selector, FluidIds.Nitrogen, .1m);
        FluidDeviceBehavior behavior = GetFluidDeviceBehavior(selector);
        Check(report, "未选择目标不放行", !behavior.CanRoute(selector, selector.Definition.Fluid.Ports[1], FluidPhase.Gas, FluidIds.Oxygen, out _));
        Require(behavior.Execute(selector, "fluid.select", FluidIds.Oxygen), "选择氧气");
        Check(report, "无效物质拒绝选择", !behavior.Execute(selector, "fluid.select", "core:missing_test_fluid") && GetFluidState(selector).SelectedGasId == FluidIds.Oxygen);
        for (int i = 0; i < 12; i++) f.Step();
        Check(report, "目标出口只输出所选气体", GetFluidInventory(selectedTank).GetGasMoles(FluidIds.Nitrogen) == 0 && GetFluidInventory(selectedTank).GetGasMoles(FluidIds.Oxygen) > 0);
        Check(report, "其他出口不输出所选气体", GetFluidInventory(otherTank).GetGasMoles(FluidIds.Oxygen) == 0 && GetFluidInventory(otherTank).GetGasMoles(FluidIds.Nitrogen) > 0);
        Check(report, "分流后的总物质量守恒", Near(Total(selector, north, south, selectedTank, otherTank).Gas, .2m));
    }

    private static void PhasePreference(Action<string, bool, string> report)
    {
        foreach (int direction in new[] { 0, 1, 2, 3 })
        foreach (bool mixed in new[] { true, false })
        {
            using var f = new GameplayFluidScope();
            MachineEntity source = f.Add("FluidOutlet", rotation: direction);
            Vector2Int offset = MechanicalNetworkGraph.Directions[direction];
            MachineEntity target = f.Add("LiquidStorageTank", offset.x, offset.y);
            f.Rebuild(); bool vertical = (direction & 1) == 1;
            if (mixed || vertical) Fill(source, FluidIds.Oxygen, .05m);
            if (mixed || !vertical) Fill(source, FluidIds.PureWater, 0, 1m);
            for (int step = 0; step < 12; step++) f.Step();
            FluidInventory result = GetFluidInventory(target);
            Check(report, $"方向 {direction} {(mixed ? "气液分别结算" : "单相输送")}",
                (mixed || vertical ? result.GetGasMoles(FluidIds.Oxygen) > 0m : result.GetGasMoles(FluidIds.Oxygen) == 0m) &&
                (mixed || !vertical ? result.GetLiquidMoles(FluidIds.PureWater) + result.GetGasMoles(FluidIds.PureWater) > 0m :
                    result.GetLiquidMoles(FluidIds.PureWater) + result.GetGasMoles(FluidIds.PureWater) == 0m));
        }
    }

    private static void PortRejection(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        MachineEntity source = f.Add("FluidOutlet"), target = f.Add("LiquidStorageTank", 1);
        source.Definition.Fluid.Ports[1].FluidId = FluidIds.Nitrogen;
        TopologyChanged(source); f.Rebuild(); FluidInventory input = Fill(source, FluidIds.Oxygen, .1m);
        f.Step();
        Check(report, "端口物质筛选拒收且不扣量", input.GasMoles == .1m && GetFluidInventory(target).IsEmpty);
        source.Definition.Fluid.Ports[1].FluidId = ""; TopologyChanged(source);
        GetFluidState(source).OutletPhase = "liquid"; f.Step();
        Check(report, "设备出口相态开关阻止另一相", input.GasMoles == .1m && GetFluidInventory(target).IsEmpty);
        GetFluidState(source).OutletPhase = "both"; f.Step();
        Check(report, "解除限制后恢复气体输出", GetFluidInventory(target).GasMoles > 0);
    }

    private static void ReceiverRoutes(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        MachineEntity receiver = f.Add("FluidPipe"), middle = f.Add("FluidPipe", 0, 1), source = f.Add("GasTankBlock_Iron", 0, 2);
        receiver.Definition.Fluid.Kind = "receiver";
        receiver.Definition.Fluid.Ports = new[] { new FluidMachinePortDefinition { Id = "in", Direction = 1, Mode = "input", Phase = "gas" } };
        TopologyChanged(receiver); f.Rebuild(); Fill(source, FluidIds.Oxygen, 1m); Fill(source, FluidIds.Hydrogen, 2m);
        var route = new FluidNetworkGraph.SupplyWorkspace(); string[] required = { FluidIds.Hydrogen, FluidIds.Oxygen };
        CollectFluidSupplyRoutes(receiver, "main", FluidPhase.Gas, required, route);
        int index = route.Nodes.FindIndex(node => node.Node == source);
        Check(report, "正式有向供给图能找到混气来源", index >= 0);
        Require(index >= 0, "混气供给路径"); f.Begin();
        Check(report, "receiver 不接普通随机推送", GetFluidInventory(receiver).IsEmpty);
        double capacity = GetFluidSupplyStandardLiters(route, index, f.Seconds, true);
        var projected = new Dictionary<FluidInventory, double>();
        GetFluidSupplyStandardLiters(route, index, f.Seconds, true, projected);
        ReserveFluidSupplyProjection(route, index, capacity, projected);
        Check(report, "探算只预留预算不扣库存", GetFluidSupplyStandardLiters(route, index, f.Seconds, true, projected) == 0 && GetFluidInventory(source).GasMoles == 3m);
        Check(report, "供给库存来自起始快照", GetFluidSupplyStock(route, index, FluidIds.Oxygen, FluidPhase.Gas) == 1m);
        middle.Definition.Fluid.Ports[3].Phase = "liquid"; middle.Definition.Fluid.Ports[1].FluidId = FluidIds.Oxygen;
        TopologyChanged(middle);
        CollectFluidSupplyRoutes(receiver, "main", FluidPhase.Gas, required, route);
        Check(report, "路径筛选必须同时允许全部需求物质", !route.Nodes.Exists(node => node.Node == source));
    }

    private static void Probe(Action<string, bool, string> report)
    {
        foreach (string id in new[] { "PressureProbe_Mechanical", "PressureProbe_Electronic" })
        {
            using var f = new GameplayFluidScope();
            MachineEntity pipe = f.Add("FluidPipe"), probe = f.Add(id);
            f.Rebuild(); FluidInventory contents = Fill(pipe, FluidIds.Oxygen, .1m);
            FluidDeviceBehavior behavior = GetFluidDeviceBehavior(probe);
            Check(report, id + " 拒绝倒置阈值及非有限值", !behavior.Execute(probe, "fluid.probe", "20|30") && !behavior.Execute(probe, "fluid.probe", "NaN|10"));
            Require(behavior.Execute(probe, "fluid.probe", "200|100"), "设置测压阈值");
            void Pressure(double kpa)
            {
                contents.Drain(); decimal moles = (decimal)(kpa * GetFluidVolumeLiters(pipe) / (FluidUnits.GasConstant * 293.15));
                Fill(pipe, FluidIds.Oxygen, moles); behavior.BeginStep(probe, f.Seconds); f.Rebuild();
            }
            Pressure(210); Check(report, id + " 超上限断开", !GetFluidState(probe).ProbeConnected && !probe.FluidProbeConnected);
            Pressure(150); Check(report, id + " 回差带保持断开", !GetFluidState(probe).ProbeConnected);
            Pressure(90); Check(report, id + " 降到下限以下接通", GetFluidState(probe).ProbeConnected && probe.FluidProbeConnected);
            Check(report, id + " 读数来自同格权威管段", Near(GetPressureProbeReading(probe), 90));
            if (probe.Definition.HasMechanicalPorts)
            { Pressure(210); Check(report, "机械探针切断真实机械端口", !probe.HasPort(0) && !probe.HasPort(2)); }
        }
    }
    #endregion

    #region 共享储罐、存档与跨作用域
    private static void MergeSplit(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope(); string from = f.NewShip(), to = f.NewShip();
        using var fromScope = UseScope(GetShipScopeKey(from));
        MachineEntity a = f.Add("GasTankBlock_Iron"), b = f.Add("GasTankBlock_Iron", 1), c = f.Add("GasTankBlock_Steel", 2);
        f.Rebuild();
        Check(report, "同材四邻共享库存，异材保持独立", ReferenceEquals(GetFluidInventory(a), GetFluidInventory(b)) && !ReferenceEquals(GetFluidInventory(b), GetFluidInventory(c)));
        Check(report, "组合容量和气相保留空间累加", GetFluidVolumeLiters(a) == 200 && Near(GetFluidMinimumGasSpaceLiters(a), .2));
        Check(report, "储罐连接外形跟随权威组", GetFluidTankConnectionMask(a) == 2 && GetFluidTankConnectionMask(b) == 8 && GetFluidTankConnectionMask(c) == 0);
        Fill(a, FluidIds.Oxygen, 2m); Fill(a, FluidIds.PureWater, 0, 20m);
        FluidInventory old = GetFluidInventory(a); var initial = Total(a, b);
        int ownerId = f.Groups().First(group => group.Members.Contains(a)).OwnerId;
        ItemData saved = CaptureSnapshot(a.Id == ownerId ? b : a);
        Check(report, "非主成员不保存第二份内容", MachinePersistence.Read<FluidMachineState>(saved, "fluid").Chambers["main"].Components.Count == 0);
        TransferShipNodes(from, to, new[] { b.Id }); var end = Total(a, b);
        Check(report, "拆分库存和内能守恒", Near(end.Gas, initial.Gas) && Near(end.Liquid, initial.Liquid) && Near(end.Energy, initial.Energy));
        FluidInventory first; using (UseNodeScope(a)) first = GetFluidInventory(a);
        FluidInventory second; using (UseNodeScope(b)) second = GetFluidInventory(b);
        Check(report, "拆分后撤销旧库存防重复提取", old.IsEmpty && !ReferenceEquals(first, second));
        Check(report, "等容积分配真实两相份额", Near(first.GasMoles, 1m) && Near(second.LiquidMoles, 10m));
    }

    private static void SnapshotRestore(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        MachineEntity pipe = f.Add("FluidPipe"); f.Rebuild(); FluidInventory inventory = Fill(pipe, FluidIds.Oxygen, .1m);
        Require(inventory.TryReserve("saved-port", FluidPhase.Gas, .02m, out _), "保存预留");
        string initial = Payload(inventory); ItemData saved = CaptureSnapshot(pipe);
        Require(Remove(pipe.Id) != null, "移除管段"); f.ForgetDetached(pipe);
        MachineEntity restored = RestoreMachine(saved); f.Rebuild();
        Check(report, "快照恢复整网气量、内能、预留和随机态", Payload(GetPhaseFluidInventory(restored, "main", FluidPhase.Gas)) == initial);
        Check(report, "恢复后连接可重新建立", GetById(restored.Id) == restored && GetFluidPipeAt(restored.Cell) == restored);
    }

    private static void ScopeTransfer(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope(); string from = f.NewShip(), to = f.NewShip();
        MachineEntity a, b; double energy;
        using (UseScope(GetShipScopeKey(from)))
        {
            a = f.Add("GasTankBlock_Iron"); b = f.Add("GasTankBlock_Iron", 1); f.Rebuild();
            Fill(a, FluidIds.Oxygen, 4m); Fill(a, FluidIds.PureWater, 0, 40m);
            Require(GetFluidInventory(a).TryReserve("ship-port", FluidPhase.Gas, .1m, out _), "转移前预留");
            energy = GetFluidInventory(a).State.TotalInternalEnergyJoules;
        }
        TransferShipNodes(from, to, new[] { b.Id });
        var total = Total(a, b);
        Check(report, "跨船整批转移不丢气液和内能", Near(total.Gas, 4m) && Near(total.Liquid, 40m) && Near(total.Energy, energy));
        using (UseScope(GetShipScopeKey(from)))
            Check(report, "来源撤销移走身份", !Contains(b) && Contains(a));
        using (UseScope(GetShipScopeKey(to)))
            Check(report, "目标拥有新身份和真实份额", Contains(b) && b.ScopeKey == GetShipScopeKey(to) && Near(GetFluidInventory(b).GasMoles, 2m));
        TransferShipNodes(from, to, new[] { a.Id });
        using (UseScope(GetShipScopeKey(to)))
        {
            f.Rebuild(); Check(report, "重聚后共享唯一总库存", ReferenceEquals(GetFluidInventory(a), GetFluidInventory(b)) && Near(GetFluidInventory(a).GasMoles, 4m));
            Check(report, "迁移释放原出口预留", GetFluidInventory(a).State.Ports.Count == 0);
        }
    }

    private static void Dismantle(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        MachineEntity a = f.Add("GasTankBlock_Iron"), b = f.Add("GasTankBlock_Iron", 1), pipe = f.Add("FluidPipe", 5);
        f.Rebuild(); Fill(a, FluidIds.Oxygen, 2m); Fill(a, FluidIds.PureWater, 0, 20m); Fill(pipe, FluidIds.Oxygen, .1m);
        Check(report, "有料管段拒绝普通拆回", !CanDismantleFluid(pipe, out _));
        Check(report, "方块罐允许按真实份额拆除", CanDismantleFluid(a, out _));
        ItemData prepared = CaptureSnapshot(a); PrepareDismantledFluidSnapshot(a, prepared);
        Check(report, "准备返空罐不提前扣世界库存", MachinePersistence.Read<FluidMachineState>(prepared, "fluid").Chambers.Count == 0 && GetFluidInventory(b).GasMoles == 2m);
        decimal atmosphereBefore = f.Atmosphere.CurrentMoles;
        ItemData removed = Remove(a.Id); f.Rebuild();
        Check(report, "拆除只释放一格气体份额", Near(f.Atmosphere.CurrentMoles - atmosphereBefore, 1m) && Near(GetFluidInventory(b).GasMoles, 1m));
        var spills = SaveDataMgr.Instance.SaveData.Mechanical.PendingPressureLiquidSpills.Where(spill => spill.WorldKey == f.Key).ToList();
        Check(report, "一格液体份额写入持久溢流", Near(spills.Sum(spill => spill.LiquidMoles), 10m) && Near(GetFluidInventory(b).LiquidMoles, 10m));
        Check(report, "实际拆回快照为空罐", MachinePersistence.Read<FluidMachineState>(removed, "fluid").Chambers.Values.All(contents => contents.Components.Count == 0));
    }

    private static void Overpressure(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        MachineEntity a = f.Add("GasTankBlock_Iron"), b = f.Add("GasTankBlock_Iron", 1); f.Rebuild();
        a.State.Hp = b.State.Hp = 1000; Fill(a, FluidIds.Oxygen, 100m);
        f.Step(); FluidTankGroup group = f.Groups().Single(); int victim = group.OverpressureVictimId;
        Check(report, "超压固定一个受损成员", victim != 0 && (a.State.Hp < 1000) != (b.State.Hp < 1000));
        f.Step();
        Check(report, "持续超压不反复抽选受损成员", group.OverpressureVictimId == victim && (victim == a.Id ? b.State.Hp : a.State.Hp) == 1000);
        group.Inventory.Drain(); f.Step();
        Check(report, "恢复安全压力解除受损目标", group.OverpressureVictimId == 0);
    }

    private static void Rupture(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        MachineEntity a = f.Add("GasTankBlock_Iron"), b = f.Add("GasTankBlock_Iron", 1); f.Rebuild();
        Fill(a, FluidIds.Oxygen, 2m); Fill(a, FluidIds.PureWater, 0, 20m); a.State.Hp = 0;
        Require(HandleFluidTankZeroHp(a), "首罐破裂");
        var archive = SaveDataMgr.Instance.SaveData.Mechanical;
        string budgetId = GetFluidState(b).RuptureBudgetId;
        Require(archive.FluidRuptures.TryGetValue(budgetId, out FluidTankRuptureBudget budget), "共享破裂预算");
        int count = archive.PressureExplosions.Count(entry => entry.WorldKey == f.Key);
        Check(report, "破裂物料只进入一个真实队列载荷", count == 1 && Near(archive.PressureExplosions.Where(entry => entry.WorldKey == f.Key).SelectMany(entry => entry.Materials).Sum(value => value.GasMoles), 2m));
        Check(report, "库存排空，存活罐锁定等待结算", GetFluidInventory(b).IsEmpty && !CanDismantleFluid(b, out _));
        HandleFluidTankZeroHp(a);
        Check(report, "重复破裂不追加事件", archive.PressureExplosions.Count(entry => entry.WorldKey == f.Key) == count && budget.Claimed.Count == 1);
        b.State.Hp = 0; Require(HandleFluidTankZeroHp(b), "第二罐破裂");
        Check(report, "各成员只领取自己的有限爆炸预算", budget.Claimed.Count == 2 && archive.PressureExplosions.Count(entry => entry.WorldKey == f.Key) == 2);
        Check(report, "多个事件不重复释放液体", Near(archive.PressureExplosions.Where(entry => entry.WorldKey == f.Key).SelectMany(entry => entry.Materials).Sum(value => value.LiquidMoles), 20m));
        CompleteFluidRuptures(); Check(report, "共享破裂批次可完成清理", !archive.FluidRuptures.ContainsKey(budgetId));
    }
    #endregion

    #region 环境与容器生产事务
    private static void Atmosphere(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope();
        var target = new FluidInventory(); ulong random = 77; decimal original = f.Atmosphere.CurrentMoles;
        Require(AtmosphereService.TryExtractTo(f.Atmosphere, 1m, ref random, target, 1, .001, out FluidBatch extracted), "真实抽气");
        Check(report, "有限大气与抽气库存守恒", Near(original - f.Atmosphere.CurrentMoles, target.GasMoles) && target.GasMoles == extracted.GasMoles);
        var incompatible = new FluidInventory(); Require(incompatible.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.Nitrogen), .1m, 0, 293.15), 1, .001), "拒收目标");
        decimal before = f.Atmosphere.CurrentMoles; ulong state = random;
        Check(report, "抽气目标拒收不扣来源或随机态", !AtmosphereService.TryExtractTo(f.Atmosphere, 1m, ref random, incompatible, 1, .001, out _) && f.Atmosphere.CurrentMoles == before && random == state);
        f.Atmosphere.Gases[0].Moles = 99.5m;
        FluidBatch batch = FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.Oxygen), 2m, 0, 293.15);
        AtmosphereEmissionResult result = AtmosphereService.Emit(f.Atmosphere, batch);
        Check(report, "排气满容量分开记留存与逸散", result.RetainedMoles == .5m && result.EscapedMoles == 1.5m && f.Atmosphere.CapacityMoles == 100m && f.Atmosphere.CurrentMoles == 100m && f.Atmosphere.TotalEscapedMoles == 1.5m);
        Check(report, "无环境排气全部逸散", AtmosphereService.Emit(null, batch).EscapedMoles == 2m);
    }

    private static void OutletVent(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope(); MachineEntity outlet = f.Add("FluidOutlet"), pipe = f.Add("FluidPipe", 5);
        f.Rebuild(); FluidInventory source = Fill(outlet, FluidIds.Oxygen, .1m); decimal before = f.Atmosphere.CurrentMoles;
        f.Step(); FluidMachineState state = GetFluidState(outlet);
        Check(report, "正式环境出口扣量并登记排放", source.GasMoles < .1m && f.Atmosphere.CurrentMoles > before && state.LastEmittedStandardLiters > 0);
        Fill(pipe, FluidIds.Oxygen, .1m); Fill(pipe, FluidIds.PureWater, 0, 1m);
        Require(VentFluidPipe(pipe), "显式排空管段气体");
        Check(report, "排气只清气相保留真实液体", GetPhaseFluidInventory(pipe, "main", FluidPhase.Gas).GasMoles == 0 &&
            GetFluidInventory(pipe).LiquidMoles == 1m);
    }

    private static void LiquidTransfer(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope(); MachineEntity a = f.Add("FluidPipe"), b = f.Add("FluidPipe", 1); f.Rebuild();
        FillLiters(a, FluidIds.PureWater, .4m);
        var source = new IndustrialLiquidTransferPort((FluidMachineLogic)a.Logic, new FluidMachinePortDefinition { Id = "manual-out", Mode = "output", Phase = "liquid" });
        var target = new IndustrialLiquidTransferPort((FluidMachineLogic)b.Logic, new FluidMachinePortDefinition { Id = "manual-in", Mode = "input", Phase = "liquid" });
        var context = new ContainerTransferContext(a.Position, 0, ContainerAccessKind.Machine, "流体回归");
        var initial = Total(a, b);
        ContainerTransferResult result = ContainerTransferService.TransferLiquid(source, target, context, .5f);
        Check(report, "正式液体端口按份数真实转移", result.Success && Near(GetFluidInventory(b).GetLiquidLiters(), .125));
        var end = Total(a, b);
        Check(report, "跨端口转液质量和热量守恒", Near(initial.Liquid, end.Liquid) && Near(initial.Energy, end.Energy));
        string before = Payload(GetFluidInventory(a));
        Check(report, "相同库存端口拒绝自转移", !ContainerTransferService.TransferLiquid(source, source, context, .5f).Success && Payload(GetFluidInventory(a)) == before);
        var far = new ContainerTransferContext((Vector2)a.Position + Vector2.right * 10, 0, ContainerAccessKind.Machine, "超距离");
        Check(report, "越距操作拒绝且不扣量", !ContainerTransferService.TransferLiquid(source, target, far, .5f).Success && Payload(GetFluidInventory(a)) == before);
        object saved = target.CaptureState(); GetFluidInventory(b).Drain(); target.RestoreState(saved);
        Check(report, "容器快照回滚恢复真实内容", Near(GetFluidInventory(b).GetLiquidLiters(), .125));
    }

    private static void TickScheduler(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope(); MachineEntity source = f.Add("FluidOutlet"), target = f.Add("LiquidStorageTank", 1);
        f.Rebuild(); Fill(source, FluidIds.Oxygen, .1m); uint step = TransportStep;
        f.Tick();
        Check(report, "完整 MachineWorld.Tick 推进固定步长和真实管网", TransportStep == step + 1 && GetFluidInventory(target).GasMoles > 0);
        Check(report, "非玩家世界保持流体网活动", IsFluidActive(source) && IsFluidActive(target));
    }
    #endregion

    #region PlayMode Lua 管网与储罐原始采样
    // 夹具只摆放并调用生产接口，物质筛选、相态和守恒规则由 Lua 读取原始数值断言。
    internal static JObject CaptureSelectorRoutes(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        int seed = args.Value<int?>("seed") ?? 1;
        if (seed <= 0) throw new ArgumentOutOfRangeException(nameof(args), "选择器采样随机态必须为正整数。");
        bool onlySelected = args.Value<bool?>("onlySelected") ?? false;
        using var f = new GameplayFluidScope();
        MachineEntity selector = f.Add("GasSelector"), north = f.Add("FluidOutlet", 0, 1, 1), south = f.Add("FluidOutlet", 0, -1, 3);
        MachineEntity selectedTank = f.Add("LiquidStorageTank", 0, 2), otherTank = f.Add("LiquidStorageTank", 0, -2);
        selector.Definition.Fluid.SingleGas = false; TopologyChanged(selector); f.Rebuild();
        FluidInventory source = Fill(selector, FluidIds.Oxygen, .1m);
        if (!onlySelected) Fill(selector, FluidIds.Nitrogen, .1m);
        source.State.RandomState = (ulong)seed;
        string unselectedBefore = Payload(source), unselectedRandom = source.State.RandomState.ToString();
        f.Step();
        var unselected = new JObject
        {
            ["before"] = unselectedBefore, ["after"] = Payload(source),
            ["randomBefore"] = unselectedRandom, ["randomAfter"] = source.State.RandomState.ToString(),
            ["ports"] = source.State.Ports.Count,
            ["selectedTankMoles"] = GetFluidInventory(selectedTank).GasMoles,
            ["otherTankMoles"] = GetFluidInventory(otherTank).GasMoles
        };
        FluidDeviceBehavior behavior = GetFluidDeviceBehavior(selector);
        bool selected = behavior.Execute(selector, "fluid.select", FluidIds.Oxygen);
        string selectedBeforeInvalid = GetFluidState(selector).SelectedGasId, invalidBefore = Payload(source);
        bool invalidAccepted = behavior.Execute(selector, "fluid.select", "core:missing_test_fluid");
        var invalid = new JObject
        {
            ["accepted"] = invalidAccepted, ["selectedBefore"] = selectedBeforeInvalid,
            ["selectedAfter"] = GetFluidState(selector).SelectedGasId,
            ["before"] = invalidBefore, ["after"] = Payload(source)
        };
        var initial = Total(selector, north, south, selectedTank, otherTank);
        var steps = new JArray();
        for (int i = 0; i < 12; i++)
        {
            f.Step();
            steps.Add(new JObject
            {
                ["step"] = i + 1,
                ["selectedOxygen"] = GetFluidInventory(selectedTank).GetGasMoles(FluidIds.Oxygen),
                ["selectedNitrogen"] = GetFluidInventory(selectedTank).GetGasMoles(FluidIds.Nitrogen),
                ["otherOxygen"] = GetFluidInventory(otherTank).GetGasMoles(FluidIds.Oxygen),
                ["otherNitrogen"] = GetFluidInventory(otherTank).GetGasMoles(FluidIds.Nitrogen),
                ["reservations"] = JArray.FromObject(source.State.Ports)
            });
        }
        var final = Total(selector, north, south, selectedTank, otherTank);
        MachineEntity poolNode = f.Add("GasSelector", 20);
        poolNode.Definition.Fluid.SingleGas = false; TopologyChanged(poolNode); f.Rebuild();
        FluidInventory pool = Fill(poolNode, FluidIds.Oxygen, .1m); Fill(poolNode, FluidIds.Nitrogen, .1m);
        pool.State.RandomState = (ulong)seed;
        string refusedBefore = Payload(pool), refusedRandom = pool.State.RandomState.ToString();
        bool refused = pool.TryReserve("no-candidate", FluidPhase.Gas, .02m, id => id == FluidIds.Hydrogen, out _);
        var rejection = new JObject
        {
            ["accepted"] = refused, ["before"] = refusedBefore, ["after"] = Payload(pool),
            ["randomBefore"] = refusedRandom, ["randomAfter"] = pool.State.RandomState.ToString(),
            ["ports"] = pool.State.Ports.Count
        };
        bool reserved = pool.TryReserve("sticky", FluidPhase.Gas, .02m, id => id == FluidIds.Oxygen, out FluidBatch first);
        string retryBefore = Payload(pool), retryRandom = pool.State.RandomState.ToString();
        bool retried = pool.TryReserve("sticky", FluidPhase.Gas, .001m, id => id == FluidIds.Nitrogen, out FluidBatch retry);
        MachineEntity switching = f.Add("GasSelector", 30), outlet = f.Add("FluidOutlet", 30, 1, 1);
        MachineEntity receiver = f.Add("LiquidStorageTank", 30, 2);
        switching.Definition.Fluid.SingleGas = false; TopologyChanged(switching); f.Rebuild();
        FluidInventory switchingInventory = Fill(switching, FluidIds.Oxygen, .1m);
        Fill(switching, FluidIds.Nitrogen, .1m);
        FluidDeviceBehavior switchingBehavior = GetFluidDeviceBehavior(switching);
        bool selectedOxygen = switchingBehavior.Execute(switching, "fluid.select", FluidIds.Oxygen);
        FluidNetworkGraph.Edge selectedEdge = f.Graph.Edges.Single(edge => edge.Source == switching &&
            edge.Target == outlet && edge.SourcePort.Id == "selected");
        string switchedPort = selectedEdge.Id + ":gas";
        bool oldReserved = switchingInventory.TryReserve(switchedPort, FluidPhase.Gas, .02m,
            id => id == FluidIds.Oxygen, out _);
        bool selectedNitrogen = switchingBehavior.Execute(switching, "fluid.select", FluidIds.Nitrogen);
        var switchedInitial = Total(switching, outlet, receiver);
        for (int step = 0; step < 5; step++) f.Step();
        var switchedFinal = Total(switching, outlet, receiver);
        FluidOutputReservation remainingReservation = switchingInventory.GetReservation(switchedPort);
        var switched = new JObject
        {
            ["oldReserved"] = oldReserved, ["selectedOxygen"] = selectedOxygen, ["selectedNitrogen"] = selectedNitrogen,
            ["remainingReservationFluid"] = remainingReservation?.FluidId ?? string.Empty,
            ["remainingOxygen"] = switchingInventory.GetGasMoles(FluidIds.Oxygen),
            ["receiverOxygen"] = GetFluidInventory(receiver).GetGasMoles(FluidIds.Oxygen),
            ["receiverNitrogen"] = GetFluidInventory(receiver).GetGasMoles(FluidIds.Nitrogen),
            ["initialGas"] = switchedInitial.Gas, ["finalGas"] = switchedFinal.Gas,
            ["initialEnergy"] = switchedInitial.Energy, ["finalEnergy"] = switchedFinal.Energy
        };
        return new JObject
        {
            ["seed"] = seed, ["onlySelected"] = onlySelected, ["selectionAccepted"] = selected,
            ["inputOxygen"] = .1m, ["inputNitrogen"] = onlySelected ? 0m : .1m,
            ["unselected"] = unselected, ["invalidSelection"] = invalid, ["steps"] = steps,
            ["initialGas"] = initial.Gas, ["finalGas"] = final.Gas,
            ["initialEnergy"] = initial.Energy, ["finalEnergy"] = final.Energy,
            ["rejectedPool"] = rejection,
            ["switched"] = switched,
            ["sticky"] = new JObject
            {
                ["reserved"] = reserved, ["retried"] = retried,
                ["firstFluid"] = first.FluidId, ["retryFluid"] = retry.FluidId,
                ["firstMoles"] = first.GasMoles, ["retryMoles"] = retry.GasMoles,
                ["before"] = retryBefore, ["after"] = Payload(pool),
                ["randomBefore"] = retryRandom, ["randomAfter"] = pool.State.RandomState.ToString()
            }
        };
    }

    internal static JObject CapturePhasePreference(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        int direction = args.Value<int?>("direction") ?? 0;
        if (direction < 0 || direction > 3) throw new ArgumentOutOfRangeException(nameof(args), "相态采样方向必须为 0 到 3。");
        bool mixed = args.Value<bool?>("mixed") ?? true;
        using var f = new GameplayFluidScope();
        MachineEntity source = f.Add("FluidOutlet", rotation: direction);
        Vector2Int offset = MechanicalNetworkGraph.Directions[direction];
        MachineEntity target = f.Add("LiquidStorageTank", offset.x, offset.y);
        f.Rebuild(); bool vertical = (direction & 1) == 1;
        if (mixed || vertical) Fill(source, FluidIds.Oxygen, .05m);
        if (mixed || !vertical) Fill(source, FluidIds.PureWater, 0, 1m);
        var initial = Total(source, target);
        for (int step = 0; step < 12; step++) f.Step();
        FluidInventory result = GetFluidInventory(target), remaining = GetFluidInventory(source);
        var final = Total(source, target);
        return new JObject
        {
            ["direction"] = direction, ["mixed"] = mixed, ["flowPhase"] = GetFluidState(source).LastFlowPhase,
            ["targetOxygenGas"] = result.GetGasMoles(FluidIds.Oxygen),
            ["targetWaterGas"] = result.GetGasMoles(FluidIds.PureWater),
            ["targetWaterLiquid"] = result.GetLiquidMoles(FluidIds.PureWater),
            ["remainingOxygenGas"] = remaining.GetGasMoles(FluidIds.Oxygen),
            ["remainingWaterGas"] = remaining.GetGasMoles(FluidIds.PureWater),
            ["remainingWaterLiquid"] = remaining.GetLiquidMoles(FluidIds.PureWater),
            ["initialGas"] = initial.Gas, ["initialLiquid"] = initial.Liquid,
            ["finalGas"] = final.Gas, ["finalLiquid"] = final.Liquid,
            ["initialEnergy"] = initial.Energy, ["finalEnergy"] = final.Energy,
            ["reservations"] = JArray.FromObject(remaining.State.Ports)
        };
    }

    // 通过正式机器命令给真实双格共享罐补气，采样交给 Lua 判断守恒和操作资格。
    internal static JObject CaptureTankManualAirFill(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f = new GameplayFluidScope();
        f.Atmosphere.CapacityMoles = f.Atmosphere.ReferenceMoles = 1000m;
        f.Atmosphere.ReferencePressureKPa = PortableGasTankAirFilling.TargetPressureKPa * 2d;
        f.Atmosphere.Gases = new List<AtmosphereGasAmount>
        {
            new() { FluidId = FluidIds.Oxygen, Moles = 210m },
            new() { FluidId = FluidIds.Nitrogen, Moles = 790m }
        };
        MachineEntity a = f.Add("GasTankBlock_Iron"), b = f.Add("GasTankBlock_Iron", 1);
        MachineEntity empty = f.Add("GasTankBlock_Iron", 4), steel = f.Add("GasTankBlock_Steel", 8);
        f.Rebuild();
        Fill(a, FluidIds.CarbonDioxide, .05m); Fill(a, FluidIds.PureWater, 0m, 20m);
        FluidInventory inventory = GetFluidInventory(a);
        decimal sourceBefore = f.Atmosphere.CurrentMoles, gasBefore = inventory.GasMoles;
        bool available = CanFillFluidTankAir(a);
        bool accepted = RequestOperation(b, "work", "", player);
        decimal sourceAfter = f.Atmosphere.CurrentMoles, gasAfter = inventory.GasMoles;
        bool repeated = RequestOperation(a, "work", "", player);
        bool fullAvailable = CanFillFluidTankAir(a);
        bool steelAccepted = RequestOperation(steel, "work", "", player);
        int rotationBefore = a.RotationQuarterTurns;
        bool rotated = RequestOperation(a, "fluid.rotate", "", player);
        var result = new JObject
        {
            ["available"] = available, ["accepted"] = accepted,
            ["shared"] = ReferenceEquals(inventory, GetFluidInventory(b)),
            ["volume"] = GetFluidVolumeLiters(a),
            ["pressure"] = inventory.GetPressureKPa(GetFluidVolumeLiters(a), GetFluidMinimumGasSpaceLiters(a)),
            ["oxygen"] = inventory.GetGasMoles(FluidIds.Oxygen), ["nitrogen"] = inventory.GetGasMoles(FluidIds.Nitrogen),
            ["carbon"] = inventory.GetGasMoles(FluidIds.CarbonDioxide), ["water"] = inventory.GetLiquidMoles(FluidIds.PureWater),
            ["sourceTaken"] = sourceBefore - sourceAfter, ["targetAdded"] = gasAfter - gasBefore,
            ["repeated"] = repeated, ["fullAvailable"] = fullAvailable,
            ["retrySourceUnchanged"] = f.Atmosphere.CurrentMoles == sourceAfter,
            ["steelAccepted"] = steelAccepted, ["steelEmpty"] = GetFluidInventory(steel).IsEmpty,
            ["rotated"] = rotated, ["rotationBefore"] = rotationBefore, ["rotationAfter"] = a.RotationQuarterTurns
        };
        f.Atmosphere.ReferencePressureKPa = 50d;
        result["thinAvailable"] = CanFillFluidTankAir(empty);
        result["thinAccepted"] = RequestOperation(empty, "work", "", player);
        f.Atmosphere.Gases.Clear();
        result["vacuumAvailable"] = CanFillFluidTankAir(empty);
        result["vacuumAccepted"] = RequestOperation(empty, "work", "", player);
        result["emptyAfterRejections"] = GetFluidInventory(empty).IsEmpty;
        return result;
    }

    internal static JObject CaptureTankOwnership(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f = new GameplayFluidScope(); string from = f.NewShip(), to = f.NewShip();
        using var fromScope = UseScope(GetShipScopeKey(from));
        MachineEntity a = f.Add("GasTankBlock_Iron"), b = f.Add("GasTankBlock_Iron", 1), c = f.Add("GasTankBlock_Steel", 2);
        f.Rebuild();
        FluidTankGroup group = f.Groups().First(value => value.Members.Contains(a));
        MachineEntity owner = group.OwnerId == a.Id ? a : b, nonOwner = group.OwnerId == a.Id ? b : a;
        bool shared = ReferenceEquals(GetFluidInventory(a), GetFluidInventory(b));
        bool foreignShared = ReferenceEquals(GetFluidInventory(b), GetFluidInventory(c));
        double volume = GetFluidVolumeLiters(a), gasSpace = GetFluidMinimumGasSpaceLiters(a);
        int maskA = GetFluidTankConnectionMask(a), maskB = GetFluidTankConnectionMask(b), maskC = GetFluidTankConnectionMask(c);
        int ownerA = GetFluidState(a).GroupOwnerId, ownerB = GetFluidState(b).GroupOwnerId;
        Fill(a, FluidIds.Oxygen, 2m); Fill(a, FluidIds.PureWater, 0, 20m);
        FluidInventory old = GetFluidInventory(a); var initial = Total(a, b);
        FluidMachineState primary = MachinePersistence.Read<FluidMachineState>(CaptureSnapshot(owner), "fluid");
        FluidMachineState secondary = MachinePersistence.Read<FluidMachineState>(CaptureSnapshot(nonOwner), "fluid");
        FluidInventoryState primaryContents = primary.Chambers["main"], secondaryContents = secondary.Chambers["main"];
        int ownerId = group.OwnerId;
        TransferShipNodes(from, to, new[] { b.Id }); var final = Total(a, b);
        FluidInventory first; using (UseNodeScope(a)) first = GetFluidInventory(a);
        FluidInventory second; using (UseNodeScope(b)) second = GetFluidInventory(b);
        return new JObject
        {
            ["aId"] = a.Id, ["bId"] = b.Id, ["ownerId"] = ownerId,
            ["ownerA"] = ownerA, ["ownerB"] = ownerB, ["savedOwnerId"] = owner.Id, ["savedNonOwnerId"] = nonOwner.Id,
            ["shared"] = shared, ["foreignShared"] = foreignShared, ["volume"] = volume, ["minimumGasSpace"] = gasSpace,
            ["maskA"] = maskA, ["maskB"] = maskB, ["maskC"] = maskC,
            ["primaryContents"] = JObject.FromObject(primaryContents), ["secondaryContents"] = JObject.FromObject(secondaryContents),
            ["initialGas"] = initial.Gas, ["initialLiquid"] = initial.Liquid, ["initialEnergy"] = initial.Energy,
            ["finalGas"] = final.Gas, ["finalLiquid"] = final.Liquid, ["finalEnergy"] = final.Energy,
            ["oldInventoryEmpty"] = old.IsEmpty, ["splitShared"] = ReferenceEquals(first, second),
            ["firstGas"] = first.GasMoles, ["firstLiquid"] = first.LiquidMoles,
            ["secondGas"] = second.GasMoles, ["secondLiquid"] = second.LiquidMoles
        };
    }

    // 正式地表与船本地格往返只采样真实引用、坐标和库存，业务判断交给 Lua。
    internal static JObject CaptureGroundScopeRoundtrip(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f = new GameplayFluidScope(surface: true);
        string groundKey = f.Key, shipKey = GetShipScopeKey(f.NewShip());
        MachineEntity a = f.Add("GasTankBlock_Iron", rotation: 0);
        MachineEntity b = f.Add("GasTankBlock_Iron", 1, rotation: 1);
        MachineEntity pipe = f.Add("FluidPipe", 2, rotation: 2);
        MachineEntity mill = f.Add("Millstone", 2, rotation: 3);
        RecipeProcessor processor = mill.Processor ?? throw new InvalidOperationException("石磨夹具没有创建正式加工器。");
        MachineEntity[] members = { a, b, pipe, mill };
        MachineEntity[] fluidMembers = { a, b, pipe };
        int[] ids = members.Select(node => node.Id).ToArray();
        var ground = members.ToDictionary(node => node.Id,
            node => new MachineScopePlacement(node.Cell, node.Snapshot.transform.position, node.RotationQuarterTurns));
        var local = new Dictionary<int, MachineScopePlacement>
        {
            [a.Id] = new(new Vector2Int(0, 0), new Vector3(.5f, .5f), 3),
            [b.Id] = new(new Vector2Int(1, 0), new Vector3(1.5f, .5f), 2),
            [pipe.Id] = new(new Vector2Int(2, 0), new Vector3(2.5f, .5f), 1),
            [mill.Id] = new(new Vector2Int(2, 0), new Vector3(2.5f, .5f), 0)
        };
        f.Rebuild();
        Fill(a, FluidIds.Oxygen, 4m); Fill(a, FluidIds.PureWater, 0m, 40m);
        Fill(pipe, FluidIds.Oxygen, .1m);

        JObject Sample(string key)
        {
            using var lease = UseScope(key);
            var total = Total(fluidMembers);
            var nodes = new JArray();
            foreach (MachineEntity node in members)
            {
                MachineState saved = ReadMachineState(node.Snapshot);
                nodes.Add(new JObject
                {
                    ["id"] = node.Id, ["scopeKey"] = node.ScopeKey, ["owned"] = Contains(node),
                    ["sameNode"] = ReferenceEquals(node, GetAt(node.Cell, node.Definition.Layer)),
                    ["cellX"] = node.Cell.x, ["cellY"] = node.Cell.y,
                    ["positionX"] = node.Snapshot.transform.position.x, ["positionY"] = node.Snapshot.transform.position.y,
                    ["quarterTurns"] = node.RotationQuarterTurns,
                    ["stateQuarterTurns"] = node.State?.RotationQuarterTurns,
                    ["savedQuarterTurns"] = saved.RotationQuarterTurns
                });
            }
            return new JObject
            {
                ["nodes"] = nodes, ["processorPresent"] = mill.Processor != null,
                ["sameProcessor"] = ReferenceEquals(processor, mill.Processor),
                ["gas"] = total.Gas, ["liquid"] = total.Liquid, ["energy"] = total.Energy,
                ["tanksShared"] = ReferenceEquals(GetFluidInventory(a), GetFluidInventory(b)),
                ["pipeGas"] = GetFluidInventory(pipe).GasMoles
            };
        }

        int OwnedIn(string key)
        {
            using var lease = UseScope(key);
            return members.Count(Contains);
        }

        JObject initial = Sample(groundKey);
        TransferScopeNodes(groundKey, shipKey, ids, node => local[node.Id]);
        JObject flight = Sample(shipKey);
        flight["groundOwnedCount"] = OwnedIn(groundKey);

        MachineEntity blocker;
        using (UseScope(groundKey)) blocker = f.Add("GasTankBlock_Steel");
        bool rejected = false;
        string rejection = null;
        try { TransferScopeNodes(shipKey, groundKey, ids, node => ground[node.Id]); }
        catch (InvalidOperationException error) { rejected = true; rejection = error.Message; }
        JObject afterRejected = Sample(shipKey);
        afterRejected["rejected"] = rejected;
        afterRejected["reason"] = rejection;
        afterRejected["groundOwnedCount"] = OwnedIn(groundKey);
        using (UseScope(groundKey))
        {
            afterRejected["sameBlocker"] = ReferenceEquals(blocker, GetAt(blocker.Cell, blocker.Definition.Layer));
            DiscardPlacementCandidate(blocker.Id);
            f.ForgetDetached(blocker);
        }

        TransferScopeNodes(shipKey, groundKey, ids, node => ground[node.Id]);
        JObject landed = Sample(groundKey);
        landed["shipOwnedCount"] = OwnedIn(shipKey);
        return new JObject
        {
            ["groundKey"] = groundKey, ["shipKey"] = shipKey,
            ["initial"] = initial, ["flight"] = flight,
            ["afterRejected"] = afterRejected, ["landed"] = landed
        };
    }
    #endregion
}
