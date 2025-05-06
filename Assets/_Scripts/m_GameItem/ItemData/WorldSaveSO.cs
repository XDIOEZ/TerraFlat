using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

[CreateAssetMenu(fileName = "New WorldSaveSO", menuName = "ScriptableObjects/����浵")]
public class WorldSaveSO : ScriptableObject
{
    [Tooltip("��������")]
    public string buildingName;
    [Tooltip("����MapSaveData·��")]
    public string buildingPath = "Assets/Saves/GameSaveData/";
    [Tooltip("�����������")]
    public Vector2 buildingEntrance;

    // �����ֶ�
    private GameSaveData _cachedMapSave;

    // �Ƿ��Ѽ���
    private bool _isLoaded = false;

    [ShowInInspector]
    [Tooltip("����MapSaveData�������أ�")]
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

    [Button("ǿ�Ƽ���")]
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
        // ��ȡ��ǰ�����ĵ�ͼ���ݣ��������м�����Ʒ��
        GameSaveData mapSave = SaveActiveScene_Map();

        // ���������ļ�·��
        string fullPath = buildingPath + buildingName + ".GameSaveData";

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
    public GameSaveData SaveActiveScene_Map()
    {
        return SaveAndLoad.Instance.GetSaveData();
    }
    #endregion
}
