using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Gameplay.Events;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    private const int MaxVisibleSearchResults = 12;

    private enum GmPageId
    {
        Player,
        Buff,
        Spawn,
        World,
        Structures,
        GameEvents,
        Commands,
        Quests
    }

    private sealed class GmPageView
    {
        public GameObject Root;
        public Button TabButton;
        public ScrollRect Scroll;
        public RectTransform Content;
    }

    private sealed class GmSearchEntry
    {
        public GmPageId PageId;
        public string Label;
        public string SearchText;
        public RectTransform Target;
    }

    /// <summary>记录运行时操作网格的最大列数，用于按实际视口宽度重新布局。</summary>
    private sealed class GmResponsiveGrid
    {
        public GridLayoutGroup Grid;
        public int MaxColumns;
        public float CellHeight;
    }

    private readonly Dictionary<GmPageId, GmPageView> gmPages = new();
    private readonly List<GmSearchEntry> gmSearchEntries = new();
    private readonly List<GmResponsiveGrid> gmResponsiveGrids = new();

    private RectTransform gmCanvasRect;
    private RectTransform gmWindowRect;
    private Vector2 lastGmCanvasSize;
    private Transform gmPageHost;
    private TMP_InputField gmSearchInput;
    private TextMeshProUGUI gmSearchSummaryText;
    private GameObject gmSearchResultsRoot;
    private Transform gmSearchResultsContent;
    private RectTransform gmSearchResultsRect;
    private RectTransform gameEventPageContent;
    private RectTransform commandPageContent;
    private GameEventManager boundGameEventManager;
    private Coroutine searchNavigationCoroutine;
    private Coroutine gameEventRefreshCoroutine;

    private void BuildTabbedWindow()
    {
        gmPages.Clear();
        gmSearchEntries.Clear();
        gmResponsiveGrids.Clear();

        GameObject canvasObject = new(
            "GM Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        gmCanvasRect = canvasObject.GetComponent<RectTransform>();

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        windowRoot = CreateUiObject("GM Tabbed Window", canvasObject.transform);
        gmWindowRect = windowRoot.GetComponent<RectTransform>();
        gmWindowRect.anchorMin = gmWindowRect.anchorMax = new Vector2(0.5f, 0.5f);
        gmWindowRect.pivot = new Vector2(0.5f, 0.5f);
        gmWindowRect.sizeDelta = new Vector2(1160f, 780f);

        Image panelImage = windowRoot.AddComponent<Image>();
        panelImage.color = new Color(0.031f, 0.082f, 0.114f, 0.99f);
        Outline panelOutline = windowRoot.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.55f);
        panelOutline.effectDistance = new Vector2(1f, -1f);

        VerticalLayoutGroup panelLayout = windowRoot.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(20, 20, 18, 18);
        panelLayout.spacing = 8f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        BuildTabbedHeader();
        BuildGlobalSearchBar();
        BuildSearchResultsPanel();
        BuildTabBar();

        GameObject pageHostObject = CreateUiObject("Page Host", windowRoot.transform);
        LayoutElement hostLayout = pageHostObject.AddComponent<LayoutElement>();
        hostLayout.minHeight = 300f;
        hostLayout.flexibleHeight = 1f;
        gmPageHost = pageHostObject.transform;

        BuildPlayerPage();
        BuildBuffPage();
        BuildQuestPage();
        BuildSpawnPage();
        BuildWorldPage();
        BuildStructurePage();
        gameEventPageContent = CreatePage(GmPageId.GameEvents).Content;
        commandPageContent = CreatePage(GmPageId.Commands).Content;

        statusText = CreateText(
            windowRoot.transform,
            "按 F4 打开或关闭此窗口。",
            12f,
            new Color(0.66f, 0.71f, 0.71f));
        statusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        BindGameEventManager();
        RebuildGameEventPage();
        RebuildCommandPage();
        BuildAirdropBrowser(canvasObject.transform);
        BuildAiCreatureBrowser(canvasObject.transform);
        int savedPageIndex = GMConsolePreferences.ActivePageIndex;
        GmPageId savedPage = Enum.IsDefined(typeof(GmPageId), savedPageIndex)
            ? (GmPageId)savedPageIndex
            : GmPageId.Player;
        SetActivePage(savedPage);

        Canvas.ForceUpdateCanvases();
        ClampTabbedWindowToCanvas();
        LayoutRebuilder.ForceRebuildLayoutImmediate(gmWindowRect);
        windowRoot.SetActive(false);
    }

    private void BuildTabbedHeader()
    {
        GameObject header = CreateUiObject("Header", windowRoot.transform);
        header.AddComponent<LayoutElement>().preferredHeight = 54f;
        header.AddComponent<Image>().color = new Color(0.063f, 0.153f, 0.188f, 1f);

        HorizontalLayoutGroup layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(16, 12, 7, 7);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        TextMeshProUGUI title = CreateText(
            header.transform,
            "FlatWorld GM 管理工具",
            20f,
            new Color(0.95f, 0.91f, 0.84f));
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        TextMeshProUGUI shortcut = CreateText(
            header.transform,
            "F4  开启 / 关闭",
            12f,
            new Color(0.66f, 0.71f, 0.71f));
        shortcut.alignment = TextAlignmentOptions.Right;
        shortcut.gameObject.AddComponent<LayoutElement>().preferredWidth = 130f;

        Button closeButton = CreateButton(header.transform, "关闭", () => SetWindowVisible(false), 68f, 34f);
        closeButton.GetComponent<Image>().color = new Color(0.09f, 0.17f, 0.20f, 1f);
    }

    private void BuildGlobalSearchBar()
    {
        GameObject toolbar = CreateUiObject("Global Search", windowRoot.transform);
        toolbar.AddComponent<LayoutElement>().preferredHeight = 42f;
        toolbar.AddComponent<Image>().color = new Color(0.043f, 0.112f, 0.139f, 1f);

        HorizontalLayoutGroup layout = toolbar.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 3, 3);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        gmSearchInput = CreateInputField(toolbar.transform, "搜索功能、事件名称或命令", 680f, false);
        LayoutElement searchLayout = gmSearchInput.GetComponent<LayoutElement>();
        searchLayout.flexibleWidth = 1f;
        gmSearchInput.onValueChanged.AddListener(_ => RebuildGlobalSearchResults());

        gmSearchSummaryText = CreateText(
            toolbar.transform,
            "输入名称后跳转",
            12f,
            new Color(0.66f, 0.71f, 0.71f));
        gmSearchSummaryText.alignment = TextAlignmentOptions.Right;
        gmSearchSummaryText.enableWordWrapping = false;
        gmSearchSummaryText.overflowMode = TextOverflowModes.Ellipsis;
        gmSearchSummaryText.gameObject.AddComponent<LayoutElement>().preferredWidth = 190f;

        CreateButton(toolbar.transform, "清空", ClearGlobalSearch, 72f, 34f);
    }

    private void BuildSearchResultsPanel()
    {
        gmSearchResultsRoot = CreateUiObject("Search Results", windowRoot.transform);
        LayoutElement resultsLayout = gmSearchResultsRoot.AddComponent<LayoutElement>();
        resultsLayout.ignoreLayout = true;
        gmSearchResultsRect = gmSearchResultsRoot.GetComponent<RectTransform>();
        ConfigureSearchResultsOverlay(gmSearchResultsRect, 150f);
        gmSearchResultsRoot.AddComponent<Image>().color = new Color(0.025f, 0.065f, 0.086f, 1f);
        Outline outline = gmSearchResultsRoot.AddComponent<Outline>();
        outline.effectColor = new Color(0.83f, 0.49f, 0.23f, 0.35f);
        outline.effectDistance = new Vector2(1f, -1f);

        gmSearchResultsContent = ConfigureVerticalScroll(gmSearchResultsRoot, 7f, out _);
        gmSearchResultsRoot.SetActive(false);
    }

    /// <summary>把搜索结果定位在搜索框下方，不参与主窗口纵向布局。</summary>
    private static void ConfigureSearchResultsOverlay(RectTransform resultsRect, float height)
    {
        if (resultsRect == null)
            return;

        const float topOffset = 128f;
        resultsRect.anchorMin = new Vector2(0f, 1f);
        resultsRect.anchorMax = new Vector2(1f, 1f);
        resultsRect.pivot = new Vector2(0.5f, 1f);
        resultsRect.offsetMin = new Vector2(8f, -topOffset - height);
        resultsRect.offsetMax = new Vector2(-8f, -topOffset);
    }

    private void BuildTabBar()
    {
        GameObject tabBar = CreateUiObject("Top Tabs", windowRoot.transform);
        tabBar.AddComponent<LayoutElement>().preferredHeight = 42f;
        tabBar.AddComponent<Image>().color = new Color(0.043f, 0.112f, 0.139f, 1f);

        ScrollRect scroll = tabBar.AddComponent<ScrollRect>();
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 40f;

        GameObject viewport = CreateUiObject("Tab Viewport", tabBar.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(6f, 3f);
        viewportRect.offsetMax = new Vector2(-6f, -3f);
        viewport.AddComponent<RectMask2D>();
        scroll.viewport = viewportRect;

        GameObject content = CreateUiObject("Tab Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 0f);
        contentRect.anchorMax = new Vector2(0f, 1f);
        contentRect.pivot = new Vector2(0f, 0.5f);
        contentRect.sizeDelta = Vector2.zero;

        HorizontalLayoutGroup layout = content.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        scroll.content = contentRect;

        CreateTab(content.transform, GmPageId.Player, "玩家", 128f);
        CreateTab(content.transform, GmPageId.Buff, "Buff", 100f);
        CreateTab(content.transform, GmPageId.Quests, "任务", 100f);
        CreateTab(content.transform, GmPageId.Spawn, "生成", 128f);
        CreateTab(content.transform, GmPageId.World, "世界", 100f);
        CreateTab(content.transform, GmPageId.Structures, "遗迹", 100f);
        CreateTab(content.transform, GmPageId.GameEvents, "事件", 110f);
        CreateTab(content.transform, GmPageId.Commands, "命令", 110f);
        contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 911f);
    }

    private void CreateTab(Transform parent, GmPageId pageId, string label, float width)
    {
        Button button = CreateButton(parent, label, () => SetActivePage(pageId), width, 36f);
        LayoutElement layout = button.GetComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.flexibleWidth = 0f;
        gmPages[pageId] = new GmPageView { TabButton = button };
    }

    private GmPageView CreatePage(GmPageId pageId)
    {
        GameObject pageRoot = CreateUiObject(GetPageLabel(pageId) + " Page", gmPageHost);
        RectTransform rootRect = pageRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        pageRoot.AddComponent<Image>().color = new Color(0.026f, 0.069f, 0.091f, 1f);

        Transform content = ConfigureVerticalScroll(pageRoot, 10f, out ScrollRect scroll);
        GmPageView page = gmPages[pageId];
        page.Root = pageRoot;
        page.Scroll = scroll;
        page.Content = content.GetComponent<RectTransform>();
        pageRoot.SetActive(false);
        return page;
    }

    private static Transform ConfigureVerticalScroll(GameObject root, float inset, out ScrollRect scroll)
    {
        scroll = root.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        GameObject viewport = CreateUiObject("Viewport", root.transform);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(inset, inset);
        viewportRect.offsetMax = new Vector2(-inset, -inset);
        viewport.AddComponent<RectMask2D>();
        scroll.viewport = viewportRect;

        GameObject content = CreateUiObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.padding = new RectOffset(14, 14, 12, 12);
        contentLayout.spacing = 9f;
        contentLayout.childControlWidth = true;
        contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true;
        contentLayout.childForceExpandHeight = false;

        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = contentRect;
        return content.transform;
    }

    private void BuildPlayerPage()
    {
        GmPageView page = CreatePage(GmPageId.Player);
        AddPageIntro(page.Content, "玩家与管理", "玩家权限、传送、背包以及跑图速度设置。所有按钮只影响当前运行中的玩家。 ");

        Transform grid = CreateActionGrid(page.Content, 4, 256f, 36f, 8);
        CreateSearchableButton(grid, GmPageId.Player, "设为管理员", "管理员 admin 权限", SetAdministrator);
        adminInvincibilityButton = CreateSearchableButton(
            grid,
            GmPageId.Player,
            "管理员无敌：需权限",
            "管理员 无敌 开关 生命 死亡 invincibility god mode",
            ToggleAdminInvincibility);
        CreateSearchableButton(
            grid,
            GmPageId.Player,
            "传送至鼠标",
            "传送 鼠标 teleport",
            () => InvokeByTypeName("Mod_PlayerTraits", "TeleportToMousePosition"));
        teleportShortcutButton = CreateSearchableButton(
            grid,
            GmPageId.Player,
            "Ctrl+T 传送：开",
            "T键 Ctrl+T 传送开关 快捷键",
            ToggleTeleportShortcut);
        CreatePlayerMoveSpeedControl(grid);
        CreateSearchableButton(
            grid,
            GmPageId.Player,
            "创造背包",
            "创造模式 背包 inventory",
            () => InvokeByTypeName("Mod_PlayerTraits", "InitializeCreativeInventoryForAdmin"));
        CreateSearchableButton(
            grid,
            GmPageId.Player,
            "手持 +9999",
            "手持 物品 数量",
            () => InvokeByTypeName("PlayerAdminController", "AddAmountToCurrentHandItem", 9999f));
        CreateSearchableButton(
            grid,
            GmPageId.Player,
            "背包 +999",
            "背包 物品 数量",
            () => InvokeByTypeName("PlayerAdminController", "AddAmountToAllBagItems", 999f));

        RefreshTeleportShortcutButton();
        RefreshAdminInvincibilityButton();
        RefreshPlayerMoveSpeedButton();
    }

    private void BuildSpawnPage()
    {
        GmPageView page = CreatePage(GmPageId.Spawn);
        AddPageIntro(page.Content, "生成与召唤", "打开可搜索目录，选择物品空投或将 AI 生物生成到玩家附近。 ");

        Transform grid = CreateActionGrid(page.Content, 2, 516f, 52f, 2);
        Button itemButton = CreateSearchableButton(
            grid,
            GmPageId.Spawn,
            "打开物品空投目录",
            "物品 item 空投 生成 召唤",
            OpenAirdropBrowser,
            52f);
        itemButton.GetComponent<Image>().color = new Color(0.66f, 0.32f, 0.15f, 1f);

        Button creatureButton = CreateSearchableButton(
            grid,
            GmPageId.Spawn,
            "打开 AI 生物目录",
            "AI 生物 动物 怪物 creature spawn 召唤",
            OpenAiCreatureBrowser,
            52f);
        creatureButton.GetComponent<Image>().color = new Color(0.10f, 0.35f, 0.37f, 1f);

        itemHintText = AddPageHint(page.Content, "进入游戏世界后会自动刷新物品与生物目录。", 24f);
    }

    private void BuildWorldPage()
    {
        GmPageView page = CreatePage(GmPageId.World);
        AddPageIntro(page.Content, "世界与环境", "天气、时间、区块加载、视野和导航调试功能。 ");

        Transform grid = CreateActionGrid(page.Content, 4, 256f, 36f, 11);
        CreateSearchableButton(grid, GmPageId.World, "晴天", "天气 晴 clear weather", () => InvokeByTypeName("GameDebugManager", "SetClearWeather"));
        CreateSearchableButton(grid, GmPageId.World, "下雨", "天气 雨 rain weather", () => InvokeByTypeName("GameDebugManager", "SetRainWeather"));
        CreateSearchableButton(grid, GmPageId.World, "环境信息", "环境 温度 信息 debug", () => InvokeByTypeName("GameDebugManager", "ToggleEnvironmentInfo"));
        CreateSearchableButton(grid, GmPageId.World, "视野无限", "相机 视野 unlimited view", () => InvokeByTypeName("Mod_Cam", "EnableUnlimitedView"));
        CreateSearchableButton(grid, GmPageId.World, "时间 +0.5", "时间 加速 time scale", () => InvokeByTypeName("PlayerAdminController", "TryUpdateTimeScale", 0.5f));
        CreateSearchableButton(grid, GmPageId.World, "时间 -0.5", "时间 减速 time scale", () => InvokeByTypeName("PlayerAdminController", "TryUpdateTimeScale", -0.5f));
        CreateSearchableButton(grid, GmPageId.World, "时间重置", "时间 恢复 reset", () => InvokeByTypeName("PlayerAdminController", "ResetTimeScale"));
        CreateSearchableButton(grid, GmPageId.World, "刷新区块", "区块 chunk 刷新", () => InvokeByTypeName("Mod_ChunkLoader", "RefreshChunksAroundPlayer"));
        CreateSearchableButton(grid, GmPageId.World, "区块距离 +1", "区块 加载距离 chunk distance", () => InvokeByTypeName("PlayerAdminController", "IncreaseAdminChunkLoadDistance"));
        CreateChunkLoadSpeedControl(grid);
        navigationPathButton = CreateSearchableButton(
            grid,
            GmPageId.World,
            "AI 路线提示：关",
            "AI 导航 路线 path navmesh",
            ToggleNavigationPathHints);

        RefreshNavigationPathButton();
        RefreshChunkLoadSpeedControl();
    }

    private void BuildStructurePage()
    {
        GmPageView page = CreatePage(GmPageId.Structures);
        AddPageIntro(page.Content, "遗迹传送", "按当前世界种子推算未探索区域中的最近遗迹生成点。 ");

        GameObject row = CreateUiObject("Structure Teleport Row", page.Content);
        row.AddComponent<LayoutElement>().preferredHeight = 44f;
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;

        CreateButton(row.transform, "‹", () => CycleStructure(-1), 40f, 40f);
        structureSelectionText = CreateValueDisplay(row.transform, "正在读取遗迹目录…", 540f, 40f);
        LayoutElement selectionLayout = structureSelectionText.transform.parent.GetComponent<LayoutElement>();
        selectionLayout.minWidth = 180f;
        selectionLayout.flexibleWidth = 1f;
        CreateButton(row.transform, "›", () => CycleStructure(1), 40f, 40f);
        Button teleportButton = CreateButton(row.transform, "传送到最近遗迹", TeleportToSelectedStructure, 190f, 40f);
        teleportButton.GetComponent<Image>().color = new Color(0.66f, 0.32f, 0.15f, 1f);
        CreateButton(row.transform, "刷新", RefreshStructureOptions, 82f, 40f);

        structureHintText = AddPageHint(page.Content, "进入游戏世界后选择遗迹类型，再执行传送。", 26f);
        RegisterSearchEntry(
            GmPageId.Structures,
            "传送到最近遗迹",
            "遗迹 建筑 structure ruin 传送",
            teleportButton.transform as RectTransform);
    }

    private Transform CreateActionGrid(
        Transform parent,
        int columns,
        float cellWidth,
        float cellHeight,
        int itemCount)
    {
        GameObject gridObject = CreateUiObject("Action Grid", parent);
        GridLayoutGroup grid = gridObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.cellSize = new Vector2(cellWidth, cellHeight);
        grid.spacing = new Vector2(8f, 8f);
        grid.childAlignment = TextAnchor.UpperLeft;
        gmResponsiveGrids.Add(new GmResponsiveGrid
        {
            Grid = grid,
            MaxColumns = Mathf.Max(1, columns),
            CellHeight = cellHeight
        });
        SetGridHeight(gridObject.transform, itemCount, columns, cellHeight, 8f);
        return gridObject.transform;
    }

    private static void SetGridHeight(
        Transform grid,
        int itemCount,
        int columns,
        float cellHeight,
        float spacing)
    {
        int rows = itemCount > 0 ? Mathf.CeilToInt(itemCount / (float)columns) : 0;
        float height = rows > 0 ? rows * cellHeight + (rows - 1) * spacing : 0f;
        LayoutElement layout = grid.GetComponent<LayoutElement>() ?? grid.gameObject.AddComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
    }

    private static void AddPageIntro(Transform parent, string title, string description)
    {
        TextMeshProUGUI heading = CreateText(parent, title, 18f, new Color(0.95f, 0.91f, 0.84f));
        heading.fontStyle = FontStyles.Bold;
        heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        TextMeshProUGUI paragraph = CreateText(parent, description.Trim(), 12f, new Color(0.66f, 0.71f, 0.71f));
        paragraph.enableWordWrapping = true;
        paragraph.overflowMode = TextOverflowModes.Ellipsis;
        paragraph.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
    }

    private static TextMeshProUGUI AddPageHint(Transform parent, string value, float height)
    {
        TextMeshProUGUI hint = CreateText(parent, value, 12f, new Color(0.66f, 0.71f, 0.71f));
        hint.enableWordWrapping = true;
        hint.overflowMode = TextOverflowModes.Ellipsis;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return hint;
    }

    private Button CreateSearchableButton(
        Transform parent,
        GmPageId pageId,
        string label,
        string keywords,
        UnityAction action,
        float height = 36f)
    {
        Button button = CreateButton(parent, label, action, 0f, height);
        RegisterSearchEntry(pageId, label, keywords, button.transform as RectTransform);
        return button;
    }

    private void CreatePlayerMoveSpeedControl(Transform parent)
    {
        GameObject row = CreateUiObject("Player Move Speed Multiplier", parent);
        Image background = row.AddComponent<Image>();
        background.color = new Color(0.043f, 0.112f, 0.139f, 1f);
        Outline outline = row.AddComponent<Outline>();
        outline.effectColor = new Color(0.51f, 0.58f, 0.58f, 0.28f);
        outline.effectDistance = new Vector2(1f, -1f);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 3, 3);
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        TextMeshProUGUI label = CreateText(
            row.transform,
            "移速倍率",
            12f,
            new Color(0.95f, 0.91f, 0.84f));
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = 78f;

        playerMoveSpeedInput = CreateInputField(row.transform, "倍率", 84f, false);
        playerMoveSpeedInput.contentType = TMP_InputField.ContentType.DecimalNumber;
        playerMoveSpeedInput.characterLimit = 7;
        playerMoveSpeedInput.textComponent.alignment = TextAlignmentOptions.Center;
        playerMoveSpeedInput.onSubmit.AddListener(_ => ApplyPlayerMoveSpeedInput());

        playerMoveSpeedApplyButton = CreateButton(
            row.transform,
            "应用",
            ApplyPlayerMoveSpeedInput,
            60f,
            30f);

        RegisterSearchEntry(
            GmPageId.Player,
            "玩家移速倍率",
            "玩家 移动速度 跑图 speed multiplier 倍率",
            row.transform as RectTransform);
    }

    private void CreateChunkLoadSpeedControl(Transform parent)
    {
        GameObject row = CreateUiObject("Chunk Load Speed Multiplier", parent);
        Image background = row.AddComponent<Image>();
        background.color = new Color(0.043f, 0.112f, 0.139f, 1f);
        Outline outline = row.AddComponent<Outline>();
        outline.effectColor = new Color(0.51f, 0.58f, 0.58f, 0.28f);
        outline.effectDistance = new Vector2(1f, -1f);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 3, 3);
        layout.spacing = 5f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        TextMeshProUGUI label = CreateText(
            row.transform,
            "加载倍率",
            12f,
            new Color(0.95f, 0.91f, 0.84f));
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = 60f;

        chunkLoadSpeedInput = CreateInputField(row.transform, "倍率/无限", 58f, false);
        chunkLoadSpeedInput.contentType = TMP_InputField.ContentType.Standard;
        chunkLoadSpeedInput.characterLimit = 12;
        chunkLoadSpeedInput.textComponent.alignment = TextAlignmentOptions.Center;
        chunkLoadSpeedInput.onSubmit.AddListener(_ => ApplyChunkLoadSpeedInput());

        chunkLoadSpeedApplyButton = CreateButton(
            row.transform,
            "应用",
            ApplyChunkLoadSpeedInput,
            48f,
            30f);

        chunkLoadSpeedUnlimitedButton = CreateButton(
            row.transform,
            "无限",
            ToggleUnlimitedChunkLoadSpeed,
            54f,
            30f);

        RegisterSearchEntry(
            GmPageId.World,
            "区块加载倍率",
            "区块 加载 生成 speed multiplier 倍率 无限 无限制",
            row.transform as RectTransform);
    }

    private void SetActivePage(GmPageId pageId)
    {
        if (!gmPages.TryGetValue(pageId, out GmPageView selected) || selected.Root == null)
            return;

        GMConsolePreferences.SetActivePageIndex((int)pageId);

        foreach (KeyValuePair<GmPageId, GmPageView> pair in gmPages)
        {
            bool active = pair.Key == pageId;
            if (pair.Value.Root != null)
                pair.Value.Root.SetActive(active);

            Image tabImage = pair.Value.TabButton != null
                ? pair.Value.TabButton.GetComponent<Image>()
                : null;
            if (tabImage != null)
            {
                tabImage.color = active
                    ? new Color(0.66f, 0.32f, 0.15f, 1f)
                    : new Color(0.094f, 0.212f, 0.251f, 1f);
            }
        }

        Canvas.ForceUpdateCanvases();
        ResizeResponsiveGrids();
        if (selected.Content != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(selected.Content);
    }

    private void RegisterSearchEntry(
        GmPageId pageId,
        string label,
        string keywords,
        RectTransform target)
    {
        if (target == null || string.IsNullOrWhiteSpace(label))
            return;

        gmSearchEntries.Add(new GmSearchEntry
        {
            PageId = pageId,
            Label = label.Trim(),
            SearchText = $"{label} {keywords} {GetPageLabel(pageId)}".ToLowerInvariant(),
            Target = target
        });
    }

    private void RemoveSearchEntriesForPage(GmPageId pageId)
    {
        gmSearchEntries.RemoveAll(entry => entry.PageId == pageId);
    }

    private void RebuildGlobalSearchResults()
    {
        if (gmSearchInput == null || gmSearchResultsRoot == null || gmSearchResultsContent == null)
            return;

        ClearChildren(gmSearchResultsContent);
        string query = gmSearchInput.text?.Trim() ?? string.Empty;
        if (query.Length == 0)
        {
            gmSearchResultsRoot.SetActive(false);
            gmSearchSummaryText.text = "输入名称后跳转";
            return;
        }

        string[] tokens = query
            .ToLowerInvariant()
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        List<GmSearchEntry> matches = gmSearchEntries
            .Where(entry => entry.Target != null && tokens.All(token => entry.SearchText.Contains(token)))
            .OrderByDescending(entry => entry.Label.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(entry => GetPageLabel(entry.PageId), StringComparer.Ordinal)
            .ThenBy(entry => entry.Label, StringComparer.Ordinal)
            .Take(MaxVisibleSearchResults)
            .ToList();

        gmSearchResultsRoot.SetActive(true);
        gmSearchResultsRoot.transform.SetAsLastSibling();
        float resultsHeight = Mathf.Clamp(matches.Count * 36f + 18f, 56f, 158f);
        ConfigureSearchResultsOverlay(gmSearchResultsRect, resultsHeight);
        gmSearchSummaryText.text = matches.Count > 0 ? $"找到 {matches.Count} 项" : "没有匹配项";

        if (matches.Count == 0)
        {
            AddPageHint(gmSearchResultsContent, "没有找到对应功能，请尝试更短的关键词。", 32f);
            return;
        }

        for (int i = 0; i < matches.Count; i++)
        {
            GmSearchEntry entry = matches[i];
            CreateButton(
                gmSearchResultsContent,
                $"{GetPageLabel(entry.PageId)}  ›  {entry.Label}",
                () => NavigateToSearchEntry(entry),
                0f,
                32f);
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(gmSearchResultsContent as RectTransform);
    }

    private void NavigateToSearchEntry(GmSearchEntry entry)
    {
        if (entry == null || entry.Target == null)
        {
            SetStatus("搜索结果已失效，请重新搜索。", Color.yellow);
            return;
        }

        gmSearchInput.SetTextWithoutNotify(string.Empty);
        gmSearchResultsRoot.SetActive(false);
        gmSearchSummaryText.text = "输入名称后跳转";
        SetActivePage(entry.PageId);

        if (searchNavigationCoroutine != null)
            StopCoroutine(searchNavigationCoroutine);
        searchNavigationCoroutine = StartCoroutine(ScrollToSearchTarget(entry));
    }

    private IEnumerator ScrollToSearchTarget(GmSearchEntry entry)
    {
        yield return null;
        if (entry?.Target == null || !gmPages.TryGetValue(entry.PageId, out GmPageView page))
            yield break;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(page.Content);

        RectTransform viewport = page.Scroll.viewport;
        Bounds contentBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, page.Content);
        Bounds targetBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, entry.Target);
        float hiddenHeight = contentBounds.size.y - viewport.rect.height;
        if (hiddenHeight > 0.01f)
        {
            float distanceFromTop = contentBounds.max.y - targetBounds.max.y;
            page.Scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(distanceFromTop / hiddenHeight);
        }
        else
        {
            page.Scroll.verticalNormalizedPosition = 1f;
        }

        Image highlight = entry.Target.GetComponent<Image>();
        if (highlight != null)
        {
            Color original = highlight.color;
            highlight.color = new Color(0.86f, 0.48f, 0.18f, 1f);
            yield return new WaitForSecondsRealtime(0.9f);
            if (highlight != null)
                highlight.color = original;
        }

        searchNavigationCoroutine = null;
    }

    private void ClearGlobalSearch()
    {
        if (gmSearchInput == null)
            return;

        gmSearchInput.text = string.Empty;
        gmSearchInput.ActivateInputField();
    }

    private void RebuildCommandPage()
    {
        if (commandPageContent == null)
            return;

        RemoveSearchEntriesForPage(GmPageId.Commands);
        ClearChildren(commandPageContent);
        AddPageIntro(commandPageContent, "调试命令", "自动发现当前场景中经过白名单筛选的无参数调试方法。 ");

        GameObject header = CreateUiObject("Command Header", commandPageContent);
        header.AddComponent<LayoutElement>().preferredHeight = 34f;
        HorizontalLayoutGroup headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.spacing = 8f;
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlWidth = true;
        headerLayout.childControlHeight = true;
        headerLayout.childForceExpandWidth = false;

        commandCountText = CreateText(
            header.transform,
            $"反射调试命令（{commands.Count}）",
            14f,
            new Color(0.95f, 0.91f, 0.84f));
        commandCountText.fontStyle = FontStyles.Bold;
        commandCountText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton(header.transform, "重新扫描", RebuildReflectedCommands, 96f, 30f);

        commandGrid = CreateActionGrid(commandPageContent, 3, 338f, 34f, commands.Count);
        for (int i = 0; i < commands.Count; i++)
        {
            ReflectedCommand command = commands[i];
            string label = $"{command.Category} · {command.Label}";
            Button button = CreateButton(commandGrid, label, () => InvokeCommand(command), 0f, 34f);
            RegisterSearchEntry(
                GmPageId.Commands,
                label,
                $"{command.Method?.Name} {command.Target?.GetType().Name} 反射 debug",
                button.transform as RectTransform);
        }

        if (commands.Count == 0)
            AddPageHint(commandPageContent, "当前场景未发现可安全执行的调试命令。", 30f);

        RefreshSearchResultsIfVisible();
    }

    private void BindGameEventManager()
    {
        GameEventManager current = GameEventManager.Instance;
        if (ReferenceEquals(boundGameEventManager, current))
            return;

        UnbindGameEventManager();
        boundGameEventManager = current;
        if (boundGameEventManager == null)
            return;

        boundGameEventManager.EventStarted += HandleGameEventStateChanged;
        boundGameEventManager.EventEnded += HandleGameEventStateChanged;
    }

    private void UnbindGameEventManager()
    {
        if (boundGameEventManager == null)
            return;

        boundGameEventManager.EventStarted -= HandleGameEventStateChanged;
        boundGameEventManager.EventEnded -= HandleGameEventStateChanged;
        boundGameEventManager = null;
    }

    private void HandleGameEventStateChanged(GameEventRuntimeNotification notification)
    {
        RequestGameEventPageRefresh();
    }

    private void RequestGameEventPageRefresh()
    {
        if (gameEventRefreshCoroutine == null && isActiveAndEnabled)
            gameEventRefreshCoroutine = StartCoroutine(RebuildGameEventPageNextFrame());
    }

    private IEnumerator RebuildGameEventPageNextFrame()
    {
        yield return null;
        gameEventRefreshCoroutine = null;
        RebuildGameEventPage();
    }

    private void RebuildGameEventPage()
    {
        if (gameEventPageContent == null)
            return;

        RemoveSearchEntriesForPage(GmPageId.GameEvents);
        ClearChildren(gameEventPageContent);
        AddPageIntro(gameEventPageContent, "游戏事件", "查看 JSON 配置中的全局事件，并在当前主机世界中手动触发或结束事件。 ");

        BindGameEventManager();
        GameEventManager manager = boundGameEventManager;
        int definitionCount = manager?.Definitions.Count ?? 0;
        int activeCount = manager?.ActiveEvents.Count ?? 0;

        GameObject toolbar = CreateUiObject("Game Event Toolbar", gameEventPageContent);
        toolbar.AddComponent<LayoutElement>().preferredHeight = 38f;
        HorizontalLayoutGroup toolbarLayout = toolbar.AddComponent<HorizontalLayoutGroup>();
        toolbarLayout.spacing = 8f;
        toolbarLayout.childAlignment = TextAnchor.MiddleLeft;
        toolbarLayout.childControlWidth = true;
        toolbarLayout.childControlHeight = true;
        toolbarLayout.childForceExpandWidth = false;

        TextMeshProUGUI countText = CreateText(
            toolbar.transform,
            $"已加载 {definitionCount} 个事件 · 正在进行 {activeCount} 个",
            13f,
            new Color(0.82f, 0.82f, 0.78f));
        countText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton(toolbar.transform, "重载 JSON", ReloadGameEventConfiguration, 112f, 32f);

        if (manager == null || definitionCount == 0)
        {
            AddPageHint(gameEventPageContent, "没有加载到可用事件，请检查 Resources/Config/GameEvents/Definitions。", 36f);
            RefreshSearchResultsIfVisible();
            return;
        }

        for (int i = 0; i < manager.Definitions.Count; i++)
            CreateGameEventCard(manager, manager.Definitions[i]);

        RefreshSearchResultsIfVisible();
    }

    private void CreateGameEventCard(GameEventManager manager, GameEventDefinition definition)
    {
        bool active = manager.IsEventActive(definition.Id);
        GameObject card = CreateUiObject("Event " + definition.Id, gameEventPageContent);
        card.AddComponent<LayoutElement>().preferredHeight = 94f;
        card.AddComponent<Image>().color = active
            ? new Color(0.055f, 0.20f, 0.17f, 1f)
            : new Color(0.043f, 0.112f, 0.139f, 1f);
        Outline outline = card.AddComponent<Outline>();
        outline.effectColor = active
            ? new Color(0.25f, 0.85f, 0.62f, 0.55f)
            : new Color(0.51f, 0.58f, 0.58f, 0.25f);
        outline.effectDistance = new Vector2(1f, -1f);

        HorizontalLayoutGroup cardLayout = card.AddComponent<HorizontalLayoutGroup>();
        cardLayout.padding = new RectOffset(12, 10, 8, 8);
        cardLayout.spacing = 12f;
        cardLayout.childAlignment = TextAnchor.MiddleLeft;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = false;
        cardLayout.childForceExpandHeight = true;

        GameObject info = CreateUiObject("Info", card.transform);
        LayoutElement infoLayout = info.AddComponent<LayoutElement>();
        infoLayout.flexibleWidth = 1f;
        VerticalLayoutGroup infoGroup = info.AddComponent<VerticalLayoutGroup>();
        infoGroup.spacing = 1f;
        infoGroup.childControlWidth = true;
        infoGroup.childControlHeight = true;
        infoGroup.childForceExpandWidth = true;
        infoGroup.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText(
            info.transform,
            $"{definition.DisplayName}  <color=#80969B>({definition.Id})</color>",
            14f,
            active ? new Color(0.58f, 1f, 0.78f) : new Color(0.95f, 0.91f, 0.84f));
        title.fontStyle = FontStyles.Bold;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 23f;

        TextMeshProUGUI description = CreateText(
            info.transform,
            string.IsNullOrWhiteSpace(definition.Description) ? "无事件说明。" : definition.Description,
            12f,
            new Color(0.72f, 0.75f, 0.73f));
        description.enableWordWrapping = false;
        description.overflowMode = TextOverflowModes.Ellipsis;
        description.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        string duration = definition.DurationDays > 0f ? $"{definition.DurationDays:0.##} 天" : "动作完成即结束";
        string triggerType = definition.Trigger?.Type ?? "unknown";
        TextMeshProUGUI metadata = CreateText(
            info.transform,
            $"触发器 {triggerType} · 持续 {duration} · 动作 {definition.Actions.Count} 个 · {(active ? "进行中" : "未运行")}",
            11f,
            new Color(0.53f, 0.62f, 0.63f));
        metadata.enableWordWrapping = false;
        metadata.overflowMode = TextOverflowModes.Ellipsis;
        metadata.gameObject.AddComponent<LayoutElement>().preferredHeight = 19f;

        string eventId = definition.Id;
        Button actionButton = CreateButton(
            card.transform,
            active ? "结束事件" : "强制触发",
            () => TriggerOrCancelGameEvent(eventId),
            112f,
            38f);
        actionButton.GetComponent<Image>().color = active
            ? new Color(0.42f, 0.16f, 0.14f, 1f)
            : new Color(0.66f, 0.32f, 0.15f, 1f);

        RegisterSearchEntry(
            GmPageId.GameEvents,
            definition.DisplayName,
            $"{definition.Id} {definition.Description} {triggerType} 游戏事件 event 触发",
            card.transform as RectTransform);
    }

    private void TriggerOrCancelGameEvent(string eventId)
    {
        BindGameEventManager();
        if (boundGameEventManager == null)
        {
            SetStatus("游戏事件管理器不可用。", Color.yellow);
            return;
        }

        bool wasActive = boundGameEventManager.IsEventActive(eventId);
        bool success = wasActive
            ? boundGameEventManager.CancelEvent(eventId)
            : boundGameEventManager.TryForceTriggerNow(eventId);

        if (success)
        {
            SetStatus(
                wasActive ? $"已结束事件：{eventId}" : $"已触发事件：{eventId}",
                new Color(0.35f, 0.95f, 0.85f));
        }
        else
        {
            SetStatus(
                "操作失败：请确认已进入游戏世界且本机拥有状态权限。",
                Color.yellow);
        }

        RequestGameEventPageRefresh();
    }

    private void ReloadGameEventConfiguration()
    {
        BindGameEventManager();
        if (boundGameEventManager == null)
        {
            SetStatus("游戏事件管理器不可用。", Color.yellow);
            return;
        }

        GameEventConfigLoadResult result = boundGameEventManager.ReloadConfiguration();
        RebuildGameEventPage();
        SetStatus(
            result.HasErrors
                ? $"事件配置已重载，但发现 {result.Issues.Count} 个配置问题，请查看 Console。"
                : $"事件配置已重载，共 {boundGameEventManager.Definitions.Count} 个事件。",
            result.HasErrors ? Color.yellow : new Color(0.35f, 0.95f, 0.85f));
    }

    private void RefreshSearchResultsIfVisible()
    {
        if (gmSearchResultsRoot != null && gmSearchResultsRoot.activeSelf)
            RebuildGlobalSearchResults();
    }

    private void ClampTabbedWindowToCanvas()
    {
        if (gmCanvasRect == null || gmWindowRect == null)
            return;

        Canvas.ForceUpdateCanvases();
        Vector2 canvasSize = gmCanvasRect.rect.size;
        if (canvasSize.x <= 0f || canvasSize.y <= 0f)
            return;

        lastGmCanvasSize = canvasSize;

        const float safeMargin = 32f;
        gmWindowRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Horizontal,
            Mathf.Min(1160f, Mathf.Max(720f, canvasSize.x - safeMargin * 2f)));
        gmWindowRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            Mathf.Min(780f, Mathf.Max(560f, canvasSize.y - safeMargin * 2f)));
        LayoutRebuilder.ForceRebuildLayoutImmediate(gmWindowRect);
        SyncBrowserRootsToWindow();
        ResizeResponsiveGrids();
        LayoutRebuilder.ForceRebuildLayoutImmediate(gmWindowRect);

        if (airdropBrowserRect != null && airdropBrowserRoot != null && airdropBrowserRoot.activeSelf)
            LayoutRebuilder.ForceRebuildLayoutImmediate(airdropBrowserRect);
        if (aiCreatureBrowserRect != null && aiCreatureBrowserRoot != null && aiCreatureBrowserRoot.activeSelf)
            LayoutRebuilder.ForceRebuildLayoutImmediate(aiCreatureBrowserRect);
    }

    /// <summary>窗口打开期间检测分辨率变化，及时重新计算面板和目录列数。</summary>
    private void RefreshResponsiveLayoutIfCanvasChanged()
    {
        if (windowRoot == null || !windowRoot.activeSelf || gmCanvasRect == null)
            return;

        Vector2 canvasSize = gmCanvasRect.rect.size;
        if (canvasSize.x <= 0f || canvasSize.y <= 0f ||
            (canvasSize - lastGmCanvasSize).sqrMagnitude < 1f)
            return;

        ClampTabbedWindowToCanvas();
    }

    /// <summary>让目录覆盖层与 GM 主窗口保持同一尺寸和中心点。</summary>
    private void SyncBrowserRootsToWindow()
    {
        if (gmWindowRect == null)
            return;

        Vector2 windowSize = gmWindowRect.rect.size;
        SyncBrowserRoot(airdropBrowserRect, windowSize);
        SyncBrowserRoot(aiCreatureBrowserRect, windowSize);
    }

    private static void SyncBrowserRoot(RectTransform browserRect, Vector2 windowSize)
    {
        if (browserRect == null)
            return;

        browserRect.anchorMin = browserRect.anchorMax = new Vector2(0.5f, 0.5f);
        browserRect.pivot = new Vector2(0.5f, 0.5f);
        browserRect.anchoredPosition = Vector2.zero;
        browserRect.sizeDelta = windowSize;
    }

    private void ResizeResponsiveGrids()
    {
        if (gmWindowRect == null)
            return;

        gmResponsiveGrids.RemoveAll(entry => entry == null || entry.Grid == null);
        for (int i = 0; i < gmResponsiveGrids.Count; i++)
        {
            GmResponsiveGrid entry = gmResponsiveGrids[i];
            GridLayoutGroup grid = entry.Grid;
            RectTransform gridRect = grid.transform as RectTransform;
            float availableWidth = gridRect != null && gridRect.rect.width > 1f
                ? gridRect.rect.width
                : Mathf.Max(320f, gmWindowRect.rect.width - 88f);
            int columns = GetResponsiveColumnCount(entry.MaxColumns, availableWidth);
            float totalSpacing = grid.spacing.x * (columns - 1);
            float cellWidth = Mathf.Max(
                100f,
                (availableWidth - totalSpacing) / columns);
            grid.constraintCount = columns;
            grid.cellSize = new Vector2(
                cellWidth,
                entry.CellHeight);
            SetGridHeight(
                grid.transform,
                grid.transform.childCount,
                columns,
                entry.CellHeight,
                grid.spacing.y);
        }
    }

    private static int GetResponsiveColumnCount(int maxColumns, float availableWidth)
    {
        if (maxColumns <= 1 || availableWidth < 420f)
            return 1;

        if (maxColumns >= 4)
            return availableWidth >= 900f ? 4 : 2;

        if (maxColumns == 3)
            return availableWidth >= 780f ? 3 : 2;

        return availableWidth >= 560f ? 2 : 1;
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null)
            return;

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
            child.SetActive(false);
            UnityEngine.Object.Destroy(child);
        }
    }

    private static string GetPageLabel(GmPageId pageId)
    {
        return pageId switch
        {
            GmPageId.Player => "玩家",
            GmPageId.Buff => "Buff",
            GmPageId.Spawn => "生成与召唤",
            GmPageId.World => "世界",
            GmPageId.Structures => "遗迹",
            GmPageId.GameEvents => "游戏事件",
            GmPageId.Commands => "调试命令",
            GmPageId.Quests => "任务",
            _ => pageId.ToString()
        };
    }
}
