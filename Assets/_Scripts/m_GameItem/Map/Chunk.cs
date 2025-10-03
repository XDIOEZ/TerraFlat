using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 管理自己附属的Item
/// </summary>
public class Chunk : MonoBehaviour
{
    [ShowInInspector]
    public Dictionary<int, Item> RunTimeItems = new();
    [ShowInInspector]
    public Dictionary<string, List<Item>> RuntimeItemsGroup = new();
    public Map Map;
    public MapSave MapSave;
    public string ChunkOwner;

    #region 区块加载
    public Chunk LoadChunk_By_MapSaveData_Sync()
    {
        // 使用标准的foreach循环替代ForEach扩展方法
        foreach (var items in MapSave.items)
        {
            foreach(var itemData in items.Value)
            {
                Item item = ItemMgr.Instance.InstantiateItem(itemData,this.gameObject);
                item.Load();
                item.transform.position = itemData.transform.position;
                item.transform.rotation = itemData.transform.rotation;
                item.transform.localScale = itemData.transform.scale;
                RunTimeItems.Add(item.itemData.Guid, item);
                AddToGroup(item);
            }
        }
        ChunkMgr.Instance.AddActiveChunk(this);
        Map.BackTilePenalty_Sync();
        return this;
    }

    public Chunk LoadChunk_Async()
    {
        StartCoroutine(LoadChunkCoroutine());
        return this;
    }

    /// <summary>
    /// 使用协程优化的区块加载方法
    /// </summary>
    private System.Collections.IEnumerator LoadChunkCoroutine()
    {
        // 使用标准的foreach循环替代ForEach扩展方法
        int itemCount = 0;
        const int batchSize = 20; // 每批处理的物品数量
        ChunkMgr.Instance.AddActiveChunk(this);

        foreach (var items in MapSave.items)
        {
            foreach(var itemData in items.Value)
            {
                Item item = ItemMgr.Instance.InstantiateItem(itemData, this.gameObject);
                item.Load();
                item.transform.position = itemData.transform.position;
                item.transform.rotation = itemData.transform.rotation;
                item.transform.localScale = itemData.transform.scale;
                RunTimeItems.Add(item.itemData.Guid, item);
                AddToGroup(item);
                
                itemCount++;
                
                // 每加载一批物品就等待一帧，避免阻塞主线程
                if (itemCount % batchSize == 0)
                {
                    yield return null;
                }
            }
        }
        
        // 确保所有物品都已加载完成
        yield return null;
        //TODO 异步加载完毕后自动烘焙权重
        Map.BackTilePenalty_Sync();
        
        Debug.Log($"✅ 区块加载完成，共加载 {itemCount} 个物品");
    }
    #endregion

    #region 区块保存
    public Chunk SaveChunk()
    {
        MapSave.items.Clear();
        //调用所有item的Save方法
        foreach (var item in RunTimeItems.Values)
        {
            if (item == null) 
            { 
                continue;
            }
            item.Save();
            MapSave.AddItemData(item.itemData);
        }
        return this;
    }
    #endregion

    #region 区块管理
    public void FitChunkItems()
    {
        var items = GetComponentsInChildren<Item>();
        foreach(var item in items)
        {
            item.Start();
            RunTimeItems.Add(item.itemData.Guid, item);
            AddToGroup(item);
        }
    }
    #endregion

    #region 物品分组管理
    // ✅ 添加到分组s
    public void AddToGroup(Item item)
    {
        string key = item.itemData.IDName;
        if (!RuntimeItemsGroup.TryGetValue(key, out var list))
        {
            list = new List<Item>();
            RuntimeItemsGroup[key] = list;
        }
        list.Add(item);
    }
    #endregion

    #region 区块位置计算
    /// <summary>
    /// 获取指定玩家或物体所在的区块坐标
    /// </summary>
    [Button]
    public static Vector2Int GetChunkPosition(Vector2 objPos, Vector2 chunkSize = default)
    {
        //TODO 因为Transformpos 是在左下角 相对于绘制的中心来说 所以需要微调玩家的位置 来输出确切的区块坐标
        if (chunkSize == default)
            chunkSize = ChunkMgr.GetChunkSize();

        Vector2Int chunkPos = new Vector2Int(
            Mathf.FloorToInt(objPos.x / chunkSize.x) * (int)chunkSize.x,
            Mathf.FloorToInt(objPos.y / chunkSize.y) * (int)chunkSize.y
        );
        return chunkPos;
    }
    #endregion

public void AddItem(Item item)
{
     // 检查是否已存在相同的Guid
     if (RunTimeItems.ContainsKey(item.itemData.Guid))
     {
            return;
     }

    // 添加到字典中
    RunTimeItems[item.itemData.Guid] = item;
    AddToGroup(item);
    item.transform.SetParent(this.transform);
}
    public void UpdateItem(Item item)
    {
        // 添加到字典中
        RunTimeItems[item.itemData.Guid] = item;
        item.transform.SetParent(this.transform);
    }

    public void RemoveItem(Item item)
    {
        RunTimeItems.Remove(item.itemData.Guid);
        string key = item.itemData.IDName;
        if (RuntimeItemsGroup.TryGetValue(key, out var list))
        {
            list.Remove(item);
        }
    }
}

[System.Serializable]
[MemoryPackable]
public partial class ChunkData
{
    public string ChunkName;
    public Vector2Int ChunkPosition;
    public MapSave MapSave;
}