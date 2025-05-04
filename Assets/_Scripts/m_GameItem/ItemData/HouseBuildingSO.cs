using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

[CreateAssetMenu(fileName = "New House Building", menuName = "ScriptableObjects/House Building")]
public class HouseBuildingSO : ScriptableObject
{
    [Tooltip("房屋名称")]
    public string buildingName;
    [Tooltip("房屋MapSaveData路径")]
    public string buildingPath = "Assets/Saves/HouseBuilding/";
    [Tooltip("房屋MapSaveData")]
    public MapSave MapSave
    {
        get
        {
            return LoadByDisk(buildingPath);
        }
    }
    [Tooltip("建筑入口坐标")]
    public Vector2 buildingEntrance;

    public MapSave LoadByDisk(string _buildingPath)
    {
        return MemoryPackSerializer.Deserialize<MapSave>
             (File.ReadAllBytes(_buildingPath + buildingName + ".Building"));
    }

    [ContextMenu("获取场景中的地图参数保存到对应路径")]
    public void SaveToDisk()
    {
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

}
