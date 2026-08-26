// AI-Context: 设置菜单的“镜头控制”入口；承载双指缩放、镜头前探、预判平滑和缩放影响系数。

using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>连接设置主菜单与正式镜头控制 Prefab，并即时持久化全部镜头偏好。</summary>
[DisallowMultipleComponent]
public sealed class CameraControlSettingsPanelLauncher : MonoBehaviour
{
    private const string EntryButtonName = "镜头控制";
    private const float PreferredWidth = 800f;
    private const float PreferredHeight = 620f;
    private const float CanvasSafeMargin = 32f;

    private Button entryButton;
    private BasePanel settingsPanel;
    private Toggle pinchZoomToggle;
    private Slider lookaheadSlider;
    private Slider smoothingSlider;
    private Slider zoomInfluenceSlider;
    private TextMeshProUGUI lookaheadValueText;
    private TextMeshProUGUI smoothingValueText;
    private TextMeshProUGUI zoomInfluenceValueText;
    private ISettingsToggle pinchZoomSetting;
    private ISettingsSlider lookaheadSetting;
    private ISettingsSlider smoothingSetting;
    private ISettingsSlider zoomInfluenceSetting;
    private ISettingsProvider cameraSettingsProvider;
    private bool isClamping;

    /// <summary>在设置主面板上复用或挂载镜头控制入口适配器。</summary>
    public static CameraControlSettingsPanelLauncher Ensure(Transform settingsRoot)
    {
        if (settingsRoot == null)
            return null;

        CameraControlSettingsPanelLauncher launcher =
            settingsRoot.GetComponent<CameraControlSettingsPanelLauncher>();
        if (launcher == null)
            launcher = settingsRoot.gameObject.AddComponent<CameraControlSettingsPanelLauncher>();
        launcher.EnsureEntryButton();
        return launcher;
    }

    /// <summary>查找并绑定设置主菜单中的镜头控制入口。</summary>
    private void EnsureEntryButton()
    {
        entryButton ??= FindButton(transform, EntryButtonName);
        if (entryButton == null)
        {
            Debug.LogError(
                $"[CameraControlSettingsPanelLauncher] Prefab 缺少入口按钮“{EntryButtonName}”。",
                this);
            return;
        }

        entryButton.onClick.RemoveListener(Open);
        entryButton.onClick.AddListener(Open);
    }

    /// <summary>刷新设置值后打开镜头控制子页。</summary>
    private void Open()
    {
        EnsureSettingsWindow();
        if (settingsPanel == null)
            return;

        RefreshValues();
        ClampWindowToCanvas();
        settingsPanel.Open();
        settingsPanel.transform.SetAsLastSibling();
    }

