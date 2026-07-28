using Force.DeepCloner;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

// AI-Context: Item 实例、运行时注册和网络玩家副本的生命周期管理器；远程视觉物不得进入本地 Tick/存档索引。

public class ItemMgr : SingletonMono<ItemMgr>
{
    /// <summary>
    /// AI-Context: 网络层通过这两个事件观察运行时 Item 生命周期；GamePlay 层不反向依赖 Mirror。
    /// 事件发生在注册完成后、销毁注销前，订阅方不得在回调内再次销毁同一 Item。
    /// </summary>
    public static event Action<Item> RuntimeItemInstantiated;
    public static event Action<Item> RuntimeItemDespawning;

    #region Const
    private const string GROUP_MAP_CORE = "MapCore";
    #endregion

    #region Runtime Data
    [ShowInInspector]
    public Dictionary<int, Item> WorldRunTimeItems = new();

    [ShowInInspector]
    public Dictionary<string, List<Item>> RuntimeItemsGroup = new();

    [ShowInInspector]
    public Dictionary<string, Player> Player_DIC = new();

    private Map _cachedMap;

    private Transform _externalPlayerTransform;

    private readonly HashSet<Player> _networkPlayers = new();
    private readonly HashSet<Player> _networkRemoteReplicas = new();
    private readonly HashSet<Player> _networkInitializedPlayers = new();

    private readonly List<Item> _runtimeItems = new();
    private readonly List<Item> _updateSnapshot = new(256);
    private readonly List<Collider2D> _spatialColliderBuffer = new(4);
    private readonly Dictionary<Item, Collider2D[]> _perceptionColliderCache = new();

    private const float PerceptionCellSize = 8f;
    private readonly Dictionary<long, HashSet<Item>> _perceptionCells = new();
    private readonly Dictionary<Item, long> _itemPerceptionCells = new();
    private readonly Stack<HashSet<Item>> _perceptionCellPool = new();

    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int PerceptionCellCount => _perceptionCells.Count;

    #region 并行感知调度

    private const int PerceptionJobBatchSize = 8;

    private readonly List<PendingDetectorRequest> _pendingDetectorRequests = new(64);
    private readonly HashSet<Mod_ItemDetector> _pendingDetectorSet = new();
    private readonly List<Mod_ItemDetector> _inFlightDetectors = new(64);
    private readonly List<long> _inFlightDetectorVersions = new(64);
    private readonly List<DetectorQuerySnapshot> _perceptionQueryData = new(64);
    private readonly List<Item> _perceptionSnapshotItems = new(256);
    private readonly List<PerceptionItemSnapshot> _perceptionSnapshotData = new(256);
    private readonly HashSet<long> _perceptionSnapshotCells = new();
    private readonly HashSet<Item> _perceptionSnapshotItemSet = new();
    private readonly List<Item> _detectorApplyBuffer = new(64);

    private NativeList<PerceptionItemSnapshot> _inFlightItemSnapshots;
    private NativeList<DetectorQuerySnapshot> _inFlightQueries;
    private NativeParallelMultiHashMap<long, int> _inFlightSpatialMap;
    private NativeStream _inFlightResults;
    private JobHandle _perceptionJobHandle;
    private bool _perceptionJobScheduled;

    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int PendingDetectorQueryCount => _pendingDetectorRequests.Count;
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int InFlightDetectorQueryCount => _perceptionJobScheduled
        ? _inFlightDetectors.Count
        : 0;

    private readonly struct PendingDetectorRequest
    {
        public readonly Mod_ItemDetector Detector;
        public readonly long Version;

        public PendingDetectorRequest(Mod_ItemDetector detector, long version)
        {
            Detector = detector;
            Version = version;
        }
    }

    private struct PerceptionItemSnapshot
    {
        public long CellKey;
        public int Guid;
        public int InstanceId;
        public int LayerBit;
        public float2 BoundsCenter;
        public float2 BoundsExtents;
    }

    private struct DetectorQuerySnapshot
    {
        public float2 Center;
        public float Radius;
        public int LayerMask;
        public int ExcludedInstanceId;
    }

    [BurstCompile]
    private struct BatchedPerceptionQueryJob : IJobParallelFor
    {
        [Unity.Collections.ReadOnly] public NativeArray<PerceptionItemSnapshot> Items;
        [Unity.Collections.ReadOnly] public NativeArray<DetectorQuerySnapshot> Queries;
        [Unity.Collections.ReadOnly] public NativeParallelMultiHashMap<long, int> SpatialMap;
        public NativeStream.Writer Results;

        public void Execute(int queryIndex)
        {
            DetectorQuerySnapshot query = Queries[queryIndex];
            NativeStream.Writer writer = Results;
            writer.BeginForEachIndex(queryIndex);

            int minCellX = WorldToCell(query.Center.x - query.Radius) - 1;
            int maxCellX = WorldToCell(query.Center.x + query.Radius) + 1;
            int minCellY = WorldToCell(query.Center.y - query.Radius) - 1;
            int maxCellY = WorldToCell(query.Center.y + query.Radius) + 1;

            for (int cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                for (int cellY = minCellY; cellY <= maxCellY; cellY++)
                {
                    long cellKey = PackCell(cellX, cellY);
                    if (!SpatialMap.TryGetFirstValue(cellKey, out int itemIndex, out NativeParallelMultiHashMapIterator<long> iterator))
                        continue;

                    do
                    {
                        PerceptionItemSnapshot candidate = Items[itemIndex];
                        if (candidate.InstanceId == query.ExcludedInstanceId ||
                            (candidate.LayerBit & query.LayerMask) == 0)
                        {
                            continue;
                        }

                        float2 distanceToBounds = math.max(
                            math.abs(candidate.BoundsCenter - query.Center) - candidate.BoundsExtents,
                            0f);
                        if (math.lengthsq(distanceToBounds) > query.Radius * query.Radius)
                            continue;

                        writer.Write(itemIndex);
                    }
                    while (SpatialMap.TryGetNextValue(out itemIndex, ref iterator));
                }
            }

            writer.EndForEachIndex();
        }

        private static int WorldToCell(float coordinate)
        {
            return (int)math.floor(coordinate / PerceptionCellSize);
        }

        private static long PackCell(int x, int y)
        {
            return ((long)x << 32) ^ (uint)y;
        }
    }

    #endregion

    #region 分级更新调度

    private const int ScheduleBucketCount = 8;
    private const float FastTickSlice = 0.05f / ScheduleBucketCount;
    private const float NormalTickSlice = 0.1f / ScheduleBucketCount;
    private const float SlowTickSlice = 0.25f / ScheduleBucketCount;

    private readonly List<Item> _everyFrameItems = new(64);
    private readonly List<Item>[] _fastTickBuckets = CreateTickBuckets();
    private readonly List<Item>[] _normalTickBuckets = CreateTickBuckets();
    private readonly List<Item>[] _slowTickBuckets = CreateTickBuckets();
    private readonly HashSet<Item> _dirtyScheduleItems = new();

