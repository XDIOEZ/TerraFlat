using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed partial class GameplayWorldRuntime
{
    #region 世界物理服务
    public WorldPhysicsService2D Physics => GameplayWorldRegistry.GetOrCreatePhysics(WorldKey, Scene);
    #endregion
}

public static partial class GameplayWorldRegistry
{
    #region 世界物理服务生命周期
    private static readonly Dictionary<string, WorldPhysicsService2D> worldPhysics = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, WorldTerrainPhysicsSource2D> terrainPhysicsSources = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, WorldMachinePhysicsSource2D> machinePhysicsSources = new(StringComparer.Ordinal);

    internal static bool TryGetTerrainPhysicsSource(string key, out WorldTerrainPhysicsSource2D source)
        => terrainPhysicsSources.TryGetValue(key ?? string.Empty, out source);

    internal static void BindWorldPhysicsSources(FlatWorld.WorldModel.WorldRuntime model, WorldPhysicsService2D physics)
    {
        if (!terrainPhysicsSources.TryGetValue(model.WorldId, out var terrain))
            terrainPhysicsSources.Add(model.WorldId, terrain = new WorldTerrainPhysicsSource2D(physics));
        terrain.Bind(model);
        if (machinePhysicsSources.TryGetValue(model.WorldId, out var machines)) machines.Bind(model);
    }

    internal static void UnbindWorldPhysicsSources(FlatWorld.WorldModel.WorldRuntime model)
    {
        if (terrainPhysicsSources.TryGetValue(model.WorldId, out var terrain)) terrain.Unbind(model);
        if (machinePhysicsSources.TryGetValue(model.WorldId, out var machines)) machines.Unbind(model);
    }

    private static void ReleaseWorldPhysicsSources(string key)
    {
        if (terrainPhysicsSources.Remove(key, out var terrain)) terrain.Dispose();
        if (machinePhysicsSources.Remove(key, out var machines)) machines.Dispose();
    }

    public static WorldPhysicsService2D CurrentPhysics
    {
        get
        {
            if (execution != null) return execution.Physics;
            string key = ExecutionWorldKey;
            if (TryGetPhysics(key, out var existing)) return existing;
            return GetOrCreatePhysics(key, CurrentScene);
        }
    }

    internal static WorldPhysicsService2D GetOrCreatePhysics(string worldKey, Scene scene)
    {
        if (worldPhysics.TryGetValue(worldKey, out var service))
        {
            if (service.IsValid && service.PrimarySpace.Scene == scene) return service;
            ReleaseWorldPhysicsSources(worldKey);
            service.Dispose();
        }
        var next = new WorldPhysicsService2D(worldKey, WorldPhysicsScene2D.Borrow(scene));
        worldPhysics[worldKey] = next;
        try
        {
            machinePhysicsSources.Add(worldKey, new WorldMachinePhysicsSource2D(next));
            if (TryGetOwnedWorldModel(worldKey, out var model)) BindWorldPhysicsSources(model, next);
        }
        catch { ReleaseWorldPhysicsSources(worldKey); worldPhysics.Remove(worldKey); next.Dispose(); throw; }
        if (host == null && Application.isPlaying && !releasing)
        {
            var root = new GameObject("GameplayWorldRegistry");
            PersistentRuntimeObjectHierarchy.MoveToPersistentCategory(root, PersistentRuntimeObjectCategory.World);
            host = root.AddComponent<GameplayWorldRegistryHost>();
        }
        return next;
    }

    public static bool TryGetPhysics(string worldKey, out WorldPhysicsService2D service)
    {
        service = null;
        return !string.IsNullOrEmpty(worldKey) && worldPhysics.TryGetValue(worldKey, out service) && service.IsValid;
    }

    internal static void RegisterItemPhysics(Item item)
    {
        string key = GetItemWorldKey(item);
        if (string.IsNullOrEmpty(key)) { WorldPhysicsService2D.ReleaseItem(item); return; }
        Scene scene = worlds.TryGetValue(key, out var world) ? world.Scene :
            spaceContexts.TryGetValue(key, out var space) ? space.Scene : SceneManager.GetSceneByName(key);
        if (!scene.IsValid() || !scene.isLoaded) scene = item.gameObject.scene;
        using var scope = TryGet(key, out _) ? UseWorld(key) : null;
        GetOrCreatePhysics(key, scene).RegisterItem(item);
    }

    private static void RequireAssignedItemPhysicsWorld(string worldKey)
    {
        if (TryGetPhysics(worldKey, out _)) return;
        Scene scene = SceneManager.GetSceneByName(worldKey);
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException($"物品迁移前必须先绑定目标世界的物理空间：{worldKey}");
        GetOrCreatePhysics(worldKey, scene);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSessionPhysicsServices() => ReleaseAllWorldPhysics();

    private static void ReleaseWorldPhysics(string worldKey)
    {
        ReleaseWorldPhysicsSources(worldKey);
        if (worldPhysics.Remove(worldKey, out var service)) service.Dispose();
    }

    internal static void ReleaseAllWorldPhysics()
    {
        foreach (var terrain in terrainPhysicsSources.Values) terrain.Dispose();
        foreach (var machines in machinePhysicsSources.Values) machines.Dispose();
        terrainPhysicsSources.Clear(); machinePhysicsSources.Clear();
        foreach (var service in worldPhysics.Values) service.Dispose();
        worldPhysics.Clear();
    }

    private static void ReleasePhysicsForScene(Scene scene)
    {
        using var scratch = UnityEngine.Pool.ListPool<string>.Get(out var keys);
        foreach (var pair in worldPhysics)
            if (pair.Value.PrimarySpace.Scene == scene) keys.Add(pair.Key);
        for (int i = 0; i < keys.Count; i++) ReleaseWorldPhysics(keys[i]);
    }
    #endregion
}
