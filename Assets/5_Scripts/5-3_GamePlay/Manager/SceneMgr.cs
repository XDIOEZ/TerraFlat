using System;
using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 场景管理器单例
/// 负责场景的加载、卸载、切换等操作，并提供事件化接口
/// 跨场景持久化，支持异步加载与协程管理
/// </summary>
public class SceneMgr : SingletonAutoMono<SceneMgr>
{
    #region 事件定义

    /// <summary>
    /// 场景加载开始时触发
    /// </summary>
    public UltEvent<string> OnSceneLoadStart = new(); // 场景名称

    /// <summary>
    /// 场景加载进度变化时触发（0-1）
    /// </summary>
    public UltEvent<float> OnSceneLoadProgress = new(); // 进度百分比

    /// <summary>
    /// 场景加载完成时触发
    /// </summary>
    public UltEvent<string> OnSceneLoadComplete = new(); // 场景名称

    /// <summary>
    /// 场景卸载开始时触发
    /// </summary>
    public UltEvent<string> OnSceneUnloadStart = new(); // 场景名称

    /// <summary>
    /// 场景卸载完成时触发
    /// </summary>
    public UltEvent<string> OnSceneUnloadComplete = new(); // 场景名称

    /// <summary>
    /// 场景切换完成时触发
    /// </summary>
    public UltEvent<string, string> OnSceneSwitch = new(); // 从场景名, 到场景名

    #endregion

    #region 字段

    // 当前活跃场景缓存
    private string _currentActiveSceneName;

    // 异步加载操作缓存：避免重复加载同一场景
    private Dictionary<string, AsyncOperation> _loadingOperations = new();

    // 已加载场景集合
    private HashSet<string> _loadedScenes = new();

    // 当前正在执行的协程集合（便于场景切换时统一停止）
    private HashSet<Coroutine> _activeCoroutines = new();

    // 加载参数缓存
    private LoadSceneMode _defaultLoadMode = LoadSceneMode.Single;

    #endregion

    #region Unity生命周期

    protected override void Awake()
    {
        base.Awake(); // 调用父类 Awake，防止多个实例
        DontDestroyOnLoad(gameObject);
        _currentActiveSceneName = SceneManager.GetActiveScene().name;
        _loadedScenes.Add(_currentActiveSceneName);
        Debug.Log($"[SceneMgr] 初始化完成，当前场景: {_currentActiveSceneName}");
    }

    private void OnDestroy()
    {
        // 清理所有正在运行的协程
        foreach (var coroutine in _activeCoroutines)
        {
            if (coroutine != null)
                StopCoroutine(coroutine);
        }
        _activeCoroutines.Clear();
    }

    #endregion

    #region 同步加载与卸载

