// AI-Context: 设置菜单的“UI设置”入口和运行时 uGUI 面板；提供全局界面缩放、安全区适配及即时持久化。

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
    private GameObject settingsWindow;
    private Slider scaleSlider;
    private Toggle safeAreaToggle;
    private TextMeshProUGUI scaleValueText;
    private TextMeshProUGUI statusText;
    private TMP_FontAsset font;
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
        if (entryButton != null)
            return;

        Button[] buttons = GetComponentsInChildren<Button>(true);
        Button template = null;
        for (int i = 0; i < buttons.Length; i++)
        {
            Button candidate = buttons[i];
            if (candidate == null)
                continue;

            if (candidate.name == EntryButtonName)
            {
                entryButton = candidate;
                break;
            }

            template ??= candidate;
        }

        if (entryButton == null && template != null)
        {
            GameObject clone = Instantiate(template.gameObject, template.transform.parent);
            clone.name = EntryButtonName;
            clone.SetActive(true);
            entryButton = clone.GetComponent<Button>();
            SetButtonLabel(entryButton, EntryButtonName);
        }

        if (entryButton == null)
        {
            Debug.LogWarning(
                "[UISettingsPanelLauncher] 设置面板中没有可复用按钮，未能创建“UI设置”入口。",
                this);
            return;
        }

        entryButton.onClick.RemoveAllListeners();
        entryButton.onClick.AddListener(Open);
    }

    private void Open()
    {
        EnsureSettingsWindow();
        RefreshValues();
        ClampWindowToCanvas();
        settingsWindow.SetActive(true);
        settingsWindow.transform.SetAsLastSibling();
    }

    private void EnsureSettingsWindow()
    {
        if (settingsWindow != null)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        Transform parent = canvas != null ? canvas.transform : transform.parent;
        settingsWindow = CreateObject("UI设置面板", parent);

        RectTransform panelRect = settingsWindow.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;

        Image panelImage = settingsWindow.AddComponent<Image>();
        panelImage.color = new Color(0.045f, 0.075f, 0.085f, 0.99f);
        Outline outline = settingsWindow.AddComponent<Outline>();
        outline.effectColor = new Color(0.86f, 0.37f, 0.15f, 0.95f);
        outline.effectDistance = new Vector2(2f, -2f);

        VerticalLayoutGroup panelLayout = settingsWindow.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(20, 20, 18, 18);
        panelLayout.spacing = 10f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        CreateHeader();
        CreateHint();
        CreateScaleRow();
        CreateSafeAreaRow();
        CreateStatus();
        CreateFooter();

        ClampWindowToCanvas();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        FlatWorldUITheme.Apply(settingsWindow.transform);
        settingsWindow.SetActive(false);
    }

    private void CreateHeader()
    {
        GameObject header = CreateObject("标题", settingsWindow.transform);
        header.AddComponent<LayoutElement>().preferredHeight = 50f;
        header.AddComponent<Image>().color = new Color(0.07f, 0.18f, 0.21f, 1f);

        HorizontalLayoutGroup layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 10, 6, 6);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI title =
            CreateText(header.transform, "UI设置", 21f, new Color(0.96f, 0.96f, 0.92f));
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton(header.transform, "关闭", Close, 68f, 34f);
    }

    private void CreateHint()
    {
        TextMeshProUGUI hint = CreateText(
            settingsWindow.transform,
            "调整会立即应用并自动保存。缩放范围经过限制，界面仍会按屏幕宽高比保持在可见区域内。",
            13f,
            new Color(0.67f, 0.75f, 0.76f));
        hint.enableWordWrapping = true;
        hint.overflowMode = TextOverflowModes.Truncate;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
    }

    private void CreateScaleRow()
    {
        GameObject row = CreateObject("界面缩放行", settingsWindow.transform);
        row.AddComponent<LayoutElement>().preferredHeight = 52f;

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI label =
            CreateText(row.transform, "界面缩放", 14f, new Color(0.9f, 0.93f, 0.92f));
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = 96f;

        scaleSlider = CreateSlider(row.transform, "界面缩放");
        scaleSlider.minValue = UIUserSettings.MinimumScale;
        scaleSlider.maxValue = UIUserSettings.MaximumScale;
        scaleSlider.wholeNumbers = false;
        scaleSlider.onValueChanged.AddListener(OnScaleChanged);

        scaleValueText =
            CreateText(row.transform, "100%", 13f, new Color(0.86f, 0.57f, 0.31f));
        scaleValueText.alignment = TextAlignmentOptions.MidlineRight;
        scaleValueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 52f;
    }

    private void CreateSafeAreaRow()
    {
        GameObject row = CreateObject("安全区域适配行", settingsWindow.transform);
        row.AddComponent<LayoutElement>().preferredHeight = 46f;

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI label = CreateText(
            row.transform,
            "适配屏幕安全区域",
            14f,
            new Color(0.9f, 0.93f, 0.92f));
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        safeAreaToggle = CreateToggle(row.transform, "安全区域适配");
        safeAreaToggle.onValueChanged.AddListener(OnSafeAreaChanged);
    }

    private void CreateStatus()
    {
        statusText = CreateText(
            settingsWindow.transform,
            string.Empty,
            13f,
            new Color(0.67f, 0.75f, 0.76f));
        statusText.enableWordWrapping = true;
        statusText.overflowMode = TextOverflowModes.Truncate;
        statusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
    }

    private void CreateFooter()
    {
        GameObject footer = CreateObject("底部操作", settingsWindow.transform);
        footer.AddComponent<LayoutElement>().preferredHeight = 38f;

        HorizontalLayoutGroup layout = footer.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        CreateButton(footer.transform, "恢复默认", ResetToDefault, 96f, 34f);
        CreateButton(footer.transform, "完成", Close, 74f, 34f);
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
                ? "安全区域适配：开启（推荐）"
                : "安全区域适配：关闭";
        }
    }

    private void Close()
    {
        if (settingsWindow != null)
            settingsWindow.SetActive(false);
    }

    private void OnRectTransformDimensionsChange()
    {
        if (settingsWindow != null)
            ClampWindowToCanvas();
    }

    private void OnDestroy()
    {
        if (settingsWindow != null)
            Destroy(settingsWindow);
    }

    private void ClampWindowToCanvas()
    {
        if (settingsWindow == null || isClamping)
            return;

        Canvas canvas = settingsWindow.GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null ? canvas.transform as RectTransform : null;
        RectTransform panelRect = settingsWindow.transform as RectTransform;
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

    private Slider CreateSlider(Transform parent, string objectName)
    {
        GameObject sliderObject = CreateObject(objectName, parent);
        sliderObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        Image background = sliderObject.AddComponent<Image>();
        background.color = new Color(0.14f, 0.23f, 0.25f, 1f);
        Slider slider = sliderObject.AddComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;

        GameObject fillArea = CreateObject("Fill Area", sliderObject.transform);
        RectTransform fillAreaRect = fillArea.GetComponent<RectTransform>();
        fillAreaRect.anchorMin = Vector2.zero;
        fillAreaRect.anchorMax = Vector2.one;
        fillAreaRect.offsetMin = new Vector2(8f, 8f);
        fillAreaRect.offsetMax = new Vector2(-8f, -8f);

        GameObject fill = CreateObject("Fill", fillArea.transform);
        Image fillImage = fill.AddComponent<Image>();
        fillImage.color = new Color(0.18f, 0.72f, 0.69f, 1f);
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.pivot = new Vector2(0f, 0.5f);
        fillRect.sizeDelta = new Vector2(10f, 0f);
        slider.fillRect = fillRect;

        GameObject handleArea = CreateObject("Handle Slide Area", sliderObject.transform);
        RectTransform handleAreaRect = handleArea.GetComponent<RectTransform>();
        handleAreaRect.anchorMin = Vector2.zero;
        handleAreaRect.anchorMax = Vector2.one;
        handleAreaRect.offsetMin = new Vector2(5f, 0f);
        handleAreaRect.offsetMax = new Vector2(-5f, 0f);

        GameObject handle = CreateObject("Handle", handleArea.transform);
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = new Color(0.95f, 0.62f, 0.28f, 1f);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(16f, 28f);
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        return slider;
    }

    private Toggle CreateToggle(Transform parent, string objectName)
    {
        GameObject toggleObject = CreateObject(objectName, parent);
        LayoutElement element = toggleObject.AddComponent<LayoutElement>();
        element.preferredWidth = 58f;
        element.preferredHeight = 30f;

        Image background = toggleObject.AddComponent<Image>();
        background.color = new Color(0.14f, 0.23f, 0.25f, 1f);
        Toggle toggle = toggleObject.AddComponent<Toggle>();
        toggle.targetGraphic = background;

        GameObject checkmarkObject = CreateObject("Checkmark", toggleObject.transform);
        RectTransform checkmarkRect = checkmarkObject.GetComponent<RectTransform>();
        checkmarkRect.anchorMin = new Vector2(0.5f, 0.5f);
        checkmarkRect.anchorMax = new Vector2(0.5f, 0.5f);
        checkmarkRect.sizeDelta = new Vector2(42f, 18f);
        Image checkmark = checkmarkObject.AddComponent<Image>();
        checkmark.color = new Color(0.18f, 0.72f, 0.69f, 1f);
        toggle.graphic = checkmark;
        return toggle;
    }

    private TextMeshProUGUI CreateText(Transform parent, string value, float size, Color color)
    {
        GameObject textObject = CreateObject("Text", parent);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = ResolveFont();
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        return text;
    }

    private Button CreateButton(
        Transform parent,
        string label,
        UnityEngine.Events.UnityAction action,
        float width,
        float height)
    {
        GameObject buttonObject = CreateObject(label, parent);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.12f, 0.31f, 0.34f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        LayoutElement element = buttonObject.AddComponent<LayoutElement>();
        element.preferredWidth = width;
        element.preferredHeight = height;

        TextMeshProUGUI text =
            CreateText(buttonObject.transform, label, 13f, new Color(0.95f, 0.96f, 0.93f));
        text.alignment = TextAlignmentOptions.Center;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        return button;
    }

    private void SetButtonLabel(Button button, string label)
    {
        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
        {
            text.text = label;
            return;
        }

        Text legacyText = button.GetComponentInChildren<Text>(true);
        if (legacyText != null)
            legacyText.text = label;
    }

    private TMP_FontAsset ResolveFont()
    {
        if (font != null)
            return font;

        TextMeshProUGUI existing = GetComponentInChildren<TextMeshProUGUI>(true);
        font = existing != null && existing.font != null
            ? existing.font
            : TMP_Settings.defaultFontAsset;
        return font;
    }

    private static GameObject CreateObject(string name, Transform parent)
    {
        GameObject value = new GameObject(name, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        return value;
    }

    private static string ToPercent(float value)
    {
        return Mathf.RoundToInt(value * 100f) + "%";
    }
}
