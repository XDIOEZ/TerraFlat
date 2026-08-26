// AI-Context: 设置菜单的“UI设置”入口；负责界面缩放、安全区、移动摇杆模式与左右触控区，不再承载镜头控制。

using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>连接设置主菜单与正式界面设置 Prefab，并即时持久化界面及手机触控区偏好。</summary>
[DisallowMultipleComponent]
public sealed class UISettingsPanelLauncher : MonoBehaviour
{
    private const string EntryButtonName = "UI设置";
    private const float PreferredWidth = 800f;
    private const float PreferredHeight = 680f;
    private const float CanvasSafeMargin = 32f;

    private Button entryButton;
    private BasePanel settingsPanel;
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
    private ISettingsSlider scaleSetting;
    private ISettingsSlider leftControlZoneSetting;
    private ISettingsSlider rightControlZoneSetting;
    private ISettingsToggle safeAreaSetting;
    private ISettingsToggle floatingMoveJoystickSetting;
    private bool isClamping;

    /// <summary>在设置主面板上复用或挂载入口适配器。</summary>
    public static UISettingsPanelLauncher Ensure(Transform settingsRoot)
    {
        if (settingsRoot == null)
            return null;

        UISettingsPanelLauncher launcher =
            settingsRoot.GetComponent<UISettingsPanelLauncher>();
        if (launcher == null)
            launcher = settingsRoot.gameObject.AddComponent<UISettingsPanelLauncher>();
        launcher.EnsureEntryButton();
        return launcher;
    }

    /// <summary>查找并绑定设置主菜单中的界面设置入口。</summary>
    private void EnsureEntryButton()
    {
        entryButton ??= FindButton(transform, EntryButtonName);
        if (entryButton == null)
        {
            Debug.LogError(
                $"[UISettingsPanelLauncher] Prefab 缺少入口按钮“{EntryButtonName}”。",
                this);
            return;
        }

        entryButton.onClick.RemoveListener(Open);
        entryButton.onClick.AddListener(Open);
    }

    /// <summary>刷新设置值后打开界面设置子页。</summary>
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

    /// <summary>从正式 Prefab 创建并绑定界面设置控件。</summary>
    private void EnsureSettingsWindow()
    {
        if (settingsPanel != null)
            return;

        GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.UISettings);
        if (prefab == null)
        {
            Debug.LogError(
                $"[UISettingsPanelLauncher] 缺少 Prefab：{RuntimeUIPrefabKeys.UISettings}。",
                this);
            return;
        }

        settingsPanel = UIManager.Instance.CreatePanelFromGameObject(
            prefab,
            RuntimeUIPrefabKeys.UISettings);
        SettingsSubPanelInteractionGuard.Link(transform, settingsPanel);
        ISettingsProvider uiSettingsProvider = UIUserSettings.SettingsProvider;
        scaleSetting = uiSettingsProvider.GetSlider(UIUserSettings.ScaleSettingKey);
        leftControlZoneSetting =
            uiSettingsProvider.GetSlider(UIUserSettings.LeftControlZoneRatioSettingKey);
        rightControlZoneSetting =
            uiSettingsProvider.GetSlider(UIUserSettings.RightControlZoneRatioSettingKey);
        safeAreaSetting = uiSettingsProvider.GetToggle(UIUserSettings.RespectSafeAreaSettingKey);
        floatingMoveJoystickSetting =
            uiSettingsProvider.GetToggle(UIUserSettings.FloatingMoveJoystickSettingKey);
        scaleSlider = settingsPanel.GetSlider("界面缩放");
        leftControlZoneSlider = settingsPanel.GetSlider("左侧触控区比例");
        rightControlZoneSlider = settingsPanel.GetSlider("右侧触控区比例");
        safeAreaToggle = settingsPanel.GetToggle("安全区域适配");
        floatingMoveJoystickToggle = settingsPanel.GetToggle("浮动移动摇杆");
        scaleValueText = settingsPanel.GetText("界面缩放数值");
        leftControlZoneValueText = settingsPanel.GetText("左侧触控区数值");
        rightControlZoneValueText = settingsPanel.GetText("右侧触控区数值");
        controlZoneStatusText = settingsPanel.GetText("触控区域比例文本");
        statusText = settingsPanel.GetText("状态文本");

        settingsPanel.GetButton("关闭按钮")?.onClick.AddListener(Close);
        settingsPanel.GetButton("恢复默认按钮")?.onClick.AddListener(ResetToDefault);
        settingsPanel.GetButton("完成按钮")?.onClick.AddListener(Close);
        scaleSlider?.onValueChanged.AddListener(OnScaleChanged);
        leftControlZoneSlider?.onValueChanged.AddListener(OnLeftControlZoneChanged);
        rightControlZoneSlider?.onValueChanged.AddListener(OnRightControlZoneChanged);
        safeAreaToggle?.onValueChanged.AddListener(OnSafeAreaChanged);
        floatingMoveJoystickToggle?.onValueChanged.AddListener(OnFloatingMoveJoystickChanged);

        if (scaleSlider == null || leftControlZoneSlider == null ||
            rightControlZoneSlider == null || safeAreaToggle == null ||
            floatingMoveJoystickToggle == null || scaleValueText == null ||
            leftControlZoneValueText == null || rightControlZoneValueText == null ||
            controlZoneStatusText == null || statusText == null)
        {
            Debug.LogError(
                "[UISettingsPanelLauncher] 界面设置 Prefab 控件命名契约不完整。",
                settingsPanel);
        }

        ConfigureSlider(scaleSlider, scaleSetting);
        ConfigureSlider(leftControlZoneSlider, leftControlZoneSetting);
        ConfigureSlider(rightControlZoneSlider, rightControlZoneSetting);

        ClampWindowToCanvas();
        settingsPanel.PrepareForGamepadNavigation("界面缩放");
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

    /// <summary>写入界面缩放并重新限制当前子页尺寸。</summary>
    private void OnScaleChanged(float value)
    {
        scaleSetting?.SetValue(value);
        scaleSlider?.SetValueWithoutNotify(scaleSetting?.Value ?? value);
        RefreshStatus();
        ClampWindowToCanvas();
    }

    /// <summary>写入左侧移动摇杆触控区比例并刷新三段占比。</summary>
    private void OnLeftControlZoneChanged(float value)
    {
        leftControlZoneSetting?.SetValue(value);
        leftControlZoneSlider?.SetValueWithoutNotify(leftControlZoneSetting?.Value ?? value);
        RefreshStatus();
    }

    /// <summary>写入右侧普通指向触控区比例并刷新三段占比。</summary>
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
        ClampWindowToCanvas();
    }

    /// <summary>写入移动摇杆浮动或固定偏好。</summary>
    private void OnFloatingMoveJoystickChanged(bool value)
    {
        floatingMoveJoystickSetting?.SetValue(value);
    }

    /// <summary>只恢复界面页可见的界面与触控区设置。</summary>
    private void ResetToDefault()
    {
        UIUserSettings.ResetInterfaceToDefaults();
        RefreshValues();
        ClampWindowToCanvas();
    }

    /// <summary>从设置提供者回填全部界面控件。</summary>
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

    /// <summary>刷新缩放、三段触控区百分比与安全区域状态文案。</summary>
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

    /// <summary>关闭界面设置子页。</summary>
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

    /// <summary>用统一视觉边距规则限制界面设置页尺寸。</summary>
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

    /// <summary>把缩放比例转换为整数百分比。</summary>
    private static string ToPercent(float value)
    {
        return Mathf.RoundToInt(value * 100f) + "%";
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
