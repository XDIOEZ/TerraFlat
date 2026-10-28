using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using FlatWorld.Spaceflight;
using static MachineWorld;

/// <summary>设备按真实来源、固定容量与每秒速率提交物质事务。</summary>
public static class FluidDeviceOperations
{
    #region 设备工作区和通用边界
    private sealed class Workspace
    {
        public readonly FluidNetworkGraph.SupplyWorkspace Routes = new();
        public readonly HashSet<FluidInventory> Visited = new();
        public readonly Dictionary<FluidInventory, double> Budgets = new();
        public readonly Dictionary<(FluidInventory, string), decimal> Stock = new();
        public readonly List<Withdrawal> Plan = new();
        public readonly Dictionary<FluidInventory, FluidInventoryState> Before = new();
        public readonly List<FluidNetworkGraph.Edge> LiquidPumpBudgetPath = new();
        public readonly string[] LiquidPumpFluidIds = new string[1];
        public FluidInventory LiquidPumpBudgetReceiver;
        public string LiquidPumpBudgetFluidId;
        public double ReservedExtent;
    }
    private sealed class Withdrawal
    {
        public FluidInventory Inventory;
        public FluidNetworkGraph.SupplyWorkspace Route;
        public int Index;
        public FluidRequirement Requirement;
        public string Reservation;
    }
    private static readonly ConditionalWeakTable<MachineEntity, Workspace> workspaces = new();
    private static readonly string[] waterIds = { "core:pure_water", "core:filtered_water", "core:dirty_water",
        "core:drinkable_water", "core:salt_water", "core:sea_water" };
    private static bool Powered(MachineEntity node) => node.Definition.Electrical?.IsConsumer == true
        ? node.ElectricalSuppliedWatts > 0f : node.Definition.HasMechanicalPorts && node.LoadSatisfied && node.SpeedRpm > 0f;
    private static double Rate(MachineEntity node, float seconds) => seconds > 0 && float.IsFinite(seconds)
        ? Math.Max(0d, node.Definition.Fluid.ThroughputLitersPerSecond * seconds) : 0d;
    private static string Filter(FluidNetworkGraph.SupplyWorkspace routes, int index) => GetFluidRouteFilter(routes, index);
    private static double GrossFactor(FluidInventory source, string filter) => string.IsNullOrEmpty(filter) ? 1d :
        source.GetGasMoles(filter) > 0 ? (double)(source.GasMoles / source.GetGasMoles(filter)) : 1d;
    private static List<FluidRequirement> Difference(FluidInventoryState before, FluidInventory after, FluidPhase phase)
    {
        var result = new List<FluidRequirement>();
        foreach (var c in before.Components)
        {
            decimal amount = (phase == FluidPhase.Gas ? c.GasMoles - after.GetGasMoles(c.FluidId) : c.LiquidMoles - after.GetLiquidMoles(c.FluidId));
            if (amount > 0m) result.Add(new FluidRequirement(c.FluidId, phase, amount));
        }
        return result;
    }
    #endregion

