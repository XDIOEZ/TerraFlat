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
using Codice.CM.Common;
using Force.DeepCloner;
using MemoryPack;
using Sirenix.OdinInspector;
using UltEvents;

/// <summary>
/// 游戏存档与加载系统，负责管理游戏数据的保存和加载功能
/// </summary>
public class SaveLoadManager : SingletonAutoMono<SaveLoadManager>
{
    #region 存档配置
    [Header("存档相关")]
    [Tooltip("模板存档路径")]
    public string TemplateSavePath = "Assets/Saves/GameSaveData/";

    [Tooltip("玩家的存档路径")]
    public string UserSavePath = "Assets/Saves/LoaclSaveData/";

    [Tooltip("当前使用的存档数据")]
    public GameSaveData SaveData;

    [Tooltip("场景切换时调用的事件")]
    public UltEvent OnSceneSwitchStart;

    [Tooltip("临时失效物体")]
    public List<GameObject> GameObject_False;

    [Tooltip("当前控制的玩家名称")]
    public string CurrentContrrolPlayerName;

    [Tooltip("退出游戏事件")]
    public UltEvent OnSaveGame;

    [Header("默认设置")]
    public DefaultSettings defaultSettings;

    [Header("模板参考")]
    public WorldSaveSO templateSaveData;

    public bool IsGameStart = false;
    #endregion

    #region 生命周期
    private void Start()
    {
        DontDestroyOnLoad(gameObject);
       // OnSceneSwitch += Save;
    }
    #endregion

    #region 数据结构
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
    #endregion

    #region 保存功能
    /// <summary>
    /// 保存场景中的所有玩家
    /// </summary>
    /// <returns>保存的玩家数量</returns>
    [Button("保存玩家")]
    public int SavePlayer()
    {
        int playerCount = 0;
        Player[] players = FindObjectsOfType<Player>();

        foreach (Player player in players)
        {
            player.Save();
            player.ModuleSave();
            SaveData.PlayerData_Dict[player.Data.Name_User] = player.Data;
            player.gameObject.SetActive(false);
            GameObject_False.Add(player.gameObject);
            playerCount++;
        }

        return playerCount;
    }

    /// <summary>
    /// 保存当前游戏状态到磁盘
    /// </summary>
    [Button("保存存档到磁盘上")]
    public void Save()
    {
        OnSaveGame.Invoke();
        SaveActiveMapToSaveData();
        SaveToDisk(SaveData, UserSavePath, SaveData.saveName);
    }

    /// <summary>
    /// 将当前活动场景保存到存档数据中
    /// </summary>
    [Tooltip("保存当前激活场景存入当前的场景字典中")]
    public void SaveActiveMapToSaveData()
    {
        SaveData ??= new GameSaveData();
        GameObject_False.Clear();
        SavePlayer();

        MapSave mapSave = SaveActiveScene_Map();
        SaveData.Active_MapsData_Dict[mapSave.MapName] = mapSave;
    }

    /// <summary>
    /// 获取当前游戏状态的存档数据
    /// </summary>
    /// <returns>游戏存档数据</returns>
    public GameSaveData GetSaveData()
    {
        SaveData ??= new GameSaveData();
        GameObject_False.Clear();
        SavePlayer();

        MapSave mapSave = SaveActiveScene_Map();
        SaveData.Active_MapsData_Dict[mapSave.MapName] = mapSave;

        return SaveData;
    }
    #endregion

    #region 加载功能
    /// <summary>
    /// 加载指定名称的玩家
    /// </summary>
    /// <param name="playerName">玩家名称</param>
    /// <returns>加载的玩家实例</returns>
    [Button("加载玩家")]
    public Player LoadPlayer(string playerName)
    {
        Data_Player playerData;

        if (SaveData.PlayerData_Dict.TryGetValue(playerName, out var savedData))
        {
            //Debug.Log($"成功加载已保存的玩家：{playerName}");
            playerData = savedData;
        }
        else
        {
            var prefab = GameRes.Instance.GetPrefab("Player");
            var defaultPlayer = prefab.GetComponent<Player>();
            playerData = defaultPlayer.Data.DeepClone();
            playerData.Name_User = playerName;
        }

        return CreatePlayer(playerData);
    }

    /// <summary>
    /// 根据玩家数据创建玩家实例
    /// </summary>
    /// <param name="data">玩家数据</param>
    /// <returns>创建的玩家实例</returns>
    private Player CreatePlayer(Data_Player data)
    {
        GameObject newPlayerObj = GameItemManager.Instance.InstantiateItem(data).gameObject;
        Player newPlayer = newPlayerObj.GetComponentInChildren<Player>();
        newPlayer.Load();
        return newPlayer;
    }

