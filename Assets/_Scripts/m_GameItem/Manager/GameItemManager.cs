using Force.DeepCloner;
using NUnit.Framework.Interfaces;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public class GameItemManager : SingletonMono<GameItemManager>
{
    [ShowInInspector]
    public Dictionary<int, Item> RunTimeItems = new();

    [ShowInInspector]
    public Dictionary<string, List<Item>> RuntimeItemsGroup = new();

    [ShowInInspector]
    public Dictionary<string, Player> Player_DIC = new();

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
                    saveableItem.Load();
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

    }

    private void OnDestroy()
    {
       
    }


    #region 实例化物品
    [Button("实例化对象")]
    // 实例化（通过名称）
    public Item InstantiateItem(string itemName, Vector3 position = default, Quaternion rotation = default, Vector3 scale = default, GameObject parent = null)
    {
        ItemData templateData = GameRes.Instance.GetItem(itemName).itemData.DeepClone();
        // 调用通过 ItemData 的实例化方法
        templateData.Guid = 0;
        return InstantiateItem(templateData, position, rotation, scale, parent);
    }

    // 实例化（通过 ItemData）
    public Item InstantiateItem(ItemData itemData, Vector3 position = default, Quaternion rotation = default, Vector3 scale = default, GameObject parent = null)
    {
        if (position == default) position = Vector3.zero;
        if (rotation == default) rotation = Quaternion.identity;
        if (scale == default || scale == Vector3.zero)
            scale = Vector3.one;

        // 检测是否存在重复 GUID
        if (RunTimeItems.ContainsKey(itemData.Guid))
        {
            Debug.LogError($"【物品实例化错误】检测到重复的GUID：{itemData.Guid}\n" +
                $"当前物品：{itemData.IDName}\n" +
                $"已存在物品：{RunTimeItems[itemData.Guid].name}");
            return null;
        }

        GameObject itemObj = GameRes.Instance.InstantiatePrefab(itemData.IDName, position, rotation, scale);

        Item item = itemObj.GetComponent<Item>();

        if (parent != null)
        {
            itemObj.transform.SetParent(parent.transform, true);
        }
        else
        {
            if(GameChunkManager.Instance.Chunk_Dic_Active.TryGetValue(Chunk.GetChunkPosition(position).ToString(), out var chunk))
            {
                itemObj.transform.SetParent(chunk.transform, true);
            }
            else if (GameChunkManager.Instance.Chunk_Dic_UnActive.TryGetValue(Chunk.GetChunkPosition(position).ToString(), out var UnActivechunk))
            {
                itemObj.transform.SetParent(UnActivechunk.transform, true);
            }

            GameChunkManager.Instance.UpdateItem_ChunkOwner(item);
        }


        item.itemData = itemData;

        // 如果 Guid 在 -1000 到 1000 之间，重新生成唯一 Guid
        if (item.itemData.Guid > -1000 && item.itemData.Guid < 1000)
            item.itemData.Guid = System.Guid.NewGuid().GetHashCode();

        RunTimeItems[item.itemData.Guid] = item;
        AddToGroup(item); // 分组逻辑

        return item;
    }


    #endregion

    // ✅ 添加到分组
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
        if (RunTimeItems.TryGetValue(guid, out var item))
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
        Player[] players = GameItemManager.Instance.Player_DIC.Values.ToArray();

        foreach (Player player in players)
        {
            player.Save();

            player.ModuleSave();

            SaveDataManager.Instance.SaveData.PlayerData_Dict[player.Data.Name_User] = player.Data;

            player.gameObject.SetActive(false);

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
        foreach (var pair in RunTimeItems)
        {
            if (pair.Value == null)
            {
                keysToRemove.Add(pair.Key);
            }
        }
        foreach (int key in keysToRemove)
        {
            RunTimeItems.Remove(key);
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
        if (SaveDataManager.Instance.SaveData.PlayerData_Dict.TryGetValue(playerName, out var LoadedPlayerData))
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
        //更具传入数据创建玩家
        Player player = CreatePlayer(playerData);

        return player;
    }
    private Player CreatePlayer(Data_Player data)
    {
        Player newPlayer = (Player)GameItemManager.Instance.InstantiateItem(data, Vector3.zero, Quaternion.identity, Vector3.one,new GameObject("Players"));

        // ✅ 将父对象设置为空（放到场景根节点下）
        newPlayer.transform.SetParent(null, true);

        newPlayer.Load();

        return newPlayer;
    }
    [Tooltip("随机空投")]
    public void RandomDropInMap(GameObject dropObject, Chunk map = null)
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

        // 整个地图的大小（每个地图块内是 tileSizeX x tileSizeY）
        int mapWidth = SaveDataManager.Instance.SaveData.ChunkSize.x * tileSizeX;
        int mapHeight = SaveDataManager.Instance.SaveData.ChunkSize.y * tileSizeY;

        // 创建随机生成器
        System.Random rng = new System.Random();

        // 生成在整个地图区域内的随机坐标
        float randX = (float)rng.NextDouble() * mapWidth + SaveDataManager.Instance.SaveData.ChunkSize.x;
        float randY = (float)rng.NextDouble() * mapHeight + SaveDataManager.Instance.SaveData.ChunkSize.y;

        // 设置空投对象位置
        dropObject.transform.position = defaultPosition + new Vector2(randX, randY);
    }

}

