using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "Inventoryinit", menuName = "Inventory/Inventoryinit", order = 1)]
public class Inventoryinit : ScriptableObject
{
    public List<LootEntry> items;
    

    // �����������������ע�뷽��
public void InjectRandomItemsToInventory(Inventory targetInventory)
{
    // ����Ƿ��Ѿ�ע���������Ѿ�ע����ȡ����Ϊ
    if (targetInventory.Data.IsInjected)
        return;

    if (items == null || items.Count == 0)
        return;

    // �ռ�������Ч��ս��Ʒ��Ŀ
    List<GameObject> validPrefabs = new List<GameObject>();
    List<int> validCounts = new List<int>();
    
    foreach (var lootEntry in items)
    {
        // ���ݵ�����ʾ����Ƿ���Ӹ���Ʒ
        if (Random.value <= lootEntry.DropChance)
        {
            // ��ȡԤ����
            GameObject prefab = GetPrefabFromLootEntry(lootEntry);
            if (prefab != null)
            {
                // �����������������С���������֮�䣩
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
        // ʹ�����ע�뷽����������Ʒע��
        targetInventory.Data.RandomOrderAutoInjectItemDataList(validPrefabs, validCounts);
        // ���Ϊ��ע��
        targetInventory.Data.IsInjected = true;
    }
}
    
    // ��LootEntry��ȡԤ����ĸ�������
    private GameObject GetPrefabFromLootEntry(LootEntry lootEntry)
    {
        // ����ʹ��LootEntry�е�GameObject����
        if (lootEntry.LootPrefab != null)
        {
            return lootEntry.LootPrefab;
        }
        
        // ���û��ֱ�����ã�����ͨ�����ƻ�ȡԤ����
        if (!string.IsNullOrEmpty(lootEntry.LootPrefabName))
        {
            return GameRes.Instance.GetPrefab(lootEntry.LootPrefabName);
        }
        
        return null;
    }
    
    // �ڱ༭������֤ʱ�Զ�ͬ��Prefab��Ϣ
    private void OnValidate()
    {
        if (items != null)
        {
            foreach (var lootEntry in items)
            {
                // ����LootEntry�ĸ��·���ͬ��Prefab����
                lootEntry.OnValidate();
            }
        }
    }
}