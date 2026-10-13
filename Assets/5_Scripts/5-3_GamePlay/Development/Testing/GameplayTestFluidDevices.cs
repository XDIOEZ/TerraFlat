using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine.SceneManagement;

/// <summary>设备用例准备已供能边界后调用正式事务，隔离作用域保存真实原料与产物。</summary>
internal static class GameplayTestFluidDevices
{
    #region 设备用例入口与夹具准备
    internal static readonly Dictionary<string, Action<Action<string, bool, string>>> Cases = new(StringComparer.Ordinal)
    {
        ["device_gas_pump"] = GasPump,
        ["device_liquid_pump"] = LiquidPump,
        ["device_compressor_mechanical"] = report => Compressor(report, "GasCompressor_Mechanical"),
        ["device_compressor_electric"] = report => Compressor(report, "GasCompressor_Electric"),
        ["device_compressor_controls"] = CompressorControls,
        ["device_portable_fill"] = PortableFill,
        ["device_electrical_compressor"] = ElectricalCompressor,
        ["device_filter"] = Filter,
        ["device_filter_blocked"] = FilterBlocked,
        ["device_electrolysis_recipes"] = ElectrolysisRecipes,
        ["device_electrolysis_blocked"] = ElectrolysisBlocked,
        ["device_electrolysis_residue_port"] = ElectrolysisResiduePort,
        ["device_engine_blocked"] = EngineBlocked,
        ["device_engine_work"] = EngineWork
    };

    private static void Check(Action<string, bool, string> report, string label, bool success, string message = null)
        => report(label, success, message ?? (success ? "通过" : "实际结果不符合预期"));
    private static void Require(Action<string, bool, string> report, string label, bool success)
    {
        Check(report, label, success);
        if (!success) throw new InvalidOperationException("流体设备用例必要前提失败：" + label);
    }
    private static bool Near(double actual, double expected, double tolerance = 1e-8)
        => FluidUnits.IsFinite(actual) && FluidUnits.IsFinite(expected) &&
           Math.Abs(actual - expected) <= tolerance * Math.Max(1d, Math.Max(Math.Abs(actual), Math.Abs(expected)));
    private static string Payload(FluidInventory inventory) => JsonConvert.SerializeObject(inventory.State);
    private static FluidMachineState State(MachineEntity node) => MachineWorld.GetFluidState(node);
    private static FluidInventory Inventory(MachineEntity node, string chamber = "main") => MachineWorld.GetFluidInventory(node, chamber);
    private static FluidMachineLogic Logic(Action<string, bool, string> report, MachineEntity node)
    {
        Require(report, "设备使用正式流体领域逻辑", node.Logic is FluidMachineLogic);
        return (FluidMachineLogic)node.Logic;
    }
    private static void AddFluid(Action<string, bool, string> report, MachineEntity node, string fluidId, decimal gas,
        decimal liquid = 0m, string chamber = "main")
    {
        FluidInventory inventory = Inventory(node, chamber);
        Require(report, "装入实际设备原料 " + fluidId, inventory.TryAdd(
            FluidInventory.CreateBatch(inventory.Catalog.Find(fluidId), gas, liquid, 300d),
            MachineWorld.GetFluidVolumeLiters(node), MachineWorld.GetFluidMinimumGasSpaceLiters(node)));
    }
    private static void SetPower(MachineEntity node, bool enabled)
    {
        if (node.Definition.Electrical?.IsConsumer == true)
            node.ElectricalSuppliedWatts = enabled ? node.Definition.Electrical.PowerWatts : 0f;
        else { node.LoadSatisfied = enabled; node.Rpm = enabled ? 2000f : 0f; }
    }
    private static void SetItem(Action<string, bool, string> report, Inventory inventory, string itemId, float amount = 1f)
    {
        Require(report, "设备物品库存存在 " + itemId, inventory != null);
        Require(report, "实际物品定义存在 " + itemId, GameRes.ExistingInstance.TryGetItemDefinition(itemId, out RuntimeItemDefinition definition));
        ItemData data = definition.CreateItemData(); data.Stack.Amount = amount;
        inventory.Data.SetOne_ItemData(0, data);
    }
    private static void FillLiquidCapacity(Action<string, bool, string> report, MachineEntity node, string fluidId, string chamber)
    {
        FluidDefinition fluid = FluidCatalog.Default.Find(fluidId);
        decimal liters = (decimal)(MachineWorld.GetFluidVolumeLiters(node) - MachineWorld.GetFluidMinimumGasSpaceLiters(node));
        AddFluid(report, node, fluidId, 0m, FluidUnits.LiquidLitersToMol(fluid, liters), chamber);
    }
    private static void FillGasBuffer(Action<string, bool, string> report, MachineEntity node, string chamber)
        => AddFluid(report, node, FluidIds.Oxygen,
            FluidUnits.StandardLitersToMol((decimal)node.Definition.Fluid.GasBufferStandardLiters), 0m, chamber);
    #endregion

