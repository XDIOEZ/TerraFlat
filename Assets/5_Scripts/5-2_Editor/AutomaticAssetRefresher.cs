#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class AutomaticAssetRefresher
{
    // 最小刷新间隔，防止频繁刷新
    private const double MinRefreshIntervalSeconds = 0.5d;
    private static bool pendingRefresh;
    private static double nextAllowedRefreshTime;
    private static double lastChangeTime;
    private static FileSystemWatcher watcher;

    static AutomaticAssetRefresher()
    {
        StartWatcher();
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.quitting += StopWatcher;
    }

    private static void OnEditorUpdate()
    {
        if (!pendingRefresh)
        {
            return;
        }

        double now = EditorApplication.timeSinceStartup;
        if (now - lastChangeTime < MinRefreshIntervalSeconds)
        {
            return;
        }

        if (now < nextAllowedRefreshTime)
        {
            return;
        }

        pendingRefresh = false;
        nextAllowedRefreshTime = now + MinRefreshIntervalSeconds;
        AssetDatabase.Refresh();
        UnityEngine.Debug.Log("[AutoAssetRefresh] 已刷新资源数据库");
    }
    //请求
    public static void RequestRefresh()
    {
        pendingRefresh = true;
        lastChangeTime = EditorApplication.timeSinceStartup;
    }

    private static void StartWatcher()
    {
        if (watcher != null)
        {
            return;
        }

        string assetsPath = Application.dataPath;
        watcher = new FileSystemWatcher(assetsPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            Filter = "*.*"
        };

        watcher.Changed += OnFileChanged;
        watcher.Created += OnFileChanged;
        watcher.Deleted += OnFileChanged;
        watcher.Renamed += OnFileRenamed;
        watcher.EnableRaisingEvents = true;
    }

    private static void StopWatcher()
    {
        if (watcher == null)
        {
            return;
        }

        watcher.EnableRaisingEvents = false;
        watcher.Changed -= OnFileChanged;
        watcher.Created -= OnFileChanged;
        watcher.Deleted -= OnFileChanged;
        watcher.Renamed -= OnFileRenamed;
        watcher.Dispose();
        watcher = null;
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (IsIgnoredPath(e.FullPath))
        {
            return;
        }

        RequestRefresh();
    }

    private static void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (IsIgnoredPath(e.FullPath))
        {
            return;
        }

        RequestRefresh();
    }

    private static bool IsIgnoredPath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath))
        {
            return true;
        }

        string fileName = Path.GetFileName(fullPath);
        if (fileName.EndsWith(".tmp") || fileName.EndsWith(".csproj") || fileName.EndsWith(".sln"))
        {
            return true;
        }

        return false;
    }
}

public class AutoAssetRefreshOnSaveProcessor : AssetModificationProcessor
{
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
