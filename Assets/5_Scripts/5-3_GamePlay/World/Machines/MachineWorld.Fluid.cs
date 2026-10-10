using System;
using System.Collections.Generic;
using System.Globalization;
using FlatWorld.Networking;
using UnityEngine;

public static partial class MachineWorld
{
    #region 统一流体世界与共享储罐
    public sealed class FluidTankGroup
    {
        public readonly List<MachineEntity> Members = new();
        public FluidInventory Inventory;
        public double TotalVolume;
        public double MinimumGasSpace;
        public double MaxSafePressure;
        public int OverpressureVictimId;
        public ulong RandomState = 1;
        public bool Active;
        public int OwnerId => Members.Count == 0 ? 0 : Members[0].Id;
    }

    private static FluidNetworkGraph fluidGraph { get => scope.Fluid; set => scope.Fluid = value; }
    private static Dictionary<int, FluidMachineState> fluidStates => scope.Fluids;
    private static Dictionary<string, FluidInventory> fluidInventories => scope.Inventories;
    private static Dictionary<int, FluidTankGroup> fluidTankOwners => scope.TankOwners;
    private static List<FluidTankGroup> fluidTankGroups => scope.Tanks;
    private static Dictionary<FluidInventory, FluidInventoryState> fluidStepStock => scope.Stock;
    private static Dictionary<FluidInventory, double> fluidThroughputRemaining => scope.Throughput;
    private static Dictionary<MachineEntity, double> fluidMechanicalPower => scope.MechanicalPower;
    private static Dictionary<int, int> fluidAppearance => scope.Appearance;

    internal static FluidNetworkGraph FluidGraph => fluidGraph;
    public static FluidDeviceBehavior GetFluidDeviceBehavior(MachineEntity node)
        => node?.Definition.Fluid == null ? null : MachineLogicRegistry.GetFluidDeviceBehavior(node.Definition.Fluid.Kind);
    internal static Vector2Int NormalizeMachineCell(Vector2Int cell) => graph.NormalizeCell(cell);
    internal static void InvalidateFluidDeviceStrategies()
    { dirty = true; fluidAppearance.Clear(); InvalidateCombustionSupportSources(); }
    internal static bool TryGetFluidTransferBudget(FluidInventory inventory, out FluidInventoryState stock, out double budget)
    {
        bool hasStock = fluidStepStock.TryGetValue(inventory, out stock);
        return fluidThroughputRemaining.TryGetValue(inventory, out budget) && hasStock;
    }
    internal static void SetFluidTransferBudget(FluidInventory inventory, double remaining)
        => fluidThroughputRemaining[inventory] = remaining;
    internal static void ConsumeFluidStepStock(FluidInventory inventory, string fluidId, decimal gas, decimal liquid)
    {
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
        state.PhaseWarnings ??= new(StringComparer.Ordinal);
        state.ResidueProgress ??= new(StringComparer.Ordinal);
        FluidMachineDefinition definition = node.Definition.Fluid;
        if (state.DisconnectPressureKPa < 0) state.DisconnectPressureKPa = definition.DefaultDisconnectPressureKPa;
        if (state.ReconnectPressureKPa < 0) state.ReconnectPressureKPa = definition.DefaultReconnectPressureKPa;
        if (state.RandomState == 1) state.RandomState = unchecked((ulong)(uint)node.Id * 0x9E3779B97F4A7C15UL) | 1UL;
        node.FluidProbeConnected = state.ProbeConnected;
        fluidStates.Add(node.Id, state);
        return state;
    }

    public static FluidInventory GetFluidInventory(MachineEntity node, string chamberId = "main")
    {
        FluidMachineState state = GetFluidState(node);
        if (state == null) return null;
        if (chamberId == "main" && fluidTankOwners.TryGetValue(node.Id, out var group)) return group.Inventory;
        string key = node.Id + ":" + chamberId;
        if (fluidInventories.TryGetValue(key, out FluidInventory existing)) return existing;
        if (!state.Chambers.TryGetValue(chamberId, out FluidInventoryState contents))
            state.Chambers[chamberId] = contents = new FluidInventoryState();
        var inventory = new FluidInventory(contents);
        fluidInventories.Add(key, inventory);
        return inventory;
    }

