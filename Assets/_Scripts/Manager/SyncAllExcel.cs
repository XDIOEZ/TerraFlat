#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class SyncAllExcel
{
    [MenuItem("Tools/从Excel同步所有道具数据")]
    public static void SyncAll()
    {
        Debug.Log("开始同步所有道具数据");

        EditorUtility.DisplayProgressBar("同步道具数据", "正在准备资源，请稍候...", 0f);
        // 然后去做 GameRes.Awake()

        var gameRes = GameRes.Instance;
        gameRes.LoadResources();

        // 轮询资源是否加载完成
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

            // 开始逐帧同步
            EditorApplication.update += ProcessPrefabsStepByStep;
        }
    }

    private static void ProcessPrefabsStepByStep()
    {
        if (_currentIndex >= _prefabList.Count)
        {
            EditorApplication.update -= ProcessPrefabsStepByStep;
            EditorUtility.ClearProgressBar(); // 处理完成后清除进度条
            Debug.Log("所有道具数据同步完成！");
            CleanupResources();
            return;
        }

        var pair = _prefabList[_currentIndex];
        GameObject prefab = pair.Value;

        float progress = (float)_currentIndex / _prefabList.Count;
        EditorUtility.DisplayProgressBar("同步道具数据", $"同步 {pair.Key}...", progress);

        try
        {
            if (prefab == null)
            {
                Debug.LogWarning($"预制体 {pair.Key} 无效，跳过同步");
            }
            else if (!prefab.TryGetComponent<Item>(out Item item))
            {
                Debug.LogWarning($"预制体 {pair.Key} 未找到 Item 组件，跳过同步");
            }
            else
            {
                item.Item_Data.SyncData();
                Debug.Log($"同步成功：{pair.Key} ({_currentIndex + 1}/{_prefabList.Count})");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"同步 {pair.Key} 时发生异常：{ex.Message}\n{ex.StackTrace}");
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
