using System;
using System.Collections.Generic;
using FlatWorld.Gameplay.Quests;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 为本地玩家维护屏幕右侧的简洁任务追踪卡。组件通过 QuestManager 的运行时生命周期事件绑定玩家，
/// 只订阅 QuestChanged 并复用最多四个条目 Prefab；完成任务自动移出追踪列表，没有逐帧轮询，
/// 任务内容和装饰元素不会拦截输入，只保留展开/收起按钮的有意交互。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
public sealed class PlayerQuestTrackerHUD : MonoBehaviour
{
    #region 常量与状态

    public const string ViewName = "PlayerQuestTrackerHUD";

    private const string TitleNodeName = "标题";
    private const string CountNodeName = "数量文本";
    private const string EmptyNodeName = "空状态文本";
    private const string ToggleButtonNodeName = "任务面板开关按钮";
    private const string ContentNodeName = "Content";
    private const int MaximumVisibleQuestCount = 4;
    private static readonly Vector2 ExpandedViewSize = new Vector2(300f, 300f);
    private static readonly Vector2 CollapsedViewSize = new Vector2(300f, 54f);

    private Player player;
    private QuestManager questManager;
    private PlayerQuestRuntime runtime;
    private GameObject viewObject;
    private GameObject itemPrefab;
    private RectTransform viewRect;
    private RectTransform contentRect;
    private CanvasGroup viewCanvasGroup;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI countText;
    private TextMeshProUGUI emptyText;
    private Button toggleButton;
    private bool isExpanded = true;
    private bool missingPrefabLogged;

    private readonly List<QuestTrackerRowView> rowViews = new(MaximumVisibleQuestCount);
    private readonly List<QuestSnapshot> snapshotBuffer = new(8);
    private readonly List<string> displayedQuestIds = new(MaximumVisibleQuestCount);
    private readonly HashSet<string> trackedQuestIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前 Active 或 ReadyToClaim 的任务总数，包含 HUD 容量之外的任务。</summary>
    public int TrackedQuestCount => trackedQuestIds.Count;

    /// <summary>正式任务追踪 Prefab 是否已完成实例化和节点解析。</summary>
    public bool IsViewReady => viewObject != null && contentRect != null && viewCanvasGroup != null;