    #region 动力泵直达真实储罐
    public static bool IsGasPumpWet(MachineEntity node) => IsFluidEnvironmentSubmerged(node);
    public static void AdvanceGasPump(MachineEntity node, float seconds)
    {
        var state = GetFluidState(node); double maximum = Rate(node, seconds);
        state.LastFlowPhase = ""; // 仅真实交付成功的本轮抽气驱动机械反馈。
        if (maximum <= 0d) return;
        if (!Powered(node)) { state.Status = "没有动力"; return; }
        if (IsGasPumpWet(node)) { state.Status = "气泵遇到液体"; return; }
        var workspace = workspaces.GetOrCreateValue(node); bool destination = false;
        foreach (var id in FluidCatalog.Default.Definitions.Keys)
        {
            CollectFluidDeliveryRoutes(node, "main", FluidPhase.Gas, new[] { id }, workspace.Routes);
            for (int i = 1; i < workspace.Routes.Nodes.Count; i++)
            {
                var route = workspace.Routes.Nodes[i];
                if (GetIndustrialFreeCapacity(route.Node, FluidPhase.Gas, id, route.Chamber) > 0 &&
                    GetFluidSupplyStandardLiters(workspace.Routes, i, seconds, true) > 0) { destination = true; break; }
            }
            if (destination) break;
        }
        if (!destination) { state.Status = "没有可接收气体的储罐"; return; }
        if (!TryGetFluidAirSource(node, out IShipAtmosphereSource source)) { state.Status = "没有可抽取的大气"; return; }
        var pending = new FluidInventory();
        IReadOnlyList<FluidBatch> batches = source.TakeGas(FluidUnits.StandardLitersToMol((decimal)maximum));
        int received = 0;
        double moved = 0d;
        try
        {
            for (; received < batches.Count; received++)
                if (!pending.TryAdd(batches[received], 1d, .001d)) source.ReleaseGas(batches[received]);
            var ids = new List<string>(); foreach (var c in pending.State.Components) if (c.GasMoles > 0m) ids.Add(c.FluidId);
            workspace.Visited.Clear();
            foreach (var id in ids)
            {
                CollectFluidDeliveryRoutes(node, "main", FluidPhase.Gas, new[] { id }, workspace.Routes);
                for (int i = 1; i < workspace.Routes.Nodes.Count; i++)
                {
                    var route = workspace.Routes.Nodes[i]; var target = GetFluidInventory(route.Node, route.Chamber);
                    if (GetIndustrialFreeCapacity(route.Node, FluidPhase.Gas, id, route.Chamber) <= 0 || workspace.Visited.Contains(target)) continue;
                    double budget = Math.Min(maximum - moved, GetFluidSupplyStandardLiters(workspace.Routes, i, seconds, true));
                    string filter = Filter(workspace.Routes, i); double grossFactor = GrossFactor(pending, filter);
                    double actual = IndustrialFluidTransfer.Transfer(pending, target, FluidPhase.Gas, budget,
                        MachineWorld.GetFluidGasCapacityStandardLiters(route.Node), MachineWorld.GetFluidVolumeLiters(route.Node), Filter(workspace.Routes, i));
                    if (actual <= 0) continue;
                    workspace.Visited.Add(target);
                    ConsumeFluidRouteBudget(workspace.Routes, i, actual * grossFactor); StateChanged(route.Node); moved += actual;
                }
            }
        }
        finally
        {
            for (; received < batches.Count; received++) source.ReleaseGas(batches[received]);
            foreach (var remainder in pending.Drain()) source.ReleaseGas(remainder);
        }
        state.LastFlowPhase = moved > 0d ? "gas" : "";
        state.Status = moved > 0 ? "正在抽气" : "来源不足或目标不能接收";
    }

