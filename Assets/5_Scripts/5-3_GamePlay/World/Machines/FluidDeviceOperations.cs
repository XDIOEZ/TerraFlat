using System;
using System.Collections.Generic;
using UnityEngine;
using static MachineWorld;

/// <summary>设备加工各自调用这里的事务，统一管网不识别设备种类。</summary>
public static class FluidDeviceOperations
{
    #region 流体设备实际动力与取料
    private static double AvailableDeviceWork(MachineEntity node, double seconds)
    {
        if (node.Definition.Electrical?.IsConsumer == true) return Math.Max(0, node.ElectricalSuppliedWatts) * seconds;
        if (!node.Definition.HasMechanicalPorts || !node.LoadSatisfied || node.SpeedRpm <= 0) return 0;
        return MechanicalNetworkGraph.RoundLoadTorqueToTens(node.LoadTorque) * node.SpeedRpm *
            MachineCatalog.Settings.WattsPerTorqueRpm * seconds;
    }
    private static double AmbientTemperatureKelvin(MachineEntity node)
        => TemperatureMgr.Instance != null && TemperatureMgr.Instance.TryGetAmbientTemperature(node.Position, out float celsius)
            ? Math.Max(.001, celsius + 273.15) : FluidUnits.ReferenceTemperatureKelvin;

    public static void AdvanceGasPump(MachineEntity node, float seconds)
    {
        FluidMachineDefinition definition = node.Definition.Fluid;
        FluidMachineState state = GetFluidState(node);
        FluidInventory buffer = GetFluidInventory(node);
        if (IsGasPumpWet(node)) { state.Status = "气泵遇到液体"; return; }
        if (!buffer.IsEmpty) { state.Status = "等待缓冲气包送完"; return; }
        double available = AvailableDeviceWork(node, seconds);
        if (available <= 0) { state.Status = node.SpeedRpm > 0 ? "动力不够" : "没有动力"; return; }
        AtmosphereState atmosphere = GetCurrentAtmosphere();
        if (atmosphere == null || atmosphere.CurrentStandardLiters <= 0) { state.Status = "没有可抽取的大气"; return; }
        if (!GasPumpCanOutput(node)) { state.Status = "气泵出口被堵住了"; return; }
        double increment = Math.Min(1 - state.PumpProgress, definition.PumpBatchesPerSecond * GetWorkEfficiency(node) * seconds);
        double required = definition.BatchStandardLiters * definition.WorkJoulesPerStandardLiter;
        increment = Math.Min(increment, available / required);
        state.PumpProgress += increment; state.PumpStoredWorkJoules += increment * required;
        state.Status = "正在抽气";
        if (state.PumpProgress < 1 - 1e-10) return;
        if (!AtmosphereService.TryExtractTo(atmosphere, (decimal)definition.BatchStandardLiters, ref state.RandomState,
            buffer, definition.VolumeLiters, definition.MinimumGasSpaceLiters, out FluidBatch batch)) return;
        // 已积累的真实做功成为气包内能，损耗单独记账；来源只扣这一次。
        double heat = state.PumpStoredWorkJoules * definition.CompressionEfficiency;
        FluidThermodynamics.AddHeat(buffer, heat);
        state.DissipatedHeatJoules += state.PumpStoredWorkJoules - heat;
        state.PumpProgress = Math.Max(0, state.PumpProgress - 1); state.PumpStoredWorkJoules = 0;
    }

    public static bool IsGasPumpWet(MachineEntity pump)
    {
        if (GetFluidInventory(pump).LiquidMoles > 0) return true;
        if (WorldLiquidSourceResolver.TryResolve((Vector2)pump.Cell + Vector2.one * .5f, out _)) return true;
        if (FluidGraph != null)
            foreach (FluidNetworkGraph.Edge edge in FluidGraph.Edges)
                if (ReferenceEquals(edge.Source, pump) && edge.Target != null && GetFluidInventory(edge.Target, edge.TargetPort.Chamber).LiquidMoles > 0)
                    return true;
        return false;
    }

