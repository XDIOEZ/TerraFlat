#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using FlatWorld.Dialogue;
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
            var persisted = new NewPlayerGuideProgressStore(player.Data);
            return new JObject
            {
                ["active"] = guide.IsTutorialActive,
                ["stage"] = guide.CurrentStage.ToString(), ["completed"] = guide.IsTutorialCompleted,
                ["savedStage"] = persisted.CurrentStage.ToString(), ["savedCompleted"] = persisted.IsCompleted,
                ["eligible"] = persisted.IsEligible,
                ["inventoryOpened"] = persisted.HasMilestone(NewPlayerGuideIds.InventoryOpened),
                ["materialsGathered"] = persisted.HasMilestone(NewPlayerGuideIds.SurvivalMaterialsGathered),
                ["milestones"] = new JArray(persisted.Milestones),
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
        #endregion
    }
}
#endif
