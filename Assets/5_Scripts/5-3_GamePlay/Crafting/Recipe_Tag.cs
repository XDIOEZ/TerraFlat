using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    {
        if (ingredient == null || ingredient.Tags == null)
            return string.Empty;
            
        StringBuilder ingredientKey = new StringBuilder();

        List<string> sortedTags = ingredient.Tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct()
            .OrderBy(tag => tag)
            .ToList();

        foreach (string tag in sortedTags)
        {
            ingredientKey.Append($"Tag:{tag};");
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
    [Sirenix.OdinInspector.ListDrawerSettings(ShowFoldout = true, DefaultExpandedState = true)]
    [Sirenix.OdinInspector.ValueDropdown(nameof(GetGameTagsDropdown), IsUniqueList = true, DrawDropdownForListElements = true)]
    public List<string> Tags = new List<string>();

    public int amount = 1;

    private static IEnumerable<Sirenix.OdinInspector.ValueDropdownItem<string>> GetGameTagsDropdown()
    {
        return typeof(GameTags)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .Select(field =>
            {
                string value = field.GetValue(null) as string;
                TooltipAttribute tooltip = field.GetCustomAttribute<TooltipAttribute>();
                string cn = tooltip != null ? tooltip.tooltip : string.Empty;
                string label = string.IsNullOrWhiteSpace(cn) ? value : $"{value} ({cn})";
                return new Sirenix.OdinInspector.ValueDropdownItem<string>(label, value);
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .GroupBy(item => item.Value)
            .Select(group => group.First())
            .OrderBy(item => item.Value)
            .ToList();
    }
}