using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestActorWorldRuntimeProbe
{
    #region 统一生物出生现场

    private static void RegisterCreatureCreation()
    {
        GameplayTestRuntimeProbe.Register("ai", "creature_creation", CaptureCreatureCreation);
        GameplayTestRuntimeProbe.RegisterAsync("ai", "incremental_creature_creation", CaptureIncrementalCreatureCreation);
    }

    // 在隔离世界调用正式管理器，采样身份、回滚、池复用和休眠恢复，业务断言交给 Lua。
    private static JObject CaptureCreatureCreation(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        MonsterSpawnerManager manager = MonsterSpawnerManager.ExistingInstance;
        if (manager == null || AiRuntimeBackendService.UsesEntities("Chicken"))
            throw new InvalidOperationException("本案例需要正式 GO 生物刷新管理器。");
        var sample = FindOpenCell(player);
        Vector3 position = WorldLocalPresentation.ProjectPosition((Vector2)sample.WorldCell + Vector2.one * .5f);
        var owned = new List<int>();
        ItemData snapshot = null;
        GameplayTestRuntimeProbe.RegisterCleanup(new Cleanup(() =>
        {
            foreach (int guid in owned)
            {
                if (SaveDataMgr.Instance.ContainsParkedRuntimeAi(guid) && snapshot?.Guid == guid)
                    SaveDataMgr.Instance.TryWakeParkedRuntimeAi(snapshot);
                AiRuntimeBackendService.TryDespawnActor(guid);
            }
        }));
        int births = manager.SuccessfulCreatureBirths;
        int restores = manager.SuccessfulCreatureRestores;
        int rollbacks = manager.RolledBackCreatureCreations;
        var result = new JObject();
        try
        {
            bool created = AiRuntimeBackendService.TrySpawnDirect("Chicken", position, 0, out int actorGuid);
            result["created"] = created;
            if (!created) return result;
            owned.Add(actorGuid);
            Item actor = ItemMgr.Instance.GetItemByGuid(actorGuid);
            result["initialized"] = AiRuntimeBackendService.TryGetGameObjectActor(actor, out IAIActor ai) && ai.IsAlive;
            result["birthDeltaAfterFirst"] = manager.SuccessfulCreatureBirths - births;
            result["duplicateRejected"] = !manager.TrySpawnCreature("Chicken", position, actorGuid, out int duplicate) && duplicate == 0;
            actor.Save();
            snapshot = FastCloner.FastCloner.DeepClone(actor.itemData);
            AiRuntimeBackendService.TryDespawnActor(actorGuid);

            bool beforeLoad = false, loadedDuringCallback = false;
            result["callbackRejected"] = !manager.TrySpawnCreature("Chicken", position, actorGuid, out int rejected,
                beforeLoad: candidate => beforeLoad = !candidate.IsInitialized,
                afterLoad: candidate => { loadedDuringCallback = candidate.IsInitialized; return false; }) && rejected == 0;
            result["beforeLoadUninitialized"] = beforeLoad;
            result["callbackLoaded"] = loadedDuringCallback;
            result["rollbackRemoved"] = ItemMgr.Instance.GetItemByGuid(actorGuid) == null;
            result["rollbackDelta"] = manager.RolledBackCreatureCreations - rollbacks;

            int replacement = 0;
            bool reusedShell = false;
            result["destroyedCallbackRejected"] = !manager.TrySpawnCreature("Chicken", position, actorGuid, out int invalid,
                afterLoad: candidate =>
                {
                    AiRuntimeBackendService.TryDespawnActor(actorGuid);
                    if (manager.TrySpawnCreature("Chicken", position, 0, out replacement))
                    {
                        owned.Add(replacement);
                        reusedShell = ReferenceEquals(candidate, ItemMgr.Instance.GetItemByGuid(replacement));
                    }
                    return true;
                }) && invalid == 0;
            result["replacementReusedShell"] = reusedShell;
            result["replacementSurvived"] = replacement != 0 &&
                AiRuntimeBackendService.TryGetActor(replacement, out _, out bool replacementAlive) && replacementAlive;

            result["restored"] = manager.TryRestoreCreature(snapshot, out Item restored);
            result["sameIdentity"] = restored != null && restored.itemData.Guid == actorGuid && restored.itemData.IDName == snapshot.IDName;
            result["birthsUnaffectedByRestore"] = manager.SuccessfulCreatureBirths == births + 2;
            if (restored != null)
            {
                restored.Save();
                snapshot = FastCloner.FastCloner.DeepClone(restored.itemData);
                result["parked"] = SaveDataMgr.Instance.TryParkRuntimeAi(restored);
                result["parkedIdentityReserved"] = SaveDataMgr.Instance.ContainsParkedRuntimeAi(actorGuid) &&
                    !manager.TrySpawnCreature("Chicken", position, actorGuid, out _);
                result["woke"] = SaveDataMgr.Instance.TryWakeParkedRuntimeAi(snapshot);
                result["wakeSameIdentity"] = ItemMgr.Instance.GetItemByGuid(actorGuid)?.itemData.Guid == actorGuid &&
                    !SaveDataMgr.Instance.ContainsParkedRuntimeAi(actorGuid);
            }
            result["restoreDelta"] = manager.SuccessfulCreatureRestores - restores;
            result["finalBirthDelta"] = manager.SuccessfulCreatureBirths - births;
            return result;
        }
        finally
        {
            foreach (int guid in owned)
            {
                if (SaveDataMgr.Instance.ContainsParkedRuntimeAi(guid) && snapshot?.Guid == guid)
                    SaveDataMgr.Instance.TryWakeParkedRuntimeAi(snapshot);
                AiRuntimeBackendService.TryDespawnActor(guid);
            }
            result["cleanupComplete"] = owned.TrueForAll(guid => ItemMgr.Instance.GetItemByGuid(guid) == null &&
                !SaveDataMgr.Instance.ContainsParkedRuntimeAi(guid));
        }
    }

    #endregion

    #region 分帧出生现场

    // 每次只推进一个正式创建步骤，采样跨帧交付、半途取消和光源惰性创建。
    private static IEnumerator CaptureIncrementalCreatureCreation(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        MonsterSpawnerManager manager = MonsterSpawnerManager.ExistingInstance;
        var sample = FindOpenCell(player);
        Vector3 position = WorldLocalPresentation.ProjectPosition((Vector2)sample.WorldCell + Vector2.one * .5f);
        Item created = null;
        int guid = 0;
        bool callbackInitialized = false;
        int firstFrame = Time.frameCount;
        int steps = 0;
        bool prematureDelivery = false;
        double deadline = Time.realtimeSinceStartupAsDouble + 30d;
        var result = new JObject();
        try
        {
            using (var creation = manager.SpawnCreatureInSteps("Chicken", position, candidate =>
                   {
                       created = candidate;
                       guid = candidate.itemData.Guid;
                       callbackInitialized = candidate.IsInitialized && candidate.gameObject.activeInHierarchy &&
                           ReferenceEquals(ItemMgr.Instance.GetItemByGuid(guid), candidate);
                   }))
                while (creation.MoveNext())
                {
                    steps++;
                    prematureDelivery |= created != null;
                    if (Time.realtimeSinceStartupAsDouble >= deadline) throw new TimeoutException("分帧生物创建超时。");
                    yield return null;
                }
            result["steps"] = steps;
            result["frames"] = Time.frameCount - firstFrame;
            result["created"] = guid != 0 && callbackInitialized;
            result["prematureDelivery"] = prematureDelivery;
            ActorBuffLightController light = created?.GetComponentInChildren<ActorBuffLightController>(true);
            result["hasLightController"] = light != null;
            result["noUnusedLight"] = light != null && light.RuntimeLight == null;
            if (light != null)
            {
                Mod_BuffManager buffs = created.itemMods.GetMod_ByID<Mod_BuffManager>(ModText.Mod_BuffManager);
                result["radianceAdded"] = buffs.AddBuff(ActorBuffLightController.RadianceBuffId);
                light.RefreshBuffVisualState();
                var firstLight = light.RuntimeLight;
                result["lightCreatedOnDemand"] = firstLight != null && light.IsLightActive;
                buffs.RemoveBuff(ActorBuffLightController.RadianceBuffId);
                light.RefreshBuffVisualState();
                result["lightDisabled"] = !light.IsLightActive;
                buffs.AddBuff(ActorBuffLightController.RadianceBuffId);
                light.RefreshBuffVisualState();
                result["lightReused"] = ReferenceEquals(firstLight, light.RuntimeLight) && light.IsLightActive;
            }
            AiRuntimeBackendService.TryDespawnActor(guid);
            int rollbackBefore = manager.RolledBackCreatureCreations;
            bool deliveredCancelled = false;
            using (var cancelled = manager.SpawnCreatureInSteps("Chicken", position, _ => deliveredCancelled = true))
            {
                while (cancelled.MoveNext())
                {
                    if (cancelled.Current == 1) break;
                    if (Time.realtimeSinceStartupAsDouble >= deadline) throw new TimeoutException("分帧取消现场超时。");
                    yield return null;
                }
            }
            result["cancelDidNotDeliver"] = !deliveredCancelled;
            result["cancelRollbackCount"] = manager.RolledBackCreatureCreations - rollbackBefore;
            int published = 0;
            Action<Item> observe = item =>
            {
                if (item.itemData?.IDName == "Chicken" && (item.transform.position - position).sqrMagnitude < .0001f)
                    published++;
            };
            ItemMgr.RuntimeItemInstantiated += observe;
            bool invalidDelivered = false;
            try
            {
                using var invalidated = manager.SpawnCreatureInSteps("Chicken", position, _ => invalidDelivered = true, canCommit: () => false);
                while (invalidated.MoveNext())
                {
                    if (Time.realtimeSinceStartupAsDouble >= deadline) throw new TimeoutException("分帧生物拒绝现场超时。");
                    yield return null;
                }
            }
            finally { ItemMgr.RuntimeItemInstantiated -= observe; }
            result["invalidCommitRejected"] = !invalidDelivered;
            result["invalidCommitNotPublished"] = published == 0;
        }
        finally
        {
            if (guid != 0) AiRuntimeBackendService.TryDespawnActor(guid);
        }
        result["cleanupComplete"] = guid != 0 && ItemMgr.Instance.GetItemByGuid(guid) == null;
        completed(result);
    }

    #endregion
}
