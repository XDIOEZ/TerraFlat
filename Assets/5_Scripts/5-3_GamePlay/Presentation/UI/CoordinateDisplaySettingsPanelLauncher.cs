using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 从游戏内设置列表打开“显示设置”页面，并持久化左上角 HUD 的世界坐标/经纬度显示方式。
/// 页面只控制本地展示，不改变世界坐标或存档数据。
/// </summary>
[DisallowMultipleComponent]
public sealed class CoordinateDisplaySettingsPanelLauncher : MonoBehaviour
{
    #region 控件命名与布局参数

    private const string EntryButtonName = "显示设置";
    private const string WorldCoordinatesButtonName = "世界坐标模式按钮";
    private const string LatitudeLongitudeButtonName = "经纬度模式按钮";
    private const string StatusTextName = "状态文本";
    private const string CloseButtonName = "关闭按钮";
    private const string CompleteButtonName = "完成按钮";
    private const float PreferredWidth = 620f;
    private const float PreferredHeight = 360f;
    private const float CanvasSafeMargin = 32f;

    #endregion

    #region 运行时引用

    private Button entryButton;
    private BasePanel settingsPanel;
    private Button worldCoordinatesButton;
    private Button latitudeLongitudeButton;
    private TextMeshProUGUI statusText;
    private bool isClamping;

    #endregion

    #region 初始化与打开

    /// <summary>将显示设置入口绑定到游戏内设置列表。</summary>
    public static CoordinateDisplaySettingsPanelLauncher Ensure(Transform settingsRoot)
    {
        if (settingsRoot == null)
            return null;

        CoordinateDisplaySettingsPanelLauncher launcher =
            settingsRoot.GetComponent<CoordinateDisplaySettingsPanelLauncher>();
        if (launcher == null)
            launcher = settingsRoot.gameObject.AddComponent<CoordinateDisplaySettingsPanelLauncher>();

        launcher.EnsureEntryButton();
        return launcher;
    }

    private void EnsureEntryButton()
    {
        entryButton ??= FindButton(transform, EntryButtonName);
        if (entryButton == null)
        {
            Debug.LogError(
                $"[CoordinateDisplaySettingsPanelLauncher] Prefab 缺少入口按钮“{EntryButtonName}”。",
                this);
            return;
        }

        entryButton.onClick.RemoveListener(Open);
        entryButton.onClick.AddListener(Open);
    }

    private void Open()
    {
        EnsureSettingsPanel();
        if (settingsPanel == null)
            return;

        RefreshView();
        ClampWindowToCanvas();
        settingsPanel.PrepareForGamepadNavigation(
            PlayerWorldCoordinateDisplayPreferences.Mode ==
            PlayerWorldCoordinateDisplayMode.LatitudeLongitude
                ? LatitudeLongitudeButtonName
                : WorldCoordinatesButtonName);
        settingsPanel.Open();
        settingsPanel.transform.SetAsLastSibling();
    }

    private void EnsureSettingsPanel()
    {
        if (settingsPanel != null)
            return;

        GameObject prefab = GameRes.Instance?.GetPrefab(
            RuntimeUIPrefabKeys.CoordinateDisplaySettings);
        if (prefab == null)
        {
            Debug.LogError(
                $"[CoordinateDisplaySettingsPanelLauncher] 缺少 Prefab：{RuntimeUIPrefabKeys.CoordinateDisplaySettings}。",
                this);
            return;
        }

        settingsPanel = UIManager.Instance.CreatePanelFromGameObject(
            prefab,
            RuntimeUIPrefabKeys.CoordinateDisplaySettings);
        worldCoordinatesButton = settingsPanel.GetButton(WorldCoordinatesButtonName);
        latitudeLongitudeButton = settingsPanel.GetButton(LatitudeLongitudeButtonName);
        statusText = settingsPanel.GetText(StatusTextName);

        settingsPanel.GetButton(CloseButtonName)?.onClick.AddListener(Close);
        settingsPanel.GetButton(CompleteButtonName)?.onClick.AddListener(Close);
        worldCoordinatesButton?.onClick.AddListener(SelectWorldCoordinates);
        latitudeLongitudeButton?.onClick.AddListener(SelectLatitudeLongitude);

        if (worldCoordinatesButton == null || latitudeLongitudeButton == null || statusText == null)
            Debug.LogError("[CoordinateDisplaySettingsPanelLauncher] 显示设置 Prefab 控件命名契约不完整。", settingsPanel);

        ClampWindowToCanvas();
        settingsPanel.PrepareForGamepadNavigation(WorldCoordinatesButtonName);
        settingsPanel.Close();
    }

