using UnityEngine;

namespace FlatWorld.Dialogue
{
    public static class StaminaSpeechFeedback
    {
        #region 体力不足台词桥接
        internal const string ExhaustedEntryId = "stamina.work.exhausted";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            Mod_Stamina.ConsumptionRejected -= HandleConsumptionRejected;
            Mod_Stamina.ConsumptionRejected += HandleConsumptionRejected;
        }

        // 仅在本地玩家主动工作被体力不足拒绝时请求气泡。
        private static void HandleConsumptionRejected(Mod_Stamina stamina, string sourceId)
        {
            if (stamina == null || stamina.item is not Player player || !player.IsLocalProfile)
                return;

            player.GetComponent<ConfiguredSpeechProvider>()?.TrySpeakEntry(ExhaustedEntryId);
        }
        #endregion
    }
}
