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
    public Dictionary<string, HashSet<Item>> RuntimeItemsGroup = new();
    [ShowInInspector]
    public Dictionary<Vector2, Item> RunTimeItems_ByPosition = new();
    public Map Map;
    public MapSave MapSave;
    public string ChunkOwner;

    #region 区块加载
    private const int ItemBatchSize = 20; // 每批处理的物品数量

    public Chunk LoadChunk_By_MapSaveData_Sync()
    {
        if (MapSave?.items == null)
        {
            Debug.LogWarning($"⚠️ 区块 {name} 的MapSave或items为空");
            return this;
        }

        LoadItemsSync();
        CompleteChunkLoading();
        return this;
    }

    public Chunk LoadChunk_Async()
    {
        if (MapSave?.items == null)
        {
            Debug.LogWarning($"⚠️ 区块 {name} 的MapSave或items为空");
            return this;
        }

        StartCoroutine(LoadChunkCoroutine());
        return this;
    }

    /// <summary>
    /// 同步加载物品
    /// </summary>
    private void LoadItemsSync()
    {
        foreach (var items in MapSave.items)
        {
            if (items.Value == null) continue;
            
            foreach (var itemData in items.Value)
            {
                LoadSingleItem(itemData);
            }
        }
    }

    /// <summary>
    /// 异步加载物品（按批处理）
    /// </summary>
    private System.Collections.IEnumerator LoadChunkCoroutine()
    {
        int itemCount = 0;
        ChunkMgr.Instance.AddActiveChunk(this);

        foreach (var items in MapSave.items)
        {
            if (items.Value == null) continue;

            foreach (var itemData in items.Value)
            {
                LoadSingleItem(itemData);
                itemCount++;

                // 每加载一批物品就等待一帧，避免阻塞主线程
                if (itemCount % ItemBatchSize == 0)
                {
                    yield return null;
                }
            }
        }

        // 确保所有物品都已加载完成
        yield return null;
        FinalizeChunkLoading(itemCount);
    }

    /// <summary>
    /// 加载单个物品
    /// </summary>
    private void LoadSingleItem(ItemData itemData)
    {
        if (itemData == null) return;

        Item item = ItemMgr.Instance.InstantiateItem(itemData, gameObject);
        if (item == null) return;

        item.Load();
        item.transform.SetPositionAndRotation(itemData.transform.position, itemData.transform.rotation);
        item.transform.localScale = itemData.transform.scale;
        AddItemInternal(item);
    }

    /// <summary>
    /// 完成区块加载（同步）
    /// </summary>
    private void CompleteChunkLoading()
    {
        ChunkMgr.Instance.AddActiveChunk(this);
        Map?.BackTilePenalty_Sync();
    }

    /// <summary>
    /// 完成区块加载（异步）
    /// </summary>
    private void FinalizeChunkLoading(int itemCount)
    {
        Map?.BackTilePenalty_Sync();
        Debug.Log($"✅ 区块加载完成，共加载 {itemCount} 个物品");
    }
    #endregion

    #region 区块保存
    public Chunk SaveChunk()
    {
        if (MapSave == null)
        {
            Debug.LogError($"❌ 区块 {name} 的MapSave为空，无法保存");
            return this;
        }

        MapSave.items.Clear();
        
        // 调用所有item的Save方法
        foreach (var item in RunTimeItems.Values)
        {
            if (item == null) continue;
            
            item.Save();
            MapSave.AddItemData(item.itemData);
        }

        // 同时更新位置字典（确保位置字典数据一致）
        RefreshPositionDictionary();
        
        return this;
    }

    /// <summary>
    /// 刷新位置字典，重建基于当前物品位置的映射
    /// </summary>
    private void RefreshPositionDictionary()
    {
        RunTimeItems_ByPosition.Clear();
        foreach (var item in RunTimeItems.Values)
        {
            if (item != null)
            {
                RunTimeItems_ByPosition[item.transform.position] = item;
            }
        }
    }
    #endregion

    #region 区块管理
    public void FitChunkItems()
    {
        var items = GetComponentsInChildren<Item>();
        foreach (var item in items)
        {
            if (item == null) continue;
            
            // 避免重复添加
            if (RunTimeItems.ContainsKey(item.itemData.Guid))
            {
                Debug.LogWarning($"⚠️ 物品 {item.itemData.IDName} 已存在，跳过重复添加");
                continue;
            }

            item.Start();
            AddItemInternal(item);
        }
    }
    #endregion

    #region 物品分组管理
    /// <summary>
    /// 添加物品到分组
    /// </summary>
    public void AddToGroup(Item item)
    {
        if (item == null) return;

        string key = item.itemData.IDName;
        if (!RuntimeItemsGroup.TryGetValue(key, out var set))
        {
            set = new HashSet<Item>();
            RuntimeItemsGroup[key] = set;
        }
        set.Add(item);
    }

    /// <summary>
    /// 从分组移除物品
    /// </summary>
    private void RemoveFromGroup(Item item)
    {
        if (item == null) return;

        string key = item.itemData.IDName;
        if (RuntimeItemsGroup.TryGetValue(key, out var set))
        {
            set.Remove(item);
        }
    }
    #endregion

    #region 物品添加移除
    /// <summary>
    /// 内部方法：添加物品到运行时字典和分组
    /// </summary>
    private void AddItemInternal(Item item)
    {
        if (item == null) return;

        RunTimeItems[item.itemData.Guid] = item;
        AddToGroup(item);
        RunTimeItems_ByPosition[item.transform.position] = item;
    }

    /// <summary>
    /// 添加或更新物品
    /// </summary>
    public void AddItem(Item item)
    {
        if (item == null) return;

        // 检查是否已存在
        if (RunTimeItems.ContainsKey(item.itemData.Guid))
        {
            Debug.LogWarning($"⚠️ 物品 {item.itemData.IDName} 已存在，执行更新操作");
            // 如果是更新，先从旧位置移除
            foreach (var kvp in RunTimeItems_ByPosition)
            {
                if (kvp.Value.itemData.Guid == item.itemData.Guid)
                {
                    RunTimeItems_ByPosition.Remove(kvp.Key);
                    break;
                }
            }
        }

        AddItemInternal(item);
        item.transform.SetParent(transform);
    }

    /// <summary>
    /// 更新物品（已废弃，改用AddItem）
    /// </summary>
    [System.Obsolete("使用 AddItem 代替", false)]
    public void UpdateItem(Item item)
    {
        AddItem(item);
    }

    /// <summary>
    /// 移除物品
    /// </summary>
    public void RemoveItem(Item item)
    {
        if (item == null) return;

        RunTimeItems.Remove(item.itemData.Guid);
        RemoveFromGroup(item);
        RunTimeItems_ByPosition.Remove(item.transform.position);
        MapSave?.RemoveItemData(item.itemData);
    }
    #endregion

    #region 区块位置计算
    /// <summary>
    /// 通过位置快速获取物品
    /// </summary>
    public bool TryGetItemByPosition(Vector2 position, out Item item)
    {
        return RunTimeItems_ByPosition.TryGetValue(position, out item);
    }

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
}

[System.Serializable]
[MemoryPackable]
public partial class ChunkData
{
    public string ChunkName;
    public Vector2Int ChunkPosition;
    public MapSave MapSave;
}