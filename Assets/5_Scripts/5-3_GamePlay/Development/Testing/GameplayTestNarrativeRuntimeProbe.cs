using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Gameplay.Events;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Gameplay.Quests;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>时间、任务与事件使用隔离世界内已经启动的正式服务采样。</summary>
public static partial class GameplayTestNarrativeRuntimeProbe
{
    #region 真实操作登记
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register()
    {
        GameplayTestRuntimeProbe.Register("time", "live_clock", CaptureClock);
        GameplayTestRuntimeProbe.RegisterAsync("time", "fixed_clock_steps", CaptureFixedClockSteps);
        GameplayTestRuntimeProbe.Register("game_events", "signal_lifecycle", CaptureEvent);
        GameplayTestRuntimeProbe.RegisterAsync("dimension", "cave_roundtrip", CaptureDimension);
        GameplayTestRuntimeProbe.RegisterAsync("dimension", "generation_preset_roundtrip", CaptureMarsGenerationPresetRoundtrip);
    }
    #endregion

    #region 正式主时钟与场景引用
    private static IEnumerator CaptureFixedClockSteps(Player player, JObject args, Action<JObject> completed)
    {
        DayTimeSystem service = DayTimeSystem.Instance ?? throw new InvalidOperationException("真实时钟未启动。");
        string key = "[LuaFixedTimeSource]" + Guid.NewGuid().ToString("N");
        service.InitializeSceneTimeData(key, timeScale: 1f);
        TimeData clock = service.WorldTimeDict[key];
        ulong initialTick = GameplayFixedStepSimulation.TickIndex;
        ulong previousTick = initialTick;
        int notifications = 0;
        bool allInFixedStep = true, consecutiveTicks = true;
        double initialTime = clock.GetTotalGameTimeSeconds(), expectedElapsed = 0d;
        Action<string, float, float> onTime = (world, before, after) =>
        {
            if (world != key) return;
            ulong tick = GameplayFixedStepSimulation.TickIndex;
            notifications++;
            allInFixedStep &= Time.inFixedTimeStep;
            consecutiveTicks &= tick == previousTick + 1;
            previousTick = tick;
            expectedElapsed += Time.fixedDeltaTime * clock.TimeScaleModifier * GameRuleConfiguration.Current.World.TimeSpeedMultiplier;
        };
        service.TimeAdvanced += onTime;
        try
        {
            double timeout = Time.realtimeSinceStartupAsDouble + 5d;
            while (GameplayFixedStepSimulation.TickIndex - initialTick < 4 && Time.realtimeSinceStartupAsDouble < timeout)
                yield return null;
            completed(new JObject
            {
                ["steps"] = (long)(GameplayFixedStepSimulation.TickIndex - initialTick),
                ["notifications"] = notifications,
                ["allInFixedStep"] = allInFixedStep,
                ["consecutiveTicks"] = consecutiveTicks,
                ["elapsed"] = clock.GetTotalGameTimeSeconds() - initialTime,
                ["expectedElapsed"] = expectedElapsed
            });
        }
        finally
        {
            service.TimeAdvanced -= onTime;
            service.WorldTimeDict.Remove(key);
            service.SceneLightingRateDict.Remove(key);
        }
    }
    private static JObject CaptureClock(Player player, JObject args)
    {
        DayTimeSystem service = DayTimeSystem.Instance ?? throw new InvalidOperationException("真实时钟未启动。");
        string scene = SceneManager.GetActiveScene().name;
        if (!service.TryGetResolvedTimeData(scene, out string originalKey, out TimeData originalClock) ||
            !service.WorldTimeDict.TryGetValue(scene, out TimeData sceneClock))
            throw new InvalidOperationException("活动世界没有登记正式主时钟。");
        string key = "[LuaTimeSource]" + Guid.NewGuid().ToString("N");
        string alias = "[LuaTimeReference]" + Guid.NewGuid().ToString("N");
        double originalSeconds = originalClock.GetTotalGameTimeSeconds();
        string originalReference = sceneClock.ReferenceScene;
        int timeNotifications = 0, dayNotifications = 0;
        float notifiedBefore = 0f, notifiedAfter = 0f;
        Action<string, float, float> onTime = (world, before, after) =>
        {
            if (world != key) return;
            timeNotifications++; notifiedBefore = before; notifiedAfter = after;
        };
        Action<string, int, int> onDay = (world, before, after) => { if (world == key) dayNotifications++; };
        var result = new JObject();
        service.TimeAdvanced += onTime;
        service.DayChanged += onDay;
        try
        {
            // 测试钟由正式服务登记，跨日通知发生时活动世界仍引用原钟，避免推进天气和生态。
            service.InitializeSceneTimeData(key, originalClock.DayLength, originalClock.OrbitalPeriodSeconds, 0f);
            TimeData clock = service.WorldTimeDict[key];
            service.SetReferenceScene(alias, key);
            bool resolved = service.TryGetResolvedTimeData(alias, out string resolvedKey, out TimeData referenced);
            service.JumpToTime(key, clock.DayLength - 2f);
            double beforeAdvance = clock.GetTotalGameTimeSeconds();
            int beforeDay = clock.TotalDays;
            int beforeTimeEvents = timeNotifications, beforeDayEvents = dayNotifications;
            service.AdvanceTime(key, 3f);
            double afterAdvance = clock.GetTotalGameTimeSeconds();
            DayTimeSaveData saved = service.GetSaveData();
            TimeData serialized = saved.WorldTimeDict[key].ToTimeData();
            service.SetReferenceScene(scene, key);
            result["scene"] = scene; result["source"] = key;
            result["originalSource"] = originalKey;
            result["referenceResolved"] = resolved && resolvedKey == key && ReferenceEquals(referenced, clock);
            result["referenceTime"] = service.GetCurrentTime(alias);
            result["sourceTime"] = service.GetCurrentTime(key);
            result["activeSceneTime"] = service.GetCurrentTime(scene);
            result["beforeAdvance"] = beforeAdvance; result["afterAdvance"] = afterAdvance;
            result["beforeDay"] = beforeDay; result["afterDay"] = clock.TotalDays;
            result["timeNotifications"] = timeNotifications - beforeTimeEvents;
            result["dayNotifications"] = dayNotifications - beforeDayEvents;
            result["notifiedBefore"] = notifiedBefore; result["notifiedAfter"] = notifiedAfter;
            result["savedSeconds"] = serialized.GetTotalGameTimeSeconds();
            result["savedDay"] = serialized.TotalDays;
            result["lighting"] = service.GetLighting(scene);
            result["sunLighting"] = service.GetSunLighting(scene);
            return result;
        }
        finally
        {
            service.TimeAdvanced -= onTime;
            service.DayChanged -= onDay;
            sceneClock.ReferenceScene = originalReference;
            service.WorldTimeDict.Remove(key);
            service.SceneLightingRateDict.Remove(key);
            service.WorldTimeDict.Remove(alias);
            service.SceneLightingRateDict.Remove(alias);
            result["restoredSeconds"] = originalClock.GetTotalGameTimeSeconds();
            result["originalSeconds"] = originalSeconds;
            result["referenceRemoved"] = !service.WorldTimeDict.ContainsKey(alias);
            result["sourceRemoved"] = !service.WorldTimeDict.ContainsKey(key);
            result["activeReferenceRestored"] = sceneClock.ReferenceScene == originalReference;
        }
    }
    #endregion

