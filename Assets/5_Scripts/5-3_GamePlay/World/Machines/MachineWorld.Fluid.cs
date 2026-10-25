using System;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Networking;
using Unity.Profiling;
using UnityEngine;

public static partial class MachineWorld
{
    #region 无库存连接图与本地储罐
    // 展示与船体统计沿用单罐视图，每个视图严格只有一个库存所有者。
    public sealed class FluidTankGroup
    {
        public readonly List<MachineEntity> Members = new();
        public FluidInventory Inventory;
        public double TotalVolume;
        public double MinimumGasSpace;
        public bool Active;
        public int OwnerId => Members.Count == 0 ? 0 : Members[0].Id;
    }

    private static FluidNetworkGraph fluidGraph { get => scope.Fluid; set => scope.Fluid = value; }
    private static Dictionary<int, FluidMachineState> fluidStates => scope.Fluids;
    private static Dictionary<(int NodeId, string Chamber), FluidInventory> fluidInventories => scope.Inventories;
    private static Dictionary<int, FluidTankGroup> fluidTankOwners => scope.TankOwners;
    private static List<FluidTankGroup> fluidTankGroups => scope.Tanks;
    private static Dictionary<FluidInventory, FluidInventoryState> fluidStepStock => scope.Stock;
    private static Dictionary<FluidInventory, FluidInventoryState> fluidStockCache => scope.StockCache;
    private static Dictionary<FluidInventory, double> fluidThroughputRemaining => scope.Throughput;
    private static Dictionary<MachineEntity, double> fluidMechanicalPower => scope.MechanicalPower;
    private static Dictionary<int, int> fluidAppearance => scope.Appearance;

    internal static FluidNetworkGraph FluidGraph => fluidGraph;
    internal static double GetIndustrialThroughput(MachineEntity node)
        => GetFluidDeviceBehavior(node).EmitsToEnvironment && GetFluidState(node).OutputLitersPerSecond >= 0d
            ? GetFluidState(node).OutputLitersPerSecond : node.Definition.Fluid.ThroughputLitersPerSecond;
    private static readonly Dictionary<FluidMachineDefinition, (string Kind, long Generation, FluidDeviceBehavior Behavior)> fluidBehaviors = new();
    public static FluidDeviceBehavior GetFluidDeviceBehavior(MachineEntity node)
    {
        FluidMachineDefinition definition = node?.Definition.Fluid;
        if (definition == null) return null;
        long generation = MachineLogicRegistry.FluidDeviceGeneration;
        if (fluidBehaviors.TryGetValue(definition, out var cached) && cached.Generation == generation && cached.Kind == definition.Kind)
            return cached.Behavior;
        FluidDeviceBehavior behavior = MachineLogicRegistry.GetFluidDeviceBehavior(definition.Kind);
        fluidBehaviors[definition] = (definition.Kind, generation, behavior);
        return behavior;
    }
    internal static Vector2Int NormalizeMachineCell(Vector2Int cell) => graph.NormalizeCell(cell);
    internal static void ClearFluidDefinitionCaches()
    { fluidBehaviors.Clear(); }
    internal static void InvalidateFluidDeviceStrategies()
    {
        ClearFluidDefinitionCaches();
        foreach (ScopeState value in scopes.Values) { value.Dirty = true; value.FluidTopologyKey = null; value.Appearance.Clear(); }
        scope.FluidTopologyKey = null;
        dirty = true; fluidAppearance.Clear(); InvalidateCombustionSupportSources();
    }
    internal static bool TryGetFluidTransferBudget(FluidInventory inventory, out FluidInventoryState stock, out double budget)
    {
        bool hasStock = fluidStepStock.TryGetValue(inventory, out stock);
        return fluidThroughputRemaining.TryGetValue(inventory, out budget) && hasStock;
    }
    internal static void SetFluidTransferBudget(FluidInventory inventory, double remaining)
        => fluidThroughputRemaining[inventory] = remaining;
    internal static void ConsumeFluidStepStock(FluidInventory inventory, string fluidId, decimal gas, decimal liquid)
    {
        if (gas > 0m) ConsumeGasStepStock(inventory, fluidId, gas);
        if (!fluidStepStock.TryGetValue(inventory, out FluidInventoryState stock)) return;
        FluidComponentState component = stock.Components.Find(value => value.FluidId == fluidId);
        if (component == null) return;
        component.GasMoles = Math.Max(0, component.GasMoles - gas);
        component.LiquidMoles = Math.Max(0, component.LiquidMoles - liquid);
    }
    internal static void SetFluidMechanicalPower(MachineEntity node, double watts) => fluidMechanicalPower[node] = watts;
    internal static bool TryGetFluidMechanicalPower(MachineEntity node, out double watts) => fluidMechanicalPower.TryGetValue(node, out watts);
    internal static float GetFluidMechanicalSourceFactor(MachineEntity node)
        => fluidMechanicalPower.TryGetValue(node, out double watts)
            ? (float)Math.Clamp(watts / (node.Definition.Torque * node.Definition.Rpm * MachineCatalog.Settings.WattsPerTorqueRpm), 0, 1) : 0;

