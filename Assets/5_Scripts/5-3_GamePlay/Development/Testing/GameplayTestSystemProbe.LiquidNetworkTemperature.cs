using System;
using System.Collections.Generic;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 液网统一温度与动态网格规则采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterLiquidNetworkTemperatureRules()
    {
        Register("industrial_fluids", "liquid_network_temperature", CaptureLiquidNetworkTemperatureRules);
        Register("environment", "dynamic_grid_temperature_exchange", CaptureDynamicGridTemperatureExchangeRules);
    }

    private static JObject CaptureLiquidNetworkTemperatureRules(JObject args)
    {
        FluidCatalog catalog = ReadBuiltInFluidCatalogForTankRules();
        FluidDefinition water = catalog.Find(FluidIds.PureWater);
        FluidInventory Water(decimal liters, double celsius)
        {
            var inventory = new FluidInventory(catalog: catalog);
            inventory.TryAdd(FluidInventory.CreateBatch(water, 0m, FluidUnits.LiquidLitersToMol(water, liters),
                celsius + 273.15d), 1000.001d, .001d, false, true);
            return inventory;
        }
        var first = Water(100m, 100d); var second = Water(50m, 50d);
        var network = new IndustrialLiquidNetworkTemperature();
        network.AddInventory(first); network.AddInventory(second); network.AddInventory(first);
        network.RefreshContents();
        var result = new JObject
        {
            ["mixed"] = network.TemperatureKelvin - 273.15d,
            ["first"] = first.GetTemperatureKelvin() - 273.15d,
            ["second"] = second.GetTemperatureKelvin() - 273.15d,
            ["total"] = network.TotalLiquidLiters
        };
        first.TryTakeExact(water.Id, FluidPhase.Liquid, FluidUnits.LiquidLitersToMol(water, 10m), out _);
        network.RefreshContents();
        result["drained"] = network.TemperatureKelvin - 273.15d;
        result["remaining"] = network.TotalLiquidLiters;
        first.TryAdd(FluidInventory.CreateBatch(water, 0m, FluidUnits.LiquidLitersToMol(water, 10m), 283.15d),
            1000.001d, .001d, false, true);
        network.RefreshContents();
        result["inserted"] = network.TemperatureKelvin - 273.15d;
        var splitFirst = new IndustrialLiquidNetworkTemperature(); splitFirst.AddInventory(first); splitFirst.RefreshContents();
        var splitSecond = new IndustrialLiquidNetworkTemperature(); splitSecond.AddInventory(second); splitSecond.RefreshContents();
        result["splitFirst"] = splitFirst.TemperatureKelvin - 273.15d;
        result["splitSecond"] = splitSecond.TemperatureKelvin - 273.15d;
        var saved = new FluidInventory(MemoryPackSerializer.Deserialize<FluidInventoryState>(MemoryPackSerializer.Serialize(first.State)), catalog);
        result["saved"] = saved.GetTemperatureKelvin() - 273.15d;
        result["ambient"] = IndustrialLiquidNetworkTemperature.AverageWithAmbientCells(283.15d, 50d, 2) - 273.15d;
        double before = first.State.TotalInternalEnergyJoules;
        network.MirrorAuthoritativeTemperature(338.15d);
        result["mirrored"] = network.TemperatureKelvin - 273.15d;
        result["remoteInventoryUnchanged"] = before == first.State.TotalInternalEnergyJoules;
        var empty = new IndustrialLiquidNetworkTemperature(); empty.AddInventory(Water(0m, 20d)); empty.RefreshContents();
        result["empty"] = !empty.HasLiquid && empty.TotalLiquidLiters == 0d;
        return result;
    }

    private static JObject CaptureDynamicGridTemperatureExchangeRules(JObject args)
    {
        var field = new DynamicGridTemperatureExchangeField();
        object terrain = new(), reloaded = new(), first = new(), second = new();
        Vector2Int cell = Vector2Int.zero;
        var source = new Dictionary<Vector2Int, DynamicGridTemperatureExchangeField.CellOffset>
            { [cell] = new(terrain, -10f) };
        float Sample(float ambient = 20f) => ambient + field.GetOffset(cell, terrain);
        var result = new JObject { ["initial"] = Sample() };
        field.ReplaceSourceOffsets(first, source);
        result["first"] = Sample();
        long revision = field.Revision;
        field.ReplaceSourceOffsets(first, source);
        result["repeated"] = Sample(); result["unchangedRevision"] = revision == field.Revision;
        result["heaterChanged"] = Sample(40f);
        source[cell] = new(terrain, 0f); field.ReplaceSourceOffsets(second, source);
        result["overlap"] = Sample(); result["changedRevision"] = field.Revision > revision;
        float[] frozen = field.CaptureChunkOffsets(cell, 1, 1, terrain);
        field.RemoveSource(first); field.RemoveSource(second);
        field.ReplaceSourceOffsets(second, source);
        source[cell] = new(terrain, -10f); field.ReplaceSourceOffsets(first, source);
        result["reverseOverlap"] = Sample();
        result["reloaded"] = 20f + field.GetOffset(cell, reloaded);
        field.RemoveSource(first); result["remaining"] = Sample();
        field.RemoveSource(second); result["cleared"] = Sample();
        result["frozen"] = 20f + frozen[0];
        source[cell] = new(terrain, -40f); field.ReplaceSourceOffsets(first, source);
        result["mayFreeze"] = 20f + field.GetMinimumOffset(terrain) <= 0f;
        return result;
    }
    #endregion
}
