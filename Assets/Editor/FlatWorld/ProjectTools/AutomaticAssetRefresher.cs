#if UNITY_EDITOR
using System;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 监听 Assets 目录变化，并由后台线程按固定间隔合并刷新请求。
/// 后台线程不调用 Unity API，最终资源刷新始终由 Editor 主线程执行。
/// </summary>
[InitializeOnLoad]
public static class AutomaticAssetRefresher
{
    #region 常量与状态

    // 后台线程检查文件变化的固定间隔。
    private const int PollingIntervalMilliseconds = 500;

    // 程序集重载或退出时等待后台线程结束的最长时间。
    private const int WorkerJoinTimeoutMilliseconds = 2000;

    // EditorPrefs 中保存自动刷新开关的键。
    private const string EditorPrefKey = "FlatWorld_AutoAssetRefresh_Enabled";

    // 保护监听器与后台线程的创建和释放。
    private static readonly object LifecycleLock = new object();

    // 以整数保存开关，便于跨线程原子读取。
    private static int enabledState;

    // 标记文件系统已经发生变化。
    private static int fileChangesPending;

    // 标记主线程需要执行资源刷新。
    private static int mainThreadRefreshPending;

    // 监听 Assets 目录的文件变化。
    private static FileSystemWatcher watcher;

    // 按固定间隔合并变化的后台线程。
    private static Thread pollingThread;

    // 通知后台线程停止轮询。
    private static ManualResetEvent pollingStopSignal;

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
            StartPolling();
        }

        EditorApplication.update += OnEditorUpdate;
        AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
        EditorApplication.quitting += Shutdown;
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
    /// 保存开关并启动或停止后台轮询。
    /// </summary>
    private static void SetEnabled(bool enabled)
    {
        EditorPrefs.SetBool(EditorPrefKey, enabled);
        Volatile.Write(ref enabledState, enabled ? 1 : 0);

        if (enabled)
        {
            StartPolling();
            return;
        }

        StopPolling();
    }

    /// <summary>
    /// 在 Unity 主线程处理后台线程提交的刷新请求。
    /// </summary>
    private static void OnEditorUpdate()
    {
        if (Interlocked.Exchange(ref mainThreadRefreshPending, 0) == 0)
        {
            return;
        }

        if (!IsEnabled())
        {
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            Interlocked.Exchange(ref mainThreadRefreshPending, 1);
            return;
        }

        AssetDatabase.Refresh();
        Debug.Log("[AutoAssetRefresh] 已由主线程刷新资源数据库");
    }

    /// <summary>
    /// 停止后台资源并解除 Editor 生命周期订阅。
    /// </summary>
    private static void Shutdown()
    {
        StopPolling();
        EditorApplication.update -= OnEditorUpdate;
        AssemblyReloadEvents.beforeAssemblyReload -= Shutdown;
        EditorApplication.quitting -= Shutdown;
    }

    #endregion

    #region 后台轮询

    /// <summary>
    /// 启动文件监听器与固定间隔轮询线程。
    /// </summary>
    private static void StartPolling()
    {
        lock (LifecycleLock)
        {
            if (pollingThread != null)
            {
                return;
            }

            FileSystemWatcher createdWatcher = CreateWatcher(Application.dataPath);
            ManualResetEvent createdStopSignal = new ManualResetEvent(false);
            Thread createdThread = new Thread(() => PollForChanges(createdStopSignal))
            {
                IsBackground = true,
                Name = "FlatWorld Auto Asset Refresh Poller"
            };

            watcher = createdWatcher;
            pollingStopSignal = createdStopSignal;
            pollingThread = createdThread;

            createdThread.Start();
            createdWatcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>
    /// 每隔固定时间把文件变化合并为一个主线程刷新请求。
    /// </summary>
    private static void PollForChanges(ManualResetEvent stopSignal)
    {
        while (!stopSignal.WaitOne(PollingIntervalMilliseconds))
        {
            if (!IsEnabled())
            {
                continue;
            }

            if (Interlocked.Exchange(ref fileChangesPending, 0) == 0)
            {
                continue;
            }

            Interlocked.Exchange(ref mainThreadRefreshPending, 1);
        }
    }

    /// <summary>
    /// 停止文件监听器并等待后台轮询线程退出。
    /// </summary>
    private static void StopPolling()
    {
        FileSystemWatcher watcherToDispose;
        ManualResetEvent stopSignalToDispose;
        Thread threadToJoin;

        lock (LifecycleLock)
        {
            watcherToDispose = watcher;
            stopSignalToDispose = pollingStopSignal;
            threadToJoin = pollingThread;

            watcher = null;
            pollingStopSignal = null;
            pollingThread = null;
        }

        DisposeWatcher(watcherToDispose);
        stopSignalToDispose?.Set();

        bool threadStopped = threadToJoin == null
            || !threadToJoin.IsAlive
            || threadToJoin.Join(WorkerJoinTimeoutMilliseconds);

        if (threadStopped)
        {
            stopSignalToDispose?.Dispose();
        }
        else
        {
            Debug.LogWarning("[AutoAssetRefresh] 后台轮询线程未在限定时间内结束");
        }

        Interlocked.Exchange(ref fileChangesPending, 0);
        Interlocked.Exchange(ref mainThreadRefreshPending, 0);
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
    /// 记录普通文件变化，等待后台线程合并处理。
    /// </summary>
    private static void OnFileChanged(object sender, FileSystemEventArgs eventArgs)
    {
        RequestRefresh(eventArgs.FullPath);
    }

    /// <summary>
    /// 记录文件重命名，等待后台线程合并处理。
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

        Interlocked.Exchange(ref fileChangesPending, 1);
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

/// <summary>
/// 在 Unity 保存资源后向自动刷新服务提交合并请求。
/// </summary>
public class AutoAssetRefreshOnSaveProcessor : AssetModificationProcessor
{
    /// <summary>
    /// 记录本次资源保存并保持原始保存路径不变。
    /// </summary>
    private static string[] OnWillSaveAssets(string[] paths)
    {
        if (paths != null && paths.Length > 0)
        {
            AutomaticAssetRefresher.RequestRefresh();
        }

        return paths;
    }
}
#endif