    #region 气泵与液泵的真实输入边界
    private static void GasPump(Action<string, bool, string> report)
    {
        using var fixture = new MachineWorld.GameplayFluidScope();
        MachineEntity pump = fixture.Add("GasPump_Mechanical");
        MachineEntity target = fixture.Add("GasTankBlock_Iron", 1);
        fixture.Begin();
        FluidInventory buffer = Inventory(pump);
        decimal atmosphere = fixture.Atmosphere.CurrentMoles;
        SetPower(pump, false); FluidDeviceOperations.AdvanceGasPump(pump, 1f);
        Check(report, "气泵无实际动力不抽大气", buffer.IsEmpty && fixture.Atmosphere.CurrentMoles == atmosphere && State(pump).PumpProgress == 0d);

        SetPower(pump, true); FluidDeviceOperations.AdvanceGasPump(pump, 1f);
        Require(report, "气泵完成真实抽气批次", buffer.GasMoles > 0m);
        Check(report, "气泵只扣实际抽入大气量", atmosphere - fixture.Atmosphere.CurrentMoles == buffer.GasMoles);
        Check(report, "气泵每批按大气分辨率量化", buffer.GasMoles == AtmosphereService.Quantize(
            FluidUnits.StandardLitersToMol((decimal)pump.Definition.Fluid.BatchStandardLiters)));
        Check(report, "已完成气泵批次清理进度和待结算做功", State(pump).PumpProgress == 0d && State(pump).PumpStoredWorkJoules == 0d);
        Check(report, "气泵真实做功记录非零散热", State(pump).DissipatedHeatJoules > 0d);
        string before = Payload(buffer); atmosphere = fixture.Atmosphere.CurrentMoles;
        FluidDeviceOperations.AdvanceGasPump(pump, 1f);
        Check(report, "气泵有缓冲时不能重复抽取", Payload(buffer) == before && fixture.Atmosphere.CurrentMoles == atmosphere);

        buffer.Drain(); AddFluid(report, target, FluidIds.PureWater, 0m, 1m);
        Check(report, "出口真实液体令气泵判湿", FluidDeviceOperations.IsGasPumpWet(pump));
        FluidDeviceOperations.AdvanceGasPump(pump, 1f);
        Check(report, "湿阻不消耗大气或累积进度", buffer.IsEmpty && fixture.Atmosphere.CurrentMoles == atmosphere && State(pump).PumpProgress == 0d);
        Inventory(target).Drain();
        decimal highPressureMoles = (decimal)(pump.Definition.Fluid.MaximumPumpPressureKPa * 2d *
            MachineWorld.GetFluidVolumeLiters(target) / (FluidUnits.GasConstant * 300d));
        AddFluid(report, target, FluidIds.Oxygen, highPressureMoles);
        FluidDeviceOperations.AdvanceGasPump(pump, 1f);
        Check(report, "出口超过泵压上限停止抽气", buffer.IsEmpty && fixture.Atmosphere.CurrentMoles == atmosphere && State(pump).Status == "气泵出口被堵住了");
        Inventory(target).Drain(); fixture.Atmosphere.Gases.Clear();
        FluidDeviceOperations.AdvanceGasPump(pump, 1f);
        Check(report, "空大气不能生成免费气体", buffer.IsEmpty && State(pump).Status == "没有可抽取的大气");
        AddFluid(report, pump, FluidIds.PureWater, 0m, 1m);
        Check(report, "泵自身液相库存同样判湿", FluidDeviceOperations.IsGasPumpWet(pump));
    }

