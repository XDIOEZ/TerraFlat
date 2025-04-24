using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using NUnit.Framework;
using Force.DeepCloner;
using System.Linq;

[CreateAssetMenu(fileName = "新合成配方", menuName = "合成/合成配方")]
public class Recipe : ScriptableObject
{
    #region Public Fields 
    [Header("配方基本信息")]
    public string version = "1.0.0"; // 配方版本 
    public string recipeID
    {
        get { return this.name; }
        set { this.name = value; }
    }

    [Header("输入材料")]
    public Input_List inputs = new Input_List();

    [Header("输出产物")]
    public Output_List outputs = new Output_List();

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

    public override string ToString()
    {
        return $"配方名称: {name}\n" +
               $"版本: {version}\n" +
               $"输入材料: {inputs.ToString()}\n" +
               $"输出产物: {outputs.ToString()}";
    }


}
#region Nested Classes 

public enum RecipeType
{
    Crafting,
    Smelting,
}
#endregion