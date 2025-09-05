
using System.Collections.Generic;
using System;
using UnityEngine;

[Serializable]
public class Output_List
{
    [Header("�����б�")]
    public List<Result_List> results = new List<Result_List>();

    public override string ToString()
    {
        if (results == null || results.Count == 0)
            return "�޲���";

        List<string> resultStrings = new List<string>();
        foreach (var result in results)
        {
            resultStrings.Add($"{result.amount}x{result.ItemName}");
        }
        return $"����: [{string.Join(",",resultStrings)}]";
    }
    public string ToString(bool Ranking)
    {
        if (results == null || results.Count == 0)
            return "�޲���";

        List<string> ingredientStrings = new List<string>();

        foreach (var ingredient in results)
        {
            ingredientStrings.Add(ingredient.ToString());
        }

        // ���RankingΪtrue�����ֵ�˳������
        if (Ranking)
        {
            ingredientStrings.Sort();
        }

        return $"����: [{string.Join(",",ingredientStrings)}]";
    }
}