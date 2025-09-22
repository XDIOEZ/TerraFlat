using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Inventoryinit", menuName = "Inventory/Inventoryinit", order = 1)]
public class Inventoryinit : ScriptableObject
{
    public List<Prefab_Amount> items;
    
    // 在Inventoryinit类中添加此方法
    public void InjectItemsToInventory(Inventory targetInventory)
    {
        if (items == null || items.Count == 0)
            return;

        for (int i = 0; i < items.Count && i < targetInventory.Data.itemSlots.Count; i++)
        {
            var itemInfo = items[i];
            if (itemInfo.prefab != null && itemInfo.count > 0)
            {
                // 使用您之前添加的自动注入方法
                targetInventory.Data.AutoInjectItemData(itemInfo.prefab, itemInfo.count);
            }
        }
    }
    
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
            targetInventory.Data.AutoInjectItemDataList(validPrefabs, validCounts);
        }
    }
    
    // 新增：随机选择部分物品注入
    public void InjectRandomSelectionToInventory(Inventory targetInventory, int minItems = 1, int maxItems = 3)
    {
        if (items == null || items.Count == 0)
            return;

        // 收集所有有效的预制体
        List<GameObject> validPrefabs = new List<GameObject>();
        
        foreach (var itemInfo in items)
        {
            if (itemInfo.prefab != null && itemInfo.count > 0)
            {
                validPrefabs.Add(itemInfo.prefab);
            }
        }
        
        if (validPrefabs.Count > 0)
        {
            // 随机选择要注入的物品数量
            int itemCount = Random.Range(minItems, Mathf.Min(maxItems, validPrefabs.Count) + 1);
            
            // 随机选择不重复的物品
            List<GameObject> selectedPrefabs = new List<GameObject>();
            List<GameObject> availablePrefabs = new List<GameObject>(validPrefabs);
            
            for (int i = 0; i < itemCount && availablePrefabs.Count > 0; i++)
            {
                int randomIndex = Random.Range(0, availablePrefabs.Count);
                selectedPrefabs.Add(availablePrefabs[randomIndex]);
                availablePrefabs.RemoveAt(randomIndex);
            }
            
            // 为选中的物品设置随机数量（1-5个）
            List<int> counts = new List<int>();
            foreach (var prefab in selectedPrefabs)
            {
                counts.Add(Random.Range(1, 6));
            }
            
            // 使用自动注入列表方法
            targetInventory.Data.AutoInjectItemDataList(selectedPrefabs, counts);
        }
    }
}

[System.Serializable]
public struct Prefab_Amount 
{
    public GameObject prefab;
    public int count;
}