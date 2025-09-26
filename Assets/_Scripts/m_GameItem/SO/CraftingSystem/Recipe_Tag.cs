using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[CreateAssetMenu(fileName = "�ºϳ��䷽_Tag�汾", menuName = "�ϳ�/�ϳ��䷽_Tag�汾")]
public class Recipe_Tag : ScriptableObject
{
    [Header("�䷽�б�")]
    public List<CraftingIngredient_Tag> Ingredient_Tags = new List<CraftingIngredient_Tag>();
    [Header("�����б�")]
    public List<Result_List> results = new List<Result_List>();
    [Header("�䷽����")]
    public RecipeType recipeType = RecipeType.Crafting;
    [Header("�ϳ�˳��")]
    public bool isOrdered = false;//�Ƿ�������ڷ�˳��

    /// <summary>
    /// �����䷽��Ψһ��ʶ��
    /// </summary>
    /// <returns>�䷽��ֵ�ַ���</returns>
    public string GetRecipeKey()
    {
        if (Ingredient_Tags == null || Ingredient_Tags.Count == 0)
            return string.Empty;

        StringBuilder keyBuilder = new StringBuilder();
        
        if (isOrdered)
        {
            // �����䷽����ԭʼ˳������Key
            foreach (var ingredient in Ingredient_Tags)
            {
                keyBuilder.Append(GetIngredientKey(ingredient));
                keyBuilder.Append("|");
            }
        }
        else
        {
            // �����䷽������ǩ�������������Key
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
        
        // �Ƴ�ĩβ�ķָ���
        if (keyBuilder.Length > 0 && keyBuilder[keyBuilder.Length - 1] == '|')
        {
            keyBuilder.Length--;
        }
        
        return keyBuilder.ToString();
    }
    
    /// <summary>
    /// ��ȡ�����䷽���ϵļ�ֵ
    /// </summary>
    /// <param name="ingredient">�䷽����</param>
    /// <returns>���ϼ�ֵ�ַ���</returns>
    private string GetIngredientKey(CraftingIngredient_Tag ingredient)
    {        if (ingredient.Tag == null || ingredient.Tag.keys == null)
            return string.Empty;
            
        StringBuilder ingredientKey = new StringBuilder();
        
        // ֻ����Type��Material��ǩ
        foreach (var tagData in ingredient.Tag.keys)
        {
            if (tagData == null || string.IsNullOrEmpty(tagData.tag) || tagData.values == null)
                continue;
                
            // ֻ����Type��Material��ǩ����
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
        
        // �������
        ingredientKey.Append($"*{ingredient.amount}");
        
        return ingredientKey.ToString();
    }
    
    [Button("����䷽Key")]
    private void DebugRecipeKey()
    {
        Debug.Log($"�䷽ [{name}] ��Key: {GetRecipeKey()}");
    }
}

[Serializable]
public class CraftingIngredient_Tag
{
    public TagDictionary Tag = new TagDictionary();
    public int amount = 1;
}