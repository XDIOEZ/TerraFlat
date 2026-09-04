using UnityEngine;

/// <summary>
/// 管理任务追踪 HUD 的本地显示偏好。
/// 展开状态独立于任务与世界存档，通过 PlayerPrefs 跨维度切换和游戏重启保留。
/// </summary>
public static class PlayerQuestTrackerPreferences
{
    #region 常量与缓存

    private const string ExpandedKey = "FlatWorld.UI.QuestTrackerExpanded";

    public const bool DefaultExpanded = true;

    private static bool initialized;
    private static bool expanded = DefaultExpanded;

    /// <summary>当前任务追踪面板是否展开。</summary>
    public static bool Expanded
    {
        get
        {
            EnsureInitialized();
            return expanded;
        }
    }

    #endregion

    #region 公共接口

    /// <summary>保存任务追踪面板的展开状态。</summary>
    public static void SetExpanded(bool value)
    {
        EnsureInitialized();
        if (expanded == value)
            return;

        expanded = value;
        PlayerPrefs.SetInt(ExpandedKey, expanded ? 1 : 0);
        PlayerPrefs.Save();
    }

    #endregion

    #region 生命周期

    /// <summary>退出当前运行域时清空静态缓存，下次访问重新读取本地偏好。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeCache()
    {
        initialized = false;
        expanded = DefaultExpanded;
    }

    /// <summary>首次访问时从 PlayerPrefs 恢复展开状态。</summary>
    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        expanded = PlayerPrefs.GetInt(ExpandedKey, DefaultExpanded ? 1 : 0) != 0;
        initialized = true;
    }

    #endregion
}
