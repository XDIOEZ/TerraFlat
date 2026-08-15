using System;
using UnityEngine;

/// <summary>
/// 主菜单画质预设。高/中/低映射到项目 QualitySettings 中同名档位，
/// 通过 PlayerPrefs 保存并在运行开始时应用，不写入世界存档。
/// </summary>
public enum GraphicsPreset
{
    High = 0,
    Medium = 1,
    Low = 2
}

/// <summary>
/// 统一提供画质预设的读取、保存和 Unity QualitySettings 应用接口。
/// </summary>
public static class GraphicsUserSettings
{
    #region 键与默认值

    private const string PresetKey = "FlatWorld.Graphics.Preset";

    public const GraphicsPreset DefaultPreset = GraphicsPreset.High;

    #endregion
    #region 缓存与事件

    private static bool initialized;
    private static GraphicsPreset cachedPreset = DefaultPreset;

    /// <summary>画质预设实际改变后触发。</summary>
    public static event Action Changed;

    public static GraphicsPreset Preset
    {
        get
        {
            EnsureInitialized();
            return cachedPreset;
        }
    }

    /// <summary>供主菜单下拉列表直接使用的高/中/低索引。</summary>
    public static int PresetIndex => (int)Preset;

    #endregion
    #region 写入入口

    /// <summary>按高/中/低索引保存并立即应用画质。</summary>
    public static GraphicsPreset SetPresetIndex(int index)
    {
        EnsureInitialized();
        GraphicsPreset sanitized = Sanitize(index);
        bool changed = cachedPreset != sanitized;
        cachedPreset = sanitized;
        PlayerPrefs.SetInt(PresetKey, (int)cachedPreset);
        PlayerPrefs.Save();
        ApplyCurrentSettings();

        if (changed)
            Changed?.Invoke();
        return cachedPreset;
    }

    /// <summary>将当前保存的预设再次应用到 Unity 质量系统。</summary>
    public static void ApplyCurrentSettings()
    {
        EnsureInitialized();
        int qualityLevel = FindQualityLevel(cachedPreset);
        if (qualityLevel >= 0 && qualityLevel < QualitySettings.names.Length)
            QualitySettings.SetQualityLevel(qualityLevel, true);
    }

    /// <summary>恢复高画质默认值并立即应用。</summary>
    public static void ResetToDefaults()
    {
        EnsureInitialized();
        bool changed = cachedPreset != DefaultPreset;
        cachedPreset = DefaultPreset;
        PlayerPrefs.SetInt(PresetKey, (int)cachedPreset);
        PlayerPrefs.Save();
        ApplyCurrentSettings();

        if (changed)
            Changed?.Invoke();
    }

    #endregion
    #region 初始化与质量档位映射

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySavedSettingsBeforeSceneLoad()
    {
        EnsureInitialized();
        ApplyCurrentSettings();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        initialized = false;
        cachedPreset = DefaultPreset;
        Changed = null;
    }

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        cachedPreset = Sanitize(PlayerPrefs.GetInt(PresetKey, (int)DefaultPreset));
        initialized = true;
    }

    private static GraphicsPreset Sanitize(int value)
    {
        return (GraphicsPreset)Mathf.Clamp(
            value,
            (int)GraphicsPreset.High,
            (int)GraphicsPreset.Low);
    }

    private static int FindQualityLevel(GraphicsPreset preset)
    {
        string[] names = QualitySettings.names;
        if (names == null || names.Length == 0)
            return -1;

        string targetName = preset == GraphicsPreset.High
            ? "High"
            : preset == GraphicsPreset.Medium
                ? "Medium"
                : "Low";

        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], targetName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        for (int i = 0; i < names.Length; i++)
        {
            if (names[i].IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0)
                return i;
        }

        int fallback = preset == GraphicsPreset.High
            ? names.Length - 1
            : preset == GraphicsPreset.Medium
                ? names.Length / 2
                : 0;
        return Mathf.Clamp(fallback, 0, names.Length - 1);
    }

    #endregion
}
