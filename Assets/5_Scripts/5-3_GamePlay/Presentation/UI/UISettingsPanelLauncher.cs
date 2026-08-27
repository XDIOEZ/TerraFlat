// AI-Context: 设置主面板内嵌界面页；负责界面缩放、安全区、移动摇杆与左右触控区。
using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>绑定内嵌界面设置页，并即时持久化界面与手机触控区偏好。</summary>
[DisallowMultipleComponent]
public sealed class UISettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    private Slider scaleSlider;
    private Slider leftControlZoneSlider;
    private Slider rightControlZoneSlider;
    private Toggle safeAreaToggle;
    private Toggle floatingMoveJoystickToggle;
    private TextMeshProUGUI scaleValueText;
    private TextMeshProUGUI leftControlZoneValueText;
    private TextMeshProUGUI rightControlZoneValueText;
    private TextMeshProUGUI controlZoneStatusText;
    private TextMeshProUGUI statusText;
    private Button resetButton;
    private ISettingsSlider scaleSetting;
    private ISettingsSlider leftControlZoneSetting;
    private ISettingsSlider rightControlZoneSetting;
    private ISettingsToggle safeAreaSetting;
    private ISettingsToggle floatingMoveJoystickSetting;
    private bool initialized;

    /// <summary>在指定内嵌页面根节点上复用或挂载界面设置控制器。</summary>
    public static UISettingsPanelLauncher Ensure(Transform pageRoot)
    {
        if (pageRoot == null)
            return null;

        UISettingsPanelLauncher launcher = pageRoot.GetComponent<UISettingsPanelLauncher>();
        if (launcher == null)
            launcher = pageRoot.gameObject.AddComponent<UISettingsPanelLauncher>();
        launcher.Initialize();
        return launcher;
    }

    /// <summary>解析页面局部控件并绑定界面设置 Provider。</summary>
    private void Initialize()
    {
        if (initialized)
            return;

        ISettingsProvider provider = UIUserSettings.SettingsProvider;
        scaleSetting = provider.GetSlider(UIUserSettings.ScaleSettingKey);
        leftControlZoneSetting =
            provider.GetSlider(UIUserSettings.LeftControlZoneRatioSettingKey);
        rightControlZoneSetting =
            provider.GetSlider(UIUserSettings.RightControlZoneRatioSettingKey);
        safeAreaSetting = provider.GetToggle(UIUserSettings.RespectSafeAreaSettingKey);
        floatingMoveJoystickSetting =
            provider.GetToggle(UIUserSettings.FloatingMoveJoystickSettingKey);

        scaleSlider = FindComponent<Slider>(transform, "界面缩放");
        leftControlZoneSlider = FindComponent<Slider>(transform, "左侧触控区比例");
        rightControlZoneSlider = FindComponent<Slider>(transform, "右侧触控区比例");
        safeAreaToggle = FindComponent<Toggle>(transform, "安全区域适配");
        floatingMoveJoystickToggle = FindComponent<Toggle>(transform, "浮动移动摇杆");
        scaleValueText = FindComponent<TextMeshProUGUI>(transform, "界面缩放数值");
        leftControlZoneValueText = FindComponent<TextMeshProUGUI>(transform, "左侧触控区数值");
        rightControlZoneValueText = FindComponent<TextMeshProUGUI>(transform, "右侧触控区数值");
        controlZoneStatusText = FindComponent<TextMeshProUGUI>(transform, "触控区域比例文本");
        statusText = FindComponent<TextMeshProUGUI>(transform, "状态文本");
        resetButton = FindComponent<Button>(transform, "恢复默认按钮");

        ConfigureSlider(scaleSlider, scaleSetting);
        ConfigureSlider(leftControlZoneSlider, leftControlZoneSetting);
        ConfigureSlider(rightControlZoneSlider, rightControlZoneSetting);
        scaleSlider?.onValueChanged.AddListener(OnScaleChanged);
        leftControlZoneSlider?.onValueChanged.AddListener(OnLeftControlZoneChanged);
        rightControlZoneSlider?.onValueChanged.AddListener(OnRightControlZoneChanged);
        safeAreaToggle?.onValueChanged.AddListener(OnSafeAreaChanged);
        floatingMoveJoystickToggle?.onValueChanged.AddListener(OnFloatingMoveJoystickChanged);
        resetButton?.onClick.AddListener(ResetToDefault);
        initialized = true;

        if (scaleSlider == null || leftControlZoneSlider == null ||
            rightControlZoneSlider == null || safeAreaToggle == null ||
            floatingMoveJoystickToggle == null || scaleValueText == null ||
            leftControlZoneValueText == null || rightControlZoneValueText == null ||
            controlZoneStatusText == null || statusText == null || resetButton == null)
        {
            Debug.LogError(
                "[UISettingsPanelLauncher] 内嵌界面设置页控件命名契约不完整。",
                this);
        }
    }

    /// <summary>按设置契约配置滑动条范围与连续取值。</summary>
    private static void ConfigureSlider(Slider slider, ISettingsSlider setting)
    {
        if (slider == null || setting == null)
            return;

        slider.minValue = setting.MinValue;
        slider.maxValue = setting.MaxValue;
        slider.wholeNumbers = false;
    }

    /// <summary>写入界面缩放并刷新显示状态。</summary>
    private void OnScaleChanged(float value)
    {
        scaleSetting?.SetValue(value);
        scaleSlider?.SetValueWithoutNotify(scaleSetting?.Value ?? value);
        RefreshStatus();
    }

    /// <summary>写入左侧移动摇杆触控区比例。</summary>
    private void OnLeftControlZoneChanged(float value)
    {
        leftControlZoneSetting?.SetValue(value);
        leftControlZoneSlider?.SetValueWithoutNotify(leftControlZoneSetting?.Value ?? value);
        RefreshStatus();
    }

    /// <summary>写入右侧普通指向触控区比例。</summary>
    private void OnRightControlZoneChanged(float value)
    {
        rightControlZoneSetting?.SetValue(value);
        rightControlZoneSlider?.SetValueWithoutNotify(rightControlZoneSetting?.Value ?? value);
        RefreshStatus();
    }

    /// <summary>写入安全区域偏好并刷新状态。</summary>
    private void OnSafeAreaChanged(bool value)
    {
        safeAreaSetting?.SetValue(value);
        RefreshStatus();
    }

    /// <summary>写入移动摇杆浮动或固定偏好。</summary>
    private void OnFloatingMoveJoystickChanged(bool value)
    {
        floatingMoveJoystickSetting?.SetValue(value);
    }

    /// <summary>恢复界面页可见设置的默认值。</summary>
    private void ResetToDefault()
    {
        UIUserSettings.ResetInterfaceToDefaults();
        RefreshValues();
    }

    /// <summary>从设置 Provider 回填全部界面控件。</summary>
    private void RefreshValues()
    {
        if (scaleSlider != null && scaleSetting != null)
            scaleSlider.SetValueWithoutNotify(scaleSetting.Value);
        if (leftControlZoneSlider != null && leftControlZoneSetting != null)
            leftControlZoneSlider.SetValueWithoutNotify(leftControlZoneSetting.Value);
        if (rightControlZoneSlider != null && rightControlZoneSetting != null)
            rightControlZoneSlider.SetValueWithoutNotify(rightControlZoneSetting.Value);
        if (safeAreaToggle != null && safeAreaSetting != null)
            safeAreaToggle.SetIsOnWithoutNotify(safeAreaSetting.Value);
        if (floatingMoveJoystickToggle != null && floatingMoveJoystickSetting != null)
            floatingMoveJoystickToggle.SetIsOnWithoutNotify(floatingMoveJoystickSetting.Value);
        RefreshStatus();
    }

    /// <summary>刷新缩放、触控区百分比与安全区域状态文案。</summary>
    private void RefreshStatus()
    {
        if (scaleValueText != null && scaleSetting != null)
            scaleValueText.text = ToPercent(scaleSetting.Value);
        if (leftControlZoneValueText != null && leftControlZoneSetting != null)
            leftControlZoneValueText.text = ToPercent(leftControlZoneSetting.Value);
        if (rightControlZoneValueText != null && rightControlZoneSetting != null)
            rightControlZoneValueText.text = ToPercent(rightControlZoneSetting.Value);
        if (controlZoneStatusText != null)
        {
            controlZoneStatusText.text = FlatWorldLocalizationService.GetUiFormat(
                "触控区域比例：左 {0}｜中 {1}｜右 {2}",
                ToPercent(UIUserSettings.LeftControlZoneRatio),
                ToPercent(UIUserSettings.CenterControlZoneRatio),
                ToPercent(UIUserSettings.RightControlZoneRatio));
        }

        if (statusText != null)
        {
            statusText.text = safeAreaSetting != null && safeAreaSetting.Value
                ? FlatWorldLocalizationService.GetUiText("安全区域适配：开启（推荐）")
                : FlatWorldLocalizationService.GetUiText("安全区域适配：关闭");
        }
    }

    /// <summary>界面页显示时重新读取当前设置。</summary>
    public void OnSettingsPageShown()
    {
        RefreshValues();
    }

    /// <summary>界面页隐藏时无需保留额外草稿。</summary>
    public void OnSettingsPageHidden()
    {
    }

    /// <summary>解除本控制器注册的全部 UI 监听。</summary>
    private void OnDestroy()
    {
        scaleSlider?.onValueChanged.RemoveListener(OnScaleChanged);
        leftControlZoneSlider?.onValueChanged.RemoveListener(OnLeftControlZoneChanged);
        rightControlZoneSlider?.onValueChanged.RemoveListener(OnRightControlZoneChanged);
        safeAreaToggle?.onValueChanged.RemoveListener(OnSafeAreaChanged);
        floatingMoveJoystickToggle?.onValueChanged.RemoveListener(OnFloatingMoveJoystickChanged);
        resetButton?.onClick.RemoveListener(ResetToDefault);
    }

    /// <summary>把比例转换为整数百分比。</summary>
    private static string ToPercent(float value)
    {
        return Mathf.RoundToInt(value * 100f) + "%";
    }

    /// <summary>在页面局部按名称查找指定组件。</summary>
    private static T FindComponent<T>(Transform root, string objectName) where T : Component
    {
        T[] components = root.GetComponentsInChildren<T>(true);
        for (int index = 0; index < components.Length; index++)
        {
            if (components[index] != null && components[index].name == objectName)
                return components[index];
        }

        return null;
    }
}
