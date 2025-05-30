using NUnit.Framework.Interfaces;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class RunTimeItemManager :SingletonMono<RunTimeItemManager>
{
    public Dictionary<int, Item> RunTimeItems = new Dictionary<int, Item>();
    //ʵ����Item
    public Item InstantiateItem(string itemName, Vector3 position = default, Quaternion rotation = default)
    {
        GameObject itemObj = GameRes.Instance.InstantiatePrefab(itemName, position);
        Item item = itemObj.GetComponent<Item>();
        if(item.Item_Data.Guid > -1000&&item.Item_Data.Guid < 1000)
           item.Item_Data.Guid = System.Guid.NewGuid().GetHashCode();
        //��ӵ�����ʱ��Ʒ�б�
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
        //��ӵ�����ʱ��Ʒ�б�
        RunTimeItems.Add(item.Item_Data.Guid, item);

        return item;
    }

    //��������ʱ��Ʒ
    [Button]
    public Item FindItem(int guid)
    {
        if (RunTimeItems.ContainsKey(guid))
            return RunTimeItems[guid];
        return null;
    }
}
