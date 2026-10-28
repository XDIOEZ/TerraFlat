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

        Button toggleButton = CreateButton("调试悬浮按钮", safeArea.transform, "日志  0", 75f, 31f, false);
        SetTopRight(toggleButton.GetComponent<RectTransform>(), 22f, 22f, 75f, 31f);
        toggleButton.GetComponent<Image>().color = new Color(0.12f, 0.22f, 0.25f, 0.98f);
        SetButtonLabelSize(toggleButton, 14f);
        TextMeshProUGUI toggleLabel = toggleButton.GetComponentInChildren<TextMeshProUGUI>(true);
        toggleLabel.gameObject.name = "悬浮日志数量";

        GameObject panel = CreateUIObject("调试页面", safeArea.transform, typeof(Image), typeof(Shadow));
        SetTopRight(panel.GetComponent<RectTransform>(), 22f, 98f, 980f, 760f);
        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.018f, 0.03f, 0.04f, 0.985f);
        panelImage.raycastTarget = true;
        AddOutline(panelImage, Amber);
        Shadow shadow = panel.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
        shadow.effectDistance = new Vector2(8f, -8f);

        TextMeshProUGUI title = CreateText("调试页标题", panel.transform, "运行日志 / DEBUG", 25f, Cream);
        title.fontStyle = FontStyles.Bold;
        title.enableWordWrapping = false;
        SetTopLeft(title.rectTransform, 24f, 18f, 230f, 38f);

        TextMeshProUGUI summary = CreateText("日志数量摘要", panel.transform, "共 0 条    错误 0    警告 0", 16f, Muted);
        summary.enableWordWrapping = false;
        summary.overflowMode = TextOverflowModes.Ellipsis;
        SetTopLeft(summary.rectTransform, 265f, 22f, 220f, 30f);

        TextMeshProUGUI copyCountLabel = CreateText("复制槽位标题", panel.transform, "复制槽位", 15f, Muted);
        copyCountLabel.enableWordWrapping = false;
        SetTopLeft(copyCountLabel.rectTransform, 493f, 23f, 70f, 30f);

        TMP_InputField copyEntryCountInput = CreateInputField("复制日志条数输入框", panel.transform, "50");
        copyEntryCountInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        copyEntryCountInput.characterLimit = 3;
        SetTopLeft(copyEntryCountInput.GetComponent<RectTransform>(), 570f, 14f, 62f, 48f);

        Button closeButton = CreateButton("关闭调试页按钮", panel.transform, "关闭", 96f, 48f, false);
        SetTopRight(closeButton.GetComponent<RectTransform>(), 18f, 14f, 96f, 48f);
        Button clearButton = CreateButton("清空日志按钮", panel.transform, "清空", 96f, 48f, false);
        SetTopRight(clearButton.GetComponent<RectTransform>(), 124f, 14f, 96f, 48f);
        Button copyButton = CreateButton("复制日志按钮", panel.transform, "复制", 108f, 48f, true);
        SetTopRight(copyButton.GetComponent<RectTransform>(), 230f, 14f, 108f, 48f);
        SetButtonLabelSize(closeButton, 17f);
        SetButtonLabelSize(clearButton, 17f);
        SetButtonLabelSize(copyButton, 17f);

        Button openGmButton = CreateButton("打开GM面板按钮", panel.transform, "打开 GM 面板", 220f, 60f, true);
        SetTopLeft(openGmButton.GetComponent<RectTransform>(), 24f, 80f, 220f, 60f);
        SetButtonLabelSize(openGmButton, 19f);

        Toggle ordinaryLogs = CreateLogFilterToggle(panel.transform, "普通日志过滤", "普通", 284f);
        Toggle warningLogs = CreateLogFilterToggle(panel.transform, "警告日志过滤", "警告", 500f);
        Toggle errorLogs = CreateLogFilterToggle(panel.transform, "错误日志过滤", "报错", 716f);

        Image divider = CreateImage("页头分隔线", panel.transform, new Color(0.83f, 0.49f, 0.23f, 0.72f));
        divider.raycastTarget = false;
        RectTransform dividerRect = divider.rectTransform;
        dividerRect.anchorMin = new Vector2(0f, 1f);
        dividerRect.anchorMax = new Vector2(1f, 1f);
        dividerRect.pivot = new Vector2(0.5f, 1f);
        dividerRect.anchoredPosition = new Vector2(0f, -154f);
        dividerRect.sizeDelta = new Vector2(-40f, 2f);

        GameObject scrollRoot = CreateUIObject("日志滚动区", panel.transform, typeof(Image), typeof(ItemStepScrollRect));
        RectTransform scrollRootRect = scrollRoot.GetComponent<RectTransform>();
        scrollRootRect.anchorMin = Vector2.zero;
        scrollRootRect.anchorMax = Vector2.one;
        scrollRootRect.offsetMin = new Vector2(22f, 62f);
        scrollRootRect.offsetMax = new Vector2(-22f, -168f);
        Image scrollBackground = scrollRoot.GetComponent<Image>();
        scrollBackground.color = new Color(0.006f, 0.012f, 0.016f, 0.96f);
        scrollBackground.raycastTarget = true;
        AddOutline(scrollBackground, Border);

        GameObject viewport = CreateUIObject("Viewport", scrollRoot.transform, typeof(RectMask2D));
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect);
        viewportRect.offsetMin = new Vector2(14f, 12f);
        viewportRect.offsetMax = new Vector2(-14f, -12f);

        TextMeshProUGUI logText = CreateText(
            "日志正文",
            viewport.transform,
            "暂无运行日志。",
            15f,
            new Color(0.82f, 0.86f, 0.84f, 1f));
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
        logText.lineSpacing = 4f;
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

        TextMeshProUGUI status = CreateText(
            "调试操作状态",
            panel.transform,
            "复制时将取最近 50 个去重日志槽位。",
            14f,
            Muted);
        status.enableWordWrapping = false;
        status.overflowMode = TextOverflowModes.Ellipsis;
        status.rectTransform.anchorMin = new Vector2(0f, 0f);
        status.rectTransform.anchorMax = new Vector2(1f, 0f);
        status.rectTransform.pivot = new Vector2(0.5f, 0f);
        status.rectTransform.anchoredPosition = new Vector2(0f, 17f);
        status.rectTransform.sizeDelta = new Vector2(-44f, 30f);

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
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        panel.SetActive(false);
        return root;
    }

    /// <summary>过滤勾选保留60像素触控区域，正文与复制共用其状态。</summary>
    private static Toggle CreateLogFilterToggle(Transform parent, string name, string caption, float left)
    {
        FlatWorld.Localization.Editor.FlatWorldLocalizationSetup.SyncRuntimeUiTexts(caption);
        Toggle toggle = CreateToggle(name, parent);
        SetTopLeft(toggle.GetComponent<RectTransform>(), left, 80f, 60f, 60f);
        toggle.SetIsOnWithoutNotify(true);
        TextMeshProUGUI label = CreateText(name + "标签", parent, caption, 20f, Cream);
        SetTopLeft(label.rectTransform, left + 72f, 92f, 108f, 36f);
        FlatWorldUIAutoLocalizer.BindStaticTexts(new TMP_Text[] { label });
        return toggle;
    }

    #endregion
}
