#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>规则夹具直接读取独立星球的正式测量快照，Lua 判断比例、单位和来源边界。</summary>
public static class GameplayTestAtmosphereMeasurementRules
{
    #region 测量规则登记与原始采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register() => GameplayTestSystemProbe.Register(
        "environment", "atmosphere_measurement", CaptureMeasurementRules);

    private static JObject CaptureMeasurementRules(JObject args)
    {
        var atmosphere = new AtmosphereState
        {
            CapacityMoles = 200m, ReferenceMoles = 100m, ReferencePressureKPa = 100d,
            TemperatureKelvin = 293.15d,
            Gases = new List<AtmosphereGasAmount>
            {
                new() { FluidId = FluidIds.Oxygen, Moles = 21m },
                new() { FluidId = FluidIds.Nitrogen, Moles = 79m },
                new() { FluidId = FluidIds.CarbonDioxide, Moles = 0m }
            }
        };
        var first = new PlanetData { Name = "第一星球", Atmosphere = atmosphere };
        var second = new PlanetData
        {
            Name = "第二星球", Atmosphere = new AtmosphereState
            {
                CapacityMoles = 100m, ReferenceMoles = 100m, ReferencePressureKPa = 80d,
                TemperatureKelvin = 293.15d,
                Gases = new List<AtmosphereGasAmount> { new() { FluidId = FluidIds.CarbonDioxide, Moles = 100m } }
            }
        };
        var uninitialized = new PlanetData { Name = "尚未就绪" };
        var planets = new Dictionary<string, PlanetData>
        { ["probe_first"] = first, ["probe_second"] = second, ["probe_uninitialized"] = uninitialized };
        AtmosphereMeasurementSnapshot before = AtmosphereMeasurementService.CaptureForWorld("probe_first", planets);
        AtmosphereMeasurementSnapshot cave = AtmosphereMeasurementService.CaptureForWorld(
            new WorldAddress("probe_first", WorldAddress.CaveDimensionId).WorldKey, planets);
        AtmosphereMeasurementSnapshot other = AtmosphereMeasurementService.CaptureForWorld("probe_second", planets);
        long originalRevision = atmosphere.Revision;
        var result = new JObject
        {
            ["before"] = Snapshot(before), ["cave"] = Snapshot(cave), ["other"] = Snapshot(other),
            ["readDidNotChangeInventory"] = atmosphere.CurrentMoles == 100m && atmosphere.Revision == originalRevision,
            ["missingWorld"] = Snapshot(AtmosphereMeasurementService.CaptureForWorld("not_a_planet", planets)),
            ["emptyWorld"] = Snapshot(AtmosphereMeasurementService.CaptureForWorld(null, planets)),
            ["missingPlanets"] = Snapshot(AtmosphereMeasurementService.CaptureForWorld("probe_first", null)),
            ["uninitialized"] = Snapshot(AtmosphereMeasurementService.CaptureForWorld("probe_uninitialized", planets)),
            ["uninitializedPreserved"] = uninitialized.Atmosphere == null
        };
        // 先保存不可变读数，再改变来源以暴露快照借用可变列表的问题。
        atmosphere.Gases[0].Moles = 10m;
        atmosphere.Gases[1].Moles = 40m;
        atmosphere.Revision++;
        result["afterChange"] = Snapshot(AtmosphereMeasurementService.CaptureForWorld("probe_first", planets));
        result["oldSnapshot"] = Snapshot(before);
        atmosphere.Gases[0].Moles = 0m;
        atmosphere.Gases[1].Moles = 0m;
        atmosphere.Revision++;
        result["vacuum"] = Snapshot(AtmosphereMeasurementService.CaptureForWorld("probe_first", planets));
        var trace = new AtmosphereState
        {
            CapacityMoles = 1000m, ReferenceMoles = 100m, ReferencePressureKPa = 100d,
            TemperatureKelvin = 293.15d,
            Gases = new List<AtmosphereGasAmount>
            {
                new() { FluidId = FluidIds.Nitrogen, Moles = 99.999999999m },
                new() { FluidId = FluidIds.Oxygen, Moles = .000000001m }
            }
        };
        result["trace"] = Snapshot(AtmosphereMeasurementService.Capture(trace));
        return result;
    }

    private static JObject Snapshot(AtmosphereMeasurementSnapshot value)
    {
        var components = new JArray();
        foreach (AtmosphereMeasurementComponent component in value.Components)
            components.Add(new JObject
            { ["id"] = component.FluidId, ["moles"] = component.Moles, ["fraction"] = component.Fraction });
        return new JObject
        {
            ["status"] = value.Status.ToString(), ["worldKey"] = value.WorldKey,
            ["planetName"] = value.PlanetName, ["revision"] = value.Revision,
            ["totalMoles"] = value.TotalMoles, ["standardCubicMeters"] = value.TotalStandardCubicMeters,
            ["pressureKPa"] = value.PressureKPa, ["components"] = components
        };
    }
    #endregion
}
#endif
