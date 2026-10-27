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

    #region GM 纵向滚动容器

    private static Transform ConfigureVerticalScroll(GameObject root, float inset, out ScrollRect scroll)
    {
        scroll = root.AddComponent<ItemStepScrollRect>();
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

    // 多个调试列表共用自动隐藏的纵向滚动条外观。
    private static Scrollbar CreateVerticalAutoHideScrollbar(
        GameObject root,
        ScrollRect scroll,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        GameObject scrollbarObject = CreateUiObject("Scrollbar", root.transform);
        RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.offsetMin = offsetMin;
        scrollbarRect.offsetMax = offsetMax;
        scrollbarObject.AddComponent<Image>().color = GmSurfaceLow;

        GameObject slidingArea = CreateUiObject("Sliding Area", scrollbarObject.transform);
        RectTransform slidingRect = slidingArea.GetComponent<RectTransform>();
        slidingRect.anchorMin = Vector2.zero;
        slidingRect.anchorMax = Vector2.one;
        slidingRect.offsetMin = new Vector2(2f, 2f);
        slidingRect.offsetMax = new Vector2(-2f, -2f);

        GameObject handle = CreateUiObject("Handle", slidingArea.transform);
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;
        Image handleImage = handle.AddComponent<Image>();
        handleImage.color = GmSelection;

        Scrollbar scrollbar = scrollbarObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = handleRect;
        scrollbar.targetGraphic = handleImage;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return scrollbar;
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
        CreateWorldRangeControls(page.Content);
        CreateChunkStreamingBudgetControls(page.Content);
        CreateWorldWindControl(page.Content);

        RefreshChunkLoadSpeedControl();
        RefreshWorldRangeControls();
        RefreshChunkStreamingBudgetControls();
        RefreshWorldWindControl();
    }

    #region 世界时间、视野与区块范围

    /// <summary>用独立滑条控制时间流速、当天时刻、相机视野和区块加载范围。</summary>
    private void CreateWorldRangeControls(Transform parent)
    {
        timeScaleSlider = CreateWorldRangeSlider(
            parent, "时间流速", "时间 流速 加速 减速 恢复 time scale speed",
            out timeScaleValueText, false);
        timeScaleSlider.onValueChanged.AddListener(SetTimeScaleFromSlider);

        dayTimeSlider = CreateWorldRangeSlider(
            parent, "当天时间", "时间 当天 时刻 跳转 白天 夜晚 clock time of day",
            out dayTimeValueText);
        dayTimeSlider.minValue = 0f;
        dayTimeSlider.maxValue = MinutesPerGameDay - 1f;
        dayTimeSlider.onValueChanged.AddListener(SetDayTimeFromSlider);

        cameraViewSlider = CreateWorldRangeSlider(
            parent, "视野缩放", "相机 视野 缩放 无限 view camera zoom",
            out cameraViewValueText);
        cameraViewSlider.onValueChanged.AddListener(SetCameraViewFromSlider);

        chunkLoadDistanceSlider = CreateWorldRangeSlider(
            parent, "区块加载范围", "区块 加载 范围 距离 chunk load distance",
            out chunkLoadDistanceValueText);
        chunkLoadDistanceSlider.minValue = 1f;
        chunkLoadDistanceSlider.maxValue = MaxGmChunkLoadDistance;
        chunkLoadDistanceSlider.onValueChanged.AddListener(SetChunkLoadDistanceFromSlider);
    }

    /// <summary>沿用项目通用滑动条 Prefab，放入世界页可滚动的整行控件。</summary>
    private Slider CreateWorldRangeSlider(
        Transform parent, string label, string keywords, out TextMeshProUGUI valueText,
        bool wholeNumbers = true)
    {
        GameObject row = CreateUiObject(label, parent);
        row.AddComponent<Image>().color = GmSurface;
        StyleGmOutline(row.AddComponent<Outline>());
        row.AddComponent<LayoutElement>().preferredHeight = 56f;

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 0, 0);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        TextMeshProUGUI nameText = CreateText(row.transform, label, 15f, GmTextPrimary);
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.enableWordWrapping = false;
        nameText.raycastTarget = false;
        nameText.gameObject.AddComponent<LayoutElement>().preferredWidth = 140f;

        GameObject sliderObject = Instantiate(
            GameRes.ExistingInstance.GetPrefab(RuntimeUIPrefabKeys.SliderControl, false),
            row.transform, false);
        sliderObject.name = label + " Slider";
        LayoutElement sliderLayout = sliderObject.GetComponent<LayoutElement>();
        sliderLayout.minWidth = 80f;
        sliderLayout.preferredWidth = -1f;
        sliderLayout.flexibleWidth = 1f;
        Slider slider = sliderObject.GetComponent<Slider>();
        slider.wholeNumbers = wholeNumbers;
        slider.direction = Slider.Direction.LeftToRight;

        valueText = CreateText(row.transform, "--", 15f, GmTextPrimary);
        valueText.alignment = TextAlignmentOptions.Center;
        valueText.enableWordWrapping = false;
        valueText.raycastTarget = false;
        valueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 94f;
        RegisterSearchEntry(GmPageId.World, label, keywords, (RectTransform)row.transform);
        return slider;
    }

    /// <summary>按 0.1 倍步长设置本地玩家的时间流速。</summary>
    private void SetTimeScaleFromSlider(float value)
    {
        Mod_PlayerAdminController controller = FindLocalPlayerModule<Mod_PlayerAdminController>();
        if (controller == null)
        {
            RefreshWorldRangeControls();
            return;
        }

        controller.TrySetTimeScale(Mathf.Round(value * 10f) / 10f);
        RefreshWorldRangeControls();
    }

    /// <summary>拖动时预览 00:00～23:59，松开指针后才提交跳时。</summary>
    private void SetDayTimeFromSlider(float value)
    {
        pendingDayTimeMinute = Mathf.Clamp(Mathf.RoundToInt(value), 0, MinutesPerGameDay - 1);
        SetLocalizedText(dayTimeValueText, FormatDayTime(pendingDayTimeMinute));
        if (!IsDayTimePointerPressed())
            CommitPendingDayTime();
    }

    /// <summary>通过现有时钟入口跳到当天目标时刻，不更改游戏天数。</summary>
    private void CommitPendingDayTime()
    {
        if (pendingDayTimeMinute < 0)
            return;

        int minute = pendingDayTimeMinute;
        pendingDayTimeMinute = -1;
        if (!GameNetwork.HasStateAuthority ||
            !TryGetActiveDayTime(out DayTimeSystem timeSystem, out string timeSource, out TimeData timeData))
        {
            SetStatus("当前世界时间不可修改；联机时请由房主操作。", Color.yellow);
            RefreshDayTimeControl();
            return;
        }

        float targetTime = timeData.DayLength * (minute / (float)MinutesPerGameDay);
        timeSystem.JumpToTime(timeSource, targetTime);
        RefreshDayTimeControl();
    }

    /// <summary>同时识别鼠标与触摸拖动，键盘改值直接提交。</summary>
    private static bool IsDayTimePointerPressed()
    {
        return Mouse.current?.leftButton.isPressed == true ||
               Touchscreen.current?.primaryTouch.press.isPressed == true;
    }

    /// <summary>场景或面板切换时取消尚未松开的预览值。</summary>
    private void CancelPendingDayTimeJump()
    {
        pendingDayTimeMinute = -1;
    }

    /// <summary>解析当前场景实际引用的世界时钟，避免在引用维度新建独立时间数据。</summary>
    private static bool TryGetActiveDayTime(
        out DayTimeSystem timeSystem, out string timeSource, out TimeData timeData)
    {
        timeSystem = null;
        timeSource = string.Empty;
        timeData = null;
        // 主菜单也会创建 GM 面板，此时世界时钟尚未加载，不应触发必需单例的报错。
        if (GameManager.Instance == null || !GameManager.Instance.IsInGameWorld)
            return false;

        timeSystem = DayTimeSystem.GetInstance();
        return timeSystem != null &&
               timeSystem.TryGetResolvedTimeData(
                   SceneManager.GetActiveScene().name, out timeSource, out timeData) &&
               timeData != null && timeData.DayLength > 0f &&
               !float.IsNaN(timeData.DayLength) && !float.IsInfinity(timeData.DayLength);
    }

    /// <summary>按当前天内进度显示 24 小时制时间。</summary>
    private static string FormatDayTime(int minute)
    {
        return $"{minute / 60:00}:{minute % 60:00}";
    }

    /// <summary>面板停留在世界页时，定期同步仍在流逝的游戏时钟。</summary>
    private void RefreshDayTimeControlIfNeeded()
    {
        if (dayTimeSlider == null || windowRoot == null || !windowRoot.activeSelf ||
            !gmPages.TryGetValue(GmPageId.World, out GmPageView page) ||
            page.Root == null || !page.Root.activeInHierarchy)
        {
            CancelPendingDayTimeJump();
            return;
        }

        if (pendingDayTimeMinute >= 0 && !IsDayTimePointerPressed())
            CommitPendingDayTime();

        if (Time.unscaledTime < nextDayTimeRefreshAt)
            return;

        nextDayTimeRefreshAt = Time.unscaledTime + DayTimeRefreshInterval;
        if (pendingDayTimeMinute >= 0 || IsDayTimePointerPressed())
            return;

        RefreshDayTimeControl();
    }

    /// <summary>从权威时钟刷新滑条；非房主只读，拖动期间由输入回调显示目标时刻。</summary>
    private void RefreshDayTimeControl()
    {
        bool ready = TryGetActiveDayTime(out _, out _, out TimeData timeData);
        dayTimeSlider.interactable = ready && GameNetwork.HasStateAuthority;
        if (!ready)
        {
            dayTimeSlider.SetValueWithoutNotify(0f);
            SetLocalizedText(dayTimeValueText, "--:--");
            return;
        }

        float progress = Mathf.Repeat(timeData.CurrentTime, timeData.DayLength) / timeData.DayLength;
        int minute = Mathf.Clamp(
            Mathf.FloorToInt(progress * MinutesPerGameDay), 0, MinutesPerGameDay - 1);
        dayTimeSlider.SetValueWithoutNotify(minute);
        SetLocalizedText(dayTimeValueText, FormatDayTime(minute));
    }

    /// <summary>最右档解锁无限缩放并立即拉远；离开最右档恢复普通视野上限。</summary>
    private void SetCameraViewFromSlider(float value)
    {
        Mod_Cam cameraModule = FindLocalPlayerModule<Mod_Cam>();
        if (cameraModule == null || !cameraModule.IsCameraReady)
        {
            RefreshWorldRangeControls();
            return;
        }

        bool unlimited = value >= cameraViewSlider.maxValue;
        if (unlimited)
        {
            cameraModule.SetViewLimitAndSize(
                Mathf.Max(cameraModule.CurrentOrthographicSize, cameraModule.MaxPovValue * 5f),
                true,
                MaxGmFiniteCameraView);
        }
        else
        {
            cameraModule.SetViewLimitAndSize(value, false, MaxGmFiniteCameraView);
        }

        RefreshWorldRangeControls();
    }

    /// <summary>区块滑条只改变加载窗口，不修改相机正交尺寸。</summary>
    private void SetChunkLoadDistanceFromSlider(float value)
    {
        Mod_ChunkLoader loader = FindLocalPlayerModule<Mod_ChunkLoader>();
        if (loader == null)
        {
            RefreshWorldRangeControls();
            return;
        }

        loader.SetLoadChunkDistance(Mathf.RoundToInt(value));
        RefreshWorldRangeControls();
    }

    /// <summary>面板打开或切换到世界页时读取时间、相机和区块模块的当前状态。</summary>
    private void RefreshWorldRangeControls()
    {
        RefreshDayTimeControl();

        Player localPlayer = FindLocalPlayer();
        Mod_PlayerAdminController controller = localPlayer != null
            ? localPlayer.GetComponentInChildren<Mod_PlayerAdminController>(true)
            : null;
        timeScaleSlider.onValueChanged.RemoveListener(SetTimeScaleFromSlider);
        timeScaleSlider.interactable = controller != null;
        if (controller != null)
        {
            timeScaleSlider.minValue = controller.minTimeScale;
            timeScaleSlider.maxValue = controller.maxTimeScale;
            timeScaleSlider.SetValueWithoutNotify(controller.timeScale);
            SetLocalizedFormat(timeScaleValueText, "{0:0.0}x", controller.timeScale);
        }
        else
        {
            SetLocalizedText(timeScaleValueText, "--");
        }
        timeScaleSlider.onValueChanged.AddListener(SetTimeScaleFromSlider);

        Mod_Cam cameraModule = localPlayer != null
            ? localPlayer.GetComponentInChildren<Mod_Cam>(true)
            : null;
        cameraViewSlider.onValueChanged.RemoveListener(SetCameraViewFromSlider);
        bool cameraReady = cameraModule != null && cameraModule.IsCameraReady;
        cameraViewSlider.interactable = cameraReady;
        if (cameraReady)
        {
            cameraViewSlider.minValue = cameraModule.MinPovValue;
            cameraViewSlider.maxValue = MaxGmFiniteCameraView + 1f;
            cameraViewSlider.SetValueWithoutNotify(cameraModule.IsUnlimitedViewEnabled
                ? cameraViewSlider.maxValue
                : Mathf.Min(MaxGmFiniteCameraView, Mathf.Round(cameraModule.CurrentOrthographicSize)));
            if (cameraModule.IsUnlimitedViewEnabled)
                SetLocalizedText(cameraViewValueText, "无限");
            else
                SetLocalizedFormat(cameraViewValueText, "{0:0}", cameraModule.CurrentOrthographicSize);
        }
        else
        {
            SetLocalizedText(cameraViewValueText, "--");
        }
        cameraViewSlider.onValueChanged.AddListener(SetCameraViewFromSlider);

        Mod_ChunkLoader loader = localPlayer != null
            ? localPlayer.GetComponentInChildren<Mod_ChunkLoader>(true)
            : null;
        chunkLoadDistanceSlider.onValueChanged.RemoveListener(SetChunkLoadDistanceFromSlider);
        chunkLoadDistanceSlider.interactable = loader != null;
        if (loader != null)
        {
            chunkLoadDistanceSlider.maxValue = Mathf.Max(
                MaxGmChunkLoadDistance, loader.CurrentLoadChunkDistance);
            chunkLoadDistanceSlider.SetValueWithoutNotify(loader.CurrentLoadChunkDistance);
            SetLocalizedFormat(chunkLoadDistanceValueText, "{0} 区块", loader.CurrentLoadChunkDistance);
        }
        else
        {
            SetLocalizedText(chunkLoadDistanceValueText, "--");
        }
        chunkLoadDistanceSlider.onValueChanged.AddListener(SetChunkLoadDistanceFromSlider);
    }

    /// <summary>世界页只控制本机玩家，不把滑条绑定到联机远程玩家副本。</summary>
    private static T FindLocalPlayerModule<T>() where T : Component
    {
        Player localPlayer = FindLocalPlayer();
        return localPlayer != null ? localPlayer.GetComponentInChildren<T>(true) : null;
    }

    private static Player FindLocalPlayer()
    {
        return FindObjectsOfType<Player>(true)
            .FirstOrDefault(player => player.IsLocalProfile);
    }

    #endregion

    #region 区块流送预算

    /// <summary>开发人员可分别调整结果提交、基础地形和后续表现的数量与耗时上限。</summary>
    private void CreateChunkStreamingBudgetControls(Transform parent)
    {
        AddPageIntro(parent, "区块每帧预算",
            "先提交生成结果，再显示地面和补齐细节。每项同时受数量与毫秒限制；单个区块会完整执行。数量滑条按指数增长，最高 1024。");

        string[] labels =
        {
            "提交数量/帧", "提交耗时/帧", "地面数量/帧", "地面耗时/帧",
            "细节步骤/帧", "细节耗时/帧"
        };
        string[] keywords =
        {
            "区块 生成结果 提交 数量 commit count",
            "区块 生成结果 提交 毫秒 时间 commit ms",
            "区块 基础地形 显示 数量 presentation start count",
            "区块 基础地形 显示 毫秒 presentation start ms",
            "区块 草地 自然物 导航 细节 步骤 continuation count",
            "区块 草地 自然物 导航 细节 毫秒 continuation ms"
        };
        for (int i = 0; i < chunkStreamingBudgetSliders.Length; i++)
        {
            int slot = i;
            Slider slider = CreateWorldRangeSlider(parent, labels[i], keywords[i],
                out chunkStreamingBudgetValues[i], false);
            slider.minValue = i % 2 == 0 ? 0f : ChunkMgr.MinRuntimeChunkWorkMillisecondsPerFrame;
            slider.maxValue = i % 2 == 0 ? 10f : ChunkMgr.MaxRuntimeChunkWorkMillisecondsPerFrame;
            slider.onValueChanged.AddListener(value => SetChunkStreamingBudgetFromSlider(slot, value));
            chunkStreamingBudgetSliders[i] = slider;

            EventTrigger trigger = slider.GetComponent<EventTrigger>() ??
                                   slider.gameObject.AddComponent<EventTrigger>();
            foreach (EventTriggerType eventType in new[]
                         { EventTriggerType.PointerUp, EventTriggerType.Deselect })
            {
                EventTrigger.Entry entry = new() { eventID = eventType };
                entry.callback.AddListener(_ => GMConsolePreferences.SavePendingChanges());
                trigger.triggers.Add(entry);
            }
        }

        chunkStreamingStatusText = AddPageHint(parent, "生成排队 -- | 生成中 -- | 待提交 -- | 待显示 --", 32f);
    }

    /// <summary>滑动时即时写入当前 ChunkMgr，数量轴用 2 的幂覆盖 1～1024。</summary>
    private void SetChunkStreamingBudgetFromSlider(int slot, float value)
    {
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager == null)
        {
            RefreshChunkStreamingBudgetControls();
            return;
        }

        int count = Mathf.Clamp(Mathf.RoundToInt(Mathf.Pow(2f, value)),
            1, ChunkMgr.MaxRuntimeChunkWorkCountPerFrame);
        float milliseconds = Mathf.Round(value * 2f) / 2f;
        switch (slot)
        {
            case 0: chunkManager.RuntimeChunkCommitBudget = count; break;
            case 1: chunkManager.RuntimeChunkCommitMillisecondsBudget = milliseconds; break;
            case 2: chunkManager.RuntimeChunkPresentationStartBudget = count; break;
            case 3: chunkManager.RuntimeChunkPresentationStartMillisecondsBudget = milliseconds; break;
            case 4: chunkManager.RuntimeChunkPresentationContinuationBudget = count; break;
            case 5: chunkManager.RuntimeChunkPresentationContinuationMillisecondsBudget = milliseconds; break;
        }

        GMConsolePreferences.SetChunkStreamingBudgets(chunkManager);
        RefreshChunkStreamingBudgetControls();
    }

    /// <summary>从运行中管理器读取真实预算，切换世界和重新打开 GM 时不会显示旧滑条值。</summary>
    private void RefreshChunkStreamingBudgetControls()
    {
        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkStreamingBudgetSliders[0] == null)
            return;

        for (int i = 0; i < chunkStreamingBudgetSliders.Length; i++)
        {
            Slider slider = chunkStreamingBudgetSliders[i];
            slider.interactable = chunkManager != null;
            if (chunkManager == null)
            {
                SetLocalizedText(chunkStreamingBudgetValues[i], "--");
                continue;
            }

            float budget = i switch
            {
                0 => chunkManager.RuntimeChunkCommitBudget,
                1 => chunkManager.RuntimeChunkCommitMillisecondsBudget,
                2 => chunkManager.RuntimeChunkPresentationStartBudget,
                3 => chunkManager.RuntimeChunkPresentationStartMillisecondsBudget,
                4 => chunkManager.RuntimeChunkPresentationContinuationBudget,
                _ => chunkManager.RuntimeChunkPresentationContinuationMillisecondsBudget
            };
            bool isCount = i % 2 == 0;
            slider.SetValueWithoutNotify(isCount ? Mathf.Log(budget, 2f) : budget);
            if (isCount)
                SetLocalizedFormat(chunkStreamingBudgetValues[i], "{0:0}", budget);
            else
                SetLocalizedFormat(chunkStreamingBudgetValues[i], "{0:0.0} ms", budget);
        }
        RefreshChunkStreamingStatus();
    }

    /// <summary>只在世界页打开时刷新队列数，方便判断该调哪一段预算。</summary>
    private void RefreshChunkStreamingStatusIfNeeded()
    {
        if (windowRoot == null || !windowRoot.activeSelf || activeGmPage != GmPageId.World ||
            !IsPageSectionInView(GmPageId.World) || Time.unscaledTime < nextChunkStreamingStatusRefreshAt)
            return;

        nextChunkStreamingStatusRefreshAt = Time.unscaledTime + 0.4f;
        RefreshChunkStreamingStatus();
    }

    private void RefreshChunkStreamingStatus()
    {
        if (chunkStreamingStatusText == null)
            return;

        ChunkMgr chunkManager = ChunkMgr.ExistingInstance;
        if (chunkManager?.RuntimeChunks == null)
        {
            SetLocalizedText(chunkStreamingStatusText, "生成排队 -- | 生成中 -- | 待提交 -- | 待显示 --");
            return;
        }

        SetLocalizedFormat(
            chunkStreamingStatusText,
            "生成排队 {0} | 生成中 {1} | 待提交 {2} | 待显示 {3}",
            chunkManager.RuntimeChunks.QueuedGenerationCount,
            chunkManager.RuntimeChunks.ActiveGenerationCount,
            chunkManager.RuntimeChunks.PendingCommitCount,
            chunkManager.PendingRuntimeChunkPresentationCount);
    }

    #endregion

    #region AIECS 实战分页

    /// <summary>AIECS 实战入口只保留模拟宿主，所有开发操作统一嵌入 GM 分页。</summary>
    private void BuildAiecsPage()
    {
        GmPageView page = CreatePage(GmPageId.Aiecs);
        AddPageIntro(
            page.Content,
            "AIECS 实战开发",
            "控制开发入口中的真实 ECS 单位；测试场景、玩家参与开关与运行统计均集中在这里。");

        Transform grid = CreateActionGrid(page.Content, 4, 256f, 60f, 8);
        aiecsPlayerDuelButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "玩家对战",
            "AIECS ECS 玩家 对战 duel",
            () => StartAiecsScenario(AiecsPlaygroundMode.PlayerDuel),
            60f);
        aiecsArmiesButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "两军交战",
            "AIECS ECS 两军 战斗 增援 armies",
            ReinforceAiecsArmies,
            60f);
        aiecsWanderButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "无目标游荡",
            "AIECS ECS 游荡 wander",
            () => StartAiecsScenario(AiecsPlaygroundMode.Wander),
            60f);
        aiecsFleeButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "低血量逃跑",
            "AIECS ECS 低血量 逃跑 flee",
            () => StartAiecsScenario(AiecsPlaygroundMode.Flee),
            60f);
        aiecsClearButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "清理",
            "AIECS ECS 清理 clear",
            ClearAiecsScenario,
            60f);
        aiecsPlayerParticipatesButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "玩家参与感知与战斗：关",
            "AIECS ECS 玩家 参与 感知 战斗 toggle",
            ToggleAiecsPlayerParticipation,
            60f);
        aiecsLocalAvoidanceButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "局部避让：关",
            "AIECS ECS 局部 避让 separation avoidance toggle",
            ToggleAiecsLocalAvoidance,
            60f);
        aiecsPopulationButton = CreateSearchableButton(
            grid, GmPageId.Aiecs, "每军数量", "AIECS ECS 压力 数量 population",
            CycleAiecsPopulation, 60f);
        CreateSearchableButton(grid, GmPageId.Aiecs, "生物产出验收", "AIECS ECS 生物 战利品 loot 验收",
            () => { ResolveAiecsPlayground()?.QueueActorLootTest(); RefreshAiecsPage(); }, 60f);

        aiecsStatusText = CreateAiecsReadout(page.Content, "正在连接 AIECS 实战开发入口…", 42f, true);
        aiecsStatisticsText = CreateAiecsReadout(page.Content, "存活 0 / 目标 0 / 游荡 0 / 追击 0 / 逃跑 0 / 攻击 0", 30f);
        aiecsTickText = CreateAiecsReadout(page.Content, "本 Tick：感知请求 0，候选 0，LOS 0，热点格 0", 30f);
        aiecsCumulativeText = CreateAiecsReadout(page.Content, "累计：玩家受击 0（-0.0 HP），武器→ECS 命中 0，死亡 0，掉落 0", 30f);
        aiecsNavigationText = CreateAiecsReadout(page.Content, "共享目标 0 / Chunk 0 / 出口图 0 / 目标图 0 / 区块路线 0", 30f);
        RefreshAiecsPage();
    }

    private static TextMeshProUGUI CreateAiecsReadout(
        Transform parent,
        string initialText,
        float height,
        bool emphasized = false)
    {
        TextMeshProUGUI text = CreateText(
            parent,
            initialText,
            emphasized ? 14f : 13f,
            emphasized ? GmTextPrimary : GmTextSecondary);
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        text.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return text;
    }

    private void StartAiecsScenario(AiecsPlaygroundMode mode)
    {
        AiecsPlayground playground = ResolveAiecsPlayground();
        if (playground == null)
        {
            SetStatus("当前世界没有可用的 AIECS 正式宿主，无法创建 GM 调试入口。", Color.yellow);
            RefreshAiecsPage();
            return;
        }

        playground.StartScenario(mode);
        SetStatus(playground.Status, GmTextSecondary);
        RefreshAiecsPage();
    }

    private void ReinforceAiecsArmies()
    {
        AiecsPlayground playground = ResolveAiecsPlayground();
        if (playground == null)
        {
            SetStatus("当前世界没有可用的 AIECS 正式宿主，无法创建 GM 调试入口。", Color.yellow);
            RefreshAiecsPage();
            return;
        }

        playground.StartOrReinforceArmies();
        SetStatus(playground.Status, GmTextSecondary);
        RefreshAiecsPage();
    }

    private void ClearAiecsScenario()
    {
        AiecsPlayground playground = ResolveAiecsPlayground();
        if (playground == null)
        {
            SetStatus("当前世界没有可用的 AIECS 正式宿主，无法创建 GM 调试入口。", Color.yellow);
            RefreshAiecsPage();
            return;
        }

        playground.ClearScenario();
        SetStatus(playground.Status, GmTextSecondary);
        RefreshAiecsPage();
    }

    private void ToggleAiecsPlayerParticipation()
    {
        AiecsPlayground playground = ResolveAiecsPlayground();
        if (playground == null || !playground.HasActiveScenario)
        {
            SetStatus("AIECS 当前没有可切换玩家参与状态的开发场景。", Color.yellow);
            RefreshAiecsPage();
            return;
        }

        playground.SetPlayerParticipates(!playground.PlayerParticipates);
        RefreshAiecsPage();
    }

    /// <summary>切换连续邻居 Steering 与密度分流；共享 Flow Field 和地形约束始终保留。</summary>
    private void ToggleAiecsLocalAvoidance()
    {
        AiecsPlayground playground = ResolveAiecsPlayground();
        if (playground == null)
        {
            SetStatus("当前世界没有可用的 AIECS 正式宿主，无法创建 GM 调试入口。", Color.yellow);
            RefreshAiecsPage();
            return;
        }

        playground.SetLocalAvoidanceEnabled(!playground.LocalAvoidanceEnabled);
        RefreshAiecsPage();
    }

    /// <summary>只调整下一波测试请求；不修改正在交战单位或正式生态配置。</summary>
    private void CycleAiecsPopulation()
    {
        AiecsPlayground playground = ResolveAiecsPlayground();
        if (playground == null) return;
        int[] levels = { 100, 500, 1000, 2500, 5000, 10000 };
        int next = levels[0];
        foreach (int level in levels)
            if (level > playground.UnitsPerArmy) { next = level; break; }
        playground.UnitsPerArmy = next;
        RefreshAiecsPage();
    }

    /// <summary>仅在 GM 的 AIECS 分页可见时以 10Hz 更新文本，避免无窗口时制造字符串垃圾。</summary>
    private void RefreshAiecsPageIfNeeded()
    {
        if (windowRoot == null || !windowRoot.activeSelf ||
            !gmPages.TryGetValue(GmPageId.Aiecs, out GmPageView page) ||
            page.Root == null || !IsPageSectionInView(GmPageId.Aiecs) ||
            Time.unscaledTime < nextAiecsPageRefreshTime)
            return;

        nextAiecsPageRefreshTime = Time.unscaledTime + 0.1f;
        RefreshAiecsPage();
    }

    private void RefreshAiecsPage()
    {
        if (aiecsStatusText == null)
            return;

        AiecsPlayground playground = ResolveAiecsPlayground();
        bool exists = playground != null;
        bool ready = exists && playground.IsReady;
        bool activeScenario = exists && playground.HasActiveScenario;
        bool playerParticipates = activeScenario && playground.PlayerParticipates;
        bool localAvoidanceEnabled = exists && playground.LocalAvoidanceEnabled;

        if (aiecsPlayerDuelButton != null) aiecsPlayerDuelButton.interactable = ready;
        if (aiecsArmiesButton != null) aiecsArmiesButton.interactable = ready;
        if (aiecsWanderButton != null) aiecsWanderButton.interactable = ready;
        if (aiecsFleeButton != null) aiecsFleeButton.interactable = ready;
        if (aiecsClearButton != null) aiecsClearButton.interactable = activeScenario;
        if (aiecsPopulationButton != null)
        {
            aiecsPopulationButton.interactable = exists;
            if (exists)
                SetLocalizedFormat(aiecsPopulationButton.GetComponentInChildren<TextMeshProUGUI>(true),
                    "每军 {0}（点击切换）", playground.UnitsPerArmy);
            else
                SetButtonLabel(aiecsPopulationButton, "每军数量");
        }
        if (aiecsPlayerParticipatesButton != null)
        {
            aiecsPlayerParticipatesButton.interactable = activeScenario;
            TextMeshProUGUI label = aiecsPlayerParticipatesButton.GetComponentInChildren<TextMeshProUGUI>(true);
            SetLocalizedProvider(label, () => FlatWorldLocalizationService.GetUiFormat(
                "玩家参与感知与战斗：{0}",
                FlatWorldLocalizationService.GetUiText(playerParticipates ? "开" : "关")));
            SetGmButtonVisual(
                aiecsPlayerParticipatesButton,
                playerParticipates ? GmSelection : GmSurfaceRaised,
                playerParticipates);
        }
        if (aiecsLocalAvoidanceButton != null)
        {
            aiecsLocalAvoidanceButton.interactable = exists;
            TextMeshProUGUI label = aiecsLocalAvoidanceButton.GetComponentInChildren<TextMeshProUGUI>(true);
            SetLocalizedProvider(label, () => FlatWorldLocalizationService.GetUiFormat(
                "局部避让：{0}",
                FlatWorldLocalizationService.GetUiText(localAvoidanceEnabled ? "开" : "关")));
            SetGmButtonVisual(
                aiecsLocalAvoidanceButton,
                localAvoidanceEnabled ? GmSelection : GmSurfaceRaised,
                localAvoidanceEnabled);
        }

        if (aiecsArmiesButton != null)
        {
            if (exists)
                SetLocalizedFormat(aiecsArmiesButton.GetComponentInChildren<TextMeshProUGUI>(true),
                    "两军交战（每次 +{0}）", playground.ReinforcementUnitCount);
            else
                SetButtonLabel(aiecsArmiesButton, "两军交战");
        }

        if (!exists)
        {
            SetLocalizedText(aiecsStatusText, "当前尚未进入可用的正式 AIECS 世界；进入世界后本页会自动连接调试入口。");
            SetLocalizedFormat(aiecsStatisticsText,
                "存活 {0} / 目标 {1} / 游荡 {2} / 追击 {3} / 逃跑 {4} / 攻击 {5}", 0, 0, 0, 0, 0, 0);
            SetLocalizedFormat(aiecsTickText,
                "本 Tick：感知请求 {0}，候选 {1}，LOS {2}，热点格 {3}", 0, 0, 0, 0);
            SetLocalizedFormat(aiecsCumulativeText,
                "累计：玩家受击 {0}（-{1:0.0} HP），武器→ECS 命中 {2}，死亡 {3}，掉落 {4}",
                0, 0f, 0, 0, 0);
            SetLocalizedFormat(aiecsNavigationText,
                "共享目标 {0} / Chunk {1} / 出口图 {2} / 目标图 {3} / 区块路线 {4}", 0, 0, 0, 0, 0);
            return;
        }

        SetLocalizedProvider(aiecsStatusText, () => LocalizeAiecsStatus(playground.Status));
        AiecsDebugSnapshot snapshot = playground.CaptureDebugSnapshot();
        SetLocalizedFormat(aiecsStatisticsText,
            "存活 {0} / 目标 {1} / 游荡 {2} / 追击 {3} / 逃跑 {4} / 攻击 {5}",
            snapshot.Alive, snapshot.Targets, snapshot.Wander, snapshot.Chase, snapshot.Flee, snapshot.Attack);
        SetLocalizedFormat(aiecsTickText,
            "本 Tick：感知请求 {0}，候选 {1}，LOS {2}，热点格 {3}",
            snapshot.PerceptionRequests, snapshot.Candidates, snapshot.Los, snapshot.HotBucket);
        SetLocalizedFormat(aiecsCumulativeText,
            "累计：玩家受击 {0}（-{1:0.0} HP），武器→ECS 命中 {2}，死亡 {3}，掉落 {4}",
            snapshot.PlayerHits, snapshot.PlayerDamage, snapshot.WeaponHits, snapshot.Deaths, snapshot.Drops);
        SetLocalizedFormat(aiecsNavigationText,
            "共享目标 {0} / Chunk {1} / 出口图 {2} / 目标图 {3} / 区块路线 {4}",
            snapshot.SharedGoalCount, snapshot.CachedChunkCount, snapshot.ExitFieldBuilds,
            snapshot.TargetFieldBuilds, snapshot.HighLevelRouteBuilds);
    }

    // 已知状态模板按当前语言显示，异常正文和稳定 ID 保留原值。
    private static string LocalizeAiecsStatus(string status)
    {
        if (string.IsNullOrEmpty(status)) return string.Empty;
        if (status.StartsWith("模拟已停止：", StringComparison.Ordinal))
            return FlatWorldLocalizationService.GetUiFormat("模拟已停止：{0}", status.Substring("模拟已停止：".Length));
        if (status.StartsWith("创建失败：", StringComparison.Ordinal))
            return FlatWorldLocalizationService.GetUiFormat("创建失败：{0}", status.Substring("创建失败：".Length));
        if (TryReadStatusNumber(status, "本场累计生成 ", " 个 ECS 单位", out int spawned))
            return FlatWorldLocalizationService.GetUiFormat(
                "本场累计生成 {0} 个 ECS 单位；当前存活与行为见下方实时统计。", spawned);
        if (TryReadStatusNumber(status, "两军交战：累计生成 ", "；本次增援 ", out int total) &&
            TryReadStatusNumber(status, "；本次增援 ", " / 目标 ", out int added) &&
            TryReadStatusNumber(status, " / 目标 ", "。当前存活见下方统计。", out int target))
        {
            return FlatWorldLocalizationService.GetUiFormat(
                "两军交战：累计生成 {0}；本次增援 {1} / 目标 {2}。当前存活见下方统计。",
                total, added, target);
        }
        const string lootPrefix = "已提交 1 个生物产出验收请求：";
        if (status.StartsWith(lootPrefix, StringComparison.Ordinal))
        {
            int end = status.IndexOf('；', lootPrefix.Length);
            if (end >= lootPrefix.Length)
                return FlatWorldLocalizationService.GetUiFormat(
                    "已提交 1 个生物产出验收请求：{0}；等待真实占格与生成。未修改死亡数量或掉落概率。",
                    status.Substring(lootPrefix.Length, end - lootPrefix.Length));
        }
        return FlatWorldLocalizationService.GetUiText(status);
    }

    private static bool TryReadStatusNumber(string source, string prefix, string suffix, out int value)
    {
        value = 0;
        int start = source.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return false;
        start += prefix.Length;
        int end = source.IndexOf(suffix, start, StringComparison.Ordinal);
        return end > start && int.TryParse(source.Substring(start, end - start), out value);
    }
    /// <summary>正常 GameStart 世界按需创建空闲调试入口；只有场景按钮真正触发后才暂停正式生态并创建开发模拟。</summary>
    private static AiecsPlayground ResolveAiecsPlayground()
    {
        if (AiecsPlayground.Active != null)
            return AiecsPlayground.Active;

        GameManager manager = GameManager.Instance;
        if (manager == null || !manager.IsInGameWorld)
            return null;

        return AiecsEcologyRuntimeHost.Active?.EnsureDebugPlayground();
    }

    private static void SetButtonLabel(Button button, string value)
    {
        if (button == null)
            return;

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            SetLocalizedText(label, value);
    }

    #endregion

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
        TextMeshProUGUI heading = CreateText(parent, title, 18f, GmTextPrimary);
        heading.fontStyle = FontStyles.Bold;
        heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        TextMeshProUGUI paragraph = CreateText(parent, description.Trim(), 12f, GmTextSecondary);
        paragraph.enableWordWrapping = true;
        paragraph.overflowMode = TextOverflowModes.Ellipsis;
        paragraph.gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;
    }

    private static TextMeshProUGUI AddPageHint(Transform parent, string value, float height)
    {
        TextMeshProUGUI hint = CreateText(parent, value, 12f, GmTextSecondary);
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
        background.color = GmSurface;
        Outline outline = row.AddComponent<Outline>();
        StyleGmOutline(outline);

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
            GmTextPrimary);
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
        background.color = GmSurface;
        Outline outline = row.AddComponent<Outline>();
        StyleGmOutline(outline);

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
            GmTextPrimary);
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
            SetLocalizedText(gmSearchSummaryText, "输入名称后跳转");
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
        if (matches.Count > 0)
            SetLocalizedFormat(gmSearchSummaryText, "找到 {0} 项", matches.Count);
        else
            SetLocalizedText(gmSearchSummaryText, "没有匹配项");

        if (matches.Count == 0)
        {
            AddPageHint(gmSearchResultsContent, "没有找到对应功能，请尝试更短的关键词。", 32f);
            return;
        }

        for (int i = 0; i < matches.Count; i++)
        {
            GmSearchEntry entry = matches[i];
            Button resultButton = CreateButton(
                gmSearchResultsContent,
                $"{GetPageLabel(entry.PageId)}  ›  {entry.Label}",
                () => NavigateToSearchEntry(entry),
                0f,
                32f);
            TextMeshProUGUI resultLabel = resultButton.GetComponentInChildren<TextMeshProUGUI>(true);
            SetLocalizedProvider(resultLabel, () => FlatWorldLocalizationService.GetUiFormat(
                "{0}  ›  {1}",
                FlatWorldLocalizationService.GetUiText(GetPageLabel(entry.PageId)),
                FlatWorldLocalizationService.GetUiText(entry.Label)));
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
        SetLocalizedText(gmSearchSummaryText, "输入名称后跳转");
        SetActivePage(entry.PageId);

        // 切页可能重建事件、任务或命令条目，改用最新结果对应的目标。
        GmSearchEntry current = gmSearchEntries.FirstOrDefault(candidate =>
            candidate.PageId == entry.PageId && candidate.Label == entry.Label &&
            candidate.SearchText == entry.SearchText && candidate.Target != null);
        if (current == null)
        {
            SetStatus("搜索目标已更新，请重新搜索。", Color.yellow);
            return;
        }

        if (searchNavigationCoroutine != null)
            StopCoroutine(searchNavigationCoroutine);
        searchNavigationCoroutine = StartCoroutine(ScrollToSearchTarget(current));
    }

    private IEnumerator ScrollToSearchTarget(GmSearchEntry entry)
    {
        yield return null;
        if (entry?.Target == null || activeGmPage != entry.PageId ||
            !gmPages.TryGetValue(entry.PageId, out GmPageView page))
            yield break;

        Canvas.ForceUpdateCanvases();
        ScrollToPageTarget(page, entry.Target);

        Image highlight = entry.Target.GetComponent<Image>();
        if (highlight != null)
        {
            Color original = highlight.color;
            highlight.color = GmSelection;
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
            "已加载 {0} 个事件 · 正在进行 {1} 个",
            13f,
            GmTextPrimary);
        SetLocalizedFormat(countText, "已加载 {0} 个事件 · 正在进行 {1} 个", definitionCount, activeCount);
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
            ? GmSelection
            : GmSurface;
        Outline outline = card.AddComponent<Outline>();
        StyleGmOutline(outline, active);

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
            string.Empty,
            14f,
            active ? GmAccentHover : GmTextPrimary);
        SetLocalizedProvider(title, () => FlatWorldLocalizationService.GetUiFormat(
            "{0}  <color=#80969B>({1})</color>",
            FlatWorldLocalizationService.GetUiText(definition.DisplayName), definition.Id));
        title.fontStyle = FontStyles.Bold;
        title.enableWordWrapping = false;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.gameObject.AddComponent<LayoutElement>().preferredHeight = 23f;

        TextMeshProUGUI description = CreateText(
            info.transform,
            string.Empty,
            12f,
            GmTextSecondary);
        SetLocalizedProvider(description, () => FlatWorldLocalizationService.GetUiText(
            string.IsNullOrWhiteSpace(definition.Description) ? "无事件说明。" : definition.Description));
        description.enableWordWrapping = false;
        description.overflowMode = TextOverflowModes.Ellipsis;
        description.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

        string triggerType = definition.Trigger?.Type ?? "unknown";
        TextMeshProUGUI metadata = CreateText(
            info.transform,
            string.Empty,
            11f,
            GmTextSecondary);
        SetLocalizedProvider(metadata, () =>
        {
            string duration = definition.DurationDays > 0f
                ? FlatWorldLocalizationService.GetUiFormat("{0:0.##} 天", definition.DurationDays)
                : FlatWorldLocalizationService.GetUiText("动作完成即结束");
            return FlatWorldLocalizationService.GetUiFormat(
                "触发器 {0} · 持续 {1} · 动作 {2} 个 · {3}",
                triggerType, duration, definition.Actions.Count,
                FlatWorldLocalizationService.GetUiText(active ? "进行中" : "未运行"));
        });
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
        SetGmButtonVisual(actionButton, active ? GmDanger : GmSurfaceRaised, !active);

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
            SetStatusFormat(
                wasActive ? "已结束事件：{0}" : "已触发事件：{0}",
                new Color(0.35f, 0.95f, 0.85f),
                eventId);
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
        SetStatusFormat(
            result.HasErrors
                ? "事件配置已重载，但发现 {0} 个配置问题，请查看 Console。"
                : "事件配置已重载，共 {0} 个事件。",
            result.HasErrors ? Color.yellow : new Color(0.35f, 0.95f, 0.85f),
            result.HasErrors ? result.Issues.Count : boundGameEventManager.Definitions.Count);
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
