using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[CreateAssetMenu(fileName = "新合成配方_Tag版本", menuName = "合成/合成配方_Tag版本")]
public class Recipe_Tag : ScriptableObject
{
    [Header("配方列表")]
    public List<CraftingIngredient_Tag> Ingredient_Tags = new List<CraftingIngredient_Tag>();
    [Header("产物列表")]
    public List<Result_List> results = new List<Result_List>();
    [Header("配方类型")]
    public RecipeType recipeType = RecipeType.Crafting;
    [Header("合成顺序")]
    public bool isOrdered = false;//是否可以随便摆放顺序

    /// <summary>
    /// 生成配方的唯一标识键
    /// </summary>
    /// <returns>配方键值字符串</returns>
    public string GetRecipeKey()
    {
        if (Ingredient_Tags == null || Ingredient_Tags.Count == 0)
            return string.Empty;

        StringBuilder keyBuilder = new StringBuilder();
        
        if (isOrdered)
        {
            // 有序配方：按原始顺序生成Key
            foreach (var ingredient in Ingredient_Tags)
            {
                keyBuilder.Append(GetIngredientKey(ingredient));
                keyBuilder.Append("|");
            }
        }
        else
        {
            // 无序配方：按标签内容排序后生成Key
            var sortedIngredients = new List<string>();
            foreach (var ingredient in Ingredient_Tags)
            {
                sortedIngredients.Add(GetIngredientKey(ingredient));
            }
            sortedIngredients.Sort();
            
            foreach (var ingredientKey in sortedIngredients)
            {
                keyBuilder.Append(ingredientKey);
                keyBuilder.Append("|");
            }
        }
        
        // 移除末尾的分隔符
        if (keyBuilder.Length > 0 && keyBuilder[keyBuilder.Length - 1] == '|')
        {
            keyBuilder.Length--;
        }
        
        return keyBuilder.ToString();
    }
    
    /// <summary>
    /// 获取单个配方材料的键值
    /// </summary>
    /// <param name="ingredient">配方材料</param>
    /// <returns>材料键值字符串</returns>
    private string GetIngredientKey(CraftingIngredient_Tag ingredient)
    {        if (ingredient.Tag == null || ingredient.Tag.keys == null)
            return string.Empty;
            
        StringBuilder ingredientKey = new StringBuilder();
        
        // 只处理Type和Material标签
        foreach (var tagData in ingredient.Tag.keys)
        {
            if (tagData == null || string.IsNullOrEmpty(tagData.tag) || tagData.values == null)
                continue;
                
            // 只处理Type和Material标签类型
            if (tagData.tag == "Type" || tagData.tag == "Material")
            {
                foreach (var value in tagData.values)
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        ingredientKey.Append($"{tagData.tag}:{value};");
                    }
                }
            }
        }
        
        // 添加数量
        ingredientKey.Append($"*{ingredient.amount}");
        
        return ingredientKey.ToString();
    }
    
    [Button("输出配方Key")]
    private void DebugRecipeKey()
    {
        Debug.Log($"配方 [{name}] 的Key: {GetRecipeKey()}");
    }
}

[Serializable]
public class CraftingIngredient_Tag
{
    public TagDictionary Tag = new TagDictionary();
    public int amount = 1;
}