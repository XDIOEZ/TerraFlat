using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FlatWorld.Spaceflight;
using FlatWorld.WorldModel;
using MemoryPack;
using Newtonsoft.Json;
using UnityEngine;

internal static class ServiceProgram
{
    #region 真实大气和保存接口离线回归

    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static void Near(double actual, double expected, string message, double tolerance = .00001)
        => Check(Math.Abs(actual - expected) <= tolerance, message + $": actual={actual}, expected={expected}");

    private static void Main(string[] args)
    {
        // 直接加载正式物性定义，在独立 CLR 里只测试数据和公开业务接口。
        var dto = JsonConvert.DeserializeObject<FluidContentCatalogDto>(File.ReadAllText(
            Path.Combine(args[0], "Assets/StreamingAssets/GameConfig/Fluids/fluids.json")));
        var catalog = new FluidCatalog();
        var fluids = new List<FluidDefinition>();
        foreach (var source in dto.Fluids) fluids.Add(new FluidDefinition(source));
        catalog.Register(fluids);
        FluidCatalog.Replace(catalog);
        ValidateUnregisteredPlanetAtmosphere(catalog);
        ValidateSnowAndIceSnapshotRoundTrip();
        ValidatePlatformDoesNotMaterializeHiddenNaturalSnow();
        Console.WriteLine($"PASS: {checks} real atmosphere/PlanetData/MemoryPack checks outside Unity.");
    }

    private static void ValidateUnregisteredPlanetAtmosphere(FluidCatalog fluids)
    {
        var definition = new AtmosphereDefinition(new AtmosphereDefinitionDto
        {
            Id = "core:surface.offline_greenhouse", InitialStandardCubicMeters = 100m,
            CapacityStandardCubicMeters = 1000m, ReferenceStandardCubicMeters = 100m,
            ReferencePressureKPa = 100d, TemperatureKelvin = 293.15d,
            Composition = new List<AtmosphereCompositionDto>
            {
                new() { FluidId = FluidIds.CarbonDioxide, Fraction = .0004m },
                new() { FluidId = FluidIds.Nitrogen, Fraction = .9996m }
            }
        }, fluids);
        bool unregistered = false;
        try { AtmosphereCatalog.Default.Find(definition.Id); }
        catch (InvalidDataException) { unregistered = true; }
        Check(unregistered, "fixture matches unregistered per-surface atmosphere definitions");
        AtmosphereState initial = definition.CreateState();
        AtmosphereService.Validate(initial, fluids);
        Near(initial.InitialCarbonDioxidePartialPressureKPa, .04, "surface definition persists initial CO2 column");
        Near(AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(initial), 0, "unregistered surface starts with zero offset");
        var planet = new PlanetData { Atmosphere = initial, GlobalTemperature = PlanetData.DefaultGlobalTemperature + 7f };
        Near(planet.ClimateTemperatureOffset, 7, "manual planet baseline composes once");

        AtmosphereState secondPlanet = initial.Clone();
        decimal additionalCarbon = AtmosphereService.GetMoles(initial, FluidIds.CarbonDioxide);
        long beforeRevision = initial.Revision;
        FluidBatch gas = FluidInventory.CreateBatch(fluids.Find(FluidIds.CarbonDioxide), additionalCarbon,
            0m, initial.TemperatureKelvin);
        var emission = AtmosphereService.Emit(initial, gas);
        Check(emission.RetainedMoles == additionalCarbon && initial.Revision > beforeRevision, "real retained emission invalidates atmosphere revision");
        float warming = AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(initial);
        Near(warming, 1.7548875, "real CO2 emission updates cached greenhouse offset");
        Near(planet.ClimateTemperatureOffset, 7 + warming, "PlanetData exposes real atmosphere warming");
        Near(AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(secondPlanet), 0, "other planet remains isolated");

        AtmosphereState otherGasRemoved = initial.Clone();
        foreach (var component in otherGasRemoved.Gases)
            if (component.FluidId == FluidIds.Nitrogen) component.Moles /= 2m;
        Near(AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(otherGasRemoved), warming,
            "higher fraction without added CO2 does not create warming");
        AtmosphereState serialized = MemoryPackSerializer.Deserialize<AtmosphereState>(MemoryPackSerializer.Serialize(initial));
        Near(serialized.InitialCarbonDioxidePartialPressureKPa, .04, "saved initial column survives MemoryPack");
        Check(serialized.CurrentMoles == initial.CurrentMoles, "saved real gas inventory survives MemoryPack");
        Near(AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(serialized), warming,
            "reloaded atmosphere reconstructs correct offset without catalog definition");
        AtmosphereState corrupted = serialized.Clone();
        corrupted.InitialCarbonDioxidePartialPressureKPa = double.NaN;
        bool rejected = false;
        try { AtmosphereService.Validate(corrupted, fluids); }
        catch (InvalidDataException) { rejected = true; }
        Check(rejected, "invalid greenhouse baseline rejected by real atmosphere validation");
        AtmosphereState repaired = AtmosphereService.RepairInvalidSnapshot(corrupted, definition.CreateState(), out bool preserved);
        Check(preserved && repaired.CurrentMoles == corrupted.CurrentMoles, "repair preserves actual gas inventory");
        Near(repaired.InitialCarbonDioxidePartialPressureKPa, .04, "repair restores real planet initial CO2 baseline");
        Near(AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(repaired), warming, "repair does not reset retained warming");

        var shipSource = new PlanetShipAtmosphereSource(initial, 20d);
        beforeRevision = initial.Revision;
        var taken = shipSource.TakeGas(initial.CurrentMoles / 2m);
        Check(taken.Count > 0 && initial.Revision > beforeRevision, "real ship atmosphere withdrawal invalidates greenhouse cache");
        Near(AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(initial), 0, "actual withdrawal lowers CO2 column to baseline");
        Near(AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(serialized), warming, "withdrawal does not mutate detached saved snapshot");
        Near(AtmosphereService.GetAtmosphericGreenhouseTemperatureOffset(null), 0, "missing atmosphere has no offset");
    }

