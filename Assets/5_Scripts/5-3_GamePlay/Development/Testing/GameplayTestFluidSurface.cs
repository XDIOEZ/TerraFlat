using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static MachineWorld;

/// <summary>在本轮隔离地表读取真实液体层，泵和出口调用正式事务并在结束时归还测试格。</summary>
internal static class GameplayTestFluidSurface
{
    #region 地表用例与恢复快照
    internal static readonly Dictionary<string, Action<Action<string, bool, string>>> Cases = new(StringComparer.Ordinal)
    {
        ["surface_liquid_transactions"] = Transactions
    };
    private static void Require(bool passed, string label)
    { if (!passed) throw new InvalidOperationException("地表流体测试前提失败：" + label); }
    private static bool Near(double a, double b) => double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) < .00001;
    private static void Check(Action<string, bool, string> report, string label, bool passed, string detail = null)
        => report(label, passed, detail ?? (passed ? "通过" : "实际结果不符合预期"));
    private sealed class LiquidCell : IDisposable
    {
        private readonly RuntimeTerrainTileSample sample;
        private readonly string originalId;
        private readonly int originalLevel;
        public Vector2 Position { get; }
        public LiquidDefinition Definition { get; }
        public float Depth => sample.Terrain.GetLiquidDepth(sample.LocalCell.x, sample.LocalCell.y);
        public int Level => sample.Terrain.GetLiquidLevel(sample.LocalCell.x, sample.LocalCell.y);
        public string Id => sample.Terrain.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y);
        public int Ground => sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y).GroundTileId;
        public LiquidCell(Vector2 position)
        {
            Position = position;
            Require(ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(position, out sample), "真实地形采样");
            originalId = sample.Terrain.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y); originalLevel = Level;
            Require(GameRes.ExistingInstance.TryGetLiquidDefinition(FluidCatalog.Default.Find(FluidIds.PureWater).LiquidId, out LiquidDefinition water), "纯水地表定义");
            Definition = water;
        }
        public void Set(int level)
        {
            WorldLiquidSystem.TrySet(Position, Definition.Id, level);
            Require(Level == level, "设置地表液级 " + level);
        }
        public void Dispose()
        {
            WorldLiquidSystem.TrySet(Position, originalId, originalLevel);
            Require(Level == originalLevel && (Level == 0 || sample.Terrain.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y) == originalId), "归还原始液体格");
        }
    }
    #endregion

    #region 地表抽取、倾倒与工业泵守恒
    private static void Transactions(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope(surface: true); using var cell = new LiquidCell(f.SurfacePoint());
        int ground = cell.Ground; cell.Set(5);
        bool pumped = WorldLiquidSystem.TryPump(cell.Position, 2, out string id, out int removed);
        Check(report, "地表抽取返回真实身份和实际扣量", pumped && id == cell.Definition.Id && removed == 2 && cell.Level == 3);
        Check(report, "同种液体可倾倒", WorldLiquidSystem.TryPour(cell.Position, id, 1, out int added) && added == 1 && cell.Level == 4);
        int before = cell.Level;
        Check(report, "整批倾倒超出格容量拒绝且保留余量", !WorldLiquidSystem.TryPour(cell.Position, id, 8, out _) && cell.Level == before);
        string other = FluidCatalog.Default.Find("core:salt_water").LiquidId;
        Check(report, "异种液体与非正等级拒绝", !WorldLiquidSystem.TryPour(cell.Position, other, 1, out _) &&
            !WorldLiquidSystem.TryPump(cell.Position, 0, out _, out _) && cell.Level == before);
        Check(report, "抽到空格只返回实际剩余液体", WorldLiquidSystem.TryPump(cell.Position, 20, out _, out removed) && removed == before && cell.Level == 0);
        Check(report, "空格不能继续造水", !WorldLiquidSystem.TryPump(cell.Position, 1, out _, out _));
        Check(report, "液体操作保持 Ground", cell.Ground == ground);
    }

    #endregion

    #region 工业泵与出口的真实阶段采样
    internal static JObject CapturePumpFlow(Player player, JObject args)
    {
        using var f = new GameplayFluidScope(surface: true);
        var cell = new LiquidCell(f.SurfacePoint());
        var result = new JObject { ["originalCell"] = CellSample(cell), ["seconds"] = f.Seconds };
        using (cell)
        {
            cell.Set(5);
            MachineEntity pump = f.Add("LiquidPump_Electric", 1), tank = f.Add("LiquidStorageTank", 2);
            f.Rebuild();
            FluidDefinition water = FluidCatalog.Default.Find(FluidIds.PureWater);
            result["waterId"] = water.Id;
            result["depthPerServing"] = LiquidCellValue.ToNormalizedDepth(1);
            result["molesPerServing"] = FluidUnits.ServingsToMol(water, 1m);
            result["before"] = PumpSample(f, cell, pump, tank);
            pump.ElectricalSuppliedWatts = 0;
            FluidDeviceOperations.AdvanceLiquidPump(pump, f.Seconds);
            result["unpowered"] = PumpSample(f, cell, pump, tank);
            f.Begin();
            result["beginning"] = PumpSample(f, cell, pump, tank);
            pump.ElectricalSuppliedWatts = pump.Definition.Electrical.PowerWatts;
            FluidDeviceOperations.AdvanceLiquidPump(pump, f.Seconds);
            result["accumulated"] = PumpSample(f, cell, pump, tank);
            int chargingSteps = 1;
            while (cell.Level == 5 && chargingSteps < 200)
            { FluidDeviceOperations.AdvanceLiquidPump(pump, f.Seconds); chargingSteps++; }
            result["chargingSteps"] = chargingSteps;
            result["pumped"] = PumpSample(f, cell, pump, tank);
            f.Complete();
            result["sameStep"] = PumpSample(f, cell, pump, tank);
            f.Begin();
            result["nextBeginning"] = PumpSample(f, cell, pump, tank);
            f.Complete();
            result["nextStep"] = PumpSample(f, cell, pump, tank);
            MachineEntity gas = f.Add("GasPump_Mechanical");
            f.Rebuild();
            bool canOutput = GetFluidDeviceBehavior(gas).CanOutput(gas, out string reason);
            result["wetGasPump"] = new JObject { ["submerged"] = FluidDeviceOperations.IsGasPumpWet(gas),
                ["canOutput"] = canOutput, ["reason"] = reason, ["inventory"] = InventorySample(gas) };
        }
        result["restoredCell"] = CellSample(cell);
        return result;
    }

    internal static JObject CaptureOutletFlow(Player player, JObject args)
    {
        using var f = new GameplayFluidScope(surface: true);
        var cell = new LiquidCell(f.SurfacePoint(2));
        var result = new JObject { ["originalCell"] = CellSample(cell), ["seconds"] = f.Seconds };
        using (cell)
        {
            cell.Set(0);
            MachineEntity outlet = f.Add("FluidOutlet", 1);
            f.Rebuild();
            GetFluidState(outlet).OutletPhase = "liquid";
            FluidInventory inventory = GetFluidInventory(outlet);
            FluidDefinition water = FluidCatalog.Default.Find(FluidIds.PureWater);
            result["waterId"] = water.Id;
            result["depthPerServing"] = LiquidCellValue.ToNormalizedDepth(1);
            result["molesPerServing"] = FluidUnits.ServingsToMol(water, 1m);
            outlet.Definition.Fluid.ThroughputLitersPerSecond = (double)FluidUnits.MolToLiquidLiters(water, FluidUnits.ServingsToMol(water, 1m)) / (f.Seconds * 4d);
            Require(inventory.TryAdd(FluidInventory.CreateBatch(water, 0, FluidUnits.ServingsToMol(water, 2.5m), 293.15),
                GetFluidVolumeLiters(outlet), GetFluidMinimumGasSpaceLiters(outlet)), "出口液体装料");
            result["before"] = OutletSample(f, cell, outlet);
            f.Step();
            result["accumulated"] = OutletSample(f, cell, outlet);
            for (int attempt = 0; cell.Level == 0 && attempt < 32; attempt++) f.Step();
            result["emitted"] = OutletSample(f, cell, outlet);
            cell.Set(10);
            // 堵塞阶段从独立温水库存开始，避免上一阶段残留水蒸气先凝结而掩盖蒸发断言。
            inventory.Drain();
            // 蒸发会令液相略低于整份数量；三份温水可独立验证拒收、部分提交和解除堵塞续送。
            Require(inventory.TryAdd(FluidInventory.CreateBatch(water, 0, FluidUnits.ServingsToMol(water, 3m), 293.15),
                GetFluidVolumeLiters(outlet), GetFluidMinimumGasSpaceLiters(outlet)), "堵塞测试装料");
            result["beforeBlocked"] = OutletSample(f, cell, outlet);
            f.Step();
            for (int attempt = 0; GetFluidState(outlet).BlockedPorts.Count == 0 && attempt < 32; attempt++) f.Step();
            result["blocked"] = OutletSample(f, cell, outlet);
            // 只腾出一级空间，正式出口必须消耗一整份再增加一级。
            cell.Set(9);
            result["beforePartial"] = OutletSample(f, cell, outlet);
            f.Step();
            for (int attempt = 0; cell.Level == 9 && attempt < 32; attempt++) f.Step();
            result["partial"] = OutletSample(f, cell, outlet);
            cell.Set(0);
            result["beforeRetry"] = OutletSample(f, cell, outlet);
            f.Step();
            for (int attempt = 0; cell.Level == 0 && attempt < 32; attempt++) f.Step();
            result["retried"] = OutletSample(f, cell, outlet);
            result["phaseSwitch"] = CapturePhaseSwitch(f, cell, outlet, water, player);
        }
        result["restoredCell"] = CellSample(cell);
        return result;
    }

    private static JObject CapturePhaseSwitch(GameplayFluidScope f, LiquidCell cell, MachineEntity outlet,
        FluidDefinition water, Player player)
    {
        FluidInventory inventory = GetFluidInventory(outlet);
        FluidMachineLogic logic = outlet.Logic as FluidMachineLogic ?? throw new InvalidOperationException("排液出口缺少正式配置逻辑。");
        inventory.Drain();
        cell.Set(10);
        Require(logic.Execute("fluid.phase", "liquid", player), "切相前排液配置");
        Require(inventory.TryAdd(FluidInventory.CreateBatch(water, 0, FluidUnits.ServingsToMol(water, 2m), 293.15),
            GetFluidVolumeLiters(outlet), GetFluidMinimumGasSpaceLiters(outlet)), "切相温水装料");
        var result = new JObject { ["beforeBlocked"] = OutletSample(f, cell, outlet) };
        f.Step();
        for (int attempt = 0; GetFluidState(outlet).BlockedPorts.Count == 0 && attempt < 32; attempt++) f.Step();
        result["blocked"] = OutletSample(f, cell, outlet);
        // 正式加料留下超过单步吞吐的水蒸气批次，分别观察两个子槽的真实切相边界。
        Require(inventory.TryAdd(FluidInventory.CreateBatch(water, .1m, 0, 293.15),
            GetFluidVolumeLiters(outlet), GetFluidMinimumGasSpaceLiters(outlet)), "切相水蒸气装料");
        result["beforeGasCommand"] = OutletSample(f, cell, outlet);
        result["gasCommandAccepted"] = logic.Execute("fluid.phase", "gas", player);
        result["gasCommand"] = OutletSample(f, cell, outlet);
        f.Begin();
        result["gasBeginning"] = OutletSample(f, cell, outlet);
        f.Complete();
        result["gasStep"] = OutletSample(f, cell, outlet);
        result["liquidCommandAccepted"] = logic.Execute("fluid.phase", "liquid", player);
        result["liquidCommand"] = OutletSample(f, cell, outlet);
        cell.Set(0);
        result["beforeRetry"] = OutletSample(f, cell, outlet);
        f.Begin();
        result["liquidBeginning"] = OutletSample(f, cell, outlet);
        f.Complete();
        for (int attempt = 0; cell.Level == 0 && attempt < 32; attempt++) f.Step();
        result["liquidStep"] = OutletSample(f, cell, outlet);
        return result;
    }

    internal static JObject CaptureOutletRate(Player player, JObject args)
    {
        using var f = new GameplayFluidScope(surface: true);
        var cell = new LiquidCell(f.SurfacePoint(2));
        var result = new JObject { ["originalCell"] = CellSample(cell), ["seconds"] = f.Seconds };
        using (cell)
        {
            cell.Set(0);
            MachineEntity outlet = f.Add("FluidOutlet", 1);
            f.Rebuild();
            GetFluidState(outlet).OutletPhase = "liquid";
            // 加大自有夹具缓冲以持有足量库存，吞吐分别使用真实液泵速率和现有出口速率。
            double highThroughput = outlet.Definition.Fluid.ThroughputLitersPerSecond;
            outlet.Definition.Fluid.VolumeLiters = Math.Max(10d, outlet.Definition.Fluid.VolumeLiters);
            outlet.Definition.Fluid.ThroughputLitersPerSecond = 2d;
            FluidDefinition water = FluidCatalog.Default.Find(FluidIds.PureWater);
            FluidInventory inventory = GetFluidInventory(outlet);
            Require(inventory.TryAdd(FluidInventory.CreateBatch(water, 0, FluidUnits.ServingsToMol(water, 20m), 293.15),
                GetFluidVolumeLiters(outlet), GetFluidMinimumGasSpaceLiters(outlet)), "速率回归装入完整份液体");
            result["molesPerServing"] = FluidUnits.ServingsToMol(water, 1m);
            result["before"] = OutletSample(f, cell, outlet);
            var ticks = new JArray();
            for (int tick = 0; tick < 10; tick++) { f.Step(); ticks.Add(OutletSample(f, cell, outlet)); }
            result["ticks"] = ticks;
            result["normalRate"] = 2d;
            cell.Set(0);
            outlet.Definition.Fluid.ThroughputLitersPerSecond = highThroughput;
            result["highRate"] = highThroughput;
            result["beforeHigh"] = OutletSample(f, cell, outlet);
            f.Step();
            result["high"] = OutletSample(f, cell, outlet);
            cell.Set(10);
            result["beforeBlocked"] = OutletSample(f, cell, outlet);
            f.Step();
            result["blocked"] = OutletSample(f, cell, outlet);
        }
        result["restoredCell"] = CellSample(cell);
        return result;
    }

    private static JObject CellSample(LiquidCell cell) => new()
    { ["liquidId"] = cell.Id, ["depth"] = cell.Depth, ["level"] = cell.Level, ["ground"] = cell.Ground };

    private static JObject InventorySample(MachineEntity node)
    {
        FluidInventory inventory = GetFluidInventory(node);
        FluidMachineState state = GetFluidState(node);
        return new JObject { ["state"] = JObject.FromObject(inventory.State),
            ["randomState"] = inventory.State.RandomState.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["gasMoles"] = inventory.GasMoles, ["liquidMoles"] = inventory.LiquidMoles,
            ["waterGasMoles"] = inventory.GetGasMoles(FluidIds.PureWater),
            ["waterLiquidMoles"] = inventory.GetLiquidMoles(FluidIds.PureWater),
            ["energy"] = inventory.State.TotalInternalEnergyJoules, ["temperatureKelvin"] = inventory.GetTemperatureKelvin(),
            ["volumeLiters"] = GetFluidVolumeLiters(node), ["minimumGasSpaceLiters"] = GetFluidMinimumGasSpaceLiters(node),
            ["maximumPhaseMolesPerSecond"] = node.Definition.Fluid.MaximumPhaseMolesPerSecond,
            ["blockedPorts"] = JObject.FromObject(state.BlockedPorts), ["phaseWarnings"] = JObject.FromObject(state.PhaseWarnings),
            ["status"] = state.Status, ["outletPhase"] = state.OutletPhase,
            ["worldPumpLiters"] = state.WorldLiquidPumpLiters, ["worldOutputLiters"] = JObject.FromObject(state.WorldLiquidOutputLiters),
            ["lastFlowPhase"] = state.LastFlowPhase, ["suppliedWatts"] = node.ElectricalSuppliedWatts,
            ["emittedGasMoles"] = FluidUnits.StandardLitersToMol(state.LastEmittedStandardLiters) };
    }

    private static JObject BudgetSample(GameplayFluidScope f, MachineEntity node)
    {
        bool known = f.Budget(GetFluidInventory(node), out FluidInventoryState stock, out double liters);
        decimal gas = 0, liquid = 0;
        if (stock != null)
            foreach (FluidComponentState component in stock.Components) { gas += component.GasMoles; liquid += component.LiquidMoles; }
        return new JObject { ["known"] = known, ["state"] = stock == null ? null : JObject.FromObject(stock),
            ["gasMoles"] = gas, ["liquidMoles"] = liquid, ["remainingLiters"] = liters };
    }

    private static JObject PumpSample(GameplayFluidScope f, LiquidCell cell, MachineEntity pump, MachineEntity tank)
        => new() { ["cell"] = CellSample(cell), ["pump"] = InventorySample(pump), ["tank"] = InventorySample(tank),
            ["budget"] = BudgetSample(f, pump) };

    private static JObject OutletSample(GameplayFluidScope f, LiquidCell cell, MachineEntity outlet)
        => new() { ["cell"] = CellSample(cell), ["outlet"] = InventorySample(outlet), ["budget"] = BudgetSample(f, outlet) };
    #endregion
}
