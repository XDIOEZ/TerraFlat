// AI-Context: 编辑器新游戏 Prefab 重建器；根节点直接组合 BasePanel，不得改名 GameManager 依赖的控件节点。

using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class NewGamePrefabBuilder
{
    private const string PrefabPath = "Assets/2_Prefabs/2-1_UI/MainMenu/WorldSetup/UI_NewGame.prefab";
    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";

    private static Color Ink => FlatWorldUITheme.Canvas;
    private static Color InkSoft => FlatWorldUITheme.SurfaceLow;
    private static Color Surface => FlatWorldUITheme.Surface;
    private static Color Cream => FlatWorldUITheme.TextPrimary;
    private static Color Muted => FlatWorldUITheme.TextSecondary;
    private static Color Amber => FlatWorldUITheme.Accent;
    private static Color Teal => FlatWorldUITheme.Teal;

    #region 新世界分页布局

    private const float ContentPageWidth = 1316f;
    private const float ContentInnerWidth = ContentPageWidth - 80f;

    #endregion

    [MenuItem("FlatWorld/UI/Rebuild New Game UI")]
    public static void RebuildNewGameInterface()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (font == null)
        {
            Debug.LogError("[NewGameUI] 未找到项目像素字体。");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            ClearChildren(root.transform);
            ConfigureRoot(root);
            BuildScrim(root.transform);
            Image card = BuildCard(root.transform);
            BuildHeader(card.transform, font);
            BuildIdentity(card.transform, font);
            BuildWorldSettings(card.transform, font);
            BuildFooter(card.transform, font);
            RectTransform difficultyDialog = BuildDifficultyPanel(root.transform, font);
            ConfigureResponsiveScale(root, card.rectTransform, difficultyDialog);

            FlatWorldUITheme.Apply(root.transform);
            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        var localizationTexts = new List<string>
        {
            "世界设置",
            "返回名称设置",
            "可留空，自动生成名称",
            "选择 0 - 20 级挑战强度",
            "规则修改",
            "相对于难度 0",
            "死亡惩罚",
            "全部掉落",
            "保留物品",
            "时间流速",
            "战利品数量",
            "生长速度",
            "选择 0 - 20 级难度后点击应用。",
            "当前存档难度：难度 0"
        };
        for (int i = 0; i < GameDifficultyCatalog.All.Count; i++)
        {
            GameDifficultyDefinition definition = GameDifficultyCatalog.All[i];
            localizationTexts.Add(definition.DisplayName);
            localizationTexts.Add(definition.Description);
        }

        FlatWorld.Localization.Editor.FlatWorldLocalizationSetup.SyncRuntimeUiTexts(
            localizationTexts.ToArray());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[NewGameUI] 新世界创建界面已重建。");
    }

    private static void ConfigureRoot(GameObject root)
    {
        root.name = "UI_NewGame";
        root.layer = LayerMask.NameToLayer("UI");

        RectTransform rect = root.GetComponent<RectTransform>();
        Stretch(rect);

        CanvasGroup group = root.GetComponent<CanvasGroup>();
        if (group == null)
            group = root.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;

        BasePanel panel = root.GetComponent<BasePanel>();
        if (panel == null)
            panel = root.AddComponent<BasePanel>();

        panel.canvasGroup = group;
        panel.rectTransform = rect;
        panel.PanelName = GameManager.NewGamePanelKey;
        RuntimeUIPrefabBuilder.ConfigureScaleAnimation(panel);
    }

    private static void BuildScrim(Transform root)
    {
        Image scrim = CreateImage("新世界界面遮罩", root, Color.clear);
        Stretch(scrim.rectTransform);
        scrim.gameObject.AddComponent<FullScreenRectController>();
        scrim.raycastTarget = true;
    }

    private static Image BuildCard(Transform root)
    {
        Image shadow = CreateImage("新世界主卡投影", root, new Color(0f, 0f, 0f, 0.42f));
        SetRect(shadow.rectTransform, new Vector2(14f, -16f), FlatWorldUIPanelMetrics.SharedModalCardSize, new Vector2(0.5f, 0.5f));
        shadow.raycastTarget = false;

        Image card = CreateImage("新世界主卡", root, Ink);
        SetRect(card.rectTransform, Vector2.zero, FlatWorldUIPanelMetrics.SharedModalCardSize, new Vector2(0.5f, 0.5f));

        Outline outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.34f);
        outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;

        Image accent = CreateImage("新世界主卡强调线", card.transform, Amber);
        accent.rectTransform.anchorMin = new Vector2(0f, 0f);
        accent.rectTransform.anchorMax = new Vector2(0f, 1f);
        accent.rectTransform.pivot = new Vector2(0f, 0.5f);
        accent.rectTransform.anchoredPosition = Vector2.zero;
        accent.rectTransform.sizeDelta = new Vector2(6f, 0f);
        accent.raycastTarget = false;
        return card;
    }

    /// <summary>让主卡与难度弹窗共用安全区限幅，极端宽高比下只等比缩小、不放大。</summary>
    private static void ConfigureResponsiveScale(
        GameObject root,
        RectTransform card,
        RectTransform difficultyDialog)
    {
        RectTransform shadow = root.transform.Find("新世界主卡投影") as RectTransform;
        SafeAreaScaleGroup scaleGroup = root.GetComponent<SafeAreaScaleGroup>();
        if (scaleGroup == null)
            scaleGroup = root.AddComponent<SafeAreaScaleGroup>();

        scaleGroup.Configure(
            difficultyDialog != null ? difficultyDialog : card,
            new[] { shadow, card, difficultyDialog },
            new Vector2(24f, 24f));
    }

    private static void BuildHeader(Transform card, TMP_FontAsset font)
    {
        TMP_Text title = CreateText("新世界标题", card, "创建新世界", font, 48f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(42f, -34f), new Vector2(700f, 68f), new Vector2(0f, 1f));

        CreateButton(card, font, GameManager.NewGameBackButtonKey, "返回主界面", new Vector2(-42f, -28f), new Vector2(210f, 72f), InkSoft, 22f, new Vector2(1f, 1f));

        Image divider = CreateImage("新世界标题分隔线", card, new Color(0.55f, 0.64f, 0.65f, 0.18f));
        divider.rectTransform.anchorMin = new Vector2(0f, 1f);
        divider.rectTransform.anchorMax = new Vector2(1f, 1f);
        divider.rectTransform.pivot = new Vector2(0.5f, 1f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, -126f);
        divider.rectTransform.sizeDelta = new Vector2(-84f, 1f);
        divider.raycastTarget = false;
    }

    #region 名称与世界设置分页

    private static void BuildIdentity(Transform card, TMP_FontAsset font)
    {
        Image panel = CreatePanelCard(GameManager.NewGameIdentityPageKey, card);
        SetRect(panel.rectTransform, new Vector2((FlatWorldUIPanelMetrics.SharedModalCardSize.x - ContentPageWidth) * 0.5f, -150f), new Vector2(ContentPageWidth, 520f), new Vector2(0f, 1f));

        TMP_Text heading = CreateText("身份与存档标题", panel.transform, "身份与存档", font, 34f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(heading.rectTransform, new Vector2(40f, -28f), new Vector2(ContentInnerWidth, 44f), new Vector2(0f, 1f));

        CreateLabel(panel.transform, font, "玩家名称标签", "玩家名称", "可留空，自动生成名称", new Vector2(40f, -96f), ContentInnerWidth);
        CreateInput(panel.transform, font, GameManager.NewGamePlayerInputKey, "可选，例如：旅人", string.Empty, new Vector2(40f, -140f), new Vector2(ContentInnerWidth, 104f), TMP_InputField.ContentType.Standard);

        CreateLabel(panel.transform, font, "存档名称标签", "存档名称", "可留空，自动生成名称", new Vector2(40f, -270f), ContentInnerWidth);
        CreateInput(panel.transform, font, GameManager.NewGameSaveInputKey, "可选，例如：篝火以北", string.Empty, new Vector2(40f, -314f), new Vector2(ContentInnerWidth, 104f), TMP_InputField.ContentType.Standard);
    }

    private static void BuildWorldSettings(Transform card, TMP_FontAsset font)
    {
        Image panel = CreatePanelCard(GameManager.NewGameWorldPageKey, card);
        SetRect(panel.rectTransform, new Vector2((FlatWorldUIPanelMetrics.SharedModalCardSize.x - ContentPageWidth) * 0.5f, -150f), new Vector2(ContentPageWidth, 520f), new Vector2(0f, 1f));

        TMP_Text heading = CreateText("世界参数标题", panel.transform, "世界设置", font, 34f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(heading.rectTransform, new Vector2(40f, -28f), new Vector2(ContentInnerWidth, 44f), new Vector2(0f, 1f));

        RectTransform content = BuildWorldSettingsScrollView(panel.transform);

        string defaultRadius = PlanetData.DefaultRadius.ToString(CultureInfo.InvariantCulture);
        CreateWorldSettingInputRow(
            content,
            font,
            "星球半径设置项",
            "星球半径",
            "探索范围",
            GameManager.NewGameRadiusInputKey,
            defaultRadius,
            defaultRadius,
            TMP_InputField.ContentType.IntegerNumber);

        string defaultNoiseScale = PlanetData.DefaultNoiseScale.ToString("0.########", CultureInfo.InvariantCulture);
        CreateWorldSettingInputRow(
            content,
            font,
            "坐标缩放设置项",
            "坐标缩放",
            "地貌疏密",
            GameManager.NewGameNoiseInputKey,
            defaultNoiseScale,
            defaultNoiseScale,
            TMP_InputField.ContentType.DecimalNumber);

        string defaultChunkSize = NewWorldUserSettings.DefaultChunkDimension.ToString(CultureInfo.InvariantCulture);
        CreateWorldSettingInputRow(
            content,
            font,
            "区块宽度设置项",
            "区块宽度",
            "1–256 格",
            GameManager.NewGameChunkWidthInputKey,
            defaultChunkSize,
            defaultChunkSize,
            TMP_InputField.ContentType.IntegerNumber);
        CreateWorldSettingInputRow(
            content,
            font,
            "区块高度设置项",
            "区块高度",
            "建议 16–64",
            GameManager.NewGameChunkHeightInputKey,
            defaultChunkSize,
            defaultChunkSize,
            TMP_InputField.ContentType.IntegerNumber);

        Toggle topologyToggle = CreateToggle(
            content,
            font,
            GameManager.NewGameTopologyToggleKey,
            "有限循环世界",
            "越过上下左右边界后从对侧返回；关闭则使用原有无限世界。",
            Vector2.zero,
            new Vector2(ContentInnerWidth, 86f));
        LayoutElement topologyLayout = topologyToggle.gameObject.AddComponent<LayoutElement>();
        topologyLayout.preferredHeight = 86f;
        topologyToggle.isOn = true;

        CreateWorldSettingInputRow(
            content,
            font,
            "世界种子设置项",
            "世界种子",
            "留空则随机 · 支持数字或文字",
            GameManager.NewGameSeedInputKey,
            "留空自动生成随机种子",
            string.Empty,
            TMP_InputField.ContentType.Standard);
        panel.gameObject.SetActive(false);
    }

    /// <summary>世界设置正文使用正式滚动列表，后续新增设置只需要继续向 Content 追加条目。</summary>
    private static RectTransform BuildWorldSettingsScrollView(Transform panel)
    {
        GameObject scrollObject = new GameObject("世界设置滚动区", typeof(RectTransform), typeof(ItemStepScrollRect));
        scrollObject.layer = LayerMask.NameToLayer("UI");
        scrollObject.transform.SetParent(panel, false);
        SetRect(scrollObject.GetComponent<RectTransform>(), new Vector2(28f, -88f), new Vector2(ContentPageWidth - 56f, 404f), new Vector2(0f, 1f));

        GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
        viewportObject.layer = LayerMask.NameToLayer("UI");
        viewportObject.transform.SetParent(scrollObject.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        Stretch(viewport);
        Image viewportHitArea = viewportObject.GetComponent<Image>();
        viewportHitArea.color = Color.clear;
        viewportHitArea.raycastTarget = true;

        GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.layer = LayerMask.NameToLayer("UI");
        contentObject.transform.SetParent(viewportObject.transform, false);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 20);
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ItemStepScrollRect scroll = scrollObject.GetComponent<ItemStepScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        return content;
    }

    /// <summary>单条世界设置固定为“说明 + 右侧输入框”，由父级布局统一管理高度与间距。</summary>
    private static TMP_InputField CreateWorldSettingInputRow(
        Transform parent,
        TMP_FontAsset font,
        string rowName,
        string title,
        string subtitle,
        string inputName,
        string placeholderValue,
        string initialValue,
        TMP_InputField.ContentType contentType)
    {
        Image row = CreateImage(rowName, parent, InkSoft);
        LayoutElement layout = row.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = 92f;

        Outline outline = row.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.55f, 0.64f, 0.65f, 0.18f);
        outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;

        TMP_Text titleText = CreateText(rowName + "_标题", row.transform, title, font, 20f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(titleText.rectTransform, new Vector2(18f, -12f), new Vector2(640f, 30f), new Vector2(0f, 1f));

        TMP_Text subtitleText = CreateText(rowName + "_说明", row.transform, subtitle, font, 16f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(subtitleText.rectTransform, new Vector2(18f, -46f), new Vector2(640f, 26f), new Vector2(0f, 1f));

        return CreateInput(
            row.transform,
            font,
            inputName,
            placeholderValue,
            initialValue,
            new Vector2(-18f, -14f),
            new Vector2(360f, 64f),
            new Vector2(1f, 1f),
            contentType);
    }

    private static void BuildFooter(Transform card, TMP_FontAsset font)
    {
        Image divider = CreateImage("新世界操作分隔线", card, new Color(0.55f, 0.64f, 0.65f, 0.18f));
        divider.rectTransform.anchorMin = new Vector2(0f, 0f);
        divider.rectTransform.anchorMax = new Vector2(1f, 0f);
        divider.rectTransform.pivot = new Vector2(0.5f, 0f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, 98f);
        divider.rectTransform.sizeDelta = new Vector2(-84f, 1f);
        divider.raycastTarget = false;

        CreateButton(card, font, GameManager.NewGameDifficultyButtonKey, "难度设置  ·  难度 0", new Vector2(42f, 20f), new Vector2(300f, 80f), InkSoft, 22f, new Vector2(0f, 0f));

        CreatePageButton(card, GameManager.NewGameWorldSettingsButtonKey, "世界设置");
        Button identityButton = CreatePageButton(card, GameManager.NewGameIdentityButtonKey, "返回名称设置");
        identityButton.gameObject.SetActive(false);

        CreateButton(card, font, GameManager.NewGameStartButtonKey, "生成新世界", new Vector2(-42f, 20f), new Vector2(280f, 80f), new Color(0.70f, 0.36f, 0.16f, 1f), 25f, new Vector2(1f, 0f));
    }

    /// <summary>分页入口使用正式公共按钮，外部布局与底部原有操作对齐。</summary>
    private static Button CreatePageButton(Transform card, string name, string caption)
    {
        RuntimeUIPrefabBuilder.EnsureSharedControlPrefabs();
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeUIPrefabBuilder.SharedControlsRoot + "UI_Button.prefab");
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab, card) as GameObject;
        instance.name = name;
        SetRect(instance.GetComponent<RectTransform>(), new Vector2(368f, 20f), new Vector2(300f, 80f), Vector2.zero);
        LayoutElement layout = instance.GetComponent<LayoutElement>();
        layout.preferredWidth = 300f;
        layout.preferredHeight = 80f;
        instance.GetComponentInChildren<TMP_Text>(true).text = caption;
        return instance.GetComponent<Button>();
    }

    #endregion

    private static RectTransform BuildDifficultyPanel(Transform root, TMP_FontAsset font)
    {
        Image overlay = CreateImage(GameManager.NewGameDifficultyPanelKey, root, Color.clear);
        Stretch(overlay.rectTransform);
        overlay.raycastTarget = true;
        overlay.gameObject.AddComponent<FullScreenRectController>();

        Image dialog = CreatePanelCard("难度设置窗口", overlay.transform);
        SetRect(dialog.rectTransform, Vector2.zero, new Vector2(1500f, 840f), new Vector2(0.5f, 0.5f));

        Image titleAccent = CreateImage("难度标题强调线", dialog.transform, Amber);
        SetRect(titleAccent.rectTransform, new Vector2(48f, -28f), new Vector2(8f, 66f), new Vector2(0f, 1f));
        titleAccent.raycastTarget = false;

        TMP_Text title = CreateText("难度设置标题", dialog.transform, "难度设置", font, 44f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(76f, -24f), new Vector2(692f, 56f), new Vector2(0f, 1f));

        TMP_Text subtitle = CreateText("难度设置副标题", dialog.transform, "选择 0 - 20 级挑战强度", font, 20f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(subtitle.rectTransform, new Vector2(76f, -80f), new Vector2(692f, 32f), new Vector2(0f, 1f));

        CreateButton(dialog.transform, font, GameManager.NewGameDifficultyCloseButtonKey, "关闭", new Vector2(-48f, -28f), new Vector2(170f, 72f), InkSoft, 24f, new Vector2(1f, 1f));

        Image divider = CreateImage("难度标题分隔线", dialog.transform, new Color(0.55f, 0.64f, 0.65f, 0.18f));
        divider.rectTransform.anchorMin = new Vector2(0f, 1f);
        divider.rectTransform.anchorMax = new Vector2(1f, 1f);
        divider.rectTransform.pivot = new Vector2(0.5f, 1f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, -128f);
        divider.rectTransform.sizeDelta = new Vector2(-72f, 1f);
        divider.raycastTarget = false;

        BuildOfficialDifficultyPage(dialog.transform, font);
        BuildDifficultyDetails(dialog.transform, font);

        CreateButton(dialog.transform, font, GameManager.NewGameDifficultyConfirmButtonKey, "确认选择  >", new Vector2(-48f, 24f), new Vector2(300f, 72f), Amber, 25f, new Vector2(1f, 0f));
        overlay.gameObject.SetActive(false);
        return dialog.rectTransform;
    }

    private static void BuildOfficialDifficultyPage(Transform dialog, TMP_FontAsset font)
    {
        Image page = CreatePanelCard(GameManager.NewGameDifficultyOfficialPageKey, dialog);
        SetRect(page.rectTransform, new Vector2(48f, -152f), new Vector2(430f, 606f), new Vector2(0f, 1f));

        GameObject scrollObject = new GameObject("官方预设列表", typeof(RectTransform), typeof(ItemStepScrollRect));
        scrollObject.layer = LayerMask.NameToLayer("UI");
        scrollObject.transform.SetParent(page.transform, false);
        Stretch(scrollObject.GetComponent<RectTransform>(), 14f, 14f, 14f, 14f);

        GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
        viewportObject.layer = LayerMask.NameToLayer("UI");
        viewportObject.transform.SetParent(scrollObject.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        Stretch(viewport, 0f, 18f, 0f, 0f);
        Image viewportHitArea = viewportObject.GetComponent<Image>();
        viewportHitArea.color = new Color(1f, 1f, 1f, 0.001f);
        viewportHitArea.raycastTarget = true;

        GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        contentObject.layer = LayerMask.NameToLayer("UI");
        contentObject.transform.SetParent(viewportObject.transform, false);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;

        VerticalLayoutGroup layout = contentObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 6, 0, 0);
        layout.spacing = 4f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject scrollbarObject = new GameObject("Scrollbar Vertical", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Scrollbar));
        scrollbarObject.layer = LayerMask.NameToLayer("UI");
        scrollbarObject.transform.SetParent(scrollObject.transform, false);
        RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = Vector2.one;
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = new Vector2(-12f, 4f);
        scrollbarRect.offsetMax = new Vector2(0f, -4f);
        scrollbarObject.GetComponent<Image>().color = Ink;

        GameObject slidingArea = new GameObject("Sliding Area", typeof(RectTransform));
        slidingArea.layer = LayerMask.NameToLayer("UI");
        slidingArea.transform.SetParent(scrollbarObject.transform, false);
        Stretch(slidingArea.GetComponent<RectTransform>(), 2f, 2f, 2f, 2f);
        Image handle = CreateImage("Handle", slidingArea.transform, Muted);
        Stretch(handle.rectTransform);
        Scrollbar scrollbar = scrollbarObject.GetComponent<Scrollbar>();
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 26f;
        scroll.verticalScrollbar = scrollbar;

        for (int i = 0; i < GameDifficultyCatalog.All.Count; i++)
        {
            GameDifficultyDefinition definition = GameDifficultyCatalog.All[i];
            CreateDifficultyPresetButton(
                content,
                font,
                GameManager.GetNewGameDifficultyPresetButtonKey(definition.Id),
                definition.DisplayName,
                definition.Id == GameDifficultyId.Level0);
        }
    }

    private static void BuildCustomDifficultyPage(Transform dialog, TMP_FontAsset font)
    {
        Image page = CreateImage(GameManager.NewGameDifficultyCustomPageKey, dialog, Surface);
        SetRect(page.rectTransform, new Vector2(48f, -242f), new Vector2(560f, 500f), new Vector2(0f, 1f));

        TMP_Text heading = CreateText("自定义规则标题", page.transform, "自定义规则", font, 28f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(heading.rectTransform, new Vector2(20f, -16f), new Vector2(500f, 40f), new Vector2(0f, 1f));

        TMP_Text caption = CreateText("自定义规则说明", page.transform, "17 项规则均已接入实际游戏系统。", font, 18f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(caption.rectTransform, new Vector2(20f, -60f), new Vector2(520f, 30f), new Vector2(0f, 1f));
        caption.enableWordWrapping = true;
        caption.overflowMode = TextOverflowModes.Ellipsis;

        CreateButton(page.transform, font, GameManager.NewGameDifficultyCombatCategoryKey, "战斗", new Vector2(20f, -100f), new Vector2(124f, 54f), Amber, 18f, new Vector2(0f, 1f));
        CreateButton(page.transform, font, GameManager.NewGameDifficultySurvivalCategoryKey, "生存", new Vector2(152f, -100f), new Vector2(124f, 54f), InkSoft, 18f, new Vector2(0f, 1f));
        CreateButton(page.transform, font, GameManager.NewGameDifficultyWorldCategoryKey, "世界", new Vector2(284f, -100f), new Vector2(124f, 54f), InkSoft, 18f, new Vector2(0f, 1f));
        CreateButton(page.transform, font, GameManager.NewGameDifficultyProductionCategoryKey, "生产", new Vector2(416f, -100f), new Vector2(124f, 54f), InkSoft, 18f, new Vector2(0f, 1f));

        BuildCustomCombatPage(page.transform, font);
        BuildCustomSurvivalPage(page.transform, font);
        BuildCustomWorldPage(page.transform, font);
        BuildCustomProductionPage(page.transform, font);
    }

    private static void BuildCustomCombatPage(Transform parent, TMP_FontAsset font)
    {
        Image page = CreateCustomCategoryPage(GameManager.NewGameDifficultyCombatPageKey, parent);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyPlayerAttackSliderKey, "玩家伤害", "玩家及手持武器造成的伤害", 0f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyCreatureAttackSliderKey, "生物伤害", "非玩家攻击者造成的伤害", 68f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyCreatureHealthSliderKey, "生物生命", "生物与可破坏实体的等效耐久", 136f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyEnvironmentalDamageSliderKey, "环境伤害", "饥饿、温度、出血与真实伤害", 204f);
    }

    private static void BuildCustomSurvivalPage(Transform parent, TMP_FontAsset font)
    {
        Image page = CreateCustomCategoryPage(GameManager.NewGameDifficultySurvivalPageKey, parent);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyHungerDrainSliderKey, "饥饿消耗", "营养与水分自然消耗速度", 0f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyStaminaConsumptionSliderKey, "耐力消耗", "移动、奔跑与攻击耐力消耗", 68f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyStaminaRecoverySliderKey, "耐力恢复", "营养充足时的耐力恢复", 136f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyHealingSliderKey, "治疗效果", "食物、睡眠和其他治疗效果", 204f);
        CreateCompactDifficultyToggle(page.transform, font, GameManager.NewGameDifficultyDropToggleKey, "死亡掉落全部随身物品", 272f);
    }

    private static void BuildCustomWorldPage(Transform parent, TMP_FontAsset font)
    {
        Image page = CreateCustomCategoryPage(GameManager.NewGameDifficultyWorldPageKey, parent);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyTimeSpeedSliderKey, "时间流逝", "昼夜与游戏日推进速度", 0f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultySpawnFrequencySliderKey, "生成频率", "每日生成窗口与每次生成数量", 68f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultySpawnPopulationSliderKey, "种群上限", "生态预算与生物存活上限", 136f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyLootAmountSliderKey, "战利品", "生物、资源与植物产出数量", 204f);
    }

    private static void BuildCustomProductionPage(Transform parent, TMP_FontAsset font)
    {
        Image page = CreateCustomCategoryPage(GameManager.NewGameDifficultyProductionPageKey, parent);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyCropGrowthSliderKey, "作物生长", "种子、作物和浆果成熟速度", 0f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultySmeltingSpeedSliderKey, "熔炼速度", "熔炉生产进度速度", 68f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyFuelConsumptionSliderKey, "燃料消耗", "所有燃料模块的消耗速度", 136f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyCraftingOutputSliderKey, "制作产量", "手工、工作台与熔炉产量", 204f);
    }

    private static Image CreateCustomCategoryPage(string name, Transform parent)
    {
        Image page = CreateImage(name, parent, new Color(0.035f, 0.06f, 0.075f, 0.98f));
        SetRect(page.rectTransform, new Vector2(20f, -164f), new Vector2(520f, 336f), new Vector2(0f, 1f));
        return page;
    }

    private static void CreateDifficultySlider(
        Transform parent,
        TMP_FontAsset font,
        string name,
        string title,
        string description,
        float top)
    {
        GameObject row = new GameObject(name + "_行", typeof(RectTransform));
        row.layer = LayerMask.NameToLayer("UI");
        row.transform.SetParent(parent, false);
        SetRect(row.GetComponent<RectTransform>(), new Vector2(12f, -top), new Vector2(496f, 64f), new Vector2(0f, 1f));

        TMP_Text titleText = CreateText(name + "_标题", row.transform, title, font, 18f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(titleText.rectTransform, new Vector2(0f, 0f), new Vector2(210f, 26f), new Vector2(0f, 1f));
        TMP_Text descriptionText = CreateText(name + "_说明", row.transform, description, font, 15f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(descriptionText.rectTransform, new Vector2(0f, -27f), new Vector2(250f, 28f), new Vector2(0f, 1f));

        TMP_Text valueText = CreateText(name + "_数值", row.transform, "100%", font, 18f, Amber, FontStyles.Bold, TextAlignmentOptions.Right);
        SetRect(valueText.rectTransform, new Vector2(-2f, 0f), new Vector2(72f, 24f), new Vector2(1f, 1f));

        GameObject sliderObject = new GameObject(name, typeof(RectTransform), typeof(Slider));
        sliderObject.layer = LayerMask.NameToLayer("UI");
        sliderObject.transform.SetParent(row.transform, false);
        SetRect(sliderObject.GetComponent<RectTransform>(), new Vector2(-2f, -32f), new Vector2(230f, 28f), new Vector2(1f, 1f));

        Image background = CreateImage("Background", sliderObject.transform, InkSoft);
        Stretch(background.rectTransform, 0f, 0f, 6f, 6f);

        GameObject fillAreaObject = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaObject.layer = LayerMask.NameToLayer("UI");
        fillAreaObject.transform.SetParent(sliderObject.transform, false);
        RectTransform fillArea = fillAreaObject.GetComponent<RectTransform>();
        Stretch(fillArea, 3f, 3f, 6f, 6f);
        Image fill = CreateImage("Fill", fillArea, Amber);
        Stretch(fill.rectTransform);

        GameObject handleAreaObject = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleAreaObject.layer = LayerMask.NameToLayer("UI");
        handleAreaObject.transform.SetParent(sliderObject.transform, false);
        RectTransform handleArea = handleAreaObject.GetComponent<RectTransform>();
        Stretch(handleArea, 5f, 5f);
        Image handle = CreateImage("Handle", handleArea, Cream);
        SetRect(handle.rectTransform, Vector2.zero, new Vector2(10f, 22f), new Vector2(0.5f, 0.5f));

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 3f;
        slider.value = 1f;
        slider.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    private static void CreateCompactDifficultyToggle(
        Transform parent,
        TMP_FontAsset font,
        string name,
        string title,
        float top)
    {
        GameObject toggleObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
        toggleObject.layer = LayerMask.NameToLayer("UI");
        toggleObject.transform.SetParent(parent, false);
        SetRect(toggleObject.GetComponent<RectTransform>(), new Vector2(12f, -top), new Vector2(496f, 56f), new Vector2(0f, 1f));

        Image background = toggleObject.GetComponent<Image>();
        background.color = InkSoft;
        Image box = CreateImage("选择框", toggleObject.transform, new Color(0.02f, 0.04f, 0.05f, 1f));
        SetRect(box.rectTransform, new Vector2(12f, -12f), new Vector2(30f, 30f), new Vector2(0f, 1f));
        Image mark = CreateImage("勾选标记", box.transform, Amber);
        Stretch(mark.rectTransform, 5f, 5f, 5f, 5f);

        TMP_Text label = CreateText(name + "_标题", toggleObject.transform, title, font, 18f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(label.rectTransform, new Vector2(58f, -10f), new Vector2(420f, 34f), new Vector2(0f, 1f));

        Toggle toggle = toggleObject.GetComponent<Toggle>();
        toggle.targetGraphic = box;
        toggle.graphic = mark;
        toggle.isOn = false;
        toggle.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    private static void BuildDifficultyDetails(Transform dialog, TMP_FontAsset font)
    {
        Image details = CreatePanelCard("难度详情区", dialog);
        SetRect(details.rectTransform, new Vector2(502f, -152f), new Vector2(950f, 566f), new Vector2(0f, 1f));

        TMP_Text title = CreateText(GameManager.NewGameDifficultyTitleTextKey, details.transform, "难度 0", font, 46f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(28f, -20f), new Vector2(894f, 60f), new Vector2(0f, 1f));

        TMP_Text description = CreateText(GameManager.NewGameDifficultyDescriptionTextKey, details.transform, "基础规则，适合熟悉世界与系统。", font, 20f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(description.rectTransform, new Vector2(28f, -82f), new Vector2(894f, 48f), new Vector2(0f, 1f));
        description.enableWordWrapping = true;
        description.overflowMode = TextOverflowModes.Ellipsis;

        Image divider = CreateImage("难度详情分隔线", details.transform, new Color(0.55f, 0.64f, 0.65f, 0.18f));
        SetRect(divider.rectTransform, new Vector2(28f, -142f), new Vector2(894f, 1f), new Vector2(0f, 1f));
        divider.raycastTarget = false;

        TMP_Text ruleHeading = CreateText("难度规则标题", details.transform, "规则修改", font, 22f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(ruleHeading.rectTransform, new Vector2(28f, -158f), new Vector2(300f, 30f), new Vector2(0f, 1f));
        TMP_Text baseline = CreateText("难度规则基准", details.transform, "相对于难度 0", font, 16f, Muted, FontStyles.Normal, TextAlignmentOptions.Right);
        SetRect(baseline.rectTransform, new Vector2(-28f, -160f), new Vector2(260f, 28f), new Vector2(1f, 1f));

        CreateDifficultyRuleCard(
            details.transform,
            font,
            "战斗",
            "玩家伤害\n生物伤害\n生物生命",
            GameManager.NewGameDifficultyCombatValuesTextKey,
            202f);
        CreateDifficultyRuleCard(
            details.transform,
            font,
            "生存",
            "饥饿消耗\n耐力消耗\n死亡惩罚",
            GameManager.NewGameDifficultySurvivalValuesTextKey,
            294f);
        CreateDifficultyRuleCard(
            details.transform,
            font,
            "世界",
            "时间流速\n生成频率\n战利品数量",
            GameManager.NewGameDifficultyWorldValuesTextKey,
            386f);
        CreateDifficultyRuleCard(
            details.transform,
            font,
            "生产",
            "生长速度\n熔炼速度\n制作产量",
            GameManager.NewGameDifficultyProductionValuesTextKey,
            478f);
    }

    private static void CreateDifficultyRuleCard(
        Transform parent,
        TMP_FontAsset font,
        string category,
        string labels,
        string valuesTextKey,
        float top)
    {
        Image card = CreateImage("规则_" + category, parent, InkSoft);
        SetRect(card.rectTransform, new Vector2(28f, -top), new Vector2(894f, 82f), new Vector2(0f, 1f));
        Outline outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 1f, 1f, 0.10f);
        outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;
        card.raycastTarget = false;

        TMP_Text categoryText = CreateText("规则_" + category + "_标题", card.transform, category, font, 22f, Cream, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        SetRect(categoryText.rectTransform, new Vector2(18f, -12f), new Vector2(150f, 58f), new Vector2(0f, 1f));

        Image divider = CreateImage("规则_" + category + "_分隔线", card.transform, new Color(1f, 1f, 1f, 0.12f));
        SetRect(divider.rectTransform, new Vector2(168f, -10f), new Vector2(1f, 62f), new Vector2(0f, 1f));
        divider.raycastTarget = false;

        TMP_Text labelText = CreateText("规则_" + category + "_字段", card.transform, labels, font, 16f, Muted, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        SetRect(labelText.rectTransform, new Vector2(192f, -9f), new Vector2(480f, 64f), new Vector2(0f, 1f));
        labelText.lineSpacing = 2f;

        TMP_Text values = CreateText(valuesTextKey, card.transform, "0%\n0%\n0%", font, 17f, Amber, FontStyles.Bold, TextAlignmentOptions.TopRight);
        SetRect(values.rectTransform, new Vector2(-18f, -9f), new Vector2(180f, 64f), new Vector2(1f, 1f));
        values.lineSpacing = 2f;
    }

    private static void CreateDifficultyPresetButton(Transform parent, TMP_FontAsset font, string name, string title, bool selected)
    {
        Button button = CreateButton(parent, font, name, string.Empty, Vector2.zero, new Vector2(380f, 58f), selected ? Amber : InkSoft, 21f, new Vector2(0f, 1f));
        button.gameObject.AddComponent<LayoutElement>().preferredHeight = 58f;
        Outline buttonOutline = button.GetComponent<Outline>();
        if (buttonOutline != null)
            buttonOutline.effectColor = selected ? new Color(0.95f, 0.82f, 0.40f, 0.7f) : new Color(1f, 1f, 1f, 0.10f);
        TMP_Text generatedLabel = button.GetComponentInChildren<TMP_Text>();
        if (generatedLabel != null)
            generatedLabel.gameObject.SetActive(false);

        TMP_Text titleText = CreateText(name + "_标题", button.transform, title, font, 22f, Cream, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        Stretch(titleText.rectTransform, 18f, 48f, 0f, 0f);

        TMP_Text arrow = CreateText(name + "_箭头", button.transform, ">", font, 22f, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        SetRect(arrow.rectTransform, new Vector2(-14f, 0f), new Vector2(30f, 58f), new Vector2(1f, 0.5f));
    }

    private static Toggle CreateToggle(Transform parent, TMP_FontAsset font, string name, string title, string description, Vector2 position, Vector2 size)
    {
        GameObject toggleObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Toggle));
        toggleObject.layer = LayerMask.NameToLayer("UI");
        toggleObject.transform.SetParent(parent, false);
        SetRect(toggleObject.GetComponent<RectTransform>(), position, size, new Vector2(0f, 1f));

        Image row = toggleObject.GetComponent<Image>();
        row.color = InkSoft;
        Outline outline = toggleObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.55f, 0.64f, 0.65f, 0.24f);
        outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;

        Image box = CreateImage("选择框", toggleObject.transform, new Color(0.02f, 0.04f, 0.05f, 1f));
        SetRect(box.rectTransform, new Vector2(16f, -20f), new Vector2(30f, 30f), new Vector2(0f, 1f));
        Outline boxOutline = box.gameObject.AddComponent<Outline>();
        boxOutline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.55f);
        boxOutline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;

        Image mark = CreateImage("勾选标记", box.transform, Amber);
        Stretch(mark.rectTransform, 6f, 6f, 6f, 6f);

        TMP_Text titleText = CreateText(name + "_标题", toggleObject.transform, title, font, 20f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(titleText.rectTransform, new Vector2(62f, -10f), new Vector2(Mathf.Max(120f, size.x - 78f), 30f), new Vector2(0f, 1f));
        TMP_Text descriptionText = CreateText(name + "_说明", toggleObject.transform, description, font, 16f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(descriptionText.rectTransform, new Vector2(62f, -42f), new Vector2(Mathf.Max(120f, size.x - 78f), Mathf.Max(28f, size.y - 44f)), new Vector2(0f, 1f));
        descriptionText.enableWordWrapping = true;
        descriptionText.overflowMode = TextOverflowModes.Ellipsis;

        Toggle toggle = toggleObject.GetComponent<Toggle>();
        toggle.targetGraphic = box;
        toggle.graphic = mark;
        toggle.isOn = false;
        toggle.navigation = new Navigation { mode = Navigation.Mode.None };
        return toggle;
    }

    private static void CreateLabel(Transform parent, TMP_FontAsset font, string name, string title, string subtitle, Vector2 position, float width)
    {
        TMP_Text titleText = CreateText(name, parent, title, font, 24f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(titleText.rectTransform, position, new Vector2(width, 30f), new Vector2(0f, 1f));
        TMP_Text subtitleText = CreateText(name + "_说明", parent, subtitle, font, 17f, Muted, FontStyles.Normal, TextAlignmentOptions.Right);
        SetRect(subtitleText.rectTransform, position, new Vector2(width, 30f), new Vector2(0f, 1f));
    }

    private static void CreateCompactLabel(Transform parent, TMP_FontAsset font, string name, string title, string subtitle, Vector2 position, float width)
    {
        TMP_Text titleText = CreateText(name, parent, title, font, 20f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(titleText.rectTransform, position, new Vector2(width, 28f), new Vector2(0f, 1f));
        TMP_Text subtitleText = CreateText(name + "_说明", parent, subtitle, font, 16f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(subtitleText.rectTransform, position + new Vector2(0f, -28f), new Vector2(width, 26f), new Vector2(0f, 1f));
    }

    private static TMP_InputField CreateInput(Transform parent, TMP_FontAsset font, string name, string placeholderValue, string initialValue, Vector2 position, Vector2 size, TMP_InputField.ContentType contentType)
    {
        return CreateInput(parent, font, name, placeholderValue, initialValue, position, size, new Vector2(0f, 1f), contentType);
    }

    private static TMP_InputField CreateInput(
        Transform parent,
        TMP_FontAsset font,
        string name,
        string placeholderValue,
        string initialValue,
        Vector2 position,
        Vector2 size,
        Vector2 pivot,
        TMP_InputField.ContentType contentType)
    {
        GameObject inputObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        inputObject.layer = LayerMask.NameToLayer("UI");
        inputObject.transform.SetParent(parent, false);
        SetRect(inputObject.GetComponent<RectTransform>(), position, size, pivot);

        Image background = inputObject.GetComponent<Image>();
        background.color = InkSoft;
        Outline outline = inputObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.55f, 0.64f, 0.65f, 0.24f);
        outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;

        GameObject areaObject = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
        areaObject.layer = LayerMask.NameToLayer("UI");
        areaObject.transform.SetParent(inputObject.transform, false);
        RectTransform area = areaObject.GetComponent<RectTransform>();
        Stretch(area, 16f, 16f, 8f, 8f);

        TextMeshProUGUI placeholder = (TextMeshProUGUI)CreateText("Placeholder", area, placeholderValue, font, 22f, new Color(0.53f, 0.59f, 0.60f, 0.82f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        Stretch(placeholder.rectTransform);
        TextMeshProUGUI valueText = (TextMeshProUGUI)CreateText("Text", area, initialValue, font, 23f, Cream, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        Stretch(valueText.rectTransform);

        TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
        input.targetGraphic = background;
        input.textViewport = area;
        input.placeholder = placeholder;
        input.textComponent = valueText;
        input.contentType = contentType;
        input.text = initialValue;
        input.caretColor = Cream;
        input.customCaretColor = true;
        input.selectionColor = new Color(0.83f, 0.49f, 0.23f, 0.42f);
        return input;
    }

    private static Image CreatePanelCard(string name, Transform parent)
    {
        Image panel = CreateImage(name, parent, Surface);
        Outline outline = panel.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.55f, 0.64f, 0.65f, 0.18f);
        outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;
        return panel;
    }

    private static Button CreateButton(Transform parent, TMP_FontAsset font, string name, string caption, Vector2 position, Vector2 size, Color color, float fontSize, Vector2 pivot)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonObject.layer = LayerMask.NameToLayer("UI");
        buttonObject.transform.SetParent(parent, false);
        SetRect(buttonObject.GetComponent<RectTransform>(), position, size, pivot);

        Image image = buttonObject.GetComponent<Image>();
        image.color = color;
        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.28f);
        outline.effectDistance = FlatWorldUITheme.BorderOutlineDistance;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.16f, 1.11f, 1.02f, 1f);
        colors.pressedColor = new Color(0.72f, 0.76f, 0.78f, 1f);
        colors.selectedColor = FlatWorldUITheme.Selection;
        colors.disabledColor = new Color(0.42f, 0.43f, 0.44f, 0.56f);
        colors.fadeDuration = 0.12f;
        button.colors = colors;

        TMP_Text label = CreateText(name + "_文字", buttonObject.transform, caption, font, fontSize, Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(label.rectTransform);
        return button;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static TMP_Text CreateText(string name, Transform parent, string value, TMP_FontAsset font, float size, Color color, FontStyles style, TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float bottom = 0f, float top = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void SetRect(RectTransform rect, Vector2 position, Vector2 size, Vector2 pivot)
    {
        rect.anchorMin = pivot;
        rect.anchorMax = pivot;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void ClearChildren(Transform root)
    {
        for (int i = root.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.GetChild(i).gameObject, true);
    }
}
