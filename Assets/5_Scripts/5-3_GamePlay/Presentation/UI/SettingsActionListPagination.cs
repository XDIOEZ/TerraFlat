using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>设置页控制器在所属分页显示或隐藏时接收生命周期通知。</summary>
internal interface ISettingsPageLifecycle
{
    /// <summary>页面进入可见状态时刷新权威设置与临时视图。</summary>
    void OnSettingsPageShown();

    /// <summary>页面离开可见状态时清理临时输入与未提交视图。</summary>
    void OnSettingsPageHidden();
}

/// <summary>
/// 管理游戏内设置主面板的七个顶部入口与三个世界设置子页。
/// 所有页面均为 UI_ActionList Prefab 内的现成节点，切换时只改变显隐。
/// </summary>
[DisallowMultipleComponent]
public sealed class SettingsActionListPagination : MonoBehaviour
{
    #region 节点命名契约

    public const string WorldPageName = "设置分页_世界";
    public const string SessionPageName = "设置分页_会话";
    public const string InterfacePageName = "设置分页_UI设置";
    public const string InputBindingPageName = "设置分页_按键绑定";
    public const string DisplayPageName = "设置分页_显示设置";
    public const string CameraPageName = "设置分页_镜头控制";
    public const string AudioPageName = "设置分页_音量调节";
    public const string AutoSavePageName = "设置分页_自动保存";
    public const string WorldStreamingPageName = "设置分页_流送性能";
    public const string DifficultyPageName = "设置分页_游戏难度";
    public const string TabBarName = "设置分页栏";
    public const string WorldTabButtonName = "设置页签_世界";
    public const string SessionTabButtonName = "设置页签_会话";

    private const int WorldPageIndex = 0;

    private static readonly string[] PageNames =
    {
        WorldPageName,
        InterfacePageName,
        InputBindingPageName,
        DisplayPageName,
        CameraPageName,
        AudioPageName,
        SessionPageName,
        AutoSavePageName,
        WorldStreamingPageName,
        DifficultyPageName
    };

    private static readonly string[] TabButtonNames =
    {
        WorldTabButtonName,
        "UI设置",
        "按键绑定",
        "显示设置",
        "镜头控制",
        "音量调节",
        SessionTabButtonName
    };

    private static readonly int[] PageTabIndices =
    {
        0,
        1,
        2,
        3,
        4,
        5,
        6,
        0,
        0,
        0
    };

    private static readonly string[] FirstSelectableNames =
    {
        "自动保存",
        "界面缩放",
        "控制模式下拉列表",
        "世界坐标模式按钮",
        "双指缩放灵敏度",
        "MasterVolume",
        UIText.SaveButton,
        "自动保存间隔下拉列表",
        "性能模式下拉列表",
        "难度_Simple"
    };

    private static readonly Color ActiveTabColor = new Color(0.16f, 0.40f, 0.42f, 1f);
    private static readonly Color InactiveTabColor = new Color(0.094f, 0.212f, 0.247f, 0.99f);
    private static readonly Color ActiveLabelColor = new Color(0.95f, 0.91f, 0.81f, 1f);
    private static readonly Color InactiveLabelColor = new Color(0.66f, 0.72f, 0.73f, 1f);

    #endregion

    #region 运行时状态

    private readonly Transform[] pages = new Transform[PageNames.Length];
    private readonly Selectable[] firstSelectables = new Selectable[PageNames.Length];
    private readonly List<ISettingsPageLifecycle>[] pageLifecycles =
        new List<ISettingsPageLifecycle>[PageNames.Length];
    private readonly Button[] tabButtons = new Button[TabButtonNames.Length];
    private readonly Image[] tabBackgrounds = new Image[TabButtonNames.Length];
    private readonly TextMeshProUGUI[] tabLabels = new TextMeshProUGUI[TabButtonNames.Length];

    private BasePanel basePanel;
    private RectTransform contentRect;
    private int currentPageIndex = -1;
    private bool currentLifecycleActive;
    private bool configured;

    #endregion

    #region 初始化

    /// <summary>确保设置主面板具备内嵌分页控制器，并初始化世界入口页。</summary>
    public static SettingsActionListPagination Ensure(Transform settingsRoot)
    {
        if (settingsRoot == null)
            return null;

        SettingsActionListPagination pager =
            settingsRoot.GetComponent<SettingsActionListPagination>();
        if (pager == null)
            pager = settingsRoot.gameObject.AddComponent<SettingsActionListPagination>();

        pager.Configure();
        return pager;
    }

