using System;
using System.Collections.Generic;
using System.IO;
using MemoryPack;
using Newtonsoft.Json;

/// <summary>Lua 流体测试复用正式库存与热力学接口，所有载荷只存在于当前用例。</summary>
internal static class GameplayTestFluidCore
{
    #region 核心用例入口与断言
    internal static readonly Dictionary<string, Action<Action<string, bool, string>>> Cases = new(StringComparer.Ordinal)
    {
        ["core_units"] = Units,
        ["core_inventory_mixing"] = InventoryMixing,
        ["core_decimal_tail"] = DecimalTail,
        ["core_reservations"] = Reservations,
        ["core_transfer_atomicity"] = TransferAtomicity,
        ["core_recipe_atomicity"] = RecipeAtomicity,
        ["core_frozen_snapshot"] = FrozenSnapshot,
        ["core_observation"] = Observation,
        ["core_heat_pressure"] = HeatPressure,
        ["core_compression"] = Compression,
        ["core_phase_change"] = PhaseChange,
        ["core_serialization"] = Serialization
    };

    private static void Check(Action<string, bool, string> report, string label, bool success, string message = null)
        => report(label, success, message ?? (success ? "通过" : "实际结果不符合预期"));

    private static void Require(Action<string, bool, string> report, string label, bool success)
    {
        Check(report, label, success);
        if (!success) throw new InvalidOperationException("流体用例必要前提失败：" + label);
    }

    private static bool Near(double actual, double expected, double tolerance = 1e-8)
        => FluidUnits.IsFinite(actual) && FluidUnits.IsFinite(expected) &&
           Math.Abs(actual - expected) <= tolerance * Math.Max(1d, Math.Max(Math.Abs(actual), Math.Abs(expected)));

    private static bool DecimalNear(decimal actual, decimal expected)
        => Math.Abs(actual - expected) <= .000000000000000000000001m;

    private static string Payload(FluidInventoryState state) => JsonConvert.SerializeObject(state);

    private static FluidInventory Filled(Action<string, bool, string> report, string id, decimal gas, decimal liquid = 0m,
        double kelvin = 300d, double volume = 100d, FluidCatalog catalog = null)
    {
        catalog ??= FluidCatalog.Default;
        Require(report, "目录存在 " + id, catalog.TryGet(id, out FluidDefinition definition));
        var inventory = new FluidInventory(catalog: catalog);
        if (gas + liquid > 0m)
            Require(report, "装入用例起始库存 " + id,
                inventory.TryAdd(FluidInventory.CreateBatch(definition, gas, liquid, kelvin), volume, .001d));
        return inventory;
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }
    #endregion

    #region 单位与两相库存边界
    private static void Units(Action<string, bool, string> report)
    {
        Require(report, "实际纯水物性已加载", FluidCatalog.Default.TryGet(FluidIds.PureWater, out FluidDefinition water));
        decimal amount = .123456789012345678m;
        Check(report, "标准升与摩尔往返保留小数", DecimalNear(
            FluidUnits.StandardLitersToMol(FluidUnits.MolToStandardLiters(amount)), amount));
        Check(report, "液体升与摩尔往返保留小数", DecimalNear(
            FluidUnits.LiquidLitersToMol(water, FluidUnits.MolToLiquidLiters(water, amount)), amount));
        Check(report, "份数与摩尔往返保留小数", DecimalNear(
            FluidUnits.MolToServings(water, FluidUnits.ServingsToMol(water, amount)), amount));
        Check(report, "一份液体读取实际份容量", DecimalNear(
            FluidUnits.MolToLiquidLiters(water, FluidUnits.ServingsToMol(water, 1m)), water.LitersPerServing));
        Check(report, "摄氏零度换算开尔文", Near(FluidUnits.CelsiusToKelvin(0d), 273.15d));
        Check(report, "有限数识别拒绝 NaN 和无穷",
            !FluidUnits.IsFinite(double.NaN) && !FluidUnits.IsFinite(double.PositiveInfinity) && FluidUnits.IsFinite(0d));
    }

