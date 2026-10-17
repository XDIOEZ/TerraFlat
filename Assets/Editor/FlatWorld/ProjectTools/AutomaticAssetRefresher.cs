#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Unity.Profiling;
using Stopwatch = System.Diagnostics.Stopwatch;

/// <summary>
/// 监听 Assets 目录变化，并在主线程静默后合并刷新请求。
/// 后台线程不调用 Unity API，最终资源刷新始终由 Editor 主线程执行。
/// </summary>
[InitializeOnLoad]
public static class AutomaticAssetRefresher
{
    #region 常量与状态

    // 文件连续写入时等待一秒静默，再合并成一次资源刷新。
    private static readonly long DebounceIntervalTicks = Stopwatch.Frequency;
    private static readonly ProfilerMarker RefreshMarker = new("FlatWorld.Editor.AutoAssetRefresh");

    // EditorPrefs 中保存自动刷新开关的键。
    private const string EditorPrefKey = "FlatWorld_AutoAssetRefresh_Enabled";

    // 保护监听器与后台线程的创建和释放。
    private static readonly object LifecycleLock = new object();

    // 以整数保存开关，便于跨线程原子读取。
    private static int enabledState;

    // 标记主线程需要执行资源刷新。
    private static int mainThreadRefreshPending;

    private static long lastFileChangeTimestamp;

    // PlayMode 内禁止脚本域重载，避免 Ctrl+R/自动 Refresh 把运行中的世界模型和地形表现拆散。
    private static bool playModeAssemblyReloadLocked;

    // 监听 Assets 目录的文件变化。
    private static FileSystemWatcher watcher;

    // Windows 直接等待系统通知，避免 Mono 在匿名线程递归扫描整个 Assets。
    private static IDisposable nativeWatcher;
    private static string pendingWatcherWarning;

    #endregion

    #region 初始化与菜单

    /// <summary>
    /// 初始化自动刷新服务并绑定 Editor 生命周期。
    /// </summary>
    static AutomaticAssetRefresher()
    {
        bool enabled = EditorPrefs.GetBool(EditorPrefKey, true);
        Volatile.Write(ref enabledState, enabled ? 1 : 0);

        if (enabled)
        {
            StartWatching();
        }

        EditorApplication.update += OnEditorUpdate;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
        EditorApplication.quitting += Shutdown;

        if (EditorApplication.isPlaying)
        {
            LockAssemblyReloadForPlayMode();
        }
    }

    /// <summary>
    /// 切换自动刷新开关。
    /// </summary>
    [MenuItem("Tools/Auto Asset Refresh/Enable Auto Refresh")]
    public static void ToggleAutoRefreshMenu()
    {
        bool enabled = !IsEnabled();
        SetEnabled(enabled);
        Debug.Log($"[AutoAssetRefresh] 自动刷新已{(enabled ? "启用" : "禁用")}");
    }

    /// <summary>
    /// 同步菜单勾选状态。
    /// </summary>
    [MenuItem("Tools/Auto Asset Refresh/Enable Auto Refresh", true)]
    public static bool ToggleAutoRefreshMenuValidate()
    {
        Menu.SetChecked("Tools/Auto Asset Refresh/Enable Auto Refresh", IsEnabled());
        return true;
    }

    /// <summary>
    /// 返回当前自动刷新是否启用。
    /// </summary>
    private static bool IsEnabled()
    {
        return Volatile.Read(ref enabledState) == 1;
    }

    /// <summary>
    /// 保存开关并启动或停止目录通知。
    /// </summary>
    private static void SetEnabled(bool enabled)
    {
        EditorPrefs.SetBool(EditorPrefKey, enabled);
        Volatile.Write(ref enabledState, enabled ? 1 : 0);

        if (enabled)
        {
            StartWatching();
            return;
        }

        StopWatching();
    }

    /// <summary>
    /// 在 Unity 主线程处理后台线程提交的刷新请求。
    /// </summary>
    private static void OnEditorUpdate()
    {
        string warning = Interlocked.Exchange(ref pendingWatcherWarning, null);
        if (warning != null)
            Debug.LogWarning("[AutoAssetRefresh] " + warning);

        if (!IsEnabled() || Volatile.Read(ref mainThreadRefreshPending) == 0)
        {
            return;
        }

        // 游玩和模式切换期间只保留请求，回到编辑模式后再导入资源。
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating || !HasDebounceElapsed())
        {
            return;
        }

        if (Interlocked.Exchange(ref mainThreadRefreshPending, 0) == 0)
            return;