    private float _fastTickTimer;
    private float _normalTickTimer;
    private float _slowTickTimer;
    private int _fastTickCursor = -1;
    private int _normalTickCursor = -1;
    private int _slowTickCursor = -1;

    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int EveryFrameItemCount => _everyFrameItems.Count;
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int FastTickItemCount => CountBucketItems(_fastTickBuckets);
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int NormalTickItemCount => CountBucketItems(_normalTickBuckets);
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int SlowTickItemCount => CountBucketItems(_slowTickBuckets);
    [ShowInInspector, Sirenix.OdinInspector.ReadOnly] private int DormantItemCount => Mathf.Max(
        0,
        _runtimeItems.Count - EveryFrameItemCount - FastTickItemCount - NormalTickItemCount - SlowTickItemCount);

    #endregion

    #region Item对象池

    [Header("Item对象池")]
    [SerializeField, Min(1)] private int maxPoolSizePerItem = 24;
    [SerializeField, Min(1)] private int maxTotalPooledItems = 256;

    [ShowInInspector]
    private readonly Dictionary<string, Queue<Item>> _itemPools = new();
    private Transform _itemPoolRoot;
    private int _totalPooledItems;

    public int TotalPooledItemCount => _totalPooledItems;

    #endregion
    #endregion

    #region Properties

    public string PlayerInSceneName
    {
        get
        {
            string playerName = SaveDataMgr.Instance.CurrentContrrolPlayerName;
            if (Player_DIC.TryGetValue(playerName, out Player runtimePlayer) && runtimePlayer?.Data != null)
            {
                return runtimePlayer.Data.CurrentSceneName;
            }

            // 联机玩家尚未完成 Item 实例创建时，从同步后的玩家存档提供当前场景作为保底。
            if (SaveDataMgr.Instance.SaveData?.PlayerData_Dict != null &&
                SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(playerName, out Data_Player playerData) &&
                !string.IsNullOrWhiteSpace(playerData.CurrentSceneName))
            {
                return playerData.CurrentSceneName;
            }

            return SceneManager.GetActiveScene().name;
        }
    }
    public Player User_Player
    {
        get
        {
            if (Player_DIC.TryGetValue(SaveDataMgr.Instance.CurrentContrrolPlayerName, out var player))
            {
                return player;
            }

            return null;
        }
    }

    /// <summary>
    /// 当前本地玩家的 Transform。单机与联机模式均优先返回核心 Player Item。
    /// </summary>
    public Transform UserPlayerTransform
    {
        get
        {
            string playerName = SaveDataMgr.Instance.CurrentContrrolPlayerName;
            if (Player_DIC.TryGetValue(playerName, out Player runtimePlayer) && runtimePlayer != null)
                return runtimePlayer.transform;

            return _externalPlayerTransform;
        }
    }

    public void RegisterExternalPlayerTransform(Transform playerTransform)
    {
        _externalPlayerTransform = playerTransform;
    }

    public void UnregisterExternalPlayerTransform(Transform playerTransform)
    {
        if (_externalPlayerTransform == playerTransform)
            _externalPlayerTransform = null;
    }

    public Map Map
    {
        get
        {
            if (_cachedMap == null)
            {
                if (RuntimeItemsGroup.TryGetValue(GROUP_MAP_CORE, out var list) && list.Count > 0)
                {
                    _cachedMap = (Map)list[0];
                }
            }
            return _cachedMap;
        }
    }

    #endregion

    #region Unity Lifecycle

    protected override void Awake()
    {
        base.Awake();
        // 不在 Awake 中自动加载场景物品，避免破坏游戏生命周期。手动或在合适的时机调用 LoadAllRuntimeItems()
    }

    [Button("加载所有Runtime物品")]
    public void LoadAllRuntimeItems()
    {
        // 第一步：获取场景中所有的 Item（包括非激活状态）
        Item[] allItems = FindObjectsOfType<Item>(includeInactive: true);

        foreach (Item item in allItems)
        {
            if (item == null)
            {
                continue;
            }

            var pooled = item.GetComponent<PooledItemMarker>();
            if (pooled != null && pooled.InPool)
            {
                continue;
            }

            RegisterRuntimeItem(item, item.name);
        }
    }

    public void Start()
    {
        // Debug.Log("物品加载完毕");
        GameManager.Instance.BackToHelloScene_Event_Start += CleanupNullItems;
    }

    private void OnDestroy()
    {
        CompletePerceptionBatch(false);
        DisposePerceptionJobData();

        if (GameManager.Instance != null)
        {
            GameManager.Instance.BackToHelloScene_Event_Start -= CleanupNullItems;
        }
    }

    private void OnDisable()
    {
        CompletePerceptionBatch(false);
    }

    private void Update()
    {
        CompletePerceptionBatch();

        if (_runtimeItems.Count == 0)
        {
            return;
        }

        FlushDirtyItemSchedules();

        _updateSnapshot.Clear();
        _updateSnapshot.AddRange(_everyFrameItems);

        for (int i = 0; i < _updateSnapshot.Count; i++)
        {
            Item item = _updateSnapshot[i];
            if (item != null && item.isActiveAndEnabled)
                RefreshItemSpatialIndex(item);
        }

        float deltaTime = Time.deltaTime;
        for (int i = 0; i < _updateSnapshot.Count; i++)
        {
            var item = _updateSnapshot[i];
            if (item == null || !item.isActiveAndEnabled)
            {
                continue;
            }

            item.Tick(deltaTime);
        }

        ProcessTickTier(_fastTickBuckets, ref _fastTickTimer, ref _fastTickCursor, FastTickSlice, deltaTime);
        ProcessTickTier(_normalTickBuckets, ref _normalTickTimer, ref _normalTickCursor, NormalTickSlice, deltaTime);
        ProcessTickTier(_slowTickBuckets, ref _slowTickTimer, ref _slowTickCursor, SlowTickSlice, deltaTime);
    }

    private void LateUpdate()
    {
        SchedulePerceptionBatch();
    }

    #endregion


    #region Instantiate

    // 核心实例化方法：统一所有重载走这里
    public Item InstantiateItem(ItemData itemData, Vector3 position = default, Quaternion rotation = default, Vector3 scale = default, GameObject parent = null)
    {
        if (itemData == null)
        {
            throw new System.ArgumentNullException(nameof(itemData));
        }

        if (rotation == default) rotation = Quaternion.identity;
        if (scale == default || scale == Vector3.zero) scale = Vector3.one;

        GameObject itemObj = AcquireItemObject(itemData.IDName);
        Item item = itemObj.GetComponent<Item>();
        if (item == null)
        {
            Destroy(itemObj);
            throw new System.InvalidOperationException($"Prefab 缺少 Item 组件: {itemData.IDName}");
        }

        item.itemData = itemData;
        item.PrepareForPoolReuse();
        itemObj.name = itemData.IDName;
        itemObj.transform.position = position;
        itemObj.transform.rotation = rotation;
        itemObj.transform.localScale = scale;
        itemObj.SetActive(true);

        RegisterRuntimeItem(item, itemData.IDName);
        AttachToParentOrChunk(item, itemObj, position, parent);
        RuntimeItemInstantiated?.Invoke(item);

        return item;
    }

