using NUnit.Framework.Interfaces;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class RunTimeItemManager :SingletonMono<RunTimeItemManager>
{
    public Dictionary<int, Item> RunTimeItems = new Dictionary<int, Item>();
    //实例化Item
    public Item InstantiateItem(string itemName, Vector3 position = default, Quaternion rotation = default)
    {
        GameObject itemObj = GameRes.Instance.InstantiatePrefab(itemName, position);
        Item item = itemObj.GetComponent<Item>();
        if(item.Item_Data.Guid > -1000&&item.Item_Data.Guid < 1000)
           item.Item_Data.Guid = System.Guid.NewGuid().GetHashCode();
        //添加到运行时物品列表
        RunTimeItems.Add(item.Item_Data.Guid, item);

        return item;
    }
    public Item InstantiateItem(ItemData itemData,Vector3 position = default, Quaternion rotation = default)
    {
        GameObject itemObj = GameRes.Instance.InstantiatePrefab(itemData.IDName, position);
        Item item = itemObj.GetComponent<Item>();
        item.Item_Data = itemData;
        if (item.Item_Data.Guid > -1000 && item.Item_Data.Guid < 1000)
            item.Item_Data.Guid = System.Guid.NewGuid().GetHashCode();
        //添加到运行时物品列表
        RunTimeItems.Add(item.Item_Data.Guid, item);

        return item;
    }

    //查找运行时物品
    [Button]
    public Item FindItem(int guid)
    {
        if (RunTimeItems.ContainsKey(guid))
            return RunTimeItems[guid];
        return null;
    }
}
