using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestRuntimeProbe
{
    #region 液网真实拓扑和环境交换采样入口
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterLiquidNetworkTemperatureRuntimeProbes()
    {
        Register("industrial_fluids", "liquid_network_temperature", (_, _) => MachineWorld.CaptureGameplayLiquidNetworkTemperature());
        Register("industrial_fluids", "liquid_grid_exchange", (_, _) => MachineWorld.CaptureGameplayLiquidGridExchange());
    }
    #endregion
}

public static partial class MachineWorld
{
    #region 正式液网开关阀与快照采样
    private static void AddGameplayThermalWater(MachineEntity node, decimal liters, double celsius, string chamber = "main")
    {
        FluidDefinition water = FluidCatalog.Default.Find(FluidIds.PureWater);
        if (!GetFluidInventory(node, chamber).TryAdd(FluidInventory.CreateBatch(water, 0m,
            FluidUnits.LiquidLitersToMol(water, liters), celsius + 273.15d), 1000.001d, .001d, false, true))
            throw new InvalidOperationException("液网温度夹具加水失败。");
    }

    private static float GameplayLiquidNetworkCelsius(MachineEntity node, string chamber = "main")
        => TryGetLiquidNetworkTemperatureCelsius(node, out float temperature, chamber) ? temperature : float.NaN;

    internal static JObject CaptureGameplayLiquidNetworkTemperature()
    {
        using var fixture = new GameplayFluidScope();
        MachineEntity first = fixture.Add("LiquidTankBlock_Iron");
        MachineEntity valve = fixture.Add("LiquidValve", 1);
        MachineEntity second = fixture.Add("LiquidTankBlock_Steel", 2);
        fixture.Rebuild();
        AddGameplayThermalWater(first, 100m, 100d); AddGameplayThermalWater(second, 50m, 50d);
        fixture.Step();
        var result = new JObject { ["mixed"] = GameplayLiquidNetworkCelsius(first), ["pipe"] = GameplayLiquidNetworkCelsius(valve) };
        var output = new IndustrialLiquidTransferPort((FluidMachineLogic)first.Logic, first.Definition.Fluid.Ports[0]);
        if (!output.PeekLiquid(out LiquidTransferBatch batch)) throw new InvalidOperationException("正式液罐输出口没有真实批次。");
        result["output"] = batch.TemperatureCelsius;
        GetFluidState(valve).ValveOpen = false; TopologyChanged(valve); fixture.Step();
        result["closedFirst"] = GameplayLiquidNetworkCelsius(first); result["closedSecond"] = GameplayLiquidNetworkCelsius(second);
        AddGameplayThermalWater(first, 10m, 10d); StateChanged(first);
        result["separateFirst"] = GameplayLiquidNetworkCelsius(first); result["separateSecond"] = GameplayLiquidNetworkCelsius(second);
        GetFluidState(valve).ValveOpen = true; TopologyChanged(valve);
        result["immediatelyRejoined"] = GameplayLiquidNetworkCelsius(first);
        fixture.Step();
        result["rejoined"] = GameplayLiquidNetworkCelsius(first); result["total"] = GetLiquidNetworkContainedLiters(first);
        ItemData saved = CaptureSnapshot(first);
        var restored = new FluidInventory(MachinePersistence.Read<FluidMachineState>(saved, "fluid").Chambers["main"]);
        result["saved"] = restored.GetTemperatureKelvin() - 273.15d;
        MachineEntity empty = fixture.Add("LiquidTankBlock_Iron", 10);
        result["empty"] = !TryGetLiquidNetworkTemperatureCelsius(empty, out _);
        MachineEntity combinedFirst = fixture.Add("LiquidTankBlock_Iron", 20);
        MachineEntity combinedSecond = fixture.Add("LiquidTankBlock_Iron", 21);
        fixture.Rebuild(); AddGameplayThermalWater(combinedFirst, 100m, 80d); fixture.Step();
        result["combinedShared"] = ReferenceEquals(GetFluidInventory(combinedFirst), GetFluidInventory(combinedSecond));
        result["combinedLiters"] = GetLiquidNetworkContainedLiters(combinedFirst);
        result["combinedCells"] = fluidGraph.GetLiquidTemperatureComponent(combinedFirst, "main", FluidIds.PureWater).Cells.Count;
        foreach (FluidMachinePortDefinition port in valve.Definition.Fluid.Ports) port.FluidId = "core:fuel_oil";
        TopologyChanged(valve); fixture.Rebuild();
        var left = fluidGraph.GetLiquidTemperatureComponent(first, "main", FluidIds.PureWater);
        var right = fluidGraph.GetLiquidTemperatureComponent(second, "main", FluidIds.PureWater);
        result["filtered"] = !ReferenceEquals(left, right);
        AddGameplayThermalWater(first, 1m, 25d, "thermal_byproduct");
        var byproduct = fluidGraph.GetLiquidTemperatureComponent(first, "thermal_byproduct", FluidIds.PureWater);
        result["independentChambers"] = !ReferenceEquals(left, byproduct) && byproduct.Members.Count == 1;
        MachineEntity gasTank = fixture.Add("GasTankBlock_Iron", 30);
        GameplayTestFluidNetworks.AddGas(gasTank, FluidIds.Oxygen, 20d);
        AddGameplayThermalWater(gasTank, 1m, 100d);
        double gasEnergy = GetFluidInventory(gasTank).State.TotalInternalEnergyJoules;
        fixture.Step();
        result["gasUnaffected"] = !TryGetLiquidNetworkTemperatureCelsius(gasTank, out _) &&
            gasEnergy == GetFluidInventory(gasTank).State.TotalInternalEnergyJoules;
        return result;
    }
    #endregion

