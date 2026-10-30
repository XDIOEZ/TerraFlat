using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Localization;
using FlatWorld.AIECS.Gameplay;
using FlatWorld.Gameplay.Events;
using FlatWorld.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
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
        Quests,
        Layers,
        Aiecs,
        Device,
        WorldCreatures
    }

    private sealed class GmPageView
    {
        public GameObject Root;
        public Button TabButton;
        public ScrollRect Scroll;
        public RectTransform Content;
        public GmPageId SelectedPageId;
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
    private readonly Dictionary<GmPageId, GmPageView> gmGroups = new();
    private readonly Vector3[] gmSectionCorners = new Vector3[4];
    private readonly List<GmSearchEntry> gmSearchEntries = new();
    private readonly List<GmResponsiveGrid> gmResponsiveGrids = new();
    private GmPageId activeGmPage = GmPageId.Player;
    private bool activePageDataDirty = true;

    private readonly Slider[] chunkStreamingBudgetSliders = new Slider[6];
    private readonly TextMeshProUGUI[] chunkStreamingBudgetValues = new TextMeshProUGUI[6];
    private TextMeshProUGUI chunkStreamingStatusText;
    private float nextChunkStreamingStatusRefreshAt;

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
    private Button aiecsPlayerDuelButton;
    private Button aiecsArmiesButton;
    private Button aiecsWanderButton;
    private Button aiecsFleeButton;
    private Button aiecsClearButton;
    private Button aiecsPlayerParticipatesButton;
    private Button aiecsLocalAvoidanceButton;
    private Button aiecsPopulationButton;
    private TextMeshProUGUI aiecsStatusText;
    private TextMeshProUGUI aiecsStatisticsText;
    private TextMeshProUGUI aiecsTickText;
    private TextMeshProUGUI aiecsCumulativeText;
    private TextMeshProUGUI aiecsNavigationText;
    private float nextAiecsPageRefreshTime;
    private const int MaxGmChunkLoadDistance = 16; // 覆盖无限档初始 5 倍视野所需的常见区块圈数。
    private const float MaxGmFiniteCameraView = 50f; // GM 有限档可读到 50，再往右进入无限视野。
    private const int MinutesPerGameDay = 24 * 60; // 一天的滑条刻度对应 00:00～23:59。
    private const float DayTimeRefreshInterval = 0.25f; // 用未缩放时间刷新时钟显示。
    private Slider timeScaleSlider;
    private Slider dayTimeSlider; // 展示并选择当天时刻。
    private Slider cameraViewSlider;
    private Slider chunkLoadDistanceSlider;
    private TextMeshProUGUI timeScaleValueText;
    private TextMeshProUGUI dayTimeValueText; // 显示当前或拖动预览的时分。
    private TextMeshProUGUI cameraViewValueText;
    private TextMeshProUGUI chunkLoadDistanceValueText;
    private float nextDayTimeRefreshAt;
    private int pendingDayTimeMinute = -1; // 拖动松开后只提交一次跳时。

    private void BuildTabbedWindow()
    {
        gmPages.Clear();
        gmGroups.Clear();
        gmSearchEntries.Clear();
        gmResponsiveGrids.Clear();

        GameObject canvasObject = new(
            "GM Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        GameObject safeArea = CreateUiObject("GM Safe Area", canvasObject.transform);
        gmCanvasRect = safeArea.GetComponent<RectTransform>();
        gmCanvasRect.anchorMin = Vector2.zero;
        gmCanvasRect.anchorMax = Vector2.one;
        gmCanvasRect.offsetMin = gmCanvasRect.offsetMax = Vector2.zero;
        safeArea.AddComponent<SafeAreaRectController>();

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = UIManager.GlobalOverlaySortingOrder;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        windowRoot = CreateUiObject("GM Tabbed Window", safeArea.transform);
        gmWindowRect = windowRoot.GetComponent<RectTransform>();
        gmWindowRect.anchorMin = gmWindowRect.anchorMax = new Vector2(0.5f, 0.5f);
        gmWindowRect.pivot = new Vector2(0.5f, 0.5f);
        gmWindowRect.sizeDelta = new Vector2(1160f, 780f);

        Image panelImage = windowRoot.AddComponent<Image>();
        panelImage.color = GmCanvas;
        Outline panelOutline = windowRoot.AddComponent<Outline>();
        StyleGmOutline(panelOutline, true);

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
        BuildPageGroups();

        BuildPlayerPage();
        BuildBuffPage();
        BuildQuestPage();
        BuildWorldPage();
        BuildLayersPage();
        BuildTeleportPage();
        BuildSpawnPage();
        BuildWorldCreaturesPage();
        BuildAiecsPage();
        BuildDevicePage();
        gameEventPageContent = CreatePage(GmPageId.GameEvents).Content;
        commandPageContent = CreatePage(GmPageId.Commands).Content;

        statusText = CreateText(
            windowRoot.transform,
            "按 F4 打开或关闭此窗口。",
            12f,
            GmTextSecondary);
        statusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        BindGameEventManager();
        RebuildGameEventPage();
        RebuildCommandPage();
        BuildAirdropBrowser(safeArea.transform);
        BuildAiCreatureBrowser(safeArea.transform);
        BuildTeleportTargeting(canvasObject.transform);
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
        header.AddComponent<LayoutElement>().preferredHeight = 74f;
        header.AddComponent<Image>().color = GmSurfaceRaised;

        HorizontalLayoutGroup layout = header.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(16, 12, 7, 7);
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        TextMeshProUGUI title = CreateText(
            header.transform,
            "FlatWorld GM 管理工具",
            20f,
            GmTextPrimary);
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        TextMeshProUGUI shortcut = CreateText(
            header.transform,
            "F4  开启 / 关闭",
            12f,
            GmTextSecondary);
        shortcut.alignment = TextAlignmentOptions.Right;
        shortcut.gameObject.AddComponent<LayoutElement>().preferredWidth = 130f;

        Button closeButton = CreateButton(header.transform, "关闭", () => SetWindowVisible(false), 96f, 60f);
        SetGmButtonVisual(closeButton, GmSurface);
    }

    private void BuildGlobalSearchBar()
    {
        GameObject toolbar = CreateUiObject("Global Search", windowRoot.transform);
        toolbar.AddComponent<LayoutElement>().preferredHeight = 42f;
        toolbar.AddComponent<Image>().color = GmSurfaceLow;

        HorizontalLayoutGroup layout = toolbar.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 3, 3);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        gmSearchInput = CreateInputField(toolbar.transform, "搜索功能、事件名称或命令", 680f, false);
        LayoutElement searchLayout = gmSearchInput.GetComponent<LayoutElement>();
        searchLayout.flexibleWidth = 1f;
        gmSearchInput.onValueChanged.AddListener(_ => RebuildGlobalSearchResults());

        gmSearchSummaryText = CreateText(
            toolbar.transform,
            "输入名称后跳转",
            12f,
            GmTextSecondary);
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
        gmSearchResultsRoot.AddComponent<Image>().color = GmSurfaceLow;
        Outline outline = gmSearchResultsRoot.AddComponent<Outline>();
        StyleGmOutline(outline, true);

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
        tabBar.AddComponent<Image>().color = GmSurfaceLow;

        ScrollRect scroll = tabBar.AddComponent<ItemStepScrollRect>();
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

        CreateTab(content.transform, GmPageId.Player, "玩家", 210f);
        CreateTab(content.transform, GmPageId.World, "世界", 210f);
        CreateTab(content.transform, GmPageId.Spawn, "生成与 AI", 210f);
        CreateTab(content.transform, GmPageId.GameEvents, "调试工具", 210f);
        CreateTab(content.transform, GmPageId.Device, "设备数据", 210f);
        // 五个分页在常规窗口内均衡排列，窄屏继续支持横向滚动。
        ContentSizeFitter tabSize = content.AddComponent<ContentSizeFitter>();
        tabSize.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void CreateTab(Transform parent, GmPageId pageId, string label, float width)
    {
        Button button = CreateSharedGmNavigationTabButton(parent, label,
            () => SetActivePage(gmGroups[pageId].SelectedPageId), width, 36f);
        LayoutElement layout = button.GetComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.flexibleWidth = 0f;
        gmGroups[pageId] = new GmPageView { TabButton = button, SelectedPageId = pageId };
    }

    #region GM 分类页面

    // 页签复用正式公共控件，仅由使用处设置文案、事件与外部尺寸。
    private static Button CreateSharedGmNavigationTabButton(
        Transform parent, string label, UnityAction action, float width, float height)
    {
        GameObject prefab = null;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                $"Assets/2_Prefabs/2-1_UI/Common/Controls/{RuntimeUIPrefabKeys.TabButton}.prefab");
#endif
        if (prefab == null)
            prefab = GameRes.ExistingInstance?.GetPrefab(RuntimeUIPrefabKeys.TabButton, false);
        if (prefab == null)
            throw new InvalidOperationException($"GM 缺少公共页签预制体：{RuntimeUIPrefabKeys.TabButton}");

        GameObject instance = Instantiate(prefab, parent, false);
        instance.name = label;
        Button button = instance.GetComponent<Button>();
        button.onClick.AddListener(action);
        SetLocalizedText(instance.GetComponentInChildren<TMP_Text>(true), label);
        LayoutElement size = instance.GetComponent<LayoutElement>();
        size.minWidth = 0f;
        size.preferredWidth = width;
        size.flexibleWidth = 0f;
        size.minHeight = size.preferredHeight = height;
        size.flexibleHeight = 0f;
        return button;
    }

    /// <summary>一级页签按用途分组，各功能区仍独立刷新和检索。</summary>
    private void BuildPageGroups()
    {
        CreateGroupPage(GmPageId.Player, GmPageId.Player, GmPageId.Buff, GmPageId.Quests);
        CreateGroupPage(GmPageId.World, GmPageId.World, GmPageId.Layers, GmPageId.Structures);
        CreateGroupPage(GmPageId.Spawn, GmPageId.Spawn, GmPageId.WorldCreatures, GmPageId.Aiecs);
        CreateGroupPage(GmPageId.GameEvents, GmPageId.GameEvents, GmPageId.Commands);
        CreateGroupPage(GmPageId.Device, GmPageId.Device);
    }

    private void CreateGroupPage(GmPageId groupId, params GmPageId[] sections)
    {
        string groupName = groupId == GmPageId.GameEvents ? "调试工具" : GetPageLabel(groupId);
        GameObject pageRoot = CreateUiObject(groupName + " Group", gmPageHost);
        RectTransform rootRect = pageRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        pageRoot.AddComponent<Image>().color = GmSurfaceLow;

        VerticalLayoutGroup groupLayout = pageRoot.AddComponent<VerticalLayoutGroup>();
        groupLayout.padding = new RectOffset(10, 10, 10, 10);
        groupLayout.spacing = 8f;
        groupLayout.childControlWidth = true;
        groupLayout.childControlHeight = true;
        groupLayout.childForceExpandWidth = true;
        groupLayout.childForceExpandHeight = false;
        GmPageView group = gmGroups[groupId];
        group.Root = pageRoot;
        pageRoot.SetActive(false);

        // 二级页签固定在滚动区外，单功能分类省去重复导航。
        if (sections.Length > 1)
        {
            GameObject navigation = CreateUiObject("Section Navigation", pageRoot.transform);
            LayoutElement navigationSize = navigation.AddComponent<LayoutElement>();
            navigationSize.minHeight = navigationSize.preferredHeight = 42f;
            // 显式截断横排布局上报的弹性高度，剩余空间全部交给正文。
            navigationSize.flexibleHeight = 0f;
            HorizontalLayoutGroup layout = navigation.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            foreach (GmPageId sectionId in sections)
            {
                Button tab = CreateSharedGmNavigationTabButton(navigation.transform, GetPageLabel(sectionId),
                    () => SetActivePage(sectionId), 150f, 38f);
                gmPages[sectionId] = new GmPageView { TabButton = tab };
            }
        }

        GameObject contentHost = CreateUiObject("Section Page Host", pageRoot.transform);
        contentHost.AddComponent<LayoutElement>().flexibleHeight = 1f;
        group.Content = contentHost.GetComponent<RectTransform>();
    }

    private GmPageView CreatePage(GmPageId pageId)
    {
        GmPageView group = gmGroups[GetPageGroup(pageId)];
        GameObject section = CreateUiObject(GetPageLabel(pageId) + " Section", group.Content);
        section.AddComponent<Image>().color = GmSurface;
        RectTransform sectionRect = section.GetComponent<RectTransform>();
        sectionRect.anchorMin = Vector2.zero;
        sectionRect.anchorMax = Vector2.one;
        sectionRect.offsetMin = sectionRect.offsetMax = Vector2.zero;
        Transform content = ConfigureVerticalScroll(section, 0f, out ScrollRect scroll);

        // 每个分页独立保存滚动位置，隐藏内容不再占用当前页空间。
        if (!gmPages.TryGetValue(pageId, out GmPageView page))
            page = new GmPageView();
        page.Root = section;
        page.Scroll = scroll;
        page.Content = content.GetComponent<RectTransform>();
        section.SetActive(false);
        gmPages[pageId] = page;
        return page;
    }

    private static GmPageId GetPageGroup(GmPageId pageId)
    {
        return pageId switch
        {
            GmPageId.Player or GmPageId.Buff or GmPageId.Quests => GmPageId.Player,
            GmPageId.World or GmPageId.Layers or GmPageId.Structures => GmPageId.World,
            GmPageId.Spawn or GmPageId.WorldCreatures or GmPageId.Aiecs => GmPageId.Spawn,
            GmPageId.GameEvents or GmPageId.Commands => GmPageId.GameEvents,
            GmPageId.Device => GmPageId.Device,
            _ => throw new ArgumentOutOfRangeException(nameof(pageId), pageId, "GM 功能未分类")
        };
    }

    private static void ScrollToPageTarget(GmPageView page, RectTransform target)
    {
        if (page?.Scroll?.content == null || target == null)
            return;

        RectTransform content = page.Scroll.content;
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        RectTransform viewport = page.Scroll.viewport;
        Bounds contentBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, content);
        Bounds targetBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, target);
        float hiddenHeight = contentBounds.size.y - viewport.rect.height;
        page.Scroll.verticalNormalizedPosition = hiddenHeight > 0.01f
            ? 1f - Mathf.Clamp01((contentBounds.max.y - targetBounds.max.y) / hiddenHeight)
            : 1f;
    }

    /// <summary>只刷新当前分页滚动视口内的动态功能区。</summary>
    private bool IsPageSectionInView(GmPageId pageId)
    {
        if (!gmPages.TryGetValue(pageId, out GmPageView page) ||
            page.Content == null || page.Root == null || !page.Root.activeInHierarchy ||
            page.Scroll?.viewport == null)
            return false;

        RectTransform viewport = page.Scroll.viewport;
        page.Content.GetWorldCorners(gmSectionCorners);
        float bottom = viewport.InverseTransformPoint(gmSectionCorners[0]).y;
        float top = viewport.InverseTransformPoint(gmSectionCorners[1]).y;
        return top >= viewport.rect.yMin && bottom <= viewport.rect.yMax;
    }

    #endregion

    private void BuildPlayerPage()
    {
        GmPageView page = CreatePage(GmPageId.Player);
        AddPageIntro(page.Content, "玩家与管理", "电脑 T 键传送到鼠标位置；手机或电脑均可点击“点选传送”，再点击场景选择位置。");

        Transform grid = CreateActionGrid(page.Content, 4, 256f, 60f, 8);
        CreateSearchableButton(grid, GmPageId.Player, "设为管理员", "管理员 admin 权限", SetAdministrator);
        adminInvincibilityButton = CreateSearchableButton(
            grid,
            GmPageId.Player,
            "管理员无敌：需权限",
            "管理员 无敌 开关 生命 死亡 invincibility god mode",
            ToggleAdminInvincibility);
        Button teleportButton = CreateSearchableButton(
            grid,
            GmPageId.Player,
            "点选传送",
            "传送 鼠标 手机 触屏 teleport",
            BeginTeleportTargeting,
            60f);
        teleportButton.GetComponentInChildren<TextMeshProUGUI>().fontSize = 18f;
        teleportShortcutButton = CreateSearchableButton(
            grid,
            GmPageId.Player,
            "T 传送：开",
            "T键 传送开关 快捷键",
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
            () => InvokeByTypeName("Mod_PlayerAdminController", "AddAmountToCurrentHandItem", 9999f));
        CreateSearchableButton(
            grid,
            GmPageId.Player,
            "背包 +100",
            "背包 物品 数量",
            () => InvokeByTypeName("Mod_PlayerAdminController", "AddAmountToAllBagItems", 100f));

        CreatePickupMultiplierControl(page.Content);
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
        SetGmButtonVisual(itemButton, GmSurfaceRaised, true);

        Button creatureButton = CreateSearchableButton(
            grid,
            GmPageId.Spawn,
            "打开 AI 生物目录",
            "AI 生物 动物 怪物 creature spawn 召唤",
            OpenAiCreatureBrowser,
            52f);
        SetGmButtonVisual(creatureButton, GmSurfaceRaised, true);

        itemHintText = AddPageHint(page.Content, "打开目录时会载入当前可用的物品与生物。", 24f);
        BuildFixedStructureSection(page.Content);

        AddPageIntro(page.Content, "生物观察", "查看动物和蜂巢的运行时参数，开关状态会跨 GM 窗口保留。");
        Transform debugGrid = CreateActionGrid(page.Content, 2, 516f, 44f, 2);
        animalDebugOverlayButton = CreateSearchableButton(
            debugGrid, GmPageId.Spawn, "动物参数：关",
            "动物 AI 头顶 状态 饱食度 狼 野猪 小鸡 蜜蜂 蜂巢 bee debug",
            ToggleAnimalDebugOverlay, 44f);
        hiveDebugOverlayButton = CreateSearchableButton(
            debugGrid, GmPageId.Spawn, "蜂巢参数：关",
            "蜂巢 蜂蜜 成员 繁殖 警戒 hive colony bee debug",
            ToggleHiveDebugOverlay, 44f);
        RefreshAnimalDebugOverlayButton();
        RefreshHiveDebugOverlayButton();
    }

    private void BuildWorldPage()
    {
        GmPageView page = CreatePage(GmPageId.World);
        AddPageIntro(page.Content, "世界与环境", "天气、时间、区块加载与视野控制。 ");

        Transform grid = CreateActionGrid(page.Content, 4, 256f, 36f, 4);
        CreateSearchableButton(grid, GmPageId.World, "晴天", "天气 晴 clear weather", () => InvokeByTypeName("GameDebugManager", "SetClearWeather"));
        CreateSearchableButton(grid, GmPageId.World, "下雨", "天气 雨 rain weather", () => InvokeByTypeName("GameDebugManager", "SetRainWeather"));
        CreateSearchableButton(grid, GmPageId.World, "刷新区块", "区块 chunk 刷新", () => InvokeByTypeName("Mod_ChunkLoader", "RefreshChunksAroundPlayer"));
        CreateChunkLoadSpeedControl(grid);
        CreateSearchableButton(grid, GmPageId.World, "点燃周围", "火 火灾 点火 fire", () => IgniteWorldFireAroundPlayer(10));
        CreateSearchableButton(grid, GmPageId.World, "万火点燃", "火 火灾 一万 批量 压测 fire", () => IgniteWorldFireAroundPlayer(100));
        CreateSearchableButton(grid, GmPageId.World, "扑灭周围", "火 灭火 extinguish", () => ExtinguishWorldFireAroundPlayer(100));
        CreateWorldRangeControls(page.Content);
        CreateChunkStreamingBudgetControls(page.Content);
        CreateWorldWindControl(page.Content);

        RefreshChunkLoadSpeedControl();
        RefreshWorldRangeControls();
        RefreshChunkStreamingBudgetControls();
        RefreshWorldWindControl();
    }

    #region 传送分页

    private void BuildTeleportPage()
    {
        GmPageView page = CreatePage(GmPageId.Structures);
        BuildPlanetTeleportRow(page.Content);
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
        SetGmButtonVisual(teleportButton, GmSurfaceRaised, true);
        CreateButton(row.transform, "刷新", RefreshStructureOptions, 82f, 40f);

        structureHintText = AddPageHint(page.Content, "进入游戏世界后选择遗迹类型，再执行传送。", 26f);
        RegisterSearchEntry(
            GmPageId.Structures,
            "传送到最近遗迹",
            "遗迹 建筑 structure ruin 传送",
            teleportButton.transform as RectTransform);
        BuildBiomeTeleportRow(page.Content);
        BuildTileTeleportRow(page.Content);
    }

    #endregion

    #region GM 二级分页选择

    private void SetActivePage(GmPageId pageId)
    {
        if (!gmPages.TryGetValue(pageId, out GmPageView selected) || selected.Root == null)
            return;

        GmPageId groupId = GetPageGroup(pageId);
        GMConsolePreferences.SetActivePageIndex((int)pageId);
        if (pageId != GmPageId.Structures)
            CancelLocationSearches();

        ApplyGmPageSelectionVisibility(pageId);
        RefreshRuntimeData(true);
        Canvas.ForceUpdateCanvases();
        ResizeResponsiveGrids();
        LayoutRebuilder.ForceRebuildLayoutImmediate(gmGroups[groupId].Root.GetComponent<RectTransform>());
        LayoutRebuilder.ForceRebuildLayoutImmediate(selected.Content);
    }

    /// <summary>只切换分页显示与选中样式，不触发业务刷新或修改玩家偏好。</summary>
    private void ApplyGmPageSelectionVisibility(GmPageId pageId)
    {
        GmPageId groupId = GetPageGroup(pageId);
        activeGmPage = pageId;
        gmGroups[groupId].SelectedPageId = pageId;

        foreach (KeyValuePair<GmPageId, GmPageView> pair in gmGroups)
        {
            bool active = pair.Key == groupId;
            if (pair.Value.Root != null)
                pair.Value.Root.SetActive(active);

            pair.Value.TabButton?.GetComponent<ReusableUITabVisual>()?.SetSelected(active);
        }

        foreach (KeyValuePair<GmPageId, GmPageView> pair in gmPages)
        {
            bool active = pair.Key == pageId;
            if (pair.Value.Root != null)
            {
                if (!active)
                    pair.Value.Scroll?.StopMovement();
                pair.Value.Root.SetActive(active);
            }
            pair.Value.TabButton?.GetComponent<ReusableUITabVisual>()?.SetSelected(active);
        }
    }

    #endregion

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
            "反射调试命令（{0}）",
            14f,
            GmTextPrimary);
        SetLocalizedFormat(commandCountText, "反射调试命令（{0}）", commands.Count);
        commandCountText.fontStyle = FontStyles.Bold;
        commandCountText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateButton(header.transform, "重新扫描", RebuildReflectedCommands, 96f, 30f);

        commandGrid = CreateActionGrid(commandPageContent, 3, 338f, 34f, commands.Count);
        for (int i = 0; i < commands.Count; i++)
        {
            ReflectedCommand command = commands[i];
            string label = $"{command.Category} · {command.Label}";
            Button button = CreateButton(commandGrid, label, () => InvokeCommand(command), 0f, 34f);
            SetLocalizedProvider(button.GetComponentInChildren<TextMeshProUGUI>(true), () =>
                FlatWorldLocalizationService.GetUiFormat("{0} · {1}",
                    FlatWorldLocalizationService.GetUiText(command.Category),
                    FlatWorldLocalizationService.GetUiText(command.Label)));
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

    private void RefreshSearchResultsIfVisible()
    {
        if (gmSearchResultsRoot != null && gmSearchResultsRoot.activeSelf)
            RebuildGlobalSearchResults();
    }

    private void ClampTabbedWindowToCanvas()
    {
        if (gmCanvasRect == null || gmWindowRect == null)
            return;

        Vector2 canvasSize = gmCanvasRect.rect.size;
        if (canvasSize.x > 0f && canvasSize.y > 0f &&
            (canvasSize - lastGmCanvasSize).sqrMagnitude < 1f)
            return;

        Canvas.ForceUpdateCanvases();
        canvasSize = gmCanvasRect.rect.size;
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
            GmPageId.Structures => "传送",
            GmPageId.GameEvents => "游戏事件",
            GmPageId.Commands => "调试命令",
            GmPageId.Quests => "任务",
            GmPageId.Layers => "层级显示",
            GmPageId.Aiecs => "AIECS",
            GmPageId.Device => "设备数据",
            GmPageId.WorldCreatures => "世界生物信息",
            _ => pageId.ToString()
        };
    }
}
