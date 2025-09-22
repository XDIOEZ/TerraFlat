using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Inventoryinit", menuName = "Inventory/Inventoryinit", order = 1)]
public class Inventoryinit : ScriptableObject
{
    public List<Prefab_Amount> items;
    

    // 新增：单个物体随机注入方法
    public void InjectRandomItemsToInventory(Inventory targetInventory)
    {
        if (items == null || items.Count == 0)
            return;

        // 收集所有有效的预制体
        List<GameObject> validPrefabs = new List<GameObject>();
        List<int> validCounts = new List<int>();
        
        foreach (var itemInfo in items)
        {
            if (itemInfo.prefab != null && itemInfo.count > 0)
            {
                validPrefabs.Add(itemInfo.prefab);
                validCounts.Add(itemInfo.count);
            }
        }
        
        if (validPrefabs.Count > 0)
        {
            // 使用随机注入方法将所有物品注入
            targetInventory.Data.RandomOrderAutoInjectItemDataList(validPrefabs, validCounts);
        }
    }
   
}

[System.Serializable]
public struct Prefab_Amount 
{
    public GameObject prefab;
    public int count;
}