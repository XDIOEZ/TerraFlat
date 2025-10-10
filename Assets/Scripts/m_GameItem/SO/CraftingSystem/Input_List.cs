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
    [Header("合成顺序")]
    public RecipeInputRule inputOrder = RecipeInputRule.规则合成;
    

    public override string ToString()
    {
        if (RowItems_List == null || RowItems_List.Count == 0)
        {
            return $"[][{recipeType}]";
        }

        // 创建一个副本用于处理
        List<CraftingIngredient> itemsToProcess = new List<CraftingIngredient>(RowItems_List);

        // 如果是无规则合成，则对物品进行排序以消除摆放顺序的影响
        if (inputOrder == RecipeInputRule.无规则合成)
        {
            itemsToProcess.Sort((a, b) => 
            {
                // 首先按匹配模式排序
                int modeComparison = a.matchMode.CompareTo(b.matchMode);
                if (modeComparison != 0)
                    return modeComparison;
                
                // 然后按物品名或标签排序
                string aKey = a.matchMode == MatchMode.ExactItem ? a.ItemName : a.Tag;
                string bKey = b.matchMode == MatchMode.ExactItem ? b.ItemName : b.Tag;
                
                return string.Compare(aKey, bKey, StringComparison.Ordinal);
            });
        }
        
        // 构建比较键
        string ingredients = string.Join(",", itemsToProcess.Select(ingredient => ingredient.ToString()));
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

public enum RecipeInputRule
{
    无规则合成,
    规则合成,
}