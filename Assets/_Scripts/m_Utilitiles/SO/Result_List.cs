using Sirenix.OdinInspector;
using System;
using UnityEngine;

[Serializable]
public class Result_List
{
    [ReadOnly]
    public string ItemName = "";
    public GameObject ItemPrefab;
    public int amount = 1;


    public override string ToString()
    {
        return $"{amount}x{ItemName}";
    }
    public void SyncItemName()
    {
        if(ItemPrefab!= null)
        ItemName = ItemPrefab.name;
    }
}