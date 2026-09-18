using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>单个生态物种使用的 AI 运行时后端。</summary>
public enum AiRuntimeBackendKind
{
    GameObject = 0,
    Entities = 1
}

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
/// AI 后端路由。全局开关只控制是否允许 Entities，具体物种由生成配置单独选择；
/// GameObject AI 与正式 AIECS 可以并行存在，但同一物种在同一世界只能归属一个后端。
/// 后端注册采用单实例所有权，避免开发入口和正式世界同时驱动两套 ECS World。
/// </summary>
public static class AiRuntimeBackendService
{
    private static IAiEcologyBackend _ecology;
    private static readonly HashSet<string> EntitySpecies = new(StringComparer.OrdinalIgnoreCase);

    public static bool UseEntities { get; set; } = true;
    public static IAiEcologyBackend Ecology => _ecology;
    public static bool HasEntitiesRoutes => EntitySpecies.Count > 0;

    /// <summary>按当前世界生成目录冻结物种后端；同一物种禁止配置成两个不同后端。</summary>
    public static void ConfigureRoutes(IReadOnlyList<SpawnerConfig> configs)
    {
        if (configs == null)
        {
            EntitySpecies.Clear();
            return;
        }

        var configured = new Dictionary<string, AiRuntimeBackendKind>(StringComparer.OrdinalIgnoreCase);
        var entitySpecies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int configIndex = 0; configIndex < configs.Count; configIndex++)
        {
            SpawnerConfig config = configs[configIndex];
            if (config?.SpawnEntries == null)
                continue;

            for (int entryIndex = 0; entryIndex < config.SpawnEntries.Count; entryIndex++)
            {
                SpawnerConfig.SpawnEntry entry = config.SpawnEntries[entryIndex];
                string speciesId = entry?.PrefabName?.Trim();
                if (string.IsNullOrWhiteSpace(speciesId))
                    continue;

                if (configured.TryGetValue(speciesId, out AiRuntimeBackendKind existing) &&
                    existing != entry.RuntimeBackend)
                {
                    throw new InvalidOperationException(
                        $"AI 物种 {speciesId} 同时配置了 {existing} 与 {entry.RuntimeBackend} 后端。");
                }

                configured[speciesId] = entry.RuntimeBackend;
                if (entry.RuntimeBackend == AiRuntimeBackendKind.Entities)
                    entitySpecies.Add(speciesId);
            }
        }

        EntitySpecies.Clear();
        EntitySpecies.UnionWith(entitySpecies);
    }

    /// <summary>世界退出时清除本轮物种路由，避免跨存档残留。</summary>
    public static void ClearRoutes()
    {
        EntitySpecies.Clear();
    }

    /// <summary>当前物种是否被明确路由到正式 AIECS；该归属不受临时启停开关影响。</summary>
    public static bool UsesEntities(string speciesId)
    {
        return !string.IsNullOrWhiteSpace(speciesId) && EntitySpecies.Contains(speciesId.Trim());
    }

    /// <summary>当前生成条目是否明确选择 AIECS。</summary>
    public static bool UsesEntities(SpawnerConfig.SpawnEntry entry)
    {
        return entry != null && entry.RuntimeBackend == AiRuntimeBackendKind.Entities;
    }

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
