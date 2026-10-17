using UnityEngine;

/// <summary>移动平台横屏方向配置；帧率统一由 FrameRateUserSettings 应用本机偏好。</summary>
public static class MobilePlatformBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyMobilePerformanceDefaults()
    {
        if (!Application.isMobilePlatform)
            return;

        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
    }
}
