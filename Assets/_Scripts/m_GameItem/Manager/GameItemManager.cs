using NUnit.Framework.Interfaces;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class GameItemManager : SingletonMono<GameItemManager>
{
    [ShowInInspector]
    public Dictionary<int, Item> RunTimeItems = new();

    [ShowInInspector]
    public Dictionary<string, List<Item>> RuntimeItemsGroup = new();

    public Map _cachedMap;
    public Map Map
    {
        get
        {
            if (_cachedMap == null)
            {
                if (RuntimeItemsGroup.TryGetValue("MapCore", out var list) && list.Count > 0)
                {
                    _cachedMap = (Map)list[0];
                }
            }
            return _cachedMap;
        }
    }

    [Button("加载所有Runtime物品")]
    protected override void Awake()
    {
        base.Awake();
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
        //    RunTimeItems.Add(item.Item_Data.Guid, item);
            RunTimeItems[item.itemData.Guid] = item;
            AddToGroup(item); // 新增分组逻辑
        }
       // Debug.Log("物品加载完毕");
    }

    private void OnDestroy()
    {
       
    }

    public void Start()
    {
        SaveLoadManager.Instance.OnSceneSwitchStart += CleanupNullItems;
        SaveLoadManager.Instance.OnSaveGame +=  RunTimeItems.Clear;
        SaveLoadManager.Instance.OnSaveGame += RuntimeItemsGroup.Clear;
    }

    // 实例化（通过名称）
    public Item InstantiateItem(string itemName, Vector3 position = default, Quaternion rotation = default, Vector3 scale = default)
    {
        if (position == default) position = Vector3.zero;
        if (rotation == default) rotation = Quaternion.identity;

        // 修复缩放为0的问题 - 如果未指定缩放，使用Vector3.one
        if (scale == default || scale == Vector3.zero)
            scale = Vector3.one;

        GameObject itemObj = GameRes.Instance.InstantiatePrefab(itemName, position, rotation, scale);
        Item item = itemObj.GetComponent<Item>();

        if (item != null)
        {

            if (item.itemData.Guid ==0)
                item.itemData.Guid = System.Guid.NewGuid().GetHashCode();

            RunTimeItems.Add(item.itemData.Guid, item);
            AddToGroup(item); // 新增分组逻辑
        }
        return item;
    }

    // 实例化（通过 ItemData）
    public Item InstantiateItem(ItemData itemData, Vector3 position = default, Quaternion rotation = default, Vector3 scale = default)
    {
        if (position == default) position = Vector3.zero;
        if (rotation == default) rotation = Quaternion.identity;
        if (scale == default) scale = Vector3.one;

        // 检测是否存在重复 GUID
        if (RunTimeItems.ContainsKey(itemData.Guid))
        {
            Debug.LogError($"【物品实例化错误】检测到重复的GUID：{itemData.Guid}\n" +
                $"当前物品：{itemData.IDName}\n" +
                $"已存在物品：{RunTimeItems[itemData.Guid].name}");

            return null;
        }

        GameObject itemObj = GameRes.Instance.InstantiatePrefab(itemData.IDName, position, rotation, scale);
        Item item = itemObj.GetComponent<Item>();
        item.itemData = itemData;

        // 如果 Guid 在 -1000 到 1000 之间，重新生成唯一 Guid
        if (item.itemData.Guid > -1000 && item.itemData.Guid < 1000)
            item.itemData.Guid = System.Guid.NewGuid().GetHashCode();

      

        RunTimeItems.Add(item.itemData.Guid, item);
        AddToGroup(item); // 新增分组逻辑

        return item;
    }



    // ✅ 添加到分组
    private void AddToGroup(Item item)
    {
        string key = item.itemData.IDName;
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
    public Item GetItemByGuid(int guid)
    {
        if (RunTimeItems.TryGetValue(guid, out var item))
            return item;
        return null;
    }

    //TODO 添加一个清理两个字典的中Item空引用的方法
    [Button("清理空引用")]
    public void CleanupNullItems()
    {
        // 清理 RunTimeItems 中为 null 的条目
        var keysToRemove = new List<int>();
        foreach (var pair in RunTimeItems)
        {
            if (pair.Value == null)
            {
                keysToRemove.Add(pair.Key);
            }
        }
        foreach (int key in keysToRemove)
        {
            RunTimeItems.Remove(key);
        }

        // 清理 RuntimeItemsGroup 中为 null 的列表元素
        var groupsToClean = new List<string>(RuntimeItemsGroup.Keys);
        foreach (string key in groupsToClean)
        {
            var list = RuntimeItemsGroup[key];
            list.RemoveAll(item => item == null);

            // 如果列表空了，也可以选择移除整个 key
            if (list.Count == 0)
            {
                RuntimeItemsGroup.Remove(key);
            }
        }

      //  Debug.Log("已清理无效的 Item 引用。");
    }

}