    private static void LiquidPump(Action<string, bool, string> report)
    {
        using var fixture = new MachineWorld.GameplayFluidScope();
        MachineEntity source = fixture.Add("LiquidStorageTank", -1);
        MachineEntity pump = fixture.Add("LiquidPump_Electric");
        fixture.Add("LiquidStorageTank", 1);
        fixture.Rebuild(); AddFluid(report, source, FluidIds.PureWater, 0m, 20m);
        fixture.Begin();
        FluidInventory origin = Inventory(source), buffer = Inventory(pump);
        decimal originalMoles = origin.LiquidMoles;
        SetPower(pump, false); FluidDeviceOperations.AdvanceLiquidPump(pump, fixture.Seconds);
        Check(report, "液泵无电不执行抽取", buffer.IsEmpty && origin.LiquidMoles == originalMoles && State(pump).Status == "没有电");
        SetPower(pump, true); FluidDeviceOperations.AdvanceLiquidPump(pump, fixture.Seconds);
        Check(report, "工业入口等待正式管网而不旁路扣料", buffer.IsEmpty && origin.LiquidMoles == originalMoles && State(pump).Status == "等待管网输入");
        fixture.Complete();
        Check(report, "正式网络阶段向液泵输入起始液体", buffer.LiquidMoles > 0m && origin.LiquidMoles < originalMoles);
        Check(report, "工业输入物质量守恒", origin.LiquidMoles + buffer.LiquidMoles == originalMoles);
        decimal after = buffer.LiquidMoles;
        FluidDeviceOperations.AdvanceLiquidPump(pump, fixture.Seconds);
        Check(report, "同轮不能通过液泵操作重复抽工业库存", buffer.LiquidMoles == after);
    }
    #endregion

    #region 压缩机实际供能与控制
    private static void Compressor(Action<string, bool, string> report, string itemId)
    {
        using var fixture = new MachineWorld.GameplayFluidScope();
        MachineEntity node = fixture.Add(itemId); fixture.Begin();
        AddFluid(report, node, FluidIds.Oxygen, 1m);
        FluidInventory input = Inventory(node), output = Inventory(node, "output");
        string before = Payload(input);
        SetPower(node, false); FluidDeviceOperations.AdvanceCompressor(node, fixture.Seconds);
        Check(report, "压缩机断供不扣入口", Payload(input) == before && output.IsEmpty);
        SetPower(node, true);
        decimal originalMoles = input.GasMoles;
        double originalEnergy = input.State.TotalInternalEnergyJoules;
        double loss = State(node).DissipatedHeatJoules;
        FluidDeviceOperations.AdvanceCompressor(node, fixture.Seconds);
        Check(report, "压缩机供能后提交真实气体", output.GasMoles > 0m && input.GasMoles < originalMoles);
        Check(report, "压缩入口和出口物质量守恒", input.GasMoles + output.GasMoles == originalMoles);
        Check(report, "压缩做功增加库存内能且另记散热", input.State.TotalInternalEnergyJoules + output.State.TotalInternalEnergyJoules > originalEnergy && State(node).DissipatedHeatJoules > loss);

        input.Drain(); output.Drain(); AddFluid(report, node, FluidIds.Oxygen, 1m);
        FillGasBuffer(report, node, "output");
        originalMoles = input.GasMoles; originalEnergy = input.State.TotalInternalEnergyJoules;
        before = Payload(output); loss = State(node).DissipatedHeatJoules;
        FluidDeviceOperations.AdvanceCompressor(node, fixture.Seconds);
        Check(report, "满压缩出口不能损失入口物料", input.GasMoles == originalMoles && input.State.TotalInternalEnergyJoules == originalEnergy);
        Check(report, "满压缩出口不产生额外产物或散热", Payload(output) == before && State(node).DissipatedHeatJoules == loss);
        Check(report, "堵塞保留待压缩批次", input.State.Ports.Count > 0 && State(node).Status == "压缩机出口被堵住了");
    }

    private static void CompressorControls(Action<string, bool, string> report)
    {
        using var fixture = new MachineWorld.GameplayFluidScope();
        MachineEntity node = fixture.Add("GasCompressor_Electric"); fixture.Begin();
        FluidDeviceBehavior behavior = MachineWorld.GetFluidDeviceBehavior(node);
        Check(report, "充装压力允许有效显式值", behavior.Execute(node, "fluid.pressure", "600") && State(node).CompressionTargetKPa == 600d);
        double pressure = State(node).CompressionTargetKPa;
        foreach (string value in new[] { "0", "-1", "NaN", "Infinity", "999999", "abc" })
            Check(report, "充装压力拒绝非法参数 " + value, !behavior.Execute(node, "fluid.pressure", value) && State(node).CompressionTargetKPa == pressure);
        Check(report, "快捷调整压力以真实设备上限截断", behavior.Execute(node, "fluid.pressure", null) && State(node).CompressionTargetKPa == 700d);
        State(node).CompressionTargetKPa = node.Definition.Fluid.MaximumPumpPressureKPa;
        Check(report, "达到泵压上限后不继续增长", behavior.Execute(node, "fluid.pressure", null) && State(node).CompressionTargetKPa == node.Definition.Fluid.MaximumPumpPressureKPa);
        Check(report, "空压缩入口没有电力需求", behavior.ElectricalDemand(node) == 0f);
        AddFluid(report, node, FluidIds.Oxygen, 1m);
        Check(report, "有实际入口气体才产生电力需求", behavior.ElectricalDemand(node) > 0f);
    }

