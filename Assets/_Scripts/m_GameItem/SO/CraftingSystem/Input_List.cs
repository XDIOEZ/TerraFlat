using System.Collections.Generic;
using System;
using UnityEngine;
using System.Linq;
using Sirenix.OdinInspector;

[Serializable]
public class Input_List
{
    [Header("需要的原材料列表")]
    [TableList(AlwaysExpanded = true, ShowIndexLabels = true)]
    public List<CraftingIngredient> RowItems_List = new List<CraftingIngredient>();
    [Header("配方类型")]
    public RecipeType recipeType = RecipeType.Crafting;

    public override string ToString()
    {
        string ingredients = string.Join(",", RowItems_List.Select(ingredient => ingredient.ToString()));
        return $"{ingredients}[{recipeType}]";
    }

    public string ToString(bool Ranking)
    {
        if (RowItems_List == null || RowItems_List.Count == 0)
            return "无原材料";

        List<string> ingredientStrings = RowItems_List.Select(ingredient => ingredient.ToString()).ToList();

        if (Ranking)
        {
            ingredientStrings.Sort();
        }

        string ingredients = string.Join(",", ingredientStrings);
        return $"{ingredients}[{recipeType}]";
    }

    public void AddNameItem(string name)
    {
        CraftingIngredient ingredient = new CraftingIngredient();
        ingredient.ItemName = name;
        ingredient.matchMode = MatchMode.ExactItem;
        RowItems_List.Add(ingredient);
    }
    public void AddTagItem(string Tag)
    {
        CraftingIngredient ingredient = new CraftingIngredient();
        ingredient.Tag = Tag;
        ingredient.matchMode = MatchMode.ByTag;
        RowItems_List.Add(ingredient);
    }

}