    /// <summary>从正式 Prefab 创建并绑定镜头控制控件。</summary>
    private void EnsureSettingsWindow()
    {
        if (settingsPanel != null)
            return;

        GameObject prefab = GameRes.Instance?.GetPrefab(
            RuntimeUIPrefabKeys.CameraControlSettings);
        if (prefab == null)
        {
            Debug.LogError(
                $"[CameraControlSettingsPanelLauncher] 缺少 Prefab：{RuntimeUIPrefabKeys.CameraControlSettings}。",
                this);
            return;
        }

        settingsPanel = UIManager.Instance.CreatePanelFromGameObject(
            prefab,
            RuntimeUIPrefabKeys.CameraControlSettings);
        SettingsSubPanelInteractionGuard.Link(transform, settingsPanel);
        ISettingsProvider uiProvider = UIUserSettings.SettingsProvider;
        cameraSettingsProvider = CameraUserSettings.SettingsProvider;
        pinchZoomSetting = uiProvider.GetToggle(UIUserSettings.EnablePinchZoomSettingKey);
        lookaheadSetting = cameraSettingsProvider.GetSlider(CameraUserSettings.LookaheadSettingKey);
        smoothingSetting = cameraSettingsProvider.GetSlider(
            CameraUserSettings.LookaheadSmoothingSettingKey);
        zoomInfluenceSetting = cameraSettingsProvider.GetSlider(
            CameraUserSettings.LookaheadZoomInfluenceSettingKey);

        pinchZoomToggle = settingsPanel.GetToggle("双指缩放");
        lookaheadSlider = settingsPanel.GetSlider("镜头前探");
        smoothingSlider = settingsPanel.GetSlider("预判平滑");
        zoomInfluenceSlider = settingsPanel.GetSlider("缩放影响系数");
        lookaheadValueText = settingsPanel.GetText("镜头前探数值");
        smoothingValueText = settingsPanel.GetText("预判平滑数值");
        zoomInfluenceValueText = settingsPanel.GetText("缩放影响系数数值");

        settingsPanel.GetButton("关闭按钮")?.onClick.AddListener(Close);
        settingsPanel.GetButton("恢复默认按钮")?.onClick.AddListener(ResetToDefault);
        settingsPanel.GetButton("完成按钮")?.onClick.AddListener(Close);
        pinchZoomToggle?.onValueChanged.AddListener(OnPinchZoomChanged);
        lookaheadSlider?.onValueChanged.AddListener(OnLookaheadChanged);
        smoothingSlider?.onValueChanged.AddListener(OnSmoothingChanged);
        zoomInfluenceSlider?.onValueChanged.AddListener(OnZoomInfluenceChanged);

        if (pinchZoomToggle == null || lookaheadSlider == null || smoothingSlider == null ||
            zoomInfluenceSlider == null || lookaheadValueText == null ||
            smoothingValueText == null || zoomInfluenceValueText == null)
        {
            Debug.LogError(
                "[CameraControlSettingsPanelLauncher] 镜头控制 Prefab 控件命名契约不完整。",
                settingsPanel);
        }

        ConfigureSlider(lookaheadSlider, lookaheadSetting);
        ConfigureSlider(smoothingSlider, smoothingSetting);
        ConfigureSlider(zoomInfluenceSlider, zoomInfluenceSetting);
        ClampWindowToCanvas();
        settingsPanel.PrepareForGamepadNavigation("双指缩放");
        settingsPanel.Close();
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

    /// <summary>写入双指缩放偏好并让手机 HUD 清理已有触点。</summary>
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

    /// <summary>从设置提供者回填全部镜头控制值。</summary>
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

    /// <summary>关闭镜头控制子页。</summary>
    private void Close()
    {
        settingsPanel?.Close();
    }

    /// <summary>根画布尺寸变化时重新限制子页面板。</summary>
    private void OnRectTransformDimensionsChange()
    {
        if (settingsPanel != null)
            ClampWindowToCanvas();
    }

    /// <summary>解除入口事件并销毁运行时创建的子页。</summary>
    private void OnDestroy()
    {
        if (entryButton != null)
            entryButton.onClick.RemoveListener(Open);
        if (settingsPanel != null)
        {
            settingsPanel.Close();
            Destroy(settingsPanel.gameObject);
        }
    }

    /// <summary>用统一视觉边距规则限制镜头控制页尺寸。</summary>
    private void ClampWindowToCanvas()
    {
        if (settingsPanel == null || isClamping)
            return;

        isClamping = true;
        try
        {
            SettingsPanelLayoutUtility.ClampToCanvas(
                settingsPanel,
                new Vector2(PreferredWidth, PreferredHeight),
                CanvasSafeMargin);
        }
        finally
        {
            isClamping = false;
        }
    }

    /// <summary>把数值格式化为始终带正负号的显示文本。</summary>
    private static string FormatSigned(float value, string format, string suffix = "")
    {
        return (value >= 0f ? "+" : string.Empty) + value.ToString(format) + suffix;
    }

    /// <summary>按节点名查找任意层级中的按钮。</summary>
    private static Button FindButton(Transform root, string buttonName)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].name == buttonName)
                return buttons[i];
        }

        return null;
    }
}