    public void DespawnItem(Item item, bool saveData = true, bool detachFromChunk = true)
    {
        if (item == null)
        {
            throw new System.ArgumentNullException(nameof(item));
        }

        if (item.DestructionHandled)
            return;

        RuntimeItemDespawning?.Invoke(item);

        if (detachFromChunk)
            item.GetComponentInParent<Chunk>()?.RemoveItem(item);

        UnregisterRuntimeItem(item);

        item.PrepareForDespawn(saveData);
        if (saveData && TryReturnItemToPool(item))
            return;

        Destroy(item.gameObject);
    }

    public void DestroyItem(Item item)
    {
        DespawnItem(item);
    }

    // 通过名称实例化：只保留一个（用可选参数覆盖绝大多数用法）
    public Item InstantiateItem(string itemName, Vector3 position = default, Quaternion rotation = default, Vector3 scale = default, GameObject parent = null)
    {
        var prefab = GameRes.Instance.GetPrefab(itemName);
        if (prefab == null)
        {
            throw new System.InvalidOperationException($"找不到物品Prefab: {itemName}");
        }

        var templateItem = prefab.GetComponent<Item>();
        if (templateItem == null || templateItem.itemData == null)
        {
            throw new System.InvalidOperationException($"Prefab 缺少 Item 或 itemData: {itemName}");
        }

        ItemData templateData = templateItem.itemData.DeepClone();
        return InstantiateItem(templateData, position, rotation, scale, parent);
    }

    /// <summary>
    /// 噪声地图使用的确定性实例化入口。相同世界种子与格子会在所有联机端得到相同 Guid。
    /// </summary>
    public Item InstantiateItemDeterministic(
        string itemName,
        int deterministicGuid,
        Vector3 position = default,
        Quaternion rotation = default,
        Vector3 scale = default,
        GameObject parent = null)
    {
        var prefab = GameRes.Instance.GetPrefab(itemName);
        if (prefab == null)
            throw new InvalidOperationException($"找不到物品Prefab: {itemName}");

        var templateItem = prefab.GetComponent<Item>();
        if (templateItem == null || templateItem.itemData == null)
            throw new InvalidOperationException($"Prefab 缺少 Item 或 itemData: {itemName}");

        ItemData templateData = templateItem.itemData.DeepClone();
        templateData.Guid = deterministicGuid == 0 ? 1 : deterministicGuid;
        return InstantiateItem(templateData, position, rotation, scale, parent);
    }

    /// <summary>
    /// AI-Context: 仅供权威网络快照补建世界 Item；调用方必须先校验 ID、GUID 与位置。
    /// </summary>
    public Item InstantiateNetworkItem(
        string itemName,
        int authoritativeGuid,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale)
    {
        if (authoritativeGuid == 0)
            throw new ArgumentOutOfRangeException(nameof(authoritativeGuid), "网络 Item GUID 不能为 0");

        GameObject prefab = GameRes.Instance.GetPrefab(itemName);
        if (prefab == null)
            throw new InvalidOperationException($"找不到物品Prefab: {itemName}");

        Item templateItem = prefab.GetComponent<Item>();
        if (templateItem == null || templateItem.itemData == null)
            throw new InvalidOperationException($"Prefab 缺少 Item 或 itemData: {itemName}");

        ItemData data = templateItem.itemData.DeepClone();
        data.Guid = authoritativeGuid;
        return InstantiateItem(data, position, rotation, scale);
    }

    // 通过ItemData的transform信息实例化（保留此重载：项目内多处在用）
    public Item InstantiateItem(ItemData itemData, GameObject parent)
        => InstantiateItem(itemData, itemData.transform.position, itemData.transform.rotation, itemData.transform.scale, parent);

    // 生成GUID的辅助方法
    public int GenerateGuid() => System.Guid.NewGuid().GetHashCode();

    private void RegisterRuntimeItem(Item item, string context)
    {
        if (item == null)
        {
            Debug.LogError($"RegisterRuntimeItem: item为空, context={context}");
            return;
        }

        if (item.itemData == null)
        {
            Debug.LogError($"物品缺少itemData: {item.name}, context={context}", item);
            return;
        }

        if (WorldRunTimeItems.ContainsKey(item.itemData.Guid))
        {
            item.itemData.Guid = GenerateGuid();
        }

        WorldRunTimeItems[item.itemData.Guid] = item;
        AddToGroup(item);
        if (!_runtimeItems.Contains(item))
        {
            _runtimeItems.Add(item);
        }

        RefreshItemSpatialIndex(item);
        RefreshPerceptionColliderCache(item);
        RegisterItemSchedule(item);

        if (item is Map mapItem)
        {
            _cachedMap = mapItem;
        }
    }

    public void InjectRuntimeItem(Item item, string context = null)
    {
        if (string.IsNullOrEmpty(context))
        {
            context = item?.itemData != null ? item.itemData.IDName : item?.name;
        }
        RegisterRuntimeItem(item, context);
    }

    /// <summary>
    /// 将网络远程手持物从本地权威 Item 循环中移除，保留 GameObject 仅作视觉展示。
    /// </summary>
    public void MarkAsRemoteVisualOnly(Item item)
    {
        RuntimeItemDespawning?.Invoke(item);
        UnregisterRuntimeItem(item);
    }

    private void UnregisterRuntimeItem(Item item)
    {
        if (item == null || item.itemData == null) return;

        WorldRunTimeItems.Remove(item.itemData.Guid);

        string key = item.itemData.IDName;
        if (RuntimeItemsGroup.TryGetValue(key, out var list))
        {
            list.Remove(item);
            if (list.Count == 0)
            {
                RuntimeItemsGroup.Remove(key);
            }
        }

        if (item is Map)
        {
            _cachedMap = null;
        }

        _runtimeItems.Remove(item);
        RemoveItemFromSpatialIndex(item);
        _perceptionColliderCache.Remove(item);
        RemoveItemSchedule(item);
    }

    private void AttachToParentOrChunk(Item item, GameObject itemObj, Vector3 position, GameObject parent)
    {
        if (parent != null)
        {
            itemObj.transform.SetParent(parent.transform, true);
            return;
        }

        Vector2Int chunkPos = Chunk.GetChunkPosition(position);

        if (ChunkMgr.Instance.TryGetActiveChunkByPos(chunkPos, out var chunk))
        {
            if (chunk == null)
            {
                ChunkMgr.Instance.GetClosestChunk(itemObj.transform.position, out chunk);
            }

            if (chunk != null)
            {
                itemObj.transform.SetParent(chunk.transform, true);
                chunk.AddItem(item);
                return;
            }
        }

        if (ChunkMgr.Instance.TryGetUnActiveChunkByPos(chunkPos, out var unActiveChunk) && unActiveChunk != null)
        {
            itemObj.transform.SetParent(unActiveChunk.transform, true);
        }
    }

