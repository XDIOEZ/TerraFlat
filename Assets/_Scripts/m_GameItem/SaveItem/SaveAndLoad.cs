﻿using MemoryPack;
using Meryel.UnityCodeAssist.YamlDotNet.Core;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
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
    public string playerName;

    [Header("默认设置")]
    public DefaultSettings defaultSettings;
    public void Start()
    {
        DontDestroyOnLoad(gameObject);
        //调试模式自动调用场景中所有的load方法
        //获取所有IloadAndSave接口

        // 第一步：获取场景中所有的 Item（包括非激活状态）
        Item[] allItems = FindObjectsOfType<Item>(includeInactive: false);

        // 第二步：先调用 Save()，避免遗漏
        foreach (Item item in allItems)
        {
            if (item is ISave_Load saveableItem)
            {
                try
                {
                    saveableItem.Load();
                    //将无效的物品添加到临时失效物体列表
                    if (!item.gameObject.activeInHierarchy)
                    {
                        GameObject_False.Add(item.gameObject);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"加载物品失败: {item.name}", item);
                    Debug.LogException(ex);
                }
            }


        }
    }

    [Serializable]
    public class DefaultSettings
    {
        [Tooltip("默认加载的SaveADD标签")]
        public string Default_ADDTable = "Template_SaveData";
        [Tooltip("默认玩家模板存档")]
        public string Default_PlayerSave = "玩家";
        [Tooltip("默认玩家名字")]
        public string Default_PlayerName = "默认";
        [Tooltip("默认初始地图")]
        public string Default_Map = "平原";
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
            SaveData.PlayerData_Dict[player.Data.Name_User] = player.Data;
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

    public GameSaveData GetSaveData()
    {
        SaveData ??= new GameSaveData();
        GameObject_False.Clear();
        SavePlayer();
        // 保存当前激活的地图
        MapSave mapSave = SaveActiveScene_Map();
        SaveData.MapSaves_Dict[mapSave.MapName] = mapSave;
        return SaveData;
    }

    #endregion

    #region 加载

    [Button("加载玩家")]
    public void LoadPlayer(string playerName)
    {
        if (SaveData.PlayerData_Dict.ContainsKey(playerName))
        {
            Data_Player _data = SaveData.PlayerData_Dict[playerName];
            Debug.Log("成功加载玩家：" + playerName);
            CreatePlayer(_data);
        }
        else
        {
            LoadAssetByLabelAndName<WorldSaveSO>(defaultSettings.Default_ADDTable, defaultSettings.Default_PlayerSave, result =>
            {
                Data_Player _data;

                if (result == null)
                {
                    Debug.LogWarning("加载失败：WorldSaveSO 对象为空！");
                    _data = new Data_Player();
                }
                else if (result.SaveData == null)
                {
                    Debug.LogWarning("加载失败：SaveData 为空！");
                    _data = new Data_Player();
                }
                else if (!result.SaveData.PlayerData_Dict.ContainsKey("默认"))
                {
                    Debug.LogWarning("加载成功，但未包含“默认”玩家数据！");
                    _data = new Data_Player();
                }
                else
                {
                    _data = result.SaveData.PlayerData_Dict[defaultSettings.Default_PlayerName];
                    _data.Name_User = playerName; // ✅ 直接修改名字
                    Debug.Log($"成功加载默认模板玩家，并设置为新玩家名：{playerName}");
                }

                CreatePlayer(_data);
            });
        }
    }


    private void CreatePlayer(Data_Player data)
    {
        GameObject newPlayerObj = RunTimeItemManager.Instance.InstantiateItem("Player").gameObject;
        Player newPlayer = newPlayerObj.GetComponentInChildren<Player>();
        newPlayer.Data = data;
        newPlayer.Load();
      //  Debug.Log("玩家加载成功！");
    }


    [Button("加载存档")]
    public void Load()
    {
        LoadByDisk(SaveName);
    }

    // Fix for CS7036: Provide the required "onComplete" parameter when calling LoadOnDefaultTemplateMap.

    [Button("加载指定地图")]
    public void LoadMap(string mapName)
    {
        if (SaveData.MapSaves_Dict.TryGetValue(mapName, out MapSave mapSave))
        {
            Debug.Log($"成功加载地图：{mapName}");
            InstantiateItemsFromMapSave(mapSave);
            LoadPlayer(playerName);
            return;
        }

        Debug.LogWarning($"未找到地图数据：{mapName}，尝试加载默认模板");

        LoadAssetByLabelAndName<WorldSaveSO>(defaultSettings.Default_ADDTable, mapName, result =>
        {
            if (result != null && result.SaveData != null &&
                result.SaveData.MapSaves_Dict.TryGetValue(mapName, out MapSave defaultMapSave))
            {
               InstantiateItemsFromMapSave(defaultMapSave);
                LoadPlayer(playerName);
            }
            else
            {
                Debug.LogError($"加载默认模板地图失败，名称：{mapName}");
            }
        });
    }


    // 放在 RunTimeItemManager 中
    public void InstantiateItemsFromMapSave(MapSave mapSave)
    {
        foreach (var kvp in mapSave.items)
        {
            List<ItemData> itemDataList = kvp.Value;

            foreach (ItemData forLoadItemData in itemDataList)
            {
                Item item = RunTimeItemManager.Instance.InstantiateItem(forLoadItemData, forLoadItemData._transform.Position);

                item.transform.rotation = forLoadItemData._transform.Rotation;
                item.transform.localScale = forLoadItemData._transform.Scale;
                item.gameObject.SetActive(true);

                if (item is ISave_Load save_Load)
                    save_Load.Load();
            }
        }
    }





    public void LoadAssetByLabelAndName<T>(string label, string name, Action<T> onComplete) where T : UnityEngine.Object
    {
        Addressables.LoadResourceLocationsAsync(label).Completed += locationHandle =>
        {
            if (locationHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"标签加载失败：{label}");
                onComplete?.Invoke(null);
                return;
            }

            var locations = locationHandle.Result;
            var targetLocation = locations.FirstOrDefault(loc => loc.PrimaryKey.Contains(name));

            if (targetLocation == null)
            {
                Debug.LogWarning($"未找到资源，标签：{label}，名称包含：{name}");
                onComplete?.Invoke(null);
                return;
            }

            Addressables.LoadAssetAsync<T>(targetLocation).Completed += assetHandle =>
            {
                if (assetHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    onComplete?.Invoke(assetHandle.Result);
                }
                else
                {
                    Debug.LogError($"加载失败：{name}（标签：{label}）");
                    onComplete?.Invoke(null);
                }
            };
        };
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

            if (!itemDataDict.TryGetValue(itemData.IDName, out List<ItemData> list))
            {
                list = new List<ItemData>();
                itemDataDict[itemData.IDName] = list;
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
    public void ChangeScene(string sceneName)
    {
        // 若场景名称为空，则使用默认地图
        if(sceneName == "")
        {
            sceneName = defaultSettings.Default_Map;
        }
        SaveData.ActiveSceneName = sceneName;
        // 保存当前场景的地图数据
        SaveActiveMapToSaveData();

        // 进入新场景
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
    public Dictionary<string, MapSave> MapSaves_Dict = new();
    //玩家数据
    [ShowInInspector]
    public Dictionary<string, Data_Player> PlayerData_Dict = new();
    //建筑与场景之间的切换表_位置
    [ShowInInspector]
    public Dictionary<string, Vector2> Scenen_Building_Pos = new();
    //建筑与场景之间的切换表_名字
    [ShowInInspector]
    public Dictionary<string, string> Scenen_Building_Name = new();

    public string ActiveSceneName = "平原";//当前激活场景名称





    public string leaveTime = "0";//离开时间

    //构造函数
    public GameSaveData()
    {
        MapSaves_Dict = new Dictionary<string, MapSave>();
        PlayerData_Dict = new Dictionary<string, Data_Player>();
    }
}