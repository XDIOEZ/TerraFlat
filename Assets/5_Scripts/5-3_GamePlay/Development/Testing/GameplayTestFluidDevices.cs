using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using static MachineWorld;

/// <summary>真实设备生产操作的隔离采样，测试不另写运输算法。</summary>
internal static class GameplayTestFluidDevices
{
    #region 真实泵与三槽
    internal static readonly Dictionary<string, Action<Action<string, bool, string>>> Cases = new();
    private static void Power(MachineEntity node, bool enabled)
    { node.Rpm = enabled ? 2000 : 0; node.LoadSatisfied = enabled; node.ElectricalSuppliedWatts = enabled ? node.Definition.Electrical?.PowerWatts ?? 0 : 0; }
    internal static JObject CapturePump(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f = new GameplayFluidScope();var pump=f.Add("GasPump_Mechanical");var tank=f.Add("GasTankBlock_Iron",1);
        f.Atmosphere.Gases.Clear();f.Atmosphere.Gases.Add(new AtmosphereGasAmount { FluidId=FluidIds.Oxygen,Moles=21 });
        f.Atmosphere.Gases.Add(new AtmosphereGasAmount { FluidId=FluidIds.Nitrogen,Moles=79 });
        decimal initial=f.Atmosphere.CurrentMoles;f.Begin();Power(pump,false);FluidDeviceOperations.AdvanceGasPump(pump,f.Seconds);
        double unpowered=GameplayTestFluidNetworks.Gas(tank);decimal unpoweredSource=f.Atmosphere.CurrentMoles;
        for(int i=0;i<2000 && GameplayTestFluidNetworks.Gas(tank)<99.999999;i++)
        { f.Begin();Power(pump,true);FluidDeviceOperations.AdvanceGasPump(pump,f.Seconds); }
        double full=GameplayTestFluidNetworks.Gas(tank);decimal beforeFull=f.Atmosphere.CurrentMoles;
        f.Begin();Power(pump,true);FluidDeviceOperations.AdvanceGasPump(pump,f.Seconds);
        return new JObject { ["unpowered"]=unpowered,["unpoweredSourceStable"]=unpoweredSource==initial,
            ["full"]=full,["capacity"]=tank.Definition.Fluid.GasCapacityStandardLiters,["fullSourceStable"]=f.Atmosphere.CurrentMoles==beforeFull,
            ["sourceTaken"]=(double)FluidUnits.MolToStandardLiters(initial-f.Atmosphere.CurrentMoles),
            ["oxygen"]=(double)FluidUnits.MolToStandardLiters(GetFluidInventory(tank).GetGasMoles(FluidIds.Oxygen)),
            ["nitrogen"]=(double)FluidUnits.MolToStandardLiters(GetFluidInventory(tank).GetGasMoles(FluidIds.Nitrogen)) };
    }
    internal static JObject CaptureFiller(Player player,JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var f=new GameplayFluidScope();var source=f.Add("GasTankBlock_Iron");var filler=f.Add("GasFiller",1);
        GameplayTestFluidNetworks.AddGas(source,FluidIds.Oxygen,100);
        var logic=(FluidMachineLogic)filler.Logic;var inventory=logic.PortableTank;
        for(int i=0;i<3;i++)
        {
            if(!GameRes.ExistingInstance.TryGetItemDefinition("PortableGasTank_Iron",out var definition))throw new InvalidOperationException("缺铁罐。");
            inventory.Data.SetOne_ItemData(i,definition.CreateItemData());
        }
        FluidTankStorage.TryGet(inventory.Data.itemSlots[0].itemData,out var first,out var config);
        first.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.Oxygen),FluidUnits.StandardLitersToMol((decimal)(config.GasCapacityStandardLiters-.1)),0,293.15),1,.001);
        FluidTankStorage.Write(inventory.Data.itemSlots[0].itemData,first);
        f.Begin();FluidDeviceOperations.FillContainers(filler,f.Seconds,false);
        JArray Read()
        {
            var values=new JArray();foreach(var slot in inventory.Data.itemSlots)
            { FluidTankStorage.TryGet(slot.itemData,out var contents,out _);values.Add((double)FluidUnits.MolToStandardLiters(contents.GasMoles)); }return values;
        }
        var firstStep=Read();double sourceAfterFirst=GameplayTestFluidNetworks.Gas(source);
        for(int i=0;i<500;i++){f.Begin();FluidDeviceOperations.FillContainers(filler,f.Seconds,false);}
        var final=Read();double sourceAfter=GameplayTestFluidNetworks.Gas(source);
        ItemData snapshot=CaptureSnapshot(filler);
        return new JObject { ["slots"]=inventory.Data.itemSlots.Count,["firstStep"]=firstStep,["sourceAfterFirst"]=sourceAfterFirst,
            ["final"]=final,["sourceAfter"]=sourceAfter,["initialPortable"]=config.GasCapacityStandardLiters-.1,
            ["savedSlots"]=MachinePersistence.Read<FluidMachineState>(snapshot,"fluid").PortableTank.itemSlots.Count };
    }
    #endregion

    #region 引擎物料与空载
    internal static JObject CaptureEngine(Player player,JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        bool water=args.Value<bool?>("water")??false;
        using var f=new GameplayFluidScope();var engine=f.Add(water?"SteamEngine_Body":"GasEngine");var load=f.Add("GasPump_Mechanical",1);
        FluidMachineLogic steam = water ? (FluidMachineLogic)engine.Logic : null;
        RuntimeItemDefinition tinder = null;
        JObject fuel = null;
        if(water)
        {
            var fluid=FluidCatalog.Default.Find("core:dirty_water");
            GetFluidInventory(engine).TryAdd(FluidInventory.CreateBatch(fluid,0,FluidUnits.LiquidLitersToMol(fluid,1),293.15),2,.001);
            f.Begin();TryGetFluidMechanicalPower(engine,out double coldPower);FluidDeviceOperations.ReleaseReservations(engine);
            bool emptyIgnition=steam.Execute("work",null,null);
            bool acceptsNonFuel=steam.FuelInventory.Data.CanAcceptItem(engine.Snapshot,steam.FuelInventory.Data.itemSlots[0]);
            steam.FuelInventory.Data.SetOne_ItemData(0,engine.Snapshot);
            bool invalidDeposited=steam.FuelInventory.Data.itemSlots[0].itemData!=null;
            if(!GameRes.ExistingInstance.TryGetItemDefinition("FireSeed",out tinder))throw new InvalidOperationException("缺火种。");
            foreach(var slot in steam.FuelInventory.Data.itemSlots)
            {
                ItemData item=tinder.CreateItemData();item.Stack.Amount=2;
                steam.FuelInventory.Data.SetOne_ItemData(slot.Index,item);
            }
            bool acceptsTinder=steam.FuelInventory.Data.CanAcceptItem(steam.FuelInventory.Data.itemSlots[0].itemData,
                steam.FuelInventory.Data.itemSlots[0]);
            f.Begin();TryGetFluidMechanicalPower(engine,out double unlitPower);FluidDeviceOperations.ReleaseReservations(engine);
            bool cancelledIgnition=steam.Execute("work",null,null);
            steam.Tick(1f);
            ItemData removed=steam.FuelInventory.Data.itemSlots[0].itemData;
            steam.FuelInventory.Data.SetOne_ItemData(0,null);
            steam.Tick(engine.Definition.Fluid.SolidFuelAbsorptionSeconds);
            float removedFuel=steam.Fuel.Fuel.x;
            bool removedIgnited=steam.SolidFuelBurner.IsIgnited;
            float removedAmount=removed.Stack.Amount;
            steam.FuelInventory.Data.SetOne_ItemData(0,removed);
            bool ignition=steam.Execute("work",null,null);
            steam.Tick(engine.Definition.Fluid.SolidFuelAbsorptionSeconds-.1f);
            float pendingAmount=steam.FuelInventory.Data.itemSlots[0].itemData.Stack.Amount;
            float pendingProgress=steam.GetFuelAbsorptionProgress(0);
            f.Begin();TryGetFluidMechanicalPower(engine,out double pendingPower);FluidDeviceOperations.ReleaseReservations(engine);
            steam.Tick(.1f);
            fuel=new JObject { ["slots"]=steam.FuelInventory.Data.itemSlots.Count,["coldPower"]=coldPower,
                ["emptyIgnition"]=emptyIgnition,["unlitPower"]=unlitPower,["ignition"]=ignition,
                ["acceptsNonFuel"]=acceptsNonFuel,["invalidDeposited"]=invalidDeposited,["acceptsTinder"]=acceptsTinder,
                ["cancelledIgnition"]=cancelledIgnition,["removedFuel"]=removedFuel,["removedIgnited"]=removedIgnited,
                ["removedAmount"]=removedAmount,
                ["pendingPower"]=pendingPower,["pendingAmount"]=pendingAmount,["pendingProgress"]=pendingProgress,
                ["absorbedAmount"]=steam.FuelInventory.Data.itemSlots[0].itemData.Stack.Amount,
                ["burning"]=steam.IsBurning,["capacity"]=steam.Fuel.Fuel.y,["before"]=steam.Fuel.Fuel.x };
        }
        else GameplayTestFluidNetworks.AddGas(engine,FluidIds.Oxygen,10);
        f.Begin();TryGetFluidMechanicalPower(engine,out double airPower);FluidDeviceOperations.ReleaseReservations(engine);
        if(!water)
        {
            GameplayTestFluidNetworks.AddGas(engine,FluidIds.Hydrogen,10);
            GetFluidInventory(engine,"oxygen").TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.Oxygen),.5m,0,293.15),2,.001);
        }
        f.Begin();TryGetFluidMechanicalPower(engine,out double available);f.SetEngineWork(engine,available);
        decimal before=water?GetFluidInventory(engine).LiquidMoles:GetFluidInventory(engine).GetGasMoles(FluidIds.Hydrogen);
        engine.Rpm=0;FluidDeviceOperations.SettleFluidEngine(engine,f.Seconds);
        decimal idle=water?GetFluidInventory(engine).LiquidMoles:GetFluidInventory(engine).GetGasMoles(FluidIds.Hydrogen);
        if(water)fuel["idle"]=steam.Fuel.Fuel.x;
        f.Begin();TryGetFluidMechanicalPower(engine,out available);f.SetEngineWork(engine,available);
        engine.Network.TorqueDemand=load.LoadTorque;engine.Rpm=engine.Definition.Rpm;engine.SourceFactor=1;engine.TorqueRatio=1;
        engine.RemainingSourceTorque=engine.SourceTorque-load.LoadTorque;
        decimal oxygenBefore=GetFluidInventory(engine,"oxygen").GetGasMoles(FluidIds.Oxygen);
        FluidDeviceOperations.SettleFluidEngine(engine,f.Seconds);
        decimal after=water?GetFluidInventory(engine).LiquidMoles:GetFluidInventory(engine).GetGasMoles(FluidIds.Hydrogen);
        int remainingReservations=GetFluidInventory(engine).State.Ports.Count;
        if(water)
        {
            fuel["after"]=steam.Fuel.Fuel.x;
            fuel["extinguished"]=steam.Execute("work",null,null);
            fuel["burningAfterStop"]=steam.IsBurning;
            float stoppedFuel=steam.Fuel.Fuel.x;
            f.Begin();TryGetFluidMechanicalPower(engine,out double stoppedPower);FluidDeviceOperations.ReleaseReservations(engine);
            fuel["stoppedPower"]=stoppedPower;fuel["stoppedFuel"]=steam.Fuel.Fuel.x;
            fuel["beforeStop"]=stoppedFuel;
            fuel["relit"]=steam.Execute("work",null,null);
            steam.Tick(engine.Definition.Fluid.SolidFuelAbsorptionSeconds);
            GetFluidInventory(engine).Restore(new FluidInventoryState());
            float dryFuel=steam.Fuel.Fuel.x;
            f.Begin();TryGetFluidMechanicalPower(engine,out double dryPower);FluidDeviceOperations.ReleaseReservations(engine);
            fuel["dryPower"]=dryPower;fuel["dryBefore"]=dryFuel;fuel["dryAfter"]=steam.Fuel.Fuel.x;
            f.Atmosphere.Gases.Clear();steam.Tick(.1f);
            fuel["oxygenStarvedIgnited"]=steam.SolidFuelBurner.IsIgnited;
            fuel["oxygenStarvedFuel"]=steam.Fuel.Fuel.x;
            FluidMachineState saved=MachinePersistence.Read<FluidMachineState>(CaptureSnapshot(engine),"fluid");
            fuel["savedSlots"]=saved.FuelInventory.itemSlots.Count;
            fuel["savedFuel"]=saved.SolidFuel.Fuel.Fuel.x;
            f.Atmosphere.Gases.Add(new AtmosphereGasAmount { FluidId=FluidIds.Oxygen,Moles=50m });
            fuel["finalIgnition"]=steam.Execute("work",null,null);
            steam.Tick(engine.Definition.Fluid.SolidFuelAbsorptionSeconds);
            fuel["burningBeforeExhaustion"]=steam.IsBurning;
            foreach(var slot in steam.FuelInventory.Data.itemSlots)steam.FuelInventory.Data.SetOne_ItemData(slot.Index,null);
            float consumptionRate=steam.SolidFuelBurner.ConsumptionRate;
            fuel["consumptionRate"]=consumptionRate;fuel["beforeExhaustion"]=steam.Fuel.Fuel.x;
            steam.SolidFuelBurner.ConsumeWork(consumptionRate>0?steam.Fuel.Fuel.x/consumptionRate+1:0);
            fuel["afterExhaustion"]=steam.Fuel.Fuel.x;fuel["ignitedAfterExhaustion"]=steam.SolidFuelBurner.IsIgnited;
            steam.SolidFuelBurner.Extinguish();
            steam.FuelInventory.Data.SetOne_ItemData(0,tinder.CreateItemData());
            steam.State.Ruptured=true;
            fuel["rupturedIgnition"]=steam.Execute("work",null,null);
            fuel["rupturedIgnited"]=steam.SolidFuelBurner.IsIgnited;
        }
        return new JObject { ["water"]=water,["airPower"]=airPower,["available"]=available,["before"]=(double)before,
            ["idle"]=(double)idle,["after"]=(double)after,["oxygenUsed"]=(double)(oxygenBefore-GetFluidInventory(engine,"oxygen").GetGasMoles(FluidIds.Oxygen)),
            ["waterProduct"]=(double)GetFluidInventory(engine,"water").LiquidMoles,["remainingReservations"]=remainingReservations,["fuel"]=fuel };
    }
    #endregion
}
