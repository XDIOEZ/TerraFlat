using UnityEngine;

/// <summary>
/// GM 面板本机偏好。只保存调试面板的显示与运行时调速选择，不写入世界存档。
/// 区块无限制使用独立布尔值保存，避免把 Infinity 写入 PlayerPrefs。
/// </summary>
internal static class GMConsolePreferences
{
    private const string KeyPrefix = "FlatWorld.GMConsole.";
    private const string PlayerMoveSpeedKey = KeyPrefix + "PlayerMoveSpeed";
    private const string ChunkLoadSpeedKey = KeyPrefix + "ChunkLoadSpeed";
    private const string ChunkLoadUnlimitedKey = KeyPrefix + "ChunkLoadUnlimited";
    private const string TeleportShortcutKey = KeyPrefix + "TeleportShortcut";
    private const string NavigationPathKey = KeyPrefix + "NavigationPath";
    private const string AnimalDebugOverlayKey = KeyPrefix + "AnimalDebugOverlay";
    private const string ActivePageKey = KeyPrefix + "ActivePage";
    private const string TemperatureOverlayKey = KeyPrefix + "TemperatureOverlay";
    private const string TemperatureOverlayTransparencyKey = KeyPrefix + "TemperatureOverlayTransparency";
    private const string WorldLayerOverlayModeKey = KeyPrefix + "WorldLayerOverlayMode";

    #region 读取

    public static float PlayerMoveSpeedMultiplier =>
        GetFinitePositiveFloat(PlayerMoveSpeedKey, 1f);

    public static float ChunkLoadSpeedMultiplier =>
        GetFinitePositiveFloat(ChunkLoadSpeedKey, 1f);

    public static bool ChunkLoadSpeedUnlimited =>
        PlayerPrefs.GetInt(ChunkLoadUnlimitedKey, 0) != 0;

    public static bool TeleportShortcutEnabled =>
        PlayerPrefs.GetInt(TeleportShortcutKey, 1) != 0;

    public static bool NavigationPathVisible =>
        PlayerPrefs.GetInt(NavigationPathKey, 0) != 0;

    public static bool AnimalDebugOverlayVisible =>
        PlayerPrefs.GetInt(AnimalDebugOverlayKey, 0) != 0;

    public static int ActivePageIndex => PlayerPrefs.GetInt(ActivePageKey, 0);

    /// <summary>新版本统一保存观察层模式；没有新键时继承旧温度眼镜开关。</summary>
    public static GmWorldLayerMode WorldLayerOverlayMode
    {
        get
        {
            if (!PlayerPrefs.HasKey(WorldLayerOverlayModeKey))
                return PlayerPrefs.GetInt(TemperatureOverlayKey, 0) != 0
                    ? GmWorldLayerMode.Temperature
                    : GmWorldLayerMode.Off;

            int value = PlayerPrefs.GetInt(WorldLayerOverlayModeKey, 0);
            return System.Enum.IsDefined(typeof(GmWorldLayerMode), value)
                ? (GmWorldLayerMode)value
                : GmWorldLayerMode.Off;
        }
    }

    // 42% 透明度保留温度层原先 0.58 的覆盖强度。
    public static float WorldLayerOverlayTransparency => PlayerPrefs.GetFloat(TemperatureOverlayTransparencyKey, 0.42f);

    #endregion

    #region 写入

    public static void SetPlayerMoveSpeed(float multiplier)
    {
        if (!IsFinitePositive(multiplier))
            return;

        PlayerPrefs.SetFloat(PlayerMoveSpeedKey, multiplier);
        PlayerPrefs.Save();
    }

    public static void SetChunkLoadSpeed(float finiteMultiplier, bool unlimited)
    {
        if (IsFinitePositive(finiteMultiplier))
            PlayerPrefs.SetFloat(ChunkLoadSpeedKey, finiteMultiplier);

        PlayerPrefs.SetInt(ChunkLoadUnlimitedKey, unlimited ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void SetTeleportShortcut(bool enabled)
    {
        PlayerPrefs.SetInt(TeleportShortcutKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void SetNavigationPathVisible(bool visible)
    {
        PlayerPrefs.SetInt(NavigationPathKey, visible ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void SetAnimalDebugOverlayVisible(bool visible)
    {
        PlayerPrefs.SetInt(AnimalDebugOverlayKey, visible ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void SetActivePageIndex(int pageIndex)
    {
        PlayerPrefs.SetInt(ActivePageKey, pageIndex);
        PlayerPrefs.Save();
    }

    /// <summary>保存当前世界观察层；同时同步旧温度键，开发期回退版本时仍保持合理状态。</summary>
    public static void SetWorldLayerOverlayMode(GmWorldLayerMode mode)
    {
        PlayerPrefs.SetInt(WorldLayerOverlayModeKey, (int)mode);
        PlayerPrefs.SetInt(TemperatureOverlayKey, mode == GmWorldLayerMode.Temperature ? 1 : 0);
        PlayerPrefs.Save();
    }

    /// <summary>滑动期间只更新内存偏好，交互结束后再统一写盘。</summary>
    public static void SetWorldLayerOverlayTransparency(float transparency)
    {
        PlayerPrefs.SetFloat(TemperatureOverlayTransparencyKey, Mathf.Clamp01(transparency));
    }

    /// <summary>提交连续交互产生的偏好；正常退出时 Unity 也会保存 PlayerPrefs。</summary>
    public static void SavePendingChanges() => PlayerPrefs.Save();

    #endregion

    #region 校验

    private static float GetFinitePositiveFloat(string key, float fallback)
    {
        float value = PlayerPrefs.GetFloat(key, fallback);
        return IsFinitePositive(value) ? value : fallback;
    }

    private static bool IsFinitePositive(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    }

    #endregion
}
