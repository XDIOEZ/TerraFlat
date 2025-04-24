using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using NUnit.Framework;
using Force.DeepCloner;
using System.Linq;

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


}
#region Nested Classes 

public enum RecipeType
{
    Crafting,
    Smelting,
}
#endregion