using FlatWorld.Gameplay.Progress;
using Newtonsoft.Json.Linq;

namespace FlatWorld.Guide
{
    /// <summary>只保存教程本地资格，目标进度由玩家任务运行时保存。</summary>
    public static class NewPlayerGuideProgressStore
    {
        #region 本地资格

        public const string TutorialNamespace = "flatworld.tutorial";

        public static bool HasEligibility(Data_Player playerData)
        {
            return playerData != null &&
                   ItemSpecialDataJsonStore.ReadNamespace(playerData, TutorialNamespace).Value<bool?>("eligible") == true;
        }

        public static void EstablishEligibility(Data_Player playerData)
        {
            if (playerData == null || HasEligibility(playerData))
                return;

            JObject data = ItemSpecialDataJsonStore.ReadNamespace(playerData, TutorialNamespace);
            data["eligible"] = true;
            ItemSpecialDataJsonStore.WriteNamespace(playerData, TutorialNamespace, data);
        }

        #endregion
    }
}
