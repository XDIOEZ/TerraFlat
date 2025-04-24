using System.Collections.Generic;
using System;
using UnityEngine;
using System.Linq;

[Serializable]
public class Input_List
{
    [Header("��Ҫ��ԭ�����б�")]
    public List<CraftingIngredient> RowItems_List = new List<CraftingIngredient>();
    public override string ToString()
    {
        if (RowItems_List == null || RowItems_List.Count == 0)
            return "��ԭ����";

        return string.Join(",", RowItems_List.Select(ingredient => ingredient.ToString()));
    }

    public string ToString(bool Ranking)
    {
        if (RowItems_List == null || RowItems_List.Count == 0)
            return "��ԭ����";

        List<string> ingredientStrings = RowItems_List.Select(ingredient => ingredient.ToString()).ToList();

        if (Ranking)
        {
            ingredientStrings.Sort();
        }

        return string.Join(", ", ingredientStrings);
    }

}