    public static void AdvanceLiquidPump(MachineEntity node, float seconds)
    {
        var state = GetFluidState(node); var workspace = workspaces.GetOrCreateValue(node);
        double remaining = Rate(node, seconds);
        if (remaining <= 0 || !Powered(node))
        { ResetLiquidPumpBatchCredit(state, workspace); state.Status = "没有动力或流量关闭"; return; }
        const double batchLiters = FlatWorld.WorldModel.LiquidCellValue.LitersPerUnit;
        bool movedAny = false;
        while (remaining > 1e-9)
        {
            Vector2 intake = (Vector2)(node.Cell + MechanicalNetworkGraph.Directions[(node.RotationQuarterTurns + 2) & 3]) + Vector2.one * .5f;
            if (!TrySelectFluidEnvironmentPumpSource(intake, out string world, out Vector2 position, out LiquidDefinition liquid,
                out int level, out WorldLiquidPumpSourceSelection sourceSelection) || level <= 0 ||
                !FluidCatalog.Default.TryFromLiquidId(liquid.Id, out FluidDefinition fluid) || !fluid.AllowIndustrialTransport)
            {
                ResetLiquidPumpBatchCredit(state, workspace);
                state.Status = movedAny ? "正在抽液体" : sourceSelection == WorldLiquidPumpSourceSelection.Searching
                    ? "正在寻找可抽取的液体" : "入口没有可抽取的液体"; return;
            }
            workspace.LiquidPumpFluidIds[0] = fluid.Id;
            CollectFluidDeliveryRoutes(node, "main", FluidPhase.Liquid, workspace.LiquidPumpFluidIds, workspace.Routes);
            int destination = -1;
            for (int i = 1; i < workspace.Routes.Nodes.Count; i++)
            {
                var route = workspace.Routes.Nodes[i];
                if (!GetFluidDeviceBehavior(route.Node).IsTransit &&
                    GetIndustrialFreeCapacity(route.Node, FluidPhase.Liquid, fluid.Id, route.Chamber) + 1e-9 >= batchLiters &&
                    GetFluidSupplyStandardLiters(workspace.Routes, i, seconds, true) > 1e-9) { destination = i; break; }
            }
            if (destination < 0)
            { ResetLiquidPumpBatchCredit(state, workspace); state.Status = movedAny ? "正在抽液体" : "液网不能接收完整十升"; return; }
            var receiver = workspace.Routes.Nodes[destination];
            var target = GetFluidInventory(receiver.Node, receiver.Chamber);
            BindLiquidPumpBatchCreditPath(state, workspace, target, fluid.Id, destination);
            // 这里只预付路径本轮吞吐额度，累计满十升之前不从地表扣任何水。
            double credit = Math.Min(remaining, Math.Min(batchLiters - state.WorldLiquidPumpLiters,
                GetFluidSupplyStandardLiters(workspace.Routes, destination, seconds, true)));
            if (credit <= 1e-9) break;
            ConsumeFluidRouteBudget(workspace.Routes, destination, credit);
            state.WorldLiquidPumpLiters += credit; remaining -= credit;
            if (state.WorldLiquidPumpLiters + 1e-9 < batchLiters)
            { state.Status = "累计十升抽水量"; StateChanged(node); return; }

            int reserve = sourceSelection == WorldLiquidPumpSourceSelection.Surplus ? 1 : 0;
            if (!TryPumpFluidEnvironmentLiquid(world, position, 1, reserve, out string actualId, out int removed) || removed != 1)
            { ResetLiquidPumpBatchCredit(state, workspace); state.Status = "来源暂时不能提供十升"; return; }
            var buffer = GetFluidInventory(node);
            FluidInventoryState bufferBefore = buffer.State.Clone(), targetBefore = target.State.Clone();
            try
            {
                if (actualId != liquid.Id || !buffer.TryAdd(FluidInventory.CreateBatch(fluid, 0m,
                    FluidUnits.LiquidLitersToMol(fluid, (decimal)batchLiters), FluidUnits.ReferenceTemperatureKelvin),
                    Math.Max(GetFluidVolumeLiters(node), batchLiters) + .001d, .001d, false, true) ||
                    IndustrialFluidTransfer.Transfer(buffer, target, FluidPhase.Liquid, batchLiters,
                        GetFluidGasCapacityStandardLiters(receiver.Node), GetFluidVolumeLiters(receiver.Node), fluid.Id) + 1e-9 < batchLiters)
                    throw new InvalidOperationException("液泵十升整批接收被拒绝。");
            }
            catch
            {
                buffer.Restore(bufferBefore); target.Restore(targetBefore);
                ResetLiquidPumpBatchCredit(state, workspace);
                if (ReleaseFluidEnvironmentLiquid(world, position, actualId, removed) != removed)
                    throw new InvalidOperationException("液泵失败回源未能恢复地表液体。");
                state.Status = "十升批次接收失败，来源已恢复"; return;
            }
            state.WorldLiquidPumpLiters = 0d; movedAny = true;
            StateChanged(receiver.Node); StateChanged(node);
        }
        state.Status = movedAny ? "正在抽液体" : "累计十升抽水量";
    }

