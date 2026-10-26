using System;
using System.Collections.Generic;
using FlatWorld.Spaceflight;
using MemoryPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static partial class GameplayTestSystemProbe
{
    #region 大气温室规则与真实库存采样
    private static void RegisterGreenhouseOperations(Dictionary<string, Func<JObject, JObject>> operations)
        => operations.Add("environment/greenhouse_temperature", GreenhouseTemperatureValues);

    // 使用独立真实大气库存与正式抽排事务，Lua 持有增温、守恒和回滚断言。
    private static JObject GreenhouseTemperatureValues(JObject args)
    {
        FluidCatalog previousFluids = FluidCatalog.Default;
        AtmosphericGreenhouseConfiguration previousConfiguration = AtmosphericGreenhouseConfigurationService.Current;
        var fluids = new FluidCatalog();
        foreach (string id in new[] { FluidIds.Nitrogen, FluidIds.CarbonDioxide })
            fluids.Register(new[] { new FluidDefinition(new FluidDefinitionDto
            {
                Id = id, DisplayName = id, MolarMassKgPerMol = .032d, LiquidDensityKgPerLiter = 1d,
                GasHeatCapacityJPerMolKelvin = 20d, LiquidHeatCapacityJPerMolKelvin = 40d
            }) });
        FluidCatalog.Replace(fluids);
        AtmosphericGreenhouseConfigurationService.ReplaceConfiguration(AtmosphericGreenhouseConfiguration.Default);
        try
        {
            var samples = new JObject
            {
                ["zero"] = AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(0d, 0d),
                ["scale"] = AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(.04d, 0d),
                ["tripleScale"] = AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(.12d, 0d),
                ["relativeScale"] = AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(.12d, .04d),
                ["clampedHigh"] = AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(1e100d, 0d),
                ["clampedLow"] = AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(0d, 1e100d),
                ["extremeEqual"] = AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(double.MaxValue, double.MaxValue,
                    double.MaxValue),
                ["missing"] = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(null)
            };
            AtmosphereState State(decimal carbonDioxide, decimal nitrogen = 100m) => new()
            {
                DefinitionId = "probe:unregistered_surface", CapacityMoles = 1000m, ReferenceMoles = 1000m,
                ReferencePressureKPa = 100d, TemperatureKelvin = 293.15d,
                Gases = new List<AtmosphereGasAmount>
                {
                    new() { FluidId = FluidIds.CarbonDioxide, Moles = carbonDioxide },
                    new() { FluidId = FluidIds.Nitrogen, Moles = nitrogen }
                }
            };
            AtmosphereState atmosphere = State(0m);
            samples["beforeEmission"] = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(atmosphere);
            long revision = atmosphere.Revision;
            AtmosphereEmissionResult first = AtmosphereService.Emit(atmosphere,
                FluidInventory.CreateBatch(fluids.Find(FluidIds.CarbonDioxide), .4m, 0m, 293.15d));
            samples["retained"] = first.RetainedMoles;
            samples["emissionRevisionChanged"] = atmosphere.Revision > revision;
            samples["afterEmission"] = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(atmosphere);
            samples["otherGasReduced"] = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(State(.4m, 10m));
            samples["thinnerSameFraction"] = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(State(.04m, 10m));
            AtmosphereService.Emit(atmosphere,
                FluidInventory.CreateBatch(fluids.Find(FluidIds.CarbonDioxide), .8m, 0m, 293.15d));
            samples["afterSecondEmission"] = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(atmosphere);
            samples["repeatedRead"] = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(atmosphere);
            AtmosphereState exhausted = State(0m, 0m);
            samples["empty"] = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(exhausted);

            var sourceAtmosphere = State(.4m);
            float sourceBefore = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(sourceAtmosphere);
            var source = new PlanetShipAtmosphereSource(sourceAtmosphere, 20d);
            revision = sourceAtmosphere.Revision;
            source.TakeGas(sourceAtmosphere.CurrentMoles / 2m);
            samples["sourceBefore"] = sourceBefore;
            samples["sourceAfter"] = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(sourceAtmosphere);
            samples["sourceRevisionChanged"] = sourceAtmosphere.Revision > revision;

            var definition = new AtmosphereDefinition(new AtmosphereDefinitionDto
            {
                Id = "probe:surface_with_initial_carbon", InitialStandardCubicMeters = 1m,
                CapacityStandardCubicMeters = 2m, ReferenceStandardCubicMeters = 1m,
                ReferencePressureKPa = 100d, TemperatureKelvin = 293.15d,
                Composition = new List<AtmosphereCompositionDto>
                {
                    new() { FluidId = FluidIds.CarbonDioxide, Fraction = .01m },
                    new() { FluidId = FluidIds.Nitrogen, Fraction = .99m }
                }
            }, fluids);
            AtmosphereState initial = definition.CreateState();
            samples["unregisteredInitial"] = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(initial);
            samples["initialPartialPressure"] = initial.InitialCarbonDioxidePartialPressureKPa;
            AtmosphereState cloned = initial.Clone();
            AtmosphereState binary = MemoryPackSerializer.Deserialize<AtmosphereState>(MemoryPackSerializer.Serialize(initial));
            AtmosphereState json = JsonConvert.DeserializeObject<AtmosphereState>(JsonConvert.SerializeObject(initial));
            samples["cloneBaseline"] = cloned.InitialCarbonDioxidePartialPressureKPa;
            samples["binaryBaseline"] = binary.InitialCarbonDioxidePartialPressureKPa;
            samples["jsonBaseline"] = json.InitialCarbonDioxidePartialPressureKPa;
            samples["cloneOffset"] = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(cloned);
            samples["binaryOffset"] = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(binary);
            samples["jsonOffset"] = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(json);
            AtmosphereState repaired = AtmosphereService.RepairInvalidSnapshot(cloned, initial, out bool preserved);
            samples["repairBaseline"] = repaired.InitialCarbonDioxidePartialPressureKPa;
            samples["repairPreservedInventory"] = preserved && ReferenceEquals(repaired.Gases, cloned.Gases);

            var firstPlanet = new PlanetData { GlobalTemperature = PlanetData.DefaultGlobalTemperature + 2f, Atmosphere = atmosphere };
            var secondPlanet = new PlanetData { GlobalTemperature = PlanetData.DefaultGlobalTemperature + 2f, Atmosphere = State(0m) };
            samples["firstPlanet"] = firstPlanet.ClimateTemperatureOffset;
            samples["secondPlanet"] = secondPlanet.ClimateTemperatureOffset;
            samples["firstPlanetRepeated"] = firstPlanet.ClimateTemperatureOffset;

            var candidate = new ResourceReloadContext();
            AtmosphericGreenhouseConfigurationService.ConfigureResourceReload(candidate);
            candidate.InCandidate(() => AtmosphericGreenhouseConfigurationService.ReplaceConfiguration(new(.04d, 6d, -30d, 60d)));
            candidate.InCandidate(() => samples["candidateOffset"] = firstPlanet.ClimateTemperatureOffset);
            samples["afterCandidateRollback"] = firstPlanet.ClimateTemperatureOffset;
            samples["candidateKeptInventory"] = atmosphere.CurrentMoles == 101.2m;

            var invalid = new JArray();
            foreach (double value in new[] { double.NaN, double.NegativeInfinity, -1d })
            {
                bool rejected = false;
                try { AtmosphericGreenhouseTemperatureRules.ResolveTemperatureOffset(value, 0d); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                invalid.Add(rejected);
            }
            samples["invalidRejected"] = invalid;
            return samples;
        }
        finally
        {
            FluidCatalog.Replace(previousFluids);
            AtmosphericGreenhouseConfigurationService.ReplaceConfiguration(previousConfiguration);
        }
    }
    #endregion
}
