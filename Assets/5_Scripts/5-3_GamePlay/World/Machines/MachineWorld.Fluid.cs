using System;
using System.Collections.Generic;
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

    private static FluidNetworkGraph fluidGraph;
    private static readonly Dictionary<int, FluidMachineState> fluidStates = new();
    private static readonly Dictionary<string, FluidInventory> fluidInventories = new(StringComparer.Ordinal);
    private static readonly Dictionary<int, FluidTankGroup> fluidTankOwners = new();
    private static readonly List<FluidTankGroup> fluidTankGroups = new();
    private static readonly Dictionary<FluidInventory, FluidInventoryState> fluidStepStock = new();
    private static readonly Dictionary<FluidInventory, double> fluidThroughputRemaining = new();
    private static readonly Dictionary<MachineEntity, double> fluidEnginePower = new();
    private static readonly Dictionary<int, int> fluidAppearance = new();

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

    private static void RebuildFluidNetworks()
    {
        SaveFluidGroups();
        fluidTankOwners.Clear(); fluidTankGroups.Clear();
        var sorted = new List<MachineEntity>(nodes.Values); sorted.Sort((a, b) => a.Id.CompareTo(b.Id));
        var tankCells = new Dictionary<Vector2Int, MachineEntity>();
        foreach (MachineEntity node in sorted)
            if (node.Definition.Fluid?.CombineAdjacent == true) tankCells.Add(graph.NormalizeCell(node.Cell), node);
        var queue = new Queue<MachineEntity>();
        foreach (MachineEntity root in sorted)
        {
            if (root.Definition.Fluid?.Kind != "tank" || fluidTankOwners.ContainsKey(root.Id)) continue;
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

    private static void ResetFluidRuntime()
    {
        fluidGraph = null; fluidStates.Clear(); fluidInventories.Clear(); fluidTankOwners.Clear(); fluidTankGroups.Clear();
        fluidStepStock.Clear(); fluidThroughputRemaining.Clear(); fluidEnginePower.Clear();
        fluidAppearance.Clear();
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
        fluidStepStock.Clear(); fluidThroughputRemaining.Clear(); fluidEnginePower.Clear();
        foreach (MachineEntity node in nodes.Values)
        {
            if (node.Definition.Fluid == null || !IsFluidActive(node)) continue;
            FluidMachineDefinition definition = node.Definition.Fluid;
            foreach (FluidMachinePortDefinition port in definition.Ports) RememberFluidBudget(node, port.Chamber, seconds);
            RememberFluidBudget(node, "main", seconds);
            if (definition.Kind == "engine") fluidEnginePower[node] = CalculateAvailableEnginePower(node, seconds);
        }
        UpdatePressureProbes();
    }

    private static bool IsFluidActive(MachineEntity node)
        => fluidGraph != null && fluidGraph.NodeComponents.TryGetValue(node.Id, out var component) && component.Active;
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
            if (node.Definition.Fluid == null || !IsFluidActive(node) || node.Definition.Fluid.Kind == "tank") continue;
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
            int gas = (int)Math.Clamp(FluidUnits.MolToStandardLiters(contents.GasMoles) / (decimal)node.Definition.Fluid.GasBufferStandardLiters * 32, 0, 32);
            int liquid = Math.Clamp((int)(contents.GetLiquidLiters() / GetFluidVolumeLiters(node) * 32), 0, 32);
            int appearance = HashCode.Combine(gas, liquid, state.LastFlowDirection, state.LastFlowPhase, state.BlockedPorts.Count > 0,
                state.ProbeConnected, state.ValveOpen, contents.GetPressureKPa(GetFluidVolumeLiters(node), GetFluidMinimumGasSpaceLiters(node)) > node.Definition.Fluid.MaxSafePressureKPa);
            if (fluidAppearance.TryGetValue(node.Id, out int previous) && previous == appearance) continue;
            fluidAppearance[node.Id] = appearance; NotifyVisualChanged(node);
        }
    }
    private static void UpdateFluidPhaseWarning(MachineEntity node, string chamber, FluidPhaseChangeResult result)
    {
        string warning = result.BlockedReason;
        if (result.UnsupportedFluidIds?.Count > 0)
            warning = "超出首轮相变曲线，保留真实相态：" + string.Join(", ", result.UnsupportedFluidIds);
        if (string.IsNullOrEmpty(warning)) GetFluidState(node).PhaseWarnings.Remove(chamber);
        else GetFluidState(node).PhaseWarnings[chamber] = warning;
    }
    #endregion
}
