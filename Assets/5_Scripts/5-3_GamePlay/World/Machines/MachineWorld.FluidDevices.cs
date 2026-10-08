using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class MachineWorld
{
    #region 流体设备实际动力与取料
    public static void AdvanceFluidDevice(MachineEntity node, float seconds)
    {
        if (node.Definition.Fluid == null || !IsFluidActive(node) || GetFluidState(node).Ruptured) return;
        switch (node.Definition.Fluid.Kind)
        {
            case "gas-pump": AdvanceGasPump(node, seconds); break;
            case "liquid-pump": AdvanceLiquidPump(node, seconds); break;
            case "compressor": AdvanceCompressor(node, seconds); break;
            case "heat-exchanger": AdvanceHeatExchanger(node, seconds); break;
            case "filter": case "distiller": case "electrolyzer": AdvanceFluidReaction(node, seconds); break;
            case "engine": SettleFluidEngine(node, seconds); break;
        }
    }

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

    private static void AdvanceGasPump(MachineEntity node, float seconds)
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

    private static bool GasPumpCanOutput(MachineEntity node)
    {
        if (fluidGraph == null) return false;
        foreach (FluidNetworkGraph.Edge edge in fluidGraph.Edges)
        {
            if (!ReferenceEquals(edge.Source, node) || edge.Target == null || !IsFluidActive(edge.Target)) continue;
            FluidInventory target = GetFluidInventory(edge.Target, edge.TargetPort.Chamber);
            if (target.LiquidMoles > 0 || target.GetPressureKPa(GetFluidVolumeLiters(edge.Target), GetFluidMinimumGasSpaceLiters(edge.Target)) >= node.Definition.Fluid.MaximumPumpPressureKPa) continue;
            if (edge.Target.Definition.Fluid.SingleGas && target.GasMoles >= FluidUnits.StandardLitersToMol((decimal)edge.Target.Definition.Fluid.GasBufferStandardLiters)) continue;
            return true;
        }
        return false;
    }

    private static void AdvanceLiquidPump(MachineEntity node, float seconds)
    {
        FluidMachineState state = GetFluidState(node); FluidMachineDefinition definition = node.Definition.Fluid;
        double work = AvailableDeviceWork(node, seconds);
        if (work <= 0) { state.Status = "没有电"; return; }
        FluidInventory buffer = GetFluidInventory(node);
        int rotation = node.RotationQuarterTurns;
        if (node.Logic is FluidMachineLogic logic && TryFindAdjacentVesselPort(node, (rotation + 2) & 3, ContainerPortDirection.Output, out ILiquidTransferPort vessel))
        {
            FluidMachinePortDefinition intakePort = Array.Find(definition.Ports, port => port.Mode == "input" && port.Phase != "gas");
            if (intakePort == null || !vessel.PeekLiquid(out LiquidTransferBatch preview) ||
                !FluidCatalog.Default.TryFromLiquidId(preview.LiquidId, out FluidDefinition fluid)) { state.Status = "容器没有可抽取的液体"; return; }
            double vesselLiters = Math.Min(definition.ThroughputLitersPerSecond * seconds, work / definition.WorkJoulesPerStandardLiter);
            float vesselServings = (float)FluidUnits.MolToServings(fluid, FluidUnits.LiquidLitersToMol(fluid, (decimal)vesselLiters));
            var result = ContainerTransferService.TransferLiquid(vessel, new IndustrialLiquidTransferPort(logic, intakePort),
                new ContainerTransferContext(node.Position, UnityEngine.SceneManagement.SceneManager.GetSceneByName(worldKey).handle,
                    ContainerAccessKind.Machine, "液体泵抽取容器"), vesselServings);
            state.Status = result.Success ? "正在从容器抽液体" : "液体入口被堵住了";
            if (result.Success) state.DissipatedHeatJoules += (double)FluidUnits.MolToLiquidLiters(fluid,
                FluidUnits.ServingsToMol(fluid, (decimal)result.LiquidServings)) * definition.WorkJoulesPerStandardLiter;
            return;
        }
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

    private static void AdvanceCompressor(MachineEntity node, float seconds)
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
        if (!fluidStepStock.TryGetValue(source, out FluidInventoryState beginning) ||
            !fluidThroughputRemaining.TryGetValue(source, out double budget) || budget <= 0) return;
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
        fluidThroughputRemaining[source] = Math.Max(0, budget - (double)FluidUnits.MolToStandardLiters(moved.GasMoles));
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

    private static void AdvanceHeatExchanger(MachineEntity node, float seconds)
    {
        FluidInventory hot = GetFluidInventory(node, "hot"), cold = GetFluidInventory(node, "cold");
        if (hot.IsEmpty || cold.IsEmpty) { GetFluidState(node).Status = "请接入两侧实际流体"; return; }
        var hotBefore = hot.State.Clone(); var coldBefore = cold.State.Clone();
        double transferred = FluidThermodynamics.ExchangeHeat(hot, cold, node.Definition.Fluid.HeatConductanceWattsPerKelvin, seconds);
        FluidPhaseChangeResult a = FluidThermodynamics.AdvancePhaseChange(hot, node.Definition.Fluid.VolumeLiters,
            node.Definition.Fluid.MinimumGasSpaceLiters, seconds, node.Definition.Fluid.MaximumPhaseMolesPerSecond, true, true);
        FluidPhaseChangeResult b = FluidThermodynamics.AdvancePhaseChange(cold, node.Definition.Fluid.VolumeLiters,
            node.Definition.Fluid.MinimumGasSpaceLiters, seconds, node.Definition.Fluid.MaximumPhaseMolesPerSecond, true, true);
        UpdateFluidPhaseWarning(node, "hot", a); UpdateFluidPhaseWarning(node, "cold", b);
        if (a.IsBlocked || b.IsBlocked)
        { hot.Restore(hotBefore); cold.Restore(coldBefore); GetFluidState(node).Status = a.BlockedReason ?? b.BlockedReason; return; }
        GetFluidState(node).Status = Math.Abs(transferred) > .000001 ? "正在交换热量" : "两侧温度已接近";
    }
    #endregion

    #region 多产物可靠反应与燃料机械能
    private static double CalculateAvailableEnginePower(MachineEntity node, float seconds)
    {
        FluidMachineState state = GetFluidState(node);
        if (state.Ruptured || !string.IsNullOrEmpty(state.RuptureBudgetId) || node.Definition.Fluid.Reactions.Length == 0) return 0;
        FluidMachineReactionDefinition reaction = node.Definition.Fluid.Reactions[0];
        double extent = GetReactionInputExtent(node, reaction, seconds);
        extent = Math.Min(extent, GetReactionOutputExtent(node, reaction));
        return extent <= 0 ? 0 : Math.Min(node.Definition.Torque * node.Definition.Rpm * MachineCatalog.Settings.WattsPerTorqueRpm,
            extent * reaction.ChemicalJoulesPerReaction * reaction.MechanicalEfficiency / seconds);
    }
    private static float GetFluidEngineSourceFactor(MachineEntity node)
        => fluidEnginePower.TryGetValue(node, out double watts)
            ? (float)Math.Clamp(watts / (node.Definition.Torque * node.Definition.Rpm * MachineCatalog.Settings.WattsPerTorqueRpm), 0, 1) : 0;
    private static void SettleFluidEngine(MachineEntity node, float seconds)
    {
        if (!fluidEnginePower.TryGetValue(node, out double maximum) || maximum <= 0 || node.SpeedRpm <= 0 || node.Network?.TorqueDemand <= 0)
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
    private static void AdvanceFluidReaction(MachineEntity node, float seconds)
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
    private static double GetReactionInputExtent(MachineEntity node, FluidMachineReactionDefinition reaction, double seconds)
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
    private static double GetReactionOutputExtent(MachineEntity node, FluidMachineReactionDefinition reaction)
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
    private static bool TryGetResiduePort(MachineEntity node, FluidMachineReactionDefinition reaction, out IItemTransferPort port, out ItemData item)
    {
        port = null; item = null;
        if (node.Logic is not FluidMachineLogic logic || logic.Residue == null || GameRes.ExistingInstance == null ||
            !GameRes.ExistingInstance.TryGetItemDefinition(reaction.SolidResidueItemId, out RuntimeItemDefinition definition)) return false;
        item = definition.CreateItemData();
        port = new InventoryItemTransferPort(logic.Residue, null, new ContainerPortConfiguration { Id = "residue" },
            () => Contains(node) && ReferenceEquals(node.Logic, logic), "machine:" + node.Id,
            node.Position, UnityEngine.SceneManagement.SceneManager.GetSceneByName(worldKey).handle);
        return true;
    }

    public static float ConsumeFurnaceOxygen(MachineEntity furnace, float seconds, float fuelUnits)
    {
        if (fuelUnits <= 0 || fluidGraph == null) return 0;
        foreach (MachineEntity node in nodes.Values)
        {
            if (node.Definition.Fluid?.Kind != "oxygen-burner" || !IsFluidActive(node) ||
                graph.NormalizeCell(node.Cell + MechanicalNetworkGraph.Directions[node.RotationQuarterTurns]) != furnace.Cell) continue;
            FluidInventory oxygen = GetFluidInventory(node);
            decimal requested = FluidUnits.StandardLitersToMol((decimal)(fuelUnits * node.Definition.Fluid.OxygenServingsPerFuelUnit));
            decimal actual = Math.Min(requested, oxygen.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas));
            if (actual <= 0 || !oxygen.TryTakeExact(FluidIds.Oxygen, FluidPhase.Gas, actual, out _)) continue;
            return (float)(node.Definition.Fluid.OxygenTemperatureBonus * (double)(actual / requested));
        }
        return 0;
    }
    #endregion
}
