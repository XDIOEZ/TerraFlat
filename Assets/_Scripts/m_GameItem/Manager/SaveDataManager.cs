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
public class SaveDataManager : SingletonAutoMono<SaveDataManager>
{
    #region 存档配置
    [Tooltip("玩家的存档路径")]
    public string UserSavePath = "Assets/Saves/LoaclSaveData/";

    [Tooltip("当前使用的存档数据")]
    public GameSaveData SaveData;

    [Tooltip("当前控制的玩家名称")]
    public string CurrentContrrolPlayerName;

    #endregion

    private void Start()
    {
        DontDestroyOnLoad(gameObject);
    }


    #region 保存功能


    /// <summary>
    /// 保存当前游戏状态到磁盘
    /// </summary>
    [Button("保存存档到磁盘上")]
    public void Save_And_WriteToDisk()
    {
        SaveToDisk(SaveData, UserSavePath, SaveData.saveName);
    }

    /// <summary>
    /// 将当前活动场景保存到存档数据中
    /// </summary>
    [Tooltip("保存当前激活场景存入当前的场景字典中")]
    public void SaveChunk_To_SaveData(Chunk MapParent)
    {
        SaveData ??= new GameSaveData();

        MapSave mapSave = GetMapSave_By_Parent(MapParent);

        SaveData.Active_MapsData_Dict[mapSave.MapName] = mapSave;
    }

