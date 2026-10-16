using FlatWorld.Audio;
using FlatWorld.Gameplay.Building;
using FlatWorld.Gameplay.Progress;
using UnityEngine;

namespace FlatWorld.Gameplay.Presentation.Audio
{
    /// <summary>把已提交的玩法事务转成可听反馈，不参与任何业务事务与回滚。</summary>
    internal static class GameplayFeedbackAudioBridge
    {
        #region 玩法音效事件路由

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            GameplayProgressEvents.SignalPublished -= OnProgress;
            GameplayProgressEvents.SignalPublished += OnProgress;
            BuildingPlacementFeedbackEvents.PlacementRejected -= OnPlacementRejected;
            BuildingPlacementFeedbackEvents.PlacementRejected += OnPlacementRejected;
            Mod_FishingRod.SoundRequested -= OnFishingSound;
            Mod_FishingRod.SoundRequested += OnFishingSound;
        }

        private static void OnProgress(GameplayProgressSignal signal)
        {
            if (signal.Actor == null || !signal.Actor.IsLocalProfile)
                return;

            string cueId = signal.Type switch
            {
                GameplayProgressTypes.BuildingPlaced => AudioEventIds.BuildingPlace,
                GameplayProgressTypes.CraftSucceeded => AudioEventIds.CraftComplete,
                GameplayProgressTypes.SmeltSucceeded => AudioEventIds.SmeltComplete,
                GameplayProgressTypes.FireSeedCreated => AudioEventIds.FireSeed,
                GameplayProgressTypes.FurnaceIgnited => AudioEventIds.FurnaceIgnite,
                _ => null
            };

            // 只有最终成功事件才发声；同一事务的多产物通过 Cue 冷却合为一次提示。
            if (cueId != null)
                AudioService.Instance.PlayAt(cueId, signal.Actor.transform.position);
        }

        private static void OnPlacementRejected(Player actor, BuildingPlacementFailureReason reason)
        {
            if (actor != null && actor.IsLocalProfile)
                AudioService.Instance.PlayAt(AudioEventIds.BuildingReject, actor.transform.position);
        }

        private static void OnFishingSound(Player actor, FishingSoundAction action)
        {
            if (actor == null || !actor.IsLocalProfile)
                return;

            string cueId = action switch
            {
                FishingSoundAction.Cast => AudioEventIds.FishingCast,
                FishingSoundAction.Hook => AudioEventIds.FishingHook,
                FishingSoundAction.Reel => AudioEventIds.FishingReel,
                _ => null
            };
            if (cueId != null)
                AudioService.Instance.PlayAt(cueId, actor.transform.position);
        }

        #endregion
    }
}