    private static void InventoryMixing(Action<string, bool, string> report)
    {
        var inventory = Filled(report, FluidIds.Oxygen, 1m, 1m);
        FluidDefinition nitrogen = inventory.Catalog.Find(FluidIds.Nitrogen);
        FluidBatch gas = FluidInventory.CreateBatch(nitrogen, 1m, 0m, 300d);
        FluidBatch liquid = FluidInventory.CreateBatch(nitrogen, 0m, 1m, 300d);
        string before = Payload(inventory.State);
        Check(report, "单气相拒绝另一种气体", !inventory.TryAdd(gas, 100d, .001d, true, false));
        Check(report, "单液相拒绝另一种液体", !inventory.TryAdd(liquid, 100d, .001d, false, true));
        Check(report, "拒收不改变库存或内能", Payload(inventory.State) == before);
        Check(report, "混合模式接收真实气液组分", inventory.TryAdd(gas, 100d, .001d) && inventory.TryAdd(liquid, 100d, .001d));
        Check(report, "气液各自累计", inventory.GasMoles == 2m && inventory.LiquidMoles == 2m && inventory.State.Components.Count == 2);
        Check(report, "稳定物质身份忽略大小写", inventory.GetGasMoles("CORE:OXYGEN") == 1m);

        var full = Filled(report, FluidIds.PureWater, 0m);
        FluidDefinition water = full.Catalog.Find(FluidIds.PureWater);
        decimal capacityMoles = FluidUnits.LiquidLitersToMol(water, .999m);
        Require(report, "液体装至最小气相空间边界", full.TryAdd(
            FluidInventory.CreateBatch(water, 0m, capacityMoles, 300d), 1d, .001d));
        before = Payload(full.State);
        Check(report, "越过液体容量边界拒绝追加", !full.TryAdd(
            FluidInventory.CreateBatch(water, 0m, 1m, 300d), 1d, .001d));
        Check(report, "满液拒收不改变载荷", Payload(full.State) == before);
        Check(report, "无效几何抛出明确错误", Throws<ArgumentOutOfRangeException>(() => full.GetPressureKPa(1d, 1d)));
    }

    private static void DecimalTail(Action<string, bool, string> report)
    {
        const decimal tail = .00000000000000000001m;
        var inventory = Filled(report, FluidIds.Oxygen, tail * 3m);
        bool taken = inventory.TryTakeExact(FluidIds.Oxygen, FluidPhase.Gas, tail * 2m, out FluidBatch batch);
        Check(report, "真实小数尾量可提取", taken && batch.GasMoles == tail * 2m);
        Check(report, "提取后尾量精确保留", inventory.GasMoles == tail);
        Check(report, "最后一份尾量可取尽", inventory.TryTakeExact(FluidIds.Oxygen, FluidPhase.Gas, tail, out _) && inventory.IsEmpty);
        Check(report, "取尽后内能归零", inventory.State.TotalInternalEnergyJoules == 0d);

        var huge = Filled(report, FluidIds.Oxygen, FluidInventory.MaximumMoles);
        string before = Payload(huge.State);
        Check(report, "无法改变大库存的微量不能免费提取", !huge.TryTakeExact(FluidIds.Oxygen, FluidPhase.Gas, tail, out _));
        Check(report, "无法改变大库存的微量不能只加入热量", !huge.TryAdd(
            FluidInventory.CreateBatch(huge.Catalog.Find(FluidIds.Oxygen), tail, 0m, 300d), 100d, .001d));
        Check(report, "分辨率拒绝保持原载荷", Payload(huge.State) == before);
    }
    #endregion

