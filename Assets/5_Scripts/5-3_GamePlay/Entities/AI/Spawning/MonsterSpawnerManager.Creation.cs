using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using UnityEngine;

public partial class MonsterSpawnerManager
{
    #region 统一生物出生事务

    private readonly HashSet<int> _creatingCreatureGuids = new();
    public int SuccessfulCreatureBirths { get; private set; }
    public int SuccessfulCreatureRestores { get; private set; }
    public int RolledBackCreatureCreations { get; private set; }

    private void ResetCreatureCreationDiagnostics()
    {
        // 只统计本轮世界进入后的事务，不把上个世界或维度的诊断混进来。
        SuccessfulCreatureBirths = SuccessfulCreatureRestores = RolledBackCreatureCreations = 0;
    }

    // 来源负责何时、何地出生，管理器只负责权威校验、唯一身份、实际创建与失败回收。
    internal static bool IsValidCreatureRequest(string speciesId, Vector3 position, bool authority, bool ready)
    {
        return authority && ready && !string.IsNullOrWhiteSpace(speciesId) &&
               IsFinite(position.x) && IsFinite(position.y) && IsFinite(position.z);
    }

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public bool TrySpawnCreature(string speciesId, Vector3 position, int requestedGuid, out int actorGuid,
        Action<Item> beforeLoad = null, Func<Item, bool> afterLoad = null,
        SpawnerConfig.SpawnEntry initialization = null)
    {
        return TryCreateCreature(speciesId, position, requestedGuid, false, out actorGuid,
            beforeLoad, afterLoad, initialization);
    }

    /// <summary>来源自己保存的居民恢复原身份，不能再算一次新出生。</summary>
    public bool TryRestoreResident(string speciesId, Vector3 position, int residentGuid, out int actorGuid,
        Func<Item, bool> afterLoad)
    {
        actorGuid = 0;
        return residentGuid != 0 && TryCreateCreature(speciesId, position, residentGuid, true,
            out actorGuid, null, afterLoad, null);
    }

    private bool TryCreateCreature(string speciesId, Vector3 position, int requestedGuid, bool restoring,
        out int actorGuid, Action<Item> beforeLoad, Func<Item, bool> afterLoad,
        SpawnerConfig.SpawnEntry initialization)
    {
        using var worldScope = EnterOwnedWorld();
        actorGuid = 0;
        if (!PrepareCreatureFactory() || !IsValidCreatureRequest(speciesId, position, GameNetwork.HasStateAuthority,
                IsOwnedWorldReady) ||
            GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetActorDefinition(speciesId, out _) ||
            (requestedGuid != 0 && IsCreatureIdentityOccupied(requestedGuid, false)))
            return false;

        if (AiRuntimeBackendService.UsesEntities(speciesId))
        {
            // 需要真实模块的来源不能偷偷绕过物种后端，也不能返回尚未完成来源绑定的实体。
            if (beforeLoad != null || afterLoad != null || !AiRuntimeBackendService.UseEntities ||
                AiRuntimeBackendService.Ecology?.IsReady != true)
                return false;
            int reservedGuid = requestedGuid != 0 ? requestedGuid : GenerateCreatureGuid();
            _creatingCreatureGuids.Add(reservedGuid);
            int createdGuid = 0;
            try
            {
                using (EntityCreationMarker.Auto())
                {
                    if (!AiRuntimeBackendService.Ecology.TryCreateActor(speciesId, position, reservedGuid,
                            initialization, out createdGuid)) return false;
                }
                if (createdGuid != reservedGuid ||
                    !AiRuntimeBackendService.Ecology.TryGetActor(createdGuid, out _, out bool alive) || !alive)
                    throw new InvalidOperationException($"生物 {speciesId} 的后端没有交付有效身份。");
                actorGuid = createdGuid;
                if (restoring) SuccessfulCreatureRestores++;
                else SuccessfulCreatureBirths++;
                return true;
            }
            catch (Exception exception)
            {
                if (createdGuid != 0) AiRuntimeBackendService.Ecology.TryDespawnActor(createdGuid);
                RolledBackCreatureCreations++;
                Debug.LogError($"[MonsterSpawnerManager] 生成 {speciesId} 失败：{exception}", this);
                return false;
            }
            finally { _creatingCreatureGuids.Remove(reservedGuid); }
        }

        ItemData data;
        try
        {
            data = GameRes.ExistingInstance.CreateItemData(speciesId);
            data.Guid = requestedGuid != 0 ? requestedGuid : GenerateCreatureGuid();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[MonsterSpawnerManager] 准备 {speciesId} 出生数据失败：{exception}", this);
            return false;
        }
        if (!TryMaterializeCreature(data, position, Quaternion.identity, Vector3.one, false,
                beforeLoad, afterLoad, initialization, out Item item)) return false;
        actorGuid = item.itemData.Guid;
        if (restoring) SuccessfulCreatureRestores++;
        else SuccessfulCreatureBirths++;
        return true;
    }

