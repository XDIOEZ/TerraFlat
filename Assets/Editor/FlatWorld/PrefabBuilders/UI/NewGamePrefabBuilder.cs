// AI-Context: 编辑器新游戏 Prefab 重建器；根节点直接组合 BasePanel，不得改名 GameManager 依赖的控件节点。

using System.Globalization;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class NewGamePrefabBuilder
{
    private const string PrefabPath = "Assets/2_Prefabs/2-1_UI/MainMenu/WorldSetup/UI_NewGame.prefab";
    private const string FontPath = "Assets/Plugins/TextMesh Pro/Fonts/fusion-pixel-12px-monospaced-zh_hans.asset";

    private static readonly Color Ink = new Color(0.025f, 0.043f, 0.058f, 0.985f);
    private static readonly Color InkSoft = new Color(0.045f, 0.075f, 0.095f, 0.98f);
    private static readonly Color Surface = new Color(0.06f, 0.095f, 0.115f, 0.98f);
    private static readonly Color Cream = new Color(0.95f, 0.91f, 0.81f, 1f);
    private static readonly Color Muted = new Color(0.64f, 0.70f, 0.71f, 1f);
    private static readonly Color Amber = new Color(0.83f, 0.49f, 0.23f, 1f);
    private static readonly Color Teal = new Color(0.26f, 0.61f, 0.57f, 1f);

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
            BuildDifficultyPanel(root.transform, font);

            EditorUtility.SetDirty(root);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

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
    }

    private static void BuildScrim(Transform root)
    {
        Image scrim = CreateImage("新世界界面遮罩", root, new Color(0.006f, 0.016f, 0.024f, 0.76f));
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
        outline.effectDistance = new Vector2(1f, -1f);

        Image accent = CreateImage("新世界主卡强调线", card.transform, Amber);
        accent.rectTransform.anchorMin = new Vector2(0f, 0f);
        accent.rectTransform.anchorMax = new Vector2(0f, 1f);
        accent.rectTransform.pivot = new Vector2(0f, 0.5f);
        accent.rectTransform.anchoredPosition = Vector2.zero;
        accent.rectTransform.sizeDelta = new Vector2(6f, 0f);
        accent.raycastTarget = false;
        return card;
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

    private static void BuildIdentity(Transform card, TMP_FontAsset font)
    {
        Image panel = CreatePanelCard("身份与存档区", card);
        SetRect(panel.rectTransform, new Vector2(42f, -150f), new Vector2(620f, 520f), new Vector2(0f, 1f));

        CreateStepBadge(panel.transform, font, "STEP 01", new Vector2(24f, -22f));
        TMP_Text heading = CreateText("身份与存档标题", panel.transform, "身份与存档", font, 28f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(heading.rectTransform, new Vector2(24f, -62f), new Vector2(320f, 40f), new Vector2(0f, 1f));

        CreateLabel(panel.transform, font, "玩家名称标签", "玩家名称", "可留空，自动生成带前缀的名称", new Vector2(24f, -112f), 572f);
        CreateInput(panel.transform, font, GameManager.NewGamePlayerInputKey, "可选，例如：旅人", string.Empty, new Vector2(24f, -150f), new Vector2(572f, 84f), TMP_InputField.ContentType.Standard);

        CreateLabel(panel.transform, font, "存档名称标签", "存档名称", "可留空，自动生成带前缀的名称", new Vector2(24f, -258f), 572f);
        CreateInput(panel.transform, font, GameManager.NewGameSaveInputKey, "可选，例如：篝火以北", string.Empty, new Vector2(24f, -296f), new Vector2(572f, 84f), TMP_InputField.ContentType.Standard);

        Image note = CreateImage("存档命名提示底板", panel.transform, new Color(0.035f, 0.06f, 0.075f, 0.98f));
        SetRect(note.rectTransform, new Vector2(24f, -406f), new Vector2(572f, 82f), new Vector2(0f, 1f));
        Image noteAccent = CreateImage("存档命名提示强调线", note.transform, Teal);
        noteAccent.rectTransform.anchorMin = new Vector2(0f, 0f);
        noteAccent.rectTransform.anchorMax = new Vector2(0f, 1f);
        noteAccent.rectTransform.pivot = new Vector2(0f, 0.5f);
        noteAccent.rectTransform.anchoredPosition = Vector2.zero;
        noteAccent.rectTransform.sizeDelta = new Vector2(4f, 0f);
        noteAccent.raycastTarget = false;

        TMP_Text noteText = CreateText("存档命名提示", note.transform, "两个名称都可留空；系统会自动填写 Player_ 和 World_ 前缀的随机名称。", font, 18f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        Stretch(noteText.rectTransform, 18f, 12f, 8f, 8f);
    }

    private static void BuildWorldSettings(Transform card, TMP_FontAsset font)
    {
        Image panel = CreatePanelCard("世界参数区", card);
        SetRect(panel.rectTransform, new Vector2(702f, -150f), new Vector2(656f, 520f), new Vector2(0f, 1f));

        CreateStepBadge(panel.transform, font, "STEP 02", new Vector2(24f, -22f));
        TMP_Text heading = CreateText("世界参数标题", panel.transform, "世界参数", font, 28f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(heading.rectTransform, new Vector2(24f, -62f), new Vector2(320f, 40f), new Vector2(0f, 1f));

        CreateCompactLabel(panel.transform, font, "星球半径标签", "星球半径", "越大，探索范围越广", new Vector2(24f, -108f), 286f);
        CreateCompactLabel(panel.transform, font, "噪声缩放标签", "世界坐标缩放", "越小舒展，越大密集", new Vector2(322f, -108f), 286f);

        string defaultRadius = PlanetData.DefaultRadius.ToString(CultureInfo.InvariantCulture);
        CreateInput(panel.transform, font, GameManager.NewGameRadiusInputKey, defaultRadius, defaultRadius, new Vector2(24f, -166f), new Vector2(286f, 84f), TMP_InputField.ContentType.IntegerNumber);
        string defaultNoiseScale = PlanetData.DefaultNoiseScale.ToString("0.########", CultureInfo.InvariantCulture);
        CreateInput(panel.transform, font, GameManager.NewGameNoiseInputKey, defaultNoiseScale, defaultNoiseScale, new Vector2(322f, -166f), new Vector2(286f, 84f), TMP_InputField.ContentType.DecimalNumber);

        Toggle topologyToggle = CreateToggle(
            panel.transform,
            font,
            GameManager.NewGameTopologyToggleKey,
            "有限循环世界",
            "越过上下左右边界后从对侧返回；关闭则使用原有无限世界。",
            new Vector2(24f, -264f),
            new Vector2(608f, 84f));
        topologyToggle.isOn = true;

        Image profile = CreateImage("世界生成概览", panel.transform, new Color(0.035f, 0.06f, 0.075f, 0.98f));
        SetRect(profile.rectTransform, new Vector2(24f, -362f), new Vector2(608f, 126f), new Vector2(0f, 1f));

        TMP_Text seedHint = CreateText("世界种子提示", profile.transform, "留空则随机 · 支持数字或文字", font, 16f, Muted, FontStyles.Normal, TextAlignmentOptions.Right);
        SetRect(seedHint.rectTransform, new Vector2(-18f, -12f), new Vector2(340f, 26f), new Vector2(1f, 1f));

        CreateInput(
            profile.transform,
            font,
            GameManager.NewGameSeedInputKey,
            "留空自动生成随机种子",
            string.Empty,
            new Vector2(18f, -46f),
            new Vector2(572f, 70f),
            TMP_InputField.ContentType.Standard);
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

        CreateButton(card, font, GameManager.NewGameDifficultyButtonKey, "难度设置  ·  简单", new Vector2(42f, 20f), new Vector2(300f, 80f), InkSoft, 22f, new Vector2(0f, 0f));

        CreateButton(card, font, GameManager.NewGameStartButtonKey, "生成新世界", new Vector2(-42f, 20f), new Vector2(280f, 80f), new Color(0.70f, 0.36f, 0.16f, 1f), 25f, new Vector2(1f, 0f));
    }

    private static void BuildDifficultyPanel(Transform root, TMP_FontAsset font)
    {
        Image overlay = CreateImage(GameManager.NewGameDifficultyPanelKey, root, new Color(0.004f, 0.012f, 0.018f, 0.92f));
        Stretch(overlay.rectTransform);
        overlay.gameObject.AddComponent<FullScreenRectController>();

        Image dialog = CreatePanelCard("难度设置窗口", overlay.transform);
        SetRect(dialog.rectTransform, Vector2.zero, new Vector2(1320f, 720f), new Vector2(0.5f, 0.5f));

        TMP_Text eyebrow = CreateText("难度设置眉题", dialog.transform, "DIFFICULTY  /  沙盒规则", font, 17f, Amber, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(eyebrow.rectTransform, new Vector2(40f, -22f), new Vector2(540f, 28f), new Vector2(0f, 1f));
        eyebrow.characterSpacing = 2f;

        TMP_Text title = CreateText("难度设置标题", dialog.transform, "选择官方预设，或创建自己的规则", font, 36f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(40f, -52f), new Vector2(860f, 50f), new Vector2(0f, 1f));

        CreateButton(dialog.transform, font, GameManager.NewGameDifficultyCloseButtonKey, "关闭", new Vector2(-40f, -24f), new Vector2(140f, 64f), InkSoft, 21f, new Vector2(1f, 1f));

        Image divider = CreateImage("难度标题分隔线", dialog.transform, new Color(0.55f, 0.64f, 0.65f, 0.18f));
        divider.rectTransform.anchorMin = new Vector2(0f, 1f);
        divider.rectTransform.anchorMax = new Vector2(1f, 1f);
        divider.rectTransform.pivot = new Vector2(0.5f, 1f);
        divider.rectTransform.anchoredPosition = new Vector2(0f, -120f);
        divider.rectTransform.sizeDelta = new Vector2(-60f, 1f);
        divider.raycastTarget = false;

        CreateButton(dialog.transform, font, GameManager.NewGameDifficultyOfficialTabKey, "官方预设", new Vector2(40f, -140f), new Vector2(220f, 60f), Amber, 20f, new Vector2(0f, 1f));
        CreateButton(dialog.transform, font, GameManager.NewGameDifficultyCustomTabKey, "自定义", new Vector2(276f, -140f), new Vector2(220f, 60f), InkSoft, 20f, new Vector2(0f, 1f));

        BuildOfficialDifficultyPage(dialog.transform, font);
        BuildCustomDifficultyPage(dialog.transform, font);
        BuildDifficultyDetails(dialog.transform, font);

        TMP_Text note = CreateText("难度存档提示", dialog.transform, "难度属于当前世界存档；进入游戏后可在设置面板中切换官方预设。", font, 17f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(note.rectTransform, new Vector2(40f, 28f), new Vector2(840f, 34f), new Vector2(0f, 0f));

        CreateButton(dialog.transform, font, GameManager.NewGameDifficultyConfirmButtonKey, "确认选择", new Vector2(-40f, 22f), new Vector2(230f, 72f), new Color(0.70f, 0.36f, 0.16f, 1f), 23f, new Vector2(1f, 0f));
        overlay.gameObject.SetActive(false);
    }

    private static void BuildOfficialDifficultyPage(Transform dialog, TMP_FontAsset font)
    {
        Image page = CreateImage(GameManager.NewGameDifficultyOfficialPageKey, dialog, Surface);
        SetRect(page.rectTransform, new Vector2(40f, -216f), new Vector2(456f, 414f), new Vector2(0f, 1f));

        TMP_Text heading = CreateText("官方预设标题", page.transform, "官方预设", font, 24f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(heading.rectTransform, new Vector2(20f, -16f), new Vector2(400f, 34f), new Vector2(0f, 1f));

        TMP_Text caption = CreateText("官方预设说明", page.transform, "预设会持续扩充，并保持规则组合清晰。", font, 16f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(caption.rectTransform, new Vector2(20f, -54f), new Vector2(410f, 28f), new Vector2(0f, 1f));

        GameObject scrollObject = new GameObject("官方预设列表", typeof(RectTransform), typeof(ScrollRect));
        scrollObject.layer = LayerMask.NameToLayer("UI");
        scrollObject.transform.SetParent(page.transform, false);
        SetRect(scrollObject.GetComponent<RectTransform>(), new Vector2(20f, -92f), new Vector2(416f, 300f), new Vector2(0f, 1f));

        GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportObject.layer = LayerMask.NameToLayer("UI");
        viewportObject.transform.SetParent(scrollObject.transform, false);
        RectTransform viewport = viewportObject.GetComponent<RectTransform>();
        Stretch(viewport);

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
        layout.spacing = 12f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 24f;

        for (int i = 0; i < GameDifficultyCatalog.All.Count; i++)
        {
            GameDifficultyDefinition definition = GameDifficultyCatalog.All[i];
            string rule = definition.PlayerDeath.DropAllCarriedItems
                ? "死亡掉落全部物品"
                : "死亡保留全部物品";

            CreateDifficultyPresetButton(
                content,
                font,
                GameManager.GetNewGameDifficultyPresetButtonKey(definition.Id),
                definition.DisplayName,
                $"{rule} · {definition.Description}");
        }
    }

    private static void BuildCustomDifficultyPage(Transform dialog, TMP_FontAsset font)
    {
        Image page = CreateImage(GameManager.NewGameDifficultyCustomPageKey, dialog, Surface);
        SetRect(page.rectTransform, new Vector2(40f, -216f), new Vector2(456f, 414f), new Vector2(0f, 1f));

        TMP_Text heading = CreateText("自定义规则标题", page.transform, "自定义规则", font, 24f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(heading.rectTransform, new Vector2(20f, -16f), new Vector2(400f, 34f), new Vector2(0f, 1f));

        TMP_Text caption = CreateText("自定义规则说明", page.transform, "17 项规则均已接入实际游戏系统。", font, 16f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(caption.rectTransform, new Vector2(20f, -54f), new Vector2(410f, 28f), new Vector2(0f, 1f));
        caption.enableWordWrapping = true;
        caption.overflowMode = TextOverflowModes.Ellipsis;

        CreateButton(page.transform, font, GameManager.NewGameDifficultyCombatCategoryKey, "战斗", new Vector2(20f, -88f), new Vector2(98f, 46f), Amber, 16f, new Vector2(0f, 1f));
        CreateButton(page.transform, font, GameManager.NewGameDifficultySurvivalCategoryKey, "生存", new Vector2(126f, -88f), new Vector2(98f, 46f), InkSoft, 16f, new Vector2(0f, 1f));
        CreateButton(page.transform, font, GameManager.NewGameDifficultyWorldCategoryKey, "世界", new Vector2(232f, -88f), new Vector2(98f, 46f), InkSoft, 16f, new Vector2(0f, 1f));
        CreateButton(page.transform, font, GameManager.NewGameDifficultyProductionCategoryKey, "生产", new Vector2(338f, -88f), new Vector2(98f, 46f), InkSoft, 16f, new Vector2(0f, 1f));

        BuildCustomCombatPage(page.transform, font);
        BuildCustomSurvivalPage(page.transform, font);
        BuildCustomWorldPage(page.transform, font);
        BuildCustomProductionPage(page.transform, font);
    }

    private static void BuildCustomCombatPage(Transform parent, TMP_FontAsset font)
    {
        Image page = CreateCustomCategoryPage(GameManager.NewGameDifficultyCombatPageKey, parent);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyPlayerAttackSliderKey, "玩家伤害", "玩家及手持武器造成的伤害", 0f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyCreatureAttackSliderKey, "生物伤害", "非玩家攻击者造成的伤害", 54f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyCreatureHealthSliderKey, "生物生命", "生物与可破坏实体的等效耐久", 108f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyEnvironmentalDamageSliderKey, "环境伤害", "饥饿、温度、流血与真实伤害", 162f);
    }

    private static void BuildCustomSurvivalPage(Transform parent, TMP_FontAsset font)
    {
        Image page = CreateCustomCategoryPage(GameManager.NewGameDifficultySurvivalPageKey, parent);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyHungerDrainSliderKey, "饥饿消耗", "营养与水分自然消耗速度", 0f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyStaminaConsumptionSliderKey, "耐力消耗", "移动、奔跑与攻击耐力消耗", 42f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyStaminaRecoverySliderKey, "耐力恢复", "营养充足时的耐力恢复", 84f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyHealingSliderKey, "治疗效果", "食物、睡眠和其他治疗效果", 126f);
        CreateCompactDifficultyToggle(page.transform, font, GameManager.NewGameDifficultyDropToggleKey, "死亡掉落全部随身物品", 174f);
    }

    private static void BuildCustomWorldPage(Transform parent, TMP_FontAsset font)
    {
        Image page = CreateCustomCategoryPage(GameManager.NewGameDifficultyWorldPageKey, parent);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyTimeSpeedSliderKey, "时间流逝", "昼夜与游戏日推进速度", 0f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultySpawnFrequencySliderKey, "生成频率", "每日生成窗口与每次生成数量", 54f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultySpawnPopulationSliderKey, "种群上限", "生态预算与生物存活上限", 108f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyLootAmountSliderKey, "战利品", "生物、资源与植物产出数量", 162f);
    }

    private static void BuildCustomProductionPage(Transform parent, TMP_FontAsset font)
    {
        Image page = CreateCustomCategoryPage(GameManager.NewGameDifficultyProductionPageKey, parent);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyCropGrowthSliderKey, "作物生长", "种子、作物和浆果成熟速度", 0f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultySmeltingSpeedSliderKey, "熔炼速度", "熔炉生产进度速度", 54f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyFuelConsumptionSliderKey, "燃料消耗", "所有燃料模块的消耗速度", 108f);
        CreateDifficultySlider(page.transform, font, GameManager.NewGameDifficultyCraftingOutputSliderKey, "制作产量", "手工、工作台与熔炉产量", 162f);
    }

    private static Image CreateCustomCategoryPage(string name, Transform parent)
    {
        Image page = CreateImage(name, parent, new Color(0.035f, 0.06f, 0.075f, 0.98f));
        SetRect(page.rectTransform, new Vector2(20f, -146f), new Vector2(416f, 248f), new Vector2(0f, 1f));
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
        SetRect(row.GetComponent<RectTransform>(), new Vector2(12f, -top), new Vector2(392f, 50f), new Vector2(0f, 1f));

        TMP_Text titleText = CreateText(name + "_标题", row.transform, title, font, 16f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(titleText.rectTransform, new Vector2(0f, 0f), new Vector2(150f, 22f), new Vector2(0f, 1f));
        TMP_Text descriptionText = CreateText(name + "_说明", row.transform, description, font, 13f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(descriptionText.rectTransform, new Vector2(0f, -22f), new Vector2(205f, 22f), new Vector2(0f, 1f));

        TMP_Text valueText = CreateText(name + "_数值", row.transform, "100%", font, 16f, Amber, FontStyles.Bold, TextAlignmentOptions.Right);
        SetRect(valueText.rectTransform, new Vector2(-2f, 0f), new Vector2(62f, 20f), new Vector2(1f, 1f));

        GameObject sliderObject = new GameObject(name, typeof(RectTransform), typeof(Slider));
        sliderObject.layer = LayerMask.NameToLayer("UI");
        sliderObject.transform.SetParent(row.transform, false);
        SetRect(sliderObject.GetComponent<RectTransform>(), new Vector2(-2f, -27f), new Vector2(180f, 22f), new Vector2(1f, 1f));

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
        SetRect(handle.rectTransform, Vector2.zero, new Vector2(8f, 18f), new Vector2(0.5f, 0.5f));

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
        SetRect(toggleObject.GetComponent<RectTransform>(), new Vector2(12f, -top), new Vector2(392f, 48f), new Vector2(0f, 1f));

        Image background = toggleObject.GetComponent<Image>();
        background.color = InkSoft;
        Image box = CreateImage("选择框", toggleObject.transform, new Color(0.02f, 0.04f, 0.05f, 1f));
        SetRect(box.rectTransform, new Vector2(10f, -9f), new Vector2(24f, 24f), new Vector2(0f, 1f));
        Image mark = CreateImage("勾选标记", box.transform, Amber);
        Stretch(mark.rectTransform, 5f, 5f, 5f, 5f);

        TMP_Text label = CreateText(name + "_标题", toggleObject.transform, title, font, 16f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(label.rectTransform, new Vector2(48f, -8f), new Vector2(330f, 30f), new Vector2(0f, 1f));

        Toggle toggle = toggleObject.GetComponent<Toggle>();
        toggle.targetGraphic = box;
        toggle.graphic = mark;
        toggle.isOn = false;
        toggle.navigation = new Navigation { mode = Navigation.Mode.None };
    }

    private static void BuildDifficultyDetails(Transform dialog, TMP_FontAsset font)
    {
        Image details = CreateImage("难度详情区", dialog, new Color(0.035f, 0.06f, 0.075f, 0.98f));
        SetRect(details.rectTransform, new Vector2(516f, -140f), new Vector2(764f, 490f), new Vector2(0f, 1f));

        TMP_Text eyebrow = CreateText("难度详情眉题", details.transform, "SELECTED PROFILE", font, 16f, Teal, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(eyebrow.rectTransform, new Vector2(24f, -22f), new Vector2(420f, 22f), new Vector2(0f, 1f));
        eyebrow.characterSpacing = 2f;

        TMP_Text title = CreateText(GameManager.NewGameDifficultyTitleTextKey, details.transform, "简单", font, 40f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(24f, -58f), new Vector2(700f, 56f), new Vector2(0f, 1f));

        TMP_Text description = CreateText(GameManager.NewGameDifficultyDescriptionTextKey, details.transform, "保持当前游戏配置。玩家死亡后不会掉落随身物品。", font, 20f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(description.rectTransform, new Vector2(24f, -126f), new Vector2(716f, 82f), new Vector2(0f, 1f));
        description.enableWordWrapping = true;
        description.overflowMode = TextOverflowModes.Ellipsis;

        Image ruleCard = CreateImage("难度规则卡片", details.transform, Surface);
        SetRect(ruleCard.rectTransform, new Vector2(24f, -220f), new Vector2(716f, 178f), new Vector2(0f, 1f));
        TMP_Text ruleLabel = CreateText("难度规则标签", ruleCard.transform, "当前已接入规则", font, 17f, Teal, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(ruleLabel.rectTransform, new Vector2(16f, -14f), new Vector2(360f, 24f), new Vector2(0f, 1f));
        TMP_Text rule = CreateText(GameManager.NewGameDifficultyRuleTextKey, ruleCard.transform, "战斗：玩家 100% / 生物伤害 100% / 生物生命 100%", font, 17f, Cream, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        SetRect(rule.rectTransform, new Vector2(16f, -50f), new Vector2(684f, 114f), new Vector2(0f, 1f));
        rule.enableWordWrapping = true;
        rule.overflowMode = TextOverflowModes.Ellipsis;

        TMP_Text future = CreateText("难度未来规则提示", details.transform, "官方预设与自定义面板共享同一套存档规则，后续扩充时不需要玩家重新创建世界。", font, 17f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(future.rectTransform, new Vector2(24f, -418f), new Vector2(716f, 48f), new Vector2(0f, 1f));
        future.enableWordWrapping = true;
        future.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static void CreateDifficultyPresetButton(Transform parent, TMP_FontAsset font, string name, string title, string description)
    {
        Button button = CreateButton(parent, font, name, string.Empty, Vector2.zero, new Vector2(416f, 116f), InkSoft, 19f, new Vector2(0f, 1f));
        button.gameObject.AddComponent<LayoutElement>().preferredHeight = 116f;
        TMP_Text generatedLabel = button.GetComponentInChildren<TMP_Text>();
        if (generatedLabel != null)
            generatedLabel.gameObject.SetActive(false);

        TMP_Text titleText = CreateText(name + "_标题", button.transform, title, font, 24f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(titleText.rectTransform, new Vector2(18f, -14f), new Vector2(380f, 34f), new Vector2(0f, 1f));
        TMP_Text descriptionText = CreateText(name + "_说明", button.transform, description, font, 16f, Muted, FontStyles.Normal, TextAlignmentOptions.Left);
        SetRect(descriptionText.rectTransform, new Vector2(18f, -54f), new Vector2(380f, 50f), new Vector2(0f, 1f));
        descriptionText.enableWordWrapping = true;
        descriptionText.overflowMode = TextOverflowModes.Ellipsis;
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
        outline.effectDistance = new Vector2(1f, -1f);

        Image box = CreateImage("选择框", toggleObject.transform, new Color(0.02f, 0.04f, 0.05f, 1f));
        SetRect(box.rectTransform, new Vector2(16f, -20f), new Vector2(30f, 30f), new Vector2(0f, 1f));
        Outline boxOutline = box.gameObject.AddComponent<Outline>();
        boxOutline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.55f);
        boxOutline.effectDistance = new Vector2(1f, -1f);

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
        TMP_Text titleText = CreateText(name, parent, title, font, 21f, Cream, FontStyles.Bold, TextAlignmentOptions.Left);
        SetRect(titleText.rectTransform, position, new Vector2(width, 30f), new Vector2(0f, 1f));
        TMP_Text subtitleText = CreateText(name + "_说明", parent, subtitle, font, 16f, Muted, FontStyles.Normal, TextAlignmentOptions.Right);
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
        GameObject inputObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(TMP_InputField));
        inputObject.layer = LayerMask.NameToLayer("UI");
        inputObject.transform.SetParent(parent, false);
        SetRect(inputObject.GetComponent<RectTransform>(), position, size, new Vector2(0f, 1f));

        Image background = inputObject.GetComponent<Image>();
        background.color = InkSoft;
        Outline outline = inputObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.55f, 0.64f, 0.65f, 0.24f);
        outline.effectDistance = new Vector2(1f, -1f);

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
        outline.effectDistance = new Vector2(1f, -1f);
        return panel;
    }

    private static void CreateStepBadge(Transform parent, TMP_FontAsset font, string value, Vector2 position)
    {
        Image badge = CreateImage(value + "_底板", parent, new Color(0.12f, 0.23f, 0.22f, 1f));
        SetRect(badge.rectTransform, position, new Vector2(104f, 32f), new Vector2(0f, 1f));
        TMP_Text text = CreateText(value + "_文字", badge.transform, value, font, 15f, Teal, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
        text.characterSpacing = 1f;
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
        outline.effectDistance = new Vector2(1f, -1f);

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
