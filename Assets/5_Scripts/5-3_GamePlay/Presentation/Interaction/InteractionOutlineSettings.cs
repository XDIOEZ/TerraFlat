using System;
using System.Collections.Generic;
using FlatWorld.Settings;
using UnityEngine;

/// <summary>交互描边的本机视觉偏好，开关与屏幕像素宽度共用统一设置保存流程。</summary>
public static class InteractionOutlineSettings
{
    #region 偏好与渲染参数

    public const string EnabledSettingKey = "interaction-outline.enabled";
    public const string ThicknessSettingKey = "interaction-outline.thickness";
    private const string EnabledPreferenceKey = "FlatWorld.Visual.InteractionOutlineEnabled";
    private const string ThicknessPreferenceKey = "FlatWorld.Visual.InteractionOutlineThickness";
    private static readonly int ThicknessProperty = Shader.PropertyToID("_WorldInteractionOutlinePixels");
    private static readonly ISettingsProvider provider = new OutlineSettingsProvider();
    private static bool initialized;
    private static bool enabled;
    private static float thicknessPixels;

    public static bool DefaultEnabled => WorldRenderingConfigCatalog.Default.interactionOutline.enabled;
    public static float DefaultThickness => WorldRenderingConfigCatalog.Default.interactionOutline.thicknessPixels;
    public static float MinThickness => WorldRenderingConfigCatalog.Default.interactionOutline.minimumThicknessPixels;
    public static float MaxThickness => WorldRenderingConfigCatalog.Default.interactionOutline.maximumThicknessPixels;
    public static event Action Changed;

    public static bool Enabled
    {
        get { EnsureInitialized(); return enabled; }
    }

    public static float ThicknessPixels
    {
        get { EnsureInitialized(); return thicknessPixels; }
    }

    public static ISettingsProvider SettingsProvider
    {
        get { SettingsProviderRegistry.Register(provider); return provider; }
    }

    public static void SetEnabled(bool value)
    {
        EnsureInitialized();
        if (enabled == value) return;
        enabled = value;
        PlayerPrefs.SetInt(EnabledPreferenceKey, value ? 1 : 0);
        PlayerPrefs.Save();
        ApplyShaderState();
        Changed?.Invoke();
    }

    public static void SetThickness(float value)
    {
        EnsureInitialized();
        float next = NormalizeThickness(value);
        if (Mathf.Approximately(thicknessPixels, next)) return;
        thicknessPixels = next;
        PlayerPrefs.SetFloat(ThicknessPreferenceKey, next);
        PlayerPrefs.Save();
        ApplyShaderState();
        Changed?.Invoke();
    }

    private static float NormalizeThickness(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) value = DefaultThickness;
        return Mathf.Clamp(Mathf.Round(value), MinThickness, MaxThickness);
    }

    private static void EnsureInitialized()
    {
        if (initialized) return;
        enabled = PlayerPrefs.GetInt(EnabledPreferenceKey, DefaultEnabled ? 1 : 0) != 0;
        thicknessPixels = NormalizeThickness(PlayerPrefs.GetFloat(ThicknessPreferenceKey, DefaultThickness));
        initialized = true;
        ApplyShaderState();
    }

    private static void ApplyShaderState()
    {
        // 关闭时统一传零，普通 Sprite 和合批建筑、机械都会立即停止显示白边。
        Shader.SetGlobalFloat(ThicknessProperty, enabled ? thicknessPixels : 0f);
    }

    #endregion

    #region 设置契约

    private sealed class OutlineSettingsProvider : ISettingsProvider
    {
        private IReadOnlyList<ISettingsSlider> sliders;
        public string ProviderId => "interaction-outline";
        public string DisplayName => "交互描边";
        public int Order => 48;
        public IReadOnlyList<ISettingsToggle> ToggleSettings { get; } = new ISettingsToggle[]
        {
            new SettingsToggle(new SettingDescriptor(EnabledSettingKey, "交互描边",
                SettingControlType.Toggle, "visual-effects"), () => Enabled, SetEnabled)
        };
        public IReadOnlyList<ISettingsSlider> SliderSettings => sliders ??= new ISettingsSlider[]
        {
            new SettingsSlider(new SettingDescriptor(ThicknessSettingKey, "描边粗细",
                SettingControlType.Slider, "visual-effects"), MinThickness, MaxThickness, 1f,
                () => ThicknessPixels, SetThickness)
        };
        public IReadOnlyList<ISettingsDropdown> DropdownSettings => Array.Empty<ISettingsDropdown>();
        public IReadOnlyList<ISettingsSwitch> SwitchSettings => Array.Empty<ISettingsSwitch>();

        public void ResetToDefaults()
        {
            SetEnabled(DefaultEnabled);
            SetThickness(DefaultThickness);
        }
    }

    #endregion

    #region 运行会话

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        SettingsProviderRegistry.Unregister(provider);
        initialized = false;
        Changed = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterOnLoad()
    {
        EnsureInitialized();
        SettingsProviderRegistry.Register(provider);
    }

    #endregion
}
