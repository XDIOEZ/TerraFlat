using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Structures;
using Newtonsoft.Json.Linq;
using static MachineWorld;

/// <summary>隔离现场只调用生产图和快照接口，Lua 持有新工业规则断言。</summary>
internal static class GameplayTestFluidNetworks
{
    #region 原始采样
    internal static readonly Dictionary<string, Action<Action<string, bool, string>>> Cases = new();
    internal static void AddGas(MachineEntity node, string id, double liters)
    {
        if (!GetFluidInventory(node).TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(id),
            FluidUnits.StandardLitersToMol((decimal)liters), 0, 293.15), node.Definition.Fluid.VolumeLiters + .001, .001))
            throw new InvalidOperationException("测试气体装料失败。");
    }
    internal static double Gas(MachineEntity node) => (double)FluidUnits.MolToStandardLiters(GetFluidInventory(node).GasMoles);
    internal static JObject CaptureRoutes(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f = new GameplayFluidScope();
        var source = f.Add("GasTankBlock_Iron"); var pipe = f.Add("FluidPipe", 1);
        var valve = f.Add("FluidValve", 2); var filter = f.Add("GasSelector", 3); var filler = f.Add("GasFiller", 4);
        AddGas(source, FluidIds.Oxygen, 21); AddGas(source, FluidIds.Nitrogen, 79);
        GetFluidState(filter).SelectedGasId = FluidIds.Oxygen; f.Begin();
        var routes = new FluidNetworkGraph.SupplyWorkspace();
        bool Reach(string id)
        { CollectFluidSupplyRoutes(filler, "main", FluidPhase.Gas, new[] { id }, routes); return routes.Nodes.Any(n => n.Node == source); }
        bool oxygen = Reach(FluidIds.Oxygen), nitrogen = Reach(FluidIds.Nitrogen);
        GetFluidState(valve).ValveOpen = false; TopologyChanged(valve); bool closed = Reach(FluidIds.Oxygen);
        GetFluidState(valve).ValveOpen = true; TopologyChanged(valve);
        filter.State.RotationQuarterTurns = 2; TopologyChanged(filter); bool reverse = Reach(FluidIds.Oxygen);
        filter.State.RotationQuarterTurns = 0; TopologyChanged(filter);
        bool phase = false; CollectFluidSupplyRoutes(filler, "main", FluidPhase.Liquid, new[] { FluidIds.PureWater }, routes);
        phase = routes.Nodes.Any(n => n.Node == source);
        double before = Gas(source); int pipeChambers = GetFluidState(pipe).Chambers.Count; Remove(pipe.Id); f.Rebuild();
        return new JObject { ["oxygenReachable"] = oxygen, ["nitrogenReachable"] = nitrogen,
            ["closedReachable"] = closed, ["reverseReachable"] = reverse, ["liquidReachable"] = phase,
            ["beforeRemoval"] = before, ["afterRemoval"] = Gas(source), ["afterBreakReachable"] = Reach(FluidIds.Oxygen),
            ["pipeChambers"] = pipeChambers };
    }
    internal static JObject CaptureOwnership(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f = new GameplayFluidScope(); var a = f.Add("GasTankBlock_Iron"); var b = f.Add("GasTankBlock_Iron", 1);
        AddGas(a, FluidIds.Oxygen, 20); AddGas(b, FluidIds.Nitrogen, 70); f.Step();
        bool shared = ReferenceEquals(GetFluidInventory(a), GetFluidInventory(b));
        double capacity = GetFluidGasCapacityStandardLiters(a);
        ItemData savedA = CaptureSnapshot(a), savedB = CaptureSnapshot(b);
        PrepareDismantledFluidSnapshot(a, savedA);
        double savedAmount = (double)FluidUnits.MolToStandardLiters(new FluidInventory(MachinePersistence.Read<FluidMachineState>(savedA,"fluid").Chambers["main"]).GasMoles);
        double beforeAtmosphere = (double)f.Atmosphere.CurrentMoles;
        Remove(a.Id); f.Rebuild(); double remainingAfterRemoval = Gas(b);
        Remove(b.Id); f.ForgetDetached(a); f.ForgetDetached(b);
        var restoredB = RestoreMachine(savedB); var restoredA = RestoreMachine(savedA); f.Rebuild();
        var combinations = new JArray();
        int row = 0;
        foreach (string id in new[] { "GasTankBlock_Iron", "GasTankBlock_Steel", "LiquidTankBlock_Iron", "LiquidTankBlock_Steel" })
        {
            int y = 10 + row++ * 4;
            var left = f.Add(id, 0, y); var middle = f.Add(id, 1, y); var right = f.Add(id, 2, y); f.Rebuild();
            bool gas = id.StartsWith("Gas", StringComparison.Ordinal);
            if (gas) AddGas(left, FluidIds.Oxygen, 150);
            else GetFluidInventory(left).TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.PureWater), 0,
                FluidUnits.LiquidLitersToMol(FluidCatalog.Default.Find(FluidIds.PureWater), 150), 293.15), 300.001, .001);
            bool allShared = ReferenceEquals(GetFluidInventory(left), GetFluidInventory(middle)) && ReferenceEquals(GetFluidInventory(left), GetFluidInventory(right));
            double totalCapacity = gas ? GetFluidGasCapacityStandardLiters(left) : GetFluidVolumeLiters(left);
            int leftMask = GetFluidTankConnectionMask(left), middleMask = GetFluidTankConnectionMask(middle), rightMask = GetFluidTankConnectionMask(right);
            ItemData held = CaptureSnapshot(middle); PrepareDismantledFluidSnapshot(middle, held);
            var carried = new FluidInventory(MachinePersistence.Read<FluidMachineState>(held, "fluid").Chambers["main"]);
            Remove(middle.Id); f.Rebuild();
            double leftAmount = gas ? Gas(left) : GetFluidInventory(left).GetLiquidLiters();
            double rightAmount = gas ? Gas(right) : GetFluidInventory(right).GetLiquidLiters();
            bool split = !ReferenceEquals(GetFluidInventory(left), GetFluidInventory(right));
            int splitMask = GetFluidTankConnectionMask(left) | GetFluidTankConnectionMask(right);
            MachineEntity restoredMiddle = RestoreMachine(held); f.Rebuild();
            double restoredAmount = gas ? Gas(left) : GetFluidInventory(left).GetLiquidLiters();
            var templateResources = new Dictionary<int, FixedStructureResources>();
            foreach (MachineEntity member in new[] { left, restoredMiddle, right })
                templateResources.Add(member.Id, gas ? new FixedStructureResources { Gas = new FixedStructureGas {
                    StandardLiters = 30, Components = new Dictionary<string, double> { [FluidIds.Oxygen] = 1 } } } :
                    new FixedStructureResources { Liquids = new Dictionary<string, decimal> { [FluidIds.PureWater] = 30 } });
            FixedStructureResourceInitializer.ValidateSharedResources(f.Groups(), templateResources);
            var filled = new HashSet<FluidInventory>();
            foreach (MachineEntity member in new[] { left, restoredMiddle, right })
                FixedStructureResourceInitializer.Refill(member, templateResources[member.Id], filled);
            combinations.Add(new JObject { ["id"] = id, ["shared"] = allShared, ["capacity"] = totalCapacity,
                ["memberCapacity"] = gas ? left.Definition.Fluid.GasCapacityStandardLiters : left.Definition.Fluid.VolumeLiters,
                ["leftMask"] = leftMask, ["middleMask"] = middleMask, ["rightMask"] = rightMask,
                ["carried"] = gas ? (double)FluidUnits.MolToStandardLiters(carried.GasMoles) : carried.GetLiquidLiters(),
                ["leftAfterSplit"] = leftAmount, ["rightAfterSplit"] = rightAmount, ["split"] = split, ["splitMask"] = splitMask,
                ["restored"] = restoredAmount, ["refilled"] = gas ? Gas(left) : GetFluidInventory(left).GetLiquidLiters() });
        }
        var gasIron = f.Add("GasTankBlock_Iron", 20, 0); var liquidIron = f.Add("LiquidTankBlock_Iron", 21, 0);
        var gasSteel = f.Add("GasTankBlock_Steel", 20, 1); f.Rebuild();
        var waterTank = f.Add("LiquidTankBlock_Iron", 20, 20); var otherLiquidTank = f.Add("LiquidTankBlock_Iron", 22, 20); f.Rebuild();
        foreach (var sample in new[] { (Node: waterTank, Fluid: FluidIds.PureWater, Liters: 60m),
            (Node: otherLiquidTank, Fluid: FluidIds.FilteredWater, Liters: 30m) })
            GetFluidInventory(sample.Node).TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(sample.Fluid), 0,
                FluidUnits.LiquidLitersToMol(FluidCatalog.Default.Find(sample.Fluid), sample.Liters), 293.15), 100.001, .001);
        var bridge = f.Add("LiquidTankBlock_Iron", 21, 20); f.Rebuild();
        bool differentLiquidsSeparate = !ReferenceEquals(GetFluidInventory(waterTank), GetFluidInventory(otherLiquidTank));
        double liquidsBeforeEmptying = GetFluidInventory(waterTank).GetLiquidLiters() + GetFluidInventory(otherLiquidTank).GetLiquidLiters();
        var removedLiquid = new FluidInventory();
        IndustrialFluidTransfer.Transfer(GetFluidInventory(otherLiquidTank), removedLiquid, FluidPhase.Liquid, 30, 100, 100);
        StateChanged(otherLiquidTank); f.Rebuild();
        bool emptyTankJoined = ReferenceEquals(GetFluidInventory(waterTank), GetFluidInventory(otherLiquidTank)) &&
            GetFluidTankConnectionMask(bridge) == 10 && Math.Abs(GetFluidInventory(waterTank).GetLiquidLiters() - 60) < .000001;
        return new JObject { ["shared"] = shared, ["capacity"] = capacity, ["a"] = Gas(restoredA), ["b"] = Gas(restoredB),
            ["dismantledAmount"] = savedAmount, ["atmosphereDelta"] = (double)f.Atmosphere.CurrentMoles-beforeAtmosphere,
            ["remainingAfterRemoval"] = remainingAfterRemoval, ["combinations"] = combinations,
            ["typesAndMaterialsSeparate"] = GetFluidTankConnectionMask(gasIron) == 0 && GetFluidTankConnectionMask(liquidIron) == 0 && GetFluidTankConnectionMask(gasSteel) == 0,
            ["differentLiquidsSeparate"] = differentLiquidsSeparate, ["liquidsBeforeEmptying"] = liquidsBeforeEmptying,
            ["emptyTankJoined"] = emptyTankJoined,
            ["restoredShared"] = ReferenceEquals(GetFluidInventory(restoredA),GetFluidInventory(restoredB)) };
    }
    internal static JObject CaptureNaturalFill(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f = new GameplayFluidScope();var tank=f.Add("GasTankBlock_Iron");f.Rebuild();
        decimal initial=f.Atmosphere.CurrentMoles;bool filled=FillFluidTankAir(tank);double amount=Gas(tank);
        bool repeat=FillFluidTankAir(tank);decimal after=f.Atmosphere.CurrentMoles;
        AddGas(tank,FluidIds.Nitrogen,10);double higher=Gas(tank);bool highFill=FillFluidTankAir(tank);
        return new JObject { ["filled"]=filled,["amount"]=amount,["capacity"]=tank.Definition.Fluid.GasCapacityStandardLiters,
            ["fraction"]=tank.Definition.Fluid.NaturalFillFraction,["repeat"]=repeat,
            ["sourceTakenLiters"]=(double)FluidUnits.MolToStandardLiters(initial-after),["highFill"]=highFill,
            ["higherBefore"]=higher,["higherAfter"]=Gas(tank),["sourceStable"]=f.Atmosphere.CurrentMoles==after };
    }
    internal static JObject CaptureOutlet(Player player,JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f=new GameplayFluidScope();var tank=f.Add("GasTankBlock_Iron");var outlet=f.Add("FluidOutlet",1);
        AddGas(tank,FluidIds.Oxygen,21);AddGas(tank,FluidIds.Nitrogen,79);f.Rebuild();
        decimal beforeStopped = GetFluidInventory(tank).GasMoles;
        var behavior=GetFluidDeviceBehavior(outlet);bool zero=behavior.Execute(outlet,"fluid.rate","0");f.Step();double stopped=Gas(tank);
        bool stoppedContentsUnchanged = beforeStopped == GetFluidInventory(tank).GasMoles;
        bool rate=behavior.Execute(outlet,"fluid.rate","2");f.Step();
        var inventory=GetFluidInventory(tank);
        return new JObject { ["zeroAccepted"]=zero,["rateAccepted"]=rate,["stopped"]=stopped,["after"]=Gas(tank),
            ["stoppedContentsUnchanged"] = stoppedContentsUnchanged,
            ["seconds"]=f.Seconds,["oxygen"]=(double)FluidUnits.MolToStandardLiters(inventory.GetGasMoles(FluidIds.Oxygen)),
            ["nitrogen"]=(double)FluidUnits.MolToStandardLiters(inventory.GetGasMoles(FluidIds.Nitrogen)),
            ["savedRate"]=MachinePersistence.Read<FluidMachineState>(CaptureSnapshot(outlet),"fluid").OutputLitersPerSecond };
    }
    #endregion
}