    /// <summary>解析 Prefab 命名契约并建立顶部页签与世界子页入口。</summary>
    private void Configure()
    {
        UnbindPanelLifecycle();
        basePanel = GetComponent<BasePanel>();
        contentRect = ResolveOuterContent(transform);
        configured = basePanel != null && contentRect != null &&
                     FindTransform(transform, TabBarName) != null;

        for (int index = 0; index < PageNames.Length; index++)
        {
            pages[index] = FindDirectChild(contentRect, PageNames[index]);
            firstSelectables[index] = FindSelectable(pages[index], FirstSelectableNames[index]);
            pageLifecycles[index] ??= new List<ISettingsPageLifecycle>();
            configured &= pages[index] != null && firstSelectables[index] != null;
        }

        Transform tabBar = FindTransform(transform, TabBarName);
        for (int index = 0; index < TabButtonNames.Length; index++)
        {
            tabButtons[index] = FindButton(tabBar, TabButtonNames[index]);
            tabBackgrounds[index] = tabButtons[index] != null
                ? tabButtons[index].GetComponent<Image>()
                : null;
            tabLabels[index] = tabButtons[index] != null
                ? tabButtons[index].GetComponentInChildren<TextMeshProUGUI>(true)
                : null;
            configured &= tabButtons[index] != null &&
                          tabBackgrounds[index] != null &&
                          tabLabels[index] != null;
        }

        if (!configured)
        {
            Debug.LogError(
                "[SettingsActionListPagination] UI_ActionList Prefab 内嵌分页节点不完整。",
                this);
            return;
        }

        BindTabButtons();
        BindWorldDetailButtons();
        BindPanelLifecycle();
        RefreshPageLifecycles();
        ShowPage(WorldPageIndex, false);
    }

    /// <summary>从主滚动区域自身取得 Content，避免命中下拉模板的同名节点。</summary>
    private static RectTransform ResolveOuterContent(Transform settingsRoot)
    {
        Transform scrollTransform = FindTransform(settingsRoot, "Scroll View");
        ScrollRect scrollRect = scrollTransform != null
            ? scrollTransform.GetComponent<ScrollRect>()
            : null;
        return scrollRect != null ? scrollRect.content : null;
    }

    /// <summary>为七个顶部按钮绑定各自的主分页。</summary>
    private void BindTabButtons()
    {
        for (int index = 0; index < tabButtons.Length; index++)
        {
            int pageIndex = index;
            tabButtons[index].onClick.RemoveAllListeners();
            tabButtons[index].onClick.AddListener(() => ShowPage(pageIndex, true));
        }
    }

    /// <summary>把世界入口页的三个设置按钮绑定到同面板子分页。</summary>
    private void BindWorldDetailButtons()
    {
        BindPageButton(pages[WorldPageIndex], "自动保存", AutoSavePageName);
        BindPageButton(pages[WorldPageIndex], "流送性能", WorldStreamingPageName);
        BindPageButton(pages[WorldPageIndex], "游戏难度", DifficultyPageName);
    }

    /// <summary>把一个页面内按钮绑定到目标分页。</summary>
    private void BindPageButton(Transform ownerPage, string buttonName, string targetPageName)
    {
        Button button = FindButton(ownerPage, buttonName);
        int targetIndex = FindPageIndex(targetPageName);
        if (button == null || targetIndex < 0)
        {
            configured = false;
            Debug.LogError(
                $"[SettingsActionListPagination] 缺少分页入口：{buttonName} -> {targetPageName}。",
                this);
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => ShowPage(targetIndex, true));
    }

    /// <summary>订阅主设置面板开关事件，确保页面生命周期与面板可见性一致。</summary>
    private void BindPanelLifecycle()
    {
        if (basePanel == null)
            return;

        basePanel.Opened -= HandlePanelOpened;
        basePanel.Closed -= HandlePanelClosed;
        basePanel.Opened += HandlePanelOpened;
        basePanel.Closed += HandlePanelClosed;
    }

    /// <summary>解除主设置面板开关事件。</summary>
    private void UnbindPanelLifecycle()
    {
        if (basePanel == null)
            return;

        basePanel.Opened -= HandlePanelOpened;
        basePanel.Closed -= HandlePanelClosed;
    }

    #endregion

    #region 页面查询与生命周期

    /// <summary>取得指定设置分页根节点，供页面控制器进行局部绑定。</summary>
    public Transform GetPageRoot(string pageName)
    {
        int pageIndex = FindPageIndex(pageName);
        return pageIndex >= 0 ? pages[pageIndex] : null;
    }

    /// <summary>页面控制器完成挂载后重新收集每页生命周期接收者。</summary>
    public void RefreshPageLifecycles()
    {
        for (int index = 0; index < pages.Length; index++)
        {
            List<ISettingsPageLifecycle> lifecycles = pageLifecycles[index];
            lifecycles.Clear();
            if (pages[index] == null)
                continue;

            MonoBehaviour[] behaviours = pages[index].GetComponentsInChildren<MonoBehaviour>(true);
            for (int behaviourIndex = 0; behaviourIndex < behaviours.Length; behaviourIndex++)
            {
                if (behaviours[behaviourIndex] is ISettingsPageLifecycle lifecycle)
                    lifecycles.Add(lifecycle);
            }
        }

        if (currentLifecycleActive)
            NotifyPageShown(currentPageIndex);
    }

    /// <summary>主面板打开时刷新当前页权威数据。</summary>
    private void HandlePanelOpened()
    {
        if (currentPageIndex < 0)
            return;

        currentLifecycleActive = true;
        NotifyPageShown(currentPageIndex);
    }

