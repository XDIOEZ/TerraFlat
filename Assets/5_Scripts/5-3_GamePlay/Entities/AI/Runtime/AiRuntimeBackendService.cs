using System;
using System.Collections.Generic;
using UnityEngine;

#region ECS 生物后端契约

/// <summary>单个生态物种使用的 AI 运行时后端。</summary>
public enum AiRuntimeBackendKind
{
    Unsupported = 0,
    Entities = 1
}

/// <summary>蜂巢与 ECS 成员交换的持久状态，不暴露 Entity 或旧鸟类模块。</summary>
[Serializable]
public struct AiHiveActorState
{
    public float Satiety;
    public float Anger;
    public bool Angry;
    public bool ReturningHome;
    public bool Orphaned;
}

/// <summary>事件和 MOD 向 ECS 生物下发的推进目标。</summary>
public readonly struct AIAdvanceCommand
{
    public int TargetItemGuid { get; }
    public Vector3 TargetPosition { get; }
    public float ArrivalDistance { get; }
    public bool AttackActorsOnRoute { get; }

    public AIAdvanceCommand(int targetItemGuid, Vector3 targetPosition,
        float arrivalDistance, bool attackActorsOnRoute)
    {
        TargetItemGuid = targetItemGuid;
        TargetPosition = targetPosition;
        ArrivalDistance = Mathf.Max(0.05f, arrivalDistance);
        AttackActorsOnRoute = attackActorsOnRoute;
    }
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
    /// <summary>玩家实例卸载前解除后端引用，但保留当前世界的生态运行态。</summary>
    void ReleasePlayer(Player player);
    bool SupportsSpecies(string speciesId);
    bool TrySpawn(SpawnerConfig config, SpawnerConfig.SpawnEntry entry, Vector3 position);
    bool TrySpawnEvent(string speciesId, Vector3 position);
    bool TrySpawnEvent(string speciesId, Vector3 position, out int actorGuid);
    bool TrySpawnDirect(string speciesId, Vector3 position, int requestedGuid, out int actorGuid);
    bool TrySetAdvanceCommand(int actorGuid, AIAdvanceCommand command);
    bool TryClearAdvanceCommand(int actorGuid);
    bool TryGetActor(int actorGuid, out Vector3 position, out bool alive);
    bool TryDespawnActor(int actorGuid);
    bool TryBindHiveActor(int actorGuid, int hiveGuid, Vector2 home, float patrolRadius, AiHiveActorState state);
    bool TryGetHiveActorState(int actorGuid, out AiHiveActorState state);
    bool TrySetHiveActorDirective(int actorGuid, bool returnHome, float alert, Vector2 defensePosition, bool hasDefenseTarget);
    bool TryReleaseHiveActor(int actorGuid, Vector2 formerHome, Vector2 defensePosition, bool hasDefenseTarget);
    bool TryCollectHiveHoney(int actorGuid, Vector2 home, float arrivalRadius,
        float minimumSatiety, float contributionCost);
    bool TryGiveHiveHoney(int actorGuid, Vector2 home, float arrivalRadius,
        float feedBelow, float mealGain);
    void CaptureResidents(MonsterSpawnerSaveData destination);
    int GetGroupCount(SpawnerConfig config);
    int GetSpeciesCount(string speciesId);
    int ResidentCount { get; }
    int PopulationLimitedCount { get; }
    int CountGroupWithinRadius(SpawnerConfig config, Vector3 center, float radiusSqr);
}

#endregion

#region ECS 物种路由

/// <summary>
/// AI 后端路由。正式 Actor 目录统一归属 Entities，生成配置只决定何时何地出生；
/// 后端注册采用单实例所有权，避免开发入口和正式世界同时驱动两套 ECS World。
/// </summary>
public static class AiRuntimeBackendService
{
    private static IAiEcologyBackend _ecology;
    private static readonly HashSet<string> EntitySpecies = new(StringComparer.OrdinalIgnoreCase);

    public static bool UseEntities { get; set; } = true;
    public static IAiEcologyBackend Ecology => _ecology;
    public static bool HasEntitiesRoutes => EntitySpecies.Count > 0;

    /// <summary>当前已注册的 Actor 统一交给 ECS；普通生成器和蜂巢等直接生成入口使用同一归属。</summary>
    public static void ConfigureRoutes(IReadOnlyList<SpawnerConfig> configs)
    {
        if (configs == null)
        {
            EntitySpecies.Clear();
            return;
        }

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

                if (entry.RuntimeBackend != AiRuntimeBackendKind.Entities ||
                    GameRes.Instance == null || !GameRes.Instance.ActorDefinitions.ContainsKey(speciesId))
                    throw new InvalidOperationException(
                        $"生态条目 {speciesId} 必须是已注册的 ECS Actor，后端={entry.RuntimeBackend}。");
                entitySpecies.Add(speciesId);
            }
        }

        // 普通生态规则未列出的 Actor（例如蜂巢成员）也必须归入同一 ECS 后端。
        if (GameRes.Instance != null)
            foreach (string speciesId in GameRes.Instance.ActorDefinitions.Keys)
                entitySpecies.Add(speciesId);

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

    /// <summary>物种归属是唯一权威，生成规则不能把已迁移 Actor 送回旧 GameObject 后端。</summary>
    public static bool UsesEntities(SpawnerConfig.SpawnEntry entry)
    {
        return entry != null && UsesEntities(entry.PrefabName);
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

#endregion