    #region 已启动任务运行时与真实背包事务
    public static Inventory RequireBag(Player player)
    {
        Mod_Inventory bag = player.itemMods?.GetMod_ByID<Mod_Inventory>(ModText.Bag);
        return bag?.InventoryInstances?.FirstOrDefault(value => value?.Data != null) ??
            throw new InvalidOperationException("主角真实主背包尚未建立。");
    }

    private sealed class QuestScope : IDisposable
    {
        private readonly Player player;
        private readonly string questId;
        private readonly QuestProgressSaveRecord original;
        private readonly bool hadOriginal;
        public readonly PlayerQuestRuntime Runtime;
        private bool disposed;
        public QuestScope(Player player, string questId)
        {
            this.player = player;
            this.questId = questId;
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            QuestManager manager = UnityEngine.Object.FindObjectOfType<QuestManager>();
            if (manager == null || !manager.TryGetRuntime(player, out PlayerQuestRuntime runtime) || !runtime.IsEnabled)
                throw new InvalidOperationException("主角没有在正式任务管理器中登记已启动运行时。");
            Runtime = runtime;
            QuestProgressSaveDocument staged = QuestProgressStore.Load(player.Data);
            hadOriginal = staged.Quests.TryGetValue(questId, out original);
            staged.Quests.Remove(questId);
            QuestProgressStore.Save(player.Data, staged);
            try
            {
                if (!Runtime.Initialize(out string reason)) throw new InvalidOperationException(reason);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            QuestProgressSaveDocument current = QuestProgressStore.Load(player.Data);
            if (hadOriginal) current.Quests[questId] = original;
            else current.Quests.Remove(questId);
            QuestProgressStore.Save(player.Data, current);
            if (!Runtime.Initialize(out string reason))
                throw new InvalidOperationException("任务自有前置条件无法恢复：" + reason);
        }
    }

