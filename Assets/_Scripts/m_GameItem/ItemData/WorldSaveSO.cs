using Force.DeepCloner;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.IO;
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

    // 缓存字段
    private GameSaveData _cachedMapSave;

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
