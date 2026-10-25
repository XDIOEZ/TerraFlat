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
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterConnectedPumpProbe() => GameplayTestRuntimeProbe.Register(
        "industrial_fluids", "connected_surface_pump", CaptureConnectedPumpFlow);

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
        public RuntimeTerrainTileSample Sample => sample;
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
            Require(GameRes.ExistingInstance.TryGetLiquidDefinition("core:dirty_water", out LiquidDefinition water), "纯水地表定义");
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

    #region 简单工业真实地表采样
    internal static JObject CaptureConnectedPumpFlow(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f = new GameplayFluidScope(surface: true);
        using var inlet = new LiquidCell(f.SurfacePoint());
        using var middle = new LiquidCell(f.SurfacePoint(1));
        using var far = new LiquidCell(f.SurfacePoint(2));
        LiquidCell[] cells = { inlet, middle, far };
        float[] excavated = new float[3], covered = new float[3];
        var barrier = new object();
        var boundary = new List<Vector2Int>();
        Vector2Int first = inlet.Sample.WorldCell;
        boundary.Add(first + Vector2Int.left);
        boundary.Add(first + Vector2Int.right * 3);
        for (int x = 0; x < 3; x++)
        {
            boundary.Add(first + new Vector2Int(x, -1));
            boundary.Add(first + new Vector2Int(x, 1));
            var sample = cells[x].Sample;
            sample.Terrain.TryGetEnvironmentValue(TerrainSurfaceHeightLayer.ExcavatedLevelsLayerId,
                sample.LocalCell.x, sample.LocalCell.y, out excavated[x]);
            sample.Terrain.TryGetEnvironmentValue(TerrainSurfaceHeightLayer.CoverLevelsLayerId,
                sample.LocalCell.x, sample.LocalCell.y, out covered[x]);
        }
        try
        {
            WorldLiquidFlowObstacles.Register(barrier, boundary);
            for (int x = 0; x < 3; x++)
            {
                var sample = cells[x].Sample;
                sample.Terrain.SetEnvironmentValue(TerrainSurfaceHeightLayer.ExcavatedLevelsLayerId,
                    sample.LocalCell.x, sample.LocalCell.y, 1);
                sample.Terrain.SetEnvironmentValue(TerrainSurfaceHeightLayer.CoverLevelsLayerId,
                    sample.LocalCell.x, sample.LocalCell.y, 0);
                cells[x].Set(x == 0 ? 0 : x == 1 ? 7 : 5);
            }
            var pump = f.Add("LiquidPump_Electric", 1);
            var tank = f.Add("LiquidStorageTank", 2);
            f.Begin(); pump.ElectricalSuppliedWatts = 0;
            FluidDeviceOperations.AdvanceLiquidPump(pump, f.Seconds);
            int unpowered = middle.Level + far.Level + inlet.Level;
            // 正式机器逐步消费整组水量，Lua 检查空入口、有限水量和设备库存守恒。
            int steps = (int)Math.Ceiling(4d / pump.Definition.Fluid.ThroughputLitersPerSecond / f.Seconds) + 4;
            Require(steps > 0 && steps <= 10000, "抽液回归步数有效");
            for (int i = 0; i < steps; i++)
            {
                f.Begin(); pump.ElectricalSuppliedWatts = pump.Definition.Electrical.PowerWatts;
                FluidDeviceOperations.AdvanceLiquidPump(pump, f.Seconds);
            }
            var source = GetFluidInventory(pump); var target = GetFluidInventory(tank);
            bool bodyFound = ChunkMgr.ExistingInstance.TryGetStaticWaterBody(inlet.Position, out var body);
            return new JObject
            {
                ["initialLevel"] = 12, ["unpoweredLevel"] = unpowered, ["inletLevel"] = inlet.Level,
                ["remainingLevel"] = inlet.Level + middle.Level + far.Level,
                ["pumpLiters"] = source.GetLiquidLiters(), ["tankLiters"] = target.GetLiquidLiters(),
                ["bodyFound"] = bodyFound, ["bodyLevel"] = body.TotalLevel, ["bodyCells"] = body.CellCount
            };
        }
        finally
        {
            for (int x = 0; x < 3; x++)
            {
                var sample = cells[x].Sample;
                sample.Terrain.SetEnvironmentValue(TerrainSurfaceHeightLayer.ExcavatedLevelsLayerId,
                    sample.LocalCell.x, sample.LocalCell.y, excavated[x]);
                sample.Terrain.SetEnvironmentValue(TerrainSurfaceHeightLayer.CoverLevelsLayerId,
                    sample.LocalCell.x, sample.LocalCell.y, covered[x]);
            }
            WorldLiquidFlowObstacles.Unregister(barrier);
        }
    }

    internal static JObject CapturePumpFlow(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f = new GameplayFluidScope(surface: true); using var cell = new LiquidCell(f.SurfacePoint());
        cell.Set(10);
        var pump = f.Add("LiquidPump_Electric",1); var tank = f.Add("LiquidStorageTank",2);
        f.Begin(); pump.ElectricalSuppliedWatts=0; FluidDeviceOperations.AdvanceLiquidPump(pump,f.Seconds);
        int unpowered=cell.Level;
        for(int i=0;i<10;i++) { f.Begin();pump.ElectricalSuppliedWatts=pump.Definition.Electrical.PowerWatts;
            FluidDeviceOperations.AdvanceLiquidPump(pump,f.Seconds); }
        var source=GetFluidInventory(pump);var target=GetFluidInventory(tank);
        return new JObject { ["unpoweredLevel"]=unpowered,["level"]=cell.Level,["initialLevel"]=10,
            ["sourceLiters"]=source.GetLiquidLiters(),["targetLiters"]=target.GetLiquidLiters(),["seconds"]=f.Seconds,
            ["rate"]=pump.Definition.Fluid.ThroughputLitersPerSecond,["servingLiters"]=.25,
            ["dirtyWaterMoles"]=(double)target.GetLiquidMoles("core:dirty_water"),["pureWaterMoles"]=(double)target.GetLiquidMoles(FluidIds.PureWater) };
    }
    internal static JObject CaptureOutletFlow(Player player,JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f=new GameplayFluidScope(surface:true);using var cell=new LiquidCell(f.SurfacePoint(2));
        cell.Set(0);var tank=f.Add("LiquidStorageTank");var outlet=f.Add("LiquidOutlet",1);
        var water=FluidCatalog.Default.Find("core:dirty_water");var stock=GetFluidInventory(tank);
        stock.TryAdd(FluidInventory.CreateBatch(water,0,FluidUnits.LiquidLitersToMol(water,3),293.15),201,.001);
        GetFluidDeviceBehavior(outlet).Execute(outlet,"fluid.rate","0");f.Step();int stopped=cell.Level;
        GetFluidDeviceBehavior(outlet).Execute(outlet,"fluid.rate","2");
        for(int i=0;i<10;i++)f.Step();
        double after=stock.GetLiquidLiters();int emitted=cell.Level;
        cell.Set(10);double beforeFull=stock.GetLiquidLiters();f.Step();
        return new JObject { ["stoppedLevel"]=stopped,["emittedLevel"]=emitted,["sourceAfter"]=after,
            ["initialLiters"]=3,["fullSourceBefore"]=beforeFull,["fullSourceAfter"]=stock.GetLiquidLiters(),["seconds"]=f.Seconds };
    }
    #endregion
}
