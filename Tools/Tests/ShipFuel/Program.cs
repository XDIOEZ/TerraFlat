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
            Fluid = new() { Kind = kind, Ports = ports, ThroughputLitersPerSecond = 1000, SingleGas = false } } };
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
        Check(ShipFuel.TryBurn(inventory, 1m, out double energy) && energy == 483600d, "burn real fuel atomically");
        Check(inventory.GetGasMoles(FluidIds.Hydrogen) == 18m && inventory.GetGasMoles(FluidIds.Oxygen) == 9m, "burn consumes exactly 2H:1O");
        Check(!ShipFuel.TryBurn(inventory, 10m, out _) && inventory.GetGasMoles(FluidIds.Hydrogen) == 18m, "insufficient oxygen leaves hydrogen intact");
        inventory.State.Ports.Add(new FluidOutputReservation { PortId = "reserved", FluidId = FluidIds.Hydrogen, GasMoles = 18m });
        Check(!ShipFuel.TryBurn(inventory, 1m, out _), "reserved fuel cannot be burned again");
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
        Check(ShipFuel.TryBurn(inventory, 1m, out _), "consume budget fixture fuel");
        MachineWorld.ConsumeFluidSupplyBudget(routes, source, recipe);
        double used = (double)FluidUnits.MolToStandardLiters(3m);
        Check(Math.Abs(MachineWorld.GetFluidSupplyStandardLiters(routes, source, 1d, true) - (1000d - used)) < 1e-9, "burn deducts shared budget along all nodes");
        Check(MachineWorld.GetFluidSupplyStock(routes, source, FluidIds.Oxygen, FluidPhase.Gas) == 8m, "burn deducts frozen source oxygen stock");
        AddGas(inventory, FluidIds.Hydrogen, 2m); AddGas(inventory, FluidIds.Oxygen, 1m);
        Check(MachineWorld.GetFluidSupplyStock(routes, source, FluidIds.Oxygen, FluidPhase.Gas) == 8m, "new gas does not pass frozen stock in same machine step");
        MachineWorld.SetFluidTransferBudget(MachineWorld.GetFluidInventory(pipe), 0d);
        Check(MachineWorld.GetFluidSupplyStandardLiters(routes, source, 1d, true) == 0d, "shared exhausted pipe blocks subsequent engine draws");
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
            var intake = engine.Definition.Fluid.Ports.Single();
            Vector2Int cell = engine.Cell + MechanicalNetworkGraph.Directions[(intake.Direction + engine.RotationQuarterTurns) & 3];
            Check(graph.PipeAt(cell) != null, $"engine {engine.Cell} intake has physical pipe");
            var routes = Routes(graph, engine);
            Check(routes.Nodes.Any(value => value.Node.Definition.Fluid.Kind == "tank"), $"engine {engine.Cell} reaches actual tank through formal gas graph");
        }
    }
    #endregion

    private static void Main()
    {
        Connectivity(); AtomicFuelAndBudgets(); Template();
        Console.WriteLine($"SHIP_FUEL_OK assertions={assertions}");
    }
}
