using System.Globalization;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 只负责读取 Mod_Food，并向自言自语上下文贡献稳定的饥饿与水分 Facts。
    /// 台词、优先级、显示时间和冷却均由 JSON 配置负责。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HungerSpeechProvider : MonoBehaviour, ICharacterSpeechContextContributor
    {
        #region 饥饿等级

        private enum HungerTier
        {
            Healthy,
            Low,
            Critical,
            Starving
        }

        private enum HydrationTier
        {
            Healthy,
            Thirsty,
            VeryThirsty,
            Dehydrated
        }

        #endregion

        #region 配置与缓存

        [Header("阈值")]
        [SerializeField, Range(0f, 1f)] private float lowThreshold = 0.4f;
        [SerializeField, Range(0f, 1f)] private float criticalThreshold = 0.15f;
        [SerializeField, Range(0f, 1f)] private float thirstyThreshold = 0.4f;
        [SerializeField, Range(0f, 1f)] private float veryThirstyThreshold = 0.15f;

        private Item actorItem;
        private Mod_Food food;

        #endregion

        public int ContextOrder => 100;

        #region Fact 贡献

        public void Contribute(CharacterSpeechContext context)
        {
            if (!TryResolveFood())
                return;

            float rate = GetHungerRate();
            HungerTier tier = GetTier(rate);
            context.SetFact(
                CharacterSpeechFacts.HungerRate,
                rate.ToString("0.000", CultureInfo.InvariantCulture));
            context.SetFact(CharacterSpeechFacts.HungerTier, tier.ToString());
            context.SetFact(
                CharacterSpeechFacts.HungerIsTakingDamage,
                (tier == HungerTier.Starving).ToString());

            float hydrationRate = GetHydrationRate();
            HydrationTier hydrationTier = GetHydrationTier(hydrationRate);
            context.SetFact(
                CharacterSpeechFacts.HydrationRate,
                hydrationRate.ToString("0.000", CultureInfo.InvariantCulture));
            context.SetFact(CharacterSpeechFacts.HydrationTier, hydrationTier.ToString());
            context.SetFact(
                CharacterSpeechFacts.HydrationIsTakingDamage,
                (hydrationTier == HydrationTier.Dehydrated).ToString());
        }

        #endregion

        #region 饥饿状态读取

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

        private float GetHydrationRate()
        {
            Nutrition nutrition = food.Data.nutrition;
            if (nutrition.Max_Water <= 0f)
                return 0f;

            return Mathf.Clamp01(nutrition.Water / nutrition.Max_Water);
        }

        private HydrationTier GetHydrationTier(float hydrationRate)
        {
            // 水分耗尽后 Mod_Food 会按固定间隔扣血，此档与真实伤害条件保持一致。
            if (food.Data.nutrition.Water <= 0.001f)
                return HydrationTier.Dehydrated;
            if (hydrationRate <= Mathf.Min(thirstyThreshold, veryThirstyThreshold))
                return HydrationTier.VeryThirsty;
            if (hydrationRate <= Mathf.Max(thirstyThreshold, veryThirstyThreshold))
                return HydrationTier.Thirsty;
            return HydrationTier.Healthy;
        }

        #endregion
    }
}
