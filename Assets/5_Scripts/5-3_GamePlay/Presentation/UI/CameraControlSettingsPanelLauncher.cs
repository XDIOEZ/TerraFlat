// AI-Context: 设置主面板内嵌镜头控制页；承载双指缩放、镜头前探、平滑与缩放影响。
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>绑定内嵌镜头控制页，并即时持久化全部镜头偏好。</summary>
[DisallowMultipleComponent]
public sealed class CameraControlSettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    private Toggle pinchZoomToggle;
    private Slider lookaheadSlider;
    private Slider smoothingSlider;
    private Slider zoomInfluenceSlider;
    private TextMeshProUGUI lookaheadValueText;
    private TextMeshProUGUI smoothingValueText;
    private TextMeshProUGUI zoomInfluenceValueText;
    private Button resetButton;
    private ISettingsToggle pinchZoomSetting;
    private ISettingsSlider lookaheadSetting;
    private ISettingsSlider smoothingSetting;
    private ISettingsSlider zoomInfluenceSetting;
    private ISettingsProvider cameraSettingsProvider;
    private bool initialized;

    /// <summary>在指定内嵌页面根节点上复用或挂载镜头控制器。</summary>
    public static CameraControlSettingsPanelLauncher Ensure(Transform pageRoot)
    {
        if (pageRoot == null)
            return null;

        CameraControlSettingsPanelLauncher launcher =
            pageRoot.GetComponent<CameraControlSettingsPanelLauncher>();
        if (launcher == null)
            launcher = pageRoot.gameObject.AddComponent<CameraControlSettingsPanelLauncher>();
        launcher.Initialize();
        return launcher;
    }

    /// <summary>解析页面局部控件并绑定镜头设置 Provider。</summary>
    private void Initialize()
    {
        if (initialized)
            return;

        ISettingsProvider uiProvider = UIUserSettings.SettingsProvider;
        cameraSettingsProvider = CameraUserSettings.SettingsProvider;
        pinchZoomSetting = uiProvider.GetToggle(UIUserSettings.EnablePinchZoomSettingKey);
        lookaheadSetting = cameraSettingsProvider.GetSlider(CameraUserSettings.LookaheadSettingKey);
        smoothingSetting = cameraSettingsProvider.GetSlider(
            CameraUserSettings.LookaheadSmoothingSettingKey);
        zoomInfluenceSetting = cameraSettingsProvider.GetSlider(
            CameraUserSettings.LookaheadZoomInfluenceSettingKey);

        pinchZoomToggle = FindComponent<Toggle>(transform, "双指缩放");
        lookaheadSlider = FindComponent<Slider>(transform, "镜头前探");
        smoothingSlider = FindComponent<Slider>(transform, "预判平滑");
        zoomInfluenceSlider = FindComponent<Slider>(transform, "缩放影响系数");
        lookaheadValueText = FindComponent<TextMeshProUGUI>(transform, "镜头前探数值");
        smoothingValueText = FindComponent<TextMeshProUGUI>(transform, "预判平滑数值");
        zoomInfluenceValueText = FindComponent<TextMeshProUGUI>(transform, "缩放影响系数数值");
        resetButton = FindComponent<Button>(transform, "恢复默认按钮");

        ConfigureSlider(lookaheadSlider, lookaheadSetting);
        ConfigureSlider(smoothingSlider, smoothingSetting);
        ConfigureSlider(zoomInfluenceSlider, zoomInfluenceSetting);
        pinchZoomToggle?.onValueChanged.AddListener(OnPinchZoomChanged);
        lookaheadSlider?.onValueChanged.AddListener(OnLookaheadChanged);
        smoothingSlider?.onValueChanged.AddListener(OnSmoothingChanged);
        zoomInfluenceSlider?.onValueChanged.AddListener(OnZoomInfluenceChanged);
        resetButton?.onClick.AddListener(ResetToDefault);
        initialized = true;

        if (pinchZoomToggle == null || lookaheadSlider == null || smoothingSlider == null ||
            zoomInfluenceSlider == null || lookaheadValueText == null ||
            smoothingValueText == null || zoomInfluenceValueText == null || resetButton == null)
        {
            Debug.LogError(
                "[CameraControlSettingsPanelLauncher] 内嵌镜头控制页控件命名契约不完整。",
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

    /// <summary>写入双指缩放偏好。</summary>
    private void OnPinchZoomChanged(bool value)
    {
        pinchZoomSetting?.SetValue(value);
    }

    /// <summary>写入带符号的镜头前探值。</summary>
    private void OnLookaheadChanged(float value)
    {
        lookaheadSetting?.SetValue(value);
        lookaheadSlider?.SetValueWithoutNotify(lookaheadSetting?.Value ?? value);
        RefreshValueTexts();
    }

    /// <summary>写入镜头预判平滑值。</summary>
    private void OnSmoothingChanged(float value)
    {
        smoothingSetting?.SetValue(value);
        smoothingSlider?.SetValueWithoutNotify(smoothingSetting?.Value ?? value);
        RefreshValueTexts();
    }

    /// <summary>写入镜头拉远时的预测影响系数。</summary>
    private void OnZoomInfluenceChanged(float value)
    {
        zoomInfluenceSetting?.SetValue(value);
        zoomInfluenceSlider?.SetValueWithoutNotify(zoomInfluenceSetting?.Value ?? value);
        RefreshValueTexts();
    }

    /// <summary>恢复镜头控制页的四项默认设置。</summary>
    private void ResetToDefault()
    {
        UIUserSettings.ResetPinchZoomToDefault();
        cameraSettingsProvider?.ResetToDefaults();
        RefreshValues();
    }

    /// <summary>从设置 Provider 回填全部镜头控制值。</summary>
    private void RefreshValues()
    {
        if (pinchZoomToggle != null && pinchZoomSetting != null)
            pinchZoomToggle.SetIsOnWithoutNotify(pinchZoomSetting.Value);
        if (lookaheadSlider != null && lookaheadSetting != null)
            lookaheadSlider.SetValueWithoutNotify(lookaheadSetting.Value);
        if (smoothingSlider != null && smoothingSetting != null)
            smoothingSlider.SetValueWithoutNotify(smoothingSetting.Value);
        if (zoomInfluenceSlider != null && zoomInfluenceSetting != null)
            zoomInfluenceSlider.SetValueWithoutNotify(zoomInfluenceSetting.Value);
        RefreshValueTexts();
    }

    /// <summary>刷新三个镜头参数的数值文本。</summary>
    private void RefreshValueTexts()
    {
        if (lookaheadValueText != null && lookaheadSetting != null)
            lookaheadValueText.text = FormatSigned(lookaheadSetting.Value, "0.00", "s");
        if (smoothingValueText != null && smoothingSetting != null)
            smoothingValueText.text = smoothingSetting.Value.ToString("0.0");
        if (zoomInfluenceValueText != null && zoomInfluenceSetting != null)
            zoomInfluenceValueText.text = FormatSigned(zoomInfluenceSetting.Value, "0.00");
    }

    /// <summary>镜头页显示时重新读取当前设置。</summary>
    public void OnSettingsPageShown()
    {
        RefreshValues();
    }

    /// <summary>镜头页隐藏时无需保留额外草稿。</summary>
    public void OnSettingsPageHidden()
    {
    }

    /// <summary>解除本控制器注册的全部 UI 监听。</summary>
    private void OnDestroy()
    {
        pinchZoomToggle?.onValueChanged.RemoveListener(OnPinchZoomChanged);
        lookaheadSlider?.onValueChanged.RemoveListener(OnLookaheadChanged);
        smoothingSlider?.onValueChanged.RemoveListener(OnSmoothingChanged);
        zoomInfluenceSlider?.onValueChanged.RemoveListener(OnZoomInfluenceChanged);
        resetButton?.onClick.RemoveListener(ResetToDefault);
    }

    /// <summary>把数值格式化为始终带正负号的显示文本。</summary>
    private static string FormatSigned(float value, string format, string suffix = "")
    {
        return (value >= 0f ? "+" : string.Empty) + value.ToString(format) + suffix;
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