    private static void ResetLiquidPumpBatchCredit(FluidMachineState state, Workspace workspace)
    {
        state.WorldLiquidPumpLiters = 0d; workspace.LiquidPumpBudgetReceiver = null;
        workspace.LiquidPumpBudgetFluidId = null; workspace.LiquidPumpBudgetPath.Clear();
    }
    private static void BindLiquidPumpBatchCreditPath(FluidMachineState state, Workspace workspace, FluidInventory receiver,
        string fluidId, int destination)
    {
        bool same = ReferenceEquals(workspace.LiquidPumpBudgetReceiver, receiver) && workspace.LiquidPumpBudgetFluidId == fluidId;
        int length = 0;
        for (int i = destination; i >= 0; i = workspace.Routes.Nodes[i].DownstreamIndex)
        {
            FluidNetworkGraph.Edge edge = workspace.Routes.Nodes[i].Boundary;
            if (edge == null) continue;
            if (same && length < workspace.LiquidPumpBudgetPath.Count)
            {
                FluidNetworkGraph.Edge saved = workspace.LiquidPumpBudgetPath[length];
                same = ReferenceEquals(saved.Source, edge.Source) && ReferenceEquals(saved.Target, edge.Target) &&
                    ReferenceEquals(saved.SourcePort, edge.SourcePort) && ReferenceEquals(saved.TargetPort, edge.TargetPort) && saved.Direction == edge.Direction;
            }
            else same = false;
            length++;
        }
        if (same && length == workspace.LiquidPumpBudgetPath.Count) return;
        ResetLiquidPumpBatchCredit(state, workspace);
        workspace.LiquidPumpBudgetReceiver = receiver; workspace.LiquidPumpBudgetFluidId = fluidId;
        for (int i = destination; i >= 0; i = workspace.Routes.Nodes[i].DownstreamIndex)
            if (workspace.Routes.Nodes[i].Boundary != null) workspace.LiquidPumpBudgetPath.Add(workspace.Routes.Nodes[i].Boundary);
    }

    internal static string GetLiquidPumpTransferFailureStatus(ContainerTransferFailure failure, FluidInventory receiver = null,
        FluidDefinition incoming = null, bool singleLiquid = false) => failure == ContainerTransferFailure.Full
            ? "液体接收空间不足或液体种类冲突" : "液体转移被拒绝，来源已保留";
    #endregion

