#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// 统一关闭 Cinemachine 2.10.3 的 SaveDuringPlay 编辑器功能。FlatWorld 开启了 DisableDomainReload，退出播放时动态实体可能已经销毁，
/// 而 Cinemachine 仍会扫描全部 MonoBehaviour 并访问其 GameObject，导致 SaveDuringPlay.cs:88 抛出 NullReferenceException；项目运行时相机不依赖该编辑器回写功能。
/// </summary>
[InitializeOnLoad]
internal static class CinemachineSaveDuringPlayPolicy
{
    static CinemachineSaveDuringPlayPolicy()
    {
        DisableSaveDuringPlay();
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    /// <summary>进入播放前再次确保全局编辑器偏好关闭。</summary>
    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            DisableSaveDuringPlay();
        }
    }

    /// <summary>通过 Cinemachine 公共 API 关闭全局 SaveDuringPlay。</summary>
    private static void DisableSaveDuringPlay()
    {
        if (SaveDuringPlay.SaveDuringPlay.Enabled)
        {
            SaveDuringPlay.SaveDuringPlay.Enabled = false;
        }
    }
}
#endif
