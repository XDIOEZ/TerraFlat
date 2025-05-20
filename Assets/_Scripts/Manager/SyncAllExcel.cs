#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SyncAllExcel
{
    [MenuItem("Tools/��Excelͬ�����е�������")]
    public static void SyncAll()
    {
        Debug.Log("��ʼͬ�����е�������");

        EditorUtility.DisplayProgressBar("ͬ����������", "����׼����Դ�����Ժ�...", 0f);
        // Ȼ��ȥ�� GameRes.Awake()

        var gameRes = GameRes.Instance;
        gameRes.LoadResources();

        // ��ѯ��Դ�Ƿ�������
        EditorApplication.update += CheckResourcesLoaded;
    }

    private static GameRes _gameRes;
    private static bool _isChecking = false;
    private static List<KeyValuePair<string, GameObject>> _prefabList;
    private static int _currentIndex;

    private static void CheckResourcesLoaded()
    {
        if (!_isChecking)
        {
            _isChecking = true;
            _gameRes = GameRes.Instance;
        }

        if (_gameRes.AllPrefabs != null && _gameRes.AllPrefabs.Count > 0)
        {
            EditorApplication.update -= CheckResourcesLoaded;
            _isChecking = false;

            _prefabList = new List<KeyValuePair<string, GameObject>>(_gameRes.AllPrefabs);
            _currentIndex = 0;

            // ��ʼ��֡ͬ��
            EditorApplication.update += ProcessPrefabsStepByStep;
        }
    }

    private static void ProcessPrefabsStepByStep()
    {
        if (_currentIndex >= _prefabList.Count)
        {
            EditorApplication.update -= ProcessPrefabsStepByStep;
            EditorUtility.ClearProgressBar(); // ������ɺ����������
            Debug.Log("���е�������ͬ����ɣ�");
            CleanupResources();
            return;
        }

        var pair = _prefabList[_currentIndex];
        GameObject prefab = pair.Value;

        float progress = (float)_currentIndex / _prefabList.Count;
        EditorUtility.DisplayProgressBar("ͬ����������", $"ͬ�� {pair.Key}...", progress);

        try
        {
            if (prefab == null)
            {
                Debug.LogWarning($"Ԥ���� {pair.Key} ��Ч������ͬ��");
            }
            else if (!prefab.TryGetComponent<Item>(out Item item))
            {
                Debug.LogWarning($"Ԥ���� {pair.Key} δ�ҵ� Item ���������ͬ��");
            }
            else
            {
                item.Item_Data.SyncData();
                Debug.Log($"ͬ���ɹ���{pair.Key} ({_currentIndex + 1}/{_prefabList.Count})");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ͬ�� {pair.Key} ʱ�����쳣��{ex.Message}\n{ex.StackTrace}");
        }

        _currentIndex++;
    }

    private static void CleanupResources()
    {
        if (_gameRes != null)
        {
            _gameRes = null;
        }

        Resources.UnloadUnusedAssets();
    }
}
#endif