    public static double GetFluidVolumeLiters(MachineEntity node)
        => fluidTankOwners.TryGetValue(node.Id, out var group) ? group.TotalVolume : node.Definition.Fluid.VolumeLiters;
    public static double GetFluidMinimumGasSpaceLiters(MachineEntity node)
        => fluidTankOwners.TryGetValue(node.Id, out var group) ? group.MinimumGasSpace : node.Definition.Fluid.MinimumGasSpaceLiters;
    public static MachineEntity GetFluidPipeAt(Vector2Int cell)
    { EnsureScope(); RebuildGraphsIfDirty(); return fluidGraph?.PipeAt(cell); }
    public static void CollectFluidTankGroups(List<FluidTankGroup> result)
    { EnsureScope(); RebuildGraphsIfDirty(); result.Clear(); result.AddRange(fluidTankGroups); }

    public static bool IsFluidTankBlock(MachineEntity node)
        => node?.Definition.Fluid?.CombineAdjacent == true && GetFluidDeviceBehavior(node)?.IsSharedStorage == true;

    // 外观只连接同一权威储罐组的四邻格，位序为北1、东2、南4、西8。
    public static int GetFluidTankConnectionMask(MachineEntity node)
    {
        if (!IsFluidTankBlock(node)) return 0;
        using var nodeScope = UseNodeScope(node);
        RebuildGraphsIfDirty();
        if (!fluidTankOwners.TryGetValue(node.Id, out FluidTankGroup group)) return 0;
        int mask = 0;
        foreach (Vector2Int direction in MechanicalNetworkGraph.Directions)
        {
            MachineEntity neighbor = graph.At(node.Cell + direction, node.Definition.Layer);
            if (neighbor == null || !fluidTankOwners.TryGetValue(neighbor.Id, out FluidTankGroup adjacent) ||
                !ReferenceEquals(group, adjacent)) continue;
            mask |= direction.y > 0 ? 1 : direction.x > 0 ? 2 : direction.y < 0 ? 4 : 8;
        }
        return mask;
    }

