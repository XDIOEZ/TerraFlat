using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 为本地玩家维护屏幕左侧中部的 Buff 提示栏。
/// 只实例化 UI_BuffStatus 与 UI_BuffStatusItem Prefab；通过 BuffManager 生命周期事件和 0.25 秒兜底刷新，
/// 展示玩家当前拥有的 Buff、占位图标及剩余时间。面板默认不拦截输入，没有 Buff 时整体隐藏。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
public sealed class PlayerBuffStatusHUD : MonoBehaviour
{
    #region 常量与运行时状态

    public const string ViewName = "PlayerBuffStatusHUD";

    private const string TitleNodeName = "标题";
    private const string CountNodeName = "数量文本";
    private const string EmptyNodeName = "空状态文本";
    private const string ContentNodeName = "Content";
    private const float RefreshInterval = 0.25f;

    private Player player;
    private BuffManager buffManager;
    private GameObject viewObject;
    private GameObject itemPrefab;
    private RectTransform viewRect;
    private RectTransform contentRect;
    private CanvasGroup viewCanvasGroup;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI countText;
    private TextMeshProUGUI emptyText;
    private bool refreshRequested = true;
    private float nextRefreshTime;
    private bool missingPrefabLogged;

    private readonly List<BuffStatusRowView> rowViews = new(8);
    private readonly List<BuffInstance> buffSnapshot = new(8);

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        ResolvePlayer();
    }

    private void OnEnable()
    {
        ResolvePlayer();
        if (player != null)
            player.ProfileContextChanged += HandleProfileContextChanged;

        refreshRequested = true;
    }

    /// <summary>在玩家模块完成加载后补绑定 BuffManager，并按低频间隔刷新剩余时间。</summary>
    private void LateUpdate()
    {
        ResolvePlayer();
        if (player == null || !player.IsLocalProfile)
        {
            UnbindBuffManager();
            SetViewActive(false);
            return;
        }

        if (!TryBindBuffManager())
        {
            SetViewActive(false);
            return;
        }

        if (refreshRequested || Time.unscaledTime >= nextRefreshTime)
            RefreshNow();
    }

    private void OnDisable()
    {
        if (player != null)
            player.ProfileContextChanged -= HandleProfileContextChanged;

        UnbindBuffManager();
        SetViewActive(false);
    }

    private void OnDestroy()
    {
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        if (viewObject != null)
            Destroy(viewObject);
    }

    #endregion

    #region 数据绑定与刷新

    private bool TryBindBuffManager()
    {
        BuffManager candidate = player != null
            ? player.GetComponentInChildren<BuffManager>(true)
            : null;
        if (candidate == buffManager)
            return candidate != null;

        UnbindBuffManager();
        buffManager = candidate;
        if (buffManager == null)
            return false;

        buffManager.BuffAdded += HandleBuffChanged;
        buffManager.BuffRemoved += HandleBuffChanged;
        buffManager.BuffDurationChanged += HandleBuffChanged;
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
        refreshRequested = true;
        return true;
    }

    private void UnbindBuffManager()
    {
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        if (buffManager == null)
            return;

        buffManager.BuffAdded -= HandleBuffChanged;
        buffManager.BuffRemoved -= HandleBuffChanged;
        buffManager.BuffDurationChanged -= HandleBuffChanged;
        buffManager = null;
    }

    private void HandleBuffChanged(BuffInstance _)
    {
        refreshRequested = true;
        nextRefreshTime = Time.unscaledTime;
    }

    private void HandleLanguageChanged(string _)
    {
        refreshRequested = true;
        nextRefreshTime = Time.unscaledTime;
    }

    /// <summary>读取 ActiveBuffs 快照，复用行 Prefab，完成后强制重建 Content 布局。</summary>
    private void RefreshNow()
    {
        refreshRequested = false;
        nextRefreshTime = Time.unscaledTime + RefreshInterval;

        if (!EnsureView() || buffManager == null)
        {
            SetViewActive(false);
            return;
        }

        RefreshStaticTexts();
        buffSnapshot.Clear();
        foreach (KeyValuePair<string, BuffInstance> pair in buffManager.ActiveBuffs)
        {
            if (pair.Value != null && pair.Value.Definition != null)
                buffSnapshot.Add(pair.Value);
        }

        buffSnapshot.Sort(CompareBuffs);
        EnsureRowViews(buffSnapshot.Count);
        for (int i = 0; i < rowViews.Count; i++)
        {
            bool active = i < buffSnapshot.Count;
            rowViews[i].gameObject.SetActive(active);
            if (active)
                rowViews[i].Bind(buffSnapshot[i]);
        }

        if (countText != null)
            countText.SetText("{0}", buffSnapshot.Count);
        if (emptyText != null)
            emptyText.gameObject.SetActive(buffSnapshot.Count == 0);

        SetViewActive(buffSnapshot.Count > 0);
        Canvas.ForceUpdateCanvases();
        if (contentRect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);
    }

    private static int CompareBuffs(BuffInstance left, BuffInstance right)
    {
        string leftName = left?.Definition?.DisplayName ?? left?.DefinitionId ?? string.Empty;
        string rightName = right?.Definition?.DisplayName ?? right?.DefinitionId ?? string.Empty;
        int nameCompare = string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
        return nameCompare != 0
            ? nameCompare
            : string.Compare(left?.DefinitionId, right?.DefinitionId, StringComparison.OrdinalIgnoreCase);
    }

    private void EnsureRowViews(int requiredCount)
    {
        while (rowViews.Count < requiredCount)
        {
            GameObject rowObject = Instantiate(itemPrefab, contentRect, false);
            rowObject.name = RuntimeUIPrefabKeys.BuffStatusItem;
            BuffStatusRowView rowView = rowObject.GetComponent<BuffStatusRowView>();
            if (rowView == null)
            {
                Debug.LogError("[PlayerBuffStatusHUD] Buff 行 Prefab 缺少 BuffStatusRowView。", rowObject);
                Destroy(rowObject);
                break;
            }

            rowViews.Add(rowView);
        }
    }

    #endregion

    #region 视图管理

    private bool EnsureView()
    {
        Transform panelRoot = UIManager.Instance?.panelRoot;
        RectTransform rootRect = panelRoot as RectTransform ?? panelRoot?.GetComponent<RectTransform>();
        if (rootRect == null)
            return false;

        if (viewObject != null)
        {
            if (viewRect != null && viewRect.parent != rootRect)
                viewRect.SetParent(rootRect, false);

            PlaceBelowInteractivePanels();
            return viewRect != null && contentRect != null && itemPrefab != null;
        }

        GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.BuffStatus, false);
        itemPrefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.BuffStatusItem, false);
        if (prefab == null || itemPrefab == null)
        {
            if (!missingPrefabLogged && GameRes.Instance != null)
            {
                Debug.LogError("[PlayerBuffStatusHUD] 缺少 Buff HUD Prefab。", this);
                missingPrefabLogged = true;
            }

            return false;
        }

        viewObject = Instantiate(prefab, rootRect, false);
        viewObject.name = ViewName;
        viewRect = viewObject.GetComponent<RectTransform>();
        viewCanvasGroup = viewObject.GetComponent<CanvasGroup>();
        titleText = FindChildText(viewObject.transform, TitleNodeName);
        countText = FindChildText(viewObject.transform, CountNodeName);
        emptyText = FindChildText(viewObject.transform, EmptyNodeName);
        Transform content = FindChild(viewObject.transform, ContentNodeName);
        contentRect = content as RectTransform ?? content?.GetComponent<RectTransform>();

        if (viewRect == null || viewCanvasGroup == null || contentRect == null ||
            titleText == null || countText == null || emptyText == null)
        {
            Debug.LogError("[PlayerBuffStatusHUD] UI_BuffStatus Prefab 控件命名契约不完整。", viewObject);
            Destroy(viewObject);
            viewObject = null;
            viewRect = null;
            contentRect = null;
            viewCanvasGroup = null;
            titleText = null;
            countText = null;
            emptyText = null;
            return false;
        }

        viewCanvasGroup.interactable = false;
        viewCanvasGroup.blocksRaycasts = false;
        RefreshStaticTexts();
        PlaceBelowInteractivePanels();
        SetViewActive(false);
        return true;
    }

    private void RefreshStaticTexts()
    {
        if (titleText != null)
            titleText.text = FlatWorldLocalizationService.GetUiText("状态效果 / BUFFS");
        if (emptyText != null)
            emptyText.text = FlatWorldLocalizationService.GetUiText("暂无状态");
    }

    private void SetViewActive(bool active)
    {
        if (viewObject == null)
            return;

        if (viewCanvasGroup != null)
        {
            viewCanvasGroup.interactable = false;
            viewCanvasGroup.blocksRaycasts = false;
        }

        if (viewObject.activeSelf != active)
            viewObject.SetActive(active);
    }

    /// <summary>常驻提示位于 PanelRoot 第一层交互 UI 之后，不抢占对话气泡和模态面板的层级。</summary>
    private void PlaceBelowInteractivePanels()
    {
        if (viewRect == null || viewRect.parent == null || viewRect.parent.childCount <= 1)
            return;

        viewRect.SetSiblingIndex(Mathf.Min(1, viewRect.parent.childCount - 1));
    }

    #endregion

    #region 玩家资格与辅助

    private void ResolvePlayer()
    {
        player ??= GetComponent<Player>();
    }

    private void HandleProfileContextChanged()
    {
        refreshRequested = true;
        if (player == null || !player.IsLocalProfile)
            SetViewActive(false);
    }

    private static Transform FindChild(Transform root, string childName)
    {
        if (root == null)
            return null;

        Transform[] children = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null && children[i].name == childName)
                return children[i];
        }

        return null;
    }

    private static TextMeshProUGUI FindChildText(Transform root, string childName)
    {
        Transform child = FindChild(root, childName);
        return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
    }

    #endregion
}
