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

        // 独立背景、标题、统计、过滤、正文、底部操作形成清晰的信息层级。
        Image headerBackground = CreateImage("日志顶部栏背景", panel.transform, new Color(0.09f, 0.12f, 0.16f, 1f));
        SetTopLeft(headerBackground.rectTransform, 2f, 2f, 956f, 92f);
        headerBackground.raycastTarget = false;

        Image headerAccent = CreateImage("日志标题AccentRail", panel.transform, Teal);
        SetTopLeft(headerAccent.rectTransform, 26f, 24f, 4f, 52f);
        headerAccent.raycastTarget = false;

        TextMeshProUGUI eyebrow = CreateText(
            "诊断英文标识", panel.transform, "TERRAFLAT  /  DEVELOPER CONSOLE", 12f, Muted);
        SetTopLeft(eyebrow.rectTransform, 43f, 16f, 405f, 24f);
        eyebrow.enableWordWrapping = false;

        TextMeshProUGUI title = CreateText("调试页标题", panel.transform, "运行日志", 27f, Cream);
        title.fontStyle = FontStyles.Bold;
        title.enableWordWrapping = false;
        SetTopLeft(title.rectTransform, 43f, 42f, 240f, 38f);

        Image liveDot = CreateImage("实时指示灯", panel.transform, Teal);
        SetTopLeft(liveDot.rectTransform, 278f, 54f, 8f, 8f);
        liveDot.raycastTarget = false;
        TextMeshProUGUI liveLabel = CreateText("运行状态", panel.transform, "LIVE", 12f, Muted);
        SetTopLeft(liveLabel.rectTransform, 292f, 45f, 72f, 27f);

        Button openGmButton = CreateButton("打开GM面板按钮", panel.transform, "GM 控制台", 150f, 44f, false);
        SetTopRight(openGmButton.GetComponent<RectTransform>(), 123f, 26f, 150f, 44f);
        Button closeButton = CreateButton("关闭调试页按钮", panel.transform, "关闭", 88f, 44f, false);
        SetTopRight(closeButton.GetComponent<RectTransform>(), 22f, 26f, 88f, 44f);

        Image headerDivider = CreateImage("日志页头分隔线", panel.transform, Border);
        headerDivider.raycastTarget = false;
        SetTopLeft(headerDivider.rectTransform, 24f, 94f, 912f, 1f);

        Image summaryBackground = CreateImage("日志统计栏背景", panel.transform,
            new Color(0.07f, 0.10f, 0.13f, 0.95f));
        SetTopLeft(summaryBackground.rectTransform, 24f, 104f, 912f, 56f);
        summaryBackground.raycastTarget = false;

        TextMeshProUGUI summary = CreateText("日志数量摘要", panel.transform, "总计 0 条     警告 0     错误 0", 17f, Cream);
        summary.enableWordWrapping = false;
        summary.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(summary.rectTransform, 42f, 115f, 560f, 34f);

        TextMeshProUGUI latestLabel = CreateText("日志统计说明", panel.transform, "内存日志  ·  实时更新", 13f, Muted);
        latestLabel.alignment = TextAlignmentOptions.MidlineRight;
        SetTopLeft(latestLabel.rectTransform, 658f, 116f, 254f, 32f);

        TextMeshProUGUI filterTitle = CreateText("类型筛选说明", panel.transform, "筛选 / FILTER", 14f, Muted);
        SetTopLeft(filterTitle.rectTransform, 29f, 182f, 132f, 35f);
        Toggle ordinaryLogs = CreateLogFilterToggle(panel.transform, "普通日志过滤", "普通日志", 169f, Teal);
        Toggle warningLogs = CreateLogFilterToggle(panel.transform, "警告日志过滤", "警告日志", 377f, Amber);
        Toggle errorLogs = CreateLogFilterToggle(panel.transform, "错误日志过滤", "错误日志", 585f,
            new Color(0.93f, 0.46f, 0.42f, 1f));

        TextMeshProUGUI streamTitle = CreateText("日志流标题", panel.transform, "LOG STREAM   /   日志详情", 14f, Cream);
        SetTopLeft(streamTitle.rectTransform, 28f, 238f, 390f, 27f);
        TextMeshProUGUI streamHint = CreateText("日志流说明", panel.transform, "最新日志在下方  ·  可滚动查看", 12f, Muted);
        streamHint.alignment = TextAlignmentOptions.MidlineRight;
        SetTopLeft(streamHint.rectTransform, 647f, 238f, 282f, 27f);

        GameObject scrollRoot = CreateUIObject("日志滚动区", panel.transform, typeof(Image), typeof(ItemStepScrollRect));
        RectTransform scrollRootRect = scrollRoot.GetComponent<RectTransform>();
        scrollRootRect.anchorMin = Vector2.zero;
        scrollRootRect.anchorMax = Vector2.one;
        scrollRootRect.offsetMin = new Vector2(24f, 110f);
        scrollRootRect.offsetMax = new Vector2(-24f, -270f);
        Image scrollBackground = scrollRoot.GetComponent<Image>();
        scrollBackground.color = new Color(0.025f, 0.036f, 0.05f, 0.97f);
        scrollBackground.raycastTarget = true;
        AddOutline(scrollBackground, Border);

        GameObject viewport = CreateUIObject("Viewport", scrollRoot.transform, typeof(RectMask2D));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMin = new Vector2(16f, 14f);
        viewportRect.offsetMax = new Vector2(-16f, -14f);

        TextMeshProUGUI logText = CreateText(
            "日志正文",
            viewport.transform,
            "暂无运行日志。",
            15f,
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
        logText.lineSpacing = 5f;
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
            new Color(0.075f, 0.10f, 0.135f, 1f));
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

        Button clearButton = CreateButton("清空日志按钮", panel.transform, "清空", 112f, 48f, false);
        SetBottomRight(clearButton.GetComponent<RectTransform>(), 182f, 24f, 112f, 48f);
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

    /// <summary>过滤器保持共享 Toggle 的交互和点击范围，彩色侧条帮助识别日志级别。</summary>
    private static Toggle CreateLogFilterToggle(
        Transform parent, string name, string caption, float left, Color accentColor)
    {
        FlatWorld.Localization.Editor.FlatWorldLocalizationSetup.SyncRuntimeUiTexts(caption);
        Image filterBackground = CreateImage(name + "背景", parent, new Color(0.085f, 0.112f, 0.15f, 1f));
        SetTopLeft(filterBackground.rectTransform, left, 174f, 190f, 54f);
        filterBackground.raycastTarget = false;
        Image accentRail = CreateImage(name + "等级AccentRail", parent, accentColor);
        SetTopLeft(accentRail.rectTransform, left, 180f, 3f, 42f);
        accentRail.raycastTarget = false;
        Toggle toggle = CreateToggle(name, parent);
        SetTopLeft(toggle.GetComponent<RectTransform>(), left + 13f, 183f, 54f, 38f);
        toggle.SetIsOnWithoutNotify(true);
        TextMeshProUGUI label = CreateText(name + "标签", parent, caption, 16f, Cream);
        SetTopLeft(label.rectTransform, left + 75f, 184f, 109f, 36f);
        FlatWorldUIAutoLocalizer.BindStaticTexts(new TMP_Text[] { label });
        return toggle;
    }

    #endregion
}