    private static bool GasPumpCanOutput(MachineEntity node)
    {
        if (FluidGraph == null) return false;
        foreach (FluidNetworkGraph.Edge edge in FluidGraph.Edges)
        {
            if (!ReferenceEquals(edge.Source, node) || edge.Target == null || !IsFluidActive(edge.Target)) continue;
            FluidInventory target = GetFluidInventory(edge.Target, edge.TargetPort.Chamber);
            if (target.LiquidMoles > 0 || target.GetPressureKPa(GetFluidVolumeLiters(edge.Target), GetFluidMinimumGasSpaceLiters(edge.Target)) >= node.Definition.Fluid.MaximumPumpPressureKPa) continue;
            if (edge.Target.Definition.Fluid.SingleGas && target.GasMoles >= FluidUnits.StandardLitersToMol((decimal)edge.Target.Definition.Fluid.GasBufferStandardLiters)) continue;
            return true;
        }
        return false;
    }

    public static void AdvanceLiquidPump(MachineEntity node, float seconds)
    {
        FluidMachineState state = GetFluidState(node); FluidMachineDefinition definition = node.Definition.Fluid;
        double work = AvailableDeviceWork(node, seconds);
        if (work <= 0) { state.Status = "没有电"; return; }
        FluidInventory buffer = GetFluidInventory(node);
        int rotation = node.RotationQuarterTurns;
        bool industrialInput = false;
        if (node.Logic is FluidMachineLogic logic && TryFindAdjacentLiquidPort(node, (rotation + 2) & 3,
            ContainerPortDirection.Output, out ILiquidTransferPort vessel, out industrialInput))
        {
            FluidMachinePortDefinition intakePort = Array.Find(definition.Ports, port => port.Mode == "input" && port.Phase != "gas");
            if (intakePort == null || !vessel.PeekLiquid(out LiquidTransferBatch preview) ||
                !FluidCatalog.Default.TryFromLiquidId(preview.LiquidId, out FluidDefinition fluid)) { state.Status = "容器没有可抽取的液体"; return; }
            double vesselLiters = Math.Min(definition.ThroughputLitersPerSecond * seconds, work / definition.WorkJoulesPerStandardLiter);
            float vesselServings = (float)FluidUnits.MolToServings(fluid, FluidUnits.LiquidLitersToMol(fluid, (decimal)vesselLiters));
            var result = ContainerTransferService.TransferLiquid(vessel, new IndustrialLiquidTransferPort(logic, intakePort),
                new ContainerTransferContext(node.Position, UnityEngine.SceneManagement.SceneManager.GetSceneByName(WorldKey).handle,
                    ContainerAccessKind.Machine, "液体泵抽取容器"), vesselServings);
            state.Status = result.Success ? "正在从容器抽液体" : "液体入口被堵住了";
            if (result.Success) state.DissipatedHeatJoules += (double)FluidUnits.MolToLiquidLiters(fluid,
                FluidUnits.ServingsToMol(fluid, (decimal)result.LiquidServings)) * definition.WorkJoulesPerStandardLiter;
            return;
        }
        // 工业输入只由管网按本轮起始库存搬运，泵不能旁路抽取或继续抽同格地表。
        if (industrialInput) { state.Status = "等待管网输入"; return; }
        Vector2 intake = (Vector2)(node.Cell + MechanicalNetworkGraph.Directions[(rotation + 2) & 3]) + Vector2.one * .5f;
        if (!WorldLiquidSourceResolver.TryResolve(intake, out WorldLiquidSourceTarget source) ||
            !FluidCatalog.Default.TryFromLiquidId(source.Liquid.Id, out FluidDefinition substance) || !substance.AllowIndustrialTransport)
        { state.Status = "入口没有可抽取的液体"; return; }
        double freeLiters = definition.VolumeLiters - definition.MinimumGasSpaceLiters - buffer.GetLiquidLiters();
        double liters = Math.Min(freeLiters, Math.Min(definition.ThroughputLitersPerSecond * seconds, work / definition.WorkJoulesPerStandardLiter));
        decimal servings = FluidUnits.MolToServings(substance, FluidUnits.LiquidLitersToMol(substance, (decimal)Math.Max(0, liters)));
        float requestedDepth = Mathf.Min(source.Sample.LiquidDepth, (float)servings * source.Liquid.WorldWater.DepthPerServing);
        if (requestedDepth <= .0000001f) { state.Status = "液体空间已满"; return; }
        decimal plannedMoles = FluidUnits.ServingsToMol(substance, (decimal)(requestedDepth / source.Liquid.WorldWater.DepthPerServing));
        FluidBatch planned = FluidInventory.CreateBatch(substance, 0, plannedMoles, AmbientTemperatureKelvin(node));
        if (!buffer.CanAdd(planned, definition.VolumeLiters, definition.MinimumGasSpaceLiters, true, true))
        { state.Status = "液相槽里还有其他物质"; return; }
        float beforeDepth = source.Sample.LiquidDepth;
        if (!WorldLiquidSystem.TryPump(source.Sample, requestedDepth, out string liquidId, out float removedDepth)) return;
        decimal actualMoles = FluidUnits.ServingsToMol(substance, (decimal)(removedDepth / source.Liquid.WorldWater.DepthPerServing));
        FluidBatch actual = FluidInventory.CreateBatch(substance, 0, actualMoles, AmbientTemperatureKelvin(node));
        if (!buffer.TryAdd(actual, definition.VolumeLiters, definition.MinimumGasSpaceLiters, true, true))
        {
            WorldLiquidSystem.TrySet(source.Sample, liquidId, beforeDepth);
            throw new InvalidOperationException("世界液体抽取与泵缓冲提交失败。");
        }
        state.Status = "正在抽液体";
        state.DissipatedHeatJoules += (double)FluidUnits.MolToLiquidLiters(substance, actualMoles) * definition.WorkJoulesPerStandardLiter;
    }

