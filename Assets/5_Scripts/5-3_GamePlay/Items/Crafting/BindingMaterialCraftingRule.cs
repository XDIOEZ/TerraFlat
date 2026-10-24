using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>绳料品质只读取真实扣料计划，最弱材料决定成品是否获得一次耐久加成。</summary>
public sealed class BindingMaterialCraftingRule : ICraftingOutputRule
{
    #region 绳料制作品质
    public const string BindingTag = "Rope";
    public const string DurabilityTagPrefix = "BindingDurability:";
    public const string ReinforcedTag = CraftedDurabilityQuality.MarkerTagPrefix + "BindingReinforced";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register() => CraftingOutputRules.Register("materials.binding", new BindingMaterialCraftingRule());

    public bool Prepare(Inventory input, CraftingRecipeMatch match, IReadOnlyList<ItemData> outputs, out string error)
    {
        error = null;
        if (match?.Recipe?.inputs?.RowItems_List == null || input?.Data?.itemSlots == null || outputs == null)
            return true;
        bool requiresBinding = false;
        foreach (RuntimeRecipeIngredient ingredient in match.Recipe.inputs.RowItems_List)
            if (ingredient.amount > 0 && ingredient.matchMode == MatchMode.ByTag &&
                string.Equals(ingredient.Tag, BindingTag, StringComparison.OrdinalIgnoreCase))
            { requiresBinding = true; break; }
        if (!requiresBinding) return true;

        float multiplier = float.MaxValue;
        bool consumedBinding = false;
        foreach (CraftingConsumption consumption in match.Consumptions)
        {
            if (consumption.Amount <= 0f) continue;
            if (consumption.SlotIndex < 0 || consumption.SlotIndex >= input.Data.itemSlots.Count)
            { error = "绳料扣料计划已经失效"; return false; }
            ItemData material = input.Data.itemSlots[consumption.SlotIndex]?.itemData;
            if (!HasBindingTag(material)) continue;
            consumedBinding = true;
            if (!TryGetMultiplier(material, out float materialMultiplier))
            { error = $"材料 {material.IDName} 的绳料耐久品质无效"; return false; }
            multiplier = Mathf.Min(multiplier, materialMultiplier);
        }
        if (!consumedBinding || multiplier <= 1f) return true;

        // 多堆皮带可合供，混入普通绳子则不加成；预览仅修改独立产物。
        foreach (ItemData output in outputs)
        {
            if (output?.Tags == null || output.MaxDurability <= 0f || output.Tags.Contains(ReinforcedTag)) continue;
            output.CraftedDurabilityMultiplier *= multiplier;
            output.MaxDurability *= multiplier;
            output.Durability *= multiplier;
            output.Tags.Add(ReinforcedTag);
        }
        return true;
    }

    private static bool HasBindingTag(ItemData material)
    {
        if (material?.Tags == null) return false;
        foreach (string tag in material.Tags)
            if (string.Equals(tag, BindingTag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool TryGetMultiplier(ItemData material, out float multiplier)
    {
        multiplier = 1f;
        bool found = false;
        foreach (string tag in material.Tags)
        {
            if (tag == null || !tag.StartsWith(DurabilityTagPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!float.TryParse(tag.Substring(DurabilityTagPrefix.Length), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value) || value < 1f)
                return false;
            multiplier = found ? Mathf.Min(multiplier, value) : value;
            found = true;
        }
        return true;
    }
    #endregion
}
