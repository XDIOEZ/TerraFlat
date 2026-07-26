// AI-Context: 设置面板的音量入口与独立音量窗口；只使用 AudioService 的公开总线 API，不直接操作 AudioSource。
using System.Collections.Generic;
using FlatWorld.Audio;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class AudioSettingsPanelLauncher : MonoBehaviour
{
    private const string EntryButtonName = "音量调节";

    private sealed class VolumeRow
    {
        public Slider Slider;
        public TextMeshProUGUI ValueText;
    }

    private readonly List<VolumeRow> rows = new List<VolumeRow>(6);
    private Button entryButton;
    private GameObject volumePanel;
    private TMP_FontAsset font;

    public static AudioSettingsPanelLauncher Ensure(Transform settingsPanel)
    {
        if (settingsPanel == null)
            return null;

        AudioSettingsPanelLauncher launcher = settingsPanel.GetComponent<AudioSettingsPanelLauncher>();
        if (launcher == null)
            launcher = settingsPanel.gameObject.AddComponent<AudioSettingsPanelLauncher>();
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
            if (buttons[i] == null)
                continue;

            if (buttons[i].name == EntryButtonName)
            {
                entryButton = buttons[i];
                break;
            }

            template ??= buttons[i];
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
            Debug.LogWarning("[AudioSettingsPanelLauncher] 设置面板中没有可复用的按钮，未能创建“音量调节”入口。", this);
            return;
        }

        entryButton.onClick.RemoveAllListeners();
        entryButton.onClick.AddListener(Open);
    }

    private void Open()
    {
        EnsureVolumePanel();
        volumePanel.SetActive(true);
        volumePanel.transform.SetAsLastSibling();
        AudioSettingsPanelBinder.Ensure(volumePanel.transform);
        RefreshValues();
    }

    private void EnsureVolumePanel()
    {
        if (volumePanel != null)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        Transform parent = canvas != null ? canvas.transform : transform.parent;
        volumePanel = CreateObject("音量调节面板", parent);
        RectTransform panelRect = volumePanel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(620f, 515f);

        Image panelImage = volumePanel.AddComponent<Image>();
        panelImage.color = new Color(0.045f, 0.075f, 0.085f, 0.99f);
        Outline outline = volumePanel.AddComponent<Outline>();
        outline.effectColor = new Color(0.86f, 0.37f, 0.15f, 0.95f);
        outline.effectDistance = new Vector2(2f, -2f);

        VerticalLayoutGroup panelLayout = volumePanel.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(20, 20, 18, 18);
        panelLayout.spacing = 10f;
        panelLayout.childControlWidth = true;
        // 子项通过 LayoutElement 声明高度，布局组必须接管高度才能防止使用默认 100 高度而超框。
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        GameObject header = CreateObject("标题", volumePanel.transform);
        header.AddComponent<LayoutElement>().preferredHeight = 50f;
        header.AddComponent<Image>().color = new Color(0.07f, 0.18f, 0.21f, 1f);
        HorizontalLayoutGroup headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(14, 10, 6, 6);
        headerLayout.spacing = 10f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childForceExpandWidth = false;

        TextMeshProUGUI title = CreateText(header.transform, "音量调节", 21f, new Color(0.96f, 0.96f, 0.92f));
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton(header.transform, "关闭", Close, 68f, 34f);

        TextMeshProUGUI hint = CreateText(
            volumePanel.transform,
            "主音量控制全部声音；音乐、音效和界面音量可分别调整。设置会自动保存。",
            13f,
            new Color(0.67f, 0.75f, 0.76f));
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

        AudioUserSettings settings = AudioService.Instance.UserSettings;
        AddSliderRow("主音量", "MasterVolume", settings.Master);
        AddSliderRow("音乐音量", "MusicVolume", settings.Music);
        AddSliderRow("音效音量", "SfxVolume", settings.Sfx);
        AddSliderRow("UI 音量", "UIVolume", settings.UI);
        AddSliderRow("环境音量", "AmbientVolume", settings.Ambient);
        AddSliderRow("语音音量", "VoiceVolume", settings.Voice);

        GameObject footer = CreateObject("底部操作", volumePanel.transform);
        footer.AddComponent<LayoutElement>().preferredHeight = 38f;
        HorizontalLayoutGroup footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 10f;
        footerLayout.childAlignment = TextAnchor.MiddleRight;
        footerLayout.childControlWidth = false;
        footerLayout.childForceExpandWidth = false;
        CreateButton(footer.transform, "恢复默认", ResetToDefault, 96f, 34f);
        CreateButton(footer.transform, "完成", Close, 74f, 34f);

        AudioSettingsPanelBinder.Ensure(volumePanel.transform);
        RefreshValues();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        volumePanel.SetActive(false);
    }

    private void AddSliderRow(string label, string sliderName, float value)
    {
        GameObject row = CreateObject(label + "行", volumePanel.transform);
        row.AddComponent<LayoutElement>().preferredHeight = 38f;
        HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 12f;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;

        TextMeshProUGUI labelText = CreateText(row.transform, label, 14f, new Color(0.9f, 0.93f, 0.92f));
        labelText.gameObject.AddComponent<LayoutElement>().preferredWidth = 88f;

        Slider slider = CreateSlider(row.transform, sliderName);
        slider.SetValueWithoutNotify(value);

        TextMeshProUGUI valueText = CreateText(row.transform, ToPercent(value), 13f, new Color(0.86f, 0.57f, 0.31f));
        valueText.alignment = TextAlignmentOptions.MidlineRight;
        valueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 48f;

        slider.onValueChanged.AddListener(changedValue => valueText.text = ToPercent(changedValue));
        rows.Add(new VolumeRow { Slider = slider, ValueText = valueText });
    }

    private void ResetToDefault()
    {
        AudioService.Instance.ResetUserSettings();
        AudioSettingsPanelBinder.Ensure(volumePanel.transform);
        RefreshValues();
    }

    private void RefreshValues()
    {
        if (volumePanel == null)
            return;

        AudioUserSettings settings = AudioService.Instance.UserSettings;
        float[] values = { settings.Master, settings.Music, settings.Sfx, settings.UI, settings.Ambient, settings.Voice };
        for (int i = 0; i < rows.Count && i < values.Length; i++)
        {
            rows[i].Slider.SetValueWithoutNotify(values[i]);
            rows[i].ValueText.text = ToPercent(values[i]);
        }
    }

    private void Close()
    {
        if (volumePanel != null)
            volumePanel.SetActive(false);
    }

    private void OnDestroy()
    {
        if (volumePanel != null)
            Destroy(volumePanel);
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

    private Slider CreateSlider(Transform parent, string objectName)
    {
        GameObject sliderObject = CreateObject(objectName, parent);
        sliderObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Image background = sliderObject.AddComponent<Image>();
        background.color = new Color(0.14f, 0.23f, 0.25f, 1f);
        Slider slider = sliderObject.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
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
        handleRect.sizeDelta = new Vector2(16f, 26f);
        slider.handleRect = handleRect;
        slider.targetGraphic = handleImage;
        return slider;
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

    private Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action, float width, float height)
    {
        GameObject buttonObject = CreateObject(label, parent);
        Image image = buttonObject.AddComponent<Image>();
        image.color = new Color(0.12f, 0.31f, 0.34f, 1f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);
        buttonObject.AddComponent<LayoutElement>().preferredWidth = width;
        buttonObject.GetComponent<LayoutElement>().preferredHeight = height;

        TextMeshProUGUI text = CreateText(buttonObject.transform, label, 13f, new Color(0.95f, 0.96f, 0.93f));
        text.alignment = TextAlignmentOptions.Center;
        RectTransform textRect = text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        return button;
    }

    private TMP_FontAsset ResolveFont()
    {
        if (font != null)
            return font;

        TextMeshProUGUI existing = GetComponentInChildren<TextMeshProUGUI>(true);
        font = existing != null && existing.font != null ? existing.font : TMP_Settings.defaultFontAsset;
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
        return Mathf.RoundToInt(Mathf.Clamp01(value) * 100f) + "%";
    }
}
