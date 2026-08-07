using System;
using System.Collections.Generic;

/// <summary>
/// ItemMgr 的纯运行时索引：只负责 GUID、类型分组和可调度 Item 列表。
/// 它不参与 Unity 实例化、Chunk 归属或 Tick 执行。
/// </summary>
internal sealed class ItemRuntimeRegistry
{
    public Dictionary<int, Item> ItemsByGuid { get; } = new();
    public Dictionary<string, List<Item>> Groups { get; } = new();
    public List<Item> Items { get; } = new();

    public void Register(Item item, Func<int> generateGuid)
    {
        if (ItemsByGuid.ContainsKey(item.itemData.Guid))
        {
            item.itemData.Guid = generateGuid();
        }

        ItemsByGuid[item.itemData.Guid] = item;
        AddToGroup(item);
        if (!Items.Contains(item))
        {
            Items.Add(item);
        }
    }

    public void AddToGroup(Item item)
    {
        string key = item.itemData.IDName;
        if (!Groups.TryGetValue(key, out List<Item> group))
        {
            group = new List<Item>();
            Groups[key] = group;
        }

        if (!group.Contains(item))
        {
            group.Add(item);
        }
    }

    public void Remove(Item item)
    {
        ItemsByGuid.Remove(item.itemData.Guid);

        string key = item.itemData.IDName;
        if (Groups.TryGetValue(key, out List<Item> group))
        {
            group.Remove(item);
            if (group.Count == 0)
            {
                Groups.Remove(key);
            }
        }

        Items.Remove(item);
    }

    public void CleanupNullItems()
    {
        List<int> keysToRemove = new();
        foreach (KeyValuePair<int, Item> pair in ItemsByGuid)
        {
            if (pair.Value == null)
            {
                keysToRemove.Add(pair.Key);
            }
        }

        for (int i = 0; i < keysToRemove.Count; i++)
        {
            ItemsByGuid.Remove(keysToRemove[i]);
        }

        Items.RemoveAll(item => item == null);

        List<string> groupsToClean = new(Groups.Keys);
        for (int i = 0; i < groupsToClean.Count; i++)
        {
            string key = groupsToClean[i];
            List<Item> group = Groups[key];
            group.RemoveAll(item => item == null);
            if (group.Count == 0)
            {
                Groups.Remove(key);
            }
        }
    }
}
