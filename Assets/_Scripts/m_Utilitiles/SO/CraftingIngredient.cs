
using System.Collections.Generic;
using System;

[Serializable]
public class CraftingIngredient
{

    public string ItemName = "";
    public int amount = 1;

    public override string ToString()
    {
        return $"{ItemName}"; // ֱ�ӷ�����Ʒ����
    }
    public string ToStringList(List<CraftingIngredient> list)
    {
        string[] ingredientStrings = new string[list.Count];
        foreach (var ingredient in list)
        {
            ingredientStrings[list.IndexOf(ingredient)] = ingredient.ToString();
        }
        return string.Join(",", ingredientStrings); // ֱ�ӷ��ض��ŷָ����ַ���
    }

    public CraftingIngredient(string inputItem)
    {
        ItemName = inputItem;
        amount = 1;
    }
}