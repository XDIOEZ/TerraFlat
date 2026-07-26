using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class InputBindingPanelLauncher : MonoBehaviour
{
    private const string EntryButtonName = "按键绑定";

    private sealed class BindingRow
    {
        public InputBindingEntry Entry;
        public TextMeshProUGUI BindingText;
        public Button RebindButton;
    }

    private readonly List<BindingRow> rows = new List<BindingRow>();

    private GameController gameController;
    private InputBindingService bindingService;
    private Button entryButton;
    private GameObject overlay;
    private RectTransform dialogRect;
    private TextMeshProUGUI statusText;
    private TMP_FontAsset font;
    private bool panelSuspendedInput;
    private bool previousGameplayLock;
    private int suppressEscapeCloseFrame = -1;

    public static InputBindingPanelLauncher Ensure(
        Transform settingsPanel,
        GameController gameController)
    {
        if (settingsPanel == null)
            return null;

        InputBindingPanelLauncher launcher =
            settingsPanel.GetComponent<InputBindingPanelLauncher>();
        if (launcher == null)
            launcher = settingsPanel.gameObject.AddComponent<InputBindingPanelLauncher>();

        launcher.Initialize(gameController);
        launcher.EnsureEntryButton();
        return launcher;
    }

    private void Initialize(GameController controller)
    {
        InputBindingService nextService = controller != null ? controller.InputBindings : null;
        if (ReferenceEquals(gameController, controller) &&
            ReferenceEquals(bindingService, nextService))
        {
            return;
        }

        if (bindingService != null)
            bindingService.BindingsChanged -= RefreshRows;

        ReleaseInputLock();
        gameController = controller;
        bindingService = nextService;

        if (bindingService != null)
            bindingService.BindingsChanged += RefreshRows;
    }

    private void EnsureEntryButton()
    {
        if (entryButton != null)
            return;

        Button[] buttons = GetComponentsInChildren<Button>(true);
        Button template = null;
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null)
                continue;

            if (button.name == EntryButtonName)
            {
                entryButton = button;
                break;
            }

            template ??= button;
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
                "[InputBindingPanelLauncher] 设置面板中没有可复用按钮，未能创建“按键绑定”入口。",
                this);
            return;
        }

        entryButton.onClick.RemoveAllListeners();
        entryButton.onClick.AddListener(Open);
    }

    private void Update()
    {
        if (overlay == null || !overlay.activeSelf || bindingService == null)
            return;

        if (bindingService.IsRebinding || Time.frameCount == suppressEscapeCloseFrame)
            return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            Close();
    }

    private void Open()
    {
        if (bindingService == null)
        {
            Debug.LogError(
                "[InputBindingPanelLauncher] GameController 尚未准备好按键绑定服务。",
                this);
            return;
        }

        EnsurePanel();
        if (!panelSuspendedInput)
        {
            previousGameplayLock =
                gameController != null && gameController.IsGameplayInputLocked;
            gameController?.SetGameplayInputLocked(true);
            bindingService.SuspendGameplayInput();
            panelSuspendedInput = true;
        }

        UpdateDialogSize();
        RefreshRows();
        SetStatus("选择一项后按下新按键；Esc 取消录入。设置会自动保存。");
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(dialogRect);
    }

    private void Close()
    {
        if (bindingService != null && bindingService.IsRebinding)
            bindingService.CancelActiveRebind();

        if (overlay != null)
            overlay.SetActive(false);

        ReleaseInputLock();
    }

    private void EnsurePanel()
    {
        if (overlay != null)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        Transform parent = canvas != null ? canvas.transform : transform.parent;
        font = FindFont();

        overlay = CreateObject("按键绑定遮罩", parent);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        Stretch(overlayRect);
        Image overlayImage = overlay.AddComponent<Image>();
        overlayImage.color = new Color(0.015f, 0.028f, 0.034f, 0.78f);
        overlayImage.raycastTarget = true;

        GameObject dialog = CreateObject("按键绑定面板", overlay.transform);
        dialogRect = dialog.GetComponent<RectTransform>();
        dialogRect.anchorMin = dialogRect.anchorMax = new Vector2(0.5f, 0.5f);
        dialogRect.pivot = new Vector2(0.5f, 0.5f);

        Image dialogImage = dialog.AddComponent<Image>();
        dialogImage.color = new Color(0.043f, 0.074f, 0.086f, 0.995f);
        dialogImage.raycastTarget = true;
        Outline outline = dialog.AddComponent<Outline>();
        outline.effectColor = new Color(0.86f, 0.37f, 0.15f, 0.95f);
        outline.effectDistance = new Vector2(2f, -2f);

        VerticalLayoutGroup dialogLayout = dialog.AddComponent<VerticalLayoutGroup>();
        dialogLayout.padding = new RectOffset(20, 20, 18, 18);
        dialogLayout.spacing = 10f;
        dialogLayout.childControlWidth = true;
        dialogLayout.childControlHeight = true;
        dialogLayout.childForceExpandWidth = true;
        dialogLayout.childForceExpandHeight = false;

        CreateHeader(dialog.transform);
        CreateHint(dialog.transform);
        Transform content = CreateScrollView(dialog.transform);
        CreateRows(content);
        CreateStatus(dialog.transform);
        CreateFooter(dialog.transform);

        UpdateDialogSize();
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(dialogRect);
        overlay.SetActive(false);
    }

    private void CreateHeader(Transform parent)
    {
        GameObject header = CreateObject("标题", parent);
        header.AddComponent<LayoutElement>().preferredHeight = 52f;
        header.AddComponent<Image>().color = new Color(0.07f, 0.18f, 0.21f, 1f);

        HorizontalLayoutGroup layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 10, 6, 6);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        TextMeshProUGUI title = CreateText(
            header.transform,
            "按键绑定",
            22f,
            new Color(0.96f, 0.96f, 0.92f));
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton(header.transform, "关闭", Close, 72f, 36f);
    }

    private void CreateHint(Transform parent)
    {
        TextMeshProUGUI hint = CreateText(
            parent,
            "点击“修改”后按下键盘按键或鼠标按键。重复绑定会被拦截；界面保留 Esc 作为安全取消键。",
            13f,
            new Color(0.68f, 0.76f, 0.77f));
        hint.enableWordWrapping = true;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
    }

    private Transform CreateScrollView(Transform parent)
    {
        GameObject scrollRoot = CreateObject("绑定列表", parent);
        LayoutElement scrollElement = scrollRoot.AddComponent<LayoutElement>();
        scrollElement.minHeight = 180f;
        scrollElement.flexibleHeight = 1f;

        Image scrollBackground = scrollRoot.AddComponent<Image>();
        scrollBackground.color = new Color(0.025f, 0.047f, 0.055f, 1f);
        ScrollRect scrollRect = scrollRoot.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 28f;

        GameObject viewport = CreateObject("Viewport", scrollRoot.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMax = new Vector2(-18f, 0f);
        Image viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
        viewportImage.raycastTarget = true;
        viewport.AddComponent<RectMask2D>();

        GameObject content = CreateObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(8, 8, 8, 8);
        contentLayout.spacing = 7f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Scrollbar scrollbar = CreateScrollbar(scrollRoot.transform);
        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility =
            ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
        scrollRect.verticalScrollbarSpacing = 4f;
        return content.transform;
    }

    private void CreateRows(Transform content)
    {
        rows.Clear();
        IReadOnlyList<InputBindingEntry> entries = bindingService.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            InputBindingEntry entry = entries[i];
            GameObject rowObject = CreateObject(entry.DisplayName, content);
            rowObject.AddComponent<LayoutElement>().preferredHeight = 44f;
            Image rowImage = rowObject.AddComponent<Image>();
            rowImage.color = i % 2 == 0
                ? new Color(0.075f, 0.12f, 0.135f, 0.96f)
                : new Color(0.06f, 0.102f, 0.116f, 0.96f);
            rowImage.raycastTarget = false;

            HorizontalLayoutGroup rowLayout =
                rowObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.padding = new RectOffset(12, 8, 5, 5);
            rowLayout.spacing = 10f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = true;

            TextMeshProUGUI label = CreateText(
                rowObject.transform,
                entry.DisplayName,
                15f,
                new Color(0.91f, 0.94f, 0.93f));
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            TextMeshProUGUI bindingText = CreateText(
                rowObject.transform,
                bindingService.GetBindingDisplayString(entry),
                14f,
                new Color(0.94f, 0.62f, 0.32f));
            bindingText.alignment = TextAlignmentOptions.MidlineRight;
            LayoutElement bindingElement =
                bindingText.gameObject.AddComponent<LayoutElement>();
            bindingElement.preferredWidth = 190f;

            BindingRow row = new BindingRow
            {
                Entry = entry,
                BindingText = bindingText
            };
            row.RebindButton = CreateButton(
                rowObject.transform,
                "修改",
                () => BeginRebind(row),
                86f,
                34f);
            rows.Add(row);
        }
    }

    private void CreateStatus(Transform parent)
    {
        statusText = CreateText(
            parent,
            string.Empty,
            13f,
            new Color(0.69f, 0.78f, 0.79f));
        statusText.enableWordWrapping = false;
        statusText.overflowMode = TextOverflowModes.Ellipsis;
        statusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
    }

    private void CreateFooter(Transform parent)
    {
        GameObject footer = CreateObject("底部操作", parent);
        footer.AddComponent<LayoutElement>().preferredHeight = 40f;
        HorizontalLayoutGroup footerLayout =
            footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 10f;
        footerLayout.childAlignment = TextAnchor.MiddleRight;
        footerLayout.childControlWidth = false;
        footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = false;
        footerLayout.childForceExpandHeight = true;

        GameObject spacer = CreateObject("Spacer", footer.transform);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton(footer.transform, "恢复默认", ResetToDefaults, 104f, 36f);
        CreateButton(footer.transform, "完成", Close, 78f, 36f);
    }

    private void BeginRebind(BindingRow row)
    {
        if (bindingService == null || row == null)
            return;

        SetRowsInteractable(false);
        row.BindingText.text = "等待输入…";
        SetStatus($"正在修改“{row.Entry.DisplayName}”；按 Esc 取消。");
        bindingService.BeginInteractiveRebind(row.Entry, result =>
        {
            SetRowsInteractable(true);
            RefreshRows();

            switch (result.Status)
            {
                case InputRebindStatus.Completed:
                    SetStatus($"“{row.Entry.DisplayName}”已保存。");
                    break;
                case InputRebindStatus.Canceled:
                    suppressEscapeCloseFrame = Time.frameCount;
                    SetStatus("已取消本次修改。");
                    break;
                case InputRebindStatus.Conflict:
                    SetStatus(
                        $"该按键已用于“{result.ConflictingEntry?.DisplayName ?? "其他操作"}”，未作修改。",
                        true);
                    break;
                default:
                    SetStatus(
                        $"修改失败：{result.Exception?.Message ?? "未知错误"}",
                        true);
                    break;
            }
        });
    }

    private void ResetToDefaults()
    {
        if (bindingService == null)
            return;

        bindingService.ResetToDefaults();
        RefreshRows();
        SetStatus("已恢复默认按键。");
    }

    private void RefreshRows()
    {
        if (bindingService == null)
            return;

        for (int i = 0; i < rows.Count; i++)
        {
            BindingRow row = rows[i];
            if (row?.BindingText != null)
            {
                row.BindingText.text =
                    bindingService.GetBindingDisplayString(row.Entry);
            }
        }
    }

    private void SetRowsInteractable(bool interactable)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i]?.RebindButton != null)
                rows[i].RebindButton.interactable = interactable;
        }
    }

    private void SetStatus(string message, bool isError = false)
    {
        if (statusText == null)
            return;

        statusText.text = message;
        statusText.color = isError
            ? new Color(1f, 0.48f, 0.35f)
            : new Color(0.69f, 0.78f, 0.79f);
    }

    private void UpdateDialogSize()
    {
        if (dialogRect == null)
            return;

        Canvas canvas = dialogRect.GetComponentInParent<Canvas>();
        RectTransform canvasRect = canvas != null
            ? canvas.transform as RectTransform
            : null;
        Vector2 canvasSize = canvasRect != null
            ? canvasRect.rect.size
            : new Vector2(Screen.width, Screen.height);
        Vector2 available = new Vector2(
            Mathf.Max(1f, canvasSize.x - 64f),
            Mathf.Max(1f, canvasSize.y - 64f));

        dialogRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            Mathf.Min(760f, available.x));
        dialogRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            Mathf.Min(720f, available.y));
    }

    private void ReleaseInputLock()
    {
        if (!panelSuspendedInput)
            return;

        bindingService?.ResumeGameplayInput();
        gameController?.SetGameplayInputLocked(previousGameplayLock);
        panelSuspendedInput = false;
    }

    private TMP_FontAsset FindFont()
    {
        TextMeshProUGUI existing =
            GetComponentInChildren<TextMeshProUGUI>(true);
        return existing != null && existing.font != null
            ? existing.font
            : TMP_Settings.defaultFontAsset;
    }

    private static GameObject CreateObject(string objectName, Transform parent)
    {
        GameObject result = new GameObject(objectName, typeof(RectTransform));
        result.layer = parent.gameObject.layer;
        result.transform.SetParent(parent, false);
        return result;
    }

    private TextMeshProUGUI CreateText(
        Transform parent,
        string value,
        float fontSize,
        Color color)
    {
        GameObject textObject = new GameObject(
            value,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        textObject.layer = parent.gameObject.layer;
        textObject.transform.SetParent(parent, false);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        return text;
    }

    private Button CreateButton(
        Transform parent,
        string label,
        UnityEngine.Events.UnityAction onClick,
        float width,
        float height)
    {
        GameObject buttonObject = new GameObject(
            label,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement));
        buttonObject.layer = parent.gameObject.layer;
        buttonObject.transform.SetParent(parent, false);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.72f, 0.31f, 0.13f, 1f);

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.86f, 0.72f, 1f);
        colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f);
        button.colors = colors;
        button.targetGraphic = image;
        button.onClick.AddListener(onClick);

        LayoutElement element = buttonObject.GetComponent<LayoutElement>();
        element.preferredWidth = width;
        element.preferredHeight = height;

        TextMeshProUGUI text = CreateText(
            buttonObject.transform,
            label,
            14f,
            new Color(0.98f, 0.96f, 0.91f));
        text.alignment = TextAlignmentOptions.Center;
        Stretch(text.rectTransform);
        return button;
    }

    private static Scrollbar CreateScrollbar(Transform parent)
    {
        GameObject scrollbarObject = new GameObject(
            "Scrollbar Vertical",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Scrollbar));
        scrollbarObject.layer = parent.gameObject.layer;
        scrollbarObject.transform.SetParent(parent, false);

        RectTransform scrollbarRect =
            scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = Vector2.one;
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = new Vector2(-14f, 2f);
        scrollbarRect.offsetMax = new Vector2(-2f, -2f);

        Image background = scrollbarObject.GetComponent<Image>();
        background.color = new Color(0.08f, 0.12f, 0.13f, 1f);

        GameObject slidingArea = CreateObject(
            "Sliding Area",
            scrollbarObject.transform);
        Stretch(slidingArea.GetComponent<RectTransform>());
        GameObject handle = new GameObject(
            "Handle",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        handle.layer = parent.gameObject.layer;
        handle.transform.SetParent(slidingArea.transform, false);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        Stretch(handleRect);
        Image handleImage = handle.GetComponent<Image>();
        handleImage.color = new Color(0.45f, 0.56f, 0.57f, 1f);

        Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.size = 0.35f;
        return scrollbar;
    }

    private static void SetButtonLabel(Button button, string label)
    {
        if (button == null)
            return;

        TextMeshProUGUI text =
            button.GetComponentInChildren<TextMeshProUGUI>(true);
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
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void OnDestroy()
    {
        if (bindingService != null)
        {
            bindingService.BindingsChanged -= RefreshRows;
            if (bindingService.IsRebinding)
                bindingService.CancelActiveRebind();
        }

        ReleaseInputLock();
        if (overlay != null)
            Destroy(overlay);
    }
}
