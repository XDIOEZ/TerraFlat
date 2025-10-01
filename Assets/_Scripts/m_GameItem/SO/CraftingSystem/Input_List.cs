using System.Collections.Generic;
using System;
using UnityEngine;
using System.Linq;
using Sirenix.OdinInspector;

[Serializable]
public class Input_List
{
    [Header("��Ҫ��ԭ�����б�")]
    [TableList(AlwaysExpanded = true, ShowIndexLabels = true)]
    public List<CraftingIngredient> RowItems_List = new List<CraftingIngredient>();
    [Header("�䷽����")]
    public RecipeType recipeType = RecipeType.Crafting;
    [Header("�ϳ�˳��")]
    public RecipeInputRule inputOrder = RecipeInputRule.����ϳ�;
    

    public override string ToString()
    {
        if (RowItems_List == null || RowItems_List.Count == 0)
        {
            return $"[][{recipeType}]";
        }

        // ����һ���������ڴ���
        List<CraftingIngredient> itemsToProcess = new List<CraftingIngredient>(RowItems_List);

        // ������޹���ϳɣ������Ʒ���������������ڷ�˳���Ӱ��
        if (inputOrder == RecipeInputRule.�޹���ϳ�)
        {
            itemsToProcess.Sort((a, b) => 
            {
                // ���Ȱ�ƥ��ģʽ����
                int modeComparison = a.matchMode.CompareTo(b.matchMode);
                if (modeComparison != 0)
                    return modeComparison;
                
                // Ȼ����Ʒ�����ǩ����
                string aKey = a.matchMode == MatchMode.ExactItem ? a.ItemName : a.Tag;
                string bKey = b.matchMode == MatchMode.ExactItem ? b.ItemName : b.Tag;
                
                return string.Compare(aKey, bKey, StringComparison.Ordinal);
            });
        }
        
        // �����Ƚϼ�
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
    �޹���ϳ�,
    ����ϳ�,
}