    public static FluidMachineState GetFluidState(MachineEntity node)
    {
        if (node?.Definition.Fluid == null) return null;
        if (fluidStates.TryGetValue(node.Id, out FluidMachineState state)) return state;
        state = MachinePersistence.Read<FluidMachineState>(node.Snapshot, "fluid") ?? new FluidMachineState();
        state.Chambers ??= new(StringComparer.Ordinal);
        state.BlockedPorts ??= new(StringComparer.Ordinal);
        FluidMachineDefinition definition = node.Definition.Fluid;
        if (state.RandomState == 1) state.RandomState = unchecked((ulong)(uint)node.Id * 0x9E3779B97F4A7C15UL) | 1UL;
        fluidStates.Add(node.Id, state);
        return state;
    }

    public static FluidInventory GetFluidInventory(MachineEntity node, string chamberId = "main")
    {
        FluidMachineState state = GetFluidState(node);
        if (state == null) return null;
        var key = (node.Id, chamberId);
        if (fluidInventories.TryGetValue(key, out FluidInventory existing)) return existing;
        if (GetFluidDeviceBehavior(node).IsTransit)
        { var empty = new FluidInventory(); fluidInventories.Add(key, empty); return empty; }
        if (!state.Chambers.TryGetValue(chamberId, out FluidInventoryState contents))
            state.Chambers[chamberId] = contents = new FluidInventoryState();
        var inventory = new FluidInventory(contents);
        fluidInventories.Add(key, inventory);
        return inventory;
    }

    // 物料始终由设备自己的腔体持有，连接图没有容量和气体副本。
    internal static FluidInventory GetPhaseFluidInventory(MachineEntity node, string chamber, FluidPhase phase)
        => GetFluidInventory(node, chamber);
    internal static double GetPhaseFluidVolumeLiters(MachineEntity node, FluidPhase phase) => GetFluidVolumeLiters(node);
    internal static double GetPhaseFluidMinimumGasSpaceLiters(MachineEntity node, FluidPhase phase) => GetFluidMinimumGasSpaceLiters(node);
    public static double GetFluidVolumeLiters(MachineEntity node) => node.Definition.Fluid.VolumeLiters;
    public static double GetFluidMinimumGasSpaceLiters(MachineEntity node) => node.Definition.Fluid.MinimumGasSpaceLiters;
    public static double GetIndustrialFreeCapacity(MachineEntity node, FluidPhase phase, string fluidId = null, string chamber = "main")
    {
        if (node?.Definition.Fluid == null || GetFluidDeviceBehavior(node).IsTransit ||
            phase == FluidPhase.Liquid && fluidId != null && !CanAcceptIndustrialLiquid(node, fluidId, chamber)) return 0d;
        return IndustrialFluidTransfer.FreeCapacity(GetFluidInventory(node, chamber), phase,
            node.Definition.Fluid.GasCapacityStandardLiters, node.Definition.Fluid.VolumeLiters, fluidId);
    }
    public static MachineEntity GetFluidPipeAt(Vector2Int cell)
    { EnsureScope(); RebuildGraphsIfDirty(); return fluidGraph?.PipeAt(cell); }
    public static int GetFluidPipeConnectionMask(MachineEntity node)
    {
        if (node?.Definition.IsFluidPipeSegment != true) return 0;
        using var nodeScope = UseNodeScope(node);
        RebuildGraphsIfDirty();
        return fluidGraph?.GetConnectionMask(node) ?? 0;
    }
    public static void CollectFluidTankGroups(List<FluidTankGroup> result)
    { EnsureScope(); RebuildGraphsIfDirty(); result.Clear(); result.AddRange(fluidTankGroups); }

