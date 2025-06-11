using MemoryPack;
using Meryel.UnityCodeAssist.YamlDotNet.Core;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UltEvents;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

public class SaveAndLoad : SingletonAutoMono<SaveAndLoad>
{
    [Header("存档相关")]

    [Tooltip("模板存档路径")]
    public string TemplateSavePath = "Assets/Saves/GameSaveData/";

    [Tooltip("玩家的存档路径")]
    public string PlayerSavePath = "Assets/Saves/LoaclSaveData/";

    [Tooltip("当前使用的存档数据")]
    public GameSaveData SaveData;

    [Tooltip("场景切换时 会调用的事件")]
    public UltEvent OnSceneSwitch;

    [Tooltip("临时失效物体")]
    public List<GameObject> GameObject_False;

    public string CurrentContrrolPlayerName;

    [Header("默认设置")]
    public DefaultSettings defaultSettings;

    public void Start()
    {
        DontDestroyOnLoad(gameObject);
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
        // 获取场景中所有 Player
        Player[] players = FindObjectsOfType<Player>();

        // 保存玩家数据
        foreach (Player player in players)
        {
            player.Save();
            SaveData.PlayerData_Dict[player.Data.Name_User] = player.Data;
            player.gameObject.SetActive(false);

            // 存入临时失效物体列表
            GameObject_False.Add(player.gameObject);
            playerCount++;
        }

        return playerCount;
    }

    [Button("保存存档到磁盘上")]
    void Save()
    {
        SaveActiveMapToSaveData();
        SaveToDisk(SaveData,PlayerSavePath, SaveData.saveName);
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
    }

    public GameSaveData GetSaveData()
    {
        SaveData ??= new GameSaveData();
        GameObject_False.Clear();
        SavePlayer();

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
                    _data.Name_User = playerName;
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
    }


    [Button("加载指定地图")]
    public void LoadMap(string mapName)
    {
        if (SaveData.MapSaves_Dict.TryGetValue(mapName, out MapSave mapSave))
        {
            Debug.Log($"成功加载地图：{mapName}");
            InstantiateItemsFromMapSave(mapSave);
            LoadPlayer(CurrentContrrolPlayerName);
            return;
        }

        Debug.LogWarning($"未找到地图数据：{mapName}，尝试加载默认模板");

        LoadAssetByLabelAndName<WorldSaveSO>(defaultSettings.Default_ADDTable, mapName, result =>
        {
            if (result != null && result.SaveData != null &&
                result.SaveData.MapSaves_Dict.TryGetValue(mapName, out MapSave defaultMapSave))
            {
                InstantiateItemsFromMapSave(defaultMapSave);
                LoadPlayer(CurrentContrrolPlayerName);
            }
            else
            {
                Debug.LogError($"加载默认模板地图失败，名称：{mapName}");
            }
        });
    }

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

    #region 工具方法 - 保存到磁盘

    public void SaveToDisk(GameSaveData SaveData,string SavePath,string SaveName)
    {
        SaveData.saveName = SaveName;

        byte[] dataBytes = MemoryPackSerializer.Serialize(SaveData);
        File.WriteAllBytes(SavePath + SaveName + ".QAQ", dataBytes);
        Debug.Log("存档成功！");
    }

    public MapSave SaveActiveScene_Map()
    {
        MapSave worldSave = new MapSave();
        worldSave.MapName = SceneManager.GetActiveScene().name;
        worldSave.items = GetActiveSceneAllItemData();

        foreach (GameObject go in GameObject_False)
        {
            go.SetActive(true);
        }

        return worldSave;
    }

    public Dictionary<string, List<ItemData>> GetActiveSceneAllItemData()
    {
        Dictionary<string, List<ItemData>> itemDataDict = new Dictionary<string, List<ItemData>>();

        Item[] allItems = FindObjectsOfType<Item>(includeInactive: false);

        foreach (Item item in allItems)
        {
            if (item == null)
                continue;

            if (item is ISave_Load saveableItem)
            {
                try
                {
                    saveableItem.Save();

                    if (!item.gameObject.activeInHierarchy)
                    {
                        GameObject_False.Add(item.gameObject);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"保存物品失败: {item.name}", item);
                    Debug.LogException(ex);
                }
            }
        }

        foreach (Item item in allItems)
        {
            if (item == null || item.transform == null || item.gameObject == null)
                continue;

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

    #region 工具方法 - 从磁盘加载

    public void LoadSaveByDisk(string LoadSavePath)
    {
        Debug.Log("开始从磁盘加载存档：" + LoadSavePath);
        SaveData = null;
        SaveData = MemoryPackSerializer.Deserialize<GameSaveData>(File.ReadAllBytes(LoadSavePath + ".QAQ"));
    }


    public GameSaveData GetSaveByDisk(string savePath, string Load_saveName)
    {
        return MemoryPackSerializer.Deserialize<GameSaveData>(File.ReadAllBytes(savePath + Load_saveName + ".QAQ"));
    }
    #endregion

    public void DeletSave(string savePath, string saveName)
    {
        string filePath = Path.Combine(savePath, saveName + ".QAQ");
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Debug.Log($"存档已删除：{filePath}");
            }
            else
            {
                Debug.LogWarning($"未找到要删除的存档文件：{filePath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"删除存档时发生错误：{filePath}");
            Debug.LogException(ex);
        }
    }

    #region 场景切换

    [Button("改变场景")]
    public void ChangeScene(string sceneName)
    {
        if (sceneName == "")
        {
            sceneName = defaultSettings.Default_Map;
        }

        SaveData.ActiveSceneName = sceneName;

        SaveActiveMapToSaveData();

        newScene = SceneManager.CreateScene(sceneName);

        Scene previousScene = SceneManager.GetActiveScene();

        SceneManager.sceneUnloaded += OnPreviousSceneUnloaded;

        SceneManager.UnloadSceneAsync(previousScene);
    }

    private Scene newScene;

    private void OnPreviousSceneUnloaded(Scene unloadedScene)
    {
        SceneManager.sceneUnloaded -= OnPreviousSceneUnloaded;

        SceneManager.SetActiveScene(newScene);

        LoadMap(newScene.name);

        OnSceneSwitch?.Invoke();
    }

    #endregion
}
