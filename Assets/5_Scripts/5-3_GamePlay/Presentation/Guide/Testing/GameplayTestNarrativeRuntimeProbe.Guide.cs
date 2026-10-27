#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Dialogue;
using FlatWorld.Gameplay.Progress;
using FlatWorld.Gameplay.Quests;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Guide
{
    /// <summary>任务和教程共用实际背包与拾取事务，不直接发布虚假的玩法成功事件。</summary>
    public static class GameplayTestNarrativeGuideRuntimeProbe
    {
        #region 真实操作自注册
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif
        private static void Register()
        {
            GameplayTestRuntimeProbe.Register("guide", "inventory_quest", CaptureInventory);
            GameplayTestRuntimeProbe.Register("guide", "pickup_quest", CapturePickup);
            GameplayTestRuntimeProbe.Register("guide", "quest_progress", CaptureQuestProgress);
            GameplayTestRuntimeProbe.Register("guide", "material_threshold", CaptureMaterialThreshold);
        }
        #endregion

        #region 已加载教程与对话 Facts
        private static NewPlayerGuideController RequireGuide(Player player)
        {
            NewPlayerGuideController guide = player.GetComponentInChildren<NewPlayerGuideController>(true);
            if (guide == null || !guide.isActiveAndEnabled || !guide.IsTutorialActive)
                throw new InvalidOperationException("本轮主角没有加载具备新手资格的正式教程控制器。");
            return guide;
        }

        private static JObject Sample(Player player, NewPlayerGuideController guide)
        {
            var context = new CharacterSpeechContext(player.transform, CharacterSpeechTrigger.Debug, Time.unscaledTime);
            guide.Contribute(context);
            QuestProgressSaveDocument document = QuestProgressStore.Load(player.Data);
            if (!document.Quests.TryGetValue(NewPlayerGuideIds.QuestId, out QuestProgressSaveRecord record))
                throw new InvalidOperationException("正式教程任务尚未接取。");
            var milestones = new JArray();
            for (int index = 0; index < NewPlayerGuideIds.OrderedMilestones.Length; index++)
            {
                string milestone = NewPlayerGuideIds.OrderedMilestones[index];
                if (record.ObjectiveProgress.TryGetValue($"{(NewPlayerGuideStage)index}/{milestone}", out float value) && value >= 1f)
                    milestones.Add(milestone);
            }
            return new JObject
            {
                ["active"] = guide.IsTutorialActive,
                ["stage"] = guide.CurrentStage.ToString(), ["completed"] = guide.IsTutorialCompleted,
                ["savedStage"] = record.Status == QuestStatus.Completed ? nameof(NewPlayerGuideStage.Completed) : record.CurrentStageId,
                ["savedCompleted"] = record.Status == QuestStatus.Completed,
                ["eligible"] = NewPlayerGuideProgressStore.HasEligibility(player.Data),
                ["inventoryOpened"] = milestones.Any(value => value.Value<string>() == NewPlayerGuideIds.InventoryOpened),
                ["materialsGathered"] = milestones.Any(value => value.Value<string>() == NewPlayerGuideIds.SurvivalMaterialsGathered),
                ["milestones"] = milestones,
                ["facts"] = JObject.FromObject(context.Facts)
            };
        }

        private static JObject CaptureInventory(Player player, JObject args)
        {
            NewPlayerGuideController guide = RequireGuide(player);
            JObject before = Sample(player, guide);
            JObject result = GameplayTestNarrativeRuntimeProbe.CaptureInventoryQuest(player);
            result["guideBefore"] = before;
            result["guideAfter"] = Sample(player, guide);
            return result;
        }

        private static JObject CapturePickup(Player player, JObject args)
        {
            NewPlayerGuideController guide = RequireGuide(player);
            JObject before = Sample(player, guide);
            JObject result = GameplayTestNarrativeRuntimeProbe.CapturePickupQuest(player);
            result["guideBefore"] = before;
            result["guideAfter"] = Sample(player, guide);
            return result;
        }

        // 在正式玩家任务运行时采样乱序、幂等、保存和普通任务默认规则。
        private static JObject CaptureQuestProgress(Player player, JObject args)
        {
            NewPlayerGuideController guide = RequireGuide(player);
            PlayerQuestRuntime runtime = RequireQuestRuntime(player);
            const string ordinaryQuestId = "flatworld:debug_craft_and_build";
            QuestProgressSaveDocument original = QuestProgressStore.Load(player.Data);
            QuestProgressSaveDocument staged = QuestProgressStore.Load(player.Data);
            staged.Quests.Remove(NewPlayerGuideIds.QuestId);
            staged.Quests.Remove(ordinaryQuestId);
            QuestProgressStore.Save(player.Data, staged);
            int emittedSignals = 0;
            Action<GameplayProgressSignal> observe = signal => { if (signal.Actor == player) emittedSignals++; };
            GameplayProgressEvents.SignalPublished += observe;
            try
            {
                if (!runtime.Initialize(out string reason)) throw new InvalidOperationException(reason);
                bool accepted = runtime.AcceptQuest(NewPlayerGuideIds.QuestId, out _);
                bool ordinaryAccepted = runtime.AcceptQuest(ordinaryQuestId, out _);
                runtime.HandleSignal(new GameplayProgressSignal(player, GameplayProgressTypes.BuildingPlaced, "probe", 1f));
                runtime.HandleSignal(new GameplayProgressSignal(player, GameplayProgressTypes.CraftSucceeded, "ChippedTool", 1f));
                runtime.TryGetSnapshot(ordinaryQuestId, out QuestSnapshot ordinaryAfterCraft);
                runtime.HandleSignal(new GameplayProgressSignal(player, GameplayProgressTypes.BuildingPlaced, "probe", 1f));
                runtime.TryGetSnapshot(ordinaryQuestId, out QuestSnapshot ordinaryAfterBuild);

                emittedSignals = 0;
                SendMilestone(runtime, player, NewPlayerGuideIds.BonfireIgnited);
                SendMilestone(runtime, player, NewPlayerGuideIds.BonfireIgnited);
                SendMilestone(runtime, player, "probe-unknown");
                JObject early = Sample(player, guide);
                QuestProgressSaveRecord earlyRecord = QuestProgressStore.Load(player.Data).Quests[NewPlayerGuideIds.QuestId];
                float earlyValue = earlyRecord.ObjectiveProgress[$"{NewPlayerGuideStage.IgniteBonfire}/{NewPlayerGuideIds.BonfireIgnited}"];
                for (int index = 0; index < NewPlayerGuideIds.OrderedMilestones.Length - 1; index++)
                    SendMilestone(runtime, player, NewPlayerGuideIds.OrderedMilestones[index]);
                SendMilestone(runtime, player, NewPlayerGuideIds.BonfireIgnited);
                QuestProgressSaveRecord completed = QuestProgressStore.Load(player.Data).Quests[NewPlayerGuideIds.QuestId];
                int guideEmittedSignals = emittedSignals;
                if (!runtime.Initialize(out reason)) throw new InvalidOperationException(reason);
                JObject restored = Sample(player, guide);
                QuestProgressSaveDocument orderedDocument = QuestProgressStore.Load(player.Data);
                orderedDocument.Quests.Remove(NewPlayerGuideIds.QuestId);
                QuestProgressStore.Save(player.Data, orderedDocument);
                if (!runtime.Initialize(out reason) || !runtime.AcceptQuest(NewPlayerGuideIds.QuestId, out reason))
                    throw new InvalidOperationException(reason);
                var orderedStages = new JArray();
                foreach (string milestone in NewPlayerGuideIds.OrderedMilestones)
                {
                    SendMilestone(runtime, player, milestone);
                    orderedStages.Add(guide.CurrentStage.ToString());
                }
                QuestCatalog.TryGet(NewPlayerGuideIds.QuestId, out QuestDefinition definition);
                QuestCatalog.TryGet(ordinaryQuestId, out QuestDefinition ordinaryDefinition);
                return new JObject
                {
                    ["accepted"] = accepted, ["ordinaryAccepted"] = ordinaryAccepted,
                    ["early"] = early, ["earlyValue"] = earlyValue,
                    ["restored"] = restored, ["completionCount"] = completed.CompletionCount,
                    ["orderedStages"] = orderedStages,
                    ["objectiveCount"] = completed.ObjectiveProgress.Count,
                    ["hidden"] = definition?.HiddenFromJournal == true &&
                                 !runtime.GetSnapshots().Any(snapshot => snapshot.QuestId == NewPlayerGuideIds.QuestId),
                    ["futureTracking"] = definition?.TrackFutureStageSignals == true,
                    ["emptyRewards"] = definition?.Rewards?.Count == 0,
                    ["ordinaryFutureTracking"] = ordinaryDefinition?.TrackFutureStageSignals == true,
                    ["ordinaryStageAfterCraft"] = ordinaryAfterCraft?.CurrentStageId,
                    ["ordinaryStatusAfterBuild"] = ordinaryAfterBuild?.Status.ToString(),
                    ["emittedSignals"] = guideEmittedSignals
                };
            }
            finally
            {
                GameplayProgressEvents.SignalPublished -= observe;
                QuestProgressStore.Save(player.Data, original);
                if (!runtime.Initialize(out string reason))
                    throw new InvalidOperationException("教程任务现场恢复失败：" + reason);
            }
        }

        private static void SendMilestone(PlayerQuestRuntime runtime, Player player, string milestone)
        {
            runtime.HandleSignal(new GameplayProgressSignal(player, NewPlayerGuideIds.MilestoneSignal, milestone, 1f));
        }

        private static PlayerQuestRuntime RequireQuestRuntime(Player player)
        {
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            QuestManager manager = UnityEngine.Object.FindObjectOfType<QuestManager>();
            if (manager == null || !manager.TryGetRuntime(player, out PlayerQuestRuntime runtime) || !runtime.IsEnabled)
                throw new InvalidOperationException("主角正式任务运行时尚未就绪。");
            return runtime;
        }

        // 使用真实拾取和库存检查材料必须同时满足，达标后再消耗也不撤销。
        private static JObject CaptureMaterialThreshold(Player player, JObject args)
        {
            NewPlayerGuideController guide = RequireGuide(player);
            PlayerQuestRuntime runtime = RequireQuestRuntime(player);
            Mod_ItemPicker picker = player.itemMods?.GetMod_ByID<Mod_ItemPicker>(ModText.Picker) ??
                throw new InvalidOperationException("主角正式拾取模块缺失。");
            Mod_Inventory bagMod = player.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag);
            Inventory bag = GameplayTestNarrativeRuntimeProbe.RequireBag(player);
            Mod_HotBar hotbar = player.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
            var candidates = new List<Inventory>(bagMod.InventoryInstances);
            candidates.Add(hotbar?.RuntimeInventory);
            candidates.AddRange(picker.AddTargetInventories.Select(value => value?.GetDefaultTargetInventory()));
            Inventory[] inventories = candidates.Where(value => value?.Data?.itemSlots != null)
                .GroupBy(value => value.Data).Select(group => group.First()).ToArray();
            var snapshots = inventories.ToDictionary(value => value, value => InventoryInstanceSnapshot.Capture(value.Data));
            QuestProgressSaveDocument original = QuestProgressStore.Load(player.Data);
            bool wasOpen = bag.basePanel != null && bag.basePanel.IsOpen();
            int multiplier = picker.PickupAmountMultiplier;
            try
            {
                if (wasOpen) bag.SwitchUI();
                picker.PickupAmountMultiplier = 1;
                ClearInventoryItems(inventories, null);
                QuestProgressSaveDocument staged = QuestProgressStore.Load(player.Data);
                staged.Quests.Remove(NewPlayerGuideIds.QuestId);
                QuestProgressStore.Save(player.Data, staged);
                if (!runtime.Initialize(out string reason) || !runtime.AcceptQuest(NewPlayerGuideIds.QuestId, out reason))
                    throw new InvalidOperationException("无法重建教程任务：" + reason);

                bool sticksPicked = TryPickupResource(picker, NewPlayerGuideIds.StickWood, 3f);
                JObject sticksOnly = Sample(player, guide);
                ClearInventoryItems(inventories, NewPlayerGuideIds.StickWood);
                bool logsPicked = TryPickupResource(picker, NewPlayerGuideIds.Log, 3f);
                bool leafPicked = TryPickupResource(picker, NewPlayerGuideIds.Leaf, 1f);
                JObject nonSimultaneous = Sample(player, guide);
                bool twoSticksPicked = TryPickupResource(picker, NewPlayerGuideIds.StickWood, 2f);
                JObject partial = Sample(player, guide);
                bool finalStickPicked = TryPickupResource(picker, NewPlayerGuideIds.StickWood, 1f);
                JObject enough = Sample(player, guide);
                ClearInventoryItems(inventories, null);
                bag.SwitchUI();
                JObject afterConsumed = Sample(player, guide);
                return new JObject
                {
                    ["pickedAll"] = sticksPicked && logsPicked && leafPicked && twoSticksPicked && finalStickPicked,
                    ["sticksOnly"] = sticksOnly, ["nonSimultaneous"] = nonSimultaneous,
                    ["partial"] = partial, ["enough"] = enough, ["afterConsumed"] = afterConsumed
                };
            }
            finally
            {
                picker.PickupAmountMultiplier = multiplier;
                foreach (KeyValuePair<Inventory, InventoryInstanceSnapshot> entry in snapshots)
                {
                    entry.Value.RestoreTo(entry.Key.Data);
                    entry.Key.RefreshUI();
                }
                if ((bag.basePanel != null && bag.basePanel.IsOpen()) != wasOpen) bag.SwitchUI();
                QuestProgressStore.Save(player.Data, original);
                if (!runtime.Initialize(out string reason))
                    throw new InvalidOperationException("教程材料现场恢复失败：" + reason);
            }
        }

        private static bool TryPickupResource(Mod_ItemPicker picker, string itemId, float amount)
        {
            ItemData item = GameRes.ExistingInstance?.CreateItemData(itemId);
            if (item?.Stack == null) throw new InvalidOperationException("无法创建教程材料：" + itemId);
            item.Stack.Amount = amount;
            return picker.TryAcceptNetworkPickup(item, false);
        }

        private static void ClearInventoryItems(IEnumerable<Inventory> inventories, string itemId)
        {
            foreach (Inventory inventory in inventories)
            {
                for (int index = 0; index < inventory.Data.itemSlots.Count; index++)
                {
                    ItemSlot slot = inventory.Data.itemSlots[index];
                    if (slot?.itemData != null && (itemId == null || slot.itemData.IDName == itemId))
                        inventory.Data.RemoveItemAll(slot, index);
                }
                inventory.RefreshUI();
            }
        }
        #endregion
    }
}
#endif
