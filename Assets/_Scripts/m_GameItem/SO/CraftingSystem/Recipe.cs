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
    [Header("合成时行为")]
    [InlineEditor]
    public List<CraftingAction> action;

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