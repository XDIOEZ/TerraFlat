using System;
using System.Collections.Generic;
using System.Diagnostics;
using MemoryPack;
using Newtonsoft.Json;
using FlatWorld.Spaceflight;

public static class Program
{
    #region 流体真实状态与热路径回归
    private static int checks;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); checks++; }
    private static bool Near(double a, double b, double scale = 1e-9) => Math.Abs(a - b) <= scale * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    private static FluidDefinition Gas(string id) => new(new FluidDefinitionDto {
        Id = id, MolarMassKgPerMol = .032, LiquidDensityKgPerLiter = 1,
        GasHeatCapacityJPerMolKelvin = 20, LiquidHeatCapacityJPerMolKelvin = 40
    });
    private static FluidInventory Filled(string id, decimal gas, decimal liquid = 0m)
    {
        var inventory = new FluidInventory();
        if (gas + liquid > 0) Check(inventory.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(id), gas, liquid, 300), 100, .001), "fill");
        return inventory;
    }
    private static (long Bytes, double Ms) Bench(Action action, int iterations)
    {
        for (int i = 0; i < 100; i++) action();
        var watch = new Stopwatch();
        long before = GC.GetAllocatedBytesForCurrentThread();
        watch.Start();
        for (int i = 0; i < iterations; i++) action();
        watch.Stop();
        return (GC.GetAllocatedBytesForCurrentThread() - before, watch.Elapsed.TotalMilliseconds);
    }
    public static void Main()
    {
        FluidCatalog.Default.Register(Gas("test:a")); FluidCatalog.Default.Register(Gas("test:b"));
        var inventory = Filled("test:a", 10, 2);
        int events = 0;
        inventory.State.MassChanged += _ => events++;
        long mass = inventory.State.MassRevision;
        inventory.State.Components[0].GasMoles += 1;
        Check(events == 1 && inventory.State.MassRevision > mass && inventory.GasMoles == 11, "direct moles invalidate mass/cache");
        mass = inventory.State.MassRevision; events = 0;
        FluidThermodynamics.AddHeat(inventory, 20);
        Check(events == 0 && inventory.State.MassRevision == mass, "heat does not invalidate mass");
        Check(inventory.TryReserve("reserve", FluidPhase.Gas, 1, out _), "reserve");
        Check(events == 0 && inventory.State.MassRevision == mass, "reservation does not invalidate mass");
        long stateVersion = inventory.State.StateRevision;
        inventory.State.Ports[0].GasMoles -= .1m;
        Check(inventory.State.StateRevision > stateVersion && inventory.State.MassRevision == mass, "direct reservation invalidates state only");
        var oldComponent = inventory.State.Components[0];
        var swapped = oldComponent.Clone();
        inventory.State.Components = new List<FluidComponentState> { swapped };
        Check(events == 1, "component list replacement notifies");
        events = 0; oldComponent.GasMoles += 1;
        Check(events == 0, "old component detached");
        swapped.GasMoles += 1;
        Check(events == 1 && inventory.GasMoles == 12, "new component observed");
        mass = inventory.State.MassRevision;
        inventory.State.Components[0] = swapped.Clone();
        Check(inventory.State.MassRevision > mass, "same-count list edit revision synchronizes");
        inventory.State.TotalInternalEnergyJoules = FluidCatalog.Default.Find("test:a").EnergyAt(12, 2, 300);
        var before = inventory.State.Clone();
        Check(inventory.TryTakeExact("test:a", FluidPhase.Gas, 2, out _), "take");
        inventory.Restore(before);
        Check(inventory.GasMoles == 12 && inventory.LiquidMoles == 2 && inventory.State.Ports.Count == 1 && Near(inventory.GetTemperatureKelvin(), 300), "atomic restore");
        var freeze = new FluidInventoryState(); freeze.CopyFrom(inventory.State, false);
        Check(freeze.Ports.Count == 0 && freeze.Components[0].GasMoles == 12, "frozen budget excludes reservations");
        inventory.State.Components[0].GasMoles -= 1;
        Check(freeze.Components[0].GasMoles == 12, "frozen stock independent");
        inventory.Restore(before);
        var json = JsonConvert.SerializeObject(inventory.State);
        Check(!json.Contains("Revision") && !json.Contains("MassChanged") && !json.Contains("Owner"), "JSON ignores runtime observer graph");
        var binary = MemoryPackSerializer.Serialize(inventory.State);
        var restored = MemoryPackSerializer.Deserialize<FluidInventoryState>(binary);
        Check(binary[0] == 4 && restored.Components.Count == 1 && restored.Ports.Count == 1, "MemoryPack persists only four payload members");
        var exact = new FluidInventoryState
        {
            Components = new List<FluidComponentState> { new FluidComponentState
                { FluidId = "test:a", GasMoles = 1.234567890123456789m, LiquidMoles = 0.0000000000000000001m } },
            Ports = new List<FluidOutputReservation> { new FluidOutputReservation
                { PortId = "port", FluidId = "test:a", GasMoles = .0000000000000000001m, LiquidMoles = 1.00000000000001m } }
        };
        var oldCulture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var exactRestored = MemoryPackSerializer.Deserialize<FluidInventoryState>(MemoryPackSerializer.Serialize(exact));
            Check(exactRestored.Components[0].GasMoles == 1.234567890123456789m &&
                exactRestored.Components[0].LiquidMoles == .0000000000000000001m &&
                exactRestored.Ports[0].LiquidMoles == 1.00000000000001m, "decimal payload remains exact across locale");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = oldCulture; }
        int originalEvents = events;
        var cloned = FastCloner.FastCloner.DeepClone(inventory.State);
        _ = cloned.MassRevision; cloned.Components[0].GasMoles += 1;
        Check(events == originalEvents && inventory.State.Components[0].GasMoles == 12, "FastCloner excludes source subscriber and owner");
        Check(inventory.TryGetPressureAfterAdding(FluidInventory.CreateBatch(FluidCatalog.Default.Find("test:b"), 1, 0, 250), 100, .001, out double predicted), "pressure projection");
        var trial = new FluidInventory(inventory.State.Clone());
        Check(trial.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find("test:b"), 1, 0, 250), 100, .001) && Near(predicted, trial.GetPressureKPa(100, .001)), "pressure projection equals actual add");
        var a = Filled("test:a", 3); var b = Filled("test:a", 0); var c = Filled("test:a", 0);
        var sa = new FluidInventoryState(); var sb = new FluidInventoryState();
        sa.CopyFrom(a.State, false); sb.CopyFrom(b.State, false);
        Check(a.TryReserve("a-b", FluidPhase.Gas, 1, out _) && a.TryTransfer("a-b", b, 1, 100, .001), "chain first segment");
        Check(sb.Components.Count == 0 && b.GasMoles == 1 && c.GasMoles == 0, "new stock cannot traverse next segment same step");
        sb.CopyFrom(b.State, false);
        Check(sb.Components[0].GasMoles == 1 && b.TryReserve("b-c", FluidPhase.Gas, 1, out _) && b.TryTransfer("b-c", c, 1, 100, .001), "next-step stock advances");
        var water = new FluidDefinition(new FluidDefinitionDto {
            Id = "test:water", MolarMassKgPerMol = .018, LiquidDensityKgPerLiter = 1,
            GasHeatCapacityJPerMolKelvin = 34, LiquidHeatCapacityJPerMolKelvin = 75,
            PhaseChange = new FluidPhaseChangeDto { LatentHeatJPerMol = 40000, ReferenceBoilingKelvin = 373, CriticalTemperatureKelvin = 650,
                Saturation = new List<FluidSaturationPointDto> { new() { TemperatureKelvin = 273, PressureKPa = .6 }, new() { TemperatureKelvin = 373, PressureKPa = 100 }, new() { TemperatureKelvin = 600, PressureKPa = 10000 } } }
        });
        FluidCatalog.Default.Register(water);
        var wet = Filled("test:water", 0, 10);
        double energy = wet.State.TotalInternalEnergyJoules;
        var phase = FluidThermodynamics.AdvancePhaseChange(wet, 100, .001, .1, 10);
        Check(phase.ConvertedMoles > 0 && wet.GasMoles + wet.LiquidMoles == 10 && wet.State.TotalInternalEnergyJoules == energy, "phase change preserves total mass/energy");
        long beforeNoop = inventory.State.StateRevision; inventory.Restore(before);
        Check(inventory.State.StateRevision == beforeNoop, "same-state restore does not manufacture changes");
        var cloneBench = Bench(() => _ = inventory.State.Clone(), 10000);
        var freezeBench = Bench(() => freeze.CopyFrom(inventory.State, false), 10000);
        var empty = new FluidInventory();
        var emptyPhaseBench = Bench(() => FluidThermodynamics.AdvancePhaseChange(empty, 1, .001, .1, 10), 10000);
        var readsBench = Bench(() => { _ = inventory.GasMoles; _ = inventory.GetLiquidLiters(); _ = inventory.GetPressureKPa(100, .001); }, 10000);
        Check(freezeBench.Bytes == 0 && emptyPhaseBench.Bytes == 0 && readsBench.Bytes == 0,
            "stable freeze, empty phase, and derived queries allocate zero bytes");
        TestEnvironmentQueries();
        Console.WriteLine($"PASS {checks} checks");
        Console.WriteLine($"10000 full snapshot clone: {cloneBench.Bytes} B / {cloneBench.Ms:F3} ms");
        Console.WriteLine($"10000 reusable freeze: {freezeBench.Bytes} B / {freezeBench.Ms:F3} ms");
        Console.WriteLine($"10000 empty phase steps: {emptyPhaseBench.Bytes} B / {emptyPhaseBench.Ms:F3} ms");
        Console.WriteLine($"10000 combined derived reads: {readsBench.Bytes} B / {readsBench.Ms:F3} ms");
    }
    #endregion

    #region 舱室索引与库存包装失效回归
    private sealed class Outside : IShipAtmosphereSource
    {
        public double PressureKPa => 90;
        public double TemperatureCelsius => 10;
        public IReadOnlyList<FluidBatch> TakeGas(decimal maximumMoles) => throw new NotSupportedException();
        public void ReleaseGas(FluidBatch gas) => throw new NotSupportedException();
    }
    private static void TestEnvironmentQueries()
    {
        FluidCatalog.Default.Register(Gas(FluidIds.Oxygen));
        FluidCatalog.Default.Register(Gas(FluidIds.CarbonDioxide));
        var ship = new ShipState();
        var floor = new ShipPieceState { Kind = ShipPieceKind.Floor, CellX = -2, CellY = -1,
            Width = 2, Height = 3, QuarterTurns = 1 };
        ship.Pieces.Add(floor);
        var room = new ShipCompartmentState { VolumeLiters = 100,
            Cells = new List<ShipCell> { new(-2, -1), new(-1, -1) }, Gas = Filled(FluidIds.Oxygen, 5).State };
        ship.Compartments.Add(room);
        ShipEnvironmentSnapshot initial = ShipAtmosphereService.GetEnvironment(ship, -2, -1);
        Check(initial.IsSealed && initial.HasFloorSupport && initial.CompartmentId == room.CompartmentId && initial.CanBreathe,
            "sealed room and negative floor coordinate indexed");
        Check(ShipAtmosphereService.HasFloor(ship, 0, 0) && !ShipAtmosphereService.HasFloor(ship, 1, 0),
            "rotated floor footprint indexed");
        var outside = new Outside();
        var open = ShipAtmosphereService.GetEnvironment(ship, 0, 0, outside);
        Check(!open.IsSealed && open.HasFloorSupport && open.PressureKPa == 90 && open.TemperatureCelsius == 10,
            "unsealed supported cell reads actual outside");
        FluidInventory wrapper = ShipAtmosphereService.GetCompartmentInventory(room);
        Check(ReferenceEquals(wrapper, ShipAtmosphereService.GetCompartmentInventory(room)), "stable room inventory wrapper reused");
        room.Gas.Components[0].GasMoles += 1;
        var changedQuantity = ShipAtmosphereService.GetEnvironment(ship, -2, -1);
        Check(wrapper.GasMoles == 6 && Near(wrapper.GetTemperatureKelvin(), 250) && Near(changedQuantity.PressureKPa, initial.PressureKPa),
            "direct room quantity changes reach reused wrapper");
        Check(wrapper.TryGetPressureAfterAdding(FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.CarbonDioxide), .01m, 0, 300),
            room.VolumeLiters, .001, out _) && wrapper.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find(FluidIds.CarbonDioxide), .01m, 0, 300),
            room.VolumeLiters, .001), "room gas change");
        Check(ShipAtmosphereService.GetEnvironment(ship, -2, -1).CarbonDioxidePartialPressureKPa > 0,
            "new gas component appears in cached environment");
        FluidInventoryState previousGas = room.Gas;
        room.Gas = Filled(FluidIds.Oxygen, 2).State;
        FluidInventory replacementWrapper = ShipAtmosphereService.GetCompartmentInventory(room);
        Check(!ReferenceEquals(wrapper, replacementWrapper) && ReferenceEquals(replacementWrapper.State, room.Gas),
            "gas reference replacement rebinds wrapper");
        Check(ShipAtmosphereService.GetEnvironment(ship, -2, -1).PressureKPa < initial.PressureKPa,
            "gas reference replacement invalidates pressure readings");
        previousGas.Components[0].GasMoles += 50;
        Check(replacementWrapper.GasMoles == 2, "old room gas no longer affects environment");
        FluidCatalog previousCatalog = FluidCatalog.Default;
        var replacementCatalog = new FluidCatalog();
        replacementCatalog.Register(Gas(FluidIds.Oxygen)); replacementCatalog.Register(Gas(FluidIds.CarbonDioxide));
        FluidCatalog.Replace(replacementCatalog);
        FluidInventory catalogWrapper = ShipAtmosphereService.GetCompartmentInventory(room);
        Check(!ReferenceEquals(replacementWrapper, catalogWrapper) && ReferenceEquals(catalogWrapper.Catalog, replacementCatalog) &&
            ReferenceEquals(catalogWrapper.State, room.Gas), "catalog replacement rebinds wrapper without copying gas");
        Check(Near(ShipAtmosphereService.GetEnvironment(ship, -2, -1).PressureKPa,
            catalogWrapper.GetPressureKPa(room.VolumeLiters, .001)), "catalog-rebound environment derives current pressure");
        FluidCatalog.Replace(previousCatalog);
        floor.CellX = 10;
        room.Cells[0].X = 10;
        ship.NotifyStructureChanged();
        Check(!ShipAtmosphereService.HasFloor(ship, -2, -1) && ShipAtmosphereService.HasFloor(ship, 10, -1) &&
            ShipAtmosphereService.FindCompartment(ship, -2, -1) == null && ReferenceEquals(ShipAtmosphereService.FindCompartment(ship, 10, -1), room),
            "structure revision invalidates both floor and room indexes");
        var newRoom = new ShipCompartmentState { VolumeLiters = 100, Cells = new List<ShipCell> { new(10, -1) }, Gas = room.Gas };
        ship.Compartments = new List<ShipCompartmentState> { newRoom };
        Check(ReferenceEquals(ShipAtmosphereService.FindCompartment(ship, 10, -1), newRoom),
            "same-count room collection replacement invalidates index");
        ship.Compartments.Add(new ShipCompartmentState { Cells = new List<ShipCell> { new(12, -1) }, VolumeLiters = 100 });
        Check(ShipAtmosphereService.FindCompartment(ship, 12, -1) != null, "room count change invalidates index");
        ship.Pieces = new List<ShipPieceState> { new() { Kind = ShipPieceKind.Floor, CellX = 10, CellY = -1, Width = 1, Height = 1 } };
        Check(ShipAtmosphereService.HasFloor(ship, 10, -1) && !ShipAtmosphereService.HasFloor(ship, 11, -1),
            "same-count floor collection replacement invalidates index");
        var environmentBench = Bench(() => _ = ShipAtmosphereService.GetEnvironment(ship, 10, -1), 10000);
        Check(environmentBench.Bytes == 0, "stable cached environment queries allocate zero bytes");
        Console.WriteLine($"10000 cached GetEnvironment: {environmentBench.Bytes} B / {environmentBench.Ms:F3} ms");
    }
    #endregion
}