    /// <summary>
    /// 保存当前活动场景为地图保存数据
    /// </summary>
    /// <returns>地图保存数据</returns>
    public MapSave GetMapSave_By_Parent(Chunk MapParent)
    {
        MapSave worldSave = new MapSave();
      
        worldSave.items = GetActiveSceneAllItemData(MapParent);

        worldSave.MapName = MapParent.name;

        worldSave.MapPosition = MapParent.transform.position;

        return worldSave;
    }
    /// <summary>
    /// 获取当前活动场景中所有物品的数据
    /// </summary>
    /// <returns>物品数据字典</returns>
    public Dictionary<string, List<ItemData>> GetActiveSceneAllItemData(Chunk MapParent)
    {
        Dictionary<string, List<ItemData>> itemDataDict = new Dictionary<string, List<ItemData>>();

        // 先处理可保存物品
        foreach (Item item in MapParent.RunTimeItems.Values)
        {
            if (item == null)
                continue;

            item.ModuleSave();

            if (item is ISave_Load saveableItem)
            {
              saveableItem.Save();
            }
        }

        foreach (Item item in MapParent.RunTimeItems.Values)
        {
            if (item == null)
                continue;
            //同步当前位置
            item.SyncPosition();

            ItemData itemData = item.itemData;

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

/*    #region 玩家相关
*//*
    /// <summary>
    /// 加载指定名称的玩家
    /// </summary>
    /// <param name="playerName">玩家名称</param>
    /// <returns>加载的玩家实例</returns>
    [Button("加载玩家")]
    public Player LoadPlayer(string playerName)
    {
        Debug.Log("开始加载玩家");
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
        Player player = CreatePlayer(playerData);
        GameItemManager.Instance.Player_DIC[playerName] = player;
        return player;
    }
    /// <summary>
    /// 根据玩家数据创建玩家实例
    /// </summary>
    /// <param name="data">玩家数据</param>
    /// <returns>创建的玩家实例</returns>
    private Player CreatePlayer(Data_Player data)
    {
        GameObject newPlayerObj = GameItemManager.Instance.InstantiateItem(data).gameObject;

        // ✅ 将父对象设置为空（放到场景根节点下）
        newPlayerObj.transform.SetParent(null, true);

        Player newPlayer = newPlayerObj.GetComponentInChildren<Player>();
        newPlayer.Load();
        return newPlayer;
    }*/

/*    /// <summary>
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
            //GameObject_False.Add(player.gameObject);
            playerCount++;
        }

        return playerCount;
    }*//*
    #endregion*/
 /*   #region 加载地图
*//*
    /// <summary>
    /// 加载指定名称的地图
    /// </summary>
    /// <param name="mapName">地图名称</param>
    /// 

    [Button("加载指定地图")]
    public void LoadMap(string mapName)
    {
        if (SaveData.Active_MapsData_Dict.TryGetValue(mapName, out MapSave mapSave))
        {
            Debug.Log($"成功获取地图：{mapName}");

            Load_MapSave(mapSave);
            return;
        }

        Debug.LogWarning($"未找到地图数据：{mapName}，尝试加载默认模板");
        CreatNewMap(mapName);
    }*//*
    /// <summary>
    /// 创建地图对象并注册到ItemParentDIC
    /// </summary>
    /// <param name="mapName">地图名</param>
    /// <param name="position">地图位置</param>
    /// <returns>新建的Map GameObject及其ChunkManager</returns>
    private (GameObject mapObj, Chunk chunkManager) CreateMapBase(string mapName, Vector3 position)
    {
        // 1. 创建地图根物体
        GameObject newMapObj = new GameObject(mapName);

        // 2. 添加区块管理器
        Chunk chunkManager = newMapObj.AddComponent<Chunk>();

        // 3. 设置位置
        newMapObj.transform.position = position;

        // 4. 注册到管理器
        GameChunkManager.Instance.Chunk_Dic[mapName] = chunkManager;

        return (newMapObj, chunkManager);
    }

    public void Load_MapSave(MapSave mapSave)
    {
        // 1. 清理空物品父物体
        GameItemManager.Instance.CleanEmptyParents();

        // 2. 通过公共方法创建地图对象
        var (newMapObj, chunkManager) = CreateMapBase(mapSave.MapName, mapSave.MapPosition);

        // 3. 加载物品
        foreach (var kvp in mapSave.items)
        {
            foreach (ItemData forLoadItemData in kvp.Value)
            {
                Item item = GameItemManager.Instance.InstantiateItem
                    (forLoadItemData, forLoadItemData._transform.Position, default, default, newMapObj);
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
        chunkManager.Init();
    }

    public void CreatNewMap(string mapName)
    {
        TryParseVector2Int(mapName, out Vector2Int pos);
        var (newMapObj, chunkManager) = CreateMapBase(mapName, new Vector3(pos.x, pos.y, 0));

        // 实例化地图核心物体
        Map map = GameItemManager.Instance.InstantiateItem("MapCore", default, default, default, newMapObj) as Map;
        map.ParentObject = newMapObj;
        map.Act();

        // 初始化区块管理器
        chunkManager.Init();
    }

    #endregion*/


/*
    #region 通过MapSave切换场景的方法

    private MapSave targetMap; // 新增字段，保存要切换的 MapSave
    [Button("切换指定地图")]
    public void ChangeTOMapByMapSave(MapSave map)
    {
        #region 新建场景

        // 2. 记录当前将要切换的场景名
        previousSceneName = map.MapName;

        // 3. 创建一个新的空场景（用于后续地图加载）
        newScene = SceneManager.CreateScene(map.MapName);

        // 4. 设置当前激活地图名称为该场景名
        SaveData.Active_MapName = previousSceneName;

        targetMap = map;
        #endregion

        #region 卸载旧场景

        // 7. 获取当前激活场景
        Scene previousScene = SceneManager.GetActiveScene();

        // 8. 注册卸载回调方法
        SceneManager.sceneUnloaded += SaveMap_SceneUnloaded;

        // 9. 异步卸载当前场景，卸载完成后调用 OnPreviousSceneUnloaded
        SceneManager.UnloadSceneAsync(previousScene);

        #endregion
    }
    /// <summary>
    /// 当旧场景卸载完成时执行（自动回调）
    /// </summary>
    /// <param name="unloadedScene">被卸载的场景</param>
    void SaveMap_SceneUnloaded(Scene unloadedScene)
    {
       
        // 1. 注销事件监听，避免重复调用
        SceneManager.sceneUnloaded -= SaveMap_SceneUnloaded;

        // 2. 通知切换开始
        OnSceneSwitchStart.Invoke();

        // 3. 设置新场景为当前激活场景
        SceneManager.SetActiveScene(newScene);

        Load_MapSave(targetMap);

        LoadPlayer(CurrentContrrolPlayerName);

        targetMap = null;
    }
    #endregion
*/
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
    public static MapSave GetCurrentMapStatic()
    {
        MapSave worldSave = new MapSave();
        worldSave.MapName = SceneManager.GetActiveScene().name;
        worldSave.items = GetActiveSceneAllItemData_Static();

        return worldSave;
    }
    /// <summary>
    /// 获取当前活动场景中所有物品的数据
    /// </summary>
    /// <returns>物品数据字典</returns>
    public static Dictionary<string, List<ItemData>> GetActiveSceneAllItemData_Static()
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

    /*#region 场景切换*/

/*    /// <summary>
    /// 切换到指定场景（格式为 "(x, y)" 的地图坐标）
    /// </summary>
    /// <param name="NewSceneName">场景名称，对应地图坐标</param>
    [Button("改变场景")]
    public void ChangeScene(string NewSceneName,GameObject LastScene)
    {

        // 1. 检查场景名称是否合法
        if (string.IsNullOrEmpty(NewSceneName))
        {
            Debug.LogWarning("场景名称不能为空！");
            // 默认切换到 (0,0)
            NewSceneName = new Vector2Int(0, 0).ToString();
        }

        // 2. 如果上一个场景在字典中，先将其设为非激活
        if (GameChunkManager.Instance.Chunk_Dic.TryGetValue(SaveData.Active_MapName, out Chunk LastmapObj))
        {
            GameChunkManager.Instance.SetChunkActive(LastmapObj, false);
        }

        // 3. 修改当前激活的场景名称
        SaveData.Active_MapName = NewSceneName;

        // 4. 判断是否已经处于 "GameScene" 场景中
        if (SceneManager.GetActiveScene().name != "GameScene")
        {
            // 注册场景加载完成后的回调方法
            SceneManager.sceneLoaded += OnScene_Loaded;

            // 切换到 GameScene（统一的游戏主场景）
            SceneManager.LoadScene("GameScene");

            // 加载 GameScene 后，再次进入本方法逻辑
            return;
        }

        // 5. 通知外部：场景切换开始
        OnSceneSwitchStart.Invoke();

        // 6. 判断存档中是否有该地图数据
        bool sceneExistsInSave = SaveData.Active_MapsData_Dict.ContainsKey(SaveData.Active_MapName);

        // 7. 检查 GameManager 中是否已经存在对应的地图对象
        if (GameChunkManager.Instance.Chunk_Dic.TryGetValue(NewSceneName, out Chunk mapObj))
        {
            // 如果对象存在但处于未激活状态，则激活它
            if (!mapObj.gameObject.activeSelf)
            {
                GameChunkManager.Instance.SetChunkActive(LastmapObj, true);
            }
            // 地图已存在，不需要再创建或加载
            return;
        }

        // 8. 如果场景在存档中存在，则加载存档数据
        if (sceneExistsInSave)
        {
            Debug.Log($"从存档加载场景: {SaveData.Active_MapName}");
            LoadMap(SaveData.Active_MapName);
        }
        else
        {
            // 否则创建一个新的地图场景
            CreatNewMap(SaveData.Active_MapName);
        }

        // 9. 通知外部：场景切换结束
        OnSceneSwitchEnd.Invoke();
    }*/


/*    [Button]
    public void ClearFarAwayChunks()
    {
        if (GameChunkManager.Instance.Chunk_Dic == null || GameChunkManager.Instance.Chunk_Dic.Count == 0)
            return;

        Transform player = GameItemManager.Instance.Player_DIC.Values.FirstOrDefault().transform;
        if (player == null)
        {
            Debug.LogWarning("未找到 Player，无法清理远处区块！");
            return;
        }

        Vector2 playerPos = player.position;
        float maxDistance = Distance;

        List<string> toRemove = new List<string>();

        foreach (var kvp in GameChunkManager.Instance.Chunk_Dic)
        {
            Chunk chunk = kvp.Value;
            if (chunk == null) continue;

            float distance = Vector2.Distance(playerPos, chunk.transform.position);

            if (distance > maxDistance)
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (string key in toRemove)
        {
            Chunk chunk = GameChunkManager.Instance.Chunk_Dic[key];
            if (chunk != null)
            {
                // ✅ 用你自己的销毁方法
                GameChunkManager.Instance.DestroyChunk(chunk);
            }

            GameChunkManager.Instance.Chunk_Dic.Remove(key);
        }

        if (toRemove.Count > 0)
            Debug.Log($"清理了 {toRemove.Count} 个远离玩家的区块（调用 DestroyMap）");
    }*/



/*    // Update the method signature of OnPreviousSceneUnloaded to match the UnityAction<Scene, LoadSceneMode> delegate.
    void OnScene_Loaded(Scene unloadedScene, LoadSceneMode mode)
    {
        // 1. 注销事件监听，避免重复调用
        SceneManager.sceneLoaded -= OnScene_Loaded;

        // 2. 通知切换开始
        OnSceneSwitchStart.Invoke();

        // 4. 判断该场景是否存在于存档数据中
        bool sceneExistsInSave = SaveData.Active_MapsData_Dict.ContainsKey(SaveData.Active_MapName);


        if (sceneExistsInSave)
        {

            LoadPlayer(CurrentContrrolPlayerName);
            Debug.Log($"从存档加载场景: {SaveData.Active_MapName}");
            LoadMap(SaveData.Active_MapName); // 加载存档中的地图数据
        }
        else
        {
            // 玩家数据存在则加载，否则创建并初始化默认位置
            if (SaveData.PlayerData_Dict.ContainsKey(CurrentContrrolPlayerName))
            {
                Item player = LoadPlayer(CurrentContrrolPlayerName);
            }
            else
            {
                Item player = LoadPlayer(CurrentContrrolPlayerName);

                // 尝试解析地图坐标
                TryParseVector2Int(SaveData.Active_MapName, out Vector2Int mapPos);

                // 地图格子的实际世界尺寸（单位：世界单位，例如每格宽100高120）
                int tileSizeX = 1; // 或你自己的逻辑
                int tileSizeY = 1;

                // 整个地图的大小（每个地图块内是 tileSizeX x tileSizeY）
                int mapWidth = SaveData.ChunkSize.x * tileSizeX;
                int mapHeight = SaveData.ChunkSize.y * tileSizeY;

                // 创建随机生成器（如有随机种子可加进去）
                System.Random rng = new System.Random();

                // 生成在整个地图区域内的随机坐标
                float randX = (float)rng.NextDouble() * mapWidth + SaveData.Active_MapPos.x;
                float randY = (float)rng.NextDouble() * mapHeight + SaveData.Active_MapPos.y;

                Vector3 spawnPos = new Vector3(randX, randY, 0);
                player.transform.position = spawnPos;
            }
            CreatNewMap(SaveData.Active_MapName);
        }
    }
*//*
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
    #endregion*/
}