    #region 三槽真实容器装填
    public static void FillContainers(MachineEntity node, float seconds, bool liquid)
    {
        if (node.Logic is not FluidMachineLogic logic || logic.PortableTank == null) return;
        decimal remaining = (decimal)Rate(node, seconds); if (remaining <= 0) return;
        var workspace = workspaces.GetOrCreateValue(node); FluidPhase phase = liquid ? FluidPhase.Liquid : FluidPhase.Gas;
        bool changed = false;
        foreach (var slot in logic.PortableTank.Data.itemSlots)
        {
            ItemData item = slot?.itemData; if (item == null || item.Stack.Amount != 1 || remaining <= 0) continue;
            FluidInventory target = null; FluidTankConfiguration config = null; LiquidVesselTransferPort liquidTarget = null;
            ColdInventoryLiquidVessel vessel = null;
            if (!liquid)
            { if (!FluidTankStorage.TryGet(item, out target, out config)) continue; }
            else
            {
                if (!Mod_WaterVessel.TryRead(item, out _, out _)) continue;
                vessel = new ColdInventoryLiquidVessel(item, logic.PortableTank, null);
                liquidTarget = new LiquidVesselTransferPort(vessel);
            }
            workspace.Visited.Clear();
            foreach (string id in FluidCatalog.Default.Definitions.Keys)
            {
                CollectFluidSupplyRoutes(node, "main", phase, new[] { id }, workspace.Routes);
                for (int i = 0; i < workspace.Routes.Nodes.Count && remaining > 0; i++)
                {
                    var route = workspace.Routes.Nodes[i]; var source = GetFluidInventory(route.Node, route.Chamber);
                    if ((liquid ? source.LiquidMoles : source.GasMoles) <= 0 || workspace.Visited.Contains(source)) continue;
                    double budget = i == 0 ? (double)remaining : Math.Min((double)remaining, GetFluidSupplyStandardLiters(workspace.Routes, i, seconds, true));
                    FluidInventoryState before = source.State.Clone(); double actual = 0d;

                    if (!liquid)
                    {
                        actual = IndustrialFluidTransfer.Transfer(source, target, phase, budget, config.GasCapacityStandardLiters,
                            config.VolumeLiters, Filter(workspace.Routes, i));
                        if (actual > 0) { FluidTankStorage.Write(item, target); logic.PortableTank.Data.NotifyItemStateChanged(item); }
                    }
                    else if (route.Node.Logic is FluidMachineLogic sourceLogic)
                    {
                        var port = new IndustrialLiquidTransferPort(sourceLogic, new FluidMachinePortDefinition
                            { Id = "filler-source", Chamber = route.Chamber, Mode = "output", Phase = "liquid" });
                        object targetBefore = liquidTarget.CaptureState();
                        try
                        {
                            if (port.ReserveLiquid(out var batch) && FluidCatalog.Default.TryFromLiquidId(batch.LiquidId, out var fluid))
                            {
                                bool compatible = true;
                                foreach (var pair in vessel.Data.Composition)
                                    if (pair.Value > 0 && pair.Key != batch.LiquidId) compatible = false;
                                float amount = compatible ? Math.Min(batch.Servings, liquidTarget.GetReceivableServings(batch.LiquidId,
                                    (float)FluidUnits.MolToServings(fluid, FluidUnits.LiquidLitersToMol(fluid, (decimal)budget)))) : 0;
                                if (amount > 0 && port.ExtractLiquid(batch, amount) && liquidTarget.InsertLiquid(batch, amount))
                                { actual = (double)fluid.LitersPerServing * amount; liquidTarget.PublishState(); }
                            }
                            if (actual <= 0) { source.Restore(before); liquidTarget.RestoreState(targetBefore); }
                        }
                        catch { source.Restore(before); liquidTarget.RestoreState(targetBefore); liquidTarget.PublishState(); throw; }
                    }
                    if (actual <= 0) continue;
                    workspace.Visited.Add(source);
                    if (i == 0) StateChanged(node);
                    else ConsumeFluidSupplyBudget(workspace.Routes, i, Difference(before, source, phase), GetFluidRouteCharge(workspace.Routes, i, before, actual));
                    remaining -= (decimal)actual; changed = true;
                }
            }
        }
        if (changed) { logic.PortableTank.Save(); GetFluidState(node).Status = "正在按槽位顺序装填"; }
        else GetFluidState(node).Status = "没有合适容器、来源或剩余容量";
    }
    #endregion

