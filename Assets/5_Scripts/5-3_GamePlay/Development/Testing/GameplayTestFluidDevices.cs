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
        if(water)
        {
            var fluid=FluidCatalog.Default.Find("core:dirty_water");
            GetFluidInventory(engine).TryAdd(FluidInventory.CreateBatch(fluid,0,FluidUnits.LiquidLitersToMol(fluid,1),293.15),2,.001);
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
        f.Begin();TryGetFluidMechanicalPower(engine,out available);f.SetEngineWork(engine,available);
        engine.Network.TorqueDemand=load.LoadTorque;engine.Rpm=engine.Definition.Rpm;engine.SourceFactor=1;engine.TorqueRatio=1;
        engine.RemainingSourceTorque=engine.SourceTorque-load.LoadTorque;
        decimal oxygenBefore=GetFluidInventory(engine,"oxygen").GetGasMoles(FluidIds.Oxygen);
        FluidDeviceOperations.SettleFluidEngine(engine,f.Seconds);
        decimal after=water?GetFluidInventory(engine).LiquidMoles:GetFluidInventory(engine).GetGasMoles(FluidIds.Hydrogen);
        return new JObject { ["water"]=water,["airPower"]=airPower,["available"]=available,["before"]=(double)before,
            ["idle"]=(double)idle,["after"]=(double)after,["oxygenUsed"]=(double)(oxygenBefore-GetFluidInventory(engine,"oxygen").GetGasMoles(FluidIds.Oxygen)),
            ["waterProduct"]=(double)GetFluidInventory(engine,"water").LiquidMoles,["remainingReservations"]=GetFluidInventory(engine).State.Ports.Count };
    }
    #endregion
}