    public static bool IsFluidTankBlock(MachineEntity node)
        => node?.Definition.Fluid != null && GetFluidDeviceBehavior(node)?.IsStorage == true;

    public static IReadOnlyList<MachineEntity> GetFluidTankMembers(MachineEntity node)
    {
        if (!IsFluidTankBlock(node)) return Array.Empty<MachineEntity>();
        using var nodeScope = UseNodeScope(node);
        RebuildGraphsIfDirty();
        return fluidTankOwners.TryGetValue(node.Id, out FluidTankGroup group) ? group.Members : Array.Empty<MachineEntity>();
    }

    // 外观只连接同一权威储罐组的四邻格，位序为北1、东2、南4、西8。
    public static int GetFluidTankConnectionMask(MachineEntity node) => 0;

    private static void RebuildFluidNetworks()
    {
        var sorted = new List<MachineEntity>(nodes.Values); sorted.Sort((a,b) => a.Id.CompareTo(b.Id));
        string topologyKey = GetFluidTopologyKey(sorted);
        if (fluidGraph != null && scope.FluidTopologyKey == topologyKey && ReferenceEquals(scope.FluidTopologyCatalog, FluidCatalog.Default)) return;
        fluidTankOwners.Clear(); fluidTankGroups.Clear();
        foreach (MachineEntity node in sorted)
        {
            if (!IsFluidTankBlock(node)) continue;
            var view = new FluidTankGroup { Inventory = GetFluidInventory(node), TotalVolume = node.Definition.Fluid.VolumeLiters,
                MinimumGasSpace = node.Definition.Fluid.MinimumGasSpaceLiters };
            view.Members.Add(node); fluidTankOwners.Add(node.Id, view); fluidTankGroups.Add(view);
        }
        fluidGraph = new FluidNetworkGraph(graph.NormalizeCell); fluidGraph.Rebuild(sorted);
        fluidStepStock.Clear(); fluidStockCache.Clear(); fluidThroughputRemaining.Clear(); ResetGasBudgets(true);
        scope.FluidTopologyKey = topologyKey; scope.FluidTopologyCatalog = FluidCatalog.Default;
    }

    public static void CaptureFluidState(MachineEntity node)
    {
        if (node?.Definition.Fluid == null) return;
        MachinePersistence.Write(node.Snapshot, "fluid", CreatePersistentFluidState(node));
    }

    private static FluidMachineState CreatePersistentFluidState(MachineEntity node)
    {
        FluidMachineState saved = MachinePersistence.Clone(GetFluidState(node));
        foreach (FluidInventoryState chamber in saved.Chambers.Values)
        { chamber.Ports.RemoveAll(port => port.PortId.StartsWith("engine:", StringComparison.Ordinal)); chamber.SynchronizeReservations(); }
        return saved;
    }

    #region 船体作用域库存迁移
    private static FluidMachineState DetachFluidNodeForScopeTransfer(MachineEntity node)
    {
        foreach (MachineEntity member in nodes.Values)
            if (member.Definition.Fluid != null) FluidDeviceOperations.ReleaseReservations(member);
        FluidMachineState transferred = CreatePersistentFluidState(node);
        MachinePersistence.Write(node.Snapshot, "fluid", transferred);
        ForgetDetachedFluidNode(node); dirty = true; return transferred;
    }

