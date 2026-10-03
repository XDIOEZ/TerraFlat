#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>每次进入播放都重建脚本域与场景，避免资源目录和单例沿用失效引用。</summary>
[InitializeOnLoad]
internal static class FullPlayModeReloadPolicy
{
    #region 播放重载策略

    static FullPlayModeReloadPolicy()
    {
        EditorApplication.delayCall += ApplyInEditMode;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void ApplyInEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        EnsureFullReload();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // 当前播放保持原样，只在返回编辑状态或下一次进入播放前修正配置。
        if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.ExitingEditMode)
            EnsureFullReload();
    }

    private static void EnsureFullReload()
    {
        if (!EditorSettings.enterPlayModeOptionsEnabled &&
            EditorSettings.enterPlayModeOptions == EnterPlayModeOptions.None)
            return;

        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;
        EditorSettings.enterPlayModeOptionsEnabled = false;
        Debug.Log("[PlayModeReload] 已恢复脚本域与场景重载，下一次播放将重新初始化 Addressables 和静态状态。");
    }

    #endregion
}
#endif
