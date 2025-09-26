// InputListWithMatrix.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;
using DG.Tweening;

[Serializable]
public class CraftingIngredient
{
    public MatchMode matchMode = MatchMode.ExactItem;
    [ReadOnly]
    public string ItemName = "";
    public GameObject ItemPrefab;

    public string Tag = "";//当Tag存在时 表示该物品适配对应Tag的物品,

    public int amount = 1;

    public override string ToString()
    {
        switch (matchMode)
        {
            case MatchMode.ExactItem:
                return ItemName;
            case MatchMode.ByTag:
                return Tag;
            default:
                return ItemName;
        }
    }
    
    public string ToStringWithAmount()
    {
        switch (matchMode)
        {
            case MatchMode.ExactItem:
                return $"{ItemName}*{amount}";
            case MatchMode.ByTag:
                return $"{Tag}*{amount}";
            default:
                return $"{ItemName}*{amount}";
        }
    }

    // 修复了原来 ToStringList 中使用 IndexOf 的潜在问题
    public string ToStringList(List<CraftingIngredient> list)
    {
        if (list == null || list.Count == 0) return "";
        var strings = new string[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            strings[i] = list[i] != null ? list[i].ToString() : "空";
        }
        return string.Join(",", strings);
    }

    public CraftingIngredient() { }

    public CraftingIngredient(string inputItem)
    {
        ItemName = inputItem;
        amount = 1;
    }

    public void SyncItemName()
    {
        if (ItemPrefab != null)
        {
            ItemName = ItemPrefab.name;
        }
        else
        {
            ItemName = "";
            amount = 0;
        }
        
    }
}

public enum MatchMode
{
    ExactItem, // 必须是指定物品
    ByTag      // 任意带这个 Tag 的物品
}