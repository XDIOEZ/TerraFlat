using System;
using System.Collections.Generic;

#region 独立进程的 Unity 与资源边界替身
namespace MemoryPack
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class MemoryPackIgnoreAttribute : Attribute { }
}
namespace UnityEngine
{
    public sealed class GameObject
    {
        public Module Module;
        public T GetComponentInChildren<T>(bool includeInactive) where T : class => Module as T;
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 Scale(Vector2 a, Vector2 b) => new(a.x * b.x, a.y * b.y);
        public static implicit operator Vector2(Vector3 a) => new(a.x, a.y);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 one => new(1f, 1f, 1f);
        public static Vector3 Scale(Vector3 a, Vector3 b) => new(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
    }
    public struct Bounds { public Vector3 min, size, center; }
    public sealed class Sprite { public Bounds bounds = new() { size = Vector3.one }; }
    public static class Mathf
    {
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
    }
}
public class Module { }
public sealed class AuxiliaryModule : Module { }
public sealed class RuntimeItemModuleDefinition
{
    public bool Enabled = true;
    public string PrefabId, ParametersJson;
}
public sealed class RuntimeItemDefinition
{
    public string Id;
    public readonly List<RuntimeItemModuleDefinition> ModuleDefinitions = new();
}
public sealed class GameRes
{
    public static GameRes ExistingInstance;
    public readonly Dictionary<string, RuntimeItemDefinition> Definitions = new();
    public readonly Dictionary<string, UnityEngine.GameObject> Prefabs = new();
    public int Lookups;
    public bool TryGetItemDefinition(string id, out RuntimeItemDefinition value)
    { Lookups++; return Definitions.TryGetValue(id, out value); }
    public UnityEngine.GameObject GetPrefab(string id, bool ignored)
    { Lookups++; return Prefabs.TryGetValue(id, out var value) ? value : null; }
}
#endregion

#region 独立快照与作物图像边界替身
public class ModuleData { public string ID; }
public sealed class Ex_ModData_MemoryPackable : ModuleData
{
    public byte[] BitData;
    private object value;
    public T GetData<T>() => (T)value;
    public void WriteData<T>(T state) { value = state; BitData = new byte[] { 1 }; }
}
public sealed class ItemData { public readonly Dictionary<string, ModuleData> ModuleDataDic = new(); }
public static class MachinePersistence { public static T Clone<T>(T value) => value; }
public sealed class CultivatorVisualConfiguration
{
    public UnityEngine.Vector2[] PlotAnchors = { new(.5f, .5f) };
    public UnityEngine.Vector2 PlotSize = new(.25f, .25f);
}
public sealed class Mod_Cultivator : Module
{
    public const string ModuleId = "cultivator";
    public CultivatorVisualConfiguration Visual = new();
}
public sealed class CultivatorState { public List<UnityEngine.Sprite> Plots = new(); }
public sealed class CultivatorLogic
{
    public CultivatorState State;
    public bool TryGetPlotVisual(int index, out UnityEngine.Sprite sprite, out float growth, out float scale)
    { growth = 0f; return TryGetPlotVisual(State, index, out sprite, out scale); }
    public static bool TryGetPlotVisual(CultivatorState state, int index, out UnityEngine.Sprite sprite, out float scale)
    {
        scale = 1f;
        sprite = state?.Plots != null && (uint)index < state.Plots.Count ? state.Plots[index] : null;
        return sprite != null;
    }
}
public sealed class MachineEntity
{
    public MachineDefinition Definition;
    public ItemData Snapshot;
    public object Logic;
    public bool Active;
}
#endregion
