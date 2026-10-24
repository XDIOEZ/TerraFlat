using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using static MachineWorld;

/// <summary>设备加工各自调用这里的事务，统一管网不识别设备种类。</summary>
public static class FluidDeviceOperations
{
    #region 反应定义与事务工作区
    private sealed class CompiledReaction
    {
        public FluidCatalog Catalog;
        public FluidReactionTerm[] Inputs;
        public (string Chamber, double Moles)[] GasOutputs;
        public (string Chamber, double Liters)[] LiquidOutputs;
    }
    private sealed class ReactionWorkspace
    {
        public readonly List<FluidInventory> Inventories = new();
        public readonly List<FluidInventoryState> Snapshots = new();
        public int Count;
        public void Begin() { Inventories.Clear(); Count = 0; }
        public void Remember(FluidInventory inventory)
        {
            if (Inventories.Contains(inventory)) return;
            if (Count == Snapshots.Count) Snapshots.Add(new FluidInventoryState());
            Snapshots[Count].CopyFrom(inventory.State); Inventories.Add(inventory); Count++;
        }
        public void Restore()
        { for (int i = 0; i < Count; i++) Inventories[i].Restore(Snapshots[i]); }
    }
    private sealed class CompressorWorkspace
    {
        public readonly FluidInventoryState Before = new();
        public string CompressionPort, PortablePort;
        public CompressorWorkspace(MachineEntity node)
        { CompressionPort = node.Id + ":compress"; PortablePort = node.Id + ":portable-fill"; }
    }
    private static readonly Dictionary<FluidMachineReactionDefinition, CompiledReaction> compiledReactions = new();
    private static readonly ConditionalWeakTable<MachineEntity, ReactionWorkspace> reactionWorkspaces = new();
    private static readonly ConditionalWeakTable<MachineEntity, CompressorWorkspace> compressorWorkspaces = new();
    internal static void ClearCompiledReactions() => compiledReactions.Clear();
    private static CompiledReaction CompileReaction(FluidMachineReactionDefinition reaction)
    {
        FluidCatalog catalog = FluidCatalog.Default;
        if (compiledReactions.TryGetValue(reaction, out CompiledReaction existing) && ReferenceEquals(existing.Catalog, catalog)) return existing;
        // 静态配方先合并同腔同相需求，正常供电探算不再拼键或创建字典。
        var requirements = new Dictionary<(string Chamber, string FluidId, string Phase), double>();
        foreach (FluidReactionTerm input in reaction.Inputs)
        {
            var key = (input.Chamber, input.FluidId, input.Phase);
            requirements[key] = requirements.GetValueOrDefault(key) + input.Moles;
        }
        var inputs = new FluidReactionTerm[requirements.Count];
        int index = 0;
        foreach (var input in requirements)
            inputs[index++] = new FluidReactionTerm { Chamber = input.Key.Chamber, FluidId = input.Key.FluidId,
                Phase = input.Key.Phase, Moles = input.Value };
        var gas = new Dictionary<string, double>(StringComparer.Ordinal);
        var liquid = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (FluidReactionTerm output in reaction.Outputs)
        {
            if (output.Phase == "gas") gas[output.Chamber] = gas.GetValueOrDefault(output.Chamber) + output.Moles;
            else
            {
                FluidDefinition fluid = catalog.Find(output.FluidId);
                liquid[output.Chamber] = liquid.GetValueOrDefault(output.Chamber) +
                    output.Moles * fluid.MolarMassKgPerMol / fluid.LiquidDensityKgPerLiter;
            }
        }
        var result = new CompiledReaction { Catalog = catalog, Inputs = inputs,
            GasOutputs = new (string, double)[gas.Count], LiquidOutputs = new (string, double)[liquid.Count] };
        index = 0; foreach (var value in gas) result.GasOutputs[index++] = (value.Key, value.Value);
        index = 0; foreach (var value in liquid) result.LiquidOutputs[index++] = (value.Key, value.Value);
        compiledReactions[reaction] = result;
        return result;
    }
    #endregion

