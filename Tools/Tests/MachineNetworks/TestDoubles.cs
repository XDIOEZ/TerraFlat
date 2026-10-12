using UnityEngine;

namespace UnityEngine
{
    #region 离线数学与坐标
    public readonly record struct Vector2Int(int x, int y)
    {
        public static Vector2Int right => new(1, 0);
        public static Vector2Int up => new(0, 1);
        public static Vector2Int left => new(-1, 0);
        public static Vector2Int down => new(0, -1);
        public static Vector2Int zero => new(0, 0);
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
    }
    public readonly record struct Vector3Int(int x, int y, int z);
    public readonly record struct Vector3(float x, float y, float z);
    public readonly struct BoundsInt
    {
        public readonly int xMin, yMin;
        public readonly Vector3Int size;
        public BoundsInt(int x, int y, int z, int width, int height, int depth)
        { xMin = x; yMin = y; size = new(width, height, depth); }
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Abs(float a) => MathF.Abs(a);
        public static float Floor(float a) => MathF.Floor(a);
        public static int FloorToInt(float a) => (int)MathF.Floor(a);
        public static float Sign(float a) => a >= 0 ? 1 : -1;
        public static int Clamp(int a, int minimum, int maximum) => Math.Clamp(a, minimum, maximum);
        public static float Clamp(float a, float minimum, float maximum) => Math.Clamp(a, minimum, maximum);
        public static float Clamp01(float a) => Math.Clamp(a, 0f, 1f);
    }
    #endregion
}
namespace Unity.Profiling
{
    public readonly struct ProfilerMarker
    {
        private readonly string name;
        public static readonly Dictionary<string, int> Counts = new();
        public ProfilerMarker(string name) { this.name = name; }
        public Sample Auto() { Counts[name] = Counts.GetValueOrDefault(name) + 1; return default; }
        public readonly struct Sample : IDisposable { public void Dispose() { } }
    }
}
namespace FlatWorld.Spaceflight
{
    public sealed class SpaceSession
    {
        public static SpaceSession Current => null;
        public Vector3 ProjectMachinePosition(MachineEntity node) => default;
    }
}

#region 非测试目标依赖替身
public sealed class ItemData { public TransformData transform = new(); }
public sealed class TransformData { public Vector3 position; }
public sealed class RecipeProcessor { }
public sealed class MachineLogic { }
public sealed class Mod_MechanicalNode { }
public sealed class MachineState { public float ManualSeconds, Hp = 100f, ElectricalStoredJoules; }
public readonly struct WorldTopologyDomain
{
    public int NormalizeX(int value) => value;
    public int NormalizeY(int value) => value;
}
public sealed class MechanicalSettings
{
    public int DeactivationChunks = 3, ActivationChunks = 2;
    public float UnloadDelaySeconds = 1, ReferenceRpm = 60, WattsPerTorqueRpm = 1;
}
public static class MachineCatalog { public static MechanicalSettings Settings { get; } = new(); }
public sealed class MachineDefinition
{
    public string Kind = "shaft", Ports = "all", Source = "", PortMode = "relay";
    public bool HasMechanicalPorts = true;
    public int Layer;
    public float Torque, Rpm = 60, TorqueLoad, ManualDriveTorque, ManualDriveRpm = 60;
    public int SourceRotationDirection = 1;
    public ElectricalDefinition Electrical;
    public object Fluid;
    public TransportDefinition Transport;
    public float[] Ratios = { 1f, 2f };
    public bool IsConverter => Electrical?.IsConverter == true;
    public static bool Positive(float value) => float.IsFinite(value) && value > 0f;
    public string GetPortMode() => PortMode;
    public bool HasAxlePort(int direction) => true;
    public void GetTransmission(bool smallInput, int ratio, out float speed, out float torque)
    { speed = smallInput ? .5f : 2f; torque = 1f / speed; }
}
public sealed class TransportDefinition { public bool SideDrive; }
public sealed class ElectricalDefinition
{
    public string Role = "wire", PowerProvider = "", DemandProvider = "", Connection = "cell";
    public float ConversionEfficiency = 1f, PowerWatts = 120, NominalVoltage = 120,
        MinimumVoltage, MaximumVoltage, CapacityJoules = 1000, MaxDischargeWatts = 100, MaxChargeWatts = 100, MaxCurrentAmps = 1000;
    public bool HasConnection => true;
    public bool IsWire => Role == "wire";
    public bool IsGenerator => Role == "generator";
    public bool IsConsumer => Role == "consumer";
    public bool IsBattery => Role == "battery";
    public bool IsConverter => Role == "converter";
    public Vector2Int GetConnectionOffset(int rotation) => Vector2Int.zero;
    public bool AcceptsVoltage(float voltage) => IsWire ? MaximumVoltage <= 0 || voltage <= MaximumVoltage + .001f
        : voltage + .001f >= (MinimumVoltage > 0 ? MinimumVoltage : NominalVoltage) &&
          voltage <= (MaximumVoltage > 0 ? MaximumVoltage : NominalVoltage) + .001f;
}
public sealed class FluidDeviceBehavior
{
    public bool SwitchesMechanicalPorts, SwitchesElectricalPorts, FloorsSourceTorque, HasMechanicalSource;
    public bool CanCacheElectricalDemandWithinStep;
}
public sealed partial class MachineEntity
{
    public float TestSourceFactor = 1f;
    public int TestSourceCalls;
    public ConveyorPath ConveyorRoute = new();
}
public sealed class ConveyorPath
{
    public bool HasPathPort(int direction) => true;
    public bool HasDrivePort(int direction) => true;
}
public sealed partial class MechanicalNetworkGraph
{
    public readonly List<MachineEntity> ConveyorChanges = new();
    private void RebuildConveyorPaths(List<MachineEntity> nodes) { }
}
public static partial class MachineWorld
{
    private sealed class ScopeState { public ElectricalNetworkGraph Electrical; public int TopologyMutationDepth = 0; }
    private static readonly ScopeState scope = new();
    private static MechanicalNetworkGraph graph;
    private static readonly Dictionary<int, MachineEntity> nodes = new();
    private static readonly Dictionary<string, Func<MachineEntity, float>> sourceProviders = new();
    private static readonly Dictionary<string, Func<MachineEntity, float>> sourceRpmProviders = new();
    private static bool dirty;
    public static uint TransportStep;
    public static event Action<Vector2Int> CellChanged;
    private static void NotifySurfaceCellChanged(Vector2Int cell) => CellChanged?.Invoke(cell);
    public static FluidDeviceBehavior GetFluidDeviceBehavior(MachineEntity node) => null;
    public static float GetSourceFactor(MachineEntity node)
    {
        node.TestSourceCalls++;
        if (node.Definition.IsConverter) return GetConverterMotorFactor(node);
        if (node.Definition.ManualDriveTorque > 0) return !node.IncomingPower && node.State.ManualSeconds > 0 ? 1f : 0f;
        return node.Definition.Source == "electric" ? node.ElectricalPowerRatio : node.TestSourceFactor;
    }
    public static float GetSourceRpm(MachineEntity node) => node.Definition.Rpm * node.Definition.SourceRotationDirection;
    public static float GetWorkEfficiency(MachineEntity node) => node.SpeedRpm / node.Definition.Rpm;
    public static bool Contains(MachineEntity node) => nodes.ContainsKey(node.Id);
    public static void StateChanged(MachineEntity node) { }
    private static void EnsureScope() { }
    private static void RebuildFluidNetworks() { }
    private static void RebuildCombatSearchPadding() { }
    private static void UpdateConveyorVisualSpeed(MachineEntity node) { }
    private static float GetFluidElectricalDemand(MachineEntity node) => 1f;
    public static void SetWorld(MechanicalNetworkGraph mechanical, ElectricalNetworkGraph electrical, IEnumerable<MachineEntity> entities)
    {
        graph = mechanical; scope.Electrical = electrical; nodes.Clear();
        foreach (MachineEntity node in entities) nodes.Add(node.Id, node);
        foreach (MechanicalNetwork network in graph.Networks) network.Active = true;
        TransportStep++;
    }
    public static void SettleCoupled(float seconds) => SolveElectricalNetworks(seconds);

