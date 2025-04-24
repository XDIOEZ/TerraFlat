
using System.Collections.Generic;
using System;

[Serializable]
public class CraftingIngredient
{

    public string ItemName = "";
    public int amount = 1;

    public override string ToString()
    {
        return $"{ItemName}"; // 直接返回物品名称
    }
    public string ToStringList(List<CraftingIngredient> list)
    {
        string[] ingredientStrings = new string[list.Count];
        foreach (var ingredient in list)
        {
            ingredientStrings[list.IndexOf(ingredient)] = ingredient.ToString();
        }
        return string.Join(",", ingredientStrings); // 直接返回逗号分隔的字符串
    }

    public CraftingIngredient(string inputItem)
    {
        ItemName = inputItem;
        amount = 1;
    }
}