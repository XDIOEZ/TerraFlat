using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 将 JSON 动作描述路由给 C# 行为处理器。
/// </summary>
public static class RecipeActionRunner
{
    public const string ChangeDurabilityType = "change_durability";

    private static readonly Dictionary<string, IRecipeActionHandler> Handlers =
        new Dictionary<string, IRecipeActionHandler>(StringComparer.OrdinalIgnoreCase)
        {
            { ChangeDurabilityType, new ChangeDurabilityRecipeActionHandler() }
        };

    public static bool HasHandler(string type)
    {
        return !string.IsNullOrWhiteSpace(type) && Handlers.ContainsKey(type);
    }

    public static void Execute(RuntimeRecipe recipe, Inventory inventory)
    {
        if (recipe?.action == null || inventory == null)
            return;

        foreach (RuntimeRecipeAction action in recipe.action)
        {
            if (action == null || !Handlers.TryGetValue(action.Type ?? string.Empty, out IRecipeActionHandler handler))
            {
                Debug.LogError($"[CraftingAction] 配方 {recipe?.Id} 找不到动作处理器：{action?.Type}");
                continue;
            }

            handler.Execute(action, inventory, recipe);
        }
    }
}

public interface IRecipeActionHandler
{
    void Execute(RuntimeRecipeAction action, Inventory inventory, RuntimeRecipe recipe);
}

/// <summary>
/// 按工具角色（当前映射为 Item Tag）扣除耐久。
/// </summary>
public sealed class ChangeDurabilityRecipeActionHandler : IRecipeActionHandler
{
    public void Execute(RuntimeRecipeAction action, Inventory inventory, RuntimeRecipe recipe)
    {
        if (inventory?.Data?.itemSlots == null)
        {
            Debug.LogWarning($"[CraftingAction] 配方 {recipe.Id} 找不到目标库存");
            return;
        }

        if (action.SlotIndex >= 0 && action.SlotIndex < inventory.Data.itemSlots.Count &&
            TryApply(inventory.Data.itemSlots[action.SlotIndex], action, recipe))
        {
            return;
        }

        for (int i = 0; i < inventory.Data.itemSlots.Count; i++)
        {
            if (i == action.SlotIndex)
                continue;
            if (TryApply(inventory.Data.itemSlots[i], action, recipe))
                return;
        }

        Debug.LogWarning($"[CraftingAction] 配方 {recipe.Id} 找不到工具角色：{action.TargetRole}");
    }

    private static bool TryApply(ItemSlot slot, RuntimeRecipeAction action, RuntimeRecipe recipe)
    {
        ItemData itemData = slot?.itemData;
        if (itemData?.Tags == null || !itemData.Tags.Contains(action.TargetRole))
            return false;
        if (itemData.Durability <= 0f || itemData.MaxDurability <= 0f)
        {
            Debug.LogWarning($"[CraftingAction] {itemData.IDName} 没有可扣除的耐久度");
            return true;
        }

        string itemId = itemData.IDName;
        itemData.AddDurability(-action.Value);
        if (itemData.Durability <= 0f)
        {
            itemData.Durability = 0f;
            slot.ClearData();
            Debug.Log($"[CraftingAction] {itemId} 耐久耗尽并已移除");
        }
        else
        {
            Debug.Log($"[CraftingAction] {itemId} 消耗耐久 {action.Value}，剩余 {itemData.Durability}/{itemData.MaxDurability}");
        }
        return true;
    }
}
