using FlatWorld.Spaceflight;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class Program
{
    #region 正式气相图与守恒预算回归
    private static readonly string[] fuelIds = { FluidIds.Hydrogen, FluidIds.Oxygen };
    private static int assertions;
    private static void Check(bool pass, string message)
    { assertions++; if (!pass) throw new InvalidOperationException(message); }
    private static FluidMachinePortDefinition Port(string id, int direction, string mode = "both", string phase = "gas")
        => new() { Id = id, Direction = direction, Mode = mode, Phase = phase };
    private static MachineEntity Node(int id, int x, int y, string kind, params FluidMachinePortDefinition[] ports)
        => new() { Id = id, Cell = new(x, y), Definition = new() { Id = kind, Layer = kind == "pipe" ? 2 : 0,
            Fluid = new() { Kind = kind, Ports = ports, ThroughputLitersPerSecond = 1000, GasCapacityStandardLiters = 1000, VolumeLiters = 5 } } };
    private static FluidNetworkGraph Graph(params MachineEntity[] nodes)
    {
        var graph = new FluidNetworkGraph(cell => cell); graph.Rebuild(nodes); MachineWorld.SetGraph(graph); return graph;
    }
    private static FluidNetworkGraph.SupplyWorkspace Routes(FluidNetworkGraph graph, MachineEntity engine)
    { var workspace = new FluidNetworkGraph.SupplyWorkspace(); graph.CollectSupplyRoutes(engine, "main", FluidPhase.Gas, fuelIds, workspace); return workspace; }
    private static void AddGas(FluidInventory inventory, string id, decimal amount)
        => Check(inventory.TryAdd(FluidInventory.CreateBatch(inventory.Catalog.Find(id), amount, 0m, 293.15), 100, .001), "add fixture gas");
    private static void Connectivity()
    {
        MachineWorld.Reset();
        var engine = Node(1, 0, 0, "receiver", Port("fuel", 1, "input"));
        var pipe = Node(2, 0, 1, "pipe", Port("down", 3), Port("up", 1));
        var tank = Node(3, 0, 2, "tank", Port("down", 3));
        var disconnected = Node(4, 2, 2, "tank", Port("down", 3));
        var graph = Graph(engine, pipe, tank, disconnected);
        Check(Routes(graph, engine).Nodes.Any(value => value.Node == tank), "connected upstream tank can feed engine");
        Check(!Routes(graph, engine).Nodes.Any(value => value.Node == disconnected), "same ship disconnected tank cannot feed engine");
        Check(graph.GetConnectionMask(pipe) == 5, "pipe displays both engine inlet and tank edges");
        graph = Graph(engine, tank, disconnected);
        Check(Routes(graph, engine).Nodes.Count == 1, "removed pipe cuts supply");
        graph = Graph(engine, pipe, tank); engine.RotationQuarterTurns = 2;
        graph.Rebuild(new[] { engine, pipe, tank });
        Check(Routes(graph, engine).Nodes.Count == 1, "rotated engine intake no longer accepts wrong side");
        engine.RotationQuarterTurns = 0; pipe.Definition.Fluid.Kind = "valve";
        pipe.FluidState.ValveOpen = false; graph.Rebuild(new[] { engine, pipe, tank });
        Check(Routes(graph, engine).Nodes.Count == 1, "closed valve cuts supply");
        pipe.FluidState.ValveOpen = true; graph.Rebuild(new[] { engine, pipe, tank });
        Check(Routes(graph, engine).Nodes.Count == 3, "reopened valve reconnects supply");
        pipe.Definition.Fluid.Kind = "selector"; pipe.Definition.Fluid.Ports[0].Id = "selected";
        graph.Rebuild(new[] { engine, pipe, tank });
        Check(Routes(graph, engine).Nodes.Count == 1, "hydrogen-only selector is not a mixed fuel route");
        pipe.Definition.Fluid.Kind = "pipe"; pipe.Definition.Fluid.Ports[0].FluidId = FluidIds.Hydrogen;
        graph.Rebuild(new[] { engine, pipe, tank });
        Check(Routes(graph, engine).Nodes.Count == 1, "hydrogen-only port refuses oxygen recipe");
        pipe.Definition.Fluid.Ports[0].FluidId = ""; tank.Definition.Fluid.Ports[0].Mode = "input";
        graph.Rebuild(new[] { engine, pipe, tank });
        Check(!Routes(graph, engine).Nodes.Any(value => value.Node == tank), "input-only tank cannot be used backwards");
        tank.Definition.Fluid.Ports[0].Mode = "both"; tank.Definition.Fluid.Ports[0].Phase = "liquid";
        graph.Rebuild(new[] { engine, pipe, tank });
        Check(!Routes(graph, engine).Nodes.Any(value => value.Node == tank), "liquid-only port cannot supply gas fuel");
    }
    private static void AtomicFuelAndBudgets()
    {
        MachineWorld.Reset();
        var engine = Node(11, 0, 0, "receiver", Port("fuel", 1, "input"));
        var pipe = Node(12, 0, 1, "pipe", Port("down", 3), Port("up", 1));
        var tank = Node(13, 0, 2, "tank", Port("down", 3));
        var graph = Graph(engine, pipe, tank); var nodes = new[] { engine, pipe, tank };
        var inventory = MachineWorld.GetFluidInventory(tank);
        AddGas(inventory, FluidIds.Hydrogen, 20m); AddGas(inventory, FluidIds.Oxygen, 10m);
        var configuration = new ShipPartConfiguration();
        Check(ShipFuel.IsEligible(inventory, configuration), "2:1 real tank is qualified fuel");
        Check(ShipFuel.TryBurn(inventory, configuration, 1m, out double energy) && energy == 483600d, "burn real fuel atomically");
        Check(inventory.GetGasMoles(FluidIds.Hydrogen) == 18m && inventory.GetGasMoles(FluidIds.Oxygen) == 9m, "burn consumes exactly 2H:1O");
        Check(!ShipFuel.TryBurn(inventory, configuration, 10m, out _) && inventory.GetGasMoles(FluidIds.Hydrogen) == 18m, "insufficient oxygen leaves hydrogen intact");
        inventory.State.Ports.Add(new FluidOutputReservation { PortId = "reserved", FluidId = FluidIds.Hydrogen, GasMoles = 18m });
        Check(!ShipFuel.TryBurn(inventory, configuration, 1m, out _), "reserved fuel cannot be burned again");
        inventory.State.Ports.Clear(); MachineWorld.Freeze(nodes, 1d);
        var routes = Routes(graph, engine); int source = routes.Nodes.FindIndex(value => value.Node == tank);
        Check(MachineWorld.GetFluidSupplyStandardLiters(routes, source, 1d, true) == 1000d, "whole path obeys normal throughput");
        pipe.Definition.Fluid.ThroughputLitersPerSecond = 50;
        Check(MachineWorld.GetFluidSupplyStandardLiters(routes, source, 1d, true) == 50d, "one narrow pipe limits entire supply path");
        var projected = new Dictionary<FluidInventory, double>();
        Check(MachineWorld.GetFluidSupplyStandardLiters(routes, source, 1d, false, projected) == 50d, "prediction starts with real path bottleneck");
        MachineWorld.ReserveFluidSupplyProjection(routes, source, 40d, projected);
        Check(MachineWorld.GetFluidSupplyStandardLiters(routes, source, 1d, false, projected) == 10d, "prediction shares pipe and receiver budget across supplies");
        Check(MachineWorld.GetFluidSupplyStandardLiters(routes, source, 1d, true) == 50d, "prediction leaves live budgets untouched");
        pipe.Definition.Fluid.ThroughputLitersPerSecond = 1000;
        var recipe = new[] { new FluidRequirement(FluidIds.Hydrogen, FluidPhase.Gas, 2m), new FluidRequirement(FluidIds.Oxygen, FluidPhase.Gas, 1m) };
        Check(ShipFuel.TryBurn(inventory, configuration, 1m, out _), "consume budget fixture fuel");
        MachineWorld.ConsumeFluidSupplyBudget(routes, source, recipe);
        double used = (double)FluidUnits.MolToStandardLiters(3m);
        Check(Math.Abs(MachineWorld.GetFluidSupplyStandardLiters(routes, source, 1d, true) - (1000d - used)) < 1e-9, "burn deducts shared budget along all nodes");
        Check(MachineWorld.GetFluidSupplyStock(routes, source, FluidIds.Oxygen, FluidPhase.Gas) == 8m, "burn deducts frozen source oxygen stock");
        AddGas(inventory, FluidIds.Hydrogen, 2m); AddGas(inventory, FluidIds.Oxygen, 1m);
        Check(MachineWorld.GetFluidSupplyStock(routes, source, FluidIds.Oxygen, FluidPhase.Gas) == 8m, "new gas does not pass frozen stock in same machine step");
        MachineWorld.ConsumeFluidRouteBudget(routes, source, 1000d);
        Check(MachineWorld.GetFluidSupplyStandardLiters(routes, source, 1d, true) == 0d, "shared exhausted pipe blocks subsequent engine draws");
    }
    #endregion

    #region 双燃料消耗和两相补充
    private static FluidInventory Fuel(decimal hydrogen, decimal oxygen, decimal oil, string liquidId = ShipFuel.LiquidFuelId)
    {
        var inventory = new FluidInventory();
        if (hydrogen > 0m) AddGas(inventory, FluidIds.Hydrogen, hydrogen);
        if (oxygen > 0m) AddGas(inventory, FluidIds.Oxygen, oxygen);
        if (oil > 0m) Check(inventory.TryAdd(FluidInventory.CreateBatch(inventory.Catalog.Find(liquidId), 0m, oil, 293.15d),
            100d, .001d), "prepare liquid fuel");
        return inventory;
    }

    private static void DualFuelReactions()
    {
        var configuration = new ShipPartConfiguration();
        foreach (var sample in new[]
        {
            ("hydrogen rich", 10m, 2m, 0m, 2d/3d, 6m, 0m, 0m),
            ("oxygen rich", 2m, 5m, 0m, 1d/3d, 0m, 4m, 0m),
            ("oil oxygen limited", 0m, 3m, 1m, 1d, 0m, 0m, .85m),
            ("oil fuel limited", 0m, 5m, .05m, 1d/3d, 0m, 4m, 0m),
            ("oil without oxygen", 0m, 0m, 1m, 0d, 0m, 0m, 1m),
            ("oxygen only", 0m, 5m, 0m, 0d, 0m, 5m, 0m),
            ("hydrogen only", 5m, 0m, 0m, 0d, 5m, 0m, 0m),
            ("dual fuel switch", 2m, 3m, 1m, 1d, 0m, 0m, .9m)
        })
        {
            var inventory = Fuel(sample.Item2, sample.Item3, sample.Item4);
            double output = ShipFuel.ConsumeStoredChemicalPropellants(inventory, configuration, 1d, 1d);
            Check(Math.Abs(output - sample.Item5) < 1e-12d, sample.Item1 + " actual output");
            Check(inventory.GetGasMoles(FluidIds.Hydrogen) == sample.Item6 && inventory.GetGasMoles(FluidIds.Oxygen) == sample.Item7 &&
                inventory.GetLiquidMoles(ShipFuel.LiquidFuelId) == sample.Item8, sample.Item1 + " exact leftovers");
            Check(ShipFuel.ConsumeStoredChemicalPropellants(inventory, configuration, 1d, 1d) == 0d, sample.Item1 + " stops on missing component");
        }
        var idle = Fuel(2m, 3m, 1m);
        string before = JsonConvert.SerializeObject(idle.State);
        Check(ShipFuel.ConsumeStoredChemicalPropellants(idle, configuration, 0d, 1d) == 0d &&
            before == JsonConvert.SerializeObject(idle.State), "idle engine keeps every component");
        Check(!ShipFuel.TryBurn(idle, configuration, 4m, out _) && before == JsonConvert.SerializeObject(idle.State), "overdraw rolls back both phases");
        var impure = Fuel(10m, 3m, 0m); AddGas(impure, FluidIds.Nitrogen, 100m);
        Check(ShipFuel.TryBurn(impure, configuration, 3m, out _) && impure.GetGasMoles(FluidIds.Nitrogen) == 100m,
            "unrelated gas remains and does not impose a perfect mixture");
        var wrong = Fuel(0m, 3m, 1m, FluidIds.Nitrogen);
        Check(!ShipFuel.IsEligible(wrong, configuration) && !ShipFuel.TryBurn(wrong, configuration, 1m, out _), "nonfuel liquid cannot ignite");
        var reserved = Fuel(0m, 3m, 1m);
        reserved.State.Ports.Add(new FluidOutputReservation { PortId = "other-consumer", FluidId = FluidIds.Oxygen, GasMoles = 3m });
        Check(!ShipFuel.TryBurn(reserved, configuration, 1m, out _) && reserved.GetLiquidMoles(ShipFuel.LiquidFuelId) == 1m,
            "reserved oxygen cannot consume liquid fuel");
        configuration.LiquidFuelOxygenMolesPerMole = 10f;
        var configured = Fuel(0m, 1m, 1m);
        Check(ShipFuel.TryBurn(configured, configuration, 1m, out _) && configured.GetLiquidMoles(ShipFuel.LiquidFuelId) == .9m,
            "liquid oxygen ratio follows engine configuration");
    }

    private static void DualPhaseRefilling()
    {
        MachineWorld.Reset();
        var gasPort = Port("gas", 1, "input");
        var liquidPort = Port("liquid", 0, "input", "liquid"); liquidPort.FluidId = ShipFuel.LiquidFuelId;
        var engine = Node(41, 0, 0, "receiver", gasPort, liquidPort);
        var pipe = Node(42, 0, 1, "pipe", Port("down", 3), Port("up", 1));
        var liquidPipe = Node(45, 1, 0, "pipe", Port("left", 2, phase: "liquid"), Port("right", 0, phase: "liquid"));
        var gasTank = Node(43, 0, 2, "tank", Port("down", 3));
        var liquidTank = Node(44, 2, 0, "tank", Port("left", 2, phase: "liquid"));
        var nodes = new[] { engine, pipe, gasTank, liquidTank, liquidPipe };
        Graph(nodes);
        var gas = MachineWorld.GetFluidInventory(gasTank); AddGas(gas, FluidIds.Oxygen, 6m);
        var liquid = MachineWorld.GetFluidInventory(liquidTank);
        Check(liquid.TryAdd(FluidInventory.CreateBatch(liquid.Catalog.Find(ShipFuel.LiquidFuelId), 0m, 1m, 293.15d), 5d, .001d), "liquid source prepared");
        MachineWorld.Freeze(nodes, 1d); MachineWorld.SupplyForTest(engine, 1f);
        var local = MachineWorld.GetFluidInventory(engine);
        Check(local.GetGasMoles(FluidIds.Oxygen) > 0m && local.GetLiquidMoles(ShipFuel.LiquidFuelId) > 0m, "formal consumer fills both phases");
        Check(local.GetGasMoles(FluidIds.Oxygen) + gas.GetGasMoles(FluidIds.Oxygen) == 6m &&
            local.GetLiquidMoles(ShipFuel.LiquidFuelId) + liquid.GetLiquidMoles(ShipFuel.LiquidFuelId) == 1m, "refilling conserves both real sources");
        Check(ShipFuel.ConsumeStoredChemicalPropellants(local, new ShipPartConfiguration(), 1d, 1d) == 1d, "filled oil and oxygen provide full output");
        var restored = new FluidInventory(JsonConvert.DeserializeObject<FluidInventoryState>(JsonConvert.SerializeObject(local.State)));
        Check(restored.GetGasMoles(FluidIds.Oxygen) == local.GetGasMoles(FluidIds.Oxygen) &&
            restored.GetLiquidMoles(ShipFuel.LiquidFuelId) == local.GetLiquidMoles(ShipFuel.LiquidFuelId), "stored propellants survive inventory snapshot");
    }

    private static void ExhaustionPreservesRemainingFluidTemperature()
    {
        const decimal tail = .0000000000000000000000000001m;
        var configuration = new ShipPartConfiguration();
        var gas = Fuel(.04m + tail, .02m, 0m);
        Check(ShipFuel.TryBurn(gas, configuration, .02m, out _), "burn near-empty hydrogen oxygen fixture");
        Check(gas.GetGasMoles(FluidIds.Hydrogen) == tail && gas.GetGasMoles(FluidIds.Oxygen) == 0m, "keep exact unpaired hydrogen tail");
        Check(gas.State.TotalInternalEnergyJoules > 0d && Math.Abs(gas.GetTemperatureKelvin() - 293.15d) < 1e-8d,
            "near-empty fuel keeps positive energy and unchanged temperature");
        Check(new FluidInventory(gas.State.Clone()).GetGasMoles(FluidIds.Hydrogen) == tail, "near-empty fuel survives cold reconstruction");
        var liquid = Fuel(0m, 2m, .1m + tail);
        Check(ShipFuel.TryBurn(liquid, configuration, 2m, out _) && liquid.GetLiquidMoles(ShipFuel.LiquidFuelId) == tail,
            "keep exact liquid fuel remainder");
        Check(new FluidInventory(liquid.State.Clone()).GetTemperatureKelvin() > 0d, "near-empty liquid survives cold reconstruction");
        var vent = Fuel(20m, 0m, tail);
        vent.ClearGas(out _);
        Check(vent.GetLiquidMoles(ShipFuel.LiquidFuelId) == tail && new FluidInventory(vent.State.Clone()).GetTemperatureKelvin() > 0d,
            "venting gas preserves tiny liquid thermal state");
        var odd = Fuel(.0703443967748270505658671563m, .0351721983874135252829335782m, 0m);
        Check(ShipFuel.ConsumeStoredChemicalPropellants(odd, configuration, 1d, 1d) > 0d, "odd decimal hydrogen does not round recipe above real stock");
        Check(new FluidInventory(odd.State.Clone()).GetTemperatureKelvin() > 0d, "odd decimal reaction preserves remaining thermal state");
    }
    #endregion

    #region GM模板的真实端口图
    private static void Template()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Assets/Resources/Config/Mechanical/mechanical-catalog.json")))
            root = Directory.GetParent(root)?.FullName ?? throw new IOException("repository root not found");
        var catalog = JObject.Parse(File.ReadAllText(Path.Combine(root, "Assets/Resources/Config/Mechanical/mechanical-catalog.json")));
        var definitions = catalog["Nodes"].Cast<JObject>().ToDictionary(value => (string)value["Id"]);
        var structures = JObject.Parse(File.ReadAllText(Path.Combine(root, "Assets/StreamingAssets/GameConfig/Structures/fixed-structures.json")));
        var template = structures["structures"].Single(value => (string)value["id"] == "gm:small-test-ship");
        var nodes = new List<MachineEntity>(); int sequence = 100;
        foreach (var stamp in template["items"])
        {
            string id = (string)stamp["itemId"];
            if (!definitions.TryGetValue(id, out var definition) || definition["Fluid"] == null) continue;
            for (int y = 0; y < ((int?)stamp["height"] ?? 1); y++)
            for (int x = 0; x < ((int?)stamp["width"] ?? 1); x++)
                nodes.Add(new MachineEntity { Id = ++sequence, Cell = new((int)stamp["x"] + x, (int)stamp["y"] + y),
                    RotationQuarterTurns = (int?)stamp["quarterTurns"] ?? 0, Definition = new MachineDefinition { Id = id,
                        Layer = (int?)definition["PlacementLayer"] ?? 0,
                        Fluid = definition["Fluid"].ToObject<FluidMachineDefinition>() } });
        }
        var graph = Graph(nodes.ToArray()); var engines = nodes.Where(value => value.Definition.Fluid.Kind == "receiver").ToArray();
        Check(engines.Length == 6, "GM has six receiver engines");
        foreach (var engine in engines)
        {
            var intake = engine.Definition.Fluid.Ports.Single(port => port.Phase == "gas");
            Check(engine.Definition.Fluid.PullFromNetwork && engine.Definition.Fluid.GasCapacityStandardLiters == 1000, "engine owns and refills actual gas buffer");
            Check(engine.Definition.Fluid.Ports.Any(port => port.Phase == "liquid" && port.FluidId == ShipFuel.LiquidFuelId && port.Direction != intake.Direction), "engine uses independent cells for gas and liquid pipes");
            Vector2Int cell = engine.Cell + MechanicalNetworkGraph.Directions[(intake.Direction + engine.RotationQuarterTurns) & 3];
            Check(graph.PipeAt(cell) != null, $"engine {engine.Cell} intake has physical pipe");
            var routes = Routes(graph, engine);
            Check(routes.Nodes.Any(value => value.Node.Definition.Fluid.Kind == "tank"), $"engine {engine.Cell} reaches actual tank through formal gas graph");
        }
    }
    #endregion

    private static void Main()
    {
        Connectivity(); AtomicFuelAndBudgets(); DualFuelReactions(); DualPhaseRefilling(); ExhaustionPreservesRemainingFluidTemperature(); Template();
        Console.WriteLine($"SHIP_FUEL_OK assertions={assertions}");
    }
}
