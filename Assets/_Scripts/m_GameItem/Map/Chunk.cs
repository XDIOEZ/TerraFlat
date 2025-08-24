using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Chunk : MonoBehaviour
{
    [ShowInInspector]
    public Dictionary<int, Item> RunTimeItems = new();
    [ShowInInspector]
    public Dictionary<string, List<Item>> RuntimeItemsGroup = new();
    public Map Map;
    public MapSave MapSave;
    public void Init()
    {
        Map = GetComponentInChildren<Map>();

        Map.Act();

        // 第一步：获取场景中所有的 Item（包括非激活状态）
        Item[] allItems = this.GetComponentsInChildren<Item>(includeInactive: true);

        foreach (Item item in allItems)
        {
            RunTimeItems[item.itemData.Guid] = item;
            AddToGroup(item); // 新增分组逻辑
        }

    }

    // ✅ 添加到分组s
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

    /// <summary>
    /// 获取指定玩家或物体所在的区块坐标
    /// </summary>
    [Button]
    public static Vector2Int GetChunkPosition(Vector2 objPos)
    {
        Vector2 chunkSize = SaveDataManager.Instance.SaveData.ChunkSize;
       Vector2Int chunkPos = new Vector2Int(
            Mathf.FloorToInt(objPos.x / chunkSize.x) * (int)chunkSize.x,
            Mathf.FloorToInt(objPos.y / chunkSize.y) * (int)chunkSize.y
        );
        return chunkPos;
    }

}