    private static void RebuildFluidNetworks()
    {
        SaveFluidGroups();
        var previousGroups = new List<FluidTankGroup>(fluidTankGroups);
        var previousContents = new HashSet<FluidInventoryState>();
        fluidTankOwners.Clear(); fluidTankGroups.Clear();
        var sorted = new List<MachineEntity>(nodes.Values); sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
        var tankCells = new Dictionary<Vector2Int, MachineEntity>();
        foreach (MachineEntity node in sorted)
            if (node.Definition.Fluid?.CombineAdjacent == true && GetFluidDeviceBehavior(node).IsSharedStorage)
                tankCells.Add(graph.NormalizeCell(node.Cell), node);
        var queue = new Queue<MachineEntity>();
        foreach (MachineEntity root in sorted)
        {
            if (GetFluidDeviceBehavior(root)?.IsSharedStorage != true || fluidTankOwners.ContainsKey(root.Id)) continue;
            var group = new FluidTankGroup(); fluidTankGroups.Add(group); fluidTankOwners.Add(root.Id, group); queue.Enqueue(root);
            while (queue.Count > 0)
            {
                MachineEntity node = queue.Dequeue(); group.Members.Add(node);
                FluidMachineDefinition definition = node.Definition.Fluid;
                group.TotalVolume += definition.VolumeLiters; group.MinimumGasSpace += definition.MinimumGasSpaceLiters;
                group.MaxSafePressure = definition.MaxSafePressureKPa;
                if (!definition.CombineAdjacent) continue;
                foreach (Vector2Int direction in MechanicalNetworkGraph.Directions)
                {
                    if (!tankCells.TryGetValue(graph.NormalizeCell(node.Cell + direction), out MachineEntity neighbor) ||
                        neighbor.Definition.Fluid.MaterialId != definition.MaterialId || fluidTankOwners.ContainsKey(neighbor.Id)) continue;
                    fluidTankOwners.Add(neighbor.Id, group); queue.Enqueue(neighbor);
                }
            }
            group.Members.Sort((a, b) => a.Id.CompareTo(b.Id));
            group.Inventory = new FluidInventory();
            bool inheritedRandom = false;
            foreach (MachineEntity member in group.Members)
            {
                FluidMachineState state = GetFluidState(member);
                if (state.Chambers.TryGetValue("main", out FluidInventoryState saved))
                {
                    previousContents.Add(saved);
                    if (!inheritedRandom && (state.GroupOwnerId == member.Id || saved.Components.Count > 0 || saved.Ports.Count > 0))
                    { group.Inventory.State.RandomState = saved.RandomState; inheritedRandom = true; }
                    // 拓扑重建只合并各载荷拥有的那一份库存，引用成员没有第二份内容。
                    MergeFluidInventory(group.Inventory, new FluidInventory(saved), group.TotalVolume, group.MinimumGasSpace);
                }
                if (state.OverpressureVictimId != 0) group.OverpressureVictimId = state.OverpressureVictimId;
                if (member.Id == group.OwnerId) group.RandomState = state.RandomState;
                fluidInventories.Remove(member.Id + ":main");
            }
            if (!inheritedRandom) group.Inventory.State.RandomState = group.RandomState;
        }
        fluidGraph = new FluidNetworkGraph(graph.NormalizeCell);
        fluidGraph.Rebuild(sorted);
        SaveFluidGroups();
        // 新网接过真实库存后撤销旧对象中的份额，旧端口引用不能继续取第二次。
        foreach (FluidInventoryState previous in previousContents)
            new FluidInventory(previous).Restore(new FluidInventoryState { RandomState = previous.RandomState });
        foreach (FluidTankGroup previous in previousGroups)
        {
            previous.Inventory.Restore(new FluidInventoryState { RandomState = previous.Inventory.State.RandomState });
            previous.Members.Clear(); previous.TotalVolume = 0d; previous.MinimumGasSpace = 0d; previous.Active = false;
        }
        fluidStepStock.Clear(); fluidThroughputRemaining.Clear();
    }

    private static void MergeFluidInventory(FluidInventory target, FluidInventory source, double volume, double minimum)
    {
        double totalEnergy = target.State.TotalInternalEnergyJoules + source.State.TotalInternalEnergyJoules;
        foreach (FluidBatch batch in FluidInventoryBatches(source))
            if (!target.TryAdd(batch, volume, minimum)) throw new InvalidOperationException("组合罐合并无法容纳已有真实库存。");
        // 端口预留包含在原库存中；核心状态的预留在合并时按稳定端口身份承接。
        FluidIndustrialStateBridge.MergeReservations(target.State, source.State);
        target.State.TotalInternalEnergyJoules = totalEnergy;
    }

    private static void SaveFluidGroups()
    {
        foreach (FluidTankGroup group in fluidTankGroups)
        {
            foreach (MachineEntity member in group.Members)
            {
                if (!nodes.ContainsKey(member.Id)) continue;
                FluidMachineState state = GetFluidState(member);
                state.GroupOwnerId = group.OwnerId;
                state.OverpressureVictimId = group.OverpressureVictimId;
                state.RandomState = group.RandomState;
                state.Chambers["main"] = member.Id == group.OwnerId ? group.Inventory.State : new FluidInventoryState();
            }
        }
    }

    public static void CaptureFluidState(MachineEntity node)
    {
        if (node?.Definition.Fluid == null) return;
        SaveFluidGroups();
        MachinePersistence.Write(node.Snapshot, "fluid", GetFluidState(node));
    }

