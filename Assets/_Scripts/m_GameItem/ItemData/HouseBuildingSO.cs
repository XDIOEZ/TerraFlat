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
    [Tooltip("��������")]
    public string buildingName;
    [Tooltip("����MapSaveData·��")]
    public string buildingPath = "Assets/Saves/HouseBuilding/";
    [Tooltip("�����������")]
    public Vector2 buildingEntrance;

    // �����ֶ�
    private MapSave _cachedMapSave;

    // �Ƿ��Ѽ���
    private bool _isLoaded = false;

    [ShowInInspector]
    [Tooltip("����MapSaveData�������أ�")]
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
    [Button("ǿ�Ƽ���")]
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
                Debug.LogError($"[HouseBuildingSO] ��ȡ�ļ�ʧ��: {fullPath}\n{ex}");
            }
        }
        else
        {
            Debug.LogWarning($"[HouseBuildingSO] �ļ�������: {fullPath}");
        }

        return null;
    }


    [ContextMenu("��ȡ�����еĵ�ͼ�������浽��Ӧ·��")]
    public void SaveToDisk()
    {
        SetEntranceFromWorldEdge();
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

            Debug.Log($"��ͼ�ѳɹ�������: {fullPath} \\ {mapSave.MapName}" );
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

    public void SetEntranceFromWorldEdge()
    {
        // ���ҵ�ǰ������е� WorldEdge ���
        WorldEdge worldEdge = GameObject.FindObjectOfType<WorldEdge>();

        if (worldEdge != null)
        {
            // ���� buildingEntrance Ϊ WorldEdge ������
            buildingEntrance = new Vector2(worldEdge.transform.position.x, worldEdge.transform.position.y);
            Debug.Log($"����������óɹ���{buildingEntrance}");
        }
        else
        {
            Debug.LogWarning("δ�ҵ������е� WorldEdge ������޷����ý�����ڡ�");
        }
    }

}
*/