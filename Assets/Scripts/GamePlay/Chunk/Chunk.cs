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
    public Dictionary<Vector2, List<Item>> RunTimeItems_ByPosition = new();
    public Map Map;
    public MapSave MapSave;
    public string ChunkOwner;

    /// <summary>
    /// 区块完成加载（所有物品加载完并烘焙完权重）时的回调
    /// </summary>
    public event System.Action<Chunk> OnChunkLoaded;
    public int ItemBatchSize = 1; // 每批处理的物品数量

    #region 区块加载
    #region 同步加载区块的所有数据
    public Chunk LoadChunkFromMapSave()
    {
        if (MapSave?.items == null)
        {
            Debug.LogWarning($"⚠️ 区块 {name} 的MapSave或items为空");
            return this;
        }

        InitializeItems();
        ChunkMgr.Instance.AddActiveChunk(this);
        Map?.BackTilePenalty_Sync();
        // 通知监听者：区块已完全加载
        OnChunkLoaded?.Invoke(this);
        return this;
    }

    /// <summary>
    /// 同步加载物品
    /// </summary>
    private void InitializeItems()
    {
        // 第一步：实例化所有物品，但暂不调用它们的 Load
        List<Item> createdItems = new List<Item>();

        foreach (var items in MapSave.items)
        {
            if (items.Value == null) continue;

            foreach (var itemData in items.Value)
            {
                if (itemData == null) continue;

                Item item = ItemMgr.Instance.InstantiateItem(itemData, gameObject);
                if (item == null) continue;

                // 先恢复位置信息和加入运行时字典
                item.transform.SetPositionAndRotation(itemData.transform.position, itemData.transform.rotation);
                item.transform.localScale = itemData.transform.scale;
                AddItemInternal(item);

                createdItems.Add(item);
            }
        }

        // 第二步：统一调用所有刚实例化物品的 Load 方法
        foreach (var item in createdItems)
        {
            if (item == null) continue;
            item.Load();
        }
    }
    #endregion

    #region 异步加载区块的所有数据
    /// <summary>
    /// 异步加载物品（按批处理）
    /// </summary>
    public System.Collections.IEnumerator BatchLoadItemsCoroutine()
    {
        int itemCount = 0;
        ChunkMgr.Instance.AddActiveChunk(this);

        // 第一步：按批实例化所有物品，但先不调用它们的 Load
        List<Item> createdItems = new List<Item>();

        foreach (var items in MapSave.items)
        {
            if (items.Value == null) continue;

            foreach (var itemData in items.Value)
            {
                if (itemData == null) continue;

                Item item = ItemMgr.Instance.InstantiateItem(itemData, gameObject);
                if (item == null) continue;

                // 先恢复位置信息和加入运行时字典
                item.transform.SetPositionAndRotation(itemData.transform.position, itemData.transform.rotation);
                item.transform.localScale = itemData.transform.scale;
                AddItemInternal(item);

                createdItems.Add(item);
                itemCount++;

                // 每加载一批物品就等待一帧，避免阻塞主线程
                if (itemCount % ItemBatchSize == 0)
                {
                    yield return null;
                }
            }
        }

        // 第二步：分批调用所有刚实例化物品的 Load 方法
        int processedCount = 0;
        foreach (var item in createdItems)
        {
            if (item == null) continue;
            item.Load();
            processedCount++;

            if (processedCount % ItemBatchSize == 0)
            {
                yield return null;
            }
        }

        // 确保所有物品都已加载完成
        yield return null;
        OnChunkLoaded?.Invoke(this);
    }
    #endregion

    #region  工具方法
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

    #endregion

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
                Vector2 pos = item.transform.position;
                if (!RunTimeItems_ByPosition.ContainsKey(pos))
                {
                    RunTimeItems_ByPosition[pos] = new List<Item>();
                }
                RunTimeItems_ByPosition[pos].Add(item);
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
        Vector2 pos = item.transform.position;
        if (!RunTimeItems_ByPosition.ContainsKey(pos))
        {
            RunTimeItems_ByPosition[pos] = new List<Item>();
        }
        RunTimeItems_ByPosition[pos].Add(item);
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
            // 如果是更新，先从旧位置移除
            foreach (var kvp in RunTimeItems_ByPosition)
            {
                kvp.Value.RemoveAll(i => i.itemData.Guid == item.itemData.Guid);
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
        Vector2 pos = item.transform.position;
        if (RunTimeItems_ByPosition.TryGetValue(pos, out var items))
        {
            items.Remove(item);
            if (items.Count == 0)
            {
                RunTimeItems_ByPosition.Remove(pos);
            }
        }
        MapSave?.RemoveItemData(item.itemData);
    }
    #endregion

    #region 区块位置计算
    /// <summary>
    /// 通过位置快速获取物品列表
    /// </summary>
    public bool TryGetItemsByPosition(Vector2 position, out List<Item> items)
    {
        return RunTimeItems_ByPosition.TryGetValue(position, out items);
    }

    /// <summary>
    /// 通过位置获取第一个物品（向后兼容）
    /// </summary>
    public bool TryGetItemByPosition(Vector2 position, out Item item)
    {
        item = null;
        if (RunTimeItems_ByPosition.TryGetValue(position, out var items) && items.Count > 0)
        {
            item = items[0];
            return true;
        }
        return false;
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

    /// <summary>
    /// 对位置进行取整（用于位置查询前的预处理）
    /// 将浮点坐标取整为整数坐标，便于查询字典中的Item
    /// 处理游戏中坐标向右上0.5偏移的问题
    /// </summary>
    public static Vector2 RoundPositionForQuery(Vector2 position)
    {
        // 先取整到最近的整数
        Vector2 roundedPos = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));
        // 然后加上0.5的偏移（向右上）
        return roundedPos + new Vector2(0.5f, 0.5f);
    }

    /// <summary>
    /// 获取指定位置及其周围范围内的所有物品
    /// </summary>
    /// <param name="position">要查询的位置</param>
    /// <param name="radius">搜索范围（半径），默认为1</param>
    /// <param name="allItems">输出：找到的所有物品列表</param>
    /// <returns>是否找到物品</returns>
    public bool TryGetItemsInRange(Vector2 position, float radius, out List<Item> allItems)
    {
        allItems = new List<Item>();

        // 先对位置进行取整
        Vector2 roundedPos = RoundPositionForQuery(position);

        // 遍历所有物品位置，查找范围内的物品
        foreach (var kvp in RunTimeItems_ByPosition)
        {
            float distance = Vector2.Distance(kvp.Key, roundedPos);
            if (distance <= radius && kvp.Value != null)
            {
                allItems.AddRange(kvp.Value);
            }
        }

        return allItems.Count > 0;
    }

    /// <summary>
    /// 获取指定位置及其周围范围内的第一个物品
    /// </summary>
    /// <param name="position">要查询的位置</param>
    /// <param name="radius">搜索范围（半径），默认为1</param>
    /// <param name="item">输出：找到的物品，未找到时为null</param>
    /// <returns>是否找到物品</returns>
    public bool TryGetItemInRange(Vector2 position, float radius, out Item item)
    {
        item = null;

        if (TryGetItemsInRange(position, radius, out var items) && items.Count > 0)
        {
            item = items[0];
            return true;
        }

        return false;
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