    #region 引擎按需求原子取料
    private static FluidMachineReactionDefinition EngineReaction(MachineEntity node)
        => node.Definition.Fluid.Reactions.Length > 0 ? node.Definition.Fluid.Reactions[0] : null;
    private static bool PlanEngine(MachineEntity node, double extent, double seconds, bool useBudget, Workspace workspace)
    {
        workspace.Plan.Clear(); workspace.Stock.Clear(); workspace.Budgets.Clear();
        var reaction = EngineReaction(node); if (reaction == null) return false;
        foreach (var input in reaction.Inputs)
        {
            FluidPhase phase = input.Phase == "gas" ? FluidPhase.Gas : FluidPhase.Liquid;
            decimal remaining = (decimal)(extent * input.Moles);
            string[] ids = node.Definition.Fluid.Kind == "water-engine" ? waterIds : new[] { input.FluidId };
            foreach (var id in ids)
            {
                var routes = new FluidNetworkGraph.SupplyWorkspace();
                CollectFluidSupplyRoutes(node, input.Chamber, phase, new[] { id }, routes);
                for (int i = 0; i < routes.Nodes.Count && remaining > 0; i++)
                {
                    var route = routes.Nodes[i]; var inventory = GetFluidInventory(route.Node, route.Chamber);
                    var key = (inventory, id);
                    double budget = GetFluidSupplyStandardLiters(routes, i, seconds, useBudget, workspace.Budgets);
                    decimal capacity = phase == FluidPhase.Gas ? FluidUnits.StandardLitersToMol((decimal)budget) :
                        FluidUnits.LiquidLitersToMol(FluidCatalog.Default.Find(id), (decimal)budget);
                    if (!workspace.Stock.TryGetValue(key, out decimal stock))
                        stock = useBudget ? GetFluidSupplyStock(routes, i, id, phase) :
                            phase == FluidPhase.Gas && inventory.SuppliesInfiniteGas(id) ? capacity : inventory.GetAvailableMoles(id, phase);
                    decimal amount = Math.Min(remaining, Math.Min(stock, capacity)); if (amount <= 0) continue;
                    workspace.Plan.Add(new Withdrawal { Inventory = inventory, Route = routes, Index = i,
                        Requirement = new FluidRequirement(id, phase, amount) });
                    workspace.Stock[key] = stock - amount; remaining -= amount;
                    double liters = (double)(phase == FluidPhase.Gas ? FluidUnits.MolToStandardLiters(amount) :
                        FluidUnits.MolToLiquidLiters(FluidCatalog.Default.Find(id), amount));
                    ReserveFluidSupplyProjection(routes, i, liters, workspace.Budgets);
                }
                if (remaining <= 0) break;
            }
            if (remaining > .000000000000000001m) return false;
        }
        foreach (var output in reaction.Outputs)
        {
            double liters = (double)(output.Phase == "gas" ? FluidUnits.MolToStandardLiters((decimal)(extent * output.Moles)) :
                FluidUnits.MolToLiquidLiters(FluidCatalog.Default.Find(output.FluidId), (decimal)(extent * output.Moles)));
            if (GetIndustrialFreeCapacity(node, output.Phase == "gas" ? FluidPhase.Gas : FluidPhase.Liquid, output.FluidId, output.Chamber) + 1e-12 < liters) return false;
        }
        return true;
    }

    public static double CalculateAvailableEnginePower(MachineEntity node, float seconds)
    {
        var reaction = EngineReaction(node); if (reaction == null || seconds <= 0) return 0;
        if (!HasPotentialMechanicalLoad(node)) { ReleaseReservations(node); return 0; }
        double joules = reaction.ChemicalJoulesPerReaction * reaction.MechanicalEfficiency;
        if (joules <= 0) return 0;
        double maximumPower = node.Definition.Torque * node.Definition.Rpm * MachineCatalog.Settings.WattsPerTorqueRpm;
        double extent = Math.Min(reaction.MolesPerSecond * seconds, maximumPower * seconds / joules);
        MachineSolidFuelBurner burner = (node.Logic as FluidMachineLogic)?.SolidFuelBurner;
        if (burner != null)
        {
            extent = Math.Min(extent, burner.AvailableFullLoadSeconds * maximumPower / joules);
            if (extent <= 0) { ReleaseReservations(node); return 0; }
        }
        var workspace = workspaces.GetOrCreateValue(node);
        ReleaseEngine(workspace);
        for (int attempt = 0; attempt < 13; attempt++, extent *= .5)
        {
            if (!PlanEngine(node, extent, seconds, true, workspace)) continue;
            bool reserved = true;
            for (int i = 0; i < workspace.Plan.Count; i++)
            {
                var entry = workspace.Plan[i]; var need = entry.Requirement;
                entry.Reservation = "engine:" + node.Id + ":" + i;
                if (!entry.Inventory.TryReserve(entry.Reservation, need.Phase, need.Moles, id => id == need.FluidId, out var batch) || batch.TotalMoles < need.Moles)
                { reserved = false; break; }
            }
            if (!reserved) { ReleaseEngine(workspace); continue; }
            foreach (var entry in workspace.Plan)
            {
                var need = entry.Requirement;
                double liters = (double)(need.Phase == FluidPhase.Gas ? FluidUnits.MolToStandardLiters(need.Moles) :
                    FluidUnits.MolToLiquidLiters(FluidCatalog.Default.Find(need.FluidId), need.Moles));
                ConsumeFluidRouteBudget(entry.Route, entry.Index, liters);
            }
            workspace.ReservedExtent = extent;
            return extent * joules / seconds;
        }
        workspace.Plan.Clear(); return 0;
    }

