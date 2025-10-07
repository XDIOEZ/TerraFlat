using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections.Generic;

[MemoryPackable]
[System.Serializable]
public partial class MapSave
{
    public string Name;

    [ShowInInspector]
    // 将原先存储单个 ItemData 的字典改为存储 HashSet<ItemData>，key 为物品名称
    public Dictionary<string, HashSet<ItemData>> items = new Dictionary<string, HashSet<ItemData>>();

    public float SunlightIntensity;

    public UnityEngine.Vector2Int MapPosition;

    public void AddItemData(ItemData itemData)
    {
        string key = itemData.IDName;
        if (!items.TryGetValue(key, out var set))
        {
            set = new HashSet<ItemData>();
            items[key] = set;
        }
        set.Add(itemData);
    }
    
    public void RemoveItemData(ItemData itemData)
    {
        string key = itemData.IDName;
        if (items.TryGetValue(key, out var set))
        {
            set.Remove(itemData);
            // 如果集合为空，可以选择移除整个键值对
            if (set.Count == 0)
            {
                items.Remove(key);
            }
        }
    }
}