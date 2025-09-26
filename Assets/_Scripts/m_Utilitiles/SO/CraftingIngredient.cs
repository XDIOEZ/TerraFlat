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

    public string Tag = "";//��Tag����ʱ ��ʾ����Ʒ�����ӦTag����Ʒ,

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

    // �޸���ԭ�� ToStringList ��ʹ�� IndexOf ��Ǳ������
    public string ToStringList(List<CraftingIngredient> list)
    {
        if (list == null || list.Count == 0) return "";
        var strings = new string[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            strings[i] = list[i] != null ? list[i].ToString() : "��";
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
    ExactItem, // ������ָ����Ʒ
    ByTag      // �������� Tag ����Ʒ
}