// AI-Context: 设置菜单的“UI设置”入口和运行时 uGUI 面板；提供全局界面缩放、安全区适配及即时持久化。

using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class UISettingsPanelLauncher : MonoBehaviour
{
    private const string EntryButtonName = "UI设置";
    private const float PreferredWidth = 620f;
    private const float PreferredHeight = 390f;
    private const float CanvasSafeMargin = 32f;

    private Button entryButton;
    private BasePanel settingsPanel;
    private Slider scaleSlider;
    private Toggle safeAreaToggle;
    private TextMeshProUGUI scaleValueText;
    private TextMeshProUGUI statusText;
    private bool isClamping;

    public static UISettingsPanelLauncher Ensure(Transform settingsPanel)
    {
        if (settingsPanel == null)
            return null;

        UISettingsPanelLauncher launcher =
            settingsPanel.GetComponent<UISettingsPanelLauncher>();
        if (launcher == null)
            launcher = settingsPanel.gameObject.AddComponent<UISettingsPanelLauncher>();
        launcher.EnsureEntryButton();
        return launcher;
    }

private void EnsureEntryButton()
    {
        if (entryButton == null)
            entryButton = FindButton(transform, EntryButtonName);

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
        scaleSlider = settingsPanel.GetSlider("界面缩放");
        safeAreaToggle = settingsPanel.GetToggle("安全区域适配");
        scaleValueText = settingsPanel.GetText("界面缩放数值");
        statusText = settingsPanel.GetText("状态文本");

        settingsPanel.GetButton("关闭按钮")?.onClick.AddListener(Close);
        settingsPanel.GetButton("恢复默认按钮")?.onClick.AddListener(ResetToDefault);
        settingsPanel.GetButton("完成按钮")?.onClick.AddListener(Close);
        scaleSlider?.onValueChanged.AddListener(OnScaleChanged);
        safeAreaToggle?.onValueChanged.AddListener(OnSafeAreaChanged);

        if (scaleSlider == null || safeAreaToggle == null || scaleValueText == null || statusText == null)
            Debug.LogError("[UISettingsPanelLauncher] UI 设置 Prefab 控件命名契约不完整。", settingsPanel);

        if (scaleSlider != null)
        {
            scaleSlider.minValue = UIUserSettings.MinimumScale;
            scaleSlider.maxValue = UIUserSettings.MaximumScale;
            scaleSlider.wholeNumbers = false;
        }

        ClampWindowToCanvas();
        settingsPanel.PrepareForGamepadNavigation("界面缩放");
        settingsPanel.Close();
    }













    private void OnScaleChanged(float value)
    {
        float applied = UIUserSettings.SetScale(value);
        scaleSlider.SetValueWithoutNotify(applied);
        RefreshStatus();
        ClampWindowToCanvas();
    }

    private void OnSafeAreaChanged(bool value)
    {
        UIUserSettings.SetRespectSafeArea(value);
        RefreshStatus();
        ClampWindowToCanvas();
    }

    private void ResetToDefault()
    {
        UIUserSettings.ResetToDefaults();
        RefreshValues();
        ClampWindowToCanvas();
    }

    private void RefreshValues()
    {
        if (scaleSlider != null)
            scaleSlider.SetValueWithoutNotify(UIUserSettings.Scale);
        if (safeAreaToggle != null)
            safeAreaToggle.SetIsOnWithoutNotify(UIUserSettings.RespectSafeArea);
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (scaleValueText != null)
            scaleValueText.text = ToPercent(UIUserSettings.Scale);
        if (statusText != null)
        {
            statusText.text = UIUserSettings.RespectSafeArea
                ? FlatWorldLocalizationService.GetUiText("安全区域适配：开启（推荐）")
                : FlatWorldLocalizationService.GetUiText("安全区域适配：关闭");
        }
    }

private void Close()
    {
        settingsPanel?.Close();
    }

private void OnRectTransformDimensionsChange()
    {
        if (settingsPanel != null)
            ClampWindowToCanvas();
    }

private void OnDestroy()
    {
        if (entryButton != null)
            entryButton.onClick.RemoveListener(Open);
        if (settingsPanel != null)
            Destroy(settingsPanel.gameObject);
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
            float width = Mathf.Min(PreferredWidth, Mathf.Max(1f, available.x));
            float height = Mathf.Min(PreferredHeight, Mathf.Max(1f, available.y));
            panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            panelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            panelRect.anchoredPosition = Vector2.zero;
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        }
        finally
        {
            isClamping = false;
        }
    }















    private static string ToPercent(float value)
    {
        return Mathf.RoundToInt(value * 100f) + "%";
    }


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
