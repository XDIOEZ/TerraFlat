using Force.DeepCloner;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Pool;

public class ItemMgr : SingletonMono<ItemMgr>
{

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

    private readonly Dictionary<string, ObjectPool<GameObject>> _itemPoolById = new();
    private Transform _poolRoot;
    #endregion

    #region Pool Config

    [TitleGroup("对象池")]
    [LabelText("启用物品对象池")]
    public bool EnableItemPool = true;

    [TitleGroup("对象池")]
    [LabelText("所有物品都进池(谨慎)")]
    public bool PoolAllItems = false;

    [TitleGroup("对象池")]
    [LabelText("允许进入对象池的ID列表")]
    public List<string> PoolItemIDs = new();

    [TitleGroup("对象池")]
    [LabelText("默认池最大容量")]
    public int DefaultPoolMaxSize = 64;

    [TitleGroup("对象池")]
    [LabelText("Awake时预热数量")]
    public int DefaultPrewarmCount = 0;

    #endregion

    #region Properties

    public string PlayerInSceneName => Player_DIC[SaveDataMgr.Instance.CurrentContrrolPlayerName].Data.CurrentSceneName;
    public Player User_Player
    {
        get
        {
            if (Player_DIC.TryGetValue(SaveDataMgr.Instance.CurrentContrrolPlayerName, out var player))
            {
                return player;
            }

            Debug.LogError($"当前控制玩家未加载: {SaveDataMgr.Instance.CurrentContrrolPlayerName}");
            return null;
        }
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

    [Button("加载所有Runtime物品")]
    protected override void Awake()
    {
        base.Awake();

        EnsurePoolRoot();
        if (EnableItemPool && DefaultPrewarmCount > 0)
        {
            PrewarmAll(DefaultPrewarmCount);
        }

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
        GameManager.Instance.Event_ExitGame_Start += CleanupNullItems;
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.Event_ExitGame_Start -= CleanupNullItems;
        }
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

        GameObject itemObj = SpawnItemObject(itemData.IDName);
        itemObj.transform.position = position;
        itemObj.transform.rotation = rotation;
        itemObj.transform.localScale = scale;

        Item item = itemObj.GetComponent<Item>();
        if (item == null)
        {
            throw new System.InvalidOperationException($"Prefab 缺少 Item 组件: {itemData.IDName}");
        }

        item.itemData = itemData;

        RegisterRuntimeItem(item, itemData.IDName);
        AttachToParentOrChunk(item, itemObj, position, parent);

        return item;
    }

    public void DespawnItem(Item item)
    {
        if (item == null)
        {
            throw new System.ArgumentNullException(nameof(item));
        }

        var pooled = item.GetComponent<PooledItemMarker>();
        if (pooled == null || !EnableItemPool)
        {
            Destroy(item.gameObject);
            return;
        }

        UnregisterRuntimeItem(item);

        item.OnItemDestroy.Invoke(item);
        if (item.itemData != null)
        {
            item.Save();
        }

        pooled.InPool = true;

        if (_itemPoolById.TryGetValue(pooled.PoolKey, out var pool))
        {
            pool.Release(item.gameObject);
            return;
        }

        Debug.LogError($"[ItemPool] 找不到对象池: {pooled.PoolKey}，将直接销毁 {item.name}", item);
        Destroy(item.gameObject);
    }

    [Button("对象池/预热所有")]
    public void PrewarmAll(int countPerId)
    {
        if (countPerId <= 0) return;
        foreach (var id in GetPoolIdsSnapshot())
        {
            Prewarm(id, countPerId);
        }
    }

    [Button("对象池/预热")]
    public void Prewarm(string itemId, int count)
    {
        if (string.IsNullOrEmpty(itemId) || count <= 0) return;

        var pool = GetOrCreatePool(itemId);
        var temp = ListPool<GameObject>.Get();
        for (int i = 0; i < count; i++)
        {
            temp.Add(pool.Get());
        }
        for (int i = 0; i < temp.Count; i++)
        {
            pool.Release(temp[i]);
        }
        ListPool<GameObject>.Release(temp);
    }

    [Button("对象池/清空")]
    public void ClearPools()
    {
        foreach (var pool in _itemPoolById.Values)
        {
            pool.Clear();
        }
        _itemPoolById.Clear();
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

        if (item is Map mapItem)
        {
            _cachedMap = mapItem;
        }
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
    }

