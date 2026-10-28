// 按业务职责维护 Prefab，复用公共控件与布局实现。
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static FlatWorldEditorUiLayout;

public static partial class RuntimeUIPrefabBuilder
{
    #region 加载界面

    /// <summary>构建主菜单启动阶段使用的资源加载面板，确保长错误信息在手机上可读。</summary>
    private static GameObject BuildResourceLoading()
    {
        GameObject root = new GameObject(
            RuntimeUIPrefabKeys.ResourceLoading,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup),
            typeof(CanvasRenderer),
            typeof(Image));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);
        rootRect.localScale = Vector3.one;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = UIManager.GlobalOverlaySortingOrder + 10;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        root.AddComponent<FullScreenRectController>();

        Image blocker = root.GetComponent<Image>();
        blocker.color = new Color(0.005f, 0.012f, 0.018f, 0.42f);
        blocker.raycastTarget = true;

        // 全屏遮罩保留原样，卡片单独适配安全区和窄屏。
        GameObject safeArea = CreateUIObject("资源加载安全区", root.transform, typeof(SafeAreaRectController));
        Stretch(safeArea.GetComponent<RectTransform>());

        GameObject card = CreateUIObject("资源加载内容", safeArea.transform, typeof(Image), typeof(Shadow));
        SetCentered(card.GetComponent<RectTransform>(), Vector2.zero, new Vector2(760f, 372f));
        RectTransform cardRect = card.GetComponent<RectTransform>();
        safeArea.AddComponent<SafeAreaScaleGroup>().Configure(cardRect, new[] { cardRect }, new Vector2(32f, 32f));
        Image cardImage = card.GetComponent<Image>();
        cardImage.color = new Color(0.025f, 0.043f, 0.058f, 0.98f);
        cardImage.raycastTarget = true;
        AddOutline(cardImage, Amber);
        Shadow shadow = card.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.72f);
        shadow.effectDistance = new Vector2(8f, -8f);

        VerticalLayoutGroup layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(42, 42, 30, 28);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText(GameRes.ResourceLoadingTitleKey, card.transform, "正在准备游戏", 28f, Cream);
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.enableAutoSizing = true;
        title.fontSizeMin = FlatWorldUITextLayout.MinimumFontSize(28f);
        title.fontSizeMax = 28f;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 46f;

        TextMeshProUGUI status = CreateText(GameRes.ResourceLoadingStatusKey, card.transform, "正在加载资源…", 18f, Muted);
        status.alignment = TextAlignmentOptions.Center;
        status.enableAutoSizing = true;
        status.fontSizeMin = FlatWorldUITextLayout.MinimumFontSize(18f);
        status.fontSizeMax = 18f;
        status.enableWordWrapping = true;
        status.overflowMode = TextOverflowModes.Ellipsis;
        LayoutElement statusLayout = status.gameObject.AddComponent<LayoutElement>();
        statusLayout.preferredHeight = 112f;
        statusLayout.minHeight = 88f;

        Slider progress = InstantiateSharedControl<Slider>("UI_ProgressBar", GameRes.ResourceLoadingProgressKey, card.transform);
        progress.interactable = false;
        progress.value = 0f;
        LayoutElement progressLayout = progress.GetComponent<LayoutElement>();
        progressLayout.flexibleWidth = 0f;
        progressLayout.preferredWidth = 650f;
        progressLayout.preferredHeight = 30f;
        Image progressFill = progress.fillRect.GetComponent<Image>();
        progressFill.gameObject.name = GameRes.ResourceLoadingProgressFillKey;
        progressFill.color = Teal;

        TextMeshProUGUI percent = CreateText(GameRes.ResourceLoadingProgressTextKey, card.transform, "0%", 16f, Teal);
        percent.alignment = TextAlignmentOptions.Center;
        percent.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;

        TextMeshProUGUI hint = CreateText("资源加载提示", card.transform, string.Empty, 16f, Muted);
        hint.gameObject.SetActive(false);
        hint.alignment = TextAlignmentOptions.Center;
        hint.enableAutoSizing = true;
        hint.fontSizeMin = FlatWorldUITextLayout.MinimumFontSize(16f);
        hint.fontSizeMax = 16f;
        hint.enableWordWrapping = false;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        return root;
    }

    /// <summary>构建进入世界阶段的全屏加载面板。</summary>
    private static GameObject BuildWorldLoading()
    {
        GameObject root = new GameObject(
            RuntimeUIPrefabKeys.WorldLoading,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup),
            typeof(CanvasRenderer),
            typeof(Image));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);
        rootRect.localScale = Vector3.one;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = UIManager.GlobalOverlaySortingOrder;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        root.AddComponent<FullScreenRectController>();

        Image overlay = root.GetComponent<Image>();
        // 世界加载期间完全遮挡玩法 UI，避免快捷栏、摇杆和按钮透出。
        overlay.color = new Color(0.012f, 0.012f, 0.014f, 1f);
        overlay.raycastTarget = true;

        GameObject card = CreateUIObject(GameManager.WorldLoadingContentKey, root.transform, typeof(Image), typeof(Shadow));
        CanvasGroup cardGroup = card.AddComponent<CanvasGroup>();
        cardGroup.interactable = false;
        cardGroup.blocksRaycasts = false;
        SetCentered(card.GetComponent<RectTransform>(), Vector2.zero, new Vector2(660f, 270f));
        Image cardImage = card.GetComponent<Image>();
        cardImage.color = Canvas;
        cardImage.raycastTarget = true;
        AddOutline(cardImage, Amber);
        Shadow shadow = card.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.65f);
        shadow.effectDistance = new Vector2(6f, -6f);

        VerticalLayoutGroup layout = card.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(34, 34, 28, 26);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText("加载标题", card.transform, "正在进入世界", 28f, Cream);
        title.fontStyle = FontStyles.Bold;
        title.alignment = TextAlignmentOptions.Center;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;

        TextMeshProUGUI status = CreateText("加载状态", card.transform, "正在准备世界数据…", 17f, Muted);
        status.alignment = TextAlignmentOptions.Center;
        status.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;

        Slider progress = InstantiateSharedControl<Slider>("UI_ProgressBar", "加载进度", card.transform);
        progress.interactable = false;
        progress.value = 0.08f;
        LayoutElement progressLayout = progress.GetComponent<LayoutElement>();
        progressLayout.flexibleWidth = 0f;
        progressLayout.preferredWidth = 560f;
        progressLayout.preferredHeight = 28f;

        TextMeshProUGUI percent = CreateText("加载进度文本", card.transform, "8%", 14f, Amber);
        percent.alignment = TextAlignmentOptions.Center;
        percent.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;

        TextMeshProUGUI hint = CreateText("加载提示", card.transform, string.Empty, 13f, Muted);
        hint.gameObject.SetActive(false);
        hint.alignment = TextAlignmentOptions.Center;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
        return root;
    }

    private static GameObject BuildDimensionLoading()
    {
        GameObject root = new GameObject(
            RuntimeUIPrefabKeys.DimensionLoading,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(CanvasGroup),
            typeof(CanvasRenderer),
            typeof(Image));
        RectTransform rootRect = root.GetComponent<RectTransform>();
        Stretch(rootRect);
        rootRect.localScale = Vector3.one;

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = UIManager.GlobalOverlaySortingOrder;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        CanvasGroup canvasGroup = root.GetComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        root.AddComponent<FullScreenRectController>();

        Image blocker = root.GetComponent<Image>();
        blocker.color = Color.clear;
        blocker.raycastTarget = true;

        Image background = CreateImage(GameManager.DimensionLoadingBackgroundKey, root.transform,
            new Color(0.055f, 0.055f, 0.065f, 1f));
        background.raycastTarget = false;
        Stretch(background.rectTransform);

        Image texture = CreateImage(GameManager.DimensionLoadingTextureKey, root.transform,
            new Color(1f, 1f, 1f, 0.16f));
        texture.raycastTarget = false;
        texture.type = Image.Type.Tiled;
        Stretch(texture.rectTransform);

        Image shade = CreateImage("像素暗角", root.transform, new Color(0.01f, 0.012f, 0.015f, 0.46f));
        shade.raycastTarget = false;
        Stretch(shade.rectTransform);

        // 背景仍铺满屏幕，仅让中央卡片跟随设备安全区等比收缩。
        GameObject safeArea = CreateUIObject("维度加载安全区", root.transform, typeof(SafeAreaRectController));
        Stretch(safeArea.GetComponent<RectTransform>());

        GameObject card = CreateUIObject("维度加载内容", safeArea.transform, typeof(Image), typeof(Shadow));
        SetCentered(card.GetComponent<RectTransform>(), Vector2.zero, new Vector2(720f, 560f));
        RectTransform cardRect = card.GetComponent<RectTransform>();
        safeArea.AddComponent<SafeAreaScaleGroup>().Configure(cardRect, new[] { cardRect }, new Vector2(32f, 32f));
        Image cardImage = card.GetComponent<Image>();
        cardImage.color = new Color(0.025f, 0.03f, 0.035f, 0.94f);
        cardImage.raycastTarget = false;
        AddOutline(cardImage, new Color(0.95f, 0.62f, 0.22f, 0.75f));
        Shadow shadow = card.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.75f);
        shadow.effectDistance = new Vector2(8f, -8f);

        TextMeshProUGUI eyebrow = CreateText("维度加载标题", card.transform, "维度跃迁", 17f, Muted);
        eyebrow.alignment = TextAlignmentOptions.Center;
        eyebrow.enableAutoSizing = true;
        eyebrow.fontSizeMin = FlatWorldUITextLayout.MinimumFontSize(17f);
        eyebrow.fontSizeMax = 17f;
        eyebrow.enableWordWrapping = false;
        eyebrow.overflowMode = TextOverflowModes.Ellipsis;
        SetCentered(eyebrow.rectTransform, new Vector2(0f, 218f), new Vector2(620f, 34f));

        Image iconFrame = CreateImage("维度图标边框", card.transform, new Color(0.08f, 0.085f, 0.09f, 1f));
        iconFrame.raycastTarget = false;
        SetCentered(iconFrame.rectTransform, new Vector2(0f, 112f), new Vector2(132f, 132f));
        AddOutline(iconFrame, Border);

        Image icon = CreateImage(GameManager.DimensionLoadingIconKey, iconFrame.transform, Color.white);
        icon.raycastTarget = false;
        icon.preserveAspect = true;
        Stretch(icon.rectTransform);
        icon.rectTransform.offsetMin = new Vector2(14f, 14f);
        icon.rectTransform.offsetMax = new Vector2(-14f, -14f);

        TextMeshProUGUI dimensionName = CreateText(GameManager.DimensionLoadingNameKey, card.transform, "地下矿洞", 34f, Amber);
        dimensionName.fontStyle = FontStyles.Bold;
        dimensionName.alignment = TextAlignmentOptions.Center;
        dimensionName.enableAutoSizing = true;
        dimensionName.fontSizeMin = FlatWorldUITextLayout.MinimumFontSize(34f);
        dimensionName.fontSizeMax = 34f;
        dimensionName.enableWordWrapping = false;
        dimensionName.overflowMode = TextOverflowModes.Ellipsis;
        SetCentered(dimensionName.rectTransform, new Vector2(0f, 18f), new Vector2(620f, 56f));

        TextMeshProUGUI status = CreateText(GameManager.DimensionLoadingStatusKey, card.transform, "正在创建目标维度…", 18f, Cream);
        status.alignment = TextAlignmentOptions.Center;
        status.enableAutoSizing = true;
        status.fontSizeMin = FlatWorldUITextLayout.MinimumFontSize(18f);
        status.fontSizeMax = 18f;
        status.enableWordWrapping = true;
        status.overflowMode = TextOverflowModes.Ellipsis;
        SetCentered(status.rectTransform, new Vector2(0f, -38f), new Vector2(620f, 44f));

        Slider progress = InstantiateSharedControl<Slider>("UI_ProgressBar", GameManager.DimensionLoadingProgressKey, card.transform);
        progress.interactable = false;
        progress.value = 0.48f;
        SetCentered(progress.GetComponent<RectTransform>(), new Vector2(0f, -91f), new Vector2(580f, 30f));
        Transform progressFill = progress.transform.Find("Fill Area/Fill");
        if (progressFill != null)
            progressFill.name = GameManager.DimensionLoadingProgressFillKey;

        TextMeshProUGUI percent = CreateText(GameManager.DimensionLoadingProgressTextKey, card.transform, "48%", 17f, Amber);
        percent.alignment = TextAlignmentOptions.Center;
        SetCentered(percent.rectTransform, new Vector2(0f, -133f), new Vector2(180f, 28f));

        TextMeshProUGUI hint = CreateText(GameManager.DimensionLoadingHintKey, card.transform,
            string.Empty, 16f, Muted);
        hint.gameObject.SetActive(false);
        hint.alignment = TextAlignmentOptions.Center;
        hint.enableAutoSizing = true;
        hint.fontSizeMin = FlatWorldUITextLayout.MinimumFontSize(16f);
        hint.fontSizeMax = 16f;
        hint.enableWordWrapping = false;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        SetCentered(hint.rectTransform, new Vector2(0f, -192f), new Vector2(620f, 30f));

        Image lowerLine = CreateImage("底部像素强调线", card.transform, Amber);
        lowerLine.raycastTarget = false;
        SetCentered(lowerLine.rectTransform, new Vector2(0f, -238f), new Vector2(580f, 4f));
        return root;
    }

    #endregion
}
