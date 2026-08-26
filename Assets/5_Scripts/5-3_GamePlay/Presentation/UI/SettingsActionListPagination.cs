using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 管理游戏内设置入口列表的“世界”和“保存退出”两张内容页。
/// 五个专项设置入口已经提升为顶部页签按钮，仍由各自 Launcher 打开正式设置页。
/// </summary>
[DisallowMultipleComponent]
public sealed class SettingsActionListPagination : MonoBehaviour
{
    #region 节点命名契约

    public const string WorldPageName = "设置分页_世界";
    public const string SessionPageName = "设置分页_会话";
    public const string TabBarName = "设置分页栏";
    public const string WorldTabButtonName = "设置页签_世界";
    public const string SessionTabButtonName = "设置页签_会话";

    private static readonly string[] PageNames =
    {
        WorldPageName,
        SessionPageName
    };

    private static readonly string[] FirstButtonNames =
    {
        "自动保存",
        "保存游戏"
    };

    private static readonly string[] TabButtonNames =
    {
        WorldTabButtonName,
        SessionTabButtonName
    };

    private static readonly Color ActiveTabColor = new Color(0.16f, 0.40f, 0.42f, 1f);
    private static readonly Color InactiveTabColor = new Color(0.094f, 0.212f, 0.247f, 0.99f);
    private static readonly Color ActiveLabelColor = new Color(0.95f, 0.91f, 0.81f, 1f);
    private static readonly Color InactiveLabelColor = new Color(0.66f, 0.72f, 0.73f, 1f);

    #endregion

    #region 运行时状态

    private readonly Transform[] pages = new Transform[2];
    private readonly Button[] firstButtons = new Button[2];
    private readonly Button[] tabButtons = new Button[2];
    private readonly Image[] tabBackgrounds = new Image[2];
    private readonly TextMeshProUGUI[] tabLabels = new TextMeshProUGUI[2];

    private BasePanel basePanel;
    private RectTransform contentRect;
    private int currentPage;
    private bool configured;

    #endregion

    #region 初始化

    /// <summary>确保设置面板具备分页控制器，并初始化显示第一页。</summary>
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

    private void Configure()
    {
        basePanel = GetComponent<BasePanel>();
        contentRect = FindTransform(transform, "Content") as RectTransform;
        configured = contentRect != null && FindTransform(transform, TabBarName) != null;
        for (int index = 0; index < PageNames.Length; index++)
        {
            pages[index] = FindTransform(transform, PageNames[index]);
            configured &= pages[index] != null;
            firstButtons[index] = pages[index] != null
                ? FindButton(pages[index], FirstButtonNames[index])
                : null;
            configured &= firstButtons[index] != null;

            tabButtons[index] = FindButton(transform, TabButtonNames[index]);
            configured &= tabButtons[index] != null;
            tabBackgrounds[index] = tabButtons[index] != null
                ? tabButtons[index].GetComponent<Image>()
                : null;
            tabLabels[index] = tabButtons[index] != null
                ? tabButtons[index].GetComponentInChildren<TextMeshProUGUI>(true)
                : null;
            configured &= tabBackgrounds[index] != null && tabLabels[index] != null;
        }

        if (!configured)
        {
            Debug.LogError("[SettingsActionListPagination] UI_ActionList Prefab 分页节点不完整。", this);
            return;
        }

        tabButtons[0].onClick.RemoveListener(ShowWorldPage);
        tabButtons[0].onClick.AddListener(ShowWorldPage);
        tabButtons[1].onClick.RemoveListener(ShowSessionPage);
        tabButtons[1].onClick.AddListener(ShowSessionPage);
        ShowPage(0, false);
    }

    #endregion

    #region 分页切换

    private void ShowWorldPage()
    {
        ShowPage(0, true);
    }

    private void ShowSessionPage()
    {
        ShowPage(1, true);
    }

    private void ShowPage(int pageIndex, bool focusFirstButton)
    {
        if (!configured)
            return;

        currentPage = Mathf.Clamp(pageIndex, 0, pages.Length - 1);
        for (int index = 0; index < pages.Length; index++)
        {
            bool active = index == currentPage;
            pages[index].gameObject.SetActive(index == currentPage);
            tabBackgrounds[index].color = active ? ActiveTabColor : InactiveTabColor;
            tabLabels[index].color = active ? ActiveLabelColor : InactiveLabelColor;
        }

        if (contentRect != null)
            LayoutRebuilder.MarkLayoutForRebuild(contentRect);

        // 页面节点没有增删，只刷新现有快照的导航状态，避免翻页时重新扫描整个面板。
        basePanel?.RefreshGamepadNavigationState();
        if (focusFirstButton)
            FocusCurrentPageFirstButton();
    }

    /// <summary>分页改变后把手柄焦点转到新页首项，避免选中已隐藏按钮。</summary>
    private void FocusCurrentPageFirstButton()
    {
        if (basePanel == null || !basePanel.IsOpen() || EventSystem.current == null)
            return;

        Button firstButton = firstButtons[currentPage];
        if (firstButton == null || !firstButton.IsInteractable())
            return;

        EventSystem.current.SetSelectedGameObject(null);
        EventSystem.current.SetSelectedGameObject(firstButton.gameObject);
    }

    #endregion

    #region 清理与查询

    private void OnDestroy()
    {
        if (tabButtons[0] != null)
            tabButtons[0].onClick.RemoveListener(ShowWorldPage);
        if (tabButtons[1] != null)
            tabButtons[1].onClick.RemoveListener(ShowSessionPage);
    }

    private static Transform FindTransform(Transform root, string objectName)
    {
        Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
        for (int index = 0; index < transforms.Length; index++)
        {
            if (transforms[index] != null && transforms[index].name == objectName)
                return transforms[index];
        }

        return null;
    }

    private static Button FindButton(Transform root, string buttonName)
    {
        if (root == null)
            return null;

        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int index = 0; index < buttons.Length; index++)
        {
            if (buttons[index] != null && buttons[index].name == buttonName)
                return buttons[index];
        }

        return null;
    }

    #endregion
}
