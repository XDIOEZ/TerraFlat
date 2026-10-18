using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestActorWorldRuntimeProbe
{
    #region 统一生物出生现场

    private static void RegisterCreatureCreation()
    {
        GameplayTestRuntimeProbe.Register("ai", "creature_creation", CaptureCreatureCreation);
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
}