    private static void ForgetDetachedFluidNode(MachineEntity node)
    {
        if (fluidStates.Remove(node.Id, out FluidMachineState previous))
            foreach (FluidInventoryState chamber in previous.Chambers.Values)
                new FluidInventory(chamber).Restore(new FluidInventoryState { RandomState = chamber.RandomState });
        ForgetFluidNodeInventoryCaches(node.Id);
        fluidAppearance.Remove(node.Id);
        ResetGasBudgets(true);
        fluidMechanicalPower.Remove(node);
        fluidStepStock.Clear(); fluidStockCache.Clear(); fluidThroughputRemaining.Clear();
        InvalidateCombustionSupportSources();
    }

    private static void ForgetFluidNodeInventoryCaches(int id)
    {
        var remove = new List<(int NodeId, string Chamber)>();
        foreach (var key in fluidInventories.Keys) if (key.NodeId == id) remove.Add(key);
        foreach (var key in remove)
        { fluidStockCache.Remove(fluidInventories[key]); fluidInventories.Remove(key); }
    }


    #endregion

    private static void ResetFluidRuntime()
    {
        fluidGraph = null; scope.FluidTopologyKey = null; scope.FluidTopologyCatalog = null;
        ResetGasBudgets(true);
        fluidStates.Clear(); fluidInventories.Clear(); fluidTankOwners.Clear(); fluidTankGroups.Clear();
        fluidStepStock.Clear(); fluidStockCache.Clear(); fluidThroughputRemaining.Clear(); fluidMechanicalPower.Clear();
        fluidAppearance.Clear();
        combustionSources.Clear();
        combustionSourceSnapshot = Array.Empty<CombustionSupportLease>();
        combustionSourcesDirty = true;
    }

    private static void ApplyFluidRemoteState(MachineEntity node, bool logicApplied)
    {
        if (node.Definition.Fluid == null) return;
        FluidMachineState incoming = logicApplied && node.Logic is FluidMachineLogic logic ? logic.State :
            MachinePersistence.Read<FluidMachineState>(node.Snapshot, "fluid") ?? new FluidMachineState();
        fluidStates[node.Id] = incoming;
        ForgetFluidNodeInventoryCaches(node.Id);
        // 设备/液体更新只替换本节点；其他节点的气网快照、热量和库存不受消息到达顺序影响。
        if (fluidTankOwners.TryGetValue(node.Id, out FluidTankGroup group) && group.OwnerId == node.Id &&
            incoming.Chambers.TryGetValue("main", out FluidInventoryState contents))
            group.Inventory = new FluidInventory(contents);
    }
    #endregion

    #region 整网活动与每轮库存预算
    private static readonly ProfilerMarker fluidTransportMarker = new("MachineWorld.Fluid.Complete.Transport");
    private static readonly ProfilerMarker fluidAppearanceMarker = new("MachineWorld.Fluid.Complete.Appearance");
    private static void BeginFluidStep(float seconds)
    {
        if (fluidGraph == null) return;
        foreach (MachineEntity node in nodes.Values)
            if (node.Definition.Fluid != null) FluidDeviceOperations.ReleaseReservations(node);
        foreach (FluidNetworkGraph.Component component in fluidGraph.Components)
        {
            // 船内流体网随船持续模拟，不用地表玩家区块判断休眠。
            bool near = KeepSimulatingWithoutPlayers;
            int range = component.Active ? MachineCatalog.Settings.DeactivationChunks : MachineCatalog.Settings.ActivationChunks;
            if (!near)
            {
                foreach (MachineEntity node in component.Nodes)
                foreach (Vector2Int player in players) near |= graph.IsNear(node.SimulationBounds, player, range);
            }
            if (near) component.AwaySeconds = 0;
            else if (component.Active) component.AwaySeconds += seconds;
            component.Active = near || component.Active && component.AwaySeconds < MachineCatalog.Settings.UnloadDelaySeconds;
            foreach (MachineEntity node in component.Nodes)
            {
                if (component.Active)
                {
                    if (node.State == null) WakeNode(node);
                    node.Active = true;
                }
                else if (node.Definition.HasMechanicalPorts != true)
                {
                    if (node.State != null) { CaptureNode(node); DisposeProcessor(node); node.State = null; }
                    node.Active = false;
                }
            }
        }
        foreach (FluidTankGroup group in fluidTankGroups)
        {
            group.Active = false;
            foreach (MachineEntity member in group.Members) group.Active |= member.Active;
        }
        fluidStepStock.Clear(); fluidThroughputRemaining.Clear(); fluidMechanicalPower.Clear();
        scope.GasNodeBudget.Clear(); scope.LiquidNodeBudget.Clear();
        ResetGasBudgets();
        foreach (MachineEntity node in nodes.Values)
        {
            if (node.Definition.Fluid == null || !IsFluidActive(node)) continue;
            FluidMachineDefinition definition = node.Definition.Fluid;
            scope.GasNodeBudget[node.Id] = scope.LiquidNodeBudget[node.Id] = GetIndustrialThroughput(node) * seconds;
            if (GetFluidDeviceBehavior(node).IsTransit) continue;
            foreach (FluidMachinePortDefinition port in definition.Ports)
            {
                RememberFluidBudget(node, port.Chamber, seconds);
                RememberGasInventory(GetPhaseFluidInventory(node, port.Chamber, FluidPhase.Gas),
                    definition.ThroughputLitersPerSecond, seconds);
            }
            RememberFluidBudget(node, "main", seconds);
            RememberGasInventory(GetFluidInventory(node), definition.ThroughputLitersPerSecond, seconds);
        }
        foreach (MachineEntity node in nodes.Values)
            if (node.Definition.Fluid?.PullFromNetwork == true && IsFluidActive(node)) SupplyIndustrialConsumer(node, seconds);
        // 全部真实来源冻结后再给各设备分配本轮动力和预约。
        foreach (MachineEntity node in nodes.Values)
            if (node.Definition.Fluid != null && IsFluidActive(node)) GetFluidDeviceBehavior(node).BeginStep(node, seconds);
    }

