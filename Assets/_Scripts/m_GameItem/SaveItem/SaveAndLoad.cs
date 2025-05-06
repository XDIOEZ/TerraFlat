using MemoryPack;
using Sirenix.OdinInspector;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SaveAndLoad : SingletonAutoMono<SaveAndLoad>
{
    [Header("存档相关")]
    [Tooltip("存档名称")]
    public string SaveName = "DefaultSave";
    [Tooltip("存档路径")]
    public string SavePath = "Assets/Saves/";
    [Tooltip("当前使用的存档数据")]
    public GameSaveData SaveData;

    [Tooltip("临时失效物体")]
    public List<GameObject> GameObject_False;

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
        DontDestroyOnLoad(gameObject);
    }



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
            //存入临时失效物体列表
            GameObject_False.Add(player.gameObject);

            playerCount++;
        }

       // Debug.Log("玩家数据保存成功！玩家数量：" + playerCount);
        return playerCount;
    }

    [Button("保存存档到磁盘上")]
    void Save()
    {
        SaveActiveMapToSaveData();

        SaveToDisk(SaveData);
        
    }

    [Tooltip("保存当前激活场景存入当前的场景字典中")]
    public void SaveActiveMapToSaveData()
    {

        SaveData ??= new GameSaveData();
        GameObject_False.Clear();
        SavePlayer();
        // 保存当前激活的地图
        MapSave mapSave = SaveActiveScene_Map();
        SaveData.MapSaves_Dict[mapSave.MapName] = mapSave;

     //   Debug.Log("成功将当前激活场景存入当前使用的存档");
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
            Debug.Log("成功加载玩家：" + playerName);
        }
        else
        {
            if (TemplateSaveData.PlayerData_Dict.Count == 0)//如果模板存档中没有玩家数据，则加载默认模板存档
            {
                TemplateSaveData = GetSaveByDisk(TemplateSavePath, TemplateSaveName);
            }
            _data = TemplateSaveData.PlayerData_Dict["Ikun"];
            Debug.Log("未找到玩家数据，加载默认模板玩家数据");
        }

        GameObject newPlayerObj = GameRes.Instance.InstantiatePrefab("Player");
        Player newPlayer = newPlayerObj.GetComponentInChildren<Player>();
        newPlayer.Data = _data;
        newPlayer.Load();
       // Debug.Log("玩家加载成功！");
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

        //检测存档是否存在对应的地图数据 如果没有则加载默认模板
        if (!SaveData.MapSaves_Dict.ContainsKey(mapName))
        {
            Debug.LogWarning($"未找到地图数据，名称：{mapName} , 加载默认模板");
            mapSave = LoadOnDefaultTemplateMap(mapName);
        }
        else
        {
            mapSave = SaveData.MapSaves_Dict[mapName];
            Debug.Log("成功加载地图：" + mapName);
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
        //  Debug.Log("物体加载成功！");

        LoadPlayer(playerName);
    }

    public MapSave LoadOnDefaultTemplateMap(string mapName)
    {
        if (TemplateSaveData.MapSaves_Dict.Count == 0)
        {
            TemplateSaveData = GetSaveByDisk(TemplateSavePath, TemplateSaveName);
        }
        return TemplateSaveData.MapSaves_Dict[mapName];
    }

    #endregion

    #region 工具方法
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
        //激活失效物体
        foreach (GameObject go in GameObject_False)
        {
            go.SetActive(true);
        }

        return worldSave;
    }
    /// <summary>
    /// 获取当前激活的场景中的所有物品数据
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, List<ItemData>> GetActiveSceneAllItemData()
    {
        Dictionary<string, List<ItemData>> itemDataDict = new Dictionary<string, List<ItemData>>();

        // 第一步：获取场景中所有的 Item（包括非激活状态）
        Item[] allItems = FindObjectsOfType<Item>(includeInactive: false);

        // 第二步：先调用 Save()，避免遗漏
        foreach (Item item in allItems)
        {
            if (item == null)
                continue;

            if (item is ISave_Load saveableItem)
            {
                try
                {
                    saveableItem.Save();
                    //将无效的物品添加到临时失效物体列表
                    if (!item.gameObject.activeInHierarchy)
                    {
                        GameObject_False.Add(item.gameObject);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"保存物品失败: {item.name}", item);
                    Debug.LogException(ex);
                }
            }

           
        }

        // 第三步：再次遍历所有 Item，筛选出 still active 的，并同步位置、收集数据
        foreach (Item item in allItems)
        {
            if (item == null || item.transform == null || item.gameObject == null)
                continue;

            // 只处理当前仍处于激活状态的 Item
            if (!item.gameObject.activeInHierarchy)
                continue;

            item.SyncPosition();

            ItemData itemData = item.Item_Data;
            if (itemData == null)
                continue;

            if (!itemDataDict.TryGetValue(itemData.Name, out List<ItemData> list))
            {
                list = new List<ItemData>();
                itemDataDict[itemData.Name] = list;
            }

            list.Add(itemData);
        }

        return itemDataDict;
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

    #region 场景切换

    [Button("改变场景")]
    public void ChangeScene(string sceneName = "平原")
    {
        // 保存当前场景的地图数据
        SaveActiveMapToSaveData();
        // 切换场景
        EnterScene(sceneName);
    }

    private Scene newScene;

    /// <summary>
    /// 切换场景
    /// </summary>
    /// <param name="sceneName"></param>
    public void EnterScene(string sceneName = "平原")
    {
        // 1. 创建新场景（空场景）
        newScene = SceneManager.CreateScene(sceneName);

        // 2. 获取当前活动场景
        Scene previousScene = SceneManager.GetActiveScene();

        // 3. 注册卸载完成后的回调
        SceneManager.sceneUnloaded += OnPreviousSceneUnloaded;

        // 4. 开始卸载旧场景
        SceneManager.UnloadSceneAsync(previousScene);
    }

    private void OnPreviousSceneUnloaded(Scene unloadedScene)
    {
        // 取消事件注册，避免重复调用
        SceneManager.sceneUnloaded -= OnPreviousSceneUnloaded;

        // 设为新场景为活动场景
        SceneManager.SetActiveScene(newScene);

        // 加载内容
        LoadMap(newScene.name);
    }




    #endregion
}

[MemoryPackable]
[System.Serializable]
public partial class MapSave
{
    public string MapName;

    [ShowInInspector]
    // 将原先存储单个 ItemData 的字典改为存储 List<ItemData>，key 为物品名称
    public Dictionary<string, List<ItemData>> items = new Dictionary<string, List<ItemData>>();

    // 说明：在保存物品时，同一名称的物品会存储在同一 List 中，
    // 方便后续加载时批量实例化并赋值
}


[MemoryPackable]
[System.Serializable]
public partial class GameSaveData
{
    public string saveName = "defaultSaveName";//存档名称
    [ShowInInspector]
    //存档数据结构
    public Dictionary<string, MapSave> MapSaves_Dict = new Dictionary<string, MapSave>();
    //玩家数据
    [ShowInInspector]
    public Dictionary<string, PlayerData> PlayerData_Dict = new Dictionary<string, PlayerData>();

    public string leaveTime = "0";//离开时间

    //构造函数
    public GameSaveData()
    {
        MapSaves_Dict = new Dictionary<string, MapSave>();
        PlayerData_Dict = new Dictionary<string, PlayerData>();
    }
}