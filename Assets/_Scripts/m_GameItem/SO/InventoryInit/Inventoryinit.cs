using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Inventoryinit", menuName = "Inventory/Inventoryinit", order = 1)]
public class Inventoryinit : ScriptableObject
{
    public List<LootEntry> items;
    

    // 新增：单个物体随机注入方法
public void InjectRandomItemsToInventory(Inventory targetInventory)
{
    // 检查是否已经注入过，如果已经注入则取消行为
    if (targetInventory.Data.IsInjected)
        return;

    if (items == null || items.Count == 0)
        return;

    // 收集所有有效的战利品条目
    List<GameObject> validPrefabs = new List<GameObject>();
    List<int> validCounts = new List<int>();
    
    foreach (var lootEntry in items)
    {
        // 根据掉落概率决定是否添加该物品
        if (Random.value <= lootEntry.DropChance)
        {
            // 获取预制体
            GameObject prefab = GetPrefabFromLootEntry(lootEntry);
            if (prefab != null)
            {
                // 随机生成数量（在最小和最大数量之间）
                int count = Random.Range(lootEntry.MinAmount, lootEntry.MaxAmount + 1);
                if (count > 0)
                {
                    validPrefabs.Add(prefab);
                    validCounts.Add(count);
                }
            }
        }
    }
    
    if (validPrefabs.Count > 0)
    {
        // 使用随机注入方法将所有物品注入
        targetInventory.Data.RandomOrderAutoInjectItemDataList(validPrefabs, validCounts);
        // 标记为已注入
        targetInventory.Data.IsInjected = true;
    }
}
    
    // 从LootEntry获取预制体的辅助方法
    private GameObject GetPrefabFromLootEntry(LootEntry lootEntry)
    {
        // 优先使用LootEntry中的GameObject引用
        if (lootEntry.LootPrefab != null)
        {
            return lootEntry.LootPrefab;
        }
        
        // 如果没有直接引用，尝试通过名称获取预制体
        if (!string.IsNullOrEmpty(lootEntry.LootPrefabName))
        {
            return GameRes.Instance.GetPrefab(lootEntry.LootPrefabName);
        }
        
        return null;
    }
    
    // 在编辑器中验证时自动同步Prefab信息
    private void OnValidate()
    {
        if (items != null)
        {
            foreach (var lootEntry in items)
            {
                // 调用LootEntry的更新方法同步Prefab名称
                lootEntry.OnValidate();
            }
        }
    }
}