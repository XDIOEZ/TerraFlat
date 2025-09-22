using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Inventoryinit", menuName = "Inventory/Inventoryinit", order = 1)]
public class Inventoryinit : ScriptableObject
{
    public List<Prefab_Amount> items;
    

    // �����������������ע�뷽��
    public void InjectRandomItemsToInventory(Inventory targetInventory)
    {
        if (items == null || items.Count == 0)
            return;

        // �ռ�������Ч��Ԥ����
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
            // ʹ�����ע�뷽����������Ʒע��
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