    private static void PortableFill(Action<string, bool, string> report)
    {
        using var fixture = new MachineWorld.GameplayFluidScope();
        MachineEntity node = fixture.Add("GasCompressor_Electric"); fixture.Rebuild();
        FluidMachineLogic logic = Logic(report, node);
        SetItem(report, logic.PortableTank, "PortableGasTank_Iron");
        ItemData item = logic.PortableTank.Data.itemSlots[0].itemData;
        Require(report, "实际便携罐二进制载荷可初始化", FluidTankStorage.TryGet(item,
            out FluidInventory portable, out FluidTankConfiguration configuration));
        Check(report, "实际便携罐初始真空", portable.IsEmpty);
        AddFluid(report, node, FluidIds.Oxygen, 1m, 0m, "output");
        State(node).CompressionTargetKPa = 5d;
        fixture.Begin(); SetPower(node, false);
        FluidInventory buffer = Inventory(node, "output");
        decimal total = buffer.GasMoles;
        double energy = buffer.State.TotalInternalEnergyJoules;
        FluidDeviceOperations.AdvanceCompressor(node, fixture.Seconds);
        Require(report, "断供时可以搬运起始真实缓冲气体", FluidTankStorage.TryGet(item, out portable, out configuration) && portable.GasMoles > 0m);
        double pressure = portable.GetPressureKPa(configuration.VolumeLiters, configuration.MinimumGasSpaceLiters);
        Check(report, "便携充装不能超过设定目标压力", pressure <= State(node).CompressionTargetKPa + 1e-8 && Near(pressure, State(node).CompressionTargetKPa));
        Check(report, "便携充装摩尔量守恒", buffer.GasMoles + portable.GasMoles == total);
        Check(report, "只搬缓冲气体时内能守恒", Near(buffer.State.TotalInternalEnergyJoules + portable.State.TotalInternalEnergyJoules, energy));
        decimal filled = portable.GasMoles;
        FluidDeviceOperations.AdvanceCompressor(node, fixture.Seconds);
        Require(report, "再次读取实际便携载荷", FluidTankStorage.TryGet(item, out portable, out configuration));
        Check(report, "达到目标压力后不持续超充", Near((double)portable.GasMoles, (double)filled, 1e-10) &&
            portable.GetPressureKPa(configuration.VolumeLiters, configuration.MinimumGasSpaceLiters) <= 5d + 1e-8);

        buffer.Drain(); portable.Drain(); FluidTankStorage.Write(item, portable);
        AddFluid(report, node, FluidIds.Oxygen, 1m);
        State(node).CompressionTargetKPa = 500d;
        fixture.Begin(); SetPower(node, true);
        FluidDeviceOperations.AdvanceCompressor(node, fixture.Seconds);
        Require(report, "真实压缩产生新的输出库存", Inventory(node, "output").GasMoles > 0m);
        FluidDeviceOperations.AdvanceCompressor(node, fixture.Seconds);
        Require(report, "同轮读取便携罐状态", FluidTankStorage.TryGet(item, out portable, out configuration));
        Check(report, "本轮新压缩输出不能越过冻结预算立即充装", portable.IsEmpty);
        fixture.Begin(); SetPower(node, false);
        decimal input = Inventory(node).GasMoles;
        total = input + Inventory(node, "output").GasMoles;
        FluidDeviceOperations.AdvanceCompressor(node, fixture.Seconds);
        Require(report, "下一轮可以充装已存在的输出气体", FluidTankStorage.TryGet(item, out portable, out configuration) && portable.GasMoles > 0m);
        Check(report, "充装旧缓冲不伪造断供压缩过程", Inventory(node).GasMoles == input);
        Check(report, "压缩缓冲和便携载荷物质量共同守恒", Inventory(node).GasMoles + Inventory(node, "output").GasMoles + portable.GasMoles == total);
    }

