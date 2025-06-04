using NUnit.Framework.Interfaces;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class RunTimeItemManager : SingletonMono<RunTimeItemManager>
{
    [ShowInInspector]
    public Dictionary<int, Item> RunTimeItems = new();
    [ShowInInspector]
    public Dictionary<string, List<Item>> RuntimeItemsGroup = new();


    public void Awake()
    {
        // 第一步：获取场景中所有的 Item（包括非激活状态）
        Item[] allItems = FindObjectsOfType<Item>(includeInactive: false);

        foreach (Item item in allItems)
        {
            if (item is ISave_Load saveableItem)
            {
                try
                {
                    saveableItem.Load();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"加载物品失败: {item.name}", item);
                    Debug.LogException(ex);
                }
            }
            if (item.Item_Data.Guid == 0)
            {
               //随机生成一个Guid
                item.Item_Data.Guid = System.Guid.NewGuid().GetHashCode();
            }
            RunTimeItems.Add(item.Item_Data.Guid, item);
            AddToGroup(item); // 新增分组逻辑
        }
        Debug.Log("物品加载完毕");
    }
    // 实例化（通过名称）
    public Item InstantiateItem(string itemName, Vector3 position = default, Quaternion rotation = default)
    {
        GameObject itemObj = GameRes.Instance.InstantiatePrefab(itemName, position);
        Item item = itemObj.GetComponent<Item>();

        if (item.Item_Data.Guid > -1000 && item.Item_Data.Guid < 1000)
            item.Item_Data.Guid = System.Guid.NewGuid().GetHashCode();

        RunTimeItems.Add(item.Item_Data.Guid, item);
        AddToGroup(item); // 新增分组逻辑

        return item;
    }

    // 实例化（通过 ItemData）
    public Item InstantiateItem(ItemData itemData, Vector3 position = default, Quaternion rotation = default)
    {
        GameObject itemObj = GameRes.Instance.InstantiatePrefab(itemData.IDName, position);
        Item item = itemObj.GetComponent<Item>();
        item.Item_Data = itemData;

        if (item.Item_Data.Guid > -1000 && item.Item_Data.Guid < 1000)
            item.Item_Data.Guid = System.Guid.NewGuid().GetHashCode();

        RunTimeItems.Add(item.Item_Data.Guid, item);
        AddToGroup(item); // 新增分组逻辑

        return item;
    }

    // ✅ 添加到分组
    private void AddToGroup(Item item)
    {
        string key = item.Item_Data.IDName;
        if (!RuntimeItemsGroup.TryGetValue(key, out var list))
        {
            list = new List<Item>();
            RuntimeItemsGroup[key] = list;
        }
        list.Add(item);
    }

    // ✅ 获取同类物品列表
    public List<Item> GetItemsByNameID(string nameId)
    {
        if (RuntimeItemsGroup.TryGetValue(nameId, out var list))
        {
            return list;
        }
        return new List<Item>();
    }

    // 查找运行时物品
    [Button]
    public Item GetItem(int guid)
    {
        if (RunTimeItems.TryGetValue(guid, out var item))
            return item;
        return null;
    }
}