    private static JObject QuestSample(PlayerQuestRuntime runtime, string id)
    {
        if (!runtime.TryGetSnapshot(id, out QuestSnapshot snapshot)) return new JObject { ["exists"] = false };
        return new JObject
        {
            ["exists"] = true, ["status"] = snapshot.Status.ToString(), ["stage"] = snapshot.CurrentStageId,
            ["progress"] = JObject.FromObject(snapshot.ObjectiveProgress)
        };
    }

    public static JObject CaptureInventoryQuest(Player player)
    {
        const string id = "flatworld:debug_open_inventory";
        Inventory bag = RequireBag(player);
        bool wasOpen = bag.basePanel != null && bag.basePanel.IsOpen();
        var result = new JObject();
        using var scope = new QuestScope(player, id);
        try
        {
            if (wasOpen) bag.SwitchUI();
            result["accepted"] = scope.Runtime.AcceptQuest(id, out string acceptReason);
            result["acceptReason"] = acceptReason;
            result["before"] = QuestSample(scope.Runtime, id);
            result["prematureClaim"] = scope.Runtime.ClaimQuest(id, out _);
            bag.SwitchUI();
            result["panelOpen"] = bag.basePanel != null && bag.basePanel.IsOpen();
            result["afterOpen"] = QuestSample(scope.Runtime, id);
            bag.SwitchUI();
            bag.SwitchUI();
            result["afterRepeat"] = QuestSample(scope.Runtime, id);
            result["claimed"] = scope.Runtime.ClaimQuest(id, out string claimReason);
            result["claimReason"] = claimReason;
            result["afterClaim"] = QuestSample(scope.Runtime, id);
            result["duplicateClaim"] = scope.Runtime.ClaimQuest(id, out _);
            result["duplicateAccept"] = scope.Runtime.AcceptQuest(id, out _);
            QuestProgressSaveRecord stored = QuestProgressStore.Load(player.Data).Quests[id];
            result["savedStatus"] = stored.Status.ToString();
            result["savedRewardsClaimed"] = stored.RewardsClaimed;
            result["savedCompletionCount"] = stored.CompletionCount;
            return result;
        }
        finally
        {
            bool nowOpen = bag.basePanel != null && bag.basePanel.IsOpen();
            if (nowOpen != wasOpen) bag.SwitchUI();
            result["panelRestored"] = (bag.basePanel != null && bag.basePanel.IsOpen()) == wasOpen;
        }
    }

