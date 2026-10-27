#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.IO;
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
        result["planetSizing"] = CaptureAtmosphereSizingFromSelectedPlanetRadius();
        return result;
    }

    private static JObject CaptureAtmosphereSizingFromSelectedPlanetRadius()
    {
        var fluids = new FluidCatalog();
        foreach (var gas in new[] { (FluidIds.Nitrogen, .0280134d), (FluidIds.Oxygen, .0319988d),
                     (FluidIds.CarbonDioxide, .0440095d) })
            fluids.Register(new[] { new FluidDefinition(new FluidDefinitionDto
            {
                Id = gas.Item1, MolarMassKgPerMol = gas.Item2, LiquidDensityKgPerLiter = 1d,
                GasHeatCapacityJPerMolKelvin = 20d, LiquidHeatCapacityJPerMolKelvin = 40d
            }) });
        var configuration = new AtmosphereDefinitionDto
        {
            Id = "probe:planet_sized_atmosphere", InitialStandardCubicMeters = 1m,
            CapacityStandardCubicMeters = 1m, ReferenceStandardCubicMeters = 1m,
            ReferencePressureKPa = 100d, TemperatureKelvin = 293.15d,
            Composition = new List<AtmosphereCompositionDto>
            {
                new() { FluidId = FluidIds.Nitrogen, Fraction = .8m },
                new() { FluidId = FluidIds.Oxygen, Fraction = .2m }
            }
        };
        var definition = new AtmosphereDefinition(configuration, fluids);
        var planet = new PlanetData { Radius = 1000, PhysicalRadiusMeters = 99999d };
        JObject Sample(AtmosphereDefinition source)
        {
            AtmosphereState state = source.CreateState(planet);
            AtmosphereService.Validate(state, fluids);
            JObject sample = Snapshot(AtmosphereMeasurementService.Capture(state));
            sample["capacityMoles"] = state.CapacityMoles;
            sample["referenceMoles"] = state.ReferenceMoles;
            sample["initialCarbonDioxideKPa"] = state.InitialCarbonDioxidePartialPressureKPa;
            return sample;
        }
        var result = new JObject { ["earth"] = Sample(definition) };
        planet.Radius = 2000;
        result["larger"] = Sample(definition);
        planet.Radius = 1000; planet.SurfaceGravityMetersPerSecondSquared = 19.62d;
        result["strongerGravity"] = Sample(definition);
        planet.SurfaceGravityMetersPerSecondSquared = 9.81d;
        configuration.ReferencePressureKPa = 200d;
        result["higherPressure"] = Sample(new AtmosphereDefinition(configuration, fluids));
        configuration.ReferencePressureKPa = 100d; configuration.TemperatureKelvin = 400d;
        result["warmer"] = Sample(new AtmosphereDefinition(configuration, fluids));
        configuration.TemperatureKelvin = 293.15d;
        configuration.InitialStandardCubicMeters = .5m; configuration.CapacityStandardCubicMeters = 2m;
        result["partial"] = Sample(new AtmosphereDefinition(configuration, fluids));
        configuration.InitialStandardCubicMeters = configuration.CapacityStandardCubicMeters = 1m;
        configuration.Composition[0].Fraction = .79m;
        configuration.Composition.Add(new AtmosphereCompositionDto { FluidId = FluidIds.CarbonDioxide, Fraction = .01m });
        result["withCarbon"] = Sample(new AtmosphereDefinition(configuration, fluids));
        configuration.Composition = new List<AtmosphereCompositionDto> { new() { FluidId = FluidIds.Oxygen, Fraction = 1m } };
        result["pureOxygen"] = Sample(new AtmosphereDefinition(configuration, fluids));
        configuration.InitialStandardCubicMeters = configuration.CapacityStandardCubicMeters = configuration.ReferenceStandardCubicMeters = 0m;
        configuration.ReferencePressureKPa = 0d; configuration.Composition.Clear();
        planet.Radius = 0; planet.SurfaceGravityMetersPerSecondSquared = 0d;
        result["vacuum"] = Sample(new AtmosphereDefinition(configuration, fluids));
        string Rejected(PlanetData invalid)
        {
            try { definition.CreateState(invalid); return null; }
            catch (InvalidDataException exception) { return exception.GetType().Name; }
        }
        result["invalidRadius"] = Rejected(new PlanetData { Radius = 0 });
        result["invalidGravity"] = Rejected(new PlanetData { SurfaceGravityMetersPerSecondSquared = 0d });
        result["nonfiniteGravity"] = Rejected(new PlanetData { SurfaceGravityMetersPerSecondSquared = double.NaN });
        result["outsideRange"] = Rejected(new PlanetData { Radius = int.MaxValue });
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