    /// <summary>
    /// 加载指定名称的地图
    /// </summary>
    /// <param name="mapName">地图名称</param>
    [Button("加载指定地图")]
    public void LoadMap(string mapName)
    {
        if (SaveData.Active_MapsData_Dict.TryGetValue(mapName, out MapSave mapSave))
        {
        //    Debug.Log($"成功加载地图：{mapName}");
            InstantiateItemsFromMapSave(mapSave);
            LoadPlayer(CurrentContrrolPlayerName);
            return;
        }

        Debug.LogWarning($"未找到地图数据：{mapName}，尝试加载默认模板");

      /*  LoadAssetByLabelAndName<WorldSaveSO>(defaultSettings.Default_ADDTable, mapName, result =>
        {
            if (result != null && result.SaveData != null &&
                result.SaveData.Active_MapsData_Dict.TryGetValue(mapName, out MapSave defaultMapSave))
            {
                //InstantiateItemsFromMapSave(defaultMapSave);
                LoadPlayer(CurrentContrrolPlayerName);
            }
            else
            {
                Debug.LogError($"加载默认模板地图失败，名称：{mapName}");
            }
        });*/
    }

    /// <summary>
    /// 从地图保存数据中实例化所有物品
    /// </summary>
    /// <param name="mapSave">地图保存数据</param>
    public void InstantiateItemsFromMapSave(MapSave mapSave)
    {
        foreach (var kvp in mapSave.items)
        {
            List<ItemData> itemDataList = kvp.Value;

            foreach (ItemData forLoadItemData in itemDataList)
            {
                Item item = GameItemManager.Instance.InstantiateItem(forLoadItemData, forLoadItemData._transform.Position);
                if (item == null)
                {
                    Debug.LogError($"加载物品失败：{forLoadItemData.IDName}");
                    continue;
                }
                item.transform.rotation = forLoadItemData._transform.Rotation;
                item.transform.localScale = forLoadItemData._transform.Scale;
                item.gameObject.SetActive(true);

                if (item is ISave_Load save_Load)
                    save_Load.Load();
            }
        }
    }

    /// <summary>
    /// 通过标签和名称加载Addressable资源
    /// </summary>
    /// <typeparam name="T">资源类型</typeparam>
    /// <param name="label">标签</param>
    /// <param name="name">名称</param>
    /// <param name="onComplete">加载完成回调</param>
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

    #region 磁盘操作
    /// <summary>
    /// 将游戏存档数据保存到磁盘
    /// </summary>
    /// <param name="SaveData">存档数据</param>
    /// <param name="SavePath">保存路径</param>
    /// <param name="SaveName">保存名称</param>
    public void SaveToDisk(GameSaveData SaveData, string SavePath, string SaveName)
    {
        SaveData.saveName = SaveName;

        byte[] dataBytes = MemoryPackSerializer.Serialize(SaveData);
        File.WriteAllBytes(SavePath + SaveName + ".GameSaveData", dataBytes);
        Debug.Log("存档成功！");
    }

    /// <summary>
    /// 保存当前活动场景为地图保存数据
    /// </summary>
    /// <returns>地图保存数据</returns>
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