    public static JObject CapturePickupQuest(Player player)
    {
        const string id = "flatworld:debug_pickup_items";
        Mod_ItemPicker picker = player.itemMods?.GetMod_ByID<Mod_ItemPicker>(ModText.Picker) ??
            throw new InvalidOperationException("主角正式拾取模块缺失。");
        var inventories = picker.AddTargetInventories.Select(value => value?.GetDefaultTargetInventory())
            .Where(value => value?.Data != null).Distinct().ToArray();
        var snapshots = inventories.ToDictionary(value => value, value => InventoryInstanceSnapshot.Capture(value.Data));
        var signals = new JArray();
        Action<GameplayProgressSignal> observe = signal =>
        {
            if (signal.Actor == player && signal.Type == GameplayProgressTypes.ItemPickedUp)
                signals.Add(new JObject { ["type"] = signal.Type, ["amount"] = signal.Amount, ["target"] = signal.TargetId });
        };
        var result = new JObject();
        using var scope = new QuestScope(player, id);
        GameplayProgressEvents.SignalPublished += observe;
        try
        {
            result["accepted"] = scope.Runtime.AcceptQuest(id, out string reason);
            result["acceptReason"] = reason;
            result["before"] = QuestSample(scope.Runtime, id);
            GameRes resources = GameRes.ExistingInstance ?? throw new InvalidOperationException("正式物品目录缺失。");
            ItemData first = resources.CreateItemData("Stick_Wood"); first.Stack.Amount = 2f;
            result["firstPickup"] = picker.TryAcceptNetworkPickup(first, false);
            result["afterFirst"] = QuestSample(scope.Runtime, id);
            ItemData second = resources.CreateItemData("Stick_Wood"); second.Stack.Amount = 1f;
            result["secondPickup"] = picker.TryAcceptNetworkPickup(second, false);
            result["afterSecond"] = QuestSample(scope.Runtime, id);
            result["savedCompletionCount"] = QuestProgressStore.Load(player.Data).Quests[id].CompletionCount;
            result["signals"] = signals;
            result["inventoryCountAfter"] = CountItem(inventories, "Stick_Wood");
            result["inventoryCountBefore"] = snapshots.Values.Sum(value => CountItem(new[] { new Inventory { Data = value.CreateColdData() } }, "Stick_Wood"));
            result["duplicateClaim"] = scope.Runtime.ClaimQuest(id, out _);
            return result;
        }
        finally
        {
            GameplayProgressEvents.SignalPublished -= observe;
            foreach (var entry in snapshots)
            {
                entry.Value.RestoreTo(entry.Key.Data);
                entry.Key.RefreshUI();
            }
            result["inventoryCountRestored"] = CountItem(inventories, "Stick_Wood");
        }
    }

    private static float CountItem(IEnumerable<Inventory> inventories, string id) => inventories
        .SelectMany(value => value.Data.itemSlots).Where(value => value?.itemData?.IDName == id)
        .Sum(value => value.itemData.Stack.Amount);
    #endregion

