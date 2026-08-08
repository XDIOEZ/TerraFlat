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
    private const string ActivePageKey = KeyPrefix + "ActivePage";

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

    public static int ActivePageIndex => PlayerPrefs.GetInt(ActivePageKey, 0);

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

    public static void SetActivePageIndex(int pageIndex)
    {
        PlayerPrefs.SetInt(ActivePageKey, pageIndex);
        PlayerPrefs.Save();
    }

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
