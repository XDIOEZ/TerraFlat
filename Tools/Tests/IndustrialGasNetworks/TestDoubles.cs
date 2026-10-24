using System;
using System.Collections.Generic;

// 只隔离 Unity 坐标、设备行为入口；被测图、库存、组分和热力学均来自正式源码。
namespace UnityEngine
{
    public readonly struct Vector2
    {
        public readonly float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 one => new(1, 1);
        public static explicit operator Vector2(Vector2Int value) => new(value.x, value.y);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.x + b.x, a.y + b.y);
        public static Vector2 operator *(Vector2 value, float scalar) => new(value.x * scalar, value.y * scalar);
    }
    public readonly struct Vector2Int : IEquatable<Vector2Int>
    {
        public readonly int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Vector2Int other) => x == other.x && y == other.y;
        public override bool Equals(object value) => value is Vector2Int other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new(a.x + b.x, a.y + b.y);
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.Equals(b);
        public static bool operator !=(Vector2Int a, Vector2Int b) => !a.Equals(b);
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => MathF.Max(a, b);
        public static float Min(float a, float b) => MathF.Min(a, b);
        public static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
        public static float MoveTowards(float current, float target, float delta)
            => MathF.Abs(target - current) <= delta ? target : current + MathF.Sign(target - current) * delta;
    }
}

public static class TemperatureMgr { public const float DefaultAmbientTemperature = 20f; }
public sealed class FluidCatalog
{
    private readonly Dictionary<string, FluidDefinition> values = new(StringComparer.OrdinalIgnoreCase);
    public static FluidCatalog Default { get; } = new();
    public void Register(FluidDefinition value) => values[value.Id] = value;
    public bool TryGet(string id, out FluidDefinition value) => values.TryGetValue(id, out value);
    public FluidDefinition Find(string id) => values[id];
}

public sealed class MachineEntity
{
    public int Id, RotationQuarterTurns;
    public UnityEngine.Vector2Int Cell;
    public MachineDefinition Definition = new();
    public FluidMachineState State = new();
    public FluidInventory Local = new();
    public bool Active = true;
}
public sealed class MachineDefinition
{
    public int Layer = 2;
    public FluidMachineDefinition Fluid = new();
}
public sealed class FluidMachineDefinition
{
    public string Kind = "pipe", MaterialId = "iron";
    public double VolumeLiters = 10, MinimumGasSpaceLiters = .001, ThroughputLitersPerSecond = 5;
    public bool CombineAdjacent;
    public bool SingleLiquid;
    public bool SingleGas;
    public double GasBufferStandardLiters = 100, BatchStandardLiters = 100;
    public double MaximumPhaseMolesPerSecond = 10;
    public FluidMachinePortDefinition[] Ports = Array.Empty<FluidMachinePortDefinition>();
}
public sealed class FluidMachinePortDefinition
{
    public string Id = "", Chamber = "main", Mode = "both", Phase = "both", FluidId = "";
    public int Direction;
    public bool Outlet;
}
public sealed class FluidMachineState
{
    public bool Ruptured, ValveOpen = true;
    public string RuptureBudgetId = "", OutletPhase = "both";
    public string Status, LastFlowPhase;
    public int LastFlowDirection;
    public readonly Dictionary<string, string> BlockedPorts = new();
}
public class FluidDeviceBehavior
{
    public virtual bool IsTransit => true;
    public virtual bool EmitsToEnvironment => false;
    public virtual bool ReceivesOnDemand => false;
    public virtual bool SwitchesMechanicalPorts => false;
    public virtual bool SwitchesElectricalPorts => false;
    public virtual bool IsSharedStorage => false;
    public virtual bool PortsEnabled(MachineEntity node) => node.State.ValveOpen;
    public virtual bool CanOutput(MachineEntity node, out string reason) { reason = null; return true; }
    public virtual bool CanRoute(MachineEntity node, FluidMachinePortDefinition port, FluidPhase phase, string id, out string reason)
    { reason = null; return true; }
}
public static partial class MachineWorld
{
    internal static IndustrialGasNetworkGraph Gas;
    public static bool IsSharedGasNetworkMember(MachineEntity node) => Gas != null && Gas.TryGetGroup(node, out _);
    private static readonly FluidDeviceBehavior transit = new();
    private sealed class Device : FluidDeviceBehavior { public override bool IsTransit => false; }
    private sealed class Outlet : FluidDeviceBehavior { public override bool EmitsToEnvironment => true; }
    private static readonly FluidDeviceBehavior device = new Device(), outlet = new Outlet();
    public static FluidMachineState GetFluidState(MachineEntity node) => node.State;
    public static FluidDeviceBehavior GetFluidDeviceBehavior(MachineEntity node)
        => node.Definition.Fluid.Kind == "device" ? device : node.Definition.Fluid.Kind == "outlet" ? outlet : transit;
}
public static class MechanicalNetworkGraph
{
    public static readonly UnityEngine.Vector2Int[] Directions =
        { new(1, 0), new(0, 1), new(-1, 0), new(0, -1) };
}
