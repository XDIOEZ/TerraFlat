using UnityEngine;

/// <summary>Android 首版运行参数：关闭垂直同步并以 60 FPS 为目标；画质档位仍由 ProjectSettings 的 Android High 默认值决定。</summary>
public static class MobilePlatformBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyMobilePerformanceDefaults()
    {
        if (!Application.isMobilePlatform)
            return;

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
    }
}
