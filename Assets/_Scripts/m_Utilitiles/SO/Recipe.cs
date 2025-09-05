using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using NUnit.Framework;
using Force.DeepCloner;
using System.Linq;
using Sirenix.OdinInspector;

[CreateAssetMenu(fileName = "新合成配方", menuName = "合成/合成配方")]
public class Recipe : ScriptableObject
{
    #region Public Fields 
    [Header("输入材料")]
    public Input_List inputs = new Input_List();
    [Header("输出产物")]
    public Output_List outputs = new Output_List();
    [Header("配方类型")]
    public RecipeType recipeType = RecipeType.Crafting;

    #endregion
    // 将合成清单列表转换为字符串，用于作为字典的键
    public static string ToStringList(List<CraftingIngredient> list)
    {
        string[] ingredientStrings = new string[list.Count];
        foreach (var ingredient in list)
        {
            ingredientStrings[list.IndexOf(ingredient)] = ingredient.ToString();
        }
        return string.Join(",", ingredientStrings); // 直接返回逗号分隔的字符串
    }

    public void OnValidate()
    {
        inputs.RowItems_List.ForEach(x => x.SyncItemName());
        outputs.results.ForEach(x => x.SyncItemName());
    }

    [Button("同步数据从Excel")]
    public int SyncDataFromExcel()
    {
        m_ExcelManager.Instance.ChangeWorlSheet(this.GetType().Name);

        var excel = m_ExcelManager.Instance;

        int nameColumn = excel.FindColumn(0, "Name");
        int itemRow = excel.FindRow(nameColumn, name);
        int itemL = -1;

        // 确保列表有足够元素
        while (inputs.RowItems_List.Count < 9)
        {
            inputs.RowItems_List.Add(new CraftingIngredient("")); // 替换为你的实际类型构造函数
        }

        // 名称列 与 数量列 映射（index对齐）
        string[] dataCols = { "7", "8", "9", "4", "5", "6", "1", "2", "3" };
        string[] amountCols = { "n7", "n8", "n9", "n4", "n5", "n6", "n1", "n2", "n3" };

        for (int i = 0; i < dataCols.Length; i++)
        {
            // 获取物品名
            int nameColIndex = excel.FindColumn(0, dataCols[i]);
            string nameValue = excel.GetCellValue(itemRow, nameColIndex)?.ToString() ?? "";
            inputs.RowItems_List[i].ItemName = nameValue;
            
            // 获取数量
            int amountColIndex = excel.FindColumn(0, amountCols[i]);
            if (amountColIndex != -1)
            {
                object rawAmount = excel.GetCellValue(itemRow, amountColIndex);
                if (rawAmount != null && int.TryParse(rawAmount.ToString(), out int amount))
                {
                    inputs.RowItems_List[i].amount = amount;
                }
                else
                {
                    inputs.RowItems_List[i].amount = 0; // fallback
                }
            }
            else
            {
                inputs.RowItems_List[i].amount = 0; // fallback if not found
            }
        }

        string[] resultCols = { "r1" };
        string[] resultamountCols = { "r1a"};

        // 确保数量足够
        while (outputs.results.Count < resultCols.Length)
        {
            outputs.results.Add(new Result_List());
        }

        for (int i = 0; i < resultCols.Length; i++)
        {
            int colName = excel.FindColumn(0, resultCols[i]);
            int colAmt = excel.FindColumn(0, resultamountCols[i]);

            outputs.results[i].ItemName = excel.GetCellValue(itemRow, colName)?.ToString() ?? "";

            object amt = excel.GetCellValue(itemRow, colAmt);
            if (amt != null && int.TryParse(amt.ToString(), out int parsed))
                outputs.results[i].amount = parsed;
            else
                outputs.results[i].amount = 0;
        }


        itemL = excel.FindColumn(0, dataCols[^1]); // 最后一个列
        return itemL;
    }
}
#region Nested Classes 

public enum RecipeType
{
    Crafting,
    Smelting,
}
#endregion