// InputListWithMatrix.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

[Serializable]
public class CraftingIngredient
{
    [ReadOnly]
    public string ItemName = "";
    public GameObject ItemPrefab;
    public int amount = 1;

    public override string ToString() => ItemName;

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
            ItemName = ItemPrefab.name;
    }
}