    private GameObject SpawnItemObject(string itemId)
    {
        GameObject obj = GameRes.Instance.InstantiatePrefab(itemId);
        if (obj == null) throw new System.InvalidOperationException($"InstantiatePrefab 失败: {itemId}");
        return obj;
    }

    private GameObject AcquireItemObject(string itemId)
    {
        if (_itemPools.TryGetValue(itemId, out Queue<Item> pool))
        {
            while (pool.Count > 0)
            {
                Item pooledItem = pool.Dequeue();
                _totalPooledItems = Mathf.Max(0, _totalPooledItems - 1);
                if (pooledItem == null)
                    continue;

                PooledItemMarker marker = pooledItem.GetComponent<PooledItemMarker>();
                if (marker != null)
                {
                    marker.InPool = false;
                    marker.RestoreBaseline();
                }

                pooledItem.transform.SetParent(null, false);
                return pooledItem.gameObject;
            }
        }

        GameObject itemObject = SpawnItemObject(itemId);
        PooledItemMarker newMarker = itemObject.GetComponent<PooledItemMarker>();
        if (newMarker == null)
            newMarker = itemObject.AddComponent<PooledItemMarker>();

        newMarker.PoolKey = itemId;
        newMarker.InPool = false;
        newMarker.PoolingDisabled = !CanPoolItem(itemObject.GetComponent<Item>());
        newMarker.CaptureBaseline();
        return itemObject;
    }

    private bool TryReturnItemToPool(Item item)
    {
        PooledItemMarker marker = item.GetComponent<PooledItemMarker>();
        if (marker == null || marker.InPool || marker.PoolingDisabled ||
            !marker.HasOriginalHierarchy() || !CanPoolItem(item))
            return false;

        string poolKey = string.IsNullOrEmpty(marker.PoolKey) ? item.itemData?.IDName : marker.PoolKey;
        if (string.IsNullOrEmpty(poolKey) || _totalPooledItems >= Mathf.Max(1, maxTotalPooledItems))
            return false;

        if (!_itemPools.TryGetValue(poolKey, out Queue<Item> pool))
        {
            pool = new Queue<Item>();
            _itemPools[poolKey] = pool;
        }

        if (pool.Count >= Mathf.Max(1, maxPoolSizePerItem))
            return false;

        item.NotifyReturnedToPool();
        marker.InPool = true;
        item.gameObject.SetActive(false);
        item.transform.SetParent(GetItemPoolRoot(), false);
        pool.Enqueue(item);
        _totalPooledItems++;
        return true;
    }

    private bool CanPoolItem(Item item)
    {
        if (item == null || item is Player || item is Map)
            return false;

        MonoBehaviour[] behaviours = item.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || behaviour == item || behaviour is PooledItemMarker || behaviour is IItemPoolLifecycle)
                continue;

            System.Type type = behaviour.GetType();
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly;

