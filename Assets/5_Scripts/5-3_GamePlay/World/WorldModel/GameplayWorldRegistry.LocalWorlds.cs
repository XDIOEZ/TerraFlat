using System;
using FlatWorld.WorldModel;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayWorldRegistry
{
    #region 会话局部坐标世界
    public static bool UsesLocalCoordinates(string worldKey)
        => worldKey != null && worlds.TryGetValue(worldKey, out var world) && world.UsesLocalCoordinates;

    public static bool SimulatesWithoutObservers(string worldKey)
        => worldKey != null && (worlds.TryGetValue(worldKey, out var world) && world.SimulateWithoutObservers ||
            spaceContexts.TryGetValue(worldKey, out var context) && context.SimulateWithoutObservers);
    public static ulong ItemOwnershipRevision => worldItems.Revision;

    public static bool GetPresentationRequiredForceRenderingOff(Renderer renderer, bool originalFallback = false)
    {
        if (renderer == null || !PresentationOwners.TryGetValue(renderer, out var owner) || owner == null)
            return originalFallback;
        string visibleWorld = LocalPresentationWorldKey;
        return TryResolveWorldKeyForScene(renderer.gameObject.scene, out string worldKey) && worldKey != visibleWorld ||
            owner.GetOriginalForceRenderingOff(renderer);
    }

    internal static GameplayWorldRuntime CreateLocalWorld(string worldKey, IChunkPureGenerator generator, int chunkSize)
    {
        if (TryGet(worldKey, out var existing))
        {
            if (!existing.UsesLocalCoordinates) throw new InvalidOperationException("世界身份已用于其他世界。");
            return existing;
        }
        if (releasing || string.IsNullOrWhiteSpace(worldKey) || generator == null || chunkSize <= 0)
            throw new ArgumentException("局部世界的身份或生成设置无效。");
        BindSceneCache();
        Scene scene = WorldPhysicsScene2D.CreateIsolatedScene(worldKey);
        var world = new GameplayWorldRuntime { WorldKey = worldKey, Scene = scene, OwnsPhysics = true,
            UsesLocalCoordinates = true, SimulateWithoutObservers = true };
        WorldRuntime model = GetOrCreateOwnedWorldModel(worldKey, 1);
        worlds.Add(worldKey, world);
        worldSceneBindings.BindWorldToScene(scene.handle, worldKey, world);
        worldIdentities.GetOrCreateRuntimeWorldIdentity(worldKey);
        try
        {
            _ = world.Physics;
            world.Chunks = ChunkMgr.CreateNetworkWorldManager(worldKey);
            using (UseWorld(worldKey)) world.Chunks.AttachLocalWorld(model, generator, chunkSize);
            return world;
        }
        catch
        {
            if (world.Chunks != null)
            {
                world.Chunks.DetachAfterPresentationSceneUnloaded();
                UnityEngine.Object.Destroy(world.Chunks.gameObject);
            }
            ReleaseWorldPhysics(worldKey);
            worldSceneBindings.UnbindWorldFromScene(scene.handle, worldKey, world);
            worlds.Remove(worldKey);
            ReleaseOwnedWorldModel(worldKey, model);
            UnloadLocalWorldScene(scene);
            throw;
        }
    }

    internal static void ReleaseLocalWorld(string worldKey)
    {
        if (!worlds.TryGetValue(worldKey ?? string.Empty, out var world) || !world.UsesLocalCoordinates) return;
        if (GetWorldItemCount(worldKey) != 0)
            throw new InvalidOperationException("局部世界仍有物品，必须先移交或卸载：" + worldKey);
        using (UseWorld(worldKey))
        {
            MonsterSpawnerManager.ReleaseWorld(worldKey);
            world.Chunks?.ReleaseLocalWorldHost();
            DroppedItemService.ReleaseWorld();
            WorldLiquidFlowObstacles.ClearWorld();
            FlatWorld.NaturalEntities.NaturalEntityEcsService.ReleaseWorld();
        }
        WorldEntityRuntime.ReleaseWorld(worldKey);
        WorldNavigationManager.ReleaseWorld(worldKey);
        BuildingOccupancyRegistry.ReleaseWorld(worldKey);
        ReleaseWorldPhysics(worldKey);
        worldSceneBindings.UnbindWorldFromScene(world.Scene.handle, worldKey, world);
        worlds.Remove(worldKey);
        if (world.Chunks != null) UnityEngine.Object.Destroy(world.Chunks.gameObject);
        if (TryGetOwnedWorldModel(worldKey, out var model)) ReleaseOwnedWorldModel(worldKey, model);
        UnloadLocalWorldScene(world.Scene);
    }

    private static void UnloadLocalWorldScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(scene))
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            else UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            return;
        }
#endif
        SceneManager.UnloadSceneAsync(scene);
    }
    #endregion
}