    #endregion

    #region 设置应用

    private void SelectWorldCoordinates()
    {
        SetDisplayMode(PlayerWorldCoordinateDisplayMode.WorldCoordinates);
    }

    private void SelectLatitudeLongitude()
    {
        SetDisplayMode(PlayerWorldCoordinateDisplayMode.LatitudeLongitude);
    }

    private void SetDisplayMode(PlayerWorldCoordinateDisplayMode mode)
    {
        PlayerWorldCoordinateDisplayPreferences.SetMode(mode);
        RefreshView();
    }

    private void RefreshView()
    {
        PlayerWorldCoordinateDisplayMode mode = PlayerWorldCoordinateDisplayPreferences.Mode;
        SetSelectionVisual(
            worldCoordinatesButton,
            mode == PlayerWorldCoordinateDisplayMode.WorldCoordinates);
        SetSelectionVisual(
            latitudeLongitudeButton,
            mode == PlayerWorldCoordinateDisplayMode.LatitudeLongitude);

        if (statusText != null)
        {
            statusText.text = mode == PlayerWorldCoordinateDisplayMode.LatitudeLongitude
                ? FlatWorldLocalizationService.GetUiText("当前显示：经纬度（经度 / 纬度）")
                : FlatWorldLocalizationService.GetUiText("当前显示：世界坐标（X / Y）");
        }
    }

    private static void SetSelectionVisual(Button button, bool selected)
    {
        if (button?.targetGraphic == null)
            return;

        button.targetGraphic.color = selected
            ? FlatWorldUITheme.Accent
            : FlatWorldUITheme.Surface;
    }

    #endregion

    #region 生命周期与布局

    private void OnRectTransformDimensionsChange()
    {
        if (settingsPanel != null)
            ClampWindowToCanvas();
    }

    private void OnDestroy()
    {
        if (entryButton != null)
            entryButton.onClick.RemoveListener(Open);
        if (worldCoordinatesButton != null)
            worldCoordinatesButton.onClick.RemoveListener(SelectWorldCoordinates);
        if (latitudeLongitudeButton != null)
            latitudeLongitudeButton.onClick.RemoveListener(SelectLatitudeLongitude);
        if (settingsPanel != null)
            Destroy(settingsPanel.gameObject);
    }

    private void Close()
    {
        settingsPanel?.Close();
    }

    private void ClampWindowToCanvas()
    {
        if (settingsPanel == null || isClamping)
            return;

        Canvas canvas = settingsPanel.GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        RectTransform panelRect = settingsPanel.rectTransform;
        if (canvasRect == null || panelRect == null)
            return;

        isClamping = true;
        try
        {
            Canvas.ForceUpdateCanvases();
            Vector2 available = canvasRect.rect.size -
                                new Vector2(CanvasSafeMargin * 2f, CanvasSafeMargin * 2f);
            panelRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                Mathf.Min(PreferredWidth, Mathf.Max(1f, available.x)));
            panelRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                Mathf.Min(PreferredHeight, Mathf.Max(1f, available.y)));
            panelRect.anchoredPosition = Vector2.zero;
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        }
        finally
        {
            isClamping = false;
        }
    }

    private static Button FindButton(Transform root, string buttonName)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int index = 0; index < buttons.Length; index++)
        {
            if (buttons[index] != null && buttons[index].name == buttonName)
                return buttons[index];
        }

        return null;
    }

    #endregion
}