    /// <summary>用于确认任务内容不抢占玩法输入；只有展开/收起按钮接收点击。</summary>
    public bool IsInputTransparent => viewCanvasGroup != null &&
                                      viewCanvasGroup.interactable &&
                                      viewCanvasGroup.blocksRaycasts &&
                                      toggleButton != null;

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
        if (toggleButton != null)
            toggleButton.onClick.RemoveListener(ToggleExpanded);
        RefreshBinding();
    }

    private void OnDisable()
    {
        if (player != null)
            player.ProfileContextChanged -= HandleProfileContextChanged;

        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        if (toggleButton != null)
            toggleButton.onClick.RemoveListener(ToggleExpanded);
        UnbindQuestManager();
        UnbindRuntime();
        SetViewActive(false);
    }

    private void OnDestroy()
    {
        FlatWorldLocalizationService.LanguageChanged -= HandleLanguageChanged;
        if (viewObject != null)
            Destroy(viewObject);
    }

    #endregion

    #region 任务运行时绑定

    /// <summary>只为本地档案玩家绑定任务协调器；远端玩家不创建任务 HUD。</summary>
    private void RefreshBinding()
    {
        ResolvePlayer();
        if (player == null || !player.IsLocalProfile)
        {
            UnbindQuestManager();
            UnbindRuntime();
            SetViewActive(false);
            return;
        }

        BindQuestManager();
        if (questManager != null && questManager.TryGetRuntime(player, out PlayerQuestRuntime candidate))
        {
            BindRuntime(candidate);
            return;
        }

        UnbindRuntime();
        SetViewActive(false);
    }

    private void BindQuestManager()
    {
        QuestManager candidate = QuestManager.Instance;
        if (candidate == questManager)
            return;

        UnbindQuestManager();
        questManager = candidate;
        if (questManager == null)
            return;

        questManager.RuntimeReady += HandleRuntimeReady;
        questManager.RuntimeRemoving += HandleRuntimeRemoving;
    }

    private void UnbindQuestManager()
    {
        if (questManager != null)
        {
            questManager.RuntimeReady -= HandleRuntimeReady;
            questManager.RuntimeRemoving -= HandleRuntimeRemoving;
        }

        questManager = null;
    }

    private void BindRuntime(PlayerQuestRuntime candidate)
    {
        if (candidate == runtime)
        {
            RefreshEntries();
            return;
        }

        UnbindRuntime();
        runtime = candidate;
        if (runtime == null)
            return;

        runtime.QuestChanged += HandleQuestChanged;
        RefreshEntries();
    }

    private void UnbindRuntime()
    {
        if (runtime != null)
            runtime.QuestChanged -= HandleQuestChanged;

        runtime = null;
        snapshotBuffer.Clear();
        trackedQuestIds.Clear();
        displayedQuestIds.Clear();
    }

    private void HandleRuntimeReady(Player owner, PlayerQuestRuntime readyRuntime)
    {
        if (owner == player)
            BindRuntime(readyRuntime);
    }

    private void HandleRuntimeRemoving(Player owner)
    {
        if (owner != player)
            return;

        UnbindRuntime();
        SetViewActive(false);
    }

    private void HandleQuestChanged(QuestSnapshot _)
    {
        RefreshEntries();
    }

    private void HandleLanguageChanged(string _)
    {
        RefreshStaticTexts();
        if (runtime != null)
            RefreshEntries();
    }

    #endregion

    #region 列表刷新

    /// <summary>重读只读快照并复用行池；任务增删或排序变化时才标记 Content 布局。</summary>
    private void RefreshEntries()
    {
        if (runtime == null || !EnsureView())
        {
            SetViewActive(false);
            return;
        }

        RefreshStaticTexts();
        snapshotBuffer.Clear();
        trackedQuestIds.Clear();
        IReadOnlyList<QuestSnapshot> snapshots = runtime.GetSnapshots();
        for (int index = 0; index < snapshots.Count; index++)
        {
            QuestSnapshot snapshot = snapshots[index];
            if (snapshot == null || snapshot.Status == QuestStatus.Completed)
                continue;

            snapshotBuffer.Add(snapshot);
            trackedQuestIds.Add(snapshot.QuestId);
        }

        snapshotBuffer.Sort(CompareSnapshots);
        int desiredVisibleCount = Mathf.Min(snapshotBuffer.Count, MaximumVisibleQuestCount);
        bool structureChanged = HasStructureChanged(desiredVisibleCount);
        EnsureRowViews(desiredVisibleCount);
        int visibleCount = Mathf.Min(desiredVisibleCount, rowViews.Count);

        for (int index = 0; index < rowViews.Count; index++)
        {
            bool active = index < visibleCount;
            QuestTrackerRowView row = rowViews[index];
            if (row.gameObject.activeSelf != active)
                row.gameObject.SetActive(active);

            if (!active)
            {
                row.Clear();
                continue;
            }

            QuestSnapshot snapshot = snapshotBuffer[index];
            QuestCatalog.TryGet(snapshot.QuestId, out QuestDefinition definition);
            row.Bind(definition, snapshot);
        }

        if (countText != null)
            countText.SetText("{0}", snapshotBuffer.Count);

        SetViewActive(true);
        ApplyExpandedState();
        if (structureChanged && contentRect != null)
            LayoutRebuilder.MarkLayoutForRebuild(contentRect);
    }

    private bool HasStructureChanged(int visibleCount)
    {
        bool changed = displayedQuestIds.Count != visibleCount;
        if (!changed)
        {
            for (int index = 0; index < visibleCount; index++)
            {
                if (!string.Equals(
                        displayedQuestIds[index],
                        snapshotBuffer[index].QuestId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
            return false;

        displayedQuestIds.Clear();
        for (int index = 0; index < visibleCount; index++)
            displayedQuestIds.Add(snapshotBuffer[index].QuestId);
        return true;
    }

    private void EnsureRowViews(int requiredCount)
    {
        while (rowViews.Count < requiredCount)
        {
            GameObject rowObject = Instantiate(itemPrefab, contentRect, false);
            rowObject.name = RuntimeUIPrefabKeys.QuestTrackerItem;
            QuestTrackerRowView rowView = rowObject.GetComponent<QuestTrackerRowView>();
            if (rowView == null)
            {
                Debug.LogError("[PlayerQuestTrackerHUD] 任务行 Prefab 缺少 QuestTrackerRowView。", rowObject);
                Destroy(rowObject);
                break;
            }

            rowViews.Add(rowView);
        }
    }

    private static int CompareSnapshots(QuestSnapshot left, QuestSnapshot right)
    {
        int leftPriority = left?.Status == QuestStatus.ReadyToClaim ? 0 : 1;
        int rightPriority = right?.Status == QuestStatus.ReadyToClaim ? 0 : 1;
        int statusCompare = leftPriority.CompareTo(rightPriority);
        if (statusCompare != 0)
            return statusCompare;

        int titleCompare = string.Compare(
            left?.Title,
            right?.Title,
            StringComparison.OrdinalIgnoreCase);
        return titleCompare != 0
            ? titleCompare
            : string.Compare(left?.QuestId, right?.QuestId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>查询任务是否仍处于追踪状态，供运行时自动化做只读断言。</summary>
    public bool IsQuestTracked(string questId)
    {
        return !string.IsNullOrWhiteSpace(questId) && trackedQuestIds.Contains(questId);
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

            if (toggleButton != null)
            {
                toggleButton.onClick.RemoveListener(ToggleExpanded);
                toggleButton.onClick.AddListener(ToggleExpanded);
            }
            PlaceBelowInteractivePanels();
            return viewRect != null && contentRect != null && itemPrefab != null;
        }

        GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.QuestTracker, false);
        itemPrefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.QuestTrackerItem, false);
        if (prefab == null || itemPrefab == null)
        {
            if (!missingPrefabLogged && GameRes.Instance != null)
            {
                Debug.LogError("[PlayerQuestTrackerHUD] 缺少任务追踪 HUD Prefab。", this);
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
        toggleButton = FindChildButton(viewObject.transform, ToggleButtonNodeName);
        Transform content = FindChild(viewObject.transform, ContentNodeName);
        contentRect = content as RectTransform ?? content?.GetComponent<RectTransform>();

        if (viewRect == null || viewCanvasGroup == null || contentRect == null ||
            titleText == null || countText == null || emptyText == null || toggleButton == null)
        {
            Debug.LogError("[PlayerQuestTrackerHUD] UI_QuestTracker Prefab 控件命名契约不完整。", viewObject);
            Destroy(viewObject);
            viewObject = null;
            viewRect = null;
            contentRect = null;
            viewCanvasGroup = null;
            titleText = null;
            countText = null;
            emptyText = null;
            toggleButton = null;
            return false;
        }

        viewCanvasGroup.interactable = true;
        viewCanvasGroup.blocksRaycasts = true;
        toggleButton.onClick.RemoveListener(ToggleExpanded);
        toggleButton.onClick.AddListener(ToggleExpanded);
        RefreshStaticTexts();
        ApplyExpandedState();
        PlaceBelowInteractivePanels();
        SetViewActive(false);
        return true;
    }

    private void RefreshStaticTexts()
    {
        if (titleText != null)
            titleText.text = FlatWorldLocalizationService.GetUiText("任务追踪 / QUESTS");
        if (emptyText != null)
            emptyText.text = FlatWorldLocalizationService.GetUiText("暂无进行中的任务");
        if (toggleButton != null)
        {
            TextMeshProUGUI toggleLabel = toggleButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (toggleLabel != null)
                toggleLabel.text = FlatWorldLocalizationService.GetUiText(isExpanded ? "收起" : "展开");
        }
    }

    private void SetViewActive(bool active)
    {
        if (viewObject == null)
            return;

        if (viewCanvasGroup != null)
        {
            viewCanvasGroup.interactable = active;
            viewCanvasGroup.blocksRaycasts = active;
        }

        if (viewObject.activeSelf != active)
            viewObject.SetActive(active);
    }

    private void ToggleExpanded()
    {
        isExpanded = !isExpanded;
        ApplyExpandedState();
    }

    private void ApplyExpandedState()
    {
        if (viewRect == null)
            return;

        viewRect.sizeDelta = isExpanded ? ExpandedViewSize : CollapsedViewSize;
        bool hasVisibleQuests = snapshotBuffer.Count > 0;
        GameObject contentRoot = contentRect != null && contentRect.parent != null
            ? contentRect.parent.gameObject
            : null;
        if (contentRoot != null)
            contentRoot.SetActive(isExpanded && hasVisibleQuests);
        if (emptyText != null)
            emptyText.gameObject.SetActive(isExpanded && !hasVisibleQuests);
        if (toggleButton != null)
        {
            TextMeshProUGUI label = toggleButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
                label.text = FlatWorldLocalizationService.GetUiText(isExpanded ? "收起" : "展开");
        }
    }

    /// <summary>坐标与 Buff HUD 之后排列，仍保持在所有交互面板下方。</summary>
    private void PlaceBelowInteractivePanels()
    {
        if (viewRect == null || viewRect.parent == null)
            return;

        int targetIndex = Mathf.Min(2, viewRect.parent.childCount - 1);
        if (viewRect.GetSiblingIndex() != targetIndex)
            viewRect.SetSiblingIndex(targetIndex);
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
        for (int index = 0; index < children.Length; index++)
        {
            if (children[index] != null && children[index].name == childName)
                return children[index];
        }

        return null;
    }

    private static TextMeshProUGUI FindChildText(Transform root, string childName)
    {
        Transform child = FindChild(root, childName);
        return child != null ? child.GetComponent<TextMeshProUGUI>() : null;
    }

    private static Button FindChildButton(Transform root, string childName)
    {
        Transform child = FindChild(root, childName);
        return child != null ? child.GetComponent<Button>() : null;
    }

    #endregion
}
