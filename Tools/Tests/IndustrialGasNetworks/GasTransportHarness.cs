using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Networking
{
    public static class GameNetwork { public static bool HasStateAuthority = true; }
}
public sealed class CombustionSupportRequest
{
    public double FuelUnits = 1;
    public bool TrySupply(double fuel, double heat) => throw new NotSupportedException("不伪造燃烧系统集成结果。");
}
public static class MachineCatalog
{
    public sealed class Configuration { public double OxygenOutletHeatBonus = 1; }
    public static Configuration Settings = new();
}

// 仅隔离世界、网络开关和环境宿主；结算、预算、端口适配器和拓扑均直接编译正式实现。
public static partial class MachineWorld
{
    private sealed class ScopeState
    {
        public readonly Dictionary<FluidInventory, FluidInventoryState> GasStock = new(), GasStockCache = new();
        public readonly Dictionary<FluidInventory, double> GasThroughput = new();
        public readonly Dictionary<(int NodeId, string PortId), double> GasPortThroughput = new();
    }
    private static readonly ScopeState scope = new();
    private static readonly Dictionary<int, MachineEntity> registered = new();
    public static readonly List<FluidBatch> TestEmissions = new();
    public static bool Contains(MachineEntity node) => registered.TryGetValue(node.Id, out var actual) && ReferenceEquals(node, actual);
    internal static FluidInventory GetPhaseFluidInventory(MachineEntity node, string chamber, FluidPhase phase)
        => phase == FluidPhase.Gas && Gas != null && Gas.TryGetGroup(node, out var group) ? group.Inventory : node.Local;
    internal static double GetPhaseFluidVolumeLiters(MachineEntity node, FluidPhase phase)
        => phase == FluidPhase.Gas && Gas != null && Gas.TryGetGroup(node, out var group) ? group.GasSpaceLiters : node.Definition.Fluid.VolumeLiters;
    internal static double GetPhaseFluidMinimumGasSpaceLiters(MachineEntity node, FluidPhase phase)
        => phase == FluidPhase.Gas && Gas != null && Gas.TryGetGroup(node, out var group) ? group.MinimumGasSpaceLiters : node.Definition.Fluid.MinimumGasSpaceLiters;
    public static void StateChanged(MachineEntity node)
    { if (Gas != null && Gas.TryGetGroup(node, out var group)) group.UpdateLiquid(node, node.Local); }
    private static void RecordAtmosphereEmission(MachineEntity node, FluidBatch batch, Vector2? position = null)
        => throw new NotSupportedException("本套测试不模拟星球或飞船大气提交。");
    public static void TestBeginGasStep(float seconds, params MachineEntity[] nodes)
    {
        registered.Clear(); TestEmissions.Clear(); ResetGasBudgets(true);
        foreach (MachineEntity node in nodes)
        {
            registered.Add(node.Id, node);
            bool shared = Gas != null && Gas.TryGetGroup(node, out _);
            var inventory = GetPhaseFluidInventory(node, "main", FluidPhase.Gas);
            double throughput = node.Definition.Fluid.ThroughputLitersPerSecond;
            if (shared && Gas.TryGetGroup(node, out var group)) throughput = group.ThroughputLitersPerSecond;
            RememberGasInventory(inventory, throughput, seconds);
        }
    }
    public static bool TestMoveGas(FluidNetworkGraph.Edge edge, float seconds) => TryMoveGasBoundary(edge, seconds);
}
