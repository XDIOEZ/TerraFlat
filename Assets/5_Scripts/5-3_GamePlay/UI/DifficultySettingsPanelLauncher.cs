using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class DifficultySettingsPanelLauncher : MonoBehaviour
{
    private const string EntryButtonName = "游戏难度";

    private readonly Dictionary<GameDifficultyId, Button> optionButtons =
        new Dictionary<GameDifficultyId, Button>();

    private Button entryButton;
    private GameObject settingsWindow;
    private TextMeshProUGUI statusText;
    private TMP_FontAsset font;
    private GameDifficultyId selectedDifficulty;

    public static DifficultySettingsPanelLauncher Ensure(Transform settingsPanel)
    {
        if (settingsPanel == null)
            return null;

        DifficultySettingsPanelLauncher launcher =
            settingsPanel.GetComponent<DifficultySettingsPanelLauncher>();
        if (launcher == null)
            launcher = settingsPanel.gameObject.AddComponent<DifficultySettingsPanelLauncher>();

        launcher.EnsureEntryButton();
        return launcher;
    }

    private void EnsureEntryButton()
    {
        if (entryButton != null)
        {
            UpdateEntryLabel();
            return;
        }

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

            if (candidate.name == "自动保存")
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
        }

        if (entryButton == null)
        {
            Debug.LogWarning("[DifficultySettingsPanelLauncher] 设置面板中没有可复用的按钮，未能创建难度入口。", this);
            return;
        }

        entryButton.onClick.RemoveAllListeners();
        entryButton.onClick.AddListener(Open);
        UpdateEntryLabel();
    }

    private void Open()
    {
        EnsureWindow();
        selectedDifficulty = GameDifficultyService.CurrentId;
        RefreshSelectionVisuals();
        SetStatus($"当前存档难度：{GameDifficultyService.Current.DisplayName}", false);
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
        font = GetComponentInChildren<TextMeshProUGUI>(true)?.font ?? TMP_Settings.defaultFontAsset;

        settingsWindow = CreateObject("游戏难度设置面板", parent);
        RectTransform panelRect = settingsWindow.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(700f, 450f);

        Image panelImage = settingsWindow.AddComponent<Image>();
        panelImage.color = FlatWorldUITheme.Canvas;
        Outline outline = settingsWindow.AddComponent<Outline>();
        outline.effectColor = new Color(
            FlatWorldUITheme.Accent.r,
            FlatWorldUITheme.Accent.g,
            FlatWorldUITheme.Accent.b,
            0.75f);
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
            "难度属于当前存档并立即生效。简单难度保持现有规则；困难难度会在玩家死亡时掉落全部随身物品。",
            14f,
            FlatWorldUITheme.TextSecondary);
        hint.enableWordWrapping = true;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;

        IReadOnlyList<GameDifficultyDefinition> definitions = GameDifficultyCatalog.All;
        for (int i = 0; i < definitions.Count; i++)
            CreateDifficultyOption(definitions[i]);

        statusText = CreateText(
            settingsWindow.transform,
            string.Empty,
            13f,
            FlatWorldUITheme.Teal);
        statusText.enableWordWrapping = false;
        statusText.overflowMode = TextOverflowModes.Ellipsis;
        statusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        CreateFooter();
        FlatWorldUITheme.Apply(settingsWindow.transform);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
        settingsWindow.SetActive(false);
    }

    private void CreateHeader()
    {
        GameObject header = CreateObject("标题", settingsWindow.transform);
        header.AddComponent<LayoutElement>().preferredHeight = 50f;
        header.AddComponent<Image>().color = FlatWorldUITheme.SurfaceRaised;

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
            "游戏难度",
            21f,
            FlatWorldUITheme.TextPrimary);
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton(header.transform, "关闭", Close, 72f, 34f);
    }

    private void CreateDifficultyOption(GameDifficultyDefinition definition)
    {
        GameObject option = CreateObject($"难度_{definition.Id}", settingsWindow.transform);
        LayoutElement optionLayout = option.AddComponent<LayoutElement>();
        optionLayout.preferredHeight = 72f;

        Image background = option.AddComponent<Image>();
        background.color = FlatWorldUITheme.Surface;

        Button button = option.AddComponent<Button>();
        button.targetGraphic = background;
        GameDifficultyId id = definition.Id;
        button.onClick.AddListener(() => SelectDifficulty(id));

        TextMeshProUGUI title = CreateText(
            option.transform,
            definition.DisplayName,
            17f,
            FlatWorldUITheme.TextPrimary);
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        SetRect(
            title.rectTransform,
            new Vector2(16f, -34f),
            new Vector2(-16f, -6f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f));

        TextMeshProUGUI description = CreateText(
            option.transform,
            definition.Description,
            12.5f,
            FlatWorldUITheme.TextSecondary);
        description.enableWordWrapping = false;
        description.overflowMode = TextOverflowModes.Ellipsis;
        description.alignment = TextAlignmentOptions.MidlineLeft;
        SetRect(
            description.rectTransform,
            new Vector2(16f, 6f),
            new Vector2(-16f, 34f),
            new Vector2(0f, 0f),
            new Vector2(1f, 0f));

        optionButtons[definition.Id] = button;
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
        CreateButton(footer.transform, "应用", Apply, 92f, 36f);
    }

    private void SelectDifficulty(GameDifficultyId difficulty)
    {
        selectedDifficulty = difficulty;
        RefreshSelectionVisuals();
        GameDifficultyDefinition definition = GameDifficultyCatalog.Get(difficulty);
        SetStatus($"已选择：{definition.DisplayName}。点击“应用”后生效。", false);
    }

    private void Apply()
    {
        if (!GameDifficultyService.TrySetCurrent(selectedDifficulty, out string error))
        {
            SetStatus(error, true);
            return;
        }

        GameDifficultyDefinition definition = GameDifficultyCatalog.Get(selectedDifficulty);
        UpdateEntryLabel();
        RefreshSelectionVisuals();
        SetStatus($"已应用：{definition.DisplayName}。设置将在正常存档时写入磁盘。", false);
    }

    private void RefreshSelectionVisuals()
    {
        foreach (KeyValuePair<GameDifficultyId, Button> pair in optionButtons)
        {
            if (pair.Value?.targetGraphic == null)
                continue;

            pair.Value.targetGraphic.color = pair.Key == selectedDifficulty
                ? FlatWorldUITheme.Accent
                : FlatWorldUITheme.Surface;
        }
    }

    private void UpdateEntryLabel()
    {
        if (entryButton == null)
            return;

        SetButtonLabel(
            entryButton,
            $"{EntryButtonName}：{GameDifficultyService.Current.DisplayName}");
    }

    private void SetStatus(string message, bool isError)
    {
        if (statusText == null)
            return;

        statusText.text = message;
        statusText.color = isError ? FlatWorldUITheme.Danger : FlatWorldUITheme.Teal;
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

    private Button CreateButton(
        Transform parent,
        string label,
        UnityAction action,
        float width,
        float height)
    {
        GameObject root = CreateObject(label, parent);
        LayoutElement layout = root.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = height;

        Image image = root.AddComponent<Image>();
        image.color = label == "应用"
            ? FlatWorldUITheme.Accent
            : FlatWorldUITheme.SurfaceRaised;

        Button button = root.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        TextMeshProUGUI text = CreateText(
            root.transform,
            label,
            14f,
            FlatWorldUITheme.TextPrimary);
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        Stretch(text.rectTransform);
        return button;
    }

    private TextMeshProUGUI CreateText(
        Transform parent,
        string value,
        float fontSize,
        Color color)
    {
        GameObject root = CreateObject("文字", parent);
        TextMeshProUGUI text = root.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static GameObject CreateObject(string name, Transform parent)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        root.layer = parent != null ? parent.gameObject.layer : 5;
        root.transform.SetParent(parent, false);
        return root;
    }

    private static void SetButtonLabel(Button button, string label)
    {
        if (button == null)
            return;

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

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetRect(
        RectTransform rect,
        Vector2 offsetMin,
        Vector2 offsetMax,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