    /// <summary>主面板关闭时通知当前页清理临时状态。</summary>
    private void HandlePanelClosed()
    {
        if (!currentLifecycleActive || currentPageIndex < 0)
            return;

        NotifyPageHidden(currentPageIndex);
        currentLifecycleActive = false;
    }

    /// <summary>通知指定页面刷新显示状态。</summary>
    private void NotifyPageShown(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= pageLifecycles.Length)
            return;

        List<ISettingsPageLifecycle> lifecycles = pageLifecycles[pageIndex];
        for (int index = 0; index < lifecycles.Count; index++)
            lifecycles[index]?.OnSettingsPageShown();
    }

    /// <summary>通知指定页面清理隐藏状态。</summary>
    private void NotifyPageHidden(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= pageLifecycles.Length)
            return;

        List<ISettingsPageLifecycle> lifecycles = pageLifecycles[pageIndex];
        for (int index = 0; index < lifecycles.Count; index++)
            lifecycles[index]?.OnSettingsPageHidden();
    }

    #endregion

    #region 分页切换

    /// <summary>返回世界设置入口页，不提交当前子页草稿。</summary>
    public void ShowWorldPage()
    {
        ShowPage(WorldPageIndex, true);
    }

    /// <summary>按固定索引切换页面并同步顶部高亮、布局和手柄焦点。</summary>
    private void ShowPage(int pageIndex, bool focusFirstSelectable)
    {
        if (!configured || pageIndex < 0 || pageIndex >= pages.Length)
            return;

        bool panelOpen = basePanel != null && basePanel.IsOpen();
        if (currentLifecycleActive && currentPageIndex >= 0 && currentPageIndex != pageIndex)
            NotifyPageHidden(currentPageIndex);

        currentPageIndex = pageIndex;
        for (int index = 0; index < pages.Length; index++)
            pages[index].gameObject.SetActive(index == currentPageIndex);

        RefreshTabVisuals(PageTabIndices[currentPageIndex]);
        currentLifecycleActive = panelOpen;
        if (currentLifecycleActive)
            NotifyPageShown(currentPageIndex);

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
        basePanel.RefreshGamepadNavigationState();
        if (focusFirstSelectable)
            FocusCurrentPageFirstSelectable();
    }

    /// <summary>刷新七个顶部页签的选中与未选中颜色。</summary>
    private void RefreshTabVisuals(int activeTabIndex)
    {
        for (int index = 0; index < tabButtons.Length; index++)
        {
            bool active = index == activeTabIndex;
            tabBackgrounds[index].color = active ? ActiveTabColor : InactiveTabColor;
            tabLabels[index].color = active ? ActiveLabelColor : InactiveLabelColor;
        }
    }

    /// <summary>分页改变后把手柄焦点转到新页首项。</summary>
    private void FocusCurrentPageFirstSelectable()
    {
        if (basePanel == null || !basePanel.IsOpen() || EventSystem.current == null)
            return;

        Selectable selectable = firstSelectables[currentPageIndex];
        if (selectable == null || !selectable.IsInteractable())
            return;

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(selectable.gameObject);
    }

    #endregion

    #region 清理与查询

    /// <summary>解除所有运行时按钮和面板事件。</summary>
    private void OnDestroy()
    {
        UnbindPanelLifecycle();
        for (int index = 0; index < tabButtons.Length; index++)
        {
            if (tabButtons[index] != null)
                tabButtons[index].onClick.RemoveAllListeners();
        }
    }

    /// <summary>按稳定页面名取得数组索引。</summary>
    private static int FindPageIndex(string pageName)
    {
        for (int index = 0; index < PageNames.Length; index++)
        {
            if (PageNames[index] == pageName)
                return index;
        }

        return -1;
    }

    /// <summary>只在给定父节点的直属子级中查找页面，避免串入嵌套 Prefab。</summary>
    private static Transform FindDirectChild(Transform parent, string objectName)
    {
        if (parent == null)
            return null;

        for (int index = 0; index < parent.childCount; index++)
        {
            Transform child = parent.GetChild(index);
            if (child != null && child.name == objectName)
                return child;
        }

        return null;
    }

    /// <summary>按节点名查找任意层级 Transform。</summary>
    private static Transform FindTransform(Transform root, string objectName)
    {
        if (root == null)
            return null;

        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            if (transforms[index] != null && transforms[index].name == objectName)
                return transforms[index];
        }

        return null;
    }

    /// <summary>在指定页面内按名称查找按钮。</summary>
    private static Button FindButton(Transform root, string buttonName)
    {
        return FindSelectable(root, buttonName) as Button;
    }

    /// <summary>在指定页面内按名称查找可导航控件。</summary>
    private static Selectable FindSelectable(Transform root, string selectableName)
    {
        if (root == null)
            return null;

        Selectable[] selectables = root.GetComponentsInChildren<Selectable>(true);
        for (int index = 0; index < selectables.Length; index++)
        {
            if (selectables[index] != null && selectables[index].name == selectableName)
                return selectables[index];
        }

        return null;
    }

    #endregion
}
