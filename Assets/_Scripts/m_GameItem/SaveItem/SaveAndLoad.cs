using MemoryPack;
using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json;

public class SaveAndLoad : SingletonAutoMono<SaveAndLoad>
{
    [Header("存档相关")]
    [Tooltip("存档名称")]
    public string SaveName = "DefaultSave";
    [Tooltip("存档路径")]
    public string SavePath = "Assets/Saves/";
    [Tooltip("当前使用的存档数据")]
    public GameSaveData SaveData;



    [Header("预设模板相关")]
    [Tooltip("预设模板存档名称")]
    public string TemplateSaveName = "模板";
    [Tooltip("预设模板存档全路径")]
    public string TemplateSavePath = "Assets/Saves/DefaultTemplate/";
    [Tooltip("当前使用的预设模板存档数据")]
    public GameSaveData TemplateSaveData;

    public string playerName;


    public void Start()
    {
        print("SaveAndLoad Start");
        //避免销毁
        DontDestroyOnLoad(gameObject);
    }

    //加载玩家
  
    //保存玩家
   


    #region 保存
 
     [Button("保存玩家")]
    public int SavePlayer()
    {
        int playerCount = 0;
        //获取场景中所有Player
        Player[] players = FindObjectsOfType<Player>();

        //保存玩家数据
        foreach (Player player in players)
        {
            player.Save();
            SaveData.PlayerData_Dict[player.Data.PlayerUserName] = player.Data;
            player.gameObject.SetActive(false);
         //Destroy(player.gameObject);
            playerCount++;
        }

        Debug.Log("玩家数据保存成功！玩家数量：" + playerCount);
        return playerCount;
    }
    [Button("保存存档")]
    void Save()
    {
        //确保模板存档不会被覆盖
        if(SaveData.saveName == "模板"&&!Input.GetKey(KeyCode.S))
        {
            Load();
        }

        SaveActiveMapToSaveData();

        if (SaveData != null)
        {
            SaveToDisk(SaveData);
        }
    }

    public void SaveActiveMapToSaveData()
    {
        if (SaveData == null)
        {
            SaveData = new GameSaveData();
            SaveData.MapSaves_Dict = new Dictionary<string, MapSave>();
            SaveData.PlayerData_Dict = new Dictionary<string, PlayerData>();
        }

        // 等待保存玩家数据的协程（如果 SavePlayer 是异步的）
        SavePlayer();

        // 保存当前激活的地图
        MapSave mapSave = SaveActiveScene_Map();
        SaveData.MapSaves_Dict[mapSave.MapName] = mapSave;
        Debug.Log("当前地图数据保存成功！");
    }
    #endregion

    #region 加载
    [Button("加载玩家")]
    public void LoadPlayer(string playerName)
    {
        PlayerData _data;
        if (SaveData.PlayerData_Dict.ContainsKey(playerName))
        {
            _data = SaveData.PlayerData_Dict[playerName];
        }
        else
        {
            if (TemplateSaveData.PlayerData_Dict.Count == 0)//如果模板存档中没有玩家数据，则加载默认模板存档
            {
                TemplateSaveData = GetSaveByDisk(TemplateSavePath, TemplateSaveName);
            }
            _data = TemplateSaveData.PlayerData_Dict["Ikun"];
        }

        GameObject player = GameRes.Instance.GetPrefab("Player");
        //实例化玩家
        GameObject newPlayerObj = Instantiate(player, Vector3.zero, Quaternion.identity);
        Player newPlayer = newPlayerObj.GetComponentInChildren<Player>();
        newPlayer.Data = _data;
        newPlayer.Data.PlayerUserName = playerName;
        newPlayer.Load();
        Debug.Log("玩家加载成功！");
    }

    [Button("加载存档")]
    public void Load()
    {
        LoadByDisk(SaveName);
    }

    [Button("加载指定地图")]
    public void LoadMap(string mapName)
    {
        MapSave mapSave;

        //检测玩家的存档是否存在对应的地图数据 如果没有则加载默认模板
        if (!SaveData.MapSaves_Dict.ContainsKey(mapName))
        {
            mapSave = LoadOnDefaultTemplateMap(mapName);
        }
        else
        {
            mapSave = SaveData.MapSaves_Dict[mapName];
        }
       

        // 遍历字典中每个键值对
        foreach (var kvp in mapSave.items)
        {
            string itemName = kvp.Key;
            List<ItemData> itemDataList = kvp.Value;
            // 对于同一名称的多个物品逐个实例化
            foreach (ItemData forLoadItemData in itemDataList)
            {
                GameObject itemPrefab;
                // 假设 GameResManager.Instance.AllPrefabs 以物品名称为 key
                if (!GameRes.Instance.AllPrefabs.TryGetValue(forLoadItemData.Name, out itemPrefab))
                {
                    Debug.LogWarning($"未找到预制体，名称：{forLoadItemData.Name}");
                    continue;
                }
                // 实例化物品
                GameObject newItemObj = Instantiate(itemPrefab, Vector3.zero, Quaternion.identity);
                // 设置物品数据
                newItemObj.GetComponent<Item>().Item_Data = forLoadItemData;
                // 设置位置、旋转、缩放
                newItemObj.transform.position = forLoadItemData._transform.Position;
                newItemObj.transform.rotation = forLoadItemData._transform.Rotation;
                newItemObj.transform.localScale = forLoadItemData._transform.Scale;
                newItemObj.SetActive(true);

                if(newItemObj.GetComponent<Item>() is ISave_Load save_Load)
                {
                    save_Load.Load();
                }
            }
        }
        Debug.Log("物体加载成功！");

        LoadPlayer(playerName);
    }

