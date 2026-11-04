// 按业务职责维护 Prefab，复用公共控件与布局实现。
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class RuntimeUIPrefabBuilder
{
    #region 运行诊断悬浮窗

    /// <summary>构建独立运行的日志悬浮窗，折叠入口和展开页都约束在设备安全区内。</summary>
    private static GameObject BuildRuntimeDebugOverlay()
    {
        // 先校验全部双语文案，避免构建中断后留下临时 Canvas。
        FlatWorld.Localization.Editor.FlatWorldLocalizationSetup.SyncRuntimeUiTexts(
            "折叠", "日志", "警告", "错误", "搜索日志、堆栈...", "日志列表",
            "选择日志查看完整消息与调用堆栈", "详细信息 / Stack Trace",
            "选择上方日志以查看消息和调用堆栈。", "没有匹配的日志。");
        GameObject root = new GameObject(
            RuntimeUIPrefabKeys.RuntimeDebugOverlay,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);
        // 独立 Canvas 不经过 UIManager 的打开动画，必须以可见缩放直接启动。
        rootRect.localScale = Vector3.one;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = UIManager.GlobalOverlaySortingOrder + 20;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        GameObject safeArea = CreateUIObject("调试安全区", root.transform, typeof(SafeAreaRectController));
        Stretch(safeArea.GetComponent<RectTransform>());

        Button toggleButton = CreateButton("调试悬浮按钮", safeArea.transform, "日志  0", 132f, 42f, false);
        SetTopRight(toggleButton.GetComponent<RectTransform>(), 22f, 22f, 132f, 42f);
        SetButtonLabelSize(toggleButton, 15f);
        TextMeshProUGUI toggleLabel = toggleButton.GetComponentInChildren<TextMeshProUGUI>(true);
        toggleLabel.gameObject.name = "悬浮日志数量";

        GameObject panel = CreateUIObject("调试页面", safeArea.transform, typeof(Image), typeof(Shadow));
        SetTopRight(panel.GetComponent<RectTransform>(), 22f, 80f, 960f, 780f);
        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.055f, 0.075f, 0.102f, 0.985f);
        panelImage.raycastTarget = true;
        AddOutline(panelImage, Border);
        Shadow shadow = panel.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.7f);
        shadow.effectDistance = new Vector2(6f, -8f);

        // 仿 Unity Console：紧凑工具栏、可虚拟化日志列表、选中消息的堆栈详情。
        Image headerBackground = CreateImage("日志顶部栏背景", panel.transform,
            new Color(0.11f, 0.12f, 0.135f, 1f));
        SetTopLeft(headerBackground.rectTransform, 2f, 2f, 956f, 62f);
        headerBackground.raycastTarget = false;

        TextMeshProUGUI title = CreateText("调试页标题", panel.transform, "Console", 23f, Cream);
        title.fontStyle = FontStyles.Bold;
        title.enableWordWrapping = false;
        SetTopLeft(title.rectTransform, 26f, 10f, 154f, 44f);

        TextMeshProUGUI summary = CreateText("日志数量摘要", panel.transform,
            "总计 0 条     警告 0     错误 0", 14f, Muted);
        summary.enableWordWrapping = false;
        summary.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(summary.rectTransform, 207f, 15f, 466f, 36f);

        Button openGmButton = CreateButton("打开GM面板按钮", panel.transform, "GM", 92f, 38f, false);
        SetTopRight(openGmButton.GetComponent<RectTransform>(), 120f, 13f, 92f, 38f);
        Button closeButton = CreateButton("关闭调试页按钮", panel.transform, "关闭", 84f, 38f, false);
        SetTopRight(closeButton.GetComponent<RectTransform>(), 24f, 13f, 84f, 38f);

        Image toolbarBackground = CreateImage("控制台工具栏背景", panel.transform,
            new Color(0.085f, 0.095f, 0.11f, 1f));
        SetTopLeft(toolbarBackground.rectTransform, 2f, 65f, 956f, 49f);
        toolbarBackground.raycastTarget = false;

        Button clearButton = CreateButton("清空日志按钮", panel.transform, "清空", 94f, 34f, false);
        SetTopLeft(clearButton.GetComponent<RectTransform>(), 26f, 73f, 94f, 34f);
        Toggle collapseToggle = CreateConsoleToolbarToggle(
            panel.transform, "折叠重复日志", "折叠", 137f, 168f, Muted, false);
        Toggle ordinaryLogs = CreateConsoleToolbarToggle(
            panel.transform, "普通日志过滤", "日志", 580f, 110f, new Color(0.63f, 0.74f, 0.82f, 1f), true);
        Toggle warningLogs = CreateConsoleToolbarToggle(
            panel.transform, "警告日志过滤", "警告", 698f, 110f, Amber, true);
        Toggle errorLogs = CreateConsoleToolbarToggle(
            panel.transform, "错误日志过滤", "错误", 816f, 116f,
            new Color(0.97f, 0.39f, 0.39f, 1f), true);

        TMP_InputField searchInput = CreateInputField("日志搜索框", panel.transform, "搜索日志、堆栈...");
        searchInput.lineType = TMP_InputField.LineType.SingleLine;
        SetTopLeft(searchInput.GetComponent<RectTransform>(), 24f, 123f, 912f, 39f);
        if (searchInput.placeholder is TMP_Text searchPlaceholder)
            FlatWorldUIAutoLocalizer.BindStaticTexts(new TMP_Text[] { searchPlaceholder });

        TextMeshProUGUI listTitle = CreateText(
            "日志列表标题", panel.transform, "日志列表", 13f, Muted);
        SetTopLeft(listTitle.rectTransform, 27f, 170f, 180f, 22f);
        TextMeshProUGUI listHint = CreateText(
            "日志列表提示", panel.transform, "选择日志查看完整消息与调用堆栈", 12f, Muted);
        listHint.alignment = TextAlignmentOptions.MidlineRight;
        SetTopLeft(listHint.rectTransform, 576f, 170f, 352f, 22f);
        FlatWorldUIAutoLocalizer.BindStaticTexts(new TMP_Text[] { listTitle, listHint });

        GameObject listRoot = CreateUIObject("控制台日志列表", panel.transform,
            typeof(Image), typeof(ItemStepScrollRect));
        SetTopLeft(listRoot.GetComponent<RectTransform>(), 24f, 196f, 912f, 260f);
        Image listBackground = listRoot.GetComponent<Image>();
        listBackground.color = new Color(0.11f, 0.12f, 0.135f, 1f);
        listBackground.raycastTarget = true;
        AddOutline(listBackground, Border);

        GameObject listViewport = CreateUIObject("Viewport", listRoot.transform, typeof(RectMask2D));
        RectTransform listViewportRect = listViewport.GetComponent<RectTransform>();
        Stretch(listViewportRect);
        listViewportRect.offsetMin = new Vector2(2f, 2f);
        listViewportRect.offsetMax = new Vector2(-2f, -2f);

        GameObject listContent = CreateUIObject("日志列表内容", listViewport.transform);
        RectTransform listContentRect = listContent.GetComponent<RectTransform>();
        listContentRect.anchorMin = new Vector2(0f, 1f);
        listContentRect.anchorMax = Vector2.one;
        listContentRect.pivot = new Vector2(0.5f, 1f);
        listContentRect.anchoredPosition = Vector2.zero;
        listContentRect.sizeDelta = new Vector2(0f, 1f);

        GameObject rowTemplate = CreateUIObject("日志行模板", listContent.transform, typeof(Image), typeof(Button));
        RectTransform rowRect = rowTemplate.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 1f);
        rowRect.anchorMax = Vector2.one;
        rowRect.pivot = new Vector2(0.5f, 1f);
        rowRect.anchoredPosition = Vector2.zero;
        rowRect.sizeDelta = new Vector2(0f, 34f);
        Image rowBackground = rowTemplate.GetComponent<Image>();
        rowBackground.color = new Color(0.115f, 0.125f, 0.14f, 1f);
        Button rowButton = rowTemplate.GetComponent<Button>();
        rowButton.targetGraphic = rowBackground;
        rowButton.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock rowColors = rowButton.colors;
        rowColors.normalColor = Color.white;
        rowColors.highlightedColor = new Color(1.24f, 1.24f, 1.24f, 1f);
        rowColors.pressedColor = new Color(0.8f, 0.9f, 1f, 1f);
        rowButton.colors = rowColors;

        Image rowLevel = CreateImage("级别标记", rowTemplate.transform, Teal);
        SetTopLeft(rowLevel.rectTransform, 9f, 7f, 4f, 20f);
        rowLevel.raycastTarget = false;
        TextMeshProUGUI rowSummary = CreateText("日志摘要", rowTemplate.transform,
            "日志消息", 14f, Cream);
        SetTopLeft(rowSummary.rectTransform, 23f, 4f, 795f, 26f);
        rowSummary.alignment = TextAlignmentOptions.MidlineLeft;
        rowSummary.enableWordWrapping = false;
        rowSummary.overflowMode = TextOverflowModes.Ellipsis;
        rowSummary.richText = false;
        rowSummary.raycastTarget = false;
        TextMeshProUGUI rowRepeat = CreateText("重复计数", rowTemplate.transform, "", 13f, Muted);
        SetTopRight(rowRepeat.rectTransform, 15f, 4f, 74f, 26f);
        rowRepeat.alignment = TextAlignmentOptions.MidlineRight;
        rowRepeat.enableWordWrapping = false;
        rowRepeat.raycastTarget = false;
        rowTemplate.SetActive(false);

        TextMeshProUGUI emptyLabel = CreateText("日志空状态", listViewport.transform,
            "暂无运行日志。", 15f, Muted);
        Stretch(emptyLabel.rectTransform);
        emptyLabel.alignment = TextAlignmentOptions.Center;
        emptyLabel.raycastTarget = false;
        FlatWorldUIAutoLocalizer.BindStaticTexts(new TMP_Text[] { emptyLabel });

        ScrollRect listScrollRect = listRoot.GetComponent<ScrollRect>();
        listScrollRect.horizontal = false;
        listScrollRect.vertical = true;
        listScrollRect.movementType = ScrollRect.MovementType.Clamped;
        listScrollRect.scrollSensitivity = 34f;
        listScrollRect.viewport = listViewportRect;
        listScrollRect.content = listContentRect;

        Image splitLine = CreateImage("日志详情分隔线", panel.transform, Border);
        splitLine.raycastTarget = false;
        SetTopLeft(splitLine.rectTransform, 24f, 464f, 912f, 1f);
        TextMeshProUGUI detailTitle = CreateText("日志详情标题", panel.transform,
            "详细信息 / Stack Trace", 13f, Muted);
        SetTopLeft(detailTitle.rectTransform, 27f, 470f, 400f, 24f);
        FlatWorldUIAutoLocalizer.BindStaticTexts(new TMP_Text[] { detailTitle });

        GameObject scrollRoot = CreateUIObject("日志详情滚动区", panel.transform,
            typeof(Image), typeof(ItemStepScrollRect));
        SetTopLeft(scrollRoot.GetComponent<RectTransform>(), 24f, 499f, 912f, 170f);
        Image scrollBackground = scrollRoot.GetComponent<Image>();
        scrollBackground.color = new Color(0.075f, 0.085f, 0.10f, 1f);
        scrollBackground.raycastTarget = true;
        AddOutline(scrollBackground, Border);

        GameObject viewport = CreateUIObject("Viewport", scrollRoot.transform, typeof(RectMask2D));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMin = new Vector2(12f, 8f);
        viewportRect.offsetMax = new Vector2(-12f, -8f);

        TextMeshProUGUI logText = CreateText(
            "日志正文",
            viewport.transform,
            "选择上方日志以查看消息和调用堆栈。",
            14f,
            new Color(0.84f, 0.87f, 0.90f, 1f));
        RectTransform logRect = logText.rectTransform;
        logRect.anchorMin = new Vector2(0f, 1f);
        logRect.anchorMax = new Vector2(1f, 1f);
        logRect.pivot = new Vector2(0.5f, 1f);
        logRect.anchoredPosition = Vector2.zero;
        logRect.sizeDelta = Vector2.zero;
        logText.alignment = TextAlignmentOptions.TopLeft;
        logText.enableWordWrapping = true;
        logText.enableAutoSizing = false;
        logText.overflowMode = TextOverflowModes.Overflow;
        logText.richText = false;
        logText.lineSpacing = 3f;
        ContentSizeFitter contentFitter = logText.gameObject.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scrollRect = scrollRoot.GetComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.inertia = true;
        scrollRect.decelerationRate = 0.12f;
        scrollRect.scrollSensitivity = 34f;
        scrollRect.viewport = viewportRect;
        scrollRect.content = logRect;

        Image footerBackground = CreateImage("日志操作区背景", panel.transform,
            new Color(0.085f, 0.095f, 0.11f, 1f));
        footerBackground.raycastTarget = false;
        footerBackground.rectTransform.anchorMin = new Vector2(0f, 0f);
        footerBackground.rectTransform.anchorMax = new Vector2(1f, 0f);
        footerBackground.rectTransform.pivot = new Vector2(0.5f, 0f);
        footerBackground.rectTransform.anchoredPosition = Vector2.zero;
        footerBackground.rectTransform.sizeDelta = new Vector2(-4f, 98f);

        Image footerDivider = CreateImage("日志操作分隔线", panel.transform, Border);
        footerDivider.raycastTarget = false;
        SetBottomLeft(footerDivider.rectTransform, 24f, 98f, 912f, 1f);

        TextMeshProUGUI status = CreateText(
            "调试操作状态",
            panel.transform,
            "复制保留原始日志与堆栈，磁盘记录不会清除。",
            13f,
            Muted);
        status.enableWordWrapping = false;
        status.overflowMode = TextOverflowModes.Ellipsis;
        SetBottomLeft(status.rectTransform, 26f, 32f, 450f, 27f);

        TextMeshProUGUI copyCountLabel = CreateText("复制槽位标题", panel.transform, "复制最近", 14f, Muted);
        copyCountLabel.enableWordWrapping = false;
        SetBottomLeft(copyCountLabel.rectTransform, 490f, 33f, 70f, 27f);

        TMP_InputField copyEntryCountInput = CreateInputField("复制日志条数输入框", panel.transform, "50");
        copyEntryCountInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        copyEntryCountInput.characterLimit = 3;
        SetBottomLeft(copyEntryCountInput.GetComponent<RectTransform>(), 566f, 24f, 78f, 48f);

        Button copyButton = CreateButton("复制日志按钮", panel.transform, "复制日志", 146f, 48f, true);
        SetBottomRight(copyButton.GetComponent<RectTransform>(), 24f, 24f, 146f, 48f);
        SetButtonLabelSize(copyButton, 16f);

        GameObject feedbackToast = CreateUIObject(
            "操作Toast提示", panel.transform, typeof(Image), typeof(Shadow), typeof(CanvasGroup));
        RectTransform toastRect = feedbackToast.GetComponent<RectTransform>();
        toastRect.anchorMin = toastRect.anchorMax = new Vector2(0.5f, 0f);
        toastRect.pivot = new Vector2(0.5f, 0f);
        toastRect.anchoredPosition = new Vector2(0f, 132f);
        toastRect.sizeDelta = new Vector2(460f, 56f);
        Image toastBackground = feedbackToast.GetComponent<Image>();
        toastBackground.color = new Color(0.09f, 0.22f, 0.19f, 0.98f);
        toastBackground.raycastTarget = false;
        AddOutline(toastBackground, Teal);
        Shadow toastShadow = feedbackToast.GetComponent<Shadow>();
        toastShadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
        toastShadow.effectDistance = new Vector2(0f, -4f);
        CanvasGroup toastGroup = feedbackToast.GetComponent<CanvasGroup>();
        toastGroup.alpha = 0f;
        toastGroup.interactable = false;
        toastGroup.blocksRaycasts = false;
        TextMeshProUGUI toastText = CreateText("Toast提示内容", feedbackToast.transform, "已复制", 16f, Cream);
        Stretch(toastText.rectTransform);
        toastText.rectTransform.offsetMin = new Vector2(16f, 7f);
        toastText.rectTransform.offsetMax = new Vector2(-16f, -7f);
        toastText.alignment = TextAlignmentOptions.Center;
        toastText.enableWordWrapping = false;
        toastText.overflowMode = TextOverflowModes.Ellipsis;

        RuntimeDebugOverlay controller = root.AddComponent<RuntimeDebugOverlay>();
        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("debugPanel").objectReferenceValue = panel;
        serializedController.FindProperty("toggleButton").objectReferenceValue = toggleButton;
        serializedController.FindProperty("copyButton").objectReferenceValue = copyButton;
        serializedController.FindProperty("clearButton").objectReferenceValue = clearButton;
        serializedController.FindProperty("closeButton").objectReferenceValue = closeButton;
        serializedController.FindProperty("openGmButton").objectReferenceValue = openGmButton;
        serializedController.FindProperty("toggleLabel").objectReferenceValue = toggleLabel;
        serializedController.FindProperty("summaryText").objectReferenceValue = summary;
        serializedController.FindProperty("logText").objectReferenceValue = logText;
        serializedController.FindProperty("statusText").objectReferenceValue = status;
        serializedController.FindProperty("logScrollRect").objectReferenceValue = scrollRect;
        serializedController.FindProperty("consoleListScrollRect").objectReferenceValue = listScrollRect;
        serializedController.FindProperty("consoleListContent").objectReferenceValue = listContentRect;
        serializedController.FindProperty("consoleRowTemplate").objectReferenceValue = rowTemplate;
        serializedController.FindProperty("emptyListLabel").objectReferenceValue = emptyLabel;
        serializedController.FindProperty("searchInput").objectReferenceValue = searchInput;
        serializedController.FindProperty("collapseToggle").objectReferenceValue = collapseToggle;
        serializedController.FindProperty("copyEntryCountInput").objectReferenceValue = copyEntryCountInput;
        serializedController.FindProperty("ordinaryLogsToggle").objectReferenceValue = ordinaryLogs;
        serializedController.FindProperty("warningLogsToggle").objectReferenceValue = warningLogs;
        serializedController.FindProperty("errorLogsToggle").objectReferenceValue = errorLogs;
        serializedController.FindProperty("feedbackToast").objectReferenceValue = feedbackToast;
        serializedController.FindProperty("feedbackToastCanvasGroup").objectReferenceValue = toastGroup;
        serializedController.FindProperty("feedbackToastBackground").objectReferenceValue = toastBackground;
        serializedController.FindProperty("feedbackToastText").objectReferenceValue = toastText;
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        feedbackToast.SetActive(false);
        panel.SetActive(false);
        return root;
    }

    /// <summary>紧凑过滤控件沿用共享 Toggle，级别由左侧色标区分。</summary>
    private static Toggle CreateConsoleToolbarToggle(
        Transform parent, string name, string caption, float left, float width, Color accentColor, bool enabled)
    {
        Image filterBackground = CreateImage(name + "背景", parent, new Color(0.135f, 0.145f, 0.16f, 1f));
        SetTopLeft(filterBackground.rectTransform, left, 74f, width, 33f);
        filterBackground.raycastTarget = false;
        Image accentRail = CreateImage(name + "等级标记", parent, accentColor);
        SetTopLeft(accentRail.rectTransform, left, 80f, 3f, 21f);
        accentRail.raycastTarget = false;
        Toggle toggle = CreateToggle(name, parent);
        SetTopLeft(toggle.GetComponent<RectTransform>(), left + 9f, 78f, 42f, 27f);
        toggle.SetIsOnWithoutNotify(enabled);
        TextMeshProUGUI label = CreateText(name + "标签", toggle.transform, caption, 13f, Cream);
        SetTopLeft(label.rectTransform, 44f, -1f, width - 56f, 29f);
        label.enableWordWrapping = false;
        label.raycastTarget = true;
        FlatWorldUIAutoLocalizer.BindStaticTexts(new TMP_Text[] { label });
        return toggle;
    }

    #endregion
}
