using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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

    // 缓存字段
    private GameSaveData _cachedMapSave;

    // 是否已加载
    private bool _isLoaded = false;

    [ShowInInspector]
    [Tooltip("房屋MapSaveData（懒加载）")]
    public GameSaveData SaveData
    {
        get
        {
            if (_cachedMapSave ==null)
            {
                _cachedMapSave = LoadByDisk(buildingPath);
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
        // 获取当前场景的地图数据（包含所有激活物品）
        GameSaveData mapSave = SaveActiveScene_Map();

        // 构建完整文件路径
        string fullPath = buildingPath + buildingName + ".GameSaveData";

        try
        {
            // 确保目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

            // 使用 MemoryPack 序列化成字节流
            byte[] bytes = MemoryPackSerializer.Serialize(mapSave);

            // 写入文件
            File.WriteAllBytes(fullPath, bytes);

            Debug.Log($"地图已成功保存至: {fullPath}");
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
    public GameSaveData SaveActiveScene_Map()
    {
        return SaveAndLoad.Instance.GetSaveData();
    }
    #endregion
}