    #region 正式事件调度行动与取消
    private static JObject CaptureEvent(Player player, JObject args)
    {
        const string id = "survival_day_10_long_rain";
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        GameEventManager manager = UnityEngine.Object.FindObjectOfType<GameEventManager>() ??
            throw new InvalidOperationException("隔离世界的正式事件调度器未启动。");
        if (manager.ActiveEvents.Count != 0)
            throw new InvalidOperationException("已有其他活动事件，拒绝强制事件覆盖非自有行动。");
        GameEventDefinition definition = manager.Definitions.FirstOrDefault(value => value.Id == id && value.Enabled) ??
            throw new InvalidOperationException("正式目录缺少已启用的漫长降雨事件模板。");
        List<GameEventActionDefinition> originalActions = definition.Actions;
        GameEventExtensionDefinition originalTrigger = definition.Trigger;
        List<GameEventExtensionDefinition> originalConditions = definition.Conditions;
        var parameters = new JObject { ["signal"] = "lua.narrative", ["payload"] = new JObject { ["source"] = "actual-event-action", ["amount"] = 7 } };
        if (!new EmitSignalGameEventAction().Validate(parameters, out string validationReason))
            throw new InvalidOperationException(validationReason);
        GameEventSaveData saved = SaveDataMgr.TestExistingInstance.SaveData.GameEventData;
        bool hadProgress = saved.EventProgress.TryGetValue(id, out GameEventProgressSaveData progress);
        GameEventProgressSaveData original = hadProgress ? FastCloner.FastCloner.DeepClone(progress) : null;
        JToken originalProgress = original != null ? JObject.FromObject(original) : JValue.CreateNull();
        int started = 0, ended = 0;
        var signals = new JArray();
        Action<GameEventRuntimeNotification> onStart = value => { if (value.EventId == id) started++; };
        Action<GameEventRuntimeNotification> onEnd = value => { if (value.EventId == id) ended++; };
        Action<string, JObject> onSignal = (signal, payload) =>
        { if (signal == "lua.narrative") signals.Add(new JObject { ["signal"] = signal, ["payload"] = payload.DeepClone() }); };
        var result = new JObject();
        manager.EventStarted += onStart; manager.EventEnded += onEnd;
        manager.ConfiguredSignalRaised += onSignal;
        try
        {
            // 禁用定义不会进入正式目录，只借用未活动的已登记模板并重置本事件自有进度。
            saved.EventProgress.Remove(id);
            definition.Actions = new List<GameEventActionDefinition>
            { new() { Id = "lua_emit", Type = "signal.emit", Parameters = parameters } };
            result["template"] = new JObject
            {
                ["id"] = definition.Id, ["source"] = definition.SourceName, ["enabled"] = definition.Enabled,
                ["oncePerWorld"] = definition.OncePerWorld, ["durationDays"] = definition.DurationDays,
                ["triggerType"] = definition.Trigger.Type
            };
            result["stagedTriggerCount"] = saved.EventProgress.TryGetValue(id, out var staged) ? staged.TriggerCount : 0;
            result["ordinaryTrigger"] = manager.TryTriggerNow(id);
            result["forcedDuplicate"] = manager.TryForceTriggerNow(id);
            result["activeAfterStart"] = manager.IsEventActive(id);
            ActiveGameEventSaveData active = manager.ActiveEvents.FirstOrDefault(value => value.EventId == id);
            result["sourceWorld"] = active?.SourceWorldKey;
            result["currentWorld"] = SceneManager.GetActiveScene().name;
            result["actions"] = active == null ? new JArray() : JArray.FromObject(active.ActionStates);
            result["signals"] = signals.DeepClone();
            result["repeatTrigger"] = manager.TryTriggerNow(id, true);
            result["activeCount"] = manager.ActiveEvents.Count(value => value.EventId == id);
            result["cancelled"] = manager.CancelEvent(id);
            result["activeAfterCancel"] = manager.IsEventActive(id);
            result["cancelAgain"] = manager.CancelEvent(id);
            result["startedNotifications"] = started; result["endedNotifications"] = ended;
            result["storedTriggerCount"] = saved.EventProgress.TryGetValue(id, out var stored) ? stored.TriggerCount : 0;
            result["originalTriggerCount"] = original?.TriggerCount ?? 0;
            // 取消不抹掉一次性进度，GM 强制只能绕过限制，不能重复登记活动事件。
            result["postCancelRepeatTrigger"] = manager.TryTriggerNow(id);
            result["signalsAfterRejected"] = signals.Count;
            result["storedTriggerCountAfterRejected"] = saved.EventProgress.TryGetValue(id, out stored) ? stored.TriggerCount : 0;
            result["forcedAfterCancel"] = manager.TryForceTriggerNow(id);
            result["forcedActive"] = manager.IsEventActive(id);
            ActiveGameEventSaveData forced = manager.ActiveEvents.FirstOrDefault(value => value.EventId == id);
            result["forcedSourceWorld"] = forced?.SourceWorldKey;
            result["forcedActions"] = forced == null ? new JArray() : JArray.FromObject(forced.ActionStates);
            result["forcedPayload"] = string.IsNullOrWhiteSpace(forced?.TriggerPayloadJson)
                ? new JObject() : JObject.Parse(forced.TriggerPayloadJson);
            result["forcedSignals"] = signals.DeepClone();
            result["forcedCancel"] = manager.CancelEvent(id);
            result["forcedActiveAfterCancel"] = manager.IsEventActive(id);
            result["forcedCancelAgain"] = manager.CancelEvent(id);
            result["storedTriggerCountAfterForce"] = saved.EventProgress.TryGetValue(id, out stored) ? stored.TriggerCount : 0;
            result["startedNotificationsAfterForce"] = started;
            result["endedNotificationsAfterForce"] = ended;
            return result;
        }
        finally
        {
            try { if (manager.IsEventActive(id)) manager.CancelEvent(id); }
            finally
            {
                manager.EventStarted -= onStart; manager.EventEnded -= onEnd;
                manager.ConfiguredSignalRaised -= onSignal;
                definition.Actions = originalActions;
                if (hadProgress) saved.EventProgress[id] = original; else saved.EventProgress.Remove(id);
                result["selfEventCleaned"] = !manager.IsEventActive(id);
                result["definitionRestored"] = ReferenceEquals(definition.Actions, originalActions) &&
                    ReferenceEquals(definition.Trigger, originalTrigger) && ReferenceEquals(definition.Conditions, originalConditions);
                bool restoredExists = saved.EventProgress.TryGetValue(id, out GameEventProgressSaveData restored);
                result["progressRestored"] = restoredExists == hadProgress && (!hadProgress ||
                    JToken.DeepEquals(originalProgress, restored != null ? JObject.FromObject(restored) : JValue.CreateNull()));
                result["restoredTriggerCount"] = restored?.TriggerCount ?? 0;
            }
        }
    }
    #endregion
}
