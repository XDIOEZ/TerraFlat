
using System.Collections.Generic;
using System;
using UnityEngine;

[Serializable]
public class Output_List
{
    [Header("产物列表")]
    public List<Result_List> results = new List<Result_List>();

    public override string ToString()
    {
        if (results == null || results.Count == 0)
            return "无产物";

        List<string> resultStrings = new List<string>();
        foreach (var result in results)
        {
            resultStrings.Add($"{result.amount}x{result.ItemName}");
        }
        return $"产物: [{string.Join(",",resultStrings)}]";
    }
    public string ToString(bool Ranking)
    {
        if (results == null || results.Count == 0)
            return "无产物";

        List<string> ingredientStrings = new List<string>();

        foreach (var ingredient in results)
        {
            ingredientStrings.Add(ingredient.ToString());
        }

        // 如果Ranking为true，按字典顺序排序
        if (Ranking)
        {
            ingredientStrings.Sort();
        }

        return $"产物: [{string.Join(",",ingredientStrings)}]";
    }
}