    #region 船体作用域库存迁移
    private static FluidMachineState DetachFluidNodeForScopeTransfer(MachineEntity node)
    {
        if (fluidTankOwners.TryGetValue(node.Id, out FluidTankGroup group)) PartitionFluidGroupForScopeTransfer(group);
        FluidMachineState transferred = MachinePersistence.Clone(GetFluidState(node));
        transferred.GroupOwnerId = node.Id;
        MachinePersistence.Write(node.Snapshot, "fluid", transferred);
        ForgetDetachedFluidNode(node);
        dirty = true;
        return transferred;
    }

    private static void ForgetDetachedFluidNode(MachineEntity node)
    {
        if (fluidStates.Remove(node.Id, out FluidMachineState previous))
            foreach (FluidInventoryState chamber in previous.Chambers.Values)
                new FluidInventory(chamber).Restore(new FluidInventoryState { RandomState = chamber.RandomState });
        ForgetFluidNodeInventoryCaches(node.Id);
        fluidAppearance.Remove(node.Id);
        fluidMechanicalPower.Remove(node);
        fluidStepStock.Clear(); fluidThroughputRemaining.Clear();
        InvalidateCombustionSupportSources();
    }

    private static void ForgetFluidNodeInventoryCaches(int id)
    {
        var remove = new List<string>();
        string prefix = id.ToString(CultureInfo.InvariantCulture) + ":";
        foreach (string key in fluidInventories.Keys) if (key.StartsWith(prefix, StringComparison.Ordinal)) remove.Add(key);
        foreach (string key in remove) fluidInventories.Remove(key);
    }

    private static void PartitionFluidGroupForScopeTransfer(FluidTankGroup group)
    {
        if (group.Members.Count == 0 || group.Inventory == null || !FluidUnits.IsFinite(group.TotalVolume) || group.TotalVolume <= 0d)
            throw new InvalidOperationException("船体转移前的共享储罐组无效。");
        FluidInventoryState original = group.Inventory.State.Clone();
        var remaining = new List<FluidComponentState>();
        foreach (FluidComponentState component in original.Components) remaining.Add(component.Clone());
        var shares = new Dictionary<int, FluidInventoryState>();
        FluidInventoryState energyAdjustmentShare = null;
        double assignedEnergy = 0d;
        double temperature = group.Inventory.GetTemperatureKelvin();
        for (int i = 0; i < group.Members.Count; i++)
        {
            MachineEntity member = group.Members[i];
            if (!nodes.TryGetValue(member.Id, out MachineEntity current) || !ReferenceEquals(current, member))
                throw new InvalidOperationException("船体转移前的共享储罐成员已失效：" + member.Id);
            decimal fraction = (decimal)member.Definition.Fluid.VolumeLiters / (decimal)group.TotalVolume;
            bool last = i == group.Members.Count - 1;
            var share = new FluidInventoryState { RandomState = original.RandomState };
            for (int c = 0; c < original.Components.Count; c++)
            {
                FluidComponentState component = original.Components[c];
                decimal gas = last ? remaining[c].GasMoles : Math.Min(remaining[c].GasMoles, component.GasMoles * fraction);
                decimal liquid = last ? remaining[c].LiquidMoles : Math.Min(remaining[c].LiquidMoles, component.LiquidMoles * fraction);
                remaining[c].GasMoles -= gas; remaining[c].LiquidMoles -= liquid;
                if (gas + liquid <= 0m) continue;
                share.Components.Add(new FluidComponentState { FluidId = component.FluidId, GasMoles = gas, LiquidMoles = liquid });
                share.TotalInternalEnergyJoules += group.Inventory.Catalog.Find(component.FluidId).EnergyAt(gas, liquid, temperature);
            }
            if (share.Components.Count > 0)
            {
                assignedEnergy += share.TotalInternalEnergyJoules;
                if (energyAdjustmentShare == null || share.TotalInternalEnergyJoules > energyAdjustmentShare.TotalInternalEnergyJoules)
                    energyAdjustmentShare = share;
            }
            shares.Add(member.Id, share);
        }
        if (energyAdjustmentShare != null) energyAdjustmentShare.TotalInternalEnergyJoules += original.TotalInternalEnergyJoules - assignedEnergy;
        foreach (FluidOutputReservation reservation in original.Ports)
        {
            int separator = reservation.PortId.IndexOf(':');
            if (separator <= 0 || !int.TryParse(reservation.PortId.Substring(0, separator), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int memberId) || !shares.TryGetValue(memberId, out FluidInventoryState share)) continue;
            var portion = new FluidInventory(share, group.Inventory.Catalog);
            FluidOutputReservation local = reservation.Clone();
            local.GasMoles = Math.Min(local.GasMoles, portion.GetAvailableMoles(local.FluidId, FluidPhase.Gas));
            local.LiquidMoles = Math.Min(local.LiquidMoles, portion.GetAvailableMoles(local.FluidId, FluidPhase.Liquid));
            if (local.TotalMoles > 0m) share.Ports.Add(local);
        }
        foreach (FluidInventoryState share in shares.Values) FluidInventory.ValidateState(share, group.Inventory.Catalog);
        // 所有候选份额先校验，再清掉唯一旧库存，避免新旧组各自保存同一份总量。
        foreach (MachineEntity member in group.Members)
        {
            FluidMachineState state = GetFluidState(member);
            state.Chambers["main"] = shares[member.Id];
            state.GroupOwnerId = member.Id;
            state.OverpressureVictimId = group.OverpressureVictimId == member.Id ? member.Id : 0;
            state.RandomState = group.RandomState;
            fluidTankOwners.Remove(member.Id);
            fluidInventories.Remove(member.Id.ToString(CultureInfo.InvariantCulture) + ":main");
        }
        fluidTankGroups.Remove(group);
        group.Inventory.Restore(new FluidInventoryState { RandomState = original.RandomState });
        group.Members.Clear(); group.TotalVolume = 0d; group.MinimumGasSpace = 0d; group.Active = false;
    }
    #endregion

