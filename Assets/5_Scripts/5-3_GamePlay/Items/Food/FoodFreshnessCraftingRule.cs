using System.Collections.Generic;
using UnityEngine;

/// <summary>保鲜加工继承最不新鲜原料的剩余比例，延长保质期但不能把临期肉重置成全新肉。</summary>
public sealed class FoodFreshnessCraftingRule : ICraftingOutputRule
{
    /// <summary>作为独立产物规则接入制作事务。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register() => CraftingOutputRules.Register("food.preserve_freshness", new FoodFreshnessCraftingRule());

    /// <summary>只处理带保鲜标签的产物，读取实际扣除的原料而不是整个背包。</summary>
    public bool Prepare(Inventory input, CraftingRecipeMatch match, IReadOnlyList<ItemData> outputs, out string error)
    {
        error = null;
        bool applies = false;
        foreach (ItemData output in outputs)
            applies |= output.Tags != null && output.Tags.Contains("PreserveFreshness");
        if (!applies) return true;
        float elapsedFraction = 0f;
        foreach (CraftingConsumption consumption in match.Consumptions)
        {
            if (consumption.Amount <= 0f) continue;
            ModData_FoodData food = FindFood(input.Data.itemSlots[consumption.SlotIndex].itemData);
            if (food == null) continue;
            FoodSpoilageObserverData state = FoodSpoilageObserverData.Load(food);
            if (!state.EnableSpoilage) continue;
            float fraction = state.SpoilageElapsedSeconds / state.SpoilageIntervalSeconds;
            if (fraction >= 1f) { error = "原料已经腐败，无法腌制。"; return false; }
            elapsedFraction = Mathf.Max(elapsedFraction, fraction);
        }
        foreach (ItemData output in outputs)
        {
            if (output.Tags == null || !output.Tags.Contains("PreserveFreshness")) continue;
            ModData_FoodData food = FindFood(output);
            if (food == null) continue;
            FoodSpoilageObserverData state = FoodSpoilageObserverData.Load(food);
            state.SpoilageElapsedSeconds = state.SpoilageIntervalSeconds * elapsedFraction;
            state.Save(food);
        }
        return true;
    }
    /// <summary>从组合数据读取食品状态，不要求实例化物品。</summary>
    private static ModData_FoodData FindFood(ItemData itemData)
    {
        foreach (ModuleData module in itemData.ModuleDataDic.Values)
            if (module is ModData_FoodData food) return food;
        return null;
    }
}
