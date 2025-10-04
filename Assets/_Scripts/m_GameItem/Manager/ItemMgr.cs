using Force.DeepCloner;
using NUnit.Framework.Interfaces;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public class ItemMgr : SingletonMono<ItemMgr>
{
    [ShowInInspector]
    public Dictionary<int, Item> WorldRunTimeItems = new();

    [ShowInInspector]
    public Dictionary<string, List<Item>> RuntimeItemsGroup = new();

    [ShowInInspector]
    public Dictionary<string, Player> Player_DIC = new();

    public string PlayerInSceneName => Player_DIC[SaveDataMgr.Instance.CurrentContrrolPlayerName].Data.CurrentSceneName;
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

    public Map _cachedMap;
    public Map Map
    {
        get
        {
            if (_cachedMap == null)
            {
                if (RuntimeItemsGroup.TryGetValue("MapCore", out var list) && list.Count > 0)
                {
                    _cachedMap = (Map)list[0];
                }
            }
            return _cachedMap;
        }
    }

    [Button("加载所有Runtime物品")]
    protected override void Awake()
    {
        base.Awake();
        // 第一步：获取场景中所有的 Item（包括非激活状态）
        Item[] allItems = FindObjectsOfType<Item>(includeInactive: false);

        foreach (Item item in allItems)
        {
            if (item is ISave_Load saveableItem)
            {
                try
                {
         //           saveableItem.Load();
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"加载物品失败: {item.name}", item);
                    Debug.LogException(ex);
                }
            }
        //    RunTimeItems.Add(item.Item_Data.Guid, item);
            //RunTimeItems[item.itemData.Guid] = item;
            AddToGroup(item); // 新增分组逻辑
        }
        // Debug.Log("物品加载完毕");
        GameManager.Instance.Event_ExitGame_Start+= CleanupNullItems;
    }

    private void OnDestroy()
    {
       
    }


    #region 实例化物品
    // 主要的实例化方法 - 其他方法都应该调用这个
public Item InstantiateItem(ItemData itemData_, Vector3 position = default, Quaternion rotation = default, Vector3 scale = default, GameObject parent = null)
{
    if (position == default) position = Vector3.zero;
    if (rotation == default) rotation = Quaternion.identity;
    if (scale == default || scale == Vector3.zero) scale = Vector3.one;

    GameObject itemObj = GameRes.Instance.InstantiatePrefab(itemData_.IDName, position, rotation, scale);
    Item item = itemObj.GetComponent<Item>();

    item.itemData = itemData_;

        if (WorldRunTimeItems.ContainsKey(item.itemData.Guid))
        {
            // 实例化时刷新Guid
            item.itemData.Guid = GenerateGuid();
        }
        // 添加到世界运行物体管理字典中
        WorldRunTimeItems[item.itemData.Guid] = item;
        // 分组逻辑
        AddToGroup(item);

    if (parent != null)
    {
        itemObj.transform.SetParent(parent.transform, true);
    }
    else
    { 
        if(ChunkMgr.Instance.Chunk_Dic_Active.TryGetValue(Chunk.GetChunkPosition(position).ToString(), out var chunk))
        {
            if (chunk == null)
            {
                ChunkMgr.Instance.GetClosestChunk(itemObj.transform.position, out chunk);
            }
            itemObj.transform.SetParent(chunk.transform, true);
            chunk.AddItem(item);
        }
        else if (ChunkMgr.Instance.Chunk_Dic_UnActive.TryGetValue(Chunk.GetChunkPosition(position).ToString(), out var UnActivechunk))
        {
            itemObj.transform.SetParent(UnActivechunk.transform, true);
            // 注意：这里可能有问题，应该传入UnActivechunk而不是chunk
            // UnActivechunk.AddItem(item);
        }
    }

    return item;
}

// 通过名称实例化的重载方法
public Item InstantiateItem(string itemName, Vector3 position = default, Quaternion rotation = default, Vector3 scale = default, GameObject parent = null)
{
    ItemData templateData = GameRes.Instance.GetPrefab(itemName).GetComponent<Item>().itemData.DeepClone();
    return InstantiateItem(templateData, position, rotation, scale, parent);
}

public Item InstantiateItem(string itemName, GameObject parent = null, Vector3 position = default)
{
    return InstantiateItem(itemName, position, default, default, parent);
}

public Item InstantiateItem(string itemName, GameObject parent = null, Vector3 position = default, Quaternion rotation = default, Vector3 scale = default)
{
    return InstantiateItem(itemName, position, rotation, scale, parent);
}

public Item InstantiateItem(string itemName)
{
    return InstantiateItem(itemName, default, default, default, null);
}

// 通过ItemData和父对象实例化（简化版）
public Item InstantiateItem(ItemData itemData, GameObject parent = null)
{
    // 使用主方法，从itemData中提取transform信息
    return InstantiateItem(itemData, itemData.transform.position, itemData.transform.rotation, itemData.transform.scale, parent);
}

// 生成GUID的辅助方法
public int GenerateGuid()
{
    return System.Guid.NewGuid().GetHashCode();
}

    #endregion

    // ✅ 添加到分组
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

      //  Debug.Log("已清理无效的 Item 引用。");
    }


    [Button("加载玩家")]
    public Player LoadPlayer(string playerName)
    {
        Data_Player playerData;
        //检测存档中是否存在玩家数据
        if (SaveDataMgr.Instance.SaveData.PlayerData_Dict.TryGetValue(playerName, out var LoadedPlayerData))
        {
            playerData = LoadedPlayerData;
        }
        else //如果不存在，则创建默认玩家数据
        {
            var prefab = GameRes.Instance.GetPrefab("Player");
            var defaultPlayer = prefab.GetComponent<Player>();
            playerData = defaultPlayer.Data.DeepClone();
            playerData.Guid = playerName.GetHashCode();
            playerData.Name_User = playerName;
        }
        //传入数据创建玩家
        Player player = CreatePlayer(playerData);

        ItemMgr.Instance.Player_DIC[player.Data.Name_User] = player;

        return player;
    }
    private Player CreatePlayer(Data_Player data)
    {
        Player newPlayer = (Player)ItemMgr.Instance.InstantiateItem(data, Vector3.zero, Quaternion.identity, Vector3.one,new GameObject("Players"));

        // ✅ 将父对象设置为空（放到场景根节点下）
        newPlayer.transform.SetParent(null, true);

        return newPlayer;
    }
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

