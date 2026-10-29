using FlatWorld.Diagnostics;
using UnityEngine;

/// <summary>慢事件仅在主线程按批输出，后台保存线程不接触 Unity 日志。</summary>
public sealed class PerformanceDebugLogPump : MonoBehaviour
{
    #region 主线程慢摘要出口

    private static PerformanceDebugLogPump instance;
    private static bool quitting;
    private float nextFlush;

    public static void ApplyEnabled(bool enabled)
    {
        if (quitting) return;
        if (instance == null && enabled)
        {
            instance = FindObjectOfType<PerformanceDebugLogPump>(true);
            if (instance == null)
            {
                var host = new GameObject("PerformanceDebugLogPump");
                PersistentRuntimeObjectHierarchy.MoveToPersistentCategory(host, PersistentRuntimeObjectCategory.Debug);
                instance = host.AddComponent<PerformanceDebugLogPump>();
            }
        }
        if (instance != null)
        {
            instance.nextFlush = Time.unscaledTime + 1f;
            instance.enabled = enabled;
        }
    }

    private void Update()
    {
        if (!PerformanceDebugRegistry.SlowSummaryEnabled) { enabled = false; return; }
        if (Time.unscaledTime < nextFlush) return;
        nextFlush = Time.unscaledTime + 1f;
        if (!PerformanceDebugRegistry.HasPendingSlowEvents) return;
        string summary = PerformanceDebugRegistry.DrainSlowSummary();
        if (!string.IsNullOrEmpty(summary)) Debug.Log(summary);
    }

    private void OnApplicationQuit()
    {
        quitting = true;
        PerformanceDebugRegistry.ResetSession();
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        PerformanceDebugRegistry.DiscardPendingSlowEvents();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState() { instance = null; quitting = false; }

    #endregion
}
