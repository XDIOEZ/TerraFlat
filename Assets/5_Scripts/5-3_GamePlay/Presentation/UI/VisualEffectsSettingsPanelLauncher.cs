using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 视觉特效分页的表现控制器：正式 Prefab 提供水体、透视、太阳投影柔化和地面高度阴影控件。
/// 控件只通过 Provider 提交偏好；当前选中状态跟随设置事件刷新，不持有渲染业务。
/// </summary>
[DisallowMultipleComponent]
public sealed class VisualEffectsSettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    #region Prefab 引用与状态

    [SerializeField] private Button stylizedButton;
    [SerializeField] private Button realisticButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private Toggle occlusionToggle;
    [SerializeField] private Toggle sunShadowToggle;
    [SerializeField] private Toggle sunShadowBlurToggle; // 投影柔化开关。
    [SerializeField] private Slider sunShadowBlurSlider; // 0～100% 强度。
    [SerializeField] private TextMeshProUGUI sunShadowBlurValueText; // 当前百分比。
    [SerializeField] private Toggle groundElevationToggle;
    [SerializeField] private Slider groundElevationWidthSlider;
    [SerializeField] private TextMeshProUGUI groundElevationWidthValueText;
    private ISettingsToggle sunShadowSetting;
    private ISettingsToggle sunShadowBlurSetting; // Provider 开关契约。
    private ISettingsSlider sunShadowBlurStrengthSetting; // Provider 强度契约。
    private ISettingsToggle groundElevationSetting;
    private ISettingsSlider groundElevationWidthSetting;
    private ISettingsToggle occlusionSetting;
    private ISettingsProvider provider;
    private ISettingsSwitch styleSetting;

    #endregion

    #region 页面绑定

    /// <summary>页面首次显示时解析设置契约，保留正式 Prefab 的序列化控件引用。</summary>
    private void Awake()
    {
        if (stylizedButton == null || realisticButton == null || resetButton == null ||
            occlusionToggle == null || sunShadowToggle == null || sunShadowBlurToggle == null ||
            sunShadowBlurSlider == null || sunShadowBlurValueText == null || groundElevationToggle == null ||
            groundElevationWidthSlider == null || groundElevationWidthValueText == null)
            throw new MissingReferenceException("视觉特效设置页缺少按钮引用。");

        provider = WaterVisualSettings.SettingsProvider;
        styleSetting = provider.GetSwitch(WaterVisualSettings.StyleSettingKey);
        occlusionSetting = PlayerOcclusionShaderGlobals.SettingsProvider.GetToggle(
            PlayerOcclusionShaderGlobals.EnabledSettingKey);
        ISettingsProvider sunProvider = SunShadowSettings.SettingsProvider;
        sunShadowSetting = sunProvider.GetToggle(SunShadowSettings.EnabledSettingKey);
        sunShadowBlurSetting = sunProvider.GetToggle(SunShadowSettings.BlurEnabledSettingKey);
        sunShadowBlurStrengthSetting = sunProvider.GetSlider(SunShadowSettings.BlurStrengthSettingKey);
        sunShadowBlurSlider.minValue = sunShadowBlurStrengthSetting.MinValue;
        sunShadowBlurSlider.maxValue = sunShadowBlurStrengthSetting.MaxValue;
        sunShadowBlurSlider.wholeNumbers = false;
        ISettingsProvider elevationProvider = GroundElevationShadowSettings.SettingsProvider;
        groundElevationSetting = elevationProvider.GetToggle(GroundElevationShadowSettings.EnabledSettingKey);
        groundElevationWidthSetting = elevationProvider.GetSlider(GroundElevationShadowSettings.WidthSettingKey);
        groundElevationWidthSlider.minValue = groundElevationWidthSetting.MinValue;
        groundElevationWidthSlider.maxValue = groundElevationWidthSetting.MaxValue;
        groundElevationWidthSlider.wholeNumbers = false;
        sunShadowToggle.onValueChanged.AddListener(SetSunShadows);
        sunShadowBlurToggle.onValueChanged.AddListener(SetSunShadowBlur);
        sunShadowBlurSlider.onValueChanged.AddListener(SetSunShadowBlurStrength);
        groundElevationToggle.onValueChanged.AddListener(SetGroundElevation);
        groundElevationWidthSlider.onValueChanged.AddListener(SetGroundElevationWidth);
        occlusionToggle.onValueChanged.AddListener(SetOcclusion);
        stylizedButton.onClick.AddListener(SelectStylized);
        realisticButton.onClick.AddListener(SelectRealistic);
        resetButton.onClick.AddListener(ResetToDefaults);
    }

    /// <summary>页面激活后订阅外部设置变化并刷新当前选中项。</summary>
    private void OnEnable()
    {
        WaterVisualSettings.Changed += RefreshView;
        PlayerOcclusionShaderGlobals.Changed += RefreshView;
        SunShadowSettings.Changed += RefreshView;
        GroundElevationShadowSettings.Changed += RefreshView;
        RefreshView();
    }

    /// <summary>页面隐藏后解除偏好订阅。</summary>
    private void OnDisable()
    {
        WaterVisualSettings.Changed -= RefreshView;
        PlayerOcclusionShaderGlobals.Changed -= RefreshView;
        SunShadowSettings.Changed -= RefreshView;
        GroundElevationShadowSettings.Changed -= RefreshView;
    }

    private void SetOcclusion(bool value) => occlusionSetting.SetValue(value);

    /// <summary>通过 Provider 即时保存太阳投影开关。</summary>
    private void SetSunShadows(bool value) => sunShadowSetting.SetValue(value);

    /// <summary>通过 Provider 即时切换树木与实体投影的柔化。</summary>
    private void SetSunShadowBlur(bool value) => sunShadowBlurSetting.SetValue(value);

    /// <summary>通过 Provider 保存阴影模糊程度。</summary>
    private void SetSunShadowBlurStrength(float value) => sunShadowBlurStrengthSetting.SetValue(value);

    /// <summary>通过 Provider 即时切换地面高度阴影。</summary>
    private void SetGroundElevation(bool value) => groundElevationSetting.SetValue(value);

    /// <summary>通过 Provider 保存阴影宽度，材质收到变更后立即刷新。</summary>
    private void SetGroundElevationWidth(float value) => groundElevationWidthSetting.SetValue(value);

    /// <summary>通过设置契约选用风格化水面。</summary>
    private void SelectStylized() => SelectStyle(0);

    /// <summary>通过设置契约选用写实水面。</summary>
    private void SelectRealistic() => SelectStyle(1);

    /// <summary>提交风格并回填权威选中状态。</summary>
    private void SelectStyle(int index)
    {
        if (!styleSetting.TrySetSelectedIndex(index, out string error))
            Debug.LogError($"[VisualEffectsSettings] {error}", this);
        RefreshView();
    }

    /// <summary>恢复本页的全部视觉偏好。</summary>
    private void ResetToDefaults()
    {
        provider.ResetToDefaults();
        PlayerOcclusionShaderGlobals.SettingsProvider.ResetToDefaults();
        SunShadowSettings.SettingsProvider.ResetToDefaults();
        GroundElevationShadowSettings.SettingsProvider.ResetToDefaults();
    }

    /// <summary>用选中底色标识当前风格，两种按钮始终可导航和操作。</summary>
    private void RefreshView()
    {
        occlusionToggle.SetIsOnWithoutNotify(PlayerOcclusionShaderGlobals.Enabled);
        sunShadowToggle.SetIsOnWithoutNotify(SunShadowSettings.Enabled);
        sunShadowBlurToggle.SetIsOnWithoutNotify(SunShadowSettings.BlurEnabled);
        sunShadowBlurToggle.interactable = SunShadowSettings.Enabled;
        sunShadowBlurSlider.SetValueWithoutNotify(SunShadowSettings.BlurStrength);
        sunShadowBlurSlider.interactable = SunShadowSettings.Enabled && SunShadowSettings.BlurEnabled;
        sunShadowBlurValueText.text = $"{Mathf.RoundToInt(SunShadowSettings.BlurStrength * 100f)}%";
        groundElevationToggle.SetIsOnWithoutNotify(GroundElevationShadowSettings.Enabled);
        groundElevationWidthSlider.SetValueWithoutNotify(GroundElevationShadowSettings.Width);
        groundElevationWidthSlider.interactable = GroundElevationShadowSettings.Enabled;
        groundElevationWidthValueText.text = $"{Mathf.RoundToInt(GroundElevationShadowSettings.Width * 100f)}%";
        stylizedButton.targetGraphic.color = styleSetting.SelectedIndex == 0
            ? FlatWorldUITheme.Accent : FlatWorldUITheme.Surface;
        realisticButton.targetGraphic.color = styleSetting.SelectedIndex == 1
            ? FlatWorldUITheme.Accent : FlatWorldUITheme.Surface;
    }

    /// <summary>分页正式显示时与其它设置页保持统一生命周期。</summary>
    public void OnSettingsPageShown() => RefreshView();

    /// <summary>所有选择即时保存，隐藏时没有待提交草稿。</summary>
    public void OnSettingsPageHidden() { }

    /// <summary>销毁时解除本控制器的按钮监听。</summary>
    private void OnDestroy()
    {
        occlusionToggle.onValueChanged.RemoveListener(SetOcclusion);
        if (sunShadowToggle != null) sunShadowToggle.onValueChanged.RemoveListener(SetSunShadows);
        sunShadowBlurToggle.onValueChanged.RemoveListener(SetSunShadowBlur);
        sunShadowBlurSlider.onValueChanged.RemoveListener(SetSunShadowBlurStrength);
        groundElevationToggle.onValueChanged.RemoveListener(SetGroundElevation);
        groundElevationWidthSlider.onValueChanged.RemoveListener(SetGroundElevationWidth);
        stylizedButton.onClick.RemoveListener(SelectStylized);
        realisticButton.onClick.RemoveListener(SelectRealistic);
        resetButton.onClick.RemoveListener(ResetToDefaults);
    }

    #endregion
}
