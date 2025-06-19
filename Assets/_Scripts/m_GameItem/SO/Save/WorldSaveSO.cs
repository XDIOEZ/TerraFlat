using Force.DeepCloner;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

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
    public string sceneRelativePath = "_Scenes/Scene";

#if UNITY_EDITOR
    [ContextMenu("自动加载")]
    public void AutoLoad()
    {
        if (buildingName == "")
        {
            buildingName = name;
        }
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

        if (buildingEntrance == Vector2.zero)
        {
            // 获取场景中的 WorldEdge 组件
            WorldEdge edge = FindFirstObjectByType<WorldEdge>();
            if (edge != null)
            {
                Vector2 pos = edge.transform.position;

                // 根据位置自动偏移，避免玩家出生在边界物体上
                // 你可以根据实际边界尺寸调整这个逻辑
                if (pos.x < 0)
                    pos.x += 1f;
                else
                    pos.x -= 1f;

                if (pos.y < 0)
                    pos.y += 1f;
                else
                    pos.y -= 1f;

                buildingEntrance = pos;
            }
            else
            {
                Debug.LogWarning("未找到 WorldEdge 组件，无法自动设置 buildingEntrance！");
            }
        }
    }
#endif

    // 缓存字段
    private GameSaveData _cachedMapSave;
#if UNITY_EDITOR
    [MenuItem("Tools/模板地图数据_同步")]


    public static void SyncAllMap()
    {
        // 记录当前激活场景路径
        string activeScenePath = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;

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

        // 同步完成后切回原场景
        if (!string.IsNullOrEmpty(activeScenePath) && File.Exists(activeScenePath))
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(activeScenePath);
            Debug.Log($"已切回原场景: {activeScenePath}");
        }
        else
        {
            Debug.LogWarning("无法切回原场景，路径无效或文件不存在: " + activeScenePath);
        }
    }

#endif

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
        if (buildingName == "")
        {
            buildingName = name;
        }
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
