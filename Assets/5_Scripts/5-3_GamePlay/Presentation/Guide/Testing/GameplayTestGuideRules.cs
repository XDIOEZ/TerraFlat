#if UNITY_EDITOR || DEVELOPMENT_BUILD
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Guide
{
    /// <summary>教程只在独立玩家 DTO 中推进里程碑，不接触真实角色。</summary>
    public static class GameplayTestGuideRules
    {
        #region 测试操作注册

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterRuntime() => Register();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif

        public static void Register() => GameplayTestSystemProbe.Register("guide", "milestones", CaptureMilestones);

        #endregion

        #region 正式进度与内存恢复

        private static JObject CaptureMilestones(JObject args)
        {
            int count = (int)GameplayTestSystemProbe.Number(args, "count", 0);
            var player = new Data_Player { ItemSpecialData = "{\"other\":{\"keep\":42}}" };
            var progress = new NewPlayerGuideProgressStore(player,
                establishEligibility: GameplayTestSystemProbe.Flag(args, "eligible", true));
            bool allFirstAccepted = true;
            int accepted = 0;
            for (int index = 0; index < count && index < NewPlayerGuideIds.OrderedMilestones.Length; index++)
            {
                bool added = progress.MarkMilestone(NewPlayerGuideIds.OrderedMilestones[index]);
                allFirstAccepted &= added;
                if (added) accepted++;
            }
            var extraAccepted = new JArray();
            if (args["extra"] is JArray extra)
                foreach (JToken token in extra) extraAccepted.Add(progress.MarkMilestone(token.Value<string>()));
            bool duplicateAccepted = count > 0 && progress.MarkMilestone(NewPlayerGuideIds.OrderedMilestones[0]);
            bool unknownAccepted = progress.MarkMilestone("probe-unknown");
            var restored = new NewPlayerGuideProgressStore(player);
            return new JObject
            {
                ["accepted"] = accepted, ["allFirstAccepted"] = allFirstAccepted,
                ["extraAccepted"] = extraAccepted,
                ["duplicateAccepted"] = duplicateAccepted, ["unknownAccepted"] = unknownAccepted,
                ["stage"] = progress.CurrentStage.ToString(), ["restoredStage"] = restored.CurrentStage.ToString(),
                ["completed"] = progress.IsCompleted, ["eligible"] = restored.IsEligible,
                ["milestones"] = restored.Milestones.Count,
                ["other"] = ItemSpecialDataJsonStore.ReadRoot(player.ItemSpecialData)["other"]
            };
        }

        #endregion
    }
}
#endif
