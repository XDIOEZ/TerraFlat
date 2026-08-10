using System;
using UnityEngine;

/// <summary>
/// 左上角世界坐标 HUD 的显示格式。
/// 数值必须保持稳定，因为会写入本地 PlayerPrefs 偏好。
/// </summary>
public enum PlayerWorldCoordinateDisplayMode
{
    WorldCoordinates = 0,
    LatitudeLongitude = 1
}

/// <summary>
/// 管理玩家坐标 HUD 的本地显示偏好。
/// 设置独立于存档保存，切换世界或重启游戏后仍保持玩家最后一次选择。
/// </summary>
public static class PlayerWorldCoordinateDisplayPreferences
{
    #region 常量与缓存

    private const string DisplayModeKey = "FlatWorld.UI.PlayerCoordinateDisplayMode";

    public const PlayerWorldCoordinateDisplayMode DefaultMode =
        PlayerWorldCoordinateDisplayMode.WorldCoordinates;

    private static bool initialized;
    private static PlayerWorldCoordinateDisplayMode currentMode;

    /// <summary>显示格式实际变化后广播，常驻 HUD 无需逐帧读取偏好。</summary>
    public static event Action Changed;

    #endregion

    #region 公共接口

    /// <summary>当前已保存的显示格式。</summary>
    public static PlayerWorldCoordinateDisplayMode Mode
    {
        get
        {
            EnsureInitialized();
            return currentMode;
        }
    }

    /// <summary>立即保存并应用新的坐标显示格式。</summary>
    public static void SetMode(PlayerWorldCoordinateDisplayMode mode)
    {
        EnsureInitialized();
        PlayerWorldCoordinateDisplayMode nextMode = Normalize(mode);
        if (currentMode == nextMode)
            return;

        currentMode = nextMode;
        PlayerPrefs.SetInt(DisplayModeKey, (int)currentMode);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    /// <summary>恢复默认的世界坐标显示。</summary>
    public static void ResetToDefault()
    {
        SetMode(DefaultMode);
    }

    #endregion

    #region 初始化与校验

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeCache()
    {
        initialized = false;
        currentMode = DefaultMode;
        Changed = null;
    }

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        currentMode = Normalize((PlayerWorldCoordinateDisplayMode)PlayerPrefs.GetInt(
            DisplayModeKey,
            (int)DefaultMode));
        initialized = true;
    }

    private static PlayerWorldCoordinateDisplayMode Normalize(
        PlayerWorldCoordinateDisplayMode mode)
    {
        return mode == PlayerWorldCoordinateDisplayMode.LatitudeLongitude
            ? PlayerWorldCoordinateDisplayMode.LatitudeLongitude
            : DefaultMode;
    }

    #endregion
}
