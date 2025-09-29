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
    [Header("�������")]
    public Input_List inputs = new Input_List();
    [Header("�������")]
    public Output_List outputs = new Output_List();
    [Header("�ϳ�ʱ��Ϊ")]
    [InlineEditor]
    public List<CraftingAction> action;

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
    [Button]
    public void Test()
    {
        Debug.Log(inputs);
    }

    public void OnValidate()
    {
        inputs.RowItems_List.ForEach(x => x.SyncItemName());
        outputs.results.ForEach(x => x.SyncItemName());
    }
}
#region Nested Classes 

public enum RecipeType
{
    Crafting,
    Smelting,
}
#endregion