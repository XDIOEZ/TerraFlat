using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 为本地玩家维护屏幕左侧中部的 Buff 条目列表。
/// 只实例化 UI_BuffStatus 与 UI_BuffStatusItem Prefab；通过 Mod_BuffManager 生命周期与整秒倒计时事件刷新，
/// HUD 容器本身不绘制背景或静态标题，只显示动态 Buff 条目；没有 Buff 时整体隐藏。
/// 列表超过安全区可容纳高度时仅在列表区域允许滚动，普通状态下完全输入透明。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
public sealed class PlayerBuffStatusHUD : MonoBehaviour
{
    #region 常量与运行时状态

    public const string ViewName = "PlayerBuffStatusHUD";

    private const string ContentNodeName = "Content";
    private const float RowHeight = 42f;
    private const float RowSpacing = 6f;
    private const int MaxVisibleRows = 6;
    private const float TopSafeMargin = 16f;

    private Player player;
    private Mod_BuffManager buffManager;
    private GameObject viewObject;
    private GameObject itemPrefab;
    private RectTransform viewRect;
    private RectTransform contentRect;
    private CanvasGroup viewCanvasGroup;
    private bool missingPrefabLogged;
    private UIManager subscribedUIManager;

    private readonly List<BuffStatusRowView> rowViews = new(8);
    private readonly List<BuffInstance> buffSnapshot = new(8);
    private readonly Dictionary<string, BuffStatusRowView> activeRows =
        new(StringComparer.OrdinalIgnoreCase);

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

        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        FlatWorldLocalizationService.LanguageChanged += HandleLanguageChanged;
        RefreshBinding();
    }

    private void OnDisable()
    {
        if (player != null)
            player.ProfileContextChanged -= HandleProfileContextChanged;

        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        UnbindUiManager();
        UnbindBuffManager();
        SetViewActive(false);
    }

    private void OnDestroy()
    {
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        UnbindUiManager();
        if (viewObject != null)
            Destroy(viewObject);
    }

    /// <summary>模块修复或运行时补装改变玩家子层级时，按事件重新解析一次 Mod_BuffManager。</summary>
    private void OnTransformChildrenChanged()
    {
        if (isActiveAndEnabled && player != null && player.IsLocalProfile)
            RefreshBinding();
    }

    #endregion

    #region 数据绑定与刷新

    /// <summary>按本地玩家资格绑定一次 Mod_BuffManager；静止期间不再扫描组件。</summary>
    private void RefreshBinding()
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

        RefreshStructure();
    }

    private bool TryBindBuffManager()
    {
        Mod_BuffManager candidate = player != null
            ? player.GetComponentInChildren<Mod_BuffManager>(true)
            : null;
        if (candidate == buffManager)
            return candidate != null;

        UnbindBuffManager();
        buffManager = candidate;
        if (buffManager == null)
            return false;

        buffManager.BuffAdded += HandleBuffChanged;
        buffManager.BuffRemoved += HandleBuffChanged;
        buffManager.BuffDurationChanged += HandleBuffContentChanged;
        buffManager.BuffStacksChanged += HandleBuffContentChanged;
        buffManager.BuffCountdownChanged += HandleBuffContentChanged;
        return true;
    }

    private void UnbindBuffManager()
    {
        if (buffManager != null)
        {
            buffManager.BuffAdded -= HandleBuffChanged;
            buffManager.BuffRemoved -= HandleBuffChanged;
            buffManager.BuffDurationChanged -= HandleBuffContentChanged;
            buffManager.BuffStacksChanged -= HandleBuffContentChanged;
            buffManager.BuffCountdownChanged -= HandleBuffContentChanged;
        }

        buffManager = null;
        activeRows.Clear();
    }

    private void HandleBuffChanged(BuffInstance _)
    {
        RefreshStructure();
    }

    /// <summary>时长变化只改对应文本，不触发布局重建。</summary>
    private void HandleBuffContentChanged(BuffInstance runtime)
    {
        if (runtime == null || string.IsNullOrWhiteSpace(runtime.DefinitionId))
            return;

        if (activeRows.TryGetValue(runtime.DefinitionId, out BuffStatusRowView rowView) && rowView != null)
            rowView.Bind(runtime);
    }

    private void HandleLanguageChanged(string _)
    {
        RefreshVisibleRows();
    }

    /// <summary>读取 ActiveBuffs 快照并复用行 Prefab；仅此结构入口标记 Content 布局。</summary>
    private void RefreshStructure()
    {
        if (player == null || player.DestructionHandled || player.IsUnloadingModules)
        {
            UnbindBuffManager();
            SetViewActive(false);
            return;
        }
        if (!EnsureView() || buffManager == null)
        {
            SetViewActive(false);
            return;
        }

        buffSnapshot.Clear();
        foreach (KeyValuePair<string, BuffInstance> pair in buffManager.ActiveBuffs)
        {
            if (pair.Value != null && pair.Value.Definition != null)
                buffSnapshot.Add(pair.Value);
        }

        buffSnapshot.Sort(CompareBuffs);
        EnsureRowViews(buffSnapshot.Count);
        activeRows.Clear();
        for (int i = 0; i < rowViews.Count; i++)
        {
            bool active = i < buffSnapshot.Count;
            rowViews[i].gameObject.SetActive(active);
            if (active)
            {
                rowViews[i].Bind(buffSnapshot[i]);
                activeRows[buffSnapshot[i].DefinitionId] = rowViews[i];
            }
            else
            {
                rowViews[i].Clear();
            }
        }

        SetViewActive(buffSnapshot.Count > 0);
        RebuildDynamicLayout(buffSnapshot.Count);
    }

    /// <summary>按安全区容纳最多六行，超过时保留完整 Content 供局部滚动。</summary>
    private void RebuildDynamicLayout(int visibleRowCount)
    {
        if (viewRect == null || contentRect == null || visibleRowCount <= 0)
            return;

        float rowsHeight = visibleRowCount * RowHeight + Mathf.Max(0, visibleRowCount - 1) * RowSpacing;
        float maxRowsHeight = MaxVisibleRows * RowHeight + (MaxVisibleRows - 1) * RowSpacing;
        RectTransform safeArea = viewRect.parent as RectTransform;
        float availableHeight = safeArea != null
            ? Mathf.Max(RowHeight, safeArea.rect.height * (1f - viewRect.anchorMax.y) -
                                     viewRect.anchoredPosition.y - TopSafeMargin)
            : maxRowsHeight;
        float visibleHeight = Mathf.Min(rowsHeight, maxRowsHeight, availableHeight);
        viewRect.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            visibleHeight);

        // 只在条目溢出时开放该小块区域的滚动射线，避免普通 HUD 抢世界输入。
        if (viewCanvasGroup != null)
            viewCanvasGroup.blocksRaycasts = rowsHeight > visibleHeight + 0.5f;
        LayoutRebuilder.MarkLayoutForRebuild(contentRect);
        LayoutRebuilder.MarkLayoutForRebuild(viewRect);
    }

    /// <summary>语言切换时只刷新已显示行的内容，条目结构保持不变。</summary>
    private void RefreshVisibleRows()
    {
        if (buffManager == null || activeRows.Count == 0)
            return;

        foreach (KeyValuePair<string, BuffStatusRowView> pair in activeRows)
        {
            if (pair.Value != null && buffManager.TryGetBuff(pair.Key, out BuffInstance runtime))
                pair.Value.Bind(runtime);
        }
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
        rowViews.RemoveAll(row => row == null);
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
        UIManager manager = UIManager.Instance;
        RectTransform rootRect = manager?.SafeAreaRoot;
        if (rootRect == null)
            return false;

        if (viewObject != null)
        {
            if (viewRect != null && viewRect.parent != rootRect)
                viewRect.SetParent(rootRect, false);

            BindUiManager(manager);
            PlaceBelowInteractivePanels();
            return viewRect != null && contentRect != null && itemPrefab != null;
        }

        // 容器被场景或 UI 清理后，重建时同步丢弃旧行引用。
        rowViews.Clear();
        activeRows.Clear();
        viewRect = null;
        contentRect = null;
        viewCanvasGroup = null;
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
        Transform content = FindChild(viewObject.transform, ContentNodeName);
        contentRect = content as RectTransform ?? content?.GetComponent<RectTransform>();

        if (viewRect == null || viewCanvasGroup == null || contentRect == null)
        {
            Debug.LogError("[PlayerBuffStatusHUD] UI_BuffStatus Prefab 控件命名契约不完整。", viewObject);
            Destroy(viewObject);
            viewObject = null;
            viewRect = null;
            contentRect = null;
            viewCanvasGroup = null;
            return false;
        }

        viewCanvasGroup.interactable = false;
        viewCanvasGroup.blocksRaycasts = false;
        BindUiManager(manager);
        PlaceBelowInteractivePanels();
        SetViewActive(false);
        return true;
    }

    private void SetViewActive(bool active)
    {
        if (viewObject == null)
            return;

        if (viewCanvasGroup != null)
        {
            viewCanvasGroup.interactable = false;
            if (!active)
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

    #region 安全区响应

    private void BindUiManager(UIManager manager)
    {
        if (ReferenceEquals(subscribedUIManager, manager))
            return;

        UnbindUiManager();
        subscribedUIManager = manager;
        if (subscribedUIManager != null)
            subscribedUIManager.InteractionSurfaceChanged += HandleSafeAreaChanged;
    }

    private void UnbindUiManager()
    {
        if (!ReferenceEquals(subscribedUIManager, null))
            subscribedUIManager.InteractionSurfaceChanged -= HandleSafeAreaChanged;
        subscribedUIManager = null;
    }

    private void HandleSafeAreaChanged()
    {
        if (viewObject != null && viewObject.activeSelf)
            RebuildDynamicLayout(buffSnapshot.Count);
    }

    #endregion

    #region 玩家资格与辅助

    private void ResolvePlayer()
    {
        player ??= GetComponent<Player>();
    }

    private void HandleProfileContextChanged()
    {
        RefreshBinding();
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

    #endregion
}
