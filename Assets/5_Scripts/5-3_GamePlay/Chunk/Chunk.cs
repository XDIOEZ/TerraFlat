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
    public enum ChunkLifecycleState
    {
        Created,
        Loading,
        Ready,
        Releasing,
        Pooled
    }

    [ShowInInspector]
    public Dictionary<int, Item> RunTimeItems = new();
    [ShowInInspector]
    public Dictionary<string, HashSet<Item>> RuntimeItemsGroup = new();

    #region Position Index（数组索引）
    [ShowInInspector]
    public List<Item>[,] RunTimeItems_ByPosition_Array = new List<Item>[0, 0];

    private readonly Dictionary<int, Vector2Int> _itemGuidToLocalPos = new();
    private Vector2Int _chunkOrigin;
    private int _posWidth;
    private int _posHeight;
    private bool _hasLoggedArrayNotInit;
    private readonly List<Item> _rangeQueryBuffer = new(16);

    private void EnsurePositionArray(bool initCells = false)
    {
        Vector2 size = ChunkMgr.GetChunkSize();
        int w = (int)size.x;
        int h = (int)size.y;

        if (w <= 0 || h <= 0)
        {
            Debug.LogError($"[Chunk] ChunkSize非法: {w}x{h}", this);
            return;
        }

        _posWidth = w;
        _posHeight = h;
        _chunkOrigin = GetChunkPosition((Vector2)transform.position, size);

        if (RunTimeItems_ByPosition_Array == null
            || RunTimeItems_ByPosition_Array.Length == 0
            || RunTimeItems_ByPosition_Array.GetLength(0) != w
            || RunTimeItems_ByPosition_Array.GetLength(1) != h)
        {
            RunTimeItems_ByPosition_Array = new List<Item>[w, h];
        }

        if (!initCells)
            return;

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                RunTimeItems_ByPosition_Array[x, y] ??= new List<Item>(capacity: 2);
            }
        }
    }

    private bool TryWorldToLocal(Vector2 worldPos, out Vector2Int localPos)
    {
        EnsurePositionArray();

        if (RunTimeItems_ByPosition_Array == null || RunTimeItems_ByPosition_Array.Length == 0)
        {
            if (!_hasLoggedArrayNotInit)
            {
                _hasLoggedArrayNotInit = true;
                Debug.LogError("[Chunk] RunTimeItems_ByPosition_Array未初始化", this);
            }

            localPos = default;
            return false;
        }

        // 物体普遍在格子中心点（+0.5）附近：用 Floor 可以避免 59.5 被 Round 到 60 造成越界
        Vector2Int worldInt = new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
        localPos = worldInt - _chunkOrigin;

        if ((uint)localPos.x >= (uint)_posWidth || (uint)localPos.y >= (uint)_posHeight)
            return false;

        return true;
    }

    private void AddToPositionArray(Item item)
    {
        if (item == null || item.itemData == null)
        {
            Debug.LogError("[Chunk] AddToPositionArray失败: item或itemData为空", this);
            return;
        }

        if (!TryWorldToLocal(item.transform.position, out var localPos))
        {
            Debug.LogError($"[Chunk] 物品不在本Chunk范围内，无法索引: {item.name} pos={item.transform.position} origin={_chunkOrigin}", item);
            return;
        }

        var list = RunTimeItems_ByPosition_Array[localPos.x, localPos.y] ??= new List<Item>(capacity: 2);
        if (!list.Contains(item))
        {
            list.Add(item);
        }

        _itemGuidToLocalPos[item.itemData.Guid] = localPos;
    }

    private void RemoveFromPositionArray(Item item)
    {
        if (item == null || item.itemData == null)
            return;

        if (!_itemGuidToLocalPos.TryGetValue(item.itemData.Guid, out var localPos))
            return;

        var list = RunTimeItems_ByPosition_Array[localPos.x, localPos.y];
        if (list != null)
        {
            list.Remove(item);
        }

        _itemGuidToLocalPos.Remove(item.itemData.Guid);
    }

    private void RemoveFromPositionArrayByGuid(int guid)
    {
        if (!_itemGuidToLocalPos.TryGetValue(guid, out var localPos))
            return;

        var list = RunTimeItems_ByPosition_Array[localPos.x, localPos.y];
        if (list != null)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var it = list[i];
                if (it == null || it.itemData == null || it.itemData.Guid == guid)
                {
                    list.RemoveAt(i);
                }
            }
        }

        _itemGuidToLocalPos.Remove(guid);
    }
    #endregion
    public Map Map;
    public MapSave MapSave;
    public string ChunkOwner;
    public ChunkLifecycleState LifecycleState { get; private set; } = ChunkLifecycleState.Created;
    public bool IsReady => LifecycleState == ChunkLifecycleState.Ready;

    private bool itemsLoaded;
    private bool mapLoaded;

    #region 对象池生命周期

    public void PrepareForReuse(MapSave mapSave)
    {
        StopAllCoroutines();
        MapSave = mapSave;
        Map = null;
        ChunkOwner = null;
        ClearRuntimeState();
        ResetLifecycleState();

        transform.SetPositionAndRotation(
            new Vector3(mapSave.MapPosition.x, mapSave.MapPosition.y, 0f),
            Quaternion.identity);
        transform.localScale = Vector3.one;
        name = mapSave.Name;
        EnsurePositionArray();
    }

    public void PrepareForPool()
    {
        LifecycleState = ChunkLifecycleState.Releasing;
        StopAllCoroutines();
        OnChunkLoaded = null;

        Item[] runtimeItems = GetComponentsInChildren<Item>(includeInactive: true);
        for (int i = 0; i < runtimeItems.Length; i++)
        {
            Item runtimeItem = runtimeItems[i];
            if (runtimeItem == null)
                continue;

            if (ItemMgr.Instance != null)
                ItemMgr.Instance.DespawnItem(runtimeItem, saveData: true, detachFromChunk: false);
            else
                Destroy(runtimeItem.gameObject);
        }

        Map = null;
        MapSave = null;
        ChunkOwner = null;
        ClearRuntimeState();
    }

    public void MarkPooled()
    {
        LifecycleState = ChunkLifecycleState.Pooled;
        name = "PooledChunk";
    }

    private void ClearRuntimeState()
    {
        RunTimeItems.Clear();
        RuntimeItemsGroup.Clear();
        _itemGuidToLocalPos.Clear();
        _rangeQueryBuffer.Clear();
        _hasLoggedArrayNotInit = false;

        if (RunTimeItems_ByPosition_Array == null || RunTimeItems_ByPosition_Array.Length == 0)
            return;

        int width = RunTimeItems_ByPosition_Array.GetLength(0);
        int height = RunTimeItems_ByPosition_Array.GetLength(1);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
                RunTimeItems_ByPosition_Array[x, y]?.Clear();
        }
    }

    #endregion

    /// <summary>
    /// 区块进入可参与后续系统联动的就绪态时的回调。
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

        BeginFullLoad();
        EnsurePositionArray();
        InitializeItems();
        NotifyItemsLoaded();
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
                RestoreItemTransform(item, itemData);
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
        BeginFullLoad();
        EnsurePositionArray();

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
                RestoreItemTransform(item, itemData);
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
        NotifyItemsLoaded();
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
        RestoreItemTransform(item, itemData);
        AddItemInternal(item);
    }

    private static void RestoreItemTransform(Item item, ItemData itemData)
    {
        item.transform.SetPositionAndRotation(itemData.transform.position, itemData.transform.rotation);
        item.transform.localScale = itemData.transform.scale;
        ChunkGenerator_Cave.ApplyGeneratedResourceTransform(DimensionManager.Instance?.ActiveDefinition, item);
    }

    #endregion

    #endregion

    #region 生命周期
    /// <summary>
    /// 重置就绪状态，用于重新加载场景
    /// </summary>
    public void ResetLifecycleState()
    {
        LifecycleState = ChunkLifecycleState.Created;
        itemsLoaded = false;
        mapLoaded = false;
        hasNotifiedChunkReady = false;
    }

    private bool hasNotifiedChunkReady;

    public void BeginFullLoad()
    {
        itemsLoaded = false;
        mapLoaded = false;
        hasNotifiedChunkReady = false;
        LifecycleState = ChunkLifecycleState.Loading;
    }

    public void BeginMapLoad() => BeginFullLoad();

    public void NotifyItemsLoaded()
    {
        itemsLoaded = true;
        if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
            Debug.Log($"[WorldNav][Chunk] NotifyItemsLoaded | chunk={name} itemsLoaded={itemsLoaded} mapLoaded={mapLoaded} Map={Map != null} Map.IsReady={Map?.IsReadyForChunkLifecycle}");

        if (Map == null || Map.IsReadyForChunkLifecycle)
            mapLoaded = true;

        TryEnterReadyState();
    }

    public void NotifyMapLoaded()
    {
        mapLoaded = true;
        if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
            Debug.Log($"[WorldNav][Chunk] NotifyMapLoaded | chunk={name} itemsLoaded={itemsLoaded} mapLoaded={mapLoaded}");
        TryEnterReadyState();
    }

    private void TryEnterReadyState()
    {
        if (itemsLoaded && mapLoaded && !hasNotifiedChunkReady)
        {
            hasNotifiedChunkReady = true;
            LifecycleState = ChunkLifecycleState.Ready;
            if (WorldNavigationManager.Instance?.EnableDebugLogs == true)
                Debug.Log($"[WorldNav][Chunk] Ready | chunk={name} Map={Map != null} TileLoaded={Map?.Data?.TileLoaded}");
            OnChunkLoaded?.Invoke(this);
        }
    }

    public void MarkLoading() => LifecycleState = ChunkLifecycleState.Loading;

    public void MarkReady()
    {
        if (LifecycleState != ChunkLifecycleState.Ready)
        {
            LifecycleState = ChunkLifecycleState.Ready;
            OnChunkLoaded?.Invoke(this);
        }
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

        // Procedural chunks persist only changes against their deterministic baseline.
        // Legacy chunks without a captured baseline continue through the full-snapshot path below.
        if (SaveDataMgr.Instance != null && SaveDataMgr.Instance.TrySaveChunkDifferences(this))
        {
            RefreshPositionDictionary();
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
        EnsurePositionArray();

        _itemGuidToLocalPos.Clear();

        if (RunTimeItems_ByPosition_Array != null && RunTimeItems_ByPosition_Array.Length > 0)
        {
            for (int x = 0; x < _posWidth; x++)
            {
                for (int y = 0; y < _posHeight; y++)
                {
                    RunTimeItems_ByPosition_Array[x, y]?.Clear();
                }
            }
        }

        foreach (var item in RunTimeItems.Values)
        {
            if (item == null)
                continue;

            if (item is Map)
                continue;

            AddToPositionArray(item);
        }
    }
    #endregion

    #region 区块管理
    public void FitChunkItems()
    {
        EnsurePositionArray();
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

        if (item.itemData == null)
        {
            Debug.LogError($"[Chunk] AddItemInternal失败: itemData为空, item={item.name}", item);
            return;
        }

        RunTimeItems[item.itemData.Guid] = item;
        AddToGroup(item);

        if (!(item is Map))
        {
            AddToPositionArray(item);
        }

        ItemMgr.GetInstance()?.NotifyItemSpatialIndexChanged(item);
    }

    /// <summary>
    /// 添加或更新物品
    /// </summary>
    public void AddItem(Item item)
    {
        if (item == null) return;

        if (item.itemData == null)
        {
            Debug.LogError($"[Chunk] AddItem失败: itemData为空, item={item.name}", item);
            return;
        }

        // 检查是否已存在
        RemoveFromPositionArrayByGuid(item.itemData.Guid);

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

        if (item.itemData == null)
            return;

        RunTimeItems.Remove(item.itemData.Guid);
        RemoveFromGroup(item);
        RemoveFromPositionArray(item);
        MapSave?.RemoveItemData(item.itemData);
        ItemMgr.GetInstance()?.NotifyItemSpatialIndexChanged(item);
    }
    #endregion

    #region 区块位置计算
    /// <summary>
    /// 通过位置快速获取物品列表
    /// </summary>
    public bool TryGetItemsByPosition(Vector2 position, out List<Item> items)
    {
        items = null;
        if (!TryWorldToLocal(position, out var localPos))
            return false;

        items = RunTimeItems_ByPosition_Array[localPos.x, localPos.y];
        return items != null && items.Count > 0;
    }

    /// <summary>
    /// 通过位置获取第一个物品（向后兼容）
    /// </summary>
    public bool TryGetItemByPosition(Vector2 position, out Item item)
    {
        item = null;
        if (!TryGetItemsByPosition(position, out var items) || items == null || items.Count == 0)
            return false;

        item = items[0];
        return item != null;
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
        // 与索引规则保持一致：物体通常在格子中心(整数+0.5)，查询也应落在对应整数格
        return new Vector2(Mathf.Floor(position.x), Mathf.Floor(position.y));
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
        _rangeQueryBuffer.Clear();
        bool found = TryGetItemsInRangeNonAlloc(position, radius, _rangeQueryBuffer);
        allItems = found ? new List<Item>(_rangeQueryBuffer) : new List<Item>();
        return found;
    }

    public bool TryGetItemsInRangeNonAlloc(Vector2 position, float radius, List<Item> results)
    {
        if (results == null)
        {
            throw new ArgumentNullException(nameof(results));
        }

        results.Clear();
        if (radius < 0)
            return false;

        EnsurePositionArray();

        Vector2 roundedPos = RoundPositionForQuery(position);
        if (!TryWorldToLocal(roundedPos, out var centerLocal))
            return false;

        int r = Mathf.CeilToInt(radius);
        int minX = Mathf.Max(0, centerLocal.x - r);
        int maxX = Mathf.Min(_posWidth - 1, centerLocal.x + r);
        int minY = Mathf.Max(0, centerLocal.y - r);
        int maxY = Mathf.Min(_posHeight - 1, centerLocal.y + r);

        float sqrRadius = radius * radius;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                var list = RunTimeItems_ByPosition_Array[x, y];
                if (list == null || list.Count == 0)
                    continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var it = list[i];
                    if (it == null)
                        continue;

                    Vector2 p = RoundPositionForQuery(it.transform.position);
                    Vector2 d = p - roundedPos;
                    if (d.sqrMagnitude <= sqrRadius)
                    {
                        results.Add(it);
                    }
                }
            }
        }

        return results.Count > 0;
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
        if (radius < 0)
            return false;

        EnsurePositionArray();

        Vector2 roundedPos = RoundPositionForQuery(position);
        if (!TryWorldToLocal(roundedPos, out var centerLocal))
            return false;

        int r = Mathf.CeilToInt(radius);
        int minX = Mathf.Max(0, centerLocal.x - r);
        int maxX = Mathf.Min(_posWidth - 1, centerLocal.x + r);
        int minY = Mathf.Max(0, centerLocal.y - r);
        int maxY = Mathf.Min(_posHeight - 1, centerLocal.y + r);

        float sqrRadius = radius * radius;
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                var list = RunTimeItems_ByPosition_Array[x, y];
                if (list == null || list.Count == 0)
                    continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var it = list[i];
                    if (it == null)
                        continue;

                    Vector2 p = RoundPositionForQuery(it.transform.position);
                    Vector2 d = p - roundedPos;
                    if (d.sqrMagnitude <= sqrRadius)
                    {
                        item = it;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    #region Chunk内实例化（跳过ItemMgr按位置找Chunk）
    public Item InstantiateItemInChunk(ItemData itemData, Vector3 position, Quaternion rotation = default, Vector3 scale = default)
    {
        if (ItemMgr.Instance == null)
        {
            Debug.LogError("[Chunk] ItemMgr.Instance为空，无法实例化物品", this);
            return null;
        }

        Item item = ItemMgr.Instance.InstantiateItem(itemData, position, rotation, scale, parent: gameObject);
        AddItem(item);
        return item;
    }

    public Item InstantiateItemInChunk(string itemName, Vector3 position, Quaternion rotation = default, Vector3 scale = default)
    {
        if (ItemMgr.Instance == null)
        {
            Debug.LogError("[Chunk] ItemMgr.Instance为空，无法实例化物品", this);
            return null;
        }

        Item item = ItemMgr.Instance.InstantiateItem(itemName, position, rotation, scale, parent: gameObject);
        AddItem(item);
        return item;
    }

    public Item InstantiateItemInChunkDeterministic(
        string itemName,
        int deterministicGuid,
        Vector3 position,
        Quaternion rotation = default,
        Vector3 scale = default)
    {
        if (ItemMgr.Instance == null)
        {
            Debug.LogError("[Chunk] ItemMgr.Instance为空，无法实例化确定性物品", this);
            return null;
        }

        Item item = ItemMgr.Instance.InstantiateItemDeterministic(
            itemName,
            deterministicGuid,
            position,
            rotation,
            scale,
            parent: gameObject);
        AddItem(item);
        return item;
    }
    #endregion
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
