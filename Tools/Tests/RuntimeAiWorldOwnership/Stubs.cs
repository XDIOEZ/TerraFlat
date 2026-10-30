using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;

// 只模拟宿主能力，AI 地址归属逻辑直接编译游戏中的正式源码。
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object left, Object right)
        {
            bool leftNull = ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = ReferenceEquals(right, null) || right.Destroyed;
            return leftNull && rightNull || !leftNull && !rightNull && ReferenceEquals(left, right);
        }
        public static bool operator !=(Object left, Object right) => !(left == right);
        public override bool Equals(object value) => ReferenceEquals(this, value);
        public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponentInChildren<T>(bool includeInactive) where T : class => gameObject.Get<T>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : class => gameObject.GetAll<T>();
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> output) where T : class
            => output.AddRange(gameObject.GetAll<T>());
    }
    public class Transform : Object
    {
        public GameObject gameObject;
        public Vector2 position;
    }
    public struct Vector2 { public float x; public float y; }
    public sealed class GameObject : Object
    {
        private readonly List<object> components = new();
        public GameObject(string name = null)
        {
            transform = new Transform { gameObject = this };
            scene = new Scene { handle = 1, name = "shared-scene", isLoaded = true };
        }
        public Transform transform;
        public Scene scene;
        public void Add(object value)
        {
            components.Add(value);
            if (value is MonoBehaviour component) component.gameObject = this;
        }
        public T Get<T>() where T : class
        {
            foreach (object value in components) if (value is T match) return match;
            return null;
        }
        public T[] GetAll<T>() where T : class
        {
            var result = new List<T>();
            foreach (object value in components) if (value is T match) result.Add(match);
            return result.ToArray();
        }
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public int handle;
        public string name;
        public bool isLoaded;
        public bool IsValid() => handle != 0;
        public static bool operator ==(Scene left, Scene right) => left.handle == right.handle;
        public static bool operator !=(Scene left, Scene right) => !(left == right);
        public override bool Equals(object value) => value is Scene scene && this == scene;
        public override int GetHashCode() => handle;
    }
    public static class SceneManager { public static void MoveGameObjectToScene(GameObject obj, Scene scene) => obj.scene = scene; }
}

namespace UnityEngine.Pool
{
    public static class ListPool<T>
    {
        public static IDisposable Get(out List<T> list) { list = new List<T>(); return new Cleanup(); }
        private sealed class Cleanup : IDisposable { public void Dispose() { } }
    }
}

namespace FlatWorld.WorldModel
{
    public readonly struct WorldAddress : IEquatable<WorldAddress>
    {
        public readonly int X;
        public WorldAddress(int x) => X = x;
        public bool Equals(WorldAddress other) => X == other.X;
        public override bool Equals(object other) => other is WorldAddress address && Equals(address);
        public override int GetHashCode() => X;
        public static bool operator ==(WorldAddress left, WorldAddress right) => left.Equals(right);
        public static bool operator !=(WorldAddress left, WorldAddress right) => !left.Equals(right);
    }
    public sealed class WorldRuntime
    {
        public WorldRuntime(string worldId) => WorldId = worldId;
        public string WorldId { get; }
        public long Epoch { get; set; } = 1;
    }
}

public sealed class ModuleData { public string ID; public string Name; }
public class ItemData { public string IDName; public int Guid; public Dictionary<string, ModuleData> ModuleDataDic; }
public class Item : MonoBehaviour { public ItemData itemData; }
public sealed class Player : Item { }
public sealed class Map : Item { }
public sealed class Mod_ItemChunkAssigner : MonoBehaviour { }
public interface IAIActor { }
public sealed class AiMarker : MonoBehaviour, IAIActor { }
public static class MonsterManager { public static bool IsRegisteredSpeciesId(string id) => false; }
public sealed class RuntimeItemDefinition { public bool IsActor; }
public sealed class GameRes : MonoBehaviour
{
    public static GameRes Instance => null;
    public bool TryGetItemDefinition(string id, out RuntimeItemDefinition result) { result = null; return false; }
}
public sealed class ItemReferenceComparer : IEqualityComparer<Item>
{
    public static readonly ItemReferenceComparer Instance = new();
    public bool Equals(Item left, Item right) => ReferenceEquals(left, right);
    public int GetHashCode(Item value) => RuntimeHelpers.GetHashCode(value);
}

