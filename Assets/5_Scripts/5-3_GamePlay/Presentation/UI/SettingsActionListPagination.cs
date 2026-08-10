using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 管理游戏内设置入口列表的固定三分页。
/// 视觉节点由 Info_Button_List.prefab 固化，脚本仅切换页面和维护手柄焦点，避免入口继续在 ScrollRect 中纵向溢出。
/// </summary>
[DisallowMultipleComponent]
public sealed class SettingsActionListPagination : MonoBehaviour
{
    #region 节点命名契约

    public const string InterfacePageName = "设置分页_界面";
    public const string WorldPageName = "设置分页_世界";
    public const string SessionPageName = "设置分页_会话";
    public const string PreviousButtonName = "设置上一页按钮";
    public const string NextButtonName = "设置下一页按钮";
    public const string PageTextName = "设置页码文本";

    private static readonly string[] PageNames =
    {
        InterfacePageName,
        WorldPageName,
        SessionPageName
    };

    private static readonly string[] FirstButtonNames =
    {
        "音量调节",
        "自动保存",
        "保存游戏"
    };

    #endregion

    #region 运行时状态

    private readonly Transform[] pages = new Transform[3];
    private readonly Button[] firstButtons = new Button[3];

    private BasePanel basePanel;
    private RectTransform contentRect;
    private Button previousButton;
    private Button nextButton;
    private TextMeshProUGUI pageText;
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
        previousButton = FindButton(transform, PreviousButtonName);
        nextButton = FindButton(transform, NextButtonName);
        pageText = FindText(transform, PageTextName);

        configured = contentRect != null && previousButton != null &&
                     nextButton != null && pageText != null;
        for (int index = 0; index < PageNames.Length; index++)
        {
            pages[index] = FindTransform(transform, PageNames[index]);
            configured &= pages[index] != null;
            firstButtons[index] = pages[index] != null
                ? FindButton(pages[index], FirstButtonNames[index])
                : null;
            configured &= firstButtons[index] != null;
        }

        if (!configured)
        {
            Debug.LogError("[SettingsActionListPagination] Info_Button_List Prefab 分页节点不完整。", this);
            return;
        }

        previousButton.onClick.RemoveListener(ShowPreviousPage);
        previousButton.onClick.AddListener(ShowPreviousPage);
        nextButton.onClick.RemoveListener(ShowNextPage);
        nextButton.onClick.AddListener(ShowNextPage);
        ShowPage(0, false);
    }

    #endregion

    #region 分页切换

    private void ShowPreviousPage()
    {
        ShowPage(currentPage - 1, true);
    }

    private void ShowNextPage()
    {
        ShowPage(currentPage + 1, true);
    }

    private void ShowPage(int pageIndex, bool focusFirstButton)
    {
        if (!configured)
            return;

        currentPage = Mathf.Clamp(pageIndex, 0, pages.Length - 1);
        for (int index = 0; index < pages.Length; index++)
            pages[index].gameObject.SetActive(index == currentPage);

        previousButton.interactable = currentPage > 0;
        nextButton.interactable = currentPage < pages.Length - 1;
        pageText.SetText("PAGE  {0} / {1}", currentPage + 1, pages.Length);

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
        if (previousButton != null)
            previousButton.onClick.RemoveListener(ShowPreviousPage);
        if (nextButton != null)
            nextButton.onClick.RemoveListener(ShowNextPage);
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

    private static TextMeshProUGUI FindText(Transform root, string textName)
    {
        TextMeshProUGUI[] texts = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int index = 0; index < texts.Length; index++)
        {
            if (texts[index] != null && texts[index].name == textName)
                return texts[index];
        }

        return null;
    }

    #endregion
}
