using System;
using System.Collections.Generic;
using System.Linq;
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
        bool separate = !ReferenceEquals(GetFluidInventory(a), GetFluidInventory(b));
        ItemData savedA = CaptureSnapshot(a), savedB = CaptureSnapshot(b);
        PrepareDismantledFluidSnapshot(a, savedA);
        double savedAmount = (double)FluidUnits.MolToStandardLiters(new FluidInventory(MachinePersistence.Read<FluidMachineState>(savedA,"fluid").Chambers["main"]).GasMoles);
        double beforeAtmosphere = (double)f.Atmosphere.CurrentMoles;
        Remove(a.Id); Remove(b.Id); f.ForgetDetached(a); f.ForgetDetached(b);
        var restoredB = RestoreMachine(savedB); var restoredA = RestoreMachine(savedA); f.Rebuild();
        return new JObject { ["separate"] = separate, ["a"] = Gas(restoredA), ["b"] = Gas(restoredB),
            ["dismantledAmount"] = savedAmount, ["atmosphereDelta"] = (double)f.Atmosphere.CurrentMoles-beforeAtmosphere,
            ["restoredSeparate"] = !ReferenceEquals(GetFluidInventory(restoredA),GetFluidInventory(restoredB)) };
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
        var behavior=GetFluidDeviceBehavior(outlet);bool zero=behavior.Execute(outlet,"fluid.rate","0");f.Step();double stopped=Gas(tank);
        bool rate=behavior.Execute(outlet,"fluid.rate","2");f.Step();
        var inventory=GetFluidInventory(tank);
        return new JObject { ["zeroAccepted"]=zero,["rateAccepted"]=rate,["stopped"]=stopped,["after"]=Gas(tank),
            ["seconds"]=f.Seconds,["oxygen"]=(double)FluidUnits.MolToStandardLiters(inventory.GetGasMoles(FluidIds.Oxygen)),
            ["nitrogen"]=(double)FluidUnits.MolToStandardLiters(inventory.GetGasMoles(FluidIds.Nitrogen)),
            ["savedRate"]=MachinePersistence.Read<FluidMachineState>(CaptureSnapshot(outlet),"fluid").OutputLitersPerSecond };
    }
    #endregion
}
