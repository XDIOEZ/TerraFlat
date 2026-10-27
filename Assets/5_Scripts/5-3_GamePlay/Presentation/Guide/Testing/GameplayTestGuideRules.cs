#if UNITY_EDITOR || DEVELOPMENT_BUILD
using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FlatWorld.Guide
{
    /// <summary>验证教程资格与其他玩家数据相互独立。</summary>
    public static class GameplayTestGuideRules
    {
        #region 测试操作注册

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterRuntime() => Register();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditor() => Register();
#endif

        public static void Register() => GameplayTestSystemProbe.Register("guide", "eligibility", CaptureEligibility);

        #endregion

        #region 正式资格与数据保留

        private static JObject CaptureEligibility(JObject args)
        {
            var player = new Data_Player { ItemSpecialData = "{\"other\":{\"keep\":42},\"flatworld.tutorial\":{\"custom\":7}}" };
            bool before = NewPlayerGuideProgressStore.HasEligibility(player);
            if (GameplayTestSystemProbe.Flag(args, "eligible", true))
                NewPlayerGuideProgressStore.EstablishEligibility(player);
            string once = player.ItemSpecialData;
            if (GameplayTestSystemProbe.Flag(args, "eligible", true))
                NewPlayerGuideProgressStore.EstablishEligibility(player);
            JObject root = ItemSpecialDataJsonStore.ReadRoot(player.ItemSpecialData);
            return new JObject
            {
                ["before"] = before,
                ["eligible"] = NewPlayerGuideProgressStore.HasEligibility(player),
                ["unchangedOnRepeat"] = once == player.ItemSpecialData,
                ["other"] = root["other"],
                ["custom"] = root[NewPlayerGuideProgressStore.TutorialNamespace]?["custom"]
            };
        }

        #endregion
    }
}
#endif
