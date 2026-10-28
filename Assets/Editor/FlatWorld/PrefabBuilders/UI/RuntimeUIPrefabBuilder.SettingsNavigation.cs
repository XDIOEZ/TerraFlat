// 按业务职责维护 Prefab，复用公共控件与布局实现。
using System.IO;
using FlatWorld.Localization;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using static FlatWorldEditorUiLayout;

public static partial class RuntimeUIPrefabBuilder
{
    #region 设置分页与会话导航

    /// <summary>把顶部入口和世界子页固化为同一主面板内的分页。</summary>
    private static void ConfigureSettingsActionListPages(GameObject root)
    {
        RectTransform scrollRect = FindTransform(root.transform, "Scroll View") as RectTransform;
        ScrollRect scroll = scrollRect != null ? scrollRect.GetComponent<ScrollRect>() : null;
        Transform content = scroll != null ? scroll.content : null;
        if (content == null || scrollRect == null || scroll == null)
            throw new MissingReferenceException("UI_ActionList.prefab 缺少 Content 或 Scroll View。");

        SetStretchWithMargins(scrollRect, 54f, 84f, 54f, 174f);
        ConfigureActionListScroll(scrollRect, content);

        Transform worldPage = EnsureActionListPage(
            content,
            SettingsActionListPagination.WorldPageName);
        Transform sessionPage = EnsureActionListPage(
            content,
            SettingsActionListPagination.SessionPageName);

        MoveEntryToPage(root.transform, worldPage, "自动保存");
        MoveEntryToPage(root.transform, worldPage, "流送性能");
        MoveEntryToPage(root.transform, worldPage, "游戏难度");
        MoveEntryToPage(root.transform, worldPage, "联机设置");

        ConfigureSettingsSessionActions(root.transform, sessionPage);

        Transform interfacePage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.InterfacePageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.UISettings + ".prefab");
        Transform inputBindingPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.InputBindingPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.InputBindingSettings + ".prefab");
        Transform displayPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.DisplayPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.CoordinateDisplaySettings + ".prefab");
        Transform cameraPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.CameraPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.CameraControlSettings + ".prefab");
        Transform audioPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.AudioPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.AudioSettings + ".prefab");
        Transform visualEffectsPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.VisualEffectsPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.VisualEffectsSettings + ".prefab");
        Transform debugPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.DebugPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.DebugSettings + ".prefab");
        Transform autoSavePage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.AutoSavePageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.AutoSaveSettings + ".prefab");
        Transform streamingPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.WorldStreamingPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.WorldStreamingSettings + ".prefab");
        Transform difficultyPage = EnsureEmbeddedActionListPage(
            content,
            SettingsActionListPagination.DifficultyPageName,
            SettingsPanelsRoot + RuntimeUIPrefabKeys.DifficultySettings + ".prefab");

        worldPage.SetSiblingIndex(0);
        interfacePage.SetSiblingIndex(1);
        inputBindingPage.SetSiblingIndex(2);
        displayPage.SetSiblingIndex(3);
        cameraPage.SetSiblingIndex(4);
        audioPage.SetSiblingIndex(5);
        visualEffectsPage.SetSiblingIndex(6);
        debugPage.SetSiblingIndex(7);
        sessionPage.SetSiblingIndex(8);
        autoSavePage.SetSiblingIndex(9);
        streamingPage.SetSiblingIndex(10);
        difficultyPage.SetSiblingIndex(11);
        worldPage.gameObject.SetActive(true);
        sessionPage.gameObject.SetActive(false);
        EnsureActionListTabBar(root.transform);
        RemoveObsoleteActionListPagerControls(root.transform);
        EnsureSeasonSettingsPage(root);
        EnsureGameSettingsPage(root);
        Transform graphicsPage = ConfigureGraphicsSettingsPage(content);
        graphicsPage.gameObject.SetActive(false);
        Transform languagePage = ConfigureLanguageSettingsPage(content);
        languagePage.gameObject.SetActive(false);
        EnsureActionListTabButton(
            root.transform,
            FindTransform(root.transform, SettingsActionListPagination.TabBarName),
            SettingsActionListPagination.GraphicsTabButtonName,
            "画质设置");
        ConfigureGroupedSettingsNavigation(root);
    }

    /// <summary>主菜单和游戏内设置共用六档整体画质入口。</summary>
    private static Transform ConfigureGraphicsSettingsPage(Transform content)
    {
        Transform graphicsPage = EnsureActionListPage(content, SettingsActionListPagination.GraphicsPageName);
        ClearDirectChildren(graphicsPage);
        CreateSettingsHeader(graphicsPage, "画质设置");
        CreateSettingsHint(graphicsPage, "选择预设会同时调整水体、阴影和后处理，之后仍可单独调整。", 60f);
        TMP_Dropdown preset = CreateSettingsDropdownRow(
            graphicsPage,
            "画质预设",
            GameManager.MainMenuSettingsQualityPresetKey,
            new[] { "极低", "低", "中", "高", "极高", "极致" });
        preset.SetValueWithoutNotify(3);
        preset.RefreshShownValue();
        CreateSettingsDropdownRow(
            graphicsPage,
            "特效质量",
            GameManager.MainMenuSettingsEffectsQualityKey,
            new[] { "高", "中", "低" });
        return graphicsPage;
    }

    /// <summary>主菜单与游戏内共用同一语言设置页和下拉控件。</summary>
    private static Transform ConfigureLanguageSettingsPage(Transform content)
    {
        Transform languagePage = EnsureActionListPage(
            content,
            SettingsActionListPagination.LanguagePageName);
        ClearDirectChildren(languagePage);
        CreateSettingsHeader(languagePage, "语言");
        CreateSettingsHint(languagePage, "界面语言会立即切换，点击保存后保留修改。", 48f);
        CreateSettingsDropdownRow(
            languagePage,
            "游戏语言",
            GameManager.MainMenuSettingsLanguageDropdownKey,
            new[] { "简体中文", "English" });
        TextMeshProUGUI languageStatus = CreateText(
            GameManager.MainMenuSettingsLanguageStatusTextKey,
            languagePage,
            string.Empty,
            16f,
            Muted);
        languageStatus.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
        return languagePage;
    }

    /// <summary>
    /// 把游戏内设置结构裁成主菜单版本：保留纯客户端偏好，并补入画质与语言页；
    /// 世界、存档会话、自动保存、流送、难度和季节等依赖世界实例的页面从 Prefab 中物理移除。
    /// </summary>
    private static void ConfigureMainMenuSettingsActionList(GameObject root)
    {
        root.name = RuntimeUIPrefabKeys.MainMenuSettings;

        // 外层面板才有 BasePanel 开关契约；内嵌音量分页仅由 SetActive 切换，不独立挂面板动画。
        BasePanel panel = root.GetComponent<BasePanel>();
        ConfigureScaleAnimation(panel);
        CanvasGroup animationGroup = root.GetComponent<CanvasGroup>();
        animationGroup.alpha = 0f;
        animationGroup.interactable = false;
        animationGroup.blocksRaycasts = false;

        ScrollRect scroll = FindTransform(root.transform, "Scroll View")?.GetComponent<ScrollRect>();
        Transform content = scroll != null ? scroll.content : null;
        Transform tabBar = FindTransform(root.transform, SettingsActionListPagination.TabBarName);
        if (content == null || tabBar == null)
            throw new MissingReferenceException("主菜单设置基线缺少 Scroll View/Content 或设置分页栏。");

        string[] removedPages =
        {
            SettingsActionListPagination.WorldPageName,
            SettingsActionListPagination.SessionPageName,
            SettingsActionListPagination.AutoSavePageName,
            SettingsActionListPagination.WorldStreamingPageName,
            SettingsActionListPagination.DifficultyPageName,
            SeasonSettingsPanel.PageName
        };
        for (int index = 0; index < removedPages.Length; index++)
            DestroyDirectChild(content, removedPages[index]);

        DestroyTransformIfPresent(root.transform, SettingsExitConfirmationController.LayerName);
        DestroyTransformIfPresent(tabBar, SettingsActionListPagination.WorldTabButtonName);
        DestroyTransformIfPresent(tabBar, SettingsActionListPagination.SessionTabButtonName);

        Transform graphicsPage = ConfigureGraphicsSettingsPage(content);

        Transform languagePage = ConfigureLanguageSettingsPage(content);

        Transform[] orderedPages =
        {
            FindDirectChild(content, SettingsActionListPagination.GamePageName),
            FindDirectChild(content, SettingsActionListPagination.InterfacePageName),
            FindDirectChild(content, SettingsActionListPagination.InputBindingPageName),
            FindDirectChild(content, SettingsActionListPagination.DisplayPageName),
            FindDirectChild(content, SettingsActionListPagination.CameraPageName),
            FindDirectChild(content, SettingsActionListPagination.AudioPageName),
            FindDirectChild(content, SettingsActionListPagination.VisualEffectsPageName),
            FindDirectChild(content, SettingsActionListPagination.DebugPageName),
            graphicsPage,
            languagePage
        };
        for (int index = 0; index < orderedPages.Length; index++)
        {
            if (orderedPages[index] == null)
                throw new MissingReferenceException($"主菜单设置缺少客户端分页，索引={index}。");

            orderedPages[index].SetSiblingIndex(index);
            orderedPages[index].gameObject.SetActive(
                orderedPages[index].name == SettingsActionListPagination.InterfacePageName);
        }

        Button graphicsTab = EnsureActionListTabButton(
            root.transform,
            tabBar,
            SettingsActionListPagination.GraphicsTabButtonName,
            "画质设置");
        Button languageTab = EnsureActionListTabButton(
            root.transform,
            tabBar,
            SettingsActionListPagination.LanguageTabButtonName,
            "语言");
        Button debugTab = EnsureActionListTabButton(
            root.transform,
            tabBar,
            SettingsActionListPagination.DebugTabButtonName,
            "调试");
        Button[] orderedTabs =
        {
            FindTransform(tabBar, SettingsActionListPagination.GameTabButtonName)?.GetComponent<Button>(),
            FindTransform(root.transform, "UI设置")?.GetComponent<Button>(),
            FindTransform(root.transform, "按键绑定")?.GetComponent<Button>(),
            FindTransform(root.transform, "显示设置")?.GetComponent<Button>(),
            FindTransform(root.transform, "镜头控制")?.GetComponent<Button>(),
            FindTransform(root.transform, "音量调节")?.GetComponent<Button>(),
            FindTransform(root.transform, "视觉特效")?.GetComponent<Button>(),
            debugTab,
            graphicsTab,
            languageTab
        };
        for (int index = 0; index < orderedTabs.Length; index++)
        {
            if (orderedTabs[index] == null)
                throw new MissingReferenceException($"主菜单设置缺少客户端页签，索引={index}。");

            orderedTabs[index].transform.SetSiblingIndex(index);
            orderedTabs[index].GetComponent<Image>().color = SurfaceRaised;
        }

        TextMeshProUGUI title = FindTransform(root.transform, "FWUI_标题")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI eyebrow = FindTransform(root.transform, "FWUI_眉题")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI footerHint = FindTransform(root.transform, "FWUI_FooterHint")?.GetComponent<TextMeshProUGUI>();
        if (title != null)
            title.text = "设置";
        if (eyebrow != null)
        {
            eyebrow.text = string.Empty;
            eyebrow.gameObject.SetActive(false);
        }
        if (footerHint != null)
            footerHint.text = "未保存修改将在关闭时撤销";

        DestroyTransformIfPresent(root.transform, "恢复所有设置");
        Button resetButton = CreateButton(
            "恢复所有设置",
            root.transform,
            "恢复所有设置",
            190f,
            60f,
            false);
        RectTransform resetRect = resetButton.GetComponent<RectTransform>();
        resetRect.anchorMin = new Vector2(1f, 0f);
        resetRect.anchorMax = new Vector2(1f, 0f);
        resetRect.pivot = new Vector2(1f, 0f);
        resetRect.anchoredPosition = new Vector2(-168f, 8f);
        resetRect.sizeDelta = new Vector2(190f, 60f);
        LayoutElement resetLayout = resetButton.GetComponent<LayoutElement>();
        if (resetLayout != null)
            resetLayout.ignoreLayout = true;
        SetButtonLabelSize(resetButton, 17f);
        resetButton.transform.SetAsLastSibling();

        // 保存按钮由正式 Prefab 持有，重建时保留设置会话需要的稳定节点名。
        DestroyTransformIfPresent(root.transform, SettingsEditSessionController.SaveButtonName);
        Button saveButton = CreateButton(
            SettingsEditSessionController.SaveButtonName,
            root.transform,
            "保存",
            132f,
            60f,
            true);
        RectTransform saveRect = saveButton.GetComponent<RectTransform>();
        saveRect.anchorMin = new Vector2(1f, 0f);
        saveRect.anchorMax = new Vector2(1f, 0f);
        saveRect.pivot = new Vector2(1f, 0f);
        saveRect.anchoredPosition = new Vector2(-24f, 8f);
        saveRect.sizeDelta = new Vector2(132f, 60f);
        LayoutElement saveLayout = saveButton.GetComponent<LayoutElement>();
        if (saveLayout != null)
            saveLayout.ignoreLayout = true;
        SetButtonLabelSize(saveButton, 17f);
        saveButton.transform.SetAsLastSibling();
        if (footerHint != null)
        {
            RectTransform hintRect = footerHint.rectTransform;
            hintRect.offsetMax = new Vector2(-382f, hintRect.offsetMax.y);
        }

        Transform close = FindTransform(root.transform, GameManager.MainMenuSettingsCloseButtonKey);
        close?.SetAsLastSibling();
        ConfigureGroupedSettingsNavigation(root);
    }

    /// <summary>相近设置通过页内入口访问，顶部只保留常用大类。</summary>
    private static void ConfigureGroupedSettingsNavigation(GameObject root)
    {
        BasePanel panel = root.GetComponent<BasePanel>();
        if (panel != null)
            panel.CanDrag = false;
        foreach (UI_Drag drag in root.GetComponentsInChildren<UI_Drag>(true))
            drag.enabled = false;

        Transform tabs = FindTransform(root.transform, SettingsActionListPagination.TabBarName);
        string[] hidden = { "显示设置", "镜头控制", "视觉特效", SettingsActionListPagination.LanguageTabButtonName };
        foreach (string name in hidden)
            FindTransform(tabs, name)?.gameObject.SetActive(false);

        Transform graphics = FindTransform(root.transform, SettingsActionListPagination.GraphicsPageName);
        MoveEntryToPage(root.transform, graphics, "打开视觉特效");
        MoveEntryToPage(root.transform, graphics, "打开显示设置");
        SetButtonCaption(FindTransform(graphics, "打开视觉特效")?.GetComponent<Button>(), "视觉特效");
        SetButtonCaption(FindTransform(graphics, "打开显示设置")?.GetComponent<Button>(), "显示设置");

        Transform input = FindTransform(root.transform, SettingsActionListPagination.InputBindingPageName);
        Transform inputOptions = FindTransform(input, "操作选项");
        if (inputOptions != null)
        {
            MoveEntryToPage(root.transform, inputOptions, "打开镜头控制");
            SetButtonCaption(FindTransform(inputOptions, "打开镜头控制")?.GetComponent<Button>(), "镜头控制");
        }

        Transform language = FindTransform(root.transform, SettingsActionListPagination.LanguagePageName);
        Transform ui = FindTransform(root.transform, SettingsActionListPagination.InterfacePageName);
        ScrollRect uiScroll = ui != null ? ui.GetComponentInChildren<ScrollRect>(true) : null;
        if (language != null && uiScroll?.content != null)
        {
            MoveEntryToPage(root.transform, uiScroll.content, "打开语言设置");
            SetButtonCaption(FindTransform(uiScroll.content, "打开语言设置")?.GetComponent<Button>(), "语言");
        }
    }

    private static void SetButtonCaption(Button button, string caption)
    {
        TextMeshProUGUI label = button != null ? button.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        if (label != null)
            label.text = caption;
    }

    /// <summary>删除指定父节点的直属子节点。</summary>
    private static void DestroyDirectChild(Transform parent, string childName)
    {
        Transform child = FindDirectChild(parent, childName);
        if (child != null)
            Object.DestroyImmediate(child.gameObject);
    }

    /// <summary>按名称删除一个现有节点。</summary>
    private static void DestroyTransformIfPresent(Transform root, string objectName)
    {
        Transform target = FindTransform(root, objectName);
        if (target != null)
            Object.DestroyImmediate(target.gameObject);
    }

    /// <summary>清空页面内容但保留页面自身布局组件。</summary>
    private static void ClearDirectChildren(Transform parent)
    {
        for (int index = parent.childCount - 1; index >= 0; index--)
            Object.DestroyImmediate(parent.GetChild(index).gameObject);
    }

    /// <summary>定位正式会话分页并仅刷新该分页及其共用确认层。</summary>
    private static void ConfigureSettingsSessionActions(GameObject root)
    {
        RectTransform scrollRect = FindTransform(root.transform, "Scroll View") as RectTransform;
        ScrollRect scroll = scrollRect != null ? scrollRect.GetComponent<ScrollRect>() : null;
        Transform content = scroll != null ? scroll.content : null;
        if (content == null)
            throw new MissingReferenceException("UI_ActionList.prefab 缺少 Content 或 Scroll View。");

        Transform sessionPage = EnsureActionListPage(
            content,
            SettingsActionListPagination.SessionPageName);
        ConfigureSettingsSessionActions(root.transform, sessionPage);
    }

    /// <summary>把会话页固化为三个稳定入口，并重建唯一保存退出确认层。</summary>
    private static void ConfigureSettingsSessionActions(Transform root, Transform sessionPage)
    {
        RemoveObsoleteSessionExitEntries(root);
        MoveEntryToPage(root, sessionPage, UIText.SaveButton);
        MoveEntryToPage(root, sessionPage, UIText.ReturnToMainMenuButton);
        MoveEntryToPage(root, sessionPage, UIText.ReturnToDesktopButton);
        MoveEntryToPage(root, sessionPage, "恢复所有设置");
        FindDirectChild(sessionPage, UIText.SaveButton).SetSiblingIndex(0);
        FindDirectChild(sessionPage, UIText.ReturnToMainMenuButton).SetSiblingIndex(1);
        FindDirectChild(sessionPage, UIText.ReturnToDesktopButton).SetSiblingIndex(2);
        FindDirectChild(sessionPage, "恢复所有设置").SetSiblingIndex(3);
        RebuildSettingsExitConfirmation(root);
    }

    /// <summary>设置分页列表的视口与 Content，彻底移除旧的纵向滚动布局所有权。</summary>
    private static void ConfigureActionListScroll(RectTransform scrollRect, Transform content)
    {
        ScrollRect scroll = scrollRect.GetComponent<ScrollRect>();
        if (scroll != null)
        {
            scroll.horizontal = false;
            scroll.vertical = false;
            scroll.inertia = false;
            scroll.verticalScrollbar = null;
        }

        Transform scrollbar = FindTransform(scrollRect, "Scrollbar Vertical");
        if (scrollbar != null)
            scrollbar.gameObject.SetActive(false);

        GridLayoutGroup grid = content.GetComponent<GridLayoutGroup>();
        if (grid != null)
            Object.DestroyImmediate(grid);
        VerticalLayoutGroup verticalLayout = content.GetComponent<VerticalLayoutGroup>();
        if (verticalLayout != null)
            Object.DestroyImmediate(verticalLayout);
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            Object.DestroyImmediate(fitter);

        RectTransform contentRect = content as RectTransform;
        if (contentRect == null)
            throw new MissingComponentException("UI_ActionList.prefab 的 Content 缺少 RectTransform。");
        Stretch(contentRect);
    }

    /// <summary>创建或更新可铺满视口的单个分页容器。</summary>
    private static Transform EnsureActionListPage(Transform content, string pageName)
    {
        Transform page = FindDirectChild(content, pageName);
        if (page == null)
            page = CreateUIObject(pageName, content).transform;

        RectTransform pageRect = page as RectTransform;
        Stretch(pageRect);

        GridLayoutGroup grid = page.GetComponent<GridLayoutGroup>();
        if (grid != null)
            Object.DestroyImmediate(grid);
        ContentSizeFitter fitter = page.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            Object.DestroyImmediate(fitter);

        VerticalLayoutGroup layout = page.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
            layout = page.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return page;
    }

    /// <summary>创建无布局所有权的分页容器，并在其中保留指定页面的嵌套 Prefab 实例。</summary>
    private static Transform EnsureEmbeddedActionListPage(
        Transform content,
        string pageName,
        string prefabPath)
    {
        GameObject pagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (pagePrefab == null)
            throw new FileNotFoundException($"找不到内嵌设置页 Prefab：{prefabPath}", prefabPath);

        Transform page = FindDirectChild(content, pageName);
        if (page == null)
            page = CreateUIObject(pageName, content).transform;

        Stretch(page as RectTransform);
        GridLayoutGroup grid = page.GetComponent<GridLayoutGroup>();
        if (grid != null)
            Object.DestroyImmediate(grid);
        VerticalLayoutGroup vertical = page.GetComponent<VerticalLayoutGroup>();
        if (vertical != null)
            Object.DestroyImmediate(vertical);
        ContentSizeFitter fitter = page.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            Object.DestroyImmediate(fitter);

        GameObject embedded = null;
        for (int index = page.childCount - 1; index >= 0; index--)
        {
            GameObject child = page.GetChild(index).gameObject;
            Object source = PrefabUtility.GetCorrespondingObjectFromSource(child);
            if (embedded == null && source == pagePrefab)
            {
                embedded = child;
                continue;
            }

            Object.DestroyImmediate(child);
        }

        if (embedded == null)
            embedded = PrefabUtility.InstantiatePrefab(pagePrefab, page) as GameObject;
        if (embedded == null)
            throw new MissingReferenceException($"无法嵌套设置页 Prefab：{prefabPath}");

        embedded.name = pagePrefab.name;
        Stretch(embedded.GetComponent<RectTransform>());
        page.gameObject.SetActive(false);
        return page;
    }

    /// <summary>复用既有业务按钮或创建新入口，并将其移动到指定页面。</summary>
    private static void MoveEntryToPage(Transform root, Transform page, string entryName)
    {
        Transform entry = FindTransform(root, entryName);
        Button button = entry != null ? entry.GetComponent<Button>() : null;
        if (button == null)
            button = CreateButton(entryName, page, entryName, 360f, 64f, false);

        button.gameObject.name = entryName;
        button.transform.SetParent(page, false);
        button.gameObject.SetActive(true);

        LayoutElement element = button.GetComponent<LayoutElement>() ??
                                button.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = -1f;
        element.preferredHeight = 64f;
        element.flexibleWidth = 1f;
        button.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, 64f);
        ConfigureButtonVisual(button, false, entryName);
        SetButtonLabelSize(button, 18f);
    }

    /// <summary>删除旧会话页的组合式保存/退出入口，新 Prefab 只保留三个单一职责按钮。</summary>
    private static void RemoveObsoleteSessionExitEntries(Transform root)
    {
        string[] obsoleteNames =
        {
            "保存游戏",
            "保存并回到主界面按钮",
            "保存并退出游戏按钮",
            "不保存直接退出"
        };

        for (int index = 0; index < obsoleteNames.Length; index++)
        {
            Transform obsolete = FindTransform(root, obsoleteNames[index]);
            if (obsolete != null)
                Object.DestroyImmediate(obsolete.gameObject);
        }
    }

    /// <summary>
    /// 重建会话页共用的保存退出确认层；340 高度的内容预算为上下边距 56 + 提示 150 + 间距 18 + 按钮区 72 = 296。
    /// 全屏层保持完全透明，只负责阻断鼠标射线与底层分页焦点，不遮暗背景。
    /// </summary>
    private static void RebuildSettingsExitConfirmation(Transform root)
    {
        Transform existing = FindTransform(
            root,
            SettingsExitConfirmationController.LayerName);
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        GameObject layer = CreateUIObject(
            SettingsExitConfirmationController.LayerName,
            root,
            typeof(CanvasRenderer),
            typeof(Image));
        Stretch(layer.GetComponent<RectTransform>());
        Image blocker = layer.GetComponent<Image>();
        blocker.color = Color.clear;
        blocker.raycastTarget = true;

        GameObject dialog = CreateUIObject(
            SettingsExitConfirmationController.DialogName,
            layer.transform,
            typeof(CanvasRenderer),
            typeof(Image));
        SetCentered(
            dialog.GetComponent<RectTransform>(),
            Vector2.zero,
            new Vector2(720f, 340f));
        Image dialogBackground = dialog.GetComponent<Image>();
        dialogBackground.color = Canvas;
        dialogBackground.raycastTarget = true;
        AddOutline(dialogBackground, Amber);
        Shadow shadow = dialog.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.62f);
        shadow.effectDistance = new Vector2(8f, -8f);

        VerticalLayoutGroup dialogLayout = dialog.AddComponent<VerticalLayoutGroup>();
        dialogLayout.padding = new RectOffset(36, 36, 28, 28);
        dialogLayout.spacing = 18f;
        dialogLayout.childAlignment = TextAnchor.MiddleCenter;
        dialogLayout.childControlWidth = true;
        dialogLayout.childControlHeight = true;
        dialogLayout.childForceExpandWidth = true;
        dialogLayout.childForceExpandHeight = false;

        TextMeshProUGUI prompt = CreateText(
            SettingsExitConfirmationController.PromptName,
            dialog.transform,
            "是否保存再退出",
            30f,
            Cream);
        prompt.alignment = TextAlignmentOptions.Center;
        prompt.enableWordWrapping = true;
        prompt.enableAutoSizing = true;
        prompt.fontSizeMin = FlatWorldUITextLayout.MinimumFontSize(30f);
        prompt.fontSizeMax = 30f;
        prompt.gameObject.AddComponent<LayoutElement>().preferredHeight = 150f;

        GameObject actions = CreateUIObject("确认操作区", dialog.transform);
        actions.AddComponent<LayoutElement>().preferredHeight = 72f;
        HorizontalLayoutGroup actionLayout = actions.AddComponent<HorizontalLayoutGroup>();
        actionLayout.spacing = 28f;
        actionLayout.childAlignment = TextAnchor.MiddleCenter;
        actionLayout.childControlWidth = false;
        actionLayout.childControlHeight = true;
        actionLayout.childForceExpandWidth = false;
        actionLayout.childForceExpandHeight = false;

        Button cancelButton = CreateButton(
            SettingsExitConfirmationController.CancelButtonName,
            actions.transform,
            "不保存直接退出",
            180f,
            64f,
            false);
        cancelButton.GetComponent<Image>().color = new Color(0.30f, 0.33f, 0.35f, 1f);
        SetButtonLabelSize(cancelButton, 22f);

        Button confirmButton = CreateButton(
            SettingsExitConfirmationController.ConfirmButtonName,
            actions.transform,
            "保存并退出",
            180f,
            64f,
            true);
        confirmButton.GetComponent<Image>().color = new Color(0.91f, 0.68f, 0.18f, 1f);
        SetButtonLabelSize(confirmButton, 22f);

        cancelButton.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnRight = confirmButton
        };
        confirmButton.navigation = new Navigation
        {
            mode = Navigation.Mode.Explicit,
            selectOnLeft = cancelButton
        };

        layer.transform.SetAsLastSibling();
        layer.SetActive(false);
    }

    /// <summary>创建类似浏览器页签的顶部横向分页栏。</summary>
    private static void EnsureActionListTabBar(Transform root)
    {
        Transform tabBar = FindTransform(root, SettingsActionListPagination.TabBarName);
        if (tabBar == null)
            tabBar = CreateUIObject(
                SettingsActionListPagination.TabBarName,
                root,
                typeof(Image)).transform;

        RectTransform tabBarRect = tabBar as RectTransform;
        SetTopStretch(tabBarRect, 54f, 54f, 90f, 68f);

        Image background = tabBar.GetComponent<Image>() ?? tabBar.gameObject.AddComponent<Image>();
        background.color = Surface;
        background.raycastTarget = false;
        AddOutline(background, Border);

        HorizontalLayoutGroup layout = tabBar.GetComponent<HorizontalLayoutGroup>() ??
                                       tabBar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(4, 4, 4, 4);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        Button audioTab = EnsureActionListTabButton(root, tabBar, "音量调节", "音量调节");
        Button uiTab = EnsureActionListTabButton(root, tabBar, "UI设置", "UI设置");
        Button displayTab = EnsureActionListTabButton(root, tabBar, "显示设置", "显示设置");
        Button bindingTab = EnsureActionListTabButton(root, tabBar, "按键绑定", "按键绑定");
        Button cameraTab = EnsureActionListTabButton(root, tabBar, "镜头控制", "镜头控制");
        Button visualEffectsTab = EnsureActionListTabButton(root, tabBar, "视觉特效", "视觉特效");
        Button debugTab = EnsureActionListTabButton(
            root,
            tabBar,
            SettingsActionListPagination.DebugTabButtonName,
            "调试");
        Button worldTab = EnsureActionListTabButton(
            root,
            tabBar,
            SettingsActionListPagination.WorldTabButtonName,
            "世界");
        Button sessionTab = EnsureActionListTabButton(
            root,
            tabBar,
            SettingsActionListPagination.SessionTabButtonName,
            "保存与退出");

        Button[] orderedTabs =
        {
            worldTab,
            uiTab,
            bindingTab,
            displayTab,
            cameraTab,
            audioTab,
            visualEffectsTab,
            debugTab,
            sessionTab
        };
        for (int index = 0; index < orderedTabs.Length; index++)
        {
            orderedTabs[index].transform.SetSiblingIndex(index);
            orderedTabs[index].GetComponent<Image>().color = SurfaceRaised;
        }

        worldTab.GetComponent<Image>().color = new Color(0.16f, 0.40f, 0.42f, 1f);
        sessionTab.GetComponent<Image>().color = SurfaceRaised;
        tabBar.SetAsLastSibling();
    }

    /// <summary>复用或创建单个分页页签，并交给横向布局分配宽度。</summary>
    private static Button EnsureActionListTabButton(
        Transform root,
        Transform tabBar,
        string buttonName,
        string caption)
    {
        Button button = FindTransform(root, buttonName)?.GetComponent<Button>();
        if (button == null)
            button = CreateButton(buttonName, tabBar, caption, 120f, 60f, false);

        button.gameObject.name = buttonName;
        button.gameObject.SetActive(true);
        button.transform.SetParent(tabBar, false);
        ConfigureButtonVisual(button, false, caption);
        LayoutElement element = button.GetComponent<LayoutElement>() ??
                                button.gameObject.AddComponent<LayoutElement>();
        element.preferredWidth = 120f;
        element.preferredHeight = 60f;
        element.flexibleWidth = 1f;
        SetButtonLabelSize(button, 16f);
        return button;
    }

    /// <summary>移除旧界面总页及左右翻页节点，确保 Prefab 只保留新的页签导航。</summary>
    private static void RemoveObsoleteActionListPagerControls(Transform root)
    {
        string[] obsoleteNames =
        {
            "设置上一页按钮",
            "设置页码文本",
            "设置下一页按钮",
            "设置页签_界面",
            "设置分页_界面"
        };

        for (int index = 0; index < obsoleteNames.Length; index++)
        {
            Transform obsolete = FindTransform(root, obsoleteNames[index]);
            if (obsolete != null)
                Object.DestroyImmediate(obsolete.gameObject);
        }
    }

    #endregion
}