    private static void ElectricalCompressor(Action<string, bool, string> report)
    {
        using var fixture = new MachineWorld.GameplayFluidScope();
        MachineEntity compressor = fixture.Add("GasCompressor_Electric");
        MachineEntity battery = fixture.Add("Battery_Basic", -1);
        fixture.Add("Wire_Copper", -1); fixture.Add("Wire_Copper"); fixture.Rebuild();
        AddFluid(report, compressor, FluidIds.Oxygen, 1m);
        battery.ElectricalStoredJoules = battery.Definition.Electrical.CapacityJoules;
        battery.State.ElectricalStoredJoules = battery.ElectricalStoredJoules;
        float energy = battery.ElectricalStoredJoules;
        decimal originalMoles = Inventory(compressor).GasMoles;
        fixture.Tick();
        Check(report, "真实电线连接电池与压缩机", compressor.ElectricalNetwork != null &&
            ReferenceEquals(compressor.ElectricalNetwork, battery.ElectricalNetwork));
        Check(report, "真实电网解算向压缩机供电", compressor.ElectricalSuppliedWatts > 0f && compressor.ElectricalVoltage > 0f);
        Check(report, "真实调度用电后压缩气体", Inventory(compressor, "output").GasMoles > 0m && Inventory(compressor).GasMoles < originalMoles);
        Check(report, "压缩功率受电池实际放电上限约束", compressor.ElectricalSuppliedWatts <= battery.Definition.Electrical.MaxDischargeWatts + 1e-6f);
        Check(report, "电池实际扣能等于本轮供电", Near(energy - battery.ElectricalStoredJoules,
            compressor.ElectricalSuppliedWatts * fixture.Seconds, 1e-6));
        battery.ElectricalStoredJoules = 0f; battery.State.ElectricalStoredJoules = 0f;
        string input = Payload(Inventory(compressor)), output = Payload(Inventory(compressor, "output"));
        fixture.Tick();
        Check(report, "电池耗尽后真实电网停止供电", compressor.ElectricalSuppliedWatts == 0f);
        Check(report, "电池耗尽不继续消耗压缩原料", Payload(Inventory(compressor)) == input && Payload(Inventory(compressor, "output")) == output);
    }
    #endregion

    #region 过滤耗材残渣与满产物保护
    private static void Filter(Action<string, bool, string> report)
    {
        using var fixture = new MachineWorld.GameplayFluidScope();
        MachineEntity node = fixture.Add("FluidFilter"); fixture.Begin();
        FluidMachineReactionDefinition reaction = node.Definition.Fluid.Reactions[0];
        FluidMachineLogic logic = Logic(report, node);
        FluidReactionTerm input = reaction.Inputs[0];
        AddFluid(report, node, input.FluidId, 0m, 10m, input.Chamber);
        SetPower(node, true);
        string before = Payload(Inventory(node, input.Chamber));
        FluidDeviceOperations.AdvanceFluidReaction(node, 2f);
        Check(report, "没有真实滤材不消耗脏水", Payload(Inventory(node, input.Chamber)) == before && Inventory(node, "output").IsEmpty);
        SetItem(report, logic.FilterMaterial, node.Definition.Fluid.FilterMaterialItemId);
        decimal original = Inventory(node, input.Chamber).LiquidMoles;
        FluidDeviceOperations.AdvanceFluidReaction(node, 2f);
        decimal consumed = original - Inventory(node, input.Chamber).LiquidMoles;
        Require(report, "装入真实滤材后可以加工", consumed > 0m);
        double extent = (double)consumed / input.Moles;
        foreach (FluidReactionTerm product in reaction.Outputs)
            Check(report, "过滤产物按实际配方比例生成 " + product.FluidId,
                Near((double)Inventory(node, product.Chamber).GetLiquidMoles(product.FluidId), extent * product.Moles));
        Check(report, "首次加工只扣一份真实滤材", logic.FilterMaterial.Data.itemSlots[0].itemData == null &&
            Near(State(node).FilterRemainingWork, node.Definition.Fluid.FilterWorkPerItem - extent));
        ItemData residue = logic.Residue.Data.itemSlots[0].itemData;
        double residueAmount = residue?.Stack.Amount ?? 0f;
        double pending = State(node).ResidueProgress.GetValueOrDefault(reaction.SolidResidueItemId);
        Check(report, "残渣整份和小数进度共同守恒", Near(residueAmount + pending, extent * reaction.ResidueItemsPerReaction));
        Check(report, "残渣槽存放实际定义物品", residue != null && residue.IDName == reaction.SolidResidueItemId && residueAmount >= 1d);
    }

