using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector;
using UnityEngine;

[Serializable]
public class Input_List
{
    [Header("需要的原材料列表")]
    [TableList(AlwaysExpanded = true, ShowIndexLabels = true)]
    public List<CraftingIngredient> RowItems_List = new List<CraftingIngredient>();

    [Header("配方类型")]
    public RecipeType recipeType = RecipeType.Crafting;

    [Header("合成顺序")]
    public RecipeInputRule inputOrder = RecipeInputRule.规则合成;

    public override string ToString()
    {
        if (RowItems_List == null || RowItems_List.Count == 0)
            return $"[][{recipeType}]";

        // 使用副本排序，避免修改原始配方输入。
        var itemsToProcess = new List<CraftingIngredient>(RowItems_List);
        if (inputOrder == RecipeInputRule.无规则合成)
        {
            itemsToProcess.Sort((a, b) =>
            {
                int modeComparison = a.matchMode.CompareTo(b.matchMode);
                if (modeComparison != 0)
                    return modeComparison;

                string aKey = a.matchMode == MatchMode.ExactItem ? a.ItemName : a.Tag;
                string bKey = b.matchMode == MatchMode.ExactItem ? b.ItemName : b.Tag;
                return string.Compare(aKey, bKey, StringComparison.Ordinal);
            });
        }

        string ingredients = string.Join(",", itemsToProcess.Select(ingredient => ingredient.ToString()));
        return $"{ingredients}[{recipeType}]";
    }

    public void AddNameItem(string name)
    {
        var ingredient = new CraftingIngredient
        {
            ItemName = name,
            matchMode = MatchMode.ExactItem
        };
        RowItems_List.Add(ingredient);
    }

    public void AddTagItem(string tag)
    {
        var ingredient = new CraftingIngredient
        {
            Tag = tag,
            matchMode = MatchMode.ByTag
        };
        RowItems_List.Add(ingredient);
    }
}

public enum RecipeInputRule
{
    无规则合成,
    规则合成,
}