            if (type.GetMethod("OnDestroy", flags) != null && type.GetMethod("OnDisable", flags) == null)
                return false;
        }

        return true;
    }

    private Transform GetItemPoolRoot()
    {
        if (_itemPoolRoot != null)
            return _itemPoolRoot;

        GameObject poolRoot = new GameObject("ItemPool");
        poolRoot.transform.SetParent(transform, false);
        _itemPoolRoot = poolRoot.transform;
        return _itemPoolRoot;
    }

    #endregion

    // ✅ 添加到分组
    public void AddToGroup(Item item)
    {
        if (item == null)
        {
            Debug.LogError("AddToGroup: item为空");
            return;
        }

        if (item.itemData == null)
        {
            Debug.LogError($"AddToGroup: itemData为空, item={item.name}", item);
            return;
        }

        string key = item.itemData.IDName;
        if (!RuntimeItemsGroup.TryGetValue(key, out var list))
        {
            list = new List<Item>();
            RuntimeItemsGroup[key] = list;
        }

        if (!list.Contains(item))
        {
            list.Add(item);
        }
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
        if (WorldRunTimeItems.TryGetValue(guid, out var item))
            return item;
        return null;
    }


    //TODO 添加一个清理两个字典的中Item空引用的方法
    [Button("清理空引用")]
    public void CleanupNullItems()
    {
        // 清理 RunTimeItems 中为 null 的条目
        var keysToRemove = new List<int>();
        foreach (var pair in WorldRunTimeItems)
        {
            if (pair.Value == null)
            {
                keysToRemove.Add(pair.Key);
            }
        }
        foreach (int key in keysToRemove)
        {
            WorldRunTimeItems.Remove(key);
        }

        _runtimeItems.RemoveAll(item => item == null);
        RebuildSpatialIndex();
        RebuildAllItemSchedules();

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

        // 分组变化后，缓存需要重新计算
        _cachedMap = null;

      //  Debug.Log("已清理无效的 Item 引用。");
    }

    #region 分级更新调度

    public void NotifyItemScheduleChanged(Item item)
    {
        if (item != null)
            _dirtyScheduleItems.Add(item);
    }

    private static List<Item>[] CreateTickBuckets()
    {
        var buckets = new List<Item>[ScheduleBucketCount];
        for (int i = 0; i < buckets.Length; i++)
            buckets[i] = new List<Item>(16);
        return buckets;
    }

    private void FlushDirtyItemSchedules()
    {
        if (_dirtyScheduleItems.Count == 0)
            return;

        _updateSnapshot.Clear();
        foreach (Item item in _dirtyScheduleItems)
            _updateSnapshot.Add(item);
        _dirtyScheduleItems.Clear();

        for (int i = 0; i < _updateSnapshot.Count; i++)
        {
            Item item = _updateSnapshot[i];
            if (item != null && _runtimeItems.Contains(item))
                RegisterItemSchedule(item);
        }
    }

    private void RegisterItemSchedule(Item item)
    {
        RemoveItemSchedule(item);
        if (item == null)
            return;

        ItemTickTier tier = item.GetTickTier();
        switch (tier)
        {
            case ItemTickTier.EveryFrame:
                _everyFrameItems.Add(item);
                break;
            case ItemTickTier.Fast:
                AddToTickBucket(_fastTickBuckets, item);
                item.ResetScheduledTickClock(Time.time);
                break;
            case ItemTickTier.Normal:
                AddToTickBucket(_normalTickBuckets, item);
                item.ResetScheduledTickClock(Time.time);
                break;
            case ItemTickTier.Slow:
                AddToTickBucket(_slowTickBuckets, item);
                item.ResetScheduledTickClock(Time.time);
                break;
        }
    }

    private void RemoveItemSchedule(Item item)
    {
        if (ReferenceEquals(item, null))
            return;

        _everyFrameItems.Remove(item);
        RemoveFromTickBuckets(_fastTickBuckets, item);
        RemoveFromTickBuckets(_normalTickBuckets, item);
        RemoveFromTickBuckets(_slowTickBuckets, item);
        _dirtyScheduleItems.Remove(item);
    }

    private static void AddToTickBucket(List<Item>[] buckets, Item item)
    {
        int bucketIndex = (item.GetInstanceID() & int.MaxValue) % buckets.Length;
        buckets[bucketIndex].Add(item);
    }

    private static void RemoveFromTickBuckets(List<Item>[] buckets, Item item)
    {
        for (int i = 0; i < buckets.Length; i++)
            buckets[i].Remove(item);
    }

    private static int CountBucketItems(List<Item>[] buckets)
    {
        int count = 0;
        for (int i = 0; i < buckets.Length; i++)
            count += buckets[i].Count;
        return count;
    }

    private static void ProcessTickTier(
        List<Item>[] buckets,
        ref float timer,
        ref int cursor,
        float slice,
        float deltaTime)
    {
        timer += deltaTime;
        int elapsedSlices = Mathf.FloorToInt(timer / slice);
        if (elapsedSlices <= 0)
            return;

        int slicesToProcess = Mathf.Min(elapsedSlices, buckets.Length);
        float currentTime = Time.time;

        for (int sliceIndex = 0; sliceIndex < slicesToProcess; sliceIndex++)
        {
            cursor = (cursor + 1) % buckets.Length;
            List<Item> bucket = buckets[cursor];

            for (int i = 0; i < bucket.Count; i++)
            {
                Item item = bucket[i];
                if (item == null)
                    continue;

                if (!item.isActiveAndEnabled)
                {
                    item.ResetScheduledTickClock(currentTime);
                    continue;
                }

                item.TickScheduled(currentTime);
            }
        }

        timer = elapsedSlices >= buckets.Length
            ? 0f
            : timer - slicesToProcess * slice;
    }

    private void RebuildAllItemSchedules()
    {
        _everyFrameItems.Clear();
        ClearTickBuckets(_fastTickBuckets);
        ClearTickBuckets(_normalTickBuckets);
        ClearTickBuckets(_slowTickBuckets);
        _dirtyScheduleItems.Clear();

        for (int i = 0; i < _runtimeItems.Count; i++)
        {
            Item item = _runtimeItems[i];
            if (item != null)
                RegisterItemSchedule(item);
        }
    }

    private static void ClearTickBuckets(List<Item>[] buckets)
    {
        for (int i = 0; i < buckets.Length; i++)
            buckets[i].Clear();
    }

    #endregion

    #region 空间查询

    public void NotifyItemSpatialIndexChanged(Item item)
    {
        if (item?.itemData == null)
            return;

        if (WorldRunTimeItems.TryGetValue(item.itemData.Guid, out Item registeredItem) && registeredItem == item)
            RefreshItemSpatialIndex(item);
    }

    public void QueueDetectorQuery(Mod_ItemDetector detector, long requestVersion)
    {
        if (detector == null || requestVersion <= 0)
            return;

        if (!_pendingDetectorSet.Add(detector))
        {
            for (int i = 0; i < _pendingDetectorRequests.Count; i++)
            {
                if (_pendingDetectorRequests[i].Detector != detector)
                    continue;

                _pendingDetectorRequests[i] = new PendingDetectorRequest(detector, requestVersion);
                return;
            }
        }

        _pendingDetectorRequests.Add(new PendingDetectorRequest(detector, requestVersion));
    }

    private void SchedulePerceptionBatch()
    {
        if (_perceptionJobScheduled || _pendingDetectorRequests.Count == 0)
            return;

        _inFlightDetectors.Clear();
        _inFlightDetectorVersions.Clear();
        for (int i = 0; i < _pendingDetectorRequests.Count; i++)
        {
            PendingDetectorRequest request = _pendingDetectorRequests[i];
            if (request.Detector == null)
                continue;

            _inFlightDetectors.Add(request.Detector);
            _inFlightDetectorVersions.Add(request.Version);
        }

        _pendingDetectorRequests.Clear();
        _pendingDetectorSet.Clear();
        if (_inFlightDetectors.Count == 0)
            return;

        BuildDetectorQuerySnapshot();
        BuildPerceptionItemSnapshot();

        int itemCount = _perceptionSnapshotData.Count;
        int queryCount = _perceptionQueryData.Count;
        PreparePerceptionJobContainers(itemCount, queryCount);

        for (int i = 0; i < itemCount; i++)
        {
            PerceptionItemSnapshot snapshot = _perceptionSnapshotData[i];
            _inFlightItemSnapshots.Add(snapshot);
            _inFlightSpatialMap.Add(snapshot.CellKey, i);
        }

        for (int i = 0; i < queryCount; i++)
            _inFlightQueries.Add(_perceptionQueryData[i]);

        var queryJob = new BatchedPerceptionQueryJob
        {
            Items = _inFlightItemSnapshots.AsArray(),
            Queries = _inFlightQueries.AsArray(),
            SpatialMap = _inFlightSpatialMap,
            Results = _inFlightResults.AsWriter()
        };

        _perceptionJobHandle = queryJob.Schedule(queryCount, PerceptionJobBatchSize);
        _perceptionJobScheduled = true;
    }

    private void PreparePerceptionJobContainers(int itemCount, int queryCount)
    {
        int requiredItemCapacity = Mathf.Max(1, itemCount);
        int requiredQueryCapacity = Mathf.Max(1, queryCount);

        if (!_inFlightItemSnapshots.IsCreated)
            _inFlightItemSnapshots = new NativeList<PerceptionItemSnapshot>(requiredItemCapacity, Allocator.Persistent);
        else
        {
            _inFlightItemSnapshots.Clear();
            if (_inFlightItemSnapshots.Capacity < requiredItemCapacity)
                _inFlightItemSnapshots.Capacity = requiredItemCapacity;
        }

        if (!_inFlightQueries.IsCreated)
            _inFlightQueries = new NativeList<DetectorQuerySnapshot>(requiredQueryCapacity, Allocator.Persistent);
        else
        {
            _inFlightQueries.Clear();
            if (_inFlightQueries.Capacity < requiredQueryCapacity)
                _inFlightQueries.Capacity = requiredQueryCapacity;
        }

        if (!_inFlightSpatialMap.IsCreated)
            _inFlightSpatialMap = new NativeParallelMultiHashMap<long, int>(requiredItemCapacity, Allocator.Persistent);
        else
        {
            _inFlightSpatialMap.Clear();
            if (_inFlightSpatialMap.Capacity < requiredItemCapacity)
                _inFlightSpatialMap.Capacity = requiredItemCapacity;
        }

        DisposePerceptionResultStream();
        _inFlightResults = new NativeStream(queryCount, Allocator.TempJob);
    }

    private void BuildDetectorQuerySnapshot()
    {
        _perceptionQueryData.Clear();
        for (int i = 0; i < _inFlightDetectors.Count; i++)
        {
            Mod_ItemDetector detector = _inFlightDetectors[i];
            Item excludedItem = detector.item;
            Vector3 detectorPosition = detector.transform.position;
            _perceptionQueryData.Add(new DetectorQuerySnapshot
            {
                Center = new float2(detectorPosition.x, detectorPosition.y),
                Radius = Mathf.Max(0f, detector.DetectionRadius),
                LayerMask = detector.itemLayer.value,
                ExcludedInstanceId = excludedItem != null ? excludedItem.GetInstanceID() : 0
            });
        }
    }

    private void BuildPerceptionItemSnapshot()
    {
        _perceptionSnapshotItems.Clear();
        _perceptionSnapshotData.Clear();
        _perceptionSnapshotCells.Clear();
        _perceptionSnapshotItemSet.Clear();

        for (int queryIndex = 0; queryIndex < _perceptionQueryData.Count; queryIndex++)
        {
            DetectorQuerySnapshot query = _perceptionQueryData[queryIndex];
            int minCellX = WorldToPerceptionCell(query.Center.x - query.Radius) - 1;
            int maxCellX = WorldToPerceptionCell(query.Center.x + query.Radius) + 1;
            int minCellY = WorldToPerceptionCell(query.Center.y - query.Radius) - 1;
            int maxCellY = WorldToPerceptionCell(query.Center.y + query.Radius) + 1;

            for (int cellX = minCellX; cellX <= maxCellX; cellX++)
            {
                for (int cellY = minCellY; cellY <= maxCellY; cellY++)
                {
                    long cellKey = PackPerceptionCell(cellX, cellY);
                    if (!_perceptionSnapshotCells.Add(cellKey) ||
                        !_perceptionCells.TryGetValue(cellKey, out HashSet<Item> cellItems))
                    {
                        continue;
                    }

                    foreach (Item candidate in cellItems)
                        TryAddPerceptionSnapshot(candidate);
                }
            }
        }
    }

    private void TryAddPerceptionSnapshot(Item candidate)
    {
        if (candidate == null || candidate.itemData == null ||
            !candidate.gameObject.activeInHierarchy || candidate.DestructionHandled ||
            !_perceptionSnapshotItemSet.Add(candidate) ||
            !TryGetPerceptionBounds(candidate, out Bounds perceptionBounds))
        {
            return;
        }

        Vector3 position = candidate.transform.position;
        Vector3 boundsCenter = perceptionBounds.center;
        Vector3 boundsExtents = perceptionBounds.extents;
        _perceptionSnapshotItems.Add(candidate);
        _perceptionSnapshotData.Add(new PerceptionItemSnapshot
        {
            CellKey = GetPerceptionCellKey(position),
            Guid = candidate.itemData.Guid,
            InstanceId = candidate.GetInstanceID(),
            LayerBit = 1 << candidate.gameObject.layer,
            BoundsCenter = new float2(boundsCenter.x, boundsCenter.y),
            BoundsExtents = new float2(boundsExtents.x, boundsExtents.y)
        });
    }

    private void RefreshPerceptionColliderCache(Item item)
    {
        if (item == null)
            return;

        _perceptionColliderCache[item] = item.GetComponents<Collider2D>();
    }

    private bool TryGetPerceptionBounds(Item item, out Bounds combinedBounds)
    {
        combinedBounds = default;
        if (!_perceptionColliderCache.TryGetValue(item, out Collider2D[] colliders))
        {
            RefreshPerceptionColliderCache(item);
            _perceptionColliderCache.TryGetValue(item, out colliders);
        }

        bool hasEnabledCollider = false;
        if (colliders == null)
            return false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;

            if (!hasEnabledCollider)
            {
                combinedBounds = collider.bounds;
                hasEnabledCollider = true;
            }
            else
            {
                combinedBounds.Encapsulate(collider.bounds);
            }
        }

        return hasEnabledCollider;
    }

    private void CompletePerceptionBatch(bool applyResults = true)
    {
        if (!_perceptionJobScheduled)
            return;

        _perceptionJobHandle.Complete();
        if (applyResults)
            ApplyPerceptionBatchResults();

        _perceptionJobScheduled = false;
        DisposePerceptionResultStream();
        _inFlightDetectors.Clear();
        _inFlightDetectorVersions.Clear();
        _perceptionQueryData.Clear();
        _perceptionSnapshotItems.Clear();
        _perceptionSnapshotData.Clear();
        _perceptionSnapshotCells.Clear();
        _perceptionSnapshotItemSet.Clear();
    }

    private void ApplyPerceptionBatchResults()
    {
        NativeStream.Reader reader = _inFlightResults.AsReader();
        for (int queryIndex = 0; queryIndex < _inFlightDetectors.Count; queryIndex++)
        {
            Mod_ItemDetector detector = _inFlightDetectors[queryIndex];
            DetectorQuerySnapshot query = _inFlightQueries[queryIndex];
            int candidateCount = reader.BeginForEachIndex(queryIndex);
            _detectorApplyBuffer.Clear();

            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                int snapshotIndex = reader.Read<int>();
                if ((uint)snapshotIndex >= (uint)_perceptionSnapshotItems.Count)
                    continue;

                Item candidate = _perceptionSnapshotItems[snapshotIndex];
                PerceptionItemSnapshot snapshot = _inFlightItemSnapshots[snapshotIndex];
                if (!IsSnapshotItemStillValid(candidate, snapshot))
                    continue;

                if (!PassesColliderPerceptionFilter(candidate, query))
                    continue;

                _detectorApplyBuffer.Add(candidate);
            }

            reader.EndForEachIndex();
            if (detector != null)
                detector.ApplyDetectorResults(_inFlightDetectorVersions[queryIndex], _detectorApplyBuffer);
        }
    }

    private bool IsSnapshotItemStillValid(Item candidate, PerceptionItemSnapshot snapshot)
    {
        if (candidate == null || candidate.itemData == null || candidate.DestructionHandled ||
            !candidate.gameObject.activeInHierarchy || candidate.itemData.Guid != snapshot.Guid ||
            candidate.GetInstanceID() != snapshot.InstanceId)
        {
            return false;
        }

        return WorldRunTimeItems.TryGetValue(snapshot.Guid, out Item registeredItem) && registeredItem == candidate;
    }

    private bool PassesColliderPerceptionFilter(Item candidate, DetectorQuerySnapshot query)
    {
        int layerBit = 1 << candidate.gameObject.layer;
        if ((query.LayerMask & layerBit) == 0 || candidate.GetInstanceID() == query.ExcludedInstanceId)
            return false;

        Vector2 center = new Vector2(query.Center.x, query.Center.y);
        float radiusSqr = query.Radius * query.Radius;
        if (!_perceptionColliderCache.TryGetValue(candidate, out Collider2D[] colliders))
            return false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;

            Vector2 closestPoint = collider.ClosestPoint(center);
            if ((closestPoint - center).sqrMagnitude <= radiusSqr)
                return true;
        }

        return false;
    }

    private void DisposePerceptionJobData()
    {
        DisposePerceptionResultStream();
        if (_inFlightItemSnapshots.IsCreated)
            _inFlightItemSnapshots.Dispose();
        if (_inFlightQueries.IsCreated)
            _inFlightQueries.Dispose();
        if (_inFlightSpatialMap.IsCreated)
            _inFlightSpatialMap.Dispose();
    }

    private void DisposePerceptionResultStream()
    {
        if (_inFlightResults.IsCreated)
            _inFlightResults.Dispose();
    }

    public void QueryItemsInCircleNonAlloc(
        Vector2 center,
        float radius,
        LayerMask layerMask,
        Item excludedItem,
        List<Item> results,
        HashSet<Item> dedupe)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));
        if (dedupe == null)
            throw new ArgumentNullException(nameof(dedupe));

        results.Clear();
        dedupe.Clear();
        if (radius < 0f)
            return;

        int minCellX = WorldToPerceptionCell(center.x - radius) - 1;
        int maxCellX = WorldToPerceptionCell(center.x + radius) + 1;
        int minCellY = WorldToPerceptionCell(center.y - radius) - 1;
        int maxCellY = WorldToPerceptionCell(center.y + radius) + 1;

        for (int cellX = minCellX; cellX <= maxCellX; cellX++)
        {
            for (int cellY = minCellY; cellY <= maxCellY; cellY++)
            {
                long cellKey = PackPerceptionCell(cellX, cellY);
                if (!_perceptionCells.TryGetValue(cellKey, out HashSet<Item> cellItems))
                    continue;

                foreach (Item candidate in cellItems)
                    TryAddSpatialCandidate(candidate, center, radius, layerMask, excludedItem, results, dedupe);
            }
        }
    }

    private void RefreshItemSpatialIndex(Item item)
    {
        if (item == null || item.itemData == null)
            return;

        long newCellKey = GetPerceptionCellKey(item.transform.position);
        if (_itemPerceptionCells.TryGetValue(item, out long currentCellKey))
        {
            if (currentCellKey == newCellKey)
                return;

            RemoveItemFromPerceptionCell(item, currentCellKey);
        }

        if (!_perceptionCells.TryGetValue(newCellKey, out HashSet<Item> targetCell))
        {
            targetCell = _perceptionCellPool.Count > 0
                ? _perceptionCellPool.Pop()
                : new HashSet<Item>();
            _perceptionCells[newCellKey] = targetCell;
        }

        targetCell.Add(item);
        _itemPerceptionCells[item] = newCellKey;
    }

    private void RemoveItemFromSpatialIndex(Item item)
    {
        if (ReferenceEquals(item, null) || !_itemPerceptionCells.TryGetValue(item, out long cellKey))
            return;

        RemoveItemFromPerceptionCell(item, cellKey);
    }

    private void RemoveItemFromPerceptionCell(Item item, long cellKey)
    {
        _itemPerceptionCells.Remove(item);
        if (!_perceptionCells.TryGetValue(cellKey, out HashSet<Item> cellItems))
            return;

        cellItems.Remove(item);
        if (cellItems.Count > 0)
            return;

        _perceptionCells.Remove(cellKey);
        cellItems.Clear();
        _perceptionCellPool.Push(cellItems);
    }

    private void RebuildSpatialIndex()
    {
        foreach (HashSet<Item> cellItems in _perceptionCells.Values)
        {
            cellItems.Clear();
            _perceptionCellPool.Push(cellItems);
        }

        _perceptionCells.Clear();
        _itemPerceptionCells.Clear();

        for (int i = 0; i < _runtimeItems.Count; i++)
        {
            Item item = _runtimeItems[i];
            if (item != null)
                RefreshItemSpatialIndex(item);
        }
    }

    private static int WorldToPerceptionCell(float coordinate)
    {
        return Mathf.FloorToInt(coordinate / PerceptionCellSize);
    }

    private static long GetPerceptionCellKey(Vector2 position)
    {
        return PackPerceptionCell(
            WorldToPerceptionCell(position.x),
            WorldToPerceptionCell(position.y));
    }

    private static long PackPerceptionCell(int x, int y)
    {
        return ((long)x << 32) ^ (uint)y;
    }

    private void TryAddSpatialCandidate(
        Item candidate,
        Vector2 center,
        float radius,
        LayerMask layerMask,
        Item excludedItem,
        List<Item> results,
        HashSet<Item> dedupe)
    {
        if (candidate == null || candidate == excludedItem || candidate.itemData == null ||
            !candidate.gameObject.activeInHierarchy || candidate.DestructionHandled || dedupe.Contains(candidate))
        {
            return;
        }

        int layerBit = 1 << candidate.gameObject.layer;
        if ((layerMask.value & layerBit) == 0)
            return;

        float radiusSqr = radius * radius;
        _spatialColliderBuffer.Clear();
        candidate.GetComponents(_spatialColliderBuffer);
        for (int i = 0; i < _spatialColliderBuffer.Count; i++)
        {
            Collider2D collider = _spatialColliderBuffer[i];
            if (collider == null || !collider.enabled)
                continue;

            Vector2 closestPoint = collider.ClosestPoint(center);
            if ((closestPoint - center).sqrMagnitude > radiusSqr)
                continue;

            if (dedupe.Add(candidate))
                results.Add(candidate);
            return;
        }
    }

    #endregion

    #region 加载玩家
    /// <summary>
    /// 保存场景中的所有玩家
    /// </summary>
    /// <returns>保存的玩家数量</returns>
    [Button("保存玩家")]
    public int SavePlayer()
    {
        int playerCount = 0;
        Player[] players = ItemMgr.Instance.Player_DIC.Values.ToArray();

        foreach (Player player in players)
        {
            if (player == null) continue;
            if (_networkRemoteReplicas.Contains(player)) continue;
            player.Save();

            SaveDataMgr.Instance.SaveData.PlayerData_Dict[player.Data.Name_User] = player.Data;

            playerCount++;
        }

        return playerCount;
    }
    [Button("加载玩家")]
    [Tooltip("根据传入的玩家名称,加载玩家数据\n" +
        "优先加载当前存档中的同名玩家数据\n" +
        "如果加载不到就自动创建新的玩家数据")]
    public Player LoadPlayer(string playerName)
    {
        // 加载或者创建玩家数据
        Data_Player playerData = LoadOrCreatePlayerData(playerName, out bool wasCreated);
        //传入数据创建玩家
        Player player = CreatePlayer(playerData);
        player.SetProfileContext(localProfile: true, profileDataWasCreated: wasCreated);
        //设置玩家数据到玩家引用字典
        ItemMgr.Instance.Player_DIC[player.Data.Name_User] = player;

        player.Load();

        return player;
    }

    [Tooltip("实例化玩家 但是不初始化")]
    public Player CreatePlayer(string playerName)
    {
        // 加载或者创建玩家数据
        Data_Player playerData = LoadOrCreatePlayerData(playerName, out bool wasCreated);
        //传入数据创建玩家
        Player player = CreatePlayer(playerData);
        player.SetProfileContext(localProfile: true, profileDataWasCreated: wasCreated);
        //设置玩家数据到玩家引用字典
        ItemMgr.Instance.Player_DIC[player.Data.Name_User] = player;

        return player;
    }

    /// <summary>
    /// 为 Mirror 网络身份创建对应的核心 Player Item。
    /// 本地玩家完整加载模块；远端玩家只创建数据与外观副本，避免重复启用输入、UI 和相机。
    /// </summary>
    public Player LoadNetworkPlayer(string playerName, int networkGuid, Vector3 spawnPosition, bool initializeLocalModules)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            throw new ArgumentException("联机玩家名称不能为空", nameof(playerName));

        if (Player_DIC.TryGetValue(playerName, out Player existingPlayer) && existingPlayer != null)
        {
            if (_networkPlayers.Contains(existingPlayer) && initializeLocalModules)
                PromoteNetworkPlayerToLocal(existingPlayer, spawnPosition);

            return existingPlayer;
        }

        bool hasSavedData = SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(playerName, out Data_Player playerData);
        if (!hasSavedData || playerData == null)
        {
            playerData = CreateDefaultPlayerData(playerName);
            if (networkGuid != 0)
                playerData.Guid = networkGuid;
        }

        playerData.Name_User = playerName;
        playerData.CurrentSceneName = SceneManager.GetActiveScene().name;
        playerData.transform.position = spawnPosition;
        playerData.transform.rotation = Quaternion.identity;
        if (playerData.transform.scale == Vector3.zero)
            playerData.transform.scale = Vector3.one;

        Player player = CreatePlayer(playerData);
        player.SetProfileContext(
            localProfile: initializeLocalModules,
            profileDataWasCreated: !hasSavedData);
        Player_DIC[playerName] = player;
        _networkPlayers.Add(player);
        SaveDataMgr.Instance.SaveData.PlayerData_Dict[playerName] = playerData;

        if (initializeLocalModules)
        {
            InitializeNetworkLocalPlayer(player, spawnPosition);
        }
        else
        {
            _networkRemoteReplicas.Add(player);
            ConfigureRemoteNetworkReplica(player, spawnPosition);
        }

        return player;
    }

    public void PromoteNetworkPlayerToLocal(Player player, Vector3 spawnPosition)
    {
        if (player == null || !_networkPlayers.Contains(player))
            return;

        _networkRemoteReplicas.Remove(player);
        player.SetProfileContext(
            localProfile: true,
            profileDataWasCreated: player.WasProfileDataCreated);
        InitializeNetworkLocalPlayer(player, spawnPosition);
    }

    public void ReleaseNetworkPlayer(Player player, bool persistData)
    {
        if (player == null || !_networkPlayers.Remove(player))
            return;

        _networkRemoteReplicas.Remove(player);
        _networkInitializedPlayers.Remove(player);

        if (player.Data != null)
        {
            if (Player_DIC.TryGetValue(player.Data.Name_User, out Player registeredPlayer) && registeredPlayer == player)
                Player_DIC.Remove(player.Data.Name_User);

            if (persistData)
            {
                player.Save();
                SaveDataMgr.Instance.SaveData.PlayerData_Dict[player.Data.Name_User] = player.Data;
            }
        }

        DespawnItem(player, saveData: false);
    }

    private void InitializeNetworkLocalPlayer(Player player, Vector3 spawnPosition)
    {
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.bodyType = RigidbodyType2D.Dynamic;
            body.velocity = Vector2.zero;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
        }

        if (_networkInitializedPlayers.Add(player))
            player.Load();

        player.transform.position = spawnPosition;
        player.Data.transform.position = spawnPosition;

        GameController controller = player.GetComponentInChildren<GameController>(true);
        controller?.SetGameplayInputLocked(false);
    }

    private static void ConfigureRemoteNetworkReplica(Player player, Vector3 spawnPosition)
    {
        player.SetProfileContext(
            localProfile: false,
            profileDataWasCreated: player.WasProfileDataCreated);
        player.transform.position = spawnPosition;
        player.Data.transform.position = spawnPosition;

        GameController controller = player.GetComponentInChildren<GameController>(true);
        controller?.SetGameplayInputLocked(true);

        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
        {
            body.velocity = Vector2.zero;
            body.bodyType = RigidbodyType2D.Kinematic;
            // 网络层已经按每帧生成平滑视觉坐标，关闭物理插值避免再次插值造成节拍抖动。
            body.interpolation = RigidbodyInterpolation2D.None;
        }
    }
    private Data_Player LoadOrCreatePlayerData(string playerName, out bool wasCreated)
    {
        Data_Player playerData;
        //检测存档中是否存在玩家数据
        if (SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(playerName, out var loadedPlayerData))
        {
            playerData = loadedPlayerData;
            wasCreated = false;
        }
        else //如果不存在，则创建默认玩家数据
        {
            playerData = CreateDefaultPlayerData(playerName);
            wasCreated = true;
        }
        return playerData;
    }

    private Data_Player CreateDefaultPlayerData(string playerName)
    {
        var prefab = GameRes.Instance.GetPrefab("Player");
        var defaultPlayer = prefab.GetComponent<Player>();
        var playerData = defaultPlayer.Data.DeepClone();
        playerData.Guid = playerName.GetHashCode();
        playerData.Name_User = playerName;
        return playerData;
    }

    private Player CreatePlayer(Data_Player data)
    {
        Player newPlayer = (Player)ItemMgr.Instance.InstantiateItem(data, Vector3.zero, Quaternion.identity, Vector3.one, new GameObject("Players"));

        // ✅ 将父对象设置为空（放到场景根节点下）
        newPlayer.transform.SetParent(null, true);

        return newPlayer;
    }
    #endregion

    [Tooltip("随机空投")]
    public void RandomDropInMap(GameObject dropObject, Chunk map = null, Vector2Int quadrant = default)
    {
        Vector2 defaultPosition;
        if (map == null)
        {
            defaultPosition = Vector2.zero;
        }
        else
        {
            defaultPosition = map.MapSave.MapPosition;
        }

        // 地图格子的实际世界尺寸（单位：世界单位，例如每格宽100高120）
        int tileSizeX = 1; // 根据你的逻辑替换
        int tileSizeY = 1;

        // 整个地图的大小
        float mapWidth = ChunkMgr.GetChunkSize().x * tileSizeX;
        float mapHeight = ChunkMgr.GetChunkSize().y * tileSizeY;

        // 随机数生成器
        System.Random rng = new System.Random();

        // 在 [0, mapWidth] 范围内取随机值
        float randX = (float)rng.NextDouble() * mapWidth;
        float randY = (float)rng.NextDouble() * mapHeight;

        // 确定象限，默认(1,1)就是第一象限
        if (quadrant == default) quadrant = new Vector2Int(1, 1);

        randX *= Mathf.Sign(quadrant.x);
        randY *= Mathf.Sign(quadrant.y);

        // 设置空投对象位置（相对 map 的位置）
        dropObject.transform.position = defaultPosition + new Vector2(randX, randY);
    }

}