public sealed class ChunkMgr
{
    public static ChunkMgr ExistingInstance => GameplayWorldRegistry.ExecutionChunks;
    public WorldRuntime WorldRuntime;
    public WorldAddress ResolveWorldAddress(Vector2 position) => new((int)Math.Floor(position.x / 16));
}
public sealed class GameplayWorldRuntime
{
    public string Key;
    public Scene Scene;
    public ChunkMgr Chunks;
}
public static class GameplayWorldRegistry
{
    private static readonly Dictionary<string, GameplayWorldRuntime> worlds = new();
    private static readonly Dictionary<Item, string> itemWorlds = new(ItemReferenceComparer.Instance);
    private static GameplayWorldRuntime current;
    public static bool HasExecutionContext => current != null;
    public static string ExecutionWorldKey => current?.Key;
    public static string GetItemWorldKey(Item item) => !ReferenceEquals(item, null) && itemWorlds.TryGetValue(item, out string key) ? key : null;
    public static void AssignItemToWorld(Item item, string key) => itemWorlds[item] = key;
    public static ChunkMgr ExecutionChunks => current?.Chunks;
    public static Scene CurrentScene => current?.Scene ?? default;
    public static void Add(GameplayWorldRuntime world) => worlds.Add(world.Key, world);
    public static void Remove(string key) => worlds.Remove(key);
    public static bool TryGet(string key, out GameplayWorldRuntime world) => worlds.TryGetValue(key ?? "", out world);
    public static WorldExecutionScope UseWorld(string key)
    {
        var old = current;
        current = worlds[key];
        return new WorldExecutionScope(old);
    }
    public struct WorldExecutionScope : IDisposable
    {
        private readonly GameplayWorldRuntime previous;
        private bool active;
        public WorldExecutionScope(GameplayWorldRuntime previous) { this.previous = previous; active = true; }
        public void Dispose() { if (!active) return; active = false; current = previous; }
    }
}

public sealed class FakeRuntimeRegistry
{
    private readonly HashSet<Item> items = new(ItemReferenceComparer.Instance);
    public bool Contains(Item item) => items.Contains(item);
    public void Add(Item item) => items.Add(item);
    public void Remove(Item item) => items.Remove(item);
}
public partial class ItemMgr
{
    private readonly FakeRuntimeRegistry _runtimeRegistry = new();
    private readonly HashSet<Item> _runtimeDespawning = new(ItemReferenceComparer.Instance);
    public readonly Dictionary<int, Item> WorldRunTimeItems = new();
    private IEnumerable<Item> RuntimeItems => WorldRunTimeItems.Values;
    public event Action<Item> RuntimeItemDespawning;
    private void RefreshItemSpatialIndex(Item item) { }
    private void UnregisterRuntimeItem(Item item)
    {
        _runtimeRegistry.Remove(item);
        if (!ReferenceEquals(item, null) && item.itemData != null) WorldRunTimeItems.Remove(item.itemData.Guid);
        RemoveRuntimeAiEntity(item);
    }
    public bool TestRegister(Item item)
    {
        _runtimeRegistry.Add(item);
        WorldRunTimeItems.Add(item.itemData.Guid, item);
        return TryRegisterRuntimeAiEntity(item);
    }
    public void TestMove(Item item) => RefreshRuntimeAiAddress(item);
    public void TestUnregister(Item item) => UnregisterRuntimeItem(item);
    public void TestCleanup() => CleanupRuntimeAiIndex();
}