    private static void ReleaseEngine(Workspace workspace)
    {
        foreach (var entry in workspace.Plan)
            if (entry.Reservation != null) entry.Inventory.ReleaseReservation(entry.Reservation);
        workspace.ReservedExtent = 0;
    }
    public static void ReleaseReservations(MachineEntity node)
    {
        if (node != null && workspaces.TryGetValue(node, out Workspace workspace)) ReleaseEngine(workspace);
    }

    public static void SettleFluidEngine(MachineEntity node, float seconds)
    {
        var workspace = workspaces.GetOrCreateValue(node);
        if (!TryGetFluidMechanicalPower(node, out double maximum) || maximum <= 0 || node.SpeedRpm <= 0 || node.Network?.TorqueDemand <= 0)
        { ReleaseEngine(workspace); GetFluidState(node).Status = "没有原料或有效负载"; return; }
        double torque = Math.Floor(node.SourceTorque * node.SourceFactor / 10) * 10;
        double used = Math.Max(0, torque - node.RemainingSourceTorque * node.TorqueRatio);
        double work = Math.Min(maximum * seconds, used * node.SpeedRpm * MachineCatalog.Settings.WattsPerTorqueRpm * seconds);
        if (work <= 0) { ReleaseEngine(workspace); GetFluidState(node).Status = "没有有效负载"; return; }
        var reaction = EngineReaction(node);
        MachineSolidFuelBurner burner = (node.Logic as FluidMachineLogic)?.SolidFuelBurner;
        double maximumPower = node.Definition.Torque * node.Definition.Rpm * MachineCatalog.Settings.WattsPerTorqueRpm;
        double fullLoadSeconds = maximumPower > 0 ? work / maximumPower : 0;
        if (burner != null && (fullLoadSeconds <= 0 || fullLoadSeconds > burner.AvailableFullLoadSeconds + 1e-9))
        { ReleaseEngine(workspace); GetFluidState(node).Status = "燃料不足或已经熄火"; return; }
        double extent = work / (reaction.ChemicalJoulesPerReaction * reaction.MechanicalEfficiency);
        if (workspace.ReservedExtent <= 0 || extent > workspace.ReservedExtent + 1e-12)
            throw new InvalidOperationException("引擎实际做功超过已预约原料。");
        decimal fraction = (decimal)Math.Min(1d, extent / workspace.ReservedExtent);
        workspace.Before.Clear();
        try
        {
            foreach (var entry in workspace.Plan)
            {
                if (!workspace.Before.ContainsKey(entry.Inventory)) workspace.Before.Add(entry.Inventory, entry.Inventory.State.Clone());
                var need = entry.Requirement;
                if (!entry.Inventory.TryTakeReserved(entry.Reservation, need.Moles * fraction, out _)) throw new InvalidOperationException("引擎原料提交被拒绝。");
            }
            foreach (var output in reaction.Outputs)
            {
                var inventory = GetFluidInventory(node, output.Chamber);
                if (!workspace.Before.ContainsKey(inventory)) workspace.Before.Add(inventory, inventory.State.Clone());
                decimal amount = (decimal)(extent * output.Moles);
                var batch = FluidInventory.CreateBatch(FluidCatalog.Default.Find(output.FluidId), output.Phase == "gas" ? amount : 0,
                    output.Phase == "liquid" ? amount : 0, FluidUnits.ReferenceTemperatureKelvin);
                if (!inventory.TryAdd(batch, node.Definition.Fluid.VolumeLiters + .001d, .001d, false, true)) throw new InvalidOperationException("引擎产物提交被拒绝。");
            }
        }
        catch { foreach (var before in workspace.Before) before.Key.Restore(before.Value); throw; }
        finally { ReleaseEngine(workspace); }
        burner?.ConsumeWork(fullLoadSeconds);
        if (burner != null) StateChanged(node);
        foreach (var entry in workspace.Plan) StateChanged(entry.Route.Nodes[entry.Index].Node);
        GetFluidState(node).Status = "正在消耗原料供能";
    }
    #endregion
}
