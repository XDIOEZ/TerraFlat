using System;
using System.Collections.Generic;
using UnityEngine;

#region ECS 生物后端契约

/// <summary>单个生态物种使用的 AI 运行时后端。</summary>
public enum AiRuntimeBackendKind
{
    Unsupported = -1,
    GameObject = 0,
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
/// AI 后端路由。默认使用 GameObject，只有生成配置显式指定的物种才交给 Entities；
/// 后端注册采用单实例所有权，避免开发入口和正式世界同时驱动两套 ECS World。
/// </summary>
public static class AiRuntimeBackendService
{
    private static IAiEcologyBackend _ecology;
    private static readonly HashSet<string> EntitySpecies = new(StringComparer.OrdinalIgnoreCase);

    public static bool UseEntities { get; set; } = false;
    public static IAiEcologyBackend Ecology => _ecology;
    public static bool HasEntitiesRoutes => EntitySpecies.Count > 0;

    /// <summary>按物种冻结后端，同一物种不能同时由 GameObject 和 ECS 接管。</summary>
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

                if (entry.RuntimeBackend != AiRuntimeBackendKind.Entities &&
                    entry.RuntimeBackend != AiRuntimeBackendKind.GameObject)
                    throw new InvalidOperationException(
                        $"AI 物种 {speciesId} 的后端无效：{entry.RuntimeBackend}。");
                if (configured.TryGetValue(speciesId, out AiRuntimeBackendKind existing) &&
                    existing != entry.RuntimeBackend)
                    throw new InvalidOperationException(
                        $"AI 物种 {speciesId} 同时配置了 {existing} 与 {entry.RuntimeBackend} 后端。");
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

    /// <summary>关闭 ECS 只暂停显式 ECS 物种，不改变其归属或偷偷创建第二套 AI。</summary>
    public static bool UsesEntities(SpawnerConfig.SpawnEntry entry)
    {
        return entry != null && UsesEntities(entry.PrefabName);
    }

    #region 跨后端生物入口

    /// <summary>GM、技能、事件与 MOD 共用出生入口，GameObject 必须完成 Load 后才能返回。</summary>
    public static bool TrySpawnDirect(string speciesId, Vector3 position, int requestedGuid, out int actorGuid)
    {
        actorGuid = 0;
        if (string.IsNullOrWhiteSpace(speciesId)) return false;
        if (UsesEntities(speciesId))
            return UseEntities && _ecology != null &&
                   _ecology.TrySpawnDirect(speciesId, position, requestedGuid, out actorGuid);

        ItemMgr manager = ItemMgr.Instance;
        if (manager == null || GameRes.Instance == null ||
            !GameRes.Instance.TryGetActorDefinition(speciesId, out _)) return false;
        if (requestedGuid != 0 && (manager.GetItemByGuid(requestedGuid) != null ||
            (_ecology != null && _ecology.TryGetActor(requestedGuid, out _, out _)))) return false;

        Item item = null;
        try
        {
            ItemData data = GameRes.Instance.CreateItemData(speciesId);
            if (requestedGuid != 0) data.Guid = requestedGuid;
            item = manager.InstantiateItem(data, position);
            if (item == null) return false;
            if (!item.IsInitialized) item.Load();
            if (!TryGetGameObjectActor(item, out _))
                throw new InvalidOperationException($"生物 {speciesId} 缺少已初始化的 GameObject AI。");
            item.GetComponentInChildren<Mod_ItemDetector>(true)?.Update_Detector();
            actorGuid = item.itemData.Guid;
            return true;
        }
        catch (Exception exception)
        {
            if (item != null && !item.DestructionHandled)
                manager.DespawnItem(item, saveData: false);
            Debug.LogError($"[AI] 生成 {speciesId} 失败：{exception}");
            return false;
        }
    }

    /// <summary>只认绑定到当前 Item 的真实 AI，镜像与未初始化外壳不算 GameObject 生物。</summary>
    public static bool TryGetGameObjectActor(Item item, out IAIActor actor)
    {
        actor = null;
        if (item == null || item.DestructionHandled || !item.IsInitialized) return false;
        foreach (MonoBehaviour behaviour in item.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour is IAIActor candidate && ReferenceEquals(candidate.ActorItem, item))
            { actor = candidate; return true; }
        return false;
    }

    public static bool TryGetActor(int actorGuid, out Vector3 position, out bool alive)
    {
        Item item = ItemMgr.Instance?.GetItemByGuid(actorGuid);
        if (TryGetGameObjectActor(item, out IAIActor actor))
        {
            position = WorldTopologyRuntime.NormalizePosition(item.transform.position);
            alive = actor.IsAlive;
            return true;
        }
        position = default;
        alive = false;
        return _ecology != null && _ecology.TryGetActor(actorGuid, out position, out alive);
    }

    public static bool TrySetAdvanceCommand(int actorGuid, AIAdvanceCommand command)
    {
        Item item = ItemMgr.Instance?.GetItemByGuid(actorGuid);
        if (TryGetGameObjectActor(item, out IAIActor actor))
        {
            if (!actor.IsAlive) return false;
            foreach (MonoBehaviour behaviour in item.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is IAIAdvanceCommandReceiver receiver)
                { receiver.BeginAdvance(command); return true; }
            return false;
        }
        return _ecology?.TrySetAdvanceCommand(actorGuid, command) == true;
    }

    public static bool TryClearAdvanceCommand(int actorGuid)
    {
        Item item = ItemMgr.Instance?.GetItemByGuid(actorGuid);
        if (TryGetGameObjectActor(item, out _))
        {
            foreach (MonoBehaviour behaviour in item.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is IAIAdvanceCommandCancellationReceiver receiver)
                { receiver.CancelAdvance(); return true; }
            return false;
        }
        return _ecology?.TryClearAdvanceCommand(actorGuid) == true;
    }

    public static bool TryDespawnActor(int actorGuid)
    {
        ItemMgr manager = ItemMgr.Instance;
        Item item = manager?.GetItemByGuid(actorGuid);
        if (TryGetGameObjectActor(item, out _))
        {
            manager.DespawnItem(item, saveData: false);
            return true;
        }
        return _ecology?.TryDespawnActor(actorGuid) == true;
    }

    #endregion

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
