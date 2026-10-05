using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>钓具只报告事件，玩家气泡留在表现程序集，避免玩法反向依赖 UI。</summary>
    public static class FishingSpeechFeedback
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            Mod_FishingRod.FeedbackRequested -= HandleFeedback;
            Mod_FishingRod.FeedbackRequested += HandleFeedback;
        }

        private static void HandleFeedback(Item owner, string text)
        {
            if (owner is not Player player || !player.IsLocalProfile) return;
            player.GetComponent<CharacterSoliloquyController>()?.Say(text,
                CharacterSpeechPriority.Player, topic: "fishing.rig");
        }
    }
}
