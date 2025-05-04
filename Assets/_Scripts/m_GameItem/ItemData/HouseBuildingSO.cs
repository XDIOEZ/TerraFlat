using MemoryPack;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

[CreateAssetMenu(fileName = "New House Building", menuName = "ScriptableObjects/House Building")]
public class HouseBuildingSO : ScriptableObject
{
    [Tooltip("��������")]
    public string buildingName;
    [Tooltip("����MapSaveData·��")]
    public string buildingPath = "Assets/Saves/HouseBuilding/";
    [Tooltip("����MapSaveData")]
    public MapSave MapSave
    {
        get
        {
            return LoadByDisk(buildingPath);
        }
    }
    [Tooltip("�����������")]
    public Vector2 buildingEntrance;

    public MapSave LoadByDisk(string _buildingPath)
    {
        return MemoryPackSerializer.Deserialize<MapSave>
             (File.ReadAllBytes(_buildingPath + buildingName + ".Building"));
    }

    [ContextMenu("��ȡ�����еĵ�ͼ�������浽��Ӧ·��")]
    public void SaveToDisk()
    {
        // ��ȡ��ǰ�����ĵ�ͼ���ݣ��������м�����Ʒ��
        MapSave mapSave = SaveActiveScene_Map();

        // ���������ļ�·��
        string fullPath = buildingPath + buildingName + ".Building";

        try
        {
            // ȷ��Ŀ¼����
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

            // ʹ�� MemoryPack ���л����ֽ���
            byte[] bytes = MemoryPackSerializer.Serialize(mapSave);

            // д���ļ�
            File.WriteAllBytes(fullPath, bytes);

            Debug.Log($"��ͼ�ѳɹ�������: {fullPath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"�����ͼʧ��: {fullPath}");
            Debug.LogException(ex);
        }
    }

    #region ���浱ǰ����ĳ����ĵ�ͼ����
    /// <summary>
    /// ���浱ǰ����ĳ����ĵ�ͼ����
    /// </summary>
    /// <param name="MapName"></param>
    /// <returns></returns>
    public MapSave SaveActiveScene_Map()
    {
        return SaveAndLoad.Instance.SaveActiveScene_Map();
    }

    #endregion
    #region ��ȡ��ǰ����ĳ����е�������Ʒ����

    /// <summary>
    /// ��ȡ��ǰ����ĳ����е�������Ʒ����
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, List<ItemData>> GetActiveSceneAllItemData()
    {

        return SaveAndLoad.Instance.GetActiveSceneAllItemData();
    }

    #endregion

}
