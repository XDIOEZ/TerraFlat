using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

public partial class MonsterSpawnerManager
{
    #region 生态调度器的世界归属
    private static readonly Dictionary<string, MonsterSpawnerManager> worldManagers = new(StringComparer.Ordinal);
    private static MonsterSpawnerManager templateManager;
    private string _ownedWorld;
    private bool _worldInitialized, _populationEventsBound;

    public static MonsterSpawnerManager Instance => GetForWorld(GameplayWorldRegistry.ExecutionWorldKey);
    public static MonsterSpawnerManager GetInstance() => Instance;
    public static MonsterSpawnerManager GetExistingInstance() => ExistingInstance;

    public static MonsterSpawnerManager GetForWorld(string world, bool create = true)
    {
        if (string.IsNullOrWhiteSpace(world)) return null;
        if (worldManagers.TryGetValue(world, out MonsterSpawnerManager manager) && manager != null) return manager;
        if (!create || !Application.isPlaying || GameManager.Instance?.IsInGameWorld != true) return null;
        manager = templateManager != null && string.IsNullOrEmpty(templateManager._ownedWorld) ? templateManager : null;
        if (manager == null)
        {
            var root = new GameObject("MonsterWorld / " + world);
            DontDestroyOnLoad(root);
            manager = root.AddComponent<MonsterSpawnerManager>();
        }
        manager._ownedWorld = world;
        worldManagers[world] = manager;
        manager._gameManager = GameManager.Instance;
        manager._monsterManager.BindWorld(world);
        manager.BindPopulationEvents();
        manager.enabled = true;
        using (manager.EnterOwnedWorld()) manager.EnsureWorldInitialized();
        return manager;
    }

    private IDisposable EnterOwnedWorld() => GameplayWorldRegistry.TryGet(_ownedWorld, out _)
        ? GameplayWorldRegistry.UseWorld(_ownedWorld) : null;

    private bool IsOwnedWorldReady => _gameManager != null && _gameManager.IsInGameWorld &&
        (_gameManager.IsGameplayReady || GameNetwork.IsOnline && GameNetwork.HasStateAuthority);

    private void EnsureWorldInitialized()
    {
        if (_worldInitialized || !IsOwnedWorldReady || !SpawnerConfigCatalogService.IsLoaded) return;
        _worldInitialized = true;
        OnGameWorldEnter();
    }

    private void BindPopulationEvents()
    {
        if (_populationEventsBound || _monsterManager == null) return;
        _populationEventsBound = true;
        _monsterManager.MonsterRegistered += OnMonsterRegistered;
        _monsterManager.MonsterUnregistered += OnMonsterUnregistered;
        _monsterManager.MonsterDeathStarted += OnMonsterDeathStarted;
    }

    private void HandleWorldEnter() => GetForWorld(GameplayWorldRegistry.ExecutionWorldKey)?.EnsureWorldInitialized();

    public static void CaptureAll(GameSaveData save)
    {
        foreach (MonsterSpawnerManager manager in worldManagers.Values)
            if (manager != null) manager.CaptureSaveData(save);
    }

    // 游戏总退出才清理所有生态实例，普通玩家迁移保留其它世界的居民和计划。
    public static void ReleaseAll()
    {
        foreach (MonsterSpawnerManager manager in new List<MonsterSpawnerManager>(worldManagers.Values))
        {
            if (manager == null) continue;
            using (manager.EnterOwnedWorld()) manager.OnGameWorldExit();
            manager._worldInitialized = false;
            manager._monsterManager.ResetWorld();
            manager._monsterManager.UnbindWorld();
            manager._ownedWorld = null;
            if (manager != templateManager) Destroy(manager.gameObject);
        }
        worldManagers.Clear();
    }
    #endregion
}
