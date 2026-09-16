using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 正式 AI 运行时后端契约。GamePlay 只依赖该抽象，不反向引用 AIECS 程序集；
/// 生态生成器继续负责时间、群系、光照和预算规则，具体实体生命周期交给已注册后端。
/// </summary>
public interface IAiEcologyBackend
{
    bool IsReady { get; }
    bool PrepareWorld(IReadOnlyList<SpawnerConfig> configs);
    void ResetWorld();
    bool SupportsSpecies(string speciesId);
    bool TrySpawn(SpawnerConfig config, SpawnerConfig.SpawnEntry entry, Vector3 position);
    bool TrySpawnEvent(string speciesId, Vector3 position);
    int GetGroupCount(SpawnerConfig config);
    int GetSpeciesCount(string speciesId);
    int PopulationLimitedCount { get; }
    int CountGroupWithinRadius(SpawnerConfig config, Vector3 center, float radiusSqr);
    void RecycleDistantPopulation(IReadOnlyList<Vector3> playerPositions, float now);
}

/// <summary>
/// AI 后端全局路由。默认启用 Entities；旧 AI 只在显式切回 Legacy 时进入 Module Tick。
/// 后端注册采用单实例所有权，避免开发入口和正式世界同时驱动两套 ECS World。
/// </summary>
public static class AiRuntimeBackendService
{
    private static IAiEcologyBackend _ecology;

    public static bool UseEntities { get; set; } = true;
    public static IAiEcologyBackend Ecology => _ecology;

    /// <summary>
    /// 尝试取得正式生态后端所有权。场景切换时 GameStartScene 会短暂创建第二份 WorldManager，
    /// 新副本必须保持静默、不可抢占仍存活的跨场景后端；已被 Unity 销毁的旧宿主则允许正常接管。
    /// </summary>
    public static bool TryRegisterEcology(IAiEcologyBackend backend)
    {
        if (backend == null)
            throw new ArgumentNullException(nameof(backend));

        if (_ecology is UnityEngine.Object unityObject && unityObject == null)
            _ecology = null;

        if (_ecology != null && !ReferenceEquals(_ecology, backend))
            return false;

        _ecology = backend;
        return true;
    }

    public static void RegisterEcology(IAiEcologyBackend backend)
    {
        if (!TryRegisterEcology(backend))
            throw new InvalidOperationException("AI 生态后端已被另一个实例注册。");
    }

    public static void UnregisterEcology(IAiEcologyBackend backend)
    {
        if (ReferenceEquals(_ecology, backend))
            _ecology = null;
    }
}