    private static void FilterBlocked(Action<string, bool, string> report)
    {
        using var fixture = new MachineWorld.GameplayFluidScope();
        MachineEntity node = fixture.Add("FluidFilter"); fixture.Begin();
        FluidMachineReactionDefinition reaction = node.Definition.Fluid.Reactions[0];
        FluidMachineLogic logic = Logic(report, node);
        AddFluid(report, node, reaction.Inputs[0].FluidId, 0m, 10m, reaction.Inputs[0].Chamber);
        SetItem(report, logic.FilterMaterial, node.Definition.Fluid.FilterMaterialItemId);
        SetPower(node, true);
        FillLiquidCapacity(report, node, reaction.Outputs[0].FluidId, reaction.Outputs[0].Chamber);
        string input = Payload(Inventory(node));
        FluidDeviceOperations.AdvanceFluidReaction(node, 2f);
        Check(report, "过滤液体出口满时不扣原料", Payload(Inventory(node)) == input);
        Check(report, "过滤液体出口满时不扣滤材", logic.FilterMaterial.Data.itemSlots[0].itemData?.Stack.Amount == 1f && State(node).FilterRemainingWork == 0d);
        Inventory(node, reaction.Outputs[0].Chamber).Drain();
        SetItem(report, logic.Residue, node.Definition.Fluid.FilterMaterialItemId);
        input = Payload(Inventory(node));
        FluidDeviceOperations.AdvanceFluidReaction(node, 2f);
        Check(report, "残渣槽被其他物品占据时不扣原料", Payload(Inventory(node)) == input && Inventory(node, "output").IsEmpty);
        Check(report, "残渣堵塞保持真实滤材和残渣进度", logic.FilterMaterial.Data.itemSlots[0].itemData?.Stack.Amount == 1f && State(node).ResidueProgress.Count == 0);
    }
    #endregion

    #region 实际电解配方与独立液碱取液口
    private static void ElectrolysisRecipes(Action<string, bool, string> report)
    {
        int count;
        using (var fixture = new MachineWorld.GameplayFluidScope())
            count = fixture.Add("WaterElectrolyzer").Definition.Fluid.Reactions.Length;
        Require(report, "电解器存在实际配方", count > 0);
        for (int index = 0; index < count; index++)
        {
            using var fixture = new MachineWorld.GameplayFluidScope();
            MachineEntity node = fixture.Add("WaterElectrolyzer"); fixture.Begin();
            FluidMachineReactionDefinition reaction = node.Definition.Fluid.Reactions[index];
            FluidReactionTerm input = reaction.Inputs[0];
            Require(report, "实际电解配方为单液体入口 " + reaction.Id, reaction.Inputs.Length == 1 && input.Phase == "liquid");
            AddFluid(report, node, input.FluidId, 0m, 1m, input.Chamber);
            SetPower(node, true);
            decimal original = Inventory(node, input.Chamber).GetLiquidMoles(input.FluidId);
            FluidDeviceOperations.AdvanceFluidReaction(node, 1f);
            decimal consumed = original - Inventory(node, input.Chamber).GetLiquidMoles(input.FluidId);
            Require(report, "实际电解配方成功提交 " + reaction.Id, consumed > 0m);
            double extent = (double)consumed / input.Moles;
            Check(report, "电解受实际速度和功率预算限制 " + reaction.Id,
                extent <= reaction.MolesPerSecond + 1e-9 && extent * reaction.RequiredJoulesPerReaction <= node.ElectricalSuppliedWatts + 1e-7);
            foreach (FluidReactionTerm product in reaction.Outputs)
            {
                decimal actual = product.Phase == "gas" ? Inventory(node, product.Chamber).GetGasMoles(product.FluidId)
                    : Inventory(node, product.Chamber).GetLiquidMoles(product.FluidId);
                Check(report, "电解产物真实比例 " + reaction.Id + " " + product.FluidId, Near((double)actual, extent * product.Moles));
            }
            if (reaction.ResidueItemsPerReaction > 0d)
            {
                FluidMachineLogic logic = Logic(report, node);
                double solid = logic.Residue.Data.itemSlots[0].itemData?.Stack.Amount ?? 0f;
                Check(report, "含杂质配方保留残渣尾量 " + reaction.Id, Near(solid +
                    State(node).ResidueProgress.GetValueOrDefault(reaction.SolidResidueItemId), extent * reaction.ResidueItemsPerReaction));
            }
        }
    }

