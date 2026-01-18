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
    
    /// <summary>
    /// 获取当前活跃星球数据（快捷属性）
    /// </summary>
    public PlanetData Active_PlanetData
    {
        get => GetActivePlanetData();
    }

    /// <summary>
    /// 获取当前活跃星球数据的内部方法
    /// </summary>
    private PlanetData GetActivePlanetData()
    {
        if (SaveData?.PlanetData_Dict == null)
        {
            Debug.LogWarning("⚠️ SaveData或PlanetData_Dict为null");
            return null;
        }

        string activeSceneName = SceneManager.GetActiveScene().name;
        if (SaveData.PlanetData_Dict.TryGetValue(activeSceneName, out PlanetData planetData))
        {
            return planetData;
        }

        Debug.LogWarning($"⚠️ 未找到场景 {activeSceneName} 的星球数据");
        return null;
    }

    protected override void Awake()
    {
        base.Awake();
        DontDestroyOnLoad(gameObject); // 🔥 保证手动挂的对象也不会丢
        InitializeUserSavePath();
    }

    /// <summary>
    /// 初始化用户存档路径
    /// </summary>
    private void InitializeUserSavePath()
    {
        UserSavePath = GetDefaultSavePath();
        
        if (!Directory.Exists(UserSavePath))
        {
            Directory.CreateDirectory(UserSavePath);
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
        if (SaveData == null)
        {
            Debug.LogError("❌ SaveData为null，无法保存");
            return;
        }

        SaveToDisk(SaveData, UserSavePath, SaveData.saveName);
    }

    /// <summary>
    /// 通过区块父对象获取地图保存数据
    /// </summary>
    /// <param name="MapParent">区块对象</param>
    /// <returns>地图保存数据</returns>
    public MapSave GetMapSave_By_Parent(Chunk MapParent)
    {
        if (MapParent == null)
        {
            Debug.LogError("❌ MapParent为null");
            return null;
        }

        return new MapSave
        {
            Name = MapParent.name,
            MapPosition = new Vector2Int((int)MapParent.transform.position.x, (int)MapParent.transform.position.y),
            items = GetActiveSceneAllItemData(MapParent)
        };
    }
    
    /// <summary>
    /// 获取指定区块中所有物品的数据
    /// </summary>
    /// <param name="MapParent">区块对象</param>
    /// <returns>物品数据字典</returns>
    public Dictionary<string, HashSet<ItemData>> GetActiveSceneAllItemData(Chunk MapParent)
    {
        if (MapParent?.RunTimeItems == null)
        {
            Debug.LogWarning("⚠️ MapParent或RunTimeItems为null");
            return new Dictionary<string, HashSet<ItemData>>();
        }

        return CollectItemDataByType(MapParent.RunTimeItems.Values);
    }
    #endregion
    
    #region 磁盘操作
    /// <summary>
    /// 将游戏存档数据保存到磁盘
    /// </summary>
    /// <param name="saveData">存档数据</param>
    /// <param name="savePath">保存路径</param>
    /// <param name="saveName">保存名称</param>
    public void SaveToDisk(GameSaveData saveData, string savePath, string saveName)
    {
        if (saveData == null)
        {
            Debug.LogError("❌ saveData为null，无法保存");
            return;
        }

        try
        {
            saveData.saveName = saveName;

            // 确保保存路径存在
            EnsureDirectoryExists(savePath);

            string fullPath = GetSaveFilePath(savePath, saveName);
            byte[] dataBytes = MemoryPackSerializer.Serialize(saveData);
            File.WriteAllBytes(fullPath, dataBytes);

            Debug.Log($"✅ 存档成功！路径: {fullPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ 保存存档失败: {ex.Message}");
            Debug.LogException(ex);
        }
    }

    /// <summary>
    /// 获取当前活动场景的地图保存数据（静态版本）
    /// </summary>
    /// <returns>地图保存数据</returns>
    public static MapSave GetCurrentMapStatic()
    {
        return new MapSave
        {
            Name = SceneManager.GetActiveScene().name,
            items = GetActiveSceneAllItemData_Static()
        };
    }

    public PlanetData GetCurrentPlanetData()
    {
        string activeSceneName = SceneManager.GetActiveScene().name;
        if (SaveData.PlanetData_Dict.TryGetValue(activeSceneName, out PlanetData planetData))
        {
            return planetData;
        }
        return null;
    }
    
    /// <summary>
    /// 获取当前活动场景中所有物品的数据（静态版本）
    /// </summary>
    /// <returns>物品数据字典</returns>
    public static Dictionary<string, HashSet<ItemData>> GetActiveSceneAllItemData_Static()
    {
        Item[] allItems = FindObjectsOfType<Item>(includeInactive: false);
        
        // 先处理所有物品保存
        SaveAllItems(allItems);
        
        // 再收集所有活动物品数据
        return CollectActiveItemData(allItems);
    }

    /// <summary>
    /// 保存所有物品数据
    /// </summary>
    private static void SaveAllItems(Item[] items)
    {
        foreach (Item item in items)
        {
            if (item == null) continue;

            try
            {
                item.ModuleSave();
            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ 保存物品失败: {item.name}", item);
                Debug.LogException(ex);
            }
        }
    }

    /// <summary>
    /// 收集活跃物品数据
    /// </summary>
    private static Dictionary<string, HashSet<ItemData>> CollectActiveItemData(Item[] items)
    {
        Dictionary<string, HashSet<ItemData>> itemDataDict = new Dictionary<string, HashSet<ItemData>>();

        foreach (Item item in items)
        {
            if (item == null || item.transform == null || item.gameObject == null)
                continue;

            if (!item.gameObject.activeInHierarchy)
                continue;

            ItemData itemData = item.itemData;
            if (itemData == null)
                continue;

            if (!itemDataDict.TryGetValue(itemData.IDName, out HashSet<ItemData> set))
            {
                set = new HashSet<ItemData>();
                itemDataDict[itemData.IDName] = set;
            }

            set.Add(itemData);
        }

        return itemDataDict;
    }

    /// <summary>
    /// 从磁盘加载存档
    /// </summary>
    /// <param name="loadSavePath">存档路径</param>
    public void LoadSaveByDisk(string loadSavePath)
    {
        if (string.IsNullOrEmpty(loadSavePath))
        {
            Debug.LogError("❌ 存档路径为空");
            return;
        }

        try
        {
            string fullPath = NormalizeSavePath(loadSavePath);
            
            if (!File.Exists(fullPath))
            {
                Debug.LogWarning($"⚠️ 存档文件不存在：{fullPath}");
                return;
            }

            Debug.Log($"📖 开始加载存档：{fullPath}");
            SaveData = MemoryPackSerializer.Deserialize<GameSaveData>(File.ReadAllBytes(fullPath));
            Debug.Log($"✅ 存档加载成功");
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ 加载存档失败: {ex.Message}");
            Debug.LogException(ex);
        }
    }

    /// <summary>
    /// 删除指定存档
    /// </summary>
    /// <param name="savePath">存档路径</param>
    /// <param name="saveName">存档名称</param>
    public void DeleteSave(string savePath, string saveName)
    {
        string fullPath = GetSaveFilePath(savePath, saveName);
        DeleteSaveFile(fullPath);
    }
    
    /// <summary>
    /// 删除指定存档文件（已废弃，使用DeleteSave）
    /// </summary>
    /// <param name="fullPath">完整的存档文件路径</param>
    [System.Obsolete("使用 DeleteSave(path, name) 代替", false)]
    public void DeletSave(string fullPath)
    {
        DeleteSaveFile(fullPath);
    }

    /// <summary>
    /// 删除存档文件的内部方法
    /// </summary>
    private void DeleteSaveFile(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            Debug.LogError("❌ 存档路径为空");
            return;
        }

        try
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                Debug.Log($"✅ 存档已删除：{fullPath}");
            }
            else
            {
                Debug.LogWarning($"⚠️ 未找到要删除的存档文件：{fullPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"❌ 删除存档失败：{fullPath}");
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
        return GetSaveFilePath(UserSavePath, saveName);
    }

    /// <summary>
    /// 构建存档文件路径
    /// </summary>
    private string GetSaveFilePath(string basePath, string saveName)
    {
        return Path.Combine(basePath, saveName + ".bytes");
    }

    /// <summary>
    /// 规范化存档路径（添加.bytes扩展名）
    /// </summary>
    private string NormalizeSavePath(string path)
    {
        return path.EndsWith(".bytes") ? path : path + ".bytes";
    }

    /// <summary>
    /// 确保目录存在
    /// </summary>
    private void EnsureDirectoryExists(string path)
    {
        string directory = Path.GetDirectoryName(path);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// 通用的物品数据收集方法
    /// </summary>
    private Dictionary<string, HashSet<ItemData>> CollectItemDataByType(IEnumerable<Item> items)
    {
        Dictionary<string, HashSet<ItemData>> itemDataDict = new Dictionary<string, HashSet<ItemData>>();

        // 先保存所有物品
        foreach (Item item in items)
        {
            if (item == null) continue;

            try
            {
                item.ModuleSave();
            }
            catch (Exception ex)
            {
                Debug.LogError($"❌ 保存物品失败: {item.name}", item);
                Debug.LogException(ex);
            }
        }

        // 再收集物品数据
        foreach (Item item in items)
        {
            if (item == null) continue;

            ItemData itemData = item.itemData;
            if (itemData == null) continue;

            if (!itemDataDict.TryGetValue(itemData.IDName, out HashSet<ItemData> set))
            {
                set = new HashSet<ItemData>();
                itemDataDict[itemData.IDName] = set;
            }

            set.Add(itemData);
        }

        return itemDataDict;
    }
    
    #endregion
}