using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using MemoryPack;
using Sirenix.OdinInspector;

/// <summary>
/// 游戏存档与加载系统，负责管理游戏数据的保存和加载功能
/// </summary>
public class SaveDataMgr : SingletonAutoMono<SaveDataMgr>
{
    #region 存档配置
    [Tooltip("玩家的存档路径")]
    public string UserSavePath = ""; // 初始化为空，将在Awake中设置

    [Tooltip("当前使用的存档数据")]
    public GameSaveData SaveData;

    [Tooltip("当前控制的玩家名称")]
    public string CurrentContrrolPlayerName;
    
    public PlanetData Active_PlanetData
    {
        get
        {
            if (SaveDataMgr.Instance == null)
            {
                Debug.LogError("SaveDataManager.Instance is null.");
                return null;
            }

            if (SaveDataMgr.Instance.SaveData == null)
            {
                Debug.LogError("SaveDataManager.Instance.SaveData is null.");
                return null;
            }

            if (SaveDataMgr.Instance.SaveData.PlanetData_Dict == null)
            {
                Debug.LogError("SaveDataManager.Instance.SaveData.PlanetData_Dict is null.");
                return null;
            }

            string activeSceneName = SceneManager.GetActiveScene().name;
            if (SaveDataMgr.Instance.SaveData.PlanetData_Dict.TryGetValue(activeSceneName, out PlanetData planetData))
            {
                return planetData;
            }
            else
            {
               // Debug.LogError($"No PlanetData found for scene: {activeSceneName}");
                return null;
            }
        }
    }

    protected override void Awake()
    {
        base.Awake();
        DontDestroyOnLoad(gameObject); // 🔥 保证手动挂的对象也不会丢
        // 使用Application.persistentDataPath作为基础存档路径
        UserSavePath = Path.Combine(Application.persistentDataPath, "Saves", "LocalSaveData") + Path.DirectorySeparatorChar;
        
        // 确保存档目录存在
        string directory = Path.GetDirectoryName(UserSavePath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    #endregion

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
    /// 保存当前活动场景为地图保存数据
    /// </summary>
    /// <returns>地图保存数据</returns>
    public MapSave GetMapSave_By_Parent(Chunk MapParent)
    {
        MapSave worldSave = new MapSave();
      
        worldSave.items = GetActiveSceneAllItemData(MapParent);

        worldSave.Name = MapParent.name;

        worldSave.MapPosition = new Vector2Int((int)MapParent.transform.position.x, (int)MapParent.transform.position.y);

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
            item.ModuleSave();
        }

        foreach (Item item in MapParent.RunTimeItems.Values)
        {
            if (item == null)
                continue;
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

        // 确保保存路径存在
        string directory = Path.GetDirectoryName(SavePath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        byte[] dataBytes = MemoryPackSerializer.Serialize(SaveData);

        string fullPath = Path.Combine(SavePath, SaveName + ".bytes");
        File.WriteAllBytes(fullPath, dataBytes);

        Debug.Log("存档成功！路径: " + fullPath);
    }

    /// <summary>
    /// 保存当前活动场景为地图保存数据
    /// </summary>
    /// <returns>地图保存数据</returns>
    public static MapSave GetCurrentMapStatic()
    {
        MapSave worldSave = new MapSave();
        worldSave.Name = SceneManager.GetActiveScene().name;
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
           //         saveableItem.Save();
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
        string fullPath = LoadSavePath;
        if (!LoadSavePath.EndsWith(".bytes"))
        {
            fullPath = LoadSavePath + ".bytes";
        }
        
        Debug.Log("开始从磁盘加载存档：" + fullPath);
        
        if (File.Exists(fullPath))
        {
            SaveData = null;
            SaveData = MemoryPackSerializer.Deserialize<GameSaveData>(File.ReadAllBytes(fullPath));
        }
        else
        {
            Debug.LogWarning("存档文件不存在：" + fullPath);
        }
    }

    /// <summary>
    /// 删除指定存档
    /// </summary>
    /// <param name="savePath">存档路径</param>
    /// <param name="saveName">存档名称</param>
    public void DeletSave(string savePath, string saveName)
    {
        string filePath = Path.Combine(savePath, saveName + ".bytes");
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
    /// 删除指定存档文件
    /// </summary>
    /// <param name="fullPath">完整的存档文件路径</param>
    public void DeletSave(string fullPath)
    {
        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                Debug.Log($"存档已删除：{fullPath}");
            }
            else
            {
                Debug.LogWarning($"未找到要删除的存档文件：{fullPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"删除存档时发生错误：{fullPath}");
            Debug.LogException(ex);
        }
    }
    
    /// <summary>
    /// 获取默认存档路径
    /// </summary>
    /// <returns>默认存档路径</returns>
    public static string GetDefaultSavePath()
    {
        return Path.Combine(Application.persistentDataPath, "Saves", "LocalSaveData") + Path.DirectorySeparatorChar;
    }
    
    /// <summary>
    /// 获取存档文件完整路径
    /// </summary>
    /// <param name="saveName">存档名称</param>
    /// <returns>完整路径</returns>
    public string GetFullSavePath(string saveName)
    {
        return Path.Combine(UserSavePath, saveName + ".bytes");
    }
    
    #endregion
}