/*using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor.SearchService;
using UnityEngine;
using UnityEngine.SceneManagement;

[CreateAssetMenu(fileName = "New House Building", menuName = "ScriptableObjects/House Building")]
public class HouseBuildingSO : ScriptableObject
{
    [Tooltip("房屋名称")]
    public string buildingName;
    [Tooltip("房屋MapSaveData路径")]
    public string buildingPath = "Assets/Saves/HouseBuilding/";
    [Tooltip("建筑入口坐标")]
    public Vector2 buildingEntrance;

    // 缓存字段
    private MapSave _cachedMapSave;

    // 是否已加载
    private bool _isLoaded = false;

    [ShowInInspector]
    [Tooltip("房屋MapSaveData（懒加载）")]
    public MapSave MapSave
    {
        get
        {
            if (!_isLoaded)
            {
                _cachedMapSave = LoadByDisk(buildingPath);
                _isLoaded = true;
            }
            return _cachedMapSave;
        }
    }
    [Button("强制加载")]
    public void ForceLoad()
    {
        _cachedMapSave = LoadByDisk(buildingPath);
        _isLoaded = true;
    }

    public MapSave LoadByDisk(string _buildingPath)
    {
        string fullPath = Path.Combine(_buildingPath, buildingName + ".Building");
        if (File.Exists(fullPath))
        {
            try
            {
                return MemoryPackSerializer.Deserialize<MapSave>(File.ReadAllBytes(fullPath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HouseBuildingSO] 读取文件失败: {fullPath}\n{ex}");
            }
        }
        else
        {
            Debug.LogWarning($"[HouseBuildingSO] 文件不存在: {fullPath}");
        }

        return null;
    }


    [ContextMenu("获取场景中的地图参数保存到对应路径")]
    public void SaveToDisk()
    {
        SetEntranceFromWorldEdge();
        // 获取当前场景的地图数据（包含所有激活物品）
        MapSave mapSave = SaveActiveScene_Map();

        // 构建完整文件路径
        string fullPath = buildingPath + buildingName + ".Building";

        try
        {
            // 确保目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

            // 使用 MemoryPack 序列化成字节流
            byte[] bytes = MemoryPackSerializer.Serialize(mapSave);

            // 写入文件
            File.WriteAllBytes(fullPath, bytes);

            Debug.Log($"地图已成功保存至: {fullPath} \\ {mapSave.MapName}" );
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"保存地图失败: {fullPath}");
            Debug.LogException(ex);
        }
        
    }

    #region 保存当前激活的场景的地图数据
    /// <summary>
    /// 保存当前激活的场景的地图数据
    /// </summary>
    /// <param name="MapName"></param>
    /// <returns></returns>
    public MapSave SaveActiveScene_Map()
    {
        return SaveAndLoad.Instance.SaveActiveScene_Map();
    }

    #endregion
    #region 获取当前激活的场景中的所有物品数据

    /// <summary>
    /// 获取当前激活的场景中的所有物品数据
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, List<ItemData>> GetActiveSceneAllItemData()
    {

        return SaveAndLoad.Instance.GetActiveSceneAllItemData();
    }

    #endregion

    public void SetEntranceFromWorldEdge()
    {
        // 查找当前激活场景中的 WorldEdge 组件
        WorldEdge worldEdge = GameObject.FindObjectOfType<WorldEdge>();

        if (worldEdge != null)
        {
            // 设置 buildingEntrance 为 WorldEdge 的坐标
            buildingEntrance = new Vector2(worldEdge.transform.position.x, worldEdge.transform.position.y);
            Debug.Log($"建筑入口设置成功：{buildingEntrance}");
        }
        else
        {
            Debug.LogWarning("未找到场景中的 WorldEdge 组件，无法设置建筑入口。");
        }
    }

}
*/