using System;
using System.Linq;
using UnityEngine;
using MemoryPack;

public static class Program
{
    #region 生产库存和连接图回归
    private static int passed, failed;
    private static void Require(bool value, string label) { if (!value) throw new Exception(label); }
    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-8;
    private static void Run(string label, Action test)
    { try { test(); passed++; Console.WriteLine("PASS " + label); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + label + ": " + e); } }
    private static FluidInventory Gas(double liters, double oxygen = 1)
    {
        var value = new FluidInventory();
        if (oxygen > 0) value.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find("test:a"), FluidUnits.StandardLitersToMol((decimal)(liters * oxygen)), 0, 300), 1000, .001);
        if (oxygen < 1) value.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find("test:b"), FluidUnits.StandardLitersToMol((decimal)(liters * (1 - oxygen))), 0, 300), 1000, .001);
        return value;
    }
    private static double Liters(FluidInventory value) => (double)FluidUnits.MolToStandardLiters(value.GasMoles);
    private static MachineEntity Node(int id, int x, string kind = "pipe", string phase = "gas") => new()
    {
        Id = id, Cell = new Vector2Int(x, 0), Definition = new MachineDefinition { Fluid = new FluidMachineDefinition
        { Kind = kind, Ports = new[] { new FluidMachinePortDefinition { Id = "left", Direction = 2, Phase = phase },
            new FluidMachinePortDefinition { Id = "right", Direction = 0, Phase = phase } } } }
    };
    public static int Main()
    {
        foreach (string id in new[] { "test:a", "test:b" }) FluidCatalog.Default.Register(new FluidDefinition(new FluidDefinitionDto
        { Id = id, MolarMassKgPerMol = 1, LiquidDensityKgPerLiter = 1, GasHeatCapacityJPerMolKelvin = 20, LiquidHeatCapacityJPerMolKelvin = 40 }));
        Run("quantity kernel reaches fixed capacity without overflow", () =>
        { var source = Gas(300); var target = Gas(0); double moved = IndustrialFluidTransfer.Transfer(source, target, FluidPhase.Gas, 200, 100, 100);
          Require(Near(moved,100) && Near(Liters(target),100) && Near(Liters(source),200), "capacity or conservation");
          Require(IndustrialFluidTransfer.Transfer(source,target,FluidPhase.Gas,100,100,100)==0, "full tank accepted more"); });
        Run("mixed gas follows actual proportions", () =>
        { var source = Gas(100,.21); var target = Gas(0); IndustrialFluidTransfer.Transfer(source,target,FluidPhase.Gas,50,100,100);
          Require(Near((double)FluidUnits.MolToStandardLiters(target.GetGasMoles("test:a")),10.5) && Near(Liters(source),50), "mixture changed"); });
        Run("filter leaves every rejected component in source", () =>
        { var source=Gas(200,.21); var target=Gas(0); decimal nitrogen=source.GetGasMoles("test:b");
          double moved=IndustrialFluidTransfer.Transfer(source,target,FluidPhase.Gas,100,100,100,"test:a");
          Require(Near(moved,21) && target.GetGasMoles("test:b")==0 && source.GetGasMoles("test:b")==nitrogen && Near(Liters(source)+Liters(target),200),"filtered mixture transformed or vanished"); });
        Run("no source and zero rate cannot create matter", () =>
        { var empty=Gas(0); var target=Gas(0); Require(IndustrialFluidTransfer.Transfer(empty,target,FluidPhase.Gas,100,100,100)==0,"empty manufactured");
          Require(IndustrialFluidTransfer.Transfer(Gas(100),target,FluidPhase.Gas,0,100,100)==0 && target.IsEmpty,"disabled manufactured"); });
        Run("partial reservations preserve mixed ratio", () =>
        { var source=Gas(100,.5); var target=Gas(0); source.TryReserve("reserved",FluidPhase.Gas,FluidUnits.StandardLitersToMol(25),out _);
          IndustrialFluidTransfer.Transfer(source,target,FluidPhase.Gas,100,100,100);
          Require(Near((double)target.GetGasMoles("test:a"),(double)target.GetGasMoles("test:b")),"reservation changed unfiltered ratio"); });
        Run("sequential quantity transfers never oversell source", () =>
        { var source=Gas(100,.21); var a=Gas(0); var b=Gas(0);
          IndustrialFluidTransfer.Transfer(source,a,FluidPhase.Gas,80,100,100); IndustrialFluidTransfer.Transfer(source,b,FluidPhase.Gas,80,100,100);
          Require(Near(Liters(source)+Liters(a)+Liters(b),100) && Near(Liters(b),20),"oversold"); });
        Run("liquid conflict rejects atomically", () =>
        { var source=Gas(0);var target=Gas(0);source.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find("test:a"),0,10,300),100,.001);
          target.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find("test:b"),0,1,300),100,.001);
          Require(IndustrialFluidTransfer.Transfer(source,target,FluidPhase.Liquid,10,0,100)==0 && source.LiquidMoles==10 && target.LiquidMoles==1,"liquid conflict changed stock"); });
        Run("capacity decimal tail never exceeds target", () =>
        { var source=Gas(1000,.21);var target=Gas(0);for(int i=0;i<1000;i++)IndustrialFluidTransfer.Transfer(source,target,FluidPhase.Gas,.13,1.23456789,0);
          Require(Liters(target)<=1.23456789+1e-12 && Near(Liters(source)+Liters(target),1000),"tail overflow"); });
        Run("independent tank snapshots do not alias", () =>
        { var a=Gas(20,.21);var b=Gas(70,.5);var copy=new FluidInventory(MemoryPackSerializer.Deserialize<FluidInventoryState>(MemoryPackSerializer.Serialize(a.State)));
          IndustrialFluidTransfer.Transfer(copy,b,FluidPhase.Gas,10,100,100);
          Require(Near(Liters(a),20)&&Near(Liters(copy),10)&&Near(Liters(b),80),"local snapshot ownership changed"); });
        Run("pipe rebuild and removal never move inventory or create capacity", () =>
        { var tank=Node(1,0,"device"); tank.Local=Gas(30);var pipe=Node(2,1);var graph=new FluidNetworkGraph(c=>c);graph.Rebuild(new[]{tank,pipe}); graph.Rebuild(new[]{tank});
          Require(Near(Liters(tank.Local),30)&&pipe.Local.IsEmpty,"topology moved inventory"); });
        Run("phase-specific networks do not connect", () =>
        { var graph=new FluidNetworkGraph(c=>c); graph.Rebuild(new[]{Node(1,0,phase:"gas"),Node(2,1,phase:"liquid")});
          Require(graph.Edges.Count==0,"gas/liquid crossed"); });
        Run("closed valves and one-way ports stop route search", () =>
        { var source=Node(1,0,"device");var valve=Node(2,1);var target=Node(3,2,"device");var graph=new FluidNetworkGraph(c=>c);var routes=new FluidNetworkGraph.SupplyWorkspace();
          valve.State.ValveOpen=false;graph.Rebuild(new[]{source,valve,target});graph.CollectSupplyRoutes(target,"main",FluidPhase.Gas,new[]{"test:a"},routes);
          Require(!routes.Nodes.Any(n=>n.Node==source),"closed valve bypassed"); valve.State.ValveOpen=true;valve.Definition.Fluid.Ports[0].Mode="output";valve.Definition.Fluid.Ports[1].Mode="input";
          graph.Rebuild(new[]{source,valve,target});graph.CollectSupplyRoutes(target,"main",FluidPhase.Gas,new[]{"test:a"},routes);Require(!routes.Nodes.Any(n=>n.Node==source),"backwards valve bypassed"); });
        Console.WriteLine($"Result: {passed} passed, {failed} failed"); return failed==0?0:1;
    }
    #endregion
}
