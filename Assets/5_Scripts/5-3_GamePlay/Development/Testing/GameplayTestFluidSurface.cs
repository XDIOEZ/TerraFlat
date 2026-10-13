using System;
using System.Collections.Generic;
using FlatWorld.WorldModel;
using UnityEngine;
using static MachineWorld;

/// <summary>在本轮隔离地表读取真实液体层，泵和出口调用正式事务并在结束时归还测试格。</summary>
internal static class GameplayTestFluidSurface
{
    #region 地表用例与恢复快照
    internal static readonly Dictionary<string, Action<Action<string, bool, string>>> Cases = new(StringComparer.Ordinal)
    {
        ["surface_liquid_transactions"] = Transactions,
        ["surface_liquid_pump"] = Pump,
        ["surface_liquid_outlet"] = Outlet
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

    private static void Pump(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope(surface: true); using var cell = new LiquidCell(f.SurfacePoint());
        cell.Set(.5f); int ground = cell.Ground;
        MachineEntity pump = f.Add("LiquidPump_Electric", 1), tank = f.Add("LiquidStorageTank", 2); f.Rebuild();
        FluidInventory buffer = GetFluidInventory(pump); float before = cell.Depth;
        pump.ElectricalSuppliedWatts = 0; FluidDeviceOperations.AdvanceLiquidPump(pump, f.Seconds);
        Check(report, "断电液泵不扣地表源", cell.Depth == before && buffer.IsEmpty);
        f.Begin(); pump.ElectricalSuppliedWatts = pump.Definition.Electrical.PowerWatts;
        FluidDeviceOperations.AdvanceLiquidPump(pump, f.Seconds);
        decimal servings = (decimal)((before - cell.Depth) / cell.Definition.WorldWater.DepthPerServing);
        decimal expected = FluidUnits.ServingsToMol(FluidCatalog.Default.Find(FluidIds.PureWater), servings);
        Check(report, "液泵真实扣地表源并换算摩尔量", cell.Depth < before && buffer.LiquidMoles > 0 && Near((double)buffer.LiquidMoles, (double)expected));
        double energy = buffer.State.TotalInternalEnergyJoules; decimal amount = buffer.LiquidMoles;
        f.Complete();
        Check(report, "新抽液体不能同轮穿透出口", GetFluidInventory(tank).IsEmpty && Near((double)buffer.LiquidMoles, (double)amount));
        f.Step();
        Check(report, "后续轮输送保持液量和内能守恒", GetFluidInventory(tank).LiquidMoles > 0 &&
            Near((double)(buffer.LiquidMoles + GetFluidInventory(tank).LiquidMoles), (double)amount) &&
            Near(buffer.State.TotalInternalEnergyJoules + GetFluidInventory(tank).State.TotalInternalEnergyJoules, energy));
        Check(report, "抽液不改变地面身份", cell.Ground == ground);
        MachineEntity gas = f.Add("GasPump_Mechanical"); f.Rebuild();
        Check(report, "气泵浸在真实无支撑液体格时阻止抽气", FluidDeviceOperations.IsGasPumpWet(gas) && !GetFluidDeviceBehavior(gas).CanOutput(gas, out _));
    }

    private static void Outlet(Action<string, bool, string> report)
    {
        using var f = new GameplayFluidScope(surface: true); using var cell = new LiquidCell(f.SurfacePoint(2));
        cell.Set(0); MachineEntity outlet = f.Add("FluidOutlet", 1); f.Rebuild(); FluidInventory inventory = GetFluidInventory(outlet);
        FluidDefinition water = FluidCatalog.Default.Find(FluidIds.PureWater);
        decimal initial = FluidUnits.ServingsToMol(water, .5m);
        Require(inventory.TryAdd(FluidInventory.CreateBatch(water, 0, initial, 293.15), 1, .001), "出口液体装料");
        f.Step(); decimal worldMoles = FluidUnits.ServingsToMol(water, (decimal)(cell.Depth / cell.Definition.WorldWater.DepthPerServing));
        Check(report, "液体出口真实进入世界 Liquid 层", cell.Depth > 0 && inventory.LiquidMoles < initial);
        Check(report, "环境输出只按实际接纳深度扣库存", Near((double)(inventory.LiquidMoles + worldMoles), (double)initial));
        cell.Set(1); Require(inventory.TryAdd(FluidInventory.CreateBatch(water, 0, 1m, 293.15), 1, .001), "堵塞测试装料");
        decimal before = inventory.LiquidMoles; double energy = inventory.State.TotalInternalEnergyJoules;
        f.Step();
        Check(report, "满世界格拒收时保留出口液体与内能", inventory.LiquidMoles == before && Near(inventory.State.TotalInternalEnergyJoules, energy) && cell.Depth == 1);
        Check(report, "世界拒收原因进入端口报告", GetFluidState(outlet).BlockedPorts.Count > 0);
    }
    #endregion
}