    public static void AdvanceCompressor(MachineEntity node, float seconds)
    {
        FluidMachineState state = GetFluidState(node); FluidMachineDefinition definition = node.Definition.Fluid;
        FluidInventory input = GetFluidInventory(node), output = GetFluidInventory(node, "output");
        if (node.Logic is FluidMachineLogic portable && portable.PortableTank != null) FillPortableFluidTank(node, portable.PortableTank);
        double work = AvailableDeviceWork(node, seconds);
        if (work <= 0) { state.Status = node.Definition.Electrical != null ? "没有电" : "没有动力"; return; }
        double pressure = input.GetPressureKPa(definition.VolumeLiters, definition.MinimumGasSpaceLiters);
        if (pressure <= 0 || input.GasMoles <= 0) { state.Status = "入口没有气体"; return; }
        if (!input.TryReserve(node.Id + ":compress", FluidPhase.Gas,
            FluidUnits.StandardLitersToMol((decimal)Math.Min(definition.ThroughputLitersPerSecond * seconds, definition.BatchStandardLiters)), out FluidBatch batch)) return;
        double targetPressure = Math.Min(definition.MaximumPumpPressureKPa, Math.Max(state.CompressionTargetKPa,
            output.GetPressureKPa(definition.VolumeLiters, definition.MinimumGasSpaceLiters)));
        FluidCompressionResult compressed = FluidThermodynamics.Compress(batch, pressure, targetPressure, work, definition.CompressionEfficiency);
        if (!compressed.Success || output.GasMoles + compressed.Batch.GasMoles > FluidUnits.StandardLitersToMol((decimal)definition.GasBufferStandardLiters) ||
            !output.CanAdd(compressed.Batch, definition.VolumeLiters, definition.MinimumGasSpaceLiters, false, false))
        { state.Status = "压缩机出口被堵住了"; return; }
        FluidInventoryState previous = input.State.Clone();
        if (!input.TryTakeReserved(node.Id + ":compress", compressed.Batch.TotalMoles, out _)) return;
        if (!output.TryAdd(compressed.Batch, definition.VolumeLiters, definition.MinimumGasSpaceLiters, false, false))
        { input.Restore(previous); throw new InvalidOperationException("压缩机预算提交失败。"); }
        state.DissipatedHeatJoules += compressed.LossJoules; state.Status = "正在压缩";
    }