        using (RefreshMarker.Auto())
            AssetDatabase.Refresh();
    }

    /// <summary>
    /// 停止后台资源并解除 Editor 生命周期订阅。
    /// </summary>
    private static void Shutdown()
    {
        UnlockAssemblyReloadForPlayMode();
        StopWatching();
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
        EditorApplication.quitting -= Shutdown;
    }

    /// <summary>
    /// PlayMode 中积累自动刷新请求，C# 程序集只在退出 PlayMode 后统一重载。
    /// </summary>
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            LockAssemblyReloadForPlayMode();
            return;
        }

        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
        {
            UnlockAssemblyReloadForPlayMode();
        }
    }

    private static void LockAssemblyReloadForPlayMode()
    {
        if (playModeAssemblyReloadLocked)
        {
            return;
        }

        EditorApplication.LockReloadAssemblies();
        playModeAssemblyReloadLocked = true;
        Debug.Log("[AutoAssetRefresh] PlayMode 已锁定脚本域重载；自动资源刷新和脚本改动会在退出 PlayMode 后应用。");
    }

    private static void UnlockAssemblyReloadForPlayMode()
    {
        if (!playModeAssemblyReloadLocked)
        {
            return;
        }

        playModeAssemblyReloadLocked = false;
        EditorApplication.UnlockReloadAssemblies();
    }

    #endregion

    #region 文件通知生命周期

    /// <summary>启动事件式监听，文件变化直接合并到主线程刷新请求。</summary>
    private static void StartWatching()
    {
        lock (LifecycleLock)
        {
            if (nativeWatcher != null || watcher != null)
                return;

#if UNITY_EDITOR_WIN
            nativeWatcher = new WindowsAssetChangeWatcher(Application.dataPath,
                OnNativeFileChanged, ReportWatcherError);
#else
            FileSystemWatcher createdWatcher = CreateWatcher(Application.dataPath);
            // Mono 的兼容后端会全库轮询；明确提示改用编辑器自身刷新，避免静默退回高开销扫描。
            var backendField = typeof(FileSystemWatcher).GetField("watcher",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            object backend = backendField?.GetValue(createdWatcher);
            if (backend?.GetType().FullName == "System.IO.DefaultWatcher")
            {
                DisposeWatcher(createdWatcher);
                ReportWatcherError("当前 Mono 只支持扫描式文件监听，已停止此高开销后端；请使用 Unity 自带自动刷新或手动刷新。");
                return;
            }
            watcher = createdWatcher;
            createdWatcher.EnableRaisingEvents = true;
#endif
        }
    }

    /// <summary>停止监听并清除尚未消费的请求。</summary>
    private static void StopWatching()
    {
        IDisposable nativeToDispose;
        FileSystemWatcher watcherToDispose;
        lock (LifecycleLock)
        {
            nativeToDispose = nativeWatcher;
            watcherToDispose = watcher;
            nativeWatcher = null;
            watcher = null;
        }

        nativeToDispose?.Dispose();
        DisposeWatcher(watcherToDispose);
        Interlocked.Exchange(ref mainThreadRefreshPending, 0);
    }

    private static void OnNativeFileChanged(string path)
    {
        if (path == null)
            RequestRefresh();
        else
            RequestRefresh(path);
    }

    private static void ReportWatcherError(string message)
    {
        Interlocked.Exchange(ref pendingWatcherWarning, message);
    }

    #endregion

    #region 文件变化监听

    /// <summary>
    /// 创建只负责报告 Assets 目录变化的文件监听器。
    /// </summary>
    private static FileSystemWatcher CreateWatcher(string assetsPath)
    {
        FileSystemWatcher createdWatcher = new FileSystemWatcher(assetsPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            Filter = "*.*"
        };

        createdWatcher.Changed += OnFileChanged;
        createdWatcher.Created += OnFileChanged;
        createdWatcher.Deleted += OnFileChanged;
        createdWatcher.Renamed += OnFileRenamed;
        return createdWatcher;
    }

    /// <summary>
    /// 释放文件监听器及其事件订阅。
    /// </summary>
    private static void DisposeWatcher(FileSystemWatcher watcherToDispose)
    {
        if (watcherToDispose == null)
        {
            return;
        }

        watcherToDispose.EnableRaisingEvents = false;
        watcherToDispose.Changed -= OnFileChanged;
        watcherToDispose.Created -= OnFileChanged;
        watcherToDispose.Deleted -= OnFileChanged;
        watcherToDispose.Renamed -= OnFileRenamed;
        watcherToDispose.Dispose();
    }

    /// <summary>
    /// 记录普通文件变化，等待主线程静默合并。
    /// </summary>
    private static void OnFileChanged(object sender, FileSystemEventArgs eventArgs)
    {
        RequestRefresh(eventArgs.FullPath);
    }

    /// <summary>
    /// 记录文件重命名，等待主线程静默合并。
    /// </summary>
    private static void OnFileRenamed(object sender, RenamedEventArgs eventArgs)
    {
        RequestRefresh(eventArgs.FullPath);
    }

    /// <summary>
    /// 提交一次不绑定具体路径的资源刷新请求。
    /// </summary>
    public static void RequestRefresh()
    {
        if (!IsEnabled())
        {
            return;
        }

        Interlocked.Exchange(ref lastFileChangeTimestamp, Stopwatch.GetTimestamp());
        Interlocked.Exchange(ref mainThreadRefreshPending, 1);
    }

    private static bool HasDebounceElapsed()
    {
        return Stopwatch.GetTimestamp() - Interlocked.Read(ref lastFileChangeTimestamp) >= DebounceIntervalTicks;
    }

    /// <summary>
    /// 过滤无关文件后提交资源刷新请求。
    /// </summary>
    private static void RequestRefresh(string fullPath)
    {
        if (IsIgnoredPath(fullPath))
        {
            return;
        }

        RequestRefresh();
    }

    /// <summary>
    /// 判断文件变化是否不需要触发 Unity 资源刷新。
    /// </summary>
    private static bool IsIgnoredPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return true;
        }

        string fileName = Path.GetFileName(fullPath);
        return fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".sln", StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}

#endif
