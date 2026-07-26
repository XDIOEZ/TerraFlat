// AI-Context: 设置菜单的自动保存入口及运行时 uGUI 面板；下拉列表提供常用间隔与“永远不自动保存”，输入框提供自定义分钟数。
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class AutoSaveSettingsPanelLauncher : MonoBehaviour
{
    private const string EntryButtonName = "自动保存";
    private const int CustomOptionIndex = 6;

    private static readonly int[] PresetMinutes = { 0, 1, 5, 10, 15, 30, -1 };
    private static readonly List<string> PresetLabels = new List<string>
    {
        "永远不自动保存",
        "每 1 分钟",
        "每 5 分钟",
        "每 10 分钟",
        "每 15 分钟",
        "每 30 分钟",
        "自定义间隔"
    };

    private Button entryButton;
    private GameObject settingsWindow;
    private TMP_Dropdown intervalDropdown;
    private TMP_InputField intervalInput;
    private TextMeshProUGUI statusText;
    private TMP_FontAsset font;

    public static AutoSaveSettingsPanelLauncher Ensure(Transform settingsPanel)
    {
        if (settingsPanel == null)
            return null;

        AutoSaveSettingsPanelLauncher launcher =
            settingsPanel.GetComponent<AutoSaveSettingsPanelLauncher>();
        if (launcher == null)
            launcher = settingsPanel.gameObject.AddComponent<AutoSaveSettingsPanelLauncher>();

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

            if (candidate.name == "音量调节")
                template = candidate;
            else
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
                "[AutoSaveSettingsPanelLauncher] 设置面板中没有可复用的按钮，未能创建“自动保存”入口。",
                this);
            return;
        }

        entryButton.onClick.RemoveAllListeners();
        entryButton.onClick.AddListener(Open);
    }

    private void Open()
    {
        EnsureWindow();
        RefreshControls();
        settingsWindow.SetActive(true);
        settingsWindow.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(settingsWindow.GetComponent<RectTransform>());
    }

    private void EnsureWindow()
    {
        if (settingsWindow != null)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        Transform parent = canvas != null ? canvas.transform : transform.parent;

        settingsWindow = CreateObject("自动保存设置面板", parent);
        RectTransform panelRect = settingsWindow.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(640f, 410f);

        Image panelImage = settingsWindow.AddComponent<Image>();
        panelImage.color = new Color(0.045f, 0.075f, 0.085f, 0.99f);
        Outline outline = settingsWindow.AddComponent<Outline>();
        outline.effectColor = new Color(0.86f, 0.37f, 0.15f, 0.95f);
        outline.effectDistance = new Vector2(2f, -2f);

        VerticalLayoutGroup layout = settingsWindow.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(20, 20, 18, 18);
        layout.spacing = 10f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        CreateHeader();

        TextMeshProUGUI hint = CreateText(
            settingsWindow.transform,
            "自动保存只在游戏世界中运行，并按现实时间计时。设置会立即保存。",
            13f,
            new Color(0.67f, 0.75f, 0.76f));
        hint.enableWordWrapping = true;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;

        CreateDropdownRow();
        CreateInputRow();

        statusText = CreateText(
            settingsWindow.transform,
            string.Empty,
            13f,
            new Color(0.21f, 0.78f, 0.74f));
        statusText.enableWordWrapping = false;
        statusText.overflowMode = TextOverflowModes.Ellipsis;
        statusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        CreateFooter();

        RefreshControls();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        settingsWindow.SetActive(false);
    }

    private void CreateHeader()
    {
        GameObject header = CreateObject("标题", settingsWindow.transform);
        header.AddComponent<LayoutElement>().preferredHeight = 50f;
        header.AddComponent<Image>().color = new Color(0.07f, 0.18f, 0.21f, 1f);

        HorizontalLayoutGroup headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(14, 10, 6, 6);
        headerLayout.spacing = 10f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;
        headerLayout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText(
            header.transform,
            "自动保存",
            21f,
            new Color(0.96f, 0.96f, 0.92f));
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton(header.transform, "关闭", Close, 68f, 34f);
    }

    private void CreateDropdownRow()
    {
        GameObject row = CreateRow("保存模式", 52f);
        CreateRowLabel(row.transform, "保存模式");

        intervalDropdown = CreateDropdown(row.transform);
        intervalDropdown.ClearOptions();
        intervalDropdown.AddOptions(PresetLabels);
        intervalDropdown.onValueChanged.AddListener(OnPresetChanged);
    }

    private void CreateInputRow()
    {
        GameObject row = CreateRow("自定义间隔", 52f);
        CreateRowLabel(row.transform, "间隔（分钟）");

        intervalInput = CreateInputField(row.transform, "输入 1–1440", 340f);

        TextMeshProUGUI range = CreateText(
            row.transform,
            "1–1440",
            12f,
            new Color(0.58f, 0.65f, 0.66f));
        range.alignment = TextAlignmentOptions.MidlineRight;
        range.gameObject.AddComponent<LayoutElement>().preferredWidth = 62f;
    }

    private GameObject CreateRow(string name, float height)
    {
        GameObject row = CreateObject(name, settingsWindow.transform);
        row.AddComponent<LayoutElement>().preferredHeight = height;

        HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 12f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        return row;
    }

    private void CreateRowLabel(Transform parent, string label)
    {
        TextMeshProUGUI labelText = CreateText(
            parent,
            label,
            14f,
            new Color(0.9f, 0.93f, 0.92f));
        labelText.gameObject.AddComponent<LayoutElement>().preferredWidth = 130f;
    }

    private void CreateFooter()
    {
        GameObject footer = CreateObject("底部操作", settingsWindow.transform);
        footer.AddComponent<LayoutElement>().preferredHeight = 42f;

        HorizontalLayoutGroup footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 10f;
        footerLayout.childAlignment = TextAnchor.MiddleRight;
        footerLayout.childControlWidth = false;
        footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = false;
        footerLayout.childForceExpandHeight = false;

        CreateButton(footer.transform, "取消", Close, 82f, 36f);
        CreateButton(footer.transform, "应用", Apply, 92f, 36f, true);
    }

    private void RefreshControls()
    {
        if (intervalDropdown == null || intervalInput == null)
            return;

        int selectedIndex = ResolveCurrentOptionIndex();
        intervalDropdown.SetValueWithoutNotify(selectedIndex);
        intervalInput.SetTextWithoutNotify(AutoSavePreferences.IntervalMinutes.ToString());
        intervalInput.interactable = selectedIndex == CustomOptionIndex;
        SetCurrentStatus();
    }

    private int ResolveCurrentOptionIndex()
    {
        if (!AutoSavePreferences.Enabled)
            return 0;

        int currentMinutes = AutoSavePreferences.IntervalMinutes;
        for (int i = 1; i < PresetMinutes.Length - 1; i++)
        {
            if (PresetMinutes[i] == currentMinutes)
                return i;
        }

        return CustomOptionIndex;
    }

    private void OnPresetChanged(int selectedIndex)
    {
        bool custom = selectedIndex == CustomOptionIndex;
        intervalInput.interactable = custom;

        if (selectedIndex > 0 && selectedIndex < CustomOptionIndex)
            intervalInput.SetTextWithoutNotify(PresetMinutes[selectedIndex].ToString());

        if (selectedIndex == 0)
            SetStatus("已选择：永远不自动保存。点击“应用”后生效。", false);
        else if (custom)
            SetStatus("请输入 1–1440 分钟，然后点击“应用”。", false);
        else
            SetStatus($"已选择：每 {PresetMinutes[selectedIndex]} 分钟自动保存。", false);
    }

    private void Apply()
    {
        int selectedIndex = intervalDropdown.value;
        if (selectedIndex == 0)
        {
            AutoSavePreferences.Disable();
            SetCurrentStatus();
            return;
        }

        int minutes;
        if (selectedIndex == CustomOptionIndex)
        {
            if (!int.TryParse(intervalInput.text, out minutes) ||
                minutes < AutoSavePreferences.MinIntervalMinutes ||
                minutes > AutoSavePreferences.MaxIntervalMinutes)
            {
                SetStatus("请输入 1–1440 之间的整数分钟数。", true);
                return;
            }
        }
        else
        {
            minutes = PresetMinutes[selectedIndex];
        }

        AutoSavePreferences.Enable(minutes);
        intervalInput.SetTextWithoutNotify(minutes.ToString());
        SetCurrentStatus();
    }

    private void SetCurrentStatus()
    {
        if (!AutoSavePreferences.Enabled)
            SetStatus("当前设置：永远不自动保存。", false);
        else
            SetStatus($"当前设置：每 {AutoSavePreferences.IntervalMinutes} 分钟自动保存。", false);
    }

    private void SetStatus(string message, bool isError)
    {
        if (statusText == null)
            return;

        statusText.text = message;
        statusText.color = isError
            ? new Color(0.95f, 0.38f, 0.31f)
            : new Color(0.21f, 0.78f, 0.74f);
    }

    private void Close()
    {
        if (settingsWindow != null)
            settingsWindow.SetActive(false);
    }

    private void OnDestroy()
    {
        if (settingsWindow != null)
            Destroy(settingsWindow);
    }

    private TMP_Dropdown CreateDropdown(Transform parent)
    {
        GameObject root = CreateObject("自动保存间隔下拉列表", parent);
        LayoutElement rootLayout = root.AddComponent<LayoutElement>();
        rootLayout.preferredWidth = 402f;
        rootLayout.preferredHeight = 42f;
        Image rootImage = root.AddComponent<Image>();
        rootImage.color = new Color(0.028f, 0.071f, 0.094f, 1f);
        AddSubtleOutline(root);

        TMP_Dropdown dropdown = root.AddComponent<TMP_Dropdown>();
        dropdown.targetGraphic = rootImage;

        TextMeshProUGUI caption = CreateText(
            root.transform,
            string.Empty,
            14f,
            new Color(0.95f, 0.91f, 0.84f));
        caption.name = "Label";
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.rectTransform.anchorMin = Vector2.zero;
        caption.rectTransform.anchorMax = Vector2.one;
        caption.rectTransform.offsetMin = new Vector2(12f, 2f);
        caption.rectTransform.offsetMax = new Vector2(-42f, -2f);
        dropdown.captionText = caption;

        TextMeshProUGUI arrow = CreateText(
            root.transform,
            "▼",
            13f,
            new Color(0.86f, 0.57f, 0.31f));
        arrow.name = "Arrow";
        arrow.alignment = TextAlignmentOptions.Center;
        arrow.rectTransform.anchorMin = new Vector2(1f, 0f);
        arrow.rectTransform.anchorMax = Vector2.one;
        arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
        arrow.rectTransform.sizeDelta = new Vector2(36f, 0f);
        arrow.rectTransform.anchoredPosition = Vector2.zero;

        GameObject templateObject = CreateObject("Template", root.transform);
        RectTransform templateRect = templateObject.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0f, 0f);
        templateRect.anchorMax = new Vector2(1f, 0f);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.anchoredPosition = new Vector2(0f, -3f);
        templateRect.sizeDelta = new Vector2(0f, 224f);
        Image templateImage = templateObject.AddComponent<Image>();
        templateImage.color = new Color(0.035f, 0.09f, 0.11f, 1f);
        AddSubtleOutline(templateObject);

        ScrollRect scrollRect = templateObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        GameObject viewportObject = CreateObject("Viewport", templateObject.transform);
        RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(3f, 3f);
        viewportRect.offsetMax = new Vector2(-3f, -3f);
        viewportObject.AddComponent<RectMask2D>();

        GameObject contentObject = CreateObject("Content", viewportObject.transform);
        RectTransform contentRect = contentObject.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup contentLayout = contentObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;
        ContentSizeFitter fitter = contentObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject itemObject = CreateObject("Item", contentObject.transform);
        itemObject.AddComponent<LayoutElement>().preferredHeight = 31f;
        Image itemBackground = itemObject.AddComponent<Image>();
        itemBackground.color = new Color(0.075f, 0.18f, 0.20f, 1f);
        Toggle itemToggle = itemObject.AddComponent<Toggle>();
        itemToggle.targetGraphic = itemBackground;

        GameObject checkmarkObject = CreateObject("Item Checkmark", itemObject.transform);
        RectTransform checkmarkRect = checkmarkObject.GetComponent<RectTransform>();
        checkmarkRect.anchorMin = new Vector2(0f, 0.5f);
        checkmarkRect.anchorMax = new Vector2(0f, 0.5f);
        checkmarkRect.pivot = new Vector2(0f, 0.5f);
        checkmarkRect.anchoredPosition = new Vector2(10f, 0f);
        checkmarkRect.sizeDelta = new Vector2(8f, 18f);
        Image checkmark = checkmarkObject.AddComponent<Image>();
        checkmark.color = new Color(0.95f, 0.62f, 0.28f, 1f);
        itemToggle.graphic = checkmark;

        TextMeshProUGUI itemLabel = CreateText(
            itemObject.transform,
            "选项",
            13f,
            new Color(0.95f, 0.91f, 0.84f));
        itemLabel.name = "Item Label";
        itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
        itemLabel.rectTransform.anchorMin = Vector2.zero;
        itemLabel.rectTransform.anchorMax = Vector2.one;
        itemLabel.rectTransform.offsetMin = new Vector2(28f, 1f);
        itemLabel.rectTransform.offsetMax = new Vector2(-8f, -1f);

        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        dropdown.template = templateRect;
        dropdown.itemText = itemLabel;
        templateObject.SetActive(false);
        return dropdown;
    }

    private TMP_InputField CreateInputField(Transform parent, string placeholder, float width)
    {
        GameObject root = CreateObject("自动保存间隔输入框", parent);
        LayoutElement rootLayout = root.AddComponent<LayoutElement>();
        rootLayout.preferredWidth = width;
        rootLayout.preferredHeight = 42f;
        Image image = root.AddComponent<Image>();
        image.color = new Color(0.028f, 0.071f, 0.094f, 1f);
        AddSubtleOutline(root);

        TMP_InputField field = root.AddComponent<TMP_InputField>();
        field.targetGraphic = image;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.contentType = TMP_InputField.ContentType.IntegerNumber;

        GameObject textArea = CreateObject("Text Area", root.transform);
        RectTransform areaRect = textArea.GetComponent<RectTransform>();
        areaRect.anchorMin = Vector2.zero;
        areaRect.anchorMax = Vector2.one;
        areaRect.offsetMin = new Vector2(12f, 3f);
        areaRect.offsetMax = new Vector2(-12f, -3f);
        textArea.AddComponent<RectMask2D>();
        field.textViewport = areaRect;

        TextMeshProUGUI valueText = CreateText(
            textArea.transform,
            string.Empty,
            14f,
            new Color(0.95f, 0.91f, 0.84f));
        valueText.alignment = TextAlignmentOptions.MidlineLeft;
        valueText.rectTransform.anchorMin = Vector2.zero;
        valueText.rectTransform.anchorMax = Vector2.one;
        valueText.rectTransform.offsetMin = Vector2.zero;
        valueText.rectTransform.offsetMax = Vector2.zero;
        field.textComponent = valueText;

        TextMeshProUGUI placeholderText = CreateText(
            textArea.transform,
            placeholder,
            13f,
            new Color(0.51f, 0.57f, 0.58f));
        placeholderText.fontStyle = FontStyles.Italic;
        placeholderText.rectTransform.anchorMin = Vector2.zero;
        placeholderText.rectTransform.anchorMax = Vector2.one;
        placeholderText.rectTransform.offsetMin = Vector2.zero;
        placeholderText.rectTransform.offsetMax = Vector2.zero;
        field.placeholder = placeholderText;
        return field;
    }

    private Button CreateButton(
        Transform parent,
        string label,
        UnityAction action,
        float width,
        float height,
        bool primary = false)
    {
        GameObject buttonObject = CreateObject(label, parent);
        Image image = buttonObject.AddComponent<Image>();
        image.color = primary
            ? new Color(0.70f, 0.29f, 0.10f, 1f)
            : new Color(0.12f, 0.31f, 0.34f, 1f);

        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = height;

        TextMeshProUGUI text = CreateText(
            buttonObject.transform,
            label,
            13f,
            new Color(0.95f, 0.96f, 0.93f));
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = Vector2.zero;
        text.rectTransform.offsetMax = Vector2.zero;
        return button;
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

    private static void AddSubtleOutline(GameObject target)
    {
        Outline outline = target.AddComponent<Outline>();
        outline.effectColor = new Color(0.51f, 0.58f, 0.58f, 0.28f);
        outline.effectDistance = new Vector2(1f, -1f);
    }

    private static void SetButtonLabel(Button button, string label)
    {
        TextMeshProUGUI tmpText = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmpText != null)
        {
            tmpText.text = label;
            return;
        }

        Text legacyText = button.GetComponentInChildren<Text>(true);
        if (legacyText != null)
            legacyText.text = label;
    }

    private static GameObject CreateObject(string name, Transform parent)
    {
        GameObject value = new GameObject(name, typeof(RectTransform));
        value.transform.SetParent(parent, false);
        return value;
    }
}
