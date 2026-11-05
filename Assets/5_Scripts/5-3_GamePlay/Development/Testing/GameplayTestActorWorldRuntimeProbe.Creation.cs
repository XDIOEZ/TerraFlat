using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        int chickenCountAtSpawnCell = CountRegisteredChickensAt(position);
        bool unpublishedWhileYielding = true;
        bool sceneValid = false, sceneLoaded = false, sceneMatchesOwner = false;
        bool physicsSceneValid = false, physicsSceneMatchesOwner = false;
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
                       Scene candidateScene = candidate.gameObject.scene;
                       Scene ownerScene = GameplayWorldRegistry.CurrentScene;
                       sceneValid = candidateScene.IsValid();
                       sceneLoaded = sceneValid && candidateScene.isLoaded;
                       sceneMatchesOwner = sceneValid && candidateScene == ownerScene;
                       if (sceneValid)
                       {
                           PhysicsScene2D candidatePhysicsScene = candidateScene.GetPhysicsScene2D();
                           PhysicsScene2D ownerPhysicsScene = GameplayWorldRegistry.CurrentPhysics.PrimarySpace.PhysicsScene;
                           physicsSceneValid = candidatePhysicsScene.IsValid();
                           physicsSceneMatchesOwner = physicsSceneValid && candidatePhysicsScene == ownerPhysicsScene;
                       }
                   }))
                while (creation.MoveNext())
                {
                    steps++;
                    prematureDelivery |= created != null;
                    unpublishedWhileYielding &= CountRegisteredChickensAt(position) == chickenCountAtSpawnCell;
                    if (Time.realtimeSinceStartupAsDouble >= deadline) throw new TimeoutException("分帧生物创建超时。");
                    yield return null;
                }
            result["steps"] = steps;
            result["frames"] = Time.frameCount - firstFrame;
            result["created"] = guid != 0 && callbackInitialized;
            result["prematureDelivery"] = prematureDelivery;
            result["unpublishedWhileYielding"] = unpublishedWhileYielding;
            result["sceneValid"] = sceneValid;
            result["sceneLoaded"] = sceneLoaded;
            result["sceneMatchesOwner"] = sceneMatchesOwner;
            result["physicsSceneValid"] = physicsSceneValid;
            result["physicsSceneMatchesOwner"] = physicsSceneMatchesOwner;
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
            var replacedWorld = new JObject();
            yield return ExerciseIncrementalCreatureWorldReplacement(manager, player, position, replacedWorld);
            result["replacedWorld"] = replacedWorld;
            GameObject fishPrefab = GameRes.ExistingInstance?.GetPrefab("SmallFish", logError: false);
            result["fishColdShellWithoutBody"] = fishPrefab != null && fishPrefab.GetComponent<Rigidbody2D>() == null;
            int fishGuid = 0;
            Item fish = null;
            try
            {
                double fishDeadline = Time.realtimeSinceStartupAsDouble + 30d;
                using (var fishCreation = manager.SpawnCreatureInSteps("SmallFish", position, candidate =>
                       {
                           fish = candidate;
                           fishGuid = candidate.itemData.Guid;
                       }))
                    while (fishCreation.MoveNext())
                    {
                        if (Time.realtimeSinceStartupAsDouble >= fishDeadline)
                            throw new TimeoutException("小鱼分帧出生超时。");
                        yield return null;
                    }
                Rigidbody2D fishBody = WorldPhysicsService2D.GetItemRigidbody(fish);
                result["fishBornWithMotionBody"] = fish != null && fish.IsInitialized &&
                    ItemMgr.Instance.GetItemByGuid(fishGuid) == fish && fishBody != null && fishBody.simulated &&
                    fishBody.bodyType != RigidbodyType2D.Static &&
                    fish.itemMods.GetMod_ByID<Mod_AquaticMovement>(Mod_AquaticMovement.ModuleId)?.IsRuntimeLoaded == true;
            }
            finally
            {
                if (fishGuid != 0) AiRuntimeBackendService.TryDespawnActor(fishGuid);
                result["fishCleaned"] = fishGuid != 0 && ItemMgr.Instance.GetItemByGuid(fishGuid) == null;
            }
        }
        finally
        {
            if (guid != 0) AiRuntimeBackendService.TryDespawnActor(guid);
        }
        result["cleanupComplete"] = guid != 0 && ItemMgr.Instance.GetItemByGuid(guid) == null;
        completed(result);
    }

    // 短暂替换隔离世界登记，验证跨帧旧候选不会交给同键的新场景。
    private static IEnumerator ExerciseIncrementalCreatureWorldReplacement(MonsterSpawnerManager manager,
        Player player, Vector3 position, JObject result)
    {
        string key = GameplayWorldRegistry.GetItemWorldKey(player);
        if (!GameplayWorldRegistry.TryGet(key, out GameplayWorldRuntime original))
            throw new InvalidOperationException("换代回归缺少当前世界。");
        if (!GameplayWorldRegistry.TryGetPhysics(key, out WorldPhysicsService2D originalPhysics))
            throw new InvalidOperationException("换代回归缺少当前世界物理服务。");
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var worlds = (Dictionary<string, GameplayWorldRuntime>)typeof(GameplayWorldRegistry)
            .GetField("worlds", flags).GetValue(null);
        var bindings = (FlatWorld.WorldModel.WorldSceneBindingIndex)typeof(GameplayWorldRegistry)
            .GetField("worldSceneBindings", flags).GetValue(null);
        int initialCount = CountRegisteredChickensAt(position);
        int rollbackBefore = manager.RolledBackCreatureCreations;
        int published = 0;
        bool delivered = false;
        Item deliveredCreature = null;
        ItemData deliveredData = null;
        uint deliveredGeneration = 0;
        Action<Item> observe = item =>
        {
            if (item.itemData?.IDName == "Chicken" && (item.transform.position - position).sqrMagnitude < .0001f)
                published++;
        };
        ItemMgr.RuntimeItemInstantiated += observe;
        Scene replacementScene = default;
        Item candidate = null;
        IEnumerator<int> creation = null;
        try
        {
            creation = manager.SpawnCreatureInSteps("Chicken", position, item =>
            {
                delivered = true;
                deliveredCreature = item;
                deliveredData = item.itemData;
                deliveredGeneration = item.RuntimeGeneration;
            });
            double deadline = Time.realtimeSinceStartupAsDouble + 30d;
            while (creation.MoveNext())
            {
                if (creation.Current == 1)
                {
                    if (candidate == null) candidate = FindUnregisteredCreatureCandidate(original.Scene, position);
                    if (candidate != null && HasLoadedCreatureModule(candidate)) break;
                }
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    throw new TimeoutException("世界换代前未取得分帧出生候选。");
                yield return null;
            }
            if (candidate == null || !HasLoadedCreatureModule(candidate))
                throw new InvalidOperationException("世界换代前没有部分加载的候选。");
            int candidateGuid = candidate.itemData.Guid;
            replacementScene = WorldPhysicsScene2D.CreateIsolatedScene("GameplayTest_CreatureReplacement_" + Guid.NewGuid().ToString("N"));
            var replacement = new GameplayWorldRuntime { WorldKey = key, Scene = replacementScene };
            bindings.BindWorldToScene(replacementScene.handle, key, replacement);
            worlds[key] = replacement;
            try
            {
                result["sameKeyDifferentScene"] = replacement.WorldKey == original.WorldKey && replacement.Scene != original.Scene &&
                    GameplayWorldRegistry.TryGet(key, out GameplayWorldRuntime current) && ReferenceEquals(current, replacement);
                result["stoppedAfterReplacement"] = !creation.MoveNext();
                result["candidateReleased"] = candidate.DestructionHandled &&
                    !ItemMgr.Instance.IsRuntimeItemRegistered(candidate) &&
                    !WorldPhysicsService2D.TryGetRegisteredItemOwner(candidate, out _);
            }
            finally
            {
                worlds[key] = original;
                bindings.UnbindWorldFromScene(replacementScene.handle, key, replacement);
            }
            creation.Dispose();
            creation = null;
            result["noDeliveryOrPublish"] = !delivered && published == 0 &&
                ItemMgr.Instance.GetItemByGuid(candidateGuid) == null && CountRegisteredChickensAt(position) == initialCount;
            result["rolledBackOnce"] = manager.RolledBackCreatureCreations - rollbackBefore == 1;
            result["originalWorldRestored"] = GameplayWorldRegistry.TryGet(key, out GameplayWorldRuntime restored) &&
                ReferenceEquals(restored, original);
            result["originalPhysicsRetained"] = GameplayWorldRegistry.TryGetPhysics(key, out WorldPhysicsService2D retainedPhysics) &&
                ReferenceEquals(retainedPhysics, originalPhysics);
            yield return null;
            result["candidateDestroyed"] = candidate == null;
        }
        finally
        {
            creation?.Dispose();
            ItemMgr.RuntimeItemInstantiated -= observe;
            if (deliveredCreature != null && deliveredCreature.RuntimeGeneration == deliveredGeneration &&
                ReferenceEquals(deliveredCreature.itemData, deliveredData) &&
                ItemMgr.Instance.IsRuntimeItemRegistered(deliveredCreature))
                ItemMgr.Instance.DespawnItem(deliveredCreature, saveData: false);
            if (replacementScene.IsValid() && replacementScene.isLoaded)
                SceneManager.UnloadSceneAsync(replacementScene);
        }
    }

    private static Item FindUnregisteredCreatureCandidate(Scene scene, Vector3 position)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Item item in root.GetComponentsInChildren<Item>(true))
                if (item != null && item.itemData?.IDName == "Chicken" && !item.IsInPool &&
                    !item.DestructionHandled && !ItemMgr.Instance.IsRuntimeItemRegistered(item) &&
                    (item.transform.position - position).sqrMagnitude < .0001f) return item;
        return null;
    }

    private static bool HasLoadedCreatureModule(Item candidate)
    {
        if (candidate?.itemMods?.Mods == null) return false;
        foreach (Module module in candidate.itemMods.Mods.Values)
            if (module != null && module.IsRuntimeLoaded) return true;
        return false;
    }

    // 只统计测试落点的已注册鸡，验证分帧让帧期间候选尚未进入正式索引。
    private static int CountRegisteredChickensAt(Vector3 position)
    {
        IReadOnlyList<Item> chickens = ItemMgr.GetInstance()?.GetItemsByNameID("Chicken");
        if (chickens == null) return 0;
        int count = 0;
        for (int i = 0; i < chickens.Count; i++)
        {
            Item chicken = chickens[i];
            if (chicken != null && (chicken.transform.position - position).sqrMagnitude < 0.0001f)
                count++;
        }
        return count;
    }

    #endregion
}