    private static void ElectrolysisBlocked(Action<string, bool, string> report)
    {
        using var fixture = new MachineWorld.GameplayFluidScope();
        MachineEntity node = fixture.Add("WaterElectrolyzer"); fixture.Begin();
        AddFluid(report, node, FluidIds.PureWater, 0m, 1m);
        string before = Payload(Inventory(node));
        SetPower(node, false); FluidDeviceOperations.AdvanceFluidReaction(node, fixture.Seconds);
        Check(report, "电解无电不扣水或生成气体", Payload(Inventory(node)) == before && Inventory(node, "output").IsEmpty);
        SetPower(node, true); FillGasBuffer(report, node, "output");
        string output = Payload(Inventory(node, "output"));
        FluidDeviceOperations.AdvanceFluidReaction(node, fixture.Seconds);
        Check(report, "电解混气出口满时原子保留原料", Payload(Inventory(node)) == before && Payload(Inventory(node, "output")) == output);
        Inventory(node).Drain(); Inventory(node, "output").Drain();
        FluidMachineReactionDefinition saltReaction = Array.Find(node.Definition.Fluid.Reactions,
            reaction => Array.Exists(reaction.Outputs, term => term.Phase == "liquid"));
        Require(report, "存在真实液碱副产物配方", saltReaction != null);
        FluidReactionTerm byproduct = Array.Find(saltReaction.Outputs, term => term.Phase == "liquid");
        AddFluid(report, node, saltReaction.Inputs[0].FluidId, 0m, 1m);
        FillLiquidCapacity(report, node, byproduct.FluidId, byproduct.Chamber);
        before = Payload(Inventory(node));
        FluidDeviceOperations.AdvanceFluidReaction(node, fixture.Seconds);
        Check(report, "液碱腔满时不能只生成混气", Payload(Inventory(node)) == before && Inventory(node, "output").IsEmpty);
    }

    private static void ElectrolysisResiduePort(Action<string, bool, string> report)
    {
        using var fixture = new MachineWorld.GameplayFluidScope();
        MachineEntity node = fixture.Add("WaterElectrolyzer");
        MachineEntity target = fixture.Add("LiquidStorageTank", 1);
        fixture.Begin();
        FluidMachineLogic logic = Logic(report, node), targetLogic = Logic(report, target);
        FluidMachineReactionDefinition reaction = Array.Find(node.Definition.Fluid.Reactions,
            value => Array.Exists(value.Outputs, term => term.Phase == "liquid"));
        Require(report, "液碱实际配方存在", reaction != null);
        FluidReactionTerm byproduct = Array.Find(reaction.Outputs, term => term.Phase == "liquid");
        AddFluid(report, node, reaction.Inputs[0].FluidId, 0m, 1m);
        SetPower(node, true); FluidDeviceOperations.AdvanceFluidReaction(node, 1f);
        ILiquidTransferPort source = logic.LiquidResiduePort;
        Require(report, "电解副产物拥有真实取液端口", source != null && source.PeekLiquid(out _));
        Check(report, "液碱取液口声明独立出液方向", source.Reference.PortId == FluidMachineDefinition.LiquidResiduePortId && source.Configuration.Direction == ContainerPortDirection.Output);
        Check(report, "液碱取液口不向相邻机器自动开放", (source.Configuration.Access & ContainerAccessKind.Machine) == 0);
        var receiver = new IndustrialLiquidTransferPort(targetLogic, target.Definition.Fluid.Ports[0]);
        int scene = SceneManager.GetSceneByName(fixture.Key).handle;
        var machineContext = new ContainerTransferContext(node.Position, scene, ContainerAccessKind.Machine, "流体测试机器访问副产物");
        Check(report, "机器访问副产物取液口被拒绝", !ContainerTransferService.PreviewLiquid(source, receiver, machineContext, 1f).Success);
        decimal amount = Inventory(node, byproduct.Chamber).LiquidMoles;
        decimal gas = Inventory(node, "output").GasMoles;
        var context = new ContainerTransferContext(node.Position, scene, ContainerAccessKind.Mod, "流体测试副产物原子转移");
        ContainerTransferResult result = ContainerTransferService.TransferLiquid(source, receiver, context, 1f);
        Check(report, "正式端口事务可以取出真实液碱", result.Success && Inventory(target).GetLiquidMoles(byproduct.FluidId) > 0m, result.Failure.ToString());
        Check(report, "副产物取液前后物质量守恒", Near((double)(Inventory(node, byproduct.Chamber).LiquidMoles + Inventory(target).LiquidMoles), (double)amount));
        Check(report, "取液碱不扣混气出口", Inventory(node, "output").GasMoles == gas);
    }
    #endregion

