using Force.DeepCloner;
using MemoryPack;
using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[CreateAssetMenu(fileName = "New WorldSaveSO", menuName = "ScriptableObjects/����浵")]
public class WorldSaveSO : ScriptableObject
{
    [Tooltip("��������")]
    public string buildingName;

    [Tooltip("����MapSaveData·��")]
    public string buildingPath = "Assets/Saves/GameSaveData/";

    [Tooltip("�����������")]
    public Vector2 buildingEntrance;

    [Tooltip("���ݽṹ�汾�ţ������������ݣ�")]
    public int dataVersion = 1;

    // �����ֶ�
    private GameSaveData _cachedMapSave;

    #region ����ӿ�

    [ShowInInspector]
    [Tooltip("����MapSaveData�������أ�")]
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

    [Button("ǿ�Ƽ���")]
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
                Debug.LogError($"[WorldSaveSO] ��ȡ�ļ�ʧ��: {fullPath}\n{ex}");
            }
        }
        else
        {
            Debug.LogWarning($"[WorldSaveSO] �ļ�������: {fullPath}");
        }

        return null;
    }

    [ContextMenu("��ȡ�����еĵ�ͼ�������浽��Ӧ·��")]
    public void SaveToDisk()
    {
        GameSaveData mapSave = SaveActiveScene_Map();
        string fullPath = Path.Combine(buildingPath, buildingName + ".GameSaveData");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            byte[] bytes = MemoryPackSerializer.Serialize(mapSave);
            File.WriteAllBytes(fullPath, bytes);

            // �������»���
            _cachedMapSave = mapSave;

            Debug.Log($"��ͼ�ѳɹ�������: {fullPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"�����ͼʧ��: {fullPath}");
            Debug.LogException(ex);
        }
    }

    [Button("�򿪴浵Ŀ¼")]
    public void OpenSaveFolder()
    {
        string folderPath = Path.Combine(buildingPath);
        if (Directory.Exists(folderPath))
        {
            Application.OpenURL("file://" + folderPath);
        }
        else
        {
            Debug.LogWarning($"Ŀ¼������: {folderPath}");
        }
    }

    #region ���浱ǰ����ĳ����ĵ�ͼ����

    /// <summary>
    /// ���浱ǰ����ĳ����ĵ�ͼ����
    /// </summary>
    public GameSaveData SaveActiveScene_Map()
    {
        return SaveAndLoad.Instance.GetSaveData();
    }

    #endregion
}
