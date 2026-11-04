#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine;

internal static class AndroidModDirectoryLauncher
{
    #region Android 原生 MOD 目录入口

    // 原生插件同步返回启动结果，文件管理不回写游戏菜单或加载状态。
    public static bool TryOpenModsDirectoryInNativeFileManager(string modsRootPath)
    {
        using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
        using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        using AndroidJavaClass provider = new("com.flatworld.mods.FlatWorldModDocumentsProvider");
        return provider.CallStatic<bool>("openModsDirectory", activity, modsRootPath);
    }

    #endregion
}
#endif
