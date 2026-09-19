using System;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_Food
{
    #region 可配置的食物吸收倍率

    [Serializable]
    public sealed class TaggedNutritionMultiplier
    {
        public string Tag;
        [Min(0f)] public float Multiplier = 1f;
    }

    [Tooltip("按食物 Tag 调整吸收效果；例如鸟类对 Seed 使用 3 倍，食物本身的基础营养不改变。")]
    public List<TaggedNutritionMultiplier> NutritionTagMultipliers = new();

    public static Nutrition ScaleConsumedNutrition(Item consumer, ItemData food, Nutrition nutrition)
    {
        Mod_Food module = consumer?.itemMods?.GetMod_ByID<Mod_Food>(ModText.Food);
        return module != null ? module.ScaleNutrition(food, nutrition) : nutrition;
    }

    private Nutrition ScaleNutrition(ItemData source, Nutrition nutrition)
    {
        float multiplier = 1f;
        if (NutritionTagMultipliers != null)
            foreach (TaggedNutritionMultiplier rule in NutritionTagMultipliers)
                if (rule != null && source?.Tags?.Contains(rule.Tag) == true)
                {
                    if (float.IsNaN(rule.Multiplier) || float.IsInfinity(rule.Multiplier) || rule.Multiplier < 0f)
                        throw new InvalidOperationException("食物 Tag 营养倍率必须是非负有限数。");
                    multiplier *= rule.Multiplier;
                }
        return Mathf.Approximately(multiplier, 1f) ? nutrition : new Nutrition(
            nutrition.Carbohydrates * multiplier, nutrition.Protein * multiplier, nutrition.Water * multiplier,
            nutrition.Fat * multiplier, nutrition.Vitamins * multiplier);
    }

    /// <summary>AI 啄食直接消费世界实物，不创建临时食物 Item，也不绕过冷载荷和掉落物的原子数量事务。</summary>
    public bool TryEatDroppedFood(DroppedItemHandle handle, string tag, float reach)
    {
        if (item == null || Data?.nutrition == null || !DroppedItemService.TryGetSnapshot(handle, out ItemData snapshot)) return false;
        ModData_FoodData food = null;
        if (snapshot.ModuleDataDic != null)
            foreach (ModuleData data in snapshot.ModuleDataDic.Values)
                if (data is ModData_FoodData candidate) { food = candidate; break; }
        if (food?.EnsureFoodData()?.nutrition == null) return false;
        Nutrition gain = ScaleNutrition(snapshot, food.EnsureFoodData().nutrition);
        if (!DroppedItemService.TryConsumeTagged(handle, item.transform.position, reach, tag, 1)) return false;
        Data.nutrition = Data.nutrition + gain;
        NotifyStateChanged();
        return true;
    }

    #endregion
}