    /// <summary>
    /// 同步加载场景（会卸载当前场景）
    /// </summary>
    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            throw new ArgumentNullException(nameof(sceneName), "场景名称不能为空");
        }

        OnSceneLoadStart?.Invoke(sceneName);
        string previousScene = _currentActiveSceneName;

        try
        {
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            _currentActiveSceneName = sceneName;
            _loadedScenes.Clear();
            _loadedScenes.Add(sceneName);
            _loadingOperations.Clear();

            OnSceneLoadComplete?.Invoke(sceneName);
            OnSceneSwitch?.Invoke(previousScene, sceneName);

            Debug.Log($"[SceneMgr] 场景切换完成: {previousScene} -> {sceneName}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SceneMgr] 场景加载失败: {sceneName}, 错误: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 同步加载场景（可附加到当前场景）
    /// </summary>
    public void LoadSceneAdditive(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            throw new ArgumentNullException(nameof(sceneName), "场景名称不能为空");
        }

        if (_loadedScenes.Contains(sceneName))
        {
            Debug.LogWarning($"[SceneMgr] 场景已加载，跳过重复加载: {sceneName}");
            return;
        }

        OnSceneLoadStart?.Invoke(sceneName);

        try
        {
            SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
            _loadedScenes.Add(sceneName);

            OnSceneLoadComplete?.Invoke(sceneName);
            Debug.Log($"[SceneMgr] 附加场景加载完成: {sceneName}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SceneMgr] 附加场景加载失败: {sceneName}, 错误: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 卸载指定场景
    /// </summary>
    public void UnloadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            throw new ArgumentNullException(nameof(sceneName), "场景名称不能为空");
        }

        if (!_loadedScenes.Contains(sceneName))
        {
            Debug.LogWarning($"[SceneMgr] 场景未被加载，无法卸载: {sceneName}");
            return;
        }

        OnSceneUnloadStart?.Invoke(sceneName);

        try
        {
            SceneManager.UnloadSceneAsync(sceneName);
            _loadedScenes.Remove(sceneName);

            // 更新当前活跃场景
            if (_currentActiveSceneName == sceneName)
            {
                _currentActiveSceneName = SceneManager.GetActiveScene().name;
            }

            OnSceneUnloadComplete?.Invoke(sceneName);
            Debug.Log($"[SceneMgr] 场景卸载完成: {sceneName}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SceneMgr] 场景卸载失败: {sceneName}, 错误: {ex.Message}");
            throw;
        }
    }

    #endregion

    #region 异步加载

    /// <summary>
    /// 异步加载场景（会卸载当前场景）
    /// </summary>
    public Coroutine LoadSceneAsync(string sceneName, Action onComplete = null)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            throw new ArgumentNullException(nameof(sceneName), "场景名称不能为空");
        }

        var coroutine = StartCoroutine(LoadSceneAsyncInternal(sceneName, LoadSceneMode.Single, onComplete));
        _activeCoroutines.Add(coroutine);
        return coroutine;
    }

    /// <summary>
    /// 异步加载场景（可附加到当前场景）
    /// </summary>
    public Coroutine LoadSceneAsyncAdditive(string sceneName, Action onComplete = null)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            throw new ArgumentNullException(nameof(sceneName), "场景名称不能为空");
        }

        if (_loadedScenes.Contains(sceneName))
        {
            Debug.LogWarning($"[SceneMgr] 场景已加载，跳过重复加载: {sceneName}");
            onComplete?.Invoke();
            return null;
        }

        var coroutine = StartCoroutine(LoadSceneAsyncInternal(sceneName, LoadSceneMode.Additive, onComplete));
        _activeCoroutines.Add(coroutine);
        return coroutine;
    }

    /// <summary>
    /// 异步卸载场景
    /// </summary>
    public Coroutine UnloadSceneAsync(string sceneName, Action onComplete = null)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            throw new ArgumentNullException(nameof(sceneName), "场景名称不能为空");
        }

        if (!_loadedScenes.Contains(sceneName))
        {
            Debug.LogWarning($"[SceneMgr] 场景未被加载，无法卸载: {sceneName}");
            onComplete?.Invoke();
            return null;
        }

        var coroutine = StartCoroutine(UnloadSceneAsyncInternal(sceneName, onComplete));
        _activeCoroutines.Add(coroutine);
        return coroutine;
    }

    #endregion

    #region 协程实现

    private IEnumerator LoadSceneAsyncInternal(string sceneName, LoadSceneMode mode, Action onComplete)
    {
        OnSceneLoadStart?.Invoke(sceneName);
        string previousScene = _currentActiveSceneName;

        // 检查是否已有该场景的加载操作
        if (_loadingOperations.ContainsKey(sceneName))
        {
            Debug.LogWarning($"[SceneMgr] 场景加载已在进行中: {sceneName}");
            yield break;
        }

        AsyncOperation asyncOp = SceneManager.LoadSceneAsync(sceneName, mode);
        _loadingOperations[sceneName] = asyncOp;

        while (!asyncOp.isDone)
        {
            float progress = asyncOp.progress;
            OnSceneLoadProgress?.Invoke(progress);
            yield return null;
        }

        _loadingOperations.Remove(sceneName);
        _loadedScenes.Add(sceneName);

        if (mode == LoadSceneMode.Single)
        {
            _currentActiveSceneName = sceneName;
            OnSceneSwitch?.Invoke(previousScene, sceneName);
            Debug.Log($"[SceneMgr] 异步场景切换完成: {previousScene} -> {sceneName}");
        }
        else
        {
            Debug.Log($"[SceneMgr] 异步附加场景加载完成: {sceneName}");
        }

        OnSceneLoadComplete?.Invoke(sceneName);
        onComplete?.Invoke();

        _activeCoroutines.Remove(StartCoroutine(LoadSceneAsyncInternal(sceneName, mode, onComplete)));
    }

    private IEnumerator UnloadSceneAsyncInternal(string sceneName, Action onComplete)
    {
        OnSceneUnloadStart?.Invoke(sceneName);

        AsyncOperation asyncOp = SceneManager.UnloadSceneAsync(sceneName);

        while (!asyncOp.isDone)
        {
            yield return null;
        }

        _loadedScenes.Remove(sceneName);

        if (_currentActiveSceneName == sceneName)
        {
            _currentActiveSceneName = SceneManager.GetActiveScene().name;
        }

        OnSceneUnloadComplete?.Invoke(sceneName);
        onComplete?.Invoke();

        Debug.Log($"[SceneMgr] 异步场景卸载完成: {sceneName}");
    }

    #endregion

    #region 查询接口

    /// <summary>
    /// 获取当前活跃场景名称
    /// </summary>
    public string GetActiveSceneName()
    {
        return _currentActiveSceneName;
    }

    /// <summary>
    /// 获取当前活跃场景对象
    /// </summary>
    public Scene GetActiveScene()
    {
        return SceneManager.GetActiveScene();
    }

    /// <summary>
    /// 检查指定场景是否已加载
    /// </summary>
    public bool IsSceneLoaded(string sceneName)
    {
        return _loadedScenes.Contains(sceneName);
    }

    /// <summary>
    /// 获取所有已加载的场景名称列表
    /// </summary>
    public List<string> GetLoadedScenes()
    {
        return new List<string>(_loadedScenes);
    }

    /// <summary>
    /// 检查指定场景是否正在加载中
    /// </summary>
    public bool IsSceneLoading(string sceneName)
    {
        return _loadingOperations.ContainsKey(sceneName);
    }

    /// <summary>
    /// 获取指定场景的加载进度（0-1）
    /// </summary>
    public float GetSceneLoadProgress(string sceneName)
    {
        if (_loadingOperations.TryGetValue(sceneName, out var asyncOp))
        {
            return asyncOp.progress;
        }
        return _loadedScenes.Contains(sceneName) ? 1f : 0f;
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 停止指定的加载协程
    /// </summary>
    public void CancelSceneLoad(string sceneName)
    {
        // Note: Unity 的 AsyncOperation 无法直接取消，但我们可以清理缓存
        if (_loadingOperations.ContainsKey(sceneName))
        {
            _loadingOperations.Remove(sceneName);
            Debug.Log($"[SceneMgr] 已取消场景加载: {sceneName}");
        }
    }

    /// <summary>
    /// 清理所有活跃协程
    /// </summary>
    public void ClearAllCoroutines()
    {
        foreach (var coroutine in _activeCoroutines)
        {
            if (coroutine != null)
                StopCoroutine(coroutine);
        }
        _activeCoroutines.Clear();
        Debug.Log("[SceneMgr] 已清理所有协程");
    }

    #endregion
}