    #region 氢能发动机缺料保护与实际负载结算
    private static void EngineBlocked(Action<string, bool, string> report)
    {
        using var fixture = new MachineWorld.GameplayFluidScope();
        MachineEntity node = fixture.Add("GasEngine"); fixture.Begin();
        AddFluid(report, node, FluidIds.Hydrogen, 1m);
        Check(report, "发动机缺氧没有可供机械功率", FluidDeviceOperations.CalculateAvailableEnginePower(node, fixture.Seconds) == 0d);
        AddFluid(report, node, FluidIds.Oxygen, .5m, 0m, "oxygen");
        Require(report, "齐气发动机具有真实可供功率", FluidDeviceOperations.CalculateAvailableEnginePower(node, fixture.Seconds) > 0d);
        fixture.SetEngineWork(node, FluidDeviceOperations.CalculateAvailableEnginePower(node, fixture.Seconds));
        string hydrogen = Payload(Inventory(node)), oxygen = Payload(Inventory(node, "oxygen"));
        node.Rpm = 0f;
        FluidDeviceOperations.SettleFluidEngine(node, fixture.Seconds);
        Check(report, "发动机无转速不烧气", Payload(Inventory(node)) == hydrogen && Payload(Inventory(node, "oxygen")) == oxygen);
        node.Rpm = node.Definition.Rpm;
        Require(report, "发动机存在正式机械网络", node.Network != null);
        node.Network.TorqueDemand = 0f;
        FluidDeviceOperations.SettleFluidEngine(node, fixture.Seconds);
        Check(report, "发动机没有有效负载不烧气", Payload(Inventory(node)) == hydrogen && Payload(Inventory(node, "oxygen")) == oxygen);
        FillLiquidCapacity(report, node, FluidIds.PureWater, "water");
        Check(report, "回收水腔满时发动机可供功率归零", Near(FluidDeviceOperations.CalculateAvailableEnginePower(node, fixture.Seconds), 0d));
        Check(report, "发动机功率探算不改变气体库存", Payload(Inventory(node)) == hydrogen && Payload(Inventory(node, "oxygen")) == oxygen);
        Inventory(node).Drain(); Inventory(node, "water").Drain();
        Check(report, "发动机缺氢同样没有可供机械功率", FluidDeviceOperations.CalculateAvailableEnginePower(node, fixture.Seconds) == 0d);
    }

    private static void EngineWork(Action<string, bool, string> report)
    {
        using var fixture = new MachineWorld.GameplayFluidScope();
        MachineEntity node = fixture.Add("GasEngine");
        MachineEntity load = fixture.Add("GasPump_Mechanical", 1); fixture.Begin();
        AddFluid(report, node, FluidIds.Hydrogen, 1m);
        AddFluid(report, node, FluidIds.Oxygen, .5m, 0m, "oxygen");
        double available = FluidDeviceOperations.CalculateAvailableEnginePower(node, fixture.Seconds);
        Require(report, "发动机真实反应预算提供机械功率", available > 0d);
        fixture.SetEngineWork(node, available);
        Require(report, "发动机与负载具有正式机械网络", node.Network != null);
        // 已解算供能边界仅准备实际负载读数，燃料和产物仍由生产事务结算。
        node.Network.TorqueDemand = load.LoadTorque;
        node.Rpm = node.Definition.Rpm; node.SourceFactor = 1f; node.TorqueRatio = 1f;
        node.RemainingSourceTorque = node.SourceTorque - load.LoadTorque;
        decimal hydrogenBefore = Inventory(node).GasMoles, oxygenBefore = Inventory(node, "oxygen").GasMoles;
        double energyBefore = Inventory(node).State.TotalInternalEnergyJoules + Inventory(node, "oxygen").State.TotalInternalEnergyJoules;
        double dissipatedBefore = State(node).DissipatedHeatJoules;
        double mechanicalWork = load.LoadTorque * node.SpeedRpm * MachineCatalog.Settings.WattsPerTorqueRpm * fixture.Seconds;
        FluidDeviceOperations.SettleFluidEngine(node, fixture.Seconds);
        decimal hydrogen = hydrogenBefore - Inventory(node).GasMoles;
        decimal oxygen = oxygenBefore - Inventory(node, "oxygen").GasMoles;
        Check(report, "实际负载发动机才消耗气体", hydrogen > 0m && oxygen > 0m && State(node).Status == "正在燃烧供能");
        Check(report, "发动机按氢氧二比一真实摩尔量燃烧", Near((double)hydrogen, (double)oxygen * 2d));
        Check(report, "发动机生成真实回收水", Near((double)Inventory(node, "water").LiquidMoles, (double)hydrogen));
        double actualEnergy = Inventory(node).State.TotalInternalEnergyJoules + Inventory(node, "oxygen").State.TotalInternalEnergyJoules + Inventory(node, "water").State.TotalInternalEnergyJoules;
        double chemicalEnergy = (double)hydrogen * node.Definition.Fluid.Reactions[0].ChemicalJoulesPerReaction;
        Check(report, "化学能按库存内能实际机械功和散热守恒", Near(actualEnergy + mechanicalWork + State(node).DissipatedHeatJoules - dissipatedBefore,
            energyBefore + chemicalEnergy));
        Check(report, "实际机械功率不越过可供燃料预算", mechanicalWork <= available * fixture.Seconds + 1e-8);
    }
    #endregion
}
