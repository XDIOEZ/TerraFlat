#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SyncAllExcel
{
    [MenuItem("Tools/��Excelͬ�����е�������")]
    public static void SyncAll()
    {
        Debug.Log("��ʼͬ�����е�������");
        var gameRes = GameRes.Instance;
        gameRes.Awake();

        // ��ʼ��ѯ�����Դ�Ƿ�������
        EditorApplication.update += CheckResourcesLoaded;
    }

    private static GameRes _gameRes;
    private static bool _isChecking = false;

    private static void CheckResourcesLoaded()
    {
        if (!_isChecking)
        {
            _isChecking = true;
            _gameRes = GameRes.Instance;
        }

        // �����Դ�Ƿ�������
        if (_gameRes.AllPrefabs != null && _gameRes.AllPrefabs.Count > 0)
        {
            EditorApplication.update -= CheckResourcesLoaded;
            _isChecking = false;

            // ����Ԥ������߼�
            int prefabCount = _gameRes.AllPrefabs.Count;
            int processedCount = 0;

            foreach (var pair in _gameRes.AllPrefabs)
            {
                GameObject prefab = pair.Value;
                if (prefab == null)
                {
                    Debug.LogWarning($"Ԥ���� {pair.Key} ��Ч������ͬ��");
                    continue;
                }

                if (!prefab.TryGetComponent<Item>(out Item item))
                {
                    Debug.LogWarning($"Ԥ���� {pair.Key} δ�ҵ� Item ���������ͬ��");
                    continue;
                }

                item.Item_Data.SyncData();
                processedCount++;
                Debug.Log($"ͬ�����ȣ�{processedCount}/{prefabCount} ��ɣ�{pair.Key}��");
            }

            Debug.Log("���е�������ͬ����ɣ�");

            // **����������̬��Դ**
            CleanupResources();
        }
    }

    private static void CleanupResources()
    {
        // 1. ���� GameRes ʵ���е���Դ����
        if (_gameRes != null)
        {
            _gameRes = null; // �ͷŵ������ã��������
        }

        // 2. ж��δʹ�õ���Դ���첽������
        Resources.UnloadUnusedAssets();
    }
}
#endif