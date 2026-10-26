using UnityEngine;

namespace UnityEngine
{
    #region 离线格坐标
    public readonly record struct Vector2Int(int x, int y)
    {
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
    }
    #endregion
}
namespace MemoryPack
{
    public sealed class MemoryPackableAttribute : Attribute { }
    public sealed class MemoryPackIgnoreAttribute : Attribute { }
}
namespace FlatWorld.Spaceflight
{
    public enum ShipPieceKind { Floor, Wall, Door, DockingPort, Console, Engine, Equipment, GravityGenerator, Cultivator, Navigation, Wire, Pipe, Fiber }
}

#region 非测试目标依赖替身
public sealed class FluidCatalog
{
    private readonly Dictionary<string, FluidDefinition> definitions = new(StringComparer.Ordinal);
    public static FluidCatalog Default { get; } = new();
    public IReadOnlyDictionary<string, FluidDefinition> Definitions => definitions;
    public FluidCatalog()
    {
        foreach (string id in new[] { FluidIds.Hydrogen, FluidIds.Oxygen, FluidIds.Nitrogen, FlatWorld.Spaceflight.ShipFuel.LiquidFuelId })
            definitions.Add(id, new FluidDefinition(new FluidDefinitionDto { Id = id, DisplayName = id,
                MolarMassKgPerMol = .03, LiquidDensityKgPerLiter = 1, GasHeatCapacityJPerMolKelvin = 20,
                LiquidHeatCapacityJPerMolKelvin = 40 }));
    }
    public bool TryGet(string id, out FluidDefinition value) => definitions.TryGetValue(id, out value);
    public FluidDefinition Find(string id) => definitions[id];
}
public sealed class MachineDefinition
{
    public string Id;
    public int Layer;
    public FluidMachineDefinition Fluid;
    public static bool Positive(float value) => float.IsFinite(value) && value > 0;
}
public sealed class MachineEntity
{
    public int Id;
    public string ScopeKey = "ship:test";
    public Vector2Int Cell;
    public int RotationQuarterTurns;
    public bool Active = true;
    public MachineDefinition Definition;
    public readonly FluidMachineState FluidState = new();
}
public sealed class FluidMachineState
{
    public bool Ruptured;
    public string RuptureBudgetId = "";
    public bool ValveOpen = true;
    public string OutletPhase = "both";
    public string SelectedGasId = FluidIds.Hydrogen;
}
public class FluidDeviceBehavior
{
    private readonly string kind;
    public FluidDeviceBehavior(string kind) => this.kind = kind;
    public bool IsTransit => kind is "pipe" or "valve" or "selector";
    public bool IsSharedStorage => kind == "tank";
    public bool SwitchesMechanicalPorts => false;
    public bool SwitchesElectricalPorts => false;
    public bool EmitsToEnvironment => false;
    public bool PortsEnabled(MachineEntity node) => kind != "valve" || node.FluidState.ValveOpen;
    public bool CanOutput(MachineEntity node, out string reason) { reason = null; return true; }
    public bool CanRoute(MachineEntity node, FluidMachinePortDefinition port, FluidPhase phase, string id, out string reason)
    {
        reason = null;
        return kind != "selector" || phase == FluidPhase.Gas &&
            (id == node.FluidState.SelectedGasId) == (port.Id == "selected");
    }
}
public sealed class FluidSelectorBehavior : FluidDeviceBehavior
{
    public FluidSelectorBehavior() : base("selector") { }
}
public static class MechanicalNetworkGraph
{
    public static readonly Vector2Int[] Directions = { new(1, 0), new(0, 1), new(-1, 0), new(0, -1) };
}
public static partial class MachineWorld
{
    public sealed class FluidTankGroup { public readonly List<MachineEntity> Members = new(); }
    private static readonly Dictionary<int, FluidTankGroup> fluidTankOwners = new();
    private static readonly Dictionary<(int, string), FluidInventory> inventories = new();
    private static readonly Dictionary<FluidInventory, FluidInventoryState> stock = new();
    private static readonly Dictionary<FluidInventory, double> budgets = new();
    private static readonly Dictionary<FluidInventory, double> gasBudgets = new();
    private static readonly Dictionary<(int, string), double> portBudgets = new();
    private sealed class ScopeBudgets
    {
        public readonly Dictionary<int, double> GasNodeBudget = new(), LiquidNodeBudget = new();
    }
    private static readonly ScopeBudgets scope = new();
    private static FluidNetworkGraph fluidGraph;
    public static FluidMachineState GetFluidState(MachineEntity node) => node.FluidState;
    public static FluidDeviceBehavior GetFluidDeviceBehavior(MachineEntity node) => node.Definition.Fluid.Kind == "selector"
        ? new FluidSelectorBehavior() : new FluidDeviceBehavior(node.Definition.Fluid.Kind);
    public static FluidInventory GetFluidInventory(MachineEntity node, string chamber = "main")
    {
        var key = (node.Id, chamber);
        if (!inventories.TryGetValue(key, out FluidInventory inventory)) inventories.Add(key, inventory = new());
        return inventory;
    }
    public static void SetGraph(FluidNetworkGraph value) => fluidGraph = value;
    public static FluidInventory GetPhaseFluidInventory(MachineEntity node, string chamber, FluidPhase phase) => GetFluidInventory(node, chamber);
    public static double GetIndustrialThroughput(MachineEntity node) => node.Definition.Fluid.ThroughputLitersPerSecond;
    public static double GetFluidVolumeLiters(MachineEntity node) => node.Definition.Fluid.VolumeLiters;
    public static double GetFluidGasCapacityStandardLiters(MachineEntity node) => node.Definition.Fluid.GasCapacityStandardLiters;
    public static double GetIndustrialFreeCapacity(MachineEntity node, FluidPhase phase, string id)
        => IndustrialFluidTransfer.FreeCapacity(GetFluidInventory(node), phase, GetFluidGasCapacityStandardLiters(node), GetFluidVolumeLiters(node), id);
    public static void SupplyForTest(MachineEntity node, float seconds) => SupplyIndustrialConsumer(node, seconds);
    private static void RebuildGraphsIfDirty() { }
    public static IDisposable UseNodeScope(MachineEntity node) => new ScopeLease();
    private sealed class ScopeLease : IDisposable { public void Dispose() { } }
    public static void Freeze(IEnumerable<MachineEntity> nodes, double seconds)
    {
        stock.Clear(); budgets.Clear(); gasBudgets.Clear(); portBudgets.Clear();
        scope.GasNodeBudget.Clear(); scope.LiquidNodeBudget.Clear();
        foreach (MachineEntity node in nodes)
        {
            FluidInventory inventory = GetFluidInventory(node);
            stock[inventory] = inventory.State.Clone();
            budgets[inventory] = node.Definition.Fluid.ThroughputLitersPerSecond * seconds;
            gasBudgets[inventory] = budgets[inventory];
            scope.GasNodeBudget[node.Id] = scope.LiquidNodeBudget[node.Id] = budgets[inventory];
            foreach (var port in node.Definition.Fluid.Ports) portBudgets[(node.Id, port.Id)] = budgets[inventory];
        }
    }
    public static void Reset()
    {
        inventories.Clear(); stock.Clear(); budgets.Clear(); gasBudgets.Clear(); portBudgets.Clear(); fluidTankOwners.Clear();
        scope.GasNodeBudget.Clear(); scope.LiquidNodeBudget.Clear();
    }
    internal static bool TryGetFluidTransferBudget(FluidInventory inventory, out FluidInventoryState value, out double budget)
        => stock.TryGetValue(inventory, out value) & budgets.TryGetValue(inventory, out budget);
    internal static void SetFluidTransferBudget(FluidInventory inventory, double budget) => budgets[inventory] = budget;
    internal static bool TryGetGasTransferBudget(FluidInventory inventory, out FluidInventoryState value, out double budget)
        => stock.TryGetValue(inventory, out value) & gasBudgets.TryGetValue(inventory, out budget);
    internal static void SetGasTransferBudget(FluidInventory inventory, double budget) => gasBudgets[inventory] = budget;
    private static double GasPortBudget(MachineEntity node, FluidMachinePortDefinition port, float seconds)
        => portBudgets.GetValueOrDefault((node.Id, port.Id));
    private static void ConsumeGasPort(MachineEntity node, FluidMachinePortDefinition port, double liters)
        => portBudgets[(node.Id, port.Id)] = Math.Max(0d, portBudgets.GetValueOrDefault((node.Id, port.Id)) - liters);
    internal static void ConsumeFluidStepStock(FluidInventory inventory, string id, decimal gas, decimal liquid)
    {
        var component = stock[inventory].Components.Find(value => value.FluidId == id);
        if (component == null) return;
        component.GasMoles = Math.Max(0m, component.GasMoles - gas);
        component.LiquidMoles = Math.Max(0m, component.LiquidMoles - liquid);
    }
    private static void StateChanged(MachineEntity node) { }
}
#endregion
