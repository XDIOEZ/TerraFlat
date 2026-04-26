using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using NUnit.Framework;
using Force.DeepCloner;
using System.Linq;
using Sirenix.OdinInspector;

[CreateAssetMenu(fileName = "新配方格式", menuName = "配方/新配方")]
public class Recipe : ScriptableObject
{
    #region Public Fields 
    [Header("输入物品")]
    public Input_List inputs = new Input_List();
    [Header("输出物品")]
    public Output_List outputs = new Output_List();
    [Header("是否允许镜像")]
    public bool enableMirrorCrafting = true;
    [Header("合成动作组")]
    [InlineEditor]
    public List<CraftingAction> action;

    #endregion
    // 将配方中的原料列表转化为字符串格式，以逗号分隔的文件夹名
    public static string ToStringList(List<CraftingIngredient> list)
    {
        string[] ingredientStrings = new string[list.Count];
        foreach (var ingredient in list)
        {
            ingredientStrings[list.IndexOf(ingredient)] = ingredient.ToString();
        }
        return string.Join(",", ingredientStrings); // 直接返回带逗号分隔的字符串
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