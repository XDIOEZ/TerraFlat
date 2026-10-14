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
        private readonly float originalDepth;
        public Vector2 Position { get; }
        public LiquidDefinition Definition { get; }
        public float Depth => sample.Terrain.GetLiquidDepth(sample.LocalCell.x, sample.LocalCell.y);
        public string Id => sample.Terrain.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y);
        public int Ground => sample.Terrain.GetCell(sample.LocalCell.x, sample.LocalCell.y).GroundTileId;
        public LiquidCell(Vector2 position)
        {
            Position = position;
            Require(ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(position, out sample), "真实地形采样");
            originalId = sample.Terrain.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y); originalDepth = Depth;
            Require(GameRes.ExistingInstance.TryGetLiquidDefinition(FluidCatalog.Default.Find(FluidIds.PureWater).LiquidId, out LiquidDefinition water), "纯水地表定义");
            Definition = water;
        }
        public void Set(float depth)
        {
            WorldLiquidSystem.TrySet(Position, Definition.Id, depth);
            Require(Near(Depth, depth), "设置地表液深 " + depth);
        }
        public void Dispose()
        {
            WorldLiquidSystem.TrySet(Position, originalId, originalDepth);
            Require(Near(Depth, originalDepth) && (Depth == 0 || sample.Terrain.GetLiquidId(sample.LocalCell.x, sample.LocalCell.y) == originalId), "归还原始液体格");
        }
    }
    #endregion

    #region 地表抽取、倾倒与工业泵守恒
    private static void Transactions(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope(surface: true); using var cell = new LiquidCell(f.SurfacePoint());
        int ground = cell.Ground; cell.Set(.5f);
        bool pumped = WorldLiquidSystem.TryPump(cell.Position, .2f, out string id, out float removed);
        Check(report, "地表抽取返回真实身份和实际扣量", pumped && id == cell.Definition.Id && Near(removed, .2) && Near(cell.Depth, .3));
        Check(report, "同种液体可倾倒", WorldLiquidSystem.TryPour(cell.Position, id, .1f, out float added) && Near(added, .1) && Near(cell.Depth, .4));
        float before = cell.Depth;
        Check(report, "整批倾倒超出格容量拒绝且保留余量", !WorldLiquidSystem.TryPour(cell.Position, id, .8f, out _) && cell.Depth == before);
        string other = FluidCatalog.Default.Find("core:salt_water").LiquidId;
        Check(report, "异种液体与非有限请求拒绝", !WorldLiquidSystem.TryPour(cell.Position, other, .1f, out _) &&
            !WorldLiquidSystem.TryPump(cell.Position, float.NaN, out _, out _) && cell.Depth == before);
        Check(report, "抽到空格只返回实际剩余液体", WorldLiquidSystem.TryPump(cell.Position, 2f, out _, out removed) && Near(removed, before) && cell.Depth == 0);
        Check(report, "空格不能继续造水", !WorldLiquidSystem.TryPump(cell.Position, .1f, out _, out _));
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
            cell.Set(.5f);
            MachineEntity pump = f.Add("LiquidPump_Electric", 1), tank = f.Add("LiquidStorageTank", 2);
            f.Rebuild();
            FluidDefinition water = FluidCatalog.Default.Find(FluidIds.PureWater);
            result["waterId"] = water.Id;
            result["depthPerServing"] = cell.Definition.WorldWater.DepthPerServing;
            result["molesPerServing"] = FluidUnits.ServingsToMol(water, 1m);
            result["before"] = PumpSample(f, cell, pump, tank);
            pump.ElectricalSuppliedWatts = 0;
            FluidDeviceOperations.AdvanceLiquidPump(pump, f.Seconds);
            result["unpowered"] = PumpSample(f, cell, pump, tank);
            f.Begin();
            result["beginning"] = PumpSample(f, cell, pump, tank);
            pump.ElectricalSuppliedWatts = pump.Definition.Electrical.PowerWatts;
            FluidDeviceOperations.AdvanceLiquidPump(pump, f.Seconds);
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
            result["depthPerServing"] = cell.Definition.WorldWater.DepthPerServing;
            result["molesPerServing"] = FluidUnits.ServingsToMol(water, 1m);
            Require(inventory.TryAdd(FluidInventory.CreateBatch(water, 0, FluidUnits.ServingsToMol(water, .5m), 293.15),
                GetFluidVolumeLiters(outlet), GetFluidMinimumGasSpaceLiters(outlet)), "出口液体装料");
            result["before"] = OutletSample(f, cell, outlet);
            f.Step();
            result["emitted"] = OutletSample(f, cell, outlet);
            cell.Set(1);
            Require(inventory.TryAdd(FluidInventory.CreateBatch(water, 0, 1m, 293.15),
                GetFluidVolumeLiters(outlet), GetFluidMinimumGasSpaceLiters(outlet)), "堵塞测试装料");
            result["beforeBlocked"] = OutletSample(f, cell, outlet);
            f.Step();
            result["blocked"] = OutletSample(f, cell, outlet);
            // 留小于原批次的一点空间，再调用真实排液事务观察部分接纳和后续续送。
            cell.Set(.9995f);
            result["beforePartial"] = OutletSample(f, cell, outlet);
            f.Step();
            result["partial"] = OutletSample(f, cell, outlet);
            cell.Set(0);
            result["beforeRetry"] = OutletSample(f, cell, outlet);
            f.Step();
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
        cell.Set(1);
        Require(logic.Execute("fluid.phase", "liquid", player), "切相前排液配置");
        Require(inventory.TryAdd(FluidInventory.CreateBatch(water, 0, 1m, 293.15),
            GetFluidVolumeLiters(outlet), GetFluidMinimumGasSpaceLiters(outlet)), "切相温水装料");
        var result = new JObject { ["beforeBlocked"] = OutletSample(f, cell, outlet) };
        f.Step();
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
        result["liquidStep"] = OutletSample(f, cell, outlet);
        return result;
    }

    private static JObject CellSample(LiquidCell cell) => new()
    { ["liquidId"] = cell.Id, ["depth"] = cell.Depth, ["ground"] = cell.Ground };

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
