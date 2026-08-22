using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>
/// 全局镜头预判偏好。前探值允许 -2～1 秒：正值使用 Cinemachine 前探，负值关闭前探并按绝对值增加镜头阻尼，
/// 负值的绝对值越大代表惯性越强；预判平滑值范围为 0～10，数值越大越稳定但响应越慢。偏好通过 PlayerPrefs 保存，
/// 相机模块订阅 Changed 后会在运行时立即应用，不写入世界存档或玩家实体数据。
/// </summary>
public static class CameraUserSettings
{
    #region 键与默认值

    private const string LookaheadKey = "FlatWorld.Camera.Lookahead";
    private const string LookaheadSmoothingKey = "FlatWorld.Camera.LookaheadSmoothing";

    public const float DefaultLookahead = 0.22f;
    public const float MinimumLookahead = -2f;
    public const float MaximumLookahead = 1f;
    public const float LookaheadStep = 0.01f;

    public const float DefaultLookaheadSmoothing = 0.5f;
    public const float MinimumLookaheadSmoothing = 0f;
    public const float MaximumLookaheadSmoothing = 10f;
    public const float LookaheadSmoothingStep = 0.1f;

    public const string SettingsProviderId = "camera";
    public const string LookaheadSettingKey = "camera.lookahead";
    public const string LookaheadSmoothingSettingKey = "camera.lookaheadSmoothing";

    #endregion

    #region 缓存与事件

    private static bool initialized;
    private static float cachedLookahead = DefaultLookahead;
    private static float cachedLookaheadSmoothing = DefaultLookaheadSmoothing;

    /// <summary>任一镜头偏好实际改变后广播一次。</summary>
    public static event Action Changed;

    private static readonly ISettingsProvider settingsProvider =
        CreateSettingsProvider();

    /// <summary>供设置 UI 查找的镜头偏好提供者；首次访问时自动注册。</summary>
    public static ISettingsProvider SettingsProvider => RegisterSettingsProvider();

    public static float Lookahead
    {
        get
        {
            EnsureInitialized();
            return cachedLookahead;
        }
    }

    public static float LookaheadSmoothing
    {
        get
        {
            EnsureInitialized();
            return cachedLookaheadSmoothing;
        }
    }

    #endregion

    #region 写入入口

    /// <summary>保存带符号的镜头前探值；负值表示按绝对值增加惯性。</summary>
    public static float SetLookahead(float value)
    {
        EnsureInitialized();
        float sanitized = Sanitize(value, MinimumLookahead, MaximumLookahead, LookaheadStep, DefaultLookahead);
        if (Mathf.Approximately(cachedLookahead, sanitized))
            return cachedLookahead;

        cachedLookahead = sanitized;
        PlayerPrefs.SetFloat(LookaheadKey, sanitized);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return sanitized;
    }

    /// <summary>保存前探平滑值；数值越大越稳，但会增加预测响应延迟。</summary>
    public static float SetLookaheadSmoothing(float value)
    {
        EnsureInitialized();
        float sanitized = Sanitize(
            value,
            MinimumLookaheadSmoothing,
            MaximumLookaheadSmoothing,
            LookaheadSmoothingStep,
            DefaultLookaheadSmoothing);
        if (Mathf.Approximately(cachedLookaheadSmoothing, sanitized))
            return cachedLookaheadSmoothing;

        cachedLookaheadSmoothing = sanitized;
        PlayerPrefs.SetFloat(LookaheadSmoothingKey, sanitized);
        PlayerPrefs.Save();
        Changed?.Invoke();
        return sanitized;
    }

    /// <summary>恢复镜头前探与预判平滑默认值。</summary>
    public static void ResetToDefaults()
    {
        EnsureInitialized();
        bool changed = !Mathf.Approximately(cachedLookahead, DefaultLookahead) ||
                       !Mathf.Approximately(cachedLookaheadSmoothing, DefaultLookaheadSmoothing);
        if (!changed)
            return;

        cachedLookahead = DefaultLookahead;
        cachedLookaheadSmoothing = DefaultLookaheadSmoothing;
        PlayerPrefs.SetFloat(LookaheadKey, cachedLookahead);
        PlayerPrefs.SetFloat(LookaheadSmoothingKey, cachedLookaheadSmoothing);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }

    #endregion

    #region 初始化与校验

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(settingsProvider);
        initialized = false;
        cachedLookahead = DefaultLookahead;
        cachedLookaheadSmoothing = DefaultLookaheadSmoothing;
        Changed = null;
    }

    #region 设置提供者

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSettingsProviderOnLoad()
    {
        RegisterSettingsProvider();
    }

    private static ISettingsProvider RegisterSettingsProvider()
    {
        SettingsProviderRegistry.Register(settingsProvider);
        return settingsProvider;
    }

    private static ISettingsProvider CreateSettingsProvider()
    {
        return new CameraSettingsProvider();
    }

    private sealed class CameraSettingsProvider : ISettingsProvider
    {
        private readonly IReadOnlyList<ISettingsSlider> sliders;

        public CameraSettingsProvider()
        {
            sliders = new ISettingsSlider[]
            {
                new SettingsSlider(
                    new SettingDescriptor(
                        LookaheadSettingKey,
                        "镜头前探",
                        SettingControlType.Slider,
                        "camera",
                        order: 0),
                    MinimumLookahead,
                    MaximumLookahead,
                    LookaheadStep,
                    () => Lookahead,
                    value => SetLookahead(value)),
                new SettingsSlider(
                    new SettingDescriptor(
                        LookaheadSmoothingSettingKey,
                        "预判平滑",
                        SettingControlType.Slider,
                        "camera",
                        order: 1),
                    MinimumLookaheadSmoothing,
                    MaximumLookaheadSmoothing,
                    LookaheadSmoothingStep,
                    () => LookaheadSmoothing,
                    value => SetLookaheadSmoothing(value))
            };
        }

        public string ProviderId => SettingsProviderId;
        public string DisplayName => "镜头";
        public int Order => 30;
        public IReadOnlyList<ISettingsToggle> ToggleSettings =>
            Array.Empty<ISettingsToggle>();
        public IReadOnlyList<ISettingsSlider> SliderSettings => sliders;
        public IReadOnlyList<ISettingsDropdown> DropdownSettings =>
            Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings =>
            Array.Empty<ISettingsSwitch>();

        public void ResetToDefaults() => CameraUserSettings.ResetToDefaults();
    }

    #endregion

    private static void EnsureInitialized()
    {
        if (initialized)
            return;

        cachedLookahead = Sanitize(
            PlayerPrefs.GetFloat(LookaheadKey, DefaultLookahead),
            MinimumLookahead,
            MaximumLookahead,
            LookaheadStep,
            DefaultLookahead);
        cachedLookaheadSmoothing = Sanitize(
            PlayerPrefs.GetFloat(LookaheadSmoothingKey, DefaultLookaheadSmoothing),
            MinimumLookaheadSmoothing,
            MaximumLookaheadSmoothing,
            LookaheadSmoothingStep,
            DefaultLookaheadSmoothing);
        initialized = true;
    }

    private static float Sanitize(
        float value,
        float minimum,
        float maximum,
        float step,
        float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            value = fallback;

        float clamped = Mathf.Clamp(value, minimum, maximum);
        return Mathf.Round(clamped / step) * step;
    }

    #endregion
}
