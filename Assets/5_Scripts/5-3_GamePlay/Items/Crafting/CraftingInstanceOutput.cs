using System.Linq;
using UnityEngine;

/// <summary>实例加工保留被消耗工具的身份和状态，预览与提交使用同一份独立产物。</summary>
public static class CraftingInstanceOutput
{
    #region 实例产物
    public const string ReinforcedTag = "AdhesiveReinforced";

    public static bool TryCreate(Inventory input, RuntimeRecipe recipe, CraftingRecipeMatch match,
        RuntimeRecipeResult output, out ItemData result, out string error)
    {
        result = null;
        error = "找不到可加工的工具实例";
        RuntimeRecipeIngredient ingredient = recipe.inputs.RowItems_List.FirstOrDefault(value =>
            value.matchMode == MatchMode.ByTag && value.Tag == output.CopyInputTag && value.amount == 1);
        if (ingredient == null || input?.Data?.itemSlots == null) return false;
        for (int i = 0; i < input.Data.itemSlots.Count; i++)
        {
            ItemData source = input.Data.itemSlots[i]?.itemData;
            if (!CraftingIngredientMatcher.Matches(ingredient, source) || source.Stack == null ||
                source.Stack.Stackable || source.Stack.Amount != 1f ||
                (match != null && !match.Consumptions.Any(value => value.SlotIndex == i && value.Amount >= 1f))) continue;
            if (output.ReinforceWithAdhesive && (source.Tags.Contains(ReinforcedTag) || source.MaxDurability <= 0f)) continue;
            result = ItemInstanceDataFactory.CloneRuntime(source);
            if (output.ReinforceWithAdhesive) ApplyAdhesive(result);
            result.Stack.Amount = 1f;
            error = null;
            return true;
        }
        return false;
    }

    /// <summary>先提高耐久上限，再恢复新上限下一半缺损；实例标签阻止重复强化。</summary>
    public static void ApplyAdhesive(ItemData item)
    {
        if (item == null || item.MaxDurability <= 0f || item.Tags.Contains(ReinforcedTag)) return;
        item.CraftedDurabilityMultiplier *= 1.3f;
        item.MaxDurability *= 1.3f;
        item.Durability += Mathf.Max(0f, item.MaxDurability - item.Durability) * .5f;
        item.Tags.Add(ReinforcedTag);
    }
    #endregion
}