    /// <summary>恢复既有居民只物化快照，不消费出生计划或生态预算。</summary>
    public bool TryRestoreCreature(ItemData snapshot, out Item item)
    {
        item = null;
        if (!PrepareCreatureFactory() || snapshot?.transform == null || snapshot.Guid == 0 ||
            !IsValidCreatureRequest(snapshot.IDName, snapshot.transform.position, GameNetwork.HasStateAuthority,
                _gameManager != null && _gameManager.IsGameplayReady) ||
            GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetActorDefinition(snapshot.IDName, out _) ||
            AiRuntimeBackendService.UsesEntities(snapshot.IDName)) return false;
        if (!TryMaterializeCreature(snapshot, snapshot.transform.position, snapshot.transform.rotation,
                snapshot.transform.scale, true, null, null, null, out item)) return false;
        SuccessfulCreatureRestores++;
        return true;
    }

    private bool PrepareCreatureFactory()
    {
        // 关闭自然生态只停调度，不影响 GM、技能、孵化或蜂巢的显式出生请求。
        _itemManager = ItemMgr.GetInstance();
        return _itemManager != null && IsOwnedWorldReady;
    }

    private bool IsValidCreatedCreature(Item candidate, int guid, uint generation)
    {
        return candidate != null && candidate.itemData?.Guid == guid && candidate.RuntimeGeneration == generation &&
               AiRuntimeBackendService.TryGetGameObjectActor(candidate, out IAIActor actor) && actor.IsAlive &&
               ReferenceEquals(_itemManager.GetItemByGuid(guid), candidate);
    }

    private int GenerateCreatureGuid()
    {
        int guid;
        do { guid = _itemManager.GenerateGuid(); }
        while (guid == 0 || IsCreatureIdentityOccupied(guid, false));
        return guid;
    }

    private bool IsCreatureIdentityOccupied(int guid, bool restoring)
    {
        return _creatingCreatureGuids.Contains(guid) || _itemManager.GetItemByGuid(guid) != null ||
               (AiRuntimeBackendService.Ecology?.TryGetActor(guid, out _, out _) == true) ||
               (!restoring && SaveDataMgr.Instance?.ContainsParkedRuntimeAi(guid) == true);
    }

    private bool TryMaterializeCreature(ItemData data, Vector3 position, Quaternion rotation, Vector3 scale,
        bool restoring, Action<Item> beforeLoad, Func<Item, bool> afterLoad,
        SpawnerConfig.SpawnEntry initialization, out Item item)
    {
        item = null;
        int guid = data.Guid;
        if (guid == 0 || IsCreatureIdentityOccupied(guid, restoring)) return false;
        _creatingCreatureGuids.Add(guid);
        Item candidate = null;
        ItemData boundData = null;
        uint? loadedGeneration = null;
        bool committed = false;
        try
        {
            using (InstantiateItemMarker.Auto())
                candidate = _itemManager.InstantiateItem(data, position, rotation, scale);
            if (candidate == null) return false;
            boundData = candidate.itemData;
            loadedGeneration = candidate.RuntimeGeneration;
            beforeLoad?.Invoke(candidate);
            if (candidate == null || candidate.DestructionHandled || !ReferenceEquals(candidate.itemData, boundData) ||
                candidate.itemData?.Guid != guid || candidate.RuntimeGeneration != loadedGeneration.Value ||
                !ReferenceEquals(_itemManager.GetItemByGuid(guid), candidate)) return false;
            using (LoadCreatedItemMarker.Auto())
            {
                try { if (!candidate.IsInitialized) candidate.Load(); }
                finally { if (candidate != null) loadedGeneration = candidate.RuntimeGeneration; }
            }
            uint generation = candidate.RuntimeGeneration;
            loadedGeneration = generation;
            if (!IsValidCreatedCreature(candidate, guid, generation))
                throw new InvalidOperationException($"生物 {data.IDName} 没有交付已加载的有效 AI 或唯一身份。");
            using (SpawnInitializationMarker.Auto())
            {
                if (!restoring) ApplySpawnInitialization(candidate, initialization);
                if (afterLoad != null && !afterLoad(candidate)) return false;
            }
            if (!IsValidCreatedCreature(candidate, guid, generation)) return false;
            candidate.GetComponentInChildren<Mod_ItemDetector>(true)?.Update_Detector();
            if (!IsValidCreatedCreature(candidate, guid, generation)) return false;
            item = candidate;
            committed = true;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[MonsterSpawnerManager] 物化 {data.IDName} 失败：{exception}", this);
            return false;
        }
        finally
        {
            try
            {
                if (!committed && candidate != null && !candidate.DestructionHandled &&
                    ReferenceEquals(candidate.itemData, boundData) &&
                    (!loadedGeneration.HasValue || candidate.RuntimeGeneration == loadedGeneration.Value))
                {
                    _itemManager.DespawnItem(candidate, saveData: false);
                    RolledBackCreatureCreations++;
                }
            }
            finally { _creatingCreatureGuids.Remove(guid); }
        }
    }

    #endregion
}
