using System;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>把完整食用事件转换为本地玩家的一次性口味或临期台词。</summary>
    public static class FoodConsumptionSpeechFeedback
    {
        private const string TasteEntryPrefix = "food.taste.";
        private const string SpoilageEntryPrefix = "food.spoilage.";
        private const float SpoilageWarningRatio = 0.2f;

        #region 完整食用桥接

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            FoodConsumptionService.ConsumptionCompleted -= HandleConsumptionCompleted;
            FoodConsumptionService.ConsumptionCompleted += HandleConsumptionCompleted;
        }

        private static void HandleConsumptionCompleted(FoodConsumeResult result)
        {
            if (!(result.Consumer is Item player && ActorProfileRegistry.IsLocallyControlled(player)) ||
                string.IsNullOrWhiteSpace(result.ConsumedItemId))
            {
                return;
            }

            ConfiguredSpeechProvider provider = player.GetComponent<ConfiguredSpeechProvider>();
            if (provider == null)
                return;

            string tasteEntryId = TasteEntryPrefix + result.ConsumedItemId;
            string spoilageEntryId = SpoilageEntryPrefix + result.ConsumedItemId;
            string entryId = SelectEntryId(
                result.ConsumedItemId,
                result.SpoilageRemainingSeconds,
                result.SpoilageIntervalSeconds,
                provider.IsEntryCompleted(spoilageEntryId),
                provider.IsEntryCompleted(tasteEntryId));
            if (!string.IsNullOrEmpty(entryId))
                provider.TrySpeakEntry(entryId);
        }

        #endregion

        #region 台词选择

        internal static string SelectEntryId(
            string itemId,
            float spoilageRemainingSeconds,
            float spoilageIntervalSeconds,
            bool spoilageReminderCompleted,
            bool tasteDescriptionCompleted)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return string.Empty;

            if (IsWithinSpoilageWarningWindow(spoilageRemainingSeconds, spoilageIntervalSeconds) &&
                !spoilageReminderCompleted)
            {
                return SpoilageEntryPrefix + itemId;
            }

            return tasteDescriptionCompleted ? string.Empty : TasteEntryPrefix + itemId;
        }

        internal static bool IsWithinSpoilageWarningWindow(float remainingSeconds, float intervalSeconds)
        {
            return float.IsFinite(remainingSeconds) && float.IsFinite(intervalSeconds) &&
                   intervalSeconds > 0f && remainingSeconds <= intervalSeconds * SpoilageWarningRatio;
        }

        #endregion
    }
}
