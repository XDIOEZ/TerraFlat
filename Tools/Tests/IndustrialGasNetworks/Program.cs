using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MemoryPack;

public static class Program
{
    private static int passed, failed;
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static bool Near(double a, double b, double tolerance = 1e-11)
        => Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }
    private static MachineEntity Pipe(int id, int x, double volume = 10, string phase = "both")
        => new() { Id = id, Cell = new Vector2Int(x, 0), Definition = new MachineDefinition
        { Fluid = new FluidMachineDefinition { VolumeLiters = volume, Ports = new[]
        {
            new FluidMachinePortDefinition { Id = "right", Direction = 0, Phase = phase },
            new FluidMachinePortDefinition { Id = "left", Direction = 2, Phase = phase }
        } } } };
    private static void Add(MachineEntity node, string id, decimal gas, decimal liquid = 0, double kelvin = 300)
    {
        FluidBatch batch = FluidInventory.CreateBatch(FluidCatalog.Default.Find(id), gas, liquid, kelvin);
        Require(node.Local.TryAdd(batch, node.Definition.Fluid.VolumeLiters, node.Definition.Fluid.MinimumGasSpaceLiters), "fixture add rejected");
    }
    private static (FluidNetworkGraph Topology, IndustrialGasNetworkGraph Gas) Build(params MachineEntity[] nodes)
    {
        var topology = new FluidNetworkGraph(cell => cell);
        topology.Rebuild(nodes);
        var gas = new IndustrialGasNetworkGraph(topology, nodes, node => node.Local);
        MachineWorld.Gas = gas;
        return (topology, gas);
    }
    private static decimal Total(IndustrialGasNetworkGraph gas, params MachineEntity[] nodes)
        => gas.Groups.Sum(group => group.Inventory.GasMoles) + nodes.Sum(node => node.Local.GasMoles);
    public static int Main()
    {
        FluidCatalog.Default.Register(new FluidDefinition(new FluidDefinitionDto
        { Id = "test:a", MolarMassKgPerMol = 1, LiquidDensityKgPerLiter = 1,
          GasHeatCapacityJPerMolKelvin = 20, LiquidHeatCapacityJPerMolKelvin = 40 }));
        FluidCatalog.Default.Register(new FluidDefinition(new FluidDefinitionDto
        { Id = "test:b", MolarMassKgPerMol = 1, LiquidDensityKgPerLiter = 1,
          GasHeatCapacityJPerMolKelvin = 30, LiquidHeatCapacityJPerMolKelvin = 50 }));
        FluidCatalog.Default.Register(new FluidDefinition(new FluidDefinitionDto
        {
            Id = "test:water", MolarMassKgPerMol = .018, LiquidDensityKgPerLiter = 1,
            GasHeatCapacityJPerMolKelvin = 30, LiquidHeatCapacityJPerMolKelvin = 75,
            PhaseChange = new FluidPhaseChangeDto { LatentHeatJPerMol = 40000, ReferenceBoilingKelvin = 373,
                CriticalTemperatureKelvin = 650, Saturation = new List<FluidSaturationPointDto>
                { new() { TemperatureKelvin = 273, PressureKPa = .6 }, new() { TemperatureKelvin = 373, PressureKPa = 100 },
                    new() { TemperatureKelvin = 600, PressureKPa = 10000 } } }
        }));

        Run("mixed gas merge conserves species and internal energy", () =>
        {
            var a = Pipe(1, 0); var b = Pipe(2, 1);
            Add(a, "test:a", 2, kelvin: 250); Add(b, "test:b", 3, kelvin: 500);
            double energy = a.Local.State.TotalInternalEnergyJoules + b.Local.State.TotalInternalEnergyJoules;
            var (_, gas) = Build(a, b);
            Require(gas.Groups.Count == 1 && gas.Groups[0].Inventory.GetGasMoles("test:a") == 2 &&
                gas.Groups[0].Inventory.GetGasMoles("test:b") == 3, "merge changed species quantities");
            Require(a.Local.GasMoles == 0 && b.Local.GasMoles == 0, "duplicate local ownership");
            Require(Near(gas.Groups[0].Inventory.State.TotalInternalEnergyJoules, energy), "merge changed internal energy");
        });
        Run("ordinary pipe length does not add gas transport edges", () =>
        {
            foreach (int length in new[] { 1, 2, 100, 1000 })
            {
                var nodes = Enumerable.Range(0, length).Select(i => Pipe(i + 1, i)).ToArray();
                var (topology, gas) = Build(nodes);
                var liquid = new IndustrialLiquidNetworkGraph(topology);
                Require(gas.Groups.Count == 1 && gas.Edges.Count == 0, "internal gas edges were not collapsed");
                Require(liquid.Edges.Count == (length - 1) * 2, "liquid segments incorrectly collapsed");
            }
        });
        Run("closed valve partitions gas and preserves its chamber", () =>
        {
            var a = Pipe(1, 0); var valve = Pipe(2, 1); var b = Pipe(3, 2);
            valve.Definition.Fluid.Kind = "valve"; valve.State.ValveOpen = false;
            Add(valve, "test:a", 1);
            var (_, gas) = Build(a, valve, b);
            Require(gas.Groups.Count == 3 && Total(gas, a, valve, b) == 1, "closed valve bridged or lost contents");
        });
        Run("one-way and selector boundaries are not pooled", () =>
        {
            var a = Pipe(1, 0); var middle = Pipe(2, 1); var b = Pipe(3, 2);
            middle.Definition.Fluid.Kind = "selector";
            var (_, gas) = Build(a, middle, b);
            Require(gas.Groups.Count == 2 && !gas.TryGetGroup(middle, out _), "selector bypassed");
            middle.Definition.Fluid.Kind = "pipe";
            middle.Definition.Fluid.Ports[0].Mode = "output";
            middle.Definition.Fluid.Ports[1].Mode = "input";
            (_, gas) = Build(a, middle, b);
            Require(gas.Groups.Count == 2 && !gas.TryGetGroup(middle, out _), "one-way port bypassed");
        });
        Run("liquid-only output setting cannot be bypassed by gas pooling", () =>
        {
            var a = Pipe(1, 0); var middle = Pipe(2, 1); var b = Pipe(3, 2);
            middle.State.OutletPhase = "liquid";
            var (_, gas) = Build(a, middle, b);
            Require(gas.TryGetGroup(a, out var left) && gas.TryGetGroup(b, out var right) &&
                !ReferenceEquals(left, right), "gas crossed a liquid-only output via a shared inventory");
        });
        Run("split weights use available gas space not nominal pipe volume", () =>
        {
            var a = Pipe(1, 0); var b = Pipe(2, 1);
            Add(a, "test:a", 10, liquid: 8); Add(b, "test:a", 0, liquid: 2);
            var (_, gas) = Build(a, b);
            gas.FlushToLocal(_ => true, node => node.Local);
            Require(Math.Abs(a.Local.GasMoles - 2) < .000000000001m && Math.Abs(b.Local.GasMoles - 8) < .000000000001m,
                $"expected gas shares 2/8, got {a.Local.GasMoles}/{b.Local.GasMoles}");
            Require(a.Local.LiquidMoles == 8 && b.Local.LiquidMoles == 2 && Total(gas, a, b) == 10, "liquid or total changed");
        });
        Run("failed absorption rolls back source and destination", () =>
        {
            var group = new IndustrialGasNetworkGraph.Group { TotalVolumeLiters = 10, MinimumGasSpaceLiters = .001 };
            Require(group.Inventory.TryAdd(FluidInventory.CreateBatch(FluidCatalog.Default.Find("test:a"),
                FluidInventory.MaximumMoles - 2, 0, 300), 10, .001), "fixture group add");
            var a = Pipe(1, 0); Add(a, "test:a", 1); Add(a, "test:b", 2);
            decimal before = group.Inventory.GasMoles + a.Local.GasMoles;
            bool rejected = false;
            try { IndustrialGasNetworkGraph.AbsorbGas(group, a.Local); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "over-limit merge should reject");
            Require(group.Inventory.GasMoles + a.Local.GasMoles == before,
                $"failed merge manufactured {group.Inventory.GasMoles + a.Local.GasMoles - before} mol");
        });
        Run("failed partition is atomic across every destination", () =>
        {
            var a = Pipe(1, 0); var b = Pipe(2, 1); Add(a, "test:a", 2);
            var (_, gas) = Build(a, b);
            Add(b, "test:a", FluidInventory.MaximumMoles);
            decimal before = Total(gas, a, b);
            bool rejected = false;
            try { gas.FlushToLocal(_ => true, node => node.Local); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "over-limit destination should reject");
            Require(Total(gas, a, b) == before, $"failed partition manufactured {Total(gas, a, b) - before} mol");
        });
        Run("missing member aborts before any allocation or external emission", () =>
        {
            var a = Pipe(1, 0); var b = Pipe(2, 1); Add(a, "test:a", 2);
            var (_, gas) = Build(a, b); double energy = gas.Groups[0].Inventory.State.TotalInternalEnergyJoules;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool rejected = false;
                try { gas.FlushToLocal(node => node == a, node => node.Local); }
                catch (InvalidOperationException) { rejected = true; }
                Require(rejected && gas.Groups[0].Inventory.GasMoles == 2 && a.Local.GasMoles == 0 && b.Local.GasMoles == 0 &&
                    gas.Groups[0].Inventory.State.TotalInternalEnergyJoules == energy, "failed topology retry changed gas ownership");
            }
            gas.FlushToLocal(_ => true, node => node.Local);
            Require(a.Local.GasMoles + b.Local.GasMoles == 2 && gas.Groups[0].Inventory.GasMoles == 0, "repaired lifecycle failed to commit once");
        });
        Run("failed graph construction restores previously absorbed members", () =>
        {
            var a = Pipe(1, 0); var b = Pipe(2, 1);
            Add(a, "test:a", FluidInventory.MaximumMoles); Add(b, "test:b", 1);
            decimal before = a.Local.GasMoles + b.Local.GasMoles;
            bool rejected = false;
            try { Build(a, b); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "over-limit merged network should reject");
            Require(a.Local.GasMoles + b.Local.GasMoles == before, "failed graph build erased already absorbed gas");
        });
        Run("partition plan is pure and preserves claims, mass and energy", () =>
        {
            var a = Pipe(1, 0); Add(a, "test:a", 1, kelvin: 237.123456789); Add(a, "test:b", 2, kelvin: 784.234567891);
            Require(a.Local.TryReserve("2:out:gas", FluidPhase.Gas, .4m, out _), "fixture reserve");
            double beforeEnergy = a.Local.State.TotalInternalEnergyJoules;
            long beforeRevision = a.Local.State.StateRevision;
            var shares = FluidGasPartition.Prepare(a.Local, new decimal[] { 1, 2, 7 }, new[] { 1, 2, 3 });
            Require(a.Local.State.StateRevision == beforeRevision && a.Local.GasMoles == 3, "preparing a plan changed ownership");
            Require(shares.Sum(s => s.Components.Sum(c => c.GasMoles)) == 3, "decimal partition lost matter");
            Require(Near(shares.Sum(s => s.TotalInternalEnergyJoules), beforeEnergy, 1e-14), "partition lost internal energy");
            Require(shares[0].Ports.Count == 0 && shares[1].Ports.Count == 1 && shares[2].Ports.Count == 0,
                "reservation was not assigned to its member");
        });
        Run("minimum headspace remains valid when liquid capacity is full", () =>
        {
            var a = Pipe(1, 0, 1); Add(a, "test:a", .01m, .999m);
            var (_, gas) = Build(a);
            Require(gas.TryGetGroup(a, out var group), "missing pipe group");
            double pressure = group.Inventory.GetPressureKPa(group.GasSpaceLiters, group.MinimumGasSpaceLiters);
            Require(double.IsFinite(pressure) && pressure > 0 && Total(gas, a) == .01m, "full liquid pipe lost gas or pressure");
        });
        Run("gas snapshot round-trip does not duplicate live or local ownership", () =>
        {
            var a = Pipe(-10, 0); var b = Pipe(20, 1); Add(a, "test:a", 3); Add(b, "test:b", 7);
            var (_, gas) = Build(a, b); var group = gas.Groups[0]; group.Epoch = 1;
            FluidGasNetworkSnapshot first = group.CaptureSnapshot();
            FluidGasNetworkSnapshot decoded = MemoryPackSerializer.Deserialize<FluidGasNetworkSnapshot>(MemoryPackSerializer.Serialize(first));
            decoded.Validate();
            var saved = new FluidInventory(decoded.Inventory);
            Require(saved.GasMoles == 10 && decoded.OwnerId == -10 && decoded.MemberIds.Count == 2, "save lost member or gas");
            Require(a.Local.GasMoles == 0 && b.Local.GasMoles == 0 && group.Inventory.GasMoles == 10, "capture mutated live ownership");
            saved.TryTakeExact("test:a", FluidPhase.Gas, 1, out _);
            Require(group.Inventory.GetGasMoles("test:a") == 3, "decoded snapshot aliases authority");
        });
        Run("replicas replace once and ignore stale or repeated owner messages", () =>
        {
            var a = Pipe(1, 0); var b = Pipe(2, 1); Add(a, "test:a", 5);
            var (_, gas) = Build(a, b); var group = gas.Groups[0]; group.Epoch = 4;
            var cache = new FluidGasReplicaCache(); var first = group.CaptureSnapshot();
            Require(cache.Apply(first) && !cache.Apply(first), "duplicate message reapplied");
            Require(cache.TryGet(2, out var member) && member.Inventory.GasMoles == 5, "non-owner cannot read authority");
            Require(cache.AdvanceEpoch(4) && cache.TryGet(2, out var unchanged) && ReferenceEquals(member, unchanged),
                "ordinary member snapshot erased gas");
            group.Inventory.TryTakeExact("test:a", FluidPhase.Gas, 2, out _);
            var next = group.CaptureSnapshot();
            Require(cache.Apply(next) && !cache.Apply(first) && cache.TryGet(1, out var updated) && updated.Inventory.GasMoles == 3,
                "sequence regression or duplicate gas");
            Require(group.Inventory.GasMoles == 3 && cache.NetworkCount == 1, "client changed authoritative inventory");
        });
        Run("topology epochs reject old networks without touching new inventory", () =>
        {
            var a = Pipe(1, 0); var b = Pipe(2, 1); Add(a, "test:a", 5);
            var (_, gas) = Build(a, b); var group = gas.Groups[0]; group.Epoch = 2;
            var cache = new FluidGasReplicaCache(); var old = group.CaptureSnapshot(); cache.Apply(old);
            Require(cache.AdvanceEpoch(3) && !cache.TryGet(1, out _), "new topology retained stale membership");
            Require(!cache.Apply(old) && cache.NetworkCount == 0, "late snapshot resurrected an old gas network");
            group.Epoch = 3; var current = group.CaptureSnapshot(); cache.Apply(current);
            cache.RemoveMember(1);
            Require(cache.TryGet(2, out var remaining) && remaining.Inventory.GasMoles == 5, "partial member removal destroyed authoritative snapshot");
            cache.RemoveMember(2); Require(cache.NetworkCount == 0, "removed network leaked replica entries");
        });
        Run("invalid future snapshot preserves last valid replica", () =>
        {
            var a = Pipe(1, 0); Add(a, "test:a", 2);
            var (_, gas) = Build(a); gas.Groups[0].Epoch = 7;
            var cache = new FluidGasReplicaCache(); var valid = gas.Groups[0].CaptureSnapshot(); cache.Apply(valid);
            var invalid = valid.Clone(); invalid.Epoch = 8; invalid.MemberIds.Add(1);
            bool rejected = false;
            try { cache.Apply(invalid); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && cache.Epoch == 7 && cache.TryGet(1, out var preserved) && preserved.Inventory.GasMoles == 2,
                "invalid message erased the last valid state");
        });
        Run("actual gas boundary solver conserves gas and energy without moving liquid", () =>
        {
            var pipe = Pipe(1, 0); var device = Pipe(2, 1); device.Definition.Fluid.Kind = "device";
            Add(pipe, "test:a", 1, liquid: 2, kelvin: 310); Add(device, "test:b", 0, liquid: 3, kelvin: 290);
            var (_, gas) = Build(pipe, device);
            double energy = gas.Groups[0].Inventory.State.TotalInternalEnergyJoules + pipe.Local.State.TotalInternalEnergyJoules + device.Local.State.TotalInternalEnergyJoules;
            MachineWorld.TestBeginGasStep(1, pipe, device);
            var forward = gas.Edges.Single(edge => edge.Source == pipe && edge.Target == device);
            Require(MachineWorld.TestMoveGas(forward, 1), "valid boundary transfer did not run");
            Require(gas.Groups[0].Inventory.GasMoles + device.Local.GasMoles == 1, "boundary changed gas quantity");
            Require(pipe.Local.LiquidMoles == 2 && device.Local.LiquidMoles == 3, "gas solver moved liquid");
            Require(Near(energy, gas.Groups[0].Inventory.State.TotalInternalEnergyJoules + pipe.Local.State.TotalInternalEnergyJoules + device.Local.State.TotalInternalEnergyJoules), "boundary changed energy");
            var reverse = gas.Edges.Single(edge => edge.Source == device && edge.Target == pipe);
            Require(!MachineWorld.TestMoveGas(reverse, 1), "new arrivals were sent again in the same step");
        });
        Run("long gas pipes do not manufacture extra boundary throughput", () =>
        {
            decimal? first = null;
            foreach (int length in new[] { 1, 10, 1000 })
            {
                var nodes = Enumerable.Range(0, length + 1).Select(i => Pipe(i + 1, i)).ToArray();
                var receiver = nodes[^1]; receiver.Definition.Fluid.Kind = "device";
                Add(nodes[0], "test:a", length);
                var (topology, gas) = Build(nodes);
                Require(Near(gas.Groups[0].ThroughputLitersPerSecond, 5), "capacity scaled with interior pipe count");
                MachineWorld.TestBeginGasStep(1, nodes);
                Require(MachineWorld.TestMoveGas(gas.Edges.Single(edge => edge.Target == receiver), 1), "boundary did not move");
                first ??= receiver.Local.GasMoles;
                Require(receiver.Local.GasMoles == first, "same port moved different amounts for different pipe lengths");
                var routes = new FluidNetworkGraph.SupplyWorkspace();
                topology.CollectSupplyRoutes(receiver, "main", FluidPhase.Gas, new[] { "test:a" }, routes);
                Require(routes.Nodes.Count == 2, "gas demand traversed every internal pipe instead of the shared inventory");
            }
        });
        Run("gas receiver refusal retains source matter and energy", () =>
        {
            var pipe = Pipe(1, 0); var receiver = Pipe(2, 1); receiver.Definition.Fluid.Kind = "device";
            receiver.Definition.Fluid.SingleGas = true;
            Add(pipe, "test:a", 1); Add(receiver, "test:b", 1);
            var (_, gas) = Build(pipe, receiver);
            double energy = gas.Groups[0].Inventory.State.TotalInternalEnergyJoules;
            MachineWorld.TestBeginGasStep(1, pipe, receiver);
            Require(!MachineWorld.TestMoveGas(gas.Edges.Single(edge => edge.Target == receiver), 1), "single-gas receiver accepted another species");
            Require(gas.Groups[0].Inventory.GasMoles == 1 && receiver.Local.GasMoles == 1 &&
                gas.Groups[0].Inventory.State.TotalInternalEnergyJoules == energy, "rejected transaction consumed gas or energy");
        });
        Run("client cannot run authoritative gas settlement", () =>
        {
            var pipe = Pipe(1, 0); var receiver = Pipe(2, 1); receiver.Definition.Fluid.Kind = "device"; Add(pipe, "test:a", 1);
            var (_, gas) = Build(pipe, receiver); MachineWorld.TestBeginGasStep(1, pipe, receiver);
            FlatWorld.Networking.GameNetwork.HasStateAuthority = false;
            try
            {
                Require(!MachineWorld.TestMoveGas(gas.Edges.Single(edge => edge.Target == receiver), 1) &&
                    gas.Groups[0].Inventory.GasMoles == 1 && receiver.Local.GasMoles == 0, "client mutated gas state");
            }
            finally { FlatWorld.Networking.GameNetwork.HasStateAuthority = true; }
        });
        Run("gas pressure boundary cannot pump uphill without a device doing work", () =>
        {
            var a = Pipe(1, 0); var b = Pipe(2, 1); b.Definition.Fluid.Kind = "device";
            Add(a, "test:a", 1); Add(b, "test:a", 2);
            var (_, gas) = Build(a, b); MachineWorld.TestBeginGasStep(1, a, b);
            Require(!MachineWorld.TestMoveGas(gas.Edges.Single(edge => edge.Source == a), 1), "passive boundary moved gas against pressure");
            Require(gas.Groups[0].Inventory.GasMoles == 1 && b.Local.GasMoles == 2, "pressure refusal changed matter");
        });
        Run("gas-only outlets cannot corrupt liquid routing", () =>
        {
            var source = Pipe(1, 0); source.Definition.Fluid.Kind = "outlet";
            source.Definition.Fluid.Ports[0].Mode = "output"; source.Definition.Fluid.Ports[0].Phase = "liquid";
            source.Definition.Fluid.Ports[1].Mode = "output"; source.Definition.Fluid.Ports[1].Phase = "gas";
            source.Definition.Fluid.Ports[1].Outlet = true;
            var middle = Pipe(2, 1, phase: "liquid");
            var outlet = Pipe(3, 2, phase: "liquid"); outlet.Definition.Fluid.Kind = "outlet";
            outlet.Definition.Fluid.Ports[0].Mode = "output"; outlet.Definition.Fluid.Ports[0].Outlet = true;
            outlet.Definition.Fluid.Ports[1].Mode = "input";
            var (topology, _) = Build(source, middle, outlet); var liquid = new IndustrialLiquidNetworkGraph(topology);
            var forward = liquid.Edges.Single(edge => edge.Source == source && edge.Target == middle);
            Require(!topology.CanRoute(forward), "fixture did not reproduce mixed-route contamination");
            Require(liquid.CanRoute(forward), "liquid route was blocked by an unrelated gas outlet");
            Require(liquid.Edges.All(edge => edge.SourcePort.Phase != "gas"), "gas-only edge leaked into liquid topology");
        });
        Run("phase bridge condenses against network pressure and conserves internal energy", () =>
        {
            var a = Pipe(1, 0); var b = Pipe(2, 1);
            Add(a, "test:water", 1); Add(b, "test:water", 0, liquid: 20); Add(a, "test:a", 0, liquid: 2);
            var (_, gas) = Build(a, b); var group = gas.Groups[0];
            double energy = group.Inventory.State.TotalInternalEnergyJoules + a.Local.State.TotalInternalEnergyJoules + b.Local.State.TotalInternalEnergyJoules;
            var phase = FluidGasLiquidPhaseBridge.Advance(group.Inventory, group.PhaseContacts, group.TotalVolumeLiters,
                group.MinimumGasSpaceLiters, 1, group.MaximumPhaseMolesPerSecond);
            Require(phase.ConvertedMoles > 0 && group.Inventory.GetGasMoles("test:water") < 1, "oversaturated network incorrectly evaporated local water");
            Require(group.Inventory.LiquidMoles == 0 && a.Local.GasMoles == 0 && b.Local.GasMoles == 0, "phase bridge left mixed gas ownership");
            Require(group.Inventory.GetGasMoles("test:water") + a.Local.GetLiquidMoles("test:water") + b.Local.GetLiquidMoles("test:water") == 21,
                "phase bridge changed water amount");
            Require(a.Local.GetLiquidMoles("test:a") == 2 && b.Local.GetLiquidMoles("test:a") == 0, "unrelated liquid moved between pipes");
            Require(Near(energy, group.Inventory.State.TotalInternalEnergyJoules + a.Local.State.TotalInternalEnergyJoules + b.Local.State.TotalInternalEnergyJoules, 1e-13), "phase bridge lost latent or sensible energy");
        });
        Run("phase bridge can evaporate into an initially empty gas network", () =>
        {
            var a = Pipe(1, 0); var b = Pipe(2, 1); Add(a, "test:water", 0, liquid: 20);
            var (_, gas) = Build(a, b); var group = gas.Groups[0];
            double energy = a.Local.State.TotalInternalEnergyJoules;
            var phase = FluidGasLiquidPhaseBridge.Advance(group.Inventory, group.PhaseContacts,
                group.TotalVolumeLiters, group.MinimumGasSpaceLiters, 1, group.MaximumPhaseMolesPerSecond);
            Require(phase.ConvertedMoles > 0 && group.Inventory.GasMoles > 0 && group.Inventory.GasMoles + a.Local.LiquidMoles + b.Local.LiquidMoles == 20,
                "empty gas network could not receive real evaporation");
            Require(Near(energy, group.Inventory.State.TotalInternalEnergyJoules + a.Local.State.TotalInternalEnergyJoules + b.Local.State.TotalInternalEnergyJoules),
                "evaporation created or lost internal energy");
        });
        Run("failed condensation rolls back all gas and liquid state", () =>
        {
            var a = Pipe(1, 0); a.Definition.Fluid.SingleLiquid = true;
            Add(a, "test:water", 1); Add(a, "test:a", 0, liquid: 2);
            var (_, gas) = Build(a); var group = gas.Groups[0];
            double gasEnergy = group.Inventory.State.TotalInternalEnergyJoules, liquidEnergy = a.Local.State.TotalInternalEnergyJoules;
            var phase = FluidGasLiquidPhaseBridge.Advance(group.Inventory, group.PhaseContacts,
                group.TotalVolumeLiters, group.MinimumGasSpaceLiters, 1, group.MaximumPhaseMolesPerSecond);
            Require(phase.IsBlocked && phase.ConvertedMoles == 0 && group.Inventory.GetGasMoles("test:water") == 1 && a.Local.GetLiquidMoles("test:water") == 0,
                "forbidden condensate leaked into a single-liquid chamber");
            Require(group.Inventory.State.TotalInternalEnergyJoules == gasEnergy && a.Local.State.TotalInternalEnergyJoules == liquidEnergy,
                "failed condensation changed energy");
        });
        Console.WriteLine($"RESULT {passed} passed / {failed} failed");
        return failed == 0 ? 0 : 1;
    }
}
