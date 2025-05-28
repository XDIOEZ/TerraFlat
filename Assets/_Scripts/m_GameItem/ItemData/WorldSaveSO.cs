using Force.DeepCloner;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

[CreateAssetMenu(fileName = "New WorldSaveSO", menuName = "ScriptableObjects/世界存档")]
public class WorldSaveSO : ScriptableObject
{
    [Tooltip("房屋名称")]
    public string buildingName;

    [Tooltip("房屋MapSaveData路径")]
    public string buildingPath = "Assets/Saves/GameSaveData/";

    [Tooltip("建筑入口坐标")]
    public Vector2 buildingEntrance;

    [Tooltip("数据结构版本号（用于升级兼容）")]
    public int dataVersion = 1;

    [Tooltip("场景所在路径（相对于 Assets 文件夹）")]
    public string sceneRelativePath = "_Scenes/Scene_Template";
    [ContextMenu("自动加载")]
    public void AutoLoad()
    {
        string sceneAssetPath = Path.Combine("Assets", sceneRelativePath, buildingName + ".unity");
        string sceneName = buildingName; // 用于运行时加载

#if UNITY_EDITOR
        // 编辑器中：使用路径打开场景
        if (UnityEditor.AssetDatabase.LoadAssetAtPath<SceneAsset>(sceneAssetPath) != null)
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(sceneAssetPath);
            Debug.Log($"[WorldSaveSO] 编辑器中已打开场景: {sceneAssetPath}");
        }
        else
        {
            Debug.LogWarning($"[WorldSaveSO] 找不到场景文件: {sceneAssetPath}");
        }
#else
    // 运行时：用名称加载（场景必须加到 Build Settings）
    if (Application.CanStreamedLevelBeLoaded(sceneName))
    {
        SceneManager.LoadScene(sceneName);
        Debug.Log($"[WorldSaveSO] 运行时已加载场景: {sceneName}");
    }
    else
    {
        Debug.LogWarning($"[WorldSaveSO] 运行时无法加载场景（未加入 Build Settings？）: {sceneName}");
    }
#endif

        // 加载对应的存档数据
        SaveToDisk();
    }

    // 缓存字段
    private GameSaveData _cachedMapSave;

    [MenuItem("Tools/同步所有地图数据")]
    public static void SyncAllMap()
    {
        string[] guids = AssetDatabase.FindAssets("t:WorldSaveSO", new[] { "Assets/_Scenes" });

        if (guids.Length == 0)
        {
            Debug.LogWarning("未找到任何 WorldSaveSO 资源");
            return;
        }

        Debug.Log($"开始同步 {guids.Length} 个地图数据...");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<WorldSaveSO>(path);
            if (so != null)
            {
                Debug.Log($"同步地图数据: {so.name} ({path})");
                so.AutoLoad(); // 调用自动加载（包括场景 + 存档）
            }
        }

        Debug.Log("所有地图数据同步完成！");
    }

    #region 对外接口

    [ShowInInspector]
    [Tooltip("房屋MapSaveData（懒加载）")]
    public GameSaveData SaveData
    {
        get
        {
            if(_cachedMapSave == null)
            {
                _cachedMapSave = LoadByDisk(buildingPath);
            }
            if (_cachedMapSave.MapSaves_Dict.Count <= 0|| _cachedMapSave.PlayerData_Dict.Count <= 0)
            {
                _cachedMapSave = LoadByDisk(buildingPath);
            }
            return _cachedMapSave?.DeepClone();
        }
    }

    #endregion

    [Button("强制加载")]
    public void ForceLoad()
    {
        _cachedMapSave = LoadByDisk(buildingPath);
    }

    public GameSaveData LoadByDisk(string _buildingPath)
    {
        string fullPath = Path.Combine(_buildingPath, buildingName + ".GameSaveData");
        if (File.Exists(fullPath))
        {
            try
            {
                return MemoryPackSerializer.Deserialize<GameSaveData>(File.ReadAllBytes(fullPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WorldSaveSO] 读取文件失败: {fullPath}\n{ex}");
            }
        }
        else
        {
            Debug.LogWarning($"[WorldSaveSO] 文件不存在: {fullPath}");
        }

        return null;
    }

    [ContextMenu("获取场景中的地图参数保存到对应路径")]
    public void SaveToDisk()
    {
        GameSaveData mapSave = SaveActiveScene_Map();
        string fullPath = Path.Combine(buildingPath, buildingName + ".GameSaveData");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            byte[] bytes = MemoryPackSerializer.Serialize(mapSave);
            File.WriteAllBytes(fullPath, bytes);

            // 保存后更新缓存
            _cachedMapSave = mapSave;

            Debug.Log($"地图已成功保存至: {fullPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"保存地图失败: {fullPath}");
            Debug.LogException(ex);
        }
    }

    [Button("打开存档目录")]
    public void OpenSaveFolder()
    {
        string folderPath = Path.Combine(buildingPath);
        if (Directory.Exists(folderPath))
        {
            Application.OpenURL("file://" + folderPath);
        }
        else
        {
            Debug.LogWarning($"目录不存在: {folderPath}");
        }
    }

    #region 保存当前激活的场景的地图数据

    /// <summary>
    /// 保存当前激活的场景的地图数据
    /// </summary>
    public GameSaveData SaveActiveScene_Map()
    {
        return SaveAndLoad.Instance.GetSaveData();
    }

    #endregion
}
