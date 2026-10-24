using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 背包和容器共用的名称搜索与分类并集，只过滤显示，不改变真实库存及槽位索引。
/// </summary>
public sealed class InventoryBagSearch : MonoBehaviour
{
    #region 配置与状态

    private const string ClearButtonName = "清空搜索";

    [SerializeField] private TMP_InputField searchInput; // 标题栏中的共用输入框。

    private readonly Dictionary<RuntimeItemDefinition, string> englishNames = new();
    private UIMultiSelectDropdown categoryDropdown;
    private InventoryVirtualizedSlotGrid virtualizedGrid;
    private TMP_Text emptyLabel;
    private ScrollRect inventoryScroll;
    private Inventory_Data boundData;
    private Predicate<ItemData> filterPredicate;
    private bool refreshPending;
    private bool resetScrollPending;
    private Button clearButton; // 搜索框右侧的小扫把按钮。
    private Inventory inventory; // 当前面板绑定的真实库存。
    private string query = string.Empty; // 已去除首尾空格的搜索词。

    /// <summary>当前是否存在有效搜索词。</summary>
    public bool HasActiveQuery => query.Length > 0;
    public bool HasActiveFilter => HasActiveQuery || (categoryDropdown != null && categoryDropdown.SelectedValues.Count > 0);

    #endregion

    #region 生命周期与绑定

    private void OnEnable()
    {
        if (searchInput == null) return;
        filterPredicate ??= MatchesFilter;
        categoryDropdown = GetComponentInChildren<UIMultiSelectDropdown>(true);
        emptyLabel = transform.Find("FWUI_InventoryFilterEmpty")?.GetComponent<TMP_Text>();
        if (categoryDropdown != null)
        {
            var options = new List<UIMultiSelectDropdown.Option>();
            foreach (InventoryCategoryFilter.Category category in InventoryCategoryFilter.Categories)
                options.Add(new UIMultiSelectDropdown.Option(category.Id, category.Label));
            categoryDropdown.SetOptions(options);
            categoryDropdown.SelectionChanged += OnCategoriesChanged;
        }
        clearButton = searchInput.transform.Find(ClearButtonName)?.GetComponent<Button>();
        searchInput.onValueChanged.AddListener(OnSearchTextChanged);
        if (clearButton != null)
            clearButton.onClick.AddListener(ClearSearch);
        FlatWorldLocalizationService.LanguageChanged += OnLanguageChanged;
        if (boundData != null)
        {
            boundData.Event_RefreshUI -= OnSlotRefreshed;
            boundData.Event_RefreshUI += OnSlotRefreshed;
        }
        OnSearchTextChanged(searchInput.text);
    }

    private void OnDisable()
    {
        if (searchInput != null) searchInput.onValueChanged.RemoveListener(OnSearchTextChanged);
        if (categoryDropdown != null) categoryDropdown.SelectionChanged -= OnCategoriesChanged;
        if (boundData != null) boundData.Event_RefreshUI -= OnSlotRefreshed;
        if (clearButton != null)
            clearButton.onClick.RemoveListener(ClearSearch);
        FlatWorldLocalizationService.LanguageChanged -= OnLanguageChanged;
    }

    private void OnDestroy()
    {
        if (boundData != null) boundData.Event_RefreshUI -= OnSlotRefreshed;
        if (virtualizedGrid != null) virtualizedGrid.VisibleCountChanged -= RefreshResultCount;
    }

    /// <summary>库存每次创建或扩容槽位后重新绑定表现与数据刷新事件。</summary>
    public void Bind(Inventory target)
    {
        bool dataChanged = !ReferenceEquals(boundData, target?.Data);
        if (boundData != null) boundData.Event_RefreshUI -= OnSlotRefreshed;
        if (virtualizedGrid != null) virtualizedGrid.VisibleCountChanged -= RefreshResultCount;
        inventory = target;
        boundData = inventory?.Data;
        if (boundData != null) boundData.Event_RefreshUI += OnSlotRefreshed;
        virtualizedGrid = GetComponent<InventoryVirtualizedSlotGrid>();
        if (virtualizedGrid != null && (!virtualizedGrid.enabled || !virtualizedGrid.IsBound)) virtualizedGrid = null;
        if (virtualizedGrid != null) virtualizedGrid.VisibleCountChanged += RefreshResultCount;
        UI_Content slotContent = GetComponentInChildren<UI_Content>(true);
        inventoryScroll = slotContent != null ? slotContent.GetComponentInParent<ScrollRect>() : null;
        filterPredicate ??= MatchesFilter;
        englishNames.Clear();
        ApplySearchText(searchInput != null ? searchInput.text : string.Empty, dataChanged);
    }

    private void LateUpdate()
    {
        // 同一帧的多次库存事务合并刷新，拖拽结束前不隐藏或重排正在操作的格子。
        if (refreshPending && inventory != null && !inventory.HasActiveSlotDrag) RefreshAllSlots();
    }

    #endregion

    #region 搜索与显示

    /// <summary>输入变化时重新比较所有现存槽位。</summary>
    private void OnSearchTextChanged(string value)
    {
        ApplySearchText(value, resetScroll: true);
    }

