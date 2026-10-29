using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayTestSystemProbe
{
    #region 世界散落物环境规则采样

    private static void RegisterWorldMatterOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("item_runtime/drop_trajectory", DropTrajectory);
        operations.Add("item_runtime/world_matter_structure", WorldMatterStructure);
        operations.Add("item_runtime/natural_replacement_ownership", NaturalReplacementOwnership);
        operations.Add("item_runtime/ai_entity_live_query", AiEntityLiveQuery);
        operations.Add("item_runtime/world_tick_move_query", WorldTickMoveQuery);
        operations.Add("item_runtime/logical_world_tick", LogicalWorldTick);
        operations.Add("item_runtime/module_tick_mutations", ModuleTickMutations);
        operations.Add("item_runtime/world_matter_contact", WorldMatterContact);
        operations.Add("item_runtime/matter_transition_scan", MatterTransitionScan);
        operations.Add("item_runtime/matter_moisture_info", MatterMoistureInfo);
        operations.Add("survival/food_spoilage_moisture", FoodSpoilageMoisture);
        operations.Add("environment/combustion_oxygen", CombustionOxygen);
    }

    #region 共用掉落轨迹采样

    // 采样正式曲线工具，轨迹端点和独立弧高由 Lua 断言。
    private static JObject DropTrajectory(JObject args)
    {
        var start = Vector2.zero;
        var end = new Vector2(4f, 2f);
        Vector2 linear = Mod_Droping.CreateLinearControlPoint(start, end);
        Vector2 curved = Mod_Droping.CreateParabolicControlPoint(start, end, 2f);
        JObject Sample(string name, Vector2 control, float progress, float arc)
        {
            Vector2 point = Mod_Droping.EvaluateDropCurvePositionWithArcHeight(start, control, end, progress, arc);
            return new JObject { ["name"] = name, ["x"] = point.x, ["y"] = point.y };
        }
        return new JObject
        {
            ["linearControlX"] = linear.x, ["linearControlY"] = linear.y,
            ["curveControlX"] = curved.x, ["curveControlY"] = curved.y,
            ["samples"] = new JArray(Sample("start", curved, 0f, 1f), Sample("finish", curved, 1f, 1f),
                Sample("curve", curved, .5f, 1f), Sample("curve_without_arc", curved, .5f, 0f),
                Sample("linear_with_arc", linear, .5f, 1f))
        };
    }

    #endregion

    // 隔离停用实例调用正式生态交接，旧壳销毁不能删除候选的自然生成点。
    private static JObject NaturalReplacementOwnership(JObject args) => WithEntityProbeItem(original =>
        WithEntityProbeItem(replacement => WithEntityProbeItem(invalid =>
        {
            const int guid = 735019;
            var root = new GameObject("GameplayTest Natural Replacement")
                { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            var renderer = root.AddComponent<ChunkNaturalItemRenderer>();
            try
            {
                original.BindData(EntityProbeData("probe:natural-original", 1));
                replacement.BindData(EntityProbeData("probe:natural-replacement", 1));
                invalid.BindData(EntityProbeData("probe:natural-invalid", 1));
                original.itemData.Guid = replacement.itemData.Guid = guid;
                invalid.itemData.Guid = guid + 1;
                original.ResumeRuntimeAfterReplacement();
                replacement.ResumeRuntimeAfterReplacement();
                invalid.ResumeRuntimeAfterReplacement();
                renderer.RegisterNaturalItem(original);
                renderer.RegisterNaturalItem(original);
                var result = new JObject
                {
                    ["singleRegistration"] = renderer.SpawnedItemCount == 1
                };
                try { renderer.RegisterNaturalItem(replacement); result["duplicateRejected"] = false; }
                catch (InvalidOperationException) { result["duplicateRejected"] = true; }
                try { renderer.ReplaceNaturalItem(original, invalid); result["wrongGuidRejected"] = false; }
                catch (InvalidOperationException) { result["wrongGuidRejected"] = true; }
                invalid.ModuleUnload();
                invalid.itemData.Guid = guid;
                try { renderer.ReplaceNaturalItem(original, invalid); result["unloadedRejected"] = false; }
                catch (InvalidOperationException) { result["unloadedRejected"] = true; }
                result["sourceKeptOnRejection"] = renderer.TryGetSpawnedItem(guid, out Item current) &&
                    ReferenceEquals(current, original) && renderer.SpawnedItemCount == 1;

                renderer.ReplaceNaturalItem(original, replacement);
                result["replacementOwned"] = renderer.TryGetSpawnedItem(guid, out current) &&
                    ReferenceEquals(current, replacement) && renderer.SpawnedItemCount == 1;
                original.PrepareForDespawn(saveData: false);
                result["oldDestroyed"] = original.DestructionHandled;
                result["oldDestructionKeptReplacement"] = renderer.TryGetSpawnedItem(guid, out current) &&
                    ReferenceEquals(current, replacement);
                try { renderer.ReplaceNaturalItem(original, replacement); result["staleSourceRejected"] = false; }
                catch (InvalidOperationException) { result["staleSourceRejected"] = true; }
                replacement.PrepareForDespawn(saveData: false);
                result["replacementDestructionRemoved"] = renderer.SpawnedItemCount == 0 &&
                    !renderer.TryGetSpawnedItem(guid, out _);
                return result;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        })));

    // 查询必须实时接纳新增能力、停用子节点和移除变化，稳态不再创建组件数组。
    private static JObject AiEntityLiveQuery(JObject args) => WithEntityProbeItem(item =>
    {
        item.BindData(EntityProbeData("probe:live-ai-query", 1));
        bool initiallyNotAi = !RuntimeAiEntityUtility.IsAiEntity(item) && !RuntimeAiEntityUtility.IsAiEntity(null);
        for (int i = 0; i < 16; i++) RuntimeAiEntityUtility.IsAiEntity(item);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool negativeStable = true;
        for (int i = 0; i < 256; i++) negativeStable &= !RuntimeAiEntityUtility.IsAiEntity(item);
        long negativeAllocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var aiObject = new GameObject("LiveAiCapability");
        aiObject.transform.SetParent(item.transform, false);
        var marker = aiObject.AddComponent<GameplayTestWorldMatterAiMarker>();
        bool addedImmediately = RuntimeAiEntityUtility.IsAiEntity(item);
        aiObject.SetActive(false);
        marker.enabled = false;
        bool inactiveIncluded = RuntimeAiEntityUtility.IsAiEntity(item);
        before = GC.GetAllocatedBytesForCurrentThread();
        bool positiveStable = true;
        for (int i = 0; i < 256; i++) positiveStable &= RuntimeAiEntityUtility.IsAiEntity(item);
        long positiveAllocated = GC.GetAllocatedBytesForCurrentThread() - before;
        UnityEngine.Object.DestroyImmediate(aiObject);
        bool removedImmediately = !RuntimeAiEntityUtility.IsAiEntity(item);
        return new JObject
        {
            ["initiallyNotAi"] = initiallyNotAi, ["negativeStable"] = negativeStable,
            ["negativeAllocatedBytes"] = negativeAllocated, ["addedImmediately"] = addedImmediately,
            ["inactiveIncluded"] = inactiveIncluded, ["positiveStable"] = positiveStable,
            ["positiveAllocatedBytes"] = positiveAllocated, ["removedImmediately"] = removedImmediately
        };
    });

    // 调用真实世界调度入口，覆盖首次移动登记、重复移动、注销和重新绑定。
    private static JObject WorldTickMoveQuery(JObject args) => WithEntityProbeItem(item =>
    {
        item.BindData(EntityProbeData("probe:world-tick-move", 1));
        GameplayWorldRegistry.AssignItemToWorld(item, "probe:world-tick-move");
        var scheduler = new WorldItemTickScheduler();
        scheduler.NotifyMoved(item);
        bool firstMoveRegistered = scheduler.RegisteredCount == 1;
        for (int i = 0; i < 16; i++) scheduler.NotifyMoved(item);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++) scheduler.NotifyMoved(item);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        bool singleRegistration = scheduler.RegisteredCount == 1;
        scheduler.Remove(item);
        bool removed = scheduler.RegisteredCount == 0;
        item.BindData(EntityProbeData("probe:world-tick-rebound", 2));
        scheduler.NotifyMoved(item);
        scheduler.Register(item);
        bool reboundRegistered = scheduler.RegisteredCount == 1;
        scheduler.Remove(item);
        return new JObject
        {
            ["firstMoveRegistered"] = firstMoveRegistered, ["singleRegistration"] = singleRegistration,
            ["allocatedBytes"] = allocated, ["removed"] = removed, ["reboundRegistered"] = reboundRegistered,
            ["removedAgain"] = scheduler.RegisteredCount == 0
        };
    });

    // 真正的世界调度组应随逻辑世界迁移，不能把场景作为身份。
    private static JObject LogicalWorldTick(JObject args) => WithEntityProbeItem(first => WithEntityProbeItem(second =>
    {
        const string worldA = "probe:tick-world-a", worldB = "probe:tick-world-b";
        first.BindData(EntityProbeData("probe:tick-first", 1));
        second.BindData(EntityProbeData("probe:tick-second", 1));
        GameplayWorldRegistry.AssignItemToWorld(first, worldA);
        GameplayWorldRegistry.AssignItemToWorld(second, worldB);
        var scheduler = new WorldItemTickScheduler();
        scheduler.Register(first);
        scheduler.Register(second);
        bool sameScene = first.gameObject.scene == second.gameObject.scene;
        bool separateGroups = scheduler.GetRegisteredCountForWorld(worldA) == 1 &&
            scheduler.GetRegisteredCountForWorld(worldB) == 1 &&
            GameplayWorldRegistry.GetWorldItemCount(worldA) == 1 && GameplayWorldRegistry.GetWorldItemCount(worldB) == 1;

        GameplayWorldRegistry.AssignItemToWorld(second, worldA);
        scheduler.NotifyMoved(second);
        bool sameSceneTransfer = scheduler.GetRegisteredCountForWorld(worldA) == 2 &&
            scheduler.GetRegisteredCountForWorld(worldB) == 0 &&
            GameplayWorldRegistry.GetWorldItemCount(worldA) == 2 && GameplayWorldRegistry.GetWorldItemCount(worldB) == 0;
        Scene initialScene = second.gameObject.scene;
        Scene otherScene = WindShelterProbeScene("[LogicalWorldTickProbe]");
        try
        {
            SceneManager.MoveGameObjectToScene(second.gameObject, otherScene);
            scheduler.NotifyMoved(second);
            bool differentSceneSameGroup = first.gameObject.scene != second.gameObject.scene &&
                scheduler.GetRegisteredCountForWorld(worldA) == 2;
            for (int i = 0; i < 16; i++) scheduler.NotifyMoved(second);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++) scheduler.NotifyMoved(second);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            scheduler.Remove(second);
            second.PrepareForPoolReuse();
            bool poolClearedWorld = GameplayWorldRegistry.GetItemWorldKey(second) != worldA;
            GameplayWorldRegistry.AssignItemToWorld(second, worldB);
            scheduler.Register(second);
            bool pooledRebind = scheduler.GetRegisteredCountForWorld(worldA) == 1 &&
                scheduler.GetRegisteredCountForWorld(worldB) == 1;
            scheduler.Remove(first);
            scheduler.Remove(second);
            return new JObject
            {
                ["sameScene"] = sameScene, ["separateGroups"] = separateGroups,
                ["sameSceneTransfer"] = sameSceneTransfer, ["differentSceneSameGroup"] = differentSceneSameGroup,
                ["allocatedBytes"] = allocated, ["poolClearedWorld"] = poolClearedWorld,
                ["pooledRebind"] = pooledRebind, ["cleared"] = scheduler.RegisteredCount == 0
            };
        }
        finally
        {
            if (second != null && initialScene.IsValid() && initialScene.isLoaded && second.gameObject.scene != initialScene)
                SceneManager.MoveGameObjectToScene(second.gameObject, initialScene);
            CloseWindShelterProbeScene(otherScene);
        }
    }));

    // 在正式 Tick 内触发结构变更、嵌套调用、世界迁移和回池换代。
    private static JObject ModuleTickMutations(JObject args) => WithEntityProbeItem(item =>
    {
        item.BindData(EntityProbeData("probe:tick-mutations", 1));
        GameplayWorldRegistry.AssignItemToWorld(item, "probe:tick-mutations-a");
        var first = AddTickCallbackProbe(item, "first", ModuleTickMode.EveryFrame);
        var removed = AddTickCallbackProbe(item, "removed", ModuleTickMode.EveryFrame);
        var restarted = AddTickCallbackProbe(item, "restarted", ModuleTickMode.EveryFrame);
        var interval = AddTickCallbackProbe(item, "interval", ModuleTickMode.FixedInterval);
        item.ResumeRuntimeAfterReplacement();
        item.Tick(0f);

        GameplayTestItemTickCallbackProbe added = null;
        first.Callback = () =>
        {
            first.Callback = null;
            item.itemMods.RemoveMod(removed);
            removed.UnloadRuntime();
            restarted.SetEnabled(false);
            restarted.SetEnabled(true);
            added = AddTickCallbackProbe(item, "added", ModuleTickMode.EveryFrame);
            added.LoadRuntime();
            item.GetTickTier();
            item.Tick(0f);
        };
        item.Tick(.06f);
        bool nestedSnapshot = first.TickCount == 3 && removed.TickCount == 1 &&
            restarted.TickCount == 2 && added.TickCount == 1 && interval.TickCount == 0;
        item.Tick(.04f);
        bool rebuiltSnapshot = removed.TickCount == 1 && restarted.TickCount == 3 && added.TickCount == 2;
        bool preservedInterval = interval.TickCount == 1 && Mathf.Abs(interval.LastDelta - .1f) < .001f;

        int restartedBefore = restarted.TickCount, addedBefore = added.TickCount, intervalBefore = interval.TickCount;
        first.Callback = () =>
        {
            first.Callback = null;
            GameplayWorldRegistry.AssignItemToWorld(item, "probe:tick-mutations-b");
        };
        item.Tick(.11f);
        bool worldTransferStoppedOldTick = restarted.TickCount == restartedBefore &&
            added.TickCount == addedBefore && interval.TickCount == intervalBefore;

        GameplayWorldRegistry.AssignItemToWorld(item, null);
        uint previousGeneration = item.RuntimeGeneration;
        first.Callback = () =>
        {
            first.Callback = null;
            item.PrepareForPoolReuse();
            item.BindData(EntityProbeData("probe:tick-reused", 1));
            foreach (GameplayTestItemTickCallbackProbe module in new[] { first, restarted, added, interval })
            {
                module.ModuleInit(item, module._Data);
                item.itemMods.AddMod(module);
            }
            item.ResumeRuntimeAfterReplacement();
            item.GetTickTier();
        };
        item.Tick(.11f);
        bool poolStoppedOldTick = item.RuntimeGeneration != previousGeneration &&
            restarted.TickCount == restartedBefore && added.TickCount == addedBefore && interval.TickCount == intervalBefore;
        item.Tick(0f);
        bool poolNewGenerationTicked = restarted.TickCount == restartedBefore + 1 && added.TickCount == addedBefore + 1;
        return new JObject
        {
            ["nestedSnapshot"] = nestedSnapshot, ["rebuiltSnapshot"] = rebuiltSnapshot,
            ["preservedInterval"] = preservedInterval, ["worldTransferStoppedOldTick"] = worldTransferStoppedOldTick,
            ["poolStoppedOldTick"] = poolStoppedOldTick, ["poolNewGenerationTicked"] = poolNewGenerationTicked,
            ["schedulerRegistration"] = CaptureTickRegistrationReplacement()
        };
    });

    // 新注册令牌不得沿用本帧已取出的旧条目。
    private static bool CaptureTickRegistrationReplacement() => WithEntityProbeItem(item =>
    {
        Data_GeneralItem data = EntityProbeData("probe:tick-registration", 1);
        data.inHand = true;
        item.BindData(data);
        GameplayWorldRegistry.AssignItemToWorld(item, "probe:tick-registration");
        var module = AddTickCallbackProbe(item, "scheduler", ModuleTickMode.EveryFrame);
        item.ResumeRuntimeAfterReplacement();
        item.gameObject.SetActive(true);
        var scheduler = new ItemTickScheduler();
        scheduler.Register(item);
        scheduler.Update(.1f, target => { scheduler.Remove(target); scheduler.Register(target); }, Array.Empty<Transform>(), default);
        bool skippedOldRegistration = module.TickCount == 0;
        scheduler.Update(.1f, null, Array.Empty<Transform>(), default);
        bool newRegistrationTicked = module.TickCount == 1;
        scheduler.Remove(item);
        return new JObject { ["result"] = skippedOldRegistration && newRegistrationTicked };
    }).Value<bool>("result");

    private static GameplayTestItemTickCallbackProbe AddTickCallbackProbe(GameItem item, string name, ModuleTickMode mode)
    {
        var module = item.gameObject.AddComponent<GameplayTestItemTickCallbackProbe>();
        module.Mode = mode;
        module.ModuleInit(item, new Ex_ModData_MemoryPackable { StableName = name, ModuleId = name, Enabled = true });
        item.itemMods.AddMod(module);
        return module;
    }

    // 独立停用宿主只读取正式资格缓存，不创建世界、拾取器或完整物品管理器。
    private static JObject WorldMatterStructure(JObject args) => WithEntityProbeItem(item =>
    {
        item.BindData(EntityProbeData("probe:world-matter", 2));
        var result = new JObject();
        var initial = new ItemMgr.WorldMatterStructure(item);
        result["initialCurrent"] = initial.IsCurrent(item);
        result["initialBlocked"] = initial.BlocksWorldMatter;
        initial.IsCurrent(item);
        _ = initial.BlocksWorldMatter;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        bool stable = true;
        for (int i = 0; i < 512; i++) stable &= initial.IsCurrent(item) && !initial.BlocksWorldMatter;
        result["stableAllocatedBytes"] = GC.GetAllocatedBytesForCurrentThread() - allocated;
        result["stableAllowed"] = stable;

        var receiverObject = new GameObject("TileReceiver");
        receiverObject.transform.SetParent(item.transform, false);
        var receiver = receiverObject.AddComponent<Mod_TileEffectReceiver>();
        receiver._Data = new Ex_ModData_MemoryPackable { StableName = "tile_receiver" };
        item.itemMods.AddMod(receiver);
        result["addInvalidated"] = !initial.IsCurrent(item);
        var withReceiver = new ItemMgr.WorldMatterStructure(item);
        result["receiverBlocked"] = withReceiver.BlocksWorldMatter;
        result["receiverMatchesReference"] = withReceiver.BlocksWorldMatter ==
            (item.GetComponentInChildren<Mod_TileEffectReceiver>(true) != null);
        item.itemMods.RemoveMod(receiver);
        UnityEngine.Object.DestroyImmediate(receiverObject);
        result["removeInvalidated"] = !withReceiver.IsCurrent(item);
        var afterRemoval = new ItemMgr.WorldMatterStructure(item);
        result["afterRemoveAllowed"] = !afterRemoval.BlocksWorldMatter;

        var aiObject = new GameObject("AiCapability");
        aiObject.transform.SetParent(item.transform, false);
        aiObject.AddComponent<GameplayTestWorldMatterAiMarker>();
        item.NotifyRuntimeStructureChanged();
        var withAi = new ItemMgr.WorldMatterStructure(item);
        result["aiBlocked"] = withAi.BlocksWorldMatter;
        result["aiMatchesReference"] = withAi.BlocksWorldMatter == RuntimeAiEntityUtility.IsAiEntity(item);
        UnityEngine.Object.DestroyImmediate(aiObject);
        item.NotifyRuntimeStructureChanged();
        var beforeRebind = new ItemMgr.WorldMatterStructure(item);
        item.BindData(EntityProbeData("probe:world-matter-rebound", 3));
        result["rebindInvalidated"] = !beforeRebind.IsCurrent(item);
        var beforeLoad = new ItemMgr.WorldMatterStructure(item);
        item.ResumeRuntimeAfterReplacement();
        result["generationInvalidated"] = !beforeLoad.IsCurrent(item);
        result["generationAllowed"] = !new ItemMgr.WorldMatterStructure(item).BlocksWorldMatter;

        var combustionHost = new GameObject("CombustionCapabilities");
        combustionHost.transform.SetParent(item.transform, false);
        var combustion = combustionHost.AddComponent<Mod_Combustion>();
        var visual = combustionHost.AddComponent<Mod_CombustionVisual>();
        combustion.ModData.StableName = "probe_combustion";
        visual.ModData.StableName = "probe_combustion_visual";
        item.itemMods.AddMod(combustion);
        item.itemMods.AddMod(visual);
        var withCombustion = new ItemMgr.WorldMatterStructure(item);
        result["combustionCached"] = ReferenceEquals(withCombustion.CachedCombustion, combustion) &&
            ReferenceEquals(withCombustion.CachedCombustionVisual, visual);
        result["inactiveCombustionExcluded"] = withCombustion.ActiveCombustion == null &&
            withCombustion.ActiveCombustionVisual == null && !withCombustion.BlocksWorldMatter;
        item.itemMods.RemoveMod(visual);
        result["combustionRemovalInvalidated"] = !withCombustion.IsCurrent(item);
        result["combustionVisualRemoved"] = new ItemMgr.WorldMatterStructure(item).CachedCombustionVisual == null;
        item.itemMods.RemoveMod(combustion);
        UnityEngine.Object.DestroyImmediate(combustionHost);

        // 运动状态是模块内可变字段；缓存模块引用，但每次资格检查必须读取当前 Drop。
        var dropHost = new GameObject("DroppingCapability");
        dropHost.transform.SetParent(item.transform, false);
        var dropping = dropHost.AddComponent<Mod_Droping>();
        dropping._Data = new Ex_ModData { StableName = "probe_dropping" };
        item.itemMods.AddMod(dropping);
        var dropStructure = new ItemMgr.WorldMatterStructure(item);
        result["dropInitiallyAllowed"] = !dropStructure.BlocksWorldMatter &&
            !Mod_Droping.IsDropInProgress(item);
        dropping.drop = new Mod_BaseDroper.Drop();
        result["dropMovingBlocked"] = dropStructure.BlocksWorldMatter &&
            Mod_Droping.IsDropInProgress(item);
        dropping.drop.waterFloating = true;
        result["dropFloatingAllowed"] = !dropStructure.BlocksWorldMatter &&
            !Mod_Droping.IsDropInProgress(item);
        dropping.drop.waterFloating = false;
        dropping.drop.waterSinking = true;
        result["dropSinkingAllowed"] = !dropStructure.BlocksWorldMatter &&
            !Mod_Droping.IsDropInProgress(item);
        dropping.drop = null;
        result["dropFinishedAllowed"] = !dropStructure.BlocksWorldMatter;
        item.itemMods.RemoveMod(dropping);
        result["dropRemovalInvalidated"] = !dropStructure.IsCurrent(item) &&
            !new ItemMgr.WorldMatterStructure(item).BlocksWorldMatter;
        UnityEngine.Object.DestroyImmediate(dropHost);
        return result;
    });

    private static JObject WorldMatterContact(JObject args)
    {
        bool water = Flag(args, "water", false);
        var data = EntityProbeData("probe:world-matter-contact", 7);
        data.MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 20f };
        var contact = new WorldLiquidSettings
        {
            WaterContact = water, Temperature = (float)Number(args, "temperature", 1100),
            ContactHeatingPerSecond = (float)Number(args, "rate", 75)
        };
        float seconds = (float)Number(args, "seconds", 2);
        bool changed = WorldItemMatterRules.Advance(data, 20f, seconds, contact, out bool waterContact);
        return new JObject
        {
            ["changed"] = changed, ["waterContact"] = waterContact,
            ["temperature"] = data.MatterState.TemperatureCelsius, ["amount"] = data.Stack.Amount
        };
    }

    /// <summary>用只读列表验证物质条件顺序、边界和运行态变化不被索引优化改变。</summary>
    private static JObject MatterTransitionScan(JObject args)
    {
        var transitions = new List<RuntimeItemMatterTransition>
        {
            new("hot", 600f, null, null, null, "probe:hot", 1f, null),
            new("warm", 500f, null, null, null, "probe:warm", 1f, null),
            new("dry", null, null, null, 0.2f, "probe:dry", 1f, null)
        };
        var matter = new RuntimeItemMatterDefinition(null, 0.8f, 0f, 0f, 0f, 0f,
            transitions.AsReadOnly());
        var state = new ItemMatterState { Initialized = true, TemperatureCelsius = 700f, Moisture = 0.6f };
        bool firstMatch = ItemMatterRuntime.SelectMatchedTransition(matter, state, false) == transitions[0];
        state.TemperatureCelsius = 550f;
        bool secondMatch = ItemMatterRuntime.SelectMatchedTransition(matter, state, false) == transitions[1];
        state.TemperatureCelsius = 20f;
        state.Moisture = 0.2f;
        bool dryBoundary = ItemMatterRuntime.SelectMatchedTransition(matter, state, false) == transitions[2];
        state.Moisture = 0.21f;
        bool dryRejected = ItemMatterRuntime.SelectMatchedTransition(matter, state, false) == null;
        state.Initialized = false;
        bool uninitializedRejected = ItemMatterRuntime.SelectMatchedTransition(matter, state, false) == null;
        state.Initialized = true;
        bool noLiquidMatch = ItemMatterRuntime.SelectMatchedTransition(matter, state, true) == null;
        bool emptyRejected = ItemMatterRuntime.SelectMatchedTransition(
            new RuntimeItemMatterDefinition(null, 0f, 0f, 0f, 0f, 0f,
                Array.Empty<RuntimeItemMatterTransition>()), state, false) == null;
        bool nullRejected = ItemMatterRuntime.SelectMatchedTransition(null, state, false) == null &&
            ItemMatterRuntime.SelectMatchedTransition(matter, null, false) == null;

        return new JObject
        {
            ["firstMatch"] = firstMatch, ["secondMatch"] = secondMatch,
            ["dryBoundary"] = dryBoundary, ["dryRejected"] = dryRejected,
            ["uninitializedRejected"] = uninitializedRejected, ["noLiquidMatch"] = noLiquidMatch,
            ["emptyRejected"] = emptyRejected, ["nullRejected"] = nullRejected
        };
    }

    // 结构化含水数据与 UI 文案分别调用正式入口，确认查看物品不推进业务状态。
    private static JObject MatterMoistureInfo(JObject args)
    {
        var meatMatter = new RuntimeItemMatterDefinition(null, .8f, .00085f, 0f, .25f, .02f,
            Array.Empty<RuntimeItemMatterTransition>());
        var state = new ItemMatterState { Initialized = false };
        bool initialFound = ItemMatterInfoQuery.TryGetMoisture(state, meatMatter, out float initial);
        var initialLines = new List<string>();
        bool initialDisplayed = ItemMoistureTooltipInfoProvider.AppendMoisture(state, meatMatter, initialLines);
        bool uninitializedUnchanged = !state.Initialized && state.Moisture == 0f;

        state.Initialized = true;
        state.Moisture = .412f;
        bool liveFound = ItemMatterInfoQuery.TryGetMoisture(state, meatMatter, out float live);
        var liveLines = new List<string>();
        bool liveDisplayed = ItemMoistureTooltipInfoProvider.AppendMoisture(state, meatMatter, liveLines);
        bool liveUnchanged = state.Initialized && Mathf.Approximately(state.Moisture, .412f);

        state.Moisture = .2f;
        bool boundaryFound = ItemMatterInfoQuery.TryGetMoisture(state, meatMatter, out float boundary);
        var boundaryLines = new List<string>();
        bool boundaryDisplayed = ItemMoistureTooltipInfoProvider.AppendMoisture(state, meatMatter, boundaryLines);

        state.Moisture = float.NaN;
        bool invalidRejected = !ItemMatterInfoQuery.TryGetMoisture(state, meatMatter, out _) &&
            !ItemMoistureTooltipInfoProvider.AppendMoisture(state, meatMatter, new List<string>());
        state.Moisture = 0f;
        bool emptyMoistureVisible = ItemMatterInfoQuery.TryGetMoisture(state, meatMatter, out float emptyMoisture) &&
            emptyMoisture == 0f;

        var inertMatter = new RuntimeItemMatterDefinition(300f, 0f, 0f, 0f, 0f, 0f,
            Array.Empty<RuntimeItemMatterTransition>());
        var inertLines = new List<string>();
        bool inertHidden = !ItemMatterInfoQuery.TryGetMoisture(state, inertMatter, out _) &&
            !ItemMoistureTooltipInfoProvider.AppendMoisture(state, inertMatter, inertLines) &&
            !ItemMatterInfoQuery.TryGetMoisture(state, null, out _);
        var moistureTransitionMatter = new RuntimeItemMatterDefinition(null, 0f, 0f, 0f, 0f, 0f,
            new[] { new RuntimeItemMatterTransition("dry", null, null, null, .2f, "dry", 1f, null) });
        bool moistureTransitionVisible = ItemMatterInfoQuery.TryGetMoisture(state,
            moistureTransitionMatter, out _);
        return new JObject
        {
            ["initialFound"] = initialFound, ["initial"] = initial,
            ["initialDisplayed"] = initialDisplayed, ["initialText"] = initialLines.Count > 0 ? initialLines[0] : "",
            ["expectedInitialText"] = FlatWorld.Localization.FlatWorldLocalizationService.GetUiFormat("含水量：{0}%", "80"),
            ["uninitializedUnchanged"] = uninitializedUnchanged,
            ["liveFound"] = liveFound, ["live"] = live,
            ["liveDisplayed"] = liveDisplayed, ["liveText"] = liveLines.Count > 0 ? liveLines[0] : "",
            ["expectedLiveText"] = FlatWorld.Localization.FlatWorldLocalizationService.GetUiFormat("含水量：{0}%", "41.2"),
            ["liveUnchanged"] = liveUnchanged,
            ["boundaryFound"] = boundaryFound, ["boundary"] = boundary,
            ["boundaryDisplayed"] = boundaryDisplayed, ["boundaryText"] = boundaryLines.Count > 0 ? boundaryLines[0] : "",
            ["expectedBoundaryText"] = FlatWorld.Localization.FlatWorldLocalizationService.GetUiFormat("含水量：{0}%", "20"),
            ["invalidRejected"] = invalidRejected, ["emptyMoistureVisible"] = emptyMoistureVisible,
            ["inertHidden"] = inertHidden && inertLines.Count == 0,
            ["moistureTransitionVisible"] = moistureTransitionVisible
        };
    }

    #endregion

    #region 食物到期与含水分流采样

    // 读取正式生肉配置，采样共用时钟、临界含水量和世界环境步进，不修改真实库存。
    private static JObject FoodSpoilageMoisture(JObject args)
    {
        ItemDefinitionDto meat = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().Single(value => value.Id == "Meat");
        var food = meat.Modules["food"].Data.ToObject<ModData_FoodData>();
        food.StableName = "food";
        food.ModuleId = ModText.Food;
        var item = EntityProbeData("Meat", 3);
        item.ModuleDataDic.Add(food.StableName, food);
        item.MatterState = new ItemMatterState
        {
            Initialized = Flag(args, "initialized", true), TemperatureCelsius = 20f,
            Moisture = Flag(args, "invalidMoisture", false) ? float.NaN : (float)Number(args, "moisture", .8)
        };
        RuntimeItemMatterDefinition matter = ItemMatterReactionCompiler.CompileMatter(
            meat.Matter, item.Tags, item.IDName);
        var definition = new RuntimeItemDefinition(item.IDName, "", null, item, null, null, null,
            null, null, null, null, null, null, matter: matter);
        FoodSpoilageObserverData state = FoodSpoilageObserverData.Load(food);
        string configuredDryTarget = state.DryingTargetItemID;
        float configuredDryThreshold = state.DryingMaxMoisture;
        state.SpoilageElapsedSeconds = (float)Number(args, "elapsed", 1799);
        state.EnableSpoilage = Flag(args, "enabled", true);
        if (Flag(args, "noDrying", false)) state.DryingTargetItemID = null;
        if (Flag(args, "invalidInterval", false)) state.SpoilageIntervalSeconds = 0f;
        state.Save(food);
        bool eligible = FoodSpoilageObserverData.TryLoad(item, out _, out _);
        bool beforeReady = state.TryGetExpiredTarget(item, out _, definition);
        float seconds = Flag(args, "invalidSeconds", false) ? float.NaN : (float)Number(args, "seconds", 1);
        bool advanced = state.Advance(seconds, food);
        bool ready = state.TryGetExpiredTarget(item, out string target, definition);
        FoodSpoilageObserverData restored = FoodSpoilageObserverData.Load(food);
        bool restoredReady = restored.TryGetExpiredTarget(item, out string restoredTarget, definition);
        bool advancedAgain = restored.Advance(0f, food);

        var worldFood = meat.Modules["food"].Data.ToObject<ModData_FoodData>();
        worldFood.StableName = "food";
        worldFood.ModuleId = ModText.Food;
        var worldItem = EntityProbeData("Meat", 2);
        worldItem.ModuleDataDic.Add(worldFood.StableName, worldFood);
        worldItem.MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 20f, Moisture = .8f };
        FoodSpoilageObserverData worldState = FoodSpoilageObserverData.Load(worldFood);
        worldState.SpoilageElapsedSeconds = 1799f;
        worldState.Save(worldFood);
        bool worldChanged = WorldItemMatterRules.Advance(worldItem, 50f, 30f, null, out _);
        worldState = FoodSpoilageObserverData.Load(worldFood);
        bool worldReady = worldState.TryGetExpiredTarget(worldItem, out string worldTarget, definition);
        float worldTemperatureAtExpiry = worldItem.MatterState.TemperatureCelsius;
        WorldItemMatterRules.Advance(worldItem, 50f, 30f, null, out _);

        return new JObject
        {
            ["eligible"] = eligible, ["beforeReady"] = beforeReady, ["advanced"] = advanced,
            ["ready"] = ready, ["target"] = target ?? "", ["elapsed"] = state.SpoilageElapsedSeconds,
            ["restoredReady"] = restoredReady, ["restoredTarget"] = restoredTarget ?? "",
            ["advancedOnZero"] = advancedAgain, ["configuredDryTarget"] = configuredDryTarget,
            ["configuredDryThreshold"] = configuredDryThreshold, ["matterDryThreshold"] = matter.Transitions[1].MaxMoisture,
            ["worldChanged"] = worldChanged, ["worldReady"] = worldReady, ["worldTarget"] = worldTarget ?? "",
            ["worldElapsed"] = worldState.SpoilageElapsedSeconds, ["worldTemperatureAtExpiry"] = worldTemperatureAtExpiry,
            ["worldExpiredTemperature"] = worldItem.MatterState.TemperatureCelsius
        };
    }

    #endregion

    #region 明火氧气规则采样
    // 使用独立大气与临时环境来源，采样正式阈值和环境覆盖，不修改真实舱气。
    private static JObject CombustionOxygen(JObject args)
    {
        decimal fraction = args?.Value<decimal?>("fraction") ?? .05m;
        double pressure = Number(args, "pressure", 100d);
        var atmosphere = new AtmosphereState
        {
            CapacityMoles = 100m, ReferenceMoles = 100m,
            ReferencePressureKPa = pressure, TemperatureKelvin = 293.15d
        };
        atmosphere.Gases.Add(new AtmosphereGasAmount { FluidId = FluidIds.Oxygen, Moles = 100m * fraction });
        atmosphere.Gases.Add(new AtmosphereGasAmount { FluidId = "core:nitrogen", Moles = 100m * (1m - fraction) });
        if (Flag(args, "vacuum", false)) atmosphere.Gases.Clear();
        bool allowed = AtmosphereService.CanSustainCombustion(atmosphere);
        var source = new GameplayCombustionEnvironmentSource { Allowed = allowed };
        ItemEnvironmentSources.Register(source);
        try
        {
            bool first = ItemEnvironmentSources.CanSustainCombustion("probe:combustion", default);
            source.Allowed = !allowed;
            bool updated = ItemEnvironmentSources.CanSustainCombustion("probe:combustion", default);
            return new JObject
            {
                ["minimumFraction"] = AtmosphereService.MinimumCombustionOxygenFraction,
                ["allowed"] = allowed,
                ["partialPressureAllowed"] = AtmosphereService.CanSustainCombustion(pressure,
                    Flag(args, "vacuum", false) ? 0d : pressure * (double)fraction),
                ["missingAllowed"] = AtmosphereService.CanSustainCombustion(null),
                ["localAllowed"] = first, ["localUpdated"] = updated
            };
        }
        finally { ItemEnvironmentSources.Unregister(source); }
    }

    private sealed class GameplayCombustionEnvironmentSource : IItemEnvironmentSource
    {
        public bool Allowed;
        public bool TrySample(Item item, out ItemEnvironmentSample sample) { sample = default; return false; }
        public float SupplyAmbientOxygen(Item item, float maximumAmount) => 0f;
        public bool TrySampleCombustion(string worldKey, Vector2 position, out bool canBurn)
        { canBurn = Allowed; return worldKey == "probe:combustion"; }
    }
    #endregion
}

internal sealed class GameplayTestItemTickCallbackProbe : Module
{
    #region Tick 回调夹具
    private Ex_ModData_MemoryPackable data = new();
    public override ModuleData _Data { get => data; set => data = (Ex_ModData_MemoryPackable)value; }
    public ModuleTickMode Mode;
    public override ModuleTickMode TickMode => Mode;
    public override float FixedTickInterval => .1f;
    public Action Callback;
    public int TickCount;
    public float LastDelta;
    protected override void OnLoad() { }
    protected override void OnSave() { }
    public override void ModUpdate(float deltaTime) { TickCount++; LastDelta = deltaTime; Callback?.Invoke(); }
    #endregion
}

internal sealed class GameplayTestWorldMatterAiMarker : MonoBehaviour, IAIActor
{
    #region 独立 AI 能力夹具
    public Item ActorItem => GetComponentInParent<Item>(true);
    public bool IsAlive => true;
    #endregion
}