    public static bool IsFluidActive(MachineEntity node)
        => node != null && fluidGraph != null && fluidGraph.NodeComponents.TryGetValue(node.Id, out var component) && component.Active;
    private static bool RequiresFluidSimulation(MechanicalNetwork network)
    {
        foreach (MachineEntity node in network.Nodes) if (IsFluidActive(node)) return true;
        return false;
    }
    private static void RememberFluidBudget(MachineEntity node, string chamber, float seconds)
    {
        FluidInventory inventory = GetFluidInventory(node, chamber);
        if (fluidStepStock.ContainsKey(inventory)) return;
        if (!fluidStockCache.TryGetValue(inventory, out FluidInventoryState stock))
            fluidStockCache.Add(inventory, stock = new FluidInventoryState());
        stock.CopyFrom(inventory.State, false);
        fluidStepStock.Add(inventory, stock);
        double capacity = node.Definition.Fluid.ThroughputLitersPerSecond * seconds;
        if (fluidTankOwners.TryGetValue(node.Id, out var group)) capacity *= group.Members.Count;
        fluidThroughputRemaining.Add(inventory, capacity);
    }



    private static void CompleteFluidStep(float seconds)
    {
        if (fluidGraph == null) return;
        using (fluidTransportMarker.Auto()) AdvanceIndustrialOutlets(seconds);
        using (fluidAppearanceMarker.Auto())
        foreach (MachineEntity node in nodes.Values)
        {
            if (node.Definition.Fluid == null || !IsFluidActive(node)) continue;
            FluidMachineState state = GetFluidState(node); FluidInventory contents = GetFluidInventory(node);
            int gas = (int)Math.Clamp(FluidUnits.MolToStandardLiters(contents.GasMoles) /
                (decimal)Math.Max(.001, node.Definition.Fluid.GasCapacityStandardLiters) * 32, 0, 32);
            int liquid = Math.Clamp((int)(contents.GetLiquidLiters() / GetFluidVolumeLiters(node) * 32), 0, 32);
            int appearance = HashCode.Combine(gas, liquid, state.LastFlowDirection, state.LastFlowPhase, state.BlockedPorts.Count > 0, state.ValveOpen);
            if (fluidAppearance.TryGetValue(node.Id, out int previous) && previous == appearance) continue;
            fluidAppearance[node.Id] = appearance; NotifyVisualChanged(node);
        }
    }
    #endregion
}
