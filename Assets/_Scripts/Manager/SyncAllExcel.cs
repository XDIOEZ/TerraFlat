#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SyncAllExcel
{
    [MenuItem("Tools/从Excel同步所有道具数据")]
    public static void SyncAll()
    {
        Debug.Log("开始同步所有道具数据");
        var gameRes = GameRes.Instance;
        gameRes.Awake();

        // 开始轮询检查资源是否加载完成
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

        // 检查资源是否加载完成
        if (_gameRes.AllPrefabs != null && _gameRes.AllPrefabs.Count > 0)
        {
            EditorApplication.update -= CheckResourcesLoaded;
            _isChecking = false;

            // 处理预制体的逻辑
            int prefabCount = _gameRes.AllPrefabs.Count;
            int processedCount = 0;

            foreach (var pair in _gameRes.AllPrefabs)
            {
                GameObject prefab = pair.Value;
                if (prefab == null)
                {
                    Debug.LogWarning($"预制体 {pair.Key} 无效，跳过同步");
                    continue;
                }

                if (!prefab.TryGetComponent<Item>(out Item item))
                {
                    Debug.LogWarning($"预制体 {pair.Key} 未找到 Item 组件，跳过同步");
                    continue;
                }

                item.Item_Data.SyncData();
                processedCount++;
                Debug.Log($"同步进度：{processedCount}/{prefabCount} 完成（{pair.Key}）");
            }

            Debug.Log("所有道具数据同步完成！");

            // **新增：清理静态资源**
            CleanupResources();
        }
    }

    private static void CleanupResources()
    {
        // 1. 清理 GameRes 实例中的资源引用
        if (_gameRes != null)
        {
            _gameRes = null; // 释放单例引用（如果允许）
        }

        // 2. 卸载未使用的资源（异步操作）
        Resources.UnloadUnusedAssets();
    }
}
#endif