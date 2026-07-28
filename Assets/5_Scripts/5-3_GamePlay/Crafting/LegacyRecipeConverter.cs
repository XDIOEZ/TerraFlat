using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 旧配方 SO 的临时兼容桥，仅用于迁移旧资源与兼容旧 MOD AssetBundle。
/// </summary>
public static class LegacyRecipeConverter
{
    public static RecipeDto ToDto(Recipe legacy, string id)
    {
        if (legacy == null)
            throw new ArgumentNullException(nameof(legacy));

        int inputCount = legacy.inputs?.RowItems_List?.Count ?? 0;
        int gridWidth = InferGridWidth(inputCount);
        var dto = new RecipeDto
        {
            Id = id,
            DisplayName = legacy.name,
            RecipeType = legacy.inputs != null && legacy.inputs.recipeType == RecipeType.Smelting ? "smelting" : "crafting",
            InputRule = legacy.inputs != null && legacy.inputs.inputOrder == RecipeInputRule.无规则合成 ? "unordered" : "ordered",
            GridWidth = gridWidth,
            GridHeight = gridWidth > 0 ? Mathf.CeilToInt((float)inputCount / gridWidth) : 0,
            AllowMirror = legacy.enableMirrorCrafting
        };

        if (legacy is CookRecipe cookRecipe)
        {
            dto.RecipeType = "smelting";
            dto.Temperature = cookRecipe.Temperature;
            dto.MaxTemperature = cookRecipe.Temperature_Max;
        }

        if (legacy.inputs?.RowItems_List != null)
        {
            for (int i = 0; i < legacy.inputs.RowItems_List.Count; i++)
            {
                CraftingIngredient input = legacy.inputs.RowItems_List[i] ?? new CraftingIngredient();
                dto.Inputs.Add(new RecipeIngredientDto
                {
                    Slot = i,
                    Match = input.matchMode == MatchMode.ByTag ? "tag" : "exact_item",
                    ItemId = ResolveItemId(input.ItemPrefab, input.ItemName),
                    Tag = input.Tag,
                    Amount = input.amount
                });
            }
        }

        if (legacy.outputs?.results != null)
        {
            foreach (Result_List output in legacy.outputs.results)
            {
                if (output == null)
                    continue;
                string itemId = ResolveItemId(output.ItemPrefab, output.ItemName);
                dto.Outputs.Add(new RecipeOutputDto { ItemId = itemId, Amount = output.amount });
            }
        }

        foreach (CraftingAction legacyAction in legacy.action ?? new List<CraftingAction>())
        {
            if (legacyAction is DurabilityModifier durability)
            {
                string targetRole = durability.lostDurabilityItemTag;
                if (string.IsNullOrWhiteSpace(targetRole) && durability.slotIndex >= 0 &&
                    durability.slotIndex < dto.Inputs.Count)
                {
                    targetRole = dto.Inputs[durability.slotIndex].Tag;
                }
                dto.Actions.Add(new RecipeActionDto
                {
                    Type = RecipeActionRunner.ChangeDurabilityType,
                    TargetRole = targetRole,
                    Value = durability.durabilityCost,
                    SlotIndex = durability.slotIndex
                });
            }
            else if (legacyAction != null)
            {
                Debug.LogWarning($"[RecipeMigration] 配方 {legacy.name} 跳过未知旧动作：{legacyAction.GetType().Name}");
            }
        }

        return dto;
    }

    public static RuntimeRecipe ToRuntime(Recipe legacy, string id, Func<string, bool> itemExists)
    {
        return RecipeRuntimeFactory.Build(ToDto(legacy, id), itemExists);
    }

    private static int InferGridWidth(int count)
    {
        if (count <= 0)
            return 0;
        int square = Mathf.RoundToInt(Mathf.Sqrt(count));
        return square * square == count ? square : count;
    }

    private static string ResolveItemId(GameObject prefab, string fallback)
    {
        Item item = prefab != null ? prefab.GetComponent<Item>() : null;
        if (item?.itemData != null && !string.IsNullOrWhiteSpace(item.itemData.IDName))
            return item.itemData.IDName;
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;
        return prefab != null ? prefab.name : string.Empty;
    }
}