    private void AttachToParentOrChunk(Item item, GameObject itemObj, Vector3 position, GameObject parent)
    {
        if (parent != null)
        {
            itemObj.transform.SetParent(parent.transform, true);
            return;
        }

        string chunkKey = Chunk.GetChunkPosition(position).ToString();

        if (ChunkMgr.Instance.Chunk_Dic_Active.TryGetValue(chunkKey, out var chunk))
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

        if (ChunkMgr.Instance.Chunk_Dic_UnActive.TryGetValue(chunkKey, out var unActiveChunk) && unActiveChunk != null)
        {
            itemObj.transform.SetParent(unActiveChunk.transform, true);
        }
    }

    private GameObject SpawnItemObject(string itemId)
    {
        if (!EnableItemPool || !ShouldUsePool(itemId))
        {
            GameObject obj = GameRes.Instance.InstantiatePrefab(itemId);
            if (obj == null) throw new System.InvalidOperationException($"InstantiatePrefab 失败: {itemId}");
            return obj;
        }

        var pool = GetOrCreatePool(itemId);
        return pool.Get();
    }

    private ObjectPool<GameObject> GetOrCreatePool(string itemId)
    {
        if (_itemPoolById.TryGetValue(itemId, out var pool))
        {
            return pool;
        }

        EnsurePoolRoot();

        var prefab = GameRes.Instance.GetPrefab(itemId);
        if (prefab == null)
        {
            throw new System.InvalidOperationException($"找不到物品Prefab: {itemId}");
        }

        pool = new ObjectPool<GameObject>(
            createFunc: () =>
            {
                var obj = Instantiate(prefab, _poolRoot);
                obj.name = prefab.name;
                var marker = obj.GetComponent<PooledItemMarker>();
                if (marker == null) marker = obj.AddComponent<PooledItemMarker>();
                marker.PoolKey = itemId;
                marker.InPool = true;
                obj.SetActive(false);
                return obj;
            },
            actionOnGet: obj =>
            {
                var marker = obj.GetComponent<PooledItemMarker>();
                if (marker != null) marker.InPool = false;
                obj.SetActive(true);
            },
            actionOnRelease: obj =>
            {
                obj.transform.SetParent(_poolRoot, false);
                obj.SetActive(false);
            },
            actionOnDestroy: Destroy,
            collectionCheck: false,
            defaultCapacity: 4,
            maxSize: DefaultPoolMaxSize
        );

        _itemPoolById[itemId] = pool;
        return pool;
    }

    private bool ShouldUsePool(string itemId)
    {
        if (PoolAllItems) return true;
        return PoolItemIDs.Contains(itemId);
    }

    private void EnsurePoolRoot()
    {
        if (_poolRoot != null) return;

        var root = new GameObject("[ItemPool]");
        root.transform.SetParent(transform, false);
        root.SetActive(true);
        _poolRoot = root.transform;
    }

    private IEnumerable<string> GetPoolIdsSnapshot()
    {
        if (PoolAllItems)
        {
            return GameRes.Instance.AllPrefabs.Keys.ToArray();
        }

        return PoolItemIDs.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToArray();
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
        Data_Player playerData = LoadOrCreatePlayerData(playerName);
        //传入数据创建玩家
        Player player = CreatePlayer(playerData);
        //设置玩家数据到玩家引用字典
        ItemMgr.Instance.Player_DIC[player.Data.Name_User] = player;

        player.Load();

        return player;
    }

    [Tooltip("实例化玩家 但是不初始化")]
    public Player CreatePlayer(string playerName)
    {
        // 加载或者创建玩家数据
        Data_Player playerData = LoadOrCreatePlayerData(playerName);
        //传入数据创建玩家
        Player player = CreatePlayer(playerData);
        //设置玩家数据到玩家引用字典
        ItemMgr.Instance.Player_DIC[player.Data.Name_User] = player;

        return player;
    }
    private Data_Player LoadOrCreatePlayerData(string playerName)
    {
        Data_Player playerData;
        //检测存档中是否存在玩家数据
        if (SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(playerName, out var loadedPlayerData))
        {
            playerData = loadedPlayerData;
        }
        else //如果不存在，则创建默认玩家数据
        {
            playerData = CreateDefaultPlayerData(playerName);
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

