using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using static MachineWorld;

/// <summary>燃油发动机直接走正式管罐、策略预约和负载结算，Lua 验证原始采样。</summary>
internal static class GameplayTestFuelOilEngine
{
    #region 燃油发动机隔离流程登记
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterFuelOilEngineOperation()
        => GameplayTestRuntimeProbe.Register("industrial_fluids", "fuel_oil_engine", CaptureFuelOilEngine);

    private static JObject CaptureFuelOilEngine(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        using var fixture = new GameplayFluidScope();
        MachineEntity engine = fixture.Add("FuelEngine");
        MachineEntity load = fixture.Add("GasPump_Mechanical", 1);
        MachineEntity pipe = fixture.Add("LiquidPipe", -1);
        MachineEntity valve = fixture.Add("LiquidValve", -2);
        MachineEntity tank = fixture.Add("LiquidTankBlock_Iron", -3);
        FluidDeviceBehavior behavior = GetFluidDeviceBehavior(engine);
        FluidInventory source = GetFluidInventory(tank);
        FluidDefinition fuel = FluidCatalog.Default.Find("core:fuel_oil");
        void Fill(string id)
        {
            FluidDefinition definition = FluidCatalog.Default.Find(id);
            source.Restore(new FluidInventoryState());
            if (!source.TryAdd(FluidInventory.CreateBatch(definition, 0, FluidUnits.LiquidLitersToMol(definition, 1), 293.15),
                GetFluidVolumeLiters(tank), GetFluidMinimumGasSpaceLiters(tank)))
                throw new InvalidOperationException("燃油引擎采样无法注入真实来源液体：" + id);
            StateChanged(tank);
        }
        double BeginPower()
        {
            fixture.Begin();
            TryGetFluidMechanicalPower(engine, out double power);
            return power;
        }
        void SetLoaded()
        {
            engine.Network.TorqueDemand = load.LoadTorque;
            engine.Rpm = engine.Definition.Rpm;
            engine.SourceFactor = 1;
            engine.TorqueRatio = 1;
            engine.RemainingSourceTorque = engine.SourceTorque - load.LoadTorque;
        }
        void RestoreOxygen()
        {
            fixture.Atmosphere.Gases.Clear();
            fixture.Atmosphere.Gases.Add(new AtmosphereGasAmount { FluidId = FluidIds.Oxygen, Moles = 21 });
            fixture.Atmosphere.Gases.Add(new AtmosphereGasAmount { FluidId = FluidIds.Nitrogen, Moles = 79 });
        }
        double emptyPower = BeginPower();
        Fill(FluidIds.PureWater);
        double waterPower = BeginPower();
        double waterLeft = source.GetLiquidLiters();
        Fill(fuel.Id);
        GetFluidState(valve).ValveOpen = false;
        TopologyChanged(valve);
        double blockedPower = BeginPower();
        double blockedFuel = source.GetLiquidLiters();
        GetFluidState(valve).ValveOpen = true;
        TopologyChanged(valve);
        double available = BeginPower();
        decimal before = source.GetLiquidMoles(fuel.Id);
        engine.Rpm = 0;
        behavior.Advance(engine, fixture.Seconds);
        decimal idle = source.GetLiquidMoles(fuel.Id);
        int idleReservations = source.State.Ports.Count;

        fixture.Atmosphere.Gases.Clear();
        double oxygenStarvedPower = BeginPower();
        decimal oxygenStarved = source.GetLiquidMoles(fuel.Id);
        int oxygenStarvedReservations = source.State.Ports.Count;
        RestoreOxygen();
        double beforeOxygenLossPower = BeginPower();
        SetLoaded();
        fixture.Atmosphere.Gases.Clear();
        behavior.Advance(engine, fixture.Seconds);
        decimal afterOxygenLoss = source.GetLiquidMoles(fuel.Id);
        int oxygenLossReservations = source.State.Ports.Count;

        RestoreOxygen();
        double loadedPower = BeginPower();
        SetLoaded();
        behavior.Advance(engine, fixture.Seconds);
        decimal afterWork = source.GetLiquidMoles(fuel.Id);
        int afterWorkReservations = source.State.Ports.Count;

        BeginPower();
        FluidMachineState saved = MachinePersistence.Read<FluidMachineState>(CaptureSnapshot(tank), "fluid");
        int savedReservations = saved.Chambers.Values.Sum(contents => contents.Ports.Count);
        decimal savedFuel = new FluidInventory(saved.Chambers["main"]).GetLiquidMoles(fuel.Id);
        Remove(engine.Id);
        fixture.Rebuild();
        int removedReservations = source.State.Ports.Count;
        return new JObject { ["emptyPower"] = emptyPower, ["waterPower"] = waterPower, ["waterLeft"] = waterLeft,
            ["blockedPower"] = blockedPower, ["blockedFuel"] = blockedFuel,
            ["available"] = available, ["before"] = (double)before, ["idle"] = (double)idle,
            ["idleReservations"] = idleReservations, ["oxygenStarvedPower"] = oxygenStarvedPower,
            ["oxygenStarved"] = (double)oxygenStarved, ["oxygenStarvedReservations"] = oxygenStarvedReservations,
            ["beforeOxygenLossPower"] = beforeOxygenLossPower, ["afterOxygenLoss"] = (double)afterOxygenLoss,
            ["oxygenLossReservations"] = oxygenLossReservations, ["loadedPower"] = loadedPower,
            ["afterWork"] = (double)afterWork, ["afterWorkReservations"] = afterWorkReservations,
            ["savedReservations"] = savedReservations, ["savedFuel"] = (double)savedFuel,
            ["removedReservations"] = removedReservations, ["afterRemoval"] = (double)source.GetLiquidMoles(fuel.Id),
            ["pipeHasNoLiquid"] = GetFluidInventory(pipe).LiquidMoles == 0m };
    }
    #endregion
}