    private static void FillPortableFluidTank(MachineEntity node, Inventory inventory)
    {
        ItemData item = inventory.Data.itemSlots[0].itemData;
        if (item == null || !FluidTankStorage.TryGet(item, out FluidInventory target, out FluidTankConfiguration configuration)) return;
        FluidInventory source = GetFluidInventory(node, "output");
        if (!TryGetFluidTransferBudget(source, out FluidInventoryState beginning, out double budget) || budget <= 0) return;
        string port = node.Id + ":portable-fill";
        if (!source.TryReserve(port, FluidPhase.Gas, FluidUnits.StandardLitersToMol((decimal)node.Definition.Fluid.BatchStandardLiters), out FluidBatch batch)) return;
        FluidComponentState stock = beginning.Components.Find(component => component.FluidId == batch.FluidId);
        if (stock == null || stock.GasMoles <= 0 || batch.LiquidMoles > 0) return;
        decimal maximum = Math.Min(stock.GasMoles, FluidUnits.StandardLitersToMol((decimal)budget));
        maximum = Math.Min(maximum, batch.TotalMoles);
        double targetPressure = Math.Min(GetFluidState(node).CompressionTargetKPa, node.Definition.Fluid.MaximumPumpPressureKPa);
        if (target.GetPressureKPa(configuration.VolumeLiters, configuration.MinimumGasSpaceLiters) >= targetPressure) return;
        maximum = LimitPortableFillByPressure(target, batch, maximum, targetPressure, configuration.VolumeLiters, configuration.MinimumGasSpaceLiters);
        if (maximum <= 0) return;
        if (!source.TryTransfer(port, target, maximum, configuration.VolumeLiters, configuration.MinimumGasSpaceLiters, out FluidBatch moved)) return;
        stock.GasMoles -= moved.GasMoles;
        SetFluidTransferBudget(source, Math.Max(0, budget - (double)FluidUnits.MolToStandardLiters(moved.GasMoles)));
        FluidTankStorage.Write(item, target); inventory.Data.NotifyItemStateChanged(item);
    }
    private static decimal LimitPortableFillByPressure(FluidInventory target, FluidBatch batch, decimal maximum, double pressure,
        double volume, double minimum)
    {
        FluidInventory trial = new(target.State.Clone(), target.Catalog);
        FluidInventoryState original = trial.State.Clone();
        decimal low = 0, high = maximum;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            decimal middle = (low + high) / 2;
            trial.Restore(original);
            FluidBatch candidate = batch.Scale(middle / batch.TotalMoles);
            if (trial.TryAdd(candidate, volume, minimum) && trial.GetPressureKPa(volume, minimum) <= pressure) low = middle;
            else high = middle;
        }
        return low;
    }

    #endregion

    #region 多产物可靠反应与燃料机械能
    public static double CalculateAvailableEnginePower(MachineEntity node, float seconds)
    {
        FluidMachineState state = GetFluidState(node);
        if (state.Ruptured || !string.IsNullOrEmpty(state.RuptureBudgetId) || node.Definition.Fluid.Reactions.Length == 0) return 0;
        FluidMachineReactionDefinition reaction = node.Definition.Fluid.Reactions[0];
        double extent = GetReactionInputExtent(node, reaction, seconds);
        extent = Math.Min(extent, GetReactionOutputExtent(node, reaction));
        return extent <= 0 ? 0 : Math.Min(node.Definition.Torque * node.Definition.Rpm * MachineCatalog.Settings.WattsPerTorqueRpm,
            extent * reaction.ChemicalJoulesPerReaction * reaction.MechanicalEfficiency / seconds);
    }
    public static void SettleFluidEngine(MachineEntity node, float seconds)
    {
        if (!TryGetFluidMechanicalPower(node, out double maximum) || maximum <= 0 || node.SpeedRpm <= 0 || node.Network?.TorqueDemand <= 0)
        { GetFluidState(node).Status = "没有燃料、产物空间或有效负载"; return; }
        double suppliedTorque = Math.Floor(node.SourceTorque * node.SourceFactor / 10) * 10;
        double usedTorque = Math.Max(0, suppliedTorque - node.RemainingSourceTorque * node.TorqueRatio);
        double work = Math.Min(maximum * seconds, usedTorque * node.SpeedRpm * MachineCatalog.Settings.WattsPerTorqueRpm * seconds);
        if (work <= 0) { GetFluidState(node).Status = "没有有效负载"; return; }
        FluidMachineReactionDefinition reaction = node.Definition.Fluid.Reactions[0];
        double extent = work / (reaction.ChemicalJoulesPerReaction * reaction.MechanicalEfficiency);
        if (!CommitFluidReaction(node, reaction, extent, 0, work))
            throw new InvalidOperationException("气体发动机已供能但实际燃料预算无法提交。");
        GetFluidState(node).Status = "正在燃烧供能";
    }
    public static void AdvanceFluidReaction(MachineEntity node, float seconds)
    {
        double available = AvailableDeviceWork(node, seconds);
        FluidMachineState state = GetFluidState(node);
        if (available <= 0) { state.Status = "没有电或供热"; return; }
        foreach (FluidMachineReactionDefinition reaction in node.Definition.Fluid.Reactions)
        {
            double extent = Math.Min(GetReactionInputExtent(node, reaction, seconds), GetReactionOutputExtent(node, reaction));
            if (reaction.RequiredJoulesPerReaction > 0) extent = Math.Min(extent, available / reaction.RequiredJoulesPerReaction);
            if (reaction.RequiresFilter) extent = Math.Min(extent, GetAvailableFilterWork(node));
            if (extent <= .000000001) continue;
            if (CommitFluidReaction(node, reaction, extent, extent * reaction.RequiredJoulesPerReaction, 0))
            {
                state.Status = "正在加工"; return;
            }
        }
        state.Status = "原料不适配或产物出口被堵住了";
    }
    public static float GetReactionDemand(MachineEntity node)
    {
        foreach (FluidMachineReactionDefinition reaction in node.Definition.Fluid.Reactions)
            if (GetReactionInputExtent(node, reaction, 1) > 0 && GetReactionOutputExtent(node, reaction) > 0) return 1;
        return 0;
    }
    public static double GetReactionInputExtent(MachineEntity node, FluidMachineReactionDefinition reaction, double seconds)
    {
        double result = reaction.MolesPerSecond * seconds;
        var required = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (FluidReactionTerm input in reaction.Inputs)
            required[input.Chamber + "|" + input.FluidId + "|" + input.Phase] = required.GetValueOrDefault(input.Chamber + "|" + input.FluidId + "|" + input.Phase) + input.Moles;
        var checkedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (FluidReactionTerm input in reaction.Inputs)
        {
            string key = input.Chamber + "|" + input.FluidId + "|" + input.Phase;
            if (!checkedKeys.Add(key)) continue;
            FluidInventory inventory = GetFluidInventory(node, input.Chamber);
            decimal available = inventory.GetAvailableMoles(input.FluidId, input.Phase == "gas" ? FluidPhase.Gas : FluidPhase.Liquid);
            result = Math.Min(result, (double)available / required[key]);
        }
        return result;
    }
    public static double GetReactionOutputExtent(MachineEntity node, FluidMachineReactionDefinition reaction)
    {
        double result = double.PositiveInfinity;
        var gasPerChamber = new Dictionary<string, double>(StringComparer.Ordinal);
        var litersPerChamber = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (FluidReactionTerm output in reaction.Outputs)
        {
            if (output.Phase == "gas") gasPerChamber[output.Chamber] = gasPerChamber.GetValueOrDefault(output.Chamber) + output.Moles;
            else
            {
                FluidDefinition fluid = FluidCatalog.Default.Find(output.FluidId);
                litersPerChamber[output.Chamber] = litersPerChamber.GetValueOrDefault(output.Chamber) + output.Moles * fluid.MolarMassKgPerMol / fluid.LiquidDensityKgPerLiter;
            }
        }
        foreach (var gas in gasPerChamber)
            result = Math.Min(result, Math.Max(0, (double)(FluidUnits.StandardLitersToMol((decimal)node.Definition.Fluid.GasBufferStandardLiters) - GetFluidInventory(node, gas.Key).GasMoles)) / gas.Value);
        foreach (var liquid in litersPerChamber)
            result = Math.Min(result, Math.Max(0, node.Definition.Fluid.VolumeLiters - node.Definition.Fluid.MinimumGasSpaceLiters - GetFluidInventory(node, liquid.Key).GetLiquidLiters()) / liquid.Value);
        if (reaction.ResidueItemsPerReaction > 0)
        {
            if (!TryGetResiduePort(node, reaction, out IItemTransferPort residue, out ItemData product)) return 0;
            int capacity = residue.GetReceivableItems(product, int.MaxValue);
            if (capacity <= 0) return 0;
            result = Math.Min(result, Math.Max(0, capacity + 1 - GetFluidState(node).ResidueProgress.GetValueOrDefault(reaction.SolidResidueItemId) - .0000001) / reaction.ResidueItemsPerReaction);
        }
        return result;
    }
    private static bool CommitFluidReaction(MachineEntity node, FluidMachineReactionDefinition reaction, double extent, double work, double mechanicalWork)
    {
        if (!double.IsFinite(extent) || extent <= 0) return false;
        FluidMachineState state = GetFluidState(node);
        if (reaction.RequiresFilter && GetAvailableFilterWork(node) + .000000001 < extent) return false;
        double nextResidue = state.ResidueProgress.GetValueOrDefault(reaction.SolidResidueItemId) + extent * reaction.ResidueItemsPerReaction;
        int residueItems = (int)Math.Floor(nextResidue + .000000001);
        IItemTransferPort residuePort = null; ItemData residueItem = null;
        if (reaction.ResidueItemsPerReaction > 0 && (!TryGetResiduePort(node, reaction, out residuePort, out residueItem) ||
            residuePort.GetReceivableItems(residueItem, residueItems) < residueItems)) return false;
        var snapshots = new Dictionary<FluidInventory, FluidInventoryState>();
        double inputEnergy = 0, inputMoles = 0, weightedTemperature = 0;
        foreach (FluidReactionTerm input in reaction.Inputs)
        {
            FluidInventory inventory = GetFluidInventory(node, input.Chamber);
            if (!snapshots.ContainsKey(inventory)) snapshots.Add(inventory, inventory.State.Clone());
            decimal moles = (decimal)(extent * input.Moles);
            double inputTemperature = inventory.GetTemperatureKelvin();
            if (!inventory.TryTakeExact(input.FluidId, input.Phase == "gas" ? FluidPhase.Gas : FluidPhase.Liquid, moles, out FluidBatch batch))
            { RestoreFluidSnapshots(snapshots); return false; }
            inputEnergy += batch.InternalEnergyJoules; weightedTemperature += inputTemperature * (double)moles; inputMoles += (double)moles;
        }
        double capacity = 0, offset = 0;
        foreach (FluidReactionTerm output in reaction.Outputs)
        {
            FluidDefinition substance = FluidCatalog.Default.Find(output.FluidId);
            double moles = extent * output.Moles;
            capacity += moles * (output.Phase == "gas" ? substance.GasHeatCapacityJPerMolKelvin : substance.LiquidHeatCapacityJPerMolKelvin);
            if (output.Phase == "gas") offset += moles * substance.GasEnergyOffsetJPerMol;
        }
        double available = inputEnergy + work + extent * (reaction.ChemicalJoulesPerReaction - reaction.StoredChemicalJoulesPerReaction) - mechanicalWork;
        double temperatureLimit = weightedTemperature / Math.Max(.000001, inputMoles);
        double temperature = Math.Min(temperatureLimit, (available - offset) / capacity);
        if (!double.IsFinite(temperature) || temperature <= 0) { RestoreFluidSnapshots(snapshots); return false; }
        foreach (FluidReactionTerm output in reaction.Outputs)
        {
            FluidInventory inventory = GetFluidInventory(node, output.Chamber);
            if (!snapshots.ContainsKey(inventory)) snapshots.Add(inventory, inventory.State.Clone());
            decimal moles = (decimal)(extent * output.Moles);
            FluidBatch batch = FluidInventory.CreateBatch(inventory.Catalog.Find(output.FluidId), output.Phase == "gas" ? moles : 0,
                output.Phase == "liquid" ? moles : 0, temperature);
            if (!inventory.TryAdd(batch, node.Definition.Fluid.VolumeLiters, node.Definition.Fluid.MinimumGasSpaceLiters, node.Definition.Fluid.SingleGas, node.Definition.Fluid.SingleLiquid))
            { RestoreFluidSnapshots(snapshots); return false; }
        }
        object residueBefore = residueItems > 0 ? residuePort.CaptureState() : null;
        if (residueItems > 0 && !residuePort.InsertItems(residueItem, residueItems))
        { residuePort.RestoreState(residueBefore); RestoreFluidSnapshots(snapshots); return false; }
        if (reaction.RequiresFilter && !EnsureFilterMaterial(node))
        {
            if (residueBefore != null) residuePort.RestoreState(residueBefore);
            RestoreFluidSnapshots(snapshots); return false;
        }
        if (reaction.RequiresFilter) state.FilterRemainingWork = Math.Max(0, state.FilterRemainingWork - extent);
        if (reaction.ResidueItemsPerReaction > 0) state.ResidueProgress[reaction.SolidResidueItemId] = Math.Max(0, nextResidue - residueItems);
        if (residueItems > 0) residuePort.PublishState();
        state.DissipatedHeatJoules += Math.Max(0, available - (offset + capacity * temperature));
        return true;
    }
    private static void RestoreFluidSnapshots(Dictionary<FluidInventory, FluidInventoryState> snapshots)
    { foreach (var pair in snapshots) pair.Key.Restore(pair.Value); }

    private static bool EnsureFilterMaterial(MachineEntity node)
    {
        FluidMachineState state = GetFluidState(node);
        if (state.FilterRemainingWork > 0) return true;
        if (node.Logic is not FluidMachineLogic logic || logic.FilterMaterial == null) return false;
        ItemSlot slot = logic.FilterMaterial.Data.itemSlots[0];
        if (slot.itemData?.IDName != node.Definition.Fluid.FilterMaterialItemId) return false;
        if (!logic.FilterMaterial.Data.TryConsumeFromSlot(slot, 1, out _)) return false;
        state.FilterRemainingWork = node.Definition.Fluid.FilterWorkPerItem; return true;
    }
    private static double GetAvailableFilterWork(MachineEntity node)
    {
        if (GetFluidState(node).FilterRemainingWork > 0) return GetFluidState(node).FilterRemainingWork;
        return node.Logic is FluidMachineLogic logic && logic.FilterMaterial?.Data.itemSlots[0].itemData?.IDName == node.Definition.Fluid.FilterMaterialItemId
            ? node.Definition.Fluid.FilterWorkPerItem : 0;
    }
    /// <summary>液体副产物满一份才转入库存原料，余量与温度留在真实流体库存。</summary>
    public static void PackageLiquidResidue(MachineEntity node)
    {
        FluidMachineDefinition configuration = node.Definition.Fluid;
        GameRes resources = GameRes.ExistingInstance;
        if (string.IsNullOrEmpty(configuration.LiquidResidueFluidId) || resources == null ||
            node.Logic is not FluidMachineLogic logic || logic.Residue == null ||
            !FluidCatalog.Default.TryGet(configuration.LiquidResidueFluidId, out FluidDefinition fluid) ||
            !resources.TryGetLiquidDefinition(fluid.LiquidId, out LiquidDefinition liquid) ||
            !resources.TryGetItemDefinition(liquid.SourceItemId, out RuntimeItemDefinition definition)) return;
        FluidInventory source = GetFluidInventory(node, configuration.LiquidResidueChamber);
        decimal available = source.GetAvailableMoles(fluid.Id, FluidPhase.Liquid);
        int requested = (int)Math.Min(int.MaxValue, decimal.Floor(FluidUnits.MolToServings(fluid, available)));
        if (requested <= 0) return;
        ItemData item = definition.CreateItemData();
        item.MatterState.Initialized = true;
        item.MatterState.TemperatureCelsius = (float)(source.GetTemperatureKelvin() - 273.15);
        var target = new InventoryItemTransferPort(logic.Residue, null, new ContainerPortConfiguration { Id = "liquid-residue" },
            () => Contains(node) && ReferenceEquals(node.Logic, logic), "machine:" + node.Id,
            node.Position, UnityEngine.SceneManagement.SceneManager.GetSceneByName(WorldKey).handle);
        int amount = target.GetReceivableItems(item, requested);
        if (amount <= 0) return;
        FluidInventoryState beforeSource = source.State.Clone();
        object beforeTarget = target.CaptureState();
        if (!source.TryTakeExact(fluid.Id, FluidPhase.Liquid, FluidUnits.ServingsToMol(fluid, amount), out _)) return;
        if (!target.InsertItems(item, amount))
        {
            source.Restore(beforeSource);
            target.RestoreState(beforeTarget);
            return;
        }
        target.PublishState();
        StateChanged(node);
    }

    private static bool TryGetResiduePort(MachineEntity node, FluidMachineReactionDefinition reaction, out IItemTransferPort port, out ItemData item)
    {
        port = null; item = null;
        if (node.Logic is not FluidMachineLogic logic || logic.Residue == null || GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetItemDefinition(reaction.SolidResidueItemId, out RuntimeItemDefinition definition)) return false;
        item = definition.CreateItemData();
        port = new InventoryItemTransferPort(logic.Residue, null, new ContainerPortConfiguration { Id = "residue" },
            () => Contains(node) && ReferenceEquals(node.Logic, logic), "machine:" + node.Id,
            node.Position, UnityEngine.SceneManagement.SceneManager.GetSceneByName(WorldKey).handle);
        return true;
    }

    #endregion
}