    private static void ResetFluidRuntime()
    {
        fluidGraph = null; fluidStates.Clear(); fluidInventories.Clear(); fluidTankOwners.Clear(); fluidTankGroups.Clear();
        fluidStepStock.Clear(); fluidThroughputRemaining.Clear(); fluidMechanicalPower.Clear();
        fluidAppearance.Clear();
        combustionSources.Clear();
        combustionSourceSnapshot = Array.Empty<CombustionSupportLease>();
        combustionSourcesDirty = true;
    }

    private static void ApplyFluidRemoteState(MachineEntity node, bool logicApplied)
    {
        if (node.Definition.Fluid == null) return;
        fluidStates[node.Id] = logicApplied && node.Logic is FluidMachineLogic logic ? logic.State :
            MachinePersistence.Read<FluidMachineState>(node.Snapshot, "fluid") ?? new FluidMachineState();
        fluidInventories.Clear(); fluidTankOwners.Clear(); fluidTankGroups.Clear(); fluidGraph = null;
    }
    #endregion

    #region 整网活动与每轮库存预算
    private static void BeginFluidStep(float seconds)
    {
        if (fluidGraph == null) return;
        foreach (FluidNetworkGraph.Component component in fluidGraph.Components)
        {
            bool near = false;
            int range = component.Active ? MachineCatalog.Settings.DeactivationChunks : MachineCatalog.Settings.ActivationChunks;
            foreach (MachineEntity node in component.Nodes)
            foreach (Vector2Int player in players) near |= graph.IsNear(node.SimulationBounds, player, range);
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
        foreach (MachineEntity node in nodes.Values)
        {
            if (node.Definition.Fluid == null || !IsFluidActive(node)) continue;
            FluidMachineDefinition definition = node.Definition.Fluid;
            foreach (FluidMachinePortDefinition port in definition.Ports) RememberFluidBudget(node, port.Chamber, seconds);
            RememberFluidBudget(node, "main", seconds);
            GetFluidDeviceBehavior(node).BeginStep(node, seconds);
        }
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
        fluidStepStock.Add(inventory, inventory.State.Clone());
        double capacity = node.Definition.Fluid.ThroughputLitersPerSecond * seconds;
        if (fluidTankOwners.TryGetValue(node.Id, out var group)) capacity *= group.Members.Count;
        fluidThroughputRemaining.Add(inventory, capacity);
    }

    private static void CompleteFluidStep(float seconds)
    {
        if (fluidGraph == null) return;
        int count = fluidGraph.Edges.Count;
        int start = count == 0 ? 0 : (int)(TransportStep % (uint)count);
        for (int index = 0; index < count; index++)
        {
            FluidNetworkGraph.Edge edge = fluidGraph.Edges[(start + index) % count];
            if (!IsFluidActive(edge.Source) || edge.Target != null && !IsFluidActive(edge.Target) || !fluidGraph.CanRoute(edge)) continue;
            TryMoveFluidEdge(edge, seconds);
        }
        foreach (MachineEntity node in nodes.Values)
        {
            if (node.Definition.Fluid == null || !IsFluidActive(node) || GetFluidDeviceBehavior(node).IsSharedStorage) continue;
            foreach (string chamber in GetFluidState(node).Chambers.Keys)
            {
                FluidPhaseChangeResult phase = FluidThermodynamics.AdvancePhaseChange(GetFluidInventory(node, chamber), GetFluidVolumeLiters(node),
                    GetFluidMinimumGasSpaceLiters(node), seconds, node.Definition.Fluid.MaximumPhaseMolesPerSecond,
                    node.Definition.Fluid.SingleGas, node.Definition.Fluid.SingleLiquid);
                UpdateFluidPhaseWarning(node, chamber, phase);
                if (phase.ConvertedMoles > 0) StateChanged(node);
            }
        }
        AdvanceFluidTankPressure(seconds);
        SaveFluidGroups();
        foreach (MachineEntity node in nodes.Values)
        {
            if (node.Definition.Fluid == null || !IsFluidActive(node)) continue;
            FluidMachineState state = GetFluidState(node); FluidInventory contents = GetFluidInventory(node);
            double gasBuffer = node.Definition.Fluid.GasBufferStandardLiters * GetFluidVolumeLiters(node) / node.Definition.Fluid.VolumeLiters;
            int gas = (int)Math.Clamp(FluidUnits.MolToStandardLiters(contents.GasMoles) / (decimal)gasBuffer * 32, 0, 32);
            int liquid = Math.Clamp((int)(contents.GetLiquidLiters() / GetFluidVolumeLiters(node) * 32), 0, 32);
            int appearance = HashCode.Combine(gas, liquid, state.LastFlowDirection, state.LastFlowPhase, state.BlockedPorts.Count > 0,
                state.ProbeConnected, state.ValveOpen, contents.GetPressureKPa(GetFluidVolumeLiters(node), GetFluidMinimumGasSpaceLiters(node)) > node.Definition.Fluid.MaxSafePressureKPa);
            if (fluidAppearance.TryGetValue(node.Id, out int previous) && previous == appearance) continue;
            fluidAppearance[node.Id] = appearance; NotifyVisualChanged(node);
        }
    }
    internal static void UpdateFluidPhaseWarning(MachineEntity node, string chamber, FluidPhaseChangeResult result)
    {
        string warning = result.BlockedReason;
        if (result.UnsupportedFluidIds?.Count > 0)
            warning = "超出首轮相变曲线，保留真实相态：" + string.Join(", ", result.UnsupportedFluidIds);
        if (string.IsNullOrEmpty(warning)) GetFluidState(node).PhaseWarnings.Remove(chamber);
        else GetFluidState(node).PhaseWarnings[chamber] = warning;
    }
    #endregion
}