    #region 已加载动态温度网格的真实交换采样
    internal static JObject CaptureGameplayLiquidGridExchange()
    {
        using var fixture = new GameplayFluidScope(surface: true, surfaceWidth: 2);
        MachineEntity first = fixture.Add("LiquidTankBlock_Iron");
        MachineEntity second = fixture.Add("LiquidTankBlock_Iron", 1);
        fixture.Rebuild(); AddGameplayThermalWater(first, 100m, 10d);
        TemperatureMgr environment = TemperatureMgr.Instance;
        object heater = new();
        try
        {
            environment.TryGetAmbientTemperature(first.Position, out float beforeFirst);
            environment.TryGetAmbientTemperature(second.Position, out float beforeSecond);
            environment.TryGetGeographicTemperature(first.Position, out float geographic);
            fixture.Step();
            float after = GameplayLiquidNetworkCelsius(first);
            environment.TryGetAmbientTemperature(first.Position, out float firstAmbient);
            environment.TryGetAmbientTemperature(second.Position, out float secondAmbient);
            if (!ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(first.Position, out var tile))
                throw new InvalidOperationException("液网网格夹具缺少真实已加载区块。");
            FlatWorld.WorldModel.ChunkRuntime chunk = null;
            foreach (var candidate in ChunkMgr.ExistingInstance.WorldRuntime.Chunks.Values)
                if (ReferenceEquals(candidate.Terrain, tile.Terrain)) { chunk = candidate; break; }
            int index = tile.LocalCell.y * tile.Terrain.Width + tile.LocalCell.x;
            var temperatures = new float[tile.Terrain.CellCount];
            if (!environment.TrySampleAmbientChunk(chunk, new[] { index }, temperatures) ||
                !environment.TryCaptureAmbientChunk(chunk, out var frozen))
                throw new InvalidOperationException("液网批量或冻结温度采样失败。");
            float batch = temperatures[index];
            fixture.Step(); float repeated = GameplayLiquidNetworkCelsius(first);
            environment.SetLocalTemperatureSource(heater, first.Position, .75f, beforeFirst + 60f);
            environment.TryGetAmbientTemperature(first.Position, out float heated);
            fixture.Step(); float afterHeater = GameplayLiquidNetworkCelsius(first);
            frozen.TrySample(chunk, new[] { index }, temperatures);
            float frozenTemperature = temperatures[index];
            environment.RemoveLocalTemperatureSource(heater);
            environment.TryGetGeographicTemperature(first.Position, out float geographicAfter);
            bool volumeStable = Math.Abs(GetLiquidNetworkContainedLiters(first) - 100d) < .000001d;
            foreach (FluidBatch _ in GetFluidInventory(first).Drain()) { }
            fixture.Step();
            environment.TryGetAmbientTemperature(first.Position, out float clearedFirst);
            environment.TryGetAmbientTemperature(second.Position, out float clearedSecond);
            return new JObject
            {
                ["beforeFirst"] = beforeFirst, ["beforeSecond"] = beforeSecond, ["after"] = after,
                ["first"] = firstAmbient, ["second"] = secondAmbient, ["batch"] = batch,
                ["repeated"] = repeated, ["frozen"] = frozenTemperature, ["heated"] = heated, ["afterHeater"] = afterHeater,
                ["clearedFirst"] = clearedFirst, ["clearedSecond"] = clearedSecond,
                ["volumeStable"] = volumeStable, ["geographicStable"] = geographic == geographicAfter
            };
        }
        finally
        {
            environment.RemoveLocalTemperatureSource(heater);
            environment.RemoveDynamicGridTemperatureExchangeTargets(scope);
        }
    }
    #endregion
}