    /// <summary>
    /// 获取当前活动场景中所有物品的数据
    /// </summary>
    /// <returns>物品数据字典</returns>
    public Dictionary<string, List<ItemData>> GetActiveSceneAllItemData()
    {
        Dictionary<string, List<ItemData>> itemDataDict = new Dictionary<string, List<ItemData>>();
        Item[] allItems = FindObjectsOfType<Item>(includeInactive: false);

        // 先处理可保存物品
        foreach (Item item in allItems)
        {
            if (item == null)
                continue;

            item.ModuleSave();

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

        // 再收集所有活动物品数据
        foreach (Item item in allItems)
        {
            if (item == null || item.transform == null || item.gameObject == null)
                continue;

            if (!item.gameObject.activeInHierarchy)
                continue;

            item.SyncPosition();

            ItemData itemData = item.itemData;
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

    /// <summary>
    /// 从磁盘加载存档
    /// </summary>
    /// <param name="LoadSavePath">存档路径</param>
    public void LoadSaveByDisk(string LoadSavePath)
    {
        Debug.Log("开始从磁盘加载存档：" + LoadSavePath + ".GameSaveData");
        SaveData = null;
        SaveData = MemoryPackSerializer.Deserialize<GameSaveData>(File.ReadAllBytes(LoadSavePath + ".GameSaveData"));
    }

    /// <summary>
    /// 从磁盘获取存档数据
    /// </summary>
    /// <param name="savePath">存档路径</param>
    /// <param name="Load_saveName">存档名称</param>
    /// <returns>游戏存档数据</returns>
    public GameSaveData GetSaveByDisk(string savePath, string Load_saveName)
    {
        return MemoryPackSerializer.Deserialize<GameSaveData>(File.ReadAllBytes(savePath + Load_saveName + ".QAQ"));
    }

    /// <summary>
    /// 删除指定存档
    /// </summary>
    /// <param name="savePath">存档路径</param>
    /// <param name="saveName">存档名称</param>
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
    /// <summary>
    /// 删除指定存档
    /// </summary>
    /// <param name="savePath">存档路径</param>
    /// <param name="saveName">存档名称</param>
    public void DeletSave(string savePath)
    {
        string filePath = Path.Combine(savePath);
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
    #endregion

    #region 场景切换

    /// <summary>
    /// 切换到指定场景（格式为 "(x, y)" 的地图坐标）
    /// </summary>
    /// <param name="sceneName">场景名称，对应地图坐标</param>
    [Button("改变场景")]
    public void ChangeScene(string sceneName)
    {
        // 1. 检查场景名称合法性
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning("场景名称不能为空！");
            sceneName = new Vector2Int(0, 0).ToString(); // 默认切换到坐标 (0,0)
        }

        #region 新建场景

        // 2. 记录当前将要切换的场景名
        previousSceneName = sceneName;

        // 3. 创建一个新的空场景（用于后续地图加载）
        newScene = SceneManager.CreateScene(previousSceneName);

        // 4. 设置当前激活地图名称为该场景名
        SaveData.Active_MapName = previousSceneName;

        // 5. 尝试将场景名解析为地图坐标
        if (TryParseVector2Int(sceneName, out Vector2Int mapPos))
        {
            SaveData.Active_MapPos = mapPos;
        }
        else
        {
            Debug.LogWarning($"场景名称 '{sceneName}' 不符合 Vector2Int 格式");
        }

        #endregion

        #region 卸载旧场景

        // 6. 保存当前活动地图的数据
        SaveActiveMapToSaveData();

        // 7. 获取当前激活场景
        Scene previousScene = SceneManager.GetActiveScene();

        // 8. 注册卸载回调方法
        SceneManager.sceneUnloaded += OnPreviousSceneUnloaded;

        // 9. 异步卸载当前场景，卸载完成后调用 OnPreviousSceneUnloaded
        SceneManager.UnloadSceneAsync(previousScene);

        #endregion
    }

    /// <summary>
    /// 当旧场景卸载完成时执行（自动回调）
    /// </summary>
    /// <param name="unloadedScene">被卸载的场景</param>
    void OnPreviousSceneUnloaded(Scene unloadedScene)
    {
        // 1. 注销事件监听，避免重复调用
        SceneManager.sceneUnloaded -= OnPreviousSceneUnloaded;

        // 2. 通知切换开始
        OnSceneSwitchStart.Invoke();

        // 3. 设置新场景为当前激活场景
        SceneManager.SetActiveScene(newScene);

        // 4. 判断该场景是否存在于存档数据中
        bool sceneExistsInSave = SaveData.Active_MapsData_Dict.ContainsKey(newScene.name);

        if (sceneExistsInSave)
        {
            Debug.Log($"从存档加载场景: {newScene.name}");
            LoadMap(newScene.name); // 加载存档中的地图数据
        }
        else
        {
          //  Debug.Log($"创建新场景: {newScene.name}，仅添加地图核心");

            // 玩家数据存在则加载，否则创建并初始化默认位置
            if (SaveData.PlayerData_Dict.ContainsKey(CurrentContrrolPlayerName))
            {
                Item player = LoadPlayer(CurrentContrrolPlayerName);
            }
            else
            {
                Item player = LoadPlayer(CurrentContrrolPlayerName);

                // 尝试解析地图坐标
                TryParseVector2Int(newScene.name, out Vector2Int mapPos);

                // 地图格子的实际世界尺寸（单位：世界单位，例如每格宽100高120）
                int tileSizeX = 1; // 或你自己的逻辑
                int tileSizeY = 1;

                // 整个地图的大小（每个地图块内是 tileSizeX x tileSizeY）
                int mapWidth = SaveData.MapSize.x * tileSizeX;
                int mapHeight = SaveData.MapSize.y * tileSizeY;

                // 创建随机生成器（如有随机种子可加进去）
                System.Random rng = new System.Random();

                // 生成在整个地图区域内的随机坐标
                float randX = (float)rng.NextDouble() * mapWidth + SaveData.Active_MapPos.x;
                float randY = (float)rng.NextDouble() * mapHeight + SaveData.Active_MapPos.y;

                Vector3 spawnPos = new Vector3(randX, randY, 0);
                player.transform.position = spawnPos;
            }



            // 实例化地图核心物体（如地形、障碍物管理器等）
            GameItemManager.Instance.InstantiateItem("MapCore");
        }

        //将地图数据存入SaveData中
        SaveActiveMapToSaveData();
    }

    /// <summary>
    /// 尝试将 "(x,y)" 格式的字符串解析为 Vector2Int
    /// </summary>
    /// <param name="str">输入字符串</param>
    /// <param name="result">输出的 Vector2Int 结构</param>
    /// <returns>解析成功返回 true，否则返回 false</returns>
    private bool TryParseVector2Int(string str, out Vector2Int result)
    {
        result = Vector2Int.zero;

        // 移除括号和空格，例如 "(10, 20)" -> "10,20"
        string cleaned = str.Replace(" ", "").Replace("(", "").Replace(")", "");
        string[] parts = cleaned.Split(',');

        // 尝试转换为整数
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out int x) &&
            int.TryParse(parts[1], out int y))
        {
            result = new Vector2Int(x, y);
            return true;
        }

        return false;
    }

    private Scene newScene;              // 存储新创建的场景对象
    private string previousSceneName;    // 记录当前切换目标场景的名称

    #endregion
}