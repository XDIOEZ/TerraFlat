using System;
using System.Threading;
using UnityEngine;

/// <summary>
/// 提供 Android 强制系统手势区的屏幕像素边距，并将原生 UI 线程结果安全转回 Unity 主线程。
/// 底部回到桌面手势无法由应用排除，带滑动操作的 UI 应使用该边距主动避让；非 Android 平台始终返回零。
/// </summary>
public static class AndroidSystemGestureInsets
{
    #region 状态

    // Unity 主线程同步上下文用于接收 Android UI 线程查询结果。
    private static SynchronizationContext unityContext;
    // 当前设备底部强制系统手势区高度，单位为屏幕像素。
    private static int bottomInsetPixels;
    // 同一次运行只记录一次原生查询异常，避免恢复焦点时重复刷屏。
    private static bool queryFailureLogged;

    /// <summary>有效手势边距发生变化时通知 UI 重排。</summary>
    public static event Action Changed;

    #endregion

    #region 生命周期与查询

    /// <summary>进入新的运行时前清空静态状态，兼容关闭域重载的编辑器配置。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        unityContext = null;
        bottomInsetPixels = 0;
        queryFailureLogged = false;
        Changed = null;
    }

    /// <summary>请求 Android UI 线程重新读取当前方向下的强制系统手势边距。</summary>
    public static void RequestRefresh()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (unityContext == null)
            unityContext = SynchronizationContext.Current;

        try
        {
            using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            activity.Call("runOnUiThread", new AndroidJavaRunnable(QueryOnAndroidUiThread));
        }
        catch (Exception exception)
        {
            ApplyQueryFailure(exception.Message);
        }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>在 Android UI 线程读取 WindowInsets，并把结果投递回 Unity 主线程。</summary>
    private static void QueryOnAndroidUiThread()
    {
        int queriedBottomInset = 0;
        string failure = null;

        try
        {
            using AndroidJavaClass version = new("android.os.Build$VERSION");
            int sdkVersion = version.GetStatic<int>("SDK_INT");
            if (sdkVersion < 29)
            {
                PostQueryResult(0, null);
                return;
            }

            using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
            using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            using AndroidJavaObject window = activity.Call<AndroidJavaObject>("getWindow");
            using AndroidJavaObject decorView = window.Call<AndroidJavaObject>("getDecorView");
            using AndroidJavaObject rootInsets = decorView.Call<AndroidJavaObject>("getRootWindowInsets");
            if (rootInsets == null)
                return;

            using AndroidJavaObject mandatoryInsets = ResolveMandatoryInsets(rootInsets, sdkVersion);
            queriedBottomInset = mandatoryInsets != null
                ? Math.Max(0, mandatoryInsets.Get<int>("bottom"))
                : 0;
        }
        catch (Exception exception)
        {
            failure = exception.Message;
        }

        PostQueryResult(queriedBottomInset, failure);
    }

    /// <summary>按 Android 版本选择未弃用接口，并兼容仅提供旧接口的 Android 10。</summary>
    private static AndroidJavaObject ResolveMandatoryInsets(AndroidJavaObject rootInsets, int sdkVersion)
    {
        if (sdkVersion >= 30)
        {
            using AndroidJavaClass insetType = new("android.view.WindowInsets$Type");
            int mandatoryGestureMask = insetType.CallStatic<int>("mandatorySystemGestures");
            return rootInsets.Call<AndroidJavaObject>("getInsets", mandatoryGestureMask);
        }

        return rootInsets.Call<AndroidJavaObject>("getMandatorySystemGestureInsets");
    }

    /// <summary>将 Android UI 线程结果投递到 Unity 主线程。</summary>
    private static void PostQueryResult(int queriedBottomInset, string failure)
    {
        SynchronizationContext context = unityContext;
        if (context == null)
            return;

        context.Post(_ =>
        {
            if (!string.IsNullOrEmpty(failure))
            {
                ApplyQueryFailure(failure);
                return;
            }

            ApplyBottomInset(queriedBottomInset);
        }, null);
    }
#endif

    /// <summary>更新有效边距并通知依赖该几何信息的 UI。</summary>
    private static void ApplyBottomInset(int value)
    {
        value = Mathf.Max(0, value);
        if (bottomInsetPixels == value)
            return;

        bottomInsetPixels = value;
        Changed?.Invoke();
    }

    /// <summary>保留最近一次有效值，并仅报告一次原生查询异常。</summary>
    private static void ApplyQueryFailure(string message)
    {
        if (queryFailureLogged)
            return;

        queryFailureLogged = true;
        Debug.LogWarning($"[AndroidSystemGestureInsets] 无法读取系统手势边距，继续使用最近一次有效值：{message}");
    }

    #endregion

    #region 坐标换算

    /// <summary>把尚未被现有安全区覆盖的底部手势像素换算为指定 RectTransform 的本地单位。</summary>
    public static float GetAdditionalBottomPadding(RectTransform coordinateSpace, Rect occupiedScreenArea)
    {
        if (coordinateSpace == null)
            return 0f;

        float uncoveredPixels = Mathf.Max(0f, bottomInsetPixels - occupiedScreenArea.yMin);
        float localUnitsPerPixel = coordinateSpace.rect.height / Mathf.Max(1f, occupiedScreenArea.height);
        return uncoveredPixels * localUnitsPerPixel;
    }

    #endregion
}
