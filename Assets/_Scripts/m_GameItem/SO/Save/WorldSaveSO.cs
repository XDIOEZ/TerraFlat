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

    [Tooltip("��������·��������� Assets �ļ��У�")]
    public string sceneRelativePath = "_Scenes/Scene";

#if UNITY_EDITOR
    [ContextMenu("�Զ�����")]
    public void AutoLoad()
    {
        if (buildingName == "")
        {
            buildingName = name;
        }
        string sceneAssetPath = Path.Combine("Assets", sceneRelativePath, buildingName + ".unity");
        string sceneName = buildingName; // ��������ʱ����

#if UNITY_EDITOR
        // �༭���У�ʹ��·���򿪳���
        if (UnityEditor.AssetDatabase.LoadAssetAtPath<SceneAsset>(sceneAssetPath) != null)
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(sceneAssetPath);
            Debug.Log($"[WorldSaveSO] �༭�����Ѵ򿪳���: {sceneAssetPath}");
        }
        else
        {
            Debug.LogWarning($"[WorldSaveSO] �Ҳ��������ļ�: {sceneAssetPath}");
        }
#else
    // ����ʱ�������Ƽ��أ���������ӵ� Build Settings��
    if (Application.CanStreamedLevelBeLoaded(sceneName))
    {
        SceneManager.LoadScene(sceneName);
        Debug.Log($"[WorldSaveSO] ����ʱ�Ѽ��س���: {sceneName}");
    }
    else
    {
        Debug.LogWarning($"[WorldSaveSO] ����ʱ�޷����س�����δ���� Build Settings����: {sceneName}");
    }
#endif

        // ���ض�Ӧ�Ĵ浵����
        SaveToDisk();

        if (buildingEntrance == Vector2.zero)
        {
            // ��ȡ�����е� WorldEdge ���
            WorldEdge edge = FindFirstObjectByType<WorldEdge>();
            if (edge != null)
            {
                Vector2 pos = edge.transform.position;

                // ����λ���Զ�ƫ�ƣ�������ҳ����ڱ߽�������
                // ����Ը���ʵ�ʱ߽�ߴ��������߼�
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
                Debug.LogWarning("δ�ҵ� WorldEdge ������޷��Զ����� buildingEntrance��");
            }
        }
    }
#endif

    // �����ֶ�
    private GameSaveData _cachedMapSave;
#if UNITY_EDITOR
    [MenuItem("Tools/ģ���ͼ����_ͬ��")]


    public static void SyncAllMap()
    {
        // ��¼��ǰ�����·��
        string activeScenePath = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().path;

        string[] guids = AssetDatabase.FindAssets("t:WorldSaveSO", new[] { "Assets/_Scenes" });

        if (guids.Length == 0)
        {
            Debug.LogWarning("δ�ҵ��κ� WorldSaveSO ��Դ");
            return;
        }

        Debug.Log($"��ʼͬ�� {guids.Length} ����ͼ����...");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<WorldSaveSO>(path);
            if (so != null)
            {
                Debug.Log($"ͬ����ͼ����: {so.name} ({path})");
                so.AutoLoad(); // �����Զ����أ��������� + �浵��
            }
        }

        Debug.Log("���е�ͼ����ͬ����ɣ�");

        // ͬ����ɺ��л�ԭ����
        if (!string.IsNullOrEmpty(activeScenePath) && File.Exists(activeScenePath))
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(activeScenePath);
            Debug.Log($"���л�ԭ����: {activeScenePath}");
        }
        else
        {
            Debug.LogWarning("�޷��л�ԭ������·����Ч���ļ�������: " + activeScenePath);
        }
    }

#endif

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