    private static void ValidateSnowAndIceSnapshotRoundTrip()
    {
        var snow = new SnowCellSaveData { LocalPosition = Vector2Int.zero, Edited = true, Depth = .23f,
            SeasonalDepth = .1f, WeatherDepth = .04f, IceMeltProgress = .37f };
        var restored = MemoryPackSerializer.Deserialize<SnowCellSaveData>(MemoryPackSerializer.Serialize(snow));
        Near(restored.IceMeltProgress, .37, "partial ice progress survives actual save serializer");
        using var buffer = new ChunkTerrainBuffer(1, 1);
        buffer.SetCell(0, 0, new TerrainCell(29, 0, 0, 8, 2, TerrainCellFlags.Walkable));
        using var terrain = buffer.Seal();
        WorldSnowInteraction.Restore(terrain, restored);
        var captured = WorldSnowInteraction.Capture(terrain, Vector2Int.zero);
        Check(captured.Edited && captured.Depth == restored.Depth && captured.WeatherDepth == restored.WeatherDepth,
            "real snow terrain capture/restore preserves all dynamic snow fields");
        Near(captured.IceMeltProgress, .37, "real terrain capture/restore preserves partial ice progress");
        var wire = new WeatherSurfaceDelta();
        wire.Cells.Add(new WeatherSurfaceCellDelta { LocalPosition = Vector2Int.zero, SnowChanged = true,
            Edited = captured.Edited, Depth = captured.Depth, SeasonalDepth = captured.SeasonalDepth,
            WeatherDepth = captured.WeatherDepth, IceMeltProgress = captured.IceMeltProgress });
        var received = MemoryPackSerializer.Deserialize<WeatherSurfaceDelta>(MemoryPackSerializer.Serialize(wire));
        received.Validate(1, 1);
        Near(received.Cells[0].IceMeltProgress, .37, "real network payload serializer keeps partial ice progress");
        received.Cells[0].IceMeltProgress = float.NaN;
        bool rejected = false;
        try { received.Validate(1, 1); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "real network payload rejects invalid ice progress");
    }

    private static void ValidatePlatformDoesNotMaterializeHiddenNaturalSnow()
    {
        using var buffer = new ChunkTerrainBuffer(1, 1);
        buffer.SetCell(0, 0, new TerrainCell(29, 0, 0, 8, 2, TerrainCellFlags.Walkable));
        buffer.SetEnvironmentValue(SnowDepthLayer.LayerId, 0, 0, 1f);
        using var terrain = buffer.Seal();
        MethodInfo exposure = typeof(WorldSnowSystem).GetMethod("GetExposedNaturalDepth", BindingFlags.NonPublic | BindingFlags.Static);
        Check(exposure != null, "production natural-snow exposure entry is present");
        float Exposed() => (float)exposure.Invoke(null, new object[] { terrain, 0, 0 });
        Near(Exposed(), 1, "uncovered natural snow is eligible for thaw");
        TerrainSupportLayer.Set(terrain, 0, 0, 91, 2);
        Near(Exposed(), 0, "platform excludes hidden natural snow from thaw input");
        Near(WorldSnowSystem.GetSurfaceDepth(terrain, 0, 0, null, 0f), 0, "platform query shows no hidden ground snow");
        var thaw = WorldSurfaceThawRules.AdvanceSnow(Exposed(), false, 0f, 0f, 0f, 0f,
            10f, 0f, 100f, 0f, .003f, .00012f);
        Check(!thaw.Edited && thaw.Depth == 0f && thaw.Melted == 0f,
            "hidden snow creates neither platform snow nor meltwater");
        TerrainSupportLayer.Set(terrain, 0, 0, 0, 2);
        Near(Exposed(), 1, "removing platform restores original natural snow thaw input");
        Near(WorldSnowSystem.GetSurfaceDepth(terrain, 0, 0, null, 0f), 1, "removing platform reveals preserved natural ground snow");
    }

    #endregion
}