    #region 预留与原子事务
    private static void Reservations(Action<string, bool, string> report)
    {
        var inventory = Filled(report, FluidIds.Oxygen, 3m);
        double energy = inventory.State.TotalInternalEnergyJoules;
        Require(report, "第一出口预留两摩尔", inventory.TryReserve("first", FluidPhase.Gas, 2m, out _));
        Require(report, "第二出口只预留余量", inventory.TryReserve("second", FluidPhase.Gas, 2m, out FluidBatch second));
        Check(report, "并行出口不能超卖", second.GasMoles == 1m && inventory.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas) == 0m);
        Check(report, "第三出口无法重复预留", !inventory.TryReserve("third", FluidPhase.Gas, 1m, out _));
        Check(report, "普通提取不能抢走出口预留", !inventory.TryTakeExact(FluidIds.Oxygen, FluidPhase.Gas, .1m, out _));
        ulong random = inventory.State.RandomState;
        Check(report, "已有出口继续使用同一批次", inventory.TryReserve("first", FluidPhase.Gas, .1m, out FluidBatch sticky) && sticky.GasMoles == 2m);
        Check(report, "复用预留不重新抽随机数", inventory.State.RandomState == random);
        Check(report, "预留不提前扣料或内能", inventory.GasMoles == 3m && inventory.State.TotalInternalEnergyJoules == energy);
        inventory.ReleaseReservation("first");
        Check(report, "撤销预留归还可用物料", inventory.GetReservation("first") == null && inventory.GetAvailableMoles(FluidIds.Oxygen, FluidPhase.Gas) == 2m);
        Check(report, "真实领取只扣已领取份额", inventory.TryTakeReserved("second", .25m, out FluidBatch claimed) &&
            claimed.GasMoles == .25m && inventory.GasMoles == 2.75m && inventory.GetReservation("second").GasMoles == .75m);
        Check(report, "无效出口和相态不产生预留", !inventory.TryReserve(" ", FluidPhase.Gas, 1m, out _) &&
            !inventory.TryReserve("bad-phase", (FluidPhase)99, 1m, out _));
    }

    private static void TransferAtomicity(Action<string, bool, string> report)
    {
        var source = Filled(report, FluidIds.Oxygen, 2m);
        var target = Filled(report, FluidIds.Nitrogen, 1m);
        Require(report, "建立待转移预留", source.TryReserve("transfer", FluidPhase.Gas, 1m, out _));
        string sourceBefore = Payload(source.State), targetBefore = Payload(target.State);
        Check(report, "目标单相混料拒收", !source.TryTransfer("transfer", target, 1m, 100d, .001d, true, false));
        Check(report, "失败转移完整保留来源预留", Payload(source.State) == sourceBefore);
        Check(report, "失败转移完整保留目标", Payload(target.State) == targetBefore);
        Check(report, "同一库存不能转给自己", !source.TryTransfer("transfer", new FluidInventory(source.State), 1m, 100d, .001d));
        double totalEnergy = source.State.TotalInternalEnergyJoules + target.State.TotalInternalEnergyJoules;
        bool moved = source.TryTransfer("transfer", target, .4m, 100d, .001d, out FluidBatch batch);
        Check(report, "部分转移按实际量提交", moved && batch.GasMoles == .4m && source.GasMoles == 1.6m && target.GetGasMoles(FluidIds.Oxygen) == .4m);
        Check(report, "部分转移保留未送完批次", source.GetReservation("transfer")?.GasMoles == .6m);
        Check(report, "转移内能守恒", Near(source.State.TotalInternalEnergyJoules + target.State.TotalInternalEnergyJoules, totalEnergy));

        var waterSource = Filled(report, FluidIds.PureWater, 0m, 20m);
        var waterTarget = Filled(report, FluidIds.PureWater, 0m);
        Require(report, "液体预留可部分转移", waterSource.TryReserve("liquid", FluidPhase.Liquid, 20m, out _));
        decimal totalMoles = waterSource.LiquidMoles;
        bool liquidMoved = waterSource.TryTransfer("liquid", waterTarget, 20m, .101d, .001d, out FluidBatch liquidBatch);
        Check(report, "目标容量裁剪液体实际转移量", liquidMoved && liquidBatch.LiquidMoles > 0m && liquidBatch.LiquidMoles < 20m && Near(waterTarget.GetLiquidLiters(), .1d));
        Check(report, "部分液体转移物质量守恒", waterSource.LiquidMoles + waterTarget.LiquidMoles == totalMoles);
    }

    private static void RecipeAtomicity(Action<string, bool, string> report)
    {
        var inventory = Filled(report, FluidIds.Oxygen, 3m);
        Require(report, "装入第二种配方原料", inventory.TryAdd(
            FluidInventory.CreateBatch(inventory.Catalog.Find(FluidIds.Nitrogen), 1m, 0m, 300d), 100d, .001d));
        string before = Payload(inventory.State);
        var missing = new[] { new FluidRequirement(FluidIds.Oxygen, FluidPhase.Gas, 1m), new FluidRequirement(FluidIds.Nitrogen, FluidPhase.Gas, 2m) };
        Check(report, "缺少第二原料整笔拒绝", !inventory.TryTakeRecipe(missing, out List<FluidBatch> rejected) && rejected.Count == 0);
        Check(report, "缺料不提前扣第一原料", Payload(inventory.State) == before);
        var duplicate = new[] { new FluidRequirement(FluidIds.Oxygen, FluidPhase.Gas, 2m), new FluidRequirement(FluidIds.Oxygen, FluidPhase.Gas, 2m) };
        Check(report, "同物质重复需求合计检查", !inventory.TryTakeRecipe(duplicate, out _) && Payload(inventory.State) == before);
        var valid = new[] { new FluidRequirement(FluidIds.Oxygen, FluidPhase.Gas, 1m), new FluidRequirement(FluidIds.Oxygen, FluidPhase.Gas, 1m), new FluidRequirement(FluidIds.Nitrogen, FluidPhase.Gas, .5m) };
        double energy = inventory.State.TotalInternalEnergyJoules;
        bool committed = inventory.TryTakeRecipe(valid, out List<FluidBatch> batches);
        double removedEnergy = 0d;
        foreach (FluidBatch batch in batches) removedEnergy += batch.InternalEnergyJoules;
        Check(report, "齐料后原子扣除合并需求", committed && batches.Count == 2 && inventory.GetGasMoles(FluidIds.Oxygen) == 1m && inventory.GetGasMoles(FluidIds.Nitrogen) == .5m);
        Check(report, "配方真实扣除内能守恒", Near(energy, inventory.State.TotalInternalEnergyJoules + removedEnergy));
        before = Payload(inventory.State);
        Check(report, "空配方与负需求不扣料", !inventory.TryTakeRecipe(Array.Empty<FluidRequirement>(), out _) &&
            !inventory.TryTakeRecipe(new[] { new FluidRequirement(FluidIds.Oxygen, FluidPhase.Gas, -1m) }, out _) && Payload(inventory.State) == before);
    }
    #endregion

    #region 冻结快照与状态观察
    private static void FrozenSnapshot(Action<string, bool, string> report)
    {
        var source = Filled(report, FluidIds.Oxygen, 3m);
        var middle = Filled(report, FluidIds.Oxygen, 0m);
        var destination = Filled(report, FluidIds.Oxygen, 0m);
        Require(report, "来源冻结前预留", source.TryReserve("source-middle", FluidPhase.Gas, 1m, out _));
        var firstFrozen = new FluidInventoryState(); firstFrozen.CopyFrom(source.State, false);
        var middleFrozen = new FluidInventoryState(); middleFrozen.CopyFrom(middle.State, false);
        Check(report, "冻结库存保留数量但不复制预留", firstFrozen.Components[0].GasMoles == 3m && firstFrozen.Ports.Count == 0);
        Require(report, "第一段正式转移完成", source.TryTransfer("source-middle", middle, 1m, 100d, .001d));
        Check(report, "冻结来源不被实时扣料污染", firstFrozen.Components[0].GasMoles == 3m);
        Check(report, "本轮冻结中间段看不到新入料", middleFrozen.Components.Count == 0 && middle.GasMoles == 1m && destination.IsEmpty);
        middleFrozen.CopyFrom(middle.State, false);
        Check(report, "下轮冻结才接纳新入料", middleFrozen.Components.Count == 1 && middleFrozen.Components[0].GasMoles == 1m);
        Require(report, "下轮中间段正式预留", middle.TryReserve("middle-destination", FluidPhase.Gas, 1m, out _));
        Check(report, "下轮第二段可正式转移", middle.TryTransfer("middle-destination", destination, 1m, 100d, .001d) && destination.GasMoles == 1m);
        Check(report, "复用冻结工作区不共享组分实例", !ReferenceEquals(middleFrozen.Components[0], destination.State.Components[0]));

        FluidInventoryState saved = source.State.Clone(); string payload = Payload(saved);
        Require(report, "快照后改变真实载荷", source.TryTakeExact(FluidIds.Oxygen, FluidPhase.Gas, .5m, out _));
        source.Restore(saved);
        Check(report, "回滚恢复物质量内能随机态预留", Payload(source.State) == payload);
        long revision = source.State.StateRevision;
        source.Restore(saved);
        Check(report, "无变化恢复不制造版本变化", source.State.StateRevision == revision);
    }

    private static void Observation(Action<string, bool, string> report)
    {
        var inventory = Filled(report, FluidIds.Oxygen, 2m);
        int events = 0;
        inventory.State.MassChanged += _ => events++;
        long mass = inventory.State.MassRevision;
        inventory.State.Components[0].GasMoles += 1m;
        Check(report, "组分原位改量通知质量与派生缓存", events == 1 && inventory.State.MassRevision > mass && inventory.GasMoles == 3m);
        mass = inventory.State.MassRevision; events = 0;
        FluidThermodynamics.AddHeat(inventory, 10d);
        Check(report, "只改热量不通知质量", events == 0 && inventory.State.MassRevision == mass);
        Require(report, "建立观察用预留", inventory.TryReserve("observe", FluidPhase.Gas, 1m, out _));
        Check(report, "预留不通知质量", events == 0 && inventory.State.MassRevision == mass);
        long state = inventory.State.StateRevision;
        inventory.State.Ports[0].GasMoles -= .1m;
        Check(report, "预留原位改量只推进状态版本", inventory.State.StateRevision > state && inventory.State.MassRevision == mass);
        FluidComponentState old = inventory.State.Components[0];
        FluidComponentState replacement = old.Clone();
        inventory.State.Components = new List<FluidComponentState> { replacement };
        Check(report, "替换组分集合通知质量", events == 1);
        events = 0; old.GasMoles += 1m;
        Check(report, "旧组分解绑观察", events == 0 && inventory.GasMoles == 3m);
        replacement.GasMoles += 1m;
        Check(report, "新组分接管观察", events == 1 && inventory.GasMoles == 4m);
        mass = inventory.State.MassRevision;
        inventory.State.Components[0] = replacement.Clone();
        Check(report, "同数量列表替换仍同步引用版本", inventory.State.MassRevision > mass);
    }
    #endregion

    #region 热量压力与有限压缩
    private static void HeatPressure(Action<string, bool, string> report)
    {
        var inventory = Filled(report, FluidIds.Oxygen, 2m, 0m, 300d);
        Check(report, "理想气体压力读取实际容积和温度", Near(inventory.GetPressureKPa(10d, .001d), 2d * FluidUnits.GasConstant * 300d / 10d));
        double energy = inventory.State.TotalInternalEnergyJoules;
        double capacity = 2d * inventory.Catalog.Find(FluidIds.Oxygen).GasHeatCapacityJPerMolKelvin;
        Check(report, "加热按真实热容升温", Near(FluidThermodynamics.AddHeat(inventory, capacity * 20d), capacity * 20d) && Near(inventory.GetTemperatureKelvin(), 320d));
        Check(report, "无效热量不写入库存", FluidThermodynamics.AddHeat(inventory, double.NaN) == 0d);
        FluidThermodynamics.AddHeat(inventory, -1e20d);
        Check(report, "过度降温截断为正绝对温度", inventory.GetTemperatureKelvin() > 0d && inventory.GetTemperatureKelvin() < .00001d);

        var hot = Filled(report, FluidIds.Oxygen, 1m, 0m, 400d);
        var cold = Filled(report, FluidIds.Nitrogen, 1m, 0m, 200d);
        energy = hot.State.TotalInternalEnergyJoules + cold.State.TotalInternalEnergyJoules;
        double exchanged = FluidThermodynamics.ExchangeHeat(hot, cold, 1e9d, 1d);
        Check(report, "两库存传热从热端到冷端", exchanged > 0d && hot.GetTemperatureKelvin() < 400d && cold.GetTemperatureKelvin() > 200d);
        Check(report, "大步长传热不越过平衡温度", Near(hot.GetTemperatureKelvin(), cold.GetTemperatureKelvin()));
        Check(report, "封闭传热总内能守恒", Near(hot.State.TotalInternalEnergyJoules + cold.State.TotalInternalEnergyJoules, energy));
        Check(report, "同一状态不能向自身传热", FluidThermodynamics.ExchangeHeat(hot, new FluidInventory(hot.State), 100d, 1d) == 0d);
        // 大温差按百分比，小温差遵循统一自然传热的每秒0.5K最低速率。
        var ambientFar = Filled(report, FluidIds.Oxygen, 1m, 0m, 200d);
        FluidThermodynamics.ExchangeHeatWithEnvironment(ambientFar, 350d, 1d);
        Check(report, "环境大温差每秒消除百分之一", Near(ambientFar.GetTemperatureKelvin(), 201.5d));
        var ambientNear = Filled(report, FluidIds.Oxygen, 1m, 0m, 330d);
        FluidThermodynamics.ExchangeHeatWithEnvironment(ambientNear, 350d, 1d);
        Check(report, "环境小温差每秒至少变化0.5K", Near(ambientNear.GetTemperatureKelvin(), 330.5d));
        var empty = Filled(report, FluidIds.Oxygen, 0m);
        Check(report, "空库存保持参考温度零压力且不能存热", Near(empty.GetTemperatureKelvin(), FluidUnits.ReferenceTemperatureKelvin) &&
            empty.GetPressureKPa(1d, .001d) == 0d && FluidThermodynamics.AddHeat(empty, 100d) == 0d);
    }

    private static void Compression(Action<string, bool, string> report)
    {
        Require(report, "压缩物性已加载", FluidCatalog.Default.TryGet(FluidIds.Oxygen, out FluidDefinition oxygen));
        FluidBatch batch = FluidInventory.CreateBatch(oxygen, 1m, 0m, 300d);
        FluidCompressionResult full = FluidThermodynamics.Compress(batch, 100d, 500d, 1e9d, .7d);
        Require(report, "完整压缩产生真实批次", full.Success);
        Check(report, "完整压缩不生成额外物质", full.Batch.GasMoles == batch.GasMoles && full.Batch.LiquidMoles == 0m);
        Check(report, "压缩做功按热量与损耗记账", Near(full.WorkJoules, full.Batch.InternalEnergyJoules - batch.InternalEnergyJoules + full.LossJoules) && full.LossJoules > 0d);
        Check(report, "降低目标压力无需额外做功", FluidThermodynamics.Compress(batch, 100d, 50d, 0d, .7d).Success);
        FluidCompressionResult partial = FluidThermodynamics.Compress(batch, 100d, 500d, full.WorkJoules / 4d, .7d);
        Check(report, "真实功率预算裁剪可压缩量", partial.Success && DecimalNear(partial.Batch.GasMoles, .25m) && Near(partial.WorkJoules, full.WorkJoules / 4d));
        Check(report, "零做功不能向高压压缩", !FluidThermodynamics.Compress(batch, 100d, 500d, 0d, .7d).Success);
        Check(report, "液相不可走气体压缩", !FluidThermodynamics.Compress(FluidInventory.CreateBatch(oxygen, 0m, 1m, 300d), 100d, 500d, 100d, .7d).Success);
        Check(report, "无效压强与效率拒绝压缩", !FluidThermodynamics.Compress(batch, 0d, 500d, 100d, .7d).Success &&
            !FluidThermodynamics.Compress(batch, 100d, 500d, 100d, 1.1d).Success);
    }
    #endregion

    #region 局部物性相变与序列化隔离
    private static FluidCatalog PhaseCatalog()
    {
        var catalog = new FluidCatalog();
        catalog.Register(new[]
        {
            new FluidDefinition(new FluidDefinitionDto
            {
                Id = "test:phase_water", DisplayName = "相变测试水", MolarMassKgPerMol = .018d,
                LiquidDensityKgPerLiter = 1d, GasHeatCapacityJPerMolKelvin = 34d, LiquidHeatCapacityJPerMolKelvin = 75d,
                PhaseChange = new FluidPhaseChangeDto
                {
                    LatentHeatJPerMol = 40000d, ReferenceBoilingKelvin = 373d, CriticalTemperatureKelvin = 650d,
                    Saturation = new List<FluidSaturationPointDto>
                    {
                        new() { TemperatureKelvin = 273d, PressureKPa = .6d },
                        new() { TemperatureKelvin = 373d, PressureKPa = 100d },
                        new() { TemperatureKelvin = 600d, PressureKPa = 10000d }
                    }
                }
            }),
            new FluidDefinition(new FluidDefinitionDto
            {
                Id = "test:phase_other", DisplayName = "相变阻塞物质", MolarMassKgPerMol = .032d,
                LiquidDensityKgPerLiter = 1d, GasHeatCapacityJPerMolKelvin = 20d, LiquidHeatCapacityJPerMolKelvin = 40d
            })
        });
        return catalog;
    }

    private static void PhaseChange(Action<string, bool, string> report)
    {
        FluidCatalog originalCatalog = FluidCatalog.Default, catalog = PhaseCatalog();
        int originalDefinitionCount = originalCatalog.Definitions.Count;
        const string waterId = "test:phase_water", otherId = "test:phase_other";
        var evaporating = Filled(report, waterId, 0m, 10m, 400d, 100d, catalog);
        double energy = evaporating.State.TotalInternalEnergyJoules;
        FluidPhaseChangeResult evaporation = FluidThermodynamics.AdvancePhaseChange(evaporating, 100d, .001d, .1d, .001d);
        Check(report, "低分压液体蒸发", evaporation.ConvertedMoles > 0m && evaporating.GasMoles > 0m);
        Check(report, "蒸发遵守每秒相变量上限", evaporation.ConvertedMoles <= .0001m);
        Check(report, "蒸发保持总物质量和内能", evaporating.GasMoles + evaporating.LiquidMoles == 10m && evaporating.State.TotalInternalEnergyJoules == energy);
        Check(report, "蒸发消耗显热降低温度", evaporating.GetTemperatureKelvin() < 400d);

        var condensing = Filled(report, waterId, 10m, 0m, 300d, 1d, catalog);
        energy = condensing.State.TotalInternalEnergyJoules;
        Require(report, "全气相建立相变预留", condensing.TryReserve("phase-port", FluidPhase.Gas, 10m, out _));
        FluidPhaseChangeResult condensation = FluidThermodynamics.AdvancePhaseChange(condensing, 1d, .001d, .1d, .001d);
        Check(report, "高分压气体冷凝", condensation.ConvertedMoles > 0m && condensing.LiquidMoles > 0m);
        Check(report, "冷凝保持总物质量和内能", condensing.GasMoles + condensing.LiquidMoles == 10m && condensing.State.TotalInternalEnergyJoules == energy);
        Check(report, "冷凝潜热体现在实际升温", condensing.GetTemperatureKelvin() > 300d);
        FluidOutputReservation reservation = condensing.GetReservation("phase-port");
        Check(report, "相变同步已预留气液份额", reservation != null && reservation.GasMoles == condensing.GasMoles && reservation.LiquidMoles == condensing.LiquidMoles);
        Check(report, "零秒相变不改量", FluidThermodynamics.AdvancePhaseChange(condensing, 1d, .001d, 0d, 10d).ConvertedMoles == 0m);

        var blocked = Filled(report, waterId, 0m, 10m, 400d, 100d, catalog);
        Require(report, "装入相变目标相阻塞物", blocked.TryAdd(FluidInventory.CreateBatch(catalog.Find(otherId), 1m, 0m, 400d), 100d, .001d));
        string before = Payload(blocked.State);
        FluidPhaseChangeResult collision = FluidThermodynamics.AdvancePhaseChange(blocked, 100d, .001d, 1d, 10d, true, false);
        Check(report, "单气相不能相变出第二物质", collision.IsBlocked && collision.ConvertedMoles == 0m && Payload(blocked.State) == before);
        foreach (double temperature in new[] { 200d, 700d })
        {
            var unsupported = Filled(report, waterId, 0m, 10m, temperature, 100d, catalog);
            before = Payload(unsupported.State);
            FluidPhaseChangeResult result = FluidThermodynamics.AdvancePhaseChange(unsupported, 100d, .001d, 1d, 10d);
            Check(report, "曲线范围外保留真实相态 " + temperature + "K", result.ConvertedMoles == 0m && result.UnsupportedFluidIds.Count == 1 && Payload(unsupported.State) == before);
        }
        Check(report, "局部相变材料不替换全局目录", ReferenceEquals(FluidCatalog.Default, originalCatalog) &&
            FluidCatalog.Default.Definitions.Count == originalDefinitionCount);
    }

    private static void Serialization(Action<string, bool, string> report)
    {
        var inventory = Filled(report, FluidIds.Oxygen, 2m, .25m);
        Require(report, "保存待发送批次", inventory.TryReserve("save-port", FluidPhase.Gas, .5m, out _));
        inventory.State.RandomState = 123456789UL;
        int sourceEvents = 0; inventory.State.MassChanged += _ => sourceEvents++;
        string json = Payload(inventory.State);
        Check(report, "JSON 不保存运行观察与版本", !json.Contains("Revision") && !json.Contains("MassChanged") && !json.Contains("Owner"));
        FluidInventoryState jsonState = JsonConvert.DeserializeObject<FluidInventoryState>(json);
        Require(report, "JSON 载荷可恢复", jsonState != null);
        var jsonInventory = new FluidInventory(jsonState);
        Check(report, "JSON 恢复气液内能随机态和预留", Payload(jsonInventory.State) == json);
        byte[] bytes = MemoryPackSerializer.Serialize(inventory.State);
        FluidInventoryState binaryState = MemoryPackSerializer.Deserialize<FluidInventoryState>(bytes);
        Require(report, "MemoryPack 载荷可恢复", binaryState != null);
        var binaryInventory = new FluidInventory(binaryState);
        Check(report, "MemoryPack 恢复气液内能随机态和预留", Payload(binaryInventory.State) == json);
        var clone = new FluidInventory(inventory.State.Clone());
        Check(report, "快照克隆拥有独立组分与预留", !ReferenceEquals(clone.State.Components[0], inventory.State.Components[0]) &&
            !ReferenceEquals(clone.State.Ports[0], inventory.State.Ports[0]));
        jsonInventory.State.Components[0].GasMoles += 1m;
        binaryInventory.State.Components[0].GasMoles += 1m;
        clone.State.Components[0].GasMoles += 1m;
        Check(report, "恢复与克隆不会携带来源订阅者", sourceEvents == 0 && inventory.GasMoles == 2m);

        FluidInventoryState invalid = inventory.State.Clone(); invalid.Components[0].GasMoles = -1m;
        string before = Payload(inventory.State);
        Check(report, "负量快照明确拒绝", Throws<InvalidDataException>(() => inventory.Restore(invalid)));
        Check(report, "拒绝无效快照保持原载荷", Payload(inventory.State) == before);
        invalid = inventory.State.Clone(); invalid.Ports[0].GasMoles = 3m;
        Check(report, "超卖预留快照明确拒绝", Throws<InvalidDataException>(() => new FluidInventory(invalid)));
    }
    #endregion
}
