using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class WorldNavigationManager
{
    #region 世界导航实例路由
    private static readonly Dictionary<string, WorldNavigationManager> worldManagers = new(StringComparer.Ordinal);
    private static WorldNavigationManager localManager;
    private string ownedWorldKey;
    private bool ownsWorldObject;
    public new static WorldNavigationManager Instance => ResolveWorldManager() ??
        (GameplayWorldRegistry.TryGet(GameplayWorldRegistry.ExecutionWorldKey, out _)
            ? GetOrCreateForWorld(GameplayWorldRegistry.ExecutionWorldKey) : SingletonAutoMono<WorldNavigationManager>.Instance);
    public static WorldNavigationManager ExistingInstance => ResolveWorldManager();

    private static WorldNavigationManager ResolveWorldManager()
    {
        string world = GameplayWorldRegistry.ExecutionWorldKey;
        if (worldManagers.TryGetValue(world, out WorldNavigationManager manager) && manager != null) return manager;
        if (GameplayWorldRegistry.HasExecutionContext) return null;
        return localManager != null && (string.IsNullOrEmpty(localManager.activeWorldKey) || localManager.activeWorldKey == world)
            ? localManager : null;
    }

    // 每个世界独立持有格网、目标场和请求，观察场景切换不清空其他世界的导航。
    public static WorldNavigationManager GetOrCreateForWorld(string worldKey)
    {
        if (worldManagers.TryGetValue(worldKey, out WorldNavigationManager manager) && manager != null) return manager;
        WorldNavigationManager local = localManager ?? SingletonAutoMono<WorldNavigationManager>.Instance;
        if (local != null && string.IsNullOrEmpty(local.ownedWorldKey) &&
            (string.IsNullOrEmpty(local.activeWorldKey) || local.activeWorldKey == worldKey)) manager = local;
        else
        {
            WorldNavigationManager previous = instance;
            WorldNavigationManager previousLocal = localManager;
            try
            {
                instance = null;
                var root = new GameObject("WorldNavigation / " + worldKey);
                PersistentRuntimeObjectHierarchy.MoveToPersistentCategory(root, PersistentRuntimeObjectCategory.World);
                manager = root.AddComponent<WorldNavigationManager>();
                manager.ownsWorldObject = true;
                if (local != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(local), manager);
            }
            finally { instance = previous; localManager = previousLocal; }
        }
        manager.ownedWorldKey = worldKey;
        manager.activeWorldKey = worldKey;
        worldManagers[worldKey] = manager;
        return manager;
    }

    public GameplayWorldRegistry.WorldExecutionScope UseWorldContext() => string.IsNullOrEmpty(ownedWorldKey) ? default : GameplayWorldRegistry.UseWorld(ownedWorldKey);

    internal bool TryUseExistingWorldForRelease(out GameplayWorldRegistry.WorldExecutionScope scope)
    {
        scope = default;
        return string.IsNullOrEmpty(ownedWorldKey) ||
            GameplayWorldRegistry.TryUseExistingWorldForOwnerRelease(ownedWorldKey, this, out scope);
    }

    public static void ReleaseWorld(string worldKey)
    {
        if (!worldManagers.Remove(worldKey, out WorldNavigationManager manager) || manager == null) return;
        manager.OnGameWorldExit();
        manager.ownedWorldKey = null;
        if (manager.ownsWorldObject) Destroy(manager.gameObject);
    }
    #endregion
}
