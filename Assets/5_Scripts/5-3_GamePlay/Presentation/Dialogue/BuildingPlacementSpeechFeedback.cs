using FlatWorld.Gameplay.Building;
using FlatWorld.Localization;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 把本地玩家的建筑放置失败事件转换为角色气泡，不让建筑玩法依赖对话表现层。
    /// </summary>
    public static class BuildingPlacementSpeechFeedback
    {
        // 非法放置时显示给玩家的本地化原文。
        private const string InvalidPlacementSpeechText = "这个建筑不能放置在那里";
        // 非法放置提示使用稳定话题，便于台词系统识别来源。
        private const string InvalidPlacementSpeechTopic = "building.invalid-placement";

        /// <summary>在玩法静态事件完成重置后登记一次表现层监听。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            BuildingPlacementFeedbackEvents.PlacementRejected -= HandlePlacementRejected;
            BuildingPlacementFeedbackEvents.PlacementRejected += HandlePlacementRejected;
        }

        /// <summary>只为触发事件的本地玩家显示非法放置气泡。</summary>
        private static void HandlePlacementRejected(Player actor)
        {
            if (actor == null || !actor.IsLocalProfile)
                return;

            CharacterSoliloquyController speechController =
                actor.GetComponent<CharacterSoliloquyController>();
            if (speechController == null)
                return;

            speechController.Say(
                FlatWorldLocalizationService.GetUiText(InvalidPlacementSpeechText),
                CharacterSpeechPriority.Player,
                topic: InvalidPlacementSpeechTopic);
        }
    }
}
