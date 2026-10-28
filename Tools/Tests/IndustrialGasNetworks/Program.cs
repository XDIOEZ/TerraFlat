using System;
using System.Collections.Generic;
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
        Run("infinite gas delivers above its finite sample repeatedly", () =>
        { var source=Gas(0);source.ConfigureInfiniteGasSupply(new Dictionary<string,double>{{"test:a",2},{"test:b",1}},300,100);
          var target=Gas(0);var sample=source.State.Clone();
          for(int i=0;i<3;i++)Require(Near(IndustrialFluidTransfer.Transfer(source,target,FluidPhase.Gas,250,750,100),250),"throughput limited by sample");
          Require(Near(Liters(source),100)&&Near(Liters(target),750)&&Near((double)FluidUnits.MolToStandardLiters(target.GetGasMoles("test:a")),500),"supply altered source or composition");
          Require(source.State.TotalInternalEnergyJoules==sample.TotalInternalEnergyJoules&&IndustrialFluidTransfer.Transfer(source,target,FluidPhase.Gas,1,750,100)==0,"source changed or full target accepted gas");
          var restored=new FluidInventory(source.State.Clone());restored.ConfigureInfiniteGasSupply(new Dictionary<string,double>{{"test:a",2},{"test:b",1}},300,100);
          Require(restored.HasInfiniteGasSupply&&Near(IndustrialFluidTransfer.Transfer(restored,Gas(0),FluidPhase.Gas,200,500,100),200),"definition did not rebuild source"); });
        Run("infinite filtered supply and reservations stay bounded and transient", () =>
        { var source=Gas(0);source.ConfigureInfiniteGasSupply(new Dictionary<string,double>{{"test:a",2},{"test:b",1}},320,100);var target=Gas(0);
          Require(Near(IndustrialFluidTransfer.Transfer(source,target,FluidPhase.Gas,90,300,100,"test:b"),30)&&Near(Liters(source),100),"filter or source composition changed");
          decimal amount=FluidUnits.StandardLitersToMol(200);
          Require(source.TryReserve("a",FluidPhase.Gas,amount,id=>id=="test:a",out var a)&&a.GasMoles==amount,"first reservation clipped");
          Require(source.TryReserve("b",FluidPhase.Gas,amount,id=>id=="test:a",out var b)&&b.GasMoles==amount,"second reservation clipped");
          Require(source.State.Ports.Count==0&&source.TryTakeReserved("a",amount,out var taken)&&taken.GasMoles==amount,"transient reservation or extraction failed");
          source.ReleaseReservation("b");Require(source.GetReservation("b")==null&&Near(Liters(source),100),"release or extraction modified sample");
          Require(!source.TryTakeRecipe(new[]{new FluidRequirement("test:a",FluidPhase.Gas,amount),new FluidRequirement("test:missing",FluidPhase.Gas,1)},out _)&&Near(Liters(source),100),"missing recipe was not atomic");
          source.ClearGas(out var removed);Require(removed.Count==0&&Near(Liters(source),100)&&source.Drain().Count==0&&Near(Liters(source),100),"virtual sample escaped during cleanup"); });
        Run("infinite sample survives empty and liquid-only restores", () =>
        { var source=Gas(0);source.ConfigureInfiniteGasSupply(new Dictionary<string,double>{{"test:a",2},{"test:b",1}},320,100);
          decimal amount=FluidUnits.StandardLitersToMol(200);
          Require(source.TryReserve("pending",FluidPhase.Gas,amount,id=>id=="test:a",out _),"reservation failed");
          source.Restore(new FluidInventoryState());Require(Near(Liters(source),100),"empty restore removed sample");
          var liquid=Gas(0);Require(liquid.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find("test:b"),0,2,280),100,.001),"liquid setup failed");
          source.Restore(liquid.State);
          Require(source.LiquidMoles==2&&Near(Liters(source),100)&&Near((double)(source.GetGasMoles("test:a")/source.GasMoles),2d/3d),"liquid restore changed composition or lost liquid");
          var before=source.State.Clone();source.Restore(before);
          Require(source.State.TotalInternalEnergyJoules==before.TotalInternalEnergyJoules,"matching restore changed energy");
          Require(source.TryTakeReserved("pending",amount,out _)&&Near(IndustrialFluidTransfer.Transfer(source,Gas(0),FluidPhase.Gas,200,500,100),200),"restore lost reservation or supply");
          source.Restore(Gas(40,0).State);Require(Near(Liters(source),100)&&Near((double)(source.GetGasMoles("test:a")/source.GasMoles),2d/3d),"foreign sample overrode definition");
          var finite=Gas(20);finite.Restore(new FluidInventoryState());Require(finite.IsEmpty,"ordinary restore changed"); });
        Run("four output-only source ports retain directed routes", () =>
        { for(int direction=0;direction<4;direction++) { var source=Node(1,0,"device");var target=Node(2,0,"device");
          var delta=MechanicalNetworkGraph.Directions[direction];target.Cell=delta;
          source.Definition.Fluid.Ports=Enumerable.Range(0,4).Select(i=>new FluidMachinePortDefinition{Id="o"+i,Direction=i,Mode="output",Phase="gas"}).ToArray();
          target.Definition.Fluid.Ports=new[]{new FluidMachinePortDefinition{Id="in",Direction=(direction+2)%4,Mode="input",Phase="gas"}};
          var graph=new FluidNetworkGraph(c=>c);graph.Rebuild(new[]{source,target});var routes=new FluidNetworkGraph.SupplyWorkspace();
          graph.CollectSupplyRoutes(target,"main",FluidPhase.Gas,new[]{"test:a"},routes);
          Require(routes.Nodes.Any(n=>n.Node==source),"missing direction "+direction); } });
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
        Run("combined gas tanks share capacity and both gases", () =>
        { var a=Gas(20); var b=Gas(70,0); var combined=new CombinedFluidTankInventory();
          combined.AddMember(1,100,100,.1,a.State); combined.AddMember(2,100,100,.1,b.State);
          Require(combined.Count==2 && Near(combined.VolumeLiters,200) && Near(combined.GasCapacityStandardLiters,200) && Near(Liters(combined.Inventory),90),"not combined");
          var source=Gas(300);double moved=IndustrialFluidTransfer.Transfer(source,combined.Inventory,FluidPhase.Gas,300,combined.GasCapacityStandardLiters,combined.VolumeLiters);
          Require(Near(moved,110)&&Near(Liters(combined.Inventory),200)&&Near(Liters(source),190),"combined gas capacity clipped or created matter");
          combined.SynchronizeLocalShares();Require(Near(Liters(a),100)&&Near(Liters(b),100),"local shares not saved"); });
        Run("dismantled combined member takes only its conserved share", () =>
        { var a=Gas(20);var b=Gas(70,0);var combined=new CombinedFluidTankInventory();
          combined.AddMember(1,100,100,.1,a.State);combined.AddMember(2,100,100,.1,b.State);combined.SynchronizeLocalShares();
          var savedA=new FluidInventory(a.State.Clone());var savedB=new FluidInventory(b.State.Clone());
          combined.RemoveMember(1);Require(Near(Liters(combined.Inventory),45)&&Near(Liters(savedA),45)&&Near(combined.VolumeLiters,100),"removal lost or cloned group");
          var restored=new CombinedFluidTankInventory();restored.AddMember(2,100,100,.1,savedB.State);restored.AddMember(1,100,100,.1,savedA.State);
          Require(Near(Liters(restored.Inventory),90)&&Near((double)restored.Inventory.GetGasMoles("test:a"),(double)Gas(20).GasMoles),"reverse restore changed composition"); });
        Run("repeated uneven combined shares preserve exact moles and energy", () =>
        { var original=Gas(1,.21);var first=original.State;var second=new FluidInventoryState();var third=new FluidInventoryState();
          decimal exactGas=original.GasMoles;double energy=first.TotalInternalEnergyJoules;
          for(int index=0;index<100;index++){var combined=new CombinedFluidTankInventory();combined.AddMember(1,1,1,.001,first);combined.AddMember(2,2,2,.001,second);combined.AddMember(3,3,3,.001,third);combined.SynchronizeLocalShares();
            Require(first.Components.Sum(c=>c.GasMoles)+second.Components.Sum(c=>c.GasMoles)+third.Components.Sum(c=>c.GasMoles)==exactGas,"decimal share drift");
            Require(Near(first.TotalInternalEnergyJoules+second.TotalInternalEnergyJoules+third.TotalInternalEnergyJoules,energy),"energy drift");}
          Require(Near(Liters(new FluidInventory(first)),1d/6)&&Near(Liters(new FluidInventory(third)),.5),"capacity weighting ignored"); });
        Run("combined liquid blocks add 800L per member and conserve 1300L", () =>
        { var a=new FluidInventory();var b=new FluidInventory();var foreign=new FluidInventory();
          a.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find("test:a"),0,700,300),800,.001);
          b.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find("test:a"),0,600,300),800,.001);
          foreign.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find("test:b"),0,10,300),100,.001);
          var combined=new CombinedFluidTankInventory();combined.AddMember(1,800,100,.1,a.State);combined.AddMember(2,800,100,.1,b.State);
          Require(combined.Count==2 && Near(combined.VolumeLiters,1600),"800L block capacities did not add");
          Require(!combined.CanCombineLiquid(foreign.State),"foreign liquid merged");
          Require(IndustrialFluidTransfer.Transfer(foreign,combined.Inventory,FluidPhase.Liquid,10,200,1600)==0,"foreign liquid transferred");
          var delivered=new FluidInventory();double moved=IndustrialFluidTransfer.Transfer(combined.Inventory,delivered,FluidPhase.Liquid,1250,200,1600);
          combined.SynchronizeLocalShares();Require(Near(moved,1250)&&Near(combined.Inventory.GetLiquidLiters(),50)&&Near(a.GetLiquidLiters()+b.GetLiquidLiters()+delivered.GetLiquidLiters(),1300),"shared output lost material"); });
        Run("unchanged shared save reuses local states and strips transient reservations", () =>
        { var a=Gas(80);var b=Gas(0);var combined=new CombinedFluidTankInventory();combined.AddMember(1,100,100,.1,a.State);combined.AddMember(2,100,100,.1,b.State);
          combined.Inventory.TryReserve("engine:test",FluidPhase.Gas,FluidUnits.StandardLitersToMol(10),out _);combined.SynchronizeLocalShares();
          long first=a.State.StateRevision,second=b.State.StateRevision;combined.SynchronizeLocalShares();
          Require(a.State.StateRevision==first&&b.State.StateRevision==second&&a.State.Ports.Count==0&&b.State.Ports.Count==0,"steady save rewrote shares or copied runtime reservation");
          Require(combined.Inventory.GetAvailableMoles("test:a",FluidPhase.Gas)<combined.Inventory.GasMoles,"saving cancelled active reservation"); });
        Run("client partial member updates keep authoritative shares", () =>
        { var a=Gas(75);var b=Gas(50);var combined=new CombinedFluidTankInventory();
          combined.AddMember(1,100,100,.1,a.State);combined.AddMember(2,100,100,.1,b.State);
          combined.RemoveMember(2,false);Require(Near(Liters(a),75)&&Near(Liters(combined.Inventory),75),"client repartitioned a partial snapshot");
          var next=new CombinedFluidTankInventory();next.AddMember(1,100,100,.1,a.State);next.AddMember(2,100,100,.1,Gas(75).State);
          Require(Near(Liters(next.Inventory),150),"later authoritative member failed to restore exact group total"); });
        Console.WriteLine($"Result: {passed} passed, {failed} failed"); return failed==0?0:1;
    }
    #endregion
}
