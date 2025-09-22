using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Inventoryinit", menuName = "Inventory/Inventoryinit", order = 1)]
public class Inventoryinit : ScriptableObject
{
    public List<Prefab_Amount> items;
    
    // ��Inventoryinit������Ӵ˷���
    public void InjectItemsToInventory(Inventory targetInventory)
    {
        if (items == null || items.Count == 0)
            return;

        for (int i = 0; i < items.Count && i < targetInventory.Data.itemSlots.Count; i++)
        {
            var itemInfo = items[i];
            if (itemInfo.prefab != null && itemInfo.count > 0)
            {
                // ʹ����֮ǰ��ӵ��Զ�ע�뷽��
                targetInventory.Data.AutoInjectItemData(itemInfo.prefab, itemInfo.count);
            }
        }
    }
    
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
            targetInventory.Data.AutoInjectItemDataList(validPrefabs, validCounts);
        }
    }
    
    // ���������ѡ�񲿷���Ʒע��
    public void InjectRandomSelectionToInventory(Inventory targetInventory, int minItems = 1, int maxItems = 3)
    {
        if (items == null || items.Count == 0)
            return;

        // �ռ�������Ч��Ԥ����
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
            // ���ѡ��Ҫע�����Ʒ����
            int itemCount = Random.Range(minItems, Mathf.Min(maxItems, validPrefabs.Count) + 1);
            
            // ���ѡ���ظ�����Ʒ
            List<GameObject> selectedPrefabs = new List<GameObject>();
            List<GameObject> availablePrefabs = new List<GameObject>(validPrefabs);
            
            for (int i = 0; i < itemCount && availablePrefabs.Count > 0; i++)
            {
                int randomIndex = Random.Range(0, availablePrefabs.Count);
                selectedPrefabs.Add(availablePrefabs[randomIndex]);
                availablePrefabs.RemoveAt(randomIndex);
            }
            
            // Ϊѡ�е���Ʒ�������������1-5����
            List<int> counts = new List<int>();
            foreach (var prefab in selectedPrefabs)
            {
                counts.Add(Random.Range(1, 6));
            }
            
            // ʹ���Զ�ע���б���
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