    public MapSave LoadOnDefaultTemplateMap(string mapName)
    {
        if (TemplateSaveData.MapSaves_Dict.Count == 0)
            TemplateSaveData = GetSaveByDisk(TemplateSavePath, TemplateSaveName);

       return TemplateSaveData.MapSaves_Dict[mapName];
    }

    #endregion

    #region 其他
    //清理初始地图上的物品
    [Button("清理初始地图上的物品")]
    public void CleanActiveMapItems()
    {
        // 获取当前场景中所有对象
        Item[] allObjs = GameObject.FindObjectsOfType<Item>();

        foreach (Item obj in allObjs)
        {
            // 检查是否对象名字不为 "MapCore"，且不是 "WorldManager" 的子对象
            if (obj.name == "WorldManager" || obj.transform.parent?.name == "WorldManager")
            {
                continue;
            }
            Destroy(obj); // 销毁对象
        }
        Debug.Log("初始地图物品清理成功！");
    }
    #endregion

    #region 工具方法

    #region 保存当前激活的场景的地图数据
    /// <summary>
    /// 保存当前激活的场景的地图数据
    /// </summary>
    /// <param name="MapName"></param>
    /// <returns></returns>
    public MapSave SaveActiveScene_Map()
    {
        MapSave worldSave = new MapSave();
        // 获取当前激活场景的名称
        worldSave.MapName = SceneManager.GetActiveScene().name;
        // 获取当前场景中的所有物品数据（已按名称分组）
        worldSave.items = GetActiveSceneAllItemData();
        return worldSave;
    }

    #endregion
    #region 获取当前激活的场景中的所有物品数据

    /// <summary>
    /// 获取当前激活的场景中的所有物品数据
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, List<ItemData>> GetActiveSceneAllItemData()
    {
        // 获取当前场景中的所有物品
        Item[] itemsInScene = FindObjectsOfType<Item>();
        Dictionary<string, List<ItemData>> itemData_dict = new Dictionary<string, List<ItemData>>();
        // 遍历所有物品 找出继承ISave_Load的物品并调用Save方法
        foreach (Item item in itemsInScene)
        {
            // 4. 检查物品是否实现了 ISave_Load 接口
            if (item is ISave_Load saveableItem)
            {
                saveableItem.Save(); // 调用 Save 方法保存物品数据
            }

            item.SyncPosition(); // 同步位置信息
                                 // 获取物品数据（假设 Item_Data 包含 Name 属性）
            ItemData itemDataTemp = item.Item_Data;
            // 根据名称分组保存
            if (!itemData_dict.ContainsKey(itemDataTemp.Name))
            {
                itemData_dict[itemDataTemp.Name] = new List<ItemData>();
            }

            itemData_dict[itemDataTemp.Name].Add(itemDataTemp);
        }
        return itemData_dict;
    }

    #endregion
    #region 保存到磁盘

    public void SaveToDisk(GameSaveData SaveData)
    {
        SaveData.saveName = SaveName;

        byte[] dataBytes = MemoryPackSerializer.Serialize(SaveData);
        //获取当前场景名称设置为存档名称
       // SaveData.saveName = SceneManager.GetActiveScene().name;
        //保存到磁盘
        File.WriteAllBytes(SavePath + SaveName + ".QAQ", dataBytes);
        Debug.Log("存档成功！");
    }
    #endregion
    #region 从磁盘上加载

    public void LoadByDisk(string Load_saveName)
    {
        Debug.Log("开始从磁盘加载存档：" + Load_saveName);
        SaveData = null;
        SaveData = MemoryPackSerializer.Deserialize<GameSaveData>(File.ReadAllBytes(SavePath + Load_saveName + ".QAQ"));
    }

    public GameSaveData GetSaveByDisk(string savePath, string Load_saveName)
    {
        return MemoryPackSerializer.Deserialize<GameSaveData>(File.ReadAllBytes(savePath + Load_saveName + ".QAQ"));
    }
    #endregion

    #endregion

    #region 初始化启动游戏方法

    [Button("初始化启动游戏方法")]
    public void ChangeScene(string sceneName = "平原")
    {
        // 等待 Save() 完成（Save() 是协程）
        Save();
        // 异步加载场景
        StartCoroutine(EnterScene(sceneName));
    }
    // 协程：异步加载场景并加载地图
    public IEnumerator EnterScene(string sceneName = "平原")
    {
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);

        // 等待场景加载完成
        while (!asyncLoad.isDone)
        {
            yield return null;
        }

        // 场景加载完成后加载地图
        LoadMap(sceneName);
    }


    #endregion
}

[MemoryPackable]
[System.Serializable]
public partial class MapSave
{
    public string MapName;

    [ShowNonSerializedField]
    // 将原先存储单个 ItemData 的字典改为存储 List<ItemData>，key 为物品名称
    public Dictionary<string, List<ItemData>> items = new Dictionary<string, List<ItemData>>();

    // 说明：在保存物品时，同一名称的物品会存储在同一 List 中，
    // 方便后续加载时批量实例化并赋值
}


[MemoryPackable]
[System.Serializable]
public partial class GameSaveData
{
    public string saveName= "defaultSaveName";//存档名称
    [ShowNonSerializedField]
    //存档数据结构
    public Dictionary<string, MapSave> MapSaves_Dict = new Dictionary<string, MapSave>();
    //玩家数据
    [ShowNonSerializedField]
    public Dictionary<string, PlayerData> PlayerData_Dict = new Dictionary<string, PlayerData>();

    public string leaveTime = "0";//离开时间
}
