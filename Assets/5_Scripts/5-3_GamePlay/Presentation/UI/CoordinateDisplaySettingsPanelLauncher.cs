using FlatWorld.Localization;
using FlatWorld.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 绑定内嵌显示设置页，并持久化左上角 HUD 的坐标格式与 FPS 显隐。
/// 页面只控制本地展示，不改变世界坐标或存档数据。
/// </summary>
[DisallowMultipleComponent]
public sealed class CoordinateDisplaySettingsPanelLauncher : MonoBehaviour, ISettingsPageLifecycle
{
    private const string WorldCoordinatesButtonName = "世界坐标模式按钮";
    private const string LatitudeLongitudeButtonName = "经纬度模式按钮";
    private const string ShowFpsToggleName = "FPS显示开关";

    private Button worldCoordinatesButton;
    private Button latitudeLongitudeButton;
    private Toggle showFpsToggle;
    private TextMeshProUGUI statusText;
    private ISettingsSwitch displayModeSetting;
    private ISettingsToggle showFpsSetting;
    private bool initialized;

    /// <summary>在指定内嵌页面根节点上复用或挂载显示设置控制器。</summary>
    public static CoordinateDisplaySettingsPanelLauncher Ensure(Transform pageRoot)
    {
        if (pageRoot == null)
            return null;

        CoordinateDisplaySettingsPanelLauncher launcher =
            pageRoot.GetComponent<CoordinateDisplaySettingsPanelLauncher>();
        if (launcher == null)
            launcher = pageRoot.gameObject.AddComponent<CoordinateDisplaySettingsPanelLauncher>();
        launcher.Initialize();
        return launcher;
    }

    /// <summary>解析页面局部控件并绑定左上角 HUD 设置 Provider。</summary>
    private void Initialize()
    {
        if (initialized)
            return;

        ISettingsProvider provider = PlayerWorldCoordinateDisplayPreferences.SettingsProvider;
        displayModeSetting = provider
            .GetSwitch(PlayerWorldCoordinateDisplayPreferences.ModeSettingKey);
        showFpsSetting = provider
            .GetToggle(PlayerWorldCoordinateDisplayPreferences.ShowFpsSettingKey);
        worldCoordinatesButton = FindComponent<Button>(transform, WorldCoordinatesButtonName);
        latitudeLongitudeButton = FindComponent<Button>(transform, LatitudeLongitudeButtonName);
        showFpsToggle = FindComponent<Toggle>(transform, ShowFpsToggleName);
        statusText = FindComponent<TextMeshProUGUI>(transform, "状态文本");
        worldCoordinatesButton?.onClick.AddListener(SelectWorldCoordinates);
        latitudeLongitudeButton?.onClick.AddListener(SelectLatitudeLongitude);
        showFpsToggle?.onValueChanged.AddListener(SetShowFps);
        initialized = true;

        if (worldCoordinatesButton == null || latitudeLongitudeButton == null ||
            showFpsToggle == null || statusText == null || displayModeSetting == null ||
            showFpsSetting == null)
        {
            Debug.LogError(
                "[CoordinateDisplaySettingsPanelLauncher] 内嵌显示设置页控件命名契约不完整。",
                this);
        }
    }

    /// <summary>选择世界坐标显示方式。</summary>
    private void SelectWorldCoordinates()
    {
        SetDisplayMode(PlayerWorldCoordinateDisplayMode.WorldCoordinates);
    }

    /// <summary>选择经纬度显示方式。</summary>
    private void SelectLatitudeLongitude()
    {
        SetDisplayMode(PlayerWorldCoordinateDisplayMode.LatitudeLongitude);
    }

    /// <summary>提交显示方式并刷新页内选中状态。</summary>
    private void SetDisplayMode(PlayerWorldCoordinateDisplayMode mode)
    {
        displayModeSetting?.TrySetSelectedIndex((int)mode, out _);
        RefreshView();
    }

    /// <summary>提交 FPS 显隐并立即回填权威值。</summary>
    private void SetShowFps(bool visible)
    {
        showFpsSetting?.SetValue(visible);
        if (showFpsToggle != null && showFpsSetting != null)
            showFpsToggle.SetIsOnWithoutNotify(showFpsSetting.Value);
    }

    /// <summary>根据当前权威设置刷新坐标选项、FPS 开关和状态文本。</summary>
    private void RefreshView()
    {
        int selectedIndex = displayModeSetting != null
            ? displayModeSetting.SelectedIndex
            : (int)PlayerWorldCoordinateDisplayPreferences.DefaultMode;
        PlayerWorldCoordinateDisplayMode mode =
            (PlayerWorldCoordinateDisplayMode)selectedIndex;
        SetSelectionVisual(
            worldCoordinatesButton,
            mode == PlayerWorldCoordinateDisplayMode.WorldCoordinates);
        SetSelectionVisual(
            latitudeLongitudeButton,
            mode == PlayerWorldCoordinateDisplayMode.LatitudeLongitude);
        if (showFpsToggle != null && showFpsSetting != null)
            showFpsToggle.SetIsOnWithoutNotify(showFpsSetting.Value);

        if (statusText != null)
        {
            statusText.text = mode == PlayerWorldCoordinateDisplayMode.LatitudeLongitude
                ? FlatWorldLocalizationService.GetUiText("当前显示：经纬度（经度 / 纬度）")
                : FlatWorldLocalizationService.GetUiText("当前显示：世界坐标（X / Y）");
        }
    }

    /// <summary>设置一个显示方式按钮的选中颜色。</summary>
    private static void SetSelectionVisual(Button button, bool selected)
    {
        if (button?.targetGraphic == null)
            return;

        button.targetGraphic.color = selected
            ? FlatWorldUITheme.Accent
            : FlatWorldUITheme.Surface;
    }

    /// <summary>显示设置页显示时重新读取当前模式。</summary>
    public void OnSettingsPageShown()
    {
        RefreshView();
    }

    /// <summary>显示设置页隐藏时无需保留额外草稿。</summary>
    public void OnSettingsPageHidden()
    {
    }

    /// <summary>解除页面按钮监听。</summary>
    private void OnDestroy()
    {
        worldCoordinatesButton?.onClick.RemoveListener(SelectWorldCoordinates);
        latitudeLongitudeButton?.onClick.RemoveListener(SelectLatitudeLongitude);
        showFpsToggle?.onValueChanged.RemoveListener(SetShowFps);
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
