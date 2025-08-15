using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using NUnit.Framework;
using Force.DeepCloner;
using System.Linq;
using Sirenix.OdinInspector;

[CreateAssetMenu(fileName = "�ºϳ��䷽", menuName = "�ϳ�/�ϳ��䷽")]
public class Recipe : ScriptableObject
{
    #region Public Fields 
    [Header("�䷽������Ϣ")]
    public string version = "1.0.0"; // �䷽�汾 
    public string recipeID
    {
        get { return this.name; }
        set { this.name = value; }
    }

    [Header("�������")]
    public Input_List inputs = new Input_List();

    [Header("�������")]
    public Output_List outputs = new Output_List();

    public RecipeType recipeType = RecipeType.Crafting;

    #endregion
    // ���ϳ��嵥�б�ת��Ϊ�ַ�����������Ϊ�ֵ�ļ�
    public static string ToStringList(List<CraftingIngredient> list)
    {
        string[] ingredientStrings = new string[list.Count];
        foreach (var ingredient in list)
        {
            ingredientStrings[list.IndexOf(ingredient)] = ingredient.ToString();
        }
        return string.Join(",", ingredientStrings); // ֱ�ӷ��ض��ŷָ����ַ���
    }

    public override string ToString()
    {
        return $"�䷽����: {name}\n" +
               $"�汾: {version}\n" +
               $"�������: {inputs.ToString()}\n" +
               $"�������: {outputs.ToString()}";
    }

    [Button("ͬ�����ݴ�Excel")]
    public int SyncDataFromExcel()
    {
        m_ExcelManager.Instance.ChangeWorlSheet(this.GetType().Name);

        var excel = m_ExcelManager.Instance;

        int nameColumn = excel.FindColumn(0, "Name");
        int itemRow = excel.FindRow(nameColumn, name);
        int itemL = -1;

        // ȷ���б����㹻Ԫ��
        while (inputs.RowItems_List.Count < 9)
        {
            inputs.RowItems_List.Add(new CraftingIngredient("")); // �滻Ϊ���ʵ�����͹��캯��
        }

        // ������ �� ������ ӳ�䣨index���룩
        string[] dataCols = { "7", "8", "9", "4", "5", "6", "1", "2", "3" };
        string[] amountCols = { "n7", "n8", "n9", "n4", "n5", "n6", "n1", "n2", "n3" };

        for (int i = 0; i < dataCols.Length; i++)
        {
            // ��ȡ��Ʒ��
            int nameColIndex = excel.FindColumn(0, dataCols[i]);
            string nameValue = excel.GetCellValue(itemRow, nameColIndex)?.ToString() ?? "";
            inputs.RowItems_List[i].ItemName = nameValue;
            
            // ��ȡ����
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

        // ȷ�������㹻
        while (outputs.results.Count < resultCols.Length)
        {
            outputs.results.Add(new Result_List());
        }

        for (int i = 0; i < resultCols.Length; i++)
        {
            int colName = excel.FindColumn(0, resultCols[i]);
            int colAmt = excel.FindColumn(0, resultamountCols[i]);

            outputs.results[i].item = excel.GetCellValue(itemRow, colName)?.ToString() ?? "";

            object amt = excel.GetCellValue(itemRow, colAmt);
            if (amt != null && int.TryParse(amt.ToString(), out int parsed))
                outputs.results[i].amount = parsed;
            else
                outputs.results[i].amount = 0;
        }


        itemL = excel.FindColumn(0, dataCols[^1]); // ���һ����
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