    #region 流体设备实际动力与取料
    private static double AvailableDeviceWork(MachineEntity node, double seconds)
    {
        if (node.Definition.Electrical?.IsConsumer == true) return Math.Max(0, node.ElectricalSuppliedWatts) * seconds;
        if (!node.Definition.HasMechanicalPorts || !node.LoadSatisfied || node.SpeedRpm <= 0) return 0;
        return node.RequiredTorque * node.SpeedRpm *
            MachineCatalog.Settings.WattsPerTorqueRpm * seconds;
    }
    private static double AmbientTemperatureKelvin(MachineEntity node)
        => TryGetFluidAmbientTemperature(node, out float celsius)
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
        if (GetFluidAmbientGasMoles(node) <= 0m) { state.Status = "没有可抽取的大气"; return; }
        if (!GasPumpCanOutput(node)) { state.Status = "气泵出口被堵住了"; return; }
        double increment = Math.Min(1 - state.PumpProgress, definition.PumpBatchesPerSecond * GetWorkEfficiency(node) * seconds);
        double required = definition.BatchStandardLiters * definition.WorkJoulesPerStandardLiter;
        increment = Math.Min(increment, available / required);
        state.PumpProgress += increment; state.PumpStoredWorkJoules += increment * required;
        state.Status = "正在抽气";
        if (state.PumpProgress < 1 - 1e-10) return;
        if (!TryExtractFluidAmbientGasTo(node, (decimal)definition.BatchStandardLiters, ref state.RandomState,
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
        if (IsFluidEnvironmentSubmerged(pump)) return true;
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
            FluidInventory target = GetPhaseFluidInventory(edge.Target, edge.TargetPort.Chamber, FluidPhase.Gas);
            if (GetFluidInventory(edge.Target, edge.TargetPort.Chamber).LiquidMoles > 0 ||
                target.GetPressureKPa(GetPhaseFluidVolumeLiters(edge.Target, FluidPhase.Gas),
                    GetPhaseFluidMinimumGasSpaceLiters(edge.Target, FluidPhase.Gas)) >= node.Definition.Fluid.MaximumPumpPressureKPa) continue;
            if (edge.Target.Definition.Fluid.SingleGas && !IsSharedGasNetworkMember(edge.Target) &&
                target.GasMoles >= FluidUnits.StandardLitersToMol((decimal)edge.Target.Definition.Fluid.GasBufferStandardLiters)) continue;
            return true;
        }
        return false;
    }

    public static void AdvanceLiquidPump(MachineEntity node, float seconds)
    {
        FluidMachineState state = GetFluidState(node); FluidMachineDefinition definition = node.Definition.Fluid;
        FluidInventory buffer = GetFluidInventory(node);
        // 主动停用电力需求时先说明库存条件，避免把满缓冲误报成断电。
        if (buffer.GetLiquidLiters() >= definition.VolumeLiters - definition.MinimumGasSpaceLiters)
        { state.Status = "液体缓冲已满，等待输出"; return; }
        double work = AvailableDeviceWork(node, seconds);
        if (work <= 0) { state.Status = "没有电"; return; }
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
            state.Status = result.Success ? "正在从容器抽液体" : GetLiquidPumpTransferFailureStatus(result.Failure, buffer, fluid, definition.SingleLiquid);
            if (result.Success) state.DissipatedHeatJoules += (double)FluidUnits.MolToLiquidLiters(fluid,
                FluidUnits.ServingsToMol(fluid, (decimal)result.LiquidServings)) * definition.WorkJoulesPerStandardLiter;
            return;
        }
        // 工业输入只由管网按本轮起始库存搬运，泵不能旁路抽取或继续抽同格地表。
        if (industrialInput) { state.Status = "等待管网输入"; return; }
        Vector2 intake = (Vector2)(node.Cell + MechanicalNetworkGraph.Directions[(rotation + 2) & 3]) + Vector2.one * .5f;
        if (!TrySampleFluidEnvironmentLiquid(intake, out string sourceWorld, out Vector2 sourcePosition, out LiquidDefinition sourceLiquid, out int sourceLevel) ||
            !FluidCatalog.Default.TryFromLiquidId(sourceLiquid.Id, out FluidDefinition substance) || !substance.AllowIndustrialTransport)
        { state.Status = "入口没有可抽取的液体"; return; }
        double freeLiters = definition.VolumeLiters - definition.MinimumGasSpaceLiters - buffer.GetLiquidLiters();
        double servingLiters = (double)FluidUnits.MolToLiquidLiters(substance, FluidUnits.ServingsToMol(substance, 1m));
        if (freeLiters < servingLiters) { state.Status = "液体空间不足一份"; return; }
        double increment = Math.Max(0, Math.Min(definition.ThroughputLitersPerSecond * seconds, work / definition.WorkJoulesPerStandardLiter));
        state.WorldLiquidPumpLiters = Math.Min(servingLiters * 10d, state.WorldLiquidPumpLiters + increment);
        int requestedLevel = (int)Math.Min(sourceLevel, Math.Floor(Math.Min(freeLiters, state.WorldLiquidPumpLiters) / servingLiters));
        if (requestedLevel <= 0) { state.Status = "正在累计整份抽液"; return; }
        decimal plannedMoles = FluidUnits.ServingsToMol(substance, requestedLevel);
        FluidBatch planned = FluidInventory.CreateBatch(substance, 0, plannedMoles, AmbientTemperatureKelvin(node));
        if (!buffer.CanAdd(planned, definition.VolumeLiters, definition.MinimumGasSpaceLiters, true, true))
        { state.Status = "液相槽里还有其他物质"; return; }
        if (!TryPumpFluidEnvironmentLiquid(sourceWorld, sourcePosition, requestedLevel, out string liquidId, out int removedLevel)) return;
        decimal actualMoles = FluidUnits.ServingsToMol(substance, removedLevel);
        FluidBatch actual = FluidInventory.CreateBatch(substance, 0, actualMoles, AmbientTemperatureKelvin(node));
        if (!buffer.TryAdd(actual, definition.VolumeLiters, definition.MinimumGasSpaceLiters, true, true))
        {
            if (ReleaseFluidEnvironmentLiquid(sourceWorld, sourcePosition, liquidId, removedLevel) < removedLevel)
                throw new InvalidOperationException("世界液体抽取回滚无法恢复已扣液体。");
            throw new InvalidOperationException("世界液体抽取与泵缓冲提交失败。");
        }
        state.Status = "正在抽液体";
        double consumedLiters = (double)FluidUnits.MolToLiquidLiters(substance, actualMoles);
        state.WorldLiquidPumpLiters = Math.Max(0, state.WorldLiquidPumpLiters - consumedLiters);
        state.DissipatedHeatJoules += consumedLiters * definition.WorkJoulesPerStandardLiter;
    }

    // 容器事务已经给出拒绝原因，设备不能把所有失败都说成入口堵塞。
    internal static string GetLiquidPumpTransferFailureStatus(ContainerTransferFailure failure, FluidInventory receiver = null,
        FluidDefinition incoming = null, bool singleLiquid = false)
    {
        if (failure == ContainerTransferFailure.Full && incoming != null)
        {
            if (!incoming.AllowIndustrialTransport) return "这种液体不支持工业输送";
            if (singleLiquid && receiver != null)
                foreach (FluidComponentState component in receiver.State.Components)
                    if (component.LiquidMoles > 0 && component.FluidId != incoming.Id) return "液相槽里还有其他物质";
        }
        return failure switch
        {
            ContainerTransferFailure.Empty => "容器没有可抽取的液体",
            ContainerTransferFailure.Full => "液体接收空间不足",
            ContainerTransferFailure.FilterRejected => "容器端口不接受这种液体",
            ContainerTransferFailure.OutOfRange => "容器不在抽取范围内",
            ContainerTransferFailure.AccessDenied => "容器端口不允许机器抽取",
            ContainerTransferFailure.NoAuthority => "当前端没有容器操作权限",
            ContainerTransferFailure.StaleReference => "容器端口已失效，等待重新连接",
            ContainerTransferFailure.Changed => "容器状态已变化，等待重试",
            ContainerTransferFailure.Busy => "容器正在处理另一笔转移",
            ContainerTransferFailure.SameStorage => "不能在同一液体库存内转移",
            ContainerTransferFailure.UnsupportedAtomicity => "容器端口不支持原子转移",
            ContainerTransferFailure.CommitFailed => "液体转移提交失败，已回滚",
            ContainerTransferFailure.InvalidRequest => "液体转移参数无效",
            _ => "液体转移未完成"
        };
    }

    public static void AdvanceCompressor(MachineEntity node, float seconds)
    {
        FluidMachineState state = GetFluidState(node); FluidMachineDefinition definition = node.Definition.Fluid;
        FluidInventory input = GetFluidInventory(node), output = GetFluidInventory(node, "output");
        if (node.Logic is FluidMachineLogic portable && portable.PortableTank != null) FillPortableFluidTank(node, portable.PortableTank);
        double pressure = input.GetPressureKPa(definition.VolumeLiters, definition.MinimumGasSpaceLiters);
        if (pressure <= 0 || input.GasMoles <= 0) { state.Status = "入口没有气体"; return; }
        double work = AvailableDeviceWork(node, seconds);
        if (work <= 0) { state.Status = node.Definition.Electrical != null ? "没有电" : "没有动力"; return; }
        CompressorWorkspace workspace = compressorWorkspaces.GetValue(node, value => new CompressorWorkspace(value));
        if (!input.TryReserve(workspace.CompressionPort, FluidPhase.Gas,
            FluidUnits.StandardLitersToMol((decimal)Math.Min(definition.ThroughputLitersPerSecond * seconds, definition.BatchStandardLiters)), out FluidBatch batch)) return;
        double targetPressure = Math.Min(definition.MaximumPumpPressureKPa, Math.Max(state.CompressionTargetKPa,
            output.GetPressureKPa(definition.VolumeLiters, definition.MinimumGasSpaceLiters)));
        FluidCompressionResult compressed = FluidThermodynamics.Compress(batch, pressure, targetPressure, work, definition.CompressionEfficiency);
        if (!compressed.Success) { state.Status = "气体状态或压缩参数无效"; return; }
        if (output.GasMoles + compressed.Batch.GasMoles > FluidUnits.StandardLitersToMol((decimal)definition.GasBufferStandardLiters) ||
            !output.CanAdd(compressed.Batch, definition.VolumeLiters, definition.MinimumGasSpaceLiters, false, false))
        { state.Status = "压缩机出口被堵住了"; return; }
        workspace.Before.CopyFrom(input.State);
        if (!input.TryTakeReserved(workspace.CompressionPort, compressed.Batch.TotalMoles, out _)) return;
        if (!output.TryAdd(compressed.Batch, definition.VolumeLiters, definition.MinimumGasSpaceLiters, false, false))
        { input.Restore(workspace.Before); throw new InvalidOperationException("压缩机预算提交失败。"); }
        state.DissipatedHeatJoules += compressed.LossJoules; state.Status = "正在压缩";
    }

    private static void FillPortableFluidTank(MachineEntity node, Inventory inventory)
    {
        ItemData item = inventory.Data.itemSlots[0].itemData;
        if (item == null || !FluidTankStorage.TryGet(item, out FluidInventory target, out FluidTankConfiguration configuration)) return;
        FluidInventory source = GetFluidInventory(node, "output");
        if (!TryGetGasTransferBudget(source, out FluidInventoryState beginning, out double budget) || budget <= 0) return;
        string port = compressorWorkspaces.GetValue(node, value => new CompressorWorkspace(value)).PortablePort;
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
        SetGasTransferBudget(source, Math.Max(0, budget - (double)FluidUnits.MolToStandardLiters(moved.GasMoles)));
        FluidTankStorage.Write(item, target); inventory.Data.NotifyItemStateChanged(item);
    }
    private static decimal LimitPortableFillByPressure(FluidInventory target, FluidBatch batch, decimal maximum, double pressure,
        double volume, double minimum)
    {
        decimal low = 0, high = maximum;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            decimal middle = (low + high) / 2;
            FluidBatch candidate = batch.Scale(middle / batch.TotalMoles);
            if (target.TryGetPressureAfterAdding(candidate, volume, minimum, out double predicted) && predicted <= pressure) low = middle;
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
            throw new InvalidOperationException("氢能发动机已供能但实际燃料预算无法提交。");
        GetFluidState(node).Status = "正在燃烧供能";
    }
    public static void AdvanceFluidReaction(MachineEntity node, float seconds)
    {
        double available = AvailableDeviceWork(node, seconds);
        FluidMachineState state = GetFluidState(node);
        if (available <= 0) { state.Status = GetReactionBlockedStatus(node, seconds, available, false); return; }
        bool commitRejected = false;
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
            commitRejected = true;
        }
        state.Status = GetReactionBlockedStatus(node, seconds, available, commitRejected);
    }

    // 缺原料和满产物会令设备主动停止要电，状态仍由同一组真实反应条件判断。
    private static string GetReactionBlockedStatus(MachineEntity node, double seconds, double available, bool commitRejected)
    {
        bool hasInput = false, hasOutputSpace = false, hasFilter = false;
        foreach (FluidMachineReactionDefinition reaction in node.Definition.Fluid.Reactions)
        {
            if (GetReactionInputExtent(node, reaction, seconds) <= .000000001) continue;
            hasInput = true;
            if (GetReactionOutputExtent(node, reaction) <= .000000001) continue;
            hasOutputSpace = true;
            if (reaction.RequiresFilter && GetAvailableFilterWork(node) <= .000000001) continue;
            hasFilter = true;
        }
        if (!hasInput) return "缺少可用反应原料";
        if (!hasOutputSpace) return "产物空间不足，等待输出";
        if (!hasFilter) return "缺少可用滤材";
        if (available <= 0) return node.Definition.Electrical?.IsConsumer == true ? "没有电" : "没有动力";
        return commitRejected ? "反应提交被拒绝，原料已保留" : "可用动力不足以完成本轮反应";
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
        foreach (FluidReactionTerm input in CompileReaction(reaction).Inputs)
        {
            FluidInventory inventory = GetFluidInventory(node, input.Chamber);
            decimal available = inventory.GetAvailableMoles(input.FluidId, input.Phase == "gas" ? FluidPhase.Gas : FluidPhase.Liquid);
            result = Math.Min(result, (double)available / input.Moles);
        }
        return result;
    }
    public static double GetReactionOutputExtent(MachineEntity node, FluidMachineReactionDefinition reaction)
    {
        double result = double.PositiveInfinity;
        CompiledReaction compiled = CompileReaction(reaction);
        foreach (var gas in compiled.GasOutputs)
            result = Math.Min(result, Math.Max(0, (double)(FluidUnits.StandardLitersToMol((decimal)node.Definition.Fluid.GasBufferStandardLiters) - GetFluidInventory(node, gas.Chamber).GasMoles)) / gas.Moles);
        foreach (var liquid in compiled.LiquidOutputs)
            result = Math.Min(result, Math.Max(0, node.Definition.Fluid.VolumeLiters - node.Definition.Fluid.MinimumGasSpaceLiters - GetFluidInventory(node, liquid.Chamber).GetLiquidLiters()) / liquid.Liters);
        if (reaction.ResidueItemsPerReaction > 0)
        {
            if (!TryGetResiduePort(node, reaction, out IItemTransferPort residue, out ItemData product)) return 0;
            int capacity = residue.GetReceivableItems(product, int.MaxValue);
            if (capacity <= 0) return 0;
            result = Math.Min(result, Math.Max(0, capacity + 1d - GetFluidState(node).ResidueProgress.GetValueOrDefault(reaction.SolidResidueItemId) - .0000001) / reaction.ResidueItemsPerReaction);
        }
        return result;
    }
    private static bool CommitFluidReaction(MachineEntity node, FluidMachineReactionDefinition reaction, double extent, double work, double mechanicalWork)
    {
        if (!double.IsFinite(extent) || extent <= 0) return false;
        FluidMachineState state = GetFluidState(node);
        if (reaction.RequiresFilter && GetAvailableFilterWork(node) + .000000001 < extent) return false;
        double nextResidue = state.ResidueProgress.GetValueOrDefault(reaction.SolidResidueItemId) + extent * reaction.ResidueItemsPerReaction;
        double wholeResidue = Math.Floor(nextResidue + .000000001);
        // 完整残渣超出整型端口范围时先拒绝，不能扣掉原料后留下负数量或巨大尾量。
        if (!double.IsFinite(wholeResidue) || wholeResidue < 0d || wholeResidue > int.MaxValue) return false;
        int residueItems = (int)wholeResidue;
        IItemTransferPort residuePort = null; ItemData residueItem = null;
        if (reaction.ResidueItemsPerReaction > 0 && (!TryGetResiduePort(node, reaction, out residuePort, out residueItem) ||
            residuePort.GetReceivableItems(residueItem, residueItems) < residueItems)) return false;
        ReactionWorkspace snapshots = reactionWorkspaces.GetValue(node, _ => new ReactionWorkspace());
        snapshots.Begin();
        double inputEnergy = 0, inputMoles = 0, weightedTemperature = 0;
        foreach (FluidReactionTerm input in reaction.Inputs)
        {
            FluidInventory inventory = GetFluidInventory(node, input.Chamber);
            snapshots.Remember(inventory);
            decimal moles = (decimal)(extent * input.Moles);
            double inputTemperature = inventory.GetTemperatureKelvin();
            if (!inventory.TryTakeExact(input.FluidId, input.Phase == "gas" ? FluidPhase.Gas : FluidPhase.Liquid, moles, out FluidBatch batch))
            { snapshots.Restore(); return false; }
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
        if (!double.IsFinite(temperature) || temperature <= 0) { snapshots.Restore(); return false; }
        foreach (FluidReactionTerm output in reaction.Outputs)
        {
            FluidInventory inventory = GetFluidInventory(node, output.Chamber);
            snapshots.Remember(inventory);
            decimal moles = (decimal)(extent * output.Moles);
            FluidBatch batch = FluidInventory.CreateBatch(inventory.Catalog.Find(output.FluidId), output.Phase == "gas" ? moles : 0,
                output.Phase == "liquid" ? moles : 0, temperature);
            if (!inventory.TryAdd(batch, node.Definition.Fluid.VolumeLiters, node.Definition.Fluid.MinimumGasSpaceLiters, node.Definition.Fluid.SingleGas, node.Definition.Fluid.SingleLiquid))
            { snapshots.Restore(); return false; }
        }
        object residueBefore = residueItems > 0 ? residuePort.CaptureState() : null;
        if (residueItems > 0 && !residuePort.InsertItems(residueItem, residueItems))
        { residuePort.RestoreState(residueBefore); snapshots.Restore(); return false; }
        if (reaction.RequiresFilter && !EnsureFilterMaterial(node))
        {
            if (residueBefore != null) residuePort.RestoreState(residueBefore);
            snapshots.Restore(); return false;
        }
        if (reaction.RequiresFilter) state.FilterRemainingWork = Math.Max(0, state.FilterRemainingWork - extent);
        if (reaction.ResidueItemsPerReaction > 0) state.ResidueProgress[reaction.SolidResidueItemId] = Math.Max(0, nextResidue - residueItems);
        if (residueItems > 0) residuePort.PublishState();
        state.DissipatedHeatJoules += Math.Max(0, available - (offset + capacity * temperature));
        return true;
    }
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
            node.Position, UnityEngine.SceneManagement.SceneManager.GetSceneByName(WorldKey).handle);
        return true;
    }

    #endregion
}
