using System;
using System.Globalization;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 只负责把 Mod_Food 状态翻译成饥饿台词，不依赖任何 UI。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HungerSpeechProvider :
        MonoBehaviour,
        ICharacterSpeechContextContributor,
        ICharacterSpeechProvider,
        ICharacterSpeechTriggerSource
    {
        private enum HungerTier
        {
            Healthy,
            Low,
            Critical,
            Starving
        }

        [Header("阈值")]
        [SerializeField, Range(0f, 1f)] private float lowThreshold = 0.4f;
        [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.15f;

        [Header("重复间隔")]
        [SerializeField, Min(1f)] private float lowRepeatCooldown = 50f;
        [SerializeField, Min(1f)] private float criticalRepeatCooldown = 35f;
        [SerializeField, Min(1f)] private float starvingRepeatCooldown = 22f;

        private Item actorItem;
        private Mod_Food food;
        private HungerTier previousTier = HungerTier.Healthy;
        private float nextAllowedSpeechAt;

        public int ContextOrder => 100;
        public int ProviderOrder => 100;

        public void Contribute(CharacterSpeechContext context)
        {
            if (!TryResolveFood())
                return;

            float rate = GetHungerRate();
            HungerTier tier = GetTier(rate);
            context.SetFact(
                "hunger.rate",
                rate.ToString("0.000", CultureInfo.InvariantCulture));
            context.SetFact("hunger.tier", tier.ToString());
            context.SetFact(
                "hunger.isTakingDamage",
                (tier == HungerTier.Starving).ToString());
        }

        public bool CanProvide(CharacterSpeechContext context)
        {
            if (!TryResolveFood())
                return false;

            HungerTier tier = GetTier(GetHungerRate());
            return tier != HungerTier.Healthy &&
                   Time.unscaledTime >= nextAllowedSpeechAt;
        }

        public void RequestSpeech(
            CharacterSpeechContext context,
            Action<CharacterSpeechRequest> onCompleted)
        {
            if (!TryResolveFood())
            {
                onCompleted?.Invoke(null);
                return;
            }

            HungerTier tier = GetTier(GetHungerRate());
            CharacterSpeechRequest request = BuildRequest(tier);
            if (request != null)
                RecordSpeech(tier);

            onCompleted?.Invoke(request);
        }

        public CharacterSpeechRequest PollTriggeredSpeech(CharacterSpeechContext context)
        {
            if (!TryResolveFood())
                return null;

            HungerTier currentTier = GetTier(GetHungerRate());
            bool becameMoreSevere = currentTier > previousTier;
            previousTier = currentTier;

            if (!becameMoreSevere)
                return null;

            CharacterSpeechRequest request = BuildRequest(currentTier);
            if (request != null)
                RecordSpeech(currentTier);
            return request;
        }

        private bool TryResolveFood()
        {
            if (food != null && food.Data != null)
                return true;

            actorItem ??= GetComponentInParent<Item>();
            if (actorItem == null || !actorItem.IsInitialized || actorItem.itemMods == null)
                return false;

            food = actorItem.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);
            return food != null && food.Data != null;
        }

        private float GetHungerRate()
        {
            return Mathf.Clamp01(food.Data.nutrition.GetFoodRate());
        }

        private HungerTier GetTier(float hungerRate)
        {
            Nutrition nutrition = food.Data.nutrition;

            // Mod_Food 在 Protein 为 0 时开始持续扣血，这里与真实伤害条件保持一致。
            if (nutrition.Protein <= 0.001f)
                return HungerTier.Starving;
            if (hungerRate <= Mathf.Min(lowThreshold, criticalThreshold))
                return HungerTier.Critical;
            if (hungerRate <= Mathf.Max(lowThreshold, criticalThreshold))
                return HungerTier.Low;
            return HungerTier.Healthy;
        }

        private static CharacterSpeechRequest BuildRequest(HungerTier tier)
        {
            switch (tier)
            {
                case HungerTier.Low:
                    return new CharacterSpeechRequest(
                        "我有点饿了",
                        "hunger.low",
                        CharacterSpeechPriority.Need,
                        3.2f);
                case HungerTier.Critical:
                    return new CharacterSpeechRequest(
                        "我快饿扁了",
                        "hunger.critical",
                        CharacterSpeechPriority.Critical,
                        3.5f);
                case HungerTier.Starving:
                    return new CharacterSpeechRequest(
                        "我撑不了多久",
                        "hunger.starving",
                        CharacterSpeechPriority.Emergency,
                        3.8f);
                default:
                    return null;
            }
        }

        private void RecordSpeech(HungerTier tier)
        {
            float cooldown;
            switch (tier)
            {
                case HungerTier.Starving:
                    cooldown = starvingRepeatCooldown;
                    break;
                case HungerTier.Critical:
                    cooldown = criticalRepeatCooldown;
                    break;
                default:
                    cooldown = lowRepeatCooldown;
                    break;
            }

            nextAllowedSpeechAt = Time.unscaledTime + Mathf.Max(1f, cooldown);
        }
    }
}