    private void ApplySearchText(string value, bool resetScroll)
    {
        string nextQuery = value?.Trim() ?? string.Empty;
        // 同一库存扩缩槽只刷新结果，保留滚动和拖拽期间尚未提交的筛选回顶请求。
        resetScrollPending |= resetScroll || !string.Equals(query, nextQuery, StringComparison.Ordinal);
        query = nextQuery;
        if (clearButton != null)
            clearButton.interactable = !string.IsNullOrEmpty(value);
        RefreshAllSlots();
    }

    private void OnCategoriesChanged()
    {
        resetScrollPending = true;
        RefreshAllSlots();
    }

    /// <summary>手机端无需重新呼出软键盘即可一键清空搜索。</summary>
    private void ClearSearch()
    {
        if (string.IsNullOrEmpty(searchInput.text))
            return;

        searchInput.text = string.Empty;
    }

    /// <summary>语言切换后刷新显示名对应的匹配结果。</summary>
    private void OnLanguageChanged(string _)
    {
        englishNames.Clear();
        RefreshAllSlots();
    }

    /// <summary>固定网格合并数据刷新，虚拟网格自行处理投影更新。</summary>
    private void OnSlotRefreshed(int index)
    {
        if (virtualizedGrid == null) refreshPending = true;
    }

    /// <summary>虚拟化网格滚动重绑后只刷新实际存在的槽位表现。</summary>
    public void RefreshRenderedSlots()
    {
        RefreshAllSlots();
    }

    /// <summary>虚拟化槽位发生单格数据刷新时只更新当前渲染格。</summary>
    public void RefreshRenderedSlot(ItemSlot_UI slot, int index)
    {
        if (virtualizedGrid == null) refreshPending = true;
    }

    /// <summary>显示投影只控制可见性，所有交互仍使用槽位原本绑定的数据索引。</summary>
    private void RefreshAllSlots()
    {
        if (inventory?.Data?.itemSlots == null)
            return;
        if (inventory.HasActiveSlotDrag) { refreshPending = true; return; }
        refreshPending = false;
        if (virtualizedGrid != null)
        {
            virtualizedGrid.SetFilter(HasActiveFilter ? filterPredicate : null, resetScrollPending);
        }
        else
        {
            int visibleCount = 0;
            bool changed = false;
            for (int index = 0; index < inventory.itemSlot_UI.Count; index++)
            {
                ItemSlot_UI slot = inventory.itemSlot_UI[index];
                if (slot == null) continue;
                bool visible = index < inventory.Data.itemSlots.Count &&
                    (!HasActiveFilter || MatchesFilter(inventory.Data.itemSlots[index]?.itemData));
                if (visible) visibleCount++;
                if (slot.gameObject.activeSelf != visible)
                { slot.gameObject.SetActive(visible); changed = true; }
            }
            if (changed && inventory.basePanel != null) inventory.basePanel.RefreshUIComponents();
            RefreshResultCount(visibleCount);
        }
        if (resetScrollPending && inventoryScroll != null)
        {
            inventoryScroll.StopMovement();
            inventoryScroll.verticalNormalizedPosition = 1f;
        }
        resetScrollPending = false;
    }

    private void RefreshResultCount(int visibleCount)
    {
        if (emptyLabel == null) return;
        emptyLabel.text = FlatWorldLocalizationService.GetUiText("没有匹配物品");
        emptyLabel.gameObject.SetActive(HasActiveFilter && visibleCount == 0);
    }

    public bool MatchesFilter(ItemData item)
    {
        return item != null && InventoryCategoryFilter.MatchesAny(item.Tags, categoryDropdown?.SelectedValues)
            && (!HasActiveQuery || Matches(item));
    }

    /// <summary>中文、英文和标签均按不区分大小写的片段匹配。</summary>
    private bool Matches(ItemData item)
    {
        if (item == null)
            return false;

        if (Contains(item.IDName))
            return true;

        if (item.Tags != null)
        {
            foreach (string tag in item.Tags)
            {
                if (ItemTagLocalizationCatalog.MatchesSearch(tag, query))
                    return true;
            }
        }

        if (GameRes.Instance.TryGetItemDefinition(item.IDName, out RuntimeItemDefinition definition))
        {
            if (Contains(definition.SourceDisplayName) ||
                Contains(definition.DisplayName) ||
                Contains(GetEnglishName(definition)))
                return true;
        }

        return false;
    }

    /// <summary>供排序复用与当前搜索完全一致的命中规则。</summary>
    public bool MatchesCurrentQuery(ItemData item)
    {
        return HasActiveFilter && MatchesFilter(item);
    }

    /// <summary>按运行时定义缓存英文译名，避免每次输入都重复读取本地化表。</summary>
    private string GetEnglishName(RuntimeItemDefinition definition)
    {
        if (englishNames.TryGetValue(definition, out string name))
            return name;

        name = FlatWorldLocalizationService.GetInLocale(
            definition.LabelKey,
            FlatWorldLocalizationService.FallbackLocaleCode,
            tableName: FlatWorldLocalizationService.DefaultTable);
        if (!string.IsNullOrEmpty(name))
            englishNames.Add(definition, name);
        return name;
    }

    /// <summary>允许输入名称或标签的一部分，无需完全相同。</summary>
    private bool Contains(string text)
    {
        return !string.IsNullOrEmpty(text) &&
               text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    #endregion
}
