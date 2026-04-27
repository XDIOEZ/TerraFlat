using Force.DeepCloner;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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

    private readonly List<Item> _runtimeItems = new();
    private readonly List<Item> _updateSnapshot = new(256);
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
        if (GameManager.Instance != null)
        {
            GameManager.Instance.BackToHelloScene_Event_Start -= CleanupNullItems;
        }
    }

    private void Update()
    {
        if (_runtimeItems.Count == 0)
        {
            return;
        }

        _updateSnapshot.Clear();
        _updateSnapshot.AddRange(_runtimeItems);

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

        UnregisterRuntimeItem(item);

        item.OnItemDestroy.Invoke(item);
        if (item.itemData != null)
        {
        item.Save();
        }
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

