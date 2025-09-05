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
    public override string ToString()
    {
        return string.Join(",", RowItems_List.Select(ingredient => ingredient.ToString()));
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

        return string.Join(",", ingredientStrings);
    }

}