    // 保留优化前的耦合步骤作为差分参考，只替换外部Unity依赖，不复制新缓存实现。
    public static void SettleCoupledReference(float seconds)
    {
        bool hasConverters = graph.Networks.Any(network => network.HasConverters);
        if (!hasConverters)
        {
            foreach (MechanicalNetwork network in graph.Networks) if (network.Active) SolveNetwork(network);
            SettleElectricalNetworks(seconds, true);
        }
        else
        {
            var resolved = new HashSet<MechanicalNetwork>();
            var ancestry = new Dictionary<ElectricalNetwork, HashSet<ElectricalNetwork>>();
            foreach (MachineEntity node in nodes.Values)
            { node.ConversionMode = ElectricalConversionMode.Idle; node.ElectricalPowerRatio = 0; node.ElectricalSuppliedWatts = 0; }
            foreach (MechanicalNetwork network in graph.Networks)
            {
                if (!network.Active) continue;
                SolveNetwork(network, true);
                bool independent = network.Nodes.Any(node => node.SourceFactor > 0);
                foreach (MachineEntity node in network.Nodes)
                    if (node.Definition.IsConverter)
                        node.ConversionMode = independent ? ElectricalConversionMode.Generator : ElectricalConversionMode.Motor;
                if (!independent) continue;
                resolved.Add(network); SolveNetwork(network, true);
            }
            int depth = 0;
            for (; depth <= graph.Networks.Count; depth++)
            {
                SettleElectricalNetworks(seconds, false);
                bool advanced = false;
                foreach (MechanicalNetwork network in graph.Networks)
                {
                    if (!network.Active || !network.HasConverters || resolved.Contains(network)) continue;
                    var inputs = new HashSet<ElectricalNetwork>();
                    foreach (MachineEntity node in network.Nodes)
                        if (node.Definition.IsConverter && node.ElectricalSuppliedWatts > 0)
                        { inputs.Add(node.ElectricalNetwork); if (ancestry.TryGetValue(node.ElectricalNetwork, out var parents)) inputs.UnionWith(parents); }
                    if (inputs.Count == 0) continue;
                    foreach (MachineEntity node in network.Nodes)
                    {
                        if (!node.Definition.IsConverter || node.ElectricalSuppliedWatts > 0) continue;
                        node.ConversionMode = inputs.Contains(node.ElectricalNetwork) ? ElectricalConversionMode.Idle : ElectricalConversionMode.Generator;
                        if (node.ConversionMode != ElectricalConversionMode.Generator) continue;
                        if (!ancestry.TryGetValue(node.ElectricalNetwork, out var parents)) ancestry[node.ElectricalNetwork] = parents = new();
                        parents.UnionWith(inputs);
                    }
                    resolved.Add(network); SolveNetwork(network); advanced = true;
                }
                if (!advanced) break;
            }
            for (int pass = 0; pass <= depth; pass++)
            { SettleElectricalNetworks(seconds, false); foreach (MechanicalNetwork network in graph.Networks) if (network.Active) SolveNetwork(network); }
            SettleElectricalNetworks(seconds, true);
        }
        foreach (MechanicalNetwork network in graph.Networks) if (network.Active) SolveNetwork(network);
    }
}
#endregion
