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
    private static void RegisterConnectedPumpProbe()
    {
        GameplayTestRuntimeProbe.Register("industrial_fluids", "connected_surface_pump", CaptureConnectedPumpFlow);
        GameplayTestRuntimeProbe.Register("industrial_fluids", "shared_surface_pump_search", CaptureSharedPumpSearchContract);
        GameplayTestRuntimeProbe.Register("industrial_fluids", "combined_tank_pump_tick", CaptureCombinedTankPumpTickContract);
    }

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
    private sealed class IsolatedPumpCellBoundary : IDisposable
    {
        public IsolatedPumpCellBoundary(params Vector2Int[] cells)
        {
            // 仅封闭测试池外邻，既防借用原水域又保留测试池内部连通。
            var interior = new HashSet<Vector2Int>(cells);
            var boundary = new HashSet<Vector2Int>();
            foreach (Vector2Int cell in cells)
            foreach (Vector2Int direction in MechanicalNetworkGraph.Directions)
                if (!interior.Contains(cell + direction)) boundary.Add(cell + direction);
            WorldLiquidFlowObstacles.Register(this, boundary);
        }
        public void Dispose() => WorldLiquidFlowObstacles.Unregister(this);
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
        Check(report, "整批倾倒超出格容量拒绝且保留余量", !WorldLiquidSystem.TryPour(cell.Position, id, 100, out _) && cell.Level == before);
        string other = FluidCatalog.Default.Find("core:salt_water").LiquidId;
        Check(report, "异种液体与非正等级拒绝", !WorldLiquidSystem.TryPour(cell.Position, other, 1, out _) &&
            !WorldLiquidSystem.TryPump(cell.Position, 0, out _, out _) && cell.Level == before);
        Check(report, "抽到空格只返回实际剩余液体", WorldLiquidSystem.TryPump(cell.Position, 20, out _, out removed) && removed == before && cell.Level == 0);
        Check(report, "空格不能继续造水", !WorldLiquidSystem.TryPump(cell.Position, 1, out _, out _));
        Check(report, "液体操作保持 Ground", cell.Ground == ground);
    }

    #endregion

    #region 简单工业真实地表采样
    // 双泵使用真实地表、电池和管罐跑完整Tick，覆盖十升首批回调读图及下一批预算。
    internal static JObject CaptureCombinedTankPumpTickContract(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var fixture = new GameplayFluidScope(surface: true, surfaceWidth: 7, surfaceHeight: 4,
            surfaceLeft: -1, surfaceBottom: -1);
        using var firstSource = new LiquidCell(fixture.SurfacePoint(-1, 0));
        using var secondSource = new LiquidCell(fixture.SurfacePoint(-1, 1));
        using var boundary = new IsolatedPumpCellBoundary(firstSource.Sample.WorldCell, secondSource.Sample.WorldCell);
        firstSource.Set(100); secondSource.Set(100);
        var pumps = new[] { fixture.Add("LiquidPump_Electric", 0, 0), fixture.Add("LiquidPump_Electric", 0, 1) };
        fixture.Add("LiquidPipe", 1, 0); fixture.Add("LiquidPipe", 1, 1);
        var tanks = new HashSet<MachineEntity>();
        for (int y = 0; y < 3; y++)
        for (int x = 2; x < 6; x++) tanks.Add(fixture.Add("LiquidTankBlock_Iron", x, y));
        var batteries = new[] { fixture.Add("Battery_Basic", 0, -1), fixture.Add("Battery_Basic", -1, -1) };
        fixture.Add("Wire_Copper", -1, -1); fixture.Add("Wire_Copper", 0, -1);
        fixture.Add("Wire_Copper", 0, 0); fixture.Add("Wire_Copper", 0, 1);
        fixture.Tick();
        double initialLiters = (firstSource.Level + secondSource.Level) * LiquidCellValue.LitersPerUnit;
        foreach (MachineEntity pump in pumps)
        {
            pump.Definition.Fluid.ThroughputLitersPerSecond = 6d;
            TopologyChanged(pump);
        }
        foreach (MachineEntity battery in batteries) battery.ElectricalStoredJoules = battery.Definition.Electrical.CapacityJoules;
        fixture.Rebuild();
        int stepsPerBatch = (int)Math.Ceiling(10d / (6d * fixture.Seconds) - .000001d);
        for (int i = 0; i < stepsPerBatch - 1; i++) fixture.Tick();
        FluidNetworkGraph before = fixture.Graph;
        MachineEntity firstTank = null;
        foreach (MachineEntity tank in tanks) { firstTank = tank; break; }
        int notifications = 0;
        bool stableGraph = true, stableBudget = true;
        void ObserveTankState(MachineEntity node)
        {
            if (!tanks.Contains(node)) return;
            notifications++;
            stableGraph &= ReferenceEquals(before, fixture.Graph);
            stableBudget &= fixture.Budget(GetFluidInventory(node), out _, out _);
            stableGraph &= GetFluidTankMembers(node).Count == tanks.Count;
        }
        NodeStateChanged += ObserveTankState;
        double firstDelivered;
        try
        {
            fixture.Tick();
            firstDelivered = GetFluidInventory(firstTank).GetLiquidLiters();
        }
        finally { NodeStateChanged -= ObserveTankState; }
        bool rebuiltBetweenSteps = !ReferenceEquals(before, fixture.Graph);
        for (int i = 0; i < stepsPerBatch; i++) fixture.Tick();
        double finalDelivered = GetFluidInventory(firstTank).GetLiquidLiters(), buffers = 0d;
        foreach (MachineEntity pump in pumps) buffers += GetFluidInventory(pump).GetLiquidLiters();
        return new JObject
        {
            ["tankCount"] = tanks.Count, ["notifications"] = notifications,
            ["stableGraph"] = stableGraph, ["stableBudget"] = stableBudget,
            ["rebuiltBetweenSteps"] = rebuiltBetweenSteps, ["firstDelivered"] = firstDelivered,
            ["expectedFirstDelivered"] = 20d,
            ["finalDelivered"] = finalDelivered, ["buffers"] = buffers, ["initialLiters"] = initialLiters,
            ["remainingSurfaceLiters"] = (firstSource.Level + secondSource.Level) * LiquidCellValue.LitersPerUnit,
            ["powered"] = pumps[0].ElectricalSuppliedWatts > 0f && pumps[1].ElectricalSuppliedWatts > 0f
        };
    }

    internal static JObject CaptureSharedPumpSearchContract(Player player, JObject args)
    {
        args = (JObject)args.DeepClone();
        args["sharedCacheContract"] = true;
        return CaptureConnectedPumpFlow(player, args);
    }

    internal static JObject CaptureConnectedPumpFlow(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f = new GameplayFluidScope(surface: true);
        using var inlet = new LiquidCell(f.SurfacePoint());
        using var middle = new LiquidCell(f.SurfacePoint(1));
        using var far = new LiquidCell(f.SurfacePoint(2));
        LiquidCell[] cells = { inlet, middle, far };
        bool sharedCacheContract = args.Value<bool?>("sharedCacheContract") == true;
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
                cells[x].Set(sharedCacheContract ? (x == 2 ? 2 : 1) : x == 0 ? 0 : x == 1 ? 7 : 5);
            }
            if (sharedCacheContract) return CaptureSharedPumpSelections(cells);
            var pump = f.Add("LiquidPump_Electric", 1);
            var tank = f.Add("LiquidStorageTank", 2);
            f.Begin(); pump.ElectricalSuppliedWatts = 0;
            FluidDeviceOperations.AdvanceLiquidPump(pump, f.Seconds);
            int unpowered = middle.Level + far.Level + inlet.Level;
            // 正式机器逐步消费整组水量，Lua 检查空入口、有限水量和设备库存守恒。
            int steps = (int)Math.Ceiling(140d / pump.Definition.Fluid.ThroughputLitersPerSecond / f.Seconds) + 4;
            Require(steps > 0 && steps <= 10000, "抽液回归步数有效");
            for (int i = 0; i < steps; i++)
            {
                f.Begin(); pump.ElectricalSuppliedWatts = pump.Definition.Electrical.PowerWatts;
                FluidDeviceOperations.AdvanceLiquidPump(pump, f.Seconds);
                ChunkMgr.ExistingInstance.AdvanceWorldRuntime(0f);
            }
            var source = GetFluidInventory(pump); var target = GetFluidInventory(tank);
            bool bodyFound = ChunkMgr.ExistingInstance.TryGetStaticWaterBody(inlet.Position, out var body);
            return new JObject
            {
                ["initialLevel"] = 12, ["initialLiters"] = 120, ["litersPerUnit"] = LiquidCellValue.LitersPerUnit,
                ["unpoweredLevel"] = unpowered, ["inletLevel"] = inlet.Level,
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

    private static JObject CaptureSharedPumpSelections(LiquidCell[] cells)
    {
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        WorldLiquidPumpSourceSelection first = manager.SelectConnectedLiquidPumpSource(cells[0].Position, out var source);
        int groups = manager.SharedLiquidPumpSearchGroupCount;
        long builds = manager.SharedLiquidPumpTopologyBuildCount;
        WorldLiquidPumpSourceSelection second = manager.SelectConnectedLiquidPumpSource(cells[1].Position, out var staleSource);
        bool shared = groups == manager.SharedLiquidPumpSearchGroupCount && builds == manager.SharedLiquidPumpTopologyBuildCount;
        bool taken = first == WorldLiquidPumpSourceSelection.Surplus && WorldLiquidSystem.TryPumpPreservingMinimumLevel(
            source, 99, 1, out _, out int removed);
        int afterSource = cells[2].Level;
        bool rejectedStale = !WorldLiquidSystem.TryPumpPreservingMinimumLevel(staleSource, 1, 1, out _, out _);
        WorldLiquidPumpSourceSelection final = manager.SelectConnectedLiquidPumpSource(cells[0].Position, out var local);
        for (int i = 0; i < 64 && final == WorldLiquidPumpSourceSelection.Searching; i++)
        {
            manager.AdvanceWorldRuntime(0f);
            final = manager.SelectConnectedLiquidPumpSource(cells[0].Position, out local);
        }
        bool localTaken = final == WorldLiquidPumpSourceSelection.LocalRemainder &&
            WorldLiquidSystem.TryPump(local, 1, out _, out _);
        bool refunded = WorldLiquidSystem.TryPour(cells[2].Position, cells[2].Definition.Id, 1, out int added) && added == 1;
        WorldLiquidPumpSourceSelection reactivated = manager.SelectConnectedLiquidPumpSource(cells[0].Position, out _);
        return new JObject
        {
            ["first"] = first.ToString(), ["second"] = second.ToString(), ["sameSource"] = source.WorldCell == staleSource.WorldCell,
            ["shared"] = shared, ["taken"] = taken, ["sourceAfter"] = afterSource, ["rejectedStale"] = rejectedStale,
            ["final"] = final.ToString(), ["localTaken"] = localTaken, ["inletAfter"] = cells[0].Level,
            ["middleAfter"] = cells[1].Level, ["refunded"] = refunded, ["reactivated"] = reactivated.ToString(),
            ["remaining"] = cells[0].Level + cells[1].Level + cells[2].Level
        };
    }

    internal static JObject CapturePumpFlow(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f = new GameplayFluidScope(surface: true); using var cell = new LiquidCell(f.SurfacePoint());
        using var boundary = new IsolatedPumpCellBoundary(cell.Sample.WorldCell);
        cell.Set(100);
        var pump = f.Add("LiquidPump_Electric",1); var tank = f.Add("LiquidStorageTank",2);
        f.Begin(); pump.ElectricalSuppliedWatts=0; FluidDeviceOperations.AdvanceLiquidPump(pump,f.Seconds);
        int unpowered=cell.Level;
        var source=GetFluidInventory(pump);var target=GetFluidInventory(tank);
        double stepLiters = pump.Definition.Fluid.ThroughputLitersPerSecond * f.Seconds;
        int steps = (int)Math.Ceiling(LiquidCellValue.LitersPerUnit / stepLiters - .000001d);
        Require(steps > 1 && steps < 10000, "低速抽液量子步数有效");
        void PumpStep(bool powered = true)
        {
            f.Begin(); pump.ElectricalSuppliedWatts = powered ? pump.Definition.Electrical.PowerWatts : 0f;
            FluidDeviceOperations.AdvanceLiquidPump(pump, f.Seconds);
        }
        for (int i = 0; i < steps - 1; i++) PumpStep();
        int beforeBatchLevel = cell.Level; double beforeBatchLiters = target.GetLiquidLiters();
        PumpStep(false);
        PumpStep();
        int restartedLevel = cell.Level;
        for (int i = 1; i < steps; i++) PumpStep();
        int firstBatchLevel = cell.Level; double firstBatchLiters = target.GetLiquidLiters();
        FluidInventoryState firstBatchState = target.State.Clone();
        FluidDefinition water = FluidCatalog.Default.Find("core:dirty_water");
        double fillLiters = GetFluidVolumeLiters(tank) - target.GetLiquidLiters() - 5d;
        Require(target.TryAdd(FluidInventory.CreateBatch(water, 0m,
            FluidUnits.LiquidLitersToMol(water, (decimal)fillLiters), 293.15), GetFluidVolumeLiters(tank), .001d), "目标只剩五升空间");
        double blockedBefore = target.GetLiquidLiters();
        for (int i = 0; i < steps + 1; i++) PumpStep();
        bool blockedStable = cell.Level == firstBatchLevel && Near(blockedBefore, target.GetLiquidLiters());
        target.Restore(firstBatchState);
        PumpStep(); int reopenedLevel = cell.Level;
        for (int i = 1; i < steps; i++) PumpStep();
        return new JObject { ["unpoweredLevel"]=unpowered,["level"]=cell.Level,["initialLevel"]=100,
            ["stepsPerBatch"] = steps, ["beforeBatchLevel"] = beforeBatchLevel, ["beforeBatchLiters"] = beforeBatchLiters,
            ["restartedLevel"] = restartedLevel, ["firstBatchLevel"] = firstBatchLevel, ["firstBatchLiters"] = firstBatchLiters,
            ["blockedStable"] = blockedStable, ["reopenedLevel"] = reopenedLevel,
            ["sourceLiters"]=source.GetLiquidLiters(),["targetLiters"]=target.GetLiquidLiters(),["seconds"]=f.Seconds,
            ["rate"]=pump.Definition.Fluid.ThroughputLitersPerSecond,["servingLiters"]=LiquidCellValue.LitersPerUnit,
            ["dirtyWaterMoles"]=(double)target.GetLiquidMoles("core:dirty_water"),["pureWaterMoles"]=(double)target.GetLiquidMoles(FluidIds.PureWater) };
    }
    internal static JObject CaptureOutletFlow(Player player,JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f=new GameplayFluidScope(surface:true);using var cell=new LiquidCell(f.SurfacePoint(2));
        cell.Set(0);var tank=f.Add("LiquidStorageTank");var outlet=f.Add("LiquidOutlet",1);
        var water=FluidCatalog.Default.Find("core:dirty_water");var stock=GetFluidInventory(tank);
        Require(stock.TryAdd(FluidInventory.CreateBatch(water,0,FluidUnits.LiquidLitersToMol(water,25),293.15),201,.001),
            "出口回归初始装料");
        double initialLiters = stock.GetLiquidLiters();
        GetFluidDeviceBehavior(outlet).Execute(outlet,"fluid.rate","0");f.Step();int stopped=cell.Level;
        GetFluidDeviceBehavior(outlet).Execute(outlet,"fluid.rate","2");
        int steps = (int)Math.Ceiling(10d / (2d * f.Seconds) - .000001d);
        for(int i=0;i<steps-1;i++)f.Step();
        int partialLevel = cell.Level; double partialSource = stock.GetLiquidLiters();
        f.Step();
        double after=stock.GetLiquidLiters();int emitted=cell.Level;
        cell.Set(100);double beforeFull=stock.GetLiquidLiters();
        for(int i=0;i<steps+1;i++)f.Step();
        double afterFull = stock.GetLiquidLiters();
        Require(stock.TryAdd(FluidInventory.CreateBatch(water, 0m,
            FluidUnits.LiquidLitersToMol(water, 25m), 293.15), 201d, .001d), "末格容量边界装料");
        cell.Set(99);
        int singleFreeInitialLevel = cell.Level;
        GetFluidState(outlet).WorldLiquidOutputLiters.Clear();
        double singleFreeBefore = stock.GetLiquidLiters();
        // 仅提高测试储罐的路径流量，确保本步确实有二十升预算。
        tank.Definition.Fluid.ThroughputLitersPerSecond = 20d / f.Seconds;
        Require(GetFluidDeviceBehavior(outlet).Execute(outlet, "fluid.rate",
            (20d / f.Seconds).ToString("R", System.Globalization.CultureInfo.InvariantCulture)),
            "设置末格容量边界的出口速率");
        f.Begin();
        var routes = new FluidNetworkGraph.SupplyWorkspace();
        CollectFluidSupplyRoutes(outlet, "main", FluidPhase.Liquid, new[] { water.Id }, routes);
        int sourceIndex = routes.Nodes.FindIndex(candidate => candidate.Node == tank);
        Require(sourceIndex > 0, "末格容量边界的实际来源路径");
        double singleFreeBudgetLiters = GetFluidSupplyStandardLiters(routes, sourceIndex, f.Seconds, true);
        f.Complete();
        return new JObject { ["stoppedLevel"]=stopped,["emittedLevel"]=emitted,["sourceAfter"]=after,
            ["partialLevel"] = partialLevel, ["partialSource"] = partialSource, ["litersPerUnit"] = LiquidCellValue.LitersPerUnit,
            ["singleFreeBudgetLiters"] = singleFreeBudgetLiters, ["singleFreeInitialLevel"] = singleFreeInitialLevel,
            ["singleFreeFinalLevel"] = cell.Level,
            ["singleFreeSourceBefore"] = singleFreeBefore, ["singleFreeSourceAfter"] = stock.GetLiquidLiters(),
            ["initialLiters"]=initialLiters,["fullSourceBefore"]=beforeFull,["fullSourceAfter"]=afterFull,["seconds"]=f.Seconds };
